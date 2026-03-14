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
// Windows Service 状态句柄（仅服务模式使用）
#if defined(PLATFORM_WINDOWS)
SERVICE_STATUS_HANDLE g_svc_status_handle = nullptr;
SERVICE_STATUS        g_svc_status        = {};
#endif
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

// ─── 获取主要 IPv4 地址 ─────────────────────────────────────────────────────
static std::string get_primary_ip() {
#if defined(PLATFORM_WINDOWS)
    char hostname[256] = {};
    DWORD len = sizeof(hostname);
    GetComputerNameA(hostname, &len);
    struct addrinfo hints = {}, *res = nullptr;
    hints.ai_family = AF_INET;
    if (getaddrinfo(hostname, nullptr, &hints, &res) == 0 && res) {
        char ip[INET_ADDRSTRLEN] = {};
        inet_ntop(AF_INET,
                  &reinterpret_cast<struct sockaddr_in*>(res->ai_addr)->sin_addr,
                  ip, sizeof(ip));
        freeaddrinfo(res);
        return ip;
    }
    return "127.0.0.1";
#else
    struct ifaddrs* ifa = nullptr;
    getifaddrs(&ifa);
    std::string result = "127.0.0.1";
    for (auto* cur = ifa; cur; cur = cur->ifa_next) {
        if (!cur->ifa_addr || cur->ifa_addr->sa_family != AF_INET) continue;
        std::string name(cur->ifa_name);
        if (name == "lo") continue;
        char buf[INET_ADDRSTRLEN] = {};
        inet_ntop(AF_INET,
                  &reinterpret_cast<struct sockaddr_in*>(cur->ifa_addr)->sin_addr,
                  buf, sizeof(buf));
        result = buf;
        break;
    }
    if (ifa) freeifaddrs(ifa);
    return result;
#endif
}

// ─── 获取操作系统版本字符串 ─────────────────────────────────────────────────
static std::string get_os_version() {
#if defined(PLATFORM_WINDOWS)
    OSVERSIONINFOEXA osvi = {};
    osvi.dwOSVersionInfoSize = sizeof(osvi);
#pragma warning(suppress: 4996)
    GetVersionExA(reinterpret_cast<OSVERSIONINFOA*>(&osvi));
    char buf[64];
    snprintf(buf, sizeof(buf), "Windows %lu.%lu.%lu",
             osvi.dwMajorVersion, osvi.dwMinorVersion, osvi.dwBuildNumber);
    return buf;
#elif defined(PLATFORM_LINUX) || defined(PLATFORM_MACOS)
    struct utsname uts = {};
    uname(&uts);
    return std::string(uts.sysname) + " " + uts.release;
#else
    return "Unknown";
#endif
}

// ─── 构建终端身份 ─────────────────────────────────────────────────────────────
static terminal::v1::TerminalIdentity build_identity(st::Config& cfg,
                                                      const std::string& config_path) {
    terminal::v1::TerminalIdentity id;

    if (cfg.terminal_id.empty()) {
        cfg.terminal_id = generate_uuid();
        cfg.save_terminal_id(config_path);
        spdlog::info("Generated new terminal_id: {}", cfg.terminal_id);
    }
    id.set_terminal_id(cfg.terminal_id);
    id.set_agent_version(cfg.agent_version);
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

// ─── 核心 Agent 逻辑（服务模式和控制台模式共享此函数） ──────────────────────
static void run_agent(const std::string& config_path) {
    // 日志初始化
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
    }
    spdlog::set_level(spdlog::level::info);
    spdlog::info("Safe Terminal Agent starting... config={}", config_path);

    // 加载配置
    st::Config cfg;
    try {
        cfg = st::Config::load(config_path);
    } catch (const std::exception& e) {
        spdlog::error("Failed to load config: {}", e.what());
        return;
    }

    auto identity = build_identity(cfg, config_path);

    // 组件初始化
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

    st::SystemLogCollector log_collector(cfg.collection, identity,
        [&buffer](terminal::v1::LogEntry entry) {
            std::string data;
            entry.SerializeToString(&data);
            buffer.push({0, "logs", std::move(data)});
        },
        &policy_mgr);

    st::UsbMonitor usb_monitor(cfg.collection, identity,
        [&buffer](terminal::v1::UsbEvent event) {
            std::string data;
            event.SerializeToString(&data);
            buffer.push({0, "usb_event", std::move(data)});
        });

    st::HeartbeatReporter heartbeat(cfg.collection, identity, buffer);

    // 启动所有组件
    grpc_client.start();
    log_collector.start();
    usb_monitor.start();
    heartbeat.start();

    spdlog::info("Agent started. terminal_id={} hostname={} ip={}",
                 cfg.terminal_id, identity.hostname(), identity.ip_address());

    // 主循环等待退出信号
    while (g_running) {
        std::this_thread::sleep_for(std::chrono::seconds(1));
    }

    // 优雅关机
    spdlog::info("Shutting down...");
    heartbeat.stop();
    usb_monitor.stop();
    log_collector.stop();
    grpc_client.stop();
    buffer.flush_to_disk();
    policy_mgr.save(cfg.cache.disk_path + "/policy.bin");
    spdlog::info("Agent stopped cleanly.");
}

