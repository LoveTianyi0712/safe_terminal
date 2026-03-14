#pragma once
#include "../transport/RingBuffer.h"
#include "../config/Config.h"
#include "terminal.pb.h"
#include <atomic>
#include <thread>
#include <functional>

namespace st {

using LogEntryCallback = std::function<void(terminal::v1::LogEntry)>;

/**
 * 系统日志采集器（平台适配层）
 *
 * Windows  : Windows Event Log API (EvtSubscribe)
 * Linux    : auditd netlink socket / journald SD-Journal API
 * macOS    : Unified Logging (os_log_create / OSLogStore)
 */
class SystemLogCollector {
public:
    explicit SystemLogCollector(const CollectionConfig& cfg,
                                const terminal::v1::TerminalIdentity& identity,
                                LogEntryCallback callback);
    ~SystemLogCollector();

    void start();
    void stop();

private:
    void collect_loop();

#if defined(PLATFORM_WINDOWS)
    void init_windows();
    void poll_windows_event_log();
#elif defined(PLATFORM_LINUX)
    void init_linux_journald();
    void poll_journald();
    void poll_auditd();
#elif defined(PLATFORM_MACOS)
    void init_macos_unified_log();
    void poll_macos_unified_log();
#endif

    CollectionConfig                    cfg_;
    terminal::v1::TerminalIdentity      identity_;
    LogEntryCallback                    callback_;
    std::atomic<bool>                   running_{false};
    std::thread                         worker_;
};

} // namespace st
