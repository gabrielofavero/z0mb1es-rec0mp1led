#define RepoRoot ".."
#define Payload "out\payload"

[Setup]
AppId={{F393AB61-549B-416A-B34E-E1AB2E96AF07}
AppName=Z0MB1ES Recompiled
AppVersion=0.1.0
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName=Z0MB1ES Recompiled
PrivilegesRequired=lowest
OutputDir=out
OutputBaseFilename=ZombiesPort-Setup
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
SetupIconFile={#RepoRoot}\assets\icon\icon.ico
UninstallDisplayIcon={app}\icon-gray.ico
WizardSmallImageFile={#RepoRoot}\assets\installer\wizard-top.png
WizardImageFile={#RepoRoot}\assets\installer\wizard-large-*.bmp
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
AllowNoIcons=yes

[Files]
Source: "{#RepoRoot}\assets\icon\icon-gray.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\helper\Port.SetupHelper.exe"; Flags: dontcopy
Source: "{#Payload}\runtime\*"; DestDir: "{tmp}\port-payload\runtime"; Flags: recursesubdirs createallsubdirs dontcopy
Source: "{#Payload}\tools\*"; DestDir: "{tmp}\port-payload\tools"; Flags: recursesubdirs createallsubdirs dontcopy
Source: "{#Payload}\config\*"; DestDir: "{tmp}\port-payload\config"; Flags: recursesubdirs createallsubdirs dontcopy

[Icons]
Name: "{autoprograms}\Z0MB1ES Recompiled"; Filename: "{app}\current\Port.Launcher.exe"; Parameters: "--run ""{app}\current\ZombiesWP7.dll"" --content ""{app}\current"""; WorkingDir: "{app}\current"

[Run]
Filename: "{app}\current\Port.Launcher.exe"; Parameters: "--run ""{app}\current\ZombiesWP7.dll"" --content ""{app}\current"""; Description: "Launch Z0MB1ES Recompiled"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}\current"

[Code]
var
  XapPage: TInputFileWizardPage;
  StagePage: TOutputProgressWizardPage;
  SelectedXap: String;
  InstallCommitted: Boolean;
  SetupCompleted: Boolean;
  LastHelperError: String;

function DefaultInstallDir(Param: String): String;
var
  Base: String;
begin
  Base := GetEnv('LOCALAPPDATA');
  if Base = '' then Base := GetEnv('USERPROFILE');
  Result := AddBackslash(Base) + 'Programs\ZombiesPort';
end;

function Quoted(const S: String): String;
begin
  Result := '"' + S + '"';
end;

procedure XapFileChanged(Sender: TObject);
var
  Code: Integer;
  Output: TExecOutput;
  Path: String;
begin
  if WizardSilent or (WizardForm.CurPageID <> XapPage.ID) then Exit;
  WizardForm.NextButton.Enabled := False;
  Path := XapPage.Values[0];
  if not FileExists(Path) then
  begin
    XapPage.SubCaptionLabel.Caption := 'Choose a readable .xap file.';
    Exit;
  end;
  if not ExecAndCaptureOutput(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
     'validate-xap --xap ' + Quoted(Path), '', SW_SHOWNORMAL,
     ewWaitUntilTerminated, Code, Output) then
    XapPage.SubCaptionLabel.Caption := 'Could not start XAP validation.'
  else if Code <> 0 then
  begin
    XapPage.SubCaptionLabel.Caption := 'Unsupported or damaged XAP.';
    if GetArrayLength(Output.StdErr) > 0 then
      XapPage.SubCaptionLabel.Caption := Output.StdErr[0];
  end
  else
  begin
    XapPage.SubCaptionLabel.Caption := 'Supported game version detected. The XAP will stay in its original location.';
    WizardForm.NextButton.Enabled := True;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = XapPage.ID then XapFileChanged(nil);
end;

procedure InitializeWizard;
begin
  ExtractTemporaryFile('Port.SetupHelper.exe');
  WizardForm.WelcomeLabel2.Caption := 'You need your own supported Z0MB1ES (on teh ph0ne) XAP. Setup works offline and keeps your source package untouched.';
  XapPage := CreateInputFilePage(wpSelectDir, 'Game XAP',
    'Select your own supported Z0MB1ES XAP.',
    'Setup checks the package read-only before installing. The source file stays where it is.');
  XapPage.Add('Game XAP:', 'XAP packages|*.xap|All files|*.*', '.xap');
  XapPage.Values[0] := ExpandConstant('{param:XAP|}');
  XapPage.Edits[0].OnChange := @XapFileChanged;
  StagePage := CreateOutputProgressPage('Preparing game',
    'The selected XAP is prepared locally and verified before installation.');
end;

function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine +
    'Game version: ZombiesWP7 1.0.0.0' + NewLine +
    'Game XAP: ' + SelectedXap + NewLine +
    'Setup will prepare and verify the game locally before installing it.';
end;

procedure InstallLog(const S: String; const Error, FirstLine: Boolean);
var
  Stage: String;
  Position: Integer;
begin
  Log(S);
  if Error then LastHelperError := S;
  if Pos('STAGE ', S) <> 1 then Exit;
  Stage := Copy(S, 7, Length(S));
  Position := 1;
  if Stage = 'extract' then Position := 2
  else if Stage = 'rewrite' then Position := 3
  else if Stage = 'reuse' then Position := 3
  else if Stage = 'content' then Position := 4
  else if Stage = 'verify' then Position := 5
  else if Stage = 'commit' then Position := 6;
  StagePage.SetText('Preparing game', Stage);
  StagePage.SetProgress(Position, 6);
  StagePage.Msg1Label.Refresh;
  StagePage.Msg2Label.Refresh;
  StagePage.ProgressBar.Refresh;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Output: TExecOutput;
begin
  Result := '';
  SelectedXap := XapPage.Values[0];
  if (SelectedXap = '') or not FileExists(SelectedXap) then
  begin
    Result := 'Select a readable game XAP before installing.';
    Exit;
  end;
  if not ExecAndCaptureOutput(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
     'validate-xap --xap ' + Quoted(SelectedXap), '', SW_SHOWNORMAL,
     ewWaitUntilTerminated, Code, Output) then
    Result := 'Could not start XAP validation.'
  else if Code <> 0 then
  begin
    Result := 'The selected XAP is unsupported or damaged.';
    if GetArrayLength(Output.StdErr) > 0 then Result := Result + #13#10 + Output.StdErr[0];
  end;
  if Result <> '' then Exit;
  StagePage.SetText('Preparing and verifying game files',
    'Extraction, compatibility preparation, content conversion, and commit may take several minutes.');
  StagePage.SetProgress(1, 2);
  StagePage.Show;
  StagePage.Msg1Label.Refresh;
  StagePage.Msg2Label.Refresh;
  try
    ExtractTemporaryFiles('{tmp}\port-payload\*');
    LastHelperError := '';
    if not ExecAndLogOutput(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
       'install --destination ' + Quoted(ExpandConstant('{app}\current')) +
       ' --payload ' + Quoted(ExpandConstant('{tmp}\port-payload')) +
       ' --xap ' + Quoted(SelectedXap), '', SW_SHOWNORMAL,
       ewWaitUntilTerminated, Code, @InstallLog) then
      Result := 'Could not start game preparation.'
    else if Code <> 0 then
    begin
      Result := 'Game preparation failed. The previous installation is preserved.';
      if LastHelperError <> '' then Result := Result + #13#10 + LastHelperError;
    end
    else
    begin
      InstallCommitted := True;
      StagePage.SetProgress(2, 2);
    end;
  finally
    StagePage.Hide;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Code: Integer;
  Output: TExecOutput;
  Detail: String;
begin
  Result := True;
  if WizardSilent then Exit;
  if CurPageID <> XapPage.ID then Exit;
  SelectedXap := XapPage.Values[0];
  if (SelectedXap = '') or not FileExists(SelectedXap) then
  begin
    MsgBox('Choose a readable .xap file.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  WizardForm.NextButton.Enabled := False;
  try
    if not ExecAndCaptureOutput(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
       'validate-xap --xap ' + Quoted(SelectedXap), '', SW_SHOWNORMAL,
       ewWaitUntilTerminated, Code, Output) then
      Detail := 'Could not start XAP validation.'
    else if Code <> 0 then
    begin
      Detail := 'This XAP is unsupported, corrupted, or unreadable.';
      if GetArrayLength(Output.StdErr) > 0 then Detail := Output.StdErr[0];
    end
    else Detail := '';
    if Detail <> '' then
    begin
      MsgBox(Detail, mbError, MB_OK);
      Result := False;
    end;
  finally
    WizardForm.NextButton.Enabled := True;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssDone then Exit;
  SetupCompleted := True;
  if DirExists(ExpandConstant('{app}\current.previous')) then
    DelTree(ExpandConstant('{app}\current.previous'), True, True, True);
end;

procedure DeinitializeSetup;
var
  CurrentDir: String;
  PreviousDir: String;
begin
  if not InstallCommitted or SetupCompleted then Exit;
  CurrentDir := ExpandConstant('{app}\current');
  PreviousDir := CurrentDir + '.previous';
  if DirExists(CurrentDir) then DelTree(CurrentDir, True, True, True);
  if DirExists(PreviousDir) then RenameFile(PreviousDir, CurrentDir);
end;
