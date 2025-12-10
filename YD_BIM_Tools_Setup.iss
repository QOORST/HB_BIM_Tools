; YD BIM Tools Inno Setup Script
; 版本: 2.2.5

#define MyAppName "YD BIM Tools"
#define MyAppVersion "2.2.5"
#define MyAppPublisher "YD_BIM Tools Team"
#define MyAppURL "https://github.com/QOORST/YD_BIM_Tools"

[Setup]
AppId={{A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={commonpf}\YD_BIM_Tools
DisableDirPage=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=YD_BIM_Tools_v{#MyAppVersion}_Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "revit2024"; Description: "安裝到 Revit 2024"; GroupDescription: "選擇要安裝的 Revit 版本:"; Flags: unchecked
Name: "revit2025"; Description: "安裝到 Revit 2025"; GroupDescription: "選擇要安裝的 Revit 版本:"; Flags: unchecked

[Files]
; Revit 2024 檔案
Source: "bin\Release2024\*.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\YD_BIM"; Flags: ignoreversion; Tasks: revit2024; Excludes: "*RevitAPI*.dll"
Source: "Addins\YD_RevitTools.LicenseManager_2024.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024"; DestName: "YD_RevitTools.LicenseManager.addin"; Flags: ignoreversion; Tasks: revit2024

; Revit 2025 檔案
Source: "bin\Release2025\*.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\YD_BIM"; Flags: ignoreversion; Tasks: revit2025; Excludes: "*RevitAPI*.dll"
Source: "Addins\YD_RevitTools.LicenseManager_2025.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "YD_RevitTools.LicenseManager.addin"; Flags: ignoreversion; Tasks: revit2025

[Icons]
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Code]
function InitializeSetup(): Boolean;
var
  Revit2024Installed, Revit2025Installed: Boolean;
begin
  Result := True;
  
  // 檢查 Revit 2024 是否安裝
  Revit2024Installed := DirExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2024'));
  
  // 檢查 Revit 2025 是否安裝
  Revit2025Installed := DirExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2025'));
  
  if not Revit2024Installed and not Revit2025Installed then
  begin
    MsgBox('未偵測到 Revit 2024 或 2025。' + #13#10 + 
           '請確認已安裝 Autodesk Revit 2024 或 2025。', mbError, MB_OK);
    Result := False;
  end
  else
  begin
    if Revit2024Installed then
      WizardForm.TasksList.Checked[0] := True;
    if Revit2025Installed then
      WizardForm.TasksList.Checked[1] := True;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  
  if CurPageID = wpSelectTasks then
  begin
    if not WizardForm.TasksList.Checked[0] and not WizardForm.TasksList.Checked[1] then
    begin
      MsgBox('請至少選擇一個 Revit 版本進行安裝。', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nIt is recommended that you close all Revit applications before continuing.

