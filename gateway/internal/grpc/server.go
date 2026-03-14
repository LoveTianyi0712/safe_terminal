package grpc

import (
	"context"
	"crypto/tls"
	"crypto/x509"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"strings"
	pb "safe_terminal/gateway/gen/pb"
	"safe_terminal/gateway/config"
	kafkaclient "safe_terminal/gateway/internal/kafka"
	redisclient "safe_terminal/gateway/internal/redis"
	"sync"
	"time"

	jwtv5 "github.com/golang-jwt/jwt/v5"
	"go.uber.org/zap"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/credentials"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/peer"
	"google.golang.org/grpc/status"
)

// connRegistry 维护所有活跃连接，用于策略推送
type connRegistry struct {
	mu    sync.RWMutex
	conns map[string]chan<- *pb.GatewayCommand // terminalID -> send channel
}

func (r *connRegistry) register(id string, ch chan<- *pb.GatewayCommand) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.conns[id] = ch
}

func (r *connRegistry) unregister(id string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	delete(r.conns, id)
}

// broadcast 向所有在线终端推送命令（策略变更时使用）
func (r *connRegistry) broadcast(cmd *pb.GatewayCommand) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	for _, ch := range r.conns {
		select {
		case ch <- cmd:
		default: // 连接背压，丢弃（下次重连时通过策略接口拉取）
		}
	}
}

// GatewayServer 实现 TerminalGatewayService gRPC 接口
type GatewayServer struct {
	pb.UnimplementedTerminalGatewayServiceServer
	cfg      config.Config
	kafka    *kafkaclient.BatchProducer
	redis    *redisclient.Client
	log      *zap.Logger
	registry connRegistry
}

func NewGatewayServer(
	cfg config.Config,
	kafka *kafkaclient.BatchProducer,
	redis *redisclient.Client,
	log *zap.Logger,
) *GatewayServer {
	s := &GatewayServer{
		cfg:   cfg,
		kafka: kafka,
		redis: redis,
		log:   log,
		registry: connRegistry{
			conns: make(map[string]chan<- *pb.GatewayCommand),
		},
	}
	return s
}

// Connect 处理单条双向流连接
func (s *GatewayServer) Connect(stream pb.TerminalGatewayService_ConnectServer) error {
	ctx := stream.Context()

	// 1. 身份验证：从 metadata 或 TLS 证书提取 terminal_id
	terminalID, err := s.authenticate(ctx)
	if err != nil {
		return status.Errorf(codes.Unauthenticated, "auth failed: %v", err)
	}

	// 2. 提取客户端 IP
	ipAddr := "unknown"
	if p, ok := peer.FromContext(ctx); ok {
		ipAddr = p.Addr.String()
	}

	s.log.Info("terminal connected",
		zap.String("terminal_id", terminalID),
		zap.String("ip", ipAddr))

	// 3. 注册到连接池，维护在线状态
	sendCh := make(chan *pb.GatewayCommand, 64)
	s.registry.register(terminalID, sendCh)
	if err := s.redis.SetOnline(ctx, terminalID, ipAddr); err != nil {
		s.log.Warn("redis set online failed", zap.Error(err))
	}

	defer func() {
		s.registry.unregister(terminalID)
		_ = s.redis.SetOffline(context.Background(), terminalID)
		s.log.Info("terminal disconnected", zap.String("terminal_id", terminalID))
	}()

	// 4. 发送协程：从 sendCh 读取并发送命令
	errCh := make(chan error, 1)
	go func() {
		for {
			select {
			case cmd, ok := <-sendCh:
				if !ok {
					return
				}
				if err := stream.Send(cmd); err != nil {
					errCh <- fmt.Errorf("send error: %w", err)
					return
				}
			case <-ctx.Done():
				return
			}
		}
	}()

	// 5. 接收循环：处理探针上报数据
	for {
		select {
		case err := <-errCh:
			return err
		default:
		}

		report, err := stream.Recv()
		if err == io.EOF {
			return nil
		}
		if err != nil {
			return status.Errorf(codes.Internal, "recv error: %v", err)
		}

		if err := s.handleReport(ctx, terminalID, report); err != nil {
			s.log.Warn("handle report error", zap.Error(err))
		}

		// 心跳时续期在线状态
		if report.GetHeartbeat() != nil {
			_ = s.redis.SetOnline(ctx, terminalID, ipAddr)
		}
	}
}

// handleReport 将上报数据路由到对应 Kafka Topic
func (s *GatewayServer) handleReport(ctx context.Context, terminalID string, report *pb.AgentReport) error {
	switch p := report.Payload.(type) {
	case *pb.AgentReport_Heartbeat:
		return s.kafka.Send(s.cfg.Kafka.TopicHeartbeats, terminalID, p.Heartbeat)

	case *pb.AgentReport_LogEntry:
		return s.kafka.Send(s.cfg.Kafka.TopicLogs, terminalID, p.LogEntry)

	case *pb.AgentReport_UsbEvent:
		return s.kafka.Send(s.cfg.Kafka.TopicUSB, terminalID, p.UsbEvent)

	case *pb.AgentReport_Ack:
		s.log.Debug("received ack", zap.Uint64("command_id", p.Ack.CommandId))
		return nil

	default:
		return fmt.Errorf("unknown payload type")
	}
}

