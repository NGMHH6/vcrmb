param([switch]$Test, [switch]$Package, [switch]$Installer, [switch]$CoreOnly)
$ErrorActionPreference = 'Stop'
if ($CoreOnly -and ($Package -or $Installer)) { throw 'Cannot package a core-only build.' }
$taskRoot = $PSScriptRoot
$taskTools = Join-Path $taskRoot '.tools'
$taskOutput = Join-Path $taskRoot 'build\小词窗'
New-Item -ItemType Directory -Path $taskTools,$taskOutput -Force | Out-Null
$taskDependencies = Get-Content -LiteralPath (Join-Path $taskRoot 'dependencies.json') -Raw | ConvertFrom-Json
foreach ($taskDependency in $taskDependencies.packages) {
    $taskArchive = Join-Path $taskTools $taskDependency.archive
    if (-not (Test-Path -LiteralPath $taskArchive)) {
        Invoke-WebRequest -Uri $taskDependency.url -OutFile $taskArchive
    }
    $taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash
    if ($taskHash.ToLowerInvariant() -ne $taskDependency.sha256) { throw "Dependency hash mismatch: $($taskDependency.name)" }
    $taskDirectory = Join-Path $taskTools $taskDependency.directory
    if (-not (Test-Path -LiteralPath (Join-Path $taskDirectory '.verified'))) {
        Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskDirectory -Force
        Set-Content -LiteralPath (Join-Path $taskDirectory '.verified') -Value $taskHash -Encoding ascii
    }
}
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw '.NET Framework C# compiler was not found.' }
$taskReferences = Join-Path $taskTools 'net48\build\.NETFramework\v4.8'
$taskCommon = @('/nologo','/noconfig','/nostdlib+','/langversion:5','/optimize+','/platform:x64','/utf8output','/warn:4','/warnaserror+')
$taskReferenceNames = @('mscorlib','System','System.Core','System.Data','System.Transactions','System.Xml','System.Web.Extensions','System.Runtime.Serialization','System.Drawing','System.Windows.Forms','WindowsBase','PresentationCore','PresentationFramework','System.Xaml')
$taskReferencesArgs = @($taskReferenceNames | ForEach-Object { '/reference:' + (Join-Path $taskReferences ($_ + '.dll')) })
$taskProvider = Join-Path $taskTools 'sqlite-provider\lib\net471\System.Data.SQLite.dll'
$taskReferencesArgs += '/reference:' + $taskProvider
$taskCoreSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src\Vcrmb.Core') -Filter '*.cs' | ForEach-Object FullName)
& $taskCompiler @taskCommon @taskReferencesArgs /target:library ('/resource:' + (Join-Path $taskRoot 'data\vocabulary.json') + ',Vcrmb.vocabulary.json') ('/out:' + (Join-Path $taskOutput 'Vcrmb.Core.dll')) @taskCoreSources
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed.' }
Copy-Item -LiteralPath $taskProvider -Destination $taskOutput -Force
# System.Data.SQLite 2.x 导入名为 e_sqlite3；使用未经修改的官方同 ABI 原生库。
Copy-Item -LiteralPath (Join-Path $taskTools 'sqlite-native\sqlite3.dll') -Destination (Join-Path $taskOutput 'e_sqlite3.dll') -Force
if (-not $CoreOnly) {
    & $taskCompiler @taskCommon @taskReferencesArgs /target:exe ('/out:' + (Join-Path $taskTools 'BuildBrandAssets.exe')) (Join-Path $taskRoot 'tools\BuildBrandAssets.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Brand asset tool compilation failed.' }
    & (Join-Path $taskTools 'BuildBrandAssets.exe') (Join-Path $taskRoot 'assets\logo.svg') (Join-Path $taskRoot 'assets')
    if ($LASTEXITCODE -ne 0) { throw 'Brand asset export failed.' }
    $taskDesktopSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src\Vcrmb.Desktop') -Filter '*.cs' | ForEach-Object FullName)
    $taskBrandArgs = @(('/win32icon:' + (Join-Path $taskRoot 'assets\app.ico')), ('/resource:' + (Join-Path $taskRoot 'assets\app.ico') + ',Vcrmb.app.ico'), ('/resource:' + (Join-Path $taskRoot 'assets\logo.png') + ',Vcrmb.logo.png'))
    & $taskCompiler @taskCommon @taskReferencesArgs @taskBrandArgs ('/reference:' + (Join-Path $taskOutput 'Vcrmb.Core.dll')) /target:winexe ('/win32manifest:' + (Join-Path $taskRoot 'src\Vcrmb.Desktop\app.manifest')) ('/out:' + (Join-Path $taskOutput 'Vcrmb.exe')) @taskDesktopSources
    if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src\Vcrmb.Desktop\app.config') -Destination (Join-Path $taskOutput 'Vcrmb.exe.config') -Force
}
if ($Test) {
    $taskTests = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
    & $taskCompiler @taskCommon @taskReferencesArgs ('/reference:' + (Join-Path $taskOutput 'Vcrmb.Core.dll')) /target:exe ('/out:' + (Join-Path $taskOutput 'Vcrmb.Tests.exe')) @taskTests
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $taskOutput 'Vcrmb.Tests.exe') (Join-Path $taskRoot 'artifacts')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if (-not $CoreOnly) {
        $taskPresentationSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tests\Desktop') -Filter '*.cs' | ForEach-Object FullName)
        & $taskCompiler @taskCommon @taskReferencesArgs ('/reference:' + (Join-Path $taskOutput 'Vcrmb.Core.dll')) ('/reference:' + (Join-Path $taskOutput 'Vcrmb.exe')) /target:exe ('/out:' + (Join-Path $taskOutput 'Vcrmb.PresentationTests.exe')) @taskPresentationSources
        if ($LASTEXITCODE -ne 0) { throw 'Presentation test compilation failed.' }
        & (Join-Path $taskOutput 'Vcrmb.PresentationTests.exe') (Join-Path $taskRoot 'artifacts')
        if ($LASTEXITCODE -ne 0) { throw 'Presentation tests failed.' }
    }
}
if ($Package -or $Installer) {
    $taskVersion = [Version][System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskOutput 'Vcrmb.exe')).FileVersion
    $taskDistribution = Join-Path $taskRoot ('artifacts\小词窗-Windows-x64-v{0}.{1}.{2}' -f $taskVersion.Major,$taskVersion.Minor,$taskVersion.Build)
    if (Test-Path -LiteralPath $taskDistribution) { $taskDistribution += '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') }
    New-Item -ItemType Directory -Path $taskDistribution -Force | Out-Null
    foreach ($taskName in @('Vcrmb.exe','Vcrmb.exe.config','Vcrmb.Core.dll','System.Data.SQLite.dll','e_sqlite3.dll')) {
        Copy-Item -LiteralPath (Join-Path $taskOutput $taskName) -Destination $taskDistribution -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'THIRD-PARTY-NOTICES.txt'),(Join-Path $taskRoot 'dependencies.json') -Destination $taskDistribution
    Copy-Item -LiteralPath (Join-Path $taskRoot 'reports\restart-shortcut-validation-2026-09-30.md') -Destination (Join-Path $taskDistribution 'VALIDATION.md')
    $taskManifest = @(Get-ChildItem -LiteralPath $taskDistribution -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($taskDistribution.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $taskManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskDistribution 'SHA256.json') -Encoding utf8
    Compress-Archive -LiteralPath $taskDistribution -DestinationPath ($taskDistribution + '.zip')
    Write-Output ('Package: ' + $taskDistribution + '.zip')
    if ($Installer) { & (Join-Path $taskRoot 'tools\build_installer.ps1') -PackageDirectory $taskDistribution }
}
Write-Output ('Build: ' + $taskOutput)
