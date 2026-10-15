using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class CmdMepPositionDimension
    {
        private static bool insulationEdge;
        private static string PipeEdgeLabel => insulationEdge ? "保溫外緣淨距" : "管外緣淨距";

        private static Item PipeEdgeItem(Item center, XYZ measure, XYZ viewNormal, double datum, RevitLinkInstance link)
        {
            if (!(center.SourceElement is Pipe pipe)) throw new InvalidOperationException("管外緣模式目前僅支援圓形直管；風管、電管及線架不以中心距替代。");
            var line = TargetLine(pipe, link);
            if (Math.Abs(line.Direction.DotProduct(viewNormal)) > 1e-6)
                throw new InvalidOperationException("斜管／立管的投影外緣不作平面淨距。");
            double diameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)?.AsDouble() ?? 0;
            if (diameter <= 0) throw new InvalidOperationException("缺少實際管外徑，不以公稱管徑代替。");
            double radius = diameter / 2;
            Element surface = pipe;
            if (insulationEdge)
            {
                var insulation = InsulationLiningBase.GetInsulationIds(pipe.Document, pipe.Id)
                    .Select(id => pipe.Document.GetElement(id)).OfType<PipeInsulation>().ToList();
                if (insulation.Count != 1 || insulation[0].Thickness <= 0)
                    throw new InvalidOperationException("需有唯一、有效厚度的模型保溫層；不使用假設厚度。");
                surface = insulation[0]; radius += insulation[0].Thickness;
            }
            double distance = Math.Abs(center.Station - datum);
            if (distance <= radius + .5 / 304.8) throw new InvalidOperationException("外緣已接觸或跨越基準，不建立正值淨距。");
            double sign = Math.Sign(center.Station - datum);
            var faces = new List<Tuple<Reference, double>>();
            using (var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine })
                ReadPipeFaces(surface.get_Geometry(options), link?.GetTotalTransform() ?? Transform.Identity,
                    line, measure * -sign, radius, faces);
            var face = faces.OrderByDescending(f => f.Item2).FirstOrDefault();
            if (face == null) throw new InvalidOperationException("找不到與量測方向相符的外緣直線參照，已略過；請調整視圖方向。");
            return new Item { SourceElement = pipe, Reference = TargetReference(face.Item1, link),
                Station = center.Station - sign * radius, Start = center.Start, End = center.End,
                GeometryLine = line, Id = center.Id };
        }

        private static void ReadPipeFaces(GeometryElement geometry, Transform transform, Line axis, XYZ towardDatum,
            double radius, List<Tuple<Reference, double>> matches)
        {
            if (geometry == null) return;
            foreach (GeometryObject item in geometry)
            {
                if (item is GeometryInstance instance)
                    ReadPipeFaces(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), axis, towardDatum, radius, matches);
                else if (item is Solid solid)
                    foreach (Face raw in solid.Faces)
                    {
                        if (!(raw is CylindricalFace face) || face.Reference == null) continue;
                        if (Math.Abs(transform.OfVector(face.Axis).Normalize().DotProduct(axis.Direction)) < .999999) continue;
                        if (Math.Abs(transform.OfVector(face.get_Radius(0)).GetLength() - radius) > .1 / 304.8 ||
                            Math.Abs(transform.OfVector(face.get_Radius(1)).GetLength() - radius) > .1 / 304.8) continue;
                        var origin = transform.OfPoint(face.Origin);
                        if ((origin - axis.Project(origin).XYZPoint).GetLength() > .1 / 304.8) continue;
                        foreach (EdgeArray loop in face.EdgeLoops)
                            foreach (Edge edge in loop)
                            {
                                if (!(edge.AsCurve() is Line boundary) || edge.Reference == null) continue;
                                if (Math.Abs(transform.OfVector(boundary.Direction).Normalize().DotProduct(axis.Direction)) < .999999) continue;
                                var point = transform.OfPoint(boundary.Evaluate(.5, true));
                                var radial = point - axis.Project(point).XYZPoint;
                                // Only a true tangent in this view; never substitute a quadrant seam at another angle.
                                if ((radial - towardDatum * radius).GetLength() > .1 / 304.8) continue;
                                matches.Add(Tuple.Create(edge.Reference, radial.DotProduct(towardDatum)));
                            }
                    }
            }
        }

        private static void LabelPipeEdgeDimension(Dimension dimension)
        {
            if (!edgeDistance) return;
            if (dimension.Segments.Size == 0) dimension.Above = PipeEdgeLabel;
            else foreach (DimensionSegment segment in dimension.Segments) segment.Above = PipeEdgeLabel;
        }
    }
}
