# 安全性改善第一階段：交付與驗證紀錄

日期：2026-10-03（台灣時間）
基準：`21759b1ad039814a6f2a56750846b6fea225da45`
狀態：原始碼修正稿，尚未編譯為 Revit 外掛或發布。

## 已修改

1. 分割樓板／牆：逐原件交易、成功提交後才計數、建立失敗回復、相依刪除保護、重建幾何比對。移除整牆包圍盒高程切割近似。
2. 管線避讓：檢查內部彎頭、原有外部連接與刪除相依性；不完整時失敗回復；提交成功才回報成功。
3. COBie 匯入：唯讀預檢、預設否的確認、嚴格且唯一的識別、型別參數影響提示、衝突寫入阻擋、來源列失敗清單。
4. 發版：各年版新建置及雜湊核對、缺必要檔案中止、移除錯年版 DLL 替代；保留現行版本號，不自行發布。
5. 文件：修正 README 版本狀態及功能限制，新增功能矩陣、驗收清單與離線回歸入口。

## 保守行為與相容性變更

- 分割目前僅接受可驗證的矩形水平樓板區域及直線垂直基本牆區域。孔洞、曲面、斜面、部分切割、宿主／標註相依性等無法確認時保留原件並略過。這不是完整幾何分割演算法。
- 成功重建會更換元素 ID；Revit 外部保存的 ID 關係无法自動偵測，仍需人工重新對應。
- 管線中段支接、刪除會影響其他元素，或提交產生警告／錯誤時，避讓會保守取消。原先會勉強完成的案例可能改為明確失敗。
- 匯入提供過期 UniqueId／ElementId 時不再退回 Mark 猜測；請使用同一模型重新匯出的識別資料。ElementId 單獨不足以確認跨模型身分。
- 發版腳本要求完整同年版主程式、外部公司族庫專案及相依資源。缺項會中止，不能視為支援缺件部署。

## 本次已執行的檢查

- `git diff --check`：通過。
- `python3 Tests/AutoAvoidConnection.Tests/check_transaction_guard.py`：10 項原始碼契約檢查通過。
- `python3 -m unittest discover -s Tests/ReleaseSafeguards -p 'test*.py'`：7 項原始碼契約檢查通過。
- 新增 C# 測試專案 XML 格式檢查：通過。
- 使用者同意後，已將 Microsoft 官方 .NET SDK **8.0.425** 安裝於雲端工作區專用目錄（未變更系統安裝或使用者電腦）。
- `Tools/QA/run-safety-regression.py`：五套 C# 離線測試全部編譯並執行通過，共 **85 項**：SplitSafety 15、CobieImportSafety 24、AutoAvoidConnection 24、FormworkAnalysisScope 5、FormworkMeshBudget 17。首次執行即通過，未為通過測試追加程式修正。
- 獨立靜態檢閱：修正發版版本變數被年份覆蓋、刪除相依檢查、重複識別欄位、XLSX 列號、參數 ID 轉型與不相交切割構件處理。

上述 17 項 Python 檢查只確認特定原始碼結構。85 項 C# 檢查包含正式來源連結的純邏輯及 Revit 替身控制流程測試；兩者均不是 Revit 實測，也不代表完整外掛或各年版 API 編譯通過。執行紀錄位於 `artifacts/safety-validation/`。

## 未執行／阻擋

- 未執行四年版完整 Revit API 編譯、Windows PowerShell 發版腳本、簽章或安裝。
- 未驗證原生 Revit 幾何、接點、參數、交易失敗回復或 Undo。
- 未推送 GitHub、建立 PR、發布 Release，亦未修改使用者電腦上的外掛。

## 後續驗證入口

本次已使用獲准安裝的 SDK 執行；後續可重跑：

`python3 Tools/QA/run-safety-regression.py --dotnet <dotnet 路徑>`

此入口只執行三套新增離線測試及兩套既有純邏輯測試，不安裝、不部署、不操作 Revit。SDK 不存在時以非零結束碼回報 NOT RUN，不將未執行視為通過。

Windows／Revit 環境可用且部署測試版本獲同意後，依各測試目錄 README、[功能矩陣](capability-matrix.md)及[發版門檻](release-validation.md)逐項驗收。請先使用可復原模型副本，勿直接套用正式模型。

## Windows validation update (2026-10-03)

- Applied this patch in an independent checkout at the stated base commit; original checkout unchanged.
- Revit 2022 main DLL: Release2022/x64 built successfully, 0 warnings and 0 errors.
- Revit 2024 main DLL: Release2024/x64 rebuilt successfully after the UI source-link fix, 5 existing CS0618 warnings and 0 errors.
- Five safety C# offline suites passed 85 checks; Python source contracts passed 17 checks. Explicit UTF-8 reads fix the source checks on Traditional Chinese Windows.
- Additional offline UI suites: AutoDimensionUi.Tests passed 195 assertions; ToolSuiteUi.Tests passed 150 checks after extracting the shared SleeveLevelPolicy.Choice source.
- Revit 2025/2026 builds were not run because their API DLLs were unavailable. The external family-library project and complete installer were not built.
- Revit model acceptance remains NOT RUN. The attempted Revit 2024 deployment was blocked by Windows administrator file permissions; installed DLL/PDB/manifest hashes remained unchanged. No model was opened or modified, and no deployment retry is part of this change.
- Offline doubles and UI fixtures do not validate native geometry, transactions, connector restoration or model Undo.
