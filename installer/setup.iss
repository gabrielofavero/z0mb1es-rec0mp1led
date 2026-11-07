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
  NormalPage: TInputOptionWizardPage;
  VikingPage: TInputOptionWizardPage;
  SteamPage: TInputDirWizardPage;
  Normal360Page: TInputFileWizardPage;
  Viking360Page: TInputFileWizardPage;
  StagePage: TOutputProgressWizardPage;
  SelectedXap: String;
  InstallCommitted: Boolean;
  SetupCompleted: Boolean;
  LastHelperError: String;
  SelectedNormal: String;
  SelectedViking: String;
  SelectedNormalSource: String;
  SelectedVikingSource: String;

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

function NormalChoice: String;
begin
  if WizardSilent then Result := ExpandConstant('{param:ARTNORMAL|keep}')
  else if NormalPage.Values[1] then Result := 'none'
  else if NormalPage.Values[2] then Result := 'steam'
  else if NormalPage.Values[3] then Result := '360'
  else Result := 'keep';
end;

function VikingChoice: String;
begin
  if WizardSilent then Result := ExpandConstant('{param:ARTVIKING|keep}')
  else if VikingPage.Values[1] then Result := 'none'
  else if VikingPage.Values[2] then Result := '360'
  else Result := 'keep';
end;

function NormalSource: String;
begin
  if WizardSilent then Result := ExpandConstant('{param:ARTNORMALSOURCE|}')
  else if NormalChoice = 'steam' then Result := SteamPage.Values[0]
  else Result := Normal360Page.Values[0];
end;

function VikingSource: String;
begin
  if WizardSilent then Result := ExpandConstant('{param:ARTVIKINGSOURCE|}')
  else Result := Viking360Page.Values[0];
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = SteamPage.ID) and (NormalChoice <> 'steam') or
    (PageID = Normal360Page.ID) and (NormalChoice <> '360') or
    (PageID = Viking360Page.ID) and (VikingChoice <> '360');
end;

function ValidateArtPage(const Kind, Path: String; var Detail: String): Boolean;
var
  Code: Integer;
  Output: TExecOutput;
begin
  Result := False;
  if Path = '' then
  begin
    Detail := 'Select the optional art source or choose Original on the previous page.';
    Exit;
  end;
  if not ExecAndCaptureOutput(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
     'validate-art --kind ' + Kind + ' --source ' + Quoted(Path), '', SW_SHOWNORMAL,
     ewWaitUntilTerminated, Code, Output) then
    Detail := 'Could not start optional art validation.'
  else if Code <> 0 then
  begin
    Detail := 'The selected art source is unsupported or damaged.';
    if GetArrayLength(Output.StdErr) > 0 then Detail := Output.StdErr[0];
  end
  else
  begin
    Detail := Output.StdOut[0];
    Result := True;
  end;
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
  NormalPage := CreateInputOptionPage(XapPage.ID, 'I MAED artwork',
    'Choose one optional artwork source for I MAED. It can also improve Endless.',
    'Setup keeps the phone game and Original art available in every case.', True, False);
  NormalPage.Add('Keep current artwork choice (phone Original on a new install)');
  NormalPage.Add('Remove imported I MAED artwork and use Original');
  NormalPage.Add('Import Steam I MAED 1.0.0.0 (I MAED and Endless artwork)');
  NormalPage.Add('Import supported I MAED 360 LIVE (121 mapped artworks)');
  NormalPage.Values[0] := True;
  VikingPage := CreateInputOptionPage(NormalPage.ID, 'Time Viking artwork',
    'Choose Time Viking artwork independently.',
    'The 360 import supplies 270 mapped artworks; Original remains available.', True, False);
  VikingPage.Add('Keep current artwork choice (phone Original on a new install)');
  VikingPage.Add('Remove imported Time Viking artwork and use Original');
  VikingPage.Add('Import supported Time Viking 360 LIVE (270 mapped artworks)');
  VikingPage.Values[0] := True;
  SteamPage := CreateInputDirPage(VikingPage.ID, 'Steam I MAED folder',
    'Select your own Steam installation directory.',
    'Setup verifies the executable and all nine required texture files. It never runs the game.', False, '');
  SteamPage.Add('Steam I MAED folder:');
  Normal360Page := CreateInputFilePage(SteamPage.ID, 'I MAED 360 LIVE',
    'Select your own supported I MAED 360 package.',
    'Setup verifies the complete LIVE package and title before preparing mapped art.');
  Normal360Page.Add('I MAED LIVE:', 'LIVE packages|*.*', '');
  Viking360Page := CreateInputFilePage(Normal360Page.ID, 'Time Viking 360 LIVE',
    'Select your own supported Time Viking 360 package.',
    'Setup verifies the complete LIVE package and title before preparing mapped art.');
  Viking360Page.Add('Time Viking LIVE:', 'LIVE packages|*.*', '');
  StagePage := CreateOutputProgressPage('Preparing game',
    'The selected XAP is prepared locally and verified before installation.');
