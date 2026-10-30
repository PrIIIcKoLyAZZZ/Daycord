Unicode true

!ifndef APP_VERSION
    !error "APP_VERSION is required."
!endif

!ifndef PUBLISH_DIR
    !error "PUBLISH_DIR is required."
!endif

!ifndef OUTPUT_FILE
    !error "OUTPUT_FILE is required."
!endif

!ifndef UNINSTALL_MANIFEST
    !error "UNINSTALL_MANIFEST is required."
!endif

!include "MUI2.nsh"

!define APP_NAME "Daycord"
!define APP_EXE "Daycord.Desktop.exe"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Daycord"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "${OUTPUT_FILE}"

InstallDir "$PROGRAMFILES64\Daycord"

RequestExecutionLevel admin

SetCompressor /SOLID lzma
SetOverwrite on

ShowInstDetails show
ShowUninstDetails show

BrandingText "Daycord"

VIProductVersion "${APP_VERSION}.0"

VIAddVersionKey "ProductName" "Daycord"
VIAddVersionKey "FileDescription" "Daycord Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}.0"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 Daycord contributors"

!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING

; Do not automatically launch Daycord from elevated setup.

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Russian"

Function .onInit
    SetShellVarContext all
    SetRegView 64
FunctionEnd

Function un.onInit
    SetShellVarContext all
    SetRegView 64
FunctionEnd

Section "Daycord" SecMain

    SectionIn RO

    ; Remove files belonging to the previous installation.

    IfFileExists "$INSTDIR\Uninstall.exe" 0 check_orphan

    DetailPrint "Removing previous Daycord installation..."

    ClearErrors

    ExecWait '"$INSTDIR\Uninstall.exe" /S _?=$INSTDIR' $0

    IfErrors upgrade_failed

    StrCmp $0 "0" previous_removed upgrade_failed

previous_removed:

    IfFileExists "$INSTDIR\${APP_EXE}" upgrade_failed 0

    IfFileExists "$INSTDIR\Uninstall.exe" 0 install_new

    ClearErrors
    Delete "$INSTDIR\Uninstall.exe"

    IfErrors upgrade_failed

    Goto install_new

check_orphan:

    IfFileExists "$INSTDIR\${APP_EXE}" 0 install_new

    MessageBox MB_OK|MB_ICONSTOP \
        "An existing Daycord installation was found without its uninstaller. Setup cannot safely replace it."

    Abort

upgrade_failed:

    MessageBox MB_OK|MB_ICONSTOP \
        "Could not remove the previous Daycord installation. Close Daycord and try again."

    Abort

install_new:

    SetOutPath "$INSTDIR"

    ; Complete self-contained publication.
    File /r "${PUBLISH_DIR}/*"

    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; All-users Start Menu shortcuts.
    CreateDirectory "$SMPROGRAMS\Daycord"

    CreateShortcut \
        "$SMPROGRAMS\Daycord\Daycord.lnk" \
        "$INSTDIR\${APP_EXE}" \
        "" \
        "$INSTDIR\${APP_EXE}"

    CreateShortcut \
        "$SMPROGRAMS\Daycord\Uninstall Daycord.lnk" \
        "$INSTDIR\Uninstall.exe"

    ; Register under HKLM in the 64-bit registry view.
    WriteRegStr HKLM "${UNINSTALL_KEY}" \
        "DisplayName" "Daycord"

    WriteRegStr HKLM "${UNINSTALL_KEY}" \
        "DisplayVersion" "${APP_VERSION}"

    WriteRegStr HKLM "${UNINSTALL_KEY}" \
        "Publisher" "Daycord"

    WriteRegStr HKLM "${UNINSTALL_KEY}" \
        "InstallLocation" "$INSTDIR"

    WriteRegStr HKLM "${UNINSTALL_KEY}" \
        "DisplayIcon" "$INSTDIR\${APP_EXE},0"

    WriteRegStr HKLM "${UNINSTALL_KEY}" \
        "UninstallString" '"$INSTDIR\Uninstall.exe"'

    WriteRegStr HKLM "${UNINSTALL_KEY}" \
        "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'

    WriteRegDWORD HKLM "${UNINSTALL_KEY}" \
        "NoModify" 1

    WriteRegDWORD HKLM "${UNINSTALL_KEY}" \
        "NoRepair" 1

SectionEnd

Section "Uninstall"

    ; Remove only Daycord Start Menu shortcuts.
    Delete "$SMPROGRAMS\Daycord\Daycord.lnk"
    Delete "$SMPROGRAMS\Daycord\Uninstall Daycord.lnk"

    RMDir "$SMPROGRAMS\Daycord"

    ; Files owned by this installed release.
    !include "${UNINSTALL_MANIFEST}"

    Delete "$INSTDIR\Uninstall.exe"

    SetOutPath "$TEMP"

    ; Remove only when empty.
    RMDir "$INSTDIR"

    DeleteRegKey HKLM "${UNINSTALL_KEY}"

    ; User data must always survive uninstall:
    ; %LOCALAPPDATA%\Daycord
    ;
    ; SQLite databases, settings and backups
    ; are not included in the application directory.

SectionEnd