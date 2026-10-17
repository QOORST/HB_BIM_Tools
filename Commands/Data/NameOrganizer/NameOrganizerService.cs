using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;
using RView = Autodesk.Revit.DB.View;
using RFamily = Autodesk.Revit.DB.Family;

namespace YD_RevitTools.LicenseManager.Commands.Data.NameOrganizer
{
    internal sealed class NameOrganizerService
    {
        private readonly Document _doc;
        private readonly Func<string, string, IEnumerable<string[]>, IWin32Window, bool> _confirm;
        private readonly Action<IWin32Window, string> _notify;
        internal NameOrganizerService(Document doc, Func<string, string, IEnumerable<string[]>, IWin32Window, bool> confirm = null, Action<IWin32Window, string> notify = null)
        {
            _doc = doc;
            _confirm = confirm ?? ((title, hint, rows, owner) => { using (var preview = new NameImpactForm(title, hint, rows)) return preview.ShowDialog(owner) == DialogResult.OK; });
            _notify = notify ?? ((owner, message) => MessageBox.Show(owner, message, "模型名稱整理"));
        }
        internal List<NameRow> Read()
        {
            var rows = new List<NameRow>();
            var instances = new FilteredElementCollector(_doc).WhereElementIsNotElementType().ToList();
            var counts = instances.Where(e => e.GetTypeId() != ElementId.InvalidElementId).GroupBy(e => e.GetTypeId()).ToDictionary(g => g.Key, g => g.Count());
            var types = new FilteredElementCollector(_doc).WhereElementIsElementType().Cast<ElementType>().ToList();
            var familyCounts = types.OfType<FamilySymbol>().GroupBy(s => s.Family.Id).ToDictionary(g => g.Key, g => g.Sum(s => counts.TryGetValue(s.Id, out var n) ? n : 0));
            var views = instances.OfType<RView>().Where(IsSupportedView).ToList();
            var placements = new Dictionary<ElementId, HashSet<string>>();
            void Place(ElementId id, string number) { if (!placements.TryGetValue(id, out var set)) placements[id] = set = new HashSet<string>(); set.Add(number); }
            foreach (var sheet in views.OfType<ViewSheet>()) foreach (var id in sheet.GetAllPlacedViews()) Place(id, sheet.SheetNumber);
            foreach (var placed in instances.OfType<ScheduleSheetInstance>().Where(s => !s.IsTitleblockRevisionSchedule))
                if (_doc.GetElement(placed.OwnerViewId) is ViewSheet sheet) Place(placed.ScheduleId, sheet.SheetNumber);
            var templateCounts = views.Where(v => !v.IsTemplate && v.ViewTemplateId != ElementId.InvalidElementId).GroupBy(v => v.ViewTemplateId).ToDictionary(g => g.Key, g => g.Count());
            var filterCounts = new Dictionary<ElementId, int>(); var filterReadFailed = false;
            foreach (var view in views.Where(v => v.AreGraphicsOverridesAllowed()))
                try { foreach (var id in view.GetFilters()) filterCounts[id] = filterCounts.TryGetValue(id, out var n) ? n + 1 : 1; }
                catch { filterReadFailed = true; }
            foreach (var family in instances.OfType<RFamily>())
                Add(family, NameKind.Family, "Family", family.FamilyCategory?.Name, "放置 " + (familyCounts.TryGetValue(family.Id, out var n) ? n : 0) + " 個實例");
            foreach (var type in types.Where(t => t.Category != null || t is DimensionType))
            {
                var scope = type is FamilySymbol symbol ? "Type|" + symbol.Family.UniqueId : "Type|" + type.GetType().FullName + "|" + type.Category?.Id.GetIdValue() + "|" + type.FamilyName;
                Add(type, NameKind.Type, scope, (type.Category?.Name ?? "尺寸") + "｜" + type.FamilyName, "放置 " + (counts.TryGetValue(type.Id, out var n) ? n : 0) + " 個實例；其他參照未判定");
            }
            foreach (var view in views)
            {
                if (view is ViewSheet sheet) Add(view, NameKind.Sheet, "Sheet|" + view.UniqueId, "圖號 " + sheet.SheetNumber, "圖號 " + sheet.SheetNumber + "（不修改）");
                else if (view.IsTemplate) Add(view, NameKind.Template, "View", view.ViewType.ToString(), "直接套用 " + (templateCounts.TryGetValue(view.Id, out var n) ? n : 0) + " 個視圖；其他參照未判定");
                else Add(view, NameKind.View, "View", view.ViewType.ToString(), placements.TryGetValue(view.Id, out var placed) ? "圖紙：" + string.Join("、", placed.OrderBy(x => x)) : "未上圖紙；其他參照未判定");
            }
            foreach (var material in instances.OfType<Material>()) Add(material, NameKind.Material, "Material", material.MaterialCategory, "使用情況未判定");
            foreach (var filter in instances.OfType<FilterElement>()) Add(filter, NameKind.Filter, "Filter", filter is ParameterFilterElement ? "參數篩選器" : "選取篩選器",
                filterReadFailed ? "使用情況未完整取得" : "套用 " + (filterCounts.TryGetValue(filter.Id, out var n) ? n : 0) + " 個視圖／樣板；其他參照未判定");
            return rows.OrderBy(r => r.Kind).ThenBy(r => r.Context).ThenBy(r => r.CurrentName).ToList();
            void Add(Element element, NameKind kind, string scope, string context, string usage)
            {
                if (string.IsNullOrWhiteSpace(element.Name)) return;
                rows.Add(new NameRow { UniqueId = element.UniqueId, Kind = kind, Scope = scope, Context = context ?? "", CurrentName = element.Name, NewName = element.Name, Usage = usage, Status = "" });
            }
        }
        internal static bool IsSupportedView(RView view) => view.ViewType != ViewType.ProjectBrowser && view.ViewType != ViewType.SystemBrowser && view.ViewType != ViewType.Internal
            && !(view is ViewSchedule schedule && schedule.IsTitleblockRevisionSchedule);
        internal static bool Supports(Element element) => element is RFamily || (element is ElementType type && (type.Category != null || type is DimensionType)) || element is Material || element is FilterElement || (element is RView v && IsSupportedView(v));
        private sealed class RollbackErrors : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor) => accessor.GetFailureMessages().Any(f => f.GetSeverity() == FailureSeverity.Error)
                ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
        private Transaction Start(string title)
        {
            var tx = new Transaction(_doc, title); tx.Start();
            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new RollbackErrors()).SetClearAfterRollback(true).SetForcedModalHandling(true));
            return tx;
        }
        private Element Resolve(NameRow row)
        {
            var element = _doc.GetElement(row.UniqueId);
            if (element == null || !Supports(element) || element.Name != row.CurrentName) throw new InvalidOperationException("項目已變更或不存在，請重新讀取：" + row.CurrentName);
            return element;
        }
        internal bool Apply(List<NameRow> targets, IWin32Window owner)
        {
            var errors = NamePolicy.Validate(targets, Read());
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\r\n", errors.Take(15)));
            if (!_confirm("確認名稱變更", $"本次修改 {targets.Count} 筆名稱。整批成功才保留；任一項目失敗會回復整批。\r\n圖號、參數值及視圖樣板設定不會隨名稱修改。",
                targets.Select(r => new[] { r.KindName + "｜" + r.Context, r.CurrentName, r.NewName.Trim() }), owner)) return false;
            errors = NamePolicy.Validate(targets, Read());
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\r\n", errors.Take(15)));
            using (var tx = Start("模型名稱整理 - 批次改名"))
            {
                try
                {
                    var resolved = targets.Select(r => new { Row = r, Element = Resolve(r) }).ToList();
                    // Unique temporary names support swaps and longer cycles within one atomic transaction.
                    foreach (var item in resolved) item.Element.Name = "HB_TEMP_" + Guid.NewGuid().ToString("N");
                    foreach (var item in resolved) item.Element.Name = item.Row.NewName.Trim();
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("名稱變更未成功提交，沒有回報為已套用；請重新讀取確認。");
                }
                catch { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); throw; }
            }
            _notify(owner, $"已提交 {targets.Count} 筆名稱變更。"); return true;
        }
        internal bool Delete(List<NameRow> targets, ElementId activeViewId, IWin32Window owner)
        {
            var ids = targets.Select(r => Resolve(r).Id).ToList();
            HashSet<ElementId> affected;
            using (var probe = Start("模型名稱整理 - 刪除影響預演"))
            {
                try { affected = new HashSet<ElementId>(_doc.Delete(ids)); }
                finally { if (probe.GetStatus() == TransactionStatus.Started && probe.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("刪除預演未正常回復，已停止操作。"); }
            }
            if (affected.Contains(activeViewId)) throw new InvalidOperationException("刪除影響包含目前作用中視圖。請切換視圖後重新確認。");
            var requested = new HashSet<ElementId>(ids);
            var expected = affected.ToDictionary(id => id, id => IdentityAndName(_doc.GetElement(id)));
            var impact = affected.OrderBy(id => id.GetIdValue()).Select(id => {
                var e = _doc.GetElement(id);
                return new[] { (requested.Contains(id) ? "勾選" : "連帶刪除") + "｜ID " + id.GetIdValue(), e?.Name ?? "無名稱", e?.Category?.Name ?? e?.GetType().Name ?? "未知" };
            }).ToList();
            if (!_confirm("確認實際刪除影響", $"勾選 {ids.Count} 筆，預演將刪除 {affected.Count} 個元素（含連帶項目）。\r\n預演已回復模型；確認後才正式刪除。零放置數量不代表沒有其他參照。", impact, owner)) return false;
            foreach (var row in targets) Resolve(row);
            using (var tx = Start("模型名稱整理 - 刪除勾選項目"))
            {
                try
                {
                    if (expected.Any(pair => IdentityAndName(_doc.GetElement(pair.Key)) != pair.Value)) throw new InvalidOperationException("預演後模型已變更，請重新預演。");
                    var actual = new HashSet<ElementId>(_doc.Delete(ids));
                    if (!actual.SetEquals(affected)) throw new InvalidOperationException("實際刪除範圍與預演不同，已停止並回復。");
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("刪除未成功提交，請重新讀取確認。");
                }
                catch { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); throw; }
            }
            _notify(owner, $"已提交刪除，共 {affected.Count} 個元素（含連帶項目）。"); return true;
        }
        private static string IdentityAndName(Element element) => element == null ? null : element.UniqueId + "\n" + element.Name;
    }
}
