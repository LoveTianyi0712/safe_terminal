; ============================================================
; Safe Terminal Agent - NSIS 安装脚本
; 用法（交互）: makensis installer.nsi
; 用法（静默）: st_agent_setup_1.0.0_x64.exe /S /GATEWAY=192.168.1.1:50051
; ============================================================

!define PRODUCT_NAME    "Safe Terminal Agent"
!define PRODUCT_VERSION "1.0.0"
!define PRODUCT_EXE     "st_agent.exe"
!define SVC_NAME        "SafeTerminalAgent"
!define INST_DIR        "$PROGRAMFILES64\SafeTerminal"
!define DATA_DIR        "$COMMONAPPDATA\SafeTerminal"
!define UNINST_KEY      "Software\Microsoft\Windows\CurrentVersion\Uninstall\${SVC_NAME}"

; ── 现代 UI ──────────────────────────────────────────────────────────────────
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "st_agent_setup_${PRODUCT_VERSION}_x64.exe"
InstallDir "${INST_DIR}"
RequestExecutionLevel admin
ShowInstDetails show
ShowUnInstDetails show

; 命令行参数：/GATEWAY=host:port（静默安装时注入网关地址）
Var GatewayAddr

; ── 页面 ─────────────────────────────────────────────────────────────────────
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\..\packaging\LICENSE.txt"
!define MUI_PAGE_CUSTOMFUNCTION_PRE GatewayPagePre
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "启动 Safe Terminal Agent 控制台"
!define MUI_FINISHPAGE_RUN_FUNCTION RunConsole
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

; ── 组件 ─────────────────────────────────────────────────────────────────────
Section "主程序（必选）" SEC_MAIN
  SectionIn RO
  SetOutPath "${INST_DIR}\bin"
  File "..\..\build\Release\${PRODUCT_EXE}"
  File "packaging\windows\install-service.bat"
  File "packaging\windows\uninstall-service.bat"

  SetOutPath "${INST_DIR}\etc\safe_terminal"
  File "config\config.toml.template"

  ; 如果通过命令行传入了网关地址，替换模板中的占位符
  ${If} $GatewayAddr != ""
    nsExec::ExecToLog 'powershell -NoProfile -Command \
      "(Get-Content \"${INST_DIR}\etc\safe_terminal\config.toml.template\") \
       -replace \"GATEWAY_ADDRESS:50051\",\"$GatewayAddr\" \
       | Set-Content \"${INST_DIR}\etc\safe_terminal\config.toml.template\""'
  ${EndIf}

  ; 写注册表（用于卸载）
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayName"      "${PRODUCT_NAME}"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayVersion"   "${PRODUCT_VERSION}"
  WriteRegStr   HKLM "${UNINST_KEY}" "Publisher"        "SafeTerminal"
  WriteRegStr   HKLM "${UNINST_KEY}" "UninstallString"  '"${INST_DIR}\uninstall.exe"'
  WriteRegStr   HKLM "${UNINST_KEY}" "InstallLocation"  "${INST_DIR}"
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify"         1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair"         1
  WriteUninstaller "${INST_DIR}\uninstall.exe"
SectionEnd

Section "注册为 Windows 服务（推荐）" SEC_SERVICE
  ; 以静默方式运行服务注册脚本
  nsExec::ExecToLog '"${INST_DIR}\bin\install-service.bat"'
SectionEnd

Section "开始菜单快捷方式" SEC_SHORTCUT
  CreateDirectory "$SMPROGRAMS\SafeTerminal"
  CreateShortcut  "$SMPROGRAMS\SafeTerminal\${PRODUCT_NAME}.lnk" \
                  "${INST_DIR}\bin\${PRODUCT_EXE}" \
                  '"${DATA_DIR}\config.toml"'
  CreateShortcut  "$SMPROGRAMS\SafeTerminal\卸载.lnk" \
                  "${INST_DIR}\uninstall.exe"
SectionEnd

; ── 卸载 ─────────────────────────────────────────────────────────────────────
Section "Uninstall"
  ; 停止并删除服务
  nsExec::ExecToLog '"$INSTDIR\bin\uninstall-service.bat"'

  ; 删除安装文件
  Delete "$INSTDIR\bin\${PRODUCT_EXE}"
  Delete "$INSTDIR\bin\install-service.bat"
  Delete "$INSTDIR\bin\uninstall-service.bat"
  Delete "$INSTDIR\etc\safe_terminal\config.toml.template"
  Delete "$INSTDIR\uninstall.exe"
  RMDir  "$INSTDIR\bin"
  RMDir  "$INSTDIR\etc\safe_terminal"
  RMDir  "$INSTDIR\etc"
  RMDir  "$INSTDIR"

  ; 删除快捷方式
  Delete "$SMPROGRAMS\SafeTerminal\${PRODUCT_NAME}.lnk"
  Delete "$SMPROGRAMS\SafeTerminal\卸载.lnk"
  RMDir  "$SMPROGRAMS\SafeTerminal"

  ; 注册表
  DeleteRegKey HKLM "${UNINST_KEY}"

  ; 注意：$COMMONAPPDATA\SafeTerminal（含配置/缓存）保留，避免误删监控数据
  MessageBox MB_ICONINFORMATION "卸载完成。配置与缓存数据保留于 $COMMONAPPDATA\SafeTerminal，如需彻底清除请手动删除。"
SectionEnd

; ── 辅助函数 ─────────────────────────────────────────────────────────────────
Function .onInit
  ; 解析命令行参数 /GATEWAY=
  ${GetParameters} $R0
  ${GetOptions} $R0 "/GATEWAY=" $GatewayAddr
FunctionEnd

Function GatewayPagePre
  ; 静默安装（/S）时跳过 GUI 页面，此处无需额外逻辑
FunctionEnd

Function RunConsole
  Exec '"${INST_DIR}\bin\${PRODUCT_EXE}" "${DATA_DIR}\config.toml"'
FunctionEnd
