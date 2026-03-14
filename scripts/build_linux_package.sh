#!/usr/bin/env bash
# ============================================================
# Linux DEB / RPM 打包脚本
# 输出：client/dist/safe-terminal-agent_<version>_amd64.deb
#                   safe-terminal-agent-<version>.x86_64.rpm
#
# 用法：
#   ./build_linux_package.sh [版本] [网关地址]
# 示例：
#   ./build_linux_package.sh 1.0.1 192.168.10.5:50051
# ============================================================
set -euo pipefail

VERSION="${1:-1.0.0}"
GATEWAY="${2:-}"
BUILD_TYPE="Release"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
CLIENT_DIR="$REPO_ROOT/client"
BUILD_DIR="$CLIENT_DIR/build"
DIST_DIR="$CLIENT_DIR/dist"

echo "=== Safe Terminal Agent - Linux Package Build ==="
echo "Version  : $VERSION"
echo "Gateway  : ${GATEWAY:-'(none - set at install time)'}"
echo "BuildType: $BUILD_TYPE"
echo ""

# ── 检查依赖工具 ──────────────────────────────────────────────────────────────
for cmd in cmake make dpkg-deb rpmbuild; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "WARNING: '$cmd' not found. Skipping related package format."
    fi
done

# ── 1. CMake 构建 ─────────────────────────────────────────────────────────────
echo "[1/3] Building with CMake..."
mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"
cmake .. \
    -DCMAKE_BUILD_TYPE="$BUILD_TYPE" \
    -DPROJECT_VERSION="$VERSION" \
    -DCMAKE_INSTALL_PREFIX=/usr/local
cmake --build . --config "$BUILD_TYPE" --parallel "$(nproc)"

# ── 2. 预处理配置模板（注入网关地址） ────────────────────────────────────────
echo "[2/3] Preparing config template..."
TMPL="$CLIENT_DIR/config/config.toml.template"
if [ -n "$GATEWAY" ]; then
    sed -i "s|GATEWAY_ADDRESS:50051|${GATEWAY}|g" "$TMPL"
    echo "  Gateway address set to: $GATEWAY"
fi

# 替换 Linux 磁盘缓存路径（模板已是 Linux 路径，无需修改）

# ── 3. CPack 生成包 ───────────────────────────────────────────────────────────
echo "[3/3] Generating packages via CPack..."

# DEB
if command -v dpkg-deb >/dev/null 2>&1; then
    cpack -G DEB -C "$BUILD_TYPE"
    echo "  DEB package generated."
fi

# RPM（需 rpm-build）
if command -v rpmbuild >/dev/null 2>&1; then
    cpack -G RPM -C "$BUILD_TYPE"
    echo "  RPM package generated."
fi

# ── 复制到 dist/ ─────────────────────────────────────────────────────────────
mkdir -p "$DIST_DIR"
find "$BUILD_DIR" -maxdepth 1 \( -name "*.deb" -o -name "*.rpm" \) -exec cp {} "$DIST_DIR/" \;

echo ""
echo "✓ Packages in $DIST_DIR:"
ls -lh "$DIST_DIR/"*.deb "$DIST_DIR/"*.rpm 2>/dev/null || echo "  (none)"

# ── 提示下一步 ────────────────────────────────────────────────────────────────
cat <<'EOF'

部署方式：
  Debian/Ubuntu/UOS/Kylin (DEB)：
    sudo dpkg -i safe-terminal-agent_*_amd64.deb
    # 或静默安装：
    sudo DEBIAN_FRONTEND=noninteractive dpkg -i safe-terminal-agent_*_amd64.deb

  RHEL/CentOS/Kylin RPM：
    sudo rpm -ivh safe-terminal-agent-*.rpm
    # 或：
    sudo yum localinstall safe-terminal-agent-*.rpm

  安装后修改网关地址：
    sudo nano /etc/safe_terminal/config.toml
    sudo systemctl restart st-agent

  批量部署（Ansible 示例）：
    ansible all -m copy -a "src=safe-terminal-agent_*_amd64.deb dest=/tmp/"
    ansible all -m apt  -a "deb=/tmp/safe-terminal-agent_*_amd64.deb"
EOF
