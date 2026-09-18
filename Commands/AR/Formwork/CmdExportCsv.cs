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
        private static bool _exportRunning;
        private bool _generatedOnly;
        private bool _cancelRequested;
        private ProgressWindow _progress;
        private readonly System.Diagnostics.Stopwatch _elapsed = new System.Diagnostics.Stopwatch();
        private long _lastUiUpdate;
        private int _templateCount;
        private string _currentStage = "尚未開始";
        private List<Level> _levels;
        private readonly List<string> _timings = new List<string>();
        private string ReportTitle => _generatedOnly ? "BIM 已生成模板匯出報告" : "BIM 結構模板分析報告";
        private string DuplicateCheckDescription => _generatedOnly
            ? "快速匯出未執行包圍盒重複檢查；面積取自現有模板參數，未重新驗證幾何。"
            : "疑似重複僅比對同宿主包圍盒，不涵蓋部分重疊；未自動去重。";

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet set)
        {
            if (_exportRunning) return Result.Cancelled;
            _exportRunning = true;
            Document doc = null;
            bool budgetStarted = false;

            try
            {
                doc = data.Application.ActiveUIDocument?.Document;
                if (doc == null) return Result.Cancelled;
                // 授權檢查
                if (!LicenseHelper.CheckLicense("ExportCSV", "匯出CSV", LicenseType.Professional))
                {
                    return Result.Cancelled;
                }

                var mode = new TaskDialog("匯出模板資料")
                {
                    MainInstruction = "選擇匯出內容",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                mode.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "已生成模板（快速匯出）",
                    "模板生成與面生面；直接讀取參數，不重算結構、混凝土或鋼筋。未生成模板的構件不列入。");
                mode.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "完整結構分析後匯出",
                    "重算整份模型並比對模板包圍盒；大型模型可能耗時較長。");
                var choice = mode.Show();
                if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                    return Result.Cancelled;
                _generatedOnly = choice == TaskDialogResult.CommandLink1;

                var sfd = new SaveFileDialog
                {
                    Title = "匯出模板資料",
                    Filter = "CSV (*.csv)|*.csv|Excel 檔案 (*.xlsx)|*.xlsx",
                    FileName = $"Formwork_Report_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    DefaultExt = "csv"
                };
                if (sfd.ShowDialog() != true) return Result.Cancelled;

                _cancelRequested = false;
                _lastUiUpdate = 0;
                _levels = null;
                _timings.Clear();
                _elapsed.Restart();
                _progress = new ProgressWindow { Title = "模板資料匯出進度" };
                new System.Windows.Interop.WindowInteropHelper(_progress).Owner = data.Application.MainWindowHandle;
                _progress.CancelRequested += () => _cancelRequested = true;
                _progress.Show();
                CurvedMeshBudget.StartRun(stage => Checkpoint(stage));
                budgetStarted = true;
                Checkpoint("準備匯出", force: true);

                var analysisResult = CollectAnalysis(doc);

                Checkpoint("讀取已生成模板參數", force: true);
                var templates = CollectTemplates(doc);
                _templateCount = templates.Count;
                RecordStage("模板參數收集");
                if (analysisResult.ElementAnalyses.Count == 0 && templates.Count == 0)
                {
                    TaskDialog.Show("匯出", _generatedOnly ? "沒有找到已生成模板；快速匯出不會自動進行結構分析。" : "沒有找到可匯出的構件或模板。");
                    return Result.Succeeded;
                }

                var extension = Path.GetExtension(sfd.FileName)?.ToLowerInvariant();
                Checkpoint("彙整宿主與模板明細", force: true);
                var detailDataList = _generatedOnly
                    ? CollectGeneratedDetailData(doc, templates)
                    : CollectDetailData(doc, analysisResult, templates);
                RecordStage("宿主彙整");

                using (var output = new ExportOutputFile(sfd.FileName))
                {
                    Checkpoint("寫入報表暫存檔", force: true);
                    if (extension == ".csv")
                    {
                        using (var sw = new StreamWriter(output.TemporaryPath, false, new System.Text.UTF8Encoding(true)))
                        {
                            WriteHeader(sw);
                            WriteSummary(sw, analysisResult, detailDataList);
                            WriteDetailData(sw, detailDataList);
                            WriteTemplateData(sw, templates);
                        }
                    }
                    else
                        ExportToExcel(output.TemporaryPath, analysisResult, detailDataList, doc.ActiveView?.Name, templates);
                    RecordStage("報表寫入");
                    Checkpoint("完成檢查，準備儲存正式檔案", force: true);
                    CurvedMeshBudget.ThrowIfExceeded();
                    output.Commit();
                }
                RecordStage("檔案完成");
                _progress.ForceClose();
                _progress = null;

                TaskDialog.Show("匯出完成", 
                    $"已匯出 {detailDataList.Count} 個構件與 {templates.Count} 片模板明細。\n" +
                    (_generatedOnly ? "快速匯出：未重算幾何、混凝土、鋼筋及重複模板。\n" : string.Empty) +
                    $"需檢查的模板：{templates.Count(t => t.Warning.Length > 0)} 片（未自動去重）。\n" +
                    $"未歸屬宿主總計：{templates.Count - detailDataList.Sum(d => d.FormworkCount)} 片。\n" +
                    string.Join("\n", _timings) + $"\n{sfd.FileName}");
                return Result.Succeeded;
            }
            catch (System.OperationCanceledException)
            {
                TaskDialog.Show("已取消匯出", "匯出已取消，未替換原有報表。");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"匯出時發生錯誤：{ex.Message}\n階段：{_currentStage}\n耗時：{_elapsed.Elapsed.TotalSeconds:F1}s");
                return Result.Failed;
            }
            finally
            {
                if (budgetStarted) CurvedMeshBudget.EndRun();
                _progress?.ForceClose();
                _progress = null;
                _levels = null;
                _exportRunning = false;
            }
        }

        private StructuralAnalysisResult CollectAnalysis(Document doc)
        {
            if (_generatedOnly) return new StructuralAnalysisResult();
            Checkpoint("完整分析：重算整份模型", force: true);
            FormworkEngine.Debug.Enable(true);
            FormworkEngine.BeginRun();
            try
            {
                var result = StructuralFormworkAnalyzer.AnalyzeProject(doc);
                CurvedMeshBudget.ThrowIfExceeded();
                RecordStage("完整結構分析");
                return result;
            }
            finally { FormworkEngine.EndRun(); }
        }

        private void Checkpoint(string stage, int current = 0, int total = 0, bool force = false)
        {
            _currentStage = stage;
            if (_cancelRequested) throw new System.OperationCanceledException();
            if (_progress != null && (force || _elapsed.ElapsedMilliseconds - _lastUiUpdate >= 150))
            {
                _lastUiUpdate = _elapsed.ElapsedMilliseconds;
                _progress.UpdateProgress(current, total, _elapsed.Elapsed);
                _progress.UpdateStage(stage, _elapsed.Elapsed);
                System.Windows.Forms.Application.DoEvents();
            }
            if (_cancelRequested) throw new System.OperationCanceledException();
        }

        private void RecordStage(string stage)
        {
            string value = $"{stage}: {_elapsed.Elapsed.TotalSeconds:F2}s (累計)";
            _timings.Add(value);
            System.Diagnostics.Debug.WriteLine("[FormworkExport] " + value);
        }

        private void WriteHeader(StreamWriter sw)
        {
            sw.WriteLine($"=== {ReportTitle} ===");
            sw.WriteLine($"分析時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine(_generatedOnly ? "資料來源: 已生成模板參數；未重新驗證幾何；未生成模板的構件不列入" : "資料來源: 本次完整結構分析與已生成模板");
            sw.WriteLine(DuplicateCheckDescription);
            sw.WriteLine("階段耗時," + Q(string.Join("；", _timings)));
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
            public string Sources { get; set; }
            public string Warning { get; set; }
        }

        private List<DetailDataItem> CollectDetailData(Document doc, StructuralAnalysisResult result, List<TemplateData> templates)
        {
            var detailDataList = new List<DetailDataItem>();

            // Build a run-local index once instead of scanning all templates per host.
            var formworksByHost = templates.ToLookup(t => t.HostId);
            var analyzedIds = new HashSet<string>(result.ElementAnalyses.Keys.Select(e => e.Id.ToString()));
            foreach (var template in templates.Where(t => !analyzedIds.Contains(t.HostId)))
                template.AddWarning("宿主未納入分析總計；本片僅列模板明細");

            foreach (var kvp in result.ElementAnalyses)
            {
                var element = kvp.Key;
                Checkpoint($"彙整分析構件 ID {element.Id.GetIdValue()}");
                var analysis = kvp.Value;

                // 讀取實際生成的模板有效面積
                var related = formworksByHost[element.Id.ToString()].ToList();
                var valid = related.Where(t => t.Area.HasValue).ToList();
                double area = related.Count == 0 ? analysis.FormworkArea : valid.Sum(t => t.Area.Value);
                string formula = related.Count == 0 ? $"分析計算值 {area:F3}m²" :
                    ExportTemplateRules.BuildAreaFormula(valid.Select(t => new KeyValuePair<string, double>(t.Id, t.Area.Value)));

                detailDataList.Add(new DetailDataItem
                {
                    Element = element,
                    Analysis = analysis,
                    Name = GetElementName(element),
                    Level = GetElementLevel(element),
                    Type = analysis.ElementType,
                    Formula = formula,
                    ActualFormworkArea = area,
                    FormworkCount = related.Count,
                    Sources = related.Count == 0 ? "分析估算（無生成模板）" : string.Join("；", related.Select(t => t.Source).Distinct()),
                    Warning = string.Join("；", related.Select(t => t.Warning).Where(w => w.Length > 0).Distinct())
                });
            }

            return detailDataList;
        }

        private List<DetailDataItem> CollectGeneratedDetailData(Document doc, List<TemplateData> templates)
        {
            var result = new List<DetailDataItem>();
            foreach (var group in templates.GroupBy(t => t.HostId))
            {
                Checkpoint($"彙整已生成模板：宿主 {group.Key}");
                var id = ExportTemplateRules.ParseHostId(group.Key);
                Element host = null;
                if (id.HasValue)
                {
#if REVIT2022 || REVIT2023
                    if (id.Value <= int.MaxValue) host = doc.GetElement(new ElementId((int)id.Value));
#else
                    host = doc.GetElement(new ElementId(id.Value));
#endif
                }
                if (host == null)
                {
                    foreach (var template in group) template.AddWarning("宿主不存在或ID無效；僅列逐片明細，未納入宿主總計");
                    continue;
                }
                var related = group.ToList();
                var valid = related.Where(t => t.Area.HasValue).ToList();
                result.Add(new DetailDataItem
                {
                    Element = host,
                    Name = GetElementName(host),
                    Level = GetElementLevel(host),
                    Type = GetHostType(host),
                    Formula = ExportTemplateRules.BuildAreaFormula(valid.Select(t => new KeyValuePair<string, double>(t.Id, t.Area.Value))),
                    ActualFormworkArea = valid.Sum(t => t.Area.Value),
                    FormworkCount = related.Count,
                    Sources = string.Join("；", related.Select(t => t.Source).Distinct()),
                    Warning = string.Join("；", related.Select(t => t.Warning).Where(w => w.Length > 0).Distinct())
                });
            }
            return result;
        }

        private static StructuralElementType GetHostType(Element host)
        {
            if (host is Wall) return StructuralElementType.Wall;
            if (host is Floor) return StructuralElementType.Slab;
            long category = host.Category?.Id?.GetIdValue() ?? 0;
            if (category == (long)BuiltInCategory.OST_StructuralColumns) return StructuralElementType.Column;
            if (category == (long)BuiltInCategory.OST_StructuralFraming) return StructuralElementType.Beam;
            if (category == (long)BuiltInCategory.OST_StructuralFoundation) return StructuralElementType.Foundation;
            if (ElementCategorizer.IsStairs(host)) return StructuralElementType.Stair;
            return StructuralElementType.Other;
        }

        private class TemplateData
        {
            public string Id;
            public string HostId = string.Empty;
            public string Name;
            public string Source;
            public string AppId;
            public string DataId;
            public double? Area;
            public string BoundsKey;
            public string DuplicateGroup = string.Empty;
            public string Warning = string.Empty;

            public void AddWarning(string warning)
            {
                Warning += (Warning.Length == 0 ? string.Empty : "；") + warning;
            }
        }

        private List<TemplateData> CollectTemplates(Document doc)
        {
            var rows = new List<TemplateData>();
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType()
                .ToElementIds();

            int scanned = 0;
            foreach (var id in collector)
            {
                Checkpoint($"掃描模板參數：ID {id.GetIdValue()}", ++scanned, collector.Count);
                var ds = doc.GetElement(id) as DirectShape;
                if (ds == null) continue;
                bool hasMetadata = ds.LookupParameter(SharedParams.P_HostId)?.HasValue == true &&
                                   ds.LookupParameter(SharedParams.P_EffectiveArea)?.HasValue == true;
                if (!ExportTemplateRules.IsTemplate(ds.ApplicationId, ds.Name, hasMetadata)) continue;
                var row = new TemplateData
                {
                    Id = ds.Id.GetIdValue().ToString(CultureInfo.InvariantCulture),
                    Name = ds.Name, AppId = ds.ApplicationId, DataId = ds.ApplicationDataId,
                    Source = ExportTemplateRules.Source(ds.ApplicationDataId, ds.Name)
                };
                rows.Add(row);
                try
                {
                    var host = ds.LookupParameter(SharedParams.P_HostId);
                    if (host != null && host.StorageType == StorageType.String)
                        row.HostId = (host.AsString() ?? string.Empty).Trim();
                    else if (host != null && host.StorageType == StorageType.Integer)
                        row.HostId = host.AsInteger().ToString(CultureInfo.InvariantCulture);
                    var parsedHost = ExportTemplateRules.ParseHostId(row.HostId);
                    if (parsedHost.HasValue) row.HostId = parsedHost.Value.ToString(CultureInfo.InvariantCulture);
                    else if (row.HostId.Length > 0) row.AddWarning("宿主ID格式無效");
                    if (row.HostId.Length == 0) row.AddWarning("缺少宿主ID");

                    var area = ds.LookupParameter(SharedParams.P_EffectiveArea);
                    if (area != null && area.HasValue && area.StorageType == StorageType.Double &&
                        ExportTemplateRules.IsValidArea(area.AsDouble()))
                        row.Area = AreaCalculator.ConvertToSquareMeters(area.AsDouble());
                    else
                        row.AddWarning("有效面積缺失或無效；未計入面積加總");
                }
                catch (Exception)
                {
                    row.AddWarning("參數讀取失敗；請核對有效面積與宿主");
                }

                if (_generatedOnly) continue;
                Checkpoint($"檢查模板包圍盒：ID {row.Id}", scanned, collector.Count);
                try
                {
                    var bounds = ds.get_BoundingBox(null);
                    if (bounds == null) { row.AddWarning("無包圍盒；未檢查疑似重複"); continue; }
                    var corners = new List<XYZ>();
                    for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        corners.Add(bounds.Transform.OfPoint(new XYZ(
                            x == 0 ? bounds.Min.X : bounds.Max.X,
                            y == 0 ? bounds.Min.Y : bounds.Max.Y,
                            z == 0 ? bounds.Min.Z : bounds.Max.Z)));
                    row.BoundsKey = ExportTemplateRules.BoundsKey(row.HostId, new[]
                    {
                        corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z),
                        corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)
                    });
                }
                catch (Exception)
                {
                    row.AddWarning("包圍盒讀取失敗；未檢查疑似重複");
                }
            }

            int groupNumber = 0;
            foreach (var group in rows.Where(r => r.BoundsKey != null).GroupBy(r => r.BoundsKey).Where(g => g.Count() > 1))
            {
                Checkpoint("比對疑似重複模板");
                string groupId = "D" + (++groupNumber).ToString("D4", CultureInfo.InvariantCulture);
                foreach (var row in group)
                {
                    row.DuplicateGroup = groupId;
                    row.AddWarning("同宿主包圍盒近似一致；疑似重複，未去重");
                }
            }
            return rows.OrderBy(r => r.HostId).ThenBy(r => r.Id).ToList();
        }

        private static readonly string[] TemplateHeaders =
        {
            "模板ID", "宿主ID", "模板名稱", "生成來源", "有效面積(m²)",
            "工具識別碼", "生成路徑", "疑似重複群組", "資料警示"
        };

        private void WriteTemplateData(StreamWriter sw, List<TemplateData> templates)
        {
            sw.WriteLine();
            sw.WriteLine("=== 逐片模板明細（參數面積；未自動去重） ===");
            sw.WriteLine(DuplicateCheckDescription);
            sw.WriteLine(string.Join(",", TemplateHeaders));
            foreach (var t in templates)
            {
                Checkpoint($"寫入 CSV 模板 ID {t.Id}");
                sw.WriteLine(string.Join(",", new[]
                {
                    Q(t.Id), Q(t.HostId), Q(t.Name), Q(t.Source),
                    t.Area?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
                    Q(t.AppId), Q(t.DataId), Q(t.DuplicateGroup), Q(t.Warning)
                }));
            }
        }

        private void WriteTemplateWorksheet(ExcelWorksheet ws, List<TemplateData> templates)
        {
            ws.Cells[1, 1].Value = "逐片模板：參數面積，未去重。" + DuplicateCheckDescription;
            ws.Cells[1, 1, 1, TemplateHeaders.Length].Merge = true;
            for (int col = 0; col < TemplateHeaders.Length; col++) ws.Cells[2, col + 1].Value = TemplateHeaders[col];
            int row = 3;
            foreach (var t in templates)
            {
                Checkpoint($"寫入 Excel 模板 ID {t.Id}");
                object[] values = { t.Id, t.HostId, t.Name, t.Source, t.Area, t.AppId, t.DataId, t.DuplicateGroup, t.Warning };
                for (int col = 0; col < values.Length; col++) ws.Cells[row, col + 1].Value = values[col];
                row++;
            }
            StyleHeader(ws.Cells[2, 1, 2, TemplateHeaders.Length]);
            ws.Column(5).Style.Numberformat.Format = "0.000";
            ws.Cells[2, 1, Math.Max(2, row - 1), TemplateHeaders.Length].AutoFilter = true;
            ws.View.FreezePanes(3, 1);
            SetColumnWidths(ws, 18, 18, 28, 22, 18, 24, 24, 18, 55);
            ws.Column(9).Style.WrapText = true;
        }

        private void WriteSummary(StreamWriter sw, StructuralAnalysisResult result, List<DetailDataItem> detailData)
        {
            // 使用實際模板面積計算總計
            double totalActualArea = detailData.Sum(d => d.ActualFormworkArea);
            int totalFormworkCount = detailData.Sum(d => d.FormworkCount);

            sw.WriteLine("=== 總計統計 ===");
            sw.WriteLine($"{(_generatedOnly ? "已生成模板宿主數" : "分析構件總數")},{(_generatedOnly ? detailData.Count : result.TotalElements)}");
            sw.WriteLine($"模板明細總數,{_templateCount}");
            sw.WriteLine($"未歸屬宿主總計的模板數,{_templateCount - totalFormworkCount}");
            sw.WriteLine($"生成模板總數,{totalFormworkCount}");
            sw.WriteLine($"模板總面積(m²),{Number(totalActualArea)}");
            sw.WriteLine($"混凝土總體積(m³),{(_generatedOnly ? "未重新分析" : Number(result.TotalConcreteVolume))}");
            sw.WriteLine($"鋼筋估算重量(t),{(_generatedOnly ? "未重新分析" : Number(result.EstimatedRebarWeight))}");
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
                    ConcreteVolume = _generatedOnly ? (double?)null : g.Sum(d => d.Analysis.ConcreteVolume),
                    AvgArea = g.Sum(d => d.ActualFormworkArea) / g.Count()
                })
                .OrderBy(s => s.Type);

            foreach (var stat in categoryStats)
            {
                string typeName = GetElementTypeDisplayName(stat.Type);
                sw.WriteLine($"{typeName},{stat.Count},{stat.FormworkCount},{Number(stat.FormworkArea)},{Number(stat.ConcreteVolume)},{Number(stat.AvgArea)}");
            }
            sw.WriteLine();
        }

        private void WriteDetailData(StreamWriter sw, List<DetailDataItem> detailData)
        {
            sw.WriteLine("=== 詳細構件分析 ===");
            sw.WriteLine("構件名稱,樓層,類型,構件ID,模板數量,模板面積計算式,模板面積(m²),混凝土體積(m³),生成來源,資料警示");

            // 按樓層、類型、名稱排序
            var sortedData = detailData
                .OrderBy(x => x.Level)
                .ThenBy(x => x.Type)
                .ThenBy(x => x.Name);

            foreach (var item in sortedData)
            {
                Checkpoint($"寫入 CSV 構件 ID {item.Element.Id.GetIdValue()}");
                string elementType = GetElementTypeDisplayName(item.Type);

                sw.WriteLine($"{Q(item.Name)}," +
                           $"{Q(item.Level)}," +
                           $"{Q(elementType)}," +
                           $"{item.Element.Id.GetIdValue()}," +
                           $"{item.FormworkCount}," +
                           $"{Q(item.Formula)}," +
                           $"{Number(item.ActualFormworkArea)}," +
                           $"{Number(_generatedOnly ? (double?)null : item.Analysis.ConcreteVolume)},{Q(item.Sources)},{Q(item.Warning)}");
            }
        }

        private void ExportToExcel(string filePath, StructuralAnalysisResult result, List<DetailDataItem> detailData, string activeViewName, List<TemplateData> templates)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var file = new FileInfo(filePath);
            using (var package = new ExcelPackage())
            {
                var summarySheet = package.Workbook.Worksheets.Add("總覽");
                var detailSheet = package.Workbook.Worksheets.Add("詳細資料");

                WriteSummaryWorksheet(summarySheet, result, detailData, activeViewName);
                WriteDetailWorksheet(detailSheet, detailData);
                WriteTypeWorksheets(package, detailData);
                WriteTemplateWorksheet(package.Workbook.Worksheets.Add("逐片模板"), templates);

                Checkpoint("封裝 Excel 檔案（此單次作業需等待完成）", force: true);
                package.SaveAs(file);
                Checkpoint("Excel 封裝完成", force: true);
            }
        }

        private void WriteSummaryWorksheet(ExcelWorksheet ws, StructuralAnalysisResult result, List<DetailDataItem> detailData, string activeViewName)
        {
            double totalActualArea = detailData.Sum(d => d.ActualFormworkArea);
            int totalFormworkCount = detailData.Sum(d => d.FormworkCount);

            ws.Cells[1, 1].Value = ReportTitle;
            ws.Cells[1, 1, 1, 6].Merge = true;
            ws.Cells[2, 1].Value = "分析時間";
            ws.Cells[2, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cells[3, 1].Value = "分析系統";
            ws.Cells[3, 2].Value = _generatedOnly ? "已生成模板參數；未重新驗證幾何" : "本次完整結構分析";
            ws.Cells[4, 1].Value = "目前視圖";
            ws.Cells[4, 2].Value = string.IsNullOrWhiteSpace(activeViewName) ? "未指定" : activeViewName;
            ws.Cells[5, 1].Value = "模板掃描範圍";
            ws.Cells[5, 2].Value = "整份模型";
            ws.Cells[6, 1].Value = "模板明細 / 未歸屬宿主總計";
            ws.Cells[6, 2].Value = $"{_templateCount} / {_templateCount - totalFormworkCount}";

            ws.Cells[7, 1].Value = "總計統計";
            ws.Cells[8, 1].Value = _generatedOnly ? "已生成模板宿主數" : "分析構件總數";
            ws.Cells[8, 2].Value = _generatedOnly ? detailData.Count : result.TotalElements;
            ws.Cells[9, 1].Value = "生成模板總數";
            ws.Cells[9, 2].Value = totalFormworkCount;
            ws.Cells[10, 1].Value = "模板總面積 (m2)";
            ws.Cells[10, 2].Value = totalActualArea;
            ws.Cells[11, 1].Value = "混凝土總體積 (m3)";
            ws.Cells[11, 2].Value = _generatedOnly ? (object)"未重新分析" : result.TotalConcreteVolume;
            ws.Cells[12, 1].Value = "鋼筋估算重量 (t)";
            ws.Cells[12, 2].Value = _generatedOnly ? (object)"未重新分析" : result.EstimatedRebarWeight;

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
                    ConcreteVolume = _generatedOnly ? (double?)null : g.Sum(d => d.Analysis.ConcreteVolume),
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
            SetColumnWidths(ws, 32, 30, 18, 22, 24, 28);
        }

        private void WriteDetailWorksheet(ExcelWorksheet ws, List<DetailDataItem> detailData)
        {
            WriteDetailWorksheet(ws, detailData, "詳細構件分析");
        }

        private void WriteDetailWorksheet(ExcelWorksheet ws, List<DetailDataItem> detailData, string title)
        {
            var headers = new[] { "構件名稱", "樓層", "類型", "構件ID", "模板數量", "模板面積計算式", "模板面積 (m2)", "混凝土體積 (m3)", "生成來源", "資料警示" };
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
                Checkpoint($"寫入 Excel 構件 ID {item.Element.Id.GetIdValue()}");
                ws.Cells[row, 1].Value = item.Name;
                ws.Cells[row, 2].Value = item.Level;
                ws.Cells[row, 3].Value = GetElementTypeDisplayName(item.Type);
                ws.Cells[row, 4].Value = item.Element.Id.GetIdValue();
                ws.Cells[row, 5].Value = item.FormworkCount;
                ws.Cells[row, 6].Value = item.Formula;
                ws.Cells[row, 7].Value = item.ActualFormworkArea;
                ws.Cells[row, 8].Value = _generatedOnly ? (double?)null : item.Analysis.ConcreteVolume;
                ws.Cells[row, 9].Value = item.Sources;
                ws.Cells[row, 10].Value = item.Warning;
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
            SetColumnWidths(ws, 28, 20, 12, 18, 14, 55, 20, 22, 24, 55);
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

        private static void SetColumnWidths(ExcelWorksheet ws, params double[] widths)
        {
            for (int column = 0; column < widths.Length; column++) ws.Column(column + 1).Width = widths[column];
        }

        private static string Number(double? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty;

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
                case StructuralElementType.Stair:
                    return "樓梯";
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
        private (string Formula, double TotalArea, int FormworkCount) GetFormworkAreaFromGeneratedElements(
            Element hostElement, ElementFormworkAnalysis analysis, IEnumerable<DirectShape> formworkCollector)
        {
            try
            {
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
                var assignedLevel = element.Document.GetElement(element.LevelId) as Level;
                if (assignedLevel != null) return assignedLevel.Name;
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
                var boundingBox = _generatedOnly ? null : element.get_BoundingBox(null);
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
                if (_levels == null) _levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .ToList();
                var levels = _levels.OrderBy(l => Math.Abs(l.Elevation - elevation)).FirstOrDefault();
                
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
                case StructuralElementType.Stair: return "樓梯";
                default: return "其他";
            }
        }

        private string Q(object v)
        {
            var s = v?.ToString() ?? "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
