#include "Config.h"
#include <toml++/toml.hpp>
#include <fstream>
#include <stdexcept>
#include <sstream>

namespace st {

Config Config::load(const std::string& path) {
    toml::table tbl;
    try {
        tbl = toml::parse_file(path);
    } catch (const toml::parse_error& e) {
        throw std::runtime_error("Config parse error: " + std::string(e.description()));
    }

    Config cfg;

    // [agent]
    cfg.terminal_id    = tbl["agent"]["terminal_id"].value_or(std::string{});
    cfg.agent_version  = tbl["agent"]["agent_version"].value_or(std::string{"1.0.0"});

    // [gateway]
    cfg.gateway.address              = tbl["gateway"]["address"].value_or(std::string{});
    cfg.gateway.ca_cert              = tbl["gateway"]["ca_cert"].value_or(std::string{});
    cfg.gateway.cert_file            = tbl["gateway"]["cert_file"].value_or(std::string{});
    cfg.gateway.key_file             = tbl["gateway"]["key_file"].value_or(std::string{});
    cfg.gateway.reconnect_initial_ms = tbl["gateway"]["reconnect_initial_ms"].value_or(1000);
    cfg.gateway.reconnect_max_ms     = tbl["gateway"]["reconnect_max_ms"].value_or(60000);

    // [collection]
    cfg.collection.log_level             = tbl["collection"]["log_level"].value_or(std::string{"WARNING"});
    cfg.collection.heartbeat_interval_sec = tbl["collection"]["heartbeat_interval_sec"].value_or(30);
    cfg.collection.usb_monitor_enabled   = tbl["collection"]["usb_monitor_enabled"].value_or(true);

    // [cache]
    cfg.cache.memory_capacity   = tbl["cache"]["memory_capacity"].value_or(1000);
    cfg.cache.disk_path         = tbl["cache"]["disk_path"].value_or(std::string{});
    cfg.cache.disk_max_mb       = tbl["cache"]["disk_max_mb"].value_or(200);
    cfg.cache.flush_interval_ms = tbl["cache"]["flush_interval_ms"].value_or(500);

    return cfg;
}

void Config::save_terminal_id(const std::string& path) const {
    // TODO: 使用 toml++ 更新文件中的 terminal_id 字段
    // 简单实现：逐行读取替换，生产中应使用完整 TOML 序列化
}

} // namespace st
