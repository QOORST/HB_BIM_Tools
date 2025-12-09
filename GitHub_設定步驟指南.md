# GitHub Releases 設定步驟指南

**目標：** 設定 GitHub Releases 作為 YD_BIM Tools 的自動更新伺服器

**預計時間：** 15-20 分鐘

---

## ✅ 檢查清單

在開始之前，請確認您已準備好：

- [ ] GitHub 帳號（如沒有，請先註冊：https://github.com/signup）
- [ ] 安裝程式檔案：`Output\YD_BIM_Tools_v2.2_Setup.exe`（2.44 MB）
- [ ] 準備好的文檔檔案（已在 `Docs/` 目錄中）

---

## 📝 步驟 1：創建 GitHub Repository

### 1.1 登入 GitHub

1. 訪問：https://github.com
2. 使用您的帳號登入

### 1.2 創建新 Repository

1. 點擊右上角的 **"+"** → **"New repository"**

2. 填寫 Repository 資訊：

   ```
   Repository name: YD_BIM_Tools
   
   Description: YD_BIM Tools - Professional Revit Plugin Suite with Auto-Update
   
   Visibility: 
   ● Public（推薦 - 使用者可直接下載）
   ○ Private（需要 GitHub Pro，使用者需要授權才能下載）
   
   Initialize this repository with:
   ☑ Add a README file
   ☐ Add .gitignore
   ☑ Choose a license: MIT License
   ```

3. 點擊 **"Create repository"**

### 1.3 記錄 Repository URL

創建完成後，記錄您的 Repository URL：
```
https://github.com//YD_BIM_Tools
```

**範例：** `https://github.com/OwenYD/YD_BIM_Tools`

---

## 📝 步驟 2：上傳文檔檔案到 Repository

### 2.1 上傳 README.md

1. 在 Repository 頁面，點擊 **"Add file"** → **"Upload files"**

2. 上傳以下檔案（從 `Docs/` 目錄）：
   - `README_GitHub.md`（上傳後重新命名為 `README.md`）
   - `CHANGELOG.md`
   - `version.json`

3. 在 "Commit changes" 區域：
   ```
   Commit message: Add documentation files
   ```

4. 點擊 **"Commit changes"**

### 2.2 編輯 README.md

1. 點擊 `README.md` 檔案

2. 點擊右上角的 **鉛筆圖標**（Edit this file）

3. 將所有 `YourUsername` 替換為您的實際 GitHub 使用者名稱

4. 點擊 **"Commit changes"**

---

## 📝 步驟 3：發布第一個 Release

### 3.1 進入 Releases 頁面

1. 在 Repository 頁面右側，點擊 **"Releases"**（或 **"0 releases"**）

2. 點擊 **"Create a new release"**

### 3.2 填寫 Release 資訊

1. **Choose a tag**
   - 輸入：`v2.2.0`
   - 點擊 **"Create new tag: v2.2.0 on publish"**

2. **Release title**
   ```
   YD_BIM Tools v2.2.0
   ```

3. **Describe this release**
   
   複製以下內容（或使用 `Docs/GitHub_Release_v2.2.0.md` 中的完整版本）：

   ```markdown
   ## 🎉 YD_BIM Tools v2.2.0
   
   專業的 Revit 工具集，整合多個實用功能，提升 BIM 工作效率。
   
   ### ✨ 新功能
   
   #### 🔄 自動更新功能
   - 一鍵更新 - 無需手動下載
   - 智能提醒 - 自動檢查更新
   - 詳細資訊 - 顯示版本號、更新內容
   
   #### 🔧 管線避讓工具
   - 自動路徑規劃 - 生成 6 點翻彎路徑
   - 參數可調 - 自訂彎角和偏移量
   - 批量處理 - 支援多條管線
   
   ### 🔨 改進
   - 優化 COBie 匯出性能
   - 提升授權驗證速度
   - 改進 UI 響應性能
   
   ### 🐛 修復
   - 修復管線套管放置問題
   - 修復連結模型元素識別
   
   ### 📦 安裝說明
   
   1. 下載 `YD_BIM_Tools_v2.2_Setup.exe`
   2. 關閉所有 Revit 實例
   3. 執行安裝程式
   4. 啟動 Revit 2024/2025/2026
   
   ### 📊 檔案資訊
   
   - 檔案大小：2.44 MB
   - MD5：DD0D10593204477615F20DBDCEFB002E
   - 支援版本：Revit 2024/2025/2026
   ```

