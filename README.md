# 🛡️ 终端合规性检查工具 v1.0

> 自动化终端合规检查 · Windows 10/11 · C# .NET 8 WPF · MVVM 架构

---

## 功能概述

| # | 检查项 | 说明 |
|---|--------|------|
| 1 | **终端基本信息** | 计算机名、用户名、IP/MAC、OS 版本、内存、磁盘 |
| 2 | **操作系统正版** | WMI 查询授权状态（含 KMS/未激活检测） |
| 3 | **已安装软件清单** | 枚举注册表中所有已安装软件 |
| 4 | **盗版软件检测** | 基于 `pirate_software_rules.json` 配置规则 |
| 5 | **远程控制软件** | 检测 ToDesk/向日葵/TeamViewer/AnyDesk 等 |
| 6 | **微信敏感文件扫描** | 自动定位微信路径，正则匹配手机号/身份证等 |
| 7 | **全盘敏感文件扫描** | 多线程并行扫描，支持超时限制 |

输出：**HTML 可视化报告**（含折叠、路径复制）+ **JSON 数据文件** + 可选邮件发送。

---

## 快速开始（推荐）

### 前提：安装 .NET 8 SDK

1. 访问 **https://dotnet.microsoft.com/download/dotnet/8.0**
2. 下载 **SDK x64** 安装包并安装
3. 打开新的 PowerShell，执行 `dotnet --version` 确认显示 `8.x.x`

### 构建方式一：一键脚本（自动检测/安装 SDK）

```powershell
# 在 safe_terminal 目录中，右键用管理员 PowerShell 运行：
.\install_and_build.ps1
```

### 构建方式二：手动命令

```powershell
cd "EndpointComplianceChecker"

# 调试运行
dotnet run

# 发布为自包含单文件 exe（无需目标机安装 .NET）
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -o bin\publish
```

输出文件：`EndpointComplianceChecker\bin\publish\EndpointComplianceChecker.exe`

### 构建方式三：Visual Studio 2022

打开 `EndpointComplianceChecker\EndpointComplianceChecker.csproj`，按 **F5** 运行。

---

## 项目结构

```
safe_terminal/
├── install_and_build.ps1          # 一键构建脚本（含 SDK 安装）
├── build.ps1                      # 简单构建脚本
├── README.md
└── EndpointComplianceChecker/
    ├── EndpointComplianceChecker.csproj
    ├── App.xaml / App.xaml.cs     # WPF 应用入口
    ├── MainWindow.xaml / .cs      # 主窗口（MVVM 绑定）
    ├── app.manifest               # 管理员权限声明
    │
    ├── Models/                    # 数据模型
    │   ├── BasicInfo.cs
    │   ├── SoftwareItem.cs
    │   ├── SensitiveFileResult.cs
    │   ├── ComplianceReport.cs
    │   └── ConfigModels.cs        # JSON 配置模型
    │
    ├── Services/                  # 业务逻辑层
    │   ├── ConfigService.cs       # 读取所有配置文件
    │   ├── BasicInfoService.cs    # 采集终端基本信息
    │   ├── SoftwareCheckService.cs# 软件合规检测
    │   ├── WeChatScanService.cs   # 微信敏感文件扫描
    │   ├── FullDiskScanService.cs # 全盘多线程扫描
    │   ├── ReportService.cs       # HTML + JSON 报告生成
    │   └── EmailService.cs        # SMTP 邮件发送
    │
    ├── ViewModels/                # MVVM ViewModel
    │   ├── BaseViewModel.cs       # INotifyPropertyChanged 基类
    │   ├── CheckItemViewModel.cs  # 检查项状态模型
    │   └── MainViewModel.cs       # 主逻辑驱动
    │
    ├── Helpers/
    │   ├── RelayCommand.cs        # ICommand 实现
    │   ├── CryptoHelper.cs        # Windows DPAPI 加解密
    │   └── RegistryHelper.cs      # 注册表操作封装
    │
    ├── Converters/
    │   └── StatusConverters.cs    # WPF 值转换器
    │
    └── config/                    # 外部配置文件（可热修改）
        ├── sensitive_patterns.json
        ├── pirate_software_rules.json
        ├── email_config.json
        ├── scan_settings.json
        └── remote_control_apps.json
```

---

## 配置文件说明

### `config/sensitive_patterns.json` — 敏感数据正则

```json
[
  { "type": "手机号", "regex": "(?<!\\d)1[3-9]\\d{9}(?!\\d)" },
  { "type": "身份证号", "regex": "\\b[1-9]\\d{5}(18|19|20)..." }
]
```
支持任意正则，可自由添加自定义类型。

### `config/pirate_software_rules.json` — 盗版检测规则

```json
[
  {
    "name": "Adobe Photoshop",
    "detection": {
      "type": "registry",
      "path": "HKLM\\SOFTWARE\\Adobe\\Photoshop",
      "pirate_indicator": ["破解", "Crack", "Keygen"]
    }
  }
]
```

### `config/email_config.json` — SMTP 邮件配置

```json
{
  "smtp_server": "smtp.office365.com",
  "port": 587,
  "use_tls": true,
  "username": "sender@company.com",
  "password": "your_password",
  "from": "sender@company.com",
  "to": ["admin@company.com"]
}
```
> 密码首次使用后，工具将自动用 **Windows DPAPI** 加密保存。

### `config/scan_settings.json` — 扫描参数

```json
{
  "full_scan": {
    "exclude_dirs": ["C:\\Windows", "C:\\Program Files"],
    "max_depth": 5,
    "max_file_size_mb": 10,
    "timeout_minutes": 30
  }
}
```

### `config/remote_control_apps.json` — 远控软件名单

可自由添加软件名称关键字（不区分大小写）。

---

## 运行注意事项

1. **建议以管理员权限运行**，确保注册表读取和磁盘扫描完整性
2. **全盘扫描时间** 取决于磁盘文件数量（默认 30 分钟超时，可在配置中调整）
3. **微信扫描** 需要微信已安装且存在存储文件
4. **邮件发送** 需先配置 `email_config.json`，并在 UI 确认收件人后发送
5. 报告默认保存在用户选择的目录，HTML 报告可直接用浏览器打开

---

## 技术栈

| 组件 | 技术 |
|------|------|
| UI 框架 | WPF .NET 8，MVVM 模式 |
| 注册表 | `Microsoft.Win32` |
| WMI 查询 | `System.Management` |
| 正则匹配 | `System.Text.RegularExpressions` |
| 并行扫描 | `Parallel.ForEach` + `CancellationToken` |
| 邮件发送 | `System.Net.Mail.SmtpClient` |
| 密码加密 | `System.Security.Cryptography.ProtectedData` (DPAPI) |
| 配置解析 | `System.Text.Json` |
| 报告生成 | 纯 HTML/CSS/JS，无外部依赖 |

---

**文档版本**：1.0 · **最后更新**：2026-05-21
