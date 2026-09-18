using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class CmdMepPositionDimension
    {
        private sealed class BeamSide
        {
            internal Reference Reference;
            internal XYZ Origin, Normal;
            internal List<XYZ> Vertices;
            internal string Id;
        }
        private sealed class AutoPlan
        {
            internal List<Item> Items;
            internal Line Axis;
            internal XYZ Direction;
            internal string Beam, Pipes;
        }
        private sealed class BeamMatch
        {
            internal BeamSide Side;
            internal double Station, Distance, Section, Overlap;
        }
        private sealed class MatchDiagnostics
        {
            internal int Direction, Side, Overlap, Height, Distance, Placement;
            internal string Describe(int count) => count == 0 ? "搜尋範圍內沒有可讀取的直梁垂直側面。" :
                $"未配對；逐階段排除數：方向 {Direction}、側別 {Side}、無重疊 {Overlap}、高程差 {Height}、超出擴大上限 {Distance}、無放置空間 {Placement}。";
        }

        private static List<BeamMatch> RankMatches(List<BeamMatch> matches, XYZ viewNormal, double preferred)
        {
            // Expansion is used only when no geometrically eligible candidate is in the primary range.
            var primary = matches.Where(m => m.Distance <= (double)searchDistance/304.8).ToList();
            if (primary.Count > 0) matches = primary;
            var representatives = new List<BeamMatch>();
            foreach (var match in matches.OrderByDescending(m=>m.Overlap).ThenBy(m=>Math.Abs(m.Section-preferred))
                .ThenBy(m=>m.Side.Id,StringComparer.Ordinal))
            {
                bool samePlane = representatives.Any(r => r.Side.Normal.DotProduct(match.Side.Normal) > .999999 &&
                    Math.Abs((r.Side.Origin-match.Side.Origin).DotProduct(match.Side.Normal)) <= .5/304.8 &&
                    Math.Min(r.Side.Vertices.Max(p=>p.DotProduct(viewNormal)),match.Side.Vertices.Max(p=>p.DotProduct(viewNormal))) -
                    Math.Max(r.Side.Vertices.Min(p=>p.DotProduct(viewNormal)),match.Side.Vertices.Min(p=>p.DotProduct(viewNormal))) > 1/304.8);
                if (!samePlane) representatives.Add(match);
            }
            return representatives.OrderBy(m=>m.Distance).ToList();
        }

        private static void ReadSides(GeometryElement geometry, Transform transform, XYZ beamDirection,
            XYZ viewNormal, RevitLinkInstance link, string id, List<BeamSide> result)
        {
            if (geometry == null) return;
            foreach (GeometryObject obj in geometry)
            {
                if (obj is GeometryInstance instance)
                    ReadSides(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), beamDirection, viewNormal, link, id, result);
                if (!(obj is Solid solid)) continue;
                foreach (Face raw in solid.Faces)
                {
                    if (!(raw is PlanarFace face) || face.Reference == null) continue;
                    XYZ normal = transform.OfVector(face.FaceNormal).Normalize();
                    if (Math.Abs(normal.DotProduct(viewNormal)) > 1e-6 || Math.Abs(normal.DotProduct(beamDirection)) > 1e-6) continue;
                    var points = face.Triangulate().Vertices.Select(transform.OfPoint).ToList();
                    if (points.Count < 3) continue;
                    result.Add(new BeamSide { Reference = link == null ? face.Reference : face.Reference.CreateLinkReference(link),
                        Origin = transform.OfPoint(face.Origin), Normal = normal, Vertices = points, Id = id });
                }
            }
        }

        private static double IntervalDistance(double a, double b, double c, double d)
            => Math.Max(0, Math.Max(c - b, a - d));

        private static Result ExecuteAutomatic(Document doc, View view, List<Element> curves, DimensionType type)
        {
            var link = sourceLink == null ? null : doc.GetElement(sourceLink) as RevitLinkInstance;
            var source = sourceLink == null ? doc : link?.GetLinkDocument();
            if (source == null) throw new InvalidOperationException("指定連結不存在或尚未載入，請重新設定參考模型。");
            var transform = link?.GetTotalTransform() ?? Transform.Identity;
            XYZ normal = view.ViewDirection.Normalize();
            Func<XYZ, XYZ> project = v => v - normal * v.DotProduct(normal);
            var notes = new List<string>();
            var buckets = new List<List<Element>>();
            var directions = new List<XYZ>();
            foreach (var e in curves.OrderBy(e => e.UniqueId, StringComparer.Ordinal))
            {
                var line = (Line)((LocationCurve)e.Location).Curve;
                XYZ d = project(line.Direction);
                if (d.GetLength() < 1e-6) { notes.Add($"管段 {e.Id}：立管不支援。"); continue; }
                d = d.Normalize();
                if (d.DotProduct(view.RightDirection) < -1e-6 ||
                    (Math.Abs(d.DotProduct(view.RightDirection)) <= 1e-6 && d.DotProduct(view.UpDirection) < 0)) d = -d;
                int bucket = directions.FindIndex(x => Math.Abs(x.DotProduct(d)) >= .999999);
                if (bucket < 0) { directions.Add(d); buckets.Add(new List<Element>()); bucket = buckets.Count - 1; }
                buckets[bucket].Add(e);
            }
            // Broad phase uses transformed bounding boxes; only actual planar faces become references.
            var endpoints = curves.SelectMany(e => {
                var l = (Line)((LocationCurve)e.Location).Curve;
                return new[] { l.GetEndPoint(0), l.GetEndPoint(1) };
            }).Select(transform.Inverse.OfPoint).ToList();
            double expand = ((double)expandedDistance + (double)heightDistance) / 304.8;
            var min = new XYZ(endpoints.Min(p => p.X) - expand, endpoints.Min(p => p.Y) - expand, endpoints.Min(p => p.Z) - expand);
            var max = new XYZ(endpoints.Max(p => p.X) + expand, endpoints.Max(p => p.Y) + expand, endpoints.Max(p => p.Z) + expand);
            var sides = new List<BeamSide>();
            using (var outline = new Outline(min, max))
            using (var collector = new FilteredElementCollector(source))
            {
                var beams = collector.OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline)).ToElements().Where(IsBeam);
                foreach (var beam in beams)
                {
                    try
                    {
                        var line = (Line)((LocationCurve)beam.Location).Curve;
                        var extracted = new List<BeamSide>();
                        using (var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine })
                            ReadSides(beam.get_Geometry(options), transform, transform.OfVector(line.Direction).Normalize(), normal, link, beam.Id.ToString(), extracted);
                        sides.AddRange(extracted);
                    }
                    catch (Exception ex) { notes.Add($"梁 {beam.Id}：無法讀取側面（{ex.Message}）。"); }
                }
            }
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (Dimension dimension in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)))
            {
                try { if (dimension.References != null) known.Add(Signature(doc, dimension.References.Cast<Reference>())); }
                catch { notes.Add($"既有尺寸 {dimension.Id}：無法檢查重複參考。"); }
            }
            var plans = new List<AutoPlan>();
            double spacing = (double)savedOffset * view.Scale / 304.8;
            for (int b = 0; b < buckets.Count; b++)
            {
                XYZ direction = directions[b], measure = normal.CrossProduct(direction).Normalize();
                var items = buckets[b].Select(e => {
                    var l = (Line)((LocationCurve)e.Location).Curve;
                    double a = l.GetEndPoint(0).DotProduct(direction), z = l.GetEndPoint(1).DotProduct(direction);
                    return new Item { Reference = new Reference(e), Id = e.Id.ToString(), Station = l.Evaluate(.5,true).DotProduct(measure), Start = Math.Min(a,z), End = Math.Max(a,z) };
                }).ToList();
                foreach (var group in GroupRuns(items, (double)savedGap / 304.8))
                {
                    string ids = string.Join(",", group.Select(i => i.Id));
                    double left = group.Min(i => i.Station), right = group.Max(i => i.Station);
                    var elevations = group.SelectMany(i => {
                        var l = (Line)((LocationCurve)doc.GetElement(i.Reference).Location).Curve;
                        return new[] { l.GetEndPoint(0).DotProduct(normal), l.GetEndPoint(1).DotProduct(normal) };
                    }).ToList();
                    var matches = new List<BeamMatch>();
                    var rejected = new MatchDiagnostics();
                    foreach (var side in sides)
                    {
                        if (Math.Abs(side.Normal.DotProduct(measure)) < .999999) { rejected.Direction++; continue; }
                        double station = side.Origin.DotProduct(measure);
                        // Only the face looking toward the entire pipe bank is eligible.
                        if (station >= left - .5/304.8 && station <= right + .5/304.8) { rejected.Side++; continue; }
                        if (((left + right)/2 - station) * side.Normal.DotProduct(measure) <= 0) { rejected.Side++; continue; }
                        double distance = Math.Min(Math.Abs(station-left), Math.Abs(station-right));
                        double hmin = side.Vertices.Min(p => p.DotProduct(normal)), hmax = side.Vertices.Max(p => p.DotProduct(normal));
                        if (IntervalDistance(elevations.Min(), elevations.Max(), hmin, hmax) > (double)heightDistance / 304.8) { rejected.Height++; continue; }
                        double start = Math.Max(group.Max(i => i.Start), side.Vertices.Min(p => p.DotProduct(direction)));
                        double end = Math.Min(group.Min(i => i.End), side.Vertices.Max(p => p.DotProduct(direction)));
                        if (end-start <= 1/304.8) { rejected.Overlap++; continue; }
                        if (distance > (double)expandedDistance/304.8) { rejected.Distance++; continue; }
                        double first = Math.Min(left, station), last = Math.Max(right, station);
                        var occupied = plans.Where(p => Math.Abs(p.Direction.DotProduct(direction)) >= .999999 &&
                            p.Axis.GetEndPoint(1).DotProduct(measure) >= first && p.Axis.GetEndPoint(0).DotProduct(measure) <= last)
                            .Select(p => p.Axis.GetEndPoint(0).DotProduct(direction));
                        var section = ChooseSection(start, end, spacing, occupied);
                        if (!section.HasValue) { rejected.Placement++; continue; }
                        matches.Add(new BeamMatch { Side = side, Station = station, Distance = distance, Section = section.Value, Overlap=end-start });
                    }
                    matches = RankMatches(matches,normal,(group.Max(i=>i.Start)+group.Min(i=>i.End))/2);
                    if (matches.Count == 0) { notes.Add($"管段 {ids}："+rejected.Describe(sides.Count)); continue; }
                    if (matches.Count > 1 && matches[1].Distance - matches[0].Distance <= 25/304.8)
                    { notes.Add($"管段 {ids}：梁側面候選接近（梁 {matches[0].Side.Id}／{matches[1].Side.Id}），請以手選基準處理。"); continue; }
                    var chosen = matches[0];
                    if (chosen.Distance>(double)searchDistance/304.8) notes.Add($"管段 {ids}：擴大搜尋配對梁 {chosen.Side.Id}，距離 {chosen.Distance*304.8:0} mm，請確認。");
                    var chain = group.Concat(new[] { new Item { Reference = chosen.Side.Reference, Station = chosen.Station } }).OrderBy(i => i.Station).ToList();
                    if (chain.Zip(chain.Skip(1),(x,y) => y.Station-x.Station).Any(d => d < .5/304.8))
                    { notes.Add($"管段 {ids}：中心重疊，略過。"); continue; }
                    string key = Signature(doc, chain.Select(i => i.Reference));
                    if (known.Contains(key)) { notes.Add($"管段 {ids}：已有相同參考尺寸，略過。"); continue; }
                    XYZ origin = normal * view.Origin.DotProduct(normal) + direction * chosen.Section;
                    plans.Add(new AutoPlan { Items = chain, Direction = direction, Beam = chosen.Side.Id, Pipes = ids,
                        Axis = Line.CreateBound(origin + measure * chain.First().Station, origin + measure * chain.Last().Station) });
                    known.Add(key);
                }
            }
            if (plans.Count == 0) { TaskDialog.Show("自動梁側面定位", "沒有可建立的尺寸。\n" + string.Join("\n", notes)); return Result.Cancelled; }
            var preview = new TaskDialog("自動梁側面定位｜確認") {
                MainInstruction = $"預計建立 {plans.Count} 組尺寸",
                MainContent = $"參考模型：{link?.Name ?? "本機模型"}\n略過／待確認：{notes.Count} 項\n尚未檢查既有文字避碰；高程搜尋差不是同樓層或穿梁檢核。",
                ExpandedContent = string.Join("\n", plans.Select((p,i) => $"第 {i+1} 組：梁 {p.Beam} → 管段 {p.Pipes}").Concat(notes)),
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel, DefaultButton = TaskDialogResult.Cancel };
            if (preview.Show() != TaskDialogResult.Ok) return Result.Cancelled;
            int created = 0;
            using (var batch = new TransactionGroup(doc, "自動梁側面定位"))
            {
                batch.Start();
                foreach (var plan in plans)
                using (var tx = new Transaction(doc, "梁側面管排尺寸"))
                {
                    try
                    {
                        tx.Start();
                        var options = tx.GetFailureHandlingOptions();
                        options.SetFailuresPreprocessor(new RollBackErrors()); options.SetClearAfterRollback(true); tx.SetFailureHandlingOptions(options);
                        var refs = new ReferenceArray(); foreach (var item in plan.Items) refs.Append(item.Reference);
                        var dim = doc.Create.NewDimension(view, plan.Axis, refs, type);
                        doc.Regenerate();
                        if (dim == null || !dim.AreReferencesAvailable) throw new InvalidOperationException("無有效關聯參考。");
                        var values = dim.Segments.Size > 0 ? dim.Segments.Cast<DimensionSegment>().Select(s => s.Value).ToList() : new List<double?> { dim.Value };
                        var expected = plan.Items.Zip(plan.Items.Skip(1),(x,y) => y.Station-x.Station).ToList();
                        if (values.Count != expected.Count || values.Where((v,i) => !v.HasValue || Math.Abs(v.Value-expected[i]) > .5/304.8).Any())
                            throw new InvalidOperationException("尺寸值與平面定位距離不一致。");
                        string layoutWarning = ArrangeDimensionText(doc, view, dim);
                        if (layoutWarning != null) notes.Add(layoutWarning);
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 拒絕提交。");
                        created++;
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        notes.Add($"梁 {plan.Beam} → 管段 {plan.Pipes}：{ex.Message}");
                    }
                }
                if (batch.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("批次未成功提交。");
            }
            if (notes.Count > 0) TaskDialog.Show("自動梁側面定位", $"建立 {created} 組。\n" + string.Join("\n", notes));
            return created > 0 ? Result.Succeeded : Result.Cancelled;
        }
    }
}
