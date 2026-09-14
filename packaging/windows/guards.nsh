Function @PREFIX@AcquireSetupMutex
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\Thaddeus.Setup.@BUILD_ID@") p .r0 ?e'
  Pop $1
  StrCmp $0 0 mutex_failed
  IntCmp $1 183 mutex_failed
  StrCpy $SetupMutex $0
  Return
  mutex_failed:
    MessageBox MB_OK|MB_ICONSTOP "Another setup or removal of this build is already running. Wait for it to finish." /SD IDOK
    SetErrorLevel 2
    Abort
FunctionEnd

Function @PREFIX@AssertPlainPath
  Exch $1
  Push $0
  Push $2
  System::Call 'kernel32::GetFullPathNameW(w r1, i 1024, w .r2, p 0) i .r0'
  IntCmp $0 0 invalid_path invalid_path path_length
  path_length:
  IntCmp $0 1024 invalid_path path_normalized invalid_path
  path_normalized:
  StrCpy $1 $2
  path_loop:
    System::Call 'kernel32::GetFileAttributesW(w r1) i .r0'
    IntCmp $0 -1 path_parent
    IntOp $2 $0 & 0x400
    IntCmp $2 0 path_parent
      MessageBox MB_OK|MB_ICONSTOP "An application or shortcut path contains a filesystem link. No linked path will be changed." /SD IDOK
      SetErrorLevel 2
      Abort
    path_parent:
    System::Call 'kernel32::GetFullPathNameW(w "$1\..", i 1024, w .r2, p 0) i .r0'
    IntCmp $0 0 invalid_path invalid_path parent_length
    parent_length:
    IntCmp $0 1024 invalid_path parent_normalized invalid_path
    parent_normalized:
    StrCmp $1 $2 path_done
    StrCpy $1 $2
    Goto path_loop
  path_done:
  Pop $2
  Pop $0
  Pop $1
  Return
  invalid_path:
    MessageBox MB_OK|MB_ICONSTOP "Choose an ordinary local application path shorter than 1,024 characters." /SD IDOK
    SetErrorLevel 2
    Abort
FunctionEnd
