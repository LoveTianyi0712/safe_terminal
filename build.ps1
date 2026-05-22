# ====================================================================
# EndpointComplianceChecker - Quick Build Script
# Usage: powershell -ExecutionPolicy Bypass -File .\build.ps1
# ====================================================================

$ErrorActionPreference = "Stop"
$dotnet      = "C:\Program Files\dotnet\dotnet.exe"
$ProjectDir  = Join-Path $PSScriptRoot "EndpointComplianceChecker"
$ProjectFile = Join-Path $ProjectDir "EndpointComplianceChecker.csproj"
$BuildOut    = Join-Path $ProjectDir "bin\Release\net8.0-windows"
$PublishOut  = Join-Path $ProjectDir "bin\publish"

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "  EndpointComplianceChecker - Build Script" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------------
# 1. Kill any running instances so the exe is not locked
# ------------------------------------------------------------------
$running = Get-Process -Name "EndpointComplianceChecker" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[INFO] Stopping running instances..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    Write-Host "[OK] Instances stopped." -ForegroundColor Green
}

# ------------------------------------------------------------------
# 2. Verify dotnet
# ------------------------------------------------------------------
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"   # fall back to PATH
}
$ver = & $dotnet --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] dotnet not found. Install .NET 8 SDK first:" -ForegroundColor Red
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    pause; exit 1
}
Write-Host "[OK] dotnet $ver" -ForegroundColor Green

# ------------------------------------------------------------------
# 3. Choose action
# ------------------------------------------------------------------
Write-Host ""
Write-Host "Choose build type:" -ForegroundColor White
Write-Host "  [1] Debug build only (fast, for testing)"
Write-Host "  [2] Release build only"
Write-Host "  [3] Release + Publish self-contained single exe (slow first time)"
Write-Host ""
$choice = Read-Host "Enter 1 / 2 / 3 (default: 2)"
if ($choice -eq "") { $choice = "2" }

# ------------------------------------------------------------------
# 4. Build
# ------------------------------------------------------------------
if ($choice -eq "1") {
    Write-Host ""
    Write-Host "[1/1] Building Debug..." -ForegroundColor Cyan
    & $dotnet build $ProjectFile -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { Write-Host "[ERROR] Build failed." -ForegroundColor Red; pause; exit 1 }
    Write-Host ""
    Write-Host "[OK] Debug build complete: $BuildOut" -ForegroundColor Green
}
elseif ($choice -eq "2") {
    Write-Host ""
    Write-Host "[1/1] Building Release..." -ForegroundColor Cyan
    & $dotnet build $ProjectFile -c Release --nologo
    if ($LASTEXITCODE -ne 0) { Write-Host "[ERROR] Build failed." -ForegroundColor Red; pause; exit 1 }
    $exe = Join-Path $BuildOut "EndpointComplianceChecker.exe"
    Write-Host ""
    Write-Host "[OK] Release build complete:" -ForegroundColor Green
    Write-Host "     $exe" -ForegroundColor White
}
elseif ($choice -eq "3") {
    Write-Host ""
    Write-Host "[1/2] Building Release..." -ForegroundColor Cyan
    & $dotnet build $ProjectFile -c Release --nologo
    if ($LASTEXITCODE -ne 0) { Write-Host "[ERROR] Build failed." -ForegroundColor Red; pause; exit 1 }

    Write-Host ""
    Write-Host "[2/2] Publishing self-contained single-file exe (win-x64)..." -ForegroundColor Cyan
    $publishArgs = @(
        "publish", $ProjectFile,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=embedded",
        "--nologo",
        "-o", $PublishOut
    )
    & $dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { Write-Host "[ERROR] Publish failed." -ForegroundColor Red; pause; exit 1 }

    $exe = Join-Path $PublishOut "EndpointComplianceChecker.exe"
    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host "  Publish complete!" -ForegroundColor Green
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host "  Output: $exe" -ForegroundColor White
    Write-Host "  (Run as Administrator for full registry/disk access)" -ForegroundColor Yellow
}
else {
    Write-Host "[WARN] Invalid choice, exiting." -ForegroundColor Yellow
    pause; exit 1
}

Write-Host ""
$run = Read-Host "Run the program now? (y/N)"
if ($run -eq 'y' -or $run -eq 'Y') {
    $exeToRun = if ($choice -eq "3") {
        Join-Path $PublishOut "EndpointComplianceChecker.exe"
    } else {
        Join-Path $BuildOut "EndpointComplianceChecker.exe"
    }
    if (Test-Path $exeToRun) {
        Start-Process -FilePath $exeToRun -Verb RunAs
    } else {
        Write-Host "[WARN] Exe not found at: $exeToRun" -ForegroundColor Yellow
    }
}

pause
