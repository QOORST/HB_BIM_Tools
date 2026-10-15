using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

#nullable enable

namespace YDBIM.AutoDimension.Core
{
internal sealed partial class AutoDimensionService
{
    private static Reference ToLinkedReference(Reference reference, RevitLinkInstance? link)
        => link == null ? reference : reference.CreateLinkReference(link);

    private static void ValidateLinkedReferences(Dimension dimension, RevitLinkInstance link)
    {
        if (!dimension.AreReferencesAvailable || dimension.References.Size < 2 ||
            dimension.References.Cast<Reference>().Any(r => r.ElementId != link.Id ||
                r.LinkedElementId == ElementId.InvalidElementId || link.GetLinkDocument()?.GetElement(r.LinkedElementId) == null))
            throw new InvalidOperationException("尺寸未保留指定連結的有效參照，已回復此筆。");
    }

    private static int CreateLinkedDimensions(Document host, View view, DimensionOptions options, DimensionType? type)
    {
#if REVIT2024 || REVIT2025 || REVIT2026
        if (view is not ViewPlan || view.IsTemplate)
            throw new InvalidOperationException("連結模型標註限目前平面視圖。");
        var link = host.GetElement(options.SourceLinkInstanceId) as RevitLinkInstance;
        if (link == null || link.GetLinkDocument() == null)
            throw new InvalidOperationException("指定連結已卸載或不存在，請重新選擇；不會改用本機構件。");
        if (link.IsHidden(view) || view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_RvtLinks)))
            throw new InvalidOperationException("指定連結在目前視圖不可見。");
        if (options.GridBubblesOnly)
            throw new InvalidOperationException("連結模型標註不修改來源軸線泡泡。");
        Transform transform = link.GetTotalTransform();
        // A tilted link has non-planar datum geometry; do not substitute projected references.
        if (Math.Abs(transform.BasisZ.Normalize().DotProduct(view.ViewDirection.Normalize())) < .999999)
            throw new InvalidOperationException("目前不支援傾斜連結的平面尺寸；未建立標註。");
        var categories = new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns, BuiltInCategory.OST_Grids });
        var elements = new FilteredElementCollector(host, view.Id, link.Id).WhereElementIsNotElementType()
            .WherePasses(categories).ToElements();
        var grids = new List<DatumInfo>();
        foreach (Grid grid in elements.OfType<Grid>())
        {
            if (grid.Curve is not Line line)
            {
                if (options.ModeType == DimensionMode.BeamGrid) options.Warnings.Add($"連結軸線 {grid.Id}：非直線，已略過。");
                continue;
            }
            XYZ a = transform.OfPoint(line.GetEndPoint(0)), b = transform.OfPoint(line.GetEndPoint(1));
            if (Math.Abs((b - a).Normalize().DotProduct(view.ViewDirection)) > VectorTolerance) continue;
            grids.Add(new DatumInfo(grid, (a + b) * .5, (b - a).Normalize(), ToLinkedReference(new Reference(grid), link), a, b));
        }
        if (options.ModeType == DimensionMode.BeamGrid)
            return CreateLinkedGridDimensions(host, view, link, grids, options, type);

        if (options.ModeType == DimensionMode.ColumnSetout)
        {
            var columns = elements.OfType<FamilyInstance>().Where(e => e.Category.Id == new ElementId(BuiltInCategory.OST_StructuralColumns)
                || e.Category.Id == new ElementId(BuiltInCategory.OST_Columns))
                .Where(e => options.SelectedColumnIds == null || options.SelectedColumnIds.Contains(e.Id)).ToList();
            int count = 0;
            var sides = new List<XYZ>();
            XYZ x = transform.OfVector(XYZ.BasisX).Normalize(), y = transform.OfVector(XYZ.BasisY).Normalize();
            XYZ right = Math.Abs(x.DotProduct(view.RightDirection)) >= Math.Abs(y.DotProduct(view.RightDirection)) ? x : y;
            if (right.DotProduct(view.RightDirection) < 0) right = right.Negate();
            XYZ up = view.ViewDirection.Normalize().CrossProduct(right).Normalize();
            if (up.DotProduct(view.UpDirection) < 0) up = up.Negate();
            if (options.ColumnLeftRightSide != LeftRightSide.None)
                sides.Add(right * (options.ColumnLeftRightSide == LeftRightSide.Left ? -1 : 1));
            if (options.ColumnFrontBackSide != FrontBackSide.None)
                sides.Add(up * (options.ColumnFrontBackSide == FrontBackSide.Back ? -1 : 1));
            foreach (var column in columns)
                foreach (XYZ side in sides)
                    count += CreateLinkedColumnDimension(host, view, link, column, grids, side, options, type);
            return count;
        }

        var beams = elements.OfType<FamilyInstance>().Where(e => e.Category.Id == new ElementId(BuiltInCategory.OST_StructuralFraming))
            .Where(e => !options.SelectedBeamsOnly || options.SelectedBeamIds.Contains(e.Id)).ToList();
        if (beams.Count < 2 && options.IncludeBeamSpacing)
            options.Warnings.Add("連結梁間距至少需要兩支可見直線梁；不足時不建立尺寸鏈。");
        int created = 0;
        if (options.IncludeBeamWidth)
            foreach (var beam in beams) created += CreateLinkedBeamWidth(host, view, link, beam, options, type);
        if (options.IncludeBeamSpacing)
            created += CreateBeamToBeamChainDimensions(host, view, beams, view.ViewDirection.Normalize(),
                options.BeamSpacingOffsetInternal ?? options.OffsetInternal, type, options, link);
        return created;
