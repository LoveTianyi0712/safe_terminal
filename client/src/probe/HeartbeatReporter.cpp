#include "HeartbeatReporter.h"
#include <spdlog/spdlog.h>
#include <google/protobuf/util/time_util.h>
#include <thread>
#include <chrono>

#if defined(PLATFORM_WINDOWS)
#  include <windows.h>
#  include <pdh.h>
#  pragma comment(lib, "pdh.lib")
#elif defined(PLATFORM_LINUX)
#  include <sys/statvfs.h>
#  include <fstream>
#  include <string>
#elif defined(PLATFORM_MACOS)
#  include <mach/mach.h>
#  include <sys/statvfs.h>
#endif

namespace st {

HeartbeatReporter::HeartbeatReporter(const CollectionConfig& cfg,
                                     const terminal::v1::TerminalIdentity& identity,
                                     RingBuffer& buffer)
    : cfg_(cfg), identity_(identity), buffer_(buffer) {}

HeartbeatReporter::~HeartbeatReporter() { stop(); }

void HeartbeatReporter::start() {
    running_ = true;
    worker_ = std::thread([this] { report_loop(); });
}

void HeartbeatReporter::stop() {
    running_ = false;
    if (worker_.joinable()) worker_.join();
}

void HeartbeatReporter::report_loop() {
    while (running_) {
        auto hb = collect_metrics();
        std::string serialized;
        hb.SerializeToString(&serialized);

        buffer_.push({0, "heartbeat", std::move(serialized)});
        spdlog::debug("[Heartbeat] cpu={:.1f}% mem={:.1f}%",
                      hb.cpu_percent(), hb.mem_percent());

        std::this_thread::sleep_for(
            std::chrono::seconds(cfg_.heartbeat_interval_sec));
    }
}

terminal::v1::Heartbeat HeartbeatReporter::collect_metrics() const {
    terminal::v1::Heartbeat hb;
    *hb.mutable_identity() = identity_;
    *hb.mutable_ts() = google::protobuf::util::TimeUtil::GetCurrentTime();
    hb.set_cpu_percent(get_cpu_percent());
    hb.set_mem_percent(get_mem_percent());
    hb.set_disk_percent(get_disk_percent());
    return hb;
}

// ─── Windows 实现 ──────────────────────────────────────────────────────────
#if defined(PLATFORM_WINDOWS)

double HeartbeatReporter::get_cpu_percent() const {
    // 使用 GetSystemTimes 两次采样差值计算 CPU 占用率
    // 比 PDH 更轻量，无需额外初始化，精度足够心跳场景使用
    FILETIME idle1, kernel1, user1;
    GetSystemTimes(&idle1, &kernel1, &user1);

    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    FILETIME idle2, kernel2, user2;
    GetSystemTimes(&idle2, &kernel2, &user2);

    auto ft_to_u64 = [](const FILETIME& ft) -> uint64_t {
        return (static_cast<uint64_t>(ft.dwHighDateTime) << 32) | ft.dwLowDateTime;
    };

    uint64_t idle_diff   = ft_to_u64(idle2)   - ft_to_u64(idle1);
    uint64_t kernel_diff = ft_to_u64(kernel2) - ft_to_u64(kernel1);
    uint64_t user_diff   = ft_to_u64(user2)   - ft_to_u64(user1);

    // kernel_diff 包含 idle 时间，需减去
    uint64_t total = kernel_diff + user_diff;
    if (total == 0) return 0.0;
    return (total - idle_diff) * 100.0 / total;
}

double HeartbeatReporter::get_mem_percent() const {
    MEMORYSTATUSEX ms{};
    ms.dwLength = sizeof(ms);
    GlobalMemoryStatusEx(&ms);
    return static_cast<double>(ms.dwMemoryLoad);
}

double HeartbeatReporter::get_disk_percent() const {
    ULARGE_INTEGER free_bytes, total_bytes;
    GetDiskFreeSpaceExW(L"C:\\", nullptr, &total_bytes, &free_bytes);
    if (total_bytes.QuadPart == 0) return 0.0;
    double used = static_cast<double>(total_bytes.QuadPart - free_bytes.QuadPart);
    return used / total_bytes.QuadPart * 100.0;
}

// ─── Linux 实现 ────────────────────────────────────────────────────────────
#elif defined(PLATFORM_LINUX)

double HeartbeatReporter::get_cpu_percent() const {
    // 两次读取 /proc/stat 第一行（cpu 汇总行），取差值计算使用率
    auto read_cpu_stat = []() -> std::array<uint64_t, 10> {
        std::array<uint64_t, 10> vals{};
        std::ifstream f("/proc/stat");
        std::string tag;
        f >> tag;  // "cpu"
        for (auto& v : vals) f >> v;
        return vals;
    };

    auto s1 = read_cpu_stat();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));
    auto s2 = read_cpu_stat();

    // 字段: user nice system idle iowait irq softirq steal guest guest_nice
    auto idle1  = s1[3] + s1[4];   // idle + iowait
    auto total1 = s1[0]+s1[1]+s1[2]+s1[3]+s1[4]+s1[5]+s1[6]+s1[7];
    auto idle2  = s2[3] + s2[4];
    auto total2 = s2[0]+s2[1]+s2[2]+s2[3]+s2[4]+s2[5]+s2[6]+s2[7];

    uint64_t total_diff = total2 - total1;
    uint64_t idle_diff  = idle2  - idle1;
    if (total_diff == 0) return 0.0;
    return (total_diff - idle_diff) * 100.0 / total_diff;
}

