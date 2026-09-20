using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System;

namespace YD_RevitTools.LicenseManager.Commands.Family
{
    [Transaction(TransactionMode.Manual)]
    public class CmdFamilyParameterSlider : IExternalCommand
    {
        private static MainWindow _openWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 族參數滑桿功能
                var licenseManager = LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Family.ParameterSlider"))
                {
                    TaskDialog.Show("授權限制", "您的授權不包含族參數滑桿，請於「授權管理」查看授權。 ");
                    return Result.Cancelled;
                }

                Document doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { TaskDialog.Show("族參數滑桿", "請先開啟族群文件。 "); return Result.Cancelled; }
                if (!doc.IsFamilyDocument)
                {
                    TaskDialog.Show("族參數滑桿", "請在族群編輯器中使用此工具。 ");
                    return Result.Failed;
                }

                if (_openWindow != null)
                {
                    if (_openWindow.IsVisible)
                    {
                        _openWindow.Activate();
                        return Result.Succeeded;
                    }
                    _openWindow = null;
                }

                var win = new MainWindow(commandData);
                _openWindow = win;
                win.Closed += (_, __) => _openWindow = null;
                win.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("族參數滑桿", $"執行失敗：{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
