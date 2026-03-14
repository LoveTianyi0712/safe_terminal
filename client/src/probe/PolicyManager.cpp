#include "PolicyManager.h"
#include <algorithm>
#include <fstream>
#include <spdlog/spdlog.h>

namespace st {

PolicyManager::PolicyManager() {
    // 默认策略
    policy_.set_log_level(terminal::v1::SEV_WARNING);
    policy_.set_heartbeat_interval_sec(30);
    policy_.set_usb_block_enabled(false);
}

void PolicyManager::update(const terminal::v1::Policy& policy) {
    std::unique_lock lock(mtx_);
    policy_ = policy;
    spdlog::info("[PolicyManager] Policy updated: id={}", policy_.policy_id());
}

terminal::v1::Severity PolicyManager::get_log_level() const {
    std::shared_lock lock(mtx_);
    return policy_.log_level();
}

int32_t PolicyManager::get_heartbeat_interval_sec() const {
    std::shared_lock lock(mtx_);
    return policy_.heartbeat_interval_sec();
}

bool PolicyManager::is_usb_allowed(const std::string& device_id) const {
    std::shared_lock lock(mtx_);
    if (policy_.usb_whitelist_size() == 0) return true; // 白名单为空=全部允许
    return matches_list(device_id,
        {policy_.usb_whitelist().begin(), policy_.usb_whitelist().end()});
}

bool PolicyManager::is_usb_blocked(const std::string& device_id) const {
    std::shared_lock lock(mtx_);
    return matches_list(device_id,
        {policy_.usb_blacklist().begin(), policy_.usb_blacklist().end()});
}

bool PolicyManager::matches_list(const std::string& device_id,
                                  const std::vector<std::string>& list) const {
    for (const auto& prefix : list) {
        if (device_id.rfind(prefix, 0) == 0) return true;
    }
    return false;
}

void PolicyManager::save(const std::string& path) const {
    std::shared_lock lock(mtx_);
    std::ofstream f(path, std::ios::binary);
    policy_.SerializeToOstream(&f);
}

void PolicyManager::load(const std::string& path) {
    std::ifstream f(path, std::ios::binary);
    if (!f) return;
    std::unique_lock lock(mtx_);
    policy_.ParseFromIstream(&f);
    spdlog::info("[PolicyManager] Policy loaded from disk: id={}", policy_.policy_id());
}

} // namespace st