double HeartbeatReporter::get_mem_percent() const {
    std::ifstream f("/proc/meminfo");
    uint64_t total = 0, available = 0;
    std::string line;
    while (std::getline(f, line)) {
        if (line.rfind("MemTotal:", 0) == 0)     total     = std::stoull(line.substr(9));
        if (line.rfind("MemAvailable:", 0) == 0) available = std::stoull(line.substr(13));
    }
    if (total == 0) return 0.0;
    return (total - available) * 100.0 / total;
}

double HeartbeatReporter::get_disk_percent() const {
    struct statvfs st;
    if (statvfs("/", &st) != 0) return 0.0;
    uint64_t total = st.f_blocks * st.f_frsize;
    uint64_t free  = st.f_bfree  * st.f_frsize;
    return (total - free) * 100.0 / total;
}

// ─── macOS 实现 ────────────────────────────────────────────────────────────
#elif defined(PLATFORM_MACOS)

double HeartbeatReporter::get_cpu_percent() const {
    auto read_ticks = []() -> std::array<uint64_t, CPU_STATE_MAX> {
        std::array<uint64_t, CPU_STATE_MAX> t{};
        host_cpu_load_info_data_t info;
        mach_msg_type_number_t count = HOST_CPU_LOAD_INFO_COUNT;
        if (host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO,
                            reinterpret_cast<host_info_t>(&info), &count) == KERN_SUCCESS) {
            for (int i = 0; i < CPU_STATE_MAX; ++i) t[i] = info.cpu_ticks[i];
        }
        return t;
    };

    auto t1 = read_ticks();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));
    auto t2 = read_ticks();

    uint64_t user_diff  = t2[CPU_STATE_USER]   - t1[CPU_STATE_USER];
    uint64_t sys_diff   = t2[CPU_STATE_SYSTEM] - t1[CPU_STATE_SYSTEM];
    uint64_t nice_diff  = t2[CPU_STATE_NICE]   - t1[CPU_STATE_NICE];
    uint64_t idle_diff  = t2[CPU_STATE_IDLE]   - t1[CPU_STATE_IDLE];
    uint64_t total_diff = user_diff + sys_diff + nice_diff + idle_diff;

    if (total_diff == 0) return 0.0;
    return (user_diff + sys_diff + nice_diff) * 100.0 / total_diff;
}

double HeartbeatReporter::get_mem_percent() const {
    mach_port_t host = mach_host_self();
    vm_size_t page_size;
    host_page_size(host, &page_size);
    vm_statistics64_data_t stats;
    mach_msg_type_number_t count = HOST_VM_INFO64_COUNT;
    host_statistics64(host, HOST_VM_INFO64, (host_info64_t)&stats, &count);
    uint64_t used = (stats.active_count + stats.wire_count) * page_size;
    uint64_t total = (stats.active_count + stats.inactive_count +
                      stats.wire_count + stats.free_count) * page_size;
    return total ? used * 100.0 / total : 0.0;
}

double HeartbeatReporter::get_disk_percent() const {
    struct statvfs st;
    if (statvfs("/", &st) != 0) return 0.0;
    uint64_t total = (uint64_t)st.f_blocks * st.f_frsize;
    uint64_t free  = (uint64_t)st.f_bfree  * st.f_frsize;
    return total ? (total - free) * 100.0 / total : 0.0;
}

#endif

} // namespace st
