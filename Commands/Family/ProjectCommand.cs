using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.Attributes;
using System;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.Family
{
    [Transaction(TransactionMode.Manual)]
    public class CmdProjectParameterSlider : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 專案參數滑桿功能
                var licenseManager = LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Family.ProjectSlider"))
                {
                    TaskDialog.Show("授權限制", "您的授權不包含此族群工具，請於「授權管理」查看授權。 ");
                    return Result.Cancelled;
                }

                var uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null) { TaskDialog.Show("選取族型資訊", "請先開啟模型。 "); return Result.Cancelled; }
                var doc = uidoc.Document;
                var sel = uidoc.Selection;

                var id = sel.GetElementIds().FirstOrDefault();
                if (id == null)
                {
                    TaskDialog.Show("選取族型資訊", "請先選取一個族群實例。 ");
                    return Result.Cancelled;
                }

                var element = doc.GetElement(id);
                if (element is FamilyInstance instance)
                {
                    TaskDialog.Show("選取族型資訊", $"族型名稱：{instance.Symbol.Name}");
                    return Result.Succeeded;
                }

                TaskDialog.Show("選取族型資訊", "目前選取的元素不是族群實例。 ");
                return Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("選取族型資訊", $"執行失敗：{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
