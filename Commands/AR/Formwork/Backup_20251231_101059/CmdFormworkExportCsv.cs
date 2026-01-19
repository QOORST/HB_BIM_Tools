using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 匯出模板數量到CSV命令
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdFormworkExportCsv : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 匯出CSV功能
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Formwork.ExportCsv"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援匯出CSV功能。\n\n" +
                        "此功能僅適用於標準版和專業版授權。\n\n" +
                        "點擊「授權管理」按鈕以查看或升級授權。");
                    return Result.Cancelled;
                }

                Document doc = commandData.Application.ActiveUIDocument?.Document;

                if (doc == null)
                {
                    message = "無法取得有效的 Revit 文件";
                    return Result.Failed;
                }

                // TODO: 收集模板元素
                var formworkElements = CollectFormworkElements(doc);

                if (formworkElements.Count == 0)
                {
                    TaskDialog.Show("匯出CSV", "專案中沒有找到模板元素。");
                    return Result.Cancelled;
                }

                // 統計模板數量
                var statistics = CalculateFormworkStatistics(doc, formworkElements);

                // 選擇儲存位置
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV 檔案 (*.csv)|*.csv",
                    Title = "匯出模板數量統計",
                    FileName = $"模板數量統計_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                {
                    return Result.Cancelled;
                }

                // 匯出CSV
                ExportToCsv(saveFileDialog.FileName, statistics);

                // 顯示結果
                TaskDialog.Show("匯出完成",
                    $"模板數量統計已成功匯出！\n\n" +
                    $"檔案位置：\n{saveFileDialog.FileName}\n\n" +
                    $"模板總數：{formworkElements.Count}");

                // 詢問是否開啟檔案
                var openResult = TaskDialog.Show("開啟檔案",
                    "是否要開啟匯出的CSV檔案？",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (openResult == TaskDialogResult.Yes)
                {
                    System.Diagnostics.Process.Start(saveFileDialog.FileName);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"執行失敗: {ex.Message}";
                TaskDialog.Show("錯誤", $"匯出CSV時發生錯誤:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 收集模板元素
        /// </summary>
        private List<Element> CollectFormworkElements(Document doc)
        {
            // TODO: 實作模板元素收集邏輯
            // 這裡需要定義如何識別模板元素
            return new List<Element>();
        }

        /// <summary>
        /// 計算模板統計資料
        /// </summary>
        private Dictionary<string, FormworkStatistics> CalculateFormworkStatistics(Document doc, List<Element> formworkElements)
        {
            var statistics = new Dictionary<string, FormworkStatistics>();

            foreach (var element in formworkElements)
            {
                // TODO: 實作統計邏輯
                // 可以按類型、樓層、區域等分類統計
                string category = element.Category?.Name ?? "未分類";

                if (!statistics.ContainsKey(category))
                {
                    statistics[category] = new FormworkStatistics
                    {
                        Category = category,
                        Count = 0,
                        TotalArea = 0,
                        TotalVolume = 0
                    };
                }

                statistics[category].Count++;
                // TODO: 計算面積和體積
            }

            return statistics;
        }

        /// <summary>
        /// 匯出到CSV檔案
        /// </summary>
        private void ExportToCsv(string filePath, Dictionary<string, FormworkStatistics> statistics)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                // 寫入標題
                writer.WriteLine("類別,數量,總面積(m²),總體積(m³)");

                // 寫入資料
                foreach (var stat in statistics.Values.OrderBy(s => s.Category))
                {
                    writer.WriteLine($"{stat.Category},{stat.Count},{stat.TotalArea:F2},{stat.TotalVolume:F3}");
                }

                // 寫入總計
                writer.WriteLine();
                writer.WriteLine($"總計,{statistics.Values.Sum(s => s.Count)},{statistics.Values.Sum(s => s.TotalArea):F2},{statistics.Values.Sum(s => s.TotalVolume):F3}");
            }
        }
    }

    /// <summary>
    /// 模板統計資料
    /// </summary>
    public class FormworkStatistics
    {
        public string Category { get; set; }
        public int Count { get; set; }
        public double TotalArea { get; set; }
        public double TotalVolume { get; set; }
    }
}

