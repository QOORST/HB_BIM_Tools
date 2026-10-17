# 模型管理檢查測試

在 Windows 執行：

```powershell
dotnet run --project Tests/ModelAudit.Tests/ModelAudit.Tests.csproj -p:NuGetAudit=false -- artifacts/model-audit-simple-20261017/ui
```

使用 .NET Framework 4.8，連結正式模型、WinForms 與報表程式碼。38 項驗證包含命名前綴選用、結果狀態、略過原因與恢復、舊結果清除、匯出包含隱藏結果、定位失敗保留視窗、名稱整理帶入及重新檢查規則沿用，以及 Excel 實際讀回比對。

會輸出主視窗／設定截圖與空白及含資料範例報告。含 60 秒逾時保護，避免非預期對話框讓測試卡住。此測試不執行 Revit API；原生驗收事項見 `Docs/qa/model-audit.md`。
