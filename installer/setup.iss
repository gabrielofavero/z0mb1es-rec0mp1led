#define RepoRoot ".."
#define Payload "out\payload"

[Setup]
AppId={{F393AB61-549B-416A-B34E-E1AB2E96AF07}
AppName=Z0MB1ES (on teh ph0ne)
AppVersion=0.1.0
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName=Z0MB1ES (on teh ph0ne)
PrivilegesRequired=lowest
OutputDir=out
OutputBaseFilename=Z0MB1ES-0.1.0
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
DisableWelcomePage=no
SetupIconFile={#RepoRoot}\assets\icon\icon.ico
UninstallDisplayIcon={app}\icon-gray.ico
WizardSmallImageFile={#RepoRoot}\assets\installer\wizard-top.png
WizardImageFile={#RepoRoot}\assets\installer\wizard-large-*.bmp
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
AllowNoIcons=yes

[Messages]
WelcomeLabel1=Welcome to Z0MB1ES (on teh ph0ne) setup
WelcomeLabel2=This fan-made port brings the Windows Phone game to your PC with keyboard, mouse, and controller support.%n%nYou will need your own copy of the game's XAP. Setup prepares it locally.
WizardSelectTasks=Shortcuts
SelectTasksDesc=Choose shortcuts
SelectTasksLabel2=Select the shortcuts to create:

[Files]
Source: "{#RepoRoot}\assets\icon\icon-gray.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\helper\Port.SetupHelper.exe"; Flags: dontcopy
Source: "{#Payload}\runtime\*"; DestDir: "{tmp}\port-payload\runtime"; Flags: recursesubdirs createallsubdirs dontcopy
Source: "{#Payload}\tools\*"; DestDir: "{tmp}\port-payload\tools"; Flags: recursesubdirs createallsubdirs dontcopy
Source: "{#Payload}\config\*"; DestDir: "{tmp}\port-payload\config"; Flags: recursesubdirs createallsubdirs dontcopy

[Tasks]
Name: "startmenu"; Description: "Start Menu shortcut"; Flags: checkedonce
Name: "desktop"; Description: "Desktop shortcut"; Flags: unchecked

[Icons]
Name: "{autoprograms}\Z0MB1ES (on teh ph0ne)"; Filename: "{app}\current\Z0MB1ES.exe"; Parameters: "--run ""{app}\current\ZombiesWP7.dll"" --content ""{app}\current"""; WorkingDir: "{app}\current"; Tasks: startmenu
Name: "{autodesktop}\Z0MB1ES (on teh ph0ne)"; Filename: "{app}\current\Z0MB1ES.exe"; Parameters: "--run ""{app}\current\ZombiesWP7.dll"" --content ""{app}\current"""; WorkingDir: "{app}\current"; Tasks: desktop

[Run]
Filename: "{app}\current\Z0MB1ES.exe"; Parameters: "--run ""{app}\current\ZombiesWP7.dll"" --content ""{app}\current"""; Description: "Launch Z0MB1ES (on teh ph0ne)"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}\current"

[Code]
var
  XapPage: TInputFileWizardPage;
  NormalPage: TInputOptionWizardPage;
  NormalReinstallPage: TInputOptionWizardPage;
  VikingPage: TInputOptionWizardPage;
  VikingReinstallPage: TInputOptionWizardPage;
  SteamPage: TInputDirWizardPage;
  Normal360Page: TInputFileWizardPage;
  Viking360Page: TInputFileWizardPage;
  SelectedXap: String;
  InstallCommitted: Boolean;
  SetupCompleted: Boolean;
  LastHelperError: String;
  SelectedNormal: String;
  SelectedViking: String;
  SelectedNormalSource: String;
  SelectedVikingSource: String;
  HelperExtracted: Boolean;
  ValidatedSteam: String;
  ValidatedNormal360: String;
  ValidatedViking360: String;
  PreparingGame: Boolean;
  CancellationRequested: Boolean;
  CancelFile: String;

function HasNormalArt: Boolean;
begin
  Result := DirExists(AddBackslash(WizardDirValue) + 'current\optional-art\i-maed-steam') or
    DirExists(AddBackslash(WizardDirValue) + 'current\optional-art\i-maed-360');
end;

function HasVikingArt: Boolean;
begin
  Result := DirExists(AddBackslash(WizardDirValue) + 'current\optional-art\time-viking-360');
end;

procedure EnsureHelperExtracted;
begin
  if HelperExtracted then Exit;
  ExtractTemporaryFile('Port.SetupHelper.exe');
  HelperExtracted := True;
end;

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
  Path: String;
begin
  if WizardSilent or (WizardForm.CurPageID <> XapPage.ID) then Exit;
  Path := XapPage.Values[0];
  WizardForm.NextButton.Enabled := FileExists(Path);
  if FileExists(Path) then
    XapPage.SubCaptionLabel.Caption := 'Click Next to check the selected XAP.'
  else
    XapPage.SubCaptionLabel.Caption := 'Choose a readable .xap file.';
end;

function CheckXapPage: Boolean;
var
  Path: String;
begin
  Result := False;
  Path := XapPage.Values[0];
  if not FileExists(Path) then Exit;
  XapPage.SubCaptionLabel.Caption := 'Checking XAP...';
  XapPage.SubCaptionLabel.Refresh;
  try
    if CompareText(GetSHA256OfFile(Path),
       '6b88c7fbd95444d2560b4b11e6f35d40a6c0f7abc30706155dcbccd18bc333fe') <> 0 then
    begin
      XapPage.SubCaptionLabel.Caption := 'Unsupported or damaged XAP.';
      Exit;
    end;
  except
    XapPage.SubCaptionLabel.Caption := 'The XAP could not be read.';
    Exit;
  end;
  XapPage.SubCaptionLabel.Caption := 'Supported game version detected.';
  Result := True;
end;

function NormalChoice: String;
begin
  if WizardSilent then Result := ExpandConstant('{param:ARTNORMAL|keep}')
  else if HasNormalArt then
  begin
    if NormalReinstallPage.Values[1] then Result := 'none'
    else if NormalReinstallPage.Values[2] then Result := 'steam'
    else if NormalReinstallPage.Values[3] then Result := '360'
    else Result := 'keep';
  end
  else if NormalPage.Values[1] then Result := 'steam'
  else if NormalPage.Values[2] then Result := '360'
  else Result := 'keep';
end;

function VikingChoice: String;
begin
  if WizardSilent then Result := ExpandConstant('{param:ARTVIKING|keep}')
  else if HasVikingArt then
  begin
    if VikingReinstallPage.Values[1] then Result := 'none'
    else if VikingReinstallPage.Values[2] then Result := '360'
    else Result := 'keep';
  end
  else if VikingPage.Values[1] then Result := '360'
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
  Result := (PageID = NormalPage.ID) and HasNormalArt or
    (PageID = NormalReinstallPage.ID) and not HasNormalArt or
    (PageID = VikingPage.ID) and HasVikingArt or
    (PageID = VikingReinstallPage.ID) and not HasVikingArt or
    (PageID = SteamPage.ID) and (NormalChoice <> 'steam') or
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
    Detail := 'Select an artwork source or go back and choose Don''t Import.';
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
    Detail := 'Supported artwork source detected.';
    Result := True;
  end;
end;

procedure ValidateArtInput(const Kind, Path: String; LabelControl: TNewStaticText;
  var CachedPath: String);
var
  Detail: String;
begin
  WizardForm.NextButton.Enabled := False;
  if (Path = CachedPath) and (Path <> '') then
  begin
    WizardForm.NextButton.Enabled := True;
    Exit;
  end;
  CachedPath := '';
  if (Path = '') or ((Kind = 'i-maed-steam') and not DirExists(Path)) or
     ((Kind <> 'i-maed-steam') and not FileExists(Path)) then
  begin
    LabelControl.Caption := 'Choose an artwork source.';
    Exit;
  end;
  LabelControl.Caption := 'Validating artwork...';
  LabelControl.Refresh;
  EnsureHelperExtracted;
  if ValidateArtPage(Kind, Path, Detail) then
  begin
    CachedPath := Path;
    WizardForm.NextButton.Enabled := True;
  end;
  LabelControl.Caption := Detail;
end;

procedure SteamChanged(Sender: TObject);
begin
  if WizardSilent or (WizardForm.CurPageID <> SteamPage.ID) then Exit;
  ValidateArtInput('i-maed-steam', SteamPage.Values[0], SteamPage.SubCaptionLabel, ValidatedSteam);
end;

procedure Normal360Changed(Sender: TObject);
begin
  if WizardSilent or (WizardForm.CurPageID <> Normal360Page.ID) then Exit;
  ValidateArtInput('i-maed-360', Normal360Page.Values[0], Normal360Page.SubCaptionLabel, ValidatedNormal360);
end;

procedure Viking360Changed(Sender: TObject);
begin
  if WizardSilent or (WizardForm.CurPageID <> Viking360Page.ID) then Exit;
  ValidateArtInput('time-viking-360', Viking360Page.Values[0], Viking360Page.SubCaptionLabel, ValidatedViking360);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = XapPage.ID then XapFileChanged(nil)
  else if CurPageID = SteamPage.ID then SteamChanged(nil)
  else if CurPageID = Normal360Page.ID then Normal360Changed(nil)
  else if CurPageID = Viking360Page.ID then Viking360Changed(nil);
end;

procedure InitializeWizard;
begin
  XapPage := CreateInputFilePage(wpSelectDir, 'Game XAP',
    'Select your Z0MB1ES (on teh ph0ne) XAP.',
    'Choose the supported game package.');
  XapPage.Add('Game XAP:', 'XAP packages|*.xap|All files|*.*', '.xap');
  XapPage.Values[0] := ExpandConstant('{param:XAP|}');
  XapPage.Edits[0].OnChange := @XapFileChanged;
  NormalPage := CreateInputOptionPage(XapPage.ID, 'I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1 artwork',
    'You can use artwork from a different game version to improve the quality.',
    'The original artwork will still be available in game settings.', True, False);
  NormalPage.Add('Don''t Import');
  NormalPage.Add('Import from Steam');
  NormalPage.Add('Import from Xbox 360 Package');
  NormalPage.Values[0] := True;
  NormalReinstallPage := CreateInputOptionPage(NormalPage.ID, 'I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1 artwork',
    'You can use artwork from a different game version to improve the quality.',
    'The original artwork will still be available in game settings.', True, False);
  NormalReinstallPage.Add('Keep Current');
  NormalReinstallPage.Add('Remove Imported Artwork');
  NormalReinstallPage.Add('Import from Steam');
  NormalReinstallPage.Add('Import from Xbox 360 Package');
  NormalReinstallPage.Values[0] := True;
  VikingPage := CreateInputOptionPage(NormalReinstallPage.ID, 'TIME VIKING artwork',
    'You can use artwork from a different game version to improve the quality.',
    'The original artwork will still be available in game settings.', True, False);
  VikingPage.Add('Don''t Import');
  VikingPage.Add('Import from Xbox 360 Package');
  VikingPage.Values[0] := True;
  VikingReinstallPage := CreateInputOptionPage(VikingPage.ID, 'TIME VIKING artwork',
    'You can use artwork from a different game version to improve the quality.',
    'The original artwork will still be available in game settings.', True, False);
  VikingReinstallPage.Add('Keep Current');
  VikingReinstallPage.Add('Remove Imported Artwork');
  VikingReinstallPage.Add('Import from Xbox 360 Package');
  VikingReinstallPage.Values[0] := True;
  SteamPage := CreateInputDirPage(VikingReinstallPage.ID, 'Steam I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1 folder',
    'Select your own Steam installation directory.',
    'Setup verifies the executable and all nine required texture files. It never runs the game.', False, '');
  SteamPage.Add('Steam folder:');
  SteamPage.Edits[0].OnChange := @SteamChanged;
  Normal360Page := CreateInputFilePage(SteamPage.ID, 'I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1 package',
    'Select your Xbox 360 package.',
    'Setup verifies the complete LIVE package and title before preparing mapped art.');
  Normal360Page.Add('Xbox 360 package:', 'LIVE packages|*.*', '');
  Normal360Page.Edits[0].OnChange := @Normal360Changed;
  Viking360Page := CreateInputFilePage(Normal360Page.ID, 'Time Viking 360 LIVE',
    'Select your own supported Time Viking 360 package.',
    'Setup verifies the complete LIVE package and title before preparing mapped art.');
  Viking360Page.Add('Time Viking LIVE:', 'LIVE packages|*.*', '');
  Viking360Page.Edits[0].OnChange := @Viking360Changed;
end;

function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine +
    'Game version: ZombiesWP7 1.0.0.0' + NewLine +
    'Game XAP: ' + SelectedXap + NewLine +
    'I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1 artwork: ' + NormalChoice + NewLine +
    'TIME VIKING artwork: ' + VikingChoice + NewLine +
    'Setup will prepare and verify the game locally before installing it.';
end;

procedure AdvanceProgress(Value: Integer);
begin
  if Value > 1000 then Value := 1000;
  if Value <= WizardForm.ProgressGauge.Position then Exit;
  WizardForm.ProgressGauge.Position := Value;
  WizardForm.ProgressGauge.Refresh;
end;

procedure InstallLog(const S: String; const Error, FirstLine: Boolean);
var
  Stage: String;
  Position: Integer;
begin
  if Pos('PROGRESS ', S) = 1 then
  begin
    Position := StrToIntDef(Copy(S, 10, Length(S)), -1);
    if (Position >= 0) and (Position <= 1000) then
      AdvanceProgress(200 + Position * 8 div 10);
    Exit;
  end;
  Log(S);
  if Error then LastHelperError := S;
  if Pos('STAGE ', S) <> 1 then Exit;
  Stage := Copy(S, 7, Length(S));
  if Stage = 'runtime' then WizardForm.StatusLabel.Caption := 'Copying runtime files...'
  else if Stage = 'reuse-check' then WizardForm.StatusLabel.Caption := 'Checking installed game files...'
  else if Stage = 'reuse' then WizardForm.StatusLabel.Caption := 'Copying prepared game data...'
  else if Stage = 'extract' then WizardForm.StatusLabel.Caption := 'Extracting game package...'
  else if Stage = 'inspect' then WizardForm.StatusLabel.Caption := 'Inspecting game assembly...'
  else if Stage = 'abi' then WizardForm.StatusLabel.Caption := 'Checking runtime compatibility...'
  else if Stage = 'rewrite' then WizardForm.StatusLabel.Caption := 'Preparing game code...'
  else if Stage = 'content' then WizardForm.StatusLabel.Caption := 'Preparing game content and music...'
  else if Stage = 'art' then WizardForm.StatusLabel.Caption := 'Preparing selected artwork...'
  else if Stage = 'art-phone-source' then WizardForm.StatusLabel.Caption := 'Checking original artwork...'
  else if Stage = 'art-verify' then WizardForm.StatusLabel.Caption := 'Checking prepared artwork...'
  else if Stage = 'verify' then WizardForm.StatusLabel.Caption := 'Verifying prepared game files...'
  else if Stage = 'commit' then WizardForm.StatusLabel.Caption := 'Finishing installation...';
  WizardForm.StatusLabel.Refresh;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
end;

function InstallGame: String;
var
  Code: Integer;
  ReusePrepared: Boolean;
  NeedArtTools: Boolean;
  Output: TExecOutput;
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
  CancelFile := ExpandConstant('{tmp}\port-cancel-requested');
  if FileExists(CancelFile) then DeleteFile(CancelFile);
  CancellationRequested := False;
  PreparingGame := True;
  WizardForm.ProgressGauge.Max := 1000;
  WizardForm.ProgressGauge.Position := 0;
  WizardForm.StatusLabel.Caption := 'Loading setup helper...';
  WizardForm.StatusLabel.Refresh;
  try
    EnsureHelperExtracted;
    AdvanceProgress(20);
    WizardForm.StatusLabel.Caption := 'Checking existing game files...';
    WizardForm.StatusLabel.Refresh;
    ReusePrepared := False;
    if FileExists(ExpandConstant('{app}\current\install-manifest.json')) then
      if ExecAndCaptureOutput(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
         'verify-installed --directory ' + Quoted(ExpandConstant('{app}\current')),
         '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code, Output) then
        ReusePrepared := Code = 0;
    AdvanceProgress(40);
    NeedArtTools := (SelectedNormal = 'steam') or (SelectedNormal = '360') or
      (SelectedViking = '360') or
      ((SelectedNormal = 'keep') and HasNormalArt) or
      ((SelectedViking = 'keep') and HasVikingArt);
    WizardForm.StatusLabel.Caption := 'Unpacking game runtime...';
    WizardForm.StatusLabel.Refresh;
    ExtractTemporaryFiles('{tmp}\port-payload\runtime\*');
    AdvanceProgress(100);
    ExtractTemporaryFiles('{tmp}\port-payload\config\*');
    AdvanceProgress(110);
    if not ReusePrepared then
    begin
      WizardForm.StatusLabel.Caption := 'Unpacking preparation tools...';
      WizardForm.StatusLabel.Refresh;
      ExtractTemporaryFiles('{tmp}\port-payload\tools\ffmpeg.exe');
      AdvanceProgress(130);
      ExtractTemporaryFiles('{tmp}\port-payload\tools\inspect_assembly.exe');
      AdvanceProgress(150);
      ExtractTemporaryFiles('{tmp}\port-payload\tools\abi\*');
      AdvanceProgress(180);
    end;
    if NeedArtTools then
    begin
      WizardForm.StatusLabel.Caption := 'Unpacking artwork tools...';
      WizardForm.StatusLabel.Refresh;
      ExtractTemporaryFiles('{tmp}\port-payload\tools\Port.AssetImporter.exe');
      AdvanceProgress(190);
      ExtractTemporaryFiles('{tmp}\port-payload\tools\decode_360_xnb.exe');
      AdvanceProgress(195);
    end;
    if CancellationRequested then Exit;
    AdvanceProgress(200);
    WizardForm.StatusLabel.Caption := 'Checking game package...';
    WizardForm.StatusLabel.Refresh;
    LastHelperError := '';
    if not ExecAndLogOutput(ExpandConstant('{tmp}\Port.SetupHelper.exe'),
       'install --destination ' + Quoted(ExpandConstant('{app}\current')) +
       ' --payload ' + Quoted(ExpandConstant('{tmp}\port-payload')) +
       ' --xap ' + Quoted(SelectedXap) +
       ' --normal ' + SelectedNormal + ' --viking ' + SelectedViking +
       ' --normal-source ' + Quoted(SelectedNormalSource) +
       ' --viking-source ' + Quoted(SelectedVikingSource) +
       ' --cancel-file ' + Quoted(CancelFile), '', SW_SHOWNORMAL,
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
      AdvanceProgress(1000);
    end;
  finally
    PreparingGame := False;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if WizardSilent then Exit;
  if CurPageID = XapPage.ID then Result := CheckXapPage
  else if CurPageID = SteamPage.ID then Result := SteamPage.Values[0] = ValidatedSteam
  else if CurPageID = Normal360Page.ID then Result := Normal360Page.Values[0] = ValidatedNormal360
  else if CurPageID = Viking360Page.ID then Result := Viking360Page.Values[0] = ValidatedViking360;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if not PreparingGame then Exit;
  Cancel := False;
  Confirm := False;
  if CancellationRequested then Exit;
  CancellationRequested := True;
  SaveStringToFile(CancelFile, 'cancel', False);
  WizardForm.StatusLabel.Caption := 'Cancelling after the current step...';
  WizardForm.StatusLabel.Refresh;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  ErrorText: String;
begin
  if CurStep = ssInstall then
  begin
    ErrorText := InstallGame;
    if CancellationRequested then Abort;
    if ErrorText <> '' then
    begin
      if WizardSilent then Log(ErrorText)
      else MsgBox(ErrorText, mbError, MB_OK);
      Abort;
    end;
    Exit;
  end;
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
