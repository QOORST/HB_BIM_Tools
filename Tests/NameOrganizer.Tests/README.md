# 名稱整理測試

```powershell
dotnet run --project Tests/NameOrganizer.Tests/NameOrganizer.Tests.csproj -p:NuGetAudit=false -- artifacts/name-organizer-20261017/ui
```

Windows／.NET Framework 4.8。連結正式名稱規則、WinForms 與服務程式；Revit API 使用測試替身。測試服務的交易順序、改名交換、例外回復、提交狀態、預演與實際刪除影響比對，以及介面範圍控制、待套用預覽保留。

替身僅供故障注入，不能驗證 Revit 幾何、工作共享、原生失敗處理及真實文件參照。原生待辦見 `Docs/qa/model-name-organizer.md`。測試有 60 秒執行逾時保護並輸出介面截圖。
