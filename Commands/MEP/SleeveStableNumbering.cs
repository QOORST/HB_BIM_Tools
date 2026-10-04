using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveStableNumbering
    {
        internal static string Mark(FamilyInstance fi)
        {
            var p = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            string mark = p?.StorageType == StorageType.String ? p.AsString() : null;
            return !string.IsNullOrWhiteSpace(mark) ? mark : SleeveQuantityData.Text(fi, "套管編號", "編號", "Sleeve Number");
        }
        internal static void Assign(Document doc, IEnumerable<FamilyInstance> sleeves, IEnumerable<FamilyInstance> newlyCreated = null)
        {
            var targets = sleeves.GroupBy(fi => fi.UniqueId).Select(g => g.First()).ToList();
            if (targets.Count == 0) return;
            var stores = SleeveDocumentStorage.Find(doc, SleeveDocumentStorage.NumberSchema);
            if (stores.Count > 1) throw new InvalidOperationException("套管編號台帳重複，已取消編號。");
            var previous = stores.Count == 0 ? new SleeveNumberLedger() : SleeveDocumentStorage.Read<SleeveNumberLedger>(stores[0], SleeveDocumentStorage.NumberSchema);
            var all = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
            var createdIds = new HashSet<string>((newlyCreated ?? Enumerable.Empty<FamilyInstance>()).Select(fi => fi.UniqueId));
            if (createdIds.Any(id => !targets.Any(fi => fi.UniqueId == id))) throw new InvalidOperationException("新建套管不在本次編號範圍。");
            // Reserve PS numbers throughout the document, including other categories and other views.
            var items = all.Select(fi => new SleeveNumberItem { Id = fi.UniqueId, Mark = Mark(fi), CreatedInOperation = createdIds.Contains(fi.UniqueId) }).ToList();
            var targetIds = new HashSet<string>(targets.Select(fi => fi.UniqueId));
            // Unrelated repeated door/window marks must not become part of the sleeve ledger.
            var relevant = items.Where(i => SleeveNumberPolicy.Number(i.Mark) > 0 || targetIds.Contains(i.Id) || previous.Assigned.ContainsKey(i.Id)).ToList();
            var targetMarks = new HashSet<string>(items.Where(i => targetIds.Contains(i.Id) && !i.CreatedInOperation && !string.IsNullOrWhiteSpace(i.Mark)).Select(i => SleeveNumberPolicy.Key(i.Mark)));
            var managedIds = new HashSet<string>(all.Where(fi => SleeveQuantityData.Supported(fi) &&
                (SleeveQuantityData.Automatic(fi) || !string.IsNullOrEmpty(fi.get_Parameter(SleeveQuantityData.Id(1))?.AsString()))).Select(fi => fi.UniqueId));
            var relevantIds = new HashSet<string>(relevant.Select(i => i.Id));
            relevant.AddRange(items.Where(i => managedIds.Contains(i.Id) && targetMarks.Contains(SleeveNumberPolicy.Key(i.Mark)) && relevantIds.Add(i.Id)));
            var plan = SleeveNumberPolicy.Plan(relevant, targetIds, previous);
            foreach (var fi in targets)
            {
                string mark = plan.Writes.TryGetValue(fi.UniqueId, out string next) ? next : Mark(fi);
                var parameter = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (parameter?.StorageType != StorageType.String || parameter.IsReadOnly)
                    parameter = new[] { "套管編號", "編號", "Sleeve Number" }.Select(fi.LookupParameter)
                        .FirstOrDefault(p => p?.StorageType == StorageType.String && !p.IsReadOnly);
                if (parameter == null || (parameter.AsString() != mark && !parameter.Set(mark))) throw new InvalidOperationException("無法寫入套管編號：" + fi.Id);
                // Tags may use a family's instance number instead of Revit Mark. Keep writable
                // aliases aligned so a family default cannot reappear when Mark is cleared.
                foreach (var alias in new[] { "套管編號", "編號", "Sleeve Number" }.Select(fi.LookupParameter))
                    if (alias?.StorageType == StorageType.String && !alias.IsReadOnly && alias.Element.Id == fi.Id && alias.AsString() != mark)
                        if (!alias.Set(mark)) throw new InvalidOperationException("無法同步套管編號：" + fi.Id);
            }
            SleeveDocumentStorage.Write(doc, SleeveDocumentStorage.NumberSchema, plan.Ledger, stores.SingleOrDefault());
        }
    }
}
