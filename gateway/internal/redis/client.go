package redis

import (
	"context"
	"fmt"
	"safe_terminal/gateway/config"
	"time"

	"github.com/redis/go-redis/v9"
	"go.uber.org/zap"
)

type Client struct {
	rdb *redis.Client
	cfg config.RedisConfig
	log *zap.Logger
}

func NewClient(cfg config.RedisConfig, log *zap.Logger) (*Client, error) {
	rdb := redis.NewClient(&redis.Options{
		Addr:     cfg.Addr,
		Password: cfg.Password,
		DB:       cfg.DB,
	})

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if err := rdb.Ping(ctx).Err(); err != nil {
		return nil, fmt.Errorf("redis ping failed: %w", err)
	}

	return &Client{rdb: rdb, cfg: cfg, log: log}, nil
}

// SetOnline 将终端标记为在线，并设置 TTL（心跳续期）
func (c *Client) SetOnline(ctx context.Context, terminalID, ipAddr string) error {
	pipe := c.rdb.Pipeline()
	pipe.HSet(ctx, c.cfg.OnlineHashKey, terminalID, ipAddr)
	pipe.Expire(ctx, onlineKey(terminalID), ttl(c.cfg.TTLSeconds))
	_, err := pipe.Exec(ctx)
	return err
}

// SetOffline 将终端标记为离线
func (c *Client) SetOffline(ctx context.Context, terminalID string) error {
	return c.rdb.HDel(ctx, c.cfg.OnlineHashKey, terminalID).Err()
}

// SubscribePolicy 订阅策略变更频道，通过 channel 返回原始 JSON/Proto bytes
func (c *Client) SubscribePolicy(ctx context.Context) <-chan string {
	sub := c.rdb.Subscribe(ctx, c.cfg.PolicyChannel)
	ch := make(chan string, 256)

	go func() {
		defer close(ch)
		msgs := sub.Channel()
		for msg := range msgs {
			select {
			case ch <- msg.Payload:
			case <-ctx.Done():
				return
			}
		}
	}()

	return ch
}

// PublishPolicy 发布策略变更（由核心后端调用，此处供测试用）
func (c *Client) PublishPolicy(ctx context.Context, payload string) error {
	return c.rdb.Publish(ctx, c.cfg.PolicyChannel, payload).Err()
}

func (c *Client) Close() error {
	return c.rdb.Close()
}

func onlineKey(id string) string { return "st:online:" + id }
func ttl(sec int) time.Duration  { return time.Duration(sec) * time.Second }
