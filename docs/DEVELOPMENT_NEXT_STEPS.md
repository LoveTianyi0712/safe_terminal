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

## ✅ P1 已完成（2025-03-14）

以下四项 P1 任务已全部实现，C++ 探针现可在 Linux 上真实采集系统日志和 USB 事件，并具备断网缓存能力。

### ✅ P1.1 — Linux journald 日志采集

**涉及文件：** `client/src/probe/SystemLogCollector.h/.cpp`

**实现内容：**
- `SystemLogCollector` 构造函数新增 `PolicyManager* policy_mgr = nullptr` 参数，用于过滤低于策略设定级别的日志
- `init_linux_journald()`：调用 `sd_journal_open(SD_JOURNAL_LOCAL_ONLY)` 打开本机日志，`sd_journal_seek_tail()` + `sd_journal_previous()` 定位到最新位置，**不重放历史日志**
- `poll_journald()`：`sd_journal_wait(200ms)` 阻塞等待新条目 → `sd_journal_next()` 逐条读取 → 提取 `MESSAGE`、`PRIORITY`、`SYSLOG_IDENTIFIER` 三个字段 → 映射 syslog 优先级到 `Severity`（0-2=CRITICAL, 3=ERROR, 4=WARNING, 5-6=INFO, 7=DEBUG 跳过）→ 按策略过滤 → 触发 `callback_`
- 头文件新增 `sd_journal* journal_{nullptr}` 平台成员变量

### ✅ P1.2 — Linux USB 监控（libudev）

**涉及文件：** `client/src/probe/UsbMonitor.h/.cpp`

**实现内容：**
- `init_udev()`：`udev_new()` → `udev_monitor_new_from_netlink("udev")` → `filter_add_match_subsystem_devtype("usb", "usb_device")` → `enable_receiving()`，错误时记录日志并安全退出
- `poll_udev()`：`poll(fd, 200ms)` → `udev_monitor_receive_device()` → 提取 `idVendor`、`idProduct`（组合为 `VID_xxxx&PID_xxxx`）、`serial`、`manufacturer`、`product` → 构造 `UsbEvent` → `udev_device_unref()`
- `cleanup_udev()`：`monitor_loop()` 退出后自动释放 `udev_monitor` 和 `udev` 上下文，防止资源泄漏
- 头文件新增 `cleanup_udev()` 私有方法声明

### ✅ P1.3 — Windows EvtSubscribe 日志采集

**涉及文件：** `client/src/probe/SystemLogCollector.h/.cpp`

**实现内容：**
- `init_windows()`：调用 `EvtSubscribe(L"Security", XPath, EvtSubscribeToFutureEvents, callback)` 订阅安全频道（覆盖登录成功/失败 4624/4625、显式凭据 4648、进程创建 4688 等高价值事件）
- `win_event_callback()`（静态成员函数）：`EvtRender(EvtRenderEventXml)` 获取 XML → `WideCharToMultiByte` 转 UTF-8 → 字符串提取 `EventID`、`Level`、`Channel`、`Data` 字段 → 映射 Windows 级别（1=CRITICAL, 2=ERROR, 3=WARNING, 4=INFO）→ 按策略过滤 → 触发 `callback_`
- `poll_windows_event_log()`：EvtSubscribe 为异步推送模式，此函数仅 `sleep(200ms)` 保持线程存活
- 头文件新增 `EVT_HANDLE subscription_{nullptr}` 和静态回调声明

### ✅ P1.4 — 磁盘 WAL 环形缓冲区

**涉及文件：** `client/src/transport/RingBuffer.cpp`

**WAL 二进制格式（每条记录）：**
```
[total_len: 4B][topic_len: 4B][topic: N B][data_len: 4B][data: M B][sequence: 8B]
```

**实现内容：**
- `write_to_wal()`：追加写入 WAL 文件（`std::ios::app`）；写入前检查文件大小，超过 `disk_max_mb_` 上限则丢弃，防止磁盘打满
- `flush_to_disk()`：持有锁后将 `mem_queue_` 中**所有未确认条目**批量追加写入 WAL；程序关机时自动调用（析构函数）
- `recover_from_disk()`：启动时顺序读取 WAL 文件 → 解析每条记录压入 `mem_queue_` → 更新 `next_seq_` 避免序号冲突 → 恢复完成后将 WAL 重命名为 `.bak`（保留备份）；文件损坏（字段长度越界）时安全截断，不崩溃

