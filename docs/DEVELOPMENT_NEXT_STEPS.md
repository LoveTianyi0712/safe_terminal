# 下一步开发重点

> 当前代码框架已搭建完毕，本文档说明每个模块需要填充的关键逻辑，  
> 按**优先级排序**，建议从上到下依次完成。

---

## ✅ P0 已完成（2025-03-14）

以下三项 P0 任务已全部实现，后端可正常启动，网关支持开发/生产双模式运行。

### ✅ P0.1 — Spring Security JWT 登录接口（Java 后端）

**涉及文件：** `backend/src/main/java/com/safeterminal/security/`

**实现内容：**

| 文件 | 功能 |
|------|------|
| `JwtTokenProvider.java` | 使用 JJWT 0.12.6 生成/解析/校验 Token；Payload 含 `sub`（用户名）、`role`（角色）、`iat`/`exp`；支持 Access Token（24h）和 Refresh Token（7d）|
| `JwtAuthFilter.java` | `OncePerRequestFilter`，从 `Authorization: Bearer <token>` 头提取并校验 Token，将认证信息写入 `SecurityContextHolder` |
| `SecurityConfig.java` | 无状态 Session（JWT 模式）；配置公开路径白名单（`/auth/**`、`/ws/**`、`/actuator/health`）；CORS 允许前端开发服务器（localhost:3000）；未认证返回 JSON 401，禁止访问返回 JSON 403 |
| `UserDetailsServiceImpl.java` | 从 MySQL `sys_user` 表加载用户，角色自动加 `ROLE_` 前缀以兼容 `hasRole()` |
| `JwtProperties.java` | 绑定 `application.yml` 中 `jwt.*` 配置（secret、expirationMs、refreshExpirationMs）|
| `AuthController.java` | `POST /auth/login` 返回 `{ token, refreshToken, expiresAt, username, role }`；`POST /auth/refresh` 用 Refresh Token 换新 Access Token；`GET /auth/me` 返回当前用户信息 |

**pom.xml** 已包含 `jjwt-api / jjwt-impl / jjwt-jackson 0.12.6` 依赖。  
**application.yml** 中 `jwt.secret` 已配置 Base64 密钥（生产环境需替换）。

---

### ✅ P0.2 — ES 动态索引名 Bean（Java 后端）

**新建文件：** `backend/src/main/java/com/safeterminal/config/EsIndexNameResolver.java`

**实现内容：**

- Spring Bean 名称为 `indexNameResolver`，与 `LogDocument` 和 `UsbEventDocument` 中 SpEL 表达式 `@Document(indexName = "#{@indexNameResolver.resolveLogIndex()}")` 完全匹配
- `resolveLogIndex()` 返回 `logs-yyyy.MM.dd`（当天日志索引）
- `resolveUsbIndex()` 返回 `usb-events-yyyy.MM.dd`（当天 USB 事件索引）
- 前缀从 `application.yml` 中 `safe-terminal.es.index.log-prefix` / `usb-prefix` 读取，可灵活修改
- **此 Bean 缺失时后端启动即报错**（SpEL 解析失败），为阻塞型 Bug，现已修复

---

### ✅ P0.3 — Go 网关 TLS 证书加载（Go 网关）

**涉及文件：**

| 文件 | 改动内容 |
|------|---------|
| `gateway/internal/grpc/server.go` | 实现 `BuildTLSCredentials(cfg TLSConfig)`：用 `tls.LoadX509KeyPair` 加载服务端证书/私钥，读取 CA 证书构建 `x509.CertPool`，配置 mTLS（`ClientAuth: RequireAndVerifyClientCert`，`MinVersion: TLS 1.2`）|
| `gateway/config/config.go` | `TLSConfig` 新增 `Enabled bool` 字段，用于控制是否启用 TLS |
| `gateway/config/config.toml` | `[tls]` 块新增 `enabled = false`（开发模式默认关闭，不需要准备证书）|
| `gateway/main.go` | 用 `if cfg.TLS.Enabled` 条件加载 TLS；`enabled=true` 时加载证书并以 mTLS 模式启动；`enabled=false` 时打印警告后以明文模式运行 |

