param([string]$LaunchProfile, [switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'This packaged launcher is currently verified for Windows only.' }
$package = [IO.Path]::GetFullPath($PSScriptRoot)
$executable = Join-Path $package 'Thaddeus.Host.exe'
$webIndex = Join-Path $package 'wwwroot/index.html'
if (!(Test-Path -LiteralPath $executable -PathType Leaf) -or !(Test-Path -LiteralPath $webIndex -PathType Leaf)) { throw 'Run the launcher inside a complete published Thaddeus package.' }
function Assert-PlainPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A launch path contains a link. Use ordinary package and data folders.' }
        $current = Split-Path -Path $current -Parent
    }
}
Assert-PlainPath $package
if (!$LaunchProfile) { $LaunchProfile = Join-Path $package 'launch.json' }
$settings = $null
if (Test-Path -LiteralPath $LaunchProfile) {
    Assert-PlainPath $LaunchProfile
    if ((Get-Item -LiteralPath $LaunchProfile).Length -gt 16000) { throw 'The launch profile is too large.' }
    $settings = Get-Content -LiteralPath $LaunchProfile -Raw -Encoding utf8 | ConvertFrom-Json
    if ($settings.schemaVersion -ne 1 -or @($settings.PSObject.Properties.Name | Where-Object { $_ -notin 'schemaVersion','dataDirectory','localOrigin','workerPort','developmentWorkerInstallation' }).Count) { throw 'Unsupported launch profile.' }
} elseif ($PSBoundParameters.ContainsKey('LaunchProfile')) { throw 'The selected launch profile does not exist.' }
$data = if ($settings.dataDirectory) { [string]$settings.dataDirectory } else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Thaddeus2' }
if (![IO.Path]::IsPathRooted($data)) { throw 'The data folder must be an absolute path.' }
$data = [IO.Path]::GetFullPath($data).TrimEnd('\')
if ($data -eq $package -or $data.StartsWith($package.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep private data outside the replaceable application package.' }
Assert-PlainPath $data
$origin = if ($settings.localOrigin) { [string]$settings.localOrigin } else { 'http://localhost:5179' }
$address = [Uri]$origin
if (!$address.IsAbsoluteUri -or !$address.IsLoopback -or $address.Scheme -ne 'http' -or $address.Port -lt 1024 -or $address.AbsolutePath -ne '/' -or $address.Query -or $address.Fragment -or $address.UserInfo -or $origin.EndsWith('/')) { throw 'Use an exact HTTP loopback origin with an unprivileged port.' }
$workerPort = if ($settings.workerPort) { [int]$settings.workerPort } else { 5183 }
if ($workerPort -lt 1024 -or $workerPort -gt 65535 -or $workerPort -eq $address.Port) { throw 'Choose a separate unprivileged worker port.' }
$installation = [string]$settings.developmentWorkerInstallation
if ($installation) {
    if (![IO.Path]::IsPathRooted($installation) -or !(Test-Path -LiteralPath $installation -PathType Leaf)) { throw 'The configured development worker installation is missing.' }
    Assert-PlainPath $installation
}
$effective = [ordered]@{package=$package;data=$data;origin=$origin;workerPort=$workerPort;installation=$installation}
$hash = [Security.Cryptography.SHA256]::Create()
try { $profileHash = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes(($effective | ConvertTo-Json -Compress)))).Replace('-','').ToLowerInvariant() } finally { $hash.Dispose() }
[IO.Directory]::CreateDirectory($data) | Out-Null
$lockPath = Join-Path $data 'launcher.lock'; Assert-PlainPath $lockPath
try { $launchLock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') } catch { throw 'Thaddeus is already being opened. Wait for that launch to finish.' }
try {
    $receiptPath = Join-Path $data 'launcher-instance.json'; Assert-PlainPath $receiptPath
    $receipt = if (Test-Path -LiteralPath $receiptPath) { Get-Content -LiteralPath $receiptPath -Raw -Encoding utf8 | ConvertFrom-Json } else { $null }
    $ports = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -eq $address.Port -or $_.LocalPort -eq $workerPort })
    $existing = if ($receipt) { Get-Process -Id $receipt.processId -ErrorAction SilentlyContinue } else { $null }
    $reused = $false
    if ($ports.Count -or $existing) {
        if (!$existing -or $existing.Path -ne $executable -or $existing.StartTime.ToUniversalTime().Ticks -ne $receipt.startTicks -or $receipt.profileHash -ne $profileHash -or @($ports | Where-Object { $_.OwningProcess -ne $existing.Id }).Count) {
            throw 'The configured ports or data folder belong to another launch. No process was stopped or replaced.'
        }
        $running = $existing; $reused = $true
    } else {
        $logRoot = Join-Path $data 'launcher-logs'; Assert-PlainPath $logRoot; [IO.Directory]::CreateDirectory($logRoot) | Out-Null
        $attempt = [Guid]::NewGuid().ToString('N')
        $launchArguments = @(('--contentRoot="' + $package + '"'), ('--Thaddeus:Data="' + $data + '"'), ('--Thaddeus:LocalOrigin=' + $origin), ('--Thaddeus:WorkerPort=' + $workerPort))
        if ($installation) { $launchArguments += '--Thaddeus:DevelopmentWorkerInstallation="' + $installation + '"' }
        # Host settings come from this profile. Keep only the explicit credential environment override.
        $savedEnvironment = @(Get-ChildItem Env: | Where-Object { $_.Name -like 'Thaddeus__*' -and $_.Name -notin 'Thaddeus__ApiKey','Thaddeus__ApiKeyEndpoint' })
        try {
            foreach ($entry in $savedEnvironment) { [Environment]::SetEnvironmentVariable($entry.Name, $null, 'Process') }
            $running = Start-Process -FilePath $executable -ArgumentList $launchArguments -WorkingDirectory $package -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot "$attempt.stdout.log") -RedirectStandardError (Join-Path $logRoot "$attempt.stderr.log") -PassThru
        } finally { foreach ($entry in $savedEnvironment) { [Environment]::SetEnvironmentVariable($entry.Name, $entry.Value, 'Process') } }
        $receipt = @{schemaVersion=1;processId=$running.Id;startTicks=$running.StartTime.ToUniversalTime().Ticks;profileHash=$profileHash;origin=$origin;attempt=$attempt}
        $receipt | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
    }
    $expectedPage = Get-Content -LiteralPath $webIndex -Raw -Encoding utf8
    $watch = [Diagnostics.Stopwatch]::StartNew(); $ready = $false
    do {
        try { $response = Invoke-WebRequest $origin -UseBasicParsing -TimeoutSec 2; $ready = $response.StatusCode -eq 200 -and [Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray()) -eq $expectedPage } catch { }
        if (!$ready) { if ($running.HasExited) { throw 'The host exited during startup. Its private launcher logs contain the details.' }; Start-Sleep -Milliseconds 200 }
    } while (!$ready -and $watch.Elapsed.TotalSeconds -lt 30)
    if (!$ready) { throw 'The expected web page did not become ready. The host was left in place for inspection; no replacement was started.' }
    if (!$NoBrowser) {
        $keyPath = Join-Path $data 'host-key.txt'; Assert-PlainPath $keyPath
        $body = @{key=(Get-Content -LiteralPath $keyPath -Raw).Trim()} | ConvertTo-Json
        $ticket = Invoke-RestMethod "$origin/api/auth/launch" -Method Post -ContentType 'application/json' -Body $body -Headers @{Origin=$origin} -TimeoutSec 10
        if ($ticket.ticket -notmatch '^[a-f0-9]{48}$') { throw 'The host did not issue a valid browser launch ticket.' }
        Start-Process ($origin + '/#launch=' + $ticket.ticket)
    }
    [pscustomobject]@{ready=$true;reused=$reused;processId=$running.Id;origin=$origin;data=$data;modelCallsStarted=0}
    Write-Host 'The study is open. No model has been disturbed.'
} finally { $launchLock.Dispose() }
