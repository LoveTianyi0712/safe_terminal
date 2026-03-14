@echo off
:: 注册 SafeTerminalAgent 为 Windows 服务（需要管理员权限）
setlocal

set SVC_NAME=SafeTerminalAgent
set SVC_DISPLAY=Safe Terminal Agent
set SVC_DESC=终端安全监控探针，用于采集系统日志和 USB 接入事件
set BIN_PATH="%ProgramFiles%\SafeTerminal\bin\st_agent.exe"
set CONF_PATH=C:\ProgramData\SafeTerminal\config.toml

:: 创建数据目录
if not exist "C:\ProgramData\SafeTerminal\cache" mkdir "C:\ProgramData\SafeTerminal\cache"
if not exist "C:\ProgramData\SafeTerminal\certs" mkdir "C:\ProgramData\SafeTerminal\certs"
if not exist "C:\ProgramData\SafeTerminal\logs"  mkdir "C:\ProgramData\SafeTerminal\logs"

:: 复制默认配置（仅当不存在时）
if not exist "%CONF_PATH%" (
    copy /Y "%ProgramFiles%\SafeTerminal\etc\safe_terminal\config.toml" "%CONF_PATH%"
    :: 将 Windows 缓存路径替换入配置（使用 PowerShell）
    powershell -NoProfile -Command ^
      "(Get-Content '%CONF_PATH%') -replace '/var/lib/safe_terminal/cache','C:/ProgramData/SafeTerminal/cache' | Set-Content '%CONF_PATH%'"
)

:: 注册服务（若已存在先删除旧的）
sc query %SVC_NAME% >nul 2>&1 && sc delete %SVC_NAME% >nul 2>&1
sc create %SVC_NAME% ^
    binPath= "%ProgramFiles%\SafeTerminal\bin\st_agent.exe \"%CONF_PATH%\"" ^
    DisplayName= "%SVC_DISPLAY%" ^
    start= auto ^
    obj= LocalSystem
sc description %SVC_NAME% "%SVC_DESC%"

:: 配置服务失败自动重启（3 次 5s 内）
sc failure %SVC_NAME% reset= 86400 actions= restart/5000/restart/5000/restart/5000

:: 启动服务
sc start %SVC_NAME%
if %errorlevel% == 0 (
    echo [OK] Service %SVC_NAME% started.
) else (
    echo [WARN] Service registered but failed to start. Check config.toml.
)

endlocal
