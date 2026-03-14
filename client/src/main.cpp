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

#if defined(PLATFORM_WINDOWS)
#  include <windows.h>
#  include <rpc.h>
#  pragma comment(lib, "rpcrt4.lib")
#endif

namespace {
std::atomic<bool> g_running{true};
}

static void signal_handler(int) { g_running = false; }

// 生成持久化的终端唯一 ID（UUID v4）
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
    // 简单实现，生产中应使用 libuuid
    std::random_device rd;
    std::mt19937_64 eng(rd());
    std::uniform_int_distribution<uint64_t> distr;
    char buf[37];
    uint64_t a = distr(eng), b = distr(eng);
    a = (a & 0xFFFFFFFFFFFF0FFFULL) | 0x0000000000004000ULL;
    b = (b & 0x3FFFFFFFFFFFFFFFULL) | 0x8000000000000000ULL;
    snprintf(buf, sizeof(buf),
             "%08x-%04x-%04x-%04x-%012llx",
             (uint32_t)(a >> 32), (uint16_t)(a >> 16), (uint16_t)a,
             (uint16_t)(b >> 48), (unsigned long long)(b & 0x0000FFFFFFFFFFFFULL));
    return buf;
#endif
}

// 组装 TerminalIdentity
static terminal::v1::TerminalIdentity build_identity(st::Config& cfg) {
    terminal::v1::TerminalIdentity id;
    if (cfg.terminal_id.empty()) {
        cfg.terminal_id = generate_uuid();
        // TODO: 持久化 terminal_id 到配置文件
    }
    id.set_terminal_id(cfg.terminal_id);
    id.set_agent_version(cfg.agent_version);

    // TODO: 填写 hostname / ip_address / os / os_version
#if defined(PLATFORM_WINDOWS)
    id.set_os(terminal::v1::OS_WINDOWS);
#elif defined(PLATFORM_LINUX)
    id.set_os(terminal::v1::OS_LINUX);
#elif defined(PLATFORM_MACOS)
    id.set_os(terminal::v1::OS_MACOS);
#endif
    return id;
}

int main(int argc, char* argv[]) {
    // ─── 日志初始化 ────────────────────────────────────────────────────────
    auto logger = spdlog::rotating_logger_mt(
        "st_agent", "/var/log/safe_terminal/agent.log",
        10 * 1024 * 1024, 3);
    spdlog::set_default_logger(logger);
    spdlog::set_level(spdlog::level::info);
    spdlog::info("Safe Terminal Agent starting...");

    // ─── 加载配置 ──────────────────────────────────────────────────────────
    std::string config_path = (argc > 1) ? argv[1] : "/etc/safe_terminal/config.toml";
    st::Config cfg;
    try {
        cfg = st::Config::load(config_path);
    } catch (const std::exception& e) {
        spdlog::error("Failed to load config: {}", e.what());
        return 1;
    }

    auto identity = build_identity(cfg);

    // ─── 组件初始化 ────────────────────────────────────────────────────────
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

    // 日志采集：将条目序列化后压入缓冲区
    st::SystemLogCollector log_collector(cfg.collection, identity,
        [&buffer](terminal::v1::LogEntry entry) {
            std::string data;
            entry.SerializeToString(&data);
            buffer.push({0, "logs", std::move(data)});
        });

    // USB 监控
    st::UsbMonitor usb_monitor(cfg.collection, identity,
        [&buffer](terminal::v1::UsbEvent event) {
            std::string data;
            event.SerializeToString(&data);
            buffer.push({0, "usb", std::move(data)});
        });

    st::HeartbeatReporter heartbeat(cfg.collection, identity, buffer);

    // ─── 启动所有组件 ──────────────────────────────────────────────────────
    signal(SIGINT,  signal_handler);
    signal(SIGTERM, signal_handler);

    grpc_client.start();
    log_collector.start();
    usb_monitor.start();
    heartbeat.start();

    spdlog::info("Agent started. terminal_id={}", cfg.terminal_id);

    while (g_running) {
        std::this_thread::sleep_for(std::chrono::seconds(1));
    }

    // ─── 优雅关机 ──────────────────────────────────────────────────────────
    spdlog::info("Shutting down...");
    heartbeat.stop();
    usb_monitor.stop();
    log_collector.stop();
    grpc_client.stop();
    buffer.flush_to_disk();
    policy_mgr.save(cfg.cache.disk_path + "/policy.bin");

    spdlog::info("Agent stopped.");
    return 0;
}
