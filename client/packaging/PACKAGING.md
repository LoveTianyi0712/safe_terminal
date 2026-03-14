# 客户端探针 — 安装包构建与分发指南

## 目录

- [架构说明](#架构说明)
- [Windows 安装包](#windows-安装包)
- [Linux DEB/RPM 包](#linux-debrpm-包)
- [批量部署](#批量部署)
- [证书预置](#证书预置)

---

## 架构说明

构建完成后，安装包包含以下核心组件：

| 组件 | Windows 路径 | Linux 路径 |
|------|-------------|-----------|
| 可执行文件 | `C:\Program Files\SafeTerminal\bin\st_agent.exe` | `/usr/local/bin/st_agent` |
| 配置文件 | `C:\ProgramData\SafeTerminal\config.toml` | `/etc/safe_terminal/config.toml` |
| 磁盘缓存 | `C:\ProgramData\SafeTerminal\cache\` | `/var/lib/safe_terminal/cache/` |
| 日志文件 | `C:\ProgramData\SafeTerminal\agent.log` | `/var/log/safe_terminal/agent.log` |
| TLS 证书 | `C:\ProgramData\SafeTerminal\certs\` | `/etc/safe_terminal/certs/` |

---

## Windows 安装包

### 前提条件

- [NSIS 3.x](https://nsis.sourceforge.io/) （`makensis` 在 PATH 中）
- Visual Studio 2022 + vcpkg
- CMake 3.20+

### 构建步骤

```powershell
# 基本构建（版本 1.0.0）
.\scripts\build_windows_installer.ps1

# 预置网关地址（企业批量分发时推荐）
.\scripts\build_windows_installer.ps1 -Version 1.0.1 -Gateway "192.168.10.5:50051"
```

输出：`client\dist\st_agent_setup_1.0.0_x64.exe`

### 安装方式

```batch
:: 交互安装
st_agent_setup_1.0.0_x64.exe

:: 静默安装（GPO/SCCM 分发）
st_agent_setup_1.0.0_x64.exe /S /GATEWAY=192.168.10.5:50051

:: 静默安装 + 指定安装目录
st_agent_setup_1.0.0_x64.exe /S /D=D:\SafeTerminal
```

### 服务管理

安装后 `SafeTerminalAgent` 服务自动注册并启动：

```batch
sc query SafeTerminalAgent       :: 查看状态
sc stop  SafeTerminalAgent       :: 停止
sc start SafeTerminalAgent       :: 启动
sc delete SafeTerminalAgent      :: 删除（卸载时自动执行）
```

---

## Linux DEB/RPM 包

### 前提条件

- GCC 11+ 或 Clang 14+
- CMake 3.20+
- `dpkg-dev`（DEB）或 `rpm-build`（RPM）
- gRPC、Protobuf、spdlog、libudev 开发包

```bash
# Ubuntu/Debian/UOS/Kylin
sudo apt install -y cmake build-essential dpkg-dev \
    libgrpc++-dev libprotobuf-dev protobuf-compiler-grpc \
    libspdlog-dev libudev-dev libssl-dev

# RHEL/CentOS/Kylin RPM
sudo yum install -y cmake gcc-c++ rpm-build \
    grpc-devel protobuf-devel spdlog-devel systemd-devel openssl-devel
```

### 构建步骤

```bash
chmod +x scripts/build_linux_package.sh

# 基本构建
./scripts/build_linux_package.sh

# 预置网关地址
./scripts/build_linux_package.sh 1.0.1 192.168.10.5:50051
```

输出：`client/dist/safe-terminal-agent_1.0.0_amd64.deb`

### 安装方式

```bash
# DEB（UOS / Kylin / Ubuntu）
sudo dpkg -i safe-terminal-agent_1.0.0_amd64.deb

# 静默安装（无交互）
sudo DEBIAN_FRONTEND=noninteractive dpkg -i safe-terminal-agent_1.0.0_amd64.deb

# RPM（Kylin RPM / CentOS）
sudo rpm -ivh safe-terminal-agent-1.0.0.x86_64.rpm

# 安装后配置网关地址
sudo nano /etc/safe_terminal/config.toml   # 修改 [gateway] address
sudo systemctl restart st-agent
```

### 服务管理

```bash
systemctl status  st-agent    # 查看状态
systemctl stop    st-agent    # 停止
systemctl start   st-agent    # 启动
systemctl disable st-agent    # 禁用开机自启
journalctl -u st-agent -f     # 实时日志
```

---

## 批量部署

### Ansible（Linux）

```yaml
# playbook: deploy_agent.yml
- hosts: terminals
  become: yes
  vars:
    deb_path: safe-terminal-agent_1.0.0_amd64.deb
    gateway:  192.168.10.5:50051
  tasks:
    - name: Copy DEB package
      copy:
        src:  "{{ deb_path }}"
        dest: /tmp/st_agent.deb

    - name: Install agent
      apt:
        deb: /tmp/st_agent.deb

    - name: Configure gateway
      lineinfile:
        path:   /etc/safe_terminal/config.toml
        regexp: '^address\s*='
        line:   'address = "{{ gateway }}"'

    - name: Restart service
      systemd:
        name:    st-agent
        state:   restarted
        enabled: yes
```

```bash
ansible-playbook -i inventory.ini deploy_agent.yml
```

### SCCM / Intune（Windows）

在 SCCM 应用部署中使用以下命令行：

```
st_agent_setup_1.0.0_x64.exe /S /GATEWAY=192.168.10.5:50051
```

检测方法：`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SafeTerminalAgent` 键存在。

### GPO 软件分发

1. 将 `st_agent_setup_1.0.0_x64.exe` 放在域内共享路径
2. 创建 GPO → 计算机配置 → 软件安装
3. 选择"高级"模式，添加部署选项 `/S /GATEWAY=...`

---

## 证书预置

生产环境启用 mTLS 时，在安装前将证书放入安装包：

```
client/
  packaging/
    certs/          ← 在此放置证书（构建时打包进安装程序）
      ca.crt
      agent.crt
      agent.key
```

然后在 `config.toml.template` 中设置：

```toml
[gateway]
ca_cert   = "/etc/safe_terminal/certs/ca.crt"
cert_file = "/etc/safe_terminal/certs/agent.crt"
key_file  = "/etc/safe_terminal/certs/agent.key"
```

NSIS 脚本和 `postinst` 会自动将证书复制到目标机器的正确位置。
