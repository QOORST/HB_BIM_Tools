# 正式工具入口對照

由 App.cs 正式註冊方法擷取，排除已 return 的舊 MEP 區段及未呼叫的重複澄清入口。含條件式入口，並非任一 Revit 版本的畫面按鈕數。此表只確認入口與原始碼對照，不代表模型操作通過。

| 工具 | 指令原始碼 | 狀態 |
| --- | --- | --- |
| 對齊牆輪廓 | [Commands/AutoJoin/CmdAlignWallProfile.cs](../../Commands/AutoJoin/CmdAlignWallProfile.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 自動標註 | [Commands/AR/AutoDimension/App/AutoDimensionCommand.cs](../../Commands/AR/AutoDimension/App/AutoDimensionCommand.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 房間內容 | [Commands/AR/AutoDimension/App/RoomContentCommand.cs](../../Commands/AR/AutoDimension/App/RoomContentCommand.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 自動接合 | [Commands/AutoJoin/CmdAutoJoin.cs](../../Commands/AutoJoin/CmdAutoJoin.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 自動標籤 | [Commands/AR/AutoTag/CmdAutoTag.cs](../../Commands/AR/AutoTag/CmdAutoTag.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 更換顏色 | [Commands/AR/Finishings/CmdChangeFinishingColor.cs](../../Commands/AR/Finishings/CmdChangeFinishingColor.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 刪除裝修 | [Commands/AR/Finishings/CmdDeleteFinishings.cs](../../Commands/AR/Finishings/CmdDeleteFinishings.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 外牆粉刷驗算 | [Commands/AR/Finishings/CmdExteriorWallFinishReport.cs](../../Commands/AR/Finishings/CmdExteriorWallFinishReport.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 面生面 | [Commands/AR/Finishings/CmdFaceToFace.cs](../../Commands/AR/Finishings/CmdFaceToFace.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 刪除模板 | [Commands/AR/Formwork/CmdDelete.cs](../../Commands/AR/Formwork/CmdDelete.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 匯出CSV | [Commands/AR/Formwork/CmdExportCsv.cs](../../Commands/AR/Formwork/CmdExportCsv.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 模板生成 | [Commands/AR/Formwork/CmdMain.cs](../../Commands/AR/Formwork/CmdMain.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 面選模板 | [Commands/AR/Formwork/CmdPickFace.cs](../../Commands/AR/Formwork/CmdPickFace.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 更新粉刷參數 | [Commands/AR/Finishings/CmdRefreshFinishParams.cs](../../Commands/AR/Finishings/CmdRefreshFinishParams.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 房間裝修 | [Commands/AR/Finishings/RoomFinish/CmdRoomFinish.cs](../../Commands/AR/Finishings/RoomFinish/CmdRoomFinish.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 關聯標籤 | [Commands/AR/AutoTag/CmdSelectRelatedTags.cs](../../Commands/AR/AutoTag/CmdSelectRelatedTags.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 分割樓板 | [Commands/AutoJoin/CmdSplitFloor.cs](../../Commands/AutoJoin/CmdSplitFloor.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 分割牆 | [Commands/AutoJoin/CmdSplitWall.cs](../../Commands/AutoJoin/CmdSplitWall.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 結構分析 | [Commands/AR/Formwork/CmdStructuralAnalysis.cs](../../Commands/AR/Formwork/CmdStructuralAnalysis.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 標籤對齊 | [Commands/AR/AutoTag/CmdTagAlign.cs](../../Commands/AR/AutoTag/CmdTagAlign.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 關於HB_BIM | [Commands/AboutCommand.cs](../../Commands/AboutCommand.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 檢查更新 | [Commands/CheckUpdateCommand.cs](../../Commands/CheckUpdateCommand.cs) | 入口對照完成；模型及介面待逐項驗收 |
| AI助理 | [Commands/AI/CmdAiAssistant.cs](../../Commands/AI/CmdAiAssistant.cs) | 入口對照完成；模型及介面待逐項驗收 |
| BIM 標準檢查 | [Commands/Data/BimStandard/CmdBimStandardAudit.cs](../../Commands/Data/BimStandard/CmdBimStandardAudit.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 釋疑簡報 | [Commands/Data/ClarificationDeck/CmdClarificationDeckExport.cs](../../Commands/Data/ClarificationDeck/CmdClarificationDeckExport.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 自訂 COBie | [Commands/Data/CmdCobieExportEnhanced.cs](../../Commands/Data/CmdCobieExportEnhanced.cs) | 入口對照完成；模型及介面待逐項驗收 |
| COBie 樣板 | [Commands/Data/CmdCobieExportTemplate.cs](../../Commands/Data/CmdCobieExportTemplate.cs) | 入口對照完成；模型及介面待逐項驗收 |
| COBie 欄位設定 | [Commands/Data/CmdCobieFieldManager.cs](../../Commands/Data/CmdCobieFieldManager.cs) | 入口對照完成；模型及介面待逐項驗收 |
| COBie 匯入 | [Commands/Data/CmdCobieImportEnhanced.cs](../../Commands/Data/CmdCobieImportEnhanced.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 標準 COBie | [Commands/Data/CmdCobieStandardExport.cs](../../Commands/Data/CmdCobieStandardExport.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 模型資料管理 | [Commands/Data/CmdModelDataManager.cs](../../Commands/Data/CmdModelDataManager.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 明細表匯出 | [Commands/Data/CmdScheduleExport.cs](../../Commands/Data/CmdScheduleExport.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 族群資料庫 | 外部族庫組件 | 公司內網環境驗收 |
| 族參數名稱修改 | [Commands/FamilyParameterRename/CmdFamilyParameterRename.cs](../../Commands/FamilyParameterRename/CmdFamilyParameterRename.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 族參數滑桿 | [Commands/Family/FamilyCommand.cs](../../Commands/Family/FamilyCommand.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 選取族型資訊 | [Commands/Family/ProjectCommand.cs](../../Commands/Family/ProjectCommand.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 授權管理 | [Commands/AR/CmdLicenseInfo.cs](../../Commands/AR/CmdLicenseInfo.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 建築開孔 | [Commands/MEP/CmdArchitecturalOpeningFromSleeves.cs](../../Commands/MEP/CmdArchitecturalOpeningFromSleeves.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 管線避讓 | [Commands/MEP/CmdAutoAvoid.cs](../../Commands/MEP/CmdAutoAvoid.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 接點生成管 | [Commands/MEP/CmdMepConnectionTools.cs](../../Commands/MEP/CmdMepConnectionTools.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 生成管設定 | [Commands/MEP/CmdMepConnectionTools.cs](../../Commands/MEP/CmdMepConnectionTools.cs) | 入口對照完成；模型及介面待逐項驗收 |
| MEP樓層歸位 | [Commands/MEP/CmdMepConnectionTools.cs](../../Commands/MEP/CmdMepConnectionTools.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 管線定位尺寸 | [Commands/MEP/CmdMepPositionDimension.cs](../../Commands/MEP/CmdMepPositionDimension.cs)<br>[Commands/MEP/MepAutomaticBeamDimension.cs](../../Commands/MEP/MepAutomaticBeamDimension.cs)<br>[Commands/MEP/MepDimensionTextLayout.cs](../../Commands/MEP/MepDimensionTextLayout.cs)<br>[Commands/MEP/MepFamilyPositionDimension.cs](../../Commands/MEP/MepFamilyPositionDimension.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 定位尺寸設定 | [Commands/MEP/CmdMepPositionDimension.cs](../../Commands/MEP/CmdMepPositionDimension.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 快速下行 | [Commands/MEP/CmdMepUpDownOffset.cs](../../Commands/MEP/CmdMepUpDownOffset.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 上下行設定 | [Commands/MEP/CmdMepUpDownOffset.cs](../../Commands/MEP/CmdMepUpDownOffset.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 快速上行 | [Commands/MEP/CmdMepUpDownOffset.cs](../../Commands/MEP/CmdMepUpDownOffset.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 手動翻彎 | [Commands/MEP/CmdMepUpDownOffset.cs](../../Commands/MEP/CmdMepUpDownOffset.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 批次對齊 | [Commands/MEP/CmdPipeCenterAlign.cs](../../Commands/MEP/CmdPipeCenterAlign.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 支管對齊 | [Commands/MEP/CmdPipeCenterAlign.cs](../../Commands/MEP/CmdPipeCenterAlign.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 自動套管 | [Commands/MEP/CmdPipeSleeve.cs](../../Commands/MEP/CmdPipeSleeve.cs) | 入口對照完成；模型及介面待逐項驗收 |
| 套管管理 | [Commands/MEP/CmdPipeSleeveManager.cs](../../Commands/MEP/CmdPipeSleeveManager.cs) | 入口對照完成；模型及介面待逐項驗收 |
