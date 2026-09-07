﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;
using Microsoft.Win32;
using YD_RevitTools.LicenseManager;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdExportCsv : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet set)
        {
            var doc = data.Application.ActiveUIDocument.Document;

            try
            {
                // 授權檢查
                if (!LicenseHelper.CheckLicense("ExportCSV", "匯出CSV", LicenseType.Professional))
                {
                    return Result.Cancelled;
                }

                // 使用新的結構分析系統進行完整分析
                FormworkEngine.Debug.Enable(true);
                FormworkEngine.BeginRun();

                var analysisResult = StructuralFormworkAnalyzer.AnalyzeProject(doc);

                FormworkEngine.EndRun();

                if (analysisResult.ElementAnalyses.Count == 0)
                {
                    TaskDialog.Show("匯出", "沒有找到可分析的結構元素。");
                    return Result.Succeeded;
                }

                // 儲存位置
                var sfd = new SaveFileDialog
                {
                    Title = "匯出準確模板分析結果 (Excel)",
                    Filter = "Excel 檔案 (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
                    FileName = $"AccurateFormwork_Report_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                    DefaultExt = "xlsx"
                };
                if (sfd.ShowDialog() != true) return Result.Cancelled;

                var extension = Path.GetExtension(sfd.FileName)?.ToLowerInvariant();
                var detailDataList = CollectDetailData(doc, analysisResult);

                if (extension == ".csv")
                {
                    using (var sw = new StreamWriter(sfd.FileName, false, new System.Text.UTF8Encoding(true)))
                    {
                        WriteHeader(sw);
                        WriteSummary(sw, analysisResult, detailDataList);
                        WriteDetailData(sw, detailDataList);
                    }
                }
                else
                {
                    ExportToExcel(sfd.FileName, analysisResult, detailDataList, doc.ActiveView?.Name);
                }

                TaskDialog.Show("匯出完成", 
                    $"已匯出 {analysisResult.ElementAnalyses.Count} 個結構元素的準確分析結果到：\n{sfd.FileName}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"匯出時發生錯誤：{ex.Message}");
                return Result.Failed;
            }
        }

        private void WriteHeader(StreamWriter sw)
        {
            sw.WriteLine("=== BIM 結構模板準確分析報告 ===");
            sw.WriteLine($"分析時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine($"分析系統: Formwork_V1 準確計算系統");
            sw.WriteLine();
        }

        /// <summary>
        /// 收集所有構件的實際模板數據
        /// </summary>
        private class DetailDataItem
        {
            public Element Element { get; set; }
            public ElementFormworkAnalysis Analysis { get; set; }
            public string Name { get; set; }
            public string Level { get; set; }
            public StructuralElementType Type { get; set; }
            public string Formula { get; set; }
            public double ActualFormworkArea { get; set; }
            public int FormworkCount { get; set; }
        }

        private List<DetailDataItem> CollectDetailData(Document doc, StructuralAnalysisResult result)
        {
            var detailDataList = new List<DetailDataItem>();

            foreach (var kvp in result.ElementAnalyses)
            {
                var element = kvp.Key;
                var analysis = kvp.Value;

                // 讀取實際生成的模板有效面積
                var formworkAreaData = GetFormworkAreaFromGeneratedElements(element, analysis);

                detailDataList.Add(new DetailDataItem
                {
                    Element = element,
                    Analysis = analysis,
                    Name = GetElementName(element),
                    Level = GetElementLevel(element),
                    Type = analysis.ElementType,
                    Formula = formworkAreaData.Formula,
                    ActualFormworkArea = formworkAreaData.TotalArea,
                    FormworkCount = formworkAreaData.FormworkCount
                });
            }

            return detailDataList;
        }

        private void WriteSummary(StreamWriter sw, StructuralAnalysisResult result, List<DetailDataItem> detailData)
        {
            // 使用實際模板面積計算總計
            double totalActualArea = detailData.Sum(d => d.ActualFormworkArea);
            int totalFormworkCount = detailData.Sum(d => d.FormworkCount);

            sw.WriteLine("=== 總計統計 ===");
            sw.WriteLine($"分析構件總數,{result.TotalElements}");
            sw.WriteLine($"生成模板總數,{totalFormworkCount}");
            sw.WriteLine($"模板總面積(m²),{totalActualArea:F3}");
            sw.WriteLine($"混凝土總體積(m³),{result.TotalConcreteVolume:F3}");
            sw.WriteLine($"鋼筋估算重量(t),{result.EstimatedRebarWeight:F3}");
            sw.WriteLine();

            sw.WriteLine("=== 分類統計 ===");
            sw.WriteLine("構件類型,數量,模板數量,模板面積(m²),混凝土體積(m³),平均模板面積(m²/構件)");
            
            // 按類型分組統計實際面積
            var categoryStats = detailData
                .GroupBy(d => d.Type)
                .Select(g => new
                {
                    Type = g.Key,
                    Count = g.Count(),
                    FormworkCount = g.Sum(d => d.FormworkCount),
                    FormworkArea = g.Sum(d => d.ActualFormworkArea),
                    ConcreteVolume = g.Sum(d => d.Analysis.ConcreteVolume),
                    AvgArea = g.Sum(d => d.ActualFormworkArea) / g.Count()
                })
                .OrderBy(s => s.Type);

            foreach (var stat in categoryStats)
            {
                string typeName = GetElementTypeDisplayName(stat.Type);
                sw.WriteLine($"{typeName},{stat.Count},{stat.FormworkCount},{stat.FormworkArea:F3},{stat.ConcreteVolume:F3},{stat.AvgArea:F3}");
            }
            sw.WriteLine();
        }

        private void WriteDetailData(StreamWriter sw, List<DetailDataItem> detailData)
        {
            sw.WriteLine("=== 詳細構件分析 ===");
            sw.WriteLine("構件名稱,樓層,類型,構件ID,模板數量,模板面積計算式,模板面積(m²),混凝土體積(m³)");

            // 按樓層、類型、名稱排序
            var sortedData = detailData
                .OrderBy(x => x.Level)
                .ThenBy(x => x.Type)
                .ThenBy(x => x.Name);

            foreach (var item in sortedData)
            {
                string elementType = GetElementTypeDisplayName(item.Type);

                sw.WriteLine($"{Q(item.Name)}," +
                           $"{Q(item.Level)}," +
                           $"{Q(elementType)}," +
                           $"{item.Element.Id.GetIdValue()}," +
                           $"{item.FormworkCount}," +
                           $"{Q(item.Formula)}," +
                           $"{item.ActualFormworkArea:F3}," +
                           $"{item.Analysis.ConcreteVolume:F3}");
            }
        }

        private void ExportToExcel(string filePath, StructuralAnalysisResult result, List<DetailDataItem> detailData, string activeViewName)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var file = new FileInfo(filePath);
            if (file.Exists)
            {
                file.Delete();
            }

            using (var package = new ExcelPackage(file))
            {
                var summarySheet = package.Workbook.Worksheets.Add("總覽");
                var detailSheet = package.Workbook.Worksheets.Add("詳細資料");

                WriteSummaryWorksheet(summarySheet, result, detailData, activeViewName);
                WriteDetailWorksheet(detailSheet, detailData);
                WriteTypeWorksheets(package, detailData);

                package.Save();
            }
        }

        private void WriteSummaryWorksheet(ExcelWorksheet ws, StructuralAnalysisResult result, List<DetailDataItem> detailData, string activeViewName)
        {
            double totalActualArea = detailData.Sum(d => d.ActualFormworkArea);
            int totalFormworkCount = detailData.Sum(d => d.FormworkCount);

            ws.Cells[1, 1].Value = "BIM 結構模板準確分析報告";
            ws.Cells[1, 1, 1, 6].Merge = true;
            ws.Cells[2, 1].Value = "分析時間";
            ws.Cells[2, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cells[3, 1].Value = "分析系統";
            ws.Cells[3, 2].Value = "Formwork_V1 準確計算系統";
            ws.Cells[4, 1].Value = "目前視圖";
            ws.Cells[4, 2].Value = string.IsNullOrWhiteSpace(activeViewName) ? "未指定" : activeViewName;
            ws.Cells[5, 1].Value = "模板掃描範圍";
            ws.Cells[5, 2].Value = "整份模型";

            ws.Cells[7, 1].Value = "總計統計";
            ws.Cells[8, 1].Value = "分析構件總數";
            ws.Cells[8, 2].Value = result.TotalElements;
            ws.Cells[9, 1].Value = "生成模板總數";
            ws.Cells[9, 2].Value = totalFormworkCount;
            ws.Cells[10, 1].Value = "模板總面積 (m2)";
            ws.Cells[10, 2].Value = totalActualArea;
            ws.Cells[11, 1].Value = "混凝土總體積 (m3)";
            ws.Cells[11, 2].Value = result.TotalConcreteVolume;
            ws.Cells[12, 1].Value = "鋼筋估算重量 (t)";
            ws.Cells[12, 2].Value = result.EstimatedRebarWeight;

            ws.Cells[14, 1].Value = "分類統計";
            var categoryHeaders = new[] { "構件類型", "數量", "模板數量", "模板面積 (m2)", "混凝土體積 (m3)", "平均模板面積 (m2/構件)" };
            for (int i = 0; i < categoryHeaders.Length; i++)
            {
                ws.Cells[15, i + 1].Value = categoryHeaders[i];
            }

            var categoryStats = detailData
                .GroupBy(d => d.Type)
                .Select(g => new
                {
                    Type = g.Key,
                    Count = g.Count(),
                    FormworkCount = g.Sum(d => d.FormworkCount),
                    FormworkArea = g.Sum(d => d.ActualFormworkArea),
                    ConcreteVolume = g.Sum(d => d.Analysis.ConcreteVolume),
                    AvgArea = g.Sum(d => d.ActualFormworkArea) / g.Count()
                })
                .OrderBy(s => s.Type)
                .ToList();

            int row = 16;
            foreach (var stat in categoryStats)
            {
                ws.Cells[row, 1].Value = GetElementTypeDisplayName(stat.Type);
                ws.Cells[row, 2].Value = stat.Count;
                ws.Cells[row, 3].Value = stat.FormworkCount;
                ws.Cells[row, 4].Value = stat.FormworkArea;
                ws.Cells[row, 5].Value = stat.ConcreteVolume;
                ws.Cells[row, 6].Value = stat.AvgArea;
                row++;
            }

            StyleTitle(ws.Cells[1, 1, 1, 6]);
            StyleSection(ws.Cells[7, 1, 7, 2]);
            StyleSection(ws.Cells[14, 1, 14, 6]);
            StyleHeader(ws.Cells[15, 1, 15, 6]);
            StyleDataArea(ws.Cells[8, 1, 12, 2]);
            if (row > 16)
            {
                StyleDataArea(ws.Cells[16, 1, row - 1, 6]);
                ws.Cells[15, 1, row - 1, 6].AutoFilter = true;
                AddCategoryChart(ws, row - 1);
            }

            ws.Column(2).Style.Numberformat.Format = "0.000";
            ws.Column(4).Style.Numberformat.Format = "0.000";
            ws.Column(5).Style.Numberformat.Format = "0.000";
            ws.Column(6).Style.Numberformat.Format = "0.000";
            ws.View.FreezePanes(15, 1);
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
        }

        private void WriteDetailWorksheet(ExcelWorksheet ws, List<DetailDataItem> detailData)
        {
            WriteDetailWorksheet(ws, detailData, "詳細構件分析");
        }

        private void WriteDetailWorksheet(ExcelWorksheet ws, List<DetailDataItem> detailData, string title)
        {
            var headers = new[] { "構件名稱", "樓層", "類型", "構件ID", "模板數量", "模板面積計算式", "模板面積 (m2)", "混凝土體積 (m3)" };
            ws.Cells[1, 1].Value = title;
            ws.Cells[1, 1, 1, headers.Length].Merge = true;

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cells[2, i + 1].Value = headers[i];
            }

            var sortedData = detailData
                .OrderBy(x => x.Level)
                .ThenBy(x => x.Type)
                .ThenBy(x => x.Name)
                .ToList();

            int row = 3;
            foreach (var item in sortedData)
            {
                ws.Cells[row, 1].Value = item.Name;
                ws.Cells[row, 2].Value = item.Level;
                ws.Cells[row, 3].Value = GetElementTypeDisplayName(item.Type);
                ws.Cells[row, 4].Value = item.Element.Id.GetIdValue();
                ws.Cells[row, 5].Value = item.FormworkCount;
                ws.Cells[row, 6].Value = item.Formula;
                ws.Cells[row, 7].Value = item.ActualFormworkArea;
                ws.Cells[row, 8].Value = item.Analysis.ConcreteVolume;
                row++;
            }

            StyleTitle(ws.Cells[1, 1, 1, headers.Length]);
            StyleHeader(ws.Cells[2, 1, 2, headers.Length]);
            if (row > 3)
            {
                StyleDataArea(ws.Cells[3, 1, row - 1, headers.Length]);
                ws.Cells[2, 1, row - 1, headers.Length].AutoFilter = true;
            }

            ws.Column(4).Style.Numberformat.Format = "0";
            ws.Column(5).Style.Numberformat.Format = "0";
            ws.Column(7).Style.Numberformat.Format = "0.000";
            ws.Column(8).Style.Numberformat.Format = "0.000";
            ws.View.FreezePanes(3, 1);
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            ws.Column(1).Width = Math.Max(ws.Column(1).Width, 20);
            ws.Column(6).Width = Math.Max(ws.Column(6).Width, 28);
        }

        private void WriteTypeWorksheets(ExcelPackage package, List<DetailDataItem> detailData)
        {
            var groupedData = detailData
                .GroupBy(item => item.Type)
                .OrderBy(group => group.Key)
                .ToList();

            foreach (var group in groupedData)
            {
                var worksheet = package.Workbook.Worksheets.Add(GetWorksheetName(group.Key));
                WriteDetailWorksheet(worksheet, group.ToList(), $"{GetElementTypeDisplayName(group.Key)}詳細資料");
            }
        }

        private void AddCategoryChart(ExcelWorksheet ws, int lastDataRow)
        {
            var chart = ws.Drawings.AddChart("CategoryAreaChart", eChartType.ColumnClustered);
            chart.Title.Text = "各類型模板面積統計";
            chart.SetPosition(1, 0, 7, 0);
            chart.SetSize(720, 320);
            chart.YAxis.Title.Text = "模板面積 (m2)";
            chart.XAxis.Title.Text = "構件類型";
            chart.Legend.Remove();

            var series = chart.Series.Add(ws.Cells[16, 4, lastDataRow, 4], ws.Cells[16, 1, lastDataRow, 1]);
            series.Header = "模板面積";
        }

        private string GetWorksheetName(StructuralElementType type)
        {
            switch (type)
            {
                case StructuralElementType.Beam:
                    return "梁";
                case StructuralElementType.Column:
                    return "柱";
                case StructuralElementType.Slab:
                    return "板";
                case StructuralElementType.Wall:
                    return "牆";
                case StructuralElementType.Foundation:
                    return "基礎";
                default:
                    return "其他";
            }
        }

        private void StyleTitle(ExcelRange range)
        {
            range.Style.Font.Bold = true;
            range.Style.Font.Size = 16;
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(31, 78, 121));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        private void StyleSection(ExcelRange range)
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(221, 235, 247));
        }

        private void StyleHeader(ExcelRange range)
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(91, 155, 213));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            range.Style.Border.BorderAround(ExcelBorderStyle.Thin, System.Drawing.Color.FromArgb(68, 114, 196));
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        private void StyleDataArea(ExcelRange range)
        {
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        /// <summary>
        /// 從生成的模板元素讀取有效面積參數
        /// </summary>
        private (string Formula, double TotalArea, int FormworkCount) GetFormworkAreaFromGeneratedElements(Element hostElement, ElementFormworkAnalysis analysis)
        {
            try
            {
                var doc = hostElement.Document;
                
                // 查找屬於此宿主元素的所有模板
                var formworkCollector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .WhereElementIsNotElementType()
                    .Cast<DirectShape>()
                    .Where(ds => ds.ApplicationId == "HB_BIM_Formwork");

                var relatedFormworks = new List<(ElementId FormworkId, double EffectiveArea)>();

                foreach (var formwork in formworkCollector)
                {
                    // 檢查 P_HostId 參數是否匹配
                    var hostIdParam = formwork.LookupParameter(SharedParams.P_HostId);
                    if (hostIdParam != null && hostIdParam.HasValue)
                    {
                        string hostIdStr = hostIdParam.AsString();
                        if (hostIdStr == hostElement.Id.ToString())
                        {
                            // 讀取有效面積參數
                            var effectiveAreaParam = formwork.LookupParameter(SharedParams.P_EffectiveArea);
                            if (effectiveAreaParam != null && effectiveAreaParam.HasValue)
                            {
                                // 參數值是平方英尺,使用 AreaCalculator 轉換為平方米
                                double areaFt2 = effectiveAreaParam.AsDouble();
                                double areaM2 = AreaCalculator.ConvertToSquareMeters(areaFt2);

                                System.Diagnostics.Debug.WriteLine($"📐 讀取模板面積: ID={formwork.Id.GetIdValue()}, {areaFt2:F6} ft² = {areaM2:F6} m²");
                                relatedFormworks.Add((formwork.Id, areaM2));
                            }
                        }
                    }
                }

                if (relatedFormworks.Count > 0)
                {
                    double totalArea = relatedFormworks.Sum(f => f.EffectiveArea);

                    // 生成計算式: 各模板面積相加，並顯示模板 ID
                    string formula;
                    if (relatedFormworks.Count == 1)
                    {
                        // 單一模板：直接顯示面積
                        var formwork = relatedFormworks[0];
                        formula = $"{formwork.EffectiveArea:F3}m² (ID:{formwork.FormworkId.GetIdValue()})";
                    }
                    else
                    {
                        // 多個模板：顯示計算式
                        var formulas = relatedFormworks.Select(f => $"{f.EffectiveArea:F3}(ID:{f.FormworkId.GetIdValue()})");
                        formula = string.Join(" + ", formulas) + $" = {totalArea:F3}m²";
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ 生成計算式: {formula}");
                    return (formula, totalArea, relatedFormworks.Count);
                }
                else
                {
                    // 如果找不到模板,使用分析結果的面積
                    System.Diagnostics.Debug.WriteLine($"⚠️ 找不到模板元素，使用分析計算值: {analysis.FormworkArea:F3}m²");
                    return ($"分析計算值 {analysis.FormworkArea:F3}m²", analysis.FormworkArea, 0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"讀取模板面積失敗: {ex.Message}");
                return ("讀取錯誤", analysis.FormworkArea, 0);
            }
        }

        /// <summary>
        /// 取得構件名稱 (優先使用類型名稱)
        /// </summary>
        private string GetElementName(Element element)
        {
            try
            {
                // 優先使用類型名稱
                var elementType = element.Document.GetElement(element.GetTypeId());
                if (elementType != null)
                {
                    string typeName = elementType.Name;
                    if (!string.IsNullOrEmpty(typeName))
                        return typeName;
                }

                // 備用: 使用元素名稱
                if (!string.IsNullOrEmpty(element.Name))
                    return element.Name;

                // 最後: 使用 ID
                return $"ID_{element.Id.GetIdValue()}";
            }
            catch
            {
                return $"ID_{element.Id.GetIdValue()}";
            }
        }

        /// <summary>
        /// 取得元素所在樓層
        /// </summary>
        private string GetElementLevel(Element element)
        {
            try
            {
                // 方法1: 從 Level 參數取得
                var levelParam = element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (levelParam != null && levelParam.HasValue)
                {
                    var levelId = levelParam.AsElementId();
                    if (levelId != null && levelId != ElementId.InvalidElementId)
                    {
                        var level = element.Document.GetElement(levelId) as Level;
                        if (level != null)
                            return level.Name;
                    }
                }

                // 方法2: 從 ReferenceLevel 取得
                if (element is FamilyInstance familyInstance)
                {
                    var refLevel = familyInstance.Host as Level;
                    if (refLevel != null)
                        return refLevel.Name;
                }

                // 方法3: 從 BASE_LEVEL_PARAM 取得
                var baseLevelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (baseLevelParam == null)
                    baseLevelParam = element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                
                if (baseLevelParam != null && baseLevelParam.HasValue)
                {
                    var levelId = baseLevelParam.AsElementId();
                    if (levelId != null && levelId != ElementId.InvalidElementId)
                    {
                        var level = element.Document.GetElement(levelId) as Level;
                        if (level != null)
                            return level.Name;
                    }
                }

                // 方法4: 根據 Z 座標推斷樓層
                var boundingBox = element.get_BoundingBox(null);
                if (boundingBox != null)
                {
                    double elevation = (boundingBox.Min.Z + boundingBox.Max.Z) / 2.0;
                    var nearestLevel = GetNearestLevel(element.Document, elevation);
                    if (nearestLevel != null)
                        return nearestLevel.Name + " (推斷)";
                }

                return "未指定樓層";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"取得樓層失敗: {ex.Message}");
                return "未知樓層";
            }
        }

        /// <summary>
        /// 根據高程找到最近的樓層
        /// </summary>
        private Level GetNearestLevel(Document doc, double elevation)
        {
            try
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => Math.Abs(l.Elevation - elevation))
                    .FirstOrDefault();
                
                return levels;
            }
            catch
            {
                return null;
            }
        }

        private string GetElementTypeDisplayName(StructuralElementType elementType)
        {
            switch (elementType)
            {
                case StructuralElementType.Beam: return "梁";
                case StructuralElementType.Column: return "柱";
                case StructuralElementType.Slab: return "板";
                case StructuralElementType.Wall: return "牆";
                case StructuralElementType.Foundation: return "基礎";
                default: return "其他";
            }
        }

        private string Q(object v)
        {
            var s = v?.ToString() ?? "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
