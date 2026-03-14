#include "RingBuffer.h"
#include <filesystem>
#include <fstream>
#include <spdlog/spdlog.h>

namespace st {

namespace fs = std::filesystem;

// WAL 二进制格式（每条记录）：
//  [total_len : uint32]   = 4 + topic_len + 4 + data_len + 8
//  [topic_len : uint32]
//  [topic     : bytes  ]  (topic_len 字节)
//  [data_len  : uint32 ]
//  [data      : bytes  ]  (data_len 字节，Protobuf 序列化)
//  [sequence  : uint64 ]
static constexpr uint32_t WAL_HEADER_MAGIC = 0x53544346; // "STCF"
static const std::string  WAL_FILE_NAME    = "cache.wal";
static const std::string  WAL_TMP_NAME     = "cache.wal.tmp";

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
        // 内存满：将最老的条目溢出写到 WAL
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

size_t RingBuffer::size() const {
    std::unique_lock lock(mtx_);
    return mem_queue_.size();
}

// ─── WAL 写入（内部辅助，调用前需持有锁或单线程）────────────────────────────
void RingBuffer::write_to_wal(const Entry& e) {
    if (disk_path_.empty()) return;

    // 检查磁盘占用上限
    const std::string wal_path = disk_path_ + "/" + WAL_FILE_NAME;
    try {
        if (fs::exists(wal_path)) {
            auto file_size_mb = static_cast<int32_t>(
                fs::file_size(wal_path) / (1024 * 1024));
            if (file_size_mb >= disk_max_mb_) {
                spdlog::warn("[RingBuffer] WAL file exceeds {}MB limit, dropping oldest entry",
                             disk_max_mb_);
                return;
            }
        }
    } catch (...) { /* 忽略文件系统错误 */ }

    std::ofstream f(wal_path, std::ios::binary | std::ios::app);
    if (!f) {
        spdlog::error("[RingBuffer] Cannot open WAL file: {}", wal_path);
        return;
    }

    // 序列化 Entry
    auto topic_len = static_cast<uint32_t>(e.topic.size());
    auto data_len  = static_cast<uint32_t>(e.serialized.size());
    // total_len = 4(topic_len) + topic + 4(data_len) + data + 8(seq)
    uint32_t total_len = 4 + topic_len + 4 + data_len + 8;

    auto write_u32 = [&](uint32_t v) {
        f.write(reinterpret_cast<const char*>(&v), 4);
    };
    auto write_u64 = [&](uint64_t v) {
        f.write(reinterpret_cast<const char*>(&v), 8);
    };

    write_u32(total_len);
    write_u32(topic_len);
    f.write(e.topic.data(), topic_len);
    write_u32(data_len);
    f.write(e.serialized.data(), data_len);
    write_u64(e.sequence);
}

// ─── 刷写：将当前内存队列中所有未确认条目持久化到 WAL ────────────────────────
void RingBuffer::flush_to_disk() {
    if (disk_path_.empty()) return;

    std::unique_lock lock(mtx_);
    if (mem_queue_.empty()) return;

    const std::string wal_path = disk_path_ + "/" + WAL_FILE_NAME;
    std::ofstream f(wal_path, std::ios::binary | std::ios::app);
    if (!f) {
        spdlog::error("[RingBuffer] flush_to_disk: cannot open WAL {}", wal_path);
        return;
    }

    size_t flushed = 0;
    for (const auto& e : mem_queue_) {
        auto topic_len = static_cast<uint32_t>(e.topic.size());
        auto data_len  = static_cast<uint32_t>(e.serialized.size());
        uint32_t total_len = 4 + topic_len + 4 + data_len + 8;

        f.write(reinterpret_cast<const char*>(&total_len), 4);
        f.write(reinterpret_cast<const char*>(&topic_len), 4);
        f.write(e.topic.data(), topic_len);
        f.write(reinterpret_cast<const char*>(&data_len), 4);
        f.write(e.serialized.data(), data_len);
        f.write(reinterpret_cast<const char*>(&e.sequence), 8);
        ++flushed;
    }

    spdlog::info("[RingBuffer] Flushed {} entries to disk ({})", flushed, wal_path);
}

// ─── 恢复：启动时读取 WAL，按序填充内存队列，然后重命名原文件 ─────────────────
void RingBuffer::recover_from_disk() {
    if (disk_path_.empty()) return;

    const std::string wal_path = disk_path_ + "/" + WAL_FILE_NAME;
    if (!fs::exists(wal_path)) return;

    std::ifstream f(wal_path, std::ios::binary);
    if (!f) {
        spdlog::warn("[RingBuffer] Cannot open WAL for recovery: {}", wal_path);
        return;
    }

    std::unique_lock lock(mtx_);
    size_t recovered = 0;

    while (f) {
        // 读取 total_len
        uint32_t total_len = 0;
        f.read(reinterpret_cast<char*>(&total_len), 4);
        if (f.gcount() < 4 || total_len == 0 || total_len > 4 * 1024 * 1024) break;

        // 读取 topic
        uint32_t topic_len = 0;
        f.read(reinterpret_cast<char*>(&topic_len), 4);
        if (f.gcount() < 4 || topic_len > 256) break;

        std::string topic(topic_len, '\0');
        f.read(topic.data(), topic_len);
        if (static_cast<uint32_t>(f.gcount()) < topic_len) break;

        // 读取 data
        uint32_t data_len = 0;
        f.read(reinterpret_cast<char*>(&data_len), 4);
        if (f.gcount() < 4 || data_len > 4 * 1024 * 1024) break;

        std::string data(data_len, '\0');
        f.read(data.data(), data_len);
        if (static_cast<uint32_t>(f.gcount()) < data_len) break;

        // 读取 sequence
        uint64_t seq = 0;
        f.read(reinterpret_cast<char*>(&seq), 8);
        if (f.gcount() < 8) break;

        // 压入队列（如果内存未满）
        if (static_cast<int32_t>(mem_queue_.size()) < mem_capacity_) {
            mem_queue_.push_back({seq, std::move(topic), std::move(data)});
            // 确保 next_seq_ 不与恢复的序号冲突
            if (seq >= next_seq_) next_seq_ = seq + 1;
            ++recovered;
        } else {
            spdlog::warn("[RingBuffer] Memory full during recovery, {} entries dropped",
                         "remaining");
            break;
        }
    }

    f.close();

    if (recovered > 0) {
        spdlog::info("[RingBuffer] Recovered {} entries from {}", recovered, wal_path);
        // 重命名旧 WAL（不直接删除，保留一份 .bak 供问题排查）
        try {
            fs::rename(wal_path, wal_path + ".bak");
        } catch (...) {
            fs::remove(wal_path); // rename 失败则直接删除，避免下次重复加载
        }
    } else {
        // WAL 为空或全部损坏，直接删除
        fs::remove(wal_path);
        spdlog::info("[RingBuffer] WAL empty or corrupt, removed: {}", wal_path);
    }
}

} // namespace st
