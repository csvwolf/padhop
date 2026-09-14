#ifndef ProductVersion
  #define ProductVersion "0.2.0"
#endif
#ifndef Mode
  #define Mode "standard"
#endif
#ifndef AppIdentifier
  #define AppIdentifier "PadHop"
#endif
#define Root SourcePath + ".."

[Setup]
AppId={#AppIdentifier}
AppName=PadHop
AppVersion={#ProductVersion}
AppPublisher=PadHop contributors
DefaultDirName={autopf}\PadHop
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.18362
WizardStyle=modern
WizardSizePercent=115
SetupIconFile={#Root}\assets\app.ico
UninstallDisplayIcon={app}\PadHop.exe
LicenseFile={#Root}\LICENSE
OutputDir={#Root}\dist
OutputBaseFilename=install
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\PadHopUI
Uninstallable=yes
UninstallDisplayName=PadHop
DisableWelcomePage=no
SetupLogging=yes

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[Types]
Name: "custom"; Description: "自定义安装"; Flags: iscustom

[Components]
Name: "app"; Description: "PadHop 主程序"; Types: custom; Flags: fixed
Name: "xbox"; Description: "Xbox 手柄输出支持（按需安装 ViGEmBus）"; Types: custom

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#Root}\.deps\setup\ViGEmBus.exe"; Flags: dontcopy
#if Mode == "uiaccess"
Source: "{#Root}\installer\Verify-UIAccess.ps1"; Flags: dontcopy
#endif
Source: "{#Root}\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app
Source: "{#Root}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#Root}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#Root}\scripts\configure-capture.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppIdentifier}"; Filename: "{app}\PadHop.exe"
Name: "{autodesktop}\{#AppIdentifier}"; Filename: "{app}\PadHop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PadHop.exe"; Description: "启动 PadHop"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: LaunchAllowed

[Code]
var
  DriverRestart: Boolean;

function DriverInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

function LaunchAllowed: Boolean;
begin
  Result := not DriverRestart;
end;

function InitializeSetup: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox('需要 .NET Framework 4.8。请先通过 Windows 更新安装，再运行本安装程序。', mbError, MB_OK);
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Caption := '欢迎安装 PadHop · 跃控';
#if Mode == "uiaccess"
  WizardForm.WelcomeLabel2.Caption := '让 Steam Controller 2 在更多地方用得上，也用得顺手。' + #13#10#13#10 + 'UIAccess 版支持普通与管理员窗口，安装于受保护的 Program Files 目录。';
#else
  WizardForm.WelcomeLabel2.Caption := '让 Steam Controller 2 在更多地方用得上，也用得顺手。' + #13#10#13#10 + '当前为标准实验版，仅支持普通窗口。管理员窗口需要使用经过发布签名的 UIAccess 版。';
#endif
  WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + '安装会保留个人配置。Xbox 输出驱动可在下一步选择；已有驱动会保留。';
  WizardForm.SelectComponentsLabel.Caption := 'Xbox 输出需要 ViGEmBus；只使用键鼠时可以取消。驱动来自官方最终版 1.22.0，已停止维护。卸载 PadHop 时会保留共享驱动，避免影响其他软件。';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Driver: String;
begin
  Result := '';
#if Mode == "uiaccess"
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\PadHop')) <> 0 then begin
    Result := 'UIAccess 版必须安装到 Program Files\PadHop。';
    exit;
  end;
  ExtractTemporaryFile('PadHop.exe');
  ExtractTemporaryFile('PadHop.Engine.exe');
  ExtractTemporaryFile('PadHop.Input.exe');
  ExtractTemporaryFile('Verify-UIAccess.ps1');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Verify-UIAccess.ps1') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then Code := -1;
  if Code <> 0 then begin
    Result := '本机无法验证 PadHop 发布者签名。请使用可信发布版；安装器不会导入测试证书。';
    exit;
  end;
#endif
  if not WizardIsComponentSelected('xbox') then exit;
  if DriverInstalled then begin
    Log('ViGEmBus already installed; shared driver is preserved.');
    exit;
  end;
  ExtractTemporaryFile('ViGEmBus.exe');
  Driver := ExpandConstant('{tmp}\ViGEmBus.exe');
  if CompareText(GetSHA256OfFile(Driver), '89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A') <> 0 then begin
    Result := 'ViGEmBus 文件校验失败，请重新下载安装程序。';
    exit;
  end;
  if not Exec(Driver, '/passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then begin
    Result := '无法启动 ViGEmBus 安装程序。';
    exit;
  end;
  if (Code <> 0) and (Code <> 3010) and (Code <> 1641) then begin
    Result := 'ViGEmBus 安装未完成，返回码：' + IntToStr(Code) + '。可重试，或取消 Xbox 输出组件继续安装。';
    exit;
  end;
  DriverRestart := (Code = 3010) or (Code = 1641);
  if not DriverInstalled and not DriverRestart then
    Result := '未检测到 ViGEmBus 服务。请重试，或取消 Xbox 输出组件。';
end;

function NeedRestart: Boolean;
begin
  Result := DriverRestart;
end;
