using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdTagAlignHorizontal : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => TagAlignQuick.Execute(data, TagAlignMode.Horizontal);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdTagAlignVertical : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => TagAlignQuick.Execute(data, TagAlignMode.Vertical);
    }

    internal static class TagAlignQuick
    {
        internal static Result Execute(ExternalCommandData data, TagAlignMode mode)
        {
            if (data.Application.ActiveUIDocument == null) return Result.Cancelled;
            string title = mode == TagAlignMode.Horizontal ? "標籤水平對齊" : "標籤垂直對齊";
            try
            {
                var result = new TagAlignService().Align(data.Application.ActiveUIDocument,
                    new TagAlignOptions { PickReference = true, Mode = mode, Scope = TagAlignScope.Selection });
                if (result.IsCancelled) return Result.Cancelled;
                if (!result.Success || result.NeedsReview) TaskDialog.Show(title, result.Message);
                return result.Success ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                TaskDialog.Show(title, "對齊未完成，請檢查模型狀態。\n" + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
