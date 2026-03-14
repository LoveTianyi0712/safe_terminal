#include "config/Config.h"
#include "transport/RingBuffer.h"
#include "transport/GrpcClient.h"
#include "probe/SystemLogCollector.h"
#include "probe/UsbMonitor.h"
#include "probe/HeartbeatReporter.h"
#include "probe/PolicyManager.h"

#include <spdlog/spdlog.h>
#include <spdlog/sinks/rotating_file_sink.h>
#include <csignal>
#include <atomic>
#include <random>
#include <string>
#include <array>
#include <cstdio>

#if defined(PLATFORM_WINDOWS)
#  include <windows.h>
#  include <rpc.h>
#  include <winsock2.h>
#  include <ws2tcpip.h>
#  pragma comment(lib, "rpcrt4.lib")
#  pragma comment(lib, "ws2_32.lib")
#elif defined(PLATFORM_LINUX) || defined(PLATFORM_MACOS)
#  include <unistd.h>
#  include <ifaddrs.h>
#  include <netdb.h>
#  include <arpa/inet.h>
#  include <sys/socket.h>
#  include <sys/utsname.h>
#endif

namespace {
std::atomic<bool> g_running{true};
}

static void signal_handler(int) { g_running = false; }

// ─── UUID v4 生成 ─────────────────────────────────────────────────────────────
static std::string generate_uuid() {
#if defined(PLATFORM_WINDOWS)
    UUID uuid;
    UuidCreate(&uuid);
    RPC_CSTR str;
    UuidToStringA(&uuid, &str);
    std::string result(reinterpret_cast<char*>(str));
    RpcStringFreeA(&str);
    return result;
#else
    std::random_device rd;
    std::mt19937_64 eng(rd());
    std::uniform_int_distribution<uint64_t> distr;
    char buf[37];
    uint64_t a = distr(eng), b = distr(eng);
    // 设置版本位 (4) 和变体位
    a = (a & 0xFFFFFFFFFFFF0FFFULL) | 0x0000000000004000ULL;
    b = (b & 0x3FFFFFFFFFFFFFFFULL) | 0x8000000000000000ULL;
    snprintf(buf, sizeof(buf),
             "%08x-%04x-%04x-%04x-%012llx",
             (uint32_t)(a >> 32),
             (uint16_t)(a >> 16),
             (uint16_t)a,
             (uint16_t)(b >> 48),
             (unsigned long long)(b & 0x0000FFFFFFFFFFFFULL));
    return buf;
#endif
}

// ─── 获取本机主机名 ─────────────────────────────────────────────────────────
static std::string get_hostname() {
#if defined(PLATFORM_WINDOWS)
    char buf[256] = {};
    DWORD len = sizeof(buf);
    GetComputerNameA(buf, &len);
    return buf;
#else
    char buf[256] = {};
    gethostname(buf, sizeof(buf));
    return buf;
#endif
}

// ─── 获取第一个非回环 IPv4 地址 ─────────────────────────────────────────────
static std::string get_primary_ip() {
#if defined(PLATFORM_WINDOWS)
    char hostname[256] = {};
    DWORD len = sizeof(hostname);
    GetComputerNameA(hostname, &len);

    addrinfo hints{}, *res = nullptr;
    hints.ai_family   = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    if (getaddrinfo(hostname, nullptr, &hints, &res) == 0 && res) {
        char ip[INET_ADDRSTRLEN] = {};
        inet_ntop(AF_INET,
                  &reinterpret_cast<sockaddr_in*>(res->ai_addr)->sin_addr,
                  ip, sizeof(ip));
        freeaddrinfo(res);
        return ip;
    }
    return "127.0.0.1";
#else
    struct ifaddrs* ifa_list = nullptr;
    getifaddrs(&ifa_list);

    std::string result = "127.0.0.1";
    for (auto* ifa = ifa_list; ifa; ifa = ifa->ifa_next) {
        if (!ifa->ifa_addr || ifa->ifa_addr->sa_family != AF_INET) continue;
        auto* addr = reinterpret_cast<sockaddr_in*>(ifa->ifa_addr);
        // 跳过回环接口
        if (ntohl(addr->sin_addr.s_addr) == INADDR_LOOPBACK) continue;
        char ip[INET_ADDRSTRLEN] = {};
        inet_ntop(AF_INET, &addr->sin_addr, ip, sizeof(ip));
        result = ip;
        break; // 取第一个有效地址
    }
    if (ifa_list) freeifaddrs(ifa_list);
    return result;
#endif
}

// ─── 获取 OS 版本字符串 ───────────────────────────────────────────────────────
static std::string get_os_version() {
#if defined(PLATFORM_WINDOWS)
    OSVERSIONINFOEXA osi{};
    osi.dwOSVersionInfoSize = sizeof(osi);
    // GetVersionEx 在 Win8.1+ 需要 manifest，此处用 RtlGetVersion（更可靠）
    using RtlGetVersion_t = LONG(WINAPI*)(OSVERSIONINFOEXA*);
    auto rtl = reinterpret_cast<RtlGetVersion_t>(
        GetProcAddress(GetModuleHandleA("ntdll.dll"), "RtlGetVersion"));
    if (rtl) rtl(&osi);
    char buf[64];
    snprintf(buf, sizeof(buf), "Windows %lu.%lu.%lu",
             osi.dwMajorVersion, osi.dwMinorVersion, osi.dwBuildNumber);
    return buf;
#elif defined(PLATFORM_LINUX) || defined(PLATFORM_MACOS)
    struct utsname uts{};
    uname(&uts);
    return std::string(uts.sysname) + " " + uts.release;
#else
    return "Unknown";
#endif
}

