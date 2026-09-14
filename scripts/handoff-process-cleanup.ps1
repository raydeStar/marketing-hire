param([Parameter(Mandatory=$true)][string]$Evidence)
$ErrorActionPreference = 'Stop'
$artifacts = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts')).TrimEnd('\')
function Ordinary-Path([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped its recorded root.' }
    $item = Get-Item -LiteralPath $full -Force
    while ($item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked cleanup paths are refused.' }
        $item = if ($item.PSIsContainer) { $item.Parent } else { $item.Directory }
    }
    return $full
}
$evidenceRoot = Ordinary-Path $Evidence $artifacts
$fixture = Get-Content -LiteralPath (Ordinary-Path (Join-Path $evidenceRoot 'fixture.json') $evidenceRoot) -Raw | ConvertFrom-Json
$marker = Ordinary-Path (Join-Path $evidenceRoot 'handoff-targets.json') $evidenceRoot
if ((Get-Item -LiteralPath $marker).Length -gt 16000) { throw 'Oversized fixture registration.' }
$targets = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
if ($targets.Count -lt 1 -or $targets.Count -gt 6) { throw 'Unexpected fixture target count.' }
$checked = @()
foreach ($target in $targets) {
    $profile = Ordinary-Path $target.profile $evidenceRoot
    $package = Ordinary-Path $target.package $artifacts
    $data = Ordinary-Path $target.dataDirectory $evidenceRoot
    if ([IO.Path]::GetFileName($profile) -ne 'launch.json') { throw 'Unexpected launch profile name.' }
    $launch = Get-Content -LiteralPath $profile -Raw | ConvertFrom-Json
    if ($launch.dataDirectory -ne $data -or $launch.localOrigin -ne $fixture.origin) { throw 'The registered profile no longer matches this fixture.' }
    $checked += @{profile=$profile;executable=(Join-Path $package 'Thaddeus.Host.exe')}
}
# Match the exact executable AND the random, fixture-owned profile before obtaining a process handle.
# The user's main study has neither this profile nor this private fixture directory.
$stopped = @()
foreach ($target in $checked) {
    foreach ($candidate in @(Get-CimInstance Win32_Process -Filter "Name='Thaddeus.Host.exe'")) {
        if ($candidate.ExecutablePath -ne $target.executable -or !$candidate.CommandLine.Contains('"' + $target.profile + '"')) { continue }
        $owned = Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue
        if (!$owned) { continue }
        $null = $owned.Handle
        if ($owned.Path -ne $target.executable -or [Math]::Abs(($owned.StartTime.ToUniversalTime() - $candidate.CreationDate.ToUniversalTime()).Ticks) -ge 10) { throw 'Fixture process identity changed; nothing was stopped.' }
        $identity = @{processId=$owned.Id;startTicks=$owned.StartTime.ToUniversalTime().Ticks.ToString();profile=$target.profile}
        $owned.Kill()
        if (!$owned.WaitForExit(15000)) { throw 'Owned handoff process did not exit; preserve the fixture.' }
        $stopped += $identity
        $owned.Dispose()
    }
}
foreach ($target in $checked) {
    if (@(Get-CimInstance Win32_Process -Filter "Name='Thaddeus.Host.exe'" | Where-Object { $_.ExecutablePath -eq $target.executable -and $_.CommandLine.Contains('"' + $target.profile + '"') }).Count) {
        throw 'A registered handoff fixture is still running.'
    }
}
@{passed=$true;registered=$checked;stopped=$stopped;mainStudyTouched=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'handoff-cleanup.json') -Encoding UTF8
Write-Host 'The handoff fixtures have left the estate; the main study remains undisturbed.'
