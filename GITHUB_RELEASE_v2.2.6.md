# v2.2.6 - 修改 PipeToISO 視圖隱藏方式為永久隱藏

## 🎯 主要更新

### 🔧 PipeToISO 工具改進

**修改視圖隱藏方式為永久隱藏**

之前使用暫時隱藏 (`IsolateElementsTemporary`)，關閉專案後會重置。現在改用永久隱藏 (`HideElements`)，關閉專案後隱藏設定仍然保留。

**改進效果**：
- ✅ ISO 視圖中的元件隱藏設定現在會永久保存
- ✅ 重新開啟專案時，不需要重新設定視圖過濾
- ✅ 視圖更乾淨、更專注於管線系統

---

## 📦 支援版本

- ✅ Autodesk Revit 2024
- ✅ Autodesk Revit 2025

---

## 📥 安裝方式

### ⭐ 方式 1：使用安裝程式（推薦）

1. 下載 **YD_BIM_Tools_v2.2.6_Setup.exe**
2. 執行安裝程式
3. 安裝程式會自動偵測已安裝的 Revit 版本並部署檔案
4. 重新啟動 Revit

**安裝程式特色**：
- ✅ 自動偵測已安裝的 Revit 版本（2024, 2025）
- ✅ 自動部署所有檔案到正確位置
- ✅ 支援多版本同時安裝
- ✅ 支援解除安裝

### 方式 2：手動安裝

1. 下載對應的 ZIP 檔案：
   - Revit 2024: **YD_BIM_Tools_v2.2.6_Revit2024.zip**
   - Revit 2025: **YD_BIM_Tools_v2.2.6_Revit2025.zip**

2. 解壓縮到對應的 Revit Addins 資料夾：
   - Revit 2024: `C:\ProgramData\Autodesk\Revit\Addins\2024\YD_BIM\`
   - Revit 2025: `C:\ProgramData\Autodesk\Revit\Addins\2025\YD_BIM\`

3. 複製 `.addin` 檔案到 Addins 資料夾：
   - Revit 2024: `C:\ProgramData\Autodesk\Revit\Addins\2024\`
   - Revit 2025: `C:\ProgramData\Autodesk\Revit\Addins\2025\`

4. 重新啟動 Revit

---

## 🔧 技術資訊

- **.NET Framework**: 4.8
- **EPPlus**: 7.5.2
- **System.Text.Encoding.CodePages**: 8.0.0
- **Newtonsoft.Json**: 13.0.3

---

## 📝 完整變更歷史

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

如有問題或建議，請在 GitHub 上提交 Issue。

**GitHub Repository**: https://github.com/QOORST/YD_BIM_Tools

