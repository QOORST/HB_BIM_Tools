using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdAiAssistant : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string modelSummary = AiRevitContextBuilder.Build(commandData);
                using (var form = new AiAssistantForm(commandData, modelSummary))
                {
                    form.ShowDialog();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("AI 助理", $"AI 助理啟動失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
