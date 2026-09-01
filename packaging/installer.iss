; ============================================================================
; Lumo — Windows installer (v3.0)
; Compiled by CI (repo root): ISCC /DAppVersion=v3.0.0-alpha.1 packaging/installer.iss
; All relative paths below resolve against THIS file's folder (packaging\).
;
; Design decisions:
;   · Per-user install (PrivilegesRequired=lowest) → no UAC prompt, no admin.
;     Default target is %LOCALAPPDATA%\Programs\Lumo — the same convention
;     VS Code / Discord / Slack use for per-user installs.
;   · Ships the WHOLE portable layout: Lumo.exe + runtimes\win-x64\ (the
;     whisper.cpp natives Whisper.net probes next to the exe) + the README.
;   · Uninstall leaves user data (%APPDATA%\Lumo, %LOCALAPPDATA%\Lumo) alone —
;     settings, chats and themes belong to the user.
; ============================================================================

#define AppName "Lumo"
#define AppPublisher "Lumo Launcher"
#define AppExeName "Lumo.exe"

#ifndef AppVersion
#define AppVersion "0.0.0-dev"
#endif

; strip a leading "v" so AddRemoveProgs shows "3.0.0-alpha.1", not "v3.0.0-alpha.1"
#define AppVersionClean
#if Copy(AppVersion, 1, 1) == "v"
#define AppVersionClean = Copy(AppVersion, 2)
#else
#define AppVersionClean = AppVersion
#endif

[Setup]
AppId={{6E8B4D5A-9C2F-4B17-AE31-5F0D7A9C1234}
AppName={#AppName}
AppVersion={#AppVersionClean}
AppVerName={#AppName} {#AppVersionClean}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Anik1377/Lumo-Launcher
AppSupportURL=https://github.com/Anik1377/Lumo-Launcher/issues
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\installer-output
OutputBaseFilename=LumoSetup-{#AppVersionClean}
SetupIconFile=..\src\Lumo\Assets\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} (launcher)
VersionInfoDescription={#AppName} installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "..\stage\Lumo\Lumo.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\stage\Lumo\runtimes\win-x64\*"; DestDir: "{app}\runtimes\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\stage\Lumo\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; optional autostart — written only when the task is ticked; HKCU, so no admin.
; The launcher's own Start-with-Windows toggle manages the same value later.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "Lumo"; ValueData: """{app}\{#AppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent
