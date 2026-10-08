; Inno Setup script for Baba. Build with installer\build-installer.ps1 (it publishes the app first).
; Per-user install by default (no administrator needed); the dialog lets an administrator install for all users.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{6F0B4C58-3D1A-4E8B-9C47-0B1A2D5E7F31}
AppName=Baba
AppVersion={#AppVersion}
AppPublisher=Baba
DefaultDirName={autopf}\Baba
DefaultGroupName=Baba
UninstallDisplayIcon={app}\Baba.Desktop.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=Baba-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Tells Windows the .baba file type changed, so Explorer shows the association straight away.
ChangesAssociations=yes
DisableProgramGroupPage=yes
#if FileExists("..\branding\baba.ico")
SetupIconFile=..\branding\baba.ico
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
; The published app (self-contained: no .NET needed). Created by build-installer.ps1.
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs

[Icons]
Name: "{autoprograms}\Baba"; Filename: "{app}\Baba.Desktop.exe"
Name: "{autodesktop}\Baba"; Filename: "{app}\Baba.Desktop.exe"; Tasks: desktopicon

[Registry]
; Double-clicking a .baba file opens it in Baba (HKA = current user for a per-user install, all users otherwise).
Root: HKA; Subkey: "Software\Classes\.baba"; ValueType: string; ValueName: ""; ValueData: "Baba.CompanyFile"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Baba.CompanyFile"; ValueType: string; ValueName: ""; ValueData: "Baba company file"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Baba.CompanyFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Baba.Desktop.exe,0"
Root: HKA; Subkey: "Software\Classes\Baba.CompanyFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Baba.Desktop.exe"" ""%1"""

[Run]
Filename: "{app}\Baba.Desktop.exe"; Description: "Start Baba"; Flags: nowait postinstall skipifsilent

[Code]
const
  WebView2ClientKey = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

// Baba shows its screens with the Microsoft Edge WebView2 runtime, which is part of current Windows 10 and 11.
function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM32, WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'))
    or (RegQueryStringValue(HKCU, WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not WebView2Installed then
  begin
    if MsgBox('Baba needs the Microsoft Edge WebView2 runtime, which was not found on this computer.' + #13#10 + #13#10 +
              'Click Yes to open the Microsoft download page now (install it, then run this setup again), ' +
              'or No to continue anyway.', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://go.microsoft.com/fwlink/p/?LinkId=2124703', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      Result := False;
    end;
  end;
end;
