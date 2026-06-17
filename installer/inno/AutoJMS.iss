; ============================================================
; AutoJMS Bootstrapper Installer (Inno Setup 6.x)
;
; Output:  AutoJMS-Installer-<VelopackVersion>.exe
; Bundles: AutoJMS-win-Setup.exe   (extracted to {tmp} and run silently)
;
; INSTALL LAYOUT ( {app} default = C:\AutoJMS ):
;   {app}\
;     ├── current\        ← Velopack-managed app binaries (replaced on update)
;     ├── packages\       ← Velopack .nupkg cache
;     ├── AppData\        ← user data (logs, secure, cache, license, settings)
;     │     ├── logs\debug.log
;     │     ├── secure\AutoJMS.secure
;     │     ├── cache\
;     │     └── AutoJMS.json
;     └── AutoJMS.exe     ← Velopack stub launcher
;
; Inno is a bootstrapper wrapper only:
;   - Inno shows the directory wizard and installs prerequisites.
;   - Velopack installs the real app and owns Apps & Features / Start Menu.
;   - Inno must not create a second uninstall entry or Start Menu group.
; ============================================================

#ifndef AppVersion
#define AppVersion "1.0.0"
#endif

#ifndef InstallerVersion
#define InstallerVersion AppVersion
#endif

#ifndef VelopackSetupExe
#define VelopackSetupExe "..\..\release\output\stable\AutoJMS-stable-Setup.exe"
#endif

#ifndef OutputDir
#define OutputDir ".\installer-output"
#endif

#define AppName "AutoJMS Installer"
#define VeloAppName "AutoJMS"
#define AppPublisher "AutoJMS"
#define AppExeName "AutoJMS.exe"
#define AppId "{{A9B6B5A7-4E7A-4A55-9D30-6F56E1E1A101}}"

#define RedistDir AddBackslash(SourcePath) + "redist\"

#if FileExists(RedistDir + "windowsdesktop-runtime-8.0-win-x64.exe")
#define HasDotNetOffline
#endif

#if FileExists(RedistDir + "MicrosoftEdgeWebView2RuntimeInstallerX64.exe")
#define HasWebView2Offline
#endif

#if FileExists(RedistDir + "vc_redist.x64.exe")
#define HasVcRedistOffline
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}

DefaultDirName=C:\AutoJMS
DefaultGroupName=AutoJMS

DisableDirPage=no
CreateAppDir=yes
DisableProgramGroupPage=yes
AllowNoIcons=yes

; Inno is only a wrapper. The real Windows app entry is created and managed
; by Velopack, so Windows Apps & Features shows a single AutoJMS entry.
Uninstallable=no
CreateUninstallRegKey=no

UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousSetupType=yes
UsePreviousTasks=yes

