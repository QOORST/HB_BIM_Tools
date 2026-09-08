═══════════════════════════════════════════════════════════════
  HB_BIM Tools v2.5.13 - Revit 外掛
═══════════════════════════════════════════════════════════════

感謝您選擇 HB_BIM Tools！

v2.5.13：更新套管族群、自動標註排版與設定遷移，並強化安裝檔簽章憑證比對。

本外掛為 Autodesk Revit 提供了一套完整的 BIM 工具集，
包含 AR 工具、釋疑簡報工具、資料管理工具和 COBie 匯入匯出功能。

───────────────────────────────────────────────────────────────
  系統需求
───────────────────────────────────────────────────────────────

• Autodesk Revit 2022 / 2024 / 2025 / 2026（任一版本）
• Windows 10/11 (64-bit)
• .NET Framework 4.8 或更高版本
• 管理員權限（用於安裝）

───────────────────────────────────────────────────────────────
  功能模組
───────────────────────────────────────────────────────────────

【關於面板】
  • 授權管理 - 管理軟體授權
  • 關於 HB_BIM Tools - 查看版本資訊

【AR 面板】
  ▸ 模板工具組（5 個工具）
    • 模板生成 - 自動生成模板
    • 刪除模板 - 批次刪除模板
    • 面選模板 - 從面選擇生成模板
    • 匯出CSV - 匯出模板資料
    • 結構分析 - 結構分析工具
    
  ▸ 裝修工具組（3 個工具）
    • 面生面 - 從面生成裝修面
    • 更換裝修面顏色 - 批次更換裝修面顏色
    • 房間裝修 - 自動生成房間裝修（Revit 2022/2024/2025/2026）
      支援匯出 Excel 交付報表與驗算資料；Revit 2026 匯出格式已與其他版本一致，
      並以模型實際粉刷元素數量作為交付數量來源
    
  ▸ 接合工具組
    • 自動接合 - 自動接合或解除接合牆、樓板、柱、梁等模型元素
    • 對齊牆輪廓 - 牆頂通常貼附樓板下；梁邊 0.5 cm 內且梁底較低時改貼梁下，避免薄縫或短凸出
      詳細操作請參考：Docs\\對齊牆輪廓使用手冊.md、Docs\\對齊牆輪廓圖文使用手冊.html
    • 分割樓板 - 依選取梁位分割樓板
    • 分割牆 - 依柱、梁或其他牆分割牆段

【MEP 工具】
  • 支管對齊 - 將支管端點中心對齊到幹管中心線，可依設定建立 Tee / Takeoff
  • 批次對齊 - 先選幹管，再框選或複選多支支管批次對齊
  • 手動翻彎 - 支援 Pipe / Duct 上下翻彎，可設定角度、偏移高度與中段長度
  • Pipe Sleeve - 依管線/風管穿越牆、樓板、梁自動放置套管或矩形開孔
    支援當前模型與連結模型，依建築構件厚度/中心定位套管，穿牆/穿梁方向垂直於構件立面，並避免重複放置。若目前 3D 視圖有啟用範圍框/Section Box，僅會在目前視圖範圍內生成或更新套管。
    圓形管線與電管使用套管-圓形_無，矩形風管與電纜線架使用開孔-矩形_無；安裝檔已附帶預設族群，會優先從本機 Resources\\Families 自動載入。建築模型端可使用「建築開孔」同步連結 MEP 自動套管，需使用獨立切割族群：套管-開口圓形_無、套管-開口矩形_無 / 開孔-開口矩形_無；開口族可為管附件類別，類型可依 DN/標稱直徑自動匹配。
    詳細操作請參考：Docs\\管線套管使用手冊.md

【資料面板】
  • COBie 欄位設定 - 設定自訂匯出欄位與參數對照
  • COBie 樣板 - 匯出 COBie 欄位樣板與說明
  • 自訂 COBie - 依自訂欄位設定匯出資料
  • 標準 COBie - 依標準工作表與檢核規則匯出資料
  • COBie匯入 - 匯入 COBie 資料

【釋疑工具】
  • 釋疑簡報 - 快速產出內部檢查或正式釋疑用 PowerPoint
    支援窗選截圖、同專案簡報追加投影片、歷史紀錄追蹤、
    進度狀態管理、Excel 匯出，以及更新既有投影片。
    追蹤資料儲存於：
    %AppData%\YD_RevitTools\ClarificationDeck\clarification_records.sqlite

