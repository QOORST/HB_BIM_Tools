# YD BIM Tools v2.2.6 Release Notes

**發布日期**: 2025-12-15  
**版本號**: 2.2.6.0

---

## 🎯 主要更新

### 🔧 PipeToISO 工具改進

**修改視圖隱藏方式為永久隱藏**

- **問題**：之前使用暫時隱藏 (`IsolateElementsTemporary`)，關閉專案後會重置
- **改進**：改用永久隱藏 (`HideElements`)，關閉專案後隱藏設定仍然保留
- **影響**：
  - ISO 視圖中的元件隱藏設定現在會永久保存
  - 重新開啟專案時，不需要重新設定視圖過濾
  - 視圖更乾淨、更專注於管線系統

**技術實作**：
```csharp
// 舊方式（暫時隱藏）
view.IsolateElementsTemporary(elementIds);

// 新方式（永久隱藏）
// 1. 收集視圖中所有元件
// 2. 找出需要隱藏的元件（不在管線系統中的元件）
// 3. 永久隱藏不需要的元件
view.HideElements(elementsToHide);
```

---

## 📦 支援版本

- ✅ Autodesk Revit 2024
- ✅ Autodesk Revit 2025

---

## 🔧 技術資訊

- **.NET Framework**: 4.8
- **EPPlus**: 7.5.2
- **System.Text.Encoding.CodePages**: 8.0.0
- **Newtonsoft.Json**: 13.0.3

---

## 📥 安裝方式

### 方式 1：使用安裝程式（推薦）

1. 下載 `YD_BIM_Tools_v2.2.6_Setup.exe`
2. 執行安裝程式
3. 安裝程式會自動偵測已安裝的 Revit 版本並部署檔案
4. 重新啟動 Revit

### 方式 2：手動安裝

1. 下載對應的 ZIP 檔案：
   - Revit 2024: `YD_BIM_Tools_v2.2.6_Revit2024.zip`
   - Revit 2025: `YD_BIM_Tools_v2.2.6_Revit2025.zip`

2. 解壓縮到對應的 Revit Addins 資料夾：
   - Revit 2024: `C:\ProgramData\Autodesk\Revit\Addins\2024\YD_BIM\`
   - Revit 2025: `C:\ProgramData\Autodesk\Revit\Addins\2025\YD_BIM\`

3. 複製 `.addin` 檔案到 Addins 資料夾：
   - Revit 2024: `C:\ProgramData\Autodesk\Revit\Addins\2024\`
   - Revit 2025: `C:\ProgramData\Autodesk\Revit\Addins\2025\`

4. 重新啟動 Revit

---

## 🐛 已知問題

無

---

## 📝 變更歷史

### v2.2.6 (2025-12-15)
- 🔧 修改 PipeToISO 視圖隱藏方式為永久隱藏

### v2.2.5 (2025-12-15)
- 🐛 修復 GB18030 編碼錯誤
- ✨ 新增 COBie 範本匯出功能
- ✨ COBie 欄位顯示中英文對照

### v2.2.4
- ✨ 新增 COBie 功能
- 🔧 改善跨版本相容性

---

## 📞 支援

如有問題或建議，請聯繫開發團隊或在 GitHub 上提交 Issue。

**GitHub Repository**: https://github.com/QOORST/YD_BIM_Tools

