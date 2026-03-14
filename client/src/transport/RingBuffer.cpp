#include "RingBuffer.h"
#include <filesystem>
#include <fstream>
#include <spdlog/spdlog.h>

namespace st {

namespace fs = std::filesystem;

RingBuffer::RingBuffer(int32_t mem_capacity, const std::string& disk_path, int32_t disk_max_mb)
    : mem_capacity_(mem_capacity), disk_path_(disk_path), disk_max_mb_(disk_max_mb) {
    if (!disk_path_.empty()) {
        fs::create_directories(disk_path_);
    }
}

RingBuffer::~RingBuffer() {
    flush_to_disk();
}

void RingBuffer::push(Entry entry) {
    std::unique_lock lock(mtx_);
    entry.sequence = next_seq_++;

    if (static_cast<int32_t>(mem_queue_.size()) >= mem_capacity_) {
        // 内存满：将最老的条目溢出到磁盘
        auto oldest = std::move(mem_queue_.front());
        mem_queue_.pop_front();
        write_to_wal(oldest);
    }

    mem_queue_.push_back(std::move(entry));
    cv_.notify_one();
}

bool RingBuffer::pop(Entry& out, int timeout_ms) {
    std::unique_lock lock(mtx_);
    if (!cv_.wait_for(lock,
            std::chrono::milliseconds(timeout_ms),
            [this] { return !mem_queue_.empty(); })) {
        return false;
    }
    out = mem_queue_.front();
    // 注意：暂不从队列移除，等待 ack() 确认
    return true;
}

void RingBuffer::ack(uint64_t sequence) {
    std::unique_lock lock(mtx_);
    if (!mem_queue_.empty() && mem_queue_.front().sequence == sequence) {
        mem_queue_.pop_front();
    }
}

void RingBuffer::flush_to_disk() {
    // TODO: 将 mem_queue_ 中未确认的消息持久化到 WAL 文件
    // 生产实现需处理文件大小限制和文件轮转
}

void RingBuffer::recover_from_disk() {
    // TODO: 启动时读取 WAL 文件，按序号重新填充 mem_queue_
    // 需要处理磁盘文件损坏、去重等场景
    spdlog::info("[RingBuffer] Recovering from disk cache at {}", disk_path_);
}

size_t RingBuffer::size() const {
    std::unique_lock lock(mtx_);
    return mem_queue_.size();
}

void RingBuffer::write_to_wal(const Entry& e) {
    // TODO: 追加写入 WAL 文件（格式：4字节长度 + 序列化字节）
}

} // namespace st
