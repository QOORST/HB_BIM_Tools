# Tools

`Tools` 只保留開發與授權管理用的輔助工具。使用者端部署請以正式安裝檔為準，不再維護舊式手動部署包流程。

## 保留工具

- `GenerateLicenseKey.ps1`：使用授權私鑰產生 HB_BIM Tools 授權碼，並寫入授權紀錄。

## 正式建置與發佈入口

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Installer\Build_Installer.ps1
```

此流程會準備 installer payload、打包支援版本、簽署 DLL/安裝檔，並輸出 `Output\HB_BIM_Tools_v*_Setup.exe`。

## 已移除流程

舊式 `PrepareDeployment.ps1`、`Deploy.ps1`、`UpdateAll.ps1` 與手動部署 README 已移除。那些流程不會完整處理安裝器解除安裝資訊、LAN 憑證匯入與新版 installer payload，容易造成不同電腦安裝狀態不一致。
