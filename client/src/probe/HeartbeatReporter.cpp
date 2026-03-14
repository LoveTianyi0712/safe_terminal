#include "HeartbeatReporter.h"
#include <spdlog/spdlog.h>
#include <google/protobuf/util/time_util.h>

#if defined(PLATFORM_WINDOWS)
#  include <windows.h>
#  include <pdh.h>
#  pragma comment(lib, "pdh.lib")
#elif defined(PLATFORM_LINUX)
#  include <sys/statvfs.h>
#  include <fstream>
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
    // TODO: 使用 PDH (Performance Data Helper) 查询 \Processor(_Total)\% Processor Time
    // PdhOpenQuery / PdhAddCounter / PdhCollectQueryData / PdhGetFormattedCounterValue
    return 0.0;
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
    // TODO: 两次读取 /proc/stat 计算差值得到 CPU 使用率
    // (user+nice+system+irq+softirq) / total * 100
    return 0.0;
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
    // TODO: 使用 host_statistics64(HOST_CPU_LOAD_INFO) 两次差值
    return 0.0;
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
