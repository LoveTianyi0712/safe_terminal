#pragma once
#include <string>
#include <cstdint>

namespace st {

struct GatewayConfig {
    std::string address;
    std::string ca_cert;
    std::string cert_file;
    std::string key_file;
    int32_t     reconnect_initial_ms{1000};
    int32_t     reconnect_max_ms{60000};
};

struct CollectionConfig {
    std::string log_level{"WARNING"};
    int32_t     heartbeat_interval_sec{30};
    bool        usb_monitor_enabled{true};
};

struct CacheConfig {
    int32_t     memory_capacity{1000};
    std::string disk_path;
    int32_t     disk_max_mb{200};
    int32_t     flush_interval_ms{500};
};

struct Config {
    std::string     terminal_id;
    std::string     agent_version{"1.0.0"};
    GatewayConfig   gateway;
    CollectionConfig collection;
    CacheConfig     cache;

    // 从 TOML 文件加载配置；抛出 std::runtime_error 表示格式错误
    static Config load(const std::string& path);

    // 持久化 terminal_id（首次生成后写回文件）
    void save_terminal_id(const std::string& path) const;
};

} // namespace st
