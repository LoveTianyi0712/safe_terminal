# ====================================================================
# Terminal Compliance Checker - One-click Build Script
# Run as administrator to auto-install .NET 8 SDK and compile
# ====================================================================
#Requires -Version 5.1

param(
    [switch]$PublishOnly,
    [switch]$SkipSdkCheck
)

$ErrorActionPreference = "Stop"
$ProjectDir  = Join-Path $PSScriptRoot "EndpointComplianceChecker"
$ProjectFile = Join-Path $ProjectDir   "EndpointComplianceChecker.csproj"
$OutputDir   = Join-Path $ProjectDir   "bin\publish"

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "  Terminal Compliance Checker - Build Script" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------------
# 1. Detect dotnet
# ------------------------------------------------------------------
function Get-DotnetPath {
    $candidates = @(
        "dotnet",
        "C:\Program Files\dotnet\dotnet.exe",
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    )
    foreach ($c in $candidates) {
        try {
            $null = & $c --version 2>&1
            if ($LASTEXITCODE -eq 0) { return $c }
        } catch {}
    }
    return $null
}

if (-not $SkipSdkCheck) {
    $dotnet = Get-DotnetPath
    if ($null -eq $dotnet) {
        Write-Host "[WARN] .NET SDK not found. Downloading installer..." -ForegroundColor Yellow
        Write-Host ""

        $installer = Join-Path $env:TEMP "dotnet-sdk-installer.exe"
        $sdkUrl = "https://download.microsoft.com/download/dotnet/8.0/sdk/win-x64/dotnet-sdk-8.0.400-win-x64.exe"

        Write-Host "Downloading .NET 8 SDK from: $sdkUrl" -ForegroundColor Gray
        try {
            Invoke-WebRequest -Uri $sdkUrl -OutFile $installer -UseBasicParsing
        } catch {
            Write-Host ""
            Write-Host "[ERROR] Download failed. Please install .NET 8 SDK manually:" -ForegroundColor Red
            Write-Host "   https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
            Write-Host ""
            Write-Host "After installation, re-run this script." -ForegroundColor Yellow
            pause; exit 1
        }

        Write-Host "Installing .NET 8 SDK (this may take a few minutes)..." -ForegroundColor Yellow
        Start-Process -FilePath $installer -ArgumentList "/quiet /norestart" -Wait
        Remove-Item $installer -ErrorAction SilentlyContinue

        # Refresh PATH
        $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" +
                    [System.Environment]::GetEnvironmentVariable("PATH", "User")
        $env:PATH += ";C:\Program Files\dotnet"

        $dotnet = Get-DotnetPath
        if ($null -eq $dotnet) {
            Write-Host "[ERROR] dotnet still not found after installation." -ForegroundColor Red
            Write-Host "Please reopen PowerShell and run this script again." -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Or run manually:" -ForegroundColor White
            Write-Host "  dotnet publish `"$ProjectFile`" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o `"$OutputDir`"" -ForegroundColor White
            pause; exit 1
        }
    }

    $version = & $dotnet --version 2>&1
    Write-Host "[OK] .NET SDK version: $version" -ForegroundColor Green
} else {
    $dotnet = "dotnet"
}

# ------------------------------------------------------------------
# 2. Restore NuGet packages
# ------------------------------------------------------------------
Write-Host ""
Write-Host "[1/2] Restoring NuGet packages..." -ForegroundColor Cyan
& $dotnet restore $ProjectFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] NuGet restore failed." -ForegroundColor Red
    pause; exit 1
}

# ------------------------------------------------------------------
# 3. Publish (self-contained single-file exe)
# ------------------------------------------------------------------
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
    "-o", $OutputDir
)

& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Publish failed. See errors above." -ForegroundColor Red
    pause; exit 1
}

# ------------------------------------------------------------------
# 4. Done
# ------------------------------------------------------------------
$exePath = Join-Path $OutputDir "EndpointComplianceChecker.exe"
Write-Host ""
Write-Host "=====================================================" -ForegroundColor Green
Write-Host "  Build successful!" -ForegroundColor Green
Write-Host "=====================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Output: $exePath" -ForegroundColor White
Write-Host ""
Write-Host "  Double-click to run (recommend running as Administrator)" -ForegroundColor Yellow
Write-Host ""

$open = Read-Host "Run the program now? (y/N)"
if ($open -eq 'y' -or $open -eq 'Y') {
    Start-Process -FilePath $exePath -Verb RunAs
}

pause
