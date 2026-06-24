using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
#if !REVIT2025 && !REVIT2026
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO;
#endif

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
#if REVIT2025 || REVIT2026
                TaskDialog.Show("管線轉 ISO 圖",
                    "PipeToISO 在 Revit 2026 版本的主流程已保留，但視圖/設定介面仍在轉換中。\n\n本次上線先採安全提示模式，避免使用者誤觸後失敗。");
                return Result.Cancelled;
#else
                // 調用實際的 PipeToISO 命令
                var pipeToISOCommand = new PipeToISOCommand();
                return pipeToISOCommand.Execute(commandData, ref message, elements);
#endif
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

