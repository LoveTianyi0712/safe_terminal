#pragma once
#include "terminal.pb.h"
#include <shared_mutex>
#include <string>
#include <vector>

namespace st {

/**
 * 策略管理器：线程安全，持有最新策略副本
 * 探针各模块读取策略时不阻塞
 */
class PolicyManager {
public:
    PolicyManager();

    // 从服务端收到策略更新时调用（GrpcClient 回调）
    void update(const terminal::v1::Policy& policy);

    // 各模块线程安全读取
    terminal::v1::Severity  get_log_level() const;
    int32_t                 get_heartbeat_interval_sec() const;
    bool                    is_usb_allowed(const std::string& device_id) const;
    bool                    is_usb_blocked(const std::string& device_id) const;

    // 持久化到磁盘（重启后恢复最后一次策略）
    void save(const std::string& path) const;
    void load(const std::string& path);

private:
    bool matches_list(const std::string& device_id,
                      const std::vector<std::string>& list) const;

    mutable std::shared_mutex   mtx_;
    terminal::v1::Policy        policy_;
};

} // namespace st
