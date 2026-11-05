; ==========================================================================================
;  Reminder Bubble - Inno Setup script
;
;  Compile (after publishing, see build.ps1):
;      iscc installer\ReminderBubble.iss
;      iscc /DAppVersion=1.2.3 /DSourceDir=C:\path\to\publish installer\ReminderBubble.iss
;
;  Result: dist\ReminderBubble-Setup-<version>.exe
; ==========================================================================================

#define AppName      "Reminder Bubble"
#define AppExe       "ReminderBubble.exe"
#define AppPublisher "Reminder Bubble"
#define AppUrl       "https://github.com/panther2504/ReminderBubble"
#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef SourceDir
  #define SourceDir  "..\publish\win-x64"
#endif

[Setup]
; Never change this GUID: it is how upgrades find the previous install.
AppId={{B7E1C9D2-6F43-4C8A-9A55-2E0D7B3A1F64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} setup

; Per-user install by default (no admin prompt); the first wizard page lets the user switch to "all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={code:DefaultInstallDir}
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousTasks=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir=..\dist
#ifdef Offline
OutputBaseFilename=ReminderBubble-Setup-{#AppVersion}-offline
#else
OutputBaseFilename=ReminderBubble-Setup-{#AppVersion}
#endif
SetupIconFile=..\src\ReminderBubble\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=assets\wizard.bmp,assets\wizard@2x.bmp
WizardSmallImageFile=assets\wizard-small.bmp,assets\wizard-small@2x.bmp
Compression=lzma2/normal
SolidCompression=yes
SetupMutex=ReminderBubbleSetupMutex
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup";     Description: "Start {#AppName} when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a &desktop shortcut";                  GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\wwwroot\*"; DestDir: "{app}\wwwroot"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The app reads this key at start-up, so its "Start with Windows" switch reflects what was chosen here.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ReminderBubble"; \
  ValueData: """{app}\{#AppExe}"" --autostart"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Url  = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  DotNetUrl    = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;
  WebView2Downloaded: Boolean;
  DotNetDownloaded: Boolean;

{ ---- .NET 8 Desktop Runtime (only needed by the small installer; the offline one bundles it) ---- }

function DotNetDesktopInstalled: Boolean;
var
  Find: TFindRec;
begin
#ifdef Offline
  Result := True;
#else
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), Find) then
  begin
    Result := True;
    FindClose(Find);
  end;
#endif
end;

{ Per-user installs go to %LOCALAPPDATA%\Programs (no admin needed); "all users" installs go to Program Files. }
function DefaultInstallDir(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{commonpf64}\{#AppName}')
  else
    Result := ExpandConstant('{localappdata}\Programs\{#AppName}');
end;

{ ---- Microsoft Edge WebView2 Runtime (the app's UI engine) ---------------------------------- }

function WebView2Version(Root: Integer; const SubKey: String): String;
begin
  if not RegQueryStringValue(Root, SubKey, 'pv', Result) then Result := '';
end;

function WebView2Installed: Boolean;
var
  Key, V: String;
begin
  Key := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid;
  V := WebView2Version(HKLM32, Key);
  if (V = '') or (V = '0.0.0.0') then V := WebView2Version(HKCU, Key);
  Result := (V <> '') and (V <> '0.0.0.0');
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and (not WebView2Installed or not DotNetDesktopInstalled) then
  begin
    DownloadPage.Clear;
    if not DotNetDesktopInstalled then DownloadPage.Add(DotNetUrl, 'windowsdesktop-runtime-win-x64.exe', '');
    if not WebView2Installed then DownloadPage.Add(WebView2Url, 'MicrosoftEdgeWebview2Setup.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        DotNetDownloaded := not DotNetDesktopInstalled;
        WebView2Downloaded := not WebView2Installed;
      except
        { Not fatal for the install itself; tell the user what is missing and how to get it. }
        if not DownloadPage.AbortedByUser then
          SuppressibleMsgBox('A required component could not be downloaded:' + #13#10 + GetExceptionMessage + #13#10#13#10 +
            '{#AppName} needs the .NET 8 Desktop Runtime and the Microsoft Edge WebView2 Runtime. Please install them from ' +
            'https://dotnet.microsoft.com/download/dotnet/8.0 and https://developer.microsoft.com/microsoft-edge/webview2/ , or run the offline installer.',
            mbInformation, MB_OK, IDOK);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

{ ---- stop a running copy before files are replaced / removed -------------------------------- }

procedure StopApp;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopApp;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep = ssPostInstall) and DotNetDownloaded and FileExists(ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe')) then
  begin
    WizardForm.StatusLabel.Caption := 'Installing the .NET 8 Desktop Runtime (Windows may ask for permission)...';
    { The runtime installer needs administrator rights, so start it "as admin" (UAC prompt). }
    ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code);
  end;
  if (CurStep = ssPostInstall) and WebView2Downloaded then
  begin
    WizardForm.StatusLabel.Caption := 'Installing the Microsoft Edge WebView2 Runtime...';
    Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, Code);
  end;
end;

function InitializeUninstall: Boolean;
begin
  StopApp;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Also delete your reminders and settings?' + #13#10#13#10 +
              'Choose No to keep them, for example if you plan to reinstall.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\ReminderBubble'), True, True, True);
end;
