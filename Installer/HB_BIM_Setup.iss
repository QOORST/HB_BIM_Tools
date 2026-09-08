; HB_BIM Tools 安裝腳本 - Inno Setup
; 版本: 2.5.13
; 日期: 2026-08-31
; 支援: Revit 2022, 2024, 2025, 2026

#define MyAppName "HB_BIM Tools"
#define MyAppVersion "2.5.13"
#define MyAppPublisher "LAN"
#define MyAppURL "https://www.ydbim.com"
#define LanCertificateThumbprint "5EBE6DDEEBEBE5194CBDC9E71CDE8E6BB91AB166"
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
DefaultDirName={tmp}\HB_BIM_Temp
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DirExistsWarning=no
DisableDirPage=yes

; 輸出設定
OutputDir=..\Output
OutputBaseFilename=HB_BIM_Tools_v{#MyAppVersion}_Setup
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
Name: "revit2022"; Description: "Install to Revit 2022"; GroupDescription: "Select Revit versions to install:"; Check: IsRevitInstalled('2022')
Name: "revit2024"; Description: "Install to Revit 2024"; GroupDescription: "Select Revit versions to install:"; Check: IsRevitInstalled('2024')
Name: "revit2025"; Description: "Install to Revit 2025"; GroupDescription: "Select Revit versions to install:"; Check: IsRevitInstalled('2025')
Name: "revit2026"; Description: "Install to Revit 2026"; GroupDescription: "Select Revit versions to install:"; Check: IsRevitInstalled('2026')

[Files]
; LAN code signing public certificate. Imported by [Run] so Revit trusts the signed add-in on target computers.
Source: "LAN_CodeSigning.cer"; DestDir: "{app}\Certificates"; Flags: ignoreversion

; 共用 Resources（所有版本共用）- 如果不存在則跳過
Source: "Resources\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: revit2022
Source: "Resources\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: revit2024
Source: "Resources\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: revit2025
Source: "Resources\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: revit2026

; 共用依賴項（所有版本共用）
Source: "Newtonsoft.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion; Tasks: revit2022
Source: "Newtonsoft.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "Newtonsoft.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "Newtonsoft.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion; Tasks: revit2026

; System.Text.Json 及其依賴項（自動更新功能需要）
Source: "System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion; Tasks: revit2022
Source: "System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "System.Text.Json.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion; Tasks: revit2026

Source: "System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Text.Encodings.Web.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Memory.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Buffers.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

; DocumentFormat.OpenXml（Excel/PPT 匯出需要）
Source: "DocumentFormat.OpenXml.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "DocumentFormat.OpenXml.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "2025\DocumentFormat.OpenXml.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "2026\DocumentFormat.OpenXml.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026
Source: "2025\System.IO.Packaging.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "2026\System.IO.Packaging.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

; EPPlus 及其依賴項（Excel 功能需要）
Source: "EPPlus.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "EPPlus.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "EPPlus.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "EPPlus.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "EPPlus.Interfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "EPPlus.Interfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "EPPlus.Interfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "EPPlus.Interfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "EPPlus.System.Drawing.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "EPPlus.System.Drawing.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "EPPlus.System.Drawing.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "EPPlus.System.Drawing.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "Microsoft.IO.RecyclableMemoryStream.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "Microsoft.IO.RecyclableMemoryStream.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "Microsoft.IO.RecyclableMemoryStream.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "Microsoft.IO.RecyclableMemoryStream.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

; SQLite（釋疑追蹤資料庫需要）
Source: "Microsoft.Data.Sqlite.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "Microsoft.Data.Sqlite.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "Microsoft.Data.Sqlite.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "Microsoft.Data.Sqlite.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "SQLitePCLRaw.batteries_v2.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "SQLitePCLRaw.batteries_v2.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "SQLitePCLRaw.batteries_v2.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "SQLitePCLRaw.batteries_v2.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "SQLitePCLRaw.core.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "SQLitePCLRaw.core.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "SQLitePCLRaw.core.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "SQLitePCLRaw.core.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "SQLitePCLRaw.provider.dynamic_cdecl.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "SQLitePCLRaw.provider.dynamic_cdecl.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "SQLitePCLRaw.provider.dynamic_cdecl.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "SQLitePCLRaw.provider.dynamic_cdecl.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

; Patched native SQLite library. Keep this at the add-in root where dynamic_cdecl resolves it.
Source: "e_sqlite3.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion; Tasks: revit2022
Source: "e_sqlite3.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "e_sqlite3.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "e_sqlite3.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion; Tasks: revit2026

Source: "runtimes\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: revit2022
Source: "runtimes\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: revit2024
Source: "runtimes\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: revit2025
Source: "runtimes\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Tasks: revit2026

Source: "Microsoft.Bcl.AsyncInterfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "Microsoft.Bcl.AsyncInterfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "Microsoft.Bcl.AsyncInterfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "Microsoft.Bcl.AsyncInterfaces.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.ComponentModel.Annotations.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.ComponentModel.Annotations.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.ComponentModel.Annotations.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.ComponentModel.Annotations.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Drawing.Common.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.Drawing.Common.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Drawing.Common.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Drawing.Common.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Numerics.Vectors.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.Numerics.Vectors.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Numerics.Vectors.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Numerics.Vectors.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Text.Encoding.CodePages.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.Text.Encoding.CodePages.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Text.Encoding.CodePages.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Text.Encoding.CodePages.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.Threading.Tasks.Extensions.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.Threading.Tasks.Extensions.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.Threading.Tasks.Extensions.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.Threading.Tasks.Extensions.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

Source: "System.ValueTuple.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2022
Source: "System.ValueTuple.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2024
Source: "System.ValueTuple.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2025
Source: "System.ValueTuple.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion skipifsourcedoesntexist; Tasks: revit2026

; Revit 2022 DLL
Source: "2022\YD_RevitTools.LicenseManager.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion uninsrestartdelete; Tasks: revit2022
Source: "2022\CompanyFamilyLibraryMvp.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion uninsrestartdelete skipifsourcedoesntexist; Tasks: revit2022

; Revit 2024 DLL
Source: "2024\YD_RevitTools.LicenseManager.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion uninsrestartdelete; Tasks: revit2024
Source: "2024\CompanyFamilyLibraryMvp.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion uninsrestartdelete skipifsourcedoesntexist; Tasks: revit2024

; Revit 2025 DLL
Source: "2025\YD_RevitTools.LicenseManager.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion uninsrestartdelete; Tasks: revit2025
Source: "2025\CompanyFamilyLibraryMvp.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion uninsrestartdelete skipifsourcedoesntexist; Tasks: revit2025

; Revit 2026 DLL
Source: "2026\YD_RevitTools.LicenseManager.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion uninsrestartdelete; Tasks: revit2026
Source: "2026\CompanyFamilyLibraryMvp.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion uninsrestartdelete skipifsourcedoesntexist; Tasks: revit2026

; 其他附件 (可選)
Source: "README.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion; Tasks: revit2022
Source: "LICENSE.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion; Tasks: revit2022
Source: "version.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM"; Flags: ignoreversion; Tasks: revit2022
Source: "README.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "LICENSE.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "version.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM"; Flags: ignoreversion; Tasks: revit2024
Source: "README.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "LICENSE.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "version.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM"; Flags: ignoreversion; Tasks: revit2025
Source: "README.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion; Tasks: revit2026
Source: "LICENSE.txt"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion; Tasks: revit2026
Source: "version.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM"; Flags: ignoreversion; Tasks: revit2026

[Icons]
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{group}\Documentation"; Filename: "{app}\README.txt"

[Run]
; Trust LAN self-signed code signing certificate for Revit add-in signature validation on target computers.
Filename: "{sys}\certutil.exe"; Parameters: "-addstore -f Root ""{app}\Certificates\LAN_CodeSigning.cer"""; Flags: runhidden waituntilterminated; StatusMsg: "Installing LAN root certificate..."
Filename: "{sys}\certutil.exe"; Parameters: "-addstore -f TrustedPublisher ""{app}\Certificates\LAN_CodeSigning.cer"""; Flags: runhidden waituntilterminated; StatusMsg: "Installing LAN trusted publisher certificate..."

[UninstallRun]
; Remove only the dedicated HB_BIM Tools certificate installed above.
Filename: "{sys}\certutil.exe"; Parameters: "-delstore Root {#LanCertificateThumbprint}"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveLanRootCertificate"
Filename: "{sys}\certutil.exe"; Parameters: "-delstore TrustedPublisher {#LanCertificateThumbprint}"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveLanPublisherCertificate"

[Code]
var
  Revit2022Installed: Boolean;
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
  Revit2022Installed := IsRevitInstalled('2022');
  Revit2024Installed := IsRevitInstalled('2024');
  Revit2025Installed := IsRevitInstalled('2025');
  Revit2026Installed := IsRevitInstalled('2026');

  // 如果沒有任何版本的 Revit 安裝
  if not (Revit2022Installed or Revit2024Installed or Revit2025Installed or Revit2026Installed) then
  begin
    Message := 'No supported Revit version (2022, 2024-2026) detected.' + #13#10 + #13#10 +
               'This add-in requires one of the following Revit versions:' + #13#10 +
               '  • Autodesk Revit 2022' + #13#10 +
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


// 清理目前使用者層舊版 YD 外掛，避免 Revit 優先載入未簽章舊 DLL。
procedure CleanLegacyUserAddins(Version: String);
var
  UserAddinsPath: String;
  CommonAddinsPath: String;
begin
  UserAddinsPath := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' + Version);
  CommonAddinsPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version);

  DeleteFile(UserAddinsPath + '\YD_RevitTools.LicenseManager.addin');
  DeleteFile(UserAddinsPath + '\YD_RevitTools.LicenseManager_' + Version + '.addin');
  DeleteFile(UserAddinsPath + '\YD_BIM_Tools.addin');
  DelTree(UserAddinsPath + '\YD_BIM_Tools', True, True, True);
  DelTree(UserAddinsPath + '\YD_BIM', True, True, True);

  DeleteFile(CommonAddinsPath + '\YD_RevitTools.LicenseManager.addin');
  DeleteFile(CommonAddinsPath + '\YD_RevitTools.LicenseManager_' + Version + '.addin');
  DeleteFile(CommonAddinsPath + '\YD_BIM_Tools.addin');
  DelTree(CommonAddinsPath + '\YD_BIM_Tools', True, True, True);
  DelTree(CommonAddinsPath + '\YD_BIM', True, True, True);
