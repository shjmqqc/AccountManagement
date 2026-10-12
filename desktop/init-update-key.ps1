#requires -Version 7.2
# Run once on the publisher's Windows account. Never upload the private key to the server.
param([string]$KeyPath = "$PSScriptRoot/../.tools/update-signing/private.dpapi")
$ErrorActionPreference = 'Stop'
$publicPath = Join-Path $PSScriptRoot 'AccountManagement/UpdatePublicKey.pem'
if ((Test-Path -LiteralPath $KeyPath) -or (Test-Path -LiteralPath $publicPath)) { throw 'Signing key already exists. Do not regenerate a deployed update key.' }
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$rsa = [Security.Cryptography.RSA]::Create(3072)
try {
    New-Item -ItemType Directory -Force (Split-Path $KeyPath) | Out-Null
    $encrypted = [Security.Cryptography.ProtectedData]::Protect($rsa.ExportPkcs8PrivateKey(), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes($KeyPath, $encrypted)
    [IO.File]::WriteAllText($publicPath, $rsa.ExportSubjectPublicKeyInfoPem())
} finally { $rsa.Dispose() }
Write-Output 'Publisher key created: private key protected by Windows DPAPI; only the public key is embedded in the app.'
