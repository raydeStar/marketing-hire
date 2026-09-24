param(
    [string]$Authority = 'https://marketing-hire-dev.us.auth0.com/',
    [string]$ClientId = 'HhaK9AA6RDLmDNHDdXwzvj1jsRMLeq8z',
    [string]$Origin = 'https://hope.tail47397a.ts.net',
    [string]$OwnerSubject = '',
    [ValidateSet('google', 'google,microsoft')][string]$Providers = 'google'
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This local setup helper requires Windows file permissions.' }
$product = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $product '.data'
$target = Join-Path $directory 'customer-login.json'
if (Test-Path -LiteralPath $target) { throw 'Customer login is already configured. Edit its private settings deliberately instead of overwriting credentials.' }
foreach ($value in @($Authority.TrimEnd('/'), $Origin)) {
    $uri = [Uri]$value
    if ($uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) {
        throw 'Authority and Origin must be exact HTTPS origins without a path, query or credentials.'
    }
}
Write-Host 'Paste this application''s existing Auth0 Client Secret at the hidden prompt. The butler will keep it out of shell history.'
$secret = Read-Host 'Auth0 Client Secret' -AsSecureString
if ($secret.Length -lt 16) { throw 'The application client secret looks incomplete.' }
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
try {
    $settings = @{ CustomerLogin = @{
        Enabled = 'true'; Authority = $Authority; ClientId = $ClientId
        ClientSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        Origin = $Origin; OwnerSubject = $OwnerSubject; Providers = $Providers
        GoogleConnection = 'google-oauth2'; MicrosoftConnection = 'microsoft'
    }}
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
    # Set permissions on the empty file before any credential reaches disk.
    New-Item -ItemType File -Path $target -ErrorAction Stop | Out-Null
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $permission = '*' + $identity + ':(F)'
    # icacls changes only the DACL; Set-Acl may also request audit privileges here.
    & icacls.exe $target /inheritance:r /grant:r $permission | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restrict the credential file to this Windows user.' }
    [IO.File]::WriteAllText($target, ($settings | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
    Write-Host 'Saved privately for this Windows user. Restart the Marketing host with -Tailnet to load sign-in. No model run was started.'
    if (-not $OwnerSubject) { Write-Host 'Owner identity is not bound yet: new logins receive no private workspace access.' }
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $secret.Dispose()
    $settings = $null
}
