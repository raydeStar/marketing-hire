param([string]$Name = (Get-Date -Format 'yyyyMMdd-HHmmss'))
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repository
if ($Name -notmatch '^[a-zA-Z0-9-]{1,60}$') { throw 'Use a simple development-build name.' }
$stage = Join-Path $repository "artifacts/package-source-$Name"
$output = Join-Path $repository "artifacts/dev-host-$Name"
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $output)) { throw 'That build already exists. Use a new name so a running host keeps its files.' }
New-Item -ItemType Directory -Path $stage | Out-Null
$sourceFiles = @(& rg --files src fixtures)
if ($LASTEXITCODE) { throw 'Could not enumerate package sources.' }
$sourceFiles += 'Directory.Build.props', 'global.json', 'scripts/publish-dev.ps1', 'scripts/launch-host.ps1', 'scripts/Start Thaddeus.cmd', 'docs/WINDOWS_LAUNCHER.md'
$provenance = @()
foreach ($relative in $sourceFiles) {
    $original = Join-Path $repository $relative
    $target = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $original -Destination $target
    $provenance += @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$web = Join-Path $repository 'src/Thaddeus.Host/wwwroot'
if (-not (Test-Path -LiteralPath (Join-Path $web 'index.html'))) { throw 'Build the web client first.' }
Copy-Item -LiteralPath $web -Destination (Join-Path $stage 'src/Thaddeus.Host/wwwroot') -Recurse
# RID-specific restore may change a lockfile. Keep those changes inside this private staging tree.
dotnet publish (Join-Path $stage 'src/Thaddeus.Host/Thaddeus.Host.csproj') --configuration Release --runtime win-x64 --self-contained true --output $output --nologo
if ($LASTEXITCODE) { throw 'Development publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launch-host.ps1') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start Thaddeus.cmd') -Destination $output
Copy-Item -LiteralPath (Join-Path $repository 'docs/WINDOWS_LAUNCHER.md') -Destination (Join-Path $output 'README.md')
$manifest = @{ schemaVersion = 1; kind = 'development-snapshot'; runtime = 'win-x64'; published = (Get-Date).ToUniversalTime().ToString('O');
    sourceHead = (& git rev-parse HEAD); sourceFiles = $provenance; isolationQualified = $false }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'build-provenance.json') -Encoding utf8
Write-Output "Development host published to $output. A separate study, so the current butler can keep working."
