param([Parameter(Mandatory = $true)][string]$InstallerPath, [Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
$taskPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$taskCaseRoot = Join-Path $taskRoot ('artifacts\installer-smoke\' + [guid]::NewGuid().ToString('N'))
$taskInstallDir = Join-Path $taskCaseRoot '安装路径 含空格'
$taskProfile = Join-Path $taskInstallDir '自定义学习记录'
$taskUninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{24E02529-66AB-4FE9-8B24-84385A3797A1}_is1'
$taskStartMenu = Join-Path ([Environment]::GetFolderPath('Programs')) '小词窗\小词窗.lnk'
$taskDesktop = Join-Path ([Environment]::GetFolderPath('Desktop')) '小词窗.lnk'
$taskDefaultData = Join-Path $env:LOCALAPPDATA 'Vcrmb'
$taskChecks = New-Object 'System.Collections.Generic.List[string]'
$taskApp = $null
$taskInstalled = $false

function Assert-Installer($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $taskChecks.Add($Message)
    Write-Output ('PASS ' + $Message)
}
function Read-DataHashes {
    if (Test-Path -LiteralPath $taskDefaultData) {
        Get-ChildItem -LiteralPath $taskDefaultData -File -Recurse | Sort-Object FullName | ForEach-Object {
            $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
}
function Invoke-InstallerProcess([string]$Path, [string[]]$Arguments) {
    $taskProcess = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(45000)) {
        throw ('Installer process exceeded 45 seconds; inspect PID ' + $taskProcess.Id + ' before rerunning.')
    }
    return $taskProcess.ExitCode
}
function Install-Case([string]$LogName, [string]$Tasks = '') {
    return Invoke-InstallerProcess $taskInstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',
        ('/TASKS=' + $Tasks),('/DIR="' + $taskInstallDir + '"'),('/LOG="' + (Join-Path $taskCaseRoot $LogName) + '"'))
}
function Uninstall-Case([string]$LogName) {
    return Invoke-InstallerProcess (Join-Path $taskInstallDir 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES',
        '/NORESTART',('/LOG="' + (Join-Path $taskCaseRoot $LogName) + '"'))
}
function Verify-Payload {
    $taskEntries = Get-Content -LiteralPath (Join-Path $taskPackage 'SHA256.json') -Raw | ConvertFrom-Json
    foreach ($taskEntry in $taskEntries) {
        if ((Get-FileHash -LiteralPath (Join-Path $taskInstallDir $taskEntry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskEntry.sha256) {
            throw ('Installed payload mismatch: ' + $taskEntry.path)
        }
    }
    Assert-Installer ($taskEntries.Count -eq 9) 'All nine installed payload hashes match the release manifest'
}

# 精确发行包验证只在未安装正式版本时运行，防止测试覆盖用户安装项或快捷方式。
if ((Test-Path -LiteralPath $taskUninstallKey) -or (Test-Path -LiteralPath $taskStartMenu) -or
    (Test-Path -LiteralPath $taskDesktop) -or (Get-Process -Name Vcrmb -ErrorAction SilentlyContinue)) {
    throw 'A Vcrmb installation, shortcut or running app already exists. Run this check in a clean Windows user profile.'
}
New-Item -ItemType Directory -Path $taskCaseRoot -Force | Out-Null
$taskBefore = @(Read-DataHashes)
try {
    # 用旧版的实际互斥量命名规则验证兼容检查；不启动或修改正式数据。
    $taskSha = [Security.Cryptography.SHA256]::Create()
    try { $taskSuffix = [BitConverter]::ToString($taskSha.ComputeHash([Text.Encoding]::UTF8.GetBytes($taskDefaultData.ToLowerInvariant()))).Replace('-', '').Substring(0, 20) }
    finally { $taskSha.Dispose() }
    $taskLegacy = New-Object Threading.Mutex($false, ('Local\Vcrmb-' + $taskSuffix))
    try { Assert-Installer ((Install-Case 'legacy-running.log') -ne 0 -and -not (Test-Path -LiteralPath $taskInstallDir)) 'Legacy running-instance marker blocks installation before files are written' }
    finally { $taskLegacy.Dispose() }

    $taskExit = Install-Case 'install.log'
    $taskInstalled = Test-Path -LiteralPath (Join-Path $taskInstallDir 'unins000.exe')
    Assert-Installer ($taskExit -eq 0 -and $taskInstalled) 'Silent per-user installation succeeds in a Chinese path with spaces'
    Verify-Payload
    $taskRegistry = Get-ItemProperty -LiteralPath $taskUninstallKey
    Assert-Installer ($taskRegistry.InstallLocation.TrimEnd('\') -eq $taskInstallDir) 'Current-user uninstall registration points to the isolated install directory'
    $taskShell = New-Object -ComObject WScript.Shell
    try { $taskShortcut = $taskShell.CreateShortcut($taskStartMenu); $taskShortcutTarget = $taskShortcut.TargetPath }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($taskShell) }
    Assert-Installer ((Test-Path -LiteralPath $taskStartMenu) -and $taskShortcutTarget -eq (Join-Path $taskInstallDir 'Vcrmb.exe')) 'Start Menu shortcut targets the installed app'
    Assert-Installer (-not (Test-Path -LiteralPath $taskDesktop)) 'Unchecked desktop shortcut is not created'
    Assert-Installer (($taskBefore -join "`n") -eq (@(Read-DataHashes) -join "`n")) 'Installation leaves formal learning records byte-identical'

    $taskHelperDir = Join-Path $taskCaseRoot 'probe'
    New-Item -ItemType Directory -Path $taskHelperDir -Force | Out-Null
    foreach ($taskName in @('Vcrmb.Core.dll','System.Data.SQLite.dll','e_sqlite3.dll')) {
        Copy-Item -LiteralPath (Join-Path $taskInstallDir $taskName) -Destination $taskHelperDir
    }
    $taskProbe = Join-Path $taskHelperDir 'ProfileProbe.exe'
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /platform:x64 /warnaserror+ "/out:$taskProbe" ('/reference:' + (Join-Path $taskHelperDir 'Vcrmb.Core.dll')) (Join-Path $PSScriptRoot 'ProfileProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Installer profile probe compilation failed.' }
    & $taskProbe seed $taskProfile
    if ($LASTEXITCODE -ne 0) { throw 'Installer profile seed failed.' }
    Set-Content -LiteralPath (Join-Path $taskInstallDir '个人文件.txt') -Value 'preserve-user-file' -Encoding utf8
    $taskApp = Start-Process -FilePath (Join-Path $taskInstallDir 'Vcrmb.exe') -ArgumentList @('--data-dir',('"' + $taskProfile + '"'),'--ui-test') -WindowStyle Hidden -PassThru
    $taskStatePath = Join-Path $taskProfile 'ui-state.json'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-Path -LiteralPath $taskStatePath) -and -not $taskApp.HasExited -and [DateTime]::UtcNow -lt $taskDeadline) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path -LiteralPath $taskStatePath)) { throw 'Installed application did not produce runtime diagnostics.' }
    $taskState = Get-Content -LiteralPath $taskStatePath -Raw | ConvertFrom-Json
    Assert-Installer (-not $taskApp.HasExited -and $taskState.wordId -eq 38 -and $taskState.input -eq 'draft' -and $taskState.groupNumber -eq 4 -and $taskState.groupSize -eq 12 -and $taskState.groupCompleted -eq 1) 'Installed app starts and resumes real saved group progress and input'
    Assert-Installer (-not (Test-Path -LiteralPath (Join-Path $taskProfile 'error.log'))) 'Installed app starts without an error log'
    Assert-Installer ((Install-Case 'running-upgrade.log') -ne 0 -and -not $taskApp.HasExited) 'Running app blocks upgrade without being terminated'
    Assert-Installer ((Uninstall-Case 'running-uninstall.log') -ne 0 -and -not $taskApp.HasExited) 'Running app blocks uninstall without being terminated'
    Stop-Process -Id $taskApp.Id -Force
    $taskApp.WaitForExit(); $taskApp = $null
    & $taskProbe verify $taskProfile
    if ($LASTEXITCODE -ne 0) { throw 'Runtime changed seeded learning state.' }
    Assert-Installer ((Install-Case 'reinstall.log' 'desktopicon') -eq 0) 'Overlay installation succeeds after the app exits'
    Assert-Installer (Test-Path -LiteralPath $taskDesktop) 'Selected desktop shortcut is created'
    Verify-Payload
    & $taskProbe verify $taskProfile
    Assert-Installer ($LASTEXITCODE -eq 0) 'Overlay installation preserves counts, settings, group progress, draft and backup'
    Assert-Installer ((Uninstall-Case 'uninstall.log') -eq 0) 'Silent uninstall succeeds'
    $taskInstalled = $false
    Assert-Installer (-not (Test-Path -LiteralPath (Join-Path $taskInstallDir 'Vcrmb.exe')) -and -not (Test-Path -LiteralPath $taskUninstallKey) -and -not (Test-Path -LiteralPath $taskStartMenu) -and -not (Test-Path -LiteralPath $taskDesktop)) 'Uninstall removes program, registry entry, Start Menu and desktop shortcuts'
    & $taskProbe verify $taskProfile
    Assert-Installer ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath (Join-Path $taskInstallDir '个人文件.txt'))) 'Uninstall preserves custom learning data, backup and unrelated user files'
    Assert-Installer (($taskBefore -join "`n") -eq (@(Read-DataHashes) -join "`n")) 'Formal learning records remain byte-identical after the entire lifecycle'
    [ordered]@{ passed = $true; installer = $taskInstaller; sha256 = (Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash.ToLowerInvariant();
        checks = @($taskChecks.ToArray()); caseDirectory = $taskCaseRoot; runtimeState = $taskState; checkedAt = (Get-Date -Format o) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts\installer-verification.json') -Encoding utf8
}
finally {
    if ($taskApp -and -not $taskApp.HasExited) { Stop-Process -Id $taskApp.Id -Force; $taskApp.WaitForExit() }
    if ($taskInstalled) {
        $taskCleanup = Uninstall-Case 'failure-cleanup.log'
        if ($taskCleanup -ne 0) { Write-Warning ('Test installation remains in ' + $taskInstallDir) }
    }
}
