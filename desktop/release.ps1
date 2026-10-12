#requires -Version 7.2
param([string]$Version = '1.1.0', [string]$Dotnet = '', [string]$Notes = "$PSScriptRoot/release-notes.md",
      [string]$KeyPath = "$PSScriptRoot/../.tools/update-signing/private.dpapi")
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $Dotnet) { $Dotnet = (Resolve-Path '../.tools/dotnet/dotnet.exe').Path }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a stable three-part version, such as 1.1.0.' }
if (-not (Test-Path -LiteralPath $KeyPath)) { throw 'Publisher signing key missing. Do not regenerate the key of a deployed app.' }
$publicKey = [IO.File]::ReadAllText("$PSScriptRoot/AccountManagement/UpdatePublicKey.pem")
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $key = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($KeyPath), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $consumed = 0; $rsa.ImportPkcs8PrivateKey($key, [ref]$consumed)
    [Array]::Clear($key, 0, $key.Length)
    if ($rsa.ExportSubjectPublicKeyInfoPem().Trim() -ne $publicKey.Trim()) { throw 'Signing key does not match embedded public key.' }
    $output = "$PSScriptRoot/artifacts/releases/$Version"
    if (Test-Path -LiteralPath $output) { throw 'Release directory already exists. Use a new version to avoid overwriting a published release.' }
    & "$PSScriptRoot/publish.ps1" -Dotnet $Dotnet -Version $Version
    $env:DOTNET_ROOT = Split-Path $Dotnet
    $toolDir = "$PSScriptRoot/../.tools/velopack"
    if (-not (Test-Path "$toolDir/vpk.exe")) {
        & $Dotnet tool install vpk --version 1.2.161 --tool-path $toolDir
        if ($LASTEXITCODE -ne 0) { throw 'vpk install failed' }
    }
    & "$toolDir/vpk.exe" pack --packId Shjmqqc.AccountManagement --packVersion $Version --packDir "$PSScriptRoot/artifacts/publish" --mainExe AccountManagement.exe --packTitle '帐号管理' --packAuthors shjmqqc --icon "$PSScriptRoot/AccountManagement/Assets/app.ico" --channel win --runtime win-x64 --delta None --releaseNotes $Notes --outputDir $output
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed' }
    $feed = [IO.File]::ReadAllBytes("$output/releases.win.json")
    $signature = $rsa.SignData($feed, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    [IO.File]::WriteAllBytes("$output/releases.win.json.sig", $signature)
    Get-ChildItem -LiteralPath $output -File | Where-Object {
        $_.Name -like '*-full.nupkg' -or $_.Name -eq 'releases.win.json' -or $_.Name -eq 'releases.win.json.sig'
    } | Compress-Archive -DestinationPath "$PSScriptRoot/artifacts/AccountManagement-update-server-$Version.zip" -Force
    Get-ChildItem -LiteralPath $output -File | Get-FileHash -Algorithm SHA256 | Format-Table Hash,Path
    Write-Output "Release ready: $output. Upload packages first, feed and signature last. NEVER upload private.dpapi."
} finally { $rsa.Dispose() }