end;

function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine +
    'Game version: ZombiesWP7 1.0.0.0' + NewLine +
    'Game XAP: ' + SelectedXap + NewLine +
    'I MAED art: ' + NormalChoice + NewLine +
    'Time Viking art: ' + VikingChoice + NewLine +
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
  else if Stage = 'art-phone-source' then Position := 5
  else if Stage = 'art-verify' then Position := 5
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
  Detail: String;
begin
  Result := '';
  SelectedXap := XapPage.Values[0];
  SelectedNormal := NormalChoice;
  SelectedViking := VikingChoice;
  SelectedNormalSource := NormalSource;
  SelectedVikingSource := VikingSource;
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
  if SelectedNormal = 'steam' then
  begin
    if not ValidateArtPage('i-maed-steam', SelectedNormalSource, Detail) then
    begin
      Result := Detail;
      Exit;
    end;
  end
  else if SelectedNormal = '360' then
  begin
    if not ValidateArtPage('i-maed-360', SelectedNormalSource, Detail) then
    begin
      Result := Detail;
      Exit;
    end;
  end;
  if SelectedViking = '360' then
  begin
    if not ValidateArtPage('time-viking-360', SelectedVikingSource, Detail) then
    begin
      Result := Detail;
      Exit;
    end;
  end;
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
       ' --xap ' + Quoted(SelectedXap) +
       ' --normal ' + SelectedNormal + ' --viking ' + SelectedViking +
       ' --normal-source ' + Quoted(SelectedNormalSource) +
       ' --viking-source ' + Quoted(SelectedVikingSource), '', SW_SHOWNORMAL,
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
  if CurPageID = SteamPage.ID then
  begin
    Result := ValidateArtPage('i-maed-steam', SteamPage.Values[0], Detail);
    if Result then MsgBox(Detail, mbInformation, MB_OK)
    else MsgBox(Detail, mbError, MB_OK);
    Exit;
  end;
  if CurPageID = Normal360Page.ID then
  begin
    Result := ValidateArtPage('i-maed-360', Normal360Page.Values[0], Detail);
    if Result then MsgBox(Detail, mbInformation, MB_OK)
    else MsgBox(Detail, mbError, MB_OK);
    Exit;
  end;
  if CurPageID = Viking360Page.ID then
  begin
    Result := ValidateArtPage('time-viking-360', Viking360Page.Values[0], Detail);
    if Result then MsgBox(Detail, mbInformation, MB_OK)
    else MsgBox(Detail, mbError, MB_OK);
    Exit;
  end;
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
var
  Code: Integer;
begin
  if CurStep <> ssDone then Exit;
  SetupCompleted := True;
  Code := -1;
  if not Exec(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
     'finalize --destination ' + Quoted(ExpandConstant('{app}\current')),
     '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Log('INSTALL_FINALIZE_FAILED; previous install retained for recovery.')
  else Log('INSTALL_FINALIZED');
end;

procedure DeinitializeSetup;
var
  Code: Integer;
begin
  if not InstallCommitted or SetupCompleted then Exit;
  Code := -1;
  if not Exec(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
     'rollback --destination ' + Quoted(ExpandConstant('{app}\current')),
     '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Log('INSTALL_ROLLBACK_FAILED; previous install retained in current.previous.')
  else Log('INSTALL_ROLLED_BACK');
end;
