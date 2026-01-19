using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 結構分析命令 - 分析結構並計算模板需求
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdFormworkStructuralAnalysis : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 結構分析功能
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Formwork.StructuralAnalysis"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援結構分析功能。\n\n" +
                        "此功能僅適用於專業版授權。\n\n" +
                        "點擊「授權管理」按鈕以查看或升級授權。");
                    return Result.Cancelled;
                }

                Document doc = commandData.Application.ActiveUIDocument?.Document;

                if (doc == null)
                {
                    message = "無法取得有效的 Revit 文件";
                    return Result.Failed;
                }

                // 收集結構元素
                var structuralElements = CollectStructuralElements(doc);

                if (structuralElements.Count == 0)
                {
                    TaskDialog.Show("結構分析", "專案中沒有找到結構元素。");
                    return Result.Cancelled;
                }

                // 執行分析
                var analysisResult = AnalyzeStructure(doc, structuralElements);

                // 顯示分析結果
                ShowAnalysisResult(analysisResult);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"執行失敗: {ex.Message}";
                TaskDialog.Show("錯誤", $"結構分析時發生錯誤:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 收集結構元素
        /// </summary>
        private List<Element> CollectStructuralElements(Document doc)
        {
            var elements = new List<Element>();

            // 收集柱
            var columns = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .ToElements();
            elements.AddRange(columns);

            // 收集梁
            var beams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToElements();
            elements.AddRange(beams);

            // 收集樓板
            var floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElements();
            elements.AddRange(floors);

            // 收集牆
            var walls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .ToElements();
            elements.AddRange(walls);

            return elements;
        }

        /// <summary>
        /// 分析結構
        /// </summary>
        private StructuralAnalysisResult AnalyzeStructure(Document doc, List<Element> elements)
        {
            var result = new StructuralAnalysisResult();

            foreach (var element in elements)
            {
                var category = element.Category?.Name ?? "未知";

#if REVIT2024 || REVIT2025 || REVIT2026
                var catId = element.Category?.Id.Value ?? 0;
#else
                var catId = element.Category?.Id.IntegerValue ?? 0;
#endif

                switch (catId)
                {
                    case (int)BuiltInCategory.OST_StructuralColumns:
                        result.ColumnCount++;
                        result.ColumnArea += CalculateElementArea(element);
                        break;

                    case (int)BuiltInCategory.OST_StructuralFraming:
                        result.BeamCount++;
                        result.BeamArea += CalculateElementArea(element);
                        break;

                    case (int)BuiltInCategory.OST_Floors:
                        result.FloorCount++;
                        result.FloorArea += CalculateElementArea(element);
                        break;

                    case (int)BuiltInCategory.OST_Walls:
                        result.WallCount++;
                        result.WallArea += CalculateElementArea(element);
                        break;
                }
            }

            // 計算模板需求（假設每平方米需要 X 張模板）
            result.EstimatedFormworkCount = (int)((result.TotalArea / 1.22) * 1.1); // 1.22m² per sheet, 10% waste

            return result;
        }

        /// <summary>
        /// 計算元素面積
        /// </summary>
        private double CalculateElementArea(Element element)
        {
            try
            {
                // TODO: 實作精確的面積計算
                // 這裡先返回簡單的估算值
                var param = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (param != null && param.HasValue)
                {
                    return param.AsDouble();
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 顯示分析結果
        /// </summary>
        private void ShowAnalysisResult(StructuralAnalysisResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 結構分析結果 ===\n");
            sb.AppendLine($"柱：{result.ColumnCount} 個，面積：{result.ColumnArea:F2} m²");
            sb.AppendLine($"梁：{result.BeamCount} 個，面積：{result.BeamArea:F2} m²");
            sb.AppendLine($"樓板：{result.FloorCount} 個，面積：{result.FloorArea:F2} m²");
            sb.AppendLine($"牆：{result.WallCount} 個，面積：{result.WallArea:F2} m²");
            sb.AppendLine($"\n總面積：{result.TotalArea:F2} m²");
            sb.AppendLine($"\n預估模板需求：約 {result.EstimatedFormworkCount} 張");
            sb.AppendLine($"（基於 1.22m²/張，含 10% 損耗）");

            TaskDialog.Show("結構分析結果", sb.ToString());
        }
    }

    /// <summary>
    /// 結構分析結果
    /// </summary>
    public class StructuralAnalysisResult
    {
        public int ColumnCount { get; set; }
        public double ColumnArea { get; set; }

        public int BeamCount { get; set; }
        public double BeamArea { get; set; }

        public int FloorCount { get; set; }
        public double FloorArea { get; set; }

        public int WallCount { get; set; }
        public double WallArea { get; set; }

        public double TotalArea => ColumnArea + BeamArea + FloorArea + WallArea;
        public int EstimatedFormworkCount { get; set; }
    }
}

