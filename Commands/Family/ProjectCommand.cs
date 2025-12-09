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
                    TaskDialog.Show("License Restriction",
                        "Your license does not support Project Parameter Slider feature.\n\n" +
                        "Please upgrade to Standard or Professional version to use this feature.\n\n" +
                        "Click 'License Management' button to view or update your license.");
                    return Result.Cancelled;
                }

                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;
                var sel = uidoc.Selection;

                var id = sel.GetElementIds().FirstOrDefault();
                if (id == null)
                {
                    TaskDialog.Show("Info", "Please select a family instance first.");
                    return Result.Cancelled;
                }

                var element = doc.GetElement(id);
                if (element is FamilyInstance instance)
                {
                    TaskDialog.Show("Info", $"Family type name: {instance.Symbol.Name}");
                    return Result.Succeeded;
                }

                TaskDialog.Show("Error", "Please select a family instance");
                return Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", $"Execution failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
