; Custodian 交互式安装包（NSIS 3 / Unicode / 单用户安装，无需管理员）
Unicode true
!include "MUI2.nsh"

!define APP_NAME     "Custodian"
!define APP_PUBLISHER "Dynesshely"

; 版本号可以由命令行覆盖（CI: makensis /DAPP_VERSION=1.2.3 /DAPP_VERSION_QUAD=1.2.3.0）
!ifndef APP_VERSION
!define APP_VERSION "0.2.4"
!endif
!ifndef APP_VERSION_QUAD
!define APP_VERSION_QUAD "${APP_VERSION}.0"
!endif
!define APP_EXE      "Custodian.exe"
!define APP_REGKEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\Custodian"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "..\artifacts\Custodian-${APP_VERSION}-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Custodian"
InstallDirRegKey HKCU "Software\Custodian" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
BrandingText "${APP_NAME} ${APP_VERSION} · ${APP_PUBLISHER}"

VIProductVersion "${APP_VERSION_QUAD}"
VIAddVersionKey /LANG=2052 "ProductName"      "${APP_NAME}"
VIAddVersionKey /LANG=2052 "FileDescription"  "${APP_NAME} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion"      "${APP_VERSION_QUAD}"
VIAddVersionKey /LANG=2052 "ProductVersion"   "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "CompanyName"      "${APP_PUBLISHER}"
VIAddVersionKey /LANG=2052 "LegalCopyright"   "© 2026 ${APP_PUBLISHER}"

!define MUI_ICON   "..\assets\custodian.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "assets\welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "assets\welcome.bmp"
!define MUI_UNICON "..\assets\custodian.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "${APP_NAME} ${APP_VERSION}"
!define MUI_WELCOMEPAGE_TEXT "Sandboxie-Plus 的进阶管理工具。$\r$\n列出沙盒里的应用，一键在沙盒中启动。$\r$\n$\r$\n安装到当前用户目录，无需管理员权限。$\r$\n需要 Windows 11 22H2 或更高版本。"
!define MUI_FINISHPAGE_RUN_TEXT "立即启动 ${APP_NAME}"
!define MUI_FINISHPAGE_NOREBOOTSUPPORT

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

; 安装/卸载前自动关闭正在运行的 Custodian（先礼貌关闭，再强制兜底）
!macro CloseAppCmd
    nsExec::Exec '"$SYSDIR\taskkill.exe" /IM ${APP_EXE}'
    Pop $0
    Sleep 900
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE}'
    Pop $0
    Sleep 300
!macroend

Function CloseRunningApp
    !insertmacro CloseAppCmd
FunctionEnd

Function un.CloseRunningApp
    !insertmacro CloseAppCmd
FunctionEnd

Function .onInit
    Call CloseRunningApp
FunctionEnd

Function un.onInit
    Call un.CloseRunningApp
FunctionEnd

Section "Custodian 主程序（必需）" SecMain
    SectionIn RO
    Call CloseRunningApp
    SetOutPath "$INSTDIR"
    File "..\artifacts\publish\${APP_EXE}"

    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0

    WriteRegStr HKCU "Software\Custodian" "InstallDir" "$INSTDIR"
    WriteRegStr HKCU "${APP_REGKEY}" "DisplayName"     "${APP_NAME}"
    WriteRegStr HKCU "${APP_REGKEY}" "DisplayVersion"  "${APP_VERSION}"
    WriteRegStr HKCU "${APP_REGKEY}" "Publisher"       "${APP_PUBLISHER}"
    WriteRegStr HKCU "${APP_REGKEY}" "DisplayIcon"     "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "${APP_REGKEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${APP_REGKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKCU "${APP_REGKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegDWORD HKCU "${APP_REGKEY}" "NoModify" 1
    WriteRegDWORD HKCU "${APP_REGKEY}" "NoRepair" 1
    WriteRegDWORD HKCU "${APP_REGKEY}" "EstimatedSize" 64000
SectionEnd

Section "桌面快捷方式" SecDesktop
    ; 静默安装（/S）不创建桌面快捷方式，避免自动化安装污染桌面
    IfSilent desktopDone
    CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
    desktopDone:
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SecMain}    "程序本体（约 62 MB，自包含运行时，无需另装 .NET）。"
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "在桌面放一个快捷方式。"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"

    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk"
    RMDir "$SMPROGRAMS\${APP_NAME}"

    Delete "$DESKTOP\${APP_NAME}.lnk"

    DeleteRegKey HKCU "${APP_REGKEY}"
    DeleteRegKey HKCU "Software\Custodian"

    ; 用户设置（%LOCALAPPDATA%\Custodian）保留，避免误删个人配置
SectionEnd