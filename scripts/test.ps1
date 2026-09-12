$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet test --nologo
if ($LASTEXITCODE) { throw 'Backend checks failed.' }
node --test scripts/luna-protocol.test.mjs
if ($LASTEXITCODE) { throw 'Model transport contract checks failed.' }
npm --prefix web run build
if ($LASTEXITCODE) { throw 'Frontend checks failed.' }
Write-Host 'Backend and frontend checks passed. Run browser tests against a disposable demo host; even ravens prefer clean test benches.'
