# Revit 2026 介面版本差異檢視

日期：2026-09-21。範圍為主專案 2025／2026 編譯排除、版本分支、正式 Ribbon 路由及視窗呼叫。僅原始碼靜態檢查，不代表 2026 載入或完整功能驗收。未修改功能。

| 功能 | 2026 判定 | 證據 |
| --- | --- | --- |
| 族參數滑桿 | 確定未提供入口；Family 整個目錄排除編譯 | App.cs:1053；csproj 2026 Compile Remove Commands/Family/** |
| 選取族型資訊 | 與滑桿一起被條件隱藏，即使本身只是資訊功能 | App.cs:1070 附近，與上述同一版本條件 |
| 管線轉 ISO | 命令僅顯示轉換中提示後 Cancelled；實作整個目錄排除 | CmdPipeToISO.cs:25；csproj PipeToISO/** |
| 自動配管 | 命令及 XAML 排除編譯 | csproj CmdAutoPipeRouting.cs、AutoPipeRouting/UI/MainWindow.xaml |
| 舊裝修整合視窗 | 不顯示舊視窗，提示改用房間裝修／面生面 | CmdFinishings.cs:32 |
| 管線避讓 | 已有 WinForms 設定入口，排除舊 XAML 不等於沒有介面 | CmdAutoAvoid.cs:331、562 |
| 授權管理 | 2025／2026 使用 LicenseManagerDialog 替代 WPF | CmdLicenseInfo.cs:21–45 |
| 房間裝修 | 2026 已有獨立非模態表單；2025 仍不提供 | CmdRoomFinish.cs:60；RoomFinish2026ModelessController.cs:25、49；App.cs:239 |
| 自動套管 | 已補 WinForms 設定與 2024 共用設定，尚待實機 | CmdPipeSleeve.cs:39；PipeSleeveSettingsForm.cs |
| 自動標註／尺寸文字整理／自動接合 | 未見以 2026 分支略過視窗的情況 | AutoDimensionModelessController、DimensionTextCleanupCommand、AutoJoinModelessController 的 Show／ShowDialog |
| 族參數名稱修改 | 程式建立 WPF 視窗，未在 Family/** 排除範圍；非已確認缺失 | Commands/FamilyParameterRename，需 2026 執行階段驗證 |
| 公司族庫 | 外部 DLL 與資料庫均存在才註冊，與單純版本分支不同 | App.cs:1013 起，外部組件不在本次靜態檢視範圍 |

## 正式入口與舊程式區分

App.cs:462 呼叫 AddOptimizedMEPToolButtons 後直接 return。ISO／自動配管的舊 Ribbon 定義位於其後不可達區段；目前正式精簡 MEP 選單不會顯示這兩項，不能誤報成現行按鈕點擊失效。舊 CmdFinishings 亦未在 App.cs 正式註冊。

## 建議處理順序

1. 若要與 2024 功能對等，先恢復族參數滑桿及選取族型資訊，不能只解除 Ribbon 条件而不恢復對應程式。
2. ISO／自動配管先確認是否納入正式工具範圍，再補完整功能及設定流程，避免只放出失效按鈕。
3. 驗證既有 2026 替代介面是否保留所有設定、預設儲存及取消語意。
4. 正式 2026 建置鏈另需核對：目前 csproj 預設 TargetFramework 為 net48，UseWPF 對 2026 為 false；本機缺少 Revit 2026 API，不能以 2024 編譯成功認定 2026 可載入。

未發現另一個已證實與舊套管完全相同的「2026 按鈕正常執行但直接跳過設定並套用硬編碼參數」案例。已確認的是上述功能隱藏、未移植與替代視窗三類差異。
