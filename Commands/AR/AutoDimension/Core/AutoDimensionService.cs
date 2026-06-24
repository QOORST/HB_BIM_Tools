using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YDBIM.AutoDimension.Core
{

internal sealed class AutoDimensionService
{
    private const double VectorTolerance = 1e-6;
    private const double PositionTolerance = 1e-5;
    private const double DuplicateLineTolerance = 1.0 / 304.8;
    private const double NearestGridSearchTolerance = 2.0;

    private sealed class ReferencePoint
    {
        public ReferencePoint(Reference reference, XYZ point, double position, double placementPosition)
        {
            Reference = reference;
            Point = point;
            Position = position;
            PlacementPosition = placementPosition;
        }

        public Reference Reference { get; }

        public XYZ Point { get; }

        public double Position { get; }

        public double PlacementPosition { get; }
    }

    public int CreateDimensions(Document doc, View view, DimensionOptions options)
    {
        DimensionType? dimensionType = ResolveDimensionType(doc, options.DimensionTypeName);

        if (options.ModeType == DimensionMode.BeamWidth)
        {
            return CreateBeamWidthDimensions(doc, view, options.OffsetInternal, dimensionType);
        }

        if (options.ModeType == DimensionMode.BeamGrid)
        {
            return CreateBeamGridDimensions(doc, view, options, dimensionType);
        }

        return CreateColumnSetoutDimensions(doc, view, options, dimensionType);
    }

    private static int CreateBeamGridDimensions(Document doc, View view, DimensionOptions options, DimensionType? dimensionType)
    {
        var allGrids = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .ToDictionary(g => ElementIdCompat.ToInt32(g.Id), g => g);

        IReadOnlyList<DatumInfo> horizontal = CollectSelectedGridDatums(allGrids, options.SelectedHorizontalGridIds, view);
        IReadOnlyList<DatumInfo> vertical = CollectSelectedGridDatums(allGrids, options.SelectedVerticalGridIds, view);

        int created = 0;
        if (horizontal.Count >= 2)
        {
            created += CreateGridDimensionForGroup(doc, view, horizontal, view.RightDirection.Normalize(), options.OffsetInternal, dimensionType);
        }

        if (vertical.Count >= 2)
        {
            created += CreateGridDimensionForGroup(doc, view, vertical, view.UpDirection.Normalize(), options.OffsetInternal, dimensionType);
        }

        return created;
    }

    private static int CreateBeamWidthDimensions(Document doc, View view, double offsetInternal, DimensionType? dimensionType)
    {
        int created = 0;
        XYZ viewNormal = view.ViewDirection.Normalize();
        IReadOnlyList<DatumInfo> visibleGrids = CollectVisibleGridDatums(doc, view);
        IReadOnlyList<FamilyInstance> visibleBeams = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .OfType<FamilyInstance>()
            .ToList();

        foreach (FamilyInstance beam in visibleBeams)
        {
            if (TryCreateBeamWidthDimension(doc, view, beam, viewNormal, visibleGrids, offsetInternal, dimensionType))
            {
                created++;
            }
        }

        if (created == 0)
        {
            return 0;
        }

        created += CreateBeamToBeamChainDimensions(doc, view, visibleBeams, viewNormal, offsetInternal, dimensionType);
        return created;
    }

    private static int CreateBeamToBeamChainDimensions(
        Document doc,
        View view,
        IReadOnlyList<FamilyInstance> visibleBeams,
        XYZ viewNormal,
        double offsetInternal,
        DimensionType? dimensionType)
    {
        var groups = new Dictionary<string, List<(FamilyInstance Beam, XYZ AxisPoint, XYZ AxisDirection, XYZ MeasureDirection)>>(StringComparer.Ordinal);
        foreach (FamilyInstance beam in visibleBeams)
        {
            if (!TryGetBeamAxisPointAndDirection(beam, viewNormal, out XYZ axisPoint, out XYZ axisDirection))
            {
                continue;
            }

            XYZ canonicalAxis = DirectionUtils.Canonicalize(axisDirection);
            XYZ measureDirection = viewNormal.CrossProduct(canonicalAxis);
            if (measureDirection.GetLength() < VectorTolerance)
            {
                continue;
            }

            measureDirection = measureDirection.Normalize();
            string key = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:F4}|{1:F4}|{2:F4}",
                canonicalAxis.X,
                canonicalAxis.Y,
                canonicalAxis.Z);
            if (!groups.TryGetValue(key, out List<(FamilyInstance Beam, XYZ AxisPoint, XYZ AxisDirection, XYZ MeasureDirection)>? group))
            {
                group = new List<(FamilyInstance Beam, XYZ AxisPoint, XYZ AxisDirection, XYZ MeasureDirection)>();
                groups.Add(key, group);
            }

            group.Add((beam, axisPoint, canonicalAxis, measureDirection));
        }

        int created = 0;
        foreach (List<(FamilyInstance Beam, XYZ AxisPoint, XYZ AxisDirection, XYZ MeasureDirection)> group in groups.Values)
        {
            if (group.Count < 2)
            {
                continue;
            }

            XYZ axisDirection = group[0].AxisDirection.Normalize();
            XYZ measureDirection = group[0].MeasureDirection.Normalize();
            var refs = new List<ReferencePoint>();
            double placementAxis = double.MinValue;

            foreach ((FamilyInstance Beam, XYZ AxisPoint, XYZ AxisDirection, XYZ MeasureDirection) item in group)
            {
                IList<(PlanarFace Face, XYZ Normal, XYZ Point)> faces = GetPlanarFaces(item.Beam, viewNormal);
                (PlanarFace Face, XYZ Normal, XYZ Point)? positive = SelectBestFace(faces, measureDirection);
                (PlanarFace Face, XYZ Normal, XYZ Point)? negative = SelectBestFace(faces, measureDirection.Negate());
                if (positive is null || negative is null)
                {
                    continue;
                }

                refs.Add(new ReferencePoint(positive.Value.Face.Reference, positive.Value.Point, positive.Value.Point.DotProduct(measureDirection), 0.0));
                refs.Add(new ReferencePoint(negative.Value.Face.Reference, negative.Value.Point, negative.Value.Point.DotProduct(measureDirection), 0.0));

                BoundingBoxXYZ? bbox = item.Beam.get_BoundingBox(view) ?? item.Beam.get_BoundingBox(null);
                if (bbox is not null)
                {
                    placementAxis = Math.Max(placementAxis, ProjectBoundingBoxMax(bbox, axisDirection));
                }
                else
                {
                    placementAxis = Math.Max(placementAxis, item.AxisPoint.DotProduct(axisDirection));
                }
            }

            refs = refs
                .OrderBy(r => r.Position)
                .Aggregate(new List<ReferencePoint>(), (unique, current) =>
                {
                    if (unique.Count == 0 || Math.Abs(unique[unique.Count - 1].Position - current.Position) > PositionTolerance)
                    {
                        unique.Add(current);
                    }

                    return unique;
                });

            if (refs.Count < 2 || placementAxis == double.MinValue)
            {
                continue;
            }

            double targetAxis = placementAxis + offsetInternal;
            double minMeasure = refs.Min(r => r.Position);
            double maxMeasure = refs.Max(r => r.Position);
            XYZ basePoint = group[0].AxisPoint;
            double baseAxis = basePoint.DotProduct(axisDirection);
            double baseMeasure = basePoint.DotProduct(measureDirection);
            XYZ origin = basePoint + (axisDirection * (targetAxis - baseAxis));
            XYZ start = origin + (measureDirection * (minMeasure - baseMeasure));
            XYZ end = origin + (measureDirection * (maxMeasure - baseMeasure));
            if (start.DistanceTo(end) < VectorTolerance)
            {
                continue;
            }

            var refArray = new ReferenceArray();
            foreach (ReferencePoint rp in refs)
            {
                refArray.Append(rp.Reference);
            }

            try
            {
                Line dimLine = Line.CreateBound(start, end);
                if (HasSimilarDimension(doc, view, dimLine))
                {
                    continue;
                }

                Dimension dimension = doc.Create.NewDimension(view, dimLine, refArray);
                if (dimensionType is not null && dimension.GetTypeId() != dimensionType.Id)
                {
                    dimension.ChangeTypeId(dimensionType.Id);
                }

                created++;
            }
            catch
            {
                // Some beam families expose references that cannot be mixed in one chain.
            }
        }

        return created;
    }

    private static bool TryCreateBeamWidthDimension(
        Document doc,
        View view,
        FamilyInstance beam,
        XYZ viewNormal,
        IReadOnlyList<DatumInfo> visibleGrids,
        double offsetInternal,
        DimensionType? dimensionType)
    {
        if (!TryGetBeamAxisPointAndDirection(beam, viewNormal, out XYZ axisPoint, out XYZ axisDirection))
        {
            return false;
        }

        XYZ measureDirection = viewNormal.CrossProduct(axisDirection);
        if (measureDirection.GetLength() < VectorTolerance)
        {
            return false;
        }

        measureDirection = measureDirection.Normalize();

        BoundingBoxXYZ? bbox = beam.get_BoundingBox(view) ?? beam.get_BoundingBox(null);
        if (bbox is null)
        {
            return false;
        }

        IList<(PlanarFace Face, XYZ Normal, XYZ Point)> faces = GetPlanarFaces(beam, viewNormal);
        (PlanarFace Face, XYZ Normal, XYZ Point)? positive = SelectBestFace(faces, measureDirection);
        (PlanarFace Face, XYZ Normal, XYZ Point)? negative = SelectBestFace(faces, measureDirection.Negate());
        if (positive is null || negative is null)
        {
            return TryCreateBeamWidthWithFamilyReferences(
                doc,
                view,
                beam,
                bbox,
                axisPoint,
                axisDirection,
                measureDirection,
                visibleGrids,
                offsetInternal,
                dimensionType);
        }

        double positivePosition = positive.Value.Point.DotProduct(measureDirection);
        double negativePosition = negative.Value.Point.DotProduct(measureDirection);
        double faceMin = Math.Min(positivePosition, negativePosition);
        double faceMax = Math.Max(positivePosition, negativePosition);

        DatumInfo? grid = FindNearestPassingGrid(visibleGrids, bbox, measureDirection, axisDirection);
        if (grid is not null)
        {
            double gridPosition = grid.Midpoint.DotProduct(measureDirection);
            if (gridPosition >= faceMin - PositionTolerance && gridPosition <= faceMax + PositionTolerance)
            {
                var refs = new List<ReferencePoint>
                {
                    new ReferencePoint(positive.Value.Face.Reference, positive.Value.Point, positivePosition, 0.0),
                    new ReferencePoint(grid.Reference, grid.Midpoint, gridPosition, 0.0),
                    new ReferencePoint(negative.Value.Face.Reference, negative.Value.Point, negativePosition, 0.0)
                };

                if (TryCreateBeamWidthDimensionElement(doc, view, axisPoint, axisDirection, measureDirection, offsetInternal, refs, dimensionType, out Line? _))
                {
                    return true;
                }
            }
        }

        return TryCreateBeamWidthWithCenterLine(
            doc,
            view,
            beam,
            axisPoint,
            axisDirection,
            measureDirection,
            positive.Value.Face.Reference,
            positive.Value.Point,
            positivePosition,
            negative.Value.Face.Reference,
            negative.Value.Point,
            negativePosition,
            offsetInternal,
            dimensionType,
            out Line? _);
    }

    private static bool TryCreateBeamWidthWithFamilyReferences(
        Document doc,
        View view,
        FamilyInstance beam,
        BoundingBoxXYZ bbox,
        XYZ axisPoint,
        XYZ axisDirection,
        XYZ measureDirection,
        IReadOnlyList<DatumInfo> visibleGrids,
        double offsetInternal,
        DimensionType? dimensionType)
    {
        if (!TryGetBeamSideReferences(beam, measureDirection, out Reference? negativeReference, out Reference? positiveReference) ||
            negativeReference is null ||
            positiveReference is null)
        {
            return false;
        }

        double baseMeasure = axisPoint.DotProduct(measureDirection);
        double negativePosition = double.MaxValue;
        double positivePosition = double.MinValue;
        foreach (XYZ corner in EnumerateBoundingBoxCorners(bbox))
        {
            double position = corner.DotProduct(measureDirection);
            negativePosition = Math.Min(negativePosition, position);
            positivePosition = Math.Max(positivePosition, position);
        }

        if (Math.Abs(positivePosition - negativePosition) < PositionTolerance)
        {
            return false;
        }

        var refs = new List<ReferencePoint>
        {
            new ReferencePoint(negativeReference, axisPoint + (measureDirection * (negativePosition - baseMeasure)), negativePosition, 0.0),
            new ReferencePoint(positiveReference, axisPoint + (measureDirection * (positivePosition - baseMeasure)), positivePosition, 0.0)
        };

        DatumInfo? grid = FindNearestPassingGrid(visibleGrids, bbox, measureDirection, axisDirection);
        if (grid is not null)
        {
            double gridPosition = grid.Midpoint.DotProduct(measureDirection);
            if (gridPosition >= negativePosition - PositionTolerance && gridPosition <= positivePosition + PositionTolerance)
            {
                refs.Add(new ReferencePoint(grid.Reference, grid.Midpoint, gridPosition, 0.0));
            }
        }

        return TryCreateBeamWidthDimensionElement(doc, view, axisPoint, axisDirection, measureDirection, offsetInternal, refs, dimensionType, out Line? _);
    }

    private static bool TryCreateBeamSpanDimension(
        Document doc,
        View view,
        XYZ axisPoint,
        XYZ axisDirection,
        XYZ measureDirection,
        IReadOnlyList<FamilyInstance> visibleColumns,
        HashSet<string> createdSpanLineKeys,
        double offsetInternal,
        DimensionType? dimensionType,
        Line? preferredLine)
    {
        var supportCandidates = new List<(Reference FaceReference, double AxisPosition)>();
        double beamMeasure = axisPoint.DotProduct(measureDirection);
        XYZ lineDirection = axisDirection.Normalize();
        double beamAxis = axisPoint.DotProduct(lineDirection);

        foreach (FamilyInstance column in visibleColumns)
        {
            BoundingBoxXYZ? bbox = column.get_BoundingBox(view) ?? column.get_BoundingBox(null);
            if (bbox is null)
            {
                continue;
            }

            double minMeasure = double.MaxValue;
            double maxMeasure = double.MinValue;
            foreach (XYZ corner in EnumerateBoundingBoxCorners(bbox))
            {
                double m = corner.DotProduct(measureDirection);
                minMeasure = Math.Min(minMeasure, m);
                maxMeasure = Math.Max(maxMeasure, m);
            }

            if (beamMeasure < minMeasure - PositionTolerance || beamMeasure > maxMeasure + PositionTolerance)
            {
                continue;
            }

            IList<(PlanarFace Face, XYZ Normal, XYZ Point)> faces = GetPlanarFaces(column, view.ViewDirection.Normalize());
            if (faces.Count == 0)
            {
                continue;
            }

            (PlanarFace Face, XYZ Normal, XYZ Point)? towardPositive = SelectBestFace(faces, lineDirection);
            (PlanarFace Face, XYZ Normal, XYZ Point)? towardNegative = SelectBestFace(faces, lineDirection.Negate());
            if (towardPositive is null || towardNegative is null)
            {
                continue;
            }

            double posAxis = towardPositive.Value.Point.DotProduct(lineDirection);
            double negAxis = towardNegative.Value.Point.DotProduct(lineDirection);

            if (posAxis <= beamAxis)
            {
                supportCandidates.Add((towardPositive.Value.Face.Reference, posAxis));
            }

            if (negAxis >= beamAxis)
            {
                supportCandidates.Add((towardNegative.Value.Face.Reference, negAxis));
            }
        }

        var left = supportCandidates
            .Where(c => c.AxisPosition < beamAxis - PositionTolerance)
            .OrderByDescending(c => c.AxisPosition)
            .FirstOrDefault();
        var right = supportCandidates
            .Where(c => c.AxisPosition > beamAxis + PositionTolerance)
            .OrderBy(c => c.AxisPosition)
            .FirstOrDefault();

        if (left.FaceReference is null || right.FaceReference is null)
        {
            return false;
        }

        double targetMeasure = preferredLine is null
            ? beamMeasure + offsetInternal
            : preferredLine.GetEndPoint(0).DotProduct(measureDirection);

        XYZ canonicalDir = DirectionUtils.Canonicalize(lineDirection);
        string spanLineKey = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:F6}|{1:F6}|{2:F6}|{3:F6}",
            canonicalDir.X,
            canonicalDir.Y,
            canonicalDir.Z,
            targetMeasure);
        if (createdSpanLineKeys.Contains(spanLineKey))
        {
            return false;
        }

        XYZ lineOrigin = axisPoint + (measureDirection * (targetMeasure - beamMeasure));
        double originProjection = lineOrigin.DotProduct(lineDirection);

        var ordered = supportCandidates
            .OrderBy(c => c.AxisPosition)
            .ToList();

        var unique = new List<(Reference FaceReference, double AxisPosition)>();
        foreach ((Reference FaceReference, double AxisPosition) item in ordered)
        {
            if (unique.Count > 0 && Math.Abs(unique[unique.Count - 1].AxisPosition - item.AxisPosition) < PositionTolerance)
            {
                continue;
            }

            unique.Add(item);
        }

        if (unique.Count < 2)
        {
            return false;
        }

        XYZ start = lineOrigin + (lineDirection * (unique[0].AxisPosition - originProjection));
        XYZ end = lineOrigin + (lineDirection * (unique[unique.Count - 1].AxisPosition - originProjection));
        if (start.DistanceTo(end) < VectorTolerance)
        {
            return false;
        }

        var refs = new ReferenceArray();
        foreach ((Reference FaceReference, double _) in unique)
        {
            refs.Append(FaceReference);
        }

        if (refs.Size < 2)
        {
            return false;
        }

        try
        {
            if (HasSimilarDimension(doc, view, Line.CreateBound(start, end)))
            {
                return false;
            }

            Dimension dimension = doc.Create.NewDimension(view, Line.CreateBound(start, end), refs);
            if (dimensionType is not null && dimension.GetTypeId() != dimensionType.Id)
            {
                dimension.ChangeTypeId(dimensionType.Id);
            }

            createdSpanLineKeys.Add(spanLineKey);
            return true;
        }
        catch
        {
            // Ignore supplemental span failure and keep primary beam width dimensions.
            return false;
        }
    }

    private static bool TryCreateBeamWidthWithCenterLine(
        Document doc,
        View view,
        FamilyInstance beam,
        XYZ axisPoint,
        XYZ axisDirection,
        XYZ measureDirection,
        Reference positiveFaceReference,
        XYZ positivePoint,
        double positivePosition,
        Reference negativeFaceReference,
        XYZ negativePoint,
        double negativePosition,
        double offsetInternal,
        DimensionType? dimensionType,
        out Line? createdLine)
    {
        createdLine = null;
        double centerPosition = axisPoint.DotProduct(measureDirection);
        foreach (Reference centerReference in GetBeamCenterReferences(beam))
        {
            var refs = new List<ReferencePoint>
            {
                new ReferencePoint(positiveFaceReference, positivePoint, positivePosition, 0.0),
                new ReferencePoint(centerReference, axisPoint, centerPosition, 0.0),
                new ReferencePoint(negativeFaceReference, negativePoint, negativePosition, 0.0)
            };

            if (TryCreateBeamWidthDimensionElement(doc, view, axisPoint, axisDirection, measureDirection, offsetInternal, refs, dimensionType, out Line? line))
            {
                createdLine = line;
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateBeamWidthDimensionElement(
        Document doc,
        View view,
        XYZ axisPoint,
        XYZ axisDirection,
        XYZ measureDirection,
        double offsetInternal,
        IReadOnlyList<ReferencePoint> refs,
        DimensionType? dimensionType,
        out Line? createdLine)
    {
        createdLine = null;
        XYZ placementOrigin = axisPoint + (axisDirection.Normalize() * offsetInternal);
        XYZ start = placementOrigin + (measureDirection * (refs.Min(r => r.Position) - placementOrigin.DotProduct(measureDirection)));
        XYZ end = placementOrigin + (measureDirection * (refs.Max(r => r.Position) - placementOrigin.DotProduct(measureDirection)));
        if (start.DistanceTo(end) < VectorTolerance)
        {
            return false;
        }

        var refArray = new ReferenceArray();
        foreach (ReferencePoint rp in refs.OrderBy(r => r.Position))
        {
            refArray.Append(rp.Reference);
        }

        try
        {
            createdLine = Line.CreateBound(start, end);
            if (HasSimilarDimension(doc, view, createdLine, refArray))
            {
                return false;
            }

            Dimension dimension = doc.Create.NewDimension(view, createdLine, refArray);
            if (dimensionType is not null && dimension.GetTypeId() != dimensionType.Id)
            {
                dimension.ChangeTypeId(dimensionType.Id);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<Reference> GetBeamCenterReferences(FamilyInstance beam)
    {
        foreach (FamilyInstanceReferenceType referenceType in new[]
        {
            FamilyInstanceReferenceType.CenterLeftRight,
            FamilyInstanceReferenceType.CenterFrontBack,
            FamilyInstanceReferenceType.CenterElevation
        })
        {
            IList<Reference>? references = null;
            try
            {
                references = beam.GetReferences(referenceType);
            }
            catch
            {
                // Some families may not expose this reference type.
            }

            if (references is null)
            {
                continue;
            }

            foreach (Reference reference in references)
            {
                if (reference is not null)
                {
                    yield return reference;
                }
            }
        }
    }

    private static bool TryGetBeamSideReferences(FamilyInstance beam, XYZ measureDirection, out Reference? negativeReference, out Reference? positiveReference)
    {
        negativeReference = null;
        positiveReference = null;

        bool preferLeftRight = Math.Abs(measureDirection.X) >= Math.Abs(measureDirection.Y);
        FamilyInstanceReferenceType negativeType = preferLeftRight
            ? FamilyInstanceReferenceType.Left
            : FamilyInstanceReferenceType.Back;
        FamilyInstanceReferenceType positiveType = preferLeftRight
            ? FamilyInstanceReferenceType.Right
            : FamilyInstanceReferenceType.Front;

        negativeReference = GetFirstFamilyReference(beam, negativeType);
        positiveReference = GetFirstFamilyReference(beam, positiveType);
        if (negativeReference is not null && positiveReference is not null)
        {
            return true;
        }

        negativeReference = GetFirstFamilyReference(beam, preferLeftRight ? FamilyInstanceReferenceType.Back : FamilyInstanceReferenceType.Left);
        positiveReference = GetFirstFamilyReference(beam, preferLeftRight ? FamilyInstanceReferenceType.Front : FamilyInstanceReferenceType.Right);
        return negativeReference is not null && positiveReference is not null;
    }

    private static bool TryGetBeamAxisPointAndDirection(FamilyInstance beam, XYZ viewNormal, out XYZ axisPoint, out XYZ axisDirection)
    {
        axisPoint = XYZ.Zero;
        axisDirection = XYZ.Zero;

        if (beam.Location is not LocationCurve locationCurve)
        {
            BoundingBoxXYZ? bbox = beam.get_BoundingBox(null);
            if (bbox is null)
            {
                return false;
            }

            XYZ center = (bbox.Min + bbox.Max) * 0.5;
            XYZ xDirection = XYZ.BasisX - (viewNormal * XYZ.BasisX.DotProduct(viewNormal));
            XYZ yDirection = XYZ.BasisY - (viewNormal * XYZ.BasisY.DotProduct(viewNormal));
            if (xDirection.GetLength() < VectorTolerance || yDirection.GetLength() < VectorTolerance)
            {
                return false;
            }

            xDirection = xDirection.Normalize();
            yDirection = yDirection.Normalize();
            double xRange = ProjectBoundingBoxRange(bbox, xDirection);
            double yRange = ProjectBoundingBoxRange(bbox, yDirection);
            axisPoint = center;
            axisDirection = xRange >= yRange ? xDirection : yDirection;
            return true;
        }

        if (locationCurve.Curve is not Line line || !line.IsBound)
        {
            return false;
        }

        XYZ start = line.GetEndPoint(0);
        XYZ end = line.GetEndPoint(1);
        XYZ rawDirection = end - start;
        XYZ projectedDirection = rawDirection - (viewNormal * rawDirection.DotProduct(viewNormal));
        if (projectedDirection.GetLength() < VectorTolerance)
        {
            return false;
        }

        axisPoint = (start + end) * 0.5;
        axisDirection = projectedDirection.Normalize();
        return true;
    }

    private static IReadOnlyList<DatumInfo> CollectSelectedGridDatums(
        IReadOnlyDictionary<int, Grid> allGrids,
        IReadOnlyList<ElementId> selectedIds,
        View view)
    {
        var result = new List<DatumInfo>();

        foreach (ElementId id in selectedIds)
        {
            if (!allGrids.TryGetValue(ElementIdCompat.ToInt32(id), out Grid? grid))
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
            result.Add(new DatumInfo(grid, midpoint, direction, new Reference(grid)));
        }

        return result;
    }

    private static int CreateGridDimensionForGroup(
        Document doc,
        View view,
        IReadOnlyList<DatumInfo> group,
        XYZ preferredVector,
        double offsetInternal,
        DimensionType? dimensionType)
    {
        XYZ viewNormal = view.ViewDirection.Normalize();
        XYZ datumDir = DirectionUtils.Canonicalize(group[0].Direction);
        XYZ dimLineDir = viewNormal.CrossProduct(datumDir);
        if (dimLineDir.GetLength() < VectorTolerance)
        {
            return 0;
        }

        dimLineDir = dimLineDir.Normalize();
        int side = ResolveSideSign(preferredVector, datumDir);
        Line? dimLine = BuildDimensionLine(group, datumDir, dimLineDir, offsetInternal, side);
        if (dimLine is null)
        {
            return 0;
        }

        IReadOnlyList<DatumInfo> sortedGroup = group
            .OrderBy(d => d.Midpoint.DotProduct(dimLineDir))
            .ToList();
        ReferenceArray references = BuildSortedReferences(sortedGroup, dimLineDir);
        if (references.Size < 2)
        {
            return 0;
        }

        int created = 0;
        try
        {
            if (!HasSimilarDimension(doc, view, dimLine, references))
            {
                Dimension dimension = doc.Create.NewDimension(view, dimLine, references);
                if (dimensionType is not null && dimension.GetTypeId() != dimensionType.Id)
                {
                    dimension.ChangeTypeId(dimensionType.Id);
                }

                created++;
            }

            if (sortedGroup.Count > 2)
            {
                ReferenceArray overallReferences = BuildOverallReferences(sortedGroup);
                Line? overallLine = BuildDimensionLine(sortedGroup, datumDir, dimLineDir, offsetInternal * 2.0, side);
                if (overallLine is not null &&
                    overallReferences.Size == 2 &&
                    !HasSimilarDimension(doc, view, overallLine, overallReferences))
                {
                    Dimension overallDimension = doc.Create.NewDimension(view, overallLine, overallReferences);
                    if (dimensionType is not null && overallDimension.GetTypeId() != dimensionType.Id)
                    {
                        overallDimension.ChangeTypeId(dimensionType.Id);
                    }

                    created++;
                }
            }

            return created;
        }
        catch
        {
            return created;
        }
    }

    private static int CreateColumnSetoutDimensions(Document doc, View view, DimensionOptions options, DimensionType? dimensionType)
    {
        int created = 0;
        XYZ right = view.RightDirection.Normalize();
        XYZ up = view.UpDirection.Normalize();
        var createdLineKeys = new HashSet<string>(StringComparer.Ordinal);

        if (options.ColumnLeftRightSide == LeftRightSide.Left)
        {
            created += CreateColumnSetoutForDirection(doc, view, right.Negate(), options.OffsetInternal, dimensionType, createdLineKeys);
        }
        else if (options.ColumnLeftRightSide == LeftRightSide.Right)
        {
            created += CreateColumnSetoutForDirection(doc, view, right, options.OffsetInternal, dimensionType, createdLineKeys);
        }

        if (options.ColumnFrontBackSide == FrontBackSide.Front)
        {
            created += CreateColumnSetoutForDirection(doc, view, up, options.OffsetInternal, dimensionType, createdLineKeys);
        }
        else if (options.ColumnFrontBackSide == FrontBackSide.Back)
        {
            created += CreateColumnSetoutForDirection(doc, view, up.Negate(), options.OffsetInternal, dimensionType, createdLineKeys);
        }

        return created;
    }

    private static int CreateColumnSetoutForDirection(
        Document doc,
        View view,
        XYZ placeDirection,
        double offsetInternal,
        DimensionType? dimensionType,
        ISet<string> createdLineKeys)
    {
        XYZ measureDirection = view.ViewDirection.Normalize().CrossProduct(placeDirection);
        if (measureDirection.GetLength() < VectorTolerance)
        {
            return 0;
        }

        measureDirection = measureDirection.Normalize();
        IReadOnlyList<DatumInfo> visibleGrids = CollectVisibleGridDatums(doc, view);
        if (visibleGrids.Count == 0)
        {
            return 0;
        }

        int created = 0;
        foreach (FamilyInstance column in CollectVisibleColumns(doc, view))
        {
            if (!IsColumnLikeInView(column, view))
            {
                continue;
            }

            if (!TryBuildColumnGridDimension(doc, view, column, visibleGrids, measureDirection, placeDirection, offsetInternal, dimensionType, createdLineKeys))
            {
                continue;
            }

            created++;
        }

        return created;
    }

    private static IReadOnlyList<FamilyInstance> CollectVisibleColumns(Document doc, View view)
    {
        var columns = new Dictionary<int, FamilyInstance>();
        foreach (BuiltInCategory category in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns })
        {
            foreach (FamilyInstance column in new FilteredElementCollector(doc, view.Id)
                         .WhereElementIsNotElementType()
                         .OfCategory(category)
                         .OfType<FamilyInstance>())
            {
                columns[ElementIdCompat.ToInt32(column.Id)] = column;
            }
        }

        return columns.Values.ToList();
    }

    private static bool IsColumnLikeInView(FamilyInstance column, View view)
    {
        BoundingBoxXYZ? bbox = column.get_BoundingBox(view) ?? column.get_BoundingBox(null);
        if (bbox is null)
        {
            return false;
        }

        double alongRight = ProjectBoundingBoxRange(bbox, view.RightDirection.Normalize());
        double alongUp = ProjectBoundingBoxRange(bbox, view.UpDirection.Normalize());
        double smaller = Math.Min(alongRight, alongUp);
        double larger = Math.Max(alongRight, alongUp);
        if (smaller < 1.0 / 304.8 || larger < 1.0 / 304.8)
        {
            return false;
        }

        Category? category = column.Category;
        int categoryId = category is null ? 0 : ElementIdCompat.ToInt32(category.Id);
        if (categoryId == (int)BuiltInCategory.OST_StructuralColumns)
        {
            return true;
        }

        return larger / smaller <= 6.0;
    }

    private static bool TryBuildColumnGridDimension(
        Document doc,
        View view,
        FamilyInstance column,
        IReadOnlyList<DatumInfo> visibleGrids,
        XYZ measureDirection,
        XYZ placeDirection,
        double offsetInternal,
        DimensionType? dimensionType,
        ISet<string> createdLineKeys)
    {
        IList<(PlanarFace Face, XYZ Normal, XYZ Point)> faces = GetPlanarFaces(column, view.ViewDirection.Normalize());
        (PlanarFace Face, XYZ Normal, XYZ Point)? positive = faces.Count == 0 ? null : SelectBestFace(faces, measureDirection);
        (PlanarFace Face, XYZ Normal, XYZ Point)? negative = faces.Count == 0 ? null : SelectBestFace(faces, measureDirection.Negate());

        BoundingBoxXYZ? bbox = column.get_BoundingBox(view) ?? column.get_BoundingBox(null);
        if (bbox is null)
        {
            return false;
        }

        DatumInfo? grid = FindNearestPassingGrid(visibleGrids, bbox, measureDirection, placeDirection);
        if (grid is null)
        {
            return false;
        }

        if (positive is null || negative is null)
        {
            if (TryBuildColumnReferenceGridDimension(
                    doc,
                    view,
                    column,
                    bbox,
                    grid,
                    measureDirection,
                    placeDirection,
                    offsetInternal,
                    dimensionType,
                    createdLineKeys))
            {
                return true;
            }

            if (TryBuildColumnCenterGridDimension(
                doc,
                view,
                column,
                bbox,
                grid,
                measureDirection,
                placeDirection,
                offsetInternal,
                dimensionType,
                createdLineKeys))
            {
                return true;
            }

            return TryBuildElementReferenceGridDimension(
                doc,
                view,
                new Reference(column),
                bbox,
                grid,
                measureDirection,
                placeDirection,
                offsetInternal,
                dimensionType,
                createdLineKeys);
        }

        double positivePosition = positive.Value.Point.DotProduct(measureDirection);
        double negativePosition = negative.Value.Point.DotProduct(measureDirection);
        double gridPosition = grid.Midpoint.DotProduct(measureDirection);
        double minAlongMeasure = Math.Min(Math.Min(positivePosition, negativePosition), gridPosition);
        double maxAlongMeasure = Math.Max(Math.Max(positivePosition, negativePosition), gridPosition);
        if (Math.Abs(maxAlongMeasure - minAlongMeasure) < PositionTolerance)
        {
            return false;
        }

        double anchorAlongPlacement = ProjectBoundingBoxMax(bbox, placeDirection) + offsetInternal;
        XYZ center = (bbox.Min + bbox.Max) * 0.5;
        double baseMeasure = center.DotProduct(measureDirection);
        double basePlacement = center.DotProduct(placeDirection);

        XYZ start = center
            + (measureDirection * (minAlongMeasure - baseMeasure))
            + (placeDirection * (anchorAlongPlacement - basePlacement));
        XYZ end = center
            + (measureDirection * (maxAlongMeasure - baseMeasure))
            + (placeDirection * (anchorAlongPlacement - basePlacement));
        if (start.DistanceTo(end) < VectorTolerance)
        {
            return false;
        }

        var references = new List<ReferencePoint>
        {
            new ReferencePoint(positive.Value.Face.Reference, positive.Value.Point, positivePosition, anchorAlongPlacement),
            new ReferencePoint(new Reference(grid.Element), grid.Midpoint, gridPosition, anchorAlongPlacement),
            new ReferencePoint(negative.Value.Face.Reference, negative.Value.Point, negativePosition, anchorAlongPlacement)
        };

        var refArray = new ReferenceArray();
        foreach (ReferencePoint rp in references.OrderBy(r => r.Position))
        {
            refArray.Append(rp.Reference);
        }

        try
        {
            Line dimLine = Line.CreateBound(start, end);
            string lineKey = BuildDimensionLineKey(view, dimLine);
            if (createdLineKeys.Contains(lineKey) || HasSimilarDimension(doc, view, dimLine, refArray))
            {
                return false;
            }

            Dimension dimension = doc.Create.NewDimension(view, dimLine, refArray);
            if (dimensionType is not null && dimension.GetTypeId() != dimensionType.Id)
            {
                dimension.ChangeTypeId(dimensionType.Id);
            }

            createdLineKeys.Add(lineKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBuildColumnReferenceGridDimension(
        Document doc,
        View view,
        FamilyInstance column,
        BoundingBoxXYZ bbox,
        DatumInfo grid,
        XYZ measureDirection,
        XYZ placeDirection,
        double offsetInternal,
        DimensionType? dimensionType,
        ISet<string> createdLineKeys)
    {
        if (!TryGetColumnSideReferences(column, measureDirection, out Reference? negativeReference, out Reference? positiveReference) ||
            negativeReference is null ||
            positiveReference is null)
        {
            return false;
        }

        XYZ center = (bbox.Min + bbox.Max) * 0.5;
        double negativePosition = double.MaxValue;
        double positivePosition = double.MinValue;
        foreach (XYZ corner in EnumerateBoundingBoxCorners(bbox))
        {
            double position = corner.DotProduct(measureDirection);
            negativePosition = Math.Min(negativePosition, position);
            positivePosition = Math.Max(positivePosition, position);
        }

        double gridPosition = grid.Midpoint.DotProduct(measureDirection);
        if (gridPosition < negativePosition - PositionTolerance || gridPosition > positivePosition + PositionTolerance)
        {
            return false;
        }

        double minAlongMeasure = Math.Min(negativePosition, gridPosition);
        double maxAlongMeasure = Math.Max(positivePosition, gridPosition);
        if (Math.Abs(maxAlongMeasure - minAlongMeasure) < PositionTolerance)
        {
            return false;
        }

        double anchorAlongPlacement = ProjectBoundingBoxMax(bbox, placeDirection) + offsetInternal;
        double baseMeasure = center.DotProduct(measureDirection);
        double basePlacement = center.DotProduct(placeDirection);
        XYZ start = center
            + (measureDirection * (minAlongMeasure - baseMeasure))
            + (placeDirection * (anchorAlongPlacement - basePlacement));
        XYZ end = center
            + (measureDirection * (maxAlongMeasure - baseMeasure))
            + (placeDirection * (anchorAlongPlacement - basePlacement));
        if (start.DistanceTo(end) < VectorTolerance)
        {
            return false;
        }

        var references = new List<ReferencePoint>
        {
            new ReferencePoint(negativeReference, center + (measureDirection * (negativePosition - baseMeasure)), negativePosition, anchorAlongPlacement),
            new ReferencePoint(new Reference(grid.Element), grid.Midpoint, gridPosition, anchorAlongPlacement),
            new ReferencePoint(positiveReference, center + (measureDirection * (positivePosition - baseMeasure)), positivePosition, anchorAlongPlacement)
        };

        var refArray = new ReferenceArray();
        foreach (ReferencePoint rp in references.OrderBy(r => r.Position))
        {
            refArray.Append(rp.Reference);
        }

        try
        {
            Line dimLine = Line.CreateBound(start, end);
            string lineKey = BuildDimensionLineKey(view, dimLine);
            if (createdLineKeys.Contains(lineKey) || HasSimilarDimension(doc, view, dimLine, refArray))
            {
                return false;
            }

            Dimension dimension = doc.Create.NewDimension(view, dimLine, refArray);
            if (dimensionType is not null && dimension.GetTypeId() != dimensionType.Id)
            {
                dimension.ChangeTypeId(dimensionType.Id);
            }

            createdLineKeys.Add(lineKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetColumnSideReferences(FamilyInstance column, XYZ measureDirection, out Reference? negativeReference, out Reference? positiveReference)
    {
        negativeReference = null;
        positiveReference = null;

        bool preferLeftRight = Math.Abs(measureDirection.X) >= Math.Abs(measureDirection.Y);
        FamilyInstanceReferenceType negativeType = preferLeftRight
            ? FamilyInstanceReferenceType.Left
            : FamilyInstanceReferenceType.Back;
        FamilyInstanceReferenceType positiveType = preferLeftRight
            ? FamilyInstanceReferenceType.Right
            : FamilyInstanceReferenceType.Front;

        negativeReference = GetFirstFamilyReference(column, negativeType);
        positiveReference = GetFirstFamilyReference(column, positiveType);
        if (negativeReference is not null && positiveReference is not null)
        {
            return true;
        }

        negativeReference = GetFirstFamilyReference(column, preferLeftRight ? FamilyInstanceReferenceType.Back : FamilyInstanceReferenceType.Left);
        positiveReference = GetFirstFamilyReference(column, preferLeftRight ? FamilyInstanceReferenceType.Front : FamilyInstanceReferenceType.Right);
        return negativeReference is not null && positiveReference is not null;
    }

    private static Reference? GetFirstFamilyReference(FamilyInstance instance, FamilyInstanceReferenceType referenceType)
    {
        try
        {
            return instance.GetReferences(referenceType).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryBuildColumnCenterGridDimension(
        Document doc,
        View view,
        FamilyInstance column,
        BoundingBoxXYZ bbox,
        DatumInfo grid,
        XYZ measureDirection,
        XYZ placeDirection,
        double offsetInternal,
        DimensionType? dimensionType,
        ISet<string> createdLineKeys)
    {
        Reference? centerReference = GetColumnCenterReferences(column, measureDirection)
            .FirstOrDefault();
        if (centerReference is null)
        {
            return false;
        }

        XYZ center = (bbox.Min + bbox.Max) * 0.5;
        double centerPosition = center.DotProduct(measureDirection);
        double gridPosition = grid.Midpoint.DotProduct(measureDirection);
        if (Math.Abs(centerPosition - gridPosition) < PositionTolerance)
        {
            return false;
        }

        double anchorAlongPlacement = ProjectBoundingBoxMax(bbox, placeDirection) + offsetInternal;
        double basePlacement = center.DotProduct(placeDirection);
        XYZ start = center + (placeDirection * (anchorAlongPlacement - basePlacement));
        XYZ end = start + (measureDirection * (gridPosition - centerPosition));
        if (start.DistanceTo(end) < VectorTolerance)
        {
            return false;
        }

        var refArray = new ReferenceArray();
        if (centerPosition <= gridPosition)
        {
            refArray.Append(centerReference);
            refArray.Append(new Reference(grid.Element));
        }
        else
        {
            refArray.Append(new Reference(grid.Element));
            refArray.Append(centerReference);
        }

        try
        {
            Line dimLine = Line.CreateBound(start, end);
            string lineKey = BuildDimensionLineKey(view, dimLine);
            if (createdLineKeys.Contains(lineKey) || HasSimilarDimension(doc, view, dimLine, refArray))
            {
                return false;
            }

            Dimension dimension = doc.Create.NewDimension(view, dimLine, refArray);
            if (dimensionType is not null && dimension.GetTypeId() != dimensionType.Id)
            {
                dimension.ChangeTypeId(dimensionType.Id);
            }

            createdLineKeys.Add(lineKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBuildElementReferenceGridDimension(
        Document doc,
        View view,
        Reference elementReference,
        BoundingBoxXYZ bbox,
        DatumInfo grid,
        XYZ measureDirection,
        XYZ placeDirection,
        double offsetInternal,
        DimensionType? dimensionType,
        ISet<string> createdLineKeys)
    {
        XYZ center = (bbox.Min + bbox.Max) * 0.5;
        double centerPosition = center.DotProduct(measureDirection);
        double gridPosition = grid.Midpoint.DotProduct(measureDirection);
        if (Math.Abs(centerPosition - gridPosition) < PositionTolerance)
        {
            return false;
        }

        double anchorAlongPlacement = ProjectBoundingBoxMax(bbox, placeDirection) + offsetInternal;
        double basePlacement = center.DotProduct(placeDirection);
        XYZ start = center + (placeDirection * (anchorAlongPlacement - basePlacement));
        XYZ end = start + (measureDirection * (gridPosition - centerPosition));
        if (start.DistanceTo(end) < VectorTolerance)
        {
            return false;
        }

        var refArray = new ReferenceArray();
        if (centerPosition <= gridPosition)
        {
            refArray.Append(elementReference);
            refArray.Append(new Reference(grid.Element));
        }
        else
        {
            refArray.Append(new Reference(grid.Element));
            refArray.Append(elementReference);
        }

        try
        {
            Line dimLine = Line.CreateBound(start, end);
            string lineKey = BuildDimensionLineKey(view, dimLine);
            if (createdLineKeys.Contains(lineKey) || HasSimilarDimension(doc, view, dimLine, refArray))
            {
                return false;
            }

            Dimension dimension = doc.Create.NewDimension(view, dimLine, refArray);
            if (dimensionType is not null && dimension.GetTypeId() != dimensionType.Id)
            {
                dimension.ChangeTypeId(dimensionType.Id);
            }

            createdLineKeys.Add(lineKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<Reference> GetColumnCenterReferences(FamilyInstance column, XYZ measureDirection)
    {
        foreach (FamilyInstanceReferenceType referenceType in new[]
        {
            Math.Abs(measureDirection.X) >= Math.Abs(measureDirection.Y)
                ? FamilyInstanceReferenceType.CenterLeftRight
                : FamilyInstanceReferenceType.CenterFrontBack,
            FamilyInstanceReferenceType.CenterLeftRight,
            FamilyInstanceReferenceType.CenterFrontBack
        })
        {
            IList<Reference>? references = null;
            try
            {
                references = column.GetReferences(referenceType);
            }
            catch
            {
                // Some families do not expose all reference types.
            }

            if (references is null)
            {
                continue;
            }

            foreach (Reference reference in references)
            {
                if (reference is not null)
                {
                    yield return reference;
                }
            }
        }
    }

    private static DatumInfo? FindNearestPassingGrid(
        IReadOnlyList<DatumInfo> visibleGrids,
        BoundingBoxXYZ bbox,
        XYZ measureDirection,
        XYZ placeDirection)
    {
        XYZ center = (bbox.Min + bbox.Max) * 0.5;
        double centerAlongMeasure = center.DotProduct(measureDirection);
        double minAlongMeasure = double.MaxValue;
        double maxAlongMeasure = double.MinValue;

        foreach (XYZ corner in EnumerateBoundingBoxCorners(bbox))
        {
            double projection = corner.DotProduct(measureDirection);
            minAlongMeasure = Math.Min(minAlongMeasure, projection);
            maxAlongMeasure = Math.Max(maxAlongMeasure, projection);
        }

        DatumInfo? passing = visibleGrids
            .Where(g => DirectionUtils.IsParallel(g.Direction, placeDirection))
            .Select(g => new { Grid = g, Position = g.Midpoint.DotProduct(measureDirection) })
            .Where(x => x.Position >= minAlongMeasure - PositionTolerance && x.Position <= maxAlongMeasure + PositionTolerance)
            .OrderBy(x => Math.Abs(x.Position - centerAlongMeasure))
            .Select(x => x.Grid)
            .FirstOrDefault();

        if (passing is not null)
        {
            return passing;
        }

        DatumInfo? nearby = visibleGrids
            .Where(g => DirectionUtils.IsParallel(g.Direction, placeDirection))
            .Select(g => new { Grid = g, Distance = Math.Abs(g.Midpoint.DotProduct(measureDirection) - centerAlongMeasure) })
            .Where(x => x.Distance <= NearestGridSearchTolerance)
            .OrderBy(x => x.Distance)
            .Select(x => x.Grid)
            .FirstOrDefault();
        if (nearby is not null)
        {
            return nearby;
        }

        return visibleGrids
            .Where(g => DirectionUtils.IsParallel(g.Direction, placeDirection))
            .Select(g => new { Grid = g, Distance = Math.Abs(g.Midpoint.DotProduct(measureDirection) - centerAlongMeasure) })
            .OrderBy(x => x.Distance)
            .Select(x => x.Grid)
            .FirstOrDefault();
    }

    private static IReadOnlyList<DatumInfo> CollectVisibleGridDatums(Document doc, View view)
    {
        var result = new List<DatumInfo>();
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
            result.Add(new DatumInfo(grid, midpoint, direction, new Reference(grid)));
        }

        return result;
    }

    private static IEnumerable<XYZ> EnumerateBoundingBoxCorners(BoundingBoxXYZ bbox)
    {
        XYZ min = bbox.Min;
        XYZ max = bbox.Max;

        yield return new XYZ(min.X, min.Y, min.Z);
        yield return new XYZ(min.X, min.Y, max.Z);
        yield return new XYZ(min.X, max.Y, min.Z);
        yield return new XYZ(min.X, max.Y, max.Z);
        yield return new XYZ(max.X, min.Y, min.Z);
        yield return new XYZ(max.X, min.Y, max.Z);
        yield return new XYZ(max.X, max.Y, min.Z);
        yield return new XYZ(max.X, max.Y, max.Z);
    }

    private static double ProjectBoundingBoxMax(BoundingBoxXYZ bbox, XYZ direction)
    {
        double best = double.MinValue;
        foreach (XYZ corner in EnumerateBoundingBoxCorners(bbox))
        {
            best = Math.Max(best, corner.DotProduct(direction));
        }

        return best;
    }

    private static double ProjectBoundingBoxRange(BoundingBoxXYZ bbox, XYZ direction)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (XYZ corner in EnumerateBoundingBoxCorners(bbox))
        {
            double projection = corner.DotProduct(direction);
            min = Math.Min(min, projection);
            max = Math.Max(max, projection);
        }

        return Math.Abs(max - min);
    }

    private static IList<(PlanarFace Face, XYZ Normal, XYZ Point)> GetPlanarFaces(FamilyInstance column, XYZ viewNormal)
    {
        var result = new List<(PlanarFace Face, XYZ Normal, XYZ Point)>();
        var options = new Options
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = true,
            DetailLevel = ViewDetailLevel.Fine
        };

        GeometryElement geometry = column.get_Geometry(options);
        if (geometry is null)
        {
            return result;
        }

        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid solid && solid.Faces.Size > 0)
            {
                ExtractPlanarFaces(solid, viewNormal, result);
            }
            else if (obj is GeometryInstance instance)
            {
                int beforeSymbol = result.Count;
                GeometryElement symbolGeometry = instance.GetSymbolGeometry();
                foreach (GeometryObject nested in symbolGeometry)
                {
                    if (nested is Solid nestedSolid && nestedSolid.Faces.Size > 0)
                    {
                        ExtractPlanarFaces(nestedSolid, viewNormal, result, instance.Transform);
                    }
                }

                if (result.Count == beforeSymbol)
                {
                    GeometryElement instanceGeometry = instance.GetInstanceGeometry();
                    foreach (GeometryObject nested in instanceGeometry)
                    {
                        if (nested is Solid nestedSolid && nestedSolid.Faces.Size > 0)
                        {
                            ExtractPlanarFaces(nestedSolid, viewNormal, result);
                        }
                    }
                }
            }
        }

        return result;
    }

    private static void ExtractPlanarFaces(
        Solid solid,
        XYZ viewNormal,
        ICollection<(PlanarFace Face, XYZ Normal, XYZ Point)> bucket,
        Transform transform = null)
    {
        foreach (Face face in solid.Faces)
        {
            if (face is not PlanarFace planar || planar.Reference is null)
            {
                continue;
            }

            XYZ normal = planar.FaceNormal;
            XYZ point = planar.Origin;
            if (transform is not null)
            {
                normal = transform.OfVector(normal);
                point = transform.OfPoint(point);
            }

            normal = normal.Normalize();
            XYZ inPlane = normal - (viewNormal * normal.DotProduct(viewNormal));
            if (inPlane.GetLength() < VectorTolerance)
            {
                continue;
            }

            bucket.Add((planar, inPlane.Normalize(), point));
        }
    }

    private static (PlanarFace Face, XYZ Normal, XYZ Point)? SelectBestFace(
        IList<(PlanarFace Face, XYZ Normal, XYZ Point)> faces,
        XYZ wantedDirection)
    {
        double best = -1.0;
        (PlanarFace Face, XYZ Normal, XYZ Point)? chosen = null;

        foreach ((PlanarFace Face, XYZ Normal, XYZ Point) candidate in faces)
        {
            double dot = candidate.Normal.DotProduct(wantedDirection);
            if (dot > best)
            {
                best = dot;
                chosen = candidate;
            }
        }

        if (best < 0.65)
        {
            return null;
        }

        return chosen;
    }

    private static XYZ SnapToCardinal(XYZ preferred, View view)
    {
        XYZ right = view.RightDirection.Normalize();
        XYZ up = view.UpDirection.Normalize();

        double rightDot = preferred.DotProduct(right);
        double upDot = preferred.DotProduct(up);

        if (Math.Abs(rightDot) >= Math.Abs(upDot))
        {
            return rightDot >= 0.0 ? right : right.Negate();
        }

        return upDot >= 0.0 ? up : up.Negate();
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

    private static int ResolveSideSign(XYZ preferredVector, XYZ datumDirection)
    {
        double dot = preferredVector.Normalize().DotProduct(datumDirection.Normalize());
        return dot >= 0.0 ? 1 : -1;
    }

    private static Line? BuildDimensionLine(IReadOnlyList<DatumInfo> group, XYZ datumDir, XYZ dimLineDir, double offsetInternal, int sideSign)
    {
        XYZ origin = group[0].Midpoint;
        double minAlongDim = double.MaxValue;
        double maxAlongDim = double.MinValue;
        double minAlongDatum = double.MaxValue;
        double maxAlongDatum = double.MinValue;

        foreach (DatumInfo datum in group)
        {
            XYZ delta = datum.Midpoint - origin;
            double alongDim = delta.DotProduct(dimLineDir);
            double alongDatum = delta.DotProduct(datumDir);

            minAlongDim = Math.Min(minAlongDim, alongDim);
            maxAlongDim = Math.Max(maxAlongDim, alongDim);
            minAlongDatum = Math.Min(minAlongDatum, alongDatum);
            maxAlongDatum = Math.Max(maxAlongDatum, alongDatum);
        }

        if (Math.Abs(maxAlongDim - minAlongDim) < 1e-6)
        {
            return null;
        }

        double anchorAlongDatum = sideSign > 0 ? maxAlongDatum + offsetInternal : minAlongDatum - offsetInternal;
        XYZ start = origin + (dimLineDir * minAlongDim) + (datumDir * anchorAlongDatum);
        XYZ end = origin + (dimLineDir * maxAlongDim) + (datumDir * anchorAlongDatum);

        if (start.DistanceTo(end) < 1e-6)
        {
            return null;
        }

        return Line.CreateBound(start, end);
    }

    private static ReferenceArray BuildSortedReferences(IReadOnlyList<DatumInfo> group, XYZ dimLineDir)
    {
        var refs = new ReferenceArray();

        foreach (DatumInfo datum in group.OrderBy(d => d.Midpoint.DotProduct(dimLineDir)))
        {
            refs.Append(datum.Reference);
        }

        return refs;
    }

    private static ReferenceArray BuildOverallReferences(IReadOnlyList<DatumInfo> sortedGroup)
    {
        var refs = new ReferenceArray();
        if (sortedGroup.Count == 0)
        {
            return refs;
        }

        refs.Append(sortedGroup[0].Reference);
        refs.Append(sortedGroup[sortedGroup.Count - 1].Reference);
        return refs;
    }

    private static bool HasSimilarDimension(Document doc, View view, Line candidate, ReferenceArray candidateReferences)
    {
        string candidateReferenceKey = BuildReferenceSetKey(doc, candidateReferences);
        if (string.IsNullOrWhiteSpace(candidateReferenceKey))
        {
            return HasSimilarDimension(doc, view, candidate);
        }

        var collector = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Dimension))
            .Cast<Dimension>();

        foreach (Dimension existing in collector)
        {
            if (existing.Curve is not Line existingLine || !existingLine.IsBound)
            {
                continue;
            }

            string existingReferenceKey = BuildReferenceSetKey(doc, existing.References);
            if (!string.Equals(candidateReferenceKey, existingReferenceKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (AreOverlappingDimensionLines(view, candidate, existingLine, 0.5))
            {
                return true;
            }
        }

        return HasSimilarDimension(doc, view, candidate);
    }

    private static bool HasSimilarDimension(Document doc, View view, Line candidate)
    {
        if (!candidate.IsBound)
        {
            return false;
        }

        XYZ candidateDirection = (candidate.GetEndPoint(1) - candidate.GetEndPoint(0)).Normalize();
        XYZ candidateMidpoint = (candidate.GetEndPoint(0) + candidate.GetEndPoint(1)) * 0.5;
        XYZ viewNormal = view.ViewDirection.Normalize();
        XYZ measureDirection = viewNormal.CrossProduct(candidateDirection);
        if (measureDirection.GetLength() < VectorTolerance)
        {
            return false;
        }

        measureDirection = measureDirection.Normalize();
        double candidateMeasure = candidateMidpoint.DotProduct(measureDirection);
        double candidateMin = Math.Min(candidate.GetEndPoint(0).DotProduct(candidateDirection), candidate.GetEndPoint(1).DotProduct(candidateDirection));
        double candidateMax = Math.Max(candidate.GetEndPoint(0).DotProduct(candidateDirection), candidate.GetEndPoint(1).DotProduct(candidateDirection));

        var collector = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Dimension))
            .Cast<Dimension>();

        foreach (Dimension existing in collector)
        {
            if (existing.Curve is not Line existingLine || !existingLine.IsBound)
            {
                continue;
            }

            XYZ existingDirection = (existingLine.GetEndPoint(1) - existingLine.GetEndPoint(0)).Normalize();
            if (!DirectionUtils.IsParallel(existingDirection, candidateDirection))
            {
                continue;
            }

            double existingMeasure = ((existingLine.GetEndPoint(0) + existingLine.GetEndPoint(1)) * 0.5).DotProduct(measureDirection);
            if (Math.Abs(existingMeasure - candidateMeasure) > DuplicateLineTolerance)
            {
                continue;
            }

            double existingMin = Math.Min(existingLine.GetEndPoint(0).DotProduct(candidateDirection), existingLine.GetEndPoint(1).DotProduct(candidateDirection));
            double existingMax = Math.Max(existingLine.GetEndPoint(0).DotProduct(candidateDirection), existingLine.GetEndPoint(1).DotProduct(candidateDirection));
            if (IntervalsMostlyOverlap(candidateMin, candidateMax, existingMin, existingMax, 0.8))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildReferenceSetKey(Document doc, ReferenceArray references)
    {
        var keys = new List<string>();
        foreach (Reference reference in references)
        {
            string key = BuildReferenceKey(doc, reference);
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys.Add(key);
            }
        }

        return string.Join("|", keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    private static string BuildReferenceKey(Document doc, Reference reference)
    {
        try
        {
            string stable = reference.ConvertToStableRepresentation(doc);
            if (!string.IsNullOrWhiteSpace(stable))
            {
                return stable;
            }
        }
        catch
        {
            // Some generated references cannot be converted to stable strings.
        }

        ElementId? elementId = reference.ElementId;
        if (elementId is not null && elementId != ElementId.InvalidElementId)
        {
            return ElementIdCompat.ToInt32(elementId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static int RemoveOverlappingDimensions(Document doc, View view)
    {
        var dimensions = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Dimension))
            .Cast<Dimension>()
            .Where(d => d.Curve is Line line && line.IsBound)
            .ToList();

        var deleteIds = new HashSet<int>();
        for (int i = 0; i < dimensions.Count; i++)
        {
            if (deleteIds.Contains(ElementIdCompat.ToInt32(dimensions[i].Id)) ||
                dimensions[i].Curve is not Line firstLine ||
                !firstLine.IsBound)
            {
                continue;
            }

            for (int j = i + 1; j < dimensions.Count; j++)
            {
                int secondId = ElementIdCompat.ToInt32(dimensions[j].Id);
                if (deleteIds.Contains(secondId) ||
                    dimensions[j].Curve is not Line secondLine ||
                    !secondLine.IsBound)
                {
                    continue;
                }

                if (AreOverlappingDimensionLines(view, firstLine, secondLine, 0.9))
                {
                    deleteIds.Add(secondId);
                }
            }
        }

        int deleted = 0;
        foreach (int id in deleteIds)
        {
            try
            {
                doc.Delete(ElementIdCompat.FromInt32(id));
                deleted++;
            }
            catch
            {
                // Ignore dimensions that cannot be deleted.
            }
        }

        return deleted;
    }

    private static bool AreOverlappingDimensionLines(View view, Line firstLine, Line secondLine, double overlapRatio)
    {
        XYZ firstDirection = (firstLine.GetEndPoint(1) - firstLine.GetEndPoint(0)).Normalize();
        XYZ secondDirection = (secondLine.GetEndPoint(1) - secondLine.GetEndPoint(0)).Normalize();
        if (!DirectionUtils.IsParallel(firstDirection, secondDirection))
        {
            return false;
        }

        XYZ direction = DirectionUtils.Canonicalize(firstDirection);
        XYZ measureDirection = view.ViewDirection.Normalize().CrossProduct(direction);
        if (measureDirection.GetLength() < VectorTolerance)
        {
            measureDirection = view.RightDirection.Normalize();
        }
        else
        {
            measureDirection = measureDirection.Normalize();
        }

        double firstMeasure = ((firstLine.GetEndPoint(0) + firstLine.GetEndPoint(1)) * 0.5).DotProduct(measureDirection);
        double secondMeasure = ((secondLine.GetEndPoint(0) + secondLine.GetEndPoint(1)) * 0.5).DotProduct(measureDirection);
        if (Math.Abs(firstMeasure - secondMeasure) > DuplicateLineTolerance)
        {
            return false;
        }

        double firstMin = Math.Min(firstLine.GetEndPoint(0).DotProduct(direction), firstLine.GetEndPoint(1).DotProduct(direction));
        double firstMax = Math.Max(firstLine.GetEndPoint(0).DotProduct(direction), firstLine.GetEndPoint(1).DotProduct(direction));
        double secondMin = Math.Min(secondLine.GetEndPoint(0).DotProduct(direction), secondLine.GetEndPoint(1).DotProduct(direction));
        double secondMax = Math.Max(secondLine.GetEndPoint(0).DotProduct(direction), secondLine.GetEndPoint(1).DotProduct(direction));
        return IntervalsMostlyOverlap(firstMin, firstMax, secondMin, secondMax, overlapRatio);
    }

    private static bool IntervalsMostlyOverlap(double firstMin, double firstMax, double secondMin, double secondMax, double overlapRatio)
    {
        double overlap = Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin);
        if (overlap <= DuplicateLineTolerance)
        {
            return false;
        }

        double shortest = Math.Min(Math.Abs(firstMax - firstMin), Math.Abs(secondMax - secondMin));
        double longest = Math.Max(Math.Abs(firstMax - firstMin), Math.Abs(secondMax - secondMin));
        return shortest > DuplicateLineTolerance &&
            longest > DuplicateLineTolerance &&
            overlap / shortest >= overlapRatio &&
            overlap / longest >= overlapRatio;
    }

    private static string BuildDimensionLineKey(View view, Line line)
    {
        XYZ direction = DirectionUtils.Canonicalize((line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize());
        XYZ viewNormal = view.ViewDirection.Normalize();
        XYZ measureDirection = viewNormal.CrossProduct(direction);
        if (measureDirection.GetLength() < VectorTolerance)
        {
            measureDirection = view.RightDirection.Normalize();
        }
        else
        {
            measureDirection = measureDirection.Normalize();
        }

        double measure = ((line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5).DotProduct(measureDirection);
        double min = Math.Min(line.GetEndPoint(0).DotProduct(direction), line.GetEndPoint(1).DotProduct(direction));
        double max = Math.Max(line.GetEndPoint(0).DotProduct(direction), line.GetEndPoint(1).DotProduct(direction));

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:F5}|{1:F5}|{2:F5}|{3:F4}|{4:F4}|{5:F4}",
            direction.X,
            direction.Y,
            direction.Z,
            measure,
            min,
            max);
    }

    private static DimensionType? ResolveDimensionType(Document doc, string? typeName)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType));

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            foreach (Element element in collector)
            {
                if (element is DimensionType dimType && string.Equals(dimType.Name, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return dimType;
                }
            }
        }

        return collector
            .Cast<Element>()
            .OfType<DimensionType>()
            .FirstOrDefault();
    }
}
}

