#include "Config.h"
#include <toml++/toml.hpp>
#include <fstream>
#include <stdexcept>
#include <sstream>
#include <vector>
#include <spdlog/spdlog.h>

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
    // 逐行读取配置文件，找到 terminal_id = "..." 行并替换值
    // 若文件中不存在该行则在 [agent] 节末追加
    std::ifstream in(path);
    if (!in.is_open()) {
        spdlog::warn("[Config] Cannot open config file for writing terminal_id: {}", path);
        return;
    }

    std::vector<std::string> lines;
    std::string line;
    bool found = false;

    while (std::getline(in, line)) {
        // 匹配形如 terminal_id = "..."（允许等号周围有空格）
        if (!found && line.find("terminal_id") != std::string::npos
                   && line.find('=') != std::string::npos) {
            lines.push_back("terminal_id = \"" + terminal_id + "\"");
            found = true;
        } else {
            lines.push_back(line);
        }
    }
    in.close();

    // 若配置文件中本来没有该字段，直接在文件末尾追加
    if (!found) {
        lines.push_back("terminal_id = \"" + terminal_id + "\"");
    }

    // 原子写入：先写临时文件再重命名，防止写入中途崩溃导致配置损坏
    std::string tmp_path = path + ".tmp";
    std::ofstream out(tmp_path);
    if (!out.is_open()) {
        spdlog::error("[Config] Cannot write to {}", tmp_path);
        return;
    }
    for (size_t i = 0; i < lines.size(); ++i) {
        out << lines[i];
        if (i + 1 < lines.size()) out << '\n';
    }
    out.close();

    // 重命名替换原文件
    if (std::rename(tmp_path.c_str(), path.c_str()) != 0) {
        spdlog::error("[Config] Failed to rename {} -> {}", tmp_path, path);
    } else {
        spdlog::info("[Config] Persisted terminal_id to {}", path);
    }
}

} // namespace st
