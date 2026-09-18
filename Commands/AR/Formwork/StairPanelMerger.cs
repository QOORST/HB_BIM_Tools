using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    // Called per host: material, thickness and source ownership are identical.
    internal static class StairPanelMerger
    {
        private const double Tolerance = 1e-6;
        private const int MaxUnionAttempts = 256;

        internal sealed class Panel
        {
            internal Solid Solid { get; }
            internal XYZ Origin { get; }
            internal XYZ Normal { get; }
            internal XYZ Min { get; }
            internal XYZ Max { get; }
            internal Panel(Solid solid, XYZ origin, XYZ normal)
            {
                Solid = solid;
                Origin = origin;
                Normal = normal;
                var bounds = solid.GetBoundingBox();
                var min = new XYZ(double.MaxValue, double.MaxValue, double.MaxValue);
                var max = new XYZ(double.MinValue, double.MinValue, double.MinValue);
                for (int i = 0; i < 8; i++)
                {
                    var p = bounds.Transform.OfPoint(new XYZ(
                        (i & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                        (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                        (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z));
                    min = new XYZ(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                    max = new XYZ(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                }
                Min = min;
                Max = max;
            }
        }

        private static bool BoundsTouch(Panel a, Panel b)
        {
            return a.Min.X <= b.Max.X + Tolerance && b.Min.X <= a.Max.X + Tolerance
                && a.Min.Y <= b.Max.Y + Tolerance && b.Min.Y <= a.Max.Y + Tolerance
                && a.Min.Z <= b.Max.Z + Tolerance && b.Min.Z <= a.Max.Z + Tolerance;
        }

        internal static bool SameSidePlane(Panel a, Panel b)
        {
            return Math.Abs(a.Normal.Z) < Tolerance && Math.Abs(b.Normal.Z) < Tolerance
                && a.Normal.Subtract(b.Normal).GetLength() < Tolerance
                && Math.Abs(a.Normal.DotProduct(b.Origin.Subtract(a.Origin))) < Tolerance;
        }

        internal static List<Panel> Merge(IList<Panel> panels)
        {
            var result = new List<Panel>();
            int attempts = 0;
            foreach (var source in panels)
            {
                CurvedMeshBudget.Checkpoint("合併樓梯側模");
                var current = source;
                for (int i = 0; i < result.Count && attempts < MaxUnionAttempts; i++)
                {
                    CurvedMeshBudget.Checkpoint("檢查樓梯側模連接");
                    var existing = result[i];
                    if (!SameSidePlane(current, existing) || !BoundsTouch(current, existing)) continue;
                    attempts++;
                    try
                    {
                        var union = BooleanOperationsUtils.ExecuteBooleanOperation(
                            current.Solid, existing.Solid, BooleanOperationsType.Union);
                        if (union == null || SolidUtils.SplitVolumes(union).Count != 1) continue;
                        double sumVolume = current.Solid.Volume + existing.Solid.Volume;
                        if (union.Volume < Math.Max(current.Solid.Volume, existing.Solid.Volume) - Tolerance
                            || union.Volume > sumVolume + Tolerance) continue;
                        // A shared edge in the source faces becomes a shared face in the extrusion.
                        // Point-only contact and disconnected solids must remain separate panels.
                        if (current.Solid.SurfaceArea + existing.Solid.SurfaceArea - union.SurfaceArea <= Tolerance)
                            continue;
                        current = new Panel(union, current.Origin, current.Normal);
                        result.RemoveAt(i);
                        i = -1; // Revisit earlier fragments when a new fragment connects them.
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Stair panel union kept separate: " + ex.Message);
                    }
                }
                result.Add(current);
            }
            return result;
        }
    }
}
