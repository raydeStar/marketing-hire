param([Parameter(Mandatory=$true)][string]$LauncherFolder, [Parameter(Mandatory=$true)][string]$Package, [Parameter(Mandatory=$true)][string]$Evidence)
$ErrorActionPreference = 'Stop'
$privateRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts')).TrimEnd('\') + '\'
foreach ($path in @($LauncherFolder, $Package, $Evidence)) {
    if (![IO.Path]::GetFullPath($path).StartsWith($privateRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Restore checks require disposable artifacts.' }
}
if (Test-Path -LiteralPath $Evidence) { throw 'Use a fresh evidence folder.' }
[IO.Directory]::CreateDirectory($Evidence) | Out-Null
$profile = Get-Content -LiteralPath (Join-Path $LauncherFolder 'launch.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$data = [IO.Path]::GetFullPath($profile.dataDirectory)
if (!$data.StartsWith($privateRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'The restored fixture must stay under artifacts.' }
$receiptPath = Join-Path $data 'launcher-instance.json'
if (Test-Path -LiteralPath $receiptPath) { throw 'The fresh restored copy must not already contain a launcher identity.' }
$executable = Join-Path $Package 'Thaddeus.Host.exe'
$owned = $null
function Owned-Process {
    if (!$script:owned) { return $null }
    $process = Get-Process -Id $script:owned.processId -ErrorAction SilentlyContinue
    if ($process -and ($process.Path -ne $executable -or $process.StartTime.ToUniversalTime().Ticks -ne $script:owned.startTicks)) {
        throw 'The restored host identity changed; no process was stopped.'
    }
    return $process
}
function Call([string]$Route, $Body=$null) {
    $arguments = @{Uri=($profile.localOrigin + '/api' + $Route);WebSession=$script:session;TimeoutSec=10}
    if ($null -ne $Body) {
        $arguments.Method='Post'; $arguments.ContentType='application/json'; $arguments.Body=($Body | ConvertTo-Json -Depth 20 -Compress)
        $arguments.Headers=@{Origin=$profile.localOrigin;'X-CSRF'=$script:login.csrf}
    }
    return Invoke-RestMethod @arguments
}
try {
    $entry = Join-Path $LauncherFolder 'Open restored study.ps1'
    $arguments = @('-NoProfile','-File',('"' + $entry + '"'),'-NoBrowser')
    $launcher = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $Evidence 'launcher.log') -RedirectStandardError (Join-Path $Evidence 'launcher.stderr.log') -PassThru
    # Keep the native handle before exit; Windows PowerShell otherwise loses a fast child's exit code.
    $null = $launcher.Handle
    if (!$launcher.WaitForExit(60000)) { throw 'The generated launcher exceeded its deadline; inspect its live process before retrying.' }
    $launcher.Refresh()
    if ($launcher.ExitCode -ne 0) { throw 'The generated launcher failed; inspect the retained logs.' }
    $owned = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!(Owned-Process)) { throw 'The generated launcher did not leave its verified host running.' }
    $body = @{key=(Get-Content -LiteralPath (Join-Path $data 'host-key.txt') -Raw).Trim()} | ConvertTo-Json
    $login = Invoke-RestMethod ($profile.localOrigin + '/api/auth/login') -Method Post -ContentType 'application/json' -Body $body -Headers @{Origin=$profile.localOrigin} -SessionVariable session -TimeoutSec 10
    $export = Call '/export'
    $review = Call '/maintenance'
    $null = Call '/maintenance/start' @{version=$review.version;mode='backup'}
    $verified = $null
    for ($attempt=0; $attempt -lt 150; $attempt++) {
        if (!(Owned-Process)) { throw 'The restored host exited during backup.' }
        try { $state = Call '/maintenance' } catch { $state = $null }
        if ($state.phase -eq 'failed') { throw 'The restored host could not verify its next backup.' }
        if ($state.phase -eq 'verified') { $verified=$state; break }
        Start-Sleep -Milliseconds 200
    }
    if (!$verified) { throw 'The restored host did not enter verified maintenance.' }
    $next = Call '/maintenance/restore/review' @{backupId=$verified.version}
    if (!$next.review.canPrepareLauncher) { throw 'Windows shortcut startup lost the package context needed for the next restore.' }
    $null = Call '/maintenance/finish' @{version=$verified.version;mode='close'}
    $process = Owned-Process
    if ($process -and !$process.WaitForExit(15000)) { throw 'The restored host did not finish its owner-requested shutdown.' }
    @{passed=$true;export=$export;data=$data;origin=$profile.localOrigin;nextRestoreLauncherAvailable=$true;liveModelCalls=0} |
        ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $Evidence 'verified.json') -Encoding UTF8
    Write-Host 'The generated launcher opened the restored ledger and closed it cleanly. The original estate stayed put.'
} finally {
    if (!$owned -and (Test-Path -LiteralPath $receiptPath)) { $owned=Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    $process = Owned-Process
    if ($process) { Stop-Process -Id $process.Id; Wait-Process -Id $process.Id -ErrorAction SilentlyContinue }
}
