using Autodesk.Revit.DB;

namespace YDBIM.AutoDimension.Core
{

internal sealed class DatumInfo
{
    public DatumInfo(Element element, XYZ midpoint, XYZ direction, Reference reference)
    {
        Element = element;
        Midpoint = midpoint;
        Direction = direction;
        Reference = reference;
    }

    public Element Element { get; }

    public XYZ Midpoint { get; }

    public XYZ Direction { get; }

    public Reference Reference { get; }
}
}

