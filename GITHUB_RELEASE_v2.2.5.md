# YD BIM Tools v2.2.5

## 🎉 主要更新

### 🐛 修復 GB18030 編碼錯誤
修復部分使用者在使用 COBie 功能時遇到的編碼錯誤問題，改善跨系統相容性。

### ✨ COBie 功能增強
- ✅ **新增 COBie 範本匯出功能** - 可匯出空白範本供廠商填寫
- ✅ **中英文對照欄位** - 所有欄位顯示格式：`01.空間名稱 (Space.Name)`
- ✅ **Excel/CSV 雙格式支援** - 匯入匯出皆支援兩種格式

### 🔧 Revit 2025 支援改善
- ✅ 修復編譯問題
- ✅ 保留所有 COBie 和資料管理功能
- ⚠️ 部分 UI 功能暫不支援（詳見下方說明）

---

## 📥 下載與安裝

### 支援版本
- ✅ Revit 2024（完整功能）
- ✅ Revit 2025（COBie 功能）

### 安裝方式

#### 方法 1：使用安裝程式（推薦）⭐
1. 下載 `YD_BIM_Tools_v2.2.5_Setup.exe` (3.93 MB)
2. 以**系統管理員身分**執行安裝程式
3. 安裝程式會自動偵測已安裝的 Revit 版本
4. 選擇要安裝的 Revit 版本（可多選）
5. 點擊安裝，完成後重新啟動 Revit

**優點**：
- ✅ 自動偵測 Revit 版本
- ✅ 自動部署所有檔案到正確位置
- ✅ 自動建立 .addin 檔案
- ✅ 支援解除安裝

#### 方法 2：手動安裝
1. 下載對應版本的 ZIP 檔案：
   - Revit 2024：`YD_BIM_Tools_v2.2.5_Revit2024.zip` (2.93 MB)
   - Revit 2025：`YD_BIM_Tools_v2.2.5_Revit2025.zip` (2.87 MB)
2. 解壓縮到 Revit 插件資料夾：
   - Revit 2024：`C:\ProgramData\Autodesk\Revit\Addins\2024\YD_BIM\`
   - Revit 2025：`C:\ProgramData\Autodesk\Revit\Addins\2025\YD_BIM\`
3. 確認 `.addin` 檔案存在：
   - `C:\ProgramData\Autodesk\Revit\Addins\[版本]\YD_RevitTools.LicenseManager.addin`
4. 重新啟動 Revit

---

## 📋 功能清單

### ✅ Revit 2024 可用功能（完整版）
- ✅ COBie 欄位管理（中英文對照）
- ✅ COBie 範本匯出
- ✅ COBie 匯出 Excel/CSV
- ✅ COBie 匯入 Excel/CSV
- ✅ 自動接合 (AutoJoin)
- ✅ 管線避讓 (AutoAvoid)
- ✅ 管線套管 (PipeSleeve)
- ✅ 管線轉 ISO (PipeToISO)
- ✅ 族群參數管理 (Family)
- ✅ 授權管理

### ✅ Revit 2025 可用功能（資料管理版）
- ✅ COBie 欄位管理（中英文對照）
- ✅ COBie 範本匯出
- ✅ COBie 匯出 Excel/CSV
- ✅ COBie 匯入 Excel/CSV
- ✅ 授權管理
- ❌ 自動接合 (AutoJoin) - 暫不支援
- ❌ 管線避讓 (AutoAvoid) - 暫不支援
- ❌ 管線套管 (PipeSleeve) - 暫不支援
- ❌ 管線轉 ISO (PipeToISO) - 暫不支援
- ❌ 族群參數管理 (Family) - 暫不支援

> **注意**：Revit 2025 部分功能暫不支援是因為 .NET SDK 與 XAML 編譯器的相容性問題，未來版本將尋找替代方案。

---

## 🔧 技術資訊

- **版本號**：2.2.5.0
- **目標框架**：.NET Framework 4.8
- **主要依賴項**：
  - EPPlus 7.5.2
  - Newtonsoft.Json 13.0.3
  - System.Text.Encoding.CodePages 8.0.0 ⭐ 新增
  - System.Text.Json 8.0.5
- **檔案數量**：18 個 DLL 檔案

---

## 📝 更新日誌

### v2.2.5 (2025-12-10)
- 🐛 修復 GB18030 編碼錯誤
- ✨ 新增 COBie 範本匯出功能
- ✨ COBie 欄位顯示中英文對照
- 🔧 改善 Revit 2025 支援
- 📦 新增部署腳本

---

## 🐛 已知問題

1. **Revit 2025 XAML 功能暫不支援**
   - 原因：.NET SDK 9.0 與 .NET Framework 4.8 的 XAML 編譯器相容性問題
   - 影響：部分需要 UI 視窗的功能暫時無法使用
   - 計劃：未來版本將尋找替代方案

---

## 📞 技術支援

如有問題或建議，請在 [Issues](https://github.com/QOORST/YD_BIM_Tools/issues) 頁面回報。

---

**感謝使用 YD BIM Tools！** 🎉

