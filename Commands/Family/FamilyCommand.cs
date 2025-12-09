using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System;

namespace YD_RevitTools.LicenseManager.Commands.Family
{
    [Transaction(TransactionMode.Manual)]
    public class CmdFamilyParameterSlider : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 族參數滑桿功能
                var licenseManager = LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Family.ParameterSlider"))
                {
                    TaskDialog.Show("License Restriction",
                        "Your license does not support Family Parameter Slider feature.\n\n" +
                        "Please upgrade to Standard or Professional version to use this feature.\n\n" +
                        "Click 'License Management' button to view or update your license.");
                    return Result.Cancelled;
                }

                Document doc = commandData.Application.ActiveUIDocument.Document;
                if (!doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Error", "Please use this feature in Family Editor.");
                    return Result.Failed;
                }

                var win = new MainWindow(commandData);
                win.Show();
                return Result.Succeeded;
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
