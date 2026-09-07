using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal static class ElementCollectorService
{
    public static IList<ClassifiedElement> Collect(UIDocument uiDoc, AutoJoinSettings settings)
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

    private static IList<Element> CollectFromSelection(UIDocument uiDoc)
    {
        var list = new List<Element>();
        foreach (var id in uiDoc.Selection.GetElementIds())
        {
            var element = uiDoc.Document.GetElement(id);
            if (element != null)
            {
                list.Add(element);
            }
        }

        return list;
    }
    }
}
