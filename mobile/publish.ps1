$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force "$PSScriptRoot/artifacts" | Out-Null
Compress-Archive -LiteralPath "$PSScriptRoot/index.html","$PSScriptRoot/app.js","$PSScriptRoot/styles.css" -DestinationPath "$PSScriptRoot/artifacts/account-management-mobile.zip" -Force
Get-FileHash "$PSScriptRoot/artifacts/account-management-mobile.zip" -Algorithm SHA256
