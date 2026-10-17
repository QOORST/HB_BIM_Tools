# 施工圖樣板管理離線測試

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' run --project Tests/DrawingStandards.Tests/DrawingStandards.Tests.csproj -c Release -- artifacts/drawing-standards-library-20261017/ui
```

直接編譯正式的規則與 WinForms 原始碼。測試包含來源實際設定變更、來源身分、舊版本／錯用途阻擋、只處理勾選視圖、主從視圖例外、版本資料往返、CSV 注入防護與換行、範圍切換失效預覽、套用後重查、編輯器還原及兩種視窗尺寸。

另測試 v1 升級、v2 重開不重複加入預設、任意系統命名、複製不沿用核對版本或指派、停用／啟用與全部停用狀態、複製資料深層隔離、歷史名稱保存，以及樣板清單／使用範圍／建立要求的來源身分。

會產生主視窗 1120 × 740、900 × 620、設定編輯器與既有樣板管理截圖。測試對話框由測試程式操控，設有逾時保護。未啟動 Revit，樣板建立回呼使用測試資料；未驗證 Revit 實際建立／複製 API、交易、圖面顯示或工作共用權限。原生驗收清單見 `Docs/qa/drawing-standards.md`。
