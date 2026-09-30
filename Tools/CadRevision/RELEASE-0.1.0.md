# HB CAD 版次比對 0.1.0 試用版

## 本次整合

- AutoCAD 2024 獨立模組，正式入口統一 HBCADCOMPARE；不載入 Revit 程序。
- 比對完成自動產生 review.html 並嘗試開啟預設瀏覽器；直線及曲線整合，四種顯示模式，不需要 Python。
- scope.txt 保存來源、單位、容差、底圖選擇、投影模式及略過項目。
- PackageContents.xml 僅限定 AutoCAD 2024，未宣稱支援 2025/2026。
- 平面投影仍屬試用且預設關閉。共用底圖不是歷史底圖比對。

## 安裝

先以 dotnet build Tools/CadRevision/CadRevision.csproj -c Release -o artifacts/cad-revision/HB.CadRevision.bundle/Contents/Win64 編譯，再將 PackageContents.xml 複製到 bundle 根目錄。執行本目錄 Install.ps1 安裝目前使用者的 Autodesk/ApplicationPlugins。

不修改 AutoCAD 安全性設定，不自動關閉 AutoCAD。若舊 HB.CadRevision 已在目前工作階段載入，於下次正常重開後驗證正式指令。舊編號 DLL 留作回復，不需繼續載入。

## 驗收紀錄與界線

- 33 項核心測試通過；6 項雲線判读測試及 JavaScript 資料驗證測試通過。
- 新 C# 整合報告以不依賴 AutoCAD 的測試程式驗證混合幾何、純曲線、HTML 跳脫及內嵌資源。
- 平鎮既有實圖：129,167 筆紀錄。
- 共善樓既有大型圖實測：234,020 筆紀錄，223,691 未變更、10,329 變更／待確認；主圖變更與未加入底圖版本相符。
- 上述實圖為先前擷取版本證據，不是本次 bundle 自動載入、完整輸出鏈或平面投影的實圖驗收。這三項仍待 AutoCAD 執行。
- 文字、填充、尺寸完整外觀、裁切參考、傾斜幾何、完整歷史底圖及 Revit 影響分析未完成。不以幾何數量估算建築元件數或工時。
- 雲線人工審查屬既有離線延伸，尚未自動整合至每一份新報告；新報告不自動判定雲線。

Autodesk 自動載入格式依據：https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Customization/files/GUID-BC76355D-682B-46ED-B9B7-66C95EEF2BD0.htm

## AutoCAD 2026 適配（2026-10-01）

本機 acdbmgd.runtimeconfig.json 為 .NET 8，API 版本 25.1.164.0.0。以 CadVersion=2026 建置 net8.0-windows，JSON 序列化改用 System.Text.Json；2024 保留 net48。兩版編譯均 0 錯誤／0 警告。

套件依 R24.3 與 R25.1 分別載入 Contents/Win64、Contents/2026；2025 不在宣告範圍。已備份並更新目前使用者套件設定。AutoCAD 2026 自動載入、設定視窗與 DWG 全流程尚待實機驗收。
