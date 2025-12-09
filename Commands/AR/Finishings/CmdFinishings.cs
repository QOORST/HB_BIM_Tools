using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Text;
using YD_RevitTools.LicenseManager.Helpers.AR.Finishings;
using YD_RevitTools.LicenseManager.UI.Finishings;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings
{
    [Transaction(TransactionMode.Manual)]
    public class CmdFinishings : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 裝修生成功能
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Finishings.Generate"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援裝修生成功能。\n\n" +
                        "請升級至試用版、標準版或專業版以使用此功能。\n\n" +
                        "點擊「授權管理」按鈕以查看或更新授權。");
                    return Result.Cancelled;
                }

                // 清空之前的日誌
                Logger.ClearLog();
                Logger.Log("=== AR_Finishings 執行開始 ===");

                var uiApp = commandData.Application;
                var uiDoc = uiApp.ActiveUIDocument;
                var doc = uiDoc.Document;

                if (uiDoc == null || doc == null)
                {
                    message = "無法取得有效的 Revit 文件";
                    return Result.Failed;
                }

                var win = new YD_RevitTools.LicenseManager.UI.Finishings.MainWindow(uiDoc);
                win.ShowDialog();
                
                if (win.ViewModel == null) 
                    return Result.Cancelled;

                return ExecuteOperation(uiDoc, win.ViewModel, ref message);
            }
            catch (Exception ex)
            {
                message = $"執行失敗: {ex.Message}";
                TaskDialog.Show("錯誤", $"AR Finishings 執行時發生錯誤:\n{ex.Message}\n\n詳細資訊:\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        private Result ExecuteOperation(UIDocument uiDoc, FinishSettings settings, ref string message)
        {
            if (uiDoc == null || settings == null)
            {
                message = "無效的輸入參數";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var errors = new StringBuilder();
            var operationCount = 0;
            var successCount = 0;

            using (var t = new Transaction(doc, "AR Finishings"))
            {
                try
                {
                    var status = t.Start();
                    if (status != TransactionStatus.Started)
                    {
                        message = "無法啟動交易";
                        return Result.Failed;
                    }

                    var writer = new ValueWriter(uiDoc);

                    try
                    {
                        writer.EnsureSharedParameters();
                    }
                    catch (Exception ex)
                    {
                        errors.AppendLine($"建立共享參數失敗: {ex.Message}");
                        Logger.Log($"建立共享參數異常: {ex.Message}\n{ex.StackTrace}");
                    }

                    // 如果只是要自動接合牆面
                    if (settings.AutoJoinWalls)
                    {
                        var generator = new GeometryGenerator(uiDoc);
                        var joinResults = generator.AutoJoinExistingWalls(settings.TargetRoomIds);
                        operationCount += joinResults.TotalAttempts;
                        successCount += joinResults.SuccessCount;
                        
                        if (joinResults.Errors.Any())
                        {
                            errors.AppendLine("自動接合牆面時發生的錯誤:");
                            foreach (var error in joinResults.Errors)
                                errors.AppendLine($"  - {error}");
                        }
                        
                        message = $"自動接合完成: 成功 {joinResults.SuccessCount}/{joinResults.TotalAttempts} 次接合";
                    }
                    else if (settings.GenerateGeometry)
                    {
                        operationCount++;
                        try
                        {
                            var gen = new GeometryGenerator(uiDoc);
                            gen.GenerateForRooms(settings);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"產生幾何元素失敗: {ex.Message}");
                        }
                    }

                    if (settings.UpdateValues)
                    {
                        operationCount++;
                        try
                        {
                            writer.UpdateValues(settings);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"更新參數值失敗: {ex.Message}");
                        }
                    }

                    if (operationCount == 0)
                    {
                        t.RollBack();
                        message = "未選擇任何操作";
                        return Result.Cancelled;
                    }

                    var commitStatus = t.Commit();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        message = $"交易提交失敗: {commitStatus}";
                        Logger.Log($"交易提交狀態: {commitStatus}");
                        return Result.Failed;
                    }

                    // 顯示結果摘要
                    var summary = new StringBuilder();
                    summary.AppendLine($"操作完成: {successCount}/{operationCount} 成功");
                    
                    if (errors.Length > 0)
                    {
                        summary.AppendLine("\n錯誤詳情:");
                        summary.Append(errors.ToString());
                    }

                    if (successCount < operationCount)
                    {
                        TaskDialog.Show("部分成功", summary.ToString());
                        return Result.Succeeded; // 部分成功仍返回 Succeeded
                    }
                    else
                    {
                        message = $"所有操作成功完成 ({successCount}/{operationCount})";
                        return Result.Succeeded;
                    }
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    message = $"交易執行失敗: {ex.Message}";
                    throw;
                }
            }
        }
    }
}
