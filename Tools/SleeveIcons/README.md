# 套管功能圖示

專用 PNG，透明背景，16／32 px。使用相同線寬、青色與橘色語意；不覆寫其他功能共用圖示。

| 名稱 | 功能 | 圖像 |
| --- | --- | --- |
| sleeve_create | 自動套管 | 穿牆套管與新增符號 |
| sleeve_manage | 套管管理 | 套管與檢核清單 |
| sleeve_cad_create | CAD 建立套管 | 圖面格線、套管與定位靶 |
| sleeve_opening | 建築開孔 | 牆體與鏤空孔洞 |
| parameter_copy | 參數複製 | 兩份欄位資料與方向箭頭 |

在此目錄執行 `dotnet run --project Generator.csproj` 可重建；結果在 bin/Debug/net10.0-windows/icons，預覽為 icon-preview.png。
原始碼使用 .cs.txt 避免主 Revit 專案預設遞迴 Compile 納入產圖程式。
圖示由主專案 Resources/Icons/*.png 自動複製；App.cs 透過既有 LoadIcon 載入。
已離線檢查淺／深色背景及透明內容；原生 Ribbon 外觀仍需部署後確認。
