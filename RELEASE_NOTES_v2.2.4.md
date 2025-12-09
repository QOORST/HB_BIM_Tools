# YD BIM Tools v2.2.4 Release Notes

**發布日期：** 2025-12-08  
**支援版本：** Revit 2024, 2025, 2026

---

## 🎯 重點更新

本版本主要修復了 **Revit 2025/2026** 中 COBie 參數建立失敗的問題，並優化了 API 相容性處理。

---

## 🐛 Bug 修復

### 1. **修復 Revit 2025/2026 中 COBie 參數建立失敗**
- **問題：** 在 Revit 2025/2026 中，使用「COBie 欄位管理器」建立共用參數時，所有參數都失敗並顯示錯誤：
  ```
  Could not load type 'Autodesk.Revit.DB.BuiltInParameterGroup' from assembly 'RevitAPI, Version=25.3.0.0'
  ```
- **原因：** Revit 2024 開始棄用 `BuiltInParameterGroup`，Revit 2025 完全移除了此類型
- **修復：** 
  - 完全使用反射處理舊版 API，避免編譯時期的類型載入錯誤
  - 優先使用新版 API（`GroupTypeId.Data`）
  - 自動回退到舊版 API（Revit 2022/2023）
  - 增強方法簽章檢測邏輯，同時支援 `Binding` 和 `ElementBinding` 參數類型

### 2. **修復 COBie 匯入資料寫入錯誤參數**
- **問題：** COBie 匯入時，資料可能被錯誤寫入內建參數（如「模型」、「製造商」）而非共用參數
- **修復：** 確保資料正確寫入共用參數（如 `COBie_TypeName`、`COBie_Manufacturer`）

---

## 🔧 技術改進

### API 相容性優化
- **檔案：** `Helpers/Data/ParamTypeCompat.cs`
- **改進內容：**
  - 增強 `InsertBinding` 方法的跨版本相容性
  - 使用反射動態載入 `BuiltInParameterGroup` 類型（Revit 2022/2023）
  - 使用反射動態取得 `GroupTypeId.Data`（Revit 2024+）
  - 自動檢測並使用正確的 API 版本
  - 清理診斷訊息，減少日誌輸出

### 支援的 Revit 版本
- ✅ Revit 2024（使用新版 API）
- ✅ Revit 2025（使用新版 API）
- ✅ Revit 2026（使用新版 API）
- ✅ Revit 2022/2023（使用舊版 API，向下相容）

---

## 📦 安裝資訊

**檔案名稱：** `YD_BIM_Tools_v2.2.4_Setup.exe`  
**檔案大小：** 2.88 MB (2,883,340 bytes)  
**MD5 校驗：** `BD0AD4E80EC58155C9004B93488BC8E6`

### 安裝步驟
1. 下載 `YD_BIM_Tools_v2.2.4_Setup.exe`
2. 關閉所有 Revit 實例
3. 執行安裝程式
4. 選擇要安裝的 Revit 版本（2024/2025/2026）
5. 完成安裝後重新啟動 Revit

---

## 🧪 測試建議

### 重點測試項目

#### 1. **COBie 參數建立測試**（Revit 2025/2026 重點測試）
1. 開啟 Revit 2025 或 2026
2. 開啟任意專案
3. 點擊「資料」→「COBie 欄位管理器」
4. 勾選需要的欄位（如 03-15）
5. 根據需求勾選「實體參數」欄位
6. 點擊「📥 載入共用參數」
7. **驗證：** 所有參數應該成功建立，不應出現 "Method not found" 或 "Could not load type" 錯誤

#### 2. **COBie 匯入測試**
1. 建立共用參數後，準備 CSV 檔案
2. 點擊「資料」→「COBie 匯入」
3. 選擇 CSV 檔案
4. **驗證：** 資料應該寫入共用參數，而非內建參數

#### 3. **跨版本相容性測試**
- 在 Revit 2024、2025、2026 中分別測試上述功能
- 確認所有版本都能正常運作

---

## 📚 相關文檔

- [COBie 欄位管理器使用說明](README.md#cobie-欄位管理器)
- [COBie 匯入/匯出功能](README.md#cobie-匯入匯出)
- [安裝指南](README.md#安裝)

---

## 🙏 致謝

感謝所有測試人員的回饋，幫助我們發現並修復了這些問題。

---

## 📞 支援

如果您遇到任何問題，請：
1. 查看 [常見問題](README.md#常見問題)
2. 在 [GitHub Issues](https://github.com/your-repo/issues) 提交問題報告
3. 提供詳細的錯誤訊息和 Revit 版本資訊

---

**完整變更記錄：** [CHANGELOG.md](CHANGELOG.md)

