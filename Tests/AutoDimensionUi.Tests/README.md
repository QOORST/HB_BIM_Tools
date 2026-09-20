# 自動標註介面與軸線標頭回歸測試

執行：`dotnet run --project Tests/AutoDimensionUi.Tests/AutoDimensionUi.Tests.csproj -- artifacts/dimension-ui-qa`

需要 Windows 與 .NET 10 SDK。直接編譯正式介面、選項與標頭服務程式；Revit API 使用離線測試替身。

驗證獨立分頁、共用軸線選取、標頭四端勾選、全顯示／全隱藏、送出與等待狀態、空清單、重新整理，以及正反方向軸線與視圖曲線的端點對應。產生一般／鎖定模式、不同寬度的介面圖片。

此測試不取代 Revit 內的實際視圖、交易及顯示效果驗收。
