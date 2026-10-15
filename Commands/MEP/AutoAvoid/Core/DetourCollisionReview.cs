using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    // Solids establish actual intersections; expanded boxes conservatively flag clearance risks.
    // A clearance flag is never presented as a proven collision or silently accepted.
    internal static class DetourCollisionReview
    {
        private static readonly BuiltInCategory[] Categories = {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_PipeInsulations, BuiltInCategory.OST_DuctInsulations,
            BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Sprinklers
        };

        private sealed class Shape
        {
            internal XYZ Min, Max;
            internal List<Solid> Solids;
        }

        internal static List<string> Check(Document doc, IList<ElementId> created, ISet<ElementId> connections, double clearanceMm)
        {
            var issues = new List<string>();
            var shapes = created.Select(id => ReadShape(doc.GetElement(id), Transform.Identity)).ToList();
            if (shapes.Count == 0 || shapes.Any(s => s == null || s.Solids.Count == 0))
                throw new InvalidOperationException("無法取得新管段／彎頭實體，不能確認碰撞。");
            var excluded = new HashSet<ElementId>(created.Concat(connections));
            CheckDocument(doc, Transform.Identity, "本機", excluded, shapes, clearanceMm / 304.8, issues);
            foreach (RevitLinkInstance link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
            {
                var linked = link.GetLinkDocument();
                if (linked == null) { issues.Add($"連結 {link.Id} 未載入，複查不完整。"); continue; }
                if (new FilteredElementCollector(linked).OfClass(typeof(RevitLinkInstance)).Any())
                    issues.Add($"連結 {link.Id} 含巢狀連結，複查不完整。");
                CheckDocument(linked, link.GetTotalTransform(), $"連結 {link.Id}", new HashSet<ElementId>(),
                    shapes, clearanceMm / 304.8, issues);
            }
            return issues.Distinct().ToList();
        }

        private static void CheckDocument(Document doc, Transform transform, string prefix, ISet<ElementId> excluded,
            List<Shape> targets, double clearance, List<string> issues)
        {
            using (var collector = new FilteredElementCollector(doc))
            foreach (var element in collector.WherePasses(new ElementMulticategoryFilter(Categories)).WhereElementIsNotElementType())
            {
                if (excluded.Contains(element.Id)) continue;
                string label = $"{prefix}／{element.Id}";
                try
                {
                    var bounds = Bounds(element.get_BoundingBox(null), transform);
                    if (bounds == null) { issues.Add(label + "：無法取得範圍。"); continue; }
                    var nearby = targets.Where(s => Near(s, bounds, clearance)).ToList();
                    if (nearby.Count == 0) continue;
                    var obstacle = ReadShape(element, transform);
                    if (obstacle == null || obstacle.Solids.Count == 0) { issues.Add(label + "：缺少可檢查實體。"); continue; }
                    bool clash = false;
                    foreach (var target in nearby)
                        foreach (var a in target.Solids)
                            foreach (var b in obstacle.Solids)
                                using (var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, BooleanOperationsType.Intersect))
                                    if (intersection.Volume > 1e-9) clash = true;
                    if (clash) issues.Add(label + "：實體碰撞。");
                    else if (clearance > 0) issues.Add(label + "：淨距待確認（擴張外框保守檢查，非已證實碰撞）。");
                }
                catch (Exception ex) { issues.Add(label + "：幾何複查失敗（" + ex.Message + "）。"); }
            }
        }

        private static bool Near(Shape a, Shape b, double gap)
            => a.Min.X <= b.Max.X + gap && a.Max.X >= b.Min.X - gap &&
               a.Min.Y <= b.Max.Y + gap && a.Max.Y >= b.Min.Y - gap &&
               a.Min.Z <= b.Max.Z + gap && a.Max.Z >= b.Min.Z - gap;

        private static Shape Bounds(BoundingBoxXYZ box, Transform transform)
        {
            if (box == null) return null;
            var points = new List<XYZ>();
            for (int i = 0; i < 8; i++) points.Add(transform.OfPoint(box.Transform.OfPoint(new XYZ(
                (i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                (i & 4) == 0 ? box.Min.Z : box.Max.Z))));
            return new Shape { Min = new XYZ(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
                Max = new XYZ(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)) };
        }

        private static Shape ReadShape(Element element, Transform transform)
        {
            if (element == null) return null;
            var shape = Bounds(element.get_BoundingBox(null), transform);
            if (shape == null) return null;
            shape.Solids = new List<Solid>();
            using (var options = new Options { DetailLevel = ViewDetailLevel.Fine })
                ReadSolids(element.get_Geometry(options), transform, shape.Solids);
            return shape;
        }

        private static void ReadSolids(GeometryElement geometry, Transform transform, List<Solid> solids)
        {
            if (geometry == null) return;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Volume > 1e-9)
                    solids.Add(transform.IsIdentity ? solid : SolidUtils.CreateTransformed(solid, transform));
                else if (item is GeometryInstance instance) ReadSolids(instance.GetInstanceGeometry(), transform, solids);
            }
        }
    }
}
