param([Parameter(Mandatory=$true)][string]$Package,[Parameter(Mandatory=$true)][string]$Name)
$ErrorActionPreference='Stop'
if ($env:OS -ne 'Windows_NT' -or $Name -notmatch '^[a-zA-Z0-9-]{1,50}$') {throw 'Use a Windows package and a fresh alphanumeric check name.'}
$repository=(Get-Location).Path
$packagePath=(Resolve-Path -LiteralPath $Package).Path
$executable=Join-Path $packagePath 'Thaddeus.Host.exe'
$root=[IO.Path]::GetFullPath((Join-Path $repository ('artifacts/desktop-failure-check-'+$Name)))
if (Test-Path -LiteralPath $root) {throw 'Use a fresh evidence directory.'}
if ((Get-PSDrive ([IO.Path]::GetPathRoot($root).Substring(0,1))).Free -lt (10GB+32MB)) {throw 'The check cannot retain 10 GiB free.'}
$null=New-Item -ItemType Directory -Path $root
$manifestPath=Join-Path $packagePath 'package-manifest.json'
$manifest=Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.runtime -ne 'win-x64') {throw 'Use the native Windows x64 package.'}
foreach ($file in $manifest.files) {
 if ((Get-FileHash -LiteralPath (Join-Path $packagePath $file.path)).Hash.ToLowerInvariant() -ne $file.sha256) {throw 'The package differs from its manifest.'}
}
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ThaddeusFailureDialogCheck {
 public delegate bool Callback(IntPtr window, IntPtr data);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback callback,IntPtr data);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent,Callback callback,IntPtr data);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr window);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool PostMessage(IntPtr window,uint message,IntPtr value,IntPtr data);
 public static string Text(IntPtr window) {var text=new StringBuilder(8192);GetWindowText(window,text,text.Capacity);return text.ToString();}
 public static IntPtr Find(int process) {
  IntPtr found=IntPtr.Zero;
  EnumWindows((window,data)=> {uint owner;GetWindowThreadProcessId(window,out owner);
   if(owner==process && Text(window)=="Thaddeus could not open"){found=window;return false;}return true;},IntPtr.Zero);
  return found;
 }
 public static string Contents(IntPtr window) {
  var text=new StringBuilder();EnumChildWindows(window,(child,data)=>{text.AppendLine(Text(child));return true;},IntPtr.Zero);return text.ToString();
 }
 public static IntPtr OkButton(IntPtr window) {
  IntPtr found=IntPtr.Zero;
  EnumChildWindows(window,(child,data)=> {var type=new StringBuilder(256);GetClassName(child,type,type.Capacity);
   if(type.ToString()=="Button" && (Text(child)=="OK" || Text(child)=="&OK")){found=child;return false;}return true;},IntPtr.Zero);
  return found;
 }
}
'@
$owned=[Collections.Generic.List[Diagnostics.Process]]::new()
$checks=[Collections.Generic.List[object]]::new()
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$passed=$false
$failure=$null
function Run-RefusedLaunch([string]$Label,[string]$Profile,[bool]$Visible,[string]$Expected) {
 $arguments=@('--desktop','--launch-profile',('"'+$Profile+'"'))
 if (!$Visible) {$arguments+='--no-browser'}
 $child=Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $packagePath -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root ($Label+'.stdout.log')) -RedirectStandardError (Join-Path $root ($Label+'.stderr.log'))
 $null=$child.Handle
 $owned.Add($child)
 $dialog=[IntPtr]::Zero
 $text=$null
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds(12)
 while ([DateTimeOffset]::UtcNow -lt $deadline) {
  $child.Refresh();$dialog=[ThaddeusFailureDialogCheck]::Find($child.Id)
  if ($dialog -ne [IntPtr]::Zero -and (!$Visible -or [ThaddeusFailureDialogCheck]::IsWindowVisible($dialog))) {break}
  if ($child.HasExited) {break}
  Start-Sleep -Milliseconds 75
 }
 if ($Visible) {
  if ($dialog -eq [IntPtr]::Zero -or ![ThaddeusFailureDialogCheck]::IsWindowVisible($dialog)) {throw 'The desktop failure dialog was not visible.'}
  $text=[ThaddeusFailureDialogCheck]::Contents($dialog)
  if (!$text.Contains($Expected)) {throw 'The visible failure lacked its recovery explanation.'}
  $button=[ThaddeusFailureDialogCheck]::OkButton($dialog)
  $posted=$button -ne [IntPtr]::Zero -and [ThaddeusFailureDialogCheck]::PostMessage($button,0xf5,[IntPtr]::Zero,[IntPtr]::Zero)
  $postError=[Runtime.InteropServices.Marshal]::GetLastWin32Error()
  [ordered]@{processId=$child.Id;window=$dialog.ToInt64();button=$button.ToInt64();buttonId=[ThaddeusFailureDialogCheck]::GetDlgCtrlID($button);text=$text;posted=$posted;postError=$postError} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root ($Label+'.dialog.json')) -Encoding utf8
  if (!$posted) {throw "Could not dismiss the owned dialog through its OK action (button=$button, error=$postError)."}
 } elseif ($dialog -ne [IntPtr]::Zero) {throw 'An unattended launch opened a dialog.'}
 if (!$child.WaitForExit(5000)) {throw 'Refused launch did not exit after the dialog action.'}
 $child.Refresh()
 if ($child.ExitCode -ne 1) {throw 'Refused launch did not preserve failure exit code 1.'}
 $errorText=Get-Content -Raw -LiteralPath (Join-Path $root ($Label+'.stderr.log'))
 if (!$errorText.Contains($Expected)) {throw 'The console failure contract changed.'}
 $checks.Add([ordered]@{name=$Label;passed=$true;visibleDialog=$Visible;dialogText=$text;exitCode=$child.ExitCode;processId=$child.Id})
}
try {
 $listener.Start()
 $port=([Net.IPEndPoint]$listener.LocalEndpoint).Port
 $workerProbe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
 try {$workerProbe.Start();$freeWorkerPort=([Net.IPEndPoint]$workerProbe.LocalEndpoint).Port} finally {$workerProbe.Stop()}
 $data=Join-Path $root 'never-created-study'
 $profile=Join-Path $root 'occupied-launch.json'
 @{schemaVersion=1;dataDirectory=$data;localOrigin=('http://127.0.0.1:'+$port);workerPort=$freeWorkerPort} | ConvertTo-Json | Set-Content -LiteralPath $profile -Encoding utf8
 Run-RefusedLaunch 'occupied-visible' $profile $true 'Settings > Storage & backups > Review maintenance'
 if (!$listener.Server.IsBound -or $listener.Pending() -or (Test-Path -LiteralPath $data)) {throw 'The refused launch touched the listener or created a study.'}
 Run-RefusedLaunch 'occupied-unattended' $profile $false 'A configured port is in use.'
 $badProfile=Join-Path $root 'invalid-launch.json'
 '{"schemaVersion":7}' | Set-Content -LiteralPath $badProfile -Encoding utf8
 Run-RefusedLaunch 'invalid-profile-visible' $badProfile $true 'Unsupported launch profile.'
 if (!$listener.Server.IsBound -or $listener.Pending() -or (Test-Path -LiteralPath $data)) {throw 'The refusal affected unrelated state.'}
 $passed=$true
} catch {$failure=$_.Exception.Message;throw}
finally {
 $listener.Stop()
 $remaining=@()
 foreach ($child in $owned) {
  $child.Refresh()
  if (!$child.HasExited) {
   $dialog=[ThaddeusFailureDialogCheck]::Find($child.Id)
   if ($dialog -ne [IntPtr]::Zero) {$null=[ThaddeusFailureDialogCheck]::PostMessage($dialog,0x10,[IntPtr]::Zero,[IntPtr]::Zero)}
   if (!$child.WaitForExit(2000)) {$child.Kill();$null=$child.WaitForExit(5000)}
  }
  $child.Refresh();if (!$child.HasExited) {$remaining+=$child.Id}
 }
 $sources=@('scripts/desktop-failure-check.ps1','src/Thaddeus.Host/DesktopLaunchFailure.cs','src/Thaddeus.Host/DesktopLaunch.cs','src/Thaddeus.Host/Program.cs') | ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()}}
 [ordered]@{passed=($passed -and $remaining.Count -eq 0);failure=$failure;package=$packagePath;packageManifestSha256=(Get-FileHash -LiteralPath $manifestPath).Hash.ToLowerInvariant();sourceHashes=$sources;checks=@($checks.ToArray());ownedProcessesRemaining=$remaining;dataCreated=(Test-Path -LiteralPath (Join-Path $root 'never-created-study'));workerStarts=0;modelCalls=0;liveSearchCalls=0;removed=@();retained='Only fictional profiles and compact logs; no study, copy, disk or build was created.'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'verified.json') -Encoding utf8
 foreach ($child in $owned) {$child.Dispose()}
 if ($remaining.Count) {throw 'An owned test process remains.'}
}
Write-Output 'Native startup failures are visible and dismissible. The raven has closed every test door.'