// authenticate 验证探针身份，三段优先级：
//  1. mTLS 证书 CN（TLS.Enabled=true 且探针携带客户端证书）
//  2. JWT Bearer Token（JWT.Enabled=true，Header: authorization: Bearer <token>）
//  3. x-terminal-id 明文 Header（开发/测试模式回退）
func (s *GatewayServer) authenticate(ctx context.Context) (string, error) {
	// ── 优先级 1: mTLS 证书 CN ────────────────────────────────────────────────
	if s.cfg.TLS.Enabled {
		if p, ok := peer.FromContext(ctx); ok {
			if tlsInfo, ok := p.AuthInfo.(credentials.TLSInfo); ok {
				if len(tlsInfo.State.PeerCertificates) > 0 {
					cn := tlsInfo.State.PeerCertificates[0].Subject.CommonName
					if cn != "" {
						s.log.Debug("authenticated via mTLS cert CN", zap.String("terminal_id", cn))
						return cn, nil
					}
				}
			}
		}
	}

	md, ok := metadata.FromIncomingContext(ctx)
	if !ok {
		return "", fmt.Errorf("missing gRPC metadata")
	}

	// ── 优先级 2: JWT Bearer Token ────────────────────────────────────────────
	if s.cfg.JWT.Enabled {
		authValues := md.Get("authorization")
		if len(authValues) > 0 {
			raw := authValues[0]
			tokenStr := strings.TrimPrefix(raw, "Bearer ")
			if tokenStr == raw {
				// 格式不对，不是 Bearer 方案
				return "", fmt.Errorf("authorization header must use Bearer scheme")
			}
			terminalID, err := s.verifyJWT(tokenStr)
			if err != nil {
				return "", fmt.Errorf("JWT verification failed: %w", err)
			}
			s.log.Debug("authenticated via JWT", zap.String("terminal_id", terminalID))
			return terminalID, nil
		}
		// JWT 模式下必须携带 token
		return "", fmt.Errorf("JWT enabled but no authorization header provided")
	}

	// ── 优先级 3: x-terminal-id（开发模式回退）────────────────────────────────
	ids := md.Get("x-terminal-id")
	if len(ids) == 0 || ids[0] == "" {
		return "", fmt.Errorf("missing x-terminal-id header (dev mode)")
	}
	s.log.Debug("authenticated via x-terminal-id (dev mode)", zap.String("terminal_id", ids[0]))
	return ids[0], nil
}

// verifyJWT 验证 HMAC-SHA256 签名的终端 JWT，提取 terminal_id claim。
// 探针在向 Java 后端注册后，后端颁发包含 terminal_id 字段的 token。
func (s *GatewayServer) verifyJWT(tokenStr string) (string, error) {
	secret, err := base64.StdEncoding.DecodeString(s.cfg.JWT.Secret)
	if err != nil {
		return "", fmt.Errorf("decode JWT secret: %w", err)
	}

	token, err := jwtv5.Parse(tokenStr, func(t *jwtv5.Token) (interface{}, error) {
		if _, ok := t.Method.(*jwtv5.SigningMethodHMAC); !ok {
			return nil, fmt.Errorf("unexpected signing method: %v", t.Header["alg"])
		}
		return secret, nil
	}, jwtv5.WithExpirationRequired())

	if err != nil {
		return "", err
	}

	claims, ok := token.Claims.(jwtv5.MapClaims)
	if !ok || !token.Valid {
		return "", fmt.Errorf("invalid token claims")
	}

	// 优先使用 terminal_id claim，回退到 sub claim
	if tid, ok := claims["terminal_id"].(string); ok && tid != "" {
		return tid, nil
	}
	if sub, ok := claims["sub"].(string); ok && sub != "" {
		return sub, nil
	}
	return "", fmt.Errorf("token missing terminal_id or sub claim")
}

// StartPolicySubscriber 监听 Redis 策略变更并广播给所有连接的探针
func (s *GatewayServer) StartPolicySubscriber(ctx context.Context) {
	ch := s.redis.SubscribePolicy(ctx)
	s.log.Info("policy subscriber started")

	for {
		select {
		case payload, ok := <-ch:
			if !ok {
				return
			}
			s.log.Info("policy update received, broadcasting to all terminals")

			var policy pb.Policy
			if err := json.Unmarshal([]byte(payload), &policy); err != nil {
				s.log.Error("failed to unmarshal policy", zap.Error(err))
				continue
			}

			cmd := &pb.GatewayCommand{
				CommandId: uint64(time.Now().UnixMilli()),
				Payload:   &pb.GatewayCommand_Policy{Policy: &policy},
			}
			s.registry.broadcast(cmd)

		case <-ctx.Done():
			return
		}
	}
}

// BuildTLSCredentials 构建 mTLS 服务端凭证
//
// 需要三个文件：
//   - cfg.CertFile — 服务端证书（server.crt）
//   - cfg.KeyFile  — 服务端私钥（server.key）
//   - cfg.CAFile   — 用于验证客户端证书的 CA 证书（ca.crt）
//
// 开发阶段可跳过调用此函数，改用 InsecureCredentials（见 main.go 注释）。
func BuildTLSCredentials(cfg config.TLSConfig) (credentials.TransportCredentials, error) {
	cert, err := tls.LoadX509KeyPair(cfg.CertFile, cfg.KeyFile)
	if err != nil {
		return nil, fmt.Errorf("load server cert/key (%s / %s): %w", cfg.CertFile, cfg.KeyFile, err)
	}

	caData, err := os.ReadFile(cfg.CAFile)
	if err != nil {
		return nil, fmt.Errorf("read CA cert (%s): %w", cfg.CAFile, err)
	}
	caPool := x509.NewCertPool()
	if !caPool.AppendCertsFromPEM(caData) {
		return nil, fmt.Errorf("failed to parse CA cert: %s", cfg.CAFile)
	}

	tlsCfg := &tls.Config{
		Certificates: []tls.Certificate{cert},
		ClientCAs:    caPool,
		// mTLS：要求并验证客户端证书（探针端需携带 agent.crt）
		ClientAuth: tls.RequireAndVerifyClientCert,
		MinVersion: tls.VersionTLS12,
	}
	return credentials.NewTLS(tlsCfg), nil
}
