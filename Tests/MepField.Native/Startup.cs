using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using YD_RevitTools.LicenseManager.Commands.MEP;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    // Data carrier matching production RoutingPlanner; geometry operations link production code.
    public class DetourPlan
    {
        public IList<XYZ> Path { get; set; }
        public DirectionMode UsedDirection { get; set; }
        public double OffsetFt { get; set; }
        public bool IsValid { get; set; } = true;
    }
}

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // Fixture adapter only; reference extraction itself is linked unchanged from production.
    public partial class CmdMepPositionDimension
    {
        private static bool edgeDistance = true;
        private sealed class Item { internal Element SourceElement; internal Reference Reference; internal double Station, Start, End; internal Line GeometryLine; internal string Id; }
        private static Line TargetLine(Element e, RevitLinkInstance link) => (Line)((LocationCurve)e.Location).Curve;
        private static Reference TargetReference(Reference r, RevitLinkInstance link) => r;
        internal static Tuple<Reference, double> Probe(Pipe pipe, bool insulated)
        {
            insulationEdge = insulated;
            var line = (Line)((LocationCurve)pipe.Location).Curve;
            var item = PipeEdgeItem(new Item { SourceElement = pipe, Station = line.Evaluate(.5, true).Y, Start = 0, End = 20, Id = pipe.Id.ToString() }, XYZ.BasisY, XYZ.BasisZ, 0, null);
            return Tuple.Create(item.Reference, item.Station);
        }
    }
}

