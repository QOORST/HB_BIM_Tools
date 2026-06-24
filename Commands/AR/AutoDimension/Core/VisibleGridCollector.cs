using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

#nullable enable

namespace YDBIM.AutoDimension.Core
{

internal sealed class GridSelectionItem
{
    public GridSelectionItem(ElementId id, string label, double axisValue)
    {
        Id = id;
        Label = label;
        AxisValue = axisValue;
    }

    public ElementId Id { get; }

    public string Label { get; }

    public double AxisValue { get; }
}

internal static class VisibleGridCollector
{
    public static (IReadOnlyList<GridSelectionItem> Horizontal, IReadOnlyList<GridSelectionItem> Vertical) Collect(Document doc, View view)
    {
        var horizontal = new List<GridSelectionItem>();
        var vertical = new List<GridSelectionItem>();

        XYZ right = view.RightDirection.Normalize();
        XYZ up = view.UpDirection.Normalize();

        var collector = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Grid));

        foreach (Element element in collector)
        {
            if (element is not Grid grid)
            {
                continue;
            }

            Curve? curve = TryGetVisibleCurve(grid, view);
            if (curve is not Line line)
            {
                continue;
            }

            XYZ direction = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
            XYZ midpoint = (line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5;

            double rightDot = Math.Abs(direction.DotProduct(right));
            double upDot = Math.Abs(direction.DotProduct(up));

            string name = string.IsNullOrWhiteSpace(grid.Name) ? ElementIdCompat.ToInt32(grid.Id).ToString() : grid.Name;
            if (rightDot >= upDot)
            {
                double axis = midpoint.DotProduct(up);
                double mm = UnitUtils.ConvertFromInternalUnits(axis, UnitTypeId.Millimeters);
                horizontal.Add(new GridSelectionItem(grid.Id, $"{name} (Y={mm:N0})", axis));
            }
            else
            {
                double axis = midpoint.DotProduct(right);
                double mm = UnitUtils.ConvertFromInternalUnits(axis, UnitTypeId.Millimeters);
                vertical.Add(new GridSelectionItem(grid.Id, $"{name} (X={mm:N0})", axis));
            }
        }

        return (
            horizontal.OrderBy(i => i.AxisValue).ToList(),
            vertical.OrderBy(i => i.AxisValue).ToList());
    }

    private static Curve? TryGetVisibleCurve(Grid grid, View view)
    {
        foreach (DatumExtentType extentType in Enum.GetValues(typeof(DatumExtentType)))
        {
            try
            {
                IList<Curve> curves = grid.GetCurvesInView(extentType, view);
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

