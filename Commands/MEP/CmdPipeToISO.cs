using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    /// <summary>
    /// 管線轉 ISO 圖命令包裝器
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdPipeToISO : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // 調用實際的 PipeToISO 命令
                var pipeToISOCommand = new PipeToISOCommand();
                return pipeToISOCommand.Execute(commandData, ref message, elements);
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", $"管線轉 ISO 圖執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}

