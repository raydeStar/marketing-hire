Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
Name "Thaddeus 2 preview"
OutFile "@OUTPUT@"
InstallDir "$LOCALAPPDATA\Programs\Thaddeus2\preview-@BUILD_ID@"
RequestExecutionLevel user
SetCompressor /SOLID zlib
CRCCheck force
ShowInstDetails show
ShowUninstDetails show
AutoCloseWindow false
AllowRootDirInstall false
VIProductVersion "2.0.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "Thaddeus 2 development preview"
VIAddVersionKey /LANG=1033 "FileDescription" "Per-user Thaddeus setup"
VIAddVersionKey /LANG=1033 "FileVersion" "2.0.0.0"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Private development build"
BrandingText "Thaddeus 2 preview | @BUILD_ID@"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "A study for Thaddeus"
!define MUI_WELCOMEPAGE_TEXT "Install Thaddeus for this Windows account.$\r$\n$\r$\nYour conversations and backups stay outside the application folder. Removing this app will keep that data.$\r$\n$\r$\nAfter installation, connect a model in Settings. Isolated research also needs a supported worker.$\r$\n$\r$\nThis is a development preview."
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE CheckDestination
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION OpenStudy
!define MUI_FINISHPAGE_RUN_TEXT "Open Thaddeus"
!define MUI_FINISHPAGE_TEXT "Thaddeus is installed. Use its Start menu entry to return.$\r$\n$\r$\nIf a study is already running, use its browser window or close it through Settings before starting another copy."
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "Remove this Thaddeus application build. Your study data, backups, other builds and any extra files you added will remain."
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

Var OwnsInstall
Var OwnedDirectory
Var SetupMutex

; The installer owns its application files; the private ledger is not furniture.
@GUARD_FUNCTIONS@

Function .onInit
  SetShellVarContext current
  SetRegView 64
  StrCpy $OwnsInstall 0
  !insertmacro GetNativeMachineArchitecture $1
  IntCmp $1 0x8664 native_x64
    MessageBox MB_OK|MB_ICONSTOP "This preview needs a native Windows x64 computer." /SD IDOK
    SetErrorLevel 2
    Abort
  native_x64:
  Call AcquireSetupMutex
  ReadRegStr $0 HKCU "@REGISTRY@" "InstallLocation"
  StrCmp $0 "" unregistered
    MessageBox MB_OK|MB_ICONSTOP "This build is already registered. Open it from the Start menu, or remove it through Windows Installed apps before installing it again." /SD IDOK
    SetErrorLevel 2
    Abort
  unregistered:
  Push "$SMPROGRAMS\Thaddeus 2\Thaddeus 2 preview @BUILD_ID@.lnk"
  Call AssertPlainPath
  IfFileExists "$SMPROGRAMS\Thaddeus 2\Thaddeus 2 preview @BUILD_ID@.lnk" 0 shortcut_available
    MessageBox MB_OK|MB_ICONSTOP "A Start menu entry already uses this build's name. No existing shortcut will be replaced." /SD IDOK
    SetErrorLevel 2
    Abort
  shortcut_available:
FunctionEnd

Function CheckDestination
  ; Unlike NSIS GetFullPathName, the Windows API accepts a new destination.
  System::Call 'kernel32::GetFullPathNameW(w "$INSTDIR", i 1024, w .r0, p 0) i .r2'
  IntCmp $2 0 destination_invalid destination_invalid destination_length
  destination_length:
  IntCmp $2 1024 destination_invalid destination_normalized destination_invalid
  destination_normalized:
  StrCpy $INSTDIR $0
  Push "$INSTDIR"
  Call AssertPlainPath
  IfFileExists "$INSTDIR" 0 destination_ready
    MessageBox MB_OK|MB_ICONSTOP "Choose a new application folder. Setup will not replace an existing folder or study." /SD IDOK
    SetErrorLevel 2
    Abort
  destination_ready:
  Return
  destination_invalid:
    MessageBox MB_OK|MB_ICONSTOP "Choose an ordinary application path shorter than 1,024 characters." /SD IDOK
    SetErrorLevel 2
    Abort
FunctionEnd

Function OpenStudy
  SetOutPath "$INSTDIR\app"
  ExecShell "open" "$INSTDIR\app\Thaddeus.Host.exe" "--desktop" SW_SHOWMINIMIZED
FunctionEnd

Section "Thaddeus"
  ; Silent installs also pass the destination check.
  Call CheckDestination
  ClearErrors
  CreateDirectory "$INSTDIR"
  IfErrors install_failed
  StrCpy $OwnedDirectory $INSTDIR
  StrCpy $OwnsInstall 1
  SetOutPath "$INSTDIR"
  FileOpen $0 "$INSTDIR\install-owner.txt" w
  IfErrors install_failed
  FileWrite $0 "@MANIFEST_SHA256@"
  FileClose $0
