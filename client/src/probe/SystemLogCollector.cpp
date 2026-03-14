#include "SystemLogCollector.h"
#include <spdlog/spdlog.h>
#include <google/protobuf/util/time_util.h>

// ─── 平台头文件 ──────────────────────────────────────────────────────────────
#if defined(PLATFORM_WINDOWS)
#  include <windows.h>
#  include <winevt.h>
#  include <vector>
#  include <string>
#  pragma comment(lib, "wevtapi.lib")
#elif defined(PLATFORM_LINUX)
#  include <systemd/sd-journal.h>
#  include <cstring>
#  include <cerrno>
#elif defined(PLATFORM_MACOS)
#  include <os/log.h>
#endif

namespace st {

SystemLogCollector::SystemLogCollector(const CollectionConfig& cfg,
                                       const terminal::v1::TerminalIdentity& identity,
                                       LogEntryCallback callback,
                                       PolicyManager* policy_mgr)
    : cfg_(cfg), identity_(identity), callback_(std::move(callback)),
      policy_mgr_(policy_mgr) {}

SystemLogCollector::~SystemLogCollector() { stop(); }

void SystemLogCollector::start() {
    running_ = true;
    worker_ = std::thread([this] { collect_loop(); });
}

void SystemLogCollector::stop() {
    running_ = false;
    if (worker_.joinable()) worker_.join();

#if defined(PLATFORM_WINDOWS)
    if (subscription_) {
        EvtClose(subscription_);
        subscription_ = nullptr;
    }
#elif defined(PLATFORM_LINUX)
    if (journal_) {
        sd_journal_close(journal_);
        journal_ = nullptr;
    }
#endif
}

void SystemLogCollector::collect_loop() {
    spdlog::info("[LogCollector] Starting log collection");

#if defined(PLATFORM_WINDOWS)
    init_windows();
    // EvtSubscribe 使用异步回调，collect_loop 只需保持运行
    while (running_) {
        poll_windows_event_log();
    }
#elif defined(PLATFORM_LINUX)
    init_linux_journald();
    while (running_) {
        poll_journald();
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

// ═══════════════════════════════════════════════════════════════════════════════
// Windows 实现 — EvtSubscribe 异步推送模式
// ═══════════════════════════════════════════════════════════════════════════════
#if defined(PLATFORM_WINDOWS)

/**
 * 静态回调：由 Windows Event Log 服务从后台线程调用
 *
 * 每当有匹配事件时：
 *   1. EvtRender → XML 字符串
 *   2. 简单字符串提取 EventID / Level / Data 字段
 *   3. 映射 Level → Severity，按策略过滤后触发 callback_
 */
DWORD WINAPI SystemLogCollector::win_event_callback(
    EVT_SUBSCRIBE_NOTIFY_ACTION action,
    PVOID userContext,
    EVT_HANDLE eventHandle)
{
    if (action != EvtSubscribeActionDeliver) return ERROR_SUCCESS;

    auto* self = static_cast<SystemLogCollector*>(userContext);
    if (!self || !self->running_) return ERROR_SUCCESS;

    // ── 1. 渲染为 XML ──────────────────────────────────────────────────────
    DWORD bufferUsed = 0, propertyCount = 0;
    // 首次调用获取所需缓冲大小
    EvtRender(nullptr, eventHandle, EvtRenderEventXml, 0, nullptr,
              &bufferUsed, &propertyCount);
    if (GetLastError() != ERROR_INSUFFICIENT_BUFFER) return ERROR_SUCCESS;

    std::vector<WCHAR> wbuf(bufferUsed / sizeof(WCHAR) + 1);
    if (!EvtRender(nullptr, eventHandle, EvtRenderEventXml,
                   bufferUsed, wbuf.data(), &bufferUsed, &propertyCount)) {
        return ERROR_SUCCESS;
    }

    // ── 2. 宽字符 → UTF-8 ─────────────────────────────────────────────────
    int narrow_len = WideCharToMultiByte(CP_UTF8, 0, wbuf.data(), -1,
                                         nullptr, 0, nullptr, nullptr);
    if (narrow_len <= 1) return ERROR_SUCCESS;
    std::string xml(static_cast<size_t>(narrow_len), '\0');
    WideCharToMultiByte(CP_UTF8, 0, wbuf.data(), -1,
                        xml.data(), narrow_len, nullptr, nullptr);

    // ── 3. 从 XML 提取字段（避免引入完整 XML 解析器）─────────────────────
    auto extract_tag = [&](const std::string& tag) -> std::string {
        const std::string open  = "<" + tag + ">";
        const std::string close = "</" + tag + ">";
        auto s = xml.find(open);
        if (s == std::string::npos) return "";
        s += open.size();
        auto e = xml.find(close, s);
        if (e == std::string::npos) return "";
        return xml.substr(s, e - s);
    };

    // 提取 EventID
    std::string event_id = extract_tag("EventID");

    // 提取 Level（Windows 级别: 1=Critical 2=Error 3=Warning 4=Information 5=Verbose）
    std::string level_str = extract_tag("Level");
    int level = level_str.empty() ? 4 : std::stoi(level_str);

    terminal::v1::Severity sev;
    switch (level) {
        case 1:  sev = terminal::v1::SEV_CRITICAL; break;
        case 2:  sev = terminal::v1::SEV_ERROR;    break;
        case 3:  sev = terminal::v1::SEV_WARNING;  break;
        default: sev = terminal::v1::SEV_INFO;     break;
    }

    // 按策略最低级别过滤
    if (self->policy_mgr_ && sev < self->policy_mgr_->get_log_level()) {
        return ERROR_SUCCESS;
    }

    // 提取 Channel（如 Security / System / Application）
    std::string channel = extract_tag("Channel");

    // 提取第一条 Data 字段作为消息摘要
    std::string message;
    {
        auto ds = xml.find("<Data");
        if (ds != std::string::npos) {
            auto de = xml.find('>', ds);
            if (de != std::string::npos) {
                auto dend = xml.find("</Data>", de + 1);
                if (dend != std::string::npos)
                    message = xml.substr(de + 1, dend - de - 1);
            }
        }
    }
    if (message.empty()) {
        message = "EventID=" + event_id;
    }

    // ── 4. 构造 LogEntry 并上报 ────────────────────────────────────────────
    terminal::v1::LogEntry entry;
    *entry.mutable_identity() = self->identity_;
    *entry.mutable_ts()       = google::protobuf::util::TimeUtil::GetCurrentTime();
    entry.set_severity(sev);
    entry.set_source(channel.empty() ? "winevt" : channel);
    entry.set_event_id(event_id);
    entry.set_message(message);

    if (self->callback_) self->callback_(std::move(entry));
    return ERROR_SUCCESS;
}

void SystemLogCollector::init_windows() {
    // 订阅 Security 频道（Warning 及以上级别：XPath Level<=3）
    // 生产中可同时订阅 System、Application，或改为 EvtSubscribeToFutureEvents
    subscription_ = EvtSubscribe(
        nullptr,                                        // 本机 session
        nullptr,                                        // 无信号 event，改用回调
        L"Security",                                    // 频道
        L"*[System[Level<=3 or (Level=0 and "
        L"(EventID=4624 or EventID=4625 or "           // 登录成功/失败
        L"EventID=4648 or EventID=4688))]]",            // 显式凭据/进程创建
        nullptr,                                        // 无书签（从头或仅未来）
        this,                                           // userContext → this
        (EVT_SUBSCRIBE_CALLBACK)win_event_callback,
        EvtSubscribeToFutureEvents                      // 仅订阅未来事件
    );

    if (!subscription_) {
        spdlog::error("[LogCollector] EvtSubscribe failed, error={}", GetLastError());
    } else {
        spdlog::info("[LogCollector] Windows Event Log subscription active (Security channel)");
    }
}

void SystemLogCollector::poll_windows_event_log() {
    // EvtSubscribe 使用异步回调，此函数只需保持线程存活
    std::this_thread::sleep_for(std::chrono::milliseconds(200));
}

// ═══════════════════════════════════════════════════════════════════════════════
// Linux 实现 — systemd SD-Journal API
// ═══════════════════════════════════════════════════════════════════════════════
#elif defined(PLATFORM_LINUX)

void SystemLogCollector::init_linux_journald() {
    int r = sd_journal_open(&journal_, SD_JOURNAL_LOCAL_ONLY);
    if (r < 0) {
        spdlog::error("[LogCollector] sd_journal_open failed: {}", strerror(-r));
        journal_ = nullptr;
        return;
    }

    // 移动到日志末尾，避免重放历史日志
    sd_journal_seek_tail(journal_);
    sd_journal_previous(journal_); // 跳过当前最后一条（下次 next 才读新事件）

    spdlog::info("[LogCollector] journald opened, waiting for new entries");
}

void SystemLogCollector::poll_journald() {
    if (!journal_) {
        std::this_thread::sleep_for(std::chrono::seconds(5));
        return;
    }

    // 等待新条目（最多 200ms），避免忙等
    int r = sd_journal_wait(journal_, 200 * 1000 /* μs */);
    if (r < 0) {
        spdlog::warn("[LogCollector] sd_journal_wait error: {}", strerror(-r));
        return;
    }
    if (r == SD_JOURNAL_NOP) return; // 超时，无新事件

    // 遍历所有新条目
    while (running_) {
        r = sd_journal_next(journal_);
        if (r == 0) break;   // 没有更多条目
        if (r < 0) {
            spdlog::warn("[LogCollector] sd_journal_next error: {}", strerror(-r));
            break;
        }

        // ── 读取字段 ────────────────────────────────────────────────────────
        const void* msg_data  = nullptr; size_t msg_len  = 0;
        const void* prio_data = nullptr; size_t prio_len = 0;
        const void* unit_data = nullptr; size_t unit_len = 0;

        sd_journal_get_data(journal_, "MESSAGE",          &msg_data,  &msg_len);
        sd_journal_get_data(journal_, "PRIORITY",         &prio_data, &prio_len);
        sd_journal_get_data(journal_, "SYSLOG_IDENTIFIER",&unit_data, &unit_len);

        // ── 解析 PRIORITY（格式: "PRIORITY=N"）──────────────────────────────
        // syslog 优先级: 0=EMERG 1=ALERT 2=CRIT 3=ERR 4=WARNING 5=NOTICE 6=INFO 7=DEBUG
        int priority = 6; // 默认 INFO
        if (prio_data && prio_len > 9) {
            priority = static_cast<const char*>(prio_data)[9] - '0';
        }

        terminal::v1::Severity sev;
        if      (priority <= 2) sev = terminal::v1::SEV_CRITICAL;
        else if (priority == 3) sev = terminal::v1::SEV_ERROR;
        else if (priority == 4) sev = terminal::v1::SEV_WARNING;
        else if (priority <= 6) sev = terminal::v1::SEV_INFO;
        else continue; // priority == 7 (DEBUG)，直接跳过

        // 按策略最低级别过滤
        if (policy_mgr_ && sev < policy_mgr_->get_log_level()) continue;

        // ── 提取消息文本（格式: "MESSAGE=actual text"，前缀长 8 字节）────────
        std::string message;
        if (msg_data && msg_len > 8) {
            message.assign(static_cast<const char*>(msg_data) + 8, msg_len - 8);
        }

        // ── 提取来源标识（格式: "SYSLOG_IDENTIFIER=sshd"，前缀长 18 字节）────
        std::string source = "journald";
        if (unit_data && unit_len > 18) {
            source.assign(static_cast<const char*>(unit_data) + 18, unit_len - 18);
        }

        // ── 构造 LogEntry 并上报 ─────────────────────────────────────────────
        terminal::v1::LogEntry entry;
        *entry.mutable_identity() = identity_;
        *entry.mutable_ts()       = google::protobuf::util::TimeUtil::GetCurrentTime();
        entry.set_severity(sev);
        entry.set_source(source);
        entry.set_message(message);

        if (callback_) callback_(std::move(entry));
    }
}

void SystemLogCollector::poll_auditd() {
    // TODO: 连接 auditd netlink socket 获取 USER_AUTH / SYSCALL 类型事件
    // 关注账号操作、特权命令等高安全价值事件
}

// ═══════════════════════════════════════════════════════════════════════════════
// macOS 实现 — Unified Logging（OSLogStore，可选）
// ═══════════════════════════════════════════════════════════════════════════════
#elif defined(PLATFORM_MACOS)

void SystemLogCollector::init_macos_unified_log() {
    // TODO: macOS 12+ 使用 OSLogStore API（需要 Objective-C 或 Swift 桥接）
    // 备选方案：fork 子进程执行 `log stream --predicate 'eventType==logEvent'`
    // 并通过 pipe 读取输出
    spdlog::info("[LogCollector] macOS Unified Log initialized (stub)");
}

void SystemLogCollector::poll_macos_unified_log() {
    // TODO: 解析 OSLogEntry，按优先级过滤后构造 LogEntry
}

#endif

} // namespace st
