using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Commands.MEP.MepCheck;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class CmdMepCheck : IExternalCommand
    {
        private static readonly List<WinForms.Form> OpenResultForms = new List<WinForms.Form>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View view = doc.ActiveView;

            try
            {
                ElementId preselectedOutletId = uiDoc.Selection.GetElementIds().FirstOrDefault() ?? ElementId.InvalidElementId;
                using (var optionsForm = new MepCheckOptionsForm(doc, preselectedOutletId))
                {
                    if (optionsForm.ShowDialog() != WinForms.DialogResult.OK)
                    {
                        return Result.Cancelled;
                    }

                    MepCheckOptions options = optionsForm.Options;
                    MepCheckResult result = new MepCheckService().Run(doc, view, options);
                    var navigationHandler = new MepCheckNavigationHandler(doc, view.Id, options);
                    ExternalEvent navigationEvent = ExternalEvent.Create(navigationHandler);
                    var resultForm = new MepCheckResultsForm(result, navigationHandler, navigationEvent);
                    navigationHandler.Attach(resultForm);
                    OpenResultForms.Add(resultForm);
                    resultForm.FormClosed += (s, e) => OpenResultForms.Remove(resultForm);
                    resultForm.Show();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MEP 檢查", $"執行 MEP 檢查時發生錯誤：\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
