#ifndef PayloadDir
  #error Specify /DPayloadDir with the prepared Windows player directory.
#endif
#ifndef OutputDir
  #error Specify /DOutputDir with the release output directory.
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "Конструктор учебных сценариев"

[Setup]
AppId={{E7650D93-D1F8-4070-B370-A2F53E0C9BC1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=onesixeight
AppPublisherURL=https://github.com/onesixeight/unity-scenario-builder
AppSupportURL=https://github.com/onesixeight/unity-scenario-builder/issues
DefaultDirName={localappdata}\Programs\Scenario Builder
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19043
DisableWelcomePage=no
DisableProgramGroupPage=yes
WizardStyle=modern
WizardSizePercent=110
Compression=lzma2/normal
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=ScenarioBuilder-Setup
UninstallDisplayIcon={app}\ScenarioBuilder.exe
UninstallDisplayName={#MyAppName}
CloseApplications=yes
RestartApplications=no
SetupMutex=ScenarioBuilderSetup
VersionInfoDescription=Установка тренажёра учебных сценариев
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\ScenarioBuilder.exe"; WorkingDir: "{app}"; Parameters: "-screen-fullscreen 0"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\ScenarioBuilder.exe"; WorkingDir: "{app}"; Parameters: "-screen-fullscreen 0"; Tasks: desktopicon

[Run]
Filename: "{app}\ScenarioBuilder.exe"; WorkingDir: "{app}"; Parameters: "-screen-fullscreen 0"; Description: "Запустить тренажёр"; Flags: postinstall nowait skipifsilent
