# 终端安全监控系统

> 支持 **3 万台** Windows / 国产 Linux（麒麟/统信）/ macOS 终端并发接入的企业级安全监控平台  
> 实时采集系统日志与 USB 接入事件，提供告警规则引擎、可视化管理控制台与可分发安装包

---

## 目录

- [系统架构](#系统架构)
- [功能概览](#功能概览)
- [目录结构](#目录结构)
- [快速开始](#快速开始)
- [客户端安装包分发](#客户端安装包分发)
- [API 速查](#api-速查)
- [技术栈](#技术栈)
- [平台支持状态](#平台支持状态)
- [相关文档](#相关文档)

---

## 系统架构

```
┌─────────────────────────────────────────────────────────────────────┐
│  企业终端（Windows / 国产 Linux / macOS）                            │
│  C++ 探针  ──gRPC mTLS/JWT──▶  Go 接入网关集群（×3 实例）           │
│                               ──Kafka──▶  Java Spring Boot 后端     │
│  探针 ◀── 策略下发 ──────── Redis Pub/Sub ◀─── 后端               │
└─────────────────────────────────────────────────────────────────────┘
                                        │
                    ┌───────────────────┼─────────────────┐
              Elasticsearch         InfluxDB        MySQL + Redis
              （日志/USB 事件）   （时序指标）    （元数据/告警/策略）
                                        │
                              Vue 3 管理控制台
                         REST API + WebSocket/STOMP
```

| 层级 | 组件 | 规模 |
|------|------|------|
| 接入层 | Go 网关（无状态，水平扩展） | 单实例 1 万连接，3 实例支撑 3 万终端 |
| 传输层 | gRPC 双向流 + TLS / mTLS | 支持证书认证与 JWT Token 认证 |
| 消息层 | Kafka 3（3 个 Topic，6/6/3 分区） | 7 天保留，2 副本 |
| 存储层 | ES + InfluxDB + MySQL + Redis | 按天分索引，时序趋势，告警持久化 |

---

## 功能概览

### C++ 客户端探针
- **系统日志采集**：Windows Event Log（`EvtSubscribe` 异步回调）、Linux journald（`sd-journal` 轮询）
- **USB 设备监控**：Linux `libudev`（热插拔实时检测）、Windows SetupAPI 枚举；提取设备 ID、厂商、序列号、接入时间
- **心跳上报**：CPU / 内存 / 磁盘实时指标（跨平台：Windows `GetSystemTimes`、Linux `/proc/stat`、macOS `host_statistics`）
- **断线缓存**：磁盘 WAL（Write-Ahead Log）环形缓冲区，断网期间数据持久化，恢复后按序重传
- **策略执行**：接收服务端下发的日志级别、USB 黑白名单策略并实时生效
- **终端标识**：首次启动自动生成 UUID 并持久化写入 `config.toml`，重启后复用
- **可分发安装包**：Windows NSIS 安装程序（支持 GPO/SCCM 静默安装）、Linux DEB/RPM 包（systemd 服务自动注册）

### Go 接入网关
- gRPC 双向流，支持 3 万并发长连接
- 三段认证优先级：mTLS 证书 CN → JWT Bearer Token → `x-terminal-id` Header（开发模式）
- 数据批量写入 Kafka（100 条/批 或 100ms 超时）
- 订阅 Redis Pub/Sub，将策略变更实时推送给探针
- 在线状态写入 Redis Hash；Prometheus 指标暴露；优雅关机

### Java Spring Boot 后端
- Kafka 消费，分流写入 Elasticsearch（日志/USB 事件，按天索引）、InfluxDB（时序指标）、Redis（在线状态）
- 规则引擎（责任链模式）：「1 小时内同一终端 3 次不同 USB」等规则触发告警
- 告警实时推送：WebSocket + STOMP，前端订阅 `/topic/alerts`
- JWT 登录认证 + Spring Security 权限控制
- InfluxDB Flux 查询接口：USB 接入趋势、心跳指标时序数据

### Vue 3 管理前端
- **仪表盘**：在线终端数、今日告警、7 天事件趋势图（ECharts）、告警类型分布饼图——全部接入真实 API
- **终端管理**：分页列表，批量在线状态查询，单终端详情（最近事件 + USB 记录 + 告警）
- **事件检索**：日志 / USB 双 Tab，支持时间范围、终端 ID、关键词过滤，后端分页，CSV 导出
- **策略配置**：日志采集级别、USB 黑白名单可视化配置，保存后自动推送至探针
- **告警中心**：实时滚动列表，支持确认、处理（含备注），WebSocket 推送新告警
- Token 自动刷新（4 分钟轮询，5 分钟内到期则续签）；路由守卫检测 Token 有效期

---

## 目录结构

```
safe_terminal/
├── proto/                        # Protobuf 消息定义（各模块共享）
│   └── terminal.proto            # LogEntry, UsbEvent, Heartbeat, Policy, AgentReport, GatewayCommand
├── client/                       # C++ 客户端探针
│   ├── CMakeLists.txt            # 含 CPack DEB/RPM/NSIS 打包配置
│   ├── config/
│   │   └── config.toml.template  # 安装包配置模板（含占位符）
│   ├── src/
│   │   ├── main.cpp              # 入口：Windows Service 模式 + 控制台模式自适应
│   │   ├── config/               # TOML 配置加载，terminal_id 持久化
│   │   ├── transport/            # gRPC 客户端、磁盘 WAL 环形缓冲区
│   │   └── probe/                # 日志采集、USB 监控、心跳上报、策略管理
│   ├── packaging/
│   │   ├── windows/
│   │   │   ├── installer.nsi     # NSIS 安装脚本（GUI + /S 静默模式）
│   │   │   ├── install-service.bat
│   │   │   └── uninstall-service.bat
│   │   ├── linux/
│   │   │   ├── st-agent.service  # systemd 单元（含资源限制/安全加固）
│   │   │   ├── postinst          # 安装后：建用户、建目录、启动服务
│   │   │   ├── prerm             # 卸载前：停服务
│   │   │   └── postrm            # 卸载后：purge 时彻底清理
│   │   └── PACKAGING.md          # 打包构建与企业批量分发指南
├── gateway/                      # Go 接入网关
│   ├── main.go
│   ├── go.mod
│   ├── config/                   # 配置结构（含 TLS / JWT / Kafka / Redis）
│   └── internal/
│       ├── grpc/                 # gRPC 服务 + 三段认证 + 策略推送
│       ├── kafka/                # 批量 Producer
│       ├── redis/                # 在线状态写入 + Pub/Sub 订阅
│       └── metrics/              # Prometheus 指标
├── backend/                      # Java Spring Boot 核心后端
│   ├── pom.xml
│   └── src/main/java/com/safeterminal/
│       ├── domain/               # JPA 实体 + ES 文档 + InfluxDB 数据对象
│       ├── repository/           # JPA Repository（含自定义 JPQL）
│       ├── kafka/                # Kafka 消费者（日志/USB/心跳）
│       ├── service/              # 业务逻辑：规则引擎、告警、统计、InfluxDB 查询
│       ├── controller/           # REST API（终端/事件/告警/策略/统计/指标）
│       ├── websocket/            # STOMP 告警推送
│       ├── security/             # JWT 登录 + Spring Security
│       └── config/               # ES 索引名解析器、InfluxDB、Kafka 配置
├── frontend/                     # Vue 3 管理控制台
│   ├── src/
│   │   ├── api/                  # Axios 封装（terminal / alert / policy / stats / metrics）
│   │   ├── views/                # 仪表盘、终端列表/详情、事件检索、策略配置、告警中心
│   │   ├── layout/               # 主布局（侧边栏 + 头部 + Token 刷新）
│   │   ├── stores/               # Pinia：用户认证（JWT 解析 + 自动刷新）、告警
│   │   ├── router/               # 路由守卫（Token 有效期检测）
│   │   └── composables/          # WebSocket Hook（SockJS + STOMP）
│   └── vite.config.ts
├── scripts/
│   ├── build_windows_installer.ps1   # Windows 安装包一键构建
│   └── build_linux_package.sh        # Linux DEB/RPM 包一键构建
├── docs/
│   ├── DEPLOYMENT.md             # 完整部署说明（本地开发 + 生产环境 + 故障排查）
│   └── DEVELOPMENT_NEXT_STEPS.md # 开发路线图（P0–P3 均已完成）
└── docker-compose.yml            # 一键启动所有基础设施
```

---

## 快速开始

### 1. 启动基础设施

```bash
docker-compose up -d
# 包含：Kafka + Zookeeper、Redis、Elasticsearch、InfluxDB、MySQL
# 可选工具（Kibana、Kafka UI）：
docker-compose --profile tools up -d
```

等待所有容器健康（约 60 秒）后进行后续步骤。

### 2. Go 接入网关

```bash
cd gateway
# 安装依赖（含 golang-jwt）
go mod tidy
# 生成 Protobuf Go 代码
protoc --go_out=gen/pb --go-grpc_out=gen/pb -I ../proto ../proto/terminal.proto
# 启动（开发模式：TLS 和 JWT 均关闭）
go run main.go -config config/config.toml
```

### 3. Java 核心后端

```bash
cd backend
./mvnw spring-boot:run
# 访问：http://localhost:8080/api
# 默认账号：admin / Admin@123456
```

### 4. Vue 管理前端

```bash
cd frontend
npm install
npm run dev
# 访问：http://localhost:3000
```

### 5. 编译 C++ 探针（开发调试）

```bash
# 安装依赖（Linux）
sudo apt install -y libgrpc++-dev libprotobuf-dev protobuf-compiler-grpc \
    libspdlog-dev libudev-dev libsystemd-dev

# 编译
cd client
cmake -B build -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_TOOLCHAIN_FILE=$VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake
cmake --build build --parallel

# 运行（Linux，配置已指向本地网关）
./build/st_agent config/config.toml.template
```

---

## 客户端安装包分发

探针支持生成企业级可分发安装包，适用于 GPO / SCCM / Ansible 批量部署。

### Windows（NSIS 安装程序）

```powershell
# 构建 .exe 安装包（自动注册 Windows 服务，无需 NSSM）
.\scripts\build_windows_installer.ps1 -Version 1.0.0 -Gateway "192.168.10.5:50051"
# 输出：client\dist\st_agent_setup_1.0.0_x64.exe

# GPO / SCCM 静默安装
st_agent_setup_1.0.0_x64.exe /S /GATEWAY=192.168.10.5:50051
```

安装后 `SafeTerminalAgent` 服务自动以 SYSTEM 账户运行，设置失败 3 次自动重启。

### Linux（DEB / RPM 包）

```bash
# 构建 .deb + .rpm（自动集成 systemd 服务）
./scripts/build_linux_package.sh 1.0.0 192.168.10.5:50051
# 输出：client/dist/safe-terminal-agent_1.0.0_amd64.deb
#        client/dist/safe-terminal-agent-1.0.0.x86_64.rpm

# 安装（UOS / 麒麟 DEB）
sudo dpkg -i safe-terminal-agent_1.0.0_amd64.deb

# Ansible 批量部署
ansible all -m copy -a "src=safe-terminal-agent_1.0.0_amd64.deb dest=/tmp/"
ansible all -m apt  -a "deb=/tmp/safe-terminal-agent_1.0.0_amd64.deb"
```

完整分发指南见 [`client/packaging/PACKAGING.md`](client/packaging/PACKAGING.md)。

---

## API 速查

所有接口以 `/api` 为前缀（如 `http://localhost:8080/api/v1/terminals`）。

### 认证

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/v1/auth/login` | 账号密码登录，返回 JWT Token |
| GET  | `/v1/auth/me`    | 获取当前登录用户信息 |
| POST | `/v1/auth/refresh` | 刷新 Token |

### 终端管理

| 方法 | 路径 | 说明 |
|------|------|------|
| GET    | `/v1/terminals` | 终端列表（分页、关键词搜索） |
| GET    | `/v1/terminals/:id` | 终端详情 |
| PATCH  | `/v1/terminals/:id` | 更新部门 / 责任人 |
| DELETE | `/v1/terminals/:id` | 注销终端 |
| GET    | `/v1/terminals/stats/online-count` | 在线终端数 |
| GET    | `/v1/terminals/online-status?ids=id1,id2` | 批量在线状态查询 |

### 事件检索

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/v1/events/logs` | 系统日志检索（terminalId / keyword / startDate / endDate / page / size） |
| GET | `/v1/events/usb`  | USB 事件检索（terminalId / deviceId / startDate / endDate / page / size） |

### 告警管理

| 方法 | 路径 | 说明 |
|------|------|------|
| GET   | `/v1/alerts` | 告警列表（terminalId / status / type / page / size） |
| PATCH | `/v1/alerts/:id/confirm` | 确认告警 |
| PATCH | `/v1/alerts/:id/resolve` | 处理告警（含备注 comment） |

### 策略配置

| 方法 | 路径 | 说明 |
|------|------|------|
| GET    | `/v1/policies` | 策略列表 |
| POST   | `/v1/policies` | 创建策略 |
| PUT    | `/v1/policies/:id` | 更新并下发策略（写入 Redis Pub/Sub） |
| DELETE | `/v1/policies/:id` | 删除策略 |
| GET    | `/v1/policies/terminal/:terminalId` | 查询终端绑定策略 |

### 统计 & 指标

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/v1/stats/event-trend?days=7` | 每日事件数趋势（日志 + USB + 告警） |
| GET | `/v1/stats/alert-distribution` | 告警类型分布 |
| GET | `/v1/metrics/usb-trend?terminalId=&hours=24` | USB 接入趋势（InfluxDB） |
| GET | `/v1/metrics/heartbeat?terminalId=&minutes=60` | 心跳指标时序（CPU/内存/磁盘） |

### WebSocket

```
连接：ws://localhost:8080/api/ws（SockJS）
订阅：/topic/alerts    → 实时告警推送
```

---

## 技术栈

| 模块 | 技术 |
|------|------|
| 客户端探针 | C++17 · gRPC-C++ · Protobuf · spdlog · toml++ · libudev · sd-journal |
| 接入网关 | Go 1.22 · gRPC-Go · Sarama · go-redis · golang-jwt · zap · Prometheus |
| 核心后端 | Java 21 · Spring Boot 3.3 · Spring Kafka · Spring Data ES/Redis/JPA · Spring Security · InfluxDB Client · WebSocket/STOMP · Lombok · MapStruct |
| 管理前端 | Vue 3 · TypeScript · Vite · Element Plus · ECharts · Pinia · Axios · SockJS+STOMP |
| 存储 | Elasticsearch 8 · InfluxDB 2 · Redis 7 · MySQL 8 |
| 消息队列 | Kafka 3（Confluent CP 7.5） |
| 打包 | CMake + CPack · NSIS · dpkg/rpm |

---

## 平台支持状态

| 平台 | 日志采集 | USB 监控 | 心跳 | 安装包 |
|------|----------|----------|------|--------|
| Windows 10/11 | ✅ EvtSubscribe | ⚠️ 待补全热插拔 | ✅ GetSystemTimes | ✅ NSIS .exe |
| 统信 UOS / 麒麟（DEB） | ✅ journald | ✅ libudev | ✅ /proc/stat | ✅ .deb |
| 麒麟（RPM） | ✅ journald | ✅ libudev | ✅ /proc/stat | ✅ .rpm |
| macOS 12+（可选） | ⚠️ 存根 | ⚠️ 存根 | ✅ host_statistics | 手动编译 |
| Linux auditd | ⚠️ 存根 | — | — | — |

> ⚠️ 标注项为功能存根，不影响主平台（Windows + 国产 Linux）的正常运行。

---

## 相关文档

| 文档 | 说明 |
|------|------|
| [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) | 完整部署说明：本地开发、生产环境、高可用拓扑、常见问题排查 |
| [`docs/DEVELOPMENT_NEXT_STEPS.md`](docs/DEVELOPMENT_NEXT_STEPS.md) | 开发路线图与各阶段（P0–P3）实现记录 |
| [`client/packaging/PACKAGING.md`](client/packaging/PACKAGING.md) | 安装包构建与企业批量分发（GPO / SCCM / Ansible）指南 |
