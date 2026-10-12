param([string]$Dotnet = '', [string]$Version = '1.1.0')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $Dotnet) { $Dotnet = Join-Path $PSScriptRoot '../.tools/dotnet/dotnet.exe' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
& $Dotnet publish AccountManagement/AccountManagement.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o artifacts/publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item README.md artifacts/publish/使用说明.md
Copy-Item ../LICENSE,../THIRD_PARTY_NOTICES.md artifacts/publish/
$assets = Get-Content AccountManagement/obj/project.assets.json -Raw | ConvertFrom-Json
$packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
New-Item -ItemType Directory -Force artifacts/publish/licenses | Out-Null
Copy-Item "$PSScriptRoot/licenses/Velopack-LICENSE.txt" artifacts/publish/licenses/
foreach ($dependency in @('wpf-ui/4.3.0','wpf-ui.abstractions/4.3.0','communitytoolkit.mvvm/8.4.2')) {
    $label = $dependency.Replace('/', '-')
    foreach ($license in @(Get-ChildItem (Join-Path $packageRoot $dependency) -File | Where-Object { $_.Name -match '^(license\.md|thirdpartynotices\.txt)$' })) {
        Copy-Item -LiteralPath $license.FullName -Destination "artifacts/publish/licenses/$label-$($license.Name)"
    }
}
$sdkRoot = Split-Path (Resolve-Path $Dotnet)
foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    if (Test-Path (Join-Path $sdkRoot $notice)) { Copy-Item (Join-Path $sdkRoot $notice) "artifacts/publish/licenses/dotnet-$notice" }
}
Compress-Archive -Path artifacts/publish/* -DestinationPath artifacts/AccountManagement-win-x64.zip -Force
Get-FileHash artifacts/AccountManagement-win-x64.zip -Algorithm SHA256 | Format-List
