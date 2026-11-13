; Inno Setup-script voor kniip (per-gebruiker installatie, geen beheerdersrechten nodig).
; Bouwen: eerst .\build.ps1 (publiceert naar obj\publish), daarna compileert build.ps1 dit script
; automatisch als Inno Setup 6 (https://jrsoftware.org/isinfo.php) is geïnstalleerd.

#define AppName "kniip"
#define AppExe "Kniip.exe"
#ifndef AppVersion
  #define AppVersion "2.3.2"
#endif
#ifndef PublishDir
  #define PublishDir "..\obj\publish"
#endif

[Setup]
AppId={{6B1F3C2A-8E4D-4C1B-9A57-2F3D9E7C4A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=kniip
DefaultDirName={localappdata}\Programs\Kniip
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\public
OutputBaseFilename=Kniip-Setup-{#AppVersion}
SetupIconFile=..\src\Kniip\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[Tasks]
Name: "autostart"; Description: "kniip starten met Windows (aanbevolen)"; GroupDescription: "Integratie:"
Name: "openwith"; Description: "kniip tonen in 'Openen met' voor afbeeldingen (ook in het Knipprogramma)"; GroupDescription: "Integratie:"
Name: "desktopicon"; Description: "Snelkoppeling op het bureaublad"; GroupDescription: "Snelkoppelingen:"; Flags: unchecked

[InstallDelete]
; Overblijfselen van voor de hernoeming van Knip+ naar kniip; de registersleutels ruimt de app zelf op.
Type: files; Name: "{app}\KnipPlus.exe"
Type: files; Name: "{autoprograms}\Knip+.lnk"
Type: files; Name: "{autodesktop}\Knip+.lnk"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\src\Kniip\Assets\Emoji\NOTICE.txt"; DestDir: "{app}"; DestName: "NOTICE-emoji.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Kniip"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

; "Openen met"
Root: HKCU; Subkey: "Software\Classes\Kniip.Image"; ValueType: string; ValueName: ""; ValueData: "kniip afbeelding"; Tasks: openwith; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Kniip.Image\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Kniip.Image\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "kniip editor"; Tasks: openwith; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".png"; ValueData: ""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".jpg"; ValueData: ""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".jpeg"; ValueData: ""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".bmp"; ValueData: ""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".gif"; ValueData: ""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\.png\OpenWithProgids"; ValueType: none; ValueName: "Kniip.Image"; Tasks: openwith; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.jpg\OpenWithProgids"; ValueType: none; ValueName: "Kniip.Image"; Tasks: openwith; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.jpeg\OpenWithProgids"; ValueType: none; ValueName: "Kniip.Image"; Tasks: openwith; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.bmp\OpenWithProgids"; ValueType: none; ValueName: "Kniip.Image"; Tasks: openwith; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.gif\OpenWithProgids"; ValueType: none; ValueName: "Kniip.Image"; Tasks: openwith; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "kniip nu starten"; Flags: nowait postinstall skipifsilent
; Stille installatie = update vanuit kniip zelf; daarna opnieuw starten.
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopKniip"
; Autostart en "Openen met" kunnen ook via de instellingen van kniip zelf aangezet zijn.
Filename: "{app}\{#AppExe}"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterKniip"