### 3.3 上傳安裝程式

1. 在 **"Attach binaries"** 區域

2. 拖曳或點擊選擇檔案：
   ```
   Output\YD_BIM_Tools_v2.2_Setup.exe
   ```

3. 等待上傳完成（約 10-30 秒，視網速而定）

### 3.4 發布 Release

1. 確認所有資訊正確

2. 點擊 **"Publish release"**

3. 等待發布完成

---

## 📝 步驟 4：獲取下載連結

### 4.1 查看 Release 頁面

發布完成後，您會看到 Release 頁面。

### 4.2 獲取安裝程式下載連結

1. 在 **Assets** 區域，找到 `YD_BIM_Tools_v2.2_Setup.exe`

2. **右鍵點擊** 檔案名稱 → **"複製連結位址"**

3. 下載連結格式應該是：
   ```
   https://github.com/您的使用者名稱/YD_BIM_Tools/releases/download/v2.2.0/YD_BIM_Tools_v2.2_Setup.exe
   ```

4. **記錄此連結**（稍後會用到）

---

## 📝 步驟 5：更新 version.json

### 5.1 編輯 version.json

1. 在 Repository 頁面，點擊 `version.json` 檔案

2. 點擊右上角的 **鉛筆圖標**（Edit this file）

3. 更新 `downloadUrl` 為步驟 4.2 獲取的連結：

   ```json
   {
     "version": "2.2.0",
     "downloadUrl": "https://github.com/您的使用者名稱/YD_BIM_Tools/releases/download/v2.2.0/YD_BIM_Tools_v2.2_Setup.exe",
     "releaseNotes": "新功能：\n• 新增自動更新功能\n• 新增管線避讓工具\n\n改進：\n• 優化性能\n• 修復已知問題",
     "releaseDate": "2025-12-05T00:00:00Z",
     "isCritical": false,
     "minimumVersion": "2.0.0"
   }
   ```

4. 點擊 **"Commit changes"**

### 5.2 獲取 version.json 的 Raw URL

1. 點擊 `version.json` 檔案

2. 點擊右上角的 **"Raw"** 按鈕

3. 複製瀏覽器網址列的 URL，格式應該是：
   ```
   https://raw.githubusercontent.com/您的使用者名稱/YD_BIM_Tools/main/version.json
   ```

4. **記錄此 URL**（這是最重要的 URL！）

---

## 📝 步驟 6：更新程式碼中的 URL

### 6.1 記錄您的 URL

請將以下資訊填寫完整：

```
GitHub 使用者名稱：QOORST

Repository URL：
https://github.com/QOORST/YD_BIM_Tools

version.json Raw URL：
https://raw.githubusercontent.com/QOORST/YD_BIM_Tools/refs/heads/main/version.json

安裝程式下載 URL：
https://github.com/QOORST/YD_BIM_Tools/releases/download/v2.2.0/YD_BIM_Tools_v2.2_Setup.exe
```

### 6.2 準備修改代碼

**請告訴我您的 GitHub 使用者名稱**，我會幫您：
1. 修改 `UpdateService.cs` 中的 URL
2. 重新編譯專案
3. 重新打包安裝程式

---

## ✅ 完成檢查

完成以上步驟後，請確認：

- [ ] GitHub Repository 已創建
- [ ] README.md 已上傳並編輯
- [ ] CHANGELOG.md 已上傳
- [ ] version.json 已上傳並更新
- [ ] Release v2.2.0 已發布
- [ ] 安裝程式已上傳到 Release
- [ ] 已獲取 version.json 的 Raw URL
- [ ] 已獲取安裝程式下載 URL

---

## 🎉 下一步

完成以上步驟後，請：

1. **提供您的 GitHub 使用者名稱**
2. 我會幫您修改代碼並重新編譯
3. 測試自動更新功能

---

**需要協助？** 請隨時告訴我您遇到的問題！