// ─── 组装 TerminalIdentity ────────────────────────────────────────────────────
static terminal::v1::TerminalIdentity build_identity(st::Config& cfg) {
    terminal::v1::TerminalIdentity id;

    // 生成并持久化 terminal_id
    if (cfg.terminal_id.empty()) {
        cfg.terminal_id = generate_uuid();
        // TODO: 持久化 terminal_id 到配置文件（Config::save_terminal_id()）
        spdlog::info("Generated new terminal_id: {}", cfg.terminal_id);
    }
    id.set_terminal_id(cfg.terminal_id);
    id.set_agent_version(cfg.agent_version);

    // 填写主机信息
    id.set_hostname(get_hostname());
    id.set_ip_address(get_primary_ip());
    id.set_os_version(get_os_version());

#if defined(PLATFORM_WINDOWS)
    id.set_os(terminal::v1::OS_WINDOWS);
#elif defined(PLATFORM_LINUX)
    id.set_os(terminal::v1::OS_LINUX);
#elif defined(PLATFORM_MACOS)
    id.set_os(terminal::v1::OS_MACOS);
#endif

    spdlog::info("Terminal identity: id={} hostname={} ip={} os={}",
                 id.terminal_id(), id.hostname(), id.ip_address(), id.os_version());
    return id;
}

int main(int argc, char* argv[]) {
    // ─── 日志初始化 ──────────────────────────────────────────────────────────
#if defined(PLATFORM_WINDOWS)
    const std::string log_path = "C:/ProgramData/SafeTerminal/agent.log";
#else
    const std::string log_path = "/var/log/safe_terminal/agent.log";
#endif

    try {
        auto logger = spdlog::rotating_logger_mt(
            "st_agent", log_path, 10 * 1024 * 1024, 3);
        spdlog::set_default_logger(logger);
    } catch (...) {
        // 日志文件创建失败时退化到控制台，不阻断启动
        spdlog::set_default_logger(spdlog::default_logger());
    }
    spdlog::set_level(spdlog::level::info);
    spdlog::info("Safe Terminal Agent starting...");

    // ─── 加载配置 ────────────────────────────────────────────────────────────
    std::string config_path = (argc > 1) ? argv[1] : "/etc/safe_terminal/config.toml";
    st::Config cfg;
    try {
        cfg = st::Config::load(config_path);
    } catch (const std::exception& e) {
        spdlog::error("Failed to load config: {}", e.what());
        return 1;
    }

    auto identity = build_identity(cfg);

    // ─── 组件初始化 ──────────────────────────────────────────────────────────
    st::RingBuffer buffer(
        cfg.cache.memory_capacity,
        cfg.cache.disk_path,
        cfg.cache.disk_max_mb);
    buffer.recover_from_disk();

    st::PolicyManager policy_mgr;
    policy_mgr.load(cfg.cache.disk_path + "/policy.bin");

    st::GrpcClient grpc_client(cfg.gateway, buffer,
        [&policy_mgr](const terminal::v1::Policy& p) {
            policy_mgr.update(p);
        });

    // 日志采集：注入 &policy_mgr 用于按级别过滤
    st::SystemLogCollector log_collector(cfg.collection, identity,
        [&buffer](terminal::v1::LogEntry entry) {
            std::string data;
            entry.SerializeToString(&data);
            buffer.push({0, "logs", std::move(data)});
        },
        &policy_mgr);  // <-- 传入策略管理器

    // USB 监控
    st::UsbMonitor usb_monitor(cfg.collection, identity,
        [&buffer](terminal::v1::UsbEvent event) {
            std::string data;
            event.SerializeToString(&data);
            buffer.push({0, "usb", std::move(data)});
        });

    st::HeartbeatReporter heartbeat(cfg.collection, identity, buffer);

    // ─── 启动所有组件 ────────────────────────────────────────────────────────
    signal(SIGINT,  signal_handler);
    signal(SIGTERM, signal_handler);

    grpc_client.start();
    log_collector.start();
    usb_monitor.start();
    heartbeat.start();

    spdlog::info("Agent started. terminal_id={} hostname={} ip={}",
                 cfg.terminal_id, identity.hostname(), identity.ip_address());

    // 主线程等待退出信号
    while (g_running) {
        std::this_thread::sleep_for(std::chrono::seconds(1));
    }

    // ─── 优雅关机 ────────────────────────────────────────────────────────────
    spdlog::info("Shutting down...");
    heartbeat.stop();
    usb_monitor.stop();
    log_collector.stop();
    grpc_client.stop();
    buffer.flush_to_disk();
    policy_mgr.save(cfg.cache.disk_path + "/policy.bin");

    spdlog::info("Agent stopped cleanly.");
    return 0;
}
