using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class WallAlignResult
{
    public int WallsInScope { get; set; }
    public int WallsAdjusted { get; set; }
    public int WallsSkipped { get; set; }
    public int FailedOperations { get; set; }
    public List<string> FailureSamples { get; } = new();
}

internal static class WallProfileAligner
{
    private const double MaxSnapDistance = 3.0;

    public static WallAlignResult Run(Document doc, IList<ClassifiedElement> scopedElements)
    {
        var result = new WallAlignResult();

        var wallCandidates = new List<Wall>();
        var referenceTargets = new List<Element>();

        foreach (var item in scopedElements)
        {
            if (item.Element is Wall wall)
            {
                wallCandidates.Add(wall);
            }

            if (item.CategorySpec.Key == "Floor" ||
                item.CategorySpec.Key == "StructuralFloor" ||
                item.CategorySpec.Key == "StructuralFraming")
            {
                referenceTargets.Add(item.Element);
            }
        }

        result.WallsInScope = wallCandidates.Count;

        using var transaction = new Transaction(doc, "Align Wall Profile");
        transaction.Start();

        foreach (var wall in wallCandidates)
        {
            try
            {
                if (!TryAlignWall(doc, wall, referenceTargets))
                {
                    result.WallsSkipped++;
                    continue;
                }

                result.WallsAdjusted++;
            }
            catch (Exception ex)
            {
                result.FailedOperations++;
                if (result.FailureSamples.Count < 5)
                {
                    result.FailureSamples.Add($"Wall({wall.Id}): {ex.Message}");
                }
            }
        }

        transaction.Commit();
        return result;
    }

    private static bool TryAlignWall(Document doc, Wall wall, IList<Element> targets)
    {
        if (wall.WallType.Kind != WallKind.Basic)
        {
            return false;
        }

        var wallBox = wall.get_BoundingBox(null);
        if (wallBox == null)
        {
            return false;
        }

        var wallMin = wallBox.Min.Z;
        var wallMax = wallBox.Max.Z;

        var hasBottom = false;
        var hasTop = false;
        var targetBottom = 0.0;
        var targetTop = 0.0;
        var bestBottomDistance = double.MaxValue;
        var bestTopDistance = double.MaxValue;

        foreach (var target in targets)
        {
            if (target.Id == wall.Id)
            {
                continue;
            }

            var targetBox = target.get_BoundingBox(null);
            if (targetBox == null || !OverlapsInXY(wallBox, targetBox))
            {
                continue;
            }

            var candidateBottom = targetBox.Min.Z;
            var candidateTop = targetBox.Max.Z;

            var bottomDistance = Math.Abs(candidateTop - wallMin);
            if (bottomDistance <= MaxSnapDistance && bottomDistance < bestBottomDistance)
            {
                bestBottomDistance = bottomDistance;
                targetBottom = candidateTop;
                hasBottom = true;
            }

            var topDistance = Math.Abs(candidateBottom - wallMax);
            if (topDistance <= MaxSnapDistance && topDistance < bestTopDistance)
            {
                bestTopDistance = topDistance;
                targetTop = candidateBottom;
                hasTop = true;
            }
        }

        if (!hasBottom && !hasTop)
        {
            return false;
        }

        var baseLevelId = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId() ?? ElementId.InvalidElementId;
        if (baseLevelId == ElementId.InvalidElementId)
        {
            return false;
        }

        var baseLevel = doc.GetElement(baseLevelId) as Level;
        if (baseLevel == null)
        {
            return false;
        }

        var baseOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
        if (hasBottom && baseOffsetParam != null && !baseOffsetParam.IsReadOnly)
        {
            var newBaseOffset = targetBottom - baseLevel.Elevation;
            baseOffsetParam.Set(newBaseOffset);
        }

        var effectiveBaseElevation = baseLevel.Elevation;
        if (baseOffsetParam != null)
        {
            effectiveBaseElevation += baseOffsetParam.AsDouble();
        }

        var topConstraintId = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() ?? ElementId.InvalidElementId;
        if (!hasTop)
        {
            return true;
        }

        if (topConstraintId == ElementId.InvalidElementId)
        {
            var unconnectedHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (unconnectedHeight != null && !unconnectedHeight.IsReadOnly)
            {
                var newHeight = targetTop - effectiveBaseElevation;
                if (newHeight > 0.05)
                {
                    unconnectedHeight.Set(newHeight);
                }
            }

            return true;
        }

        var topLevel = doc.GetElement(topConstraintId) as Level;
        if (topLevel == null)
        {
            return false;
        }

        var topOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
        if (topOffsetParam == null || topOffsetParam.IsReadOnly)
        {
            return false;
        }

        var newTopOffset = targetTop - topLevel.Elevation;
        topOffsetParam.Set(newTopOffset);
        return true;
    }

    private static bool OverlapsInXY(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        return a.Max.X >= b.Min.X &&
               b.Max.X >= a.Min.X &&
               a.Max.Y >= b.Min.Y &&
               b.Max.Y >= a.Min.Y;
    }
    }
}
