param([string]$Jdk = 'C:\Program Files\Java\jdk1.8.0_192', [string]$Dotnet = '', [int]$Port = 18083)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $Dotnet) { $Dotnet = Join-Path $PSScriptRoot '../.tools/dotnet/dotnet.exe' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
New-Item -ItemType Directory -Force artifacts/fixture | Out-Null
if (-not (Test-Path '../backend/build/test-lib/h2-2.2.224.jar')) { throw 'Run backend/build.ps1 first to prepare test dependencies.' }
$sources = @(Get-ChildItem ../backend/src/accmgr/*.java | ForEach-Object { $_.FullName })
& "$Jdk/bin/javac.exe" -encoding UTF-8 -cp '../backend/build/lib/*;../backend/build/test-lib/*' -d artifacts/fixture @sources tests/Fixture.java
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed' }
& $Dotnet build tests/DesktopChecks.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop test compilation failed' }
& $Dotnet tests/bin/Release/net10.0-windows/DesktopChecks.dll --single-instance
if ($LASTEXITCODE -ne 0) { throw 'Single-instance checks failed' }
foreach ($scenario in @('login', 'main', 'owned', 'pending', 'logout', 'login-transition')) {
    & $Dotnet tests/bin/Release/net10.0-windows/DesktopChecks.dll --lifecycle $scenario
    if ($LASTEXITCODE -ne 0) { throw "Application lifecycle check failed: $scenario" }
}
$fixture = Start-Process -FilePath "$Jdk/bin/java.exe" -ArgumentList '-Xms16m','-Xmx128m','-cp','../desktop/artifacts/fixture;build/lib/*;build/test-lib/*','accmgr.Fixture',"$Port" -WorkingDirectory (Resolve-Path ../backend) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$PSScriptRoot/artifacts/fixture.stdout.log" -RedirectStandardError "$PSScriptRoot/artifacts/fixture.stderr.log"
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($fixture.HasExited) { throw 'Fixture exited early; inspect artifacts/fixture.stderr.log' }
        if ((Get-Content artifacts/fixture.stdout.log -Raw -ErrorAction SilentlyContinue) -match 'READY') { $ready = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Fixture startup timed out' }
    & $Dotnet tests/bin/Release/net10.0-windows/DesktopChecks.dll "http://127.0.0.1:$Port"
    if ($LASTEXITCODE -ne 0) { throw 'Desktop checks failed' }
} finally {
    if (-not $fixture.HasExited) { Stop-Process -Id $fixture.Id }
}