@INSTALL_FILES@
  ClearErrors
  WriteUninstaller "$INSTDIR\Uninstall Thaddeus.exe"
  IfErrors install_failed
  CreateDirectory "$SMPROGRAMS\Thaddeus 2"
  CreateShortcut "$SMPROGRAMS\Thaddeus 2\Thaddeus 2 preview @BUILD_ID@.lnk" "$INSTDIR\app\Thaddeus.Host.exe" "--desktop" "$INSTDIR\app\Thaddeus.Host.exe" 0 SW_SHOWMINIMIZED
  IfErrors install_failed
  WriteRegStr HKCU "@REGISTRY@" "DisplayName" "Thaddeus 2 preview (@BUILD_ID@)"
  WriteRegStr HKCU "@REGISTRY@" "DisplayVersion" "2.0 preview @BUILD_ID@"
  WriteRegStr HKCU "@REGISTRY@" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "@REGISTRY@" "UninstallString" '$\"$INSTDIR\Uninstall Thaddeus.exe$\"'
  WriteRegStr HKCU "@REGISTRY@" "QuietUninstallString" '$\"$INSTDIR\Uninstall Thaddeus.exe$\" /S'
  WriteRegStr HKCU "@REGISTRY@" "DisplayIcon" "$INSTDIR\app\Thaddeus.Host.exe"
  WriteRegDWORD HKCU "@REGISTRY@" "NoModify" 1
  WriteRegDWORD HKCU "@REGISTRY@" "NoRepair" 1
  WriteRegDWORD HKCU "@REGISTRY@" "EstimatedSize" @SIZE_KIB@
  IfErrors install_failed
  SetErrorLevel 0
  Goto installation_done
  install_failed:
    SetErrorLevel 3
    Abort "Installation did not finish. Only this attempt's application files will be removed."
  installation_done:
SectionEnd

Function .onInstFailed
  StrCmp $OwnsInstall 1 0 done
  StrCmp $INSTDIR "" done
  StrCmp $INSTDIR $OwnedDirectory 0 done
  Push "$INSTDIR"
  Call AssertPlainPath
@FAILURE_CLEANUP@
  Delete "$INSTDIR\install-owner.txt"
  Delete "$INSTDIR\Uninstall Thaddeus.exe"
  Delete "$SMPROGRAMS\Thaddeus 2\Thaddeus 2 preview @BUILD_ID@.lnk"
  RMDir "$SMPROGRAMS\Thaddeus 2"
  ReadRegStr $0 HKCU "@REGISTRY@" "InstallLocation"
  StrCmp $0 $OwnedDirectory 0 no_owned_registration
    DeleteRegKey HKCU "@REGISTRY@"
  no_owned_registration:
  SetOutPath "$TEMP"
  RMDir "$INSTDIR"
  done:
FunctionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  Call un.AcquireSetupMutex
  StrCmp $INSTDIR "" wrong_owner
  Push "$INSTDIR"
  Call un.AssertPlainPath
  ReadRegStr $0 HKCU "@REGISTRY@" "InstallLocation"
  StrCmp $0 "$INSTDIR" 0 wrong_owner
  Push "$INSTDIR\install-owner.txt"
  Call un.AssertPlainPath
  ClearErrors
  FileOpen $0 "$INSTDIR\install-owner.txt" r
  IfErrors wrong_owner
  FileRead $0 $1 128
  FileClose $0
  StrCmp $1 "@MANIFEST_SHA256@" owned
  wrong_owner:
    MessageBox MB_OK|MB_ICONSTOP "This uninstaller cannot verify the original application folder. No files were removed." /SD IDOK
    SetErrorLevel 2
    Abort
  owned:
FunctionEnd

Section "Uninstall"
  ; Refuse every linked path and busy payload before removing any installed file.
@UNINSTALL_PREFLIGHT@
  ClearErrors
@UNINSTALL_FILES@
  IfErrors removal_failed
@UNINSTALL_DIRECTORIES@
  ClearErrors
  Delete "$SMPROGRAMS\Thaddeus 2\Thaddeus 2 preview @BUILD_ID@.lnk"
  RMDir "$SMPROGRAMS\Thaddeus 2"
  DeleteRegKey HKCU "@REGISTRY@"
  Delete "$INSTDIR\install-owner.txt"
  Delete "$INSTDIR\Uninstall Thaddeus.exe"
  SetOutPath "$TEMP"
  RMDir "$INSTDIR"
  DetailPrint "The application has left. Your private ledger remains in its study."
  SetErrorLevel 0
  Goto uninstall_done
  removal_failed:
    MessageBox MB_OK|MB_ICONSTOP "Some application files could not be removed. Close this build and try uninstalling again. Your study data was not removed." /SD IDOK
    SetErrorLevel 3
    Abort
  uninstall_done:
SectionEnd

Function un.AssertAvailable
  Exch $1
  Push $0
  IfFileExists "$1" 0 file_available
  System::Call 'kernel32::CreateFileW(w r1, i 0xC0000000, i 0, p 0, i 3, i 0, p 0) p .r0'
  StrCmp $0 -1 file_busy
  System::Call 'kernel32::CloseHandle(p r0)'
  Goto file_available
  file_busy:
    MessageBox MB_OK|MB_ICONSTOP "This application build is running or its files are busy. Close its study through Settings, then try removing it again. No application files were removed." /SD IDOK
    SetErrorLevel 2
    Abort
  file_available:
  Pop $0
  Pop $1
FunctionEnd
