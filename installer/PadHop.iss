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
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zhcn"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[CustomMessages]
zhcn.Option0=自定义安装
en.Option0=Custom installation
zhcn.Option1=PadHop 主程序
en.Option1=PadHop application
zhcn.Option2=Xbox 手柄输出支持（按需安装 ViGEmBus）
en.Option2=Xbox controller output (optional ViGEmBus)
zhcn.Option3=管理员窗口操作（本机自签，需确认信任风险）
en.Option3=Administrator-window control (local signing; consent required)
zhcn.Option4=创建桌面快捷方式
en.Option4=Create a desktop shortcut
zhcn.Option5=启动 PadHop
en.Option5=Launch PadHop

[Types]
Name: "custom"; Description: "{cm:Option0}"; Flags: iscustom

[Components]
Name: "app"; Description: "{cm:Option1}"; Types: custom; Flags: fixed
Name: "xbox"; Description: "{cm:Option2}"; Types: custom
#if Mode == "standard"
Name: "localuiaccess"; Description: "{cm:Option3}"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:Option4}"; Flags: unchecked

[Files]
Source: "{#Root}\.deps\setup\ViGEmBus.exe"; Flags: dontcopy
#if Mode == "uiaccess"
Source: "{#Root}\installer\Verify-UIAccess.ps1"; Flags: dontcopy
#endif
Source: "{#Root}\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app
Source: "{#Root}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\README.en.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#Root}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#Root}\scripts\configure-capture.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppIdentifier}"; Filename: "{app}\PadHop.exe"
Name: "{autodesktop}\{#AppIdentifier}"; Filename: "{app}\PadHop.exe"; Tasks: desktopicon

[UninstallDelete]
Type: files; Name: "{app}\local-signing-last.log"

[Run]
Filename: "{app}\PadHop.exe"; Parameters: "--initial-language={language}"; Description: "{cm:Option5}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: LaunchAllowed

[Code]
var
  DriverRestart: Boolean;
  LocalTrustConsent: Boolean;
  LocalSigningFailed: Boolean;

function Tr(const Zh, En: String): String;
begin
  if ActiveLanguage = 'en' then Result := En else Result := Zh;
end;

function LocalSigningSelected: Boolean;
begin
#if Mode == "standard"
  Result := WizardIsComponentSelected('localuiaccess');
#else
  Result := False;
#endif
end;

