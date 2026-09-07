using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdAutoTagHorizontal : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Execute(commandData, AutoTagMode.Horizontal, ref message);
        }

        internal static Result Execute(ExternalCommandData commandData, AutoTagMode mode, ref string message)
        {
            try
            {
                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                if (uiDoc == null)
                {
                    message = "目前沒有開啟中的 Revit 文件。";
                    return Result.Failed;
                }

                return AutoTagModelessController.Show(commandData, mode, ref message);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("自動標籤", $"建立標籤失敗：{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
