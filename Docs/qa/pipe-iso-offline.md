# 管路 ISO 離線驗證與集中部署

## 已完成的離線流程

Revit 擷取一次 → `_Review.json` → 共用排版核心 → SVG 預覽／碰撞與資料驗證 → 集中建置 → 一次 Revit 驗收。

JSON 保存 mm 單位的投影線段、元件編號、標籤文字及字框尺寸。建立 Revit 詳圖時先量測 TextNote，再呼叫與離線相同的排版程式；移動後再次核對實際字框。輸出的 JSON 可重播該次排版，無須開啟 Revit。JSON 不是原始 RVT 或完整施工模型，不能用來替代 Connector／交易／列印等 API 驗收。

### 執行

需要現有 .NET 10 SDK。第一次尚未還原測試專案時先執行 `dotnet restore`；之後不需網路。

```powershell
# 只跑內建回歸案例
& 'C:\HB\_BIM\HB_BIM_Tools\Tests\PipeToISO.Tests\Run-Offline.ps1'

# 已匯出的完整投影資料：可包含配件及實測字框
& 'C:\HB\_BIM\HB_BIM_Tools\Tests\PipeToISO.Tests\Run-Offline.ps1' `
  -ReviewJson 'C:\Users\藍\Desktop\TEST\ISO-FP_消防水管 1-20260928_Review.json'

# 舊版只有逐管表時：僅重播直管，字框採估計，不猜管件位置
& 'C:\HB\_BIM\HB_BIM_Tools\Tests\PipeToISO.Tests\Run-Offline.ps1' `
  -PipesCsv 'C:\Users\藍\Desktop\TEST\ISO-FP_消防水管 1-20260928_Pipes.csv'
```

輸出預設為工作區 `artifacts/pipeiso-offline-時間/`，含 SVG、可重播 JSON 與 `report.json`。腳本不更新 DLL、不操作 Revit、不改原始 CSV。曲管不能只用兩端點代替實際曲線，須使用 `_Review.json`。

## 本批修正

- 文字框同時避開其他文字、模型線與既有引線；引線穿越模型及其他引線另列計數。
- 搜尋完整方環，支援密集並行管；管標籤可接實際管線端點。
- 過密或交叉過多時改為 N001 等短索引，完整 ElementId 與尺寸保存在對照表。索引只適用本次圖面，不是永久材料代碼。
- 對照表每欄最多 24 筆，自動分欄，避免細長圖面。
- Revit 圖框包含模型、字框與說明，四周增加紙面 12 mm 留白。
- CSV／JSON／SVG 先寫完暫存檔再換檔；中途失敗保留舊成果，檔案占用會明確報錯。
- 輸出 `_Review.svg` 作離線預覽。JSON 的尺寸為 Revit 實測值或明列的估計值，SVG 字型外觀仍可能與 Revit 不同。

## 2026-09-28 驗證證據

- 最新使用者檔案：20:10 的 PNG、Audit、BOM、Pipes。確認原詳圖存在管線穿字、邊界缺乏留白；不是把「成功訊息」當作排版通過。
- 實際 Pipes 8 段，端點距離與模型長度在 0.003 mm 的 CSV 座標捨入容差內一致；總長 29542 mm，包含 1 根垂直管，未知加工切長保持空白。
- 共 199 項離線斷言通過；9 個執行案例為三通、迴路、斷開、24 管密集、64 管密集、垂直、投影重合、實際直管、JSON 重播。此輪 JSON 重播使用合成迴路，不冒充完整實際 15 件模型。
- 9 個案例的文字碰撞與引線交叉候選均為 0。這是本組案例的結果，不保證所有模型都無交叉。
- 已檢視實際直管與密集案例的 SVG 轉 PNG，確認文字可見、尺寸保留、多欄索引無裁切。sharp 渲染有字型快取目錄警告，仍成功輸出並完成人工視覺檢查；Revit 字型外觀待最後驗收。
- 最終結果：`artifacts/pipeiso-offline-delivery/report.json`；Release2024 x64 建置另記於主 QA 日誌。

## 剩餘驗收／能力邊界

1. 下次方便使用 Revit 時一次生成，檢查字框一致性、圖框、PNG 與完整 15 件的 `_Review.json`。這輪不要求使用者反覆開關程式。
2. 各系統模型與 Connector 讀取仍須代表性實模確認；純離線測試無法替代 Revit 原生 API。
3. 此批完善模型核對圖、算量及離線排版；正式加工切長、逐端接法／原廠扣長規則、施工尺寸鏈及 PCF 尚未完成，不宣稱可直接下料。
4. 這批沒有改動材料規格或自動套用消防接法，也沒有覆寫使用者 Desktop/TEST 既有成果。
