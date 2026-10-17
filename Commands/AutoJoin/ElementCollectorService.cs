using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal static class ElementCollectorService
{
    public static IList<ClassifiedElement> Collect(UIDocument uiDoc, AutoJoinSettings settings)
        => Collect(uiDoc, settings, out _);

    public static IList<ClassifiedElement> Collect(UIDocument uiDoc, AutoJoinSettings settings, out int rawCount)
    {
        var doc = uiDoc.Document;
        var raw = settings.Scope switch
        {
            AutoJoinScope.All => new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements(),
            AutoJoinScope.VisibleInView => new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElements(),
            AutoJoinScope.SelectedElements => CollectFromSelection(uiDoc),
            _ => new List<Element>()
        };

        rawCount = raw.Count;
        var enabled = new HashSet<string>(settings.EnabledCategoryKeys);
        var result = new List<ClassifiedElement>(raw.Count);

        foreach (var element in raw)
        {
            if (element?.Category == null)
            {
                continue;
            }

            var spec = ElementClassifier.TryClassify(element);
            if (spec == null)
            {
                continue;
            }

            // If only base wall/floor is enabled, include structural variants under the same rule.
            if (spec.Key == "StructuralFloor" && !enabled.Contains("StructuralFloor") && enabled.Contains("Floor"))
            {
                spec = CategoryCatalog.ByKey("Floor");
            }

            if (spec.Key == "StructuralWall" && !enabled.Contains("StructuralWall") && enabled.Contains("Wall"))
            {
                spec = CategoryCatalog.ByKey("Wall");
            }

            if (!enabled.Contains(spec.Key))
            {
                continue;
            }

            var bbox = element.get_BoundingBox(null);
            if (bbox == null)
            {
                continue;
            }

            result.Add(new ClassifiedElement(element, spec));
        }

        return result;
    }

    public static void UpdateScopeSummary(UIDocument uiDoc, AutoJoinForm form, bool alignOnly = false)
    {
        try { ReadScopeSummary(uiDoc, form, alignOnly); }
        catch (System.Exception ex)
        {
            form.SetScopeSummary("無法檢查範圍；請切換至模型視圖或重新選取後再檢查。\n" + ex.Message, false);
        }
    }

    private static void ReadScopeSummary(UIDocument uiDoc, AutoJoinForm form, bool alignOnly)
    {
        var settings = form.BuildSettings();
        var elements = Collect(uiDoc, settings, out int rawCount);
        var prefix = settings.Scope == AutoJoinScope.SelectedElements
            ? $"已選取 {uiDoc.Selection.GetElementIds().Count} 項；展開群組後 {rawCount} 個"
            : $"範圍內 {rawCount} 個";
        if (alignOnly)
        {
            int walls = elements.Count(x => x.Element is Wall w && w.WallType.Kind == WallKind.Basic);
            int targets = elements.Count(x => x.CategorySpec.Key == "Floor" || x.CategorySpec.Key == "StructuralFloor" || x.CategorySpec.Key == "StructuralFraming");
            form.SetScopeSummary($"{prefix}；基本牆 {walls}、參考梁板 {targets}、略過 {rawCount - walls - targets}\n" +
                (walls == 0 ? "請納入基本牆。" : targets == 0 ? "請納入參考梁或樓板。" : "符合篩選條件；實際能否對齊仍需檢查幾何。"), walls > 0 && targets > 0);
        }
        else form.SetScopeSummary($"{prefix}；符合條件 {elements.Count}、略過 {rawCount - elements.Count}\n" +
            (elements.Count < 2 ? "至少需要 2 個符合條件的構件；請檢查範圍與類別。" : "數量依類別與外框篩選；不代表一定能接合。"), elements.Count >= 2);
    }

    private static IList<Element> CollectFromSelection(UIDocument uiDoc)
    {
        var list = new List<Element>();
        var visited = new HashSet<ElementId>();
        foreach (var id in uiDoc.Selection.GetElementIds())
        {
            AddSelected(uiDoc.Document, id, visited, list);
        }

        return list;
    }
    private static void AddSelected(Document doc, ElementId id, HashSet<ElementId> visited, List<Element> list)
    {
        if (!visited.Add(id)) return;
        var element = doc.GetElement(id);
        if (element is Group group)
            foreach (var member in group.GetMemberIds()) AddSelected(doc, member, visited, list);
        else if (element != null) list.Add(element);
    }
    }
}
