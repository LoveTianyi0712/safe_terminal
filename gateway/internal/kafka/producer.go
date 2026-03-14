package kafka

import (
	"context"
	"safe_terminal/gateway/config"
	"sync"
	"time"

	"github.com/IBM/sarama"
	"go.uber.org/zap"
	"google.golang.org/protobuf/proto"
)

// BatchProducer 将消息聚合成批次后写入 Kafka，减少网络开销
type BatchProducer struct {
	cfg      config.KafkaConfig
	producer sarama.AsyncProducer
	log      *zap.Logger

	mu      sync.Mutex
	batches map[string][]proto.Message // topic -> messages
	ticker  *time.Ticker
}

func NewBatchProducer(cfg config.KafkaConfig, log *zap.Logger) (*BatchProducer, error) {
	sc := sarama.NewConfig()
	sc.Version = sarama.V2_8_0_0
	sc.Producer.Return.Successes = false
	sc.Producer.Return.Errors = true
	sc.Producer.Compression = sarama.CompressionSnappy
	sc.Producer.Flush.MaxMessages = cfg.BatchSize
	sc.Producer.Flush.Frequency = cfg.BatchTimeout

	p, err := sarama.NewAsyncProducer(cfg.Brokers, sc)
	if err != nil {
		return nil, err
	}

	bp := &BatchProducer{
		cfg:      cfg,
		producer: p,
		log:      log,
		batches:  make(map[string][]proto.Message),
		ticker:   time.NewTicker(cfg.BatchTimeout),
	}

	go bp.errorLoop()
	return bp, nil
}

// Send 将 protobuf 消息放入指定 topic 的待发送批次
func (bp *BatchProducer) Send(topic string, key string, msg proto.Message) error {
	data, err := proto.Marshal(msg)
	if err != nil {
		return err
	}

	bp.producer.Input() <- &sarama.ProducerMessage{
		Topic: topic,
		Key:   sarama.StringEncoder(key),
		Value: sarama.ByteEncoder(data),
	}
	return nil
}

func (bp *BatchProducer) Close() error {
	bp.ticker.Stop()
	return bp.producer.Close()
}

func (bp *BatchProducer) errorLoop() {
	for err := range bp.producer.Errors() {
		bp.log.Error("kafka produce error",
			zap.String("topic", err.Msg.Topic),
			zap.Error(err.Err))
	}
}

// RunFlushLoop 定时刷写（由调用方在 goroutine 中启动）
func (bp *BatchProducer) RunFlushLoop(ctx context.Context) {
	for {
		select {
		case <-ctx.Done():
			return
		case <-bp.ticker.C:
			// sarama AsyncProducer 内部自动批量提交，此处可用于监控
		}
	}
}
