using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal static class TypeJoinCatalog
    {
        // Call only from an external command or ExternalEvent.Execute.
        internal static IReadOnlyList<TypeJoinChoice> Read(Document document)
        {
            var result = new List<TypeJoinChoice>();
            var seen = new HashSet<(string Category, string Family, string Type)>();
            void Add(string key, ElementType type)
            {
                if (seen.Add((key, type.FamilyName, type.Name)))
                    result.Add(new TypeJoinChoice(key, type.FamilyName, type.Name));
            }

            var categories = CategoryCatalog.Specs.Select(x => x.BuiltInCategory).Distinct().ToList();
            using var typeCollector = new FilteredElementCollector(document);
            using var typeFilter = new ElementMulticategoryFilter(categories);
            var types = typeCollector.WherePasses(typeFilter).WhereElementIsElementType()
                .OfType<ElementType>().ToDictionary(x => x.Id);
            foreach (var type in types.Values)
            {
                if (type.Category == null) continue;
                var spec = CategoryCatalog.Specs.FirstOrDefault(x =>
                    type.Category.Id.Equals(new ElementId(x.BuiltInCategory)));
                if (spec == null || spec.BuiltInCategory == BuiltInCategory.OST_Walls ||
                    spec.BuiltInCategory == BuiltInCategory.OST_Floors) continue;
                Add(spec.Key, type);
            }

            // Structural flags belong to instances. Scan only walls/floors once;
            // never inspect geometry or collect instances once per type.
            using var instances = new FilteredElementCollector(document);
            using var instanceFilter = new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors });
            var classified = new HashSet<(ElementId TypeId, string Key)>();
            foreach (var instance in instances.WherePasses(instanceFilter).WhereElementIsNotElementType())
            {
                var spec = ElementClassifier.TryClassify(instance);
                if (spec == null) continue;
                var typeId = instance.GetTypeId();
                if (classified.Add((typeId, spec.Key)) && types.TryGetValue(typeId, out var type))
                    Add(spec.Key, type);
            }
            return result.OrderBy(x => x.CategoryKey, System.StringComparer.Ordinal)
                .ThenBy(x => x.FamilyName, System.StringComparer.Ordinal)
                .ThenBy(x => x.TypeName, System.StringComparer.Ordinal).ToList();
        }
    }
}
