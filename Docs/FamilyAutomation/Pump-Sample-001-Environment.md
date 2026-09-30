# 樣本建族環境檢查

## 實作更新

已完成獨立建族器編譯及 Revit 2024.3 實測，產生 `artifacts/PumpFamilyBuilder/prototype-03/output/` 的 RFA、接管測試 RVT 與結果報告。兩類型、幾何變形、重開及實際接管均通過；電氣接頭及 2025／2026 驗證尚未完成。本次 net48 參考組件已由成功編譯確認可用。下列清單保留最初環境盤點紀錄。

- 已找到 Revit 2024 安裝目錄及 RevitAPI.dll、RevitAPIUI.dll。
- 已找到可用候選範本：`C:/ProgramData/Autodesk/RVT 2024/Family Templates/English/Metric Mechanical Equipment.rft`。
- 繁體中文範本目錄未找到一般機械設備範本；採英文公制範本，族群參數仍可使用繁體中文。
- 已安裝 .NET SDK 7.0.202、10.0.100；尚未編譯建族外掛，未驗證 net48 參考組件完整性。
- 檢查時未找到執行中的 Revit 程序。
- 預設 Autodesk 安裝目錄未找到 Revit 2025／2026，無法宣稱已完成這兩版測試。
- 專案既有 Tests/RevitMcpBridge 是軸線標註診斷橋接，沒有可直接拿來執行任意建族程式的入口；不修改它以代替建族外掛。

## 下一個實作交付

建立獨立 Revit 2024 外部命令，使用公制機械設備範本與 pump-sample-001.input.json，產生兩個組合類型、原生簡化幾何與導桿控制參數。需要實際 Revit API 執行後才可交付 RFA；本次僅完成輸入資料與環境盤點。

未知尺寸的概念建模值須另外記錄，不能覆寫來源尺寸。電氣負載尚缺，不將空值偽裝為正式額定值。
