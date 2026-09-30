# CAD 變更比對：第一階段原型

獨立 AutoCAD 2024 / .NET Framework 4.8 外掛，不載入 Revit、不修改來源 DWG。這是幾何比對原型，尚未完成實際 DWG 驗收或部署。

## 執行

1. `dotnet build Tools/CadRevision/CadRevision.csproj -c Release`
2. 在 AutoCAD 2024 使用 `NETLOAD` 選擇 `Tools/CadRevision/bin/Release/net48/HB.CadRevision.dll`，依公司既有信任路徑規範載入；不關閉安全設定。
3. 執行 `HBCADCOMPARE`，選兩份 DWG。圖層以分號分隔，留白表示全部。
4. 確認相同原點、方向、比例；各圖依 INSUNITS 轉換成 mm。單位未設定即停止，不猜測單位。
5. 選擇報告資料夾，在新增的獨立子資料夾開啟 `report.html`。點選左側變更項目可定位右侧疊圖；`changes.csv` 可供試算表檢視。

## 範圍與限制

- 只讀模型空間的 LINE 與輕量聚合線直線段；只接受 XY 零高程（0.01 mm 容差）。高程、弧段、文字、填充及其他物件明列略過計數。圖塊與已解析外部參考遞迴展開，套用巢狀座標轉換及 0 圖層繼承。
- 統計單位為線段，不是原始 CAD 物件。聚合線拆段及線段分割方式改變可能造成假差異。
- 未變更先配對，支援端點反向與幾何容差。疑似位移須同圖層、線段向量近似，且在候選範圍內雙向唯一。無法唯一配對時不強行當成移動。
- 長度／角度修改目前呈現新增與刪除，不宣稱辨識所有修改類別。
- 使用依圖層及線段中點分格的空間索引；最多 2,500 萬次候選檢查。展開上限 20 萬線段，超過會停止而非截斷；尚未提供背景取消。
- 對齊目前是人工確認，不會自動偵測或校正整圖平移、旋轉；整圖位移可能呈現多筆疑似位移。
- HTML 完全本機，不載入外部服務；來源名稱及圖層編碼後輸出。CSV 文字欄加前綴以避免公式解讀。
- 未完成：變更區域聚合、面積聯集、Revit 元素影響清單、工時評估、直接 Excel 工作簿。

## 驗證

`dotnet run --project Tests/CadRevision.Tests/CadRevision.Tests.csproj`

2026-09-26：AutoCAD 2024 API 編譯 0 錯誤／0 警告；9 項純核心情境通過（端點反向、容差、位移、歧義、重複數量、圖層、空集合、無效參數、長度差異）。尚未在 AutoCAD 載入執行，DWG 擷取、畫面及輸出須用真實圖檔驗收。

待提供一組同樓層舊／新版 DWG。驗收需檢查來源檔案雜湊不變、同圖重存無假差異、單位一致、略過項目透明，以及位移候選與人工判讀一致。

API 依據：[Autodesk Database.ReadDwgFile](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_Database_ReadDwgFile_string_FileOpenMode__MarshalAsUnmanagedType_U1__bool_string.html)、[FileOpenMode](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_FileOpenMode.html)。

## 單位設定修正
介面可分別指定舊、新版的 mm／cm／m；預設仍依 INSUNITS，未設定時停止，不猜測。手動設定只影響本次座標換算，報告保留原始 INSUNITS 與選用單位，不儲存來源 DWG。
本次使用者確認平鎮兩版圖均為公分（1 公尺＝100）。
若原版 DLL 已載入，可載入 artifacts/cad-revision/unit-fix/HB.CadRevision.UnitFix.dll，執行 HBCADCOMPARE2，免重啟。此並存版本以 CAD_UNIT_FIX 編譯符號產生，尚待 AutoCAD 實際介面與換算驗收。


## 圖塊與外部參考修正版

已載入舊版時，可 NETLOAD `artifacts/cad-revision/xref-fix/HB.CadRevision.XrefFix.dll` 並執行 `HBCADCOMPARE3`，不必重啟。以 CAD_XREF_FIX 編譯符號產生。

- 使用 AutoCAD ResolveXrefs，保留其路徑解析結果；報告列出參考儲存路徑、狀態及插入資訊。須核對參考是否指向正確歷史版本，不會自動重新配對同名外部參考。
- 圖塊遞迴最大 32 層；循環、MINSERT、具 ACAD_FILTER 的裁切／篩選參考、未解析或卸載參考明列略過。隱藏實體及關閉／凍結圖層尚未依視圖過濾，因此結果為資料庫幾何範圍，不等同出圖畫面。
- 僅依 DIM、jet-dim、TEXT、字體名稱將線段歸為標註候選，其餘為其他圖層；不宣稱已辨識建築物件。外部參考圖層前綴保留，不跨不同圖層強行配對。
- 報告新增兩版 mm 座標範圍供人工核對，尚未自動偵測／校正整圖平移與旋轉。
- 13 項核心測試通過，包含跨負座標格線、外部參考圖層分組與 6,000 線段索引；API 編譯 0 錯誤／0 警告。巢狀轉換、外部參考解析與報告畫面仍待 AutoCAD 實圖驗收。

