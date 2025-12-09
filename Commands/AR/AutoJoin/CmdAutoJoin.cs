using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin;
using YD_RevitTools.LicenseManager.UI.AutoJoin;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoJoin : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // 檢查授權 - 自動接合功能
            var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("AutoJoin"))
            {
                TaskDialog.Show("授權限制",
                    "您的授權版本不支援自動接合功能。\n\n" +
                    "請升級至試用版、標準版或專業版以使用此功能。\n\n" +
                    "點擊「授權管理」按鈕以查看或更新授權。");
                return Result.Cancelled;
            }

            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                message = "請在專案文件中執行。";
                return Result.Failed;
            }

            var settings = new AutoJoinSettings();
            var win = new AutoJoinWindow(settings, uidoc);
            if (win.ShowDialog() != true) return Result.Cancelled;

            var engine = new AutoJoinEngine();
            var report = engine.Run(doc, uidoc, settings);

            TaskDialog.Show("YD_BIM - 結構自動接合",
                $"檢查配對：{report.PairsChecked}\n" +
                $"新接合：{report.Joined}\n" +
                $"切換順序：{report.Switched}\n" +
                $"原已接合：{report.Already}\n" +
                $"失敗/略過：{report.Failed}");

            return Result.Succeeded;
        }
    }
}
