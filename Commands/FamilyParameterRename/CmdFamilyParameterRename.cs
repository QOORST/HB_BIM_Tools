using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Services;
using YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.UI;

namespace YD_RevitTools.LicenseManager.Commands.FamilyParameterRename
{
    [Transaction(TransactionMode.Manual)]
    public class CmdFamilyParameterRename : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDocument = commandData.Application.ActiveUIDocument;
                if (uiDocument == null)
                {
                    message = "No active Revit document.";
                    return Result.Failed;
                }

                var service = new FamilyParameterRenameService(uiDocument.Document);
                var window = new FamilyParameterRenameWindow(service)
                {
                    Owner = System.Windows.Interop.HwndSource.FromHwnd(commandData.Application.MainWindowHandle)?.RootVisual as System.Windows.Window
                };

                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Family Parameter Rename", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
