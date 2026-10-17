namespace Autodesk.Revit.DB
{
    public class Document
    {
        public View ActiveView { get; } = new View { Name = "測試平面", Scale = 100 };
        public System.Collections.Generic.List<Element> Elements { get; } = new System.Collections.Generic.List<Element>();
        public Element GetElement(string uid) => Elements.Find(e => e.UniqueId == uid);
    }
    public enum BuiltInCategory { OST_RvtLinks, OST_PipeCurves, OST_PipeTags }
    public enum CategoryType { Model }
    public class Category { public ElementId Id { get; set; } = new ElementId(1); public CategoryType CategoryType => CategoryType.Model; public string Name => "管線"; }
    public class Element { public ElementId Id { get; set; } = new ElementId(1); public string UniqueId { get; set; } = "test"; public string Name { get; set; } = "測試元素"; public Category Category { get; } = new Category(); }
    public class ElementType : Element { public string FamilyName => "測試族群"; }
    public class TextNoteType : ElementType { }
    public enum ViewType { Legend, FloorPlan }
    public class View : Element { public int Scale { get; set; } = 100; public bool IsTemplate => false; public ViewType ViewType { get; set; } }
    public class ViewSheet : View { public bool IsPlaceholder => false; public string SheetNumber => "M-101"; }
    public class RevitLinkInstance : Element { public Document LinkedDocument { get; set; } public Document GetLinkDocument() => LinkedDocument; }
    public class FilteredElementCollector : System.Collections.Generic.IEnumerable<Element>, System.IDisposable
    {
        private System.Collections.Generic.IEnumerable<Element> elements;
        public FilteredElementCollector(Document doc, params ElementId[] ids) { elements = doc.Elements; }
        public FilteredElementCollector OfClass(System.Type type) { elements = System.Linq.Enumerable.Where(elements, type.IsInstanceOfType); return this; }
        public FilteredElementCollector WhereElementIsNotElementType() => this;
        public System.Collections.Generic.IEnumerator<Element> GetEnumerator() => elements.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { }
    }
    public class ElementId
    {
        public long Value { get; }
        public int IntegerValue => (int)Value;
        public ElementId(long value) { Value = value; }
        public override string ToString() => Value.ToString();
        public static ElementId InvalidElementId { get; } = new ElementId(-1);
    }
}
namespace Autodesk.Revit.UI
{
    public class UIApplication { public System.IntPtr MainWindowHandle => System.IntPtr.Zero; }
    public class Selection { public System.Collections.Generic.ICollection<Autodesk.Revit.DB.ElementId> GetElementIds() => new Autodesk.Revit.DB.ElementId[0]; }
    public class UIDocument { public Autodesk.Revit.DB.Document Document { get; } public UIDocument(Autodesk.Revit.DB.Document doc) { Document = doc; } public UIApplication Application { get; } = new UIApplication(); public Selection Selection { get; } = new Selection(); }
}
namespace YD_RevitTools.LicenseManager.Helpers
{
    internal static class IdHelper { public static long GetIdValue(this Autodesk.Revit.DB.ElementId id) => id.Value; }
}