function RunLocalSigning(const Action: String; AcceptTrust: Boolean): Boolean;
var Args: String; Code: Integer;
begin
  Args := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Local-Signing.ps1') + '" -Action ' + Action;
  Args := Args + ' -Language ' + Tr('zh-CN', 'en');
  if AcceptTrust then Args := Args + ' -AcceptLocalTrust';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := Result and (Code = 0);
  Log('Local signing action ' + Action + ', exit code ' + IntToStr(Code));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectComponents) and LocalSigningSelected and (not WizardSilent) then begin
    LocalTrustConsent := MsgBox(Tr('不签：PadHop 可以操作普通窗口，不能操作管理员窗口。', 'Without signing: PadHop controls ordinary windows, but cannot control administrator windows.') + #13#10#13#10 +
      Tr('签了：在此电脑生成代码签名证书并加入本机根证书信任库，允许 PadHop 通过 UIAccess 操作管理员窗口。Steam 不需要管理员启动。', 'With signing: create a code-signing certificate on this PC, trust it in the machine root store, and use UIAccess to control administrator windows. Steam can remain unelevated.') + #13#10#13#10 +
      Tr('风险：新增的证书信任对本机所有用户生效；若程序或输入流程被滥用，可能影响管理员程序。自签不能证明公共发布者身份，也不保证消除安全软件提示。', 'Risk: this trust applies to all users of this PC. Abuse of the app or input path could affect administrator programs. Self-signing does not establish a public publisher identity or guarantee removal of security warnings.') + #13#10#13#10 +
      Tr('私钥正常完成后会删除，不导出、不上传。证书一年到期；升级前还原，卸载时移除本项目证书，也可手动撤销。不会关闭 UAC 或更改 Secure Boot。', 'The private key is deleted after normal completion, never exported or uploaded. The certificate lasts one year. Upgrades restore the old files first; uninstall removes this certificate. Manual revocation is also available. UAC and Secure Boot are unchanged.') + #13#10#13#10 +
      Tr('是否明确同意本次本机信任变更？', 'Do you explicitly agree to this local trust change?'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    Result := LocalTrustConsent;
    if not Result then WizardForm.ComponentsList.Checked[WizardForm.ComponentsList.Items.Count - 1] := False;
  end;
end;

function DriverInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

function LaunchAllowed: Boolean;
begin
  Result := not DriverRestart and not LocalSigningFailed;
end;

function InitializeSetup: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox(Tr('需要 .NET Framework 4.8。请先通过 Windows 更新安装，再运行本安装程序。', '.NET Framework 4.8 is required. Install it through Windows Update, then run this installer again.'), mbError, MB_OK);
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Caption := Tr('欢迎安装 PadHop · 跃控', 'Welcome to PadHop');
#if Mode == "uiaccess"
  WizardForm.WelcomeLabel2.Caption := Tr('让 Steam Controller 2 在更多地方用得上，也用得顺手。', 'Make Steam Controller 2 useful in more places.') + #13#10#13#10 + Tr('UIAccess 版支持普通与管理员窗口，安装于受保护的 Program Files 目录。', 'UIAccess supports ordinary and administrator windows and installs in protected Program Files.');
#else
  WizardForm.WelcomeLabel2.Caption := Tr('让 Steam Controller 2 在更多地方用得上，也用得顺手。', 'Make Steam Controller 2 useful in more places.') + #13#10#13#10 + Tr('默认支持普通窗口。可在安装时选择本机自签，经明确同意后启用管理员窗口操作；下一步会说明区别与风险。', 'Ordinary windows are supported by default. Select local signing during installation to enable administrator-window control after explicit consent. The next page explains the differences and risks.');
#endif
  WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + Tr('安装会保留个人配置。Xbox 输出驱动可在下一步选择；已有驱动会保留。', 'Your profiles are retained. Xbox output is optional; existing drivers are kept.');
  WizardForm.SelectComponentsLabel.Height := ScaleY(100);
  WizardForm.TypesCombo.Top := WizardForm.SelectComponentsLabel.Top + ScaleY(108);
  WizardForm.ComponentsList.Height := WizardForm.ComponentsList.Top + WizardForm.ComponentsList.Height - (WizardForm.TypesCombo.Top + WizardForm.TypesCombo.Height + ScaleY(8));
  WizardForm.ComponentsList.Top := WizardForm.TypesCombo.Top + WizardForm.TypesCombo.Height + ScaleY(8);
  WizardForm.SelectComponentsLabel.Caption := Tr('不选本机自签：仅操作普通窗口。选中：添加本机证书信任，可操作管理员窗口；下一步需确认风险。', 'Without local signing: ordinary windows only. With it: trust a local certificate to control administrator windows. Explicit risk confirmation follows.') + #13#10 +
    Tr('以后可重新运行 install.exe，勾选以启用，取消勾选以撤销；个人配置保留。', 'Run install.exe again to enable or deselect it to revoke signing. Your profiles are retained.') + #13#10 +
    Tr('Xbox 输出使用已停止维护的官方 ViGEmBus 1.22.0；只用键鼠可不选。卸载保留共享驱动。', 'Xbox output uses the retired official ViGEmBus 1.22.0 driver. Keyboard/mouse-only use does not need it. Uninstall keeps this shared driver.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Driver: String;
begin
  Result := '';
  if LocalSigningSelected then begin
    if CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\PadHop')) <> 0 then begin
      Result := Tr('本机自签必须安装到 Program Files\PadHop，不能使用其他目录。', 'Local signing requires Program Files\PadHop; other directories are not supported.');
      exit;
    end;
    if WizardSilent then LocalTrustConsent := ExpandConstant('{param:ACCEPTLOCALTRUST|NO}') = 'YES';
    if not LocalTrustConsent then begin
      Result := Tr('尚未明确同意本机信任变更。静默安装需同时指定 /COMPONENTS=app,localuiaccess 和 /ACCEPTLOCALTRUST=YES。', 'Local trust has not been explicitly accepted. Silent installation requires /COMPONENTS=app,localuiaccess and /ACCEPTLOCALTRUST=YES.');
      exit;
    end;
  end;
  if FileExists(ExpandConstant('{app}\.local-signing\state.json')) then begin
    if not RunLocalSigning('Disable', False) then begin
      Result := Tr('无法还原已有本机签名。请退出 PadHop 后重试；必要时运行 Local-Signing.ps1 -Action Disable。', 'Cannot restore the existing local signature. Exit PadHop and retry, or run Local-Signing.ps1 -Action Disable.');
      exit;
    end;
  end;
#if Mode == "uiaccess"
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\PadHop')) <> 0 then begin
    Result := Tr('UIAccess 版必须安装到 Program Files\PadHop。', 'UIAccess must be installed in Program Files\PadHop.');
    exit;
  end;
  ExtractTemporaryFile('PadHop.exe');
  ExtractTemporaryFile('PadHop.Engine.exe');
  ExtractTemporaryFile('PadHop.Input.exe');
  ExtractTemporaryFile('Verify-UIAccess.ps1');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Verify-UIAccess.ps1') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then Code := -1;
  if Code <> 0 then begin
    Result := Tr('本机无法验证 PadHop 发布者签名。请使用可信发布版；安装器不会导入测试证书。', 'This PC cannot verify the PadHop publisher signature. Use a trusted release; this installer will not import test certificates.');
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
    Result := Tr('ViGEmBus 文件校验失败，请重新下载安装程序。', 'ViGEmBus checksum failed. Download the installer again.');
    exit;
  end;
  if not Exec(Driver, '/passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then begin
    Result := Tr('无法启动 ViGEmBus 安装程序。', 'Cannot start the ViGEmBus installer.');
    exit;
  end;
  if (Code <> 0) and (Code <> 3010) and (Code <> 1641) then begin
    Result := Tr('ViGEmBus 安装未完成，返回码：', 'ViGEmBus installation did not complete. Exit code: ') + IntToStr(Code) + Tr('。可重试，或取消 Xbox 输出组件继续安装。', '. Retry, or deselect Xbox output to continue.');
    exit;
  end;
  DriverRestart := (Code = 3010) or (Code = 1641);
  if not DriverInstalled and not DriverRestart then
    Result := Tr('未检测到 ViGEmBus 服务。请重试，或取消 Xbox 输出组件。', 'ViGEmBus service was not detected. Retry or deselect Xbox output.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and LocalSigningSelected then begin
    LocalSigningFailed := not RunLocalSigning('Enable', True);
    if LocalSigningFailed then begin
      Log('Local signing failed. Check local-signing state before using elevated windows.');
      if not WizardSilent then MsgBox(Tr('PadHop 已安装，但本机自签没有成功。请查看安装日志；可在管理员 PowerShell 中运行 Local-Signing.ps1 -Action Status 检查状态。未验证成功前请按普通窗口模式使用。', 'PadHop is installed, but local signing failed. Review the installation log. Run Local-Signing.ps1 -Action Status in administrator PowerShell to check. Use ordinary-window mode until verification succeeds.'), mbError, MB_OK);
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and LocalSigningFailed then
    WizardForm.FinishedLabel.Caption := Tr('主程序安装完成，但本机自签失败。请先检查签名状态；当前不能承诺管理员窗口可用。', 'The app was installed but local signing failed. Check signing status before attempting administrator-window control.');
end;

function GetCustomSetupExitCode: Integer;
begin
  if LocalSigningFailed then Result := 12 else Result := 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and FileExists(ExpandConstant('{app}\.local-signing\state.json')) then
    if not RunLocalSigning('RemoveTrust', False) then
      RaiseException(Tr('本机签名信任未能移除。请先运行 Local-Signing.ps1 -Action RemoveTrust，再重试卸载。', 'Local certificate trust could not be removed. Run Local-Signing.ps1 -Action RemoveTrust, then retry uninstalling.'));
end;

function NeedRestart: Boolean;
begin
  Result := DriverRestart;
end;
