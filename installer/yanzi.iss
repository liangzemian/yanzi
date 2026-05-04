#define AppName "Yanzi"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef PublishDir
#define PublishDir "..\.artifacts\publish\win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\.artifacts\installer"
#endif

[Setup]
AppId={{1F2FE5FB-1986-4D2A-AF2C-37A1E52750A6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Yanzi
DefaultDirName={autopf}\Yanzi
DefaultGroupName=Yanzi
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=YanziSetup-{#AppVersion}
SetupIconFile=..\src\OpenQuickHost\yanzi.ico
UninstallDisplayIcon={app}\Yanzi.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Yanzi"; Filename: "{app}\Yanzi.exe"
Name: "{autodesktop}\Yanzi"; Filename: "{app}\Yanzi.exe"; Tasks: desktopicon

[Registry]
Root: HKCR; Subkey: "yanzi"; ValueType: string; ValueName: ""; ValueData: "URL:Yanzi Protocol"; Flags: uninsdeletekey
Root: HKCR; Subkey: "yanzi"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCR; Subkey: "yanzi\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Yanzi.exe,0"
Root: HKCR; Subkey: "yanzi\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Yanzi.exe"" ""%1"""

[Run]
Filename: "{app}\Yanzi.exe"; Description: "启动 Yanzi"; Flags: nowait postinstall skipifsilent

[Code]
function GetInstallerLogPath(): string;
begin
  Result := ExpandConstant('{localappdata}\Yanzi\installer-close.log');
end;

function IsRunningAsAdmin(): Boolean;
begin
  Result := IsAdminInstallMode;
end;

procedure AppendInstallerLog(const Message: string);
var
  LogPath: string;
begin
  LogPath := GetInstallerLogPath();
  ForceDirectories(ExtractFileDir(LogPath));
  SaveStringToFile(
    LogPath,
    GetDateTimeString('yyyy-mm-dd hh:nn:ss.zzz', #0, #0) + ' ' + Message + #13#10,
    True);
end;

function QuoteForPowerShell(const Value: string): string;
var
  EscapedValue: string;
begin
  EscapedValue := Value;
  StringChangeEx(EscapedValue, '''', '''''', True);
  Result := '''' + EscapedValue + '''';
end;

function RunPowerShellAndWait(const Script: string; var ExitCode: Integer): Boolean;
var
  CommandLine: string;
begin
  CommandLine :=
    '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ' +
    QuoteForPowerShell(Script);
  Result := Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    CommandLine,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ExitCode) and (ExitCode = 0);
end;

procedure StopRunningProcesses();
var
  AppExePath: string;
  EverythingExePath: string;
  Script: string;
  ExitCode: Integer;
  ExecOk: Boolean;
begin
  AppExePath := ExpandConstant('{app}\Yanzi.exe');
  EverythingExePath := ExpandConstant('{app}\EverythingRuntime\Everything.exe');
  AppendInstallerLog('PrepareToInstall: begin');
  AppendInstallerLog('PrepareToInstall: isAdmin=' + IntToStr(Ord(IsRunningAsAdmin())));
  AppendInstallerLog('PrepareToInstall: app=' + AppExePath);
  AppendInstallerLog('PrepareToInstall: everything=' + EverythingExePath);

  ExecOk := Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Yanzi.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  AppendInstallerLog('taskkill Yanzi.exe: ok=' + IntToStr(Ord(ExecOk)) + ' exit=' + IntToStr(ExitCode));
  ExecOk := Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Everything.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  AppendInstallerLog('taskkill Everything.exe: ok=' + IntToStr(Ord(ExecOk)) + ' exit=' + IntToStr(ExitCode));
  Sleep(500);

  Script :=
    '$logPath = ' + QuoteForPowerShell(GetInstallerLogPath()) + '; ' +
    '$targets = @(' + QuoteForPowerShell(AppExePath) + ', ' + QuoteForPowerShell(EverythingExePath) + '); ' +
    '$normalizedTargets = $targets | Where-Object { $_ -and (Test-Path $_) } | ForEach-Object { [System.IO.Path]::GetFullPath($_) }; ' +
    '$processes = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and ($normalizedTargets -contains ([System.IO.Path]::GetFullPath($_.ExecutablePath))) }; ' +
    'Add-Content -Path $logPath -Value ((Get-Date -Format ''yyyy-MM-dd HH:mm:ss.fff'') + '' powershell matched='' + $processes.Count); ' +
    'foreach ($process in $processes) { ' +
      'Add-Content -Path $logPath -Value ((Get-Date -Format ''yyyy-MM-dd HH:mm:ss.fff'') + '' powershell target pid='' + $process.ProcessId + '' path='' + $process.ExecutablePath); ' +
      'try { Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop; Add-Content -Path $logPath -Value ((Get-Date -Format ''yyyy-MM-dd HH:mm:ss.fff'') + '' powershell stopped pid='' + $process.ProcessId) } ' +
      'catch { Add-Content -Path $logPath -Value ((Get-Date -Format ''yyyy-MM-dd HH:mm:ss.fff'') + '' powershell stop failed pid='' + $process.ProcessId + '' err='' + $_.Exception.Message) } ' +
    '}; ' +
    'Start-Sleep -Milliseconds 800;';

  ExecOk := RunPowerShellAndWait(Script, ExitCode);
  AppendInstallerLog('powershell close: ok=' + IntToStr(Ord(ExecOk)) + ' exit=' + IntToStr(ExitCode));
  if not ExecOk then
  begin
    ExecOk := Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Yanzi.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    AppendInstallerLog('fallback taskkill Yanzi.exe: ok=' + IntToStr(Ord(ExecOk)) + ' exit=' + IntToStr(ExitCode));
    ExecOk := Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Everything.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    AppendInstallerLog('fallback taskkill Everything.exe: ok=' + IntToStr(Ord(ExecOk)) + ' exit=' + IntToStr(ExitCode));
    Sleep(800);
  end;

  AppendInstallerLog('PrepareToInstall: end');
end;

procedure CleanupLegacyPerUserInstall();
var
  LegacyDir: string;
  CurrentDir: string;
  Path: string;
begin
  CurrentDir := ExpandConstant('{app}');
  LegacyDir := ExpandConstant('{localappdata}\Programs\Yanzi');
  AppendInstallerLog('Legacy cleanup: current=' + CurrentDir);
  AppendInstallerLog('Legacy cleanup: legacy=' + LegacyDir);

  if CompareText(RemoveBackslashUnlessRoot(CurrentDir), RemoveBackslashUnlessRoot(LegacyDir)) <> 0 then
  begin
    if DirExists(LegacyDir) then
    begin
      AppendInstallerLog('Legacy cleanup: deleting legacy dir');
      DelTree(LegacyDir, True, True, True);
    end;
  end;

  Path := ExpandConstant('{userdesktop}\Yanzi.lnk');
  if FileExists(Path) then
  begin
    AppendInstallerLog('Legacy cleanup: deleting ' + Path);
    DeleteFile(Path);
  end;

  Path := ExpandConstant('{commondesktop}\Yanzi.lnk');
  if FileExists(Path) then
  begin
    AppendInstallerLog('Legacy cleanup: deleting ' + Path);
    DeleteFile(Path);
  end;

  Path := ExpandConstant('{userprograms}\Yanzi\Yanzi.lnk');
  if FileExists(Path) then
  begin
    AppendInstallerLog('Legacy cleanup: deleting ' + Path);
    DeleteFile(Path);
  end;

  Path := ExpandConstant('{commonprograms}\Yanzi\Yanzi.lnk');
  if FileExists(Path) then
  begin
    AppendInstallerLog('Legacy cleanup: deleting ' + Path);
    DeleteFile(Path);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningProcesses();
  CleanupLegacyPerUserInstall();
  Result := '';
end;
