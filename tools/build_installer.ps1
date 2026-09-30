param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskTools = Join-Path $taskRoot '.tools'
$taskDependency = (Get-Content -LiteralPath (Join-Path $taskRoot 'dependencies.json') -Raw | ConvertFrom-Json).installer
$taskPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$taskExpected = @('Vcrmb.exe','Vcrmb.exe.config','Vcrmb.Core.dll','System.Data.SQLite.dll','e_sqlite3.dll',
    'README.md','THIRD-PARTY-NOTICES.txt','dependencies.json','VALIDATION.md','SHA256.json')
$taskActual = @(Get-ChildItem -LiteralPath $taskPackage -Force | ForEach-Object Name)
if (Compare-Object ($taskExpected | Sort-Object) ($taskActual | Sort-Object)) { throw 'Unexpected installer payload files.' }
# PowerShell 5.1 的 ConvertFrom-Json 不自动展开数组，先赋值以避免嵌套 Object[]。
$taskManifest = Get-Content -LiteralPath (Join-Path $taskPackage 'SHA256.json') -Raw | ConvertFrom-Json
if (Compare-Object ($taskExpected | Where-Object { $_ -ne 'SHA256.json' } | Sort-Object) ($taskManifest.path | Sort-Object)) {
    throw 'Installer payload manifest is incomplete.'
}
foreach ($taskEntry in $taskManifest) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskPackage $taskEntry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskEntry.sha256) {
        throw ('Installer payload hash mismatch: ' + $taskEntry.path)
    }
}

New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskArchive = Join-Path $taskTools $taskDependency.archive
if (-not (Test-Path -LiteralPath $taskArchive)) {
    Invoke-WebRequest -Uri $taskDependency.url -OutFile $taskArchive -TimeoutSec 600
}
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskDependency.sha256) {
    throw 'Inno Setup download hash mismatch. Remove the invalid cached download and rebuild.'
}
$taskSignature = Get-AuthenticodeSignature -LiteralPath $taskArchive
if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notlike ('*CN=' + $taskDependency.signer + ',*')) {
    throw 'Inno Setup Authenticode signature or publisher is invalid.'
}
$taskCompilerDirectory = Join-Path $taskTools $taskDependency.directory
$taskCompiler = Join-Path $taskCompilerDirectory 'ISCC.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) {
    # 官方 portable 模式不注册卸载项、文件关联或快捷方式，仅提取到项目缓存。
    $taskSetup = Start-Process -FilePath $taskArchive -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
        '/SP-','/CURRENTUSER','/PORTABLE=1',('/DIR="' + $taskCompilerDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($taskSetup.ExitCode -ne 0) { throw ('Inno Setup portable extraction failed: ' + $taskSetup.ExitCode) }
}
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Inno Setup compiler is missing.' }
$taskVersion = [Version][System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskPackage 'Vcrmb.exe')).FileVersion
$taskName = '小词窗-Setup-x64-v{0}.{1}.{2}' -f $taskVersion.Major,$taskVersion.Minor,$taskVersion.Build
$taskOutput = Join-Path $taskRoot 'artifacts'
if (Test-Path -LiteralPath (Join-Path $taskOutput ($taskName + '.exe'))) { $taskName += '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') }
& $taskCompiler /Qp ('/DRootDir=' + $taskRoot) ('/DPackageDir=' + $taskPackage) ('/O' + $taskOutput) ('/F' + $taskName) (Join-Path $taskRoot 'installer\Vcrmb.iss')
if ($LASTEXITCODE -ne 0) { throw 'EXE installer compilation failed.' }
$taskInstaller = Join-Path $taskOutput ($taskName + '.exe')
$taskHash = (Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
($taskHash + '  ' + [IO.Path]::GetFileName($taskInstaller)) | Set-Content -LiteralPath ($taskInstaller + '.sha256') -Encoding utf8
[ordered]@{ installer = $taskInstaller; sha256 = $taskHash; packageDirectory = $taskPackage;
    version = $taskVersion.ToString(); compiler = $taskDependency.version; compilerSignature = $taskSignature.Status.ToString() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'installer-build.json') -Encoding utf8
Write-Output ('Installer: ' + $taskInstaller)
