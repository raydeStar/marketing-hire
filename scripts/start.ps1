$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
npm --prefix web ci
if ($LASTEXITCODE) { throw 'Frontend installation failed.' }
npm --prefix web run build
if ($LASTEXITCODE) { throw 'Frontend build failed.' }
dotnet restore --locked-mode
if ($LASTEXITCODE) { throw 'Backend restore failed.' }
dotnet build --no-restore
if ($LASTEXITCODE) { throw 'Backend build failed.' }
Write-Host 'Open http://localhost:5179. The access key is in .data/host-key.txt. The raven has opened the study.'
dotnet run --no-build --project src/Thaddeus.Host --no-launch-profile
