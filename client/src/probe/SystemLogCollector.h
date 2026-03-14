#pragma once
#include "../transport/RingBuffer.h"
#include "../config/Config.h"
#include "PolicyManager.h"
#include "terminal.pb.h"
#include <atomic>
#include <thread>
#include <functional>

namespace st {

using LogEntryCallback = std::function<void(terminal::v1::LogEntry)>;

/**
 * 系统日志采集器（平台适配层）
 *
 * Windows  : Windows Event Log API (EvtSubscribe + 异步回调)
 * Linux    : systemd SD-Journal API (sd_journal_open / sd_journal_wait)
 * macOS    : Unified Logging (os_log / OSLogStore — 可选)
 *
 * 采集到的日志按 PolicyManager 中配置的最低严重级别过滤后通过 callback_ 上报。
 */
class SystemLogCollector {
public:
    SystemLogCollector(const CollectionConfig& cfg,
                       const terminal::v1::TerminalIdentity& identity,
                       LogEntryCallback callback,
                       PolicyManager* policy_mgr = nullptr);
    ~SystemLogCollector();

    void start();
    void stop();

private:
    void collect_loop();

#if defined(PLATFORM_WINDOWS)
    void init_windows();
    void poll_windows_event_log();

    // 静态成员回调：由 EvtSubscribe 从后台线程调用
    static DWORD WINAPI win_event_callback(EVT_SUBSCRIBE_NOTIFY_ACTION action,
                                           PVOID userContext,
                                           EVT_HANDLE eventHandle);

    EVT_HANDLE subscription_{nullptr};

#elif defined(PLATFORM_LINUX)
    void init_linux_journald();
    void poll_journald();
    void poll_auditd();     // 预留：auditd netlink

    sd_journal* journal_{nullptr};

#elif defined(PLATFORM_MACOS)
    void init_macos_unified_log();
    void poll_macos_unified_log();
#endif

    CollectionConfig                    cfg_;
    terminal::v1::TerminalIdentity      identity_;
    LogEntryCallback                    callback_;
    PolicyManager*                      policy_mgr_{nullptr};
    std::atomic<bool>                   running_{false};
    std::thread                         worker_;
};

} // namespace st
