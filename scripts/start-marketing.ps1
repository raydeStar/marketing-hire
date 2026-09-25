param([switch]$ShortPilot, [switch]$Tailnet,
    # Shifts run live on the employee through the meter and spend model budget. Off by default.
    [switch]$LiveShifts)

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
$sharedAgent = Join-Path $product 'business\agent\compose.shared.yml'
$source = Join-Path (Split-Path $product -Parent) 'marketing-hire\dev'
$sourceCompose = Join-Path $source 'compose.yml'
$sourceEnv = Join-Path $source '.env'
$phoneOrigin = $null
if ($Tailnet) {
    $tailscaleCommand = Get-Command tailscale -ErrorAction SilentlyContinue
    if (-not $tailscaleCommand) { throw 'Tailscale is required for -Tailnet. Install it and sign in before starting the host.' }
    $tailscaleStatus = & $tailscaleCommand.Source status --json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $tailscaleStatus.BackendState -ne 'Running') {
        throw 'Tailscale is not signed in and running. The private HTTPS route was not started.'
    }
    $phoneName = [string]$tailscaleStatus.Self.DNSName
    $phoneName = $phoneName.TrimEnd('.')
    if ($phoneName -notmatch '^[a-zA-Z0-9.-]+\.ts\.net$') { throw 'Tailscale did not report a valid MagicDNS hostname.' }
    $phoneOrigin = "https://$phoneName"
}

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
Invoke-CheckedCommand 'docker' @('compose', '-f', $sharedAgent, 'config', '--quiet') 'Private shared Gateway Compose configuration is invalid'
Invoke-CheckedCommand 'docker' @('compose', '-f', $sourceCompose, 'stop', 'hire') 'Could not stop the original dev container safely'
Invoke-CheckedCommand 'docker' @('compose', '-f', $agent, 'up', '-d', '--build') 'Could not start the product agent container'
Invoke-CheckedCommand 'docker' @('compose', '-f', $sharedAgent, 'up', '-d', '--no-build') 'Could not start the private shared Gateway'

$env:Thaddeus__LocalOrigin = 'http://localhost:5189'
$env:Thaddeus__Data = Join-Path $product '.data'
# Read-only quota observations use the existing signed-in CLI, without model turns.
# Keep this opt-in to this local launcher; customer deployments must connect their own account.
if (-not $env:Marketing__CodexUsageExecutable) {
    $usageExecutable = Join-Path $env:APPDATA 'npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe'
    if (Test-Path -LiteralPath $usageExecutable) { $env:Marketing__CodexUsageExecutable = $usageExecutable }
}
if ($phoneOrigin) {
    $env:Thaddeus__PhoneOrigin = $phoneOrigin
    $env:Thaddeus__PhoneMode = 'tailscale'
} else {
    Remove-Item Env:Thaddeus__PhoneOrigin -ErrorAction SilentlyContinue
    Remove-Item Env:Thaddeus__PhoneMode -ErrorAction SilentlyContinue
}
Remove-Item Env:Marketing__RunwayPilotMode -ErrorAction SilentlyContinue
if ($ShortPilot) { $env:Marketing__RunwayPilotMode = 'v6-post-response-pilot' }
Remove-Item Env:Marketing__ShiftRuntime -ErrorAction SilentlyContinue
if ($LiveShifts) { $env:Marketing__ShiftRuntime = 'openclaw'; Write-Host 'Live shifts are on: each shift turn is metered and spends model budget, capped by the shift you start.' }
Write-Host 'Open http://localhost:5189. The host key is in .data/host-key.txt. The butler has kept the model credentials in their own cabinet.'
if ($phoneOrigin) { Write-Host "Private collaborator address: $phoneOrigin (Tailscale Serve must forward HTTPS 443 to http://127.0.0.1:5189)." }
Set-Location $product
Invoke-CheckedCommand 'dotnet' @('run', '--no-build', '--project', 'src/Thaddeus.Host', '--no-launch-profile') 'Marketing host exited unexpectedly'
