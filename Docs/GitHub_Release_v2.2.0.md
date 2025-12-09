# GitHub Release v2.2.0 發布資訊

## Release 資訊

**Tag version:** `v2.2.0`  
**Release title:** `YD_BIM Tools v2.2.0`  
**Target:** `main` branch

---

## Release Description（發布說明）

複製以下內容到 GitHub Release 的描述欄位：

```markdown
## 🎉 YD_BIM Tools v2.2.0

專業的 Revit 工具集，整合多個實用功能，提升 BIM 工作效率。

---

### ✨ 新功能

#### 🔄 自動更新功能
- **一鍵更新** - 無需手動下載，點擊「檢查更新」即可自動安裝最新版本
- **智能提醒** - 開啟「關於 YD BIM」時自動檢查更新
- **詳細資訊** - 顯示版本號、更新內容、發布日期
- **安全可靠** - HTTPS 加密傳輸，超時保護

#### 🔧 管線避讓工具
- **自動路徑規劃** - 自動生成 6 點翻彎避讓路徑
- **參數可調** - 自訂彎角（30°-60°）和偏移量
- **批量處理** - 支援一次處理多條管線
- **循環模式** - 完成一組後可繼續處理下一組
- **支援類型** - Pipe、Duct、Conduit

---

### 🔨 改進

- ✅ 優化 COBie 匯出性能
- ✅ 提升授權驗證速度
- ✅ 改進 UI 響應性能
- ✅ 優化錯誤處理機制
- ✅ 更新文檔和使用手冊

---

### 🐛 修復

- ✅ 修復管線套管放置問題
- ✅ 修復連結模型元素識別
- ✅ 修復參數讀取錯誤

---

### 📦 安裝說明

1. **下載安裝程式**
   - 點擊下方的 `YD_BIM_Tools_v2.2_Setup.exe` 下載

2. **執行安裝**
   - 關閉所有 Revit 實例
   - 執行安裝程式
   - 按照安裝精靈完成安裝

3. **啟動 Revit**
   - 支援 Revit 2024 / 2025 / 2026
   - 在 Revit 中找到 "YD_BIM Tools" 標籤

4. **授權啟用**
   - 點擊「授權管理」按鈕
   - 輸入授權碼啟用功能

---

### 📚 文檔

- [完整使用手冊](https://github.com/YourUsername/YD_BIM_Tools)
- [自動更新功能說明](https://github.com/YourUsername/YD_BIM_Tools)
- [管線避讓工具指南](https://github.com/YourUsername/YD_BIM_Tools)

---

### 🔧 系統需求

- **作業系統**: Windows 10/11 (64-bit)
- **Revit 版本**: 2024 / 2025 / 2026
- **.NET Framework**: 4.8 或更高版本
- **磁碟空間**: 至少 50 MB

---

### 📊 檔案資訊

```
檔案名稱：YD_BIM_Tools_v2.2_Setup.exe
檔案大小：2.44 MB (2,563,379 bytes)
MD5 校驗：DD0D10593204477615F20DBDCEFB002E
發布日期：2025-12-05
```

---

### 📞 技術支援

- **Email**: qoorst123@yesdir.com.tw
- **網站**: www.ydbim.com

---

### 📝 更新日誌

查看 [CHANGELOG.md](https://github.com/YourUsername/YD_BIM_Tools/blob/main/CHANGELOG.md) 了解完整的版本歷史。

---

**© 2025 YD_BIM Owen. All rights reserved.**
```

---

## 附件檔案

上傳以下檔案到 Release：

1. **YD_BIM_Tools_v2.2_Setup.exe**
   - 位置：`Output\YD_BIM_Tools_v2.2_Setup.exe`
   - 大小：2.44 MB
   - MD5：DD0D10593204477615F20DBDCEFB002E

---

## 發布後的步驟

1. 獲取安裝程式下載連結
2. 創建 version.json
3. 上傳 version.json 到 Repository
4. 獲取 version.json 的 Raw URL
5. 更新 UpdateService.cs

---

**準備完成！現在可以在 GitHub 上發布 Release。**