**其他改动：**
- `main.cpp`：`SystemLogCollector` 构造时传入 `&policy_mgr`；`build_identity()` 调用 `get_hostname()` / `get_primary_ip()` / `get_os_version()` 填充真实主机信息（跨平台实现）

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
| ~~P1~~ | ✅ 已完成 | ~~C++ Linux USB 监控（libudev）~~ | C++ 探针 | 1 天 |
| ~~P1~~ | ✅ 已完成 | ~~C++ Linux 日志采集（journald）~~ | C++ 探针 | 1 天 |
| ~~P1~~ | ✅ 已完成 | ~~C++ Windows 日志采集（EvtSubscribe）~~ | C++ 探针 | 1.5 天 |
| ~~P1~~ | ✅ 已完成 | ~~C++ 磁盘 WAL 环形缓冲区~~ | C++ 探针 | 1 天 |
| P2 | 待开发 | Go 网关 Token 身份验证 | Go 网关 | 0.5 天 |
| P2 | 待开发 | ES 时间范围检索 + 分页 | Java 后端 | 0.5 天 |
| P2 | 待开发 | 仪表盘图表接入真实数据 | Vue 前端 | 1 天 |
| P3 | 待开发 | USB 事件 ES 检索接口 | Java 后端 | 0.5 天 |
| P3 | 待开发 | 前端登录 + Token 持久化 | Vue 前端 | 0.5 天 |

---

## ✅ 阶段一：打通主干链路（已完成）

> **完成时间**：2025-03-14  
> **目标达成**：前端可正常登录并获取 JWT，后端可启动并写入 ES，网关支持开发/生产双模式。

---

### ✅ 1.1 Java 后端 — Spring Security JWT 登录（已完成）

**实现文件：** `backend/src/main/java/com/safeterminal/security/`

| 文件 | 实现说明 |
|------|---------|
| `JwtTokenProvider.java` | JJWT 0.12.6 生成/解析/校验；Access Token（24h）+ Refresh Token（7d）；Payload 含 `sub`、`role`、`iat`、`exp` |
| `JwtAuthFilter.java` | `OncePerRequestFilter`；从 `Authorization: Bearer <token>` 提取并校验 Token；写入 `SecurityContextHolder` |
| `SecurityConfig.java` | 无状态 Session；公开路径白名单（`/auth/**`、`/ws/**`、`/actuator/health`）；CORS 允许 localhost:3000；401/403 返回 JSON |
| `UserDetailsServiceImpl.java` | 从 `sys_user` 表加载用户；角色加 `ROLE_` 前缀 |
| `JwtProperties.java` | 绑定 `application.yml` `jwt.*` 配置项 |
| `AuthController.java` | `POST /auth/login` → `{token, refreshToken, expiresAt, username, role}`；`POST /auth/refresh` 换新 Token；`GET /auth/me` 返回当前用户 |

`pom.xml` 已包含 `jjwt-api / jjwt-impl / jjwt-jackson 0.12.6`；`application.yml` 中 `jwt.secret` 已配置（**生产环境须替换**）。

---

### ✅ 1.2 Java 后端 — ES 动态索引名 Bean（已完成）

**实现文件：** `backend/src/main/java/com/safeterminal/config/EsIndexNameResolver.java`

- Bean 名 `indexNameResolver` 与 `LogDocument`、`UsbEventDocument` 的 `@Document` SpEL 完全匹配
- `resolveLogIndex()` → `logs-yyyy.MM.dd`；`resolveUsbIndex()` → `usb-events-yyyy.MM.dd`
- 前缀从 `application.yml` 读取，可配置
- **此文件缺失会导致后端启动即报错**，现已修复

---

### ✅ 1.3 Go 网关 — TLS 证书加载（已完成）

**实现文件：** `gateway/internal/grpc/server.go`、`config/config.go`、`config/config.toml`、`main.go`

- `BuildTLSCredentials()`：`tls.LoadX509KeyPair` 加载服务端证书/私钥；`x509.CertPool` 加载 CA；配置 `RequireAndVerifyClientCert`（mTLS）、`MinVersion: TLS 1.2`
- `TLSConfig` 新增 `Enabled bool` 字段，`config.toml` 默认 `enabled = false`
- `main.go` 用 `if cfg.TLS.Enabled` 条件启用：`false` 时明文运行并打印警告，`true` 时加载证书启用 mTLS
- **开发阶段**：保持 `enabled = false`，探针端使用 `InsecureChannelCredentials`，无需证书文件
- **生产部署**：改为 `enabled = true`，按 `DEPLOYMENT.md` "TLS 证书生成"章节部署三个证书文件

---

## ✅ 阶段二：完善探针采集能力（已完成）

> **完成时间**：2025-03-14  
> **目标达成**：探针在 Linux 上可真实采集 journald 日志和 USB 插拔事件；断网时数据持久化到磁盘 WAL，恢复后自动重传；Windows EvtSubscribe 订阅框架已建立。

---

### ✅ 2.1 Linux 日志采集（journald）（已完成）

**实现文件：** `client/src/probe/SystemLogCollector.h/.cpp`

