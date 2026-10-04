using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using YD_RevitTools.LicenseManager.Commands.MEP;

namespace YD_RevitTools.LicenseManager.Helpers
{
    internal static class RevitApiCompatibility
    { internal static long GetIdValue(Autodesk.Revit.DB.ElementId id) => id.Value; }
}

namespace Autodesk.Revit.DB
{
    internal sealed class ElementId { public static readonly ElementId InvalidElementId = new ElementId(-1); internal long Value; internal ElementId(long id) { Value = id; } }
    internal class Element { public ElementId Id; public string Name; }
    internal sealed class Level : Element { public string UniqueId; public double Elevation; }
    internal sealed class ViewSheet : Element { public string SheetNumber; public bool IsPlaceholder; }
    internal sealed class Document { internal List<Element> Elements = new List<Element>(); internal List<SleeveQuantityRow> Rows = new List<SleeveQuantityRow>(); internal List<SleeveQuantityRow> Manual = new List<SleeveQuantityRow>(); }
    internal sealed class FilteredElementCollector : IEnumerable<Element>
    {
        private IEnumerable<Element> _elements;
        internal FilteredElementCollector(Document doc) { _elements = doc.Elements; }
        internal FilteredElementCollector OfClass(Type type) { _elements = _elements.Where(type.IsInstanceOfType); return this; }
        public IEnumerator<Element> GetEnumerator() => _elements.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveQuantityData
    {
        internal static List<SleeveQuantityRow> Read(Autodesk.Revit.DB.Document doc, ISet<long> manual) => doc.Rows
            .Concat(doc.Manual.Where(r => manual?.Contains(r.Id.Value) == true)).ToList();
    }
}
