# 編譯指令與測試清單

## 前置需求

- Windows 10/11 (64-bit)
- Visual Studio 2022 或 .NET SDK 8.0
- Revit 2022 / 2024 / 2025 / 2026（用於測試參考）

新電腦完成 clone 後，先執行：

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Tools\Check-DevelopmentEnvironment.ps1 -Mode Development -RestorePackages
```

一般開發模式不要求公司族庫原始碼、Inno Setup、簽章私鑰或授權私鑰。缺少公司族庫時只略過該選配模組，其餘 HB_BIM Tools 仍可編譯。

---

## 編譯指令

### 使用 Visual Studio

1. 打開 `YD_RevitTools.LicenseManager.sln`
2. 選擇配置：`Debug2024` / `Release2024` / `Debug2025` 等
3. 選擇平台：`x64`
4. 執行 `Build > Build Solution`

### 使用命令行 (PowerShell)

```powershell
cd "C:\Users\BIMer\Desktop\工作區\Revit API\YD_RevitTools.LicenseManager\YD_RevitTools.LicenseManager"

# 編譯所有配置
dotnet build -c Debug2022
dotnet build -c Debug2024
dotnet build -c Debug2025
dotnet build -c Debug2026

# 編譯 Release 版本（用於發布）
dotnet build -c Release2022
dotnet build -c Release2024
dotnet build -c Release2025
dotnet build -c Release2026
```

---

## 建立正式安裝檔

正式發佈請使用安裝檔建置腳本，不再使用舊式手動部署包：

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Tools\Check-DevelopmentEnvironment.ps1 -Mode Release -RestorePackages
.\Installer\Build_Installer.ps1
```

正式發版模式要求所有支援 Revit 年版、Inno Setup、LAN 簽章私鑰憑證，以及各年版公司族庫 payload。建議固定在可連公司內網的公司電腦執行。

輸出位置：

```text
Output\HB_BIM_Tools_v*_Setup.exe
```

---

## 本次修改檢查清單

### ✓ LicenseManager.cs

- [x] `GetMachineCode()` 改為回傳新版機器碼
- [x] `GetMachineCodes()` 新增，返回 `[v2, legacy]` 陣列
- [x] `GetLegacyMachineCode()` 新增，舊版算法 (`MachineName + UserName + ProcessorCount`)
- [x] `GetMachineCodeV2()` 新增，硬體指紋算法 (`CPU ID | BaseBoard Serial | MachineName`)
- [x] `GetCpuId()` 新增，WMI 讀取 CPU ProcessorId
- [x] `GetBaseBoardSerialNumber()` 新增，WMI 讀取主機板序號
- [x] `VerifyStoredLicense()` 更新，支援新/舊版機器碼比對
- [x] `VerifyLicenseSignature()` 改用 `RSA.Create()` + `ImportParameters`
- [x] `ParseRsaPublicKeyXml()` 新增，XML 解析輔助方法
- [x] 移除 `RSACryptoServiceProvider` 使用

### ✓ Services/UpdateService.cs

- [x] `LaunchInstaller()` 移除 .cmd 腳本，改用背景執行緒直接啟動
- [x] `IsProcessRunning()` 新增，進程輪詢方法
- [x] 新增 `using System.Linq;`
- [x] 移除 `HB_BIM_WaitAndInstall.cmd` 相關代碼

---

## 測試清單

### 測試 1：既有授權 backward compatibility

**目的：** 確認舊版授權碼在新版本中仍可正常使用

**步驟：**
1. 使用既有授權碼（在舊版本生成的）
2. 在編譯後的新版本中點擊「啟用授權」
3. 貼上授權碼

**預期結果：** ✓ 成功啟用（舊版機器碼匹配）

---

### 測試 2：新授權啟用

**目的：** 確認新版機器碼正確生成並綁定

**步驟：**
1. 使用 `Tools/GenerateLicenseKey.ps1` 生成新授權碼（不指定 MachineCode）
2. 在當前機器上啟用
3. 檢查 `%AppData%\YD\RevitTools\license.dat`

**預期結果：**
- ✓ 授權成功啟用
- ✓ 機器碼格式為 `XXXX-XXXX-XXXX-XXXX`
- ✓ 機器碼基於 CPU ID + 主機板序號 + 機器名稱

---

### 測試 3：換機器測試

**目的：** 確認機器碼綁定有效

**步驟：**
1. 將授權檔複製到另一台電腦
2. 啟動 Revit

**預期結果：** ✗ 失敗，提示「授權文件已綁定到其他電腦」

---

### 測試 4：更新安裝程式啟動

**目的：** 確認 TOCTOU 風險已修復

**步驟：**
1. 執行「檢查更新」
2. 下載並安裝更新

**預期結果：**
- ✓ 無需寫入 .cmd 腳本
- ✓ Revit 關閉後直接啟動安裝程式
- ✓ 安裝程式簽章驗證通過

---

## 生成測試授權碼

```powershell
cd Tools

# 生成專業版授權（不綁定機器碼）
.\GenerateLicenseKey.ps1 -PrivateKeyPath "C:\path\to\private_key.xml" -LicenseType Professional -UserName "TestUser" -Company "TestCorp" -Days 365

# 生成標準版授權（綁定指定機器碼）
.\GenerateLicenseKey.ps1 -PrivateKeyPath "C:\path\to\private_key.xml" -LicenseType Standard -UserName "TestUser" -Company "TestCorp" -Days 365 -MachineCode "XXXX-XXXX-XXXX-XXXX"
```

---

## 常見問題

### Q: 編譯時找不到 RevitAPI.dll

**A:** 確認已安裝對應版本的 Revit，或修改 `.csproj` 中的 `HintPath`。

### Q: 授權驗證失敗「授權文件已綁定到其他電腦」

**A:** 這可能是 backward compatibility 測試失敗。檢查：
1. `GetLegacyMachineCode()` 是否正確生成舊版機器碼
2. `VerifyStoredLicense()` 是否同時比對新/舊版

### Q: `RSA.Create()` 相關方法找不到

**A:** 確認目標框架為 `.NET Framework 4.8` 或更高，`System.Security.Cryptography` 命名空間已引入。

---

## 發布前檢查

- [ ] 所有配置編譯成功
- [ ] 既有授權 backward compatibility 測試通過
- [ ] 新授權啟用測試通過
- [ ] 更新安裝程式啟動測試通過
- [ ] `CHANGELOG.md` 已更新
- [ ] `version.json` 已更新版本號
- [ ] 安裝檔已重新編譯

---

*最後更新：2026-09-07*