**关键实现：**
- `SystemLogCollector` 构造函数新增 `PolicyManager* policy_mgr = nullptr`，所有平台均支持按策略级别过滤日志
- `init_linux_journald()`：`sd_journal_open(SD_JOURNAL_LOCAL_ONLY)` 打开本机日志；`seek_tail()` + `previous()` 定位到末尾，**不重放历史日志**；失败时记录错误并安全降级
- `poll_journald()`：`sd_journal_wait(200ms)` 非忙等等新条目 → `sd_journal_next()` 逐条遍历 → 提取 `MESSAGE`、`PRIORITY`（映射规则：0-2=CRITICAL, 3=ERROR, 4=WARNING, 5-6=INFO, 7=DEBUG 跳过）、`SYSLOG_IDENTIFIER`（作为 source 字段）→ 按策略过滤 → 触发 `callback_`
- `stop()` 中调用 `sd_journal_close()` 释放资源

**头文件变更：** 新增 `sd_journal* journal_{nullptr}`（Linux 段）和 `PolicyManager* policy_mgr_{nullptr}` 成员

---

### ✅ 2.2 Linux USB 监控（libudev）（已完成）

**实现文件：** `client/src/probe/UsbMonitor.h/.cpp`

**关键实现：**
- `init_udev()`：`udev_new()` 创建上下文 → `udev_monitor_new_from_netlink("udev")` 创建监视器 → `filter_add_match_subsystem_devtype("usb", "usb_device")` 过滤只看 USB 整体设备节点 → `enable_receiving()`；任一步失败均打印错误并安全退出
- `poll_udev()`：`poll(fd, 200ms)` 非忙等 → `udev_monitor_receive_device()` → 提取 `idVendor`、`idProduct`（组合为 `VID_xxxx&PID_xxxx`，与 Windows 格式统一）、`serial`、`manufacturer`、`product` → 构造 `UsbEvent` → `udev_device_unref()`
- `cleanup_udev()`：`monitor_loop()` 退出时调用，`udev_monitor_unref()` + `udev_unref()` 防止资源泄漏

**头文件变更：** 新增 `cleanup_udev()` 私有方法；`void* udev_mon_` 注释说明实际类型

---

### ✅ 2.3 Windows EvtSubscribe 日志采集（已完成）

**实现文件：** `client/src/probe/SystemLogCollector.h/.cpp`

**关键实现：**
- `init_windows()`：`EvtSubscribe(L"Security", XPath, EvtSubscribeToFutureEvents, win_event_callback)` 以异步推送模式订阅；XPath 覆盖 Level ≤ 3 及关键安全 EventID（4624 登录成功、4625 登录失败、4648 显式凭据、4688 进程创建）
- `win_event_callback()`（静态成员函数）：由 Windows 后台线程调用 → `EvtRender(EvtRenderEventXml)` 获取完整 XML → `WideCharToMultiByte` 转 UTF-8 → 字符串函数提取 `EventID`、`Level`（1=CRITICAL, 2=ERROR, 3=WARNING, 4+=INFO）、`Channel`、`Data` → 按 `policy_mgr_` 过滤 → 触发 `callback_`
- `poll_windows_event_log()`：EvtSubscribe 为异步回调模式，此函数仅 `sleep(200ms)` 保持采集线程存活
- `stop()` 中调用 `EvtClose(subscription_)` 清理订阅句柄

**头文件变更：** 新增 `EVT_HANDLE subscription_{nullptr}` 和 `static DWORD WINAPI win_event_callback(...)` 声明

---

### ✅ 2.4 磁盘 WAL 环形缓冲区（已完成）

**实现文件：** `client/src/transport/RingBuffer.cpp`

**WAL 二进制格式（每条记录）：**

```
[total_len: 4B][topic_len: 4B][topic: N B][data_len: 4B][data: M B][sequence: 8B]
```

**关键实现：**

| 方法 | 实现说明 |
|------|---------|
| `write_to_wal()` | 追加写（`std::ios::app`）；写入前检查文件大小，超过 `disk_max_mb_` 上限时丢弃新条目防止磁盘打满 |
| `flush_to_disk()` | 持锁后将 `mem_queue_` 中**所有未确认条目**批量追加写入 WAL；析构函数自动调用，确保关机不丢数据 |
| `recover_from_disk()` | 顺序读取 WAL，解析每条记录压入 `mem_queue_`；更新 `next_seq_` 避免序号冲突；字段长度越界时安全截断；恢复完成后将 WAL 重命名为 `.bak` 保留备份，读取失败时直接删除 |

**附加改动（`main.cpp`）：**
- `SystemLogCollector` 构造时传入 `&policy_mgr`
- `build_identity()` 调用 `get_hostname()` / `get_primary_ip()` / `get_os_version()` 填充真实主机信息（Windows 用 `GetComputerNameA` + `getaddrinfo` + `RtlGetVersion`；Linux/macOS 用 `gethostname` + `getifaddrs` + `uname`）

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