end;
// 建立 .addin 檔案
procedure CreateAddinFile(Version: String);
var
  AddinPath: String;
  AddinContent: String;
  DllPath: String;
begin
  AddinPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version + '\HB_BIM_Tools.addin');
  DllPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version + '\HB_BIM\YD_RevitTools.LicenseManager.dll');

  // 如果檔案已存在，先刪除
  if FileExists(AddinPath) then
    DeleteFile(AddinPath);
  DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version + '\YD_RevitTools.LicenseManager.addin'));

  AddinContent := '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
                  '<RevitAddIns>' + #13#10 +
                  '  <AddIn Type="Application">' + #13#10 +
                  '    <Name>HB_BIM Tools</Name>' + #13#10 +
                  '    <Assembly>' + DllPath + '</Assembly>' + #13#10 +
                  '    <FullClassName>YD_RevitTools.LicenseManager.App</FullClassName>' + #13#10 +
                  '    <ClientId>B3F5D2D4-9392-4A9E-9C0D-A6F5DD93FAC7</ClientId>' + #13#10 +
                  '    <VendorId>LAN</VendorId>' + #13#10 +
                  '    <VendorDescription>LAN, HB_BIM Tools, www.ydbim.com</VendorDescription>' + #13#10 +
                  '  </AddIn>' + #13#10 +
                  '</RevitAddIns>';

  // False = 覆蓋模式（不追加）
  SaveStringToFile(AddinPath, AddinContent, False);
