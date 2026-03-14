#pragma once
#include <deque>
#include <mutex>
#include <condition_variable>
#include <functional>
#include <string>
#include <cstdint>

namespace st {

/**
 * 线程安全环形缓冲区：内存层 + 磁盘溢出层
 *
 * 内存满时将最老的消息序列化到磁盘文件（WAL 格式）。
 * 网络恢复后调用 drain() 按序取出消息发送。
 */
class RingBuffer {
public:
    struct Entry {
        uint64_t    sequence;
        std::string topic;       // "logs" | "usb" | "heartbeat"
        std::string serialized;  // Protobuf 序列化字节
    };

    RingBuffer(int32_t mem_capacity, const std::string& disk_path, int32_t disk_max_mb);
    ~RingBuffer();

    // 生产者：探针采集线程调用
    void push(Entry entry);

    // 消费者：gRPC发送线程调用，阻塞直到有数据或超时
    bool pop(Entry& out, int timeout_ms = 1000);

    // 发送成功后确认，从缓冲区移除
    void ack(uint64_t sequence);

    // 磁盘刷写（定时调用）
    void flush_to_disk();

    // 从磁盘恢复（启动时调用）
    void recover_from_disk();

    size_t size() const;

private:
    int32_t             mem_capacity_;
    std::string         disk_path_;
    int32_t             disk_max_mb_;
    std::deque<Entry>   mem_queue_;
    mutable std::mutex  mtx_;
    std::condition_variable cv_;
    uint64_t            next_seq_{1};

    void write_to_wal(const Entry& e);
};

} // namespace st
