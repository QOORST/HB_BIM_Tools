# HB_BIM Tools

**專業的 Revit 工具集 - 提升 BIM 工作效率**

[![Version](https://img.shields.io/badge/version-2.5.11-blue.svg)](https://github.com/QOORST/HB_BIM_Tools/releases)
[![Revit](https://img.shields.io/badge/Revit-2022%20|%202024%20|%202025%20|%202026-orange.svg)](https://www.autodesk.com/products/revit)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](../LICENSE.txt)

---

## 📋 概述

HB_BIM Tools 是一套專為 Revit 設計的專業工具集，整合了多個實用功能，包括自動更新、AR 裝修檢查、釋疑簡報快速產出、COBie 匯出等，大幅提升 BIM 工作效率。

---

## ✨ 主要功能

### 🔄 自動更新
- **一鍵更新** - 無需手動下載，自動檢查並安裝最新版本
- **智能提醒** - 開啟時自動檢查更新
- **安全可靠** - HTTPS 加密傳輸

### 🔧 MEP 工具
- **支管/批次對齊** - 管中心對齊與多支管批次對齊
- **Pipe Sleeve 自動套管** - 自動為 Pipe/Duct/電管/電纜線架穿牆、穿樓板、穿梁放置套管或矩形開孔，支援連結模型與內嵌預設族群

### 📊 數據工具
- **COBie 匯出** - 增強版 COBie 匯出，支援連結模型
- **CSV 匯出** - 批量匯出元素參數到 CSV

### 📝 釋疑工具
- **釋疑簡報** - 依內部檢查或正式釋疑樣板快速產出 PowerPoint
- **窗選圖片** - 直接框選畫面導入，並依範例版面最佳化圖片比例
- **項目追蹤** - 以 SQLite 管理 Q1/Q2 項次、進度、歷史紀錄與 Excel 匯出
- **同檔追加/更新** - 同一專案可追加投影片，也可從歷史紀錄更新既有投影片

### 🏗️ AR 模板工具
- **房間裝修** - 依房間設定建立裝修元素，並同步模型實際裝修牆高
- **房間裝修驗算** - 匯出「驗算報表／明細表驗收」Excel，並建立 `AR_Check_*` 3D 視圖，協助檢查交付明細報表數量是否正確
- **面生面** - 從選取面快速建立裝修元素
- **參數滑桿** - 視覺化調整族群參數

---

## 📦 安裝

### 系統需求

- **作業系統**: Windows 10/11 (64-bit)
- **Revit 版本**: 2022 / 2024 / 2025 / 2026
- **.NET Framework**: 4.8 或更高版本
- **磁碟空間**: 至少 50 MB

### 安裝步驟

1. **下載安裝程式**
   - 前往 [Releases](https://github.com/QOORST/HB_BIM_Tools/releases) 頁面
   - 下載最新版本的 `HB_BIM_Tools_v2.5.11_Setup.exe`

2. **執行安裝**
   - 關閉所有 Revit 實例
   - 執行安裝程式
   - 按照安裝精靈完成安裝

3. **啟動 Revit**
   - 啟動 Revit 2022 / 2024 / 2025 / 2026
   - 在 Revit 中找到 "HB_BIM Tools" 標籤

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

- [完整使用手冊](https://github.com/QOORST/HB_BIM_Tools/wiki)
- [對齊牆輪廓使用手冊](對齊牆輪廓使用手冊.md)
- [對齊牆輪廓圖文使用手冊](對齊牆輪廓圖文使用手冊.html)
- [安裝與診斷指南](手動部署指南.md)
- [部署方案總結](部署方案總結.md)
- [授權管理使用說明](授權管理使用說明.md)
- [管線套管使用手冊](管線套管使用手冊.md)

---

## 🔄 更新日誌

### v2.5.11 (2026-08-31)

#### 改進
- 強化授權機器碼指紋，並保留既有授權相容性。
- 安裝器更新流程移除暫存命令檔，降低更新啟動風險。
- 發佈流程統一為正式安裝檔，並排除安裝包中的開發用資源。

#### 整理
- 清理舊備份程式碼、舊封存文件與手動部署腳本。
- 安裝 payload 準備流程改由版本清單集中管理。

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
- **Issues**: [GitHub Issues](https://github.com/QOORST/HB_BIM_Tools/issues)

---

## 📄 授權

本專案採用 MIT 授權 - 查看 [LICENSE.txt](../LICENSE.txt) 檔案了解詳情。

---

## 🙏 致謝

感謝所有使用和支援 HB_BIM Tools 的使用者！

---

**© 2025 LAN. All rights reserved.**

## v2.5.10 - 2026-08-27

- 建築開孔同步支援與套管同為「管附件」類別的開口族，改以族群/類型名稱中的開口、開孔、圓形、矩形等關鍵字辨識。
- 找不到開口族時增加相近族型診斷，並在例外時復原本次交易，降低錯誤後模型停在未完成交易狀態的風險。

## v2.5.8 - 2026-08-26

- Pipe Sleeve 新增建築模型端「建築開孔」第一階段同步：可在建築模型讀取連結 MEP 模型中自動生成的套管需求，建立或更新獨立的開孔/預留洞切割族。
- 建築端開孔族群不混用 MEP 套管族；預設辨識 套管-開口圓形_無、套管-開口矩形_無 / 開孔-開口矩形_無，並可依 DN、標稱直徑或尺寸參數自動匹配族型。
- 同步規則使用連結實例與來源套管 UniqueId 對應，降低重複建置風險，並同步更新使用手冊與安裝說明。
## v2.5.7 - 2026-08-25
- 修正 Auto Join「分割牆」對結構梁位置的判斷。
- 分割牆會依梁與牆的平面重疊範圍及梁底/梁頂高度建立上下牆段。
- 未命中梁位時仍保留原本外側面輪廓分割邏輯。
- 同步更新 Revit 2022 / 2024 / 2025 / 2026 版本資訊與安裝檔說明。
## v2.5.6 - 2026-08-20
- MEP 工具面板保留穩定工具：支管對齊、批次對齊、手動翻彎、Pipe Sleeve。
- Pipe Sleeve 支援穿牆、穿樓板、穿梁自動套管，含連結模型厚度/中心判斷。
- 安裝檔內嵌 `套管-圓形_無.rfa` 與 `開孔-矩形_無.rfa`，其他電腦安裝後不需先準備族群。
- 修正穿梁套管中心點，改以管線與梁實體交段中心放置，避免偏到梁定位線。
- 修正豎井/樓板開口誤判與同來源殘留套管清理，管理清單預設跟隨目前視圖範圍。
- 更新安裝說明、手動部署說明與部署包 README。
## v2.5.5 - 2026-08-13

- Added an independent Wall Profile Alignment command under Join Tools.
- The dedicated alignment dialog now focuses on the wall alignment action instead of showing Auto Join and Unjoin actions.
- Wall tops now align to floor/structural floor undersides first, and only fall back to structural framing undersides when no floor match is available.

## v2.5.4 - 2026-07-17

- Revit 2026 Room Finish Excel export now matches the other versions with Detail, Summary, and Practical Schedule sheets.
- Revit 2026 Room Finish reports read actual managed/generated finish elements as deliverable quantities instead of mixing estimated values.
- Wall finish quantities can be split and summarized by material/type, including generated column-side finish faces.
- Manual/GenericModel finish faces are listed separately for verification.
- Missing actual finish elements are exported as 0 with model status text, making report gaps visible during delivery checking.

## v2.5.3 - 2026-07-07

- AR Exterior Wall Finish validation now includes exterior-facing wall, column, and beam faces, and the Excel report lists component category for delivery quantity checking.
- AR Exterior Wall Finish validation now uses a semi-automatic workflow: box-select the facade range, then click a representative exterior face to define the calculation direction.
- AR Exterior Wall Finish wall quantities now use a temporary thin-solid calculation and subtract adjacent exterior columns/beams before converting volume/thickness to area, reducing discrepancy with Face-to-Face GenericModel checks.
- AR Face-to-Face GenericModel finish faces now write area/finish-area/length/height schedule parameters; GenericModel area is calculated as generated solid volume divided by finish thickness.
- Added independent Clarification Tools ribbon panel and Clarification Deck command.
- Added internal review and formal clarification PowerPoint template modes.
- Added window-captured image import, image selection/clear actions, and reference-template image fitting.
- Added same-project PPT append flow and history-based slide update.
- Added Q1/Q2/Q3 default item numbering plus pending/in-progress/done progress tracking.
- Added project-isolated SQLite tracking records, history search/filtering, record editing, and Excel export.
- Added PPT file-lock checks, backup/restore, and OpenXML validation to reduce PowerPoint repair prompts.
- Installer now packages Microsoft.Data.Sqlite, SQLitePCLRaw, and native SQLite runtimes required by clarification tracking.

## v2.5.2 hotfix - 2026-06-30

- Exterior Wall Finish validation/report command: calculates net exterior side-face area for exterior walls plus exterior-facing column/beam faces, groups by level/direction/component/material, exports Excel detail/summary sheets, and creates an AR_Check exterior wall 3D view without generating finish geometry.
- AR Room Finish now supplements missing structural/architectural column side finish faces when Revit room boundary segments do not expose those faces.
- Sync Model only reads trusted generated finish elements marked by the tool, preventing residual AR_RoomId on structural walls/floors from being loaded as finish types.
- Clear AR Parameters preserves generated finish faces and only clears non-finish elements with residual AR values.
- Room Finish automatic output is limited to native Wall/Floor/Ceiling elements; column-side supplements are generated as Wall elements, not GenericModel/DirectShape.
- Added duplicate guards so column-side supplements are skipped when an equivalent finish wall was already generated from room boundary segments.
- GenericModel/DirectShape output is reserved for Face-to-Face/manual irregular-face workflows.
- Opening Room Finish no longer overwrites dropdown settings from existing model finish elements; click Sync Model when that overwrite is intended.
- Validation reports AR_RoomId/spatial room mismatches, multi-room hits, and untagged finish faces as ownership anomalies.
- Regeneration performs a delete preflight and stops before deleting pinned or grouped finish elements.
- Face-to-Face leaves AR_RoomId blank when the generated face resolves to multiple rooms.
- Face-to-Face generated finish walls default to non-room-bounding, matching Room Finish behavior.
- Regeneration cleanup still removes old room-owned GenericModel/DirectShape finish faces created by earlier builds, preventing duplicate legacy column-side faces.