namespace HB.MepField.Native
{
    public sealed class Startup : IExternalApplication, IExternalEventHandler
    {
        private ExternalEvent pending;
        private string folder;
        public Result OnStartup(UIControlledApplication app)
        {
            folder = Path.GetDirectoryName(typeof(Startup).Assembly.Location);
            pending = ExternalEvent.Create(this); app.Idling += Idle; return Result.Succeeded;
        }
        private void Idle(object sender, IdlingEventArgs args) { ((UIApplication)sender).Idling -= Idle; pending.Raise(); }
        public Result OnShutdown(UIControlledApplication app) { pending?.Dispose(); return Result.Succeeded; }
        public string GetName() => "HB MEP isolated geometry validation";
        public void Execute(UIApplication ui)
        {
            Document doc = null;
            var results = new List<string>();
            try
            {
                var previewRoutes = new List<YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.UI.DetourPreviewItem> {
                    new YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.UI.DetourPreviewItem { Label = "管段 QA｜上翻",
                        Path = DetourRouteBuilder.Build(Line.CreateBound(new XYZ(0,0,0), new XYZ(20,0,-.4)),
                            new XYZ(4,0,-.08), new XYZ(16,0,-.32), new AvoidOptions { ExtraOffsetMm = 609.6, BendAngle = 45 }, DirectionMode.Up, .01).Path } };
                using (var preview = new YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.UI.DetourPreviewForm(previewRoutes, new List<string>(), 50))
                {
                    preview.Show(); preview.PerformLayout(); System.Windows.Forms.Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(preview.Width, preview.Height))
                    { preview.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, preview.Width, preview.Height)); bitmap.Save(Path.Combine(folder, "preview.png")); }
                    preview.Close();
                }
                doc = ui.Application.NewProjectDocument("C:/ProgramData/Autodesk/RVT 2024/Templates/Traditional Chinese/Plumbing-DefaultTWNCHT.rte");
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).First();
                var view = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel.Id == level.Id);
                var pipeType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).FirstElementId();
                var system = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).FirstElementId();
                var insulationType = new FilteredElementCollector(doc).OfClass(typeof(PipeInsulationType)).FirstElementId();
                Pipe pipe; Grid grid;
                using (var tx = new Transaction(doc, "QA fixture"))
                {
                    tx.Start();
                    view.DetailLevel = ViewDetailLevel.Fine;
                    grid = Grid.Create(doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(20, 0, 0)));
                    pipe = Pipe.Create(doc, system, pipeType, level.Id, new XYZ(0, 5, level.Elevation + 3), new XYZ(20, 5, level.Elevation + 3));
                    pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(100 / 304.8);
                    if (tx.Commit() != TransactionStatus.Committed) throw new Exception("fixture commit failed");
                }
                var pipeId = pipe.Id; var gridId = grid.Id; var viewId = view.Id; double elevation = level.Elevation;
                string model = Path.Combine(folder, "MEP-Field-QA.rvt");
                doc.SaveAs(model); doc.Close(false); doc = ui.OpenAndActivateDocument(model).Document;
                pipe = (Pipe)doc.GetElement(pipeId); grid = (Grid)doc.GetElement(gridId); view = (ViewPlan)doc.GetElement(viewId);
                ui.ActiveUIDocument.ActiveView = view;
                SlopeChecks.Run(doc, system, pipeType, view.GenLevel.Id, results);
                var baselineIds = new HashSet<ElementId>(new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds());
                using (var group = new TransactionGroup(doc, "QA cancel preview"))
                {
                    group.Start();
                    using (var probe = new Transaction(doc, "QA dependency diagnostic"))
                    {
                        probe.Start();
                        var elementsBefore = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToDictionary(e => e.Id,
                            e => e.GetType().FullName + " name=" + e.Name + " category=" + e.Category?.Name + "/" + e.Category?.Id + " type=" + e.GetTypeId() + " ownerView=" + e.OwnerViewId);
                        results.Add("INFO dependent=" + string.Join(" | ", pipe.GetDependentElements(null).Select(id => id + " " + (elementsBefore.TryGetValue(id, out var name) ? name : "internal"))));
                        results.Add("INFO source delete cascade=" + string.Join(" | ", doc.Delete(pipeId).Select(id => id + " " + (elementsBefore.TryGetValue(id, out var name) ? name : "internal"))));
                        probe.RollBack(); pipe = (Pipe)doc.GetElement(pipeId);
                    }
                    using (var tx = new Transaction(doc, "QA actual reroute"))
                    {
                        tx.Start();
                        var start = ((Line)((LocationCurve)pipe.Location).Curve).GetEndPoint(0);
                        var path = new List<XYZ> { start, start + XYZ.BasisX * 4, start + XYZ.BasisX * 6 + XYZ.BasisZ * 2,
                            start + XYZ.BasisX * 14 + XYZ.BasisZ * 2, start + XYZ.BasisX * 16, start + XYZ.BasisX * 20 };
                        bool made = RevitUtils.ReplaceWithDetour(doc, pipe, new DetourPlan { Path = path }, new AvoidOptions(), out var created, out var reason, retainOriginal: true);
                        results.Add((made && created.Count == 9 ? "PASS" : "FAIL") + " actual reroute: created=" + created.Count);
                        if (made)
                        {
                            var clear = DetourCollisionReview.Check(doc, created, new HashSet<ElementId>(), 0);
                            results.Add((clear.Count == 0 ? "PASS" : "FAIL") + " clear reroute: " + string.Join(" | ", clear));
                            var commit = tx.Commit();
                            results.Add((commit == TransactionStatus.Committed ? "PASS" : "FAIL") + " temporary reroute commit");
                        }
                        else tx.RollBack();
                    }
                    group.RollBack();
                    pipe = (Pipe)doc.GetElement(pipeId);
                    bool restored = pipe != null && Math.Abs(((LocationCurve)pipe.Location).Curve.Length - 20) < 1e-9 &&
                        baselineIds.SetEquals(new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds());
                    results.Add(restored ? "PASS cancel restores original geometry and exact element set" : "FAIL cancel changed fixture");
                }
                using (var margin = new Transaction(doc, "QA clearance distinction"))
                {
                    margin.Start();
                    var wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                    double side = 5 + pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER).AsDouble() / 2 + wallType.Width / 2 + 20 / 304.8;
                    Wall.Create(doc, Line.CreateBound(new XYZ(0, side, elevation), new XYZ(20, side, elevation)), wallType.Id, view.GenLevel.Id, 12, 0, false, false);
                    doc.Regenerate();
                    var exact = DetourCollisionReview.Check(doc, new[] {pipeId}, new HashSet<ElementId>(), 0);
                    var nearby = DetourCollisionReview.Check(doc, new[] {pipeId}, new HashSet<ElementId>(), 50);
                    results.Add(exact.Count == 0 && nearby.Any(s => s.Contains("淨距待確認")) && !nearby.Any(s => s.Contains("：實體碰撞"))
                        ? "PASS clearance risk distinguished from actual solid collision" : "FAIL clearance distinction: " + string.Join(" | ", nearby));
                    margin.RollBack();
                }
                foreach (bool insulated in new[] { false, true })
                {
                    pipe = (Pipe)doc.GetElement(pipeId);
                    if (insulated)
                        using (var setup = new Transaction(doc, "QA insulation fixture"))
                        {
                            setup.Start(); var insulation = PipeInsulation.Create(doc, pipe.Id, insulationType, 25 / 304.8); setup.Commit();
                            results.Add("INFO insulation created=" + insulation.Id + " thickness=" + insulation.Thickness + " ids=" + string.Join(",", InsulationLiningBase.GetInsulationIds(doc, pipe.Id)));
                        }
                    using (var tx = new Transaction(doc, "QA edge probe"))
                    {
                        tx.Start();
                        try
                        {
                            doc.Regenerate();
                            var edge = CmdMepPositionDimension.Probe(pipe, insulated);
                            var refs = new ReferenceArray(); refs.Append(new Reference(grid)); refs.Append(edge.Item1);
                            var dim = doc.Create.NewDimension(view, Line.CreateBound(new XYZ(10, 0, elevation), new XYZ(10, 5, elevation)), refs);
                            doc.Regenerate();
                            bool pass = dim != null && dim.AreReferencesAvailable && dim.Value.HasValue && Math.Abs(dim.Value.Value - edge.Item2) < .5 / 304.8;
                            results.Add((pass ? "PASS" : "FAIL") + " " + (insulated ? "insulated" : "bare") + " expected=" + edge.Item2 + " actual=" + dim?.Value + " refs=" + dim?.AreReferencesAvailable);
                        }
                        catch (Exception ex) { results.Add("FAIL " + (insulated ? "insulated" : "bare") + ": " + ex.Message); }
                        tx.RollBack();
                    }
                }
                using (var tx = new Transaction(doc, "QA collision"))
                {
                    tx.Start();
                    var wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                    level = (Level)doc.GetElement(view.GenLevel.Id);
                    var wall = Wall.Create(doc, Line.CreateBound(new XYZ(10, 0, elevation), new XYZ(10, 10, elevation)), wallType.Id, level.Id, 12, 0, false, false);
                    doc.Regenerate();
                    var issues = DetourCollisionReview.Check(doc, new[] { pipe.Id }, new HashSet<ElementId>(), 0);
                    results.Add((issues.Any(s => s.Contains("／" + wall.Id + "：實體碰撞")) ? "PASS" : "FAIL") + " actual wall solid collision: " + string.Join(" | ", issues));
                    tx.RollBack();
                }
            }
            catch (Exception ex) { results.Add("FIXTURE ERROR: " + ex); }
            finally
            {
                File.WriteAllLines(Path.Combine(folder, "result.txt"), results);
                if (doc != null && doc.IsValidObject) { if (ui.ActiveUIDocument?.Document == doc) doc.Save(); else doc.Close(false); }
            }
        }
    }
}
