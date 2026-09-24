$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$agent = Join-Path $product 'business\agent\compose.yml'
$source = Join-Path (Split-Path $product -Parent) 'marketing-hire\dev'
$sourceCompose = Join-Path $source 'compose.yml'
$sourceEnv = Join-Path $source '.env'

if (-not (Test-Path -LiteralPath $sourceEnv)) { throw "The existing marketing-hire dev env file is missing: $sourceEnv" }
if (-not (Test-Path -LiteralPath $sourceCompose)) { throw "The existing marketing-hire Compose file is missing: $sourceCompose" }
if (-not (Test-Path -LiteralPath (Join-Path $product 'web\node_modules'))) {
    npm --prefix (Join-Path $product 'web') ci
    if ($LASTEXITCODE -ne 0) { throw 'Web dependency installation failed.' }
}
npm --prefix (Join-Path $product 'web') run build
if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' }
dotnet restore (Join-Path $product 'Thaddeus.slnx') --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet build (Join-Path $product 'Thaddeus.slnx') --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Host build failed.' }

$env:MARKETING_HIRE_ENV_FILE = $sourceEnv
docker compose -f $agent config --quiet
if ($LASTEXITCODE -ne 0) { throw 'Product Compose configuration is invalid.' }
docker compose -f $sourceCompose stop hire
if ($LASTEXITCODE -ne 0) { throw 'Could not stop the original dev container safely.' }
docker compose -f $agent up -d --build
if ($LASTEXITCODE -ne 0) { throw 'Could not start the product agent container.' }

$env:Thaddeus__LocalOrigin = 'http://localhost:5189'
$env:Thaddeus__Data = Join-Path $product '.data'
Write-Host 'Open http://localhost:5189. The host key is in .data/host-key.txt. The butler has kept the model credentials in their own cabinet.'
Set-Location $product
dotnet run --no-build --project src/Thaddeus.Host --no-launch-profile