end;

// 清理舊版本檔案
procedure CleanOldVersion(Version: String);
var
  OldDllPath: String;
begin
  OldDllPath := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version + '\HB_BIM\YD_RevitTools.LicenseManager.dll');

  // 如果舊版本 DLL 存在，刪除它
  if FileExists(OldDllPath) then
  begin
    DeleteFile(OldDllPath);
  end;
end;

// 安裝完成後
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // 安裝前先清理舊版本
    if WizardIsTaskSelected('revit2022') then
    begin
      CleanOldVersion('2022');
      CleanLegacyUserAddins('2022');
    end;

    if WizardIsTaskSelected('revit2024') then
    begin
      CleanOldVersion('2024');
      CleanLegacyUserAddins('2024');
    end;

    if WizardIsTaskSelected('revit2025') then
    begin
      CleanOldVersion('2025');
      CleanLegacyUserAddins('2025');
    end;

    if WizardIsTaskSelected('revit2026') then
    begin
      CleanOldVersion('2026');
      CleanLegacyUserAddins('2026');
    end;
  end;

  if CurStep = ssPostInstall then
  begin
    // 為每個選擇的版本建立 .addin 檔案
    if WizardIsTaskSelected('revit2022') then
      CreateAddinFile('2022');

    if WizardIsTaskSelected('revit2024') then
      CreateAddinFile('2024');

    if WizardIsTaskSelected('revit2025') then
      CreateAddinFile('2025');

    if WizardIsTaskSelected('revit2026') then
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
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM_Tools.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM_Tools.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM_Tools.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM_Tools.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2022\YD_RevitTools.LicenseManager.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2024\YD_RevitTools.LicenseManager.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2025\YD_RevitTools.LicenseManager.addin'));
    DeleteFile(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2026\YD_RevitTools.LicenseManager.addin'));
    CleanLegacyUserAddins('2022');
    CleanLegacyUserAddins('2024');
    CleanLegacyUserAddins('2025');
    CleanLegacyUserAddins('2026');

    // 刪除目錄（如果為空）
    RemoveDir(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2022\HB_BIM'));
    RemoveDir(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2024\HB_BIM'));
    RemoveDir(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2025\HB_BIM'));
    RemoveDir(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2026\HB_BIM'));
  end;
end;

[Messages]
WelcomeLabel1=歡迎使用 [name] 安裝精靈
WelcomeLabel2=這將在您的電腦上安裝 [name/ver]。%n%n本安裝程式支援 Revit 2022、2024、2025 和 2026。%n%n建議您在繼續之前關閉所有 Revit 應用程式。
FinishedLabel=安裝程式已在您的電腦上安裝 [name]。%n%n請重新啟動 Revit 以載入外掛。%n%n已安裝到以下版本：%n• 您選擇的 Revit 版本





