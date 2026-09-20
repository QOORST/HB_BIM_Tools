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
                    message = "目前沒有開啟的 Revit 模型。";
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
                TaskDialog.Show("族參數名稱修改", ex.Message);
                return Result.Failed;
            }
        }
    }
}
