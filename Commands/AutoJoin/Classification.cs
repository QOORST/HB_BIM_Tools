using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class ClassifiedElement
    {
        public ClassifiedElement(Element element, CategorySpec categorySpec)
        {
            Element = element;
            CategorySpec = categorySpec;
        }

        public Element Element { get; }

        public CategorySpec CategorySpec { get; }
    }
}