#else
        throw new InvalidOperationException("連結模型可見範圍標註需要 Revit 2024 或更新版本；2022 僅支援本機標註。");
#endif
    }

    private static IList<(PlanarFace Face, XYZ Normal, XYZ Point)> LinkedFaces(FamilyInstance element, RevitLinkInstance link, View view)
    {
        Transform transform = link.GetTotalTransform();
        return GetBeamChainFaces(element, transform.Inverse.OfVector(view.ViewDirection).Normalize())
            .Select(f => (f.Face, Normal: transform.OfVector(f.Normal).Normalize(), Point: transform.OfPoint(f.Point))).ToList();
    }

    private static List<XYZ> LinkedCorners(Element element, RevitLinkInstance link)
    {
        var box = element.get_BoundingBox(null);
        if (box == null) throw new InvalidOperationException("無可用的構件外框。");
        Transform transform = link.GetTotalTransform();
        return EnumerateBoundingBoxCorners(box).Select(p => transform.OfPoint(box.Transform.OfPoint(p))).ToList();
    }

    private static List<ReferencePoint> LinkedEdges(FamilyInstance element, RevitLinkInstance link, View view, XYZ measure)
    {
        var faces = LinkedFaces(element, link, view);
        var negative = SelectBeamChainFace(faces, measure.Negate());
        var positive = SelectBeamChainFace(faces, measure);
        if (negative == null || positive == null)
            throw new InvalidOperationException("缺少平行且可標註的兩側實體面；不使用外框或插入點替代參照。");
        return new[] { negative.Value, positive.Value }.Select(f =>
            new ReferencePoint(ToLinkedReference(f.Face.Reference, link), f.Point, f.Point.DotProduct(measure), 0)).ToList();
    }

    private static int CreateLinkedBeamWidth(Document host, View view, RevitLinkInstance link, FamilyInstance beam, DimensionOptions options, DimensionType? type)
    {
        options.RequestedDimensionCount++;
        try
        {
            Transform transform = link.GetTotalTransform();
            if (!TryGetBeamAxisPointAndDirection(beam, transform.Inverse.OfVector(view.ViewDirection), out XYZ point, out XYZ axis))
                throw new InvalidOperationException("僅支援可投影的直線梁。");
            axis = DirectionUtils.Canonicalize(transform.OfVector(axis).Normalize());
            XYZ measure = view.ViewDirection.CrossProduct(axis).Normalize();
            var refs = LinkedEdges(beam, link, view, measure);
            double placement = LinkedCorners(beam, link).Max(p => p.DotProduct(axis)) + options.OffsetInternal;
            return CreateValidatedLinkedDimension(host, view, link, refs, measure, axis, placement, type, options);
        }
        catch (Exception ex) { options.Warnings.Add($"連結梁 {beam.Id}：{ex.Message}"); return 0; }
    }

    private static int CreateLinkedColumnDimension(Document host, View view, RevitLinkInstance link, FamilyInstance column,
        IReadOnlyList<DatumInfo> grids, XYZ side, DimensionOptions options, DimensionType? type)
    {
        options.RequestedDimensionCount++;
        try
        {
            side = side.Normalize();
            XYZ measure = view.ViewDirection.CrossProduct(side).Normalize();
            var corners = LinkedCorners(column, link);
            double min = corners.Min(p => p.DotProduct(measure)), max = corners.Max(p => p.DotProduct(measure));
            var choices = grids.Where(g => DirectionUtils.IsParallel(g.Direction, side))
                .Where(g => g.Midpoint.DotProduct(measure) >= min - NearestGridSearchTolerance &&
                    g.Midpoint.DotProduct(measure) <= max + NearestGridSearchTolerance)
                .OrderBy(g => Math.Abs(g.Midpoint.DotProduct(measure) - (min + max) * .5)).ToList();
            if (choices.Count == 0) throw new InvalidOperationException("同連結內無鄰近且平行的可見軸線；未改用本機軸線。");
            if (choices.Count > 1 && Math.Abs(Math.Abs(choices[0].Midpoint.DotProduct(measure) - (min + max) * .5) -
                Math.Abs(choices[1].Midpoint.DotProduct(measure) - (min + max) * .5)) <= DimensionReferenceTolerance)
                throw new InvalidOperationException("有兩條距離接近的軸線，請確認定位基準。");
            var refs = LinkedEdges(column, link, view, measure);
            var grid = choices[0];
            refs.Add(new ReferencePoint(grid.Reference, grid.Midpoint, grid.Midpoint.DotProduct(measure), 0));
            return CreateValidatedLinkedDimension(host, view, link, refs, measure, side,
                corners.Max(p => p.DotProduct(side)) + options.OffsetInternal, type, options);
        }
        catch (Exception ex) { options.Warnings.Add($"連結柱 {column.Id}：{ex.Message}"); return 0; }
    }

    private static int CreateLinkedGridDimensions(Document host, View view, RevitLinkInstance link, IReadOnlyList<DatumInfo> grids,
        DimensionOptions options, DimensionType? type)
    {
        int count = 0;
        double scale = options.GridOffsetsInPaperSpace ? Math.Max(1, view.Scale) : 1;
        foreach (var selection in new[] { options.SelectedHorizontalGridIds, options.SelectedVerticalGridIds })
        {
            var selected = grids.Where(g => selection.Contains(g.Element.Id)).ToList();
            foreach (var group in GroupParallelGrids(selected, view))
            {
                XYZ axis = DirectionUtils.Canonicalize(group[0].Direction);
                XYZ measure = view.ViewDirection.CrossProduct(axis).Normalize();
                bool horizontal = Math.Abs(axis.DotProduct(view.RightDirection)) >= Math.Abs(axis.DotProduct(view.UpDirection));
                XYZ preferred = horizontal ? ResolveHorizontalGridPreferredVector(view, options.Direction) : ResolveVerticalGridPreferredVector(view, options.Direction);
                int sign = ResolveSideSign(preferred, axis);
                var positions = group.SelectMany(g => new[] { g.StartPoint.DotProduct(axis), g.EndPoint.DotProduct(axis) }).ToList();
                var refs = UniqueDimensionReferencesByPosition(group.Select(g => new ReferencePoint(g.Reference, g.Midpoint, g.Midpoint.DotProduct(measure), 0))).ToList();
                if (refs.Count < 2) continue;
                for (int pass = 0; pass < (refs.Count > 2 ? 2 : 1); pass++)
                {
                    options.RequestedDimensionCount++;
                    double offset = (pass == 0 ? options.GridPrimaryOffsetInternal : options.GridOverallOffsetInternal) * scale;
                    double placement = sign > 0 ? positions.Max() - offset : positions.Min() + offset;
                    var chosen = pass == 0 ? refs : new List<ReferencePoint> { refs[0], refs[refs.Count - 1] };
                    try { count += CreateValidatedLinkedDimension(host, view, link, chosen, measure, axis, placement, type, options); }
                    catch (Exception ex) { options.Warnings.Add("連結軸線 " + string.Join(",", group.Select(g => g.Element.Id)) + "：" + ex.Message); }
                }
            }
        }
        if (options.RequestedDimensionCount == 0)
            options.Warnings.Add("連結軸線：沒有同方向且位置不同的兩條有效直線軸線。");
        return count;
    }

    private static int CreateValidatedLinkedDimension(Document host, View view, RevitLinkInstance link,
        IEnumerable<ReferencePoint> candidates, XYZ measure, XYZ placementAxis, double placement,
        DimensionType? type, DimensionOptions options)
    {
        var refs = UniqueDimensionReferencesByPosition(candidates).ToList();
        if (refs.Count < 2) throw new InvalidOperationException("可用的不同位置參照不足。");
        var array = new ReferenceArray();
        foreach (var rp in refs) array.Append(rp.Reference);
        string key = BuildReferenceSetKey(host, array);
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("無法確認連結尺寸參照識別。");
        // Do not use the local geometry-only fallback: another link can occupy the same coordinates.
        foreach (Dimension existing in new FilteredElementCollector(host, view.Id).OfClass(typeof(Dimension)))
        {
            try
            {
                if (existing.AreReferencesAvailable && existing.Curve is Line line && DirectionUtils.IsParallel(line.Direction, measure)
                    && BuildReferenceSetKey(host, existing.References) == key)
                { options.ExistingDimensionCount++; return 0; }
            }
            catch { /* Unreadable native references do not prove coverage. */ }
        }
        XYZ PointAt(double distance) => view.Origin + measure * (distance - view.Origin.DotProduct(measure))
            + placementAxis * (placement - view.Origin.DotProduct(placementAxis));
        using var tx = new SubTransaction(host);
        try
        {
            tx.Start();
            var dimension = host.Create.NewDimension(view, Line.CreateBound(PointAt(refs[0].Position), PointAt(refs[refs.Count - 1].Position)), array);
            if (type != null && dimension.GetTypeId() != type.Id) dimension.ChangeTypeId(type.Id);
            host.Regenerate();
            ValidateLinkedReferences(dimension, link);
            var expected = refs.Zip(refs.Skip(1), (a, b) => b.Position - a.Position).ToList();
            var actual = dimension.Segments.Size > 0 ? dimension.Segments.Cast<DimensionSegment>().Select(s => s.Value).ToList() : new List<double?> { dimension.Value };
            if (!LinkedDimensionPolicy.SegmentsMatch(expected, actual, DimensionReferenceTolerance))
                throw new InvalidOperationException("原生尺寸值與連結投影不符，已回復此筆。");
            AdjustOverlappingDimensionText(host, dimension, view);
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("尺寸未成功提交。");
            return 1;
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            throw;
        }
    }
}
}
