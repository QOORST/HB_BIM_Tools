using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class CmdMepPositionDimension
    {
        private sealed class FamilyFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (!(element is FamilyInstance family) || element.Category == null) return false;
                if (objectMode == 4 && (element.Category.Id == new ElementId(BuiltInCategory.OST_PipeAccessory) ||
                    (openingFamily != null && family.Symbol.Family.UniqueId == openingFamily))) return true;
                if (objectMode == 2) return family.Symbol.Family.UniqueId == openingFamily;
                if (objectMode == 1) return element.Category.Id == new ElementId(BuiltInCategory.OST_PipeAccessory);
                return new[] { BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_ElectricalFixtures,
                    BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_DataDevices,
                    BuiltInCategory.OST_FireAlarmDevices, BuiltInCategory.OST_CommunicationDevices }
                    .Any(c => element.Category.Id == new ElementId(c));
            }
            public bool AllowReference(Reference reference, XYZ point) => false;
        }
        private sealed class MixedFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => new CurveFilter().AllowElement(e) || new FamilyFilter().AllowElement(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }
        private sealed class FamilyPlan
        {
            internal Reference Center;
            internal BeamSide Side;
            internal Line Axis;
            internal string Label;
            internal double Minimum, Maximum;
            internal XYZ Measure, Direction;
            internal double Start, End, Station, Sign;
            internal string BaselineKey;
        }
        private static List<List<FamilyPlan>> GroupFamilyPlans(List<FamilyPlan> plans, double gap)
        {
            var groups = new List<List<FamilyPlan>>();
            foreach (var plan in plans.OrderBy(p => p.BaselineKey, StringComparer.Ordinal).ThenBy(p => p.Station).ThenBy(p => p.Start))
            {
                var group = groups.FirstOrDefault(g => g[0].BaselineKey == plan.BaselineKey && g[0].Sign == plan.Sign &&
                    g[0].Measure.DotProduct(plan.Measure) > .999999 &&
                    plan.Station - g.Max(p => p.Station) <= gap &&
                    Math.Min(plan.End,g.Min(p=>p.End))-Math.Max(plan.Start,g.Max(p=>p.Start)) > 1/304.8);
                if (group == null) groups.Add(new List<FamilyPlan> { plan }); else group.Add(plan);
            }
            return groups;
        }
        private static bool TryAccessoryAxis(FamilyInstance instance, out XYZ axis)
        {
            axis = null;
            var ports = MepConnectionUi.Ports(instance);
            if (ports.Count == 0) return TryAccessorySolidAxis(instance, out axis);
            if (ports.Count != 2) return false;
            XYZ delta = ports[1].Origin - ports[0].Origin;
            if (delta.GetLength() < 1 / 304.8) return false;
            axis = delta.Normalize();
            XYZ direction = axis;
            // Use physical end connectors, not a family's arbitrary local X/Y/Z convention.
            return ports.All(p => Math.Abs(p.CoordinateSystem.BasisZ.Normalize().DotProduct(direction)) > .999999);
        }

        private sealed class SleeveCylinderAxis
        {
            internal XYZ Origin, Direction;
        }

        private static bool TryAccessorySolidAxis(FamilyInstance instance, out XYZ axis)
        {
            axis = null;
            var cylinders = new List<SleeveCylinderAxis>();
            using (var options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false })
                CollectSleeveCylinderAxes(instance.get_Geometry(options), Transform.Identity, cylinders);
            if (cylinders.Count == 0) return false;
            var first = cylinders[0];
            // Inner/outer bore and end collars must agree on a single physical axis.
            // Parallel but displaced cylinders (or multiple branches) remain ambiguous.
            foreach (var candidate in cylinders)
            {
                if (Math.Abs(first.Direction.DotProduct(candidate.Direction)) < .999999) return false;
                XYZ delta = candidate.Origin - first.Origin;
                if ((delta - first.Direction * delta.DotProduct(first.Direction)).GetLength() > 1 / 304.8) return false;
            }
            axis = first.Direction;
            return true;
        }

        private static void CollectSleeveCylinderAxes(GeometryElement geometry, Transform transform, List<SleeveCylinderAxis> axes)
        {
            if (geometry == null) return;
            foreach (GeometryObject item in geometry)
            {
                if (item is GeometryInstance nested)
                {
                    CollectSleeveCylinderAxes(nested.GetSymbolGeometry(), transform.Multiply(nested.Transform), axes);
                }
                else if (item is Solid solid && solid.Volume > 1e-9)
                {
                    foreach (Face face in solid.Faces)
                        if (face is CylindricalFace cylinder && cylinder.Area > 1e-9)
                            axes.Add(new SleeveCylinderAxis {
                                Origin = transform.OfPoint(cylinder.Origin),
                                Direction = transform.OfVector(cylinder.Axis).Normalize()
                            });
                }
            }
        }

        private static bool IsAccessorySpacingReference(XYZ axis, XYZ viewNormal, XYZ referenceNormal)
        {
            XYZ projected = axis - viewNormal * axis.DotProduct(viewNormal);
            // A riser has no preferred axis in plan, so both in-plane centers remain eligible.
            return projected.GetLength() < 1e-6 ||
                Math.Abs(projected.Normalize().DotProduct(referenceNormal)) < 1e-6;
        }

        private static List<XYZ> BoxCorners(BoundingBoxXYZ box)
        {
            var result = new List<XYZ>();
            foreach (double x in new[] { box.Min.X, box.Max.X })
            foreach (double y in new[] { box.Min.Y, box.Max.Y })
            foreach (double z in new[] { box.Min.Z, box.Max.Z })
                result.Add(box.Transform.OfPoint(new XYZ(x,y,z)));
            return result;
        }

        private static Result ExecuteFamilies(UIDocument ui, DimensionType type, List<FamilyInstance> supplied = null)
        {
            var doc = ui.Document; var view = ui.ActiveView;
            var selected = supplied ?? ui.Selection.PickElementsByRectangle(new FamilyFilter(), "框選需要中心定位的本機族實例")
                .Cast<FamilyInstance>().GroupBy(f => f.Id).Select(g => g.First()).ToList();
            if (selected.Count == 0) return Result.Cancelled;
            var link = sourceLink == null ? null : doc.GetElement(sourceLink) as RevitLinkInstance;
            var source = sourceLink == null ? doc : link?.GetLinkDocument();
            if (source == null) throw new InvalidOperationException("指定參考模型未載入，請重新設定。");
            XYZ normal = view.ViewDirection.Normalize();
            var transform = link?.GetTotalTransform() ?? Transform.Identity;
            var notes = new List<string>();
            var boxes = new Dictionary<ElementId,List<XYZ>>();
            foreach (var instance in selected)
            {
                var box = instance.get_BoundingBox(null);
                if (box == null) notes.Add($"{instance.Id}：沒有可搜尋的幾何範圍。");
                else boxes.Add(instance.Id, BoxCorners(box));
            }
            if (boxes.Count == 0) { TaskDialog.Show("族中心定位",string.Join("\n",notes)); return Result.Cancelled; }
            var points = boxes.Values.SelectMany(p => p).Select(transform.Inverse.OfPoint).ToList();
            double expand = ((double)expandedDistance + (double)heightDistance)/304.8;
            var sides = new List<BeamSide>();
            using (var outline = new Outline(new XYZ(points.Min(p=>p.X)-expand,points.Min(p=>p.Y)-expand,points.Min(p=>p.Z)-expand),
                new XYZ(points.Max(p=>p.X)+expand,points.Max(p=>p.Y)+expand,points.Max(p=>p.Z)+expand)))
            using (var collector = new FilteredElementCollector(source))
            {
                foreach (var beam in collector.OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline)).ToElements().Where(IsBeam))
                {
                    try
                    {
                        var direction = transform.OfVector(((Line)((LocationCurve)beam.Location).Curve).Direction).Normalize();
                        var extracted = new List<BeamSide>();
                        using (var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine })
                            ReadSides(beam.get_Geometry(options),transform,direction,normal,link,beam.Id.ToString(),extracted);
                        sides.AddRange(extracted);
                    }
                    catch (Exception ex) { notes.Add($"梁 {beam.Id}：{ex.Message}"); }
                }
            }
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (Dimension dim in new FilteredElementCollector(doc,view.Id).OfClass(typeof(Dimension)))
            {
                try { if (dim.References != null) known.Add(Signature(doc,dim.References.Cast<Reference>())); }
                catch { notes.Add($"尺寸 {dim.Id}：無法檢查重複。"); }
            }
            var plans = new List<FamilyPlan>();
            foreach (var instance in selected)
            {
                if (!boxes.TryGetValue(instance.Id,out var vertices)) continue;
                XYZ accessoryAxis = null;
                if (instance.Category?.Id == new ElementId(BuiltInCategory.OST_PipeAccessory))
                {
                    try
                    {
                        if (!TryAccessoryAxis(instance, out accessoryAxis))
                        {
                            notes.Add($"{instance.Id} {instance.Name}：接點或實體幾何無法確認唯一套管軸線，略過自動定位。");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"{instance.Id} {instance.Name}：讀取套管方向失敗：{ex.Message}");
                        continue;
                    }
                }
                var placement = instance.GetTransform();
                var kinds = new[] { FamilyInstanceReferenceType.CenterLeftRight, FamilyInstanceReferenceType.CenterFrontBack, FamilyInstanceReferenceType.CenterElevation };
                var normals = new[] { placement.BasisX.Normalize(), placement.BasisY.Normalize(), placement.BasisZ.Normalize() };
                for (int i=0;i<kinds.Length;i++)
                {
                    string label = $"{instance.Id} {instance.Name}／{(i==0 ? "左右中心" : i==1 ? "前後中心" : "上下中心")}";
                    if (Math.Abs(normals[i].DotProduct(normal))>1e-6)
                    { continue; }
                    if (accessoryAxis != null && !IsAccessorySpacingReference(accessoryAxis, normal, normals[i]))
                    { continue; }
                    var refs = instance.GetReferences(kinds[i]);
                    if (refs == null || refs.Count != 1)
                    { notes.Add(label+"：缺少唯一有效中心參考，未使用插入點代替。"); continue; }
                    XYZ measure=normals[i];
                    if (measure.DotProduct(view.RightDirection)<-1e-6 || (Math.Abs(measure.DotProduct(view.RightDirection))<=1e-6 && measure.DotProduct(view.UpDirection)<0)) measure=-measure;
                    XYZ direction=normal.CrossProduct(measure).Normalize();
                    double low=vertices.Min(p=>p.DotProduct(measure)), high=vertices.Max(p=>p.DotProduct(measure));
                    double along=vertices.Average(p=>p.DotProduct(direction));
                    var matches = new List<BeamMatch>();
                    var rejected = new MatchDiagnostics();
                    foreach (var side in sides)
                    {
                        if (Math.Abs(side.Normal.DotProduct(measure))<.999999) { rejected.Direction++; continue; }
                        double station=side.Origin.DotProduct(measure);
                        if (station>=low-.5/304.8 && station<=high+.5/304.8) { rejected.Side++; continue; }
                        if (((low+high)/2-station)*side.Normal.DotProduct(measure)<=0) { rejected.Side++; continue; }
                        double distance=Math.Min(Math.Abs(station-low),Math.Abs(station-high));
                        if (along<side.Vertices.Min(p=>p.DotProduct(direction)) || along>side.Vertices.Max(p=>p.DotProduct(direction))) { rejected.Overlap++; continue; }
                        if (IntervalDistance(vertices.Min(p=>p.DotProduct(normal)),vertices.Max(p=>p.DotProduct(normal)),
                            side.Vertices.Min(p=>p.DotProduct(normal)),side.Vertices.Max(p=>p.DotProduct(normal)))>(double)heightDistance/304.8) { rejected.Height++; continue; }
                        if (distance>(double)expandedDistance/304.8) { rejected.Distance++; continue; }
                        matches.Add(new BeamMatch { Side=side,Station=station,Distance=distance,Section=along,
                            Overlap=side.Vertices.Max(p=>p.DotProduct(direction))-side.Vertices.Min(p=>p.DotProduct(direction)) });
                    }
                    matches=RankMatches(matches,normal,along);
                    if (matches.Count==0) { notes.Add(label+"："+rejected.Describe(sides.Count)); continue; }
                    if (matches.Count>1 && matches[1].Distance-matches[0].Distance<=25/304.8)
                    { notes.Add(label+"：梁側面候選接近，未自動選定。"); continue; }
                    var match=matches[0];
                    if (match.Distance>(double)searchDistance/304.8) notes.Add(label+$"：擴大搜尋配對梁 {match.Side.Id}，距離 {match.Distance*304.8:0} mm，請確認。");
                    string key=Signature(doc,new[] { match.Side.Reference,refs[0] });
                    if (!known.Add(key)) { notes.Add(label+"：已有相同參考尺寸。"); continue; }
                    // The box locates/searches the line only. Revit measures the actual family reference.
                    XYZ origin=normal*view.Origin.DotProduct(normal)+direction*along;
                    double mid=(low+high)/2;
                    plans.Add(new FamilyPlan { Center=refs[0],Side=match.Side,Label=label,
                        Measure=measure,Direction=direction,Station=mid,Sign=Math.Sign(mid-match.Station),
                        Start=vertices.Min(p=>p.DotProduct(direction)),End=vertices.Max(p=>p.DotProduct(direction)),
                        BaselineKey=match.Side.Reference.ConvertToStableRepresentation(doc),
                        Minimum=match.Distance,Maximum=Math.Max(Math.Abs(match.Station-low),Math.Abs(match.Station-high)),
                        Axis=Line.CreateBound(origin+measure*Math.Min(match.Station,mid),origin+measure*Math.Max(match.Station,mid)) });
                }
            }
            if (plans.Count==0) { TaskDialog.Show("族中心定位","沒有可建立的尺寸。\n"+string.Join("\n",notes)); return Result.Cancelled; }
            var groups=GroupFamilyPlans(plans,(double)savedGap/304.8);
            groups=groups.Where(g => {
                bool exists=known.Contains(Signature(doc,new[] {g[0].Side.Reference}.Concat(g.Select(p=>p.Center))));
                // Single-reference plans were already inserted into 'known' during candidate selection.
                if (g.Count==1) return true;
                if (exists) notes.Add(string.Join(",",g.Select(p=>p.Label))+"：已有相同連續尺寸。");
                return !exists;
            }).ToList();
            if (groups.Count==0) { TaskDialog.Show("族中心定位","沒有新尺寸需要建立。\n"+string.Join("\n",notes)); return Result.Cancelled; }
            var preview=new TaskDialog("族中心定位｜確認") {
                MainInstruction=$"預計建立 {groups.Count} 組中心定位尺寸",
                MainContent="同梁面、同側、共同列範圍合併為連續尺寸。\n梁邊至第一個中心，再標相鄰中心距。\n尚未檢查與既有文字的碰撞。",
                ExpandedContent=string.Join("\n",groups.Select((g,i)=>$"第 {i+1} 組 → 梁 {g[0].Side.Id}："+string.Join(",",g.Select(p=>p.Label))).Concat(notes)),
                CommonButtons=TaskDialogCommonButtons.Ok|TaskDialogCommonButtons.Cancel,DefaultButton=TaskDialogResult.Cancel };
            if (preview.Show()!=TaskDialogResult.Ok) return Result.Cancelled;
            int created=0;
            using (var batch=new TransactionGroup(doc,"族中心施工定位"))
            {
                batch.Start();
                foreach (var group in groups)
                using (var tx=new Transaction(doc,"族中心尺寸"))
                {
                    try
                    {
                        tx.Start();
                        var options=tx.GetFailureHandlingOptions();
                        options.SetFailuresPreprocessor(new RollBackErrors());options.SetClearAfterRollback(true);tx.SetFailureHandlingOptions(options);
                        var first=group[0];
                        var measured=new List<Item> {new Item {Reference=first.Side.Reference,Station=first.Side.Origin.DotProduct(first.Measure)}};
                        // Probe native references inside the same transaction; no probe dimensions survive.
                        foreach (var plan in group)
                        {
                            var probeRefs=new ReferenceArray();probeRefs.Append(plan.Side.Reference);probeRefs.Append(plan.Center);
                            var probe=doc.Create.NewDimension(view,plan.Axis,probeRefs,type);doc.Regenerate();
                            if (probe==null || !probe.AreReferencesAvailable || !probe.Value.HasValue || probe.Segments.Size!=0)
                                throw new InvalidOperationException(plan.Label+"：無有效單段中心尺寸。");
                            double value=probe.Value.Value;
                            if (value<plan.Minimum-.5/304.8 || value>plan.Maximum+.5/304.8)
                                throw new InvalidOperationException(plan.Label+"：族中心參考超出幾何範圍。");
                            measured.Add(new Item {Reference=plan.Center,Station=measured[0].Station+plan.Sign*value});
                            doc.Delete(probe.Id);
                        }
                        measured=measured.OrderBy(p=>p.Station).ToList();
                        var expected=measured.Zip(measured.Skip(1),(a,b)=>b.Station-a.Station).ToList();
                        if (expected.Any(d=>d<.5/304.8)) throw new InvalidOperationException("中心參考重合，請分開選取不同高程或同位置的物件。");
                        double along=(group.Max(p=>p.Start)+group.Min(p=>p.End))/2;
                        XYZ origin=normal*view.Origin.DotProduct(normal)+first.Direction*along;
                        var axis=Line.CreateBound(origin+first.Measure*measured.First().Station,origin+first.Measure*measured.Last().Station);
                        var refs=new ReferenceArray();foreach (var item in measured) refs.Append(item.Reference);
                        var dim=doc.Create.NewDimension(view,axis,refs,type);doc.Regenerate();
                        if (dim==null || !dim.AreReferencesAvailable) throw new InvalidOperationException("連續尺寸參考失效。");
                        var actual=dim.Segments.Size>0?dim.Segments.Cast<DimensionSegment>().Select(s=>s.Value).ToList():new List<double?> {dim.Value};
                        if (actual.Count!=expected.Count || actual.Where((v,i)=>!v.HasValue || Math.Abs(v.Value-expected[i])>.5/304.8).Any())
                            throw new InvalidOperationException("連續尺寸值與原生中心距不符。");
                        string layoutWarning = ArrangeDimensionText(doc, view, dim);
                        if (layoutWarning != null) notes.Add(layoutWarning);
                        if (tx.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException("Revit 拒絕提交。");
                        created++;
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus()==TransactionStatus.Started) tx.RollBack();
                        notes.Add(string.Join(",",group.Select(p=>p.Label))+"："+ex.Message);
                    }
                }
                if (batch.Assimilate()!=TransactionStatus.Committed) throw new InvalidOperationException("批次未成功提交。");
            }
            if (notes.Count>0) TaskDialog.Show("族中心定位",$"建立 {created} 條尺寸。\n"+string.Join("\n",notes));
            return created>0?Result.Succeeded:Result.Cancelled;
        }
    }
}
