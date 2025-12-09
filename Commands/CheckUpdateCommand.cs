// Commands/CheckUpdateCommand.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Threading.Tasks;
using YD_RevitTools.LicenseManager.Services;
using YD_RevitTools.LicenseManager.UI;

namespace YD_RevitTools.LicenseManager.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CheckUpdateCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // 在背景執行檢查
                var updateService = UpdateService.Instance;
                UpdateCheckResult result = null;
                Exception taskException = null;

                // 使用 Task 來執行異步操作
                var checkTask = Task.Run(async () =>
                {
                    try
                    {
                        result = await updateService.CheckForUpdatesAsync();
                    }
                    catch (Exception ex)
                    {
                        taskException = ex;
                    }
                });

                // 等待檢查完成（最多 15 秒）
                if (!checkTask.Wait(TimeSpan.FromSeconds(15)))
                {
                    TaskDialog.Show("檢查更新",
                        "檢查更新超時，請檢查網路連線後重試。\n\n" +
                        "可能的原因：\n" +
                        "• 網路連線不穩定\n" +
                        "• 無法訪問 GitHub\n" +
                        "• 防火牆阻擋連線");
                    return Result.Cancelled;
                }

                // 檢查是否有異常
                if (taskException != null)
                {
                    string errorDetails = GetDetailedErrorMessage(taskException);
                    TaskDialog.Show("檢查更新失敗",
                        $"檢查更新時發生錯誤：\n\n{errorDetails}\n\n" +
                        "請檢查網路連線或稍後再試。");
                    return Result.Failed;
                }

                // 檢查結果
                if (result == null || !result.Success)
                {
                    string errorMsg = result?.Message ?? "未知錯誤";
                    TaskDialog.Show("檢查更新失敗",
                        $"無法檢查更新：\n\n{errorMsg}\n\n" +
                        "請檢查網路連線或稍後再試。");
                    return Result.Failed;
                }

                // 顯示結果
                if (result.HasUpdate)
                {
                    // 有新版本可用
                    ShowUpdateAvailableDialog(result);
                }
                else
                {
                    // 已是最新版本
                    TaskDialog td = new TaskDialog("檢查更新");
                    td.MainInstruction = "您已使用最新版本";
                    td.MainContent = $"當前版本：{result.CurrentVersion}\n" +
                                    $"最新版本：{result.LatestVersion}\n\n" +
                                    "無需更新。";
                    td.CommonButtons = TaskDialogCommonButtons.Ok;
                    td.Show();
                }

                return Result.Succeeded;
            }
            catch (AggregateException aex)
            {
                // 處理 Task 的聚合異常
                string errorDetails = GetDetailedErrorMessage(aex);
                TaskDialog.Show("YD_BIM Tools - 錯誤",
                    $"檢查更新時發生錯誤：\n\n{errorDetails}");
                return Result.Failed;
            }
            catch (Exception ex)
            {
                string errorDetails = GetDetailedErrorMessage(ex);
                TaskDialog.Show("YD_BIM Tools - 錯誤",
                    $"檢查更新時發生錯誤：\n\n{errorDetails}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 顯示有更新可用的對話框
        /// </summary>
        private void ShowUpdateAvailableDialog(UpdateCheckResult result)
        {
            TaskDialog td = new TaskDialog("發現新版本");
            td.MainInstruction = $"發現新版本 {result.LatestVersion}";
            td.MainContent = $"當前版本：{result.CurrentVersion}\n" +
                            $"最新版本：{result.LatestVersion}\n" +
                            $"發布日期：{result.ReleaseDate:yyyy-MM-dd}\n\n" +
                            $"更新內容：\n{result.ReleaseNotes}\n\n" +
                            "是否要下載並安裝更新？";

            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            td.DefaultButton = TaskDialogResult.Yes;

            TaskDialogResult dialogResult = td.Show();

            if (dialogResult == TaskDialogResult.Yes)
            {
                // 使用者選擇更新
                DownloadAndInstallUpdate(result);
            }
        }

        /// <summary>
        /// 下載並安裝更新
        /// </summary>
        private void DownloadAndInstallUpdate(UpdateCheckResult updateInfo)
        {
            try
            {
                // 顯示進度對話框
                TaskDialog progressDialog = new TaskDialog("下載更新");
                progressDialog.MainInstruction = "正在下載更新...";
                progressDialog.MainContent = "請稍候，正在下載安裝程式。\n\n" +
                                            "下載完成後將自動啟動安裝程式。\n" +
                                            "請關閉 Revit 後繼續安裝。";
                progressDialog.CommonButtons = TaskDialogCommonButtons.None;

                var updateService = UpdateService.Instance;
                bool downloadSuccess = false;

                // 執行下載
                var downloadTask = Task.Run(async () =>
                {
                    var progress = new Progress<int>(percent =>
                    {
                        // 更新進度（在實際應用中可以使用進度條）
                        System.Diagnostics.Debug.WriteLine($"下載進度：{percent}%");
                    });

                    downloadSuccess = await updateService.DownloadAndInstallUpdateAsync(
                        updateInfo.DownloadUrl, 
                        progress);
                });

                // 等待下載完成（最多 5 分鐘）
                if (!downloadTask.Wait(TimeSpan.FromMinutes(5)))
                {
                    TaskDialog.Show("下載超時", "下載更新超時，請稍後重試。");
                    return;
                }

                if (downloadSuccess)
                {
                    TaskDialog successDialog = new TaskDialog("準備安裝");
                    successDialog.MainInstruction = "更新下載完成";
                    successDialog.MainContent = "安裝程式已準備就緒。\n\n" +
                                               "請關閉 Revit 後，安裝程式將自動啟動。\n\n" +
                                               "安裝完成後，請重新啟動 Revit。";
                    successDialog.CommonButtons = TaskDialogCommonButtons.Ok;
                    successDialog.Show();
                }
                else
                {
                    TaskDialog.Show("下載失敗", "下載更新失敗，請稍後重試或手動下載安裝。");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"下載更新時發生錯誤：\n\n{ex.Message}");
            }
        }

        /// <summary>
        /// 獲取詳細的錯誤訊息
        /// </summary>
        private string GetDetailedErrorMessage(Exception ex)
        {
            if (ex is AggregateException aex)
            {
                // 處理聚合異常，獲取內部異常
                var innerEx = aex.InnerException ?? aex;
                return GetDetailedErrorMessage(innerEx);
            }
            else if (ex is System.Net.Http.HttpRequestException)
            {
                return $"網路連線失敗\n\n" +
                       $"錯誤：{ex.Message}\n\n" +
                       $"可能的原因：\n" +
                       $"• 無法訪問 GitHub\n" +
                       $"• 網路連線不穩定\n" +
                       $"• 防火牆阻擋連線\n" +
                       $"• DNS 解析失敗";
            }
            else if (ex is TaskCanceledException || ex is TimeoutException)
            {
                return $"連線超時\n\n" +
                       $"請檢查網路連線後重試。";
            }
            else if (ex is System.Text.Json.JsonException)
            {
                return $"版本資訊格式錯誤\n\n" +
                       $"錯誤：{ex.Message}\n\n" +
                       $"請聯繫技術支援。";
            }
            else
            {
                return $"{ex.GetType().Name}\n\n{ex.Message}";
            }
        }
    }
}

