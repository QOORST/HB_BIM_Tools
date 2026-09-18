using System;
using System.Collections;
using System.Collections.Generic;

namespace Autodesk.Revit.DB
{
    public sealed class Document
    {
        public bool IsValidObject = true;
        public string Title = "HB_BIM - Test project";
        public readonly List<Material> Materials = new List<Material>();
    }
    public sealed class ElementId
    {
        public static readonly ElementId InvalidElementId = new ElementId(-1);
        public long Value;
        public ElementId(long value) { Value = value; }
        public long GetIdValue() => Value;
    }
    public sealed class Material
    {
        public string Name;
        public ElementId Id;
    }
    public sealed class FilteredElementCollector : IEnumerable<Material>
    {
        private readonly Document _doc;
        public FilteredElementCollector(Document doc) { _doc = doc; }
        public FilteredElementCollector OfClass(Type type) => this;
        public IEnumerator<Material> GetEnumerator() => _doc.Materials.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace Autodesk.Revit.UI
{
    public sealed class UIDocument
    {
        public Autodesk.Revit.DB.Document Document;
        public UIDocument(Autodesk.Revit.DB.Document document) { Document = document; }
    }
    public sealed class UIApplication { public UIDocument ActiveUIDocument; }
    public enum ExternalEventRequest { Accepted }
    public sealed class ExternalEvent { public ExternalEventRequest Raise() => ExternalEventRequest.Accepted; }
}
