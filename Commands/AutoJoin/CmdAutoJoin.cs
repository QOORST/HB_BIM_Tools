using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdAutoJoin : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 授權檢查
        var licenseManager = LicenseManager.Instance;
        if (!licenseManager.HasFeatureAccess("AutoJoin"))
        {
            TaskDialog.Show("授權錯誤", "您的授權不包含自動接合功能，請聯繫管理員。");
            return Result.Cancelled;
        }

        var uiDoc = commandData.Application.ActiveUIDocument;
        if (uiDoc == null)
        {
            message = "找不到目前開啟的 Revit 文件。";
            return Result.Failed;
        }

        try
        {
            AutoJoinModelessController.Show(commandData.Application);
            return Result.Succeeded;
        }
        catch (System.Exception ex)
        {
            AutoJoinModelessController.Log("Startup failed: " + ex);
            message = "自動接合介面啟動失敗：" + ex.Message;
            TaskDialog.Show("自動接合", message + "\n診斷紀錄（若可寫入）：\n" + AutoJoinModelessController.DiagnosticPath);
            return Result.Failed;
        }
    }

    internal static string Run(UIDocument uiDoc, AutoJoinSettings settings, ExecutionAction action)
    {
        SettingsSerializer.Save(SettingsSerializer.DefaultPath, settings);
        var scopedElements = ElementCollectorService.Collect(uiDoc, settings);
        if (scopedElements.Count == 0)
        {
            return "在目前設定範圍內找不到可處理的元素。";
        }

        if (RequiresLargeScopeConfirmation(action, scopedElements.Count))
        {
            var confirm = new TaskDialog("自動接合")
            {
                MainInstruction = "目前範圍內元素數量較多，執行時間可能較長。",
                MainContent =
                    $"將處理 {scopedElements.Count} 個元素，最多約 {GetPairCount(scopedElements.Count):N0} 次配對檢查。\n\n" +
                    "建議優先使用「目前視圖可見」或「已選元素」縮小範圍。",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "繼續執行");

            if (confirm.Show() != TaskDialogResult.CommandLink1)
                return "已取消，未執行模型變更。";
        }

        if (action == ExecutionAction.AlignWallProfile)
        {
            var alignResult = WallProfileAligner.Run(uiDoc.Document, scopedElements);
            return BuildAlignSummary(alignResult);
        }

        JoinEngineResult result;
        if (action == ExecutionAction.AutoJoin)
        {
            result = JoinEngine.RunAutoJoin(uiDoc.Document, scopedElements, settings);
        }
        else
        {
            result = JoinEngine.RunUnjoin(uiDoc.Document, scopedElements, settings);
        }

        var summary = BuildSummary(result);
        if (result.FailureDetails.Count > 0)
            ModelessReviewController.ShowOrUpdate(action == ExecutionAction.AutoJoin ? "自動接合結果" : "解除接合結果",
                summary, result.FailureDetails, uiDoc.Document);
        return summary;
    }

    private static string BuildSummary(JoinEngineResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"範圍內元素數量: {result.ElementsInScope}");
        sb.AppendLine($"配對檢查次數: {result.PairChecks}");
        sb.AppendLine($"可能配對數量: {result.PairCandidates}");
        sb.AppendLine($"成功接合數量: {result.Joined}");
        sb.AppendLine($"成功解除接合數量: {result.Unjoined}");
        sb.AppendLine($"重排優先序數量: {result.Reordered}");
        sb.AppendLine($"失敗操作數量: {result.FailedOperations}");

        if (result.FailureSamples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("失敗範例（最多 5 筆）:");
            foreach (var sample in result.FailureSamples)
            {
                sb.AppendLine($"- {sample}");
            }
        }

        return sb.ToString();
    }

    private static bool RequiresLargeScopeConfirmation(ExecutionAction action, int elementCount)
    {
        if (action != ExecutionAction.AutoJoin && action != ExecutionAction.Unjoin)
            return false;

        return elementCount >= 800;
    }

    private static long GetPairCount(int elementCount)
    {
        if (elementCount < 2)
            return 0;

        return ((long)elementCount * (elementCount - 1)) / 2;
    }

    private static string BuildAlignSummary(WallAlignResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"範圍內牆數量: {result.WallsInScope}");
        sb.AppendLine($"成功調整牆數量: {result.WallsAdjusted}");
        sb.AppendLine($"跳過牆數量: {result.WallsSkipped}");
        sb.AppendLine($"失敗操作數量: {result.FailedOperations}");

        if (result.FailureSamples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("失敗範例（最多 5 筆）:");
            foreach (var sample in result.FailureSamples)
                sb.AppendLine($"- {sample}");
        }

        return sb.ToString();
    }
    }
}
