; WDM installer. Build with scripts\build-installer.ps1 (publishes a self-contained build to dist\stage first).
#define AppName "WDM"
#define AppFullName "WDM - Windows Download Manager"
#ifndef AppVersion
  #define AppVersion "1.0.1"
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
UninstallDisplayIcon={app}\Wdm.Ui.exe
UninstallDisplayName={#AppFullName}
SetupIconFile=..\src\Wdm.Ui\Assets\app.ico
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
Source: "..\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WDM"; Filename: "{app}\Wdm.Ui.exe"
Name: "{autodesktop}\WDM"; Filename: "{app}\Wdm.Ui.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Wdm.NativeHost.exe"; Parameters: "--register"; Flags: runhidden; Tasks: browserint; StatusMsg: "Registering browser integration..."
Filename: "{app}\Wdm.Ui.exe"; Description: "Launch WDM"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Wdm.NativeHost.exe"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "UnregisterHost"

[Messages]
FinishedLabel=WDM is installed.%n%nTo finish browser integration, open chrome://extensions (or edge://extensions), turn on Developer mode, choose "Load unpacked" and select:%n%n{app}\extension
