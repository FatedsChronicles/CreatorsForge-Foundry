#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef NumericAppVersion
  #define NumericAppVersion "1.0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point to the self-contained Foundry publish directory.
#endif
#ifndef RepositoryRoot
  #error RepositoryRoot must point to the Foundry repository root.
#endif

[Setup]
AppId={{D9786586-E859-4A81-B8AB-906A99E00510}
AppName=Creators Forge Foundry
AppVersion={#AppVersion}
AppPublisher=Fated's Chronicles
AppPublisherURL=https://github.com/FatedsChronicles/CreatorsForge-Foundry
AppSupportURL=https://github.com/FatedsChronicles/CreatorsForge-Foundry/issues
AppUpdatesURL=https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases
DefaultDirName={autopf}\Creators Forge\Foundry
DefaultGroupName=Creators Forge
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UsePreviousAppDir=yes
UsePreviousGroup=yes
DisableProgramGroupPage=auto
CloseApplications=yes
RestartApplications=no
SetupIconFile={#RepositoryRoot}\src\CreatorsForge.Foundry.App\Assets\CreatorForgeLogo.ico
UninstallDisplayIcon={app}\CreatorsForge.Foundry.exe
LicenseFile={#RepositoryRoot}\LICENSE.md
InfoAfterFile={#RepositoryRoot}\docs\privacy-and-offline.md
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=.
OutputBaseFilename=CreatorsForge-Foundry-Setup
VersionInfoDescription=Creators Forge Foundry Setup
VersionInfoCompany=Fated's Chronicles
VersionInfoProductName=Creators Forge Foundry
VersionInfoVersion={#NumericAppVersion}
VersionInfoProductVersion={#NumericAppVersion}
VersionInfoCopyright=Copyright (c) Fated's Chronicles
#ifdef SignInstaller
SignTool=foundry
SignedUninstaller=yes
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Creators Forge Foundry"; Filename: "{app}\CreatorsForge.Foundry.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Creators Forge Foundry"; Filename: "{app}\CreatorsForge.Foundry.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\CreatorsForge.Foundry.exe"; Description: "Launch Creators Forge Foundry"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\install-receipt.json"

[Code]
function JsonEscape(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Receipt: String;
begin
  if CurStep = ssPostInstall then
  begin
    Receipt := '{' + #13#10 +
      '  "schemaVersion": 2,' + #13#10 +
      '  "installer": "inno-setup",' + #13#10 +
      '  "productVersion": "' + JsonEscape('{#AppVersion}') + '",' + #13#10 +
      '  "installDirectory": "' + JsonEscape(ExpandConstant('{app}')) + '",' + #13#10 +
      '  "executable": "CreatorsForge.Foundry.exe"' + #13#10 +
      '}' + #13#10;
    SaveStringToFile(ExpandConstant('{app}\install-receipt.json'), Receipt, False);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if CheckForMutexes('CreatorsForge.Foundry') then
  begin
    MsgBox('Close Creators Forge Foundry before uninstalling.', mbError, MB_OK);
    Result := False;
  end;
end;