// ─── Windows Service API 包装 ─────────────────────────────────────────────────
#if defined(PLATFORM_WINDOWS)

static std::string g_config_path_svc;

static void report_svc_status(DWORD state, DWORD exit_code = NO_ERROR, DWORD wait_hint = 0) {
    static DWORD checkpoint = 1;
    g_svc_status.dwServiceType             = SERVICE_WIN32_OWN_PROCESS;
    g_svc_status.dwCurrentState            = state;
    g_svc_status.dwControlsAccepted        = (state == SERVICE_START_PENDING)
                                             ? 0
                                             : SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN;
    g_svc_status.dwWin32ExitCode           = exit_code;
    g_svc_status.dwServiceSpecificExitCode = 0;
    g_svc_status.dwWaitHint                = wait_hint;
    g_svc_status.dwCheckPoint             = (state == SERVICE_RUNNING || state == SERVICE_STOPPED)
                                             ? 0
                                             : checkpoint++;
    SetServiceStatus(g_svc_status_handle, &g_svc_status);
}

static VOID WINAPI SvcCtrlHandler(DWORD ctrl) {
    switch (ctrl) {
    case SERVICE_CONTROL_STOP:
    case SERVICE_CONTROL_SHUTDOWN:
        report_svc_status(SERVICE_STOP_PENDING, NO_ERROR, 5000);
        g_running = false;
        break;
    default:
        break;
    }
}

static VOID WINAPI SvcMain(DWORD /*argc*/, LPSTR* /*argv*/) {
    g_svc_status_handle = RegisterServiceCtrlHandlerA("SafeTerminalAgent", SvcCtrlHandler);
    if (!g_svc_status_handle) return;

    report_svc_status(SERVICE_START_PENDING, NO_ERROR, 3000);
    report_svc_status(SERVICE_RUNNING);

    run_agent(g_config_path_svc);

    report_svc_status(SERVICE_STOPPED);
}

#endif  // PLATFORM_WINDOWS

// ─── main 入口 ───────────────────────────────────────────────────────────────
int main(int argc, char* argv[]) {
#if defined(PLATFORM_WINDOWS)
    // 默认配置路径（可通过命令行覆盖）
    g_config_path_svc = (argc > 1) ? argv[1] : "C:/ProgramData/SafeTerminal/config.toml";

    // 尝试以 Windows 服务模式启动；若失败（非服务上下文）则回退到控制台模式
    SERVICE_TABLE_ENTRYA dispatch_table[] = {
        { const_cast<LPSTR>("SafeTerminalAgent"), SvcMain },
        { nullptr, nullptr }
    };
    if (!StartServiceCtrlDispatcherA(dispatch_table)) {
        // ERROR_FAILED_SERVICE_CONTROLLER_CONNECT (1063) 表示不在服务上下文中
        if (GetLastError() == ERROR_FAILED_SERVICE_CONTROLLER_CONNECT) {
            // 控制台交互模式
            signal(SIGINT,  signal_handler);
            signal(SIGTERM, signal_handler);
            run_agent(g_config_path_svc);
        }
    }
    return 0;
#else
    // Linux / macOS：直接运行
    signal(SIGINT,  signal_handler);
    signal(SIGTERM, signal_handler);
    const std::string config_path = (argc > 1) ? argv[1] : "/etc/safe_terminal/config.toml";
    run_agent(config_path);
    return 0;
#endif
}
