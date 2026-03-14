package main

import (
	"context"
	"flag"
	"net"
	"os"
	"os/signal"
	"syscall"

	"safe_terminal/gateway/config"
	grpcserver "safe_terminal/gateway/internal/grpc"
	"safe_terminal/gateway/internal/kafka"
	"safe_terminal/gateway/internal/metrics"
	redisclient "safe_terminal/gateway/internal/redis"
	pb "safe_terminal/gateway/gen/pb"

	grpc_middleware "github.com/grpc-ecosystem/go-grpc-middleware/v2"
	grpc_zap "github.com/grpc-ecosystem/go-grpc-middleware/v2/interceptors/logging"
	grpc_recovery "github.com/grpc-ecosystem/go-grpc-middleware/v2/interceptors/recovery"
	"go.uber.org/zap"
	"google.golang.org/grpc"
	"google.golang.org/grpc/keepalive"
	"google.golang.org/grpc/reflection"
)

func main() {
	cfgPath := flag.String("config", "config/config.toml", "path to config file")
	flag.Parse()

	// ─── 日志 ──────────────────────────────────────────────────────────────
	log, _ := zap.NewProduction()
	defer log.Sync()

	// ─── 加载配置 ──────────────────────────────────────────────────────────
	cfg, err := config.Load(*cfgPath)
	if err != nil {
		log.Fatal("failed to load config", zap.Error(err))
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// ─── 初始化 Kafka Producer ─────────────────────────────────────────────
	kafkaProducer, err := kafka.NewBatchProducer(cfg.Kafka, log)
	if err != nil {
		log.Fatal("failed to create kafka producer", zap.Error(err))
	}
	defer kafkaProducer.Close()
	go kafkaProducer.RunFlushLoop(ctx)

	// ─── 初始化 Redis 客户端 ───────────────────────────────────────────────
	redisClient, err := redisclient.NewClient(cfg.Redis, log)
	if err != nil {
		log.Fatal("failed to connect to redis", zap.Error(err))
	}
	defer redisClient.Close()

	// ─── 初始化 gRPC 服务 ──────────────────────────────────────────────────
	gatewayServer := grpcserver.NewGatewayServer(*cfg, kafkaProducer, redisClient, log)

	// 启动策略订阅（Redis -> 推送给探针）
	go gatewayServer.StartPolicySubscriber(ctx)

	// gRPC 服务器选项
	grpcOpts := []grpc.ServerOption{
		grpc.KeepaliveParams(keepalive.ServerParameters{
			MaxConnectionIdle: 60,
			Time:              30,
			Timeout:           10,
		}),
		grpc.KeepaliveEnforcementPolicy(keepalive.EnforcementPolicy{
			MinTime:             5,
			PermitWithoutStream: true,
		}),
		grpc.MaxConcurrentStreams(uint32(cfg.Server.MaxConnections)),
		grpc.ChainUnaryInterceptor(
			grpc_middleware.ChainUnaryServer(
				grpc_recovery.UnaryServerInterceptor(),
			),
		),
		grpc.ChainStreamInterceptor(
			grpc_middleware.ChainStreamServer(
				grpc_recovery.StreamServerInterceptor(),
				grpc_zap.StreamServerInterceptor(InterceptorLogger(log)),
			),
		),
	}

	// mTLS：由 config.toml [tls] enabled 字段控制
	// 开发阶段 enabled=false，探针端使用 InsecureChannelCredentials 即可
	if cfg.TLS.Enabled {
		creds, err := grpcserver.BuildTLSCredentials(cfg.TLS)
		if err != nil {
			log.Fatal("failed to build TLS credentials", zap.Error(err))
		}
		grpcOpts = append(grpcOpts, grpc.Creds(creds))
		log.Info("mTLS enabled", zap.String("cert", cfg.TLS.CertFile))
	} else {
		log.Warn("TLS disabled — running in plaintext mode (dev only)")
	}

	grpcSrv := grpc.NewServer(grpcOpts...)
	pb.RegisterTerminalGatewayServiceServer(grpcSrv, gatewayServer)
	reflection.Register(grpcSrv) // 开发调试用，生产可关闭

	// ─── 监听端口 ──────────────────────────────────────────────────────────
	lis, err := net.Listen("tcp", cfg.Server.GRPCAddr)
	if err != nil {
		log.Fatal("failed to listen", zap.Error(err))
	}

	// ─── 启动 Prometheus metrics ───────────────────────────────────────────
	go metrics.StartMetricsServer(cfg.Metrics.Addr, log)

	// ─── 启动 gRPC 服务 ────────────────────────────────────────────────────
	log.Info("gateway gRPC server starting", zap.String("addr", cfg.Server.GRPCAddr))
	go func() {
		if err := grpcSrv.Serve(lis); err != nil {
			log.Error("grpc server error", zap.Error(err))
		}
	}()

	// ─── 优雅关机 ──────────────────────────────────────────────────────────
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit

	log.Info("shutting down gateway...")
	cancel()
	grpcSrv.GracefulStop()
	log.Info("gateway stopped")
}

// InterceptorLogger 适配 zap 给 grpc-middleware 使用
func InterceptorLogger(l *zap.Logger) grpc_zap.Logger {
	return grpc_zap.LoggerFunc(func(ctx context.Context, lvl grpc_zap.Level, msg string, fields ...any) {
		f := make([]zap.Field, 0, len(fields)/2)
		for i := 0; i+1 < len(fields); i += 2 {
			if key, ok := fields[i].(string); ok {
				f = append(f, zap.Any(key, fields[i+1]))
			}
		}
		switch lvl {
		case grpc_zap.LevelDebug:
			l.Debug(msg, f...)
		case grpc_zap.LevelInfo:
			l.Info(msg, f...)
		case grpc_zap.LevelWarn:
			l.Warn(msg, f...)
		default:
			l.Error(msg, f...)
		}
	})
}
