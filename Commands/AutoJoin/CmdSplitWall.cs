using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    /// <summary>
    /// 分割牆工具
    /// 兩步驟操作：
    ///   1. 選取要分割的目標牆
    ///   2. 選取切割構件（結構柱、結構構架或其他牆）
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdSplitWall : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var licenseManager = LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("AutoJoin"))
            {
                TaskDialog.Show("授權錯誤", "您的授權不包含分割牆功能，請聯繫管理員。");
                return Result.Cancelled;
            }

            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "找不到目前開啟的 Revit 文件。";
                return Result.Failed;
            }

            var doc = uiDoc.Document;

            try
            {
                // 步驟 1：選取要分割的目標牆
                var wallRefs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new WallTargetFilter(),
                    "【步驟 1/2】請選取要分割的牆，完成後按 Finish");

                var walls = wallRefs
                    .Select(r => doc.GetElement(r) as Wall)
                    .Where(w => w != null)
                    .ToList();

                if (walls.Count == 0)
                {
                    TaskDialog.Show("分割牆", "未選取任何牆，操作取消。");
                    return Result.Cancelled;
                }

                // 步驟 2：選取切割構件（結構柱、結構構架、其他牆）
                var cutterRefs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new WallCutterFilter(),
                    "【步驟 2/2】請選取切割構件（結構柱、梁、其他牆），完成後按 Finish");

                var cutters = cutterRefs
                    .Select(r => doc.GetElement(r))
                    .ToList();

                if (cutters.Count == 0)
                {
                    TaskDialog.Show("分割牆", "未選取任何切割構件，操作取消。");
                    return Result.Cancelled;
                }

                var result = SplitEngine.RunSplitWall(doc, walls, cutters);
                TaskDialog.Show("分割牆結果", BuildSummary(result));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"分割牆時發生錯誤：{ex.Message}";
                return Result.Failed;
            }
        }

        private static string BuildSummary(SplitResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"目標牆數量：{result.TargetElements}");
            sb.AppendLine($"已分割牆數量：{result.OriginalDeleted}");
            sb.AppendLine($"新建牆段數量：{result.NewElementsCreated}");
            sb.AppendLine($"跳過（無需分割）：{result.Skipped}");
            sb.AppendLine($"失敗數量：{result.FailedOperations}");
            sb.AppendLine();
            sb.AppendLine("切割來源：結構柱、結構構架、牆");

            if (result.FailureSamples.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("失敗範例（最多 5 筆）：");
                foreach (var sample in result.FailureSamples)
                    sb.AppendLine($"  - {sample}");
            }

            return sb.ToString();
        }
    }
}
