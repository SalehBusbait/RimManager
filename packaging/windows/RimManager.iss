; RimManager Windows installer.
;
; Per-user by default (no administrator prompt): the application installs under
; %LocalAppData%\Programs\RimManager, which is the modern convention for unsigned
; desktop tools. Passing /ALLUSERS on the command line still permits a
; machine-wide install for whoever wants one.
;
; The uninstaller removes the application and, only when the user confirms it,
; the application data under %LocalAppData%\RimManager — modlists, tags,
; snapshots and settings. A silent uninstall never deletes data.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish"
#endif

[Setup]
; The AppId must never change between versions: it is what makes a newer
; installer upgrade an existing install instead of sitting beside it.
AppId={{7C2F1A96-5B0E-4D43-9A67-D08B3A6C51E4}
AppName=RimManager
AppVersion={#AppVersion}
AppVerName=RimManager {#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=Saleh Busubait
AppPublisherURL=https://github.com/SalehBusbait/RimManager
AppSupportURL=https://github.com/SalehBusbait/RimManager/issues
AppUpdatesURL=https://github.com/SalehBusbait/RimManager/releases
DefaultDirName={autopf}\RimManager
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile={#SourcePath}\..\..\LICENSE
SetupIconFile={#SourcePath}\..\..\assets\brand\rimmanager.ico
UninstallDisplayIcon={app}\RimManager.exe
UninstallDisplayName=RimManager
OutputBaseFilename=RimManager-Setup-{#AppVersion}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
; The app may be running during an upgrade; ask Windows to close it cleanly.
CloseApplications=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\RimManager"; Filename: "{app}\RimManager.exe"
Name: "{autodesktop}\RimManager"; Filename: "{app}\RimManager.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Run]
Filename: "{app}\RimManager.exe"; Description: "{cm:LaunchProgram,RimManager}"; \
  Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  { Application data is the user's work — modlists, tags, snapshot history. It is
    never removed silently, and never without an explicit yes. }
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\RimManager');
    if (not UninstallSilent) and DirExists(DataDir) then
    begin
      if MsgBox('Also remove your RimManager data?' + #13#10 + #13#10
                + 'This deletes your modlists, tags, snapshot history and settings under:'
                + #13#10 + DataDir + #13#10 + #13#10
                + 'Your game, saves and mods are not affected either way.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
