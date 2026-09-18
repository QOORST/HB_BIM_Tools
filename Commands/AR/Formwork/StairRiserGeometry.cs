using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    internal static class StairRiserGeometry
    {
        private const double VolumeTolerance = 1e-9;

        // Only subtract actual occupied host volume; never infer a rectangular riser
        // from nominal height/width or discard a small exposed return by area alone.
        internal static Solid TrimHostIntrusions(Solid panel, IList<Solid> hostSolids)
        {
            if (panel == null || panel.Volume <= VolumeTolerance) return null;
            var result = panel;
            foreach (var host in hostSolids)
            {
                CurvedMeshBudget.Checkpoint("裁切樓梯立板與宿主重疊");
                if (host == null || host.Volume <= VolumeTolerance) continue;
                var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    result, host, BooleanOperationsType.Intersect);
                if (intersection == null || intersection.Volume <= VolumeTolerance) continue;
                var difference = BooleanOperationsUtils.ExecuteBooleanOperation(
                    result, host, BooleanOperationsType.Difference);
                if (difference == null)
                    throw new InvalidOperationException("Stair host trimming returned no geometry result.");
                double expected = Math.Max(0, result.Volume - intersection.Volume);
                double tolerance = Math.Max(VolumeTolerance, result.Volume * 1e-6);
                if (Math.Abs(difference.Volume - expected) > tolerance)
                    throw new InvalidOperationException("Stair host trimming failed volume validation.");
                if (difference.Volume <= VolumeTolerance) return null;
                result = difference;
            }
            return result;
        }
    }
}
