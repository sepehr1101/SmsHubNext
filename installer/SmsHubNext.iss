#ifndef AppVersion
  #define AppVersion "0.1.6"
#endif
#ifndef PublishDir
  #error PublishDir must point to the dotnet publish output.
#endif
#ifndef HostingBundlePath
  #error HostingBundlePath must point to the offline .NET 10 Hosting Bundle installer.
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "SmsHubNext"
#define AppPublisher "SmsHubNext"
#define AppExecutable "SmsHubNext.exe"
#define StableAppId "{{D4C4D455-64E5-4AA1-99AA-B41BD58B7891}"

[Setup]
AppId={#StableAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\SmsHubNext
DefaultGroupName=SmsHubNext
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=SmsHubNext-Setup-{#AppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UninstallDisplayIcon={app}\{#AppExecutable}
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=SmsHubNext Windows and IIS installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[LangOptions]
LanguageName=Farsi
LanguageID=$0429
LanguageCodePage=0
DialogFontName=Tahoma
DialogFontSize=9
RightToLeft=yes

[Messages]
ButtonBack=< قبلی
ButtonNext=بعدی >
ButtonInstall=نصب
ButtonCancel=انصراف
ButtonFinish=پایان
SelectDirLabel3=مسیر نصب SmsHubNext را انتخاب کنید.
ReadyLabel1=نصب آماده شروع است.
ReadyLabel2a=برای شروع نصب روی «نصب» کلیک کنید.
InstallingLabel=لطفاً تا پایان نصب صبر کنید.
FinishedHeadingLabel=نصب SmsHubNext کامل شد
FinishedLabel=SmsHubNext با موفقیت نصب و بررسی شد.

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "appsettings.Development*.json,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "scripts\Installer.Common.ps1"; DestDir: "{app}\.installer"; Flags: ignoreversion
Source: "scripts\Deploy-Iis.ps1"; DestDir: "{app}\.installer"; Flags: ignoreversion
Source: "scripts\Test-Health.ps1"; DestDir: "{app}\.installer"; Flags: ignoreversion
Source: "scripts\Installer.Common.ps1"; DestName: "Installer.Common.ps1"; Flags: dontcopy noencryption
Source: "scripts\Test-Database.ps1"; DestName: "Test-Database.ps1"; Flags: dontcopy noencryption
Source: "scripts\Enable-Iis.ps1"; DestName: "Enable-Iis.ps1"; Flags: dontcopy noencryption
Source: "scripts\Ensure-HostingBundle.ps1"; DestName: "Ensure-HostingBundle.ps1"; Flags: dontcopy noencryption
Source: "scripts\Deploy-Iis.ps1"; DestName: "Deploy-Iis.ps1"; Flags: dontcopy noencryption
Source: "{#HostingBundlePath}"; DestName: "dotnet-hosting-win.exe"; Flags: dontcopy noencryption

[Dirs]
Name: "{commonappdata}\SmsHubNext\DataProtection-Keys"
Name: "{commonappdata}\SmsHubNext\Logs"
Name: "{commonappdata}\SmsHubNext\Installer\Backups"

[Registry]
Root: HKLM; Subkey: "Software\SmsHubNext\Installer"; ValueType: string; ValueName: "SiteName"; ValueData: "{code:GetSiteName}"
Root: HKLM; Subkey: "Software\SmsHubNext\Installer"; ValueType: string; ValueName: "AppPoolName"; ValueData: "SmsHubNext"
Root: HKLM; Subkey: "Software\SmsHubNext\Installer"; ValueType: dword; ValueName: "Port"; ValueData: "{code:GetSitePort}"
Root: HKLM; Subkey: "Software\SmsHubNext\Installer"; ValueType: string; ValueName: "HostName"; ValueData: "{code:GetHostName}"
Root: HKLM; Subkey: "Software\SmsHubNext\Installer"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"

[Icons]
Name: "{group}\باز کردن SmsHubNext"; Filename: "{code:GetLaunchUrl}"; Flags: runmaximized
Name: "{group}\حذف SmsHubNext"; Filename: "{uninstallexe}"

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\.installer\Deploy-Iis.ps1"" -Action Remove -SiteName ""{reg:HKLM\Software\SmsHubNext\Installer,SiteName|SmsHubNext}"" -AppPoolName ""{reg:HKLM\Software\SmsHubNext\Installer,AppPoolName|SmsHubNext}"" -Port {reg:HKLM\Software\SmsHubNext\Installer,Port|8080}"; \
  Flags: runhidden waituntilterminated; RunOnceId: "RemoveSmsHubNextIis"

[Code]
const
  AppPoolName = 'SmsHubNext';
  SqlServerAuthenticationOption = 0;
  WindowsAuthenticationOption = 1;

var
  IisPage: TInputQueryWizardPage;
  MaintenancePage: TInputOptionWizardPage;
  AuthenticationPage: TInputOptionWizardPage;
  DatabasePage: TInputQueryWizardPage;
  TestPortButton: TNewButton;
  TestConnectionButton: TNewButton;
  ReconfigureExistingDatabase: Boolean;
  ExistingProductionSettings: Boolean;
  ExternalRequestPath: String;
  RequestPath: String;
  ResultPath: String;
  BackupPath: String;
  UpgradeInstallation: Boolean;
  BackupCreated: Boolean;
  RestartRequiredByPrerequisite: Boolean;
  PreviousSiteName: String;
  PreviousPort: String;
  PreviousHostName: String;

function PowerShellPath: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function QuoteArgument(const Value: String): String;
begin
  Result := '"' + Value + '"';
end;

function JsonEscape(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
  StringChangeEx(Result, #9, '\t', True);
  StringChangeEx(Result, #13, '\r', True);
  StringChangeEx(Result, #10, '\n', True);
end;

function ContainsLineBreak(const Value: String): Boolean;
begin
  Result := (Pos(#13, Value) > 0) or (Pos(#10, Value) > 0);
end;

function IsSafeSimpleValue(const Value: String): Boolean;
begin
  Result := (Pos('"', Value) = 0) and not ContainsLineBreak(Value);
end;

function ContainsAnyCharacter(const Value: String; const Characters: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to Length(Value) do
  begin
    if Pos(Value[Index], Characters) > 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsValidHostName(const Value: String): Boolean;
var
  Index: Integer;
  Character: String;
begin
  Result := Length(Value) <= 253;
  if not Result then
    Exit;

  for Index := 1 to Length(Value) do
  begin
    Character := Value[Index];
    if Pos(Character, 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-') = 0 then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function CurrentAuthentication: String;
begin
  if AuthenticationPage.Values[SqlServerAuthenticationOption] then
    Result := 'SqlServer'
  else
    Result := 'Windows';
end;

function DatabaseConfigurationRequired: Boolean;
begin
  Result := (not ExistingProductionSettings) or
    ReconfigureExistingDatabase or
    (ExternalRequestPath <> '');
end;

procedure UpdateExistingInstallationState;
var
  AppPath: String;
begin
  AppPath := AddBackslash(WizardDirValue);
  ExistingProductionSettings := FileExists(AppPath + 'appsettings.Production.json');
  UpgradeInstallation := FileExists(AppPath + '{#AppExecutable}');
end;

procedure UpdateDatabaseCredentialControls;
var
  SqlAuthentication: Boolean;
begin
  SqlAuthentication := AuthenticationPage.Values[SqlServerAuthenticationOption];
  DatabasePage.Edits[2].Enabled := SqlAuthentication;
  DatabasePage.Edits[3].Enabled := SqlAuthentication;
end;

function WriteDatabaseRequest: Boolean;
var
  Lines: TArrayOfString;
  Json: String;
begin
  Result := False;
  if ExternalRequestPath <> '' then
  begin
    if not FileExists(ExternalRequestPath) then
    begin
      SuppressibleMsgBox('فایل ورودی تنظیمات دیتابیس پیدا نشد.', mbError, MB_OK, IDOK);
      Exit;
    end;
    Result := CopyFile(ExternalRequestPath, RequestPath, False);
    Exit;
  end;

  Json := '{' + #13#10 +
    '  "server": "' + JsonEscape(Trim(DatabasePage.Values[0])) + '",' + #13#10 +
    '  "database": "' + JsonEscape(Trim(DatabasePage.Values[1])) + '",' + #13#10 +
    '  "authentication": "' + CurrentAuthentication + '",' + #13#10 +
    '  "username": "' + JsonEscape(Trim(DatabasePage.Values[2])) + '",' + #13#10 +
    '  "password": "' + JsonEscape(DatabasePage.Values[3]) + '",' + #13#10 +
    '  "connectTimeoutSeconds": 15,' + #13#10 +
    '  "trustServerCertificate": true' + #13#10 +
    '}';

  SetArrayLength(Lines, 1);
  Lines[0] := Json;
  Result := SaveStringsToUTF8FileWithoutBOM(RequestPath, Lines, False);
end;

function ReadResultForLog(const Fallback: String): String;
var
  Content: AnsiString;
begin
  Result := Fallback;
  if LoadStringFromFile(ResultPath, Content) then
  begin
    Result := String(Content);
    Log('Installer operation result: ' + Result);
  end;
end;

function ExecutePowerShell(
  const ScriptPath: String;
  const Parameters: String;
  var ExitCode: Integer): Boolean;
var
  Arguments: String;
begin
  Arguments := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' +
    QuoteArgument(ScriptPath) + ' ' + Parameters;
  Log('Executing installer PowerShell script: ' + ExtractFileName(ScriptPath));
  Result := Exec(
    PowerShellPath,
    Arguments,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ExitCode);
end;

function ValidateDatabaseFields: Boolean;
begin
  Result := False;

  if Trim(DatabasePage.Values[0]) = '' then
  begin
    SuppressibleMsgBox('آدرس SQL Server را وارد کنید.', mbError, MB_OK, IDOK);
    Exit;
  end;
  if Trim(DatabasePage.Values[1]) = '' then
  begin
    SuppressibleMsgBox('نام دیتابیس را وارد کنید.', mbError, MB_OK, IDOK);
    Exit;
  end;
  if ContainsLineBreak(DatabasePage.Values[0]) or ContainsLineBreak(DatabasePage.Values[1]) then
  begin
    SuppressibleMsgBox('آدرس سرور و نام دیتابیس نباید چندخطی باشند.', mbError, MB_OK, IDOK);
    Exit;
  end;
  if AuthenticationPage.Values[SqlServerAuthenticationOption] then
  begin
    if Trim(DatabasePage.Values[2]) = '' then
    begin
      SuppressibleMsgBox('نام کاربری SQL را وارد کنید.', mbError, MB_OK, IDOK);
      Exit;
    end;
    if DatabasePage.Values[3] = '' then
    begin
      SuppressibleMsgBox('رمز عبور SQL را وارد کنید.', mbError, MB_OK, IDOK);
      Exit;
    end;
  end;

  Result := True;
end;

function TestDatabaseConnection(const ShowSuccess: Boolean): Boolean;
var
  ExitCode: Integer;
  Parameters: String;
begin
  Result := False;
  if not ValidateDatabaseFields then
    Exit;
  if not WriteDatabaseRequest then
  begin
    SuppressibleMsgBox('ساخت فایل موقت تنظیمات دیتابیس ناموفق بود.', mbError, MB_OK, IDOK);
    Exit;
  end;

  ExtractTemporaryFile('Installer.Common.ps1');
  ExtractTemporaryFile('Test-Database.ps1');
  DeleteFile(ResultPath);
  Parameters := '-RequestPath ' + QuoteArgument(RequestPath) +
    ' -ResultPath ' + QuoteArgument(ResultPath);

  WizardForm.NextButton.Enabled := False;
  TestConnectionButton.Enabled := False;
  try
    if ExecutePowerShell(
      ExpandConstant('{tmp}\Test-Database.ps1'),
      Parameters,
      ExitCode) and (ExitCode = 0) then
    begin
      Result := True;
      if ShowSuccess then
        SuppressibleMsgBox('اتصال به SQL Server با موفقیت برقرار شد.', mbInformation, MB_OK, IDOK);
    end
    else
      SuppressibleMsgBox(
        'اتصال به SQL Server ناموفق بود.' + #13#10 + ReadResultForLog(''),
        mbError,
        MB_OK,
        IDOK);
  finally
    WizardForm.NextButton.Enabled := True;
    TestConnectionButton.Enabled := True;
  end;
end;

procedure TestConnectionButtonClick(Sender: TObject);
begin
  TestDatabaseConnection(True);
end;

function ValidateIisFields: Boolean;
var
  Port: Integer;
begin
  Result := False;
  if (Trim(IisPage.Values[0]) = '') or
    (Length(Trim(IisPage.Values[0])) > 64) or
    not IsSafeSimpleValue(IisPage.Values[0]) or
    ContainsAnyCharacter(IisPage.Values[0], '\/:*?<>|[]') then
  begin
    SuppressibleMsgBox('نام سایت IIS معتبر نیست.', mbError, MB_OK, IDOK);
    Exit;
  end;
  Port := StrToIntDef(Trim(IisPage.Values[1]), -1);
  if (Port < 1) or (Port > 65535) then
  begin
    SuppressibleMsgBox('پورت باید عددی بین ۱ و ۶۵۵۳۵ باشد.', mbError, MB_OK, IDOK);
    Exit;
  end;
  if not IsSafeSimpleValue(IisPage.Values[2]) or
    not IsValidHostName(Trim(IisPage.Values[2])) then
  begin
    SuppressibleMsgBox('نام میزبان IIS معتبر نیست.', mbError, MB_OK, IDOK);
    Exit;
  end;
  Result := True;
end;

function TestIisBindingAvailability(const ShowSuccess: Boolean): Boolean;
var
  ExitCode: Integer;
  Parameters: String;
begin
  Result := False;
  if not ValidateIisFields then
    Exit;

  ExtractTemporaryFile('Installer.Common.ps1');
  ExtractTemporaryFile('Deploy-Iis.ps1');
  DeleteFile(ResultPath);
  Parameters := '-Action TestBinding' +
    ' -SiteName ' + QuoteArgument(IisPage.Values[0]) +
    ' -AppPoolName ' + QuoteArgument(AppPoolName) +
    ' -Port ' + IisPage.Values[1] +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  if Trim(IisPage.Values[2]) <> '' then
    Parameters := Parameters + ' -HostName ' + QuoteArgument(IisPage.Values[2]);

  WizardForm.NextButton.Enabled := False;
  TestPortButton.Enabled := False;
  try
    if ExecutePowerShell(
      ExpandConstant('{tmp}\Deploy-Iis.ps1'),
      Parameters,
      ExitCode) and (ExitCode = 0) then
    begin
      Result := True;
      if ShowSuccess then
        SuppressibleMsgBox(
          'پورت و Binding انتخاب‌شده برای IIS آزاد است.',
          mbInformation,
          MB_OK,
          IDOK);
    end
    else
      SuppressibleMsgBox(
        'پورت یا Binding انتخاب‌شده برای IIS قابل استفاده نیست.' + #13#10 + ReadResultForLog(''),
        mbError,
        MB_OK,
        IDOK);
  finally
    WizardForm.NextButton.Enabled := True;
    TestPortButton.Enabled := True;
  end;
end;

procedure TestPortButtonClick(Sender: TObject);
begin
  TestIisBindingAvailability(True);
end;

procedure InitializeWizard;
begin
  PreviousSiteName := GetPreviousData('SiteName', 'SmsHubNext');
  PreviousPort := GetPreviousData('Port', '8080');
  PreviousHostName := GetPreviousData('HostName', '');

  IisPage := CreateInputQueryPage(
    wpSelectDir,
    'تنظیمات IIS',
    'سایت برنامه',
    'مقادیر پیشنهادی برای اغلب نصب‌ها مناسب هستند.');
  IisPage.Add('نام سایت:', False);
  IisPage.Add('پورت HTTP:', False);
  IisPage.Add('Host name اختیاری:', False);
  IisPage.Values[0] := PreviousSiteName;
  IisPage.Values[1] := PreviousPort;
  IisPage.Values[2] := PreviousHostName;

  TestPortButton := TNewButton.Create(IisPage);
  TestPortButton.Parent := IisPage.Surface;
  TestPortButton.Caption := 'بررسی پورت';
  TestPortButton.Width := ScaleX(110);
  TestPortButton.Height := ScaleY(25);
  TestPortButton.Left := IisPage.SurfaceWidth - TestPortButton.Width;
  TestPortButton.Top := IisPage.Edits[2].Top + IisPage.Edits[2].Height + ScaleY(12);
  TestPortButton.OnClick := @TestPortButtonClick;

  MaintenancePage := CreateInputOptionPage(
    IisPage.ID,
    'اتصال دیتابیس',
    'تنظیم موجود یا اتصال جدید',
    'در ارتقا بهتر است تنظیم اتصال فعلی حفظ شود.',
    True,
    False);
  MaintenancePage.Add('استفاده از اتصال دیتابیس فعلی (پیشنهادی)');
  MaintenancePage.Add('تغییر اتصال دیتابیس');
  MaintenancePage.SelectedValueIndex := 0;

  AuthenticationPage := CreateInputOptionPage(
    MaintenancePage.ID,
    'روش اتصال SQL Server',
    'Authentication',
    'SQL Server Authentication برای نصب ساده‌تر پیشنهاد می‌شود.',
    True,
    False);
  AuthenticationPage.Add('SQL Server Authentication (پیشنهادی)');
  AuthenticationPage.Add('Windows Authentication (پیشرفته)');
  AuthenticationPage.Values[SqlServerAuthenticationOption] := True;
  AuthenticationPage.Values[WindowsAuthenticationOption] := False;

  DatabasePage := CreateInputQueryPage(
    AuthenticationPage.ID,
    'اتصال SQL Server',
    'مشخصات دیتابیس',
    'SQL Server می‌تواند روی همین سیستم یا یک سرور دیگر باشد.');
  DatabasePage.Add('Server / Instance:', False);
  DatabasePage.Add('Database:', False);
  DatabasePage.Add('Username:', False);
  DatabasePage.Add('Password:', True);
  DatabasePage.Values[0] := '.';
  DatabasePage.Values[1] := 'SmsHubNext';

  TestConnectionButton := TNewButton.Create(DatabasePage);
  TestConnectionButton.Parent := DatabasePage.Surface;
  TestConnectionButton.Caption := 'تست اتصال';
  TestConnectionButton.Width := ScaleX(110);
  TestConnectionButton.Height := ScaleY(25);
  TestConnectionButton.Left := DatabasePage.SurfaceWidth - TestConnectionButton.Width;
  TestConnectionButton.Top := DatabasePage.Edits[3].Top + DatabasePage.Edits[3].Height + ScaleY(12);
  TestConnectionButton.OnClick := @TestConnectionButtonClick;

  ExternalRequestPath := ExpandConstant('{param:SETUPREQUEST|}');
  RequestPath := ExpandConstant('{tmp}\SmsHubNext.DatabaseSetup.json');
  ResultPath := ExpandConstant('{tmp}\SmsHubNext.SetupResult.json');
  BackupPath := ExpandConstant('{commonappdata}\SmsHubNext\Installer\Backups\pending');
  UpdateExistingInstallationState;
  ReconfigureExistingDatabase := False;

  UpdateDatabaseCredentialControls;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = IisPage.ID then
  begin
    UpdateExistingInstallationState;
    IisPage.Edits[0].Enabled := not UpgradeInstallation;
  end;

  if CurPageID = DatabasePage.ID then
    UpdateDatabaseCredentialControls;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = MaintenancePage.ID then
    Result := not ExistingProductionSettings;

  if (PageID = AuthenticationPage.ID) or (PageID = DatabasePage.ID) then
  begin
    ReconfigureExistingDatabase := ExistingProductionSettings and
      (MaintenancePage.SelectedValueIndex = 1);
    Result := (ExternalRequestPath <> '') or not DatabaseConfigurationRequired;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = IisPage.ID then
    Result := TestIisBindingAvailability(False);

  if CurPageID = AuthenticationPage.ID then
    UpdateDatabaseCredentialControls;

  if (CurPageID = DatabasePage.ID) and DatabaseConfigurationRequired then
    Result := TestDatabaseConnection(False);
end;

function RunPrerequisite(
  const ScriptName: String;
  const Parameters: String;
  var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  DeleteFile(ResultPath);
  if not ExecutePowerShell(
    ExpandConstant('{tmp}\' + ScriptName),
    Parameters,
    ExitCode) then
  begin
    Result := 'اجرای پیش‌نیاز نصب ناموفق بود: ' + ScriptName;
    Exit;
  end;

  if ExitCode = 3010 then
  begin
    NeedsRestart := True;
    RestartRequiredByPrerequisite := True;
    Result := 'برای تکمیل پیش‌نیازها سیستم را Restart کنید و سپس همین Setup را دوباره اجرا کنید.';
    Exit;
  end;

  if ExitCode <> 0 then
    Result := 'نصب پیش‌نیاز ناموفق بود.' + #13#10 + ReadResultForLog('');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Parameters: String;
  ExitCode: Integer;
begin
  Result := '';
  NeedsRestart := False;
  RestartRequiredByPrerequisite := False;

  if not TestIisBindingAvailability(False) then
  begin
    Result := 'پورت یا Binding انتخاب‌شده برای IIS قابل استفاده نیست.';
    Exit;
  end;

  if WizardSilent and DatabaseConfigurationRequired and (ExternalRequestPath = '') then
  begin
    Result := 'در نصب silent باید مسیر فایل تنظیمات دیتابیس با پارامتر /SETUPREQUEST مشخص شود.';
    Exit;
  end;

  ExtractTemporaryFile('Installer.Common.ps1');
  ExtractTemporaryFile('Enable-Iis.ps1');
  ExtractTemporaryFile('Ensure-HostingBundle.ps1');
  ExtractTemporaryFile('Deploy-Iis.ps1');
  ExtractTemporaryFile('dotnet-hosting-win.exe');

  Result := RunPrerequisite(
    'Enable-Iis.ps1',
    '-ResultPath ' + QuoteArgument(ResultPath),
    NeedsRestart);
  if Result <> '' then
    Exit;

  Result := RunPrerequisite(
    'Deploy-Iis.ps1',
    '-Action EnsureServices' +
      ' -SiteName ' + QuoteArgument(IisPage.Values[0]) +
      ' -AppPoolName ' + QuoteArgument(AppPoolName) +
      ' -ResultPath ' + QuoteArgument(ResultPath),
    NeedsRestart);
  if Result <> '' then
    Exit;

  Result := RunPrerequisite(
    'Ensure-HostingBundle.ps1',
    '-InstallerPath ' + QuoteArgument(ExpandConstant('{tmp}\dotnet-hosting-win.exe')) +
      ' -ResultPath ' + QuoteArgument(ResultPath) +
      ' -RequiredMajorVersion 10',
    NeedsRestart);
  if Result <> '' then
    Exit;

  if DatabaseConfigurationRequired then
  begin
    if not WriteDatabaseRequest then
    begin
      Result := 'ساخت فایل موقت تنظیمات دیتابیس ناموفق بود.';
      Exit;
    end;
    if (not WizardSilent) and (ExternalRequestPath = '') and not TestDatabaseConnection(False) then
    begin
      Result := 'اتصال SQL Server ناموفق بود.';
      Exit;
    end;
  end;

  if UpgradeInstallation then
  begin
    Parameters := '-Action Backup' +
      ' -SiteName ' + QuoteArgument(PreviousSiteName) +
      ' -AppPoolName ' + QuoteArgument(AppPoolName) +
      ' -PhysicalPath ' + QuoteArgument(ExpandConstant('{app}')) +
      ' -BackupPath ' + QuoteArgument(BackupPath) +
      ' -ResultPath ' + QuoteArgument(ResultPath);
    DeleteFile(ResultPath);
    if (not ExecutePowerShell(
      ExpandConstant('{tmp}\Deploy-Iis.ps1'),
      Parameters,
      ExitCode)) or (ExitCode <> 0) then
    begin
      Result := 'تهیه نسخه پشتیبان پیش از ارتقا ناموفق بود.' + #13#10 + ReadResultForLog('');
      Exit;
    end;
    BackupCreated := True;
  end;
end;

procedure RunInstalledPowerShellOrFail(const ScriptName: String; const Parameters: String);
var
  ExitCode: Integer;
begin
  DeleteFile(ResultPath);
  if (not ExecutePowerShell(
    ExpandConstant('{app}\.installer\' + ScriptName),
    Parameters,
    ExitCode)) or (ExitCode <> 0) then
    RaiseException(ReadResultForLog('عملیات نصب ناموفق بود: ' + ScriptName));
end;

procedure RunSmsHubNextOrFail(const Parameters: String);
var
  ExitCode: Integer;
begin
  DeleteFile(ResultPath);
  Log('Executing SmsHubNext setup command.');
  if (not Exec(
    ExpandConstant('{app}\{#AppExecutable}'),
    Parameters,
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ExitCode)) or (ExitCode <> 0) then
    RaiseException(ReadResultForLog('پیکربندی SmsHubNext ناموفق بود.'));
end;

procedure RestorePreviousDeployment;
var
  Parameters: String;
  ExitCode: Integer;
begin
  if not BackupCreated then
    Exit;

  Log('Restoring the previous SmsHubNext deployment after installation failure.');
  Parameters := '-Action Restore' +
    ' -SiteName ' + QuoteArgument(PreviousSiteName) +
    ' -AppPoolName ' + QuoteArgument(AppPoolName) +
    ' -PhysicalPath ' + QuoteArgument(ExpandConstant('{app}')) +
    ' -BackupPath ' + QuoteArgument(BackupPath) +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  ExecutePowerShell(ExpandConstant('{tmp}\Deploy-Iis.ps1'), Parameters, ExitCode);

  Parameters := '-Action Ensure' +
    ' -SiteName ' + QuoteArgument(PreviousSiteName) +
    ' -AppPoolName ' + QuoteArgument(AppPoolName) +
    ' -PhysicalPath ' + QuoteArgument(ExpandConstant('{app}')) +
    ' -KeyRingPath ' + QuoteArgument(ExpandConstant('{commonappdata}\SmsHubNext\DataProtection-Keys')) +
    ' -LogsPath ' + QuoteArgument(ExpandConstant('{commonappdata}\SmsHubNext\Logs')) +
    ' -Port ' + PreviousPort +
    ' -HostName ' + QuoteArgument(PreviousHostName) +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  ExecutePowerShell(ExpandConstant('{tmp}\Deploy-Iis.ps1'), Parameters, ExitCode);

  Parameters := '-Action Start' +
    ' -SiteName ' + QuoteArgument(PreviousSiteName) +
    ' -AppPoolName ' + QuoteArgument(AppPoolName) +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  ExecutePowerShell(ExpandConstant('{tmp}\Deploy-Iis.ps1'), Parameters, ExitCode);
end;

procedure RemoveFailedFreshDeployment;
var
  Parameters: String;
  ExitCode: Integer;
begin
  if UpgradeInstallation then
    Exit;

  Log('Removing IIS resources created by a failed fresh installation.');
  Parameters := '-Action Remove' +
    ' -SiteName ' + QuoteArgument(IisPage.Values[0]) +
    ' -AppPoolName ' + QuoteArgument(AppPoolName) +
    ' -Port ' + IisPage.Values[1] +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  ExecutePowerShell(
    ExpandConstant('{app}\.installer\Deploy-Iis.ps1'),
    Parameters,
    ExitCode);
end;

procedure ConfigureInstalledApplication;
var
  Parameters: String;
begin
  if DatabaseConfigurationRequired then
  begin
    Parameters := 'setup configure' +
      ' --request ' + QuoteArgument(RequestPath) +
      ' --settings ' + QuoteArgument(ExpandConstant('{app}\appsettings.Production.json')) +
      ' --key-ring ' + QuoteArgument(ExpandConstant('{commonappdata}\SmsHubNext\DataProtection-Keys')) +
      ' --result ' + QuoteArgument(ResultPath);
    RunSmsHubNextOrFail(Parameters);
  end;

  Parameters := '-Action Ensure' +
    ' -SiteName ' + QuoteArgument(IisPage.Values[0]) +
    ' -AppPoolName ' + QuoteArgument(AppPoolName) +
    ' -PhysicalPath ' + QuoteArgument(ExpandConstant('{app}')) +
    ' -KeyRingPath ' + QuoteArgument(ExpandConstant('{commonappdata}\SmsHubNext\DataProtection-Keys')) +
    ' -LogsPath ' + QuoteArgument(ExpandConstant('{commonappdata}\SmsHubNext\Logs')) +
    ' -Port ' + IisPage.Values[1] +
    ' -HostName ' + QuoteArgument(IisPage.Values[2]) +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  RunInstalledPowerShellOrFail('Deploy-Iis.ps1', Parameters);

  Parameters := '-Action Start' +
    ' -SiteName ' + QuoteArgument(IisPage.Values[0]) +
    ' -AppPoolName ' + QuoteArgument(AppPoolName) +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  RunInstalledPowerShellOrFail('Deploy-Iis.ps1', Parameters);

  Parameters := '-Port ' + IisPage.Values[1] +
    ' -HostName ' + QuoteArgument(IisPage.Values[2]) +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  RunInstalledPowerShellOrFail('Test-Health.ps1', Parameters);
end;

procedure DeleteDeploymentBackup;
var
  Parameters: String;
  ExitCode: Integer;
begin
  if not BackupCreated then
    Exit;
  Parameters := '-Action DeleteBackup' +
    ' -SiteName ' + QuoteArgument(IisPage.Values[0]) +
    ' -AppPoolName ' + QuoteArgument(AppPoolName) +
    ' -BackupPath ' + QuoteArgument(BackupPath) +
    ' -ResultPath ' + QuoteArgument(ResultPath);
  ExecutePowerShell(
    ExpandConstant('{app}\.installer\Deploy-Iis.ps1'),
    Parameters,
    ExitCode);
  BackupCreated := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    try
      ConfigureInstalledApplication;
      DeleteDeploymentBackup;
      DeleteFile(RequestPath);
    except
      if BackupCreated then
        RestorePreviousDeployment
      else
        RemoveFailedFreshDeployment;
      RaiseException(GetExceptionMessage);
    end;
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'SiteName', IisPage.Values[0]);
  SetPreviousData(PreviousDataKey, 'Port', IisPage.Values[1]);
  SetPreviousData(PreviousDataKey, 'HostName', IisPage.Values[2]);
end;

procedure DeinitializeSetup;
begin
  DeleteFile(RequestPath);
  DeleteFile(ResultPath);
end;

function NeedRestart: Boolean;
begin
  Result := RestartRequiredByPrerequisite;
end;

function GetSiteName(Param: String): String;
begin
  Result := IisPage.Values[0];
end;

function GetSitePort(Param: String): String;
begin
  Result := IisPage.Values[1];
end;

function GetHostName(Param: String): String;
begin
  Result := IisPage.Values[2];
end;

function GetLaunchUrl(Param: String): String;
var
  Host: String;
begin
  Host := Trim(IisPage.Values[2]);
  if Host = '' then
    Host := 'localhost';
  Result := 'http://' + Host + ':' + IisPage.Values[1] + '/';
end;

function UpdateReadyMemo(
  Space: String;
  NewLine: String;
  MemoUserInfoInfo: String;
  MemoDirInfo: String;
  MemoTypeInfo: String;
  MemoComponentsInfo: String;
  MemoGroupInfo: String;
  MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine +
    'IIS:' + NewLine +
    Space + 'Site: ' + IisPage.Values[0] + NewLine +
    Space + 'Address: ' + GetLaunchUrl('') + NewLine + NewLine;

  if DatabaseConfigurationRequired then
    Result := Result + 'Database: connection will be tested and configured.'
  else
    Result := Result + 'Database: existing production connection will be preserved.';
end;
