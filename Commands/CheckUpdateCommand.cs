using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Threading.Tasks;
using YD_RevitTools.LicenseManager.Services;

namespace YD_RevitTools.LicenseManager.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CheckUpdateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var updateService = UpdateService.Instance;
                UpdateCheckResult result = null;
                Exception taskException = null;

                var checkTask = Task.Run(async () =>
                {
                    try { result = await updateService.CheckForUpdatesAsync(); }
                    catch (Exception ex) { taskException = ex; }
                });

                if (!checkTask.Wait(TimeSpan.FromSeconds(15)))
                {
                    TaskDialog.Show("檢查更新", "檢查更新逾時，請稍後再試。");
                    return Result.Cancelled;
                }

                if (taskException != null)
                {
                    TaskDialog.Show("檢查更新", $"檢查更新失敗：\n{taskException.Message}");
                    return Result.Failed;
                }

                if (result == null || !result.Success)
                {
                    TaskDialog.Show("檢查更新", result?.Message ?? "檢查更新失敗。");
                    return Result.Failed;
                }

                if (!result.HasUpdate)
                {
                    TaskDialog.Show("檢查更新",
                        $"目前已是最新版本。\n\n目前版本：{result.CurrentVersion}\n最新版本：{result.LatestVersion}");
                    return Result.Succeeded;
                }

                var td = new TaskDialog("發現新版本");
                td.MainInstruction = $"可更新至 {result.LatestVersion}";
                td.MainContent =
                    $"目前版本：{result.CurrentVersion}\n" +
                    $"最新版本：{result.LatestVersion}\n" +
                    $"發佈日期：{result.ReleaseDate:yyyy-MM-dd}\n\n" +
                    $"更新內容：\n{result.ReleaseNotes}\n\n" +
                    "按「是」後會先下載更新檔。\n" +
                    "安裝程式會在您關閉 Revit 後自動啟動，不需先手動關閉。";
                td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                td.DefaultButton = TaskDialogResult.Yes;
                if (td.Show() != TaskDialogResult.Yes)
                {
                    return Result.Cancelled;
                }

                bool downloadSuccess = false;
                var downloadTask = Task.Run(async () =>
                {
                    var progress = new Progress<int>(_ => { });
                    downloadSuccess = await updateService.DownloadAndInstallUpdateAsync(result.DownloadUrl, progress);
                });

                if (!downloadTask.Wait(TimeSpan.FromMinutes(8)))
                {
                    TaskDialog.Show("更新下載", "下載逾時，請稍後再試。");
                    return Result.Failed;
                }

                if (!downloadSuccess)
                {
                    TaskDialog.Show("更新下載", "下載或啟動更新程序失敗。");
                    return Result.Failed;
                }

                TaskDialog.Show("更新已排程",
                    "更新檔已下載完成。\n\n當您關閉 Revit 後，安裝程式將自動啟動。");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("檢查更新", $"執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
