package config

import (
	"strings"
	"time"

	"github.com/spf13/viper"
)

type Config struct {
	Server  ServerConfig  `mapstructure:"server"`
	Kafka   KafkaConfig   `mapstructure:"kafka"`
	Redis   RedisConfig   `mapstructure:"redis"`
	TLS     TLSConfig     `mapstructure:"tls"`
	Metrics MetricsConfig `mapstructure:"metrics"`
}

type ServerConfig struct {
	GRPCAddr        string        `mapstructure:"grpc_addr"`
	MaxConnections  int           `mapstructure:"max_connections"`
	ReadTimeout     time.Duration `mapstructure:"read_timeout"`
	WriteTimeout    time.Duration `mapstructure:"write_timeout"`
}

type KafkaConfig struct {
	Brokers         []string      `mapstructure:"brokers"`
	TopicLogs       string        `mapstructure:"topic_logs"`
	TopicUSB        string        `mapstructure:"topic_usb"`
	TopicHeartbeats string        `mapstructure:"topic_heartbeats"`
	BatchSize       int           `mapstructure:"batch_size"`
	BatchTimeout    time.Duration `mapstructure:"batch_timeout"`
}

type RedisConfig struct {
	Addr            string        `mapstructure:"addr"`
	Password        string        `mapstructure:"password"`
	DB              int           `mapstructure:"db"`
	OnlineHashKey   string        `mapstructure:"online_hash_key"`
	PolicyChannel   string        `mapstructure:"policy_channel"`
	TTLSeconds      int           `mapstructure:"ttl_seconds"`
}

type TLSConfig struct {
	// Enabled 为 false 时网关以明文模式启动（仅限开发/测试环境）
	Enabled  bool   `mapstructure:"enabled"`
	CertFile string `mapstructure:"cert_file"`
	KeyFile  string `mapstructure:"key_file"`
	CAFile   string `mapstructure:"ca_file"`
}

type MetricsConfig struct {
	Addr string `mapstructure:"addr"`
}

func Load(path string) (*Config, error) {
	v := viper.New()
	v.SetConfigFile(path)
	v.SetEnvKeyReplacer(strings.NewReplacer(".", "_"))
	v.AutomaticEnv()

	if err := v.ReadInConfig(); err != nil {
		return nil, err
	}

	var cfg Config
	if err := v.Unmarshal(&cfg); err != nil {
		return nil, err
	}

	setDefaults(&cfg)
	return &cfg, nil
}

func setDefaults(c *Config) {
	if c.Server.GRPCAddr == "" {
		c.Server.GRPCAddr = ":50051"
	}
	if c.Server.MaxConnections == 0 {
		c.Server.MaxConnections = 10000
	}
	if c.Kafka.BatchSize == 0 {
		c.Kafka.BatchSize = 100
	}
	if c.Kafka.BatchTimeout == 0 {
		c.Kafka.BatchTimeout = 100 * time.Millisecond
	}
	if c.Redis.OnlineHashKey == "" {
		c.Redis.OnlineHashKey = "st:online:terminals"
	}
	if c.Redis.PolicyChannel == "" {
		c.Redis.PolicyChannel = "st:policy:updates"
	}
	if c.Redis.TTLSeconds == 0 {
		c.Redis.TTLSeconds = 90
	}
	if c.Metrics.Addr == "" {
		c.Metrics.Addr = ":9090"
	}
}
