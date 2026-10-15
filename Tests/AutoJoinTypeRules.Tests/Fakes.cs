namespace Autodesk.Revit.DB
{
    public sealed record ElementId(int Value);
    public class Element
    {
        public ElementId Id { get; init; }
        public ElementId TypeId { get; init; }
        public ElementId GetTypeId() => TypeId;
    }
    public sealed class ElementType : Element
    {
        public string FamilyName { get; init; }
        public string Name { get; init; }
    }
    public sealed class Document
    {
        public Dictionary<ElementId, Element> Elements { get; } = new();
        private readonly Dictionary<(int, int), ElementId> cutters = new();
        public int SwitchCount { get; private set; }
        public bool IgnoreSwitch { get; set; }
        public Element GetElement(ElementId id) => Elements.GetValueOrDefault(id);
        private static (int, int) Pair(Element a, Element b)
            => (Math.Min(a.Id.Value, b.Id.Value), Math.Max(a.Id.Value, b.Id.Value));
        public void Join(Element a, Element b, Element cutter) => cutters[Pair(a, b)] = cutter.Id;
        public bool IsCutting(Element a, Element b) => cutters[Pair(a, b)] == a.Id;
        public void Switch(Element a, Element b)
        {
            SwitchCount++;
            if (!IgnoreSwitch) cutters[Pair(a, b)] = IsCutting(a, b) ? b.Id : a.Id;
        }
    }
    public static class JoinGeometryUtils
    {
        public static bool IsCuttingElementInJoin(Document doc, Element a, Element b) => doc.IsCutting(a, b);
        public static void SwitchJoinOrder(Document doc, Element a, Element b) => doc.Switch(a, b);
    }
}

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    public enum AutoJoinScope { VisibleInView, Selection, EntireModel }
    internal sealed record CategorySpec(string Key, bool IsStructural = false);
    internal sealed record ClassifiedElement(Autodesk.Revit.DB.Element Element, CategorySpec CategorySpec);
    // Catalog values mirror production; only the policy/settings surface is needed here.
    internal static class CategoryCatalog
    {
        public static readonly string[] AllKeys = ["Ceiling", "Column", "Floor", "GenericModel", "Roof", "Wall", "StructuralColumn", "StructuralFloor", "StructuralFoundation", "StructuralFraming", "StructuralWall"];
        public static readonly string[] DefaultEnabledKeys = ["StructuralFoundation", "StructuralColumn", "StructuralFraming", "Floor", "Wall"];
        public static readonly string[] DefaultPriorityKeys = [.. DefaultEnabledKeys];
    }
}
