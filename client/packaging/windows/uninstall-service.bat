@echo off
:: 停止并注销 SafeTerminalAgent 服务（卸载时调用）
setlocal

set SVC_NAME=SafeTerminalAgent

sc query %SVC_NAME% >nul 2>&1
if %errorlevel% == 0 (
    sc stop   %SVC_NAME% >nul 2>&1
    sc delete %SVC_NAME%
    echo [OK] Service %SVC_NAME% removed.
) else (
    echo [INFO] Service %SVC_NAME% not found, nothing to remove.
)

endlocal
