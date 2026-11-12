; Inno Setup script for the Windows installer; build it with packaging/windows/build-installer.sh.
; AppVersion, SourceDir and RedistDir are passed in by the build script (/D...).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\out\RawConverter-win-x64"
#endif
#ifndef RedistDir
  #define RedistDir "cache"
#endif

[Setup]
; keep AppId unchanged across versions so upgrades replace the previous installation;
AppId={{5B8E2F4C-7A3D-4E61-9C2B-0D1F6A8E3B57}
AppName=RawConverter
AppVersion={#AppVersion}
AppVerName=RawConverter {#AppVersion}
AppPublisher=Yates Lab, The Scripps Research Institute
AppPublisherURL=https://github.com/YatesLab-Software-Repo/RawConverter
DefaultDirName={autopf}\RawConverter
DefaultGroupName=RawConverter
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputBaseFilename=RawConverter-{#AppVersion}-win-x64-setup
OutputDir=..\..\out
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; the Visual C++ runtime needs administrator rights;
PrivilegesRequired=admin
ChangesEnvironment=yes
UninstallDisplayIcon={app}\RawConverterGUI.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add the RawConverter command-line tool to PATH"; GroupDescription: "Command line:"
Name: "contextmenu"; Description: "Add ""Convert with RawConverter"" to the right-click menu of .raw files and .d folders"; GroupDescription: "Command line:"

[Files]
; Linux libraries and debug symbols are not needed on Windows;
; the build output includes LICENSE, NOTICE and the third-party licenses (Licenses\);
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.so,*.pdb"; Flags: ignoreversion recursesubdirs
; Bruker's timsdata.dll needs the Visual C++ 2015-2022 runtime (MSVCP140, VCRUNTIME140_1, VCOMP140);
Source: "{#RedistDir}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsVcRedist

[Icons]
Name: "{group}\RawConverter"; Filename: "{app}\RawConverterGUI.exe"
Name: "{group}\Uninstall RawConverter"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RawConverter"; Filename: "{app}\RawConverterGUI.exe"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; \
    ValueName: "Path"; ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NotInPath(ExpandConstant('{app}'))

; Right-click menu (Explorer). Each item runs the command-line tool in a console window, which closes when the
; conversion succeeds and stays open on an error. Outputs go where the command line puts them by default.
; .raw uses SystemFileAssociations, so the program that opens .raw files is not changed; .d folders are matched
; by name with AppliesTo. Reinstalling without the task removes the entries;
; SystemFileAssociations\.raw: "Convert with RawConverter" submenu;
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Convert with RawConverter"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\RawConverterGUI.exe"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter"; ValueType: string; ValueName: "SubCommands"; ValueData: ""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\1ms12"; ValueType: string; ValueName: "MUIVerb"; ValueData: "MS1 and MS2"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\1ms12\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --ms1 --ms2 || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\2ms2"; ValueType: string; ValueName: "MUIVerb"; ValueData: "MS2"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\2ms2\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --ms2 || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\3ms3"; ValueType: string; ValueName: "MUIVerb"; ValueData: "MS3"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\3ms3\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --ms3 || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\4mgf"; ValueType: string; ValueName: "MUIVerb"; ValueData: "MGF"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\4mgf\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --mgf || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\5mzxml"; ValueType: string; ValueName: "MUIVerb"; ValueData: "mzXML"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\5mzxml\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --mzxml || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\6mzparquet"; ValueType: string; ValueName: "MUIVerb"; ValueData: "mzParquet"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter\shell\6mzparquet\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --mzparquet || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.raw\shell\RawConverter"; ValueType: none; Flags: deletekey; Tasks: not contextmenu
; Directory: "Convert with RawConverter" submenu;
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Convert with RawConverter"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\RawConverterGUI.exe"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter"; ValueType: string; ValueName: "SubCommands"; ValueData: ""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter"; ValueType: string; ValueName: "AppliesTo"; ValueData: "System.FileName:~>"".d"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter\shell\1ms2"; ValueType: string; ValueName: "MUIVerb"; ValueData: "MS2"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter\shell\1ms2\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --ms2 || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter\shell\2mzxml"; ValueType: string; ValueName: "MUIVerb"; ValueData: "mzXML"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter\shell\2mzxml\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --mzxml || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter\shell\3mzparquet"; ValueType: string; ValueName: "MUIVerb"; ValueData: "mzParquet"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter\shell\3mzparquet\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --mzparquet || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter\shell\4tar"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Pack into .d.tar"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter\shell\4tar\command"; ValueType: string; ValueName: ""; ValueData: "cmd.exe /c """"{app}\RawConverter.exe"" ""%1"" --tar || pause"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\RawConverter"; ValueType: none; Flags: deletekey; Tasks: not contextmenu

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Installing the Microsoft Visual C++ runtime..."; Flags: waituntilterminated; Check: NeedsVcRedist
Filename: "{app}\RawConverterGUI.exe"; Description: "{cm:LaunchProgram,RawConverter}"; Flags: nowait postinstall skipifsilent
; an update started from RawConverter (/SILENT /RELAUNCH=1) reopens it, as the user who started it;
Filename: "{app}\RawConverterGUI.exe"; Flags: nowait runasoriginaluser; Check: RelaunchRequested

[Code]
function RelaunchRequested: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;

// The runtime registers itself here; version 14.40 or later covers VCRUNTIME140_1 and VCOMP140 of VS 2022.
function NeedsVcRedist: Boolean;
var
  Installed, Major, Minor: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed)
    and (Installed = 1)
    and RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Major', Major)
    and RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Minor', Minor) then
    Result := (Major < 14) or ((Major = 14) and (Minor < 40));
end;

function NotInPath(Dir: string): Boolean;
var
  Path: string;
begin
  if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Path) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';') = 0;
end;

// Remove the installation folder from PATH on uninstall.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Path, Dir: string;
  P: Integer;
begin
  if CurUninstallStep <> usPostUninstall then
    exit;
  if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Path) then
    exit;
  Dir := ExpandConstant('{app}');
  Path := ';' + Path + ';';
  P := Pos(';' + Uppercase(Dir) + ';', Uppercase(Path));
  if P = 0 then
    exit;
  Delete(Path, P, Length(Dir) + 1);
  Path := Copy(Path, 2, Length(Path) - 2);
  RegWriteExpandStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Path);
end;