**使用方式：**
- **开发阶段**：`config.toml` 保持 `enabled = false`，探针端 `GrpcClient.cpp` 使用 `InsecureChannelCredentials`，无需任何证书文件
- **生产部署**：将 `enabled` 改为 `true`，按 `DEPLOYMENT.md` 中"TLS 证书生成"章节准备三个证书文件后直接启动

---

---

## 目录

- [优先级总览](#优先级总览)
- [阶段一：打通主干链路（必须先做）](#阶段一打通主干链路)
- [阶段二：完善探针采集能力](#阶段二完善探针采集能力)
- [阶段三：安全认证体系](#阶段三安全认证体系)
- [阶段四：功能完善与优化](#阶段四功能完善与优化)
- [各模块 TODO 定位速查](#各模块-todo-定位速查)

---

## 优先级总览

| 优先级 | 状态 | 任务 | 模块 | 预估工作量 |
|--------|------|------|------|-----------|
| ~~P0~~ | ✅ 已完成 | ~~Spring Security JWT 登录接口~~ | Java 后端 | 0.5 天 |
| ~~P0~~ | ✅ 已完成 | ~~ES 动态索引名 Bean~~ | Java 后端 | 0.5 天 |
| ~~P0~~ | ✅ 已完成 | ~~Go 网关 TLS 证书加载~~ | Go 网关 | 0.5 天 |
| P1 | 待开发 | C++ Linux USB 监控（libudev） | C++ 探针 | 1 天 |
| P1 | 待开发 | C++ Linux 日志采集（journald） | C++ 探针 | 1 天 |
| P1 | 待开发 | C++ Windows 日志采集（EvtSubscribe） | C++ 探针 | 1.5 天 |
| P1 | 待开发 | C++ 磁盘 WAL 环形缓冲区 | C++ 探针 | 1 天 |
| P2 | 待开发 | Go 网关 Token 身份验证 | Go 网关 | 0.5 天 |
| P2 | 待开发 | ES 时间范围检索 + 分页 | Java 后端 | 0.5 天 |
| P2 | 待开发 | 仪表盘图表接入真实数据 | Vue 前端 | 1 天 |
| P3 | 待开发 | USB 事件 ES 检索接口 | Java 后端 | 0.5 天 |
| P3 | 待开发 | 前端登录 + Token 持久化 | Vue 前端 | 0.5 天 |

---

## 阶段一：打通主干链路

> 目标：让前端能正常打开、登录，后端能接收 Kafka 消息并写入 ES

### 1.1 Java 后端 — Spring Security JWT 登录

**文件**：需新建 `backend/src/main/java/com/safeterminal/security/` 包

**需要创建的类：**

```
security/
├── JwtTokenProvider.java     # JWT 生成/解析/验证
├── JwtAuthFilter.java        # OncePerRequestFilter，从 Header 提取 Token
├── SecurityConfig.java       # WebSecurityConfigurerAdapter，配置白名单/CORS
└── AuthController.java       # POST /auth/login，返回 { token, expireAt, role }
```

**`AuthController.java` 核心逻辑：**

```java
@PostMapping("/auth/login")
public ResponseEntity<?> login(@RequestBody LoginRequest req) {
    // 1. 从 sys_user 表查用户
    // 2. BCryptPasswordEncoder 对比密码
    // 3. 生成 JWT（payload: username, role, exp）
    // 4. 返回 { "token": "eyJ...", "expireAt": "..." }
}
```

**推荐依赖（加入 pom.xml）：**

```xml
<dependency>
    <groupId>io.jsonwebtoken</groupId>
    <artifactId>jjwt-api</artifactId>
    <version>0.12.5</version>
</dependency>
<dependency>
    <groupId>io.jsonwebtoken</groupId>
    <artifactId>jjwt-impl</artifactId>
    <version>0.12.5</version>
    <scope>runtime</scope>
</dependency>
```

**前端对接**：`frontend/src/views/LoginView.vue` 的 `login()` 方法已预留接口调用，后端接口上线后直接可用。

---

### 1.2 Java 后端 — ES 动态索引名 Bean

**文件**：新建 `backend/src/main/java/com/safeterminal/config/EsIndexNameResolver.java`

```java
@Component("indexNameResolver")
public class EsIndexNameResolver {
    @Value("${safe-terminal.es.index.log-prefix:logs-}")
    private String logPrefix;

    @Value("${safe-terminal.es.index.usb-prefix:usb-events-}")
    private String usbPrefix;

    public String resolveLogIndex() {
        return logPrefix + LocalDate.now().format(DateTimeFormatter.ofPattern("yyyy.MM.dd"));
    }

    public String resolveUsbIndex() {
        return usbPrefix + LocalDate.now().format(DateTimeFormatter.ofPattern("yyyy.MM.dd"));
    }
}
```

`LogDocument` 和 `UsbEventDocument` 中的 `@Document(indexName = "#{@indexNameResolver.resolveLogIndex()}")` 依赖此 Bean，不创建则后端启动报错。

---

### 1.3 Go 网关 — TLS 证书加载

**文件**：`gateway/internal/grpc/server.go`，找到 `BuildTLSCredentials` 函数：

```go
func BuildTLSCredentials(cfg config.TLSConfig) (credentials.TransportCredentials, error) {
    // 替换此函数体：
    cert, err := tls.LoadX509KeyPair(cfg.CertFile, cfg.KeyFile)
    if err != nil {
        return nil, fmt.Errorf("load server cert: %w", err)
    }

    caPool := x509.NewCertPool()
    caData, err := os.ReadFile(cfg.CAFile)
    if err != nil {
        return nil, fmt.Errorf("read CA cert: %w", err)
    }
    caPool.AppendCertsFromPEM(caData)

    tlsCfg := &tls.Config{
        Certificates: []tls.Certificate{cert},
        ClientCAs:    caPool,
        ClientAuth:   tls.RequireAndVerifyClientCert, // mTLS
    }
    return credentials.NewTLS(tlsCfg), nil
}
```

**`main.go` 中取消注释（约第 60 行）：**

```go
creds, err := grpcserver.BuildTLSCredentials(cfg.TLS)
if err != nil {
    log.Fatal("failed to build TLS credentials", zap.Error(err))
}
grpcOpts = append(grpcOpts, grpc.Creds(creds))
```

**开发阶段跳过 TLS**（探针 `GrpcClient.cpp` 中改用 InsecureCredentials）：

```cpp
// GrpcClient.cpp connect_and_stream() 中临时替换：
channel_ = grpc::CreateChannel(cfg_.address, grpc::InsecureChannelCredentials());
```

---

## 阶段二：完善探针采集能力

> 目标：探针能真正采集到系统日志和 USB 事件，并通过 gRPC 上报

### 2.1 Linux 日志采集（journald）

**文件**：`client/src/probe/SystemLogCollector.cpp`，找到 `#elif defined(PLATFORM_LINUX)` 区块

**替换 `init_linux_journald()` 和 `poll_journald()`：**

```cpp
#include <systemd/sd-journal.h>

// 类头文件增加成员变量：
// sd_journal* journal_{nullptr};

void SystemLogCollector::init_linux_journald() {
    if (sd_journal_open(&journal_, SD_JOURNAL_LOCAL_ONLY) < 0) {
        spdlog::error("[LogCollector] Failed to open journald");
        return;
    }
    // 移动到最新位置，避免重放历史日志
    sd_journal_seek_tail(journal_);
    sd_journal_previous(journal_);
}

void SystemLogCollector::poll_journald() {
    int r = sd_journal_wait(journal_, 200000); // 200ms 超时
    if (r < 0) return;

    SD_JOURNAL_FOREACH_FORWARD(journal_) {
        const char* msg  = nullptr; size_t len = 0;
        const char* prio = nullptr; size_t plen = 0;

        sd_journal_get_data(journal_, "MESSAGE",  (const void**)&msg,  &len);
        sd_journal_get_data(journal_, "PRIORITY", (const void**)&prio, &plen);

        // PRIORITY 格式: "PRIORITY=6"，6=INFO, 4=WARNING, 3=ERROR
        int priority = prio ? atoi(prio + 9) : 6;
        terminal::v1::Severity sev = terminal::v1::SEV_INFO;
        if (priority <= 3) sev = terminal::v1::SEV_CRITICAL;
        else if (priority == 4) sev = terminal::v1::SEV_ERROR;
        else if (priority == 5) sev = terminal::v1::SEV_WARNING;

        // 按策略过滤
        if (sev < policy_mgr_->get_log_level()) continue;

        terminal::v1::LogEntry entry;
        *entry.mutable_identity() = identity_;
        *entry.mutable_ts() = google::protobuf::util::TimeUtil::GetCurrentTime();
        entry.set_severity(sev);
        entry.set_source("journald");
        entry.set_message(msg ? std::string(msg + 8, len - 8) : "");  // 跳过 "MESSAGE=" 前缀

        if (callback_) callback_(std::move(entry));
    }
}
```

> **注意**：`SystemLogCollector` 需要持有 `PolicyManager*`，在 `main.cpp` 中传入。

---

### 2.2 Linux USB 监控（libudev）

**文件**：`client/src/probe/UsbMonitor.cpp`，找到 `#elif defined(PLATFORM_LINUX)` 区块

**替换 `init_udev()` 和 `poll_udev()`：**

```cpp
#include <libudev.h>
#include <poll.h>

// 类头文件已声明 void* udev_, *udev_mon_
// 实际类型为 struct udev* 和 struct udev_monitor*

void UsbMonitor::init_udev() {
    struct udev* u = udev_new();
    udev_ = u;

    struct udev_monitor* mon = udev_monitor_new_from_netlink(u, "udev");
    udev_monitor_filter_add_match_subsystem_devtype(mon, "usb", "usb_device");
    udev_monitor_enable_receiving(mon);
    udev_mon_ = mon;
}

void UsbMonitor::poll_udev() {
    auto* mon = static_cast<struct udev_monitor*>(udev_mon_);
    int fd = udev_monitor_get_fd(mon);

    struct pollfd fds = {fd, POLLIN, 0};
    if (poll(&fds, 1, 200) <= 0) return;

    struct udev_device* dev = udev_monitor_receive_device(mon);
    if (!dev) return;

    const char* action = udev_device_get_action(dev);
    const char* vid    = udev_device_get_sysattr_value(dev, "idVendor");
    const char* pid    = udev_device_get_sysattr_value(dev, "idProduct");
    const char* serial = udev_device_get_sysattr_value(dev, "serial");
    const char* mfr    = udev_device_get_sysattr_value(dev, "manufacturer");
    const char* prod   = udev_device_get_sysattr_value(dev, "product");

    terminal::v1::UsbEvent event;
    *event.mutable_identity() = identity_;
    *event.mutable_ts() = google::protobuf::util::TimeUtil::GetCurrentTime();
    event.set_action(action && strcmp(action, "add") == 0
        ? terminal::v1::USB_CONNECTED
        : terminal::v1::USB_DISCONNECTED);

    if (vid && pid) {
        event.set_device_id(std::string("VID_") + vid + "&PID_" + pid);
    }
    if (mfr)    event.set_vendor(mfr);
    if (prod)   event.set_product(prod);
    if (serial) event.set_serial_number(serial);

    if (callback_) callback_(std::move(event));
    udev_device_unref(dev);
}
```

---

### 2.3 Windows 日志采集（EvtSubscribe）

**文件**：`client/src/probe/SystemLogCollector.cpp`，找到 `#if defined(PLATFORM_WINDOWS)` 区块

**核心思路**（在类中添加 `EVT_HANDLE subscription_` 成员）：

```cpp
// 订阅回调函数（静态）
static DWORD WINAPI EventCallback(EVT_SUBSCRIBE_NOTIFY_ACTION action,
                                   PVOID userContext, EVT_HANDLE event) {
    auto* collector = static_cast<SystemLogCollector*>(userContext);
    if (action == EvtSubscribeActionDeliver) {
        // 1. EvtRender(event, ..., EvtRenderEventXml) 获取 XML
        // 2. 解析 XML 提取: EventID, TimeCreated, Level, Message
        // 3. 构造 LogEntry 并调用 collector->callback_()
    }
    return ERROR_SUCCESS;
}

void SystemLogCollector::init_windows() {
    // 订阅 Security 频道（可按需添加 System/Application）
    subscription_ = EvtSubscribe(
        nullptr,                          // session
        nullptr,                          // signal event
        L"Security",                      // channel
        L"*[System[Level <= 3]]",         // XPath 过滤（仅 Warning 及以上）
        nullptr,                          // bookmark
        this,                             // userContext
        (EVT_SUBSCRIBE_CALLBACK)EventCallback,
        EvtSubscribeStartAtOldestRecord   // 改为 EvtSubscribeToFutureEvents 生产用
    );
    if (!subscription_) {
        spdlog::error("[LogCollector] EvtSubscribe failed: {}", GetLastError());
    }
}
```

---

### 2.4 C++ 磁盘 WAL 环形缓冲区

**文件**：`client/src/transport/RingBuffer.cpp`，找到 `write_to_wal()` 和 `recover_from_disk()`

**WAL 文件格式（简单二进制）：**

```
[4字节: 消息长度][N字节: 序列化的 Entry（topic长度 + topic + serialized）]
```

```cpp
void RingBuffer::write_to_wal(const Entry& e) {
    if (disk_path_.empty()) return;

    // 检查磁盘占用，超过上限则丢弃最老的 WAL 文件
    // TODO: 实现文件轮转（每 50MB 一个新文件）

    std::string wal_file = disk_path_ + "/cache.wal";
    std::ofstream f(wal_file, std::ios::binary | std::ios::app);

    // 序列化 Entry
    std::string payload;
    uint32_t topic_len = e.topic.size();
    payload.append(reinterpret_cast<char*>(&topic_len), 4);
    payload.append(e.topic);
    uint32_t data_len = e.serialized.size();
    payload.append(reinterpret_cast<char*>(&data_len), 4);
    payload.append(e.serialized);
    payload.append(reinterpret_cast<const char*>(&e.sequence), 8);

    uint32_t total_len = payload.size();
    f.write(reinterpret_cast<char*>(&total_len), 4);
    f.write(payload.data(), total_len);
}

void RingBuffer::recover_from_disk() {
    if (disk_path_.empty()) return;
    std::string wal_file = disk_path_ + "/cache.wal";
    std::ifstream f(wal_file, std::ios::binary);
    if (!f) return;

    // 逐条读取并压入 mem_queue_
    // 读取完成后删除或重命名 WAL 文件
    // TODO: 处理文件损坏（checksum 校验）
}
```

---

## 阶段三：安全认证体系

### 3.1 Go 网关 — Token 身份验证

**文件**：`gateway/internal/grpc/server.go`，找到 `authenticate()` 方法

**建议方案：JWT 验证**（与 Java 后端共享密钥）

```go
func (s *GatewayServer) authenticate(ctx context.Context) (string, error) {
    md, ok := metadata.FromIncomingContext(ctx)
    if !ok {
        return "", fmt.Errorf("missing metadata")
    }

    // 优先使用 mTLS 证书 CN
    if p, ok := peer.FromContext(ctx); ok {
        if tlsInfo, ok := p.AuthInfo.(credentials.TLSInfo); ok {
            if len(tlsInfo.State.PeerCertificates) > 0 {
                cn := tlsInfo.State.PeerCertificates[0].Subject.CommonName
                return cn, nil // CN 即为 terminal_id
            }
        }
    }

    // 备选：验证 JWT token
    tokens := md.Get("authorization")
    if len(tokens) == 0 {
        return "", fmt.Errorf("missing authorization header")
    }
    // 解析 JWT，提取 terminal_id claim
    // TODO: 使用 github.com/golang-jwt/jwt/v5 解析
    return "", fmt.Errorf("JWT validation not implemented")
}
```

---

### 3.2 前端 — 完善登录流程

**文件**：`frontend/src/views/LoginView.vue`

登录成功后需要将 token 存储并在后续请求中携带（已在 `http.ts` 的拦截器中处理），还需要：

1. 解析 JWT Payload 获取用户角色，存入 Pinia 用户 Store
2. 根据角色控制菜单和按钮权限（策略配置页的删除按钮仅 ADMIN 可见）

**新建 `frontend/src/stores/userStore.ts`：**

```typescript
export const useUserStore = defineStore('user', () => {
  const role     = ref<string>('')
  const username = ref<string>('')

  function setFromToken(token: string) {
    // 解析 JWT payload（Base64）
    const payload = JSON.parse(atob(token.split('.')[1]))
    role.value     = payload.role
    username.value = payload.sub
  }

  const isAdmin    = computed(() => role.value === 'ADMIN')
  const isOperator = computed(() => ['ADMIN', 'OPERATOR'].includes(role.value))

  return { role, username, isAdmin, isOperator, setFromToken }
})
```

---

## 阶段四：功能完善与优化

### 4.1 ES 时间范围检索 + 分页

**文件**：`backend/src/main/java/com/safeterminal/service/LogIndexService.java`

在 `search()` 方法中完善查询条件：

```java
public Page<LogDocument> search(String terminalId, String keyword,
                                 String startDate, String endDate,
                                 Pageable pageable) {
    NativeQuery query = NativeQuery.builder()
        .withQuery(q -> q.bool(b -> {
            if (terminalId != null)
                b.filter(f -> f.term(t -> t.field("terminalId").value(terminalId)));
            if (keyword != null)
                b.must(m -> m.match(mt -> mt.field("message").query(keyword)));
            if (startDate != null && endDate != null)
                b.filter(f -> f.range(r -> r
                    .field("timestamp")
                    .from(startDate + "T00:00:00Z")
                    .to(endDate   + "T23:59:59Z")));
            return b;
        }))
        .withPageable(pageable)
        .withSort(Sort.by(Sort.Direction.DESC, "timestamp"))
        .build();

    return esOperations.searchForPage(query, LogDocument.class, ...);
}
```

---

### 4.2 USB 事件检索接口

**文件**：`backend/src/main/java/com/safeterminal/controller/EventSearchController.java`

在 `TODO` 注释处添加：

```java
@GetMapping("/usb")
public List<UsbEventDocument> searchUsb(
        @RequestParam(required = false) String terminalId,
        @RequestParam(required = false) String deviceId,
        @RequestParam(required = false) String startDate,
        @RequestParam(required = false) String endDate) {
    // 类似 searchLogs 实现，查询 usb-events-* 索引
}
```

---

### 4.3 仪表盘接入真实数据

**文件**：`frontend/src/views/DashboardView.vue`

将 `eventTrendOption` 和 `alertDistOption` 中的模拟数据替换为 API 调用：

1. 在后端新增统计接口：
   - `GET /v1/stats/event-trend?days=7` → 查 ES 按天聚合
   - `GET /v1/stats/alert-distribution` → 查 MySQL `GROUP BY alert_type`

2. 前端 `onMounted` 中调用并填充 ECharts option

---

## 各模块 TODO 定位速查

以下是代码中所有 `// TODO` 的位置，方便快速定位：

### C++ 探针

| 文件 | 行为 | TODO 内容 |
|------|------|-----------|
| `Config.cpp` | `save_terminal_id()` | 写回 terminal_id 到 TOML 文件 |
| `RingBuffer.cpp` | `flush_to_disk()` | 将未确认消息序列化到 WAL 文件 |
| `RingBuffer.cpp` | `recover_from_disk()` | 启动时读取 WAL 文件重填队列 |
| `RingBuffer.cpp` | `write_to_wal()` | 追加写入 WAL（已提供实现思路） |
| `GrpcClient.cpp` | `connect_and_stream()` | 根据 topic 反序列化并填充 AgentReport |
| `SystemLogCollector.cpp` | `init_windows()` | EvtSubscribe 订阅 |
| `SystemLogCollector.cpp` | `poll_windows_event_log()` | 解析 EventLog XML |
| `SystemLogCollector.cpp` | `init_linux_journald()` | sd_journal_open |
| `SystemLogCollector.cpp` | `poll_journald()` | 读取日志条目 |
| `SystemLogCollector.cpp` | `poll_auditd()` | auditd netlink |
| `SystemLogCollector.cpp` | `init_macos_unified_log()` | OSLogStore |
| `UsbMonitor.cpp` | `poll_windows_usb()` | SetupDi 枚举 USB |
| `UsbMonitor.cpp` | `init_udev()` | udev_monitor_new |
| `UsbMonitor.cpp` | `poll_udev()` | 处理 udev 事件 |
| `UsbMonitor.cpp` | `init_iokit()` | IOKit 通知注册 |
| `HeartbeatReporter.cpp` | `get_cpu_percent()` | Windows PDH / Linux /proc/stat |
| `main.cpp` | `build_identity()` | 填写 hostname/ip_address |

### Go 网关

| 文件 | 位置 | TODO 内容 |
|------|------|-----------|
| `grpc/server.go` | `authenticate()` | JWT 签名验证 |
| `grpc/server.go` | `BuildTLSCredentials()` | 加载 mTLS 证书（已提供实现） |

### Java 后端

| 文件 | 位置 | TODO 内容 |
|------|------|-----------|
| `LogIndexService.java` | `search()` | 时间范围过滤 + 分页 |
| `EventSearchController.java` | 末尾注释 | `/v1/events/usb` 接口 |
| `PolicyService.java` | `getPolicyForTerminal()` | 查询终端绑定策略 |
| 缺失文件 | — | Spring Security JWT（`security/` 包） |
| 缺失文件 | — | `EsIndexNameResolver` Bean |
| 缺失文件 | — | `InfluxDB` 时序查询接口 |

### Vue 前端

| 文件 | 位置 | TODO 内容 |
|------|------|-----------|
| `DashboardView.vue` | 图表数据 | 接入真实统计 API |
| `TerminalListView.vue` | `onlineMap` | 批量查询在线状态填充 |
| `LoginView.vue` | `login()` | 登录成功后解析 token 存 Store |
| 缺失文件 | — | `userStore.ts`（角色权限） |
| 缺失文件 | — | `EventSearchView` USB 事件 Tab |

---

## 开发建议

1. **先在本地跑通一条完整链路**：用一台测试机部署探针（开发模式下跳过 TLS），观察数据从 gRPC → Kafka → ES 的流转。

2. **使用 Kafka UI 辅助调试**：可在 `docker-compose.yml` 中加入 `provectuslabs/kafka-ui` 镜像，可视化查看 topic 消息。

3. **探针开发建议平台顺序**：先做 Linux（libudev 文档完整），再做 Windows（Win API 复杂），macOS 最后（可选）。

4. **TLS 开发阶段可跳过**：在 C++ `GrpcClient.cpp` 改用 `InsecureChannelCredentials`，在 Go `main.go` 注释掉 `grpc.Creds(creds)` 这行，等功能稳定后再加 TLS。

5. **ES 索引模板提前创建**：生产环境建议通过 Kibana 或 API 创建 Index Template，避免自动创建时 mapping 不一致。
