#include "SystemLogCollector.h"
#include <spdlog/spdlog.h>
#include <google/protobuf/util/time_util.h>

// 平台头文件
#if defined(PLATFORM_WINDOWS)
#  include <windows.h>
#  include <winevt.h>
#  pragma comment(lib, "wevtapi.lib")
#elif defined(PLATFORM_LINUX)
#  include <systemd/sd-journal.h>
// auditd 通过 libaudit 或直接读 /var/log/audit/audit.log
#elif defined(PLATFORM_MACOS)
#  include <os/log.h>
// macOS 13+ 使用 OSLogStore (Swift/ObjC-C API)
#endif

namespace st {

SystemLogCollector::SystemLogCollector(const CollectionConfig& cfg,
                                       const terminal::v1::TerminalIdentity& identity,
                                       LogEntryCallback callback)
    : cfg_(cfg), identity_(identity), callback_(std::move(callback)) {}

SystemLogCollector::~SystemLogCollector() { stop(); }

void SystemLogCollector::start() {
    running_ = true;
    worker_ = std::thread([this] { collect_loop(); });
}

void SystemLogCollector::stop() {
    running_ = false;
    if (worker_.joinable()) worker_.join();
}

void SystemLogCollector::collect_loop() {
    spdlog::info("[LogCollector] Starting on platform");

#if defined(PLATFORM_WINDOWS)
    init_windows();
    while (running_) {
        poll_windows_event_log();
        std::this_thread::sleep_for(std::chrono::milliseconds(500));
    }
#elif defined(PLATFORM_LINUX)
    init_linux_journald();
    while (running_) {
        poll_journald();
        std::this_thread::sleep_for(std::chrono::milliseconds(500));
    }
#elif defined(PLATFORM_MACOS)
    init_macos_unified_log();
    while (running_) {
        poll_macos_unified_log();
        std::this_thread::sleep_for(std::chrono::milliseconds(500));
    }
#else
    spdlog::warn("[LogCollector] Unsupported platform, no logs collected");
#endif
}

// ─── Windows 实现 ──────────────────────────────────────────────────────────
#if defined(PLATFORM_WINDOWS)

void SystemLogCollector::init_windows() {
    // TODO: 调用 EvtSubscribe() 订阅 Security / System / Application 频道
    // 使用 EvtSubscribeStartAfterBookmark 支持断线续传
    spdlog::info("[LogCollector] Windows Event Log subscription initialized");
}

void SystemLogCollector::poll_windows_event_log() {
    // TODO: 处理订阅回调 EVT_SUBSCRIBE_CALLBACK
    // 解析 EvtRender() 获取 EventID, TimeCreated, Level, Message
    // 按 cfg_.log_level 过滤后通过 callback_ 上报
}

// ─── Linux 实现 ────────────────────────────────────────────────────────────
#elif defined(PLATFORM_LINUX)

void SystemLogCollector::init_linux_journald() {
    // TODO: sd_journal_open() 打开 journald，移动游标到当前位置
    // sd_journal_seek_tail() + sd_journal_previous()
    spdlog::info("[LogCollector] journald initialized");
}

void SystemLogCollector::poll_journald() {
    // TODO:
    // sd_journal_wait(j, 100000) 等待新条目
    // SD_JOURNAL_FOREACH_DATA 遍历字段
    // 提取 MESSAGE, PRIORITY, SYSLOG_IDENTIFIER, _COMM
    // 构造 LogEntry 并调用 callback_
}

void SystemLogCollector::poll_auditd() {
    // TODO: 连接 auditd netlink socket 或解析 audit.log
    // 关注 USER_AUTH, USER_ACCT, SYSCALL 类型事件
}

// ─── macOS 实现 ────────────────────────────────────────────────────────────
#elif defined(PLATFORM_MACOS)

void SystemLogCollector::init_macos_unified_log() {
    // TODO: macOS 12+ 使用 OSLogStore API（需要 Swift 桥接或 ObjC）
    // 备选：执行 `log stream --predicate 'eventType == logEvent'` 子进程
    spdlog::info("[LogCollector] macOS Unified Log initialized (stub)");
}

void SystemLogCollector::poll_macos_unified_log() {
    // TODO: 解析 OSLogEntry，过滤并构造 LogEntry
}

#endif

} // namespace st