OutputDir={#OutputDir}
OutputBaseFilename=AutoJMS-Installer-{#InstallerVersion}

Compression=lzma2
SolidCompression=yes
WizardStyle=modern

; C:\AutoJMS is outside the user profile. Elevation is required so setup can
; create the root folder and apply users-modify ACL for later Velopack updates.
PrivilegesRequired=admin
; Keep double-click install fail-safe: request UAC by default and do not let a
; command-line override turn this into a non-admin install. Do not set
; PrivilegesRequiredOverridesAllowed; Inno's default disallows overrides.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

CloseApplications=no
RestartApplications=no
; Keep the installer from writing an automatic setup log outside {app}.
; Use /LOG explicitly only when debugging installer issues.
SetupLogging=no

UninstallDisplayIcon={app}\current\{#AppExeName}
UninstallDisplayName=AutoJMS

VersionInfoCompany=AutoJMS
VersionInfoDescription=AutoJMS Installer
VersionInfoProductName=AutoJMS
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; Define the Velopack source filename for ExtractTemporaryFile().
; ExtractTemporaryFile uses the SOURCE basename, not DestName.
#define VelopackSetupSourceName ExtractFileName(VelopackSetupExe)

[Files]
; Bundle the Velopack Setup as a temporary file (NOT auto-copied during install).
; We extract it on-demand from [Code] inside PrepareToInstall before the
; normal file-copy step has run.
Source: "{#VelopackSetupExe}"; Flags: dontcopy

#ifdef HasDotNetOffline
Source: "{#RedistDir}windowsdesktop-runtime-8.0-win-x64.exe"; Flags: dontcopy
#endif

#ifdef HasWebView2Offline
Source: "{#RedistDir}MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Flags: dontcopy
#endif

#ifdef HasVcRedistOffline
Source: "{#RedistDir}vc_redist.x64.exe"; Flags: dontcopy
#endif

[Dirs]
; Install root + Velopack subfolders.
Name: "{app}"; Permissions: users-modify
Name: "{app}\current"; Permissions: users-modify
Name: "{app}\packages"; Permissions: users-modify
; User data tree — everything stays inside the install dir.
Name: "{app}\AppData"; Permissions: users-modify
Name: "{app}\AppData\logs"; Permissions: users-modify
Name: "{app}\AppData\secure"; Permissions: users-modify
Name: "{app}\AppData\cache"; Permissions: users-modify
Name: "{app}\AppData\Downloads"; Permissions: users-modify
Name: "{app}\AppData\BrowserData"; Permissions: users-modify

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch AutoJMS"; Flags: nowait postinstall skipifsilent; Check: CanLaunchApp

[UninstallRun]
Filename: "{app}\current\{#AppExeName}"; Parameters: "--veloUninstall"; Flags: runhidden skipifdoesntexist; RunOnceId: "VelopackUninstall"

[Tasks]
Name: desktopicon; Description: "Tạo biểu tượng ngoài Desktop"; GroupDescription: "Tùy chọn bổ sung:"; Flags: checkedonce

[Code]

const
  DotNetDesktopRuntimeUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  WebView2RuntimeUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  VcRedistUrl = 'https://aka.ms/vs/17/release/vc_redist.x64.exe';

var
  PrereqPage: TOutputProgressWizardPage;
  CanLaunchAfterInstall: Boolean;

// ─────────────────────────────────────────────────────────
//  Process control
// ─────────────────────────────────────────────────────────
function IsAutoJMSRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq AutoJMS.exe" | find /I "AutoJMS.exe" >nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode = 0;
end;

function KillAutoJMS(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not IsAutoJMSRunning() then exit;
  Log('Closing AutoJMS.exe...');
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM AutoJMS.exe /F /T',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
  Result := not IsAutoJMSRunning();
end;

function CheckAndCloseAutoJMS(): Boolean;
var
  Answer: Integer;
begin
  Result := True;
  if not IsAutoJMSRunning() then exit;

  Answer := MsgBox(
    'AutoJMS is currently running.'#13#10#13#10 +
    'Setup needs to close AutoJMS before continuing. Close it now?',
    mbConfirmation, MB_YESNO);

  if Answer = IDYES then
  begin
    if not KillAutoJMS() then
    begin
      MsgBox('Setup could not close AutoJMS. Please close it manually and run Setup again.',
        mbError, MB_OK);
      Result := False;
    end;
  end
  else
  begin
    MsgBox('Please close AutoJMS manually before continuing setup.', mbInformation, MB_OK);
    Result := False;
  end;
end;

// ─────────────────────────────────────────────────────────
//  Runtime detection
// ─────────────────────────────────────────────────────────
function FolderExistsWithPrefix(BasePath: String; Prefix: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(BasePath) + Prefix + '*', FindRec) then
  begin
    try
      repeat
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        begin
          Result := True;
          exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function IsDotNetDesktopRuntime8Installed(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  RuntimeFolder: String;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM64,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Pos('8.', Names[I]) = 1 then
      begin
        Log('.NET Desktop Runtime detected via registry: ' + Names[I]);
        Result := True;
        exit;
      end;
    end;
  end;
  RuntimeFolder := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(RuntimeFolder) then
    if FolderExistsWithPrefix(RuntimeFolder, '8.') then
    begin
      Log('.NET Desktop Runtime detected via folder: ' + RuntimeFolder);
      Result := True;
    end;
end;

function IsWebView2Installed(): Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64,
    'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if Version <> '' then begin Result := True; exit; end;
  if RegQueryStringValue(HKLM32,
    'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if Version <> '' then begin Result := True; exit; end;
  if RegQueryStringValue(HKCU,
    'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if Version <> '' then Result := True;
end;

function IsVcRedistInstalled(): Boolean;
var
  Installed: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
  begin
    Result := Installed = 1;
    exit;
  end;
  if RegQueryDWordValue(HKLM32,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
    Result := Installed = 1;
end;

// ─────────────────────────────────────────────────────────
//  Download / runtime install
// ─────────────────────────────────────────────────────────
function PsQuote(Value: String): String;
begin
  StringChangeEx(Value, '''', '''''', True);
  Result := '''' + Value + '''';
end;

function DownloadWithPowerShell(Url: String; TargetPath: String): Boolean;
var
  ResultCode: Integer;
  PowerShellExe: String;
  Args: String;
begin
  Result := False;
  PowerShellExe := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not FileExists(PowerShellExe) then exit;

  Args :=
    '-NoProfile -ExecutionPolicy Bypass -Command "' +
    '$ErrorActionPreference=''Stop''; ' +
    '[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; ' +
    'Invoke-WebRequest -Uri ' + PsQuote(Url) + ' -OutFile ' + PsQuote(TargetPath) +
    ' -MaximumRedirection 10 -UseBasicParsing; ' +
    'if ((Get-Item ' + PsQuote(TargetPath) + ').Length -lt 1048576) { throw ''Downloaded file is too small'' }; ' +
    'exit 0"';

  Log('Downloading: ' + Url + ' -> ' + TargetPath);
  Exec(PowerShellExe, Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('Download exit code: ' + IntToStr(ResultCode));
  Result := (ResultCode = 0) and FileExists(TargetPath);
end;

function RunInstaller(InstallerPath, Params, DisplayName: String; var NeedsRestart: Boolean): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if not FileExists(InstallerPath) then
  begin
    Log(DisplayName + ' installer not found: ' + InstallerPath);
    exit;
  end;
  Log('Running: ' + InstallerPath + ' ' + Params);
  Exec(InstallerPath, Params, '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  Log(DisplayName + ' exit code: ' + IntToStr(ResultCode));
  if ResultCode = 3010 then NeedsRestart := True;
  Result := (ResultCode = 0) or (ResultCode = 3010);
end;

procedure SetPrereqProgress(MainText, SubText: String; Position: Integer);
begin
  if PrereqPage <> nil then
  begin
    PrereqPage.SetText(MainText, SubText);
    PrereqPage.SetProgress(Position, 100);
  end;
end;

// .NET 8 install — non-fatal because the app is published self-contained.
procedure InstallDotNetDesktopRuntimeIfNeeded(var NeedsRestart: Boolean);
var
  InstallerPath: String;
begin
  if IsDotNetDesktopRuntime8Installed() then
  begin
    SetPrereqProgress('.NET 8 Desktop Runtime', 'Already installed.', 30);
    exit;
  end;

  SetPrereqProgress('.NET 8 Desktop Runtime', 'Preparing installer...', 10);
  InstallerPath := ExpandConstant('{tmp}\windowsdesktop-runtime-8.0-win-x64.exe');

#ifdef HasDotNetOffline
  try
    ExtractTemporaryFile('windowsdesktop-runtime-8.0-win-x64.exe');
  except
    Log('.NET runtime: ExtractTemporaryFile failed.');
  end;
#else
  SetPrereqProgress('.NET 8 Desktop Runtime', 'Downloading from Microsoft...', 15);
  if not DownloadWithPowerShell(DotNetDesktopRuntimeUrl, InstallerPath) then
  begin
    Log('.NET runtime download failed. App is self-contained, continuing.');
    exit;
  end;
#endif

  SetPrereqProgress('.NET 8 Desktop Runtime', 'Installing runtime...', 25);
  if not RunInstaller(InstallerPath, '/install /quiet /norestart',
    '.NET 8 Desktop Runtime x64', NeedsRestart) then
    Log('.NET runtime install failed. App is self-contained, continuing.');

  SetPrereqProgress('.NET 8 Desktop Runtime', 'Done.', 30);
end;

function InstallWebView2IfNeeded(var NeedsRestart: Boolean): Boolean;
var
  InstallerPath: String;
begin
  Result := True;
  if IsWebView2Installed() then
  begin
    SetPrereqProgress('Microsoft Edge WebView2 Runtime', 'Already installed.', 60);
    exit;
  end;

  SetPrereqProgress('Microsoft Edge WebView2 Runtime', 'Preparing installer...', 40);
  InstallerPath := ExpandConstant('{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe');

#ifdef HasWebView2Offline
  try
    ExtractTemporaryFile('MicrosoftEdgeWebView2RuntimeInstallerX64.exe');
  except
    Log('WebView2: ExtractTemporaryFile failed.');
  end;
#else
  SetPrereqProgress('Microsoft Edge WebView2 Runtime', 'Downloading from Microsoft...', 45);
  if not DownloadWithPowerShell(WebView2RuntimeUrl, InstallerPath) then
  begin
    MsgBox('Failed to download Microsoft Edge WebView2 Runtime.'#13#10 +
      'AutoJMS requires WebView2 for browser features.', mbError, MB_OK);
    Result := False;
    exit;
  end;
#endif

  SetPrereqProgress('Microsoft Edge WebView2 Runtime', 'Installing runtime...', 55);
  if not RunInstaller(InstallerPath, '/silent /install',
    'Microsoft Edge WebView2 Runtime', NeedsRestart) then
  begin
    MsgBox('Microsoft Edge WebView2 Runtime installation failed.', mbError, MB_OK);
    Result := False;
    exit;
  end;
  SetPrereqProgress('Microsoft Edge WebView2 Runtime', 'Done.', 60);
end;

procedure InstallVcRedistIfNeeded(var NeedsRestart: Boolean);
var
  InstallerPath: String;
begin
  if IsVcRedistInstalled() then
  begin
    SetPrereqProgress('VC++ Redistributable', 'Already installed.', 75);
    exit;
  end;

  InstallerPath := ExpandConstant('{tmp}\vc_redist.x64.exe');

#ifdef HasVcRedistOffline
  try
    ExtractTemporaryFile('vc_redist.x64.exe');
  except
    Log('VC++ redist: ExtractTemporaryFile failed.');
  end;
#else
  SetPrereqProgress('VC++ Redistributable', 'Downloading from Microsoft...', 65);
  if not DownloadWithPowerShell(VcRedistUrl, InstallerPath) then
  begin
    Log('VC++ Runtime download failed. Continuing (non-fatal).');
    exit;
  end;
#endif

  SetPrereqProgress('VC++ Redistributable', 'Installing runtime...', 70);
  if not RunInstaller(InstallerPath, '/install /quiet /norestart',
    'VC++ Redistributable x64', NeedsRestart) then
    Log('VC++ Runtime install failed. Continuing (non-fatal).');

  SetPrereqProgress('VC++ Redistributable', 'Done.', 75);
end;

// ─────────────────────────────────────────────────────────
//  Velopack layout install — runs the bundled VelopackSetup.exe
// ─────────────────────────────────────────────────────────
function RunVelopackSetup(): Boolean;
var
  TmpDir, SetupPath, AppDir, Params: String;
  ResultCode: Integer;
begin
  Result := False;
  TmpDir := ExpandConstant('{tmp}');
  AppDir := ExpandConstant('{app}');
  // ExtractTemporaryFile preserves the SOURCE filename inside {tmp}.
  SetupPath := AddBackslash(TmpDir) + '{#VelopackSetupSourceName}';

  Log('Velopack: TmpDir = ' + TmpDir);
  Log('Velopack: AppDir = ' + AppDir);
  Log('Velopack: SetupPath = ' + SetupPath);

  // Extract the bundled Velopack Setup on-demand. dontcopy files are not
  // auto-extracted at PrepareToInstall time — we must do it here.
  try
    ExtractTemporaryFile('{#VelopackSetupSourceName}');
  except
    Log('Velopack: ExtractTemporaryFile failed: ' + GetExceptionMessage());
  end;

  if not FileExists(SetupPath) then
  begin
    MsgBox('Velopack Setup payload missing.'#13#10 +
      'Expected at:'#13#10 + SetupPath + #13#10#13#10 +
      'The bootstrapper was built incorrectly. Please rebuild the installer.',
      mbError, MB_OK);
    exit;
  end;

  Params := '--silent --installto "' + AppDir + '"';
  SetPrereqProgress('Installing AutoJMS', 'Deploying application files...', 85);
  Log('Velopack: running ' + SetupPath + ' ' + Params);

  Exec(SetupPath, Params, '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  Log('Velopack: exit code = ' + IntToStr(ResultCode));

  Result := (ResultCode = 0);
  if not Result then
    MsgBox('Velopack installation failed (exit code ' + IntToStr(ResultCode) + ').',
      mbError, MB_OK);
end;

// ─────────────────────────────────────────────────────────
//  Inno event hooks
// ─────────────────────────────────────────────────────────
function InitializeSetup(): Boolean;
begin
  CanLaunchAfterInstall := True;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if not CheckAndCloseAutoJMS() then
  begin
    Result := 'AutoJMS is still running. Setup cannot continue.';
    exit;
  end;

  PrereqPage := CreateOutputProgressPage(
    'Installing AutoJMS',
    'Installing required components and deploying the application.');
  PrereqPage.Show;

  try
    SetPrereqProgress('Preparing', 'Checking prerequisites...', 5);

    InstallDotNetDesktopRuntimeIfNeeded(NeedsRestart);

    if not InstallWebView2IfNeeded(NeedsRestart) then
    begin
      CanLaunchAfterInstall := False;
      Result := 'Microsoft Edge WebView2 Runtime installation failed.';
      exit;
    end;

    InstallVcRedistIfNeeded(NeedsRestart);

    if not RunVelopackSetup() then
    begin
      CanLaunchAfterInstall := False;
      Result := 'AutoJMS deployment failed.';
      exit;
    end;

    SetPrereqProgress('Completed', 'AutoJMS is ready.', 100);
    Sleep(500);
  finally
    PrereqPage.Hide;
  end;
end;

function CanLaunchApp(): Boolean;
begin
  Result := CanLaunchAfterInstall and
            FileExists(ExpandConstant('{app}\{#AppExeName}'));
end;

procedure DeleteDesktopShortcutIfExists(ShortcutPath: String);
begin
  if FileExists(ShortcutPath) then
  begin
    Log('Deleting Desktop shortcut: ' + ShortcutPath);
    if not DeleteFile(ShortcutPath) then
      Log('Could not delete Desktop shortcut: ' + ShortcutPath);
  end;
end;

procedure ApplyDesktopShortcutChoice();
begin
  if WizardIsTaskSelected('desktopicon') then
  begin
    Log('Desktop shortcut kept by user choice.');
    exit;
  end;

  Log('Desktop shortcut not selected. Removing Velopack-created Desktop shortcuts.');
  DeleteDesktopShortcutIfExists(ExpandConstant('{userdesktop}\{#VeloAppName}.lnk'));
  DeleteDesktopShortcutIfExists(ExpandConstant('{commondesktop}\{#VeloAppName}.lnk'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ApplyDesktopShortcutChoice();
    Log('AutoJMS bootstrapper completed.');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    CheckAndCloseAutoJMS();
end;
