param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$tailscaleCommand = Get-Command tailscale -ErrorAction SilentlyContinue
$tailscaleExe = if ($tailscaleCommand) { $tailscaleCommand.Source } else { 'C:\Program Files\Tailscale\tailscale.exe' }
if (-not (Test-Path -LiteralPath $tailscaleExe)) { throw 'Install Tailscale on the computer and phone, sign both into your private network, then run this script again. See docs/PHONE_SETUP.md.' }
$tailStatus = & $tailscaleExe status --json | ConvertFrom-Json
if ($LASTEXITCODE -or $tailStatus.BackendState -ne 'Running') { throw 'Sign in to Tailscale first. No phone listener was started.' }
$phoneName = $tailStatus.Self.DNSName.TrimEnd('.')
if ($phoneName -notmatch '^[a-zA-Z0-9.-]+\.ts\.net$') { throw 'A valid Tailscale MagicDNS hostname is required.' }
if (-not $NoBuild) {
    npm --prefix web run build
    if ($LASTEXITCODE) { throw 'Frontend build failed.' }
    dotnet build --no-restore
    if ($LASTEXITCODE) { throw 'Backend build failed.' }
}
$env:Thaddeus__LocalOrigin = 'http://localhost:5179'
$env:Thaddeus__PhoneOrigin = "https://$phoneName"
$env:Thaddeus__PhoneMode = 'tailscale'
Write-Host "Phone address: $env:Thaddeus__PhoneOrigin"
Write-Host 'In a second terminal run: tailscale serve --https=443 http://localhost:5179'
Write-Host 'Keep both terminals running. Pair from Settings on the host. The raven answers only to an introduction.'
dotnet run --no-build --project src/Thaddeus.Host --no-launch-profile
