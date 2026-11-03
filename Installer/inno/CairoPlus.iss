; Inno Setup script for Cairo Plus (alternative to the NSIS installers in ..\).
; Build with:  iscc /DAppVersion=0.5.0 /DArch=x64 /DSourceDir=<publish dir> /DOutputDir=<out> CairoPlus.iss
; See docs/RELEASING.md. Arch is one of: x64, x86, arm64.
;
; This installer uses its own AppId and install folder, so it can coexist with a stock Cairo
; installed by the NSIS setup. Only one copy of Cairo can run at a time (AppMutex below).

#ifndef AppVersion
  #error AppVersion must be defined, e.g. /DAppVersion=0.5.0
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #error SourceDir must point at the publish output folder
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{038CE058-9936-476A-9666-AD6820C4CF1C}
AppName=Cairo Plus
AppVersion={#AppVersion}
AppPublisher=Cairo Plus contributors
AppPublisherURL=https://github.com/tiiow-fedora/cairoshell
DefaultDirName={autopf}\Cairo Plus
DefaultGroupName=Cairo Plus
DisableProgramGroupPage=yes
LicenseFile=..\License.txt
SetupIconFile=..\inst_icon.ico
UninstallDisplayIcon={app}\CairoDesktop.exe
OutputDir={#OutputDir}
OutputBaseFilename=CairoPlusSetup_{#Arch}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
AppMutex=CairoShell
CloseApplications=no
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif Arch == "x86"
ArchitecturesAllowed=x86compatible
#else
  #error Unknown Arch
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Cairo Plus when I sign in"; Flags: unchecked
Name: "shell"; Description: "Use Cairo Plus as my Windows shell (advanced; signing out is required)"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\License.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Cairo Plus"; Filename: "{app}\CairoDesktop.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CairoShell"; ValueData: """{app}\CairoDesktop.exe"""; Flags: uninsdeletevalue; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\Winlogon"; ValueType: string; ValueName: "Shell"; ValueData: """{app}\CairoDesktop.exe"""; Flags: uninsdeletevalue; Tasks: shell

[Run]
Filename: "{app}\CairoDesktop.exe"; Description: "Launch Cairo Plus"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User settings live in %LOCALAPPDATA% and are deliberately left alone.
Type: filesandordirs; Name: "{app}"
