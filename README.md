# YD_BIM Tools

**專業的 Revit 工具集 - 提升 BIM 工作效率**

[![Version](https://img.shields.io/badge/version-2.2.0-blue.svg)](https://github.com/QOORST/YD_BIM_Tools/releases)
[![Revit](https://img.shields.io/badge/Revit-2024%20|%202025%20|%202026-orange.svg)](https://www.autodesk.com/products/revit)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## 📋 概述

YD_BIM Tools 是一套專為 Revit 設計的專業工具集，整合了多個實用功能，包括自動更新、管線避讓、套管放置、COBie 匯出等，大幅提升 BIM 工作效率。

---

## ✨ 主要功能

### 🔄 自動更新
- **一鍵更新** - 無需手動下載，自動檢查並安裝最新版本
- **智能提醒** - 開啟時自動檢查更新
- **安全可靠** - HTTPS 加密傳輸

### 🔧 MEP 工具
- **管線避讓** - 自動生成管線避讓路徑，支援 Pipe、Duct、Conduit
- **管線套管** - 自動為穿牆/穿樓板的管線放置套管

### 📊 數據工具
- **COBie 匯出** - 增強版 COBie 匯出，支援連結模型
- **CSV 匯出** - 批量匯出元素參數到 CSV

### 🏗️ AR 模板工具
- **裝修模板** - 快速建立裝修元素
- **參數滑桿** - 視覺化調整族群參數

---

## 📦 安裝

### 系統需求

- **作業系統**: Windows 10/11 (64-bit)
- **Revit 版本**: 2024 / 2025 / 2026
- **.NET Framework**: 4.8 或更高版本
- **磁碟空間**: 至少 50 MB

### 安裝步驟

1. **下載安裝程式**
   - 前往 [Releases](https://github.com/QOORST/YD_BIM_Tools/releases) 頁面
   - 下載最新版本的 `YD_BIM_Tools_vX.X_Setup.exe`

2. **執行安裝**
   - 關閉所有 Revit 實例
   - 執行安裝程式
   - 按照安裝精靈完成安裝

3. **啟動 Revit**
   - 啟動 Revit 2024 / 2025 / 2026
   - 在 Revit 中找到 "YD_BIM Tools" 標籤

4. **授權啟用**
   - 點擊「授權管理」按鈕
   - 輸入授權碼啟用功能

---

## 🚀 快速開始

### 使用自動更新

1. 點擊 **About** 面板中的「**檢查更新**」按鈕
2. 查看最新版本資訊
3. 點擊「是」開始下載
4. 關閉 Revit 後安裝程式會自動啟動

### 使用管線避讓工具

1. 點擊 **MEP** 面板中的「**管線避讓**」按鈕
2. 設定彎角和偏移量
3. 選擇要處理的管線
4. 定義避讓範圍
5. 自動生成避讓路徑

### 使用管線套管工具

1. 點擊 **MEP** 面板中的「**管線套管**」按鈕
2. 選擇要處理的管線
3. 設定套管參數
4. 自動放置套管

---

## 📚 文檔

- [完整使用手冊](https://github.com/QOORST/YD_BIM_Tools/wiki)
- [自動更新功能說明](docs/auto-update.md)
- [管線避讓工具指南](docs/pipe-avoid.md)
- [常見問題 FAQ](docs/FAQ.md)

---

## 🔄 更新日誌

### v2.2.0 (2025-12-05)

#### 新功能
- ✨ 新增自動更新功能
- ✨ 新增管線避讓工具

#### 改進
- 🔨 優化 COBie 匯出性能
- 🔨 提升授權驗證速度
- 🔨 改進 UI 響應性能

#### 修復
- 🐛 修復管線套管放置問題
- 🐛 修復連結模型元素識別

查看 [完整更新日誌](CHANGELOG.md)

---

## 🤝 貢獻

歡迎提交問題報告和功能建議！

1. Fork 本專案
2. 創建您的功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交您的更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 開啟 Pull Request

---

## 📞 技術支援

如有任何問題或需要協助，請聯繫：

- **Email**: qoorst123@yesdir.com.tw
- **網站**: www.ydbim.com
- **Issues**: [GitHub Issues](https://github.com/QOORST/YD_BIM_Tools/issues)

---

## 📄 授權

本專案採用 MIT 授權 - 查看 [LICENSE](LICENSE) 檔案了解詳情。

---

## 🙏 致謝

感謝所有使用和支援 YD_BIM Tools 的使用者！

---

**© 2025 YD_BIM Owen. All rights reserved.**

