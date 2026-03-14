# 部署说明

> 终端安全监控系统 · 本地开发环境 & 生产环境部署指南

---

## 目录

- [环境要求](#环境要求)
- [本地开发环境](#本地开发环境（推荐先跑通）)
- [各模块详细部署](#各模块详细部署)
  - [1. 基础设施（Docker Compose）](#1-基础设施)
  - [2. Go 接入网关](#2-go-接入网关)
  - [3. Java 核心后端](#3-java-核心后端)
  - [4. Vue 管理前端](#4-vue-管理前端)
  - [5. C++ 客户端探针](#5-c-客户端探针)
- [生产环境注意事项](#生产环境注意事项)
- [服务端口速查](#服务端口速查)
- [常见问题排查](#常见问题排查)

---

## 环境要求

### 开发机（运行全部服务）

| 工具 | 版本要求 | 说明 |
|------|----------|------|
| Docker Desktop | ≥ 24.x | 运行基础设施 |
| Go | ≥ 1.22 | 编译网关 |
| JDK | 21 (LTS) | 编译/运行后端 |
| Maven | ≥ 3.9（或用 mvnw） | Java 构建工具 |
| Node.js | ≥ 20 LTS | 前端开发 |
| protoc | ≥ 25.x | 生成 Protobuf 代码 |
| CMake | ≥ 3.20 | 编译 C++ 探针 |
| vcpkg | 最新 | C++ 依赖管理 |

### 探针目标机（运行 C++ 探针）

| 平台 | 最低版本 |
|------|----------|
| Windows | Windows 10 1909 / Windows 11 |
| 国产 Linux | 统信 UOS 20 / 麒麟 V10 |
| macOS（可选） | macOS 12 Monterey |

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
# 安装 protoc-gen-go 和 protoc-gen-go-grpc
go install google.golang.org/protobuf/cmd/protoc-gen-go@latest
go install google.golang.org/grpc/cmd/protoc-gen-go-grpc@latest

# 确保 $GOPATH/bin 在 PATH 中
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
go mod download
go run main.go -config config/config.toml
```

**验证网关启动：**

```bash
# gRPC 端口
# Windows:
netstat -ano | findstr 50051
# Linux/macOS:
lsof -i :50051

# Prometheus 指标页
curl http://localhost:9090/metrics | head -20
```

#### 构建二进制（生产用）

```bash
cd gateway
# Linux 部署
GOOS=linux GOARCH=amd64 go build -o dist/st_gateway main.go
# Windows 部署
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

# 测试终端列表接口（无需认证，开发阶段）
curl http://localhost:8080/api/v1/terminals
```

#### 配置文件说明

`backend/src/main/resources/application.yml` 中需确认以下配置与 Docker 环境一致：

```yaml
spring:
  datasource:
    url: jdbc:mysql://localhost:3306/safe_terminal  # MySQL 地址
  elasticsearch:
    uris: http://localhost:9200                     # ES 地址
  data:
    redis:
      host: localhost                               # Redis 地址
      password: safeTerminal@2025
  kafka:
    bootstrap-servers: localhost:9092               # Kafka 地址

influxdb:
  url: http://localhost:8086
  token: safe-terminal-influx-token
```

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

默认账号（初始化 SQL 中预设）：
- 用户名：`admin`
- 密码：`Admin@123456`

> **注意**：登录功能需要先完成后端 JWT 接口开发（见[下一步开发文档](./DEVELOPMENT_NEXT_STEPS.md)），在此之前前端会跳转登录页但无法真正认证。临时调试可注释掉 `router/index.ts` 中的路由守卫。

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

    # WebSocket
    location /ws {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
```

---

### 5. C++ 客户端探针

#### Windows 环境

**安装依赖（vcpkg）：**

```powershell
# 安装 vcpkg（如未安装）
git clone https://github.com/microsoft/vcpkg.git C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat

# 安装依赖
C:\vcpkg\vcpkg install grpc:x64-windows protobuf:x64-windows spdlog:x64-windows tomlplusplus:x64-windows
```

**编译：**

```powershell
cd client
cmake -B build -DCMAKE_TOOLCHAIN_FILE=C:/vcpkg/scripts/buildsystems/vcpkg.cmake -DVCPKG_TARGET_TRIPLET=x64-windows
cmake --build build --config Release
```

**运行：**

```powershell
.\build\Release\st_agent.exe config.toml
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

# 安装 vcpkg
git clone https://github.com/microsoft/vcpkg.git ~/vcpkg
~/vcpkg/bootstrap-vcpkg.sh

# 安装依赖
~/vcpkg/vcpkg install grpc protobuf spdlog tomlplusplus
```

**编译：**

```bash
cd client
cmake -B build \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_TOOLCHAIN_FILE=~/vcpkg/scripts/buildsystems/vcpkg.cmake
cmake --build build -j$(nproc)
```

**运行：**

```bash
# 创建必要目录
sudo mkdir -p /etc/safe_terminal /var/log/safe_terminal /var/lib/safe_terminal/cache

# 复制配置文件并修改 gateway.address
sudo cp config.toml /etc/safe_terminal/config.toml
sudo nano /etc/safe_terminal/config.toml

# 运行（开发测试，非 TLS 模式下暂时跳过证书配置）
sudo ./build/st_agent /etc/safe_terminal/config.toml
```

**安装为 systemd 服务（Linux 生产）：**

```ini
# /etc/systemd/system/st-agent.service
[Unit]
Description=Safe Terminal Agent
After=network.target

[Service]
Type=simple
ExecStart=/usr/local/bin/st_agent /etc/safe_terminal/config.toml
Restart=on-failure
RestartSec=5
User=root

[Install]
WantedBy=multi-user.default
```

```bash
sudo systemctl enable st-agent
sudo systemctl start st-agent
sudo systemctl status st-agent
```

**安装为 Windows 服务：**

```powershell
# 使用 NSSM（Non-Sucking Service Manager）
nssm install SafeTerminalAgent "C:\Program Files\SafeTerminal\st_agent.exe" "C:\ProgramData\SafeTerminal\config.toml"
nssm start SafeTerminalAgent
```

---

## 生产环境注意事项

### TLS 证书生成（自签名，测试用）

```bash
mkdir -p certs && cd certs

# 生成 CA
openssl genrsa -out ca.key 4096
openssl req -new -x509 -days 3650 -key ca.key -out ca.crt \
    -subj "/CN=SafeTerminal-CA"

# 生成服务端证书（网关用）
openssl genrsa -out server.key 2048
openssl req -new -key server.key -out server.csr \
    -subj "/CN=gateway.safe-terminal.internal"
openssl x509 -req -days 365 -in server.csr -CA ca.crt -CAkey ca.key \
    -CAcreateserial -out server.crt

# 生成客户端证书（探针用）
openssl genrsa -out agent.key 2048
openssl req -new -key agent.key -out agent.csr -subj "/CN=agent"
openssl x509 -req -days 365 -in agent.csr -CA ca.crt -CAkey ca.key \
    -CAcreateserial -out agent.crt
```

将 `ca.crt`、`server.crt`、`server.key` 部署到网关；  
将 `ca.crt`、`agent.crt`、`agent.key` 随探针分发。

### 修改默认密码

部署前必须修改以下默认密码（`docker-compose.yml` 和 `application.yml`）：

| 服务 | 默认密码 | 配置位置 |
|------|----------|----------|
| MySQL root | `safeTerminal@2025` | docker-compose.yml |
| Redis | `safeTerminal@2025` | docker-compose.yml |
| InfluxDB | `safeTerminal@2025` | docker-compose.yml |
| 后台 admin | `Admin@123456` | db/init.sql (BCrypt) |

### Kafka 生产配置（提高可靠性）

修改 `docker-compose.yml` 中 Kafka 的副本数和 `application.yml` 中的 acks：

```yaml
# application.yml 增加
spring:
  kafka:
    producer:
      acks: all           # 等待所有副本确认
      retries: 3
```

---

## 服务端口速查

| 服务 | 端口 | 协议 | 说明 |
|------|------|------|------|
| Go 网关 gRPC | 50051 | gRPC/TLS | 探针接入 |
| Go 网关 Metrics | 9090 | HTTP | Prometheus 指标 |
| Java 后端 | 8080 | HTTP | REST API + WebSocket |
| Vue 前端（开发） | 3000 | HTTP | Vite 开发服务器 |
| Kafka | 9092 | TCP | 消息队列 |
| Elasticsearch | 9200 | HTTP | 日志存储 |
| InfluxDB | 8086 | HTTP | 时序指标 |
| Redis | 6379 | TCP | 缓存/状态 |
| MySQL | 3306 | TCP | 元数据 |
| Kibana（可选） | 5601 | HTTP | ES 可视化 |

---

## 常见问题排查

**问题：Kafka 连接失败**
```bash
# 检查 Kafka 是否就绪
docker logs st_kafka | tail -20
# 确认 topic 已创建
docker exec st_kafka kafka-topics --bootstrap-server localhost:9092 --list
```

**问题：Elasticsearch 启动后 status=red**
```bash
# 检查日志
docker logs st_elasticsearch | tail -30
# 内存不足时调低 ES_JAVA_OPTS
# 修改 docker-compose.yml: ES_JAVA_OPTS=-Xms512m -Xmx512m
```

**问题：Java 后端启动报 "Table doesn't exist"**
```bash
# 手动执行初始化脚本
docker exec -i st_mysql mysql -u root -psafeTerminal@2025 safe_terminal \
  < backend/src/main/resources/db/init.sql
# 确认 application.yml 中 ddl-auto: update（开发环境）
```

**问题：前端代理 /api 报 502**
```bash
# 确认后端已在 8080 端口监听
curl http://localhost:8080/api/actuator/health
# 检查 vite.config.ts 中代理配置是否正确
```

**问题：探针连接网关失败**
```bash
# 确认网关已启动并监听 50051
# 检查 config.toml 中 gateway.address 填写正确
# 开发阶段可临时使用 InsecureCredentials（跳过 TLS）
```
