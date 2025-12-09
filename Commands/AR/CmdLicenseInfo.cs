using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager;
using YD_RevitTools.LicenseManager.Helpers.AR;

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
                // 使用統一的授權管理系統顯示授權資訊
                var licenseManager = LicenseManager.Instance;
                var licenseInfo = licenseManager.GetCurrentLicense();
                var validationResult = licenseManager.ValidateLicense();

                TaskDialog td = new TaskDialog("授權資訊");
                td.MainInstruction = "YD BIM 工具授權資訊";

                if (licenseInfo != null)
                {
                    td.MainContent = $"授權類型：{licenseInfo.LicenseType}\n" +
                                    $"用戶名稱：{licenseInfo.UserName}\n" +
                                    $"到期日期：{licenseInfo.ExpiryDate:yyyy-MM-dd}\n" +
                                    $"剩餘天數：{validationResult.DaysUntilExpiry} 天\n\n" +
                                    $"授權狀態：{(validationResult.IsValid ? "有效" : "無效")}\n" +
                                    $"{(validationResult.IsValid ? "" : $"錯誤訊息：{validationResult.Message}")}";
                }
                else
                {
                    td.MainContent = "未找到授權資訊\n\n請聯絡管理員進行授權。";
                }

                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"顯示授權資訊時發生錯誤：{ex.Message}";
                return Result.Failed;
            }
        }
    }
}

