using System;
using System.Collections.Generic;
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

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.Manual)]
    public class CmdCobieImportEnhanced : IExternalCommand
    {
        private class FailRow
        {
            public string Reason, UniqueId, ElementId, Mark, FamilyName, TypeName, FamilyType, Field, Value;
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
                var ofd = new OpenFileDialog { Filter = "CSV (逗號分隔)|*.csv", Title = "選擇 COBie 匯入 CSV" };
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

                var rawLines = File.ReadAllLines(ofd.FileName, Encoding.UTF8);
                var lines = rawLines.Where(l => !string.IsNullOrWhiteSpace(l))
                                    .Where(l => !l.TrimStart().StartsWith("#"))
                                    .ToList();
                if (lines.Count == 0) { TaskDialog.Show("COBie 匯入", "CSV 無內容"); return Result.Cancelled; }

                var headers = SplitCsv(lines[0]).Select(h => h.Trim()).ToList();
                var rows = lines.Skip(1).Select(SplitCsv).Where(r => r.Count == headers.Count).ToList();

                int idxUnique = headers.FindIndex(h => h.Equals("UniqueId", StringComparison.OrdinalIgnoreCase));
                int idxElemId = headers.FindIndex(h => h.Equals("ElementId", StringComparison.OrdinalIgnoreCase));
                int idxMark = headers.FindIndex(h => h.Equals("Mark", StringComparison.OrdinalIgnoreCase));
                int idxFam = headers.FindIndex(h => h.Equals("FamilyName", StringComparison.OrdinalIgnoreCase));
                int idxTyp = headers.FindIndex(h => h.Equals("TypeName", StringComparison.OrdinalIgnoreCase));

                int updated = 0, skipped = 0;
                var fails = new List<FailRow>();

                using (var tx = new Transaction(doc, "COBie 匯入"))
                {
                    tx.Start();

                    foreach (var r in rows)
                    {
                        Element elem = null;
                        string u = Safe(r, idxUnique);
                        string id = Safe(r, idxElemId);
                        string mk = Safe(r, idxMark);
                        string fam = Safe(r, idxFam);
                        string typ = Safe(r, idxTyp);

                        // 1) UniqueId
                        if (!string.IsNullOrWhiteSpace(u)) { try { elem = doc.GetElement(u); } catch { } }

                        // 2) ElementId
                        if (elem == null && !string.IsNullOrWhiteSpace(id))
                        {
                            var eid = ParamTypeCompat.ParseElementId(id);
                            if (eid != null) elem = doc.GetElement(eid);
                        }

                        // 3) Mark
                        if (elem == null && !string.IsNullOrWhiteSpace(mk))
                        {
                            elem = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                                .FirstOrDefault(e =>
                                {
                                    var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                                    var s = p?.AsString() ?? p?.AsValueString();
                                    return string.Equals(s, mk, StringComparison.OrdinalIgnoreCase);
                                });
                        }

                        if (elem == null)
                        {
                            fails.Add(new FailRow { Reason = "Element not found", UniqueId = u, ElementId = id, Mark = mk, FamilyName = fam, TypeName = typ, FamilyType = $"{fam}:{typ}" });
                            skipped++; continue;
                        }

                        // 先自動寫入空間名稱與空間代碼（不需CSV提供）
                        try
                        {
                            var room = GetRoomFromElement(doc, elem);
                            if (room != null)
                            {
                                var cfgSpaceName = cfgs.FirstOrDefault(c => c.CobieName == "Space.Name");
                                var cfgSpaceCode = cfgs.FirstOrDefault(c => c.CobieName == "Component.Space");
                                if (cfgSpaceName != null) ApplyValue(elem, cfgSpaceName, room.Name);
                                if (cfgSpaceCode != null) ApplyValue(elem, cfgSpaceCode, room.Number);
                            }
                        }
                        catch { }

                        for (int c = 0; c < headers.Count; c++)
                        {
                            var head = headers[c];
                            // 識別與人工對照欄：一律忽略
                            if (head.Equals("UniqueId", StringComparison.OrdinalIgnoreCase) ||
                                head.Equals("ElementId", StringComparison.OrdinalIgnoreCase) ||
                                head.Equals("Mark", StringComparison.OrdinalIgnoreCase) ||
                                head.Equals("FamilyName", StringComparison.OrdinalIgnoreCase) ||
                                head.Equals("TypeName", StringComparison.OrdinalIgnoreCase) ||
                                head.Equals("FamilyType", StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!map.TryGetValue(head, out var cfg)) continue;

                            string val = r[c];
                            try
                            {
                                bool ok = ApplyValue(elem, cfg, val);
                                if (ok) updated++;
                                else
                                {
                                    var (ff, tt) = GetFamilyAndType(doc, elem);
                                    fails.Add(new FailRow
                                    {
                                        Reason = "Parameter not writable or type mismatch",
                                        UniqueId = elem.UniqueId,
                                        ElementId = ParamTypeCompat.ElementIdToString(elem.Id),
                                        Mark = TryGetStringParam(elem, BuiltInParameter.ALL_MODEL_MARK),
                                        FamilyName = ff,
                                        TypeName = tt,
                                        FamilyType = $"{ff}:{tt}",
                                        Field = head,
                                        Value = val
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                var (ff, tt) = GetFamilyAndType(doc, elem);
                                fails.Add(new FailRow { Reason = ex.Message, UniqueId = elem.UniqueId, ElementId = ParamTypeCompat.ElementIdToString(elem.Id), Mark = TryGetStringParam(elem, BuiltInParameter.ALL_MODEL_MARK), FamilyName = ff, TypeName = tt, FamilyType = $"{ff}:{tt}", Field = head, Value = val });
                            }
                        }
                    }

                    tx.Commit();
                }

                TaskDialog.Show("COBie 匯入", $"更新成功：{updated}\n略過：{skipped}\n失敗：{fails.Count}\n\n可另存失敗清單 CSV 以利後續查核。");

                if (fails.Count > 0)
                {
                    var ask = MessageBox.Show("是否另存「更新失敗清單」CSV？", "COBie 匯入", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (ask == DialogResult.Yes)
                    {
                        var sfd = new SaveFileDialog { Filter = "CSV (逗號分隔)|*.csv", FileName = $"COBie_Import_Fail_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
                        if (sfd.ShowDialog() == DialogResult.OK) SaveFailCsv(sfd.FileName, fails);
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex) { msg = ex.ToString(); return Result.Failed; }
        }

        private static bool ApplyValue(Element e, CmdCobieFieldManager.CobieFieldConfig cfg, string raw)
        {
            // 優先寫入共用參數，寫入順序依 IsInstance（true: 實例→型別；false: 型別→實例）
            if (!string.IsNullOrWhiteSpace(cfg.SharedParameterName))
            {
                if (cfg.IsInstance)
                {
                    var pInst = e.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Definition?.Name == cfg.SharedParameterName);
                    if (SetParam(pInst, cfg.DataType, raw)) return true;

                    // 若指定為實例但失敗，嘗試型別層級
                    var et = e.Document.GetElement(e.GetTypeId()) as ElementType;
                    var pType = et?.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Definition?.Name == cfg.SharedParameterName);
                    if (SetParam(pType, cfg.DataType, raw)) return true;
                }
                else
                {
                    var et = e.Document.GetElement(e.GetTypeId()) as ElementType;
                    var pType = et?.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Definition?.Name == cfg.SharedParameterName);
                    if (SetParam(pType, cfg.DataType, raw)) return true;

                    var pInst = e.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Definition?.Name == cfg.SharedParameterName);
                    if (SetParam(pInst, cfg.DataType, raw)) return true;
                }

                // 如果有共用參數設定但寫入失敗，直接返回 false，不要回退到內建參數
                return false;
            }

            // 只有在沒有設定共用參數時，才使用內建參數（用於向後相容）
            if (cfg.IsBuiltIn && cfg.BuiltInParam.HasValue)
            {
                if (cfg.IsInstance)
                {
                    var pInst = e.get_Parameter(cfg.BuiltInParam.Value);
                    if (SetParam(pInst, cfg.DataType, raw)) return true;

                    var et = e.Document.GetElement(e.GetTypeId()) as ElementType;
                    var pType = et?.get_Parameter(cfg.BuiltInParam.Value);
                    return SetParam(pType, cfg.DataType, raw);
                }
                else
                {
                    var et = e.Document.GetElement(e.GetTypeId()) as ElementType;
                    var pType = et?.get_Parameter(cfg.BuiltInParam.Value);
                    if (SetParam(pType, cfg.DataType, raw)) return true;

                    var pInst = e.get_Parameter(cfg.BuiltInParam.Value);
                    return SetParam(pInst, cfg.DataType, raw);
                }
            }
            return false;
        }

        private static bool SetParam(Parameter p, string dataType, string raw)
        {
            if (p == null || p.IsReadOnly) return false;
            switch ((dataType ?? "Text").Trim())
            {
                case "Number": if (double.TryParse(raw, out double d)) return p.Set(d); return false;
                case "Integer": if (int.TryParse(raw, out int i)) return p.Set(i); return false;
                case "YesNo": if (TryParseBool(raw, out int b)) return p.Set(b); return false;
                case "Date": return p.Set(raw ?? "");
                default: return p.Set(raw ?? "");
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

        private static List<string> SplitCsv(string line)
        {
            var list = new List<string>(); if (line == null) return list;
            bool inQ = false; var sb = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQ)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') inQ = false;
                    else sb.Append(c);
                }
                else
                {
                    if (c == ',') { list.Add(sb.ToString()); sb.Clear(); }
                    else if (c == '"') inQ = true;
                    else sb.Append(c);
                }
            }
            list.Add(sb.ToString());
            return list;
        }

        private static void SaveFailCsv(string path, List<FailRow> fails)
        {
            var headers = new[] { "Reason", "UniqueId", "ElementId", "Mark", "FamilyName", "TypeName", "FamilyType", "Field", "Value" };
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(true)))
            {
                sw.WriteLine(Csv(headers));
                foreach (var f in fails)
                    sw.WriteLine(Csv(new[] { f.Reason, f.UniqueId, f.ElementId, f.Mark, f.FamilyName, f.TypeName, f.FamilyType, f.Field, f.Value }));
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
    }
}
