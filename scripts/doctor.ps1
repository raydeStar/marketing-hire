Set-Location (Split-Path $PSScriptRoot -Parent)
Write-Host 'Thaddeus doctor: inspecting the study, not your secrets.'
dotnet --version
node --version
npm --version
Write-Host "Frontend built: $(Test-Path src/Thaddeus.Host/wwwroot/index.html)"
Write-Host "Host key present: $(Test-Path .data/host-key.txt)"
Write-Host "Provider credential configured: $([bool]$env:Thaddeus__ApiKey)"
Write-Host "Phone HTTPS explicitly configured: $([bool]$env:Thaddeus__PhoneOrigin)"
try { $r = Invoke-WebRequest http://localhost:5179/ -TimeoutSec 3; Write-Host "Host reachable: $($r.StatusCode)" } catch { Write-Host 'Host is not reachable on the default loopback port.' }
Write-Host 'No GPU, model weights, public deployment, or background service is required.'