───────────────────────────────────────────────────────────────
  安裝說明
───────────────────────────────────────────────────────────────


1. 關閉所有 Revit 應用程式
2. 執行安裝程式 HB_BIM_Tools_v2.5.12_Setup.exe
3. 安裝程式會自動偵測您電腦上已安裝的 Revit 版本
4. 選擇您要安裝外掛的 Revit 版本（可多選）
   • Revit 2022
   • Revit 2024
   • Revit 2025
   • Revit 2026
5. 依照安裝精靈的指示完成安裝
6. 安裝程式會一併安裝 Pipe Sleeve 預設族群：
   • Resources\Families\套管-圓形_無.rfa
   • Resources\Families\開孔-矩形_無.rfa
   其他電腦安裝後不需先手動準備套管族群。
7. 重新啟動 Revit
8. 在 Ribbon 介面中找到「HB_BIM」標籤
9. 釋疑簡報功能位於獨立的「釋疑工具」頁籤/面板

注意：只有已安裝的 Revit 版本才會顯示在選項中。

───────────────────────────────────────────────────────────────
  解除安裝
───────────────────────────────────────────────────────────────

方法 1: 使用 Windows 控制台
  • 開啟「控制台」→「程式和功能」
  • 找到「HB_BIM Tools」
  • 點擊「解除安裝」

方法 2: 使用開始選單
  • 開啟「開始選單」
  • 找到「HB_BIM Tools」資料夾
  • 點擊「解除安裝 HB_BIM Tools」

───────────────────────────────────────────────────────────────
  安裝位置
───────────────────────────────────────────────────────────────

程式檔案（依您選擇的版本）:
  C:\ProgramData\Autodesk\Revit\Addins\2022\HB_BIM\
  C:\ProgramData\Autodesk\Revit\Addins\2024\HB_BIM\
  C:\ProgramData\Autodesk\Revit\Addins\2025\HB_BIM\
  C:\ProgramData\Autodesk\Revit\Addins\2026\HB_BIM\

配置檔案（依您選擇的版本）:
  C:\ProgramData\Autodesk\Revit\Addins\2022\HB_BIM_Tools.addin
  C:\ProgramData\Autodesk\Revit\Addins\2024\HB_BIM_Tools.addin
  C:\ProgramData\Autodesk\Revit\Addins\2025\HB_BIM_Tools.addin
  C:\ProgramData\Autodesk\Revit\Addins\2026\HB_BIM_Tools.addin

Pipe Sleeve 預設族群（依您選擇的版本）:
  C:\ProgramData\Autodesk\Revit\Addins\2022\HB_BIM\Resources\Families\
  C:\ProgramData\Autodesk\Revit\Addins\2024\HB_BIM\Resources\Families\
  C:\ProgramData\Autodesk\Revit\Addins\2025\HB_BIM\Resources\Families\
  C:\ProgramData\Autodesk\Revit\Addins\2026\HB_BIM\Resources\Families\

  包含：套管-圓形_無.rfa、開孔-矩形_無.rfa。建築端開孔同步若需自動載入，請另放入：套管-開口圓形_無.rfa、套管-開口矩形_無.rfa / 開孔-開口矩形_無.rfa


───────────────────────────────────────────────────────────────
  常見問題
───────────────────────────────────────────────────────────────

Q: 安裝後在 Revit 中看不到 HB_BIM 標籤？
A: 請確認：
   1. Revit 已完全重新啟動
   2. 檢查 Revit 的「外部工具」→「外掛管理員」
   3. 確認沒有錯誤訊息

Q: 圖示沒有顯示？
A: 這通常是因為：
   1. 圖示檔案未正確安裝
   2. 請嘗試重新安裝

Q: 出現授權錯誤？
A: 請聯繫技術支援取得授權

───────────────────────────────────────────────────────────────
  技術支援
───────────────────────────────────────────────────────────────

開發團隊: LAN
網站: www.ydbim.com
技術支援: qoorst123@yesdir.com.tw

───────────────────────────────────────────────────────────────
 版本歷史
───────────────────────────────────────────────────────────────

