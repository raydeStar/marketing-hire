param([Parameter(Mandatory=$true)][string]$Package, [Parameter(Mandatory=$true)][string]$Artifacts)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$privateRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts')).TrimEnd('\') + '\'
$Package = [IO.Path]::GetFullPath($Package); $Artifacts = [IO.Path]::GetFullPath($Artifacts)
if (!$Package.StartsWith($privateRoot, [StringComparison]::OrdinalIgnoreCase) -or !$Artifacts.StartsWith($privateRoot, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $Artifacts)) { throw 'Use an existing private package and a fresh artifact directory under this repository.' }
[IO.Directory]::CreateDirectory($Artifacts) | Out-Null
$launcher = Join-Path $Package 'launch-host.ps1'; $executable = Join-Path $Package 'Thaddeus.Host.exe'
$checks = [Collections.Generic.List[string]]::new(); $owned = $null
$windowsPowerShell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
function Reserve-Port {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $listener.Start(); return $listener
}
function Launch([string]$Name, [string]$Path, [bool]$Success) {
    # Captured native-command pipes can remain inherited by the detached host on Windows PowerShell 5.
    # Wait on the launcher's process handle, with separate files for its output.
    $arguments = @('-NoProfile','-File',('"' + $launcher + '"'),'-LaunchProfile',('"' + $Path + '"'),'-NoBrowser')
    $process = Start-Process -FilePath $windowsPowerShell -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $Artifacts "$Name.log") -RedirectStandardError (Join-Path $Artifacts "$Name.stderr.log") -PassThru
    if (!$process.WaitForExit(60000)) { throw "The $Name launcher exceeded its verification deadline. Inspect its process before another attempt." }
    $process.Refresh(); $code = $process.ExitCode
    if (($code -eq 0) -ne $Success) { throw "Unexpected launcher outcome for $Name; inspect its retained log." }
}
function Stop-Owned {
    if (!$script:owned) { return }
    $process = Get-Process -Id $script:owned.processId -ErrorAction SilentlyContinue
    if ($process) {
        if ($process.Path -ne $executable -or $process.StartTime.ToUniversalTime().Ticks -ne $script:owned.startTicks) { throw 'Test host ownership changed; no process was stopped.' }
        Stop-Process -Id $process.Id; Wait-Process -Id $process.Id -ErrorAction SilentlyContinue
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while (@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object OwningProcess -eq $process.Id).Count -and $watch.Elapsed.TotalSeconds -lt 5) { Start-Sleep -Milliseconds 100 }
        if (@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object OwningProcess -eq $process.Id).Count) { throw 'Owned test listeners did not retire.' }
    }
}
function State([string]$Origin, [string]$Data, [bool]$Seed=$false) {
    $body = @{key=(Get-Content -LiteralPath (Join-Path $Data 'host-key.txt') -Raw).Trim()} | ConvertTo-Json
    $login = Invoke-RestMethod "$Origin/api/auth/login" -Method Post -ContentType 'application/json' -Body $body -Headers @{Origin=$Origin} -SessionVariable session -TimeoutSec 10
    if ($Seed) { $null = Invoke-RestMethod "$Origin/api/demo/seed" -Method Post -ContentType 'application/json' -Body '{}' -Headers @{Origin=$Origin;'X-CSRF'=$login.csrf} -WebSession $session -TimeoutSec 10 }
    return Invoke-RestMethod "$Origin/api/state" -WebSession $session -TimeoutSec 10
}
$first = Reserve-Port; $second = Reserve-Port
$port = $first.LocalEndpoint.Port; $workerPort = $second.LocalEndpoint.Port
$first.Stop(); $second.Stop()
$data = Join-Path $Artifacts ('data-' + [char]0x00e9); $launchProfileFile = Join-Path $Artifacts 'profile.json'
$settings = @{schemaVersion=1;dataDirectory=$data;localOrigin="http://127.0.0.1:$port";workerPort=$workerPort}
[IO.File]::WriteAllText($launchProfileFile, ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
try {
    Launch 'first-start' $launchProfileFile $true
    $owned = Get-Content (Join-Path $data 'launcher-instance.json') -Raw | ConvertFrom-Json
    $checks.Add('Windows PowerShell 5 starts the published executable and verifies its UTF-8 web bytes')
    $before = State $settings.localOrigin $data $true
    if ($before.runs.Count -ne 0 -or $before.pages.Count -eq 0 -or $before.research.enabled) { throw 'Unexpected default product state.' }
    $beforePages = $before.pages | ConvertTo-Json -Depth 10 -Compress
    Launch 'duplicate-start' $launchProfileFile $true
    $again = Get-Content (Join-Path $data 'launcher-instance.json') -Raw | ConvertFrom-Json
    if ($again.processId -ne $owned.processId -or $again.startTicks -ne $owned.startTicks) { throw 'Duplicate launch started another host.' }
    $checks.Add('Duplicate launch reuses the exact owned process without another start')
    $changed = $settings.Clone(); $changed.dataDirectory = Join-Path $Artifacts 'other-data'
    $changedProfile = Join-Path $Artifacts 'other-profile.json'; $changed | ConvertTo-Json | Set-Content $changedProfile -Encoding utf8
    Launch 'occupied-product-port' $changedProfile $false
    if (Test-Path (Join-Path $changed.dataDirectory 'host-key.txt')) { throw 'A conflicting launch initialized another product store.' }
    $checks.Add('Occupied product ports are refused without initializing another store')
    $available = Reserve-Port; $changed = $settings.Clone(); $changed.workerPort = $available.LocalEndpoint.Port; $available.Stop()
    $changed | ConvertTo-Json | Set-Content -LiteralPath $changedProfile -Encoding utf8
    Launch 'changed-profile' $changedProfile $false
    $checks.Add('A changed profile cannot silently reuse the existing data owner')
    Stop-Owned
    Launch 'restart' $launchProfileFile $true
    $previous = $owned; $owned = Get-Content (Join-Path $data 'launcher-instance.json') -Raw | ConvertFrom-Json
    if ($previous.processId -eq $owned.processId -and $previous.startTicks -eq $owned.startTicks) { throw 'Restart did not create a fresh owned process.' }
    $after = State $settings.localOrigin $data
    if (($after.pages | ConvertTo-Json -Depth 10 -Compress) -ne $beforePages -or $after.runs.Count -ne 0) { throw 'Restart did not preserve the existing product store.' }
    $checks.Add('Restart retains exact seeded page records and dispatches no models')
    $foreign = Reserve-Port
    try {
        $available = Reserve-Port
        $occupied = $settings.Clone(); $occupied.dataDirectory = Join-Path $Artifacts 'foreign-data'; $occupied.localOrigin = 'http://127.0.0.1:' + $foreign.LocalEndpoint.Port; $occupied.workerPort = $available.LocalEndpoint.Port
        $available.Stop()
        $occupiedProfile = Join-Path $Artifacts 'foreign-profile.json'; $occupied | ConvertTo-Json | Set-Content $occupiedProfile -Encoding utf8
        Launch 'foreign-listener' $occupiedProfile $false
        if (!$foreign.Server.IsBound -or (Test-Path (Join-Path $occupied.dataDirectory 'host-key.txt'))) { throw 'The foreign listener was disturbed or another store initialized.' }
        $checks.Add('An unrelated listening process is left intact')
    } finally { $foreign.Stop() }
    Stop-Owned
    @{passed=$true;checks=$checks;liveModelCalls=0;gpuInference=0;package=$Package;data=$data;windowsPowerShell=$windowsPowerShell} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Artifacts 'verified.json') -Encoding utf8
    Write-Host "$($checks.Count) packaged launcher checks passed. The butler kept the doors in order."
} finally {
    if (!$owned -and (Test-Path (Join-Path $data 'launcher-instance.json'))) { $owned = Get-Content (Join-Path $data 'launcher-instance.json') -Raw | ConvertFrom-Json }
    Stop-Owned
}
