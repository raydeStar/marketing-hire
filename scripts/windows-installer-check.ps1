param([Parameter(Mandatory=$true)][string]$Publication, [Parameter(Mandatory=$true)][string]$Name)
$ErrorActionPreference = 'Stop'
if (![Environment]::Is64BitProcess -or $env:OS -ne 'Windows_NT') { throw 'Run the installer check in native Windows x64 PowerShell.' }
if ($Name -notmatch '^[A-Za-z0-9-]{1,60}$') { throw 'Choose a fresh evidence name.' }
$repository = Split-Path $PSScriptRoot -Parent
$artifacts = (Resolve-Path -LiteralPath (Join-Path $repository 'artifacts')).Path
$publicationRoot = [IO.Path]::GetFullPath($Publication)
if (!$publicationRoot.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a publication inside repository artifacts.' }
$publishedPath = Join-Path $publicationRoot 'published.json'
$published = Get-Content -LiteralPath $publishedPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($published.kind -ne 'windows-host-installer-preview' -or $published.runtime -ne 'win-x64' -or $published.buildId -notmatch '^[a-f0-9]{16}$' -or !$published.unsigned -or $published.includesWorker) { throw 'Use a host-only Windows installer preview.' }
$expectedRegistry = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Thaddeus2.Preview.' + $published.buildId
if ($published.registryKey -ne $expectedRegistry) { throw 'Unexpected installer registration scope.' }
$setup = Join-Path $publicationRoot ('Thaddeus-2-preview-' + $published.buildId + '.exe')
if ($published.installer -ne $setup -or (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $published.sha256) { throw 'Installer differs from its receipt.' }
$manifestFile = Join-Path $published.hostPackage 'package-manifest.json'
if ((Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $published.hostManifestSha256) { throw 'Host manifest differs from the installer input.' }
$manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding utf8 | ConvertFrom-Json
$payloadBytes = ($manifest.files | Measure-Object size -Sum).Sum
if ((Get-PSDrive C).Free -lt (10GB + 2*$payloadBytes + 128MB)) { throw 'Insufficient space for one installed copy, fixture data and the 10 GiB reserve.' }
$root = Join-Path $artifacts ('windows-installer-check-' + $Name)
if (Test-Path -LiteralPath $root) { throw 'This evidence directory already exists.' }
$registry = 'HKCU:\' + $expectedRegistry
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ('Thaddeus 2\Thaddeus 2 preview ' + $published.buildId + '.lnk')
if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $shortcut)) { throw 'This build already has a user registration or shortcut; no installer test was started.' }
New-Item -ItemType Directory -Path $root | Out-Null
$installed = Join-Path $root 'installed'
$study = Join-Path $root 'study'
$existing = Join-Path $root 'existing'
$foreign = Join-Path $root 'foreign'
$held = Join-Path $root 'held-wwwroot'
$checks = [Collections.Generic.List[string]]::new()
$operations = [Collections.Generic.List[object]]::new()
$processes = [Collections.Generic.List[object]]::new()
$ownedHost = $null
$failure = $null
$removed = @()

function Assert-That([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Assert-Child([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture path escaped its recorded root.' }
    return $full
}
function Fingerprint([string]$Directory, [string]$Base = '') {
    if (!$Base) { $Base = $Directory }
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked fingerprint input.' }
        if ($entry.PSIsContainer) { Fingerprint $entry.FullName $Base }
        else { [pscustomobject]@{path=$entry.FullName.Substring($Base.Length+1).Replace('\','/');size=$entry.Length;sha256=(Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} }
    }
}
function Assert-App {
    $actual = @(Fingerprint (Join-Path $installed 'app') | Sort-Object path)
    $expected = @($manifest.files) + @([pscustomobject]@{path='package-manifest.json';size=(Get-Item -LiteralPath $manifestFile).Length;sha256=$published.hostManifestSha256})
    $expected = @($expected | Sort-Object path)
    Assert-That ($actual.Count -eq $expected.Count) 'Installed app inventory differs.'
    for ($i=0; $i -lt $actual.Count; $i++) {
        Assert-That ($actual[$i].path -ceq $expected[$i].path -and $actual[$i].size -eq $expected[$i].size -and $actual[$i].sha256 -eq $expected[$i].sha256) ('Installed bytes differ: ' + $expected[$i].path)
    }
}
function Invoke-Setup([string]$Executable, [string]$Arguments, [string]$Label) {
    $started = [DateTime]::UtcNow
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    $processes.Add($process)
    $operation = [ordered]@{label=$Label;processId=$process.Id;startTicks=$process.StartTime.ToUniversalTime().Ticks.ToString();executable=$Executable;arguments=$Arguments;children=@()}
    $operations.Add($operation)
    if (!$process.WaitForExit(30000)) { throw 'An owned installer process is still running; its fixture must be retained.' }
    $process.Refresh()
    $operation.exitCode = $process.ExitCode
    # Normal NSIS uninstall copies itself into TEMP. Observe that child before cleanup.
    $children = @(Get-CimInstance Win32_Process -Filter ('ParentProcessId=' + $process.Id) | Where-Object { $_.CreationDate.ToUniversalTime() -ge $started.AddSeconds(-1) })
    foreach ($child in $children) {
        Assert-That ($child.CommandLine -and $child.CommandLine.Contains($installed)) 'Unexpected setup child; retain the fixture for inspection.'
        try { $childProcess = [Diagnostics.Process]::GetProcessById($child.ProcessId) } catch [ArgumentException] { continue }
        $processes.Add($childProcess)
        $operation.children += @{processId=$childProcess.Id;startTicks=$childProcess.StartTime.ToUniversalTime().Ticks.ToString()}
        if (!$childProcess.WaitForExit(30000)) { throw 'The owned uninstaller child is still running; retain its fixture.' }
    }
    return $operation.exitCode
}
function Free-Port {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
try {
    Copy-Item -LiteralPath $publishedPath -Destination (Join-Path $root 'tested-publication.json')
    New-Item -ItemType Directory -Path $existing,$foreign | Out-Null
    [IO.File]::WriteAllText((Join-Path $existing 'keep.txt'),'Existing folder belongs to someone else.')
    [IO.File]::WriteAllText((Join-Path $foreign 'keep.txt'),'A linked directory is not installer property.')
    $guardBefore = @(Fingerprint $existing) | ConvertTo-Json -Compress
    $result = Invoke-Setup $setup ('/S /D=' + $existing) 'refuse-existing-destination'
    Assert-That ($result -ne 0) 'An existing directory was not refused.'
    Assert-That ((@(Fingerprint $existing) | ConvertTo-Json -Compress) -eq $guardBefore) 'Existing files changed.'
    Assert-That (!(Test-Path -LiteralPath $registry) -and !(Test-Path -LiteralPath $shortcut)) 'A refused install registered itself.'
    $checks.Add('Existing destination refused without changing its sentinel, registry or Start menu.')
    # Let the installer create a directory but deny file writes in this owned fixture.
    # This exercises real post-creation failure cleanup without filling the drive.
    $denied = Assert-Child (Join-Path $root 'denied')
    New-Item -ItemType Directory -Path $denied | Out-Null
    [IO.File]::WriteAllText((Join-Path $denied 'keep.txt'),'Parent files survive a failed installation.')
    $originalAcl = Get-Acl -LiteralPath $denied
    $deniedAcl = Get-Acl -LiteralPath $denied
    $account = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($account,[Security.AccessControl.FileSystemRights]::WriteData,$inheritance,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny)
    $deniedAcl.AddAccessRule($rule)
    try {
        Set-Acl -LiteralPath $denied -AclObject $deniedAcl
        $failedDestination = Join-Path $denied 'new-application'
        $result = Invoke-Setup $setup ('/S /D=' + $failedDestination) 'write-failure-cleanup'
        Assert-That ($result -eq 3) 'Expected an installation write failure after taking ownership.'
        Assert-That (!(Test-Path -LiteralPath $failedDestination)) 'Failed installation left its application directory.'
        Assert-That ([IO.File]::ReadAllText((Join-Path $denied 'keep.txt')) -eq 'Parent files survive a failed installation.') 'Failure cleanup changed its parent sentinel.'
        Assert-That (!(Test-Path -LiteralPath $registry) -and !(Test-Path -LiteralPath $shortcut)) 'Failed installation left registration or a shortcut.'
    } finally { Set-Acl -LiteralPath $denied -AclObject $originalAcl }
    $checks.Add('A real NTFS write denial after directory creation cleans the failed install and preserves its parent; original fixture permissions are restored.')
    $result = Invoke-Setup $setup ('/S /D=' + $installed) 'install'
    Assert-That ($result -eq 0) 'Installer returned a failure.'
    Assert-App
    $registration = Get-ItemProperty -LiteralPath $registry
    Assert-That ($registration.InstallLocation -eq $installed) 'Installed app registration has the wrong location.'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    Assert-That ($link.TargetPath -eq (Join-Path $installed 'app/Thaddeus.Host.exe')) 'Shortcut points outside the installed application.'
    Assert-That ($link.Arguments -eq '--desktop') 'Shortcut has unexpected arguments.'
    $link = $null
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    $checks.Add('Per-user installation has exact package bytes, a Start menu entry and Windows uninstall registration.')
    $result = Invoke-Setup $setup ('/S /D=' + (Join-Path $root 'second-install')) 'refuse-duplicate-build'
    Assert-That ($result -ne 0 -and !(Test-Path -LiteralPath (Join-Path $root 'second-install'))) 'Duplicate build registration was not refused.'
    Assert-App
    $checks.Add('Installing the same build again cannot overwrite or create another copy.')
    $uninstaller = Join-Path $installed 'Uninstall Thaddeus.exe'
    $wwwroot = Assert-Child (Join-Path $installed 'app/wwwroot')
    [void](Assert-Child $held)
    Move-Item -LiteralPath $wwwroot -Destination $held
    New-Item -ItemType Junction -Path $wwwroot -Target $foreign | Out-Null
    try {
        $result = Invoke-Setup $uninstaller ('/S _?=' + $installed) 'refuse-linked-payload'
        Assert-That ($result -ne 0) 'Linked app payload was not refused.'
        Assert-That ([IO.File]::ReadAllText((Join-Path $foreign 'keep.txt')) -eq 'A linked directory is not installer property.') 'Linked target changed.'
    } finally {
        if ((Get-Item -LiteralPath $wwwroot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete($wwwroot) }
        else { throw 'The fixture junction changed unexpectedly.' }
        Move-Item -LiteralPath $held -Destination $wwwroot
    }
    Assert-App
    $checks.Add('A real Windows junction is refused before any payload deletion; its target is preserved.')
    $uiPort = Free-Port
    do { $workerPort = Free-Port } while ($workerPort -eq $uiPort)
    $origin = 'http://localhost:' + $uiPort
    $profilePath = Join-Path $root 'fixture-launch.json'
    @{schemaVersion=1;dataDirectory=$study;localOrigin=$origin;workerPort=$workerPort} | ConvertTo-Json | Set-Content -LiteralPath $profilePath -Encoding utf8
    $hostExe = Join-Path $installed 'app/Thaddeus.Host.exe'
    $hostArguments = '--desktop --no-browser --launch-profile "' + $profilePath + '"'
    $ownedHost = Start-Process -FilePath $hostExe -ArgumentList $hostArguments -WorkingDirectory (Join-Path $installed 'app') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'host.stdout.log') -RedirectStandardError (Join-Path $root 'host.stderr.log')
    $processes.Add($ownedHost)
    $hostIdentity = @{processId=$ownedHost.Id;startTicks=$ownedHost.StartTime.ToUniversalTime().Ticks.ToString();path=$hostExe;origin=$origin}
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $ready = $false
    do {
        Assert-That (!$ownedHost.HasExited) 'The installed host exited before readiness.'
        try {
            $response = Invoke-WebRequest $origin -UseBasicParsing -TimeoutSec 2
            $ready = $response.StatusCode -eq 200 -and [Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray()) -ceq [IO.File]::ReadAllText((Join-Path $installed 'app/wwwroot/index.html'))
        } catch { }
        if (!$ready) { Start-Sleep -Milliseconds 150 }
    } while (!$ready -and $watch.Elapsed.TotalSeconds -lt 30)
    Assert-That $ready 'Installed host did not serve its exact page.'
    $second = Start-Process -FilePath $hostExe -ArgumentList $hostArguments -WorkingDirectory (Join-Path $installed 'app') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'reopen.stdout.log') -RedirectStandardError (Join-Path $root 'reopen.stderr.log')
    $processes.Add($second)
    $secondHandle = $second.Handle
    Assert-That ($second.WaitForExit(10000)) 'Second installed launch did not finish.'
    $second.Refresh()
    @{processId=$second.Id;exitCode=$second.ExitCode;handleCaptured=($secondHandle -ne [IntPtr]::Zero)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'reopen-process.json')
    Assert-That ($second.ExitCode -eq 0) 'Second installed launch failed to reuse the running study.'
    Assert-That (!$ownedHost.HasExited -and $ownedHost.StartTime.ToUniversalTime().Ticks.ToString() -eq $hostIdentity.startTicks) 'Reopening replaced the original host.'
    Assert-That ((Get-Content -LiteralPath (Join-Path $root 'reopen.stdout.log') -Raw).Contains('no second host was started')) 'Installed launcher did not report reuse.'
    $checks.Add('Launching the installed desktop entry twice reuses the original study process and succeeds without another host.')
    [IO.File]::WriteAllText((Join-Path $study 'keep-my-study.txt'),'A fictional study survives application removal.')
    $result = Invoke-Setup $uninstaller ('/S _?=' + $installed) 'refuse-running-application'
    Assert-That ($result -ne 0 -and !$ownedHost.HasExited) 'Uninstall did not refuse the running app.'
    Assert-App
    $checks.Add('Actual installed host starts; uninstall refuses it without stopping the process or deleting payload files.')
    Stop-Process -InputObject $ownedHost
    Assert-That ($ownedHost.WaitForExit(10000)) 'Owned host did not exit.'
    $studyBefore = @(Fingerprint $study | Sort-Object path)
    $studyBeforeJson = $studyBefore | ConvertTo-Json -Compress
    $studyBefore | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'study-before-uninstall.json') -Encoding utf8
    [IO.File]::WriteAllText((Join-Path $installed 'app/my-extra-note.txt'),'Extra files are kept.')
    $result = Invoke-Setup $uninstaller '/S' 'normal-uninstall'
    Assert-That ($result -eq 0) 'Normal uninstaller parent failed.'
    Assert-That (!(Test-Path -LiteralPath $registry) -and !(Test-Path -LiteralPath $shortcut)) 'Uninstaller registration or shortcut remains.'
    foreach ($file in $published.payloadFiles) { Assert-That (!(Test-Path -LiteralPath (Join-Path $installed $file))) ('Installed payload remains: ' + $file) }
    Assert-That (!(Test-Path -LiteralPath $uninstaller) -and !(Test-Path -LiteralPath (Join-Path $installed 'install-owner.txt'))) 'Uninstaller did not remove its own files.'
    Assert-That ([IO.File]::ReadAllText((Join-Path $installed 'app/my-extra-note.txt')) -eq 'Extra files are kept.') 'An extra application-folder file was removed.'
    Assert-That ((@(Fingerprint $study | Sort-Object path) | ConvertTo-Json -Compress) -eq $studyBeforeJson) 'Study data changed during uninstall.'
    $checks.Add('Normal NSIS uninstall and its child exit remove only installed files, shortcut and registration, preserving study bytes and extra files.')
    $verification = @{passed=$true;runtime='win-x64';checks=$checks;installerSha256=$published.sha256;hostManifestSha256=$published.hostManifestSha256;host=$hostIdentity;studyFilesPreserved=$studyBefore.Count;operations=$operations;modelCalls=0;workerVmStarts=0;visualWizardVerified=$false}
    $verification | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'verified.json') -Encoding utf8
} catch { $failure = $_ }
finally {
    if ($ownedHost -and !$ownedHost.HasExited) { Stop-Process -InputObject $ownedHost; [void]$ownedHost.WaitForExit(10000) }
    $allExited = @($processes | Where-Object { !$_.HasExited }).Count -eq 0
    if ($allExited) {
        # Failure cleanup is restricted to this fresh fixture's recorded registrations.
        if (Test-Path -LiteralPath $registry) {
            $registration = Get-ItemProperty -LiteralPath $registry
            if ($registration.InstallLocation -eq $installed) { Remove-Item -LiteralPath $registry -Recurse; $removed += 'owned test uninstall registration' }
            else { throw 'A different registration replaced the fixture; retained for inspection.' }
        }
        if (Test-Path -LiteralPath $shortcut) {
            $shell = New-Object -ComObject WScript.Shell
            $link = $shell.CreateShortcut($shortcut)
            if ($link.TargetPath -eq (Join-Path $installed 'app/Thaddeus.Host.exe')) { Remove-Item -LiteralPath $shortcut; $removed += 'owned test shortcut' }
            else { throw 'A different shortcut replaced the fixture; retained for inspection.' }
            $link = $null
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
        }
        $shortcutsFolder = Split-Path $shortcut -Parent
        if ((Test-Path -LiteralPath $shortcutsFolder) -and @(Get-ChildItem -LiteralPath $shortcutsFolder -Force).Count -eq 0) { Remove-Item -LiteralPath $shortcutsFolder }
        foreach ($relative in @('installed','study','existing','foreign','held-wwwroot','second-install','denied')) {
            $target = Assert-Child (Join-Path $root $relative)
            if (Test-Path -LiteralPath $target) {
                $resolved = (Resolve-Path -LiteralPath $target).Path
                if ($resolved -ne $target -or (Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked cleanup root; retain fixture.' }
                Remove-Item -LiteralPath $resolved -Recurse -Force
                $removed += $relative
            }
        }
        $profilePath = Join-Path $root 'fixture-launch.json'
        if (Test-Path -LiteralPath $profilePath) { Remove-Item -LiteralPath $profilePath; $removed += 'fixture-launch.json' }
    }
    @{passed=(!$failure -and $allExited);failure=if($failure){$failure.Exception.Message}else{$null};allOwnedProcessesExited=$allExited;removed=$removed;operations=$operations;freeBytes=(Get-PSDrive C).Free} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'cleanup.json') -Encoding utf8
}
if ($failure) { throw $failure }
Write-Output ('Installer checks passed: ' + $checks.Count + '. The ledger survived eviction of the app.')
