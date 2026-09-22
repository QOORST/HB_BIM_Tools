using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class CmdMepPositionDimension
    {
        private static RevitLinkInstance ResolveObjectLink(Document doc)
        {
            if (objectLink == null) return null;
            var link = doc.GetElement(objectLink) as RevitLinkInstance;
            if (link?.GetLinkDocument() == null)
                throw new InvalidOperationException("標註對象連結不存在或尚未載入，請重新設定。");
            return link;
        }

        private sealed class LinkedTargetFilter : ISelectionFilter
        {
            private readonly RevitLinkInstance _link;
            private readonly ISelectionFilter _filter;
            internal LinkedTargetFilter(RevitLinkInstance link, ISelectionFilter filter) { _link = link; _filter = filter; }
            public bool AllowElement(Element element) => element.Id == _link.Id;
            public bool AllowReference(Reference reference, XYZ point)
            {
                if (reference.ElementId != _link.Id || reference.LinkedElementId == ElementId.InvalidElementId) return false;
                var element = _link.GetLinkDocument()?.GetElement(reference.LinkedElementId);
                return element != null && _filter.AllowElement(element);
            }
        }

        private static List<Element> PickTargets(UIDocument ui, RevitLinkInstance link, ISelectionFilter filter)
        {
            if (link == null)
                return ui.Selection.PickElementsByRectangle(filter, "框選需要定位的本機物件")
                    .GroupBy(e => e.Id).Select(g => g.First()).ToList();
            var references = ui.Selection.PickObjects(ObjectType.LinkedElement, new LinkedTargetFilter(link, filter),
                "選取指定連結內的標註物件，完成後按完成；Esc 取消");
            var source = link.GetLinkDocument() ?? throw new InvalidOperationException("標註對象連結已卸載。");
            var result = new List<Element>();
            foreach (var reference in references)
            {
                var element = source.GetElement(reference.LinkedElementId);
                if (reference.ElementId != link.Id || element == null || !filter.AllowElement(element))
                    throw new InvalidOperationException("選取物件或連結已變更，請重新選取。");
                result.Add(element);
            }
            return result.GroupBy(e => e.Id).Select(g => g.First()).ToList();
        }

        private static Line TargetLine(Element element, RevitLinkInstance link)
        {
            var line = (element.Location as LocationCurve)?.Curve as Line;
            if (line == null) throw new InvalidOperationException("標註對象不是直線管段。");
            return link == null ? line : (Line)line.CreateTransformed(link.GetTotalTransform());
        }

        private static Reference TargetReference(Reference reference, RevitLinkInstance link) =>
            link == null ? reference : reference.CreateLinkReference(link);

        private static void CheckExistingLinkedDimensions(Document doc, View view, RevitLinkInstance target)
        {
            var links = new HashSet<ElementId>();
            if (target != null) links.Add(target.Id);
            if (sourceLink != null && doc.GetElement(sourceLink) is RevitLinkInstance baseline) links.Add(baseline.Id);
            if (links.Count == 0) return;
            var invalid = new List<string>();
            foreach (Dimension dimension in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)))
            {
                try
                {
                    if (dimension.References != null && dimension.References.Cast<Reference>().Any(r => links.Contains(r.ElementId)) &&
                        !dimension.AreReferencesAvailable) invalid.Add(dimension.Id.ToString());
                }
                catch { invalid.Add(dimension.Id + "（無法讀取參照）"); }
            }
            if (invalid.Count > 0)
                TaskDialog.Show("連結尺寸檢查", $"目前視圖有 {invalid.Count} 條尺寸參照失效或無法檢查。\n" +
                    "可能與連結重載、元件刪除或族群變更有關；本次不刪除或自動重建既有尺寸。\n元素 ID：" + string.Join("、", invalid.Take(20)));
        }
    }
}