## 密集線段修正（2026-09-27）
未變更使用細格索引，配對後移除候選；位移候選增加正反向線段向量索引。保留重複線段數量與雙向唯一配對規則，保留 2,500 萬次實際候選檢查上限。17 項核心測試通過，包括 8,000 條重疊線段及 8,000 條同中心不同長度的位移線段；真實 DWG 待驗證。
已載入舊版時，NETLOAD artifacts/cad-revision/dense-fix/HB.CadRevision.DenseFix.dll，執行 HBCADCOMPARE4，兩版公分設定維持不變。


## 既有報告互動檢視（2026-09-27）
使用 review_report.py <原報告資料夾> <新輸出資料夾>，輸出獨立 review.html，不需重讀 DWG。Canvas 疊圖、用途篩選、131 圖層可編輯分類與設定 JSON 匯入／匯出；5／10／20 公尺固定網格依幾何中心分組，不是實際影響面積或相鄰群集。
實圖轉換保留 104,098 筆，大小由 32,799,831 降至 10,950,603 bytes。瀏覽器驗證總數、用途篩選、網格定位與背景切換；設定檔匯入／匯出尚未實測。原始 CAD 弧線、圓及裁切幾何未在既有報告中，無法透過後處理補回，仍需後續 DWG 擷取器更新。


## 圓與圓弧版本（2026-09-27）
NETLOAD artifacts/cad-revision/curve-fix/HB.CadRevision.CurveFix.dll，執行 HBCADCOMPARE5；可與前版並存，不需重啟。
支援 CIRCLE、ARC、輕量聚合線弧段。巢狀變換後維持平面圓形者納入；非等比／剪切轉成橢圓者、非 XY 零高程、退化弧明列略過。裁切圖塊仍不支援。
圓與弧使用獨立曲線索引，以圓心、半徑及弧起／中／終點相對圓心向量判斷；中心與形狀各自套用 mm 容差，不宣稱全曲線 Hausdorff 誤差。鏡射弧正規化為逆時針。每個圓或弧一筆；聚合線每個弧段一筆，不拆折線計數。
輸出 curves.html（可滾輪縮放、點圖形定位）及 curves.csv（保留圓心、半徑、起角與掃角）；report.html 維持直線獨立統計。既有 review_report.py 仍只處理直線報告，曲線尚未整合 Canvas 檢視。
28 項核心情境通過；AutoCAD 2024 API 編譯 0 警告／0 錯誤。實圖讀取、鏡射／巢狀圓弧擷取與曲線 HTML 仍待 AutoCAD 執行驗收。


## 整合審查介面（2026-09-27 最新狀態）

- `cloud_review.py <review-unified 資料夾>`：由已判讀報告及來源相符的判讀 JSON 建立雲線審查與其餘變更檢視。
- 四模式：差異疊圖、左右滑桿、舊版幾何、新版幾何。交換左右不修改版本定義；縮放與平移共用座標。
- `cloud-review-bundle.json` v2 同時保存區域進度、物件判讀、圖層用途；載入前整份驗證，並相容 v1 區域進度。下載後仍須確認檔案已保存。
- 43 個確認雲線：1,760 筆雲線弧段、402 筆雲線候選，另有 7,026 筆其餘變更。總計 9,188 筆变更；不是建築元件數。
- `review-outside-clouds.html` 提供其餘變更的網格及圖層檢視，未自動判定設計意義。
- 已驗證版本幾何選取、滑桿端點、交換左右、區域前後切換、輪廓判讀及審查資料格式。

### 完整原圖底圖的待完成條件

現有 CSV 未含文字、填充與尺寸實體的完整外觀，不能由 HTML 補回。需取得兩版同範圍、同比例、同旋轉的 CAD PDF／PNG，或新增並實測 AutoCAD 原生出圖流程。兩版應保留各自歷史外部參考，確認 SHX 字型未缺失；來源 DWG 不需儲存修改。

PDF／PNG 亦須記錄 CAD 座標範圍、出圖留白與裁切範圍，才能與幾何及雲線定位對齊；不能僅依相同像素大小假設已對準。完整底圖尚未取得、未接入，本階段不能宣稱完整原圖比對已完成。
