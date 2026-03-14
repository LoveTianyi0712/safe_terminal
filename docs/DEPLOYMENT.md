# 部署说明

> 终端安全监控系统 · 本地开发环境 & 生产环境部署指南  
> **当前版本**：P0 ~ P3 全部已实现，系统功能完整可运行

---

## 目录

- [项目当前状态](#项目当前状态)
- [环境要求](#环境要求)
- [本地开发环境（推荐先跑通）](#本地开发环境推荐先跑通)
- [各模块详细部署](#各模块详细部署)
  - [1. 基础设施（Docker Compose）](#1-基础设施)
  - [2. Go 接入网关](#2-go-接入网关)
  - [3. Java 核心后端](#3-java-核心后端)
  - [4. Vue 管理前端](#4-vue-管理前端)
  - [5. C++ 客户端探针](#5-c-客户端探针)
- [API 接口速查](#api-接口速查)
- [生产环境注意事项](#生产环境注意事项)
- [服务端口速查](#服务端口速查)
- [常见问题排查](#常见问题排查)

---

## 项目当前状态

| 模块 | 状态 | 说明 |
|------|------|------|
| Java 后端 | ✅ 完整可运行 | JWT 登录、ES 动态索引、Kafka 消费、规则引擎、告警、统计 API、InfluxDB 时序查询均已实现 |
| Go 网关 | ✅ 完整可运行 | mTLS/JWT/x-terminal-id 三段认证、Kafka 批量写入、Redis 策略推送、Prometheus 指标均已实现 |
| Vue 前端 | ✅ 完整可运行 | 登录（含 Token 自动刷新）、仪表盘（真实数据）、终端管理（在线状态）、事件检索（日志+USB双Tab）、告警中心均已实现 |
| C++ 探针 | ✅ 核心功能完整 | Linux journald 日志、Linux libudev USB 监控、Windows EvtSubscribe 订阅、磁盘 WAL 缓存、心跳（CPU/内存/磁盘）、terminal_id 持久化均已实现 |
| C++ 探针 | ⚠️ 部分平台待补全 | macOS IOKit USB 采集、macOS Unified Log 采集、Windows USB 热插拔枚举、Linux auditd 采集为存根，不影响主平台（Linux/Windows）运行 |

---

## 环境要求

### 开发机（运行全部服务）

| 工具 | 版本要求 | 说明 |
|------|----------|------|
| Docker Desktop | ≥ 24.x | 运行基础设施 |
| Go | ≥ 1.22 | 编译网关（需 `golang-jwt/jwt/v5` 依赖） |
| JDK | 21 (LTS) | 编译/运行后端 |
| Maven | ≥ 3.9（或用 mvnw） | Java 构建工具 |
| Node.js | ≥ 20 LTS | 前端开发 |
| protoc | ≥ 25.x | 生成 Protobuf 代码 |
| CMake | ≥ 3.20 | 编译 C++ 探针 |
| vcpkg | 最新 | C++ 依赖管理 |

### 探针目标机（运行 C++ 探针）

| 平台 | 最低版本 | 完整度 |
|------|----------|--------|
| Windows | Windows 10 1909 / Windows 11 | ✅ 日志采集 + 心跳（USB 热插拔待补） |
| 国产 Linux | 统信 UOS 20 / 麒麟 V10 | ✅ 完整（journald + libudev + 心跳）|
| macOS（可选） | macOS 12 Monterey | ⚠️ 仅心跳，日志/USB 采集为存根 |

---

## 本地开发环境（推荐先跑通）

> 建议按以下顺序启动，每步验证无误再进行下一步。

```
第1步  Docker 基础设施  →  第2步  Go 网关  →  第3步  Java 后端  →  第4步  Vue 前端
                                                       （探针可最后单独测试）
```

---

## 各模块详细部署

### 1. 基础设施

**启动所有存储和消息中间件：**

```bash
# 在项目根目录执行
docker-compose up -d
```

**等待健康检查通过（约 60 秒）：**

```bash
docker-compose ps
# 确认所有服务 Status 显示 healthy 或 running
```

**验证各服务可达：**

```bash
# Kafka
docker exec st_kafka kafka-topics --bootstrap-server localhost:9092 --list

# Redis
docker exec st_redis redis-cli -a "safeTerminal@2025" ping
# 预期输出: PONG

# Elasticsearch
curl http://localhost:9200/_cluster/health?pretty
# 预期 status: green 或 yellow

# InfluxDB
curl http://localhost:8086/health
# 预期: {"status":"pass"}

# MySQL
docker exec st_mysql mysqladmin ping -u root -psafeTerminal@2025
# 预期: mysqld is alive
```

**可选：启动 Kafka UI（消息调试利器）：**

```bash
# 在 docker-compose.yml 中添加后执行
docker-compose --profile tools up -d kafka-ui
# 访问: http://localhost:8080（映射到容器 8080 端口）
```

Kafka UI compose 片段（追加到 `docker-compose.yml` services 节）：
```yaml
  kafka-ui:
    image: provectuslabs/kafka-ui:latest
    profiles: [tools]
    ports:
      - "18080:8080"
    environment:
      KAFKA_CLUSTERS_0_NAME: local
      KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS: kafka:29092
    depends_on:
      - kafka
```

**可选：启动 Kibana（ES 可视化工具）：**

```bash
docker-compose --profile tools up -d kibana
# 访问: http://localhost:5601
```

**停止基础设施：**

```bash
docker-compose down        # 保留数据卷
docker-compose down -v     # 同时清除数据卷（重置数据）
```

---

### 2. Go 接入网关

#### 前置：安装 protoc 插件

```bash
go install google.golang.org/protobuf/cmd/protoc-gen-go@latest
go install google.golang.org/grpc/cmd/protoc-gen-go-grpc@latest
export PATH="$PATH:$(go env GOPATH)/bin"
```

#### 生成 Protobuf Go 代码

```bash
cd gateway
mkdir -p gen/pb

protoc \
  --go_out=gen/pb \
  --go_opt=paths=source_relative \
  --go-grpc_out=gen/pb \
  --go-grpc_opt=paths=source_relative \
  -I ../proto \
  ../proto/terminal.proto
```

#### 下载依赖并运行

```bash
cd gateway
# 注意：go.mod 中新增了 golang-jwt/jwt/v5，需先同步依赖
go mod tidy
go run main.go -config config/config.toml
```

**开发模式关键配置（`config/config.toml`）：**

```toml
[tls]
enabled = false   # 开发阶段关闭 TLS，探针使用 InsecureChannelCredentials

[jwt]
enabled = false   # 开发阶段关闭 JWT 验证，探针只传 x-terminal-id header
secret  = "c2FmZS10ZXJtaW5hbC1qd3Qtc2VjcmV0LWtleS0yMDI1IQ=="  # 与 application.yml jwt.secret 保持一致
```

**验证网关启动：**

```bash
# gRPC 端口监听
lsof -i :50051        # Linux/macOS
netstat -ano | findstr 50051  # Windows

# Prometheus 指标
curl http://localhost:9090/metrics | grep grpc_server
```

#### 构建二进制（生产用）

```bash
cd gateway
GOOS=linux GOARCH=amd64 go build -o dist/st_gateway main.go
# Windows 交叉编译
GOOS=windows GOARCH=amd64 go build -o dist/st_gateway.exe main.go
```

---

### 3. Java 核心后端

#### 方式一：Maven 直接运行（开发）

```bash
cd backend
./mvnw spring-boot:run
# Windows:
mvnw.cmd spring-boot:run
```

#### 方式二：打包后运行（生产）

```bash
cd backend
./mvnw clean package -DskipTests
java -jar target/safe-terminal-backend-1.0.0-SNAPSHOT.jar
```

**验证后端启动：**

```bash
# 健康检查
curl http://localhost:8080/api/actuator/health

# 登录获取 Token
curl -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin@123456"}'
# 预期返回: {"token":"eyJ...","refreshToken":"...","username":"admin","role":"ADMIN"}
```

#### 配置文件说明

`backend/src/main/resources/application.yml` 中需确认以下配置与 Docker 环境一致：

```yaml
spring:
  datasource:
    url: jdbc:mysql://localhost:3306/safe_terminal
  elasticsearch:
    uris: http://localhost:9200
  data:
    redis:
      host: localhost
      password: safeTerminal@2025
  kafka:
    bootstrap-servers: localhost:9092

influxdb:
  url: http://localhost:8086
  token: safe-terminal-influx-token
  bucket: metrics
  org: safe-terminal

jwt:
  secret: "c2FmZS10ZXJtaW5hbC1qd3Qtc2VjcmV0LWtleS0yMDI1IQ=="  # 生产须替换！
  expiration-ms: 86400000      # 24 小时
  refresh-expiration-ms: 604800000  # 7 天
```

> **⚠️ 生产环境** 务必替换 `jwt.secret`：  
> `openssl rand -base64 32`  
> 同时同步到 `gateway/config/config.toml [jwt] secret`。

#### 数据库初始化

MySQL 初始化脚本会在 Docker 首次启动时自动执行（`db/init.sql`），如需手动执行：

```bash
docker exec -i st_mysql mysql -u root -psafeTerminal@2025 safe_terminal \
  < backend/src/main/resources/db/init.sql
```

---

### 4. Vue 管理前端

```bash
cd frontend
npm install
npm run dev
```

访问 **http://localhost:3000**

默认账号（`db/init.sql` 预设）：
- 用户名：`admin`
- 密码：`Admin@123456`

**登录功能已完整实现：**
- JWT Token 持久化到 `localStorage`，页面刷新后自动恢复登录状态
- Token 过期前 5 分钟自动续期（每 4 分钟检查一次）
- 路由守卫支持 Token 过期检测（不只是判断是否存在）
- 角色权限通过 `userStore.isAdmin` / `isOperator` 控制

#### 构建生产包

```bash
cd frontend
npm run build
# 产物在 dist/ 目录，部署到 Nginx 即可
```

**Nginx 配置参考：**

```nginx
server {
    listen 80;
    root /var/www/safe-terminal/dist;
    index index.html;

    # Vue Router history 模式
    location / {
        try_files $uri $uri/ /index.html;
    }

    # 反向代理后端 API
    location /api {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }

    # WebSocket（实时告警推送）
    location /ws {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 3600s;
    }
}
```

---

### 5. C++ 客户端探针

#### 探针配置文件 `config.toml`

```toml
[agent]
terminal_id  = ""           # 留空，首次启动自动生成 UUID 并写回此文件
agent_version = "1.0.0"

[gateway]
address              = "127.0.0.1:50051"  # 网关地址（开发: localhost，生产: 网关真实 IP）
reconnect_initial_ms = 1000
reconnect_max_ms     = 60000
# TLS 开发模式下证书留空（对应 gateway config.toml tls.enabled=false）
ca_cert   = ""
cert_file = ""
key_file  = ""

[collection]
log_level             = "WARNING"   # 最低采集日志级别
heartbeat_interval_sec = 30
usb_monitor_enabled   = true

[cache]
memory_capacity   = 1000
disk_path         = "/var/lib/safe_terminal/cache"  # Windows: C:/ProgramData/SafeTerminal/cache
disk_max_mb       = 200
flush_interval_ms = 500
```

> **terminal_id 持久化**：首次运行时探针自动生成 UUID 并写回 `config.toml`，重启后复用同一 ID，确保终端身份不变。

#### Windows 环境

**安装依赖（vcpkg）：**

```powershell
git clone https://github.com/microsoft/vcpkg.git C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat

C:\vcpkg\vcpkg install grpc:x64-windows protobuf:x64-windows spdlog:x64-windows tomlplusplus:x64-windows
```

**生成 Protobuf C++ 代码：**

```powershell
cd client
mkdir -p gen
protoc -I ../proto --cpp_out=gen --grpc_out=gen `
  --plugin=protoc-gen-grpc="C:\vcpkg\installed\x64-windows\tools\grpc\grpc_cpp_plugin.exe" `
  ../proto/terminal.proto
```

**编译：**

```powershell
cmake -B build `
  -DCMAKE_TOOLCHAIN_FILE=C:/vcpkg/scripts/buildsystems/vcpkg.cmake `
  -DVCPKG_TARGET_TRIPLET=x64-windows `
  -DPLATFORM_WINDOWS=ON
cmake --build build --config Release
```

**运行：**

```powershell
# 创建数据目录
New-Item -ItemType Directory -Force "C:\ProgramData\SafeTerminal\cache"
New-Item -ItemType Directory -Force "C:\ProgramData\SafeTerminal\logs"

.\build\Release\st_agent.exe config.toml
```

**安装为 Windows 服务（生产）：**

```powershell
# 使用 NSSM（Non-Sucking Service Manager）
nssm install SafeTerminalAgent "C:\Program Files\SafeTerminal\st_agent.exe" `
  "C:\ProgramData\SafeTerminal\config.toml"
nssm set SafeTerminalAgent AppStdout "C:\ProgramData\SafeTerminal\logs\agent.log"
nssm start SafeTerminalAgent
```

#### Linux（麒麟/统信）

**安装系统依赖：**

```bash
# 麒麟 V10 / 统信 UOS（基于 Debian/Ubuntu）
apt-get install -y \
    libudev-dev \
    libsystemd-dev \
    cmake \
    build-essential \
    pkg-config

# vcpkg
git clone https://github.com/microsoft/vcpkg.git ~/vcpkg
~/vcpkg/bootstrap-vcpkg.sh
~/vcpkg/vcpkg install grpc protobuf spdlog tomlplusplus
```

**生成 Protobuf C++ 代码：**

```bash
cd client
mkdir -p gen
protoc -I ../proto \
  --cpp_out=gen \
  --grpc_out=gen \
  --plugin=protoc-gen-grpc=$(which grpc_cpp_plugin) \
  ../proto/terminal.proto
```

**编译：**

```bash
cd client
cmake -B build \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_TOOLCHAIN_FILE=~/vcpkg/scripts/buildsystems/vcpkg.cmake \
    -DPLATFORM_LINUX=ON
cmake --build build -j$(nproc)
```

**运行：**

```bash
sudo mkdir -p /etc/safe_terminal /var/log/safe_terminal /var/lib/safe_terminal/cache
sudo cp config.toml /etc/safe_terminal/config.toml
# 修改 gateway.address 指向网关 IP
sudo nano /etc/safe_terminal/config.toml

# 启动（开发阶段，TLS 关闭）
sudo ./build/st_agent /etc/safe_terminal/config.toml
```

**安装为 systemd 服务（生产）：**

```ini
# /etc/systemd/system/st-agent.service
[Unit]
Description=Safe Terminal Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=/usr/local/bin/st_agent /etc/safe_terminal/config.toml
Restart=on-failure
RestartSec=10
User=root
StandardOutput=append:/var/log/safe_terminal/agent.log
StandardError=append:/var/log/safe_terminal/agent.log

[Install]
WantedBy=multi-user.target
```

```bash
sudo cp build/st_agent /usr/local/bin/st_agent
sudo systemctl daemon-reload
sudo systemctl enable --now st-agent
sudo systemctl status st-agent

# 查看实时日志
sudo journalctl -u st-agent -f
```

---

## API 接口速查

> 所有接口需携带 `Authorization: Bearer <token>` 请求头（`/auth/**` 除外）。

### 认证

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/auth/login` | 用户名/密码登录，返回 token + refreshToken |
| POST | `/api/auth/refresh` | 用 refreshToken 换新 accessToken |
| GET  | `/api/auth/me` | 获取当前登录用户信息 |

### 终端管理

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/v1/terminals` | 分页列表，支持 keyword/page/size |
| GET  | `/api/v1/terminals/{id}` | 终端详情 |
| GET  | `/api/v1/terminals/{id}/online` | 单个在线状态 |
| GET  | `/api/v1/terminals/online-status?ids=id1,id2` | 批量在线状态 |
| GET  | `/api/v1/terminals/stats/online-count` | 在线终端总数 |
| PATCH | `/api/v1/terminals/{id}` | 更新部门/责任人 |

### 事件检索

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/v1/events/logs` | 日志检索，支持 terminalId/keyword/startDate/endDate/page/size |
| GET  | `/api/v1/events/usb`  | USB 事件检索，支持 terminalId/deviceId/startDate/endDate/page/size |

### 统计

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/v1/stats/event-trend?days=7` | 近 N 天事件趋势（日志/USB/告警计数）|
| GET  | `/api/v1/stats/alert-distribution` | 告警类型分布（饼图数据）|

### 时序指标（InfluxDB）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/v1/metrics/usb-trend?terminalId=xxx&hours=24` | USB 接入趋势（按小时分桶）|
| GET  | `/api/v1/metrics/heartbeat?terminalId=xxx&minutes=60` | 心跳指标（CPU/内存/磁盘）|

### 告警管理

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/v1/alerts` | 告警列表，支持 status/terminalId/page/size |
| GET  | `/api/v1/alerts/stats` | 告警统计（未处理数/今日数）|
| PATCH | `/api/v1/alerts/{id}/confirm` | 确认告警 |
| PATCH | `/api/v1/alerts/{id}/resolve` | 处理告警（body: `{"comment":"..."}` 可选）|

### 策略配置

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/v1/policies` | 策略列表 |
| POST | `/api/v1/policies` | 创建策略（变更后自动发布到 Redis → 网关 → 探针）|
| PUT  | `/api/v1/policies/{id}` | 更新策略 |
| DELETE | `/api/v1/policies/{id}` | 删除策略 |

---

## 生产环境注意事项

### TLS 证书生成（mTLS，自签名）

```bash
mkdir -p certs && cd certs

# CA
openssl genrsa -out ca.key 4096
openssl req -new -x509 -days 3650 -key ca.key -out ca.crt \
    -subj "/CN=SafeTerminal-CA"

# 网关服务端证书（CN 要与客户端连接的域名/IP 匹配）
openssl genrsa -out server.key 2048
openssl req -new -key server.key -out server.csr \
    -subj "/CN=gateway.safe-terminal.internal"
openssl x509 -req -days 365 -in server.csr -CA ca.crt -CAkey ca.key \
    -CAcreateserial -out server.crt

# 探针客户端证书（CN 作为 terminal_id 使用）
openssl genrsa -out agent.key 2048
openssl req -new -key agent.key -out agent.csr -subj "/CN=agent-001"
openssl x509 -req -days 365 -in agent.csr -CA ca.crt -CAkey ca.key \
    -CAcreateserial -out agent.crt
```

**部署位置：**
- 网关：`ca.crt`、`server.crt`、`server.key` → `/etc/safe_terminal/certs/`
- 探针：`ca.crt`、`agent.crt`、`agent.key` → `/etc/safe_terminal/certs/`

**启用 TLS：**
```toml
# gateway/config/config.toml
[tls]
enabled   = true
cert_file = "/etc/safe_terminal/certs/server.crt"
key_file  = "/etc/safe_terminal/certs/server.key"
ca_file   = "/etc/safe_terminal/certs/ca.crt"
```

### JWT 密钥对齐（网关 ↔ 后端共享密钥）

生产环境中，网关和 Java 后端使用同一个 HMAC-SHA256 密钥验证终端 JWT Token：

```bash
# 生成随机密钥
openssl rand -base64 32
# 示例输出: yK3mP9xQ7nR2wE8vT4sA6cF1jH5bN0uL+gD/kI...
```

同步配置：
```yaml
# backend/src/main/resources/application.yml
jwt:
  secret: "yK3mP9xQ7nR2wE8vT4sA6cF1jH5bN0uL+gD/kI..."
```
```toml
# gateway/config/config.toml
[jwt]
enabled = true
secret  = "yK3mP9xQ7nR2wE8vT4sA6cF1jH5bN0uL+gD/kI..."
```

### 修改默认密码

部署前必须修改以下默认密码：

| 服务 | 默认密码 | 配置位置 |
|------|----------|----------|
| MySQL root | `safeTerminal@2025` | `docker-compose.yml` |
| Redis | `safeTerminal@2025` | `docker-compose.yml` + `application.yml` |
| InfluxDB | `safeTerminal@2025` | `docker-compose.yml` |
| 后台 admin | `Admin@123456` | `db/init.sql`（BCrypt 哈希）|
| JWT 密钥 | 见 yml | `application.yml` + `gateway/config.toml` |

### Kafka 生产配置

```yaml
# application.yml
spring:
  kafka:
    producer:
      acks: all
      retries: 3
    consumer:
      enable-auto-commit: false   # 手动提交 offset，防止消息丢失
```

### 高可用部署拓扑（3 万终端场景）

```
                ┌─────────────────────────────┐
                │      负载均衡（LVS/HAProxy）  │
                └──────────┬──────────────────┘
                           │ :50051
          ┌────────────────┼────────────────┐
          ↓                ↓                ↓
    Gateway-1          Gateway-2        Gateway-3
    (1万连接)          (1万连接)         (1万连接)
          └──────────────┬─────────────────┘
                         ↓
                   Kafka Cluster
                         ↓
            ┌────────────┴────────────┐
            ↓                        ↓
      Backend-1                 Backend-2
      (Kafka Consumer)          (Kafka Consumer)
            └────────────────────────┘
                         ↓
            MySQL / ES / InfluxDB / Redis
```

---

## 服务端口速查

| 服务 | 端口 | 协议 | 说明 |
|------|------|------|------|
| Go 网关 gRPC | 50051 | gRPC（含 TLS） | 探针接入 |
| Go 网关 Metrics | 9090 | HTTP | Prometheus 指标抓取 |
| Java 后端 | 8080 | HTTP | REST API + WebSocket |
| Vue 前端（开发） | 3000 | HTTP | Vite 开发服务器 |
| Kafka | 9092 | TCP | 消息队列 |
| Elasticsearch | 9200 | HTTP | 日志/事件存储 |
| InfluxDB | 8086 | HTTP | 时序指标存储 |
| Redis | 6379 | TCP | 缓存/在线状态/策略发布 |
| MySQL | 3306 | TCP | 元数据/告警/策略 |
| Kibana（可选） | 5601 | HTTP | ES 数据可视化 |
| Kafka UI（可选） | 18080 | HTTP | Kafka 消息调试 |

---

## 常见问题排查

**问题：Go 网关编译失败 `cannot find module golang-jwt/jwt/v5`**
```bash
cd gateway
go mod tidy        # 自动下载缺失依赖
go mod download    # 如 tidy 失败，尝试手动下载
```

**问题：Kafka 连接失败**
```bash
docker logs st_kafka | tail -20
docker exec st_kafka kafka-topics --bootstrap-server localhost:9092 --list
# 确认 topic 存在：raw-logs / usb-events / heartbeats
```

**问题：Elasticsearch 启动后 status=red**
```bash
docker logs st_elasticsearch | tail -30
# 内存不足时调低 JVM 堆
# 修改 docker-compose.yml: ES_JAVA_OPTS=-Xms512m -Xmx512m
```

**问题：Java 后端启动报 "Table doesn't exist"**
```bash
docker exec -i st_mysql mysql -u root -psafeTerminal@2025 safe_terminal \
  < backend/src/main/resources/db/init.sql
# 确认 application.yml ddl-auto: update（开发环境）
```

**问题：Java 后端报 `EsIndexNameResolver` bean not found**
```bash
# 确认 EsIndexNameResolver.java 在 com.safeterminal.config 包下，
# 类上有 @Component("indexNameResolver") 注解
```

**问题：前端代理 /api 报 502**
```bash
curl http://localhost:8080/api/actuator/health
# 确认后端已在 8080 端口监听，检查 vite.config.ts 代理配置
```

**问题：探针连接网关失败（TLS disabled 模式）**
```bash
# 1. 确认网关 config.toml [tls] enabled = false
# 2. 确认探针 config.toml gateway.address 地址和端口正确
# 3. 探针编译时确认未使用 SslCredentials（开发模式用 InsecureChannelCredentials）
#    在 GrpcClient.cpp 中临时替换 build_credentials() 返回值：
#    return grpc::InsecureChannelCredentials();
```

**问题：探针每次重启 terminal_id 都变了**
```bash
# 检查 /etc/safe_terminal/config.toml 中 terminal_id 字段是否为空
# 确认探针对该文件有写权限
sudo chmod 600 /etc/safe_terminal/config.toml
sudo chown root:root /etc/safe_terminal/config.toml
```

**问题：仪表盘图表为空 / 统计全是 0**
```bash
# 检查 InfluxDB 是否有数据
curl -X POST "http://localhost:8086/api/v2/query?org=safe-terminal" \
  -H "Authorization: Token safe-terminal-influx-token" \
  -H "Content-Type: application/vnd.flux" \
  -d 'from(bucket:"metrics") |> range(start: -1h) |> limit(n:5)'

# 检查 ES 日志索引
curl "http://localhost:9200/logs-*/_count"
```

**问题：告警 comment 字段未保存**
```bash
# 确认调用 PATCH /api/v1/alerts/{id}/resolve 时 body 格式正确：
# {"comment": "已处理原因"}
# AlertRepository 需有 @Modifying @Query updateComment() 方法
```
