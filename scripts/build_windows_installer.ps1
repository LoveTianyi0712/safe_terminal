<#
.SYNOPSIS
    Windows 安装包一键构建脚本
    输出：client\dist\st_agent_setup_<version>_x64.exe

.PARAMETER Version
    版本号，默认 1.0.0

.PARAMETER Gateway
    预置网关地址，默认留空（安装时手动填写）

.PARAMETER BuildType
    Release 或 Debug，默认 Release

.EXAMPLE
    .\build_windows_installer.ps1 -Version 1.0.1 -Gateway "192.168.10.5:50051"
#>
param(
    [string]$Version   = "1.0.0",
    [string]$Gateway   = "",
    [string]$BuildType = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot   = Split-Path -Parent $ScriptDir
$ClientDir  = Join-Path $RepoRoot "client"
$BuildDir   = Join-Path $ClientDir "build"
$DistDir    = Join-Path $ClientDir "dist"

Write-Host "=== Safe Terminal Agent - Windows Installer Build ===" -ForegroundColor Cyan
Write-Host "Version  : $Version"
Write-Host "Gateway  : $(if ($Gateway) { $Gateway } else { '(none - set at install time)' })"
Write-Host "BuildType: $BuildType"
Write-Host ""

# ── 1. CMake 构建 ─────────────────────────────────────────────────────────────
Write-Host "[1/3] Building with CMake..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force $BuildDir | Out-Null
Push-Location $BuildDir
try {
    cmake .. `
        -DCMAKE_BUILD_TYPE=$BuildType `
        -DPROJECT_VERSION=$Version `
        -DCMAKE_TOOLCHAIN_FILE="$env:VCPKG_ROOT\scripts\buildsystems\vcpkg.cmake"
    cmake --build . --config $BuildType --parallel
} finally {
    Pop-Location
}

# ── 2. CPack 生成 NSIS 包 ─────────────────────────────────────────────────────
Write-Host "[2/3] Generating NSIS installer via CPack..." -ForegroundColor Yellow
Push-Location $BuildDir
try {
    cpack -G NSIS -C $BuildType
} finally {
    Pop-Location
}

# ── 3. 复制到 dist/ ───────────────────────────────────────────────────────────
Write-Host "[3/3] Copying installer to dist/..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force $DistDir | Out-Null
$installer = Get-ChildItem "$BuildDir\_CPack_Packages\win64\NSIS\*.exe" | Select-Object -First 1
if ($null -eq $installer) {
    # 回退：直接用自定义 NSIS 脚本
    Write-Host "  CPack NSIS not found, falling back to makensis..." -ForegroundColor DarkYellow
    $nsiScript = Join-Path $ClientDir "packaging\windows\installer.nsi"
    if ($Gateway) {
        # 在 NSIS 脚本中注入网关地址（生成临时副本）
        $tmp = [System.IO.Path]::GetTempFileName() + ".nsi"
        (Get-Content $nsiScript) `
            -replace 'GATEWAY_ADDRESS:50051', $Gateway | Set-Content $tmp
        & makensis /V2 "/DPRODUCT_VERSION=$Version" $tmp
        Remove-Item $tmp -ErrorAction SilentlyContinue
    } else {
        & makensis /V2 "/DPRODUCT_VERSION=$Version" $nsiScript
    }
    $installer = Get-ChildItem "$ClientDir\packaging\windows\*.exe" | Select-Object -First 1
}

if ($installer) {
    $dest = Join-Path $DistDir "st_agent_setup_${Version}_x64.exe"
    Copy-Item $installer.FullName $dest -Force
    Write-Host ""
    Write-Host "✓ Installer ready: $dest" -ForegroundColor Green
} else {
    Write-Error "Build failed: installer not found."
}
