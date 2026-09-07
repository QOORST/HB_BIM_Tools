# Tools

`Tools` 只保留開發與授權管理用的輔助工具。使用者端部署請以正式安裝檔為準，不再維護舊式手動部署包流程。

## 保留工具

- `GenerateLicenseKey.ps1`：使用授權私鑰產生 HB_BIM Tools 授權碼，並寫入授權紀錄。
- `Check-DevelopmentEnvironment.ps1`：檢查新電腦的 Git、.NET、Revit API、發版工具、公司族庫 payload 與憑證狀態，不會讀取或複製私鑰內容。

## 新電腦環境檢查

一般開發電腦使用預設模式。公司族庫、Inno Setup 與簽章私鑰不存在時不會阻止其他工具開發：

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Tools\Check-DevelopmentEnvironment.ps1 -Mode Development -RestorePackages
```

公司電腦在建立正式安裝檔前使用發版模式：

```powershell
.\Tools\Check-DevelopmentEnvironment.ps1 -Mode Release -RestorePackages
```

只有需要管理授權的授權電腦才指定外部私鑰路徑：

```powershell
.\Tools\Check-DevelopmentEnvironment.ps1 `
    -Mode Release `
    -CheckLicenseAdministration `
    -LicensePrivateKeyPath "D:\HB_BIM-Secrets\license-private-key.pfx"
```

私鑰必須位於 Git 專案外；腳本只確認檔案存在，不會讀取內容。

## 正式建置與發佈入口

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Installer\Build_Installer.ps1
```

此流程會準備 installer payload、打包支援版本、簽署 DLL/安裝檔，並輸出 `Output\HB_BIM_Tools_v*_Setup.exe`。

## 已移除流程

舊式 `PrepareDeployment.ps1`、`Deploy.ps1`、`UpdateAll.ps1` 與手動部署 README 已移除。那些流程不會完整處理安裝器解除安裝資訊、LAN 憑證匯入與新版 installer payload，容易造成不同電腦安裝狀態不一致。
