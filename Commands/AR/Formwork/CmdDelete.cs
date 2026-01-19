using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    [Transaction(TransactionMode.Manual)]
    public class CmdDelete : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet set)
        {
            var doc = data.Application.ActiveUIDocument.Document;

            try
            {
                // 授權檢查
                if (!LicenseHelper.CheckLicense("DeleteFormwork", "刪除模板", LicenseType.Standard))
                {
                    return Result.Cancelled;
                }

                var targets = new FilteredElementCollector(doc)
                    .OfClass(typeof(DirectShape))
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .ToElements()
                    .Where(e => e.LookupParameter(SharedParams.P_Category) != null) // 我們建立的都有這個參數
                    .Select(e => e.Id)
                    .ToList();

                using (var t = new Transaction(doc, "Delete Formwork"))
                {
                    t.Start();
                    if (targets.Any()) doc.Delete(targets);
                    t.Commit();
                }

                TaskDialog.Show("Formwork", $"刪除 {targets.Count} 個量體。");
                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }
    }
}
