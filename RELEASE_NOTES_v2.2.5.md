# YD BIM Tools v2.2.5 Release Notes

## 🎉 主要更新

### 🐛 修復 GB18030 編碼錯誤
- **問題**：部分使用者在使用 COBie 功能時遇到 `'GB18030' is not a supported encoding name` 錯誤
- **解決方案**：
  - 新增 `System.Text.Encoding.CodePages` 套件支援
  - 在程式啟動時自動註冊編碼提供者
  - 改善跨系統和跨語言環境的相容性
- **影響**：所有使用 COBie Excel 匯入/匯出功能的使用者

### ✨ COBie 功能增強

#### 1. 新增 COBie 範本匯出功能
- 可匯出空白的 COBie Excel 範本供廠商填寫
- 支援 Excel (.xlsx) 和 CSV (.csv) 兩種格式
- Excel 範本特色：
  - 藍色標題列，包含中英文對照欄位名稱
  - 灰色說明列，顯示欄位類型、類別、是否必填
  - 自動凍結前兩列，方便查看
  - 自動調整欄寬

#### 2. COBie 欄位中英文對照
- 所有 COBie 欄位現在顯示中英文對照
- 格式：`編號.中文名稱 (英文名稱)`
- 範例：
  - `01.空間名稱 (Space.Name)`
  - `03.系統名稱 (System.Name)`
  - `11.製造廠商 (Component.Manufacturer)`
- 方便中英文雙語環境使用

#### 3. Excel 和 CSV 格式支援
- COBie 匯出：支援 Excel (.xlsx) 和 CSV (.csv)
- COBie 匯入：支援 Excel (.xlsx) 和 CSV (.csv)
- 自動偵測檔案格式

### 🔧 Revit 2025/2026 支援改善
- 修復 Revit 2025/2026 編譯問題
- 排除 XAML 相關功能（因 .NET SDK 相容性問題）
- 保留所有 COBie 和資料管理功能
- **注意**：以下功能在 Revit 2025/2026 暫時不可用：
  - 自動接合 (AutoJoin)
  - 管線避讓 (AutoAvoid)
  - 管線套管 (PipeSleeve)
  - 管線轉 ISO (PipeToISO)
  - 族群參數管理 (Family)

### 📦 部署改善
- 新增部署腳本：
  - `Deploy_Revit2024.bat` - Revit 2024 部署腳本
  - `Deploy_Revit2025.ps1` - Revit 2025 部署腳本
- 總共 18 個 DLL 檔案（新增編碼套件）

---

## 📋 完整功能清單

### ✅ Revit 2024 可用功能
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

### ✅ Revit 2025/2026 可用功能
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

---

## 🔧 技術資訊

- **版本號**：2.2.5.0
- **支援 Revit 版本**：2024, 2025
- **目標框架**：.NET Framework 4.8
- **主要依賴項**：
  - EPPlus 7.5.2
  - Newtonsoft.Json 13.0.3
  - System.Text.Encoding.CodePages 8.0.0 ⭐ 新增
  - System.Text.Json 8.0.5

---

## 📥 安裝說明

### 方法 1：使用安裝程式（推薦）
1. 下載 `YD_BIM_Tools_v2.2.5_Setup.exe`
2. 執行安裝程式
3. 重新啟動 Revit

### 方法 2：手動安裝
1. 下載對應版本的 ZIP 檔案
2. 解壓縮到 Revit 插件資料夾：
   - Revit 2024：`C:\ProgramData\Autodesk\Revit\Addins\2024\YD_BIM\`
   - Revit 2025：`C:\ProgramData\Autodesk\Revit\Addins\2025\YD_BIM\`
3. 重新啟動 Revit

---

## 🐛 已知問題

1. **Revit 2025/2026 XAML 功能暫不支援**
   - 原因：.NET SDK 9.0 與 .NET Framework 4.8 的 XAML 編譯器相容性問題
   - 影響：部分需要 UI 視窗的功能暫時無法使用
   - 計劃：未來版本將尋找替代方案

---

## 📞 技術支援

如有問題或建議，請聯繫：
- GitHub Issues: https://github.com/QOORST/YD_BIM_Tools/issues
- Email: [您的聯絡信箱]

---

## 📝 更新日誌

### v2.2.5 (2025-12-10)
- 修復 GB18030 編碼錯誤
- 新增 COBie 範本匯出功能
- COBie 欄位顯示中英文對照
- 改善 Revit 2025/2026 支援
- 新增部署腳本

### v2.2.4 (2025-12-09)
- 修復 Revit 2025/2026 參數建立錯誤
- 新增 Excel 匯入/匯出支援
- 改善授權管理功能

---

**感謝使用 YD BIM Tools！** 🎉

