using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

#nullable enable

namespace YDBIM.AutoDimension.Core
{

internal static class VisibleDatumCollector
{
    public static IReadOnlyList<DatumInfo> Collect(Document doc, View view)
    {
        var result = new List<DatumInfo>();

        CollectFromType<Grid>(doc, view, result);
        CollectFromType<Level>(doc, view, result);

        return result;
    }

    private static void CollectFromType<TDatum>(Document doc, View view, IList<DatumInfo> bucket) where TDatum : DatumPlane
    {
        var collector = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(TDatum));

        foreach (Element element in collector)
        {
            if (element is not DatumPlane datum)
            {
                continue;
            }

            Curve? curve = TryGetVisibleCurve(datum, view);
            if (curve is not Line line)
            {
                continue;
            }

            XYZ direction = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
            XYZ midpoint = (line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5;

            Reference reference;
            try
            {
                reference = new Reference(element);
            }
            catch
            {
                continue;
            }

            bucket.Add(new DatumInfo(element, midpoint, direction, reference, line.GetEndPoint(0), line.GetEndPoint(1)));
        }
    }

    private static Curve? TryGetVisibleCurve(DatumPlane datum, View view)
    {
        foreach (DatumExtentType extentType in Enum.GetValues(typeof(DatumExtentType)))
        {
            try
            {
                IList<Curve> curves = datum.GetCurvesInView(extentType, view);
                for (int i = 0; i < curves.Count; i++)
                {
                    if (curves[i] is Line line && line.IsBound)
                    {
                        return line;
                    }
                }
            }
            catch
            {
                // Some extent types are not valid in some views.
            }
        }

        return null;
    }
}
}

