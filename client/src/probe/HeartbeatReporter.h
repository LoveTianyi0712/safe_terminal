#pragma once
#include "../transport/RingBuffer.h"
#include "../config/Config.h"
#include "terminal.pb.h"
#include <atomic>
#include <thread>

namespace st {

class HeartbeatReporter {
public:
    HeartbeatReporter(const CollectionConfig& cfg,
                      const terminal::v1::TerminalIdentity& identity,
                      RingBuffer& buffer);
    ~HeartbeatReporter();

    void start();
    void stop();

private:
    void report_loop();
    terminal::v1::Heartbeat collect_metrics() const;

    // 平台特定实现
    double get_cpu_percent() const;
    double get_mem_percent() const;
    double get_disk_percent() const;

    CollectionConfig                cfg_;
    terminal::v1::TerminalIdentity  identity_;
    RingBuffer&                     buffer_;
    std::atomic<bool>               running_{false};
    std::thread                     worker_;
};

} // namespace st
