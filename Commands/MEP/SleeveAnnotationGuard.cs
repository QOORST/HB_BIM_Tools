using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveAnnotationGuard
    {
        private readonly List<Func<string>> _checks = new List<Func<string>>();
        private void AddCheck(string description, Func<bool> check)
        {
            _checks.Add(() =>
            {
                try { return check() ? null : description; }
                catch (Exception ex) { return description + "；檢查失敗：" + ex.Message; }
            });
        }
        internal SleeveAnnotationGuard(Document doc, IEnumerable<ElementId> ids)
        {
            var protectedIds = new HashSet<ElementId>(ids);
            foreach (var id in protectedIds)
            {
                var element = doc.GetElement(id);
                if (element == null) throw new InvalidOperationException("套管已不存在，請重新整理清單。");
                string uniqueId = element.UniqueId;
                AddCheck("套管 " + id + "：原件不存在或身分改變", () => doc.GetElement(id)?.UniqueId == uniqueId);
            }
            if (protectedIds.Count == 0) return;
            foreach (Dimension dimension in new FilteredElementCollector(doc).OfClass(typeof(Dimension)))
            {
                if (dimension.References == null || !dimension.References.Cast<Reference>().Any(r => protectedIds.Contains(r.ElementId))) continue;
                var id = dimension.Id;
                bool wasAvailable = dimension.AreReferencesAvailable;
                var references = dimension.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(doc)).ToArray();
                AddCheck("尺寸 " + id + "：尺寸被移除、參照改變或原有效參照失效", () => doc.GetElement(id) is Dimension current &&
                    (!wasAvailable || current.AreReferencesAvailable) && current.References != null &&
                    current.References.Cast<Reference>().Select(r => r.ConvertToStableRepresentation(doc)).SequenceEqual(references));
            }
            foreach (IndependentTag tag in new FilteredElementCollector(doc).OfClass(typeof(IndependentTag)))
            {
                var targets = new HashSet<ElementId>(tag.GetTaggedLocalElementIds());
                if (!targets.Overlaps(protectedIds)) continue;
                var id = tag.Id;
                bool wasOrphaned = tag.IsOrphaned;
                AddCheck("標籤 " + id + "：標籤被移除、標註對象改變或新增失聯", () => doc.GetElement(id) is IndependentTag current &&
                    (wasOrphaned || !current.IsOrphaned) &&
                    targets.SetEquals(current.GetTaggedLocalElementIds()));
            }
        }
        internal void Verify()
        {
            var failures = _checks.Select(check => check()).Where(message => message != null).ToList();
            if (failures.Count > 0)
                throw new InvalidOperationException("套管或標註保護檢核未通過，本次操作未保留。\n" +
                    string.Join("\n", failures.Take(8)) + (failures.Count > 8 ? "\n另有 " + (failures.Count - 8) + " 項。" : ""));
        }
    }
}