v2.5.12 (2026-09-07)
  - 更新原生 SQLite 相依套件，排除已知高風險弱點
  - 解除安裝時同步移除 HB_BIM Tools 專用 LAN 自簽憑證
  - AI 助理維持本地端／選配功能，本版不調整 AI 服務部署

v2.5.11 (2026-08-31)
  - 強化授權機器碼指紋，並保留既有授權相容性
  - 更新安裝流程移除暫存命令檔，降低更新啟動風險
  - 統一正式發佈流程，安裝包排除開發用腳本與規劃文件

v2.5.10 (2026-08-27)
  - 修正建築端「建築開孔」族型偵測，支援與套管同為管附件類別的開口族
  - 開口族以名稱中的「開口/開孔」與「圓形/矩形」判斷，不再被族群類別限制擋下
  - 找不到開口族時增加相近族型診斷，並在錯誤時復原本次交易，降低模型卡住風險

v2.5.8 (2026-08-26)
  - Pipe Sleeve 新增建築端「建築開孔」同步，讀取連結 MEP 自動套管並建立/更新獨立切割開孔族
  - 開孔族型可依 DN/標稱直徑自動匹配，預設族群名稱為 套管-開口圓形_無、套管-開口矩形_無

v2.5.7 (2026-08-25)
  - 修正 Auto Join「分割牆」對梁位的判斷
  - 結構梁切割牆時，會依梁與牆的平面重疊範圍及梁底/梁頂高度建立上下牆段
  - 未命中梁位時仍保留原本外側面輪廓分割，可繼續支援柱、梁、牆作為切割來源
  - 同步更新 Revit 2022 / 2024 / 2025 / 2026 版本資訊與安裝檔說明

v2.5.6 (2026-08-20)
  - MEP 工具面板保留穩定工具：支管對齊、批次對齊、手動翻彎、Pipe Sleeve
  - Pipe Sleeve 支援 Pipe / Duct / 電管 / 電纜線架穿牆、穿樓板、穿梁自動建立或更新套管
  - 支援目前模型與連結模型的建築構件厚度、中心與穿越方向判斷
  - 安裝檔內嵌套管-圓形_無.rfa 與開孔-矩形_無.rfa，其他電腦安裝後不需先準備族群
  - 修正穿梁套管中心點，避免偏到梁定位線或梁上緣
  - 自動套管生成與管理更新支援目前視圖 Section Box / CropBox 範圍限制
  - 修正豎井/樓板開口誤判與同來源殘留套管清理，管理清單預設跟隨目前視圖範圍
  - 同步更新管線套管使用手冊、圖文手冊、安裝說明、手動部署指南與部署包 README
v2.5.5 (2026-08-13)
  - 新增「對齊牆輪廓」獨立按鈕，可直接執行牆輪廓整理，不必進入自動接合/解除接合流程
  - 對齊牆輪廓專用視窗只保留「執行對齊」與「取消」，操作更直覺
  - 牆頂對齊改為優先貼附樓板/結構樓板下，若沒有樓板命中才貼附梁下

v2.5.4 (2026-07-17)
  - 修正 Revit 2026 房間裝修 Excel 匯出與其他版本工作表/欄位不一致
  - Revit 2026 匯出改為「明細表／統計表／施工明細表」三張工作表
  - Revit 2026 施工明細表使用模型實際粉刷元素數量，不以估算量混充
  - 支援牆面依材料類型拆分彙總，並納入柱側粉刷面與手動/一般模型裝修面
  - 未找到實際粉刷面時報表顯示 0 與模型狀態，方便交付驗算與追查

v2.5.3 (2026-07-07)
  - 新增獨立「釋疑工具」頁籤/面板與「釋疑簡報」功能
  - 支援內部檢查與正式釋疑兩種簡報樣板
  - 支援窗選截圖、選圖、清除與依範例比例最佳化圖片尺寸
  - 支援同一專案簡報檔追加投影片，以及從歷史紀錄更新既有投影片
  - 新增 Q1/Q2/Q3 項次自動預設與待進行/進行中/完成進度管理
  - 新增 SQLite 追蹤資料庫、專案隔離歷史紀錄、搜尋篩選與 Excel 匯出
  - 匯出 PPT 前後加入 OpenXML 驗證、檔案鎖定檢查與備份還原，降低 PowerPoint 修復風險
  - 安裝檔同步納入 Microsoft.Data.Sqlite、SQLitePCLRaw 與 native SQLite runtimes

