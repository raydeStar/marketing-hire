param([switch]$CheckOnly, [string]$OverrideHostDll,
    [string]$NativeProofContainer, [string]$PhoneOrigin,
    # Live shift turns only: the employee container that meters and runs them. Spends model budget.
    [string]$LiveShiftContainer,
    # Reopen a fixture kept for a follow-up (its folder under the temp directory) instead of creating a new one.
    [string]$ResumeFixture)

$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$origin = 'http://localhost:5190'
$fixtureScript = Join-Path $product 'business\agent\hire\bin\runway.py'
$hostDll = if ($OverrideHostDll) { (Resolve-Path -LiteralPath $OverrideHostDll -ErrorAction Stop).Path }
    else { Join-Path $product 'src\Thaddeus.Host\bin\Release\net10.0\Thaddeus.Host.dll' }
$webPage = Join-Path $product 'src\Thaddeus.Host\wwwroot\index.html'
$marker = Join-Path $product 'artifacts\campaign-fixture-active.json'

if (Get-NetTCPConnection -LocalPort 5190 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1) {
    throw 'Port 5190 is already in use. Leave the existing service alone.'
}
if ($PhoneOrigin) {
    $phoneUri = [uri]$PhoneOrigin
    if ($phoneUri.Scheme -ne 'https' -or $phoneUri.Port -eq 5190 -or
        (Get-NetTCPConnection -LocalPort $phoneUri.Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        throw 'The fixture phone origin must be HTTPS on a separate free port.'
    }
}
if (-not (Test-Path -LiteralPath $fixtureScript) -or -not (Test-Path -LiteralPath $hostDll) -or
    -not (Test-Path -LiteralPath $webPage)) {
    throw 'The Release host, web build, or fixture script is missing. Build this checkout before starting the fixture.'
}
if ((Get-PSDrive C).Free -lt 11GB) { throw 'Less than 11 GiB remains on C:. The fixture will not start.' }
if (Test-Path -LiteralPath $marker) {
    # A launcher that was stopped hard leaves its marker; resuming that same fixture is fine once its launcher is gone.
    $previous = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    $sameFixture = $ResumeFixture -and (Test-Path -LiteralPath $ResumeFixture) -and
        ((Split-Path $previous.dataRoot -Parent) -eq (Resolve-Path -LiteralPath $ResumeFixture).Path)
    if ($sameFixture -and -not (Get-Process -Id $previous.launcherPid -ErrorAction SilentlyContinue)) { Remove-Item -LiteralPath $marker }
    else { throw "A fixture marker already exists at $marker. Inspect the previous fixture before starting another." }
}
if ($CheckOnly) { Write-Host 'Fixture preflight passed. Port 5190 is free; no data was created.'; return }

if ($ResumeFixture) {
    $resolved = (Resolve-Path -LiteralPath $ResumeFixture).Path
    $expected = Join-Path ([System.IO.Path]::GetTempPath()) 'marketing-campaign-browser-'
    if (-not $resolved.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $resolved 'host\host-key.txt'))) {
        throw 'That is not a kept campaign fixture; nothing was started.'
    }
    $fixtureRoot = $resolved
} else {
    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('marketing-campaign-browser-' + [guid]::NewGuid().ToString('N'))
}
$originalLocation = Get-Location
$dataRoot = Join-Path $fixtureRoot 'host'
$ledgerRoot = Join-Path $fixtureRoot 'ledger'
New-Item -ItemType Directory -Path $dataRoot,$ledgerRoot -Force | Out-Null
$descriptor = @{ origin = $origin; dataRoot = $dataRoot; ledgerRoot = $ledgerRoot;
    launcherPid = $PID; createdAt = [DateTimeOffset]::UtcNow.ToString('O') }
$descriptor | ConvertTo-Json -Compress | Set-Content -LiteralPath $marker -Encoding UTF8

$settings = @{
    'Thaddeus__Data' = $dataRoot
    'Thaddeus__LocalOrigin' = $origin
    'Thaddeus__PhoneOrigin' = $(if ($PhoneOrigin) { $PhoneOrigin } else { $null })
    'Thaddeus__PhoneMode' = 'direct'
    'Thaddeus__WorkerPort' = $null
    'Thaddeus__ApiKey' = $null
    'Thaddeus__ApiKeyEndpoint' = $null
    'Marketing__FixtureLedger' = $ledgerRoot
    'Marketing__FixtureRunwayScript' = $fixtureScript
    'Marketing__Container' = 'nonexistent-fixture-container'
    'Marketing__SharedContainer' = $(if ($NativeProofContainer) { $NativeProofContainer } else { 'nonexistent-fixture-container' })
    'Marketing__FixtureNativeGatewayEnabled' = $(if ($NativeProofContainer) { 'true' } else { $null })
    'Marketing__RunwayPilotMode' = $null
    # Only live shift turns (and their meter receipts) may reach a named employee container; everything else stays isolated.
    'Marketing__ShiftContainer' = $(if ($LiveShiftContainer) { $LiveShiftContainer } else { $null })
    'Marketing__ShiftRuntime' = $(if ($LiveShiftContainer) { 'openclaw' } else { $null })
    # Browser test batches load pages much faster than a person; the product default stays at 600 a minute.
    'Thaddeus__ApiRequestsPerMinute' = '3000'
    'Thaddeus__AuthRequestsPerMinute' = '120'
}
$original = @{}
foreach ($name in $settings.Keys) {
    $original[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    if ($null -eq $settings[$name]) {
        Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue
    }
    else { [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process') }
}
try {
    if ([Environment]::GetEnvironmentVariable('Thaddeus__PhoneOrigin', 'Process') -ne $settings['Thaddeus__PhoneOrigin']) {
        throw 'Fixture phone origin does not match the requested isolation setting; no host was started.'
    }
    Write-Host "Isolated fixture at $fixtureRoot. Leave this window open while Codex runs the browser check."
    Set-Location $(if ($OverrideHostDll) { Join-Path $product 'src\Thaddeus.Host' } else { $product })
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        if ($OverrideHostDll) { & dotnet $hostDll }
        else { & dotnet run --no-build -c Release --project src/Thaddeus.Host --no-launch-profile }
        $hostExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($hostExitCode -ne 0) { throw "Fixture host exited with code $hostExitCode." }
}
finally {
    Set-Location $originalLocation
    foreach ($name in $settings.Keys) {
        if ($null -eq $original[$name]) {
            Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue
        }
        else { [Environment]::SetEnvironmentVariable($name, $original[$name], 'Process') }
    }
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    Write-Host "Fixture data remains at $fixtureRoot for the acceptance receipt."
}
