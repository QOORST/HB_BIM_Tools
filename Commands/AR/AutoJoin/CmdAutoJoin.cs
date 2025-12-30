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

            // 建立結果訊息
            string resultMessage =
                $"檢查配對：{report.PairsChecked}\n" +
                $"新接合：{report.Joined}\n" +
                $"切換順序：{report.Switched}\n" +
                $"原已接合：{report.Already}\n" +
                $"失敗/略過：{report.Failed}";

            // 如果有近距離元素，加入提示
            if (report.NearMisses > 0)
            {
                resultMessage += $"\n\n⚠️ 近距離元素：{report.NearMisses} 組";
                resultMessage += "\n（接近但未相交，可能需要調整輪廓）";
            }

            // 顯示結果
            var td = new TaskDialog("YD_BIM - 結構自動接合")
            {
                MainInstruction = "執行完成",
                MainContent = resultMessage
            };

            // 如果有近距離元素，提供查看詳情的按鈕
            if (report.NearMisses > 0)
            {
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "查看近距離元素清單",
                    "顯示接近但未相交的元素，可直接跳轉選取");
                td.CommonButtons = TaskDialogCommonButtons.Close;
                td.DefaultButton = TaskDialogResult.CommandLink1;
            }
            else
            {
                td.CommonButtons = TaskDialogCommonButtons.Ok;
            }

            var result = td.Show();

            // 如果使用者選擇查看近距離元素
            if (result == TaskDialogResult.CommandLink1 && report.NearMissList.Count > 0)
            {
                var nearMissWin = new NearMissResultWindow(uidoc, report.NearMissList);
                nearMissWin.ShowDialog();
            }

            return Result.Succeeded;
        }
    }
}
