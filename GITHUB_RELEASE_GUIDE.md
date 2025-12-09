# GitHub Release 發布指南 - v2.2.4

本指南將協助您在 GitHub 上發布 v2.2.4 版本。

---

## 📋 準備工作

### 1. 確認檔案已準備好

- ✅ 安裝程式：`Output\YD_BIM_Tools_v2.2.4_Setup.exe`
- ✅ Release Notes：`RELEASE_NOTES_v2.2.4.md`
- ✅ MD5 校驗：`BD0AD4E80EC58155C9004B93488BC8E6`

### 2. 確認程式碼已提交

在發布 Release 之前，請確保所有程式碼變更都已提交到 Git：

```bash
# 查看變更狀態
git status

# 如果有未提交的變更，請執行：
git add .
git commit -m "Release v2.2.4 - 修復 Revit 2025/2026 COBie 參數建立問題"
git push origin main
```

---

## 🚀 在 GitHub 上建立 Release

### 步驟 1：前往 Releases 頁面

1. 開啟您的 GitHub 專案頁面
2. 點擊右側的 **"Releases"** 連結
3. 點擊 **"Draft a new release"** 按鈕

### 步驟 2：填寫 Release 資訊

#### **Tag version（標籤版本）**
```
v2.2.4
```

#### **Target（目標分支）**
- 選擇 `main` 或您的主要分支

#### **Release title（發布標題）**
```
YD BIM Tools v2.2.4 - 修復 Revit 2025/2026 COBie 參數建立問題
```

#### **Description（描述）**

複製以下內容（或使用 `RELEASE_NOTES_v2.2.4.md` 的內容）：

```markdown
## 🎯 重點更新

本版本主要修復了 **Revit 2025/2026** 中 COBie 參數建立失敗的問題，並優化了 API 相容性處理。

---

## 🐛 Bug 修復

### 1. 修復 Revit 2025/2026 中 COBie 參數建立失敗
- **問題：** 在 Revit 2025/2026 中，使用「COBie 欄位管理器」建立共用參數時，所有參數都失敗
- **原因：** Revit 2025 完全移除了 `BuiltInParameterGroup` 類型
- **修復：** 完全使用反射處理 API 相容性，支援 Revit 2022-2026

### 2. 修復 COBie 匯入資料寫入錯誤參數
- 確保資料正確寫入共用參數，而非內建參數

---

## 🔧 技術改進

- 增強 `ParamTypeCompat.InsertBinding` 方法的跨版本相容性
- 優先使用新版 API（`GroupTypeId.Data`），自動回退到舊版 API
- 清理診斷訊息，減少日誌輸出

---

## 📦 安裝資訊

**檔案名稱：** `YD_BIM_Tools_v2.2.4_Setup.exe`  
**檔案大小：** 2.88 MB (2,883,340 bytes)  
**MD5 校驗：** `BD0AD4E80EC58155C9004B93488BC8E6`  
**支援版本：** Revit 2024, 2025, 2026

### 安裝步驟
1. 下載 `YD_BIM_Tools_v2.2.4_Setup.exe`
2. 關閉所有 Revit 實例
3. 執行安裝程式
4. 選擇要安裝的 Revit 版本
5. 重新啟動 Revit

---

## 🧪 測試建議

### 重點測試：Revit 2025/2026 中的 COBie 參數建立
1. 開啟「資料」→「COBie 欄位管理器」
2. 勾選需要的欄位並點擊「📥 載入共用參數」
3. 驗證所有參數成功建立，無錯誤訊息

---

## 📞 支援

如遇問題請在 [Issues](https://github.com/your-repo/issues) 提交問題報告。
```

### 步驟 3：上傳安裝程式

1. 在 **"Attach binaries"** 區域，點擊選擇檔案
2. 上傳 `Output\YD_BIM_Tools_v2.2.4_Setup.exe`
3. 等待上傳完成

### 步驟 4：發布選項

- ✅ 勾選 **"Set as the latest release"**（設為最新版本）
- ❌ 不要勾選 **"Set as a pre-release"**（這不是預覽版）

### 步驟 5：發布

點擊 **"Publish release"** 按鈕完成發布！

---

## 📝 發布後的工作

### 1. 更新 README.md

在 README.md 中更新版本資訊：

```markdown
## 最新版本

**v2.2.4** (2025-12-08)
- 修復 Revit 2025/2026 中 COBie 參數建立失敗問題
- 優化 API 相容性處理

[下載最新版本](https://github.com/your-repo/releases/latest)
```

### 2. 更新 CHANGELOG.md

在 CHANGELOG.md 頂部添加：

```markdown
## [2.2.4] - 2025-12-08

### Fixed
- 修復 Revit 2025/2026 中 COBie 參數建立失敗（BuiltInParameterGroup 已移除）
- 修復 COBie 匯入資料寫入錯誤參數問題

### Changed
- 優化 ParamTypeCompat.InsertBinding 方法的 API 相容性
- 清理診斷訊息輸出
```

### 3. 通知使用者

如果您有使用者郵件列表或社群，可以發送更新通知。

---

## ✅ 檢查清單

發布前請確認：

- [ ] 所有程式碼變更已提交並推送到 GitHub
- [ ] 安裝程式已編譯並測試通過
- [ ] Release Notes 已準備好
- [ ] MD5 校驗碼已記錄
- [ ] 在 GitHub 上建立了 v2.2.4 標籤
- [ ] 上傳了安裝程式檔案
- [ ] 發布了 Release
- [ ] 更新了 README.md
- [ ] 更新了 CHANGELOG.md

---

## 🎉 完成！

恭喜！v2.2.4 已成功發布到 GitHub！

使用者現在可以從 Releases 頁面下載最新版本。

