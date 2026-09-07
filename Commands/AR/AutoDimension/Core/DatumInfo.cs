using Autodesk.Revit.DB;

namespace YDBIM.AutoDimension.Core
{

internal sealed class DatumInfo
{
    public DatumInfo(Element element, XYZ midpoint, XYZ direction, Reference reference, XYZ startPoint = null, XYZ endPoint = null)
    {
        Element = element;
        Midpoint = midpoint;
        Direction = direction;
        Reference = reference;
        StartPoint = startPoint ?? midpoint;
        EndPoint = endPoint ?? midpoint;
    }

    public Element Element { get; }

    public XYZ Midpoint { get; }

    public XYZ Direction { get; }

    public Reference Reference { get; }

    public XYZ StartPoint { get; }

    public XYZ EndPoint { get; }
}
}

