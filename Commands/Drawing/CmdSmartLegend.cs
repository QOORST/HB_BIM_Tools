using System;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using View = Autodesk.Revit.DB.View;

namespace YD_RevitTools.LicenseManager.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdSmartLegend : IExternalCommand
    {
        private sealed class Owner : IWin32Window
        {
            public IntPtr Handle { get; }
            internal Owner(IntPtr handle) { Handle = handle; }
        }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument)
            { TaskDialog.Show("HB 智慧圖例", "請先開啟 Revit 專案，並切換到來源模型視圖或選取構件。"); return Result.Cancelled; }
            try
            {
                using (var form = new LegendForm(ui))
                {
                    if (form.ShowDialog(new Owner(data.Application.MainWindowHandle)) != DialogResult.OK) return Result.Cancelled;
                    if (ui.Document.GetElement(form.ResultView) is View view) ui.ActiveView = view;
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
