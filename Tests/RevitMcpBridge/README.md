# HB_BIM MCP 診斷橋接

RevitMcpBridge.csproj 以使用者現有 MCP 原始碼建置；CommandExecutor.generated.cs 為 CommandExecutor 的副本，僅新增 hb_diagnose_grid_dimensions 路由。未變更外部 MCP 原始碼。

HB_BIM 公開診斷入口使用目前平面視圖全部可見直線軸線與已保存設定。暫時建立標註後回復交易，輸出分組、重複判斷、例外與前後標註數。不是目前介面未儲存選取狀態的精確重播，也不驗證 Commit 階段。

部署需關閉 Revit，同時更新 HB_BIM 與 RevitMCP DLL。重開測試模型後啟動 MCP 服務，再執行 Tools/RevitMcp/diagnose-grids.mjs，參數為既有 MCP-Server 絕對路徑及報告輸出路徑。

使用工作區 diagnostic-server.mjs 提供單一診斷工具，沒有任意程式碼執行入口。
