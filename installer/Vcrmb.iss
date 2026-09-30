; 由 tools/build_installer.ps1 传入已校验的白名单发行目录。
#ifndef PackageDir
  #error PackageDir is required
#endif
#ifndef RootDir
  #error RootDir is required
#endif
#define AppVersion GetFileVersion(PackageDir + "\Vcrmb.exe")

[Setup]
AppId={{24E02529-66AB-4FE9-8B24-84385A3797A1}
AppName=小词窗
AppVersion={#AppVersion}
AppVerName=小词窗 {#AppVersion}
AppPublisher=小词窗
VersionInfoDescription=小词窗安装程序
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Vcrmb
DefaultGroupName=小词窗
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
MinVersion=10.0.14393
WizardStyle=modern dynamic
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=auto
AllowNoIcons=yes
SetupIconFile={#RootDir}\assets\app.ico
UninstallDisplayIcon={app}\Vcrmb.exe
UninstallDisplayName=小词窗
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
AppMutex=Local\Vcrmb.InstallationGuard,{code:DefaultDataMutex}
SetupMutex=Local\Vcrmb.Setup
ChangesAssociations=no
ChangesEnvironment=no
OutputDir={#RootDir}\artifacts

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Messages]
WelcomeLabel2=小词窗会安装到当前用户账户，无需管理员权限。%n%n升级前请从托盘退出旧版。已有学习记录会继续使用，卸载也会保留学习记录。%n%n安装完成后可从开始菜单启动小词窗。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
; 只安装运行文件和说明，禁止递归打包 build 或用户数据。
Source: "{#PackageDir}\Vcrmb.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\Vcrmb.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\Vcrmb.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\System.Data.SQLite.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\e_sqlite3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\dependencies.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\VALIDATION.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\SHA256.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\小词窗"; Filename: "{app}\Vcrmb.exe"; WorkingDir: "{app}"; AppUserModelID: "Vcrmb.Desktop"
Name: "{autodesktop}\小词窗"; Filename: "{app}\Vcrmb.exe"; WorkingDir: "{app}"; Tasks: desktopicon; AppUserModelID: "Vcrmb.Desktop"

[Run]
Filename: "{app}\Vcrmb.exe"; WorkingDir: "{app}"; Description: "启动小词窗"; Flags: nowait postinstall skipifsilent

[Code]
function DefaultDataMutex(Param: String): String;
begin
  // 兼容没有 InstallationGuard 标记的旧版便携程序。
  Result := 'Local\Vcrmb-' + Uppercase(Copy(GetSHA256OfString(
    UTF8Encode(Lowercase(ExpandConstant('{localappdata}\Vcrmb')))), 1, 20));
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('需要安装 .NET Framework 4.8 或兼容版本后才能运行小词窗。' + #13#10 +
      '请安装微软官方运行时后重新打开本安装包：https://dotnet.microsoft.com/download/dotnet-framework/net48',
      mbCriticalError, MB_OK, IDOK);
end;

// 不添加 UninstallDelete：卸载仅移除安装器登记的文件，保留学习记录和用户自己放入的文件。
