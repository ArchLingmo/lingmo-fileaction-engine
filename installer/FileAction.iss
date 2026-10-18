; Inno Setup script – per-user install, no admin rights needed.
; Build: iscc /DAppVersion=1.2.3 installer\FileAction.iss  (expects the published app in ..\publish)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{5B7E3C1A-8F2D-4C6B-9E1A-7D3F2B9C4A10}
AppName=FileAction
AppVersion={#AppVersion}
AppVerName=FileAction {#AppVersion}
AppPublisher=Metamorfolog
DefaultDirName={localappdata}\Programs\FileAction
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=FileAction-Setup-{#AppVersion}
SetupIconFile=..\src\FileAction.App\Assets\FileAction.ico
UninstallDisplayIcon={app}\FileAction.exe
UninstallDisplayName=FileAction
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no

[Languages]
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{userprograms}\FileAction"; Filename: "{app}\FileAction.exe"

[Run]
Filename: "{app}\FileAction.exe"; Description: "{cm:LaunchProgram,FileAction}"; Flags: nowait postinstall skipifsilent
; Silent install = in-app update: start the new version again.
Filename: "{app}\FileAction.exe"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[UninstallDelete]
; FFmpeg and Pandoc downloaded on demand by the app.
Type: filesandordirs; Name: "{localappdata}\FileAction\tools"

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /im FileAction.exe /f"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{app}\FileAction.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "Unregister"
