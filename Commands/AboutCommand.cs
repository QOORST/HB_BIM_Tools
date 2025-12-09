// Commands/AboutCommand.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Reflection;
using YD_RevitTools.LicenseManager.Services;

namespace YD_RevitTools.LicenseManager.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AboutCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // 取得版本資訊
                Assembly assembly = Assembly.GetExecutingAssembly();
                Version version = assembly.GetName().Version;
                string assemblyVersion = $"{version.Major}.{version.Minor}.{version.Build}";

                // 取得授權資訊
                var licenseManager = LicenseManager.Instance;
                var validationResult = licenseManager.ValidateLicense();

                string licenseInfo = "";
                if (validationResult.IsValid)
                {
                    licenseInfo = $"授權類型：{validationResult.LicenseInfo.LicenseType}\n" +
                                 $"授權用戶：{validationResult.LicenseInfo.UserName}\n" +
                                 $"到期日期：{validationResult.LicenseInfo.ExpiryDate:yyyy-MM-dd}\n" +
                                 $"剩餘天數：{validationResult.DaysUntilExpiry} 天";
                }
                else
                {
                    licenseInfo = "未授權或授權已過期";
                }

                // 顯示關於對話框
                TaskDialog td = new TaskDialog("關於 YD_BIM 工具");
                td.MainInstruction = "YD_BIM 工具";
                td.MainContent = $"版本：{assemblyVersion}\n\n" +
                                $"{licenseInfo}\n\n" +
                                $"開發團隊：YD_BIM Tools Team\n" +
                                $"網站：www.ydbim.com\n" +
                                $"技術支援：qoorst123@yesdir.com.tw\n\n" +
                                $"© 2025 YD_BIM Owen. All rights reserved.";

                // 添加「檢查更新」按鈕
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "檢查更新", "檢查是否有新版本可用");
                td.CommonButtons = TaskDialogCommonButtons.Close;
                td.DefaultButton = TaskDialogResult.Close;

                TaskDialogResult result = td.Show();

                // 如果使用者點擊「檢查更新」
                if (result == TaskDialogResult.CommandLink1)
                {
                    // 執行檢查更新命令
                    var checkUpdateCmd = new CheckUpdateCommand();
                    checkUpdateCmd.Execute(commandData, ref message, elements);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", $"顯示關於資訊失敗：{ex.Message}");
                return Result.Failed;
            }
        }
    }
}

