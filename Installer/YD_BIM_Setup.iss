; YD_BIM 工具安裝腳本 - Inno Setup
; 版本: 2.2.4
; 日期: 2025-12-08
; 支援: Revit 2024, 2025, 2026

#define MyAppName "YD_BIM Tools"
#define MyAppVersion "2.2.4"
#define MyAppPublisher "YD_BIM Owen"
#define MyAppURL "http://www.ydbim.com"
#define MyAppExeName "YD_RevitTools.LicenseManager.dll"
#define MyAppSupportEmail "qoorst123@yesdir.com.tw"

[Setup]
; 應用程式基本資訊
AppId={{B3F5D2D4-9392-4A9E-9C0D-A6F5DD93FAC7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppContact={#MyAppSupportEmail}

; 預設安裝路徑（不使用固定路徑，改為動態選擇）
DefaultDirName={tmp}\YD_BIM_Temp
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DirExistsWarning=no
DisableDirPage=yes

; 輸出設定
OutputDir=..\Output
OutputBaseFilename=YD_BIM_Tools_v{#MyAppVersion}_Setup
; SetupIconFile=..\Resources\Icons\license_32.png  ; PNG 不支援，需要 ICO 檔案

; 壓縮設定
Compression=lzma2
SolidCompression=no

; 安裝模式
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 相容性設定
DisableReadyPage=no
AllowNoIcons=yes
AlwaysShowDirOnReadyPage=no
AlwaysShowGroupOnReadyPage=no

; 介面設定
WizardStyle=modern
DisableWelcomePage=no
LicenseFile=..\LICENSE.txt
InfoBeforeFile=..\README.txt

; 語言
ShowLanguageDialog=no

[Languages]
Name: "chinesetrad"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "revit2024"; Description: "Install to Revit 2024"; GroupDescription: "Select Revit versions to install:"; Check: IsRevitInstalled('2024')
Name: "revit2025"; Description: "Install to Revit 2025"; GroupDescription: "Select Revit versions to install:"; Check: IsRevitInstalled('2025')
Name: "revit2026"; Description: "Install to Revit 2026"; GroupDescription: "Select Revit versions to install:"; Check: IsRevitInstalled('2026')

[Files]
; 共用圖示資源（所有版本共用）
Source: "Resources\Icons\*.png"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM\Resources\Icons"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2024
Source: "Resources\Icons\*.png"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM\Resources\Icons"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2025
Source: "Resources\Icons\*.png"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM\Resources\Icons"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: revit2026

; 共用依賴項（所有版本共用）
Source: "Newtonsoft.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "Newtonsoft.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "Newtonsoft.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion; Tasks: revit2026

; System.Text.Json 及其依賴項（自動更新功能需要）
Source: "System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion; Tasks: revit2026

Source: "System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

; Revit 2024 DLL
Source: "2024\YD_RevitTools.LicenseManager.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion; Tasks: revit2024

; Revit 2025 DLL
Source: "2025\YD_RevitTools.LicenseManager.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion; Tasks: revit2025

; Revit 2026 DLL
Source: "2026\YD_RevitTools.LicenseManager.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion; Tasks: revit2026

; 其他附件 (可選)
Source: "README.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "LICENSE.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "README.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "LICENSE.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "README.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion; Tasks: revit2026
Source: "LICENSE.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM"; Flags: ignoreversion; Tasks: revit2026

[Icons]
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{group}\Documentation"; Filename: "{app}\README.txt"

[Code]
var
  Revit2024Installed: Boolean;
  Revit2025Installed: Boolean;
  Revit2026Installed: Boolean;

// 檢查指定版本的 Revit 是否已安裝
function IsRevitInstalled(Version: String): Boolean;
var
  RevitPath: String;
  RegPath: String;
begin
  RegPath := 'SOFTWARE\Autodesk\Revit\' + Version;

  // 先檢查 64-bit 註冊表
  Result := RegQueryStringValue(HKLM64, RegPath, 'InstallPath', RevitPath);

  // 如果沒找到，檢查 32-bit 註冊表
  if not Result then
    Result := RegQueryStringValue(HKLM32, RegPath, 'InstallPath', RevitPath);

  // 如果沒找到，檢查目錄是否存在
  if not Result then
  begin
    Result := DirExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version));
  end;
end;

// 初始化安裝程式
function InitializeSetup: Boolean;
var
  Message: String;
begin
  Result := True;

  // 檢查各版本 Revit 安裝狀態
  Revit2024Installed := IsRevitInstalled('2024');
  Revit2025Installed := IsRevitInstalled('2025');
  Revit2026Installed := IsRevitInstalled('2026');

  // 如果沒有任何版本的 Revit 安裝
  if not (Revit2024Installed or Revit2025Installed or Revit2026Installed) then
  begin
    Message := 'No supported Revit version (2024-2026) detected.' + #13#10 + #13#10 +
               'This add-in requires one of the following Revit versions:' + #13#10 +
               '  • Autodesk Revit 2024' + #13#10 +
               '  • Autodesk Revit 2025' + #13#10 +
               '  • Autodesk Revit 2026' + #13#10 + #13#10 +
               'Do you want to continue the installation?';

    if MsgBox(Message, mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;

// 建立 .addin 檔案
procedure CreateAddinFile(Version: String);
var
  AddinPath: String;
  AddinContent: String;
  DllPath: String;
begin
  AddinPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version + '\YD_RevitTools.LicenseManager.addin');
  DllPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version + '\YD_BIM\YD_RevitTools.LicenseManager.dll');

  // 如果檔案已存在，先刪除
  if FileExists(AddinPath) then
    DeleteFile(AddinPath);

  AddinContent := '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
                  '<RevitAddIns>' + #13#10 +
                  '  <AddIn Type="Application">' + #13#10 +
                  '    <Name>YD_BIM Tools</Name>' + #13#10 +
                  '    <Assembly>' + DllPath + '</Assembly>' + #13#10 +
                  '    <FullClassName>YD_RevitTools.LicenseManager.App</FullClassName>' + #13#10 +
                  '    <ClientId>B3F5D2D4-9392-4A9E-9C0D-A6F5DD93FAC7</ClientId>' + #13#10 +
                  '    <VendorId>YD</VendorId>' + #13#10 +
                  '    <VendorDescription>YD_BIM Tools, www.ydbim.com</VendorDescription>' + #13#10 +
                  '  </AddIn>' + #13#10 +
                  '</RevitAddIns>';

  // False = 覆蓋模式（不追加）
  SaveStringToFile(AddinPath, AddinContent, False);
end;

// 安裝完成後
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // 為每個選擇的版本建立 .addin 檔案
    if IsTaskSelected('revit2024') then
      CreateAddinFile('2024');

    if IsTaskSelected('revit2025') then
      CreateAddinFile('2025');

    if IsTaskSelected('revit2026') then
      CreateAddinFile('2026');
  end;
end;

// 檢查 Revit 是否正在運行
function IsRevitRunning: Boolean;
var
  ResultCode: Integer;
begin
  // 使用 tasklist 檢查 Revit.exe 是否在運行
  Result := Exec('cmd.exe', '/c tasklist /FI "IMAGENAME eq Revit.exe" 2>NUL | find /I /N "Revit.exe">NUL', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

// 解除安裝前
function InitializeUninstall(): Boolean;
begin
  Result := True;

  // 檢查 Revit 是否正在運行
  if IsRevitRunning then
  begin
    MsgBox('偵測到 Revit 正在運行。' + #13#10 + #13#10 +
           '請先關閉所有 Revit 應用程式再進行解除安裝。',
           mbError, MB_OK);
    Result := False;
  end;
end;

// 解除安裝完成後
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 刪除 .addin 檔案
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2024\YD_RevitTools.LicenseManager.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2025\YD_RevitTools.LicenseManager.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2026\YD_RevitTools.LicenseManager.addin'));

    // 刪除目錄（如果為空）
    RemoveDir(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM'));
    RemoveDir(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM'));
    RemoveDir(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2026\YD_BIM'));
  end;
end;

[Messages]
WelcomeLabel1=歡迎使用 [name] 安裝精靈
WelcomeLabel2=這將在您的電腦上安裝 [name/ver]。%n%n本安裝程式支援 Revit 2024、2025 和 2026。%n%n建議您在繼續之前關閉所有 Revit 應用程式。
FinishedLabel=安裝程式已在您的電腦上安裝 [name]。%n%n請重新啟動 Revit 以載入外掛。%n%n已安裝到以下版本：%n• 您選擇的 Revit 版本

