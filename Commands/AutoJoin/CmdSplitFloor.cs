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
    /// 分割樓板工具
    /// 依選取的結構構架（梁）將樓板分割為多塊，邏輯參考 SplitFloorByBeam。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdSplitFloor : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var licenseManager = LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("AutoJoin"))
            {
                TaskDialog.Show("授權錯誤", "您的授權不包含分割樓板功能，請聯繫管理員。");
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
                // 步驟 1：選取樓板與切割構件（梁）
                var refs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new FloorAndFramingFilter(),
                    "請選取要分割的樓板及梁（結構構架），完成後按 Finish");

                var floors = refs
                    .Select(r => doc.GetElement(r) as Floor)
                    .Where(f => f != null)
                    .ToList();

                var cutters = refs
                    .Select(r => doc.GetElement(r))
                    .Where(e => !(e is Floor))
                    .ToList();

                if (floors.Count == 0)
                {
                    TaskDialog.Show("分割樓板", "未選取任何樓板，操作取消。");
                    return Result.Cancelled;
                }

                if (cutters.Count == 0)
                {
                    TaskDialog.Show("分割樓板", "未選取任何切割構件（梁），操作取消。\n\n請同時選取樓板與穿越樓板的梁。");
                    return Result.Cancelled;
                }

                var result = SplitEngine.RunSplitFloor(doc, floors, cutters);
                TaskDialog.Show("分割樓板結果", BuildSummary(result));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"分割樓板時發生錯誤：{ex.Message}";
                return Result.Failed;
            }
        }

        private static string BuildSummary(SplitResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"目標樓板數量：{result.TargetElements}");
            sb.AppendLine($"已分割樓板數量：{result.OriginalDeleted}");
            sb.AppendLine($"新建樓板數量：{result.NewElementsCreated}");
            sb.AppendLine($"跳過（無需分割）：{result.Skipped}");
            sb.AppendLine($"失敗數量：{result.FailedOperations}");

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
