param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$origin = 'http://localhost:5190'
$fixtureScript = Join-Path $product 'business\agent\hire\bin\runway.py'
$hostDll = Join-Path $product 'src\Thaddeus.Host\bin\Release\net10.0\Thaddeus.Host.dll'
$webPage = Join-Path $product 'src\Thaddeus.Host\wwwroot\index.html'
$marker = Join-Path $product 'artifacts\campaign-fixture-active.json'

if (Get-NetTCPConnection -LocalPort 5190 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1) {
    throw 'Port 5190 is already in use. Leave the existing service alone.'
}
if (-not (Test-Path -LiteralPath $fixtureScript) -or -not (Test-Path -LiteralPath $hostDll) -or
    -not (Test-Path -LiteralPath $webPage)) {
    throw 'The Release host, web build, or fixture script is missing. Build this checkout before starting the fixture.'
}
if ((Get-PSDrive C).Free -lt 11GB) { throw 'Less than 11 GiB remains on C:. The fixture will not start.' }
if (Test-Path -LiteralPath $marker) {
    throw "A fixture marker already exists at $marker. Inspect the previous fixture before starting another."
}
if ($CheckOnly) { Write-Host 'Fixture preflight passed. Port 5190 is free; no data was created.'; return }

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('marketing-campaign-browser-' + [guid]::NewGuid().ToString('N'))
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
    'Thaddeus__PhoneOrigin' = $null
    'Thaddeus__ApiKey' = $null
    'Thaddeus__ApiKeyEndpoint' = $null
    'Marketing__FixtureLedger' = $ledgerRoot
    'Marketing__FixtureRunwayScript' = $fixtureScript
    'Marketing__Container' = 'nonexistent-fixture-container'
    'Marketing__SharedContainer' = 'nonexistent-fixture-container'
    'Marketing__RunwayPilotMode' = $null
}
$original = @{}
foreach ($name in $settings.Keys) {
    $original[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
}
try {
    Write-Host "Isolated fixture at $fixtureRoot. Leave this window open while Codex runs the browser check."
    Set-Location $product
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & dotnet run --no-build -c Release --project src/Thaddeus.Host --no-launch-profile
        $hostExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($hostExitCode -ne 0) { throw "Fixture host exited with code $hostExitCode." }
}
finally {
    Set-Location $originalLocation
    foreach ($name in $settings.Keys) {
        [Environment]::SetEnvironmentVariable($name, $original[$name], 'Process')
    }
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    Write-Host "Fixture data remains at $fixtureRoot for the acceptance receipt."
}
