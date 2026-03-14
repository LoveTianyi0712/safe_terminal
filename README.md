# 终端安全监控系统 (Safe Terminal Monitor)

> 支持 3 万台 Windows / 国产 Linux（麒麟/统信）/ macOS 终端的高并发安全监控系统

---

## 系统架构

```
[C++ 探针]  ──gRPC TLS──▶  [Go 网关集群]  ──Kafka──▶  [Java 后端]  ──REST/WS──▶  [Vue 前端]
              ◀── 策略下发 ──              ◀──Redis Pub/Sub──▶
                                                  │
                              ┌───────────────────┼───────────────────┐
                           Elasticsearch      InfluxDB    MySQL + Redis
```

---

## 目录结构

```
safe_terminal/
├── proto/                  # Protobuf 消息定义（共享）
│   └── terminal.proto
├── client/                 # C++ 客户端探针
│   ├── CMakeLists.txt
│   ├── config.toml         # 探针配置文件模板
│   └── src/
│       ├── main.cpp
│       ├── config/         # 配置加载（TOML）
│       ├── transport/      # gRPC 客户端 + 环形缓冲区
│       └── probe/          # 日志采集/USB监控/心跳/策略管理
├── gateway/                # Go 接入网关
│   ├── main.go
│   ├── go.mod
│   ├── config/             # 配置（TOML + Viper）
│   └── internal/
│       ├── grpc/           # gRPC 服务实现 + 连接注册
│       ├── kafka/          # 批量 Kafka Producer
│       ├── redis/          # 在线状态 + 策略订阅
│       └── metrics/        # Prometheus 指标
├── backend/                # Java Spring Boot 核心后端
│   ├── pom.xml
│   └── src/main/java/com/safeterminal/
│       ├── domain/         # 实体（JPA）/ 文档（ES）
│       ├── repository/     # JPA Repository
│       ├── kafka/          # Kafka 消费者
│       ├── service/        # 业务逻辑 + 规则引擎
│       ├── controller/     # REST API
│       ├── websocket/      # STOMP WebSocket
│       └── config/         # Kafka/InfluxDB 配置
├── frontend/               # Vue 3 管理前端
│   ├── src/
│   │   ├── api/            # Axios API 封装
│   │   ├── views/          # 页面组件
│   │   ├── layout/         # 布局组件
│   │   ├── stores/         # Pinia 状态管理
│   │   └── composables/    # WebSocket Hook
│   └── vite.config.ts
└── docker-compose.yml      # 本地开发基础设施一键启动
```

---

## 快速开始

### 1. 启动基础设施（Docker）

```bash
docker-compose up -d
# 含: Kafka + Zookeeper, Redis, Elasticsearch, InfluxDB, MySQL
# 可选开发工具(Kibana): docker-compose --profile tools up -d
```

等待所有服务健康后进行下一步。

### 2. 编译并运行 Go 网关

```bash
cd gateway
# 生成 Protobuf Go 代码（需安装 protoc + protoc-gen-go + protoc-gen-go-grpc）
protoc --go_out=gen/pb --go-grpc_out=gen/pb -I ../proto ../proto/terminal.proto
go mod download
go run main.go -config config/config.toml
```

### 3. 运行 Java 后端

```bash
cd backend
./mvnw spring-boot:run
# 访问: http://localhost:8080/api
# Swagger: http://localhost:8080/api/swagger-ui.html（需添加 springdoc-openapi 依赖）
```

### 4. 运行 Vue 前端

```bash
cd frontend
npm install
npm run dev
# 访问: http://localhost:3000
# 默认账号: admin / Admin@123456
```

### 5. 编译 C++ 探针

```bash
cd client
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
# 运行：
./build/st_agent config.toml
```

**依赖（通过 vcpkg 安装）：**
```bash
vcpkg install grpc protobuf spdlog tomlplusplus
# Linux 额外安装:
apt-get install libudev-dev libsystemd-dev
```

---

## API 一览

| 方法     | 路径                              | 说明               |
|----------|-----------------------------------|--------------------|
| GET      | /v1/terminals                     | 终端列表（分页）   |
| GET      | /v1/terminals/:id                 | 终端详情           |
| GET      | /v1/terminals/stats/online-count  | 在线终端数         |
| PATCH    | /v1/terminals/:id                 | 更新部门/责任人    |
| GET      | /v1/events/logs                   | 日志检索（ES）     |
| GET      | /v1/alerts                        | 告警列表           |
| PATCH    | /v1/alerts/:id/confirm            | 确认告警           |
| PATCH    | /v1/alerts/:id/resolve            | 解决告警           |
| GET      | /v1/policies                      | 策略列表           |
| POST     | /v1/policies                      | 创建策略           |
| PUT      | /v1/policies/:id                  | 更新并下发策略     |
| DELETE   | /v1/policies/:id                  | 删除策略           |

**WebSocket**: `ws://localhost:8080/api/ws` → 订阅 `/topic/alerts`

---

## 关键待实现项（TODO）

| 模块       | TODO                                                    |
|------------|---------------------------------------------------------|
| C++ 探针   | Windows EvtSubscribe 实现，Linux journald/udev 完整实现  |
| C++ 探针   | 磁盘 WAL 环形缓冲区读写                                  |
| Go 网关    | mTLS 双向认证（BuildTLSCredentials）                    |
| Go 网关    | Token 签名验证（authenticate 方法）                     |
| Java 后端  | Spring Security JWT 登录接口                            |
| Java 后端  | ES 跨天索引查询时间范围过滤 + 分页                       |
| Java 后端  | IndexNameResolver Bean（动态索引名）                    |
| 前端       | 仪表盘图表接入真实 API 数据                              |

---

## 技术栈

| 模块   | 技术                                                |
|--------|-----------------------------------------------------|
| 探针   | C++17, gRPC-C++, Protobuf, spdlog, toml++           |
| 网关   | Go 1.22, gRPC-Go, sarama, go-redis, zap, Prometheus |
| 后端   | Java 21, Spring Boot 3.3, Kafka, ES 8, InfluxDB 2   |
| 前端   | Vue 3, TypeScript, Vite, Element Plus, ECharts      |
| 存储   | Elasticsearch 8, InfluxDB 2, Redis 7, MySQL 8       |
| 消息   | Kafka 3 (Confluent CP 7.5)                          |
