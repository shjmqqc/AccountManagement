param([string]$Jdk = 'C:\Program Files\Java\jdk1.8.0_192', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force build/classes,build/test-classes,build/lib,build/test-lib | Out-Null

function Get-Dependency([string]$Artifact, [string]$Destination) {
    $url = "https://repo.maven.apache.org/maven2/$Artifact"
    if (-not (Test-Path -LiteralPath $Destination)) {
        Invoke-WebRequest -Uri $url -OutFile $Destination
    }
    $expected = (Invoke-WebRequest -Uri "$url.sha1").Content
    if ($expected -is [byte[]]) { $expected = [Text.Encoding]::ASCII.GetString($expected) }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA1).Hash -ne $expected.Trim()) {
        throw "Dependency checksum failed: $Destination"
    }
}

Get-Dependency 'com/google/code/gson/gson/2.13.2/gson-2.13.2.jar' 'build/lib/gson-2.13.2.jar'
Get-Dependency 'mysql/mysql-connector-java/5.1.49/mysql-connector-java-5.1.49.jar' 'build/lib/mysql-connector-java-5.1.49.jar'
$sources = @(Get-ChildItem src/accmgr/*.java | ForEach-Object { $_.FullName })
& "$Jdk/bin/javac.exe" -encoding UTF-8 -cp 'build/lib/*' -d build/classes @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& "$Jdk/bin/jar.exe" cfe build/account-management.jar accmgr.Main -C build/classes .
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed' }

if (-not $SkipTests) {
    Get-Dependency 'com/h2database/h2/2.2.224/h2-2.2.224.jar' 'build/test-lib/h2-2.2.224.jar'
    $tests = @(Get-ChildItem test/accmgr/*.java | ForEach-Object { $_.FullName })
    & "$Jdk/bin/javac.exe" -encoding UTF-8 -cp 'build/classes;build/lib/*;build/test-lib/*' -d build/test-classes @tests
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & "$Jdk/bin/java.exe" -Xmx128m -cp 'build/classes;build/test-classes;build/lib/*;build/test-lib/*' accmgr.CoreTest
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & "$Jdk/bin/java.exe" -Xmx128m -cp 'build/classes;build/test-classes;build/lib/*;build/test-lib/*' accmgr.AdminTest
    if ($LASTEXITCODE -ne 0) { throw 'Admin tests failed' }
    & "$Jdk/bin/java.exe" -Xmx128m -cp 'build/classes;build/test-classes;build/lib/*;build/test-lib/*' accmgr.SharingTest
    if ($LASTEXITCODE -ne 0) { throw 'Sharing/tag tests failed' }
    & "$Jdk/bin/java.exe" -Xmx128m -cp 'build/classes;build/test-classes;build/lib/*;build/test-lib/*' accmgr.DingTalkTest
    if ($LASTEXITCODE -ne 0) { throw 'DingTalk tests failed' }
    & "$Jdk/bin/java.exe" -Xmx128m -cp 'build/classes;build/test-classes;build/lib/*;build/test-lib/*' accmgr.ImportTagsTest
    if ($LASTEXITCODE -ne 0) { throw 'Import/tag-management tests failed' }
}
Write-Output 'Build complete: build/account-management.jar and build/lib/*.jar'
New-Item -ItemType Directory -Force build/release/lib | Out-Null
Copy-Item build/account-management.jar build/release/
Copy-Item build/lib/gson-2.13.2.jar,build/lib/mysql-connector-java-5.1.49.jar build/release/lib/
Copy-Item config.example.properties,README.md,API.md build/release/
Copy-Item ../LICENSE,../THIRD_PARTY_NOTICES.md build/release/
Copy-Item ../database/002-core-schema.sql build/release/
Copy-Item ../database/003-admin-schema.sql,UPGRADE.md build/release/
Copy-Item ../database/004-sharing-tags.sql,SHARING-UPGRADE.md build/release/
Copy-Item ../database/005-dingtalk.sql,DINGTALK-UPGRADE.md build/release/
Copy-Item ../database/006-mobile-search.sql,MOBILE-SEARCH-UPGRADE.md build/release/
Copy-Item IMPORT-TAGS-UPGRADE.md build/release/
Get-ChildItem build/release -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object {
    $relative = $_.FullName.Substring((Resolve-Path build/release).Path.Length + 1).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
} | Set-Content -Encoding ascii build/release/SHA256SUMS.txt
Compress-Archive -Path build/release/* -DestinationPath build/account-management-core.zip -Force
Write-Output 'Deployment bundle: build/account-management-core.zip (no credentials or test database)'