v2.5.1 (2026-06-23)
  - 修正房間裝修管理介面牆高與模型實際裝修牆高度不一致
  - 同步模型時依房間 ID 回讀裝修牆「不連續高度」
  - 排除 500 mm 以下踢腳板，避免低牆高度覆蓋裝修牆高
  - 同一房間存在多段裝修牆時，以最大有效高度顯示
  - 統一預設值為牆高 3000 mm、天花高度 2700 mm
  - 新增房間裝修驗算報表：匯出「驗算報表／明細表驗收」Excel
  - 可建立 AR_Check 3D 視圖，作為交付明細報表數量核對與驗收依據
  - 新增房間重疊防呆：若元素空間回查同時命中多間房間，會略過 AR_RoomId 補寫與參數回寫，避免誤改交付報表使用的房間參數
  - 套用並更新粉刷面前會先檢查房間重疊；不安全房間不寫參數、不刪舊元素、不生成新裝修面

v2.5.0 (2026-06-18)
  - 強化 Data 工具 COBie 標準欄位檢核、欄位說明與樣板輸出
  - 優化模型資料管理與批次命名操作流程
  - 調整 AR 裝修、標註與 MEP 檢查/配管工具的穩定性
  - 更新安裝檔版本資訊與安裝說明文字

v2.4.6 (2026-06-09)
  - 修正 AR 標註工具柱線/網格標註與柱標註功能分工
  - 修正標註建立數量顯示與重複標註判斷
  - 修正安裝授權頁版本資訊

v2.4.4 (2026-05-27)
  - Refreshed all Ribbon tool icons.
  - Added distinct icons for AR finishing, split, and schedule export tools.
  - Updated installer package resources.

v2.4.3 (2026-05-26)
  - 更新安裝檔版本與版本檢查資訊
  - 修正 version.json 格式
  - 統一 Revit addin 註冊資訊

v2.4.2 (2026-05-25)
  • 授權金鑰改用 HMAC-SHA256 簽名，防止偽造
  • 金鑰生成器支援自訂天數（1-3650天）
  • 授權記錄新增複製金鑰按鈕
  • 修正到期日顏色顯示與專業版圖示錯誤

v2.4.1 (2026-03-26)
  • 修正結構分析中牆與牆接合處模板分割
  • 修正結構分析中樓板與牆接合處模板分割
  • 修正結構分析中梁側模與牆接合處模板分割
  • 補強牆/梁/柱與樓板交界的接觸扣除穩定性

v2.4.0 (2026-03-06)
  • 新增 Revit 2022 支援
  • 整合「房間裝修」功能（僅 Revit 2022/2024）
  • 優化房間參數填入邏輯：自動提取類型名稱中 "-" 之前的代號
    範例：類型名稱 "B1-不頭" → 房間參數填入 "B1"
  • 移除舊的「裝修生成」按鈕，改用「房間裝修」工具
  • 新增「面生面」和「更換裝修面顏色」工具
  • 更新安裝檔支援 Revit 2022/2024/2025/2026

v2.3.3 (2026-01-12)
  • 修正裝修工具在 Revit 2025/2026 的編譯問題
  • 使用條件編譯處理不同版本的 XAML UI 相容性
  • Revit 2024 及更早版本：保持完整 XAML UI 功能
  • Revit 2025/2026：顯示友好提示訊息（UI 功能開發中）
  • 確保所有版本都能正常編譯和運行

v2.3.2 (2026-01-08)
  • 修正裝修生成工具無法啟動的問題
  • 修正 CmdFinishings 命名空間不一致問題
  • 確保所有 AR 工具的命名空間與資料夾結構一致

v2.3.1 (2026-01-08)
  • 修正已選取面的紅色閃爍不可見問題（延長至 500ms）
  • 修正生成模板無法顯示材料顏色問題
  • 新增 SetFormworkMaterialAndColorForView 方法
  • 優化閃爍效果：綠色閃爍後正確恢復材料顏色
  • 改進視覺反饋：完全不透明閃爍，更清晰可見

v2.2.9 (2024-12-25)
  • 重新架構自動接合介面，簡化操作流程
  • 實現基於物件優先順序的邏輯（柱>梁>版>牆）
  • 新增簡化的單選按鈕介面（柱/梁/版/牆）
  • 自動判斷接合方向，無需手動選擇規則
  • 更新「接合到選取」功能使用新的優先順序邏輯

