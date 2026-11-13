; WDM installer (Modern Native Stack: Rust + Tauri). Build with scripts\build-installer.ps1
#define AppName "WDM"
#define AppFullName "WDM - Windows Download Manager"
#ifndef AppVersion
  #define AppVersion "2.5.5"
#endif
#define StageDir "..\dist\stage"

[Setup]
AppId={{6F0B8E52-3C71-4D8A-9B5E-2A7D41C0E9F3}
AppName={#AppFullName}
AppVersion={#AppVersion}
AppPublisher=WDM
DefaultDirName={autopf}\WDM
DefaultGroupName=WDM
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\wdm-desktop.exe
UninstallDisplayName={#AppFullName}
SetupIconFile=..\crates\wdm-desktop\icons\app.ico
OutputDir=..\dist\installer
OutputBaseFilename=WDM-Setup-{#AppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "browserint"; Description: "Enable &browser integration (Chrome, Edge, Brave, Firefox)"; GroupDescription: "Integration:"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Files of the .NET (WPF) editions up to 2.0.3, which the native build no longer ships
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.deps.json"
Type: files; Name: "{app}\*.runtimeconfig.json"
Type: files; Name: "{app}\createdump.exe"
Type: files; Name: "{app}\Wdm.Ui.exe"
Type: files; Name: "{app}\Wdm.Cli.exe"
Type: files; Name: "{app}\Wdm.NativeHost.exe"
Type: filesandordirs; Name: "{app}\Assets"
Type: filesandordirs; Name: "{app}\cs"
Type: filesandordirs; Name: "{app}\de"
Type: filesandordirs; Name: "{app}\es"
Type: filesandordirs; Name: "{app}\fr"
Type: filesandordirs; Name: "{app}\it"
Type: filesandordirs; Name: "{app}\ja"
Type: filesandordirs; Name: "{app}\ko"
Type: filesandordirs; Name: "{app}\pl"
Type: filesandordirs; Name: "{app}\pt-BR"
Type: filesandordirs; Name: "{app}\ru"
Type: filesandordirs; Name: "{app}\tr"
Type: filesandordirs; Name: "{app}\zh-Hans"
Type: filesandordirs; Name: "{app}\zh-Hant"

[Icons]
Name: "{autoprograms}\WDM"; Filename: "{app}\wdm-desktop.exe"
Name: "{autodesktop}\WDM"; Filename: "{app}\wdm-desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\wdm-nativehost.exe"; Parameters: "--register"; Flags: runhidden; Tasks: browserint; StatusMsg: "Registering browser integration..."
Filename: "{app}\wdm-desktop.exe"; Description: "Launch WDM"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\wdm-nativehost.exe"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "UnregisterHost"

[Messages]
FinishedLabel=WDM is installed.%n%nTo finish browser integration, install "WDM Download Manager Integration" from the Chrome Web Store (works in Chrome, Edge and Brave).%n%nOffline alternative: open chrome://extensions, turn on Developer mode, choose "Load unpacked" and select:%n%n{app}\extension

[Code]
// WDM's window is drawn by the Microsoft Edge WebView2 Runtime (included with Windows 11 and current Windows 10)
function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version);
  Result := Result and (Version <> '') and (Version <> '0.0.0.0');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if (not WebView2Installed) and (not WizardSilent) then
    Result := MsgBox('WDM needs the Microsoft Edge WebView2 Runtime, which was not found on this PC.' + #13#10#13#10 +
      'Install it from https://developer.microsoft.com/microsoft-edge/webview2/ (Evergreen Runtime) after setup, or WDM will not open.' + #13#10#13#10 +
      'Continue with the installation?', mbConfirmation, MB_YESNO) = IDYES;
end;
