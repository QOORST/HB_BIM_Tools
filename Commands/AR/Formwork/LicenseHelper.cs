// ============================================================
// AR_Formwork 授權檢查輔助類別
// 用途: 提供統一的授權檢查方法給所有命令使用
// 使用方式: 在每個命令的 Execute 方法開頭呼叫
// ============================================================

using System;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 授權檢查輔助類別
    /// </summary>
    public static class LicenseHelper
    {
        /// <summary>
        /// 檢查授權並顯示適當的訊息
        /// </summary>
        /// <param name="featureName">功能代碼 (例如: "Formwork.Generate")</param>
        /// <param name="featureDisplayName">功能顯示名稱 (例如: "模板生成")</param>
        /// <param name="requiredLicense">所需授權等級</param>
        /// <returns>true 表示可以使用，false 表示無權限</returns>
        public static bool CheckLicense(string featureName, string featureDisplayName, LicenseType requiredLicense)
        {
            var licenseManager = LicenseManager.Instance;
            var validation = licenseManager.ValidateLicense();

            // 檢查授權是否有效
            if (!validation.IsValid)
            {
                ShowLicenseError(validation.Message);
                return false;
            }

            // 檢查功能權限
            if (!licenseManager.HasFeatureAccess(featureName))
            {
                ShowFeatureRestriction(featureDisplayName, requiredLicense, validation.LicenseInfo.LicenseType);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 顯示授權錯誤訊息
        /// </summary>
        private static void ShowLicenseError(string message)
        {
            TaskDialog td = new TaskDialog("授權錯誤")
            {
                MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                MainInstruction = "授權未啟用或已過期",
                MainContent = message + "\n\n請點擊「YD BIM 工具」頁籤中的「授權管理」按鈕進行啟用。",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            td.Show();
        }

        /// <summary>
        /// 顯示功能限制訊息
        /// </summary>
        private static void ShowFeatureRestriction(string featureName, LicenseType requiredLicense, LicenseType currentLicense)
        {
            string requiredLicenseName = GetLicenseLevelName(requiredLicense);
            string currentLicenseName = GetLicenseLevelName(currentLicense);

            TaskDialog td = new TaskDialog("功能限制")
            {
                MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                MainInstruction = $"此功能需要「{requiredLicenseName}」或更高等級的授權",
                MainContent = $"功能名稱: {featureName}\n" +
                             $"您目前的授權: {currentLicenseName}\n" +
                             $"所需授權: {requiredLicenseName}\n\n" +
                             "請聯繫管理員升級授權。",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            td.Show();
        }

        /// <summary>
        /// 取得授權等級名稱
        /// </summary>
        public static string GetLicenseLevelName(LicenseType licenseType)
        {
            switch (licenseType)
            {
                case LicenseType.Trial:
                    return "試用版";
                case LicenseType.Standard:
                    return "標準版";
                case LicenseType.Professional:
                    return "專業版";
                default:
                    return "未知";
            }
        }

        /// <summary>
        /// 取得當前授權資訊摘要
        /// </summary>
        public static string GetLicenseSummary()
        {
            var licenseManager = LicenseManager.Instance;
            var validation = licenseManager.ValidateLicense();

            if (!validation.IsValid)
            {
                return "未授權";
            }

            var license = validation.LicenseInfo;
            var daysRemaining = (license.ExpiryDate - DateTime.Now).Days;

            return $"{license.GetLicenseTypeName()} - 剩餘 {daysRemaining} 天";
        }

        /// <summary>
        /// 顯示授權資訊對話框
        /// </summary>
        public static void ShowLicenseInfo()
        {
            var licenseManager = LicenseManager.Instance;
            var validation = licenseManager.ValidateLicense();

            TaskDialog dialog = new TaskDialog("授權資訊 - YD BIM Tools");

            if (!validation.IsValid)
            {
                dialog.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
                dialog.MainInstruction = "授權未啟用或已過期";
                dialog.MainContent = validation.Message + "\n\n請點擊「YD BIM 工具」頁籤中的「授權管理」按鈕進行啟用。";
            }
            else
            {
                var license = validation.LicenseInfo;
                var daysRemaining = (license.ExpiryDate - DateTime.Now).Days;

                dialog.MainIcon = TaskDialogIcon.TaskDialogIconInformation;
                dialog.MainInstruction = "授權資訊";
                dialog.MainContent = $"授權狀態:  已啟用\n\n" +
                                   $"授權類型: {license.GetLicenseTypeName()}\n" +
                                   $"使用者: {license.UserName}\n" +
                                   $"公司: {license.Company}\n" +
                                   $"開始日期: {license.StartDate:yyyy-MM-dd}\n" +
                                   $"到期日期: {license.ExpiryDate:yyyy-MM-dd}\n" +
                                   $"剩餘天數: {daysRemaining} 天\n\n" +
                                   $"\n\n" +
                                   $"可用功能：\n\n";

                // 根據授權類型顯示可用功能
                switch (license.LicenseType)
                {
                    case LicenseType.Trial:
                        dialog.MainContent += "試用版功能（2個）：\n";
                        dialog.MainContent += "   模板生成\n";
                        dialog.MainContent += "   刪除模板\n";
                        break;

                    case LicenseType.Standard:
                        dialog.MainContent += "標準版功能（4個）：\n";
                        dialog.MainContent += "   模板生成\n";
                        dialog.MainContent += "   面生面\n";
                        dialog.MainContent += "   刪除模板\n";
                        dialog.MainContent += "   匯出CSV\n";
                        break;

                    case LicenseType.Professional:
                        dialog.MainContent += "專業版功能（全部）：\n";
                        dialog.MainContent += "   模板生成\n";
                        dialog.MainContent += "   面生面\n";
                        dialog.MainContent += "   刪除模板\n";
                        dialog.MainContent += "   結構分析\n";
                        dialog.MainContent += "   匯出CSV\n";
                        dialog.MainContent += "   智能模板\n";
                        dialog.MainContent += "   測試改進引擎\n";
                        break;
                }
            }

            dialog.CommonButtons = TaskDialogCommonButtons.Ok;
            dialog.Show();
        }
    }
}