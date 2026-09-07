using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Forms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdTagAlign : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "目前沒有開啟中的 Revit 文件。";
                return Result.Failed;
            }

            using (var form = new TagAlignOptionsForm())
            {
                if (form.ShowDialog() != Forms.DialogResult.OK)
                    return Result.Cancelled;

                TagAlignResult result = new TagAlignService().Align(uiDoc, form.Options);
                if (!result.Success)
                {
                    TaskDialog.Show("標籤輔助對齊", result.Message);
                    return Result.Cancelled;
                }

                TaskDialog.Show("標籤輔助對齊", result.Message);
                return Result.Succeeded;
            }
        }
    }
}
