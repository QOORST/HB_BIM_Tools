using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdAlignWallProfile : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var licenseManager = LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("AutoJoin"))
            {
                TaskDialog.Show("授權限制", "目前授權未包含自動接合工具，請聯繫管理員或更新授權。");
                return Result.Cancelled;
            }

            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "找不到目前開啟的 Revit 文件。";
                return Result.Failed;
            }

            var settings = SettingsSerializer.LoadOrDefault(SettingsSerializer.DefaultPath);

            using var form = new AutoJoinForm(settings, true);
            var dialogResult = form.ShowDialog();
            if (dialogResult != DialogResult.OK || form.Action != ExecutionAction.AlignWallProfile)
            {
                return Result.Cancelled;
            }

            settings = form.BuildSettings();
            SettingsSerializer.Save(SettingsSerializer.DefaultPath, settings);

            var scopedElements = ElementCollectorService.Collect(uiDoc, settings);
            if (scopedElements.Count == 0)
            {
                TaskDialog.Show("對齊牆輪廓", "在目前設定範圍內找不到可處理的元素。");
                return Result.Succeeded;
            }

            var alignResult = WallProfileAligner.Run(uiDoc.Document, scopedElements);
            TaskDialog.Show("對齊牆輪廓結果", BuildAlignSummary(alignResult));
            return Result.Succeeded;
        }

        private static string BuildAlignSummary(WallAlignResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"牆面範圍數量: {result.WallsInScope}");
            sb.AppendLine($"成功對齊數量: {result.WallsAdjusted}");
            sb.AppendLine($"略過數量: {result.WallsSkipped}");
            sb.AppendLine($"失敗數量: {result.FailedOperations}");

            if (result.FailureSamples.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("失敗範例:");
                foreach (var sample in result.FailureSamples)
                {
                    sb.AppendLine($"- {sample}");
                }
            }

            return sb.ToString();
        }
    }
}
