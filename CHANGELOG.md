# 更新日誌

所有重要的專案變更都會記錄在此檔案中。

格式基於 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.0.0/)，
版本號遵循 [語義化版本](https://semver.org/lang/zh-TW/)。

---

## [2.5.11] - 2026-08-31

### 安全性改進

- **機器碼硬體指紋強化**
  - 新版機器碼改用 `CPU ProcessorId | BaseBoard SerialNumber | MachineName` 組合
  - 移除 `UserName` 依賴，避免使用者變更導致授權失效
  - 支援 backward compatibility：既有授權（舊版機器碼）仍可正常啟用

- **更新安裝程式 TOCTOU 風險修復**
  - 移除臨時 `.cmd` 腳本寫入，改用背景執行緒直接啟動安裝程式
  - 避免 `%TEMP%` 目錄的競態條件風險

- **RSA API 升級**
  - `RSACryptoServiceProvider` → `RSA.Create()`（符合 Microsoft 最新建議）
  - 使用 `ImportParameters` + `HashAlgorithmName.SHA256` + `RSASignaturePadding.Pkcs1`

### 注意事項

- 新授權將綁定新版機器碼
- 既有授權不受影響，仍可正常啟用
- 若需將授權轉移至新電腦，請聯繫技術支援重新綁定

---

## [2.5.10] - 2026-08-27

### 修正
- 更新內建套管族群版本為 2026.08.27.01，已載入舊版族群的模型會自動覆蓋更新；同版族群不會每次重複載入。
- Pipe Sleeve 新增止水套管類型相容：下拉清單保留止水套管供手動選用，但「依管徑自動套用大一級」預設規則會優先選一般套管。
- 套管類型清單改為一般套管優先、止水套管其次、開孔類型最後，方便設定時快速選到常用項目。
## [2.5.9] - 2026-08-27

### 修正
- 建築開孔同步支援與套管同為「管附件」類別的開口族，改以族群/類型名稱中的開口、開孔、圓形、矩形等關鍵字辨識，不再被類別限制擋下。
- 建築開孔找不到可用族型時，會在訊息中列出相近族型與類別，方便確認族群命名或形狀關鍵字。
- 建築開孔同步發生例外時會明確復原本次交易，降低錯誤後模型卡住或停在未完成交易狀態的風險。
## [2.5.8] - 2026-08-26

### 新增
- **建築端開孔/預留洞同步（第一階段）**
  - 新增 建築開孔 命令，可在建築模型讀取連結 MEP 模型中的自動套管需求，建立或更新建築端獨立開孔/預留洞切割族。
  - 建築開孔同步不使用 MEP 套管族，預設辨識 套管-開口圓形_無.rfa、套管-開口矩形_無.rfa / 開孔-開口矩形_無.rfa，並相容既有矩形開孔族 開孔-矩形_無.rfa。
  - 開孔族型可依來源套管的 DN、標稱直徑或尺寸參數自動匹配，減少人工建立族型對應表。

### 改進
- 建築開孔同步以連結實例 Id 與來源套管 UniqueId 建立對應鍵，重跑時會更新既有開孔，降低重複建置風險。
- 同步更新使用手冊、圖文手冊、README、授權頁與安裝說明。

---
## [2.5.7] - 2026-08-25

### 修復
- **Auto Join 分割牆梁位判斷**
  - 分割牆現在會優先依結構梁與牆的平面重疊範圍，以及梁底/梁頂高度區間建立牆段。
  - 修正梁位分割只沿牆長方向判斷，導致梁上下牆段未正確切出的情形。
  - 沒有命中梁位時仍保留原本的外側面輪廓分割邏輯，可繼續支援柱、牆等切割來源。

### 文件
- 同步更新版本資訊、自動更新 JSON、安裝說明與安裝檔版本。

## [2.5.6] - 2026-08-20

### 新增
- **MEP 自動套管工具正式納入安裝檔**
  - 支援 Pipe / Duct / 電管 / 電纜線架穿越牆、樓板、結構梁時自動放置圓形套管或矩形開孔。
  - 支援目前模型與連結模型的建築構件判斷，套管長度依對應牆厚、樓板厚或梁寬計算。
  - 安裝檔內嵌預設族群 `套管-圓形_無.rfa` 與 `開孔-矩形_無.rfa`，套管工具優先從本機 `Resources\\Families` 自動載入，矩形開孔供矩形風管與電纜線架使用。
  - 修正穿梁套管中心點：不再投影到梁 LocationCurve，改以管線與梁實體交段中心放置，避免套管跑到梁上緣或偏離管中心。
  - 新增套管樓層基準判斷：依套管中心 Z 值套用不高於該點的最近樓層，並寫入距離樓層高程 offset，避免全部掛在 1FL。

### 改進
- **MEP Ribbon 正式工具精簡**
  - MEP 面板目前僅顯示穩定工具：支管對齊、批次對齊、手動翻彎、Pipe Sleeve。
  - 其他仍在測試階段的 MEP 工具保留程式碼，但暫不顯示於 Ribbon。
- **Pipe Sleeve 幾何與效能優化**
  - 依 DN 預設套用大一級套管類型，並過濾非套管族群選項。
  - 套管位置跟隨建築構件中心；穿牆與穿梁套管方向改用構件立面垂直方向，牆/樓板可排除增築邊界，穿梁套管涵蓋梁增築寬度。
  - 新增重複放置判斷、既有套管更新選項與候選構件預篩，降低大型模型卡頓。
  - 修正部分環境第一次執行未生成、第二次才成功的情形：套管服務層新增預設族群載入保底，2024 設定視窗載入族群後會立即再生，並在候選分析前與每筆套管建立後再生模型再做方向、尺寸與中心校正。
  - 自動編號改由模型既有自動套管最大 `PS-xxx` 續編，避免分次生成或不同系統批次造成編號重覆。
  - 「更新現有套管」會依來源管線重新定位、旋轉並更新套管尺寸/長度，管線路徑調整後可直接重新執行更新。
  - 新增穿梁第一階段檢核：孔徑、梁深 1/3、梁端/柱邊 2H、同梁孔距 3D/300mm 提醒，並寫入備註與穿梁風險等級；不符原則者於目前視圖以紅色覆寫顯示。
  - 套管設定介面新增「穿梁原則示意」範例圖，顯示 2H 禁開區、孔徑、梁深比例與孔距分類，方便執行前確認檢核邏輯。
  - 備註補寫穿牆、穿梁或穿樓板資訊，方便後續查核。
  - 自動生成與套管管理更新改依目前視圖 Section Box / CropBox 限制交點範圍，避免選取長管線時在範圍外生成。
  - 修正豎井/樓板開口區域誤判：原生樓板若無實體交段不再用外框補判，避免洞口內立管誤生成套管；更新現有套管時會清理目前視圖範圍內同來源的舊版殘留套管。
  - 更新管線套管使用手冊與圖文版手冊，補充範圍框操作與套管管理範圍說明。
  - 套管管理介面補強樓層顯示：優先讀取套管樓層文字，無資料時改由 Revit 樓層約束或位置高程判斷；「整理」可重編目前清單套管編號並補寫樓層資訊。
  - 套管建立或更新完成後會立即依管理介面的排序規則整理目前視圖範圍內的自動套管編號，完成通知改顯示整理後範圍總數，避免生成暫時編號與管理清單不一致。

---
## [2.5.5] - 2026-08-13

### Added
- **Auto Join: independent Wall Profile Alignment command**
  - Added a dedicated `Align Wall Profile` command under Join Tools so wall profile cleanup can be run directly without entering the Auto Join / Unjoin workflow.
  - The dedicated command reuses the existing scope and category settings, but presents a focused execution dialog with only the wall alignment action.

### Changed
- **Wall top alignment now follows modeling practice**
  - Wall tops now align to `Floor` / `StructuralFloor` undersides first.
  - Structural framing undersides are used only when no floor underside match is available, reducing cases where local beams pull wall tops away from the main floor boundary.

## [2.5.4] - 2026-07-17

### 修復
- **Revit 2026 房間裝修 Excel 匯出一致性**
  - 修正 Revit 2026 匯出 Excel 與其他版本工作表/欄位不一致的問題。
  - Revit 2026 匯出改為一致的「明細表／統計表／施工明細表」。
  - 報表以模型實際受管理/生成的粉刷元素作為交付數量，不再以估算量混充。

### 改進
- **交付數量驗算**
  - 牆面粉刷可依材料/類型拆分彙總，包含房間裝修補生的柱側粉刷面。
  - 手動面/一般模型裝修面獨立列入報表，方便核對斜面、樓梯間或不規則面。
  - 未找到實際粉刷元素時輸出 0 並標示模型狀態，方便追查漏生成或參數未綁定的項目。

---

## [2.5.3] - 2026-07-07

### 新增
- **釋疑簡報快速產出**
  - 新增獨立「釋疑工具」頁籤/面板，不再放在 Data 工具內。
  - 支援「內部檢查」與「正式釋疑」兩種 PowerPoint 樣板模式。
  - 支援窗選截圖、選圖、清除與依範例版面最佳化圖片比例。
  - 支援同一專案簡報檔追加投影片，避免每次建立新檔。
  - 支援 Q1/Q2/Q3 項次自動預設與待進行/進行中/完成進度管理。

### 改進
- **釋疑項目追蹤**
  - 新增 SQLite 追蹤資料庫，記錄產出的釋疑項目、樣板類型、進度與投影片資訊。
  - 歷史紀錄支援搜尋、進度篩選、內容編輯、Excel 匯出與更新既有 PPT 投影片。
  - 專案歷史紀錄改以模型路徑為優先鍵值隔離，避免不同專案共用相同紀錄。
- **PPT 穩定性**
  - 匯出前檢查 PowerPoint 檔案是否被占用。
  - 更新既有檔案前建立備份，失敗時自動還原。
  - 匯出後執行 OpenXML 驗證，降低開啟 PPT 時需要修復的機率。

### 安裝
- 安裝檔新增 `Microsoft.Data.Sqlite`、`SQLitePCLRaw` 與 native SQLite runtimes，支援釋疑追蹤資料庫於 Revit 2024-2026 正常載入。

---

## [2.5.1] - 2026-06-23

### 修復
- **AR 房間裝修牆高同步**
  - 房間管理介面改為依房間 ID 回讀模型中裝修牆的實際「不連續高度」。
  - 排除高度 500 mm 以下的踢腳板，避免踢腳板高度誤覆蓋牆高。
  - 同一房間有多段裝修牆時，以最大有效高度作為介面牆高。
  - 統一預設值為牆高 3000 mm、天花高度 2700 mm。

### 新增
- **AR 房間裝修驗算報表**
  - 房間管理介面可匯出 Excel 驗算報表，供檢查與驗收交付的明細報表是否正確。
  - 報表包含「驗算報表」與「明細表驗收」兩張工作表：前者列出房間設定與模型差異，後者依牆面、樓板、天花、踢腳板彙整模型實測量、明細表應列量與差異。
  - 可依選取房間或異常列建立 `AR_Check_*` 3D 驗算視圖，作為報表數量核對與交付驗收的視覺依據。
- **房間重疊防呆**
  - 更新房間/裝修元素參數前會檢查房間空間歸屬是否唯一。
  - 若偵測到房間重疊，或元素空間回查同時命中多間房間，系統會略過自動補寫 `AR_RoomId` 與參數回寫，避免誤改交付明細報表使用的房間參數值。
  - 「套用並更新粉刷面」與共用生成器會在寫入參數、刪除舊元素、生成新裝修面前先執行房間重疊檢查；不安全房間會整間略過，避免在防呆前已發生模型變更。

---

## [2.2.0] - 2025-12-05

### 新增
- ✨ **自動更新功能** - 一鍵檢查並安裝最新版本
  - 自動檢查最新版本
  - 自動下載並安裝更新
  - 顯示更新內容和發布日期
  - 支援 HTTPS 加密傳輸
  - 超時保護和錯誤處理

- ✨ **管線避讓工具** - 自動生成管線避讓路徑
  - 支援 Pipe、Duct、Conduit
  - 可自訂彎角（30°-60°）和偏移量
  - 批量處理功能
  - 循環處理模式
  - 自動生成 6 點翻彎路徑

- 📚 **完整文檔**
  - 自動更新功能使用手冊（HTML）
  - 管線避讓工具使用手冊（HTML）
  - 部署自動更新伺服器指南
  - 文檔中心（index.html）

### 改進
- 🔨 優化 COBie 匯出性能
  - 提升大型專案的匯出速度
  - 改進記憶體使用效率

- 🔨 提升授權驗證速度
  - 優化授權檢查邏輯
  - 減少啟動時間

- 🔨 改進 UI 響應性能
  - 優化對話框載入速度
  - 改進按鈕響應時間

- 🔨 優化錯誤處理機制
  - 更詳細的錯誤訊息
  - 改進異常捕獲和處理

### 改進
- **Pipe Sleeve 族群版本偵測**
  - `套管-圓形_無` 會讀取族型參數 `HB_族群版本`，與內建版本 `2026.08.26.01` 比對；版本相同不重載，缺少版本或較舊時自動以安裝檔內建族群覆蓋。

### 修復
- 🐛 修復管線套管放置問題
  - 修正套管中心點計算
  - 修正垂直放置角度
  - 改進交點檢測邏輯

- 🐛 修復連結模型元素識別
  - 修正連結模型中的元素類型判斷
  - 改進座標轉換

- 🐛 修復參數讀取錯誤
  - 修正空間名稱讀取
  - 改進參數值獲取

### 技術
- 📦 新增 NuGet 套件
  - System.Net.Http v4.3.4
  - System.Text.Json v8.0.5

- 🏗️ 架構改進
  - 新增 UpdateService 服務層
  - 新增 CheckUpdateCommand 命令
  - 整合 AboutCommand 更新檢查

---

## [2.1.0] - 2025-11-XX

### 新增
- ✨ **管線套管工具** - 自動為穿牆/穿樓板的管線放置套管
  - 支援連結模型
  - 自動編號系統
  - 距離測量功能
  - 智能套管尺寸選擇

### 改進
- 🔨 優化 COBie 匯出
  - 支援連結模型中的房間資訊
  - 改進空間名稱和代碼讀取

### 改進
- **Pipe Sleeve 族群版本偵測**
  - `套管-圓形_無` 會讀取族型參數 `HB_族群版本`，與內建版本 `2026.08.26.01` 比對；版本相同不重載，缺少版本或較舊時自動以安裝檔內建族群覆蓋。

### 修復
- 🐛 修復圖標顯示問題
- 🐛 修復部署配置錯誤

---

## [2.0.0] - 2025-10-XX

### 新增
- ✨ **整合授權管理系統**
  - 三層授權等級（試用版、標準版、專業版）
  - 集中式授權驗證
  - 授權管理 UI

- ✨ **AR 模板工具**
  - 裝修模板快速建立
  - 參數滑桿視覺化調整

- ✨ **COBie 數據工具**
  - COBie 匯出功能
  - COBie 匯入功能
  - 欄位對應管理

### 改進
- 🔨 統一品牌名稱為 "HB_BIM Tools"
- 🔨 優化 Ribbon 面板佈局
- 🔨 改進圖標設計

### 技術
- 🏗️ 創建 Inno Setup 安裝程式
- 🏗️ 支援 Revit 2024 / 2025 / 2026
- 📦 整合多個獨立工具到單一專案

---

## [1.0.0] - 2025-09-XX

### 新增
- ✨ 初始版本發布
- ✨ 基本工具功能

---

## 版本說明

### 版本號格式：主版本號.次版本號.修訂號

- **主版本號**：重大變更，可能不向下相容
- **次版本號**：新增功能，向下相容
- **修訂號**：錯誤修復，向下相容

### 變更類型

- `新增` - 新功能
- `改進` - 現有功能的改進
- `修復` - 錯誤修復
- `移除` - 移除的功能
- `安全` - 安全性修復
- `技術` - 技術性變更（不影響使用者）

---

**© 2025 HB_BIM Owen. All rights reserved.**

# 2.5.1 Hotfix - 2026-06-29

## 修正
- **AR 房間裝修驗算 / 樓梯空間判讀**
  - 房間重疊防呆改為低、中、高多高度九宮格取樣，可檢出樓梯間、挑空、跨層房間在局部高度才發生的空間歸屬不唯一。
  - 驗算報表建立模型索引時，未標記 `AR_RoomId` 的裝修元素改用多點空間判讀；地坪往樓板上方取樣、天花往下方取樣，避免取樣點落在構件厚度中心。
  - 若裝修元素多點命中不同房間，系統不再任意歸戶，會略過該元素的自動歸屬，讓驗算報表呈現缺量/異常，避免錯誤通過驗收。
  - 手動建立的一般模型/DirectShape 裝修面若具備 AR 裝修標記或裝修名稱，會以「手動裝修面」獨立列入明細表驗收，支援樓梯間斜面、挑空側面等不規則區域核對。
## [2.5.1] - 2026-06-30

### Fixed
- AR Room Finish: added supplemental structural/architectural column side-face generation for cases where Revit room boundary segments do not expose the column face, preventing missing finish zones around columns.
- AR Room Finish output policy: automatic room-finish generation is limited to native Wall/Floor/Ceiling elements; column-side supplemental faces are generated as Wall elements instead of GenericModel/DirectShape.
- GenericModel/DirectShape output is reserved for Face-to-Face/manual irregular-face workflows.
- AR Room Finish UI: opening the room management window no longer auto-applies existing model finish elements back into dropdown settings; users must click "Sync Model" to intentionally overwrite UI settings from the model.
- AR Room Finish validation: acceptance checking now reports AR_RoomId/spatial-room mismatches, multi-room hits, and untagged finish faces as ownership anomalies instead of silently trusting stale parameters.
- AR Room Finish regeneration: added delete preflight; regeneration stops before deleting target finish elements if any are pinned or grouped.
- Face-to-Face: when the selected/generated finish face resolves to multiple rooms, the tool no longer writes AR_RoomId automatically.
- Face-to-Face: generated finish walls now default to non-room-bounding, consistent with Room Finish output, to avoid generated finish faces changing room boundary calculations.
- Room Finish cleanup: old room-owned GenericModel/DirectShape finish faces created by earlier builds are removed during regeneration to prevent duplicate legacy column-side faces after re-running the tool.
## [2.5.2] - 2026-06-30

### Fixed
- AR Finishings: added the first Exterior Wall Finish validation/report command. It calculates net exterior side-face area for exterior walls plus exterior-facing column/beam faces, groups results by level/direction/component/material, exports Excel detail/summary sheets, and creates an AR_Check exterior wall 3D view without generating finish geometry.
- AR Finishings Exterior Wall Finish: changed the core workflow to semi-automatic validation. Users box-select the facade range and click a representative exterior face to define calculation direction, reducing risk from wall flip, type function, and unrelated same-direction walls.
- AR Finishings Exterior Wall Finish: wall exterior quantities now use a temporary thin-solid calculation and subtract adjacent exterior columns/beams before converting volume/thickness to area, reducing discrepancy with Face-to-Face GenericModel checks.
- AR Finishings Face-to-Face: GenericModel finish faces now write schedule quantity parameters (`面積`, `裝修面積`, `長度`, `高度`); GenericModel area is calculated from generated solid volume divided by finish thickness.
- AR Room Finish acceptance safeguards: validation reports AR_RoomId/spatial-room mismatches, multi-room hits, and untagged finish faces.
- AR Room Finish Sync Model: only trusted generated finish elements marked by the tool are read back into room finish settings, preventing residual AR_RoomId on structural walls/floors from loading non-finish types.
- AR Room Finish Clear AR Parameters: generated finish faces are preserved; cleanup only clears non-finish elements with residual AR values.
- AR Room Finish regeneration: delete preflight stops before deleting pinned or grouped target finish elements.
- AR Room Finish column-side supplemental faces are generated as Wall elements, keeping automatic Room Finish output on Wall/Floor/Ceiling only.
- AR Room Finish duplicate guard: column-side supplements are skipped when the same or overlapping finish wall was already created from room boundary segments.
- GenericModel/DirectShape output is reserved for Face-to-Face/manual irregular-face workflows.
- Face-to-Face: ambiguous multi-room generated faces no longer receive AR_RoomId automatically.
- Includes v2.5.1 room-finish column-side face generation, Sync Model opt-in behavior, non-room-bounding finish walls, and validation report improvements.














