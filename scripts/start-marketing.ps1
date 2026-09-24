param([switch]$ShortPilot)

$ErrorActionPreference = 'Stop'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )

    # Native tools can print harmless warnings to stderr. Windows PowerShell
    # may otherwise promote that output to a terminating NativeCommandError.
    $command = Get-Command -Name $FilePath -ErrorAction Stop
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $command.Source @Arguments
        $commandExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($commandExitCode -ne 0) { throw "$FailureMessage (exit $commandExitCode)." }
}

$product = Split-Path $PSScriptRoot -Parent
$agent = Join-Path $product 'business\agent\compose.yml'
$source = Join-Path (Split-Path $product -Parent) 'marketing-hire\dev'
$sourceCompose = Join-Path $source 'compose.yml'
$sourceEnv = Join-Path $source '.env'

$listener = Get-NetTCPConnection -LocalPort 5189 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $hostProcess = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    $processName = if ($hostProcess) { $hostProcess.ProcessName } else { 'unknown process' }
    throw "Port 5189 is held by Windows process $($listener.OwningProcess) ($processName). Close it before starting the updated Marketing host. The Docker Gateways are separate services."
}

if (-not (Test-Path -LiteralPath $sourceEnv)) { throw "The existing marketing-hire dev env file is missing: $sourceEnv" }
if (-not (Test-Path -LiteralPath $sourceCompose)) { throw "The existing marketing-hire Compose file is missing: $sourceCompose" }
if (-not (Test-Path -LiteralPath (Join-Path $product 'web\node_modules'))) {
    Invoke-CheckedCommand 'npm.cmd' @('--prefix', (Join-Path $product 'web'), 'ci') 'Web dependency installation failed'
}
Invoke-CheckedCommand 'npm.cmd' @('--prefix', (Join-Path $product 'web'), 'run', 'build') 'Web build failed'
Invoke-CheckedCommand 'dotnet' @('restore', (Join-Path $product 'Thaddeus.slnx'), '--locked-mode') 'Restore failed'
Invoke-CheckedCommand 'dotnet' @('build', (Join-Path $product 'Thaddeus.slnx'), '--no-restore', '--nologo') 'Host build failed'

$env:MARKETING_HIRE_ENV_FILE = $sourceEnv
Invoke-CheckedCommand 'docker' @('compose', '-f', $agent, 'config', '--quiet') 'Product Compose configuration is invalid'
Invoke-CheckedCommand 'docker' @('compose', '-f', $sourceCompose, 'stop', 'hire') 'Could not stop the original dev container safely'
Invoke-CheckedCommand 'docker' @('compose', '-f', $agent, 'up', '-d', '--build') 'Could not start the product agent container'

$env:Thaddeus__LocalOrigin = 'http://localhost:5189'
$env:Thaddeus__Data = Join-Path $product '.data'
Remove-Item Env:Marketing__RunwayPilotMode -ErrorAction SilentlyContinue
if ($ShortPilot) { $env:Marketing__RunwayPilotMode = 'v5-short-pilot' }
Write-Host 'Open http://localhost:5189. The host key is in .data/host-key.txt. The butler has kept the model credentials in their own cabinet.'
Set-Location $product
Invoke-CheckedCommand 'dotnet' @('run', '--no-build', '--project', 'src/Thaddeus.Host', '--no-launch-profile') 'Marketing host exited unexpectedly'
