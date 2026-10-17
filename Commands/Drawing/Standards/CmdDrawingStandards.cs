using System;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.Drawing.Standards
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdDrawingStandards : IExternalCommand
    {
        private sealed class Owner : IWin32Window { public IntPtr Handle { get; set; } }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument)
            { TaskDialog.Show("施工圖樣板管理", "請先開啟 Revit 專案。"); return Result.Cancelled; }
            if (!LicenseManager.Instance.HasFeatureAccess("Data.BimStandardAudit"))
            { TaskDialog.Show("施工圖樣板管理", "此功能需要 Trial 或以上授權。"); return Result.Cancelled; }
            try
            {
                var service = new DrawingStandardService(ui);
                using (var form = new DrawingStandardForm(service.Read, service.Choices, service.Save,
                    (key, scope) => service.Preview(key, service.Targets(scope)), service.Apply,
                    new DrawingStandardManagement { Create = service.CreateStandard, Rename = service.RenameStandard,
                        Archive = service.ArchiveStandard, Inventory = service.Inventory, CreateTemplate = service.CreateTemplate }))
                    form.ShowDialog(new Owner { Handle = data.Application.MainWindowHandle });
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
