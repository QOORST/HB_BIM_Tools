using Autodesk.Revit.DB;

namespace YDBIM.AutoDimension.Core
{

internal static class DirectionUtils
{
    public static PlacementDirection NearestDirection(View view, XYZ vector)
    {
        XYZ n = vector.Normalize();
        PlacementDirection best = PlacementDirection.East;
        double bestDot = double.MinValue;

        foreach (PlacementDirection candidate in (PlacementDirection[])System.Enum.GetValues(typeof(PlacementDirection)))
        {
            XYZ candidateVector = ToVector(view, candidate);
            double dot = n.DotProduct(candidateVector);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = candidate;
            }
        }

        return best;
    }

    public static XYZ ToVector(View view, PlacementDirection direction)
    {
        XYZ right = view.RightDirection.Normalize();
        XYZ up = view.UpDirection.Normalize();

        return direction switch
        {
            PlacementDirection.East => right,
            PlacementDirection.West => right.Negate(),
            PlacementDirection.North => up,
            PlacementDirection.South => up.Negate(),
            PlacementDirection.NorthEast => (right + up).Normalize(),
            PlacementDirection.NorthWest => (right.Negate() + up).Normalize(),
            PlacementDirection.SouthEast => (right + up.Negate()).Normalize(),
            PlacementDirection.SouthWest => (right.Negate() + up.Negate()).Normalize(),
            _ => right
        };
    }

    public static bool IsParallel(XYZ a, XYZ b, double tolerance = 1e-6)
    {
        XYZ an = a.Normalize();
        XYZ bn = b.Normalize();
        return an.CrossProduct(bn).GetLength() < tolerance || an.CrossProduct(bn.Negate()).GetLength() < tolerance;
    }

    public static XYZ Canonicalize(XYZ v)
    {
        XYZ n = v.Normalize();
        if (n.X < 0.0 || (AlmostZero(n.X) && n.Y < 0.0) || (AlmostZero(n.X) && AlmostZero(n.Y) && n.Z < 0.0))
        {
            n = n.Negate();
        }

        return n;
    }

    public static bool AlmostZero(double value, double eps = 1e-9)
    {
        return value > -eps && value < eps;
    }
}
}

