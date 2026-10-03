using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager;
using YD_RevitTools.LicenseManager.Helpers.Data;
using OfficeOpenXml;

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.Manual)]
    public class CmdCobieImportEnhanced : IExternalCommand
    {
        private class FailRow
        {
            public string Reason, SourceRow, UniqueId, ElementId, Mark, FamilyName, TypeName, FamilyType, Field, Value;
        }

        private sealed class PlannedWrite
        {
            public Element Owner;
            public Parameter Parameter;
            public object Value;
            public bool IsType;
            public FailRow Source;
            public string Key => Owner.UniqueId + ":" + ParamTypeCompat.ElementIdToString(Parameter.Id);
        }

        private sealed class RollbackImportErrors : IFailuresPreprocessor
        {
            public readonly List<string> Messages = new List<string>();
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                bool hasError = false;
                foreach (var failure in accessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning) continue;
                    Messages.Add(failure.GetDescriptionText());
                    hasError = true;
                }
                return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }

        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet set)
        {
            // 檢查授權 - COBie 匯入功能
            var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("COBie.Import"))
            {
                TaskDialog.Show("授權限制",
                    "您的授權版本不支援 COBie 匯入功能。\n\n" +
                    "此功能僅適用於標準版和專業版授權。\n\n" +
                    "試用版用戶可以使用「COBie 欄位管理」和「COBie 範本」功能。\n\n" +
                    "點擊「授權管理」按鈕以查看或升級授權。");
                return Result.Cancelled;
            }

            var uidoc = cd.Application.ActiveUIDocument;
            if (uidoc == null) { msg = "沒有開啟的文件。"; return Result.Failed; }
            var doc = uidoc.Document;

            try
            {
                // 設定 EPPlus 授權模式（非商業用途）
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                var ofd = new OpenFileDialog
                {
                    Filter = "COBie 檔案|*.csv;*.xlsx|CSV 檔案 (*.csv)|*.csv|Excel 檔案 (*.xlsx)|*.xlsx|所有檔案 (*.*)|*.*",
                    Title = "選擇 COBie 匯入檔案 (CSV 或 Excel .xlsx)"
                };
                if (ofd.ShowDialog() != DialogResult.OK) return Result.Cancelled;

                var cfgs = CobieConfigIO.LoadConfig();

                // 建立雙重映射：同時支援 DisplayName（中文）和 CobieName（英文）
                var map = new Dictionary<string, CmdCobieFieldManager.CobieFieldConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var cfg in cfgs.Where(c => c.ImportEnabled))
                {
                    // 使用 DisplayName 作為 key（中文名稱，如「製造商」）
                    if (!string.IsNullOrWhiteSpace(cfg.DisplayName))
                    {
                        var displayKey = cfg.DisplayName.Trim();
                        if (!map.ContainsKey(displayKey))
                            map[displayKey] = cfg;
                    }

                    // 同時使用 CobieName 作為 key（英文名稱，如 "Component.Manufacturer"）
                    if (!string.IsNullOrWhiteSpace(cfg.CobieName))
                    {
                        var cobieKey = cfg.CobieName.Trim();
                        if (!map.ContainsKey(cobieKey))
                            map[cobieKey] = cfg;
                    }
                }

                if (map.Count == 0)
                {
                    TaskDialog.Show("COBie 匯入", "尚未啟用任何可匯入欄位，請先於「COBie 欄位管理」勾選匯入欄位。");
                    return Result.Cancelled;
                }

                // 根據檔案類型讀取資料
                List<string> headers;
                List<List<string>> rows;
                int firstDataRow = 2;

                string fileExt = Path.GetExtension(ofd.FileName).ToLower();
                if (fileExt == ".xls")
                {
                    TaskDialog.Show("COBie 匯入", "不支援舊版 Excel .xls 格式。請先另存為 .xlsx 或 .csv 後再匯入。");
                    return Result.Cancelled;
                }

                if (fileExt == ".xlsx")
                {
                    // 讀取 Excel 檔案
                    var excelData = ReadExcelFile(ofd.FileName, out firstDataRow);
                    if (excelData == null || excelData.Count == 0)
                    {
                        TaskDialog.Show("COBie 匯入", "Excel 檔案無內容或讀取失敗");
                        return Result.Cancelled;
                    }
                    headers = excelData[0];
                    rows = excelData.Skip(1).ToList();
                }
                else if (fileExt == ".csv")
                {
                    // 讀取 CSV 檔案
                    List<List<string>> records;
                    using (var reader = new StreamReader(ofd.FileName, Encoding.UTF8, true))
                        records = CsvRecordReader.Read(reader);
                    if (records.Count == 0)
                    {
                        TaskDialog.Show("COBie 匯入", "CSV 無內容");
                        return Result.Cancelled;
                    }

                    headers = records[0].Select(h => h.Trim()).ToList();
                    rows = records.Skip(1)
                        .Select(r => NormalizeRow(r, headers.Count))
                        .ToList();
                }
                else
                {
                    TaskDialog.Show("COBie 匯入", "不支援的檔案格式。請選擇 .xlsx 或 .csv 檔案。");
                    return Result.Cancelled;
                }

                var duplicateHeaders = headers.Where(h => !string.IsNullOrWhiteSpace(h))
                    .GroupBy(h => h, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (duplicateHeaders.Count > 0)
                {
                    TaskDialog.Show("COBie 匯入", "欄位名稱重複，無法安全判定識別或更新欄位。請修正後重試：\n" +
                        string.Join(", ", duplicateHeaders));
                    return Result.Cancelled;
                }

                int idxUnique = headers.FindIndex(h => h.Equals("UniqueId", StringComparison.OrdinalIgnoreCase));
                int idxElemId = headers.FindIndex(h => h.Equals("ElementId", StringComparison.OrdinalIgnoreCase));
                int idxMark = headers.FindIndex(h => h.Equals("Mark", StringComparison.OrdinalIgnoreCase));
                int idxFam = headers.FindIndex(h => h.Equals("FamilyName", StringComparison.OrdinalIgnoreCase));
                int idxTyp = headers.FindIndex(h => h.Equals("TypeName", StringComparison.OrdinalIgnoreCase));

                int updated = 0, skipped = 0, matched = 0;
                var fails = new List<FailRow>();
                var plan = new List<PlannedWrite>();
                var elements = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();
                var byUniqueId = elements.ToDictionary(e => e.UniqueId, StringComparer.OrdinalIgnoreCase);
                var matcher = new CobieImportMatcher(elements.Select(e =>
                {
                    var names = GetFamilyAndType(doc, e);
                    return new CobieImportIdentity
                    {
                        UniqueId = e.UniqueId, ElementId = ParamTypeCompat.ElementIdToString(e.Id),
                        Mark = TryGetStringParam(e, BuiltInParameter.ALL_MODEL_MARK),
                        FamilyName = names.family, TypeName = names.type
                    };
                }));
                var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var explicitFields = new HashSet<string>(headers.Where(h => map.ContainsKey(h))
                    .Select(h => map[h].CobieName ?? ""), StringComparer.OrdinalIgnoreCase);

                // Complete read-only preflight before starting any transaction.
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var r = rows[rowIndex];
                    var identity = new CobieImportIdentity
                    {
                        UniqueId = Safe(r, idxUnique), ElementId = Safe(r, idxElemId), Mark = Safe(r, idxMark),
                        FamilyName = Safe(r, idxFam), TypeName = Safe(r, idxTyp)
                    };
                    var source = new FailRow
                    {
                        SourceRow = (rowIndex + firstDataRow).ToString(CultureInfo.InvariantCulture),
                        UniqueId = identity.UniqueId, ElementId = identity.ElementId, Mark = identity.Mark,
                        FamilyName = identity.FamilyName, TypeName = identity.TypeName,
                        FamilyType = identity.FamilyName + ":" + identity.TypeName
                    };
                    if (r.Count > headers.Count)
                    {
                        source.Reason = "Row contains more cells than the header; check CSV quoting or column alignment";
                        fails.Add(source); skipped++; continue;
                    }
                    var match = matcher.Match(identity, out var reason);
                    if (match == null)
                    {
                        source.Reason = reason;
                        fails.Add(source); skipped++; continue;
                    }
                    var elem = byUniqueId[match.UniqueId];
                    matched++; matchedIds.Add(elem.UniqueId);
                    for (int c = 0; c < headers.Count; c++)
                    {
                        var head = headers[c];
                        if (IsIdentityHeader(head) || !map.TryGetValue(head, out var cfg)) continue;
                        PlanValue(elem, cfg, Safe(r, c), head, source, plan, fails);
                    }

                    // Preserve room-derived defaults, but respect import enablement and explicit file values.
                    var room = GetRoomFromElement(doc, elem);
                    if (room != null)
                    {
                        foreach (var cfg in cfgs.Where(c => c.ImportEnabled &&
                            (c.CobieName == "Space.Name" || c.CobieName == "Component.Space") &&
                            !explicitFields.Contains(c.CobieName)))
                            PlanValue(elem, cfg, cfg.CobieName == "Space.Name" ? room.Name : room.Number,
                                cfg.CobieName + " (room default)", source, plan, fails);
                    }
                }

                // Never let the last row silently win, including multiple instances sharing a type.
                var safePlan = CobieImportWritePlan.Resolve(plan, w => w.Key, w => w.Value,
                    out var conflicts, out var duplicates);
                foreach (var write in conflicts)
                {
                    write.Source.Reason = "Conflicting values for the same target parameter; all conflicting writes skipped";
                    fails.Add(write.Source);
                }
                var typeIds = new HashSet<string>(safePlan.Where(w => w.IsType).Select(w =>
                    ParamTypeCompat.ElementIdToString(w.Owner.Id)));
                var affected = elements.Where(e => typeIds.Contains(ParamTypeCompat.ElementIdToString(e.GetTypeId()))).ToList();
                var outside = affected.Count(e => !matchedIds.Contains(e.UniqueId));
                var preview = new TaskDialog("COBie 匯入預檢")
                {
                    MainInstruction = safePlan.Count == 0 ? "沒有可安全寫入的資料" : "請確認匯入範圍，再寫入模型",
                    MainContent = $"來源資料列：{rows.Count}\n匹配列：{matched}；略過列：{skipped}\n" +
                        $"可寫入欄位：{safePlan.Count}（實例 {safePlan.Count(w => !w.IsType)}；型別 {safePlan.Count(w => w.IsType)}）\n" +
                        $"重複同值寫入已合併：{duplicates}；問題項目：{fails.Count}\n" +
                        $"型別變更影響：{typeIds.Count} 個型別、{affected.Count} 個實例（其中 {outside} 個不在匹配列中）。\n\n" +
                        "預檢尚未更動模型。問題項目將略過，僅寫入上述安全項目。型別參數會影響共用該型別的所有實例。\n" +
                        "ElementId / Mark 為模型內識別，請確認檔案來自目前模型；提供 UniqueId 最可靠。",
                    ExpandedContent = string.Join("\n", fails.Take(30).Select(f =>
                        $"列 {f.SourceRow} [{f.Field}]：{f.Reason}")) +
                        (fails.Count > 30 ? "\n（更多問題可另存完整 CSV）" : ""),
                    CommonButtons = safePlan.Count == 0 ? TaskDialogCommonButtons.Close :
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = safePlan.Count == 0 ? TaskDialogResult.Close : TaskDialogResult.No
                };
                var choice = preview.Show();
                if (safePlan.Count == 0 || choice != TaskDialogResult.Yes)
                {
                    OfferFailCsv(fails, false);
                    return Result.Cancelled;
                }

                using (var tx = new Transaction(doc, "COBie 匯入"))
                {
                    if (tx.Start() != TransactionStatus.Started)
                        throw new InvalidOperationException("無法啟動 COBie 匯入交易。");
                    var failureHandler = new RollbackImportErrors();
                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                        .SetFailuresPreprocessor(failureHandler).SetClearAfterRollback(true).SetForcedModalHandling(true));
                    foreach (var write in safePlan)
                    {
                        try
                        {
                            // Write only the exact parameter/scope reviewed in preflight; never fallback.
                            if (SetPlannedValue(write)) updated++;
                            else
                            {
                                write.Source.Reason = "Revit rejected the planned parameter value";
                                fails.Add(write.Source);
                            }
                        }
                        catch (Exception ex)
                        {
                            write.Source.Reason = ex.Message;
                            fails.Add(write.Source);
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                    {
                        foreach (var write in safePlan)
                        {
                            write.Source.Reason = "Transaction not committed; planned value was not imported. " +
                                string.Join("; ", failureHandler.Messages);
                            if (!fails.Contains(write.Source)) fails.Add(write.Source);
                        }
                        TaskDialog.Show("COBie 匯入未完成", "Revit 未成功提交資料更新，本次更新數量不列為成功。\n" +
                            string.Join("\n", failureHandler.Messages));
                        OfferFailCsv(fails, false);
                        return Result.Failed;
                    }
                }
                TaskDialog.Show("COBie 匯入", $"更新成功欄位：{updated}\n略過識別列：{skipped}\n問題項目：{fails.Count}\n\n可另存問題清單 CSV 以利後續查核。");
                OfferFailCsv(fails, true);

                return Result.Succeeded;
            }
            catch (Exception ex) { msg = ex.ToString(); return Result.Failed; }
        }

        private static bool IsIdentityHeader(string header)
        {
            return new[] { "UniqueId", "ElementId", "Mark", "FamilyName", "TypeName", "FamilyType" }
                .Contains(header, StringComparer.OrdinalIgnoreCase);
        }

        private static void PlanValue(Element element, CmdCobieFieldManager.CobieFieldConfig cfg,
            string raw, string field, FailRow row, List<PlannedWrite> plan, List<FailRow> fails)
        {
            var source = new FailRow
            {
                SourceRow = row.SourceRow, UniqueId = row.UniqueId, ElementId = row.ElementId,
                Mark = row.Mark, FamilyName = row.FamilyName, TypeName = row.TypeName,
                FamilyType = row.FamilyType, Field = field, Value = raw
            };
            try
            {
                var owner = cfg.IsInstance ? element : element.Document.GetElement(element.GetTypeId());
                Parameter parameter = null;
                if (owner != null && !string.IsNullOrWhiteSpace(cfg.SharedParameterName))
                {
                    var candidates = owner.GetParameters(cfg.SharedParameterName);
                    if (candidates.Count > 1)
                        throw new InvalidOperationException("Ambiguous parameter name in configured scope: " + cfg.SharedParameterName);
                    parameter = candidates.SingleOrDefault();
                }
                else if (owner != null && cfg.IsBuiltIn && cfg.BuiltInParam.HasValue)
                    parameter = owner.get_Parameter(cfg.BuiltInParam.Value);

                if (parameter == null || parameter.IsReadOnly)
                    throw new InvalidOperationException("Parameter missing/read-only in configured " +
                        (cfg.IsInstance ? "instance" : "type") + " scope; cross-scope fallback disabled");
                if (!TryPrepareValue(parameter, cfg.DataType, raw, out var value))
                    throw new InvalidOperationException("Invalid value or storage type mismatch in configured scope");
                plan.Add(new PlannedWrite { Owner = owner, Parameter = parameter, Value = value,
                    IsType = owner is ElementType, Source = source });
            }
            catch (Exception ex)
            {
                source.Reason = ex.Message;
                fails.Add(source);
            }
        }

        private static bool TryPrepareValue(Parameter parameter, string dataType, string raw, out object value)
        {
            value = null;
            switch ((dataType ?? "Text").Trim())
            {
                case "Number":
                    if (parameter.StorageType != StorageType.Double || !TryParseDouble(raw, out double number) ||
                        double.IsNaN(number) || double.IsInfinity(number)) return false;
                    value = number; return true;
                case "Integer":
                    if (parameter.StorageType != StorageType.Integer || !TryParseInteger(raw, out int integer)) return false;
                    value = integer; return true;
                case "YesNo":
                    if (parameter.StorageType != StorageType.Integer || !TryParseBool(raw, out int boolean)) return false;
                    value = boolean; return true;
                default:
                    if (parameter.StorageType != StorageType.String) return false;
                    value = raw ?? ""; return true;
            }
        }

        private static bool SetPlannedValue(PlannedWrite write)
        {
            if (write.Parameter.IsReadOnly) return false;
            if (write.Value is double number) return write.Parameter.Set(number);
            if (write.Value is int integer) return write.Parameter.Set(integer);
            return write.Parameter.Set((string)write.Value);
        }

        private static void OfferFailCsv(List<FailRow> fails, bool committed)
        {
            if (fails.Count == 0) return;
            if (MessageBox.Show("是否另存「匯入問題清單」CSV？", "COBie 匯入",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            using (var sfd = new SaveFileDialog { Filter = "CSV (逗號分隔)|*.csv",
                FileName = $"COBie_Import_Fail_{DateTime.Now:yyyyMMdd_HHmm}.csv" })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;
                try { SaveFailCsv(sfd.FileName, fails); }
                catch (Exception ex)
                {
                    TaskDialog.Show("問題清單未儲存", (committed ? "模型匯入已提交。" : "模型未變更。") +
                        "無法儲存問題清單：\n" + ex.Message);
                }
            }
        }

        private static bool TryParseBool(string s, out int val)
        {
            val = 0; if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().ToLowerInvariant();
            if (s == "1" || s == "true" || s == "yes" || s == "y" || s == "是") { val = 1; return true; }
            if (s == "0" || s == "false" || s == "no" || s == "n" || s == "否") { val = 0; return true; }
            return false;
        }

        private static bool TryParseDouble(string raw, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var text = raw.Trim();
            var styles = NumberStyles.Float | NumberStyles.AllowThousands;
            return double.TryParse(text, styles, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseInteger(string raw, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var text = raw.Trim();
            var styles = NumberStyles.Integer | NumberStyles.AllowThousands;
            return int.TryParse(text, styles, CultureInfo.CurrentCulture, out value)
                || int.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
        }

        private static (string family, string type) GetFamilyAndType(Document doc, Element e)
        {
            try
            {
                var tid = e.GetTypeId();
                if (tid == ElementId.InvalidElementId) return (null, null);
                var et = doc.GetElement(tid) as ElementType;
                if (et == null) return (null, null);
                var fam = (et as FamilySymbol)?.Family?.Name ?? et.FamilyName;
                return (fam, et.Name);
            }
            catch { return (null, null); }
        }

        private static string TryGetStringParam(Element e, BuiltInParameter bip)
        { var p = e.get_Parameter(bip); return p?.AsString() ?? p?.AsValueString(); }

        private static List<string> NormalizeRow(List<string> row, int headerCount)
        {
            row = row ?? new List<string>();
            while (row.Count < headerCount)
            {
                row.Add("");
            }
            return row;
        }

        private static void SaveFailCsv(string path, List<FailRow> fails)
        {
            var headers = new[] { "Reason", "SourceRow", "UniqueId", "ElementId", "Mark", "FamilyName", "TypeName", "FamilyType", "Field", "Value" };
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(true)))
            {
                sw.WriteLine(Csv(headers));
                foreach (var f in fails)
                    sw.WriteLine(Csv(new[] { f.Reason, f.SourceRow, f.UniqueId, f.ElementId, f.Mark, f.FamilyName, f.TypeName, f.FamilyType, f.Field, f.Value }));
            }
        }

        private static string Csv(IEnumerable<string> cells)
            => string.Join(",", cells.Select(EscapeCsv));
        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            bool need = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
            s = s.Replace("\"", "\"\"");
            return need ? $"\"{s}\"" : s;
        }

        private static string Safe(IList<string> row, int index)
        { if (row == null) return ""; if (index < 0 || index >= row.Count) return ""; return row[index] ?? ""; }
        // 依元素位置找房間（與匯出端一致）
        private static Room GetRoomFromElement(Document doc, Element element)
        {
            try
            {
                var locPoint = element.Location as LocationPoint;
                if (locPoint != null)
                {
                    var point = locPoint.Point;
                    var phases = doc.Phases;
                    if (phases.Size > 0)
                    {
                        var phase = phases.get_Item(phases.Size - 1);
                        return doc.GetRoomAtPoint(point, phase);
                    }
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// 讀取 Excel 檔案並轉換為字串列表
        /// </summary>
        private static List<List<string>> ReadExcelFile(string filePath, out int firstDataRow)
        {
            var result = new List<List<string>>();
            firstDataRow = 2;

            try
            {
                var fileInfo = new FileInfo(filePath);
                using (var package = new ExcelPackage(fileInfo))
                {
                    // 取得第一個工作表
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Excel 檔案中沒有工作表");
                        return result;
                    }

                    // 取得使用範圍
                    if (worksheet.Dimension == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Excel 工作表沒有資料");
                        return result;
                    }

                    var start = worksheet.Dimension.Start;
                    var end = worksheet.Dimension.End;
                    firstDataRow = start.Row + 1;

                    // 讀取每一列
                    for (int row = start.Row; row <= end.Row; row++)
                    {
                        var rowData = new List<string>();

                        // 讀取每一欄
                        for (int col = start.Column; col <= end.Column; col++)
                        {
                            var cell = worksheet.Cells[row, col];
                            var value = cell.Value?.ToString() ?? "";
                            rowData.Add(value.Trim());

                        }

                        result.Add(rowData);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"讀取 Excel 檔案失敗: {ex.Message}");
                return null;
            }

            return result;
        }
    }
}
