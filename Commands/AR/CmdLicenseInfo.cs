using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR
{
    /// <summary>
    /// 顯示授權資訊命令
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdLicenseInfo : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 開啟授權管理視窗
                var window = new LicenseWindow();

                // 設定視窗的擁有者為 Revit 主視窗
                try
                {
                    var mainWindowHandle = commandData.Application.MainWindowHandle;
                    if (mainWindowHandle != IntPtr.Zero)
                    {
                        var helper = new WindowInteropHelper(window);
                        helper.Owner = mainWindowHandle;
                    }
                }
                catch
                {
                    // 如果設定擁有者失敗，忽略錯誤繼續顯示視窗
                }

                // 顯示視窗
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"顯示授權管理視窗時發生錯誤：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}

