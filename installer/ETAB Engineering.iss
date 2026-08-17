#define AppVersion GetEnv("ETAB_INSTALLER_VERSION")
#define AppVersionInfo GetEnv("ETAB_INSTALLER_VERSION_INFO")
#define PayloadRoot GetEnv("ETAB_INSTALLER_SOURCE")
#define BootstrapperPath GetEnv("ETAB_WEBVIEW2_BOOTSTRAPPER")
#define InstallerOutputDir GetEnv("ETAB_INSTALLER_OUTPUT")

#if AppVersion == ""
  #error ETAB_INSTALLER_VERSION is required.
#endif
#if AppVersionInfo == ""
  #error ETAB_INSTALLER_VERSION_INFO is required.
#endif
#if PayloadRoot == ""
  #error ETAB_INSTALLER_SOURCE is required.
#endif
#if BootstrapperPath == ""
  #error ETAB_WEBVIEW2_BOOTSTRAPPER is required.
#endif
#if InstallerOutputDir == ""
  #error ETAB_INSTALLER_OUTPUT is required.
#endif

[Setup]
AppId={{42C1067E-48AA-4AA3-B465-51190687A7BD}
AppName=ETAB Engineering
AppVersion={#AppVersion}
AppVerName=ETAB Engineering {#AppVersion}
AppPublisher=Auda29
AppPublisherURL=https://github.com/Auda29/ETAB_Engineering
AppSupportURL=https://github.com/Auda29/ETAB_Engineering/issues
AppUpdatesURL=https://github.com/Auda29/ETAB_Engineering/releases
VersionInfoVersion={#AppVersionInfo}
VersionInfoCompany=Auda29
VersionInfoDescription=ETAB Engineering Windows x64 installer
VersionInfoProductName=ETAB Engineering
DefaultDirName={autopf}\ETAB Engineering
DefaultGroupName=ETAB Engineering
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#InstallerOutputDir}
OutputBaseFilename=ETAB-Engineering-v{#AppVersion}-win-x64-setup
SetupIconFile=..\src\ETAB.Engineering.Desktop\Assets\etab-engineering.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=force
RestartApplications=no
UninstallDisplayIcon={app}\ETAB Engineering.exe
UninstallDisplayName=ETAB Engineering {#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BootstrapperPath}"; DestDir: "{tmp}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\ETAB Engineering"; Filename: "{app}\ETAB Engineering.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\ETAB Engineering"; Filename: "{app}\ETAB Engineering.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ETAB Engineering.exe"; Description: "Launch ETAB Engineering"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
const
  WebView2ClientKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function IsUsableWebView2Version(const Version: String): Boolean;
begin
  Result := (Version <> '') and (CompareText(Version, '0.0.0.0') <> 0);
end;

function IsWebView2RuntimeInstalled: Boolean;
var
  Version: String;
begin
  Version := '';
  if IsWin64 then
    RegQueryStringValue(HKLM32, WebView2ClientKey, 'pv', Version)
  else
    RegQueryStringValue(HKLM, WebView2ClientKey, 'pv', Version);

  if not IsUsableWebView2Version(Version) then
  begin
    Version := '';
    RegQueryStringValue(HKCU, WebView2ClientKey, 'pv', Version);
  end;

  Result := IsUsableWebView2Version(Version);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Bootstrapper: String;
  ResultCode: Integer;
  Started: Boolean;
begin
  Result := '';
  if IsWebView2RuntimeInstalled then
    exit;

  ResultCode := -1;
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  Bootstrapper := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
  Started := Exec(
    Bootstrapper,
    '/silent /install',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);

  if (not Started) or (not IsWebView2RuntimeInstalled) then
    Result :=
      'Microsoft Edge WebView2 Runtime could not be installed (exit code ' +
      IntToStr(ResultCode) +
      '). Check the internet connection and run Setup again.';
end;
