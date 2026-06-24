using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using YD_RevitTools.LicenseManager.Helpers;

// 解決 Revit API 與 WinForms / System.Drawing 命名衝突
using WinForm = System.Windows.Forms.Form;
using WinTextBox = System.Windows.Forms.TextBox;
using WinButton = System.Windows.Forms.Button;
using WinLabel = System.Windows.Forms.Label;
using WinCheckBox = System.Windows.Forms.CheckBox;
using WinRadioButton = System.Windows.Forms.RadioButton;
using WinGroupBox = System.Windows.Forms.GroupBox;
using WinControl = System.Windows.Forms.Control;
using WinPanel = System.Windows.Forms.Panel;
using SysColor = System.Drawing.Color;

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    // TransactionMode.Manual 必要：doc.Export(PDF) 内部需要文件写入权限
    [Transaction(TransactionMode.Manual)]
    public class CmdScheduleExport : IExternalCommand
    {
        private static readonly List<WinForm> OpenScheduleForms = new List<WinForm>();

        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet set)
        {
            var uiDoc = cd.Application.ActiveUIDocument;
            if (uiDoc == null) { msg = "No active document"; return Result.Failed; }
            var doc = uiDoc.Document;
            if (doc == null) { msg = "Document is null"; return Result.Failed; }

            var licenseManager = LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("Schedule.Export"))
            {
                TaskDialog.Show("明細表匯出",
                    "此功能需要 Standard 或 Professional 授權。\n請升級您的授權以使用明細表匯出功能。");
                return Result.Cancelled;
            }

            // 收集所有明細表並預計算預覽資訊
            var scheduleInfos = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTemplate && !vs.IsTitleblockRevisionSchedule)
                .OrderBy(vs => vs.Name)
                .Select(vs => new ScheduleInfo(vs))
                .ToList();

            if (scheduleInfos.Count == 0)
            {
                TaskDialog.Show("明細表匯出", "目前文件中沒有任何明細表。");
                return Result.Cancelled;
            }

            try
            {
                var handler = new ScheduleExportExternalEventHandler(this, doc);
                var externalEvent = ExternalEvent.Create(handler);
                ScheduleExportDialog form = null;
                form = new ScheduleExportDialog(scheduleInfos, importMode =>
                {
                    handler.Request(importMode ? ScheduleExportRequestKind.BuildImportPreview : ScheduleExportRequestKind.Export, form);
                    if (externalEvent.Raise() != ExternalEventRequest.Accepted)
                        form.SetRequestCompleted("目前無法送出 Revit 動作，請稍後再試。");
                });
                handler.Attach(form, externalEvent);
                OpenScheduleForms.Add(form);
                form.FormClosed += (s, e) =>
                {
                    OpenScheduleForms.Remove(form);
                    handler.DisposePreview();
                    externalEvent.Dispose();
                };
                form.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                msg = ex.ToString();
                return Result.Failed;
            }
        }

        private enum ScheduleExportRequestKind
        {
            None,
            Export,
            BuildImportPreview,
            ApplyImport
        }

        private sealed class ScheduleExportExternalEventHandler : IExternalEventHandler
        {
            private readonly CmdScheduleExport _owner;
            private readonly Document _doc;
            private ScheduleExportRequestKind _requestKind;
            private ScheduleExportDialog _dialog;
            private ExternalEvent _externalEvent;
            private List<ImportDiffItem> _applyItems = new List<ImportDiffItem>();
            private ImportPreviewDialog _previewDialog;

            public ScheduleExportExternalEventHandler(CmdScheduleExport owner, Document doc)
            {
                _owner = owner;
                _doc = doc;
            }

            public void Attach(ScheduleExportDialog dialog, ExternalEvent externalEvent)
            {
                _dialog = dialog;
                _externalEvent = externalEvent;
            }

            public void Request(ScheduleExportRequestKind requestKind, ScheduleExportDialog dialog)
            {
                _requestKind = requestKind;
                _dialog = dialog;
            }

            private void RequestApply(List<ImportDiffItem> items)
            {
                _applyItems = items ?? new List<ImportDiffItem>();
                _requestKind = ScheduleExportRequestKind.ApplyImport;
                if (_externalEvent.Raise() != ExternalEventRequest.Accepted)
                    _previewDialog?.SetRequestCompleted("目前無法送出 Revit 動作，請稍後再試。");
            }

            public void Execute(UIApplication app)
            {
                try
                {
                    switch (_requestKind)
                    {
                        case ScheduleExportRequestKind.Export:
                            _owner.RunExport(_doc, _dialog);
                            break;
                        case ScheduleExportRequestKind.BuildImportPreview:
                            DisposePreview();
                            var preview = _owner.BuildImportPreview(
                                _doc,
                                _dialog.SelectedSchedules.Single(),
                                _dialog.ImportFilePath);
                            if (!preview.Items.Any(i => i.CanApply))
                            {
                                TaskDialog.Show("匯入對照結果", "沒有可套用的差異。");
                                break;
                            }
                            _previewDialog = new ImportPreviewDialog(preview, RequestApply);
                            _previewDialog.FormClosed += (s, e) => _previewDialog = null;
                            _previewDialog.Show();
                            break;
                        case ScheduleExportRequestKind.ApplyImport:
                            using (var tx = new Transaction(_doc, "明細表匯入對照套用"))
                            {
                                tx.Start();
                                var result = _owner.ApplyImportItems(_doc, _applyItems);
                                tx.Commit();
                                TaskDialog.Show("匯入對照結果",
                                    $"套用項目：{_applyItems.Count}\n" +
                                    $"成功更新欄位：{result.UpdatedCells}\n" +
                                    $"成功更新列：{result.UpdatedRows}\n" +
                                    (result.Errors.Count > 0 ? ("\n錯誤前 10 筆：\n" + string.Join("\n", result.Errors.Take(10))) : ""));
                            }
                            _previewDialog?.SetRequestCompleted();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("明細表匯出", "執行失敗：\n" + ex.Message);
                    _previewDialog?.SetRequestCompleted(ex.Message);
                }
                finally
                {
                    _requestKind = ScheduleExportRequestKind.None;
                    _dialog?.SetRequestCompleted();
                }
            }

            public void DisposePreview()
            {
                if (_previewDialog != null && !_previewDialog.IsDisposed)
                    _previewDialog.Close();
                _previewDialog = null;
            }

            public string GetName()
            {
                return "YD BIM Tools - 明細表匯出";
            }
        }

        private void RunExport(Document doc, ScheduleExportDialog form)
        {
            var selectedSchedules = form.SelectedSchedules;
            var outputFolder = form.OutputFolder;
            int successCount = 0;
            var errors = new List<string>();

            if (form.ExportFormat == ExportFormat.Excel)
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                if (form.CombineIntoOne)
                {
                    var filePath = Path.Combine(outputFolder, $"明細表匯出_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                    try
                    {
                        ExportMultipleSchedulesToExcel(selectedSchedules, filePath, form.AutoParseNumbers);
                        successCount = selectedSchedules.Count;
                    }
                    catch (Exception ex) { errors.Add("合併匯出失敗：" + ex.Message); }
                }
                else
                {
                    foreach (var schedule in selectedSchedules)
                    {
                        try
                        {
                            ExportSingleScheduleToExcel(
                                schedule,
                                Path.Combine(outputFolder, SanitizeFileName(schedule.Name) + ".xlsx"),
                                form.AutoParseNumbers);
                            successCount++;
                        }
                        catch (Exception ex) { errors.Add("• " + schedule.Name + "：" + ex.Message); }
                    }
                }
            }
            else
            {
                try
                {
                    ExportSchedulesToPdf(doc, selectedSchedules, outputFolder, form.CombineIntoOne);
                    successCount = selectedSchedules.Count;
                }
                catch (Exception ex) { errors.Add("PDF 匯出失敗：" + ex.Message); }
            }

            string resultMsg = $"匯出完成！\n成功：{successCount} 個明細表\n輸出資料夾：{outputFolder}";
            if (errors.Count > 0)
                resultMsg += "\n\n失敗 " + errors.Count + " 項：\n" + string.Join("\n", errors);

            var td = new TaskDialog("明細表匯出")
            {
                MainInstruction = successCount > 0 ? "匯出成功" : "匯出失敗",
                MainContent = resultMsg,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "開啟輸出資料夾");
            if (td.Show() == TaskDialogResult.CommandLink1)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", outputFolder); } catch { }
            }
        }

        private ImportPreviewResult BuildImportPreview(Document doc, ViewSchedule schedule, string filePath)
        {
            var result = new ImportPreviewResult();
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var ws = package.Workbook.Worksheets.FirstOrDefault();
                if (ws == null || ws.Dimension == null)
                {
                    result.Errors.Add("Excel 無可讀取工作表。");
                    return result;
                }

                int headerRow = FindHeaderRow(ws);
                if (headerRow <= 0)
                {
                    result.Errors.Add("找不到標題列。請使用本工具匯出的檔案格式。");
                    return result;
                }

                var headers = new Dictionary<int, string>();
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                {
                    string h = (ws.Cells[headerRow, c].Text ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(h))
                        headers[c] = h;
                }
                if (headers.Count == 0)
                {
                    result.Errors.Add("標題列為空白。");
                    return result;
                }

                int idCol = headers.FirstOrDefault(kv => IsElementIdHeader(kv.Value)).Key;
                if (idCol <= 0)
                {
                    result.Errors.Add("找不到「元素ID / ElementId / Id」欄位，無法安全回寫。");
                    return result;
                }

                var skipHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "元素ID","ElementId","Id","ID"
                };

                for (int r = headerRow + 1; r <= ws.Dimension.End.Row; r++)
                {
                    result.TotalRows++;
                    string idText = (ws.Cells[r, idCol].Text ?? string.Empty).Trim();
                    if (!TryParseElementId(idText, out ElementId eid))
                    {
                        result.SkippedRows++;
                        continue;
                    }

                    Element elem = doc.GetElement(eid);
                    if (elem == null)
                    {
                        result.SkippedRows++;
                        continue;
                    }

                    foreach (var kv in headers)
                    {
                        string header = kv.Value;
                        if (skipHeaders.Contains(header))
                            continue;

                        string text = (ws.Cells[r, kv.Key].Text ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        Parameter p = elem.LookupParameter(header);
                        if (p == null)
                        {
                            Element typeElem = doc.GetElement(elem.GetTypeId());
                            p = typeElem?.LookupParameter(header);
                        }
                        if (p == null || p.IsReadOnly)
                            continue;
                        string oldText = GetParameterDisplayText(p);
                        if (string.Equals((oldText ?? string.Empty).Trim(), text.Trim(), StringComparison.Ordinal))
                            continue;
                        result.Items.Add(new ImportDiffItem
                        {
                            ElementId = eid,
                            ElementName = elem.Name,
                            ParameterName = header,
                            OldValue = oldText,
                            NewValue = text,
                            CanApply = true
                        });
                    }
                }
            }

            return result;
        }

        private ImportApplyResult ApplyImportItems(Document doc, List<ImportDiffItem> items)
        {
            var result = new ImportApplyResult();
            var updatedRows = new HashSet<long>();
            foreach (var item in items.Where(i => i.CanApply))
            {
                Element elem = doc.GetElement(item.ElementId);
                if (elem == null) { result.Errors.Add($"找不到元素 {item.ElementId.GetIdValue()}"); continue; }
                Parameter p = elem.LookupParameter(item.ParameterName) ?? doc.GetElement(elem.GetTypeId())?.LookupParameter(item.ParameterName);
                if (p == null || p.IsReadOnly) { result.Errors.Add($"無法寫入 {item.ParameterName}"); continue; }
                if (TrySetParameterFromText(p, item.NewValue))
                {
                    result.UpdatedCells++;
                    updatedRows.Add(item.ElementId.GetIdValue());
                }
                else
                {
                    result.Errors.Add($"寫入失敗 {item.ElementId.GetIdValue()}:{item.ParameterName}");
                }
            }
            result.UpdatedRows = updatedRows.Count;
            return result;
        }

        private static string GetParameterDisplayText(Parameter p)
        {
            if (p == null) return string.Empty;
            string vs = p.AsValueString();
            if (!string.IsNullOrWhiteSpace(vs)) return vs;
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? string.Empty;
                case StorageType.Integer: return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double: return p.AsDouble().ToString(CultureInfo.InvariantCulture);
                case StorageType.ElementId: return p.AsElementId()?.GetIdValue().ToString() ?? string.Empty;
                default: return string.Empty;
            }
        }

        private static int FindHeaderRow(ExcelWorksheet ws)
        {
            int max = Math.Min(ws.Dimension.End.Row, 8);
            for (int r = 1; r <= max; r++)
            {
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                {
                    string t = (ws.Cells[r, c].Text ?? string.Empty).Trim();
                    if (IsElementIdHeader(t))
                        return r;
                }
            }
            return 2; // fallback to common export layout
        }

        private static bool IsElementIdHeader(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            return t.Equals("元素ID", StringComparison.OrdinalIgnoreCase)
                || t.Equals("ElementId", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Id", StringComparison.OrdinalIgnoreCase)
                || t.Equals("ID", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseElementId(string text, out ElementId id)
        {
            id = ElementId.InvalidElementId;
            if (long.TryParse(text, out long v))
            {
                id = RevitApiCompatibility.CreateElementId(v);
                return true;
            }
            return false;
        }

        private static bool TrySetParameterFromText(Parameter p, string text)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return p.Set(text);
                    case StorageType.Integer:
                        if (int.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out int i) ||
                            int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out i))
                            return p.Set(i);
                        return false;
                    case StorageType.Double:
                        // 優先用專案單位語意
                        if (p.SetValueString(text)) return true;
                        if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double d) ||
                            double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                            return p.Set(d);
                        return false;
                    case StorageType.ElementId:
                        if (long.TryParse(text, out long eid))
                            return p.Set(RevitApiCompatibility.CreateElementId(eid));
                        return false;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // ── Excel 匯出：單個明細表 ─────────────────────────────────────────────

        private void ExportSingleScheduleToExcel(ViewSchedule schedule, string filePath, bool autoParseNumbers)
        {
            var fileInfo = new FileInfo(filePath);
            using (var package = new ExcelPackage(fileInfo))
            {
                var wsName = TrimSheetName(schedule.Name);
                var ws = package.Workbook.Worksheets.Add(wsName);
                WriteScheduleToWorksheet(schedule, ws, autoParseNumbers);
                package.Save();
            }
        }

        // ── Excel 匯出：多個明細表合併 ────────────────────────────────────────

        private void ExportMultipleSchedulesToExcel(List<ViewSchedule> schedules, string filePath, bool autoParseNumbers)
        {
            var fileInfo = new FileInfo(filePath);
            using (var package = new ExcelPackage(fileInfo))
            {
                var summaryWs = package.Workbook.Worksheets.Add("摘要");
                summaryWs.Cells[1, 1].Value = "明細表匯出摘要";
                summaryWs.Cells[1, 1].Style.Font.Bold = true;
                summaryWs.Cells[1, 1].Style.Font.Size = 14;
                summaryWs.Cells[2, 1].Value = string.Format("匯出時間：{0:yyyy-MM-dd HH:mm:ss}", DateTime.Now);
                summaryWs.Cells[3, 1].Value = "明細表數量：" + schedules.Count;
                summaryWs.Cells[5, 1].Value = "明細表名稱";
                summaryWs.Cells[5, 2].Value = "資料列數";
                summaryWs.Cells[5, 1].Style.Font.Bold = true;
                summaryWs.Cells[5, 2].Style.Font.Bold = true;

                for (int i = 0; i < schedules.Count; i++)
                    summaryWs.Cells[6 + i, 1].Value = schedules[i].Name;

                foreach (var schedule in schedules)
                {
                    var wsName = TrimSheetName(schedule.Name);
                    var existingNames = package.Workbook.Worksheets.Select(w => w.Name).ToHashSet();
                    if (existingNames.Contains(wsName))
                        wsName = TrimSheetName(wsName + "_" + schedule.Id.ToString());

                    var ws = package.Workbook.Worksheets.Add(wsName);
                    WriteScheduleToWorksheet(schedule, ws, autoParseNumbers);
                }

                package.Save();
            }
        }

        // ── 將明細表資料寫入 Excel 工作表 ────────────────────────────────────

        private static readonly HashSet<string> _totalKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "合計", "小計", "總計", "小計：", "合計：", "total", "grand total", "subtotal" };

        private void WriteScheduleToWorksheet(ViewSchedule schedule, ExcelWorksheet ws, bool autoParseNumbers)
        {
            if (TryWriteScheduleByNativeExport(schedule, ws, autoParseNumbers))
            {
                return;
            }

            var tableData = schedule.GetTableData();
            if (tableData == null)
            {
                ws.Cells[1, 1].Value = schedule.Name + "（無法讀取資料）";
                return;
            }

            ws.Cells[1, 1].Value = schedule.Name;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.Font.Size = 13;
            ws.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(31, 73, 125));
            ws.Cells[1, 1].Style.Font.Color.SetColor(SysColor.White);

            int currentRow = 2;

            var headerSection = tableData.GetSectionData(SectionType.Header);
            int headerEndRow = currentRow;
            if (headerSection != null && headerSection.NumberOfRows > 0)
            {
                for (int row = 0; row < headerSection.NumberOfRows; row++)
                {
                    for (int col = 0; col < headerSection.NumberOfColumns; col++)
                    {
                        var cellText = headerSection.GetCellText(row, col);
                        var cell = ws.Cells[currentRow, col + 1];
                        cell.Value = cellText;
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(68, 114, 196));
                        cell.Style.Font.Color.SetColor(SysColor.White);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                        cell.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                        cell.Style.WrapText = true;
                    }
                    currentRow++;
                }
                headerEndRow = currentRow;
            }

            var bodySection = tableData.GetSectionData(SectionType.Body);
            if (bodySection != null && bodySection.NumberOfRows > 0)
            {
                int totalCols = bodySection.NumberOfColumns;
                var bodyHeaders = new string[totalCols];
                for (int col = 0; col < totalCols; col++)
                    bodyHeaders[col] = GetScheduleColumnHeader(schedule, col);
                var scheduleLevelName = InferLevelNameFromScheduleName(schedule.Name);

                for (int row = 0; row < bodySection.NumberOfRows; row++)
                {
                    var firstCell = bodySection.GetCellText(row, 0) ?? "";
                    firstCell = firstCell.Trim();
                    bool isTotalRow = _totalKeywords.Contains(firstCell) ||
                                     firstCell.StartsWith("合計") || firstCell.StartsWith("小計");
                    bool isEven = (row % 2 == 1) && !isTotalRow;

                    for (int col = 0; col < totalCols; col++)
                    {
                        var cellText = NormalizeExportCellText(
                            bodySection.GetCellText(row, col),
                            bodyHeaders.Length > col ? bodyHeaders[col] : string.Empty,
                            scheduleLevelName);
                        var cell = ws.Cells[currentRow, col + 1];

                        cell.Value = autoParseNumbers ? TryParseNumeric(cellText) : (object)cellText;

                        if (cell.Value is double)
                        {
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            cell.Style.Numberformat.Format = "#,##0.##";
                        }

                        if (isTotalRow)
                        {
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(255, 242, 204));
                            cell.Style.Border.Top.Style = ExcelBorderStyle.Medium;
                            cell.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
                        }
                        else if (isEven)
                        {
                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(235, 241, 251));
                        }

                        cell.Style.Border.BorderAround(ExcelBorderStyle.Hair);
                    }
                    currentRow++;
                }

                if (totalCols > 1)
                    ws.Cells[1, 1, 1, totalCols].Merge = true;

                ws.View.FreezePanes(headerEndRow, 1);

                try { ws.Cells[ws.Dimension.Address].AutoFitColumns(8, 50); } catch { }
            }
        }

        /// <summary>
        /// 優先使用 Revit 原生明細表匯出，確保欄位與值和畫面一致。
        /// </summary>
        private bool TryWriteScheduleByNativeExport(ViewSchedule schedule, ExcelWorksheet ws, bool autoParseNumbers)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "YD_ScheduleExport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string fileName = SanitizeFileName(schedule.Name) + ".txt";
            string filePath = Path.Combine(tempDir, fileName);

            try
            {
                var opt = new ViewScheduleExportOptions
                {
                    ColumnHeaders = ExportColumnHeaders.OneRow,
                    FieldDelimiter = "\t",
                    TextQualifier = ExportTextQualifier.DoubleQuote,
                    HeadersFootersBlanks = true
                };

                schedule.Export(tempDir, fileName, opt);
                if (!File.Exists(filePath))
                {
                    return false;
                }

                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length == 0)
                {
                    return false;
                }

                ws.Cells[1, 1].Value = schedule.Name;
                ws.Cells[1, 1].Style.Font.Bold = true;
                ws.Cells[1, 1].Style.Font.Size = 13;
                ws.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(31, 73, 125));
                ws.Cells[1, 1].Style.Font.Color.SetColor(SysColor.White);

                int rowIndex = 2;
                int maxCols = 1;
                string[] headers = null;
                var scheduleLevelName = InferLevelNameFromScheduleName(schedule.Name);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] ?? string.Empty;
                    // Revit 以 Tab 分隔；雙引號外層可去除
                    string[] cols = line.Split('\t');
                    maxCols = Math.Max(maxCols, cols.Length);

                    bool isHeader = (i == 0);
                    if (isHeader)
                        headers = cols.Select(Unquote).ToArray();
                    string firstCell = cols.Length > 0 ? (cols[0] ?? string.Empty).Trim() : string.Empty;
                    bool isTotalRow = _totalKeywords.Contains(firstCell) ||
                                      firstCell.StartsWith("合計", StringComparison.OrdinalIgnoreCase) ||
                                      firstCell.StartsWith("小計", StringComparison.OrdinalIgnoreCase) ||
                                      firstCell.StartsWith("總計", StringComparison.OrdinalIgnoreCase);

                    for (int c = 0; c < cols.Length; c++)
                    {
                        string text = Unquote(cols[c]);
                        if (!isHeader)
                        {
                            string header = headers != null && c < headers.Length ? headers[c] : string.Empty;
                            text = NormalizeExportCellText(text, header, scheduleLevelName);
                        }
                        var cell = ws.Cells[rowIndex, c + 1];
                        cell.Value = autoParseNumbers ? TryParseNumeric(text) : (object)text;

                        if (isHeader)
                        {
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(68, 114, 196));
                            cell.Style.Font.Color.SetColor(SysColor.White);
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        }
                        else if (isTotalRow)
                        {
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(255, 242, 204));
                            cell.Style.Border.Top.Style = ExcelBorderStyle.Medium;
                            cell.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
                        }
                        else if ((i % 2) == 1)
                        {
                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(235, 241, 251));
                        }

                        if (cell.Value is double)
                        {
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            cell.Style.Numberformat.Format = "#,##0.##";
                        }
                        else if (!isHeader)
                        {
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
                        }

                        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                        cell.Style.Border.BorderAround(ExcelBorderStyle.Hair);
                    }

                    rowIndex++;
                }

                if (maxCols > 1)
                    ws.Cells[1, 1, 1, maxCols].Merge = true;

                ws.View.FreezePanes(3, 1); // 標題 + 欄名
                try { ws.Cells[ws.Dimension.Address].AutoFitColumns(8, 50); } catch { }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }

        private static string Unquote(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string v = value.Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
            {
                v = v.Substring(1, v.Length - 2);
            }
            return v.Replace("\"\"", "\"");
        }

        private static string NormalizeExportCellText(string text, string header, string scheduleLevelName)
        {
            string value = (text ?? string.Empty).Trim();
            string h = (header ?? string.Empty).Trim();

            if (IsLevelHeader(h))
            {
                if (string.IsNullOrWhiteSpace(value) || IsExportOutlineNumber(value))
                    return string.IsNullOrWhiteSpace(scheduleLevelName) ? value : scheduleLevelName;

                return NormalizeLevelName(value);
            }

            if (IsMarkOrNumberHeader(h))
                return StripExportOutlineNumber(value);

            return value;
        }

        private static string GetScheduleColumnHeader(ViewSchedule schedule, int columnIndex)
        {
            try
            {
                var definition = schedule?.Definition;
                if (definition == null || columnIndex < 0 || columnIndex >= definition.GetFieldCount())
                    return string.Empty;

                var field = definition.GetField(columnIndex);
                return field?.GetName() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsLevelHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return false;
            string normalized = header.Replace(" ", string.Empty);
            return normalized.IndexOf("樓層", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMarkOrNumberHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return false;
            string normalized = header.Replace(" ", string.Empty);
            return normalized.IndexOf("編號", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("備註", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("Mark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("Number", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsExportOutlineNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Regex.IsMatch(value.Trim(), @"^\d+\.$");
        }

        private static string StripExportOutlineNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return Regex.Replace(value.Trim(), @"^\d+\.\s*", string.Empty);
        }

        private static string InferLevelNameFromScheduleName(string scheduleName)
        {
            if (string.IsNullOrWhiteSpace(scheduleName)) return string.Empty;

            var match = Regex.Match(scheduleName, @"(?<![A-Za-z0-9])([BFR]?\d+)\s*(?:FL|F|樓|層)(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
            if (!match.Success)
                return string.Empty;

            return NormalizeLevelName(match.Groups[1].Value + "FL");
        }

        private static string NormalizeLevelName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string text = value.Trim();
            var match = Regex.Match(text, @"^([BFR]?\d+)\s*(?:FL|F|樓|層)$", RegexOptions.IgnoreCase);
            if (!match.Success)
                return text;

            return match.Groups[1].Value.ToUpperInvariant() + "FL";
        }

        // ── 數值解析 ──────────────────────────────────────────────────────────

        private static readonly Regex _numericPattern =
            new Regex(@"^\s*(-?[\d,]+\.?\d*)\s*[a-zA-Z\s%]*$");

        private static object TryParseNumeric(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var m = _numericPattern.Match(text);
            if (m.Success)
            {
                var numStr = m.Groups[1].Value.Replace(",", "");
                if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    return val;
            }
            return text;
        }

        // ── PDF 匯出 ──────────────────────────────────────────────────────────

        private void ExportSchedulesToPdf(Document doc, List<ViewSchedule> schedules,
            string outputFolder, bool combineIntoOne)
        {
            var viewIds = schedules.Select(s => s.Id).ToList();
            var options = new PDFExportOptions { Combine = combineIntoOne };
            options.FileName = combineIntoOne
                ? string.Format("明細表匯出_{0:yyyyMMdd_HHmmss}", DateTime.Now)
                : (schedules.Count == 1 ? SanitizeFileName(schedules[0].Name) : "明細表");
            doc.Export(outputFolder, viewIds, options);
        }

        // ── 工具方法 ──────────────────────────────────────────────────────────

        private static string SanitizeFileName(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('_');
        }

        private static string TrimSheetName(string name)
        {
            var invalid = new HashSet<char>(new[] { '/', '\\', '?', '*', '[', ']', ':' });
            var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return clean.Length > 31 ? clean.Substring(0, 31) : clean;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ScheduleInfo：明細表預覽資訊
    // ═══════════════════════════════════════════════════════════════════════════

    internal class ScheduleInfo
    {
        public ViewSchedule Schedule { get; }
        public string Name => Schedule.Name;
        public int BodyRows { get; }
        public int ColCount { get; }
        public string Category { get; }

        public ScheduleInfo(ViewSchedule vs)
        {
            Schedule = vs;
            try
            {
                var body = vs.GetTableData().GetSectionData(SectionType.Body);
                BodyRows = body != null ? body.NumberOfRows : 0;
                ColCount  = body != null ? body.NumberOfColumns : 0;
            }
            catch { BodyRows = 0; ColCount = 0; }
            Category = DetectCategory(vs.Name);
        }

        private static string DetectCategory(string name)
        {
            if (name.Contains("材料") || name.Contains("材資")) return "材料";
            if (name.Contains("房間") || name.Contains("室內") || name.Contains("Room")) return "房間";
            if (name.Contains("門") || name.Contains("窗")) return "門窗";
            if (name.Contains("面積") || name.Contains("Area")) return "面積";
            if (name.Contains("管") || name.Contains("風管") || name.Contains("MEP") || name.Contains("機電")) return "機電";
            return "一般";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WinForms 選擇對話框 — ListView 介面（相容 Revit 2024~2026）
    // ═══════════════════════════════════════════════════════════════════════════

    internal enum ExportFormat { Excel, Pdf }

    internal class ImportApplyResult
    {
        public int TotalRows { get; set; }
        public int UpdatedRows { get; set; }
        public int UpdatedCells { get; set; }
        public int SkippedRows { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }
    internal class ImportPreviewResult
    {
        public int TotalRows { get; set; }
        public int SkippedRows { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public List<ImportDiffItem> Items { get; } = new List<ImportDiffItem>();
    }
    internal class ImportDiffItem
    {
        public bool Selected { get; set; } = true;
        public ElementId ElementId { get; set; }
        public string ElementName { get; set; }
        public string ParameterName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public bool CanApply { get; set; }
    }

    internal class ImportPreviewDialog : WinForm
    {
        private readonly DataGridView _grid;
        private readonly ImportPreviewResult _preview;
        private const int FixedPreviewColsWidth = 50 + 90 + 160 + 300 + 300 + 26;
        private readonly WinLabel _lblStatus;
        private readonly WinCheckBox _cbOnlySelected;
        private readonly WinTextBox _txtParamFilter;
        private readonly Action<List<ImportDiffItem>> _applyAction;
        private readonly WinButton _btnApply;
        public List<ImportDiffItem> SelectedItems { get; private set; } = new List<ImportDiffItem>();

        public ImportPreviewDialog(ImportPreviewResult preview, Action<List<ImportDiffItem>> applyAction)
        {
            _preview = preview;
            _applyAction = applyAction;
            Text = "匯入對照預覽";
            Width = 1080;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft JhengHei UI", 9f);

            var pnlTop = new WinPanel
            {
                Left = 10, Top = 10, Width = 1044, Height = 34,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            pnlTop.Controls.Add(new WinLabel { Text = "參數篩選：", Left = 0, Top = 9, AutoSize = true });
            _txtParamFilter = new WinTextBox { Left = 64, Top = 6, Width = 220, Height = 24 };
            _txtParamFilter.TextChanged += (s, e) => RebindGrid();
            pnlTop.Controls.Add(_txtParamFilter);
            var btnAll = new WinButton { Text = "全選", Left = 300, Top = 4, Width = 64, Height = 26 };
            var btnNone = new WinButton { Text = "全不選", Left = 370, Top = 4, Width = 74, Height = 26 };
            var btnInvert = new WinButton { Text = "反選", Left = 450, Top = 4, Width = 64, Height = 26 };
            btnAll.Click += (s, e) => { SetVisibleRowsChecked(true); UpdatePreviewStatus(); };
            btnNone.Click += (s, e) => { SetVisibleRowsChecked(false); UpdatePreviewStatus(); };
            btnInvert.Click += (s, e) => { InvertVisibleRows(); UpdatePreviewStatus(); };
            pnlTop.Controls.AddRange(new WinControl[] { btnAll, btnNone, btnInvert });
            _cbOnlySelected = new WinCheckBox { Text = "只看已勾選", Left = 528, Top = 8, Width = 100 };
            _cbOnlySelected.CheckedChanged += (s, e) => RebindGrid();
            pnlTop.Controls.Add(_cbOnlySelected);

            _grid = new DataGridView
            {
                Left = 10, Top = 48, Width = 1044, Height = 560,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom,
                AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false
            };
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Sel", HeaderText = "套用", Width = 50 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "元素ID", Width = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Param", HeaderText = "參數", Width = 160 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Old", HeaderText = "舊值", Width = 300 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "New", HeaderText = "新值", Width = 300 });
            _grid.Resize += (s, e) => AdjustGridColumns();
            _grid.CellValueChanged += (s, e) => { if (e.ColumnIndex == 0) UpdatePreviewStatus(); };
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            _lblStatus = new WinLabel
            {
                Left = 10, Top = 614, Width = 640, Height = 24,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                ForeColor = SysColor.FromArgb(68, 114, 196)
            };

            _btnApply = new WinButton { Text = "套用勾選項目", Left = 844, Top = 612, Width = 210, Height = 36, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            _btnApply.Click += (s, e) =>
            {
                SelectedItems.Clear();
                for (int i = 0; i < _grid.Rows.Count; i++)
                {
                    bool sel = Convert.ToBoolean(_grid.Rows[i].Cells[0].Value ?? false);
                    if (!sel) continue;
                    if (_grid.Rows[i].Tag is ImportDiffItem item)
                    {
                        item.Selected = true;
                        SelectedItems.Add(item);
                    }
                }
                if (SelectedItems.Count == 0)
                {
                    MessageBox.Show("請至少勾選一筆差異。");
                    return;
                }
                _btnApply.Enabled = false;
                _btnApply.Text = "套用中...";
                _applyAction(SelectedItems.ToList());
            };
            var btnCancel = new WinButton { Text = "關閉", Left = 754, Top = 612, Width = 80, Height = 36, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            btnCancel.Click += (s, e) => Close();
            Controls.AddRange(new WinControl[] { pnlTop, _grid, _lblStatus, btnCancel, _btnApply });
            this.Resize += (s, e) => AdjustGridColumns();
            RebindGrid();
            RestoreGridColumnWidths();
            AdjustGridColumns();
            this.FormClosing += (s, e) => SaveGridColumnWidths();
        }

        public void SetRequestCompleted(string error = null)
        {
            if (IsDisposed) return;
            _btnApply.Enabled = true;
            _btnApply.Text = "套用勾選項目";
            if (!string.IsNullOrWhiteSpace(error))
                MessageBox.Show(error, "匯入對照", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void AdjustGridColumns()
        {
            if (_grid.Columns.Count < 5) return;
            int extra = _grid.ClientSize.Width - FixedPreviewColsWidth;
            int dynamicWidth = Math.Max(220, 300 + (extra / 2));
            _grid.Columns["Old"].Width = dynamicWidth;
            _grid.Columns["New"].Width = dynamicWidth;
        }

        private void RebindGrid()
        {
            var filter = (_txtParamFilter.Text ?? string.Empty).Trim();
            _grid.Rows.Clear();
            foreach (var it in _preview.Items)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    (it.ParameterName ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (_cbOnlySelected.Checked && !it.Selected)
                    continue;

                int r = _grid.Rows.Add(it.Selected, it.ElementId.GetIdValue(), it.ParameterName, it.OldValue, it.NewValue);
                _grid.Rows[r].Tag = it;
            }
            UpdatePreviewStatus();
        }

        private void SetVisibleRowsChecked(bool value)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                row.Cells[0].Value = value;
                if (row.Tag is ImportDiffItem it) it.Selected = value;
            }
        }

        private void InvertVisibleRows()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                bool cur = Convert.ToBoolean(row.Cells[0].Value ?? false);
                row.Cells[0].Value = !cur;
                if (row.Tag is ImportDiffItem it) it.Selected = !cur;
            }
        }

        private void UpdatePreviewStatus()
        {
            int total = _preview.Items.Count;
            int visible = _grid.Rows.Count;
            int selected = 0;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (Convert.ToBoolean(row.Cells[0].Value ?? false)) selected++;
                if (row.Tag is ImportDiffItem it) it.Selected = Convert.ToBoolean(row.Cells[0].Value ?? false);
            }
            _lblStatus.Text = $"差異總數：{total}　目前顯示：{visible}　已勾選：{selected}";
        }

        private void SaveGridColumnWidths()
        {
            try
            {
                var p = new Dictionary<string, int>();
                foreach (DataGridViewColumn c in _grid.Columns) p[c.Name] = c.Width;
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YD_BIM_Tools");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "ImportPreviewColumns.txt");
                File.WriteAllLines(file, p.Select(kv => kv.Key + "=" + kv.Value));
            }
            catch { }
        }

        private void RestoreGridColumnWidths()
        {
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YD_BIM_Tools", "ImportPreviewColumns.txt");
                if (!File.Exists(file)) return;
                foreach (var line in File.ReadAllLines(file))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    if (_grid.Columns.Contains(parts[0]) && int.TryParse(parts[1], out int w))
                    {
                        _grid.Columns[parts[0]].Width = Math.Max(40, w);
                    }
                }
            }
            catch { }
        }
    }

    internal class ScheduleExportDialog : WinForm
    {
        public List<ViewSchedule> SelectedSchedules { get; private set; } = new List<ViewSchedule>();
        public ExportFormat ExportFormat { get; private set; } = ExportFormat.Excel;
        public string OutputFolder { get; private set; } = string.Empty;
        public bool CombineIntoOne { get; private set; } = false;
        public bool AutoParseNumbers { get; private set; } = true;
        public bool ImportMode { get; private set; } = false;
        public string ImportFilePath { get; private set; } = string.Empty;

        private readonly ListView _listView;
        private readonly WinRadioButton _rbExcel;
        private readonly WinRadioButton _rbPdf;
        private readonly WinCheckBox _cbCombine;
        private readonly WinCheckBox _cbAutoNum;
        private readonly WinTextBox _txtFolder;
        private readonly WinTextBox _txtSearch;
        private readonly WinLabel _lblStatus;
        private readonly WinLabel _lblHeaderTitle;
        private readonly WinLabel _lblHeaderSubTitle;
        private readonly WinLabel _lblFilterSearch;
        private readonly WinLabel _lblFilterCategory;
        private readonly WinButton _btnSelectAll;
        private readonly WinButton _btnSelectNone;
        private readonly WinPanel _pnlHeader;
        private readonly WinPanel _pnlFilter;
        private readonly WinGroupBox _gbOptions;
        private readonly WinGroupBox _gbFolder;
        private readonly WinGroupBox _gbImport;
        private readonly WinPanel _pnlBtns;
        private readonly WinButton _btnExport;
        private readonly WinButton _btnCancel;
        private readonly WinButton _btnImportApply;
        private readonly WinLabel _lblImportHint;
        private readonly System.Windows.Forms.ComboBox _cboCategory;
        private readonly List<ScheduleInfo> _allInfos;
        private readonly Action<bool> _requestAction;
        private bool _requestPending;
        private const int FixedColsWidth = 80 + 70 + 90 + 28; // 資料列 + 欄數 + 分類 + 預留

        private static readonly string[] _categories = { "全部", "材料", "房間", "門窗", "面積", "機電", "一般" };

        public ScheduleExportDialog(List<ScheduleInfo> infos, Action<bool> requestAction)
        {
            _allInfos = infos;
            _requestAction = requestAction;

            Text = "YD BIM Tools - 明細表匯出";
            Width = 760;
            Height = 820;
            MinimumSize = new Size(760, 780);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = new Font("Microsoft JhengHei UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;

            // ── 標題列 ────────────────────────────────────────────────────────
            _pnlHeader = new WinPanel { Dock = DockStyle.Top, Height = 62, BackColor = SysColor.FromArgb(31, 73, 125) };
            _lblHeaderTitle = new WinLabel
            {
                Text = "明細表匯出工具",
                Font = new Font("Microsoft JhengHei UI", 13f, FontStyle.Bold),
                ForeColor = SysColor.White, Left = 14, Top = 6, AutoSize = true
            };
            _pnlHeader.Controls.Add(_lblHeaderTitle);
            _lblHeaderSubTitle = new WinLabel
            {
                Text = "共 " + infos.Count + " 個明細表  支援 Excel / PDF 雙格式匯出",
                Font = new Font("Microsoft JhengHei UI", 8.5f),
                ForeColor = SysColor.FromArgb(200, 220, 255), Left = 16, Top = 36, AutoSize = false
            };
            _pnlHeader.Controls.Add(_lblHeaderSubTitle);

            // ── 搜尋 / 篩選列 ─────────────────────────────────────────────────
            _pnlFilter = new WinPanel { Left = 10, Top = 70, Width = 724, Height = 36 };
            _lblFilterSearch = new WinLabel { Text = "搜尋：", Left = 0, Top = 7, AutoSize = true };
            _pnlFilter.Controls.Add(_lblFilterSearch);
            _txtSearch = new WinTextBox { Left = 42, Top = 4, Width = 250, Height = 24 };
            _txtSearch.TextChanged += (o, e) => RefreshList();
            _pnlFilter.Controls.Add(_txtSearch);

            _lblFilterCategory = new WinLabel { Text = "分類：", Left = 308, Top = 7, AutoSize = true };
            _pnlFilter.Controls.Add(_lblFilterCategory);
            _cboCategory = new System.Windows.Forms.ComboBox
            {
                Left = 344, Top = 4, Width = 110, DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
            };
            foreach (var c in _categories) _cboCategory.Items.Add(c);
            _cboCategory.SelectedIndex = 0;
            _cboCategory.SelectedIndexChanged += (o, e) => RefreshList();
            _pnlFilter.Controls.Add(_cboCategory);

            _btnSelectAll = new WinButton { Text = "全選", Left = 470, Top = 4, Width = 70, Height = 24 };
            _btnSelectNone = new WinButton { Text = "全不選", Left = 546, Top = 4, Width = 78, Height = 24 };
            _btnSelectAll.Click += (o, e) => { foreach (ListViewItem i in _listView.Items) i.Checked = true; };
            _btnSelectNone.Click += (o, e) => { foreach (ListViewItem i in _listView.Items) i.Checked = false; };
            _pnlFilter.Controls.AddRange(new WinControl[] { _btnSelectAll, _btnSelectNone });

            // ── ListView ──────────────────────────────────────────────────────
            _listView = new ListView
            {
                Left = 10, Top = 112, Width = 724, Height = 260,
                View = System.Windows.Forms.View.Details,
                CheckBoxes = true, FullRowSelect = true,
                GridLines = true, MultiSelect = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
            };
            _listView.Columns.Add("明細表名稱", 460);
            _listView.Columns.Add("資料列", 80, HorizontalAlignment.Right);
            _listView.Columns.Add("欄數", 70, HorizontalAlignment.Right);
            _listView.Columns.Add("分類", 90);
            _listView.ItemChecked += (o, e) => UpdateStatus();
            _listView.Resize += (o, e) => AdjustListColumns();
            PopulateListView(_allInfos);

            // ── 狀態列 ────────────────────────────────────────────────────────
            _lblStatus = new WinLabel
            {
                Left = 10, Top = 376, Width = 724, Height = 20,
                ForeColor = SysColor.FromArgb(68, 114, 196),
                Font = new Font("Microsoft JhengHei UI", 8.5f)
            };

            // ── 匯出設定 ──────────────────────────────────────────────────────
            _gbOptions = new WinGroupBox { Text = "匯出設定", Left = 10, Top = 402, Width = 724, Height = 124 };
            _gbOptions.Controls.Add(new WinLabel { Text = "格式：", Left = 10, Top = 22, AutoSize = true });
            _rbExcel = new WinRadioButton { Text = "Excel (.xlsx)  可計算 / 可匯入對照", Left = 50, Top = 20, Width = 290, Checked = true };
            _rbPdf   = new WinRadioButton { Text = "PDF (.pdf)  列印視圖",               Left = 350, Top = 20, Width = 180 };
            _rbExcel.CheckedChanged += (o, e) => _cbAutoNum.Enabled = _rbExcel.Checked;
            _gbOptions.Controls.AddRange(new WinControl[] { _rbExcel, _rbPdf });

            _cbCombine = new WinCheckBox
            {
                Text = "合併所有明細表至單一檔案（Excel: 多工作表；PDF: 單一 PDF）",
                Left = 50, Top = 48, Width = 650, Checked = true
            };
            _cbAutoNum = new WinCheckBox
            {
                Text = "數值自動識別（數字存為數值，方便 Excel 公式與小計）",
                Left = 50, Top = 76, Width = 650, Checked = true
            };
            _gbOptions.Controls.AddRange(new WinControl[] { _cbCombine, _cbAutoNum });

            // ── 輸出資料夾 ────────────────────────────────────────────────────
            _gbFolder = new WinGroupBox { Text = "輸出資料夾", Left = 10, Top = 534, Width = 724, Height = 62 };
            _txtFolder = new WinTextBox
            {
                Left = 10, Top = 24, Width = 596, Height = 24,
                Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                ReadOnly = true, BackColor = SysColor.White
            };
            var btnBrowse = new WinButton { Text = "瀏覽...", Left = 616, Top = 22, Width = 90, Height = 26 };
            btnBrowse.Click += (o, e) =>
            {
                using (var fbd = new FolderBrowserDialog { SelectedPath = _txtFolder.Text, Description = "選擇輸出資料夾" })
                    if (fbd.ShowDialog() == DialogResult.OK) _txtFolder.Text = fbd.SelectedPath;
            };
            _gbFolder.Controls.AddRange(new WinControl[] { _txtFolder, btnBrowse });

            // ── 匯入對照 ─────────────────────────────────────────────────────
            _gbImport = new WinGroupBox { Text = "匯入對照（批次修改可變參數）", Left = 10, Top = 602, Width = 724, Height = 82 };
            _lblImportHint = new WinLabel
            {
                Text = "先勾選 1 個明細表，再選 Excel（需含元素ID欄）",
                Left = 10, Top = 26, AutoSize = true, ForeColor = SysColor.FromArgb(80, 80, 80)
            };
            _btnImportApply = new WinButton
            {
                Text = "匯入對照並套用",
                Left = 554, Top = 30, Width = 152, Height = 34,
                BackColor = SysColor.FromArgb(34, 139, 34), ForeColor = SysColor.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnImportApply.FlatAppearance.BorderSize = 0;
            _btnImportApply.Click += BtnImportApply_Click;
            _gbImport.Controls.AddRange(new WinControl[] { _lblImportHint, _btnImportApply });

            // ── 按鈕列 ────────────────────────────────────────────────────────
            _pnlBtns = new WinPanel { Left = 10, Top = 692, Width = 724, Height = 44 };
            _btnExport = new WinButton
            {
                Text = "匯出明細表", Width = 140, Height = 34, Left = 496, Top = 4,
                BackColor = SysColor.FromArgb(68, 114, 196), ForeColor = SysColor.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft JhengHei UI", 10f, FontStyle.Bold)
            };
            _btnExport.FlatAppearance.BorderSize = 0;
            _btnExport.Click += BtnOk_Click;
            _btnCancel = new WinButton { Text = "關閉", Width = 86, Height = 34, Left = 648, Top = 4, FlatStyle = FlatStyle.Flat };
            _btnCancel.Click += (o, e) => Close();
            _pnlBtns.Controls.AddRange(new WinControl[] { _btnExport, _btnCancel });

            AcceptButton = _btnExport;
            CancelButton = _btnCancel;

            Controls.AddRange(new WinControl[]
            {
                _pnlHeader, _pnlFilter, _listView, _lblStatus,
                _gbOptions, _gbFolder, _gbImport, _pnlBtns
            });

            this.Resize += (o, e) => ApplyResponsiveLayout();
            ApplyResponsiveLayout();
            AdjustListColumns();
            UpdateStatus();
        }

        private void ApplyResponsiveLayout()
        {
            int margin = 10;
            int contentWidth = Math.Max(680, ClientSize.Width - margin * 2);
            int yTop = _pnlHeader.Bottom + 8;
            _lblHeaderSubTitle.Left = 16;
            _lblHeaderSubTitle.Top = 36;
            _lblHeaderSubTitle.Width = Math.Max(220, _pnlHeader.ClientSize.Width - 32);

            // 篩選列靠右按鈕，避免重疊
            _btnSelectNone.Left = _pnlFilter.ClientSize.Width - _btnSelectNone.Width;
            _btnSelectAll.Left = _btnSelectNone.Left - _btnSelectAll.Width - 8;
            _cboCategory.Left = _btnSelectAll.Left - _cboCategory.Width - 14;
            _lblFilterCategory.Left = _cboCategory.Left - _lblFilterCategory.Width - 6;
            _txtSearch.Left = _lblFilterSearch.Right + 6;
            _txtSearch.Width = Math.Max(120, _lblFilterCategory.Left - _txtSearch.Left - 12);

            // 底部區塊固定貼底，確保匯出/取消永遠可見
            _pnlBtns.SetBounds(margin, ClientSize.Height - margin - _pnlBtns.Height, contentWidth, _pnlBtns.Height);

            _gbImport.SetBounds(margin, _pnlBtns.Top - 8 - _gbImport.Height, contentWidth, _gbImport.Height);
            _gbFolder.SetBounds(margin, _gbImport.Top - 6 - _gbFolder.Height, contentWidth, _gbFolder.Height);
            _gbOptions.SetBounds(margin, _gbFolder.Top - 6 - _gbOptions.Height, contentWidth, _gbOptions.Height);
            _lblStatus.SetBounds(margin, _gbOptions.Top - 6 - _lblStatus.Height, contentWidth, _lblStatus.Height);

            _pnlFilter.SetBounds(margin, yTop, contentWidth, 36);
            int listTop = _pnlFilter.Bottom + 6;
            int listHeight = Math.Max(140, _lblStatus.Top - 4 - listTop);
            _listView.SetBounds(margin, listTop, contentWidth, listHeight);

            // 內部控制項重新貼齊
            if (_txtFolder.Parent != null)
            {
                _txtFolder.Width = Math.Max(280, _gbFolder.ClientSize.Width - 118);
            }

            _btnImportApply.Left = Math.Max(12, _gbImport.ClientSize.Width - _btnImportApply.Width - 12);
            _lblImportHint.MaximumSize = new Size(Math.Max(260, _btnImportApply.Left - 24), 0);

            _btnCancel.Left = _pnlBtns.ClientSize.Width - _btnCancel.Width;
            _btnExport.Left = _btnCancel.Left - _btnExport.Width - 10;
        }

        private void AdjustListColumns()
        {
            if (_listView.Columns.Count < 4) return;
            int w = Math.Max(260, _listView.ClientSize.Width - FixedColsWidth);
            _listView.Columns[0].Width = w;
        }

        private void PopulateListView(IEnumerable<ScheduleInfo> items)
        {
            _listView.Items.Clear();
            foreach (var info in items)
            {
                var item = new ListViewItem(info.Name);
                item.SubItems.Add(info.BodyRows.ToString());
                item.SubItems.Add(info.ColCount.ToString());
                item.SubItems.Add(info.Category);
                item.Tag = info;
                item.Checked = true;
                _listView.Items.Add(item);
            }
            UpdateStatus();
        }

        private void RefreshList()
        {
            var keyword = _txtSearch.Text.Trim();
            var cat = _cboCategory.SelectedItem != null ? _cboCategory.SelectedItem.ToString() : "全部";
            var filtered = _allInfos.AsEnumerable();
            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(i => i.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            if (cat != "全部")
                filtered = filtered.Where(i => i.Category == cat);
            PopulateListView(filtered);
        }

        private void UpdateStatus()
        {
            if (_lblStatus == null) return; // 建構式尚未完成時的保護
            int total = _listView.Items.Count;
            int checkedCount = _listView.CheckedItems.Count;
            _lblStatus.Text = string.Format("顯示 {0} 個，已勾選 {1} 個明細表（共 {2} 個）",
                total, checkedCount, _allInfos.Count);
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            ImportMode = false;
            var selected = _listView.CheckedItems
                .Cast<ListViewItem>()
                .Select(i => ((ScheduleInfo)i.Tag).Schedule)
                .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show("請至少勾選一個明細表。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtFolder.Text) || !Directory.Exists(_txtFolder.Text))
            {
                MessageBox.Show("輸出資料夾不存在，請重新選擇。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedSchedules = selected;
            ExportFormat = _rbPdf.Checked ? ExportFormat.Pdf : ExportFormat.Excel;
            OutputFolder = _txtFolder.Text;
            CombineIntoOne = _cbCombine.Checked;
            AutoParseNumbers = _cbAutoNum.Checked;

            BeginRequest(false);
        }

        private void BtnImportApply_Click(object sender, EventArgs e)
        {
            var selected = _listView.CheckedItems
                .Cast<ListViewItem>()
                .Select(i => ((ScheduleInfo)i.Tag).Schedule)
                .ToList();

            if (selected.Count != 1)
            {
                MessageBox.Show("匯入對照請勾選 1 個明細表。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var ofd = new OpenFileDialog
            {
                Title = "選擇匯入對照 Excel",
                Filter = "Excel 檔案 (*.xlsx)|*.xlsx",
                Multiselect = false
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                ImportFilePath = ofd.FileName;
            }

            SelectedSchedules = selected;
            ImportMode = true;
            BeginRequest(true);
        }

        private void BeginRequest(bool importMode)
        {
            if (_requestPending)
            {
                MessageBox.Show("上一個 Revit 動作仍在處理中，請稍候。", "明細表匯出");
                return;
            }

            _requestPending = true;
            _btnExport.Enabled = false;
            _btnImportApply.Enabled = false;
            _requestAction(importMode);
        }

        public void SetRequestCompleted(string error = null)
        {
            if (IsDisposed) return;
            _requestPending = false;
            _btnExport.Enabled = true;
            _btnImportApply.Enabled = true;
            if (!string.IsNullOrWhiteSpace(error))
                MessageBox.Show(error, "明細表匯出", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