v2.2.8 (2024-12-24)
  • 新增「柱切牆」接合規則（Rule_Wall_Column_ColumnCuts）
  • 新增「梁切樓板」接合規則（Rule_Floor_Beam_BeamCuts）
  • 自動接合工具現支援 5 個內建接合規則
  • 更新 UI 介面，新增彩色勾選框（粉紅色、紫色）
  • 更新授權協議版本至 2.2.8

v2.2 (2024-11-13)
  • 更新關於對話框文字內容
  • 更新技術支援聯絡資訊
  • 清理舊版本外掛檔案

v2.1 (2024-11-13)
  • 整合 AR_AutoJoin 專案（接合工具組）
  • 修復圖示載入問題
  • 修復部署路徑問題

v2.0 (2024-11-13)
  • 整合多個獨立外掛
  • 實現統一的 Ribbon 介面
  • 新增圖示系統

───────────────────────────────────────────────────────────────
  授權資訊
───────────────────────────────────────────────────────────────

© 2024-2026 LAN. All rights reserved.

本軟體受版權法保護。未經授權，不得複製、修改或散佈本軟體。

───────────────────────────────────────────────────────────────

感謝您使用 HB_BIM Tools！
v2.5.1 Hotfix (2026-06-29)
  - AR 房間裝修驗算補強樓梯空間判讀：房間重疊檢查改為低/中/高多高度九宮格取樣，可辨識樓梯間、挑空、跨層房間的局部重疊風險
  - 未標記 AR_RoomId 的裝修元素改用多點空間判讀；地坪往樓板上方取樣、天花往下方取樣，避免中心點落在構件厚度內造成無法判讀
  - 若多點命中不同房間，系統會略過自動歸戶，讓驗算報表呈現異常供人工確認，避免錯誤通過驗收
  - 手動建立的一般模型/DirectShape 裝修面可獨立列入「手動裝修面」驗收項，用於樓梯間斜面、挑空側面等不規則區域核對
HB_BIM Tools v2.5.2 hotfix - 2026-06-30

- Exterior Wall Finish validation/report command: calculates net exterior side-face area for exterior walls plus exterior-facing column/beam faces, groups by level/direction/component/material, exports Excel detail/summary sheets, and creates an AR_Check exterior wall 3D view without generating finish geometry.
- Exterior Wall Finish validation now uses a semi-automatic workflow: box-select the facade range, then click a representative exterior face to define the calculation direction.
- Exterior Wall Finish wall quantities use a temporary thin-solid calculation and subtract adjacent exterior columns/beams before converting volume/thickness to area, reducing discrepancy with Face-to-Face GenericModel checks.
- Face-to-Face GenericModel finish faces write area/finish-area/length/height schedule parameters; GenericModel area is calculated as generated solid volume divided by finish thickness.
- AR Room Finish: supplements missing column-side finish faces when room boundaries do not expose structural/architectural column faces.
- Sync Model only reads trusted generated finish elements marked by the tool, preventing residual AR_RoomId on structural walls/floors from being loaded as finish types.
- Clear AR Parameters preserves generated finish faces and only clears non-finish elements with residual AR values.
- Room Finish automatic output is limited to native Wall/Floor/Ceiling elements; column-side supplements are generated as Wall elements, not GenericModel/DirectShape.
- Added duplicate guards so column-side supplements are skipped when an equivalent finish wall was already generated from room boundary segments.
- GenericModel/DirectShape output is reserved for Face-to-Face/manual irregular-face workflows.
- Room Finish UI: opening the window no longer overwrites dropdown settings from existing model finish elements; click Sync Model when that overwrite is intended.
- Validation: reports AR_RoomId/spatial room mismatches, multi-room hits, and untagged finish faces as ownership anomalies.
- Regeneration: delete preflight stops before deleting pinned or grouped finish elements.
- Face-to-Face: ambiguous multi-room faces are generated but AR_RoomId is not written automatically.
- Face-to-Face: generated finish walls default to non-room-bounding, matching Room Finish behavior.
- Regeneration cleanup still removes old room-owned GenericModel/DirectShape finish faces created by earlier builds to avoid duplicate legacy column-side faces.











