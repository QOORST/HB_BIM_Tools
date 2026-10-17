using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using YD_RevitTools.LicenseManager.Commands.AR.AutoJoin;

namespace HB.SplitFloor.Native
{
    public sealed class Startup : IExternalApplication, IExternalEventHandler
    {
        private ExternalEvent pending;
        private string folder;
        private bool started;
        public Result OnStartup(UIControlledApplication app)
        {
            folder = Path.GetDirectoryName(typeof(Startup).Assembly.Location);
            pending = ExternalEvent.Create(this); app.Idling += Idle; return Result.Succeeded;
        }
        private void Idle(object sender, IdlingEventArgs args)
        {
            if ((!started || File.Exists(Path.Combine(folder, "rerun.txt"))) && !pending.IsPending) pending.Raise();
        }
        public Result OnShutdown(UIControlledApplication app) { app.Idling -= Idle; pending.Dispose(); return Result.Succeeded; }
        public string GetName() => "HB isolated split floor validation";
        public void Execute(UIApplication ui)
        {
            if (!started) { started = true; RunOnce(ui, folder); return; }
            string request = Path.Combine(folder, "rerun.txt");
            if (!File.Exists(request)) return;
            string assembly = File.ReadAllText(request).Trim(); File.Delete(request);
            System.Reflection.Assembly.LoadFrom(assembly).GetType(typeof(Startup).FullName)
                .GetMethod(nameof(RunOnce)).Invoke(null, new object[] { ui, Path.GetDirectoryName(assembly) });
        }
        public static void RunOnce(UIApplication ui, string output)
        {
            Document doc = null;
            string log = Path.Combine(output, "result.txt");
            Action<string> record = s => File.AppendAllText(log, s + Environment.NewLine);
            try
            {
                doc = ui.Application.NewProjectDocument("C:/ProgramData/Autodesk/RVT 2024/Templates/Traditional Chinese/Structural Analysis-DefaultTWNCHT.rte");
                var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
                foreach (var s in symbols.Where(s => s.Category.Id == new ElementId(BuiltInCategory.OST_StructuralFraming) || s.Category.Id == new ElementId(BuiltInCategory.OST_StructuralColumns)))
                    record("INFO symbol " + s.Id + " " + s.FamilyName + " : " + s.Name);
                Func<BuiltInCategory, FamilySymbol> find = category => symbols.First(s => s.Category.Id == new ElementId(category) && (s.FamilyName.Contains("混凝土") || s.FamilyName.Contains("Concrete")));
                var beamType = find(BuiltInCategory.OST_StructuralFraming);
                var columnType = find(BuiltInCategory.OST_StructuralColumns);
                columnType = symbols.First(s => s.Category.Id == new ElementId(BuiltInCategory.OST_StructuralColumns) && s.Name == "600 x 750mm");
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.Elevation).First();
                var floorType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(t => !t.IsFoundationSlab);
                Guid customId = Guid.NewGuid();
                string previous = ui.Application.SharedParametersFilename;
                string sharedFile = Path.Combine(output, "qa-parameters.txt"); File.WriteAllText(sharedFile, "");
                try
                {
                    ui.Application.SharedParametersFilename = sharedFile;
                    var definition = ui.Application.OpenSharedParameterFile().Groups.Create("QA")
                        .Definitions.Create(new ExternalDefinitionCreationOptions("HB QA 備註", SpecTypeId.String.Text) { GUID = customId });
                    using (var tx = new Transaction(doc, "QA binding"))
                    {
                        tx.Start(); var categories = ui.Application.Create.NewCategorySet();
                        categories.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Floors));
                        if (!doc.ParameterBindings.Insert(definition, ui.Application.Create.NewInstanceBinding(categories), GroupTypeId.Data)) throw new Exception("Binding failed");
                        beamType.Activate(); columnType.Activate(); tx.Commit();
                    }
                }
                finally { ui.Application.SharedParametersFilename = previous; }
                foreach (string name in new[] { "beam", "beam-column-notch", "beam-column-hole", "column-only",
                    "beam-column-hole-positive-offset", "beam-column-hole-negative-offset", "beam-column-hole-rotated" })
                using (var group = new TransactionGroup(doc, "QA " + name))
                {
                    group.Start();
                    double offset = name.Contains("positive") ? 1 : name.Contains("negative") ? -1 : 0;
                    var rotation = name.Contains("rotated") ? Transform.CreateRotation(XYZ.BasisZ, .37) : Transform.Identity;
                    var cutters = new List<Element>(); Floor floor;
                    using (var tx = new Transaction(doc, "QA fixture"))
                    {
                        tx.Start();
                        floor = Floor.Create(doc, new List<CurveLoop> { CurveLoop.CreateViaTransform(Rectangle(0, 0, 40, 30, level.Elevation), rotation) }, floorType.Id, level.Id);
                        floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(offset);
                        floor.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("F-QA");
                        floor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("分區施工備註");
                        floor.get_Parameter(customId).Set("共享參數繼承");
                        if (name != "column-only")
                        {
                            var beam = doc.Create.NewFamilyInstance(Line.CreateBound(rotation.OfPoint(new XYZ(20, -5, level.Elevation + offset)), rotation.OfPoint(new XYZ(20, 35, level.Elevation + offset))), beamType, level, StructuralType.Beam);
                            StructuralFramingUtils.DisallowJoinAtEnd(beam, 0); StructuralFramingUtils.DisallowJoinAtEnd(beam, 1);
                            cutters.Add(beam);
                        }
                        if (name != "beam")
                        {
                            var column = doc.Create.NewFamilyInstance(rotation.OfPoint(new XYZ(name.Contains("notch") ? 20 : 10, 15, level.Elevation)), columnType, level, StructuralType.Column);
                            column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM).Set(-3);
                            cutters.Add(column);
                        }
                        if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Fixture commit failed");
                    }
                    var sourceId = floor.Id;
                    using (var probe = new Transaction(doc, "QA inspect join geometry"))
                    {
                        probe.Start();
                        foreach (var cutter in cutters)
                        {
                            var box = cutter.get_BoundingBox(null);
                            record("INFO cutter " + cutter.Category.Name + " z=" + box.Min.Z + ".." + box.Max.Z + " intersects=" + new ElementIntersectsElementFilter(floor).PassesFilter(cutter));
                            if (!JoinGeometryUtils.AreElementsJoined(doc, floor, cutter)) JoinGeometryUtils.JoinGeometry(doc, floor, cutter);
                            if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, cutter, floor)) JoinGeometryUtils.SwitchJoinOrder(doc, floor, cutter);
                        }
                        doc.Regenerate();
                        foreach (var reference in HostObjectUtils.GetTopFaces(floor))
                        {
                            var face = (PlanarFace)floor.GetGeometryObjectFromReference(reference);
                            record("INFO top face z=" + face.Origin.Z + " normal=" + face.FaceNormal + " loops=" + face.GetEdgesAsCurveLoops().Count);
                            foreach (var loop in face.GetEdgesAsCurveLoops()) record("INFO loop edges=" + loop.Count() + " CCW=" + loop.IsCounterclockwise(XYZ.BasisZ));
                        }
                        probe.RollBack(); floor = (Floor)doc.GetElement(sourceId);
                    }
                    var filter = new FloorAndFramingFilter();
                    if (!filter.AllowElement(floor) || cutters.Any(c => !filter.AllowElement(c))) throw new Exception("Native selection filter failed");
                    var result = SplitEngine.RunSplitFloor(doc, new[] { floor }, cutters);
                    record(name + ": deleted=" + result.OriginalDeleted + " new=" + result.NewElementsCreated + " skipped=" + result.Skipped + " failed=" + result.FailedOperations + " " + string.Join(" | ", result.FailureSamples));
                    if (name == "column-only")
                    {
                        if (result.Skipped != 1 || doc.GetElement(sourceId) == null) throw new Exception("Column hole should retain source when not split");
                    }
                    else
                    {
                        if (result.OriginalDeleted != 1 || result.NewElementsCreated != 2) throw new Exception("Expected two valid split regions: " + name);
                        var pieces = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().ToList();
                        if (pieces.Count != 2 || pieces.Any(p => p.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString() != "F-QA" ||
                            p.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString() != "分區施工備註" || p.get_Parameter(customId).AsString() != "共享參數繼承" ||
                            Math.Abs(p.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).AsDouble() - offset) > 1e-9)) throw new Exception("Inherited parameters mismatch");
                        if (name.Contains("hole") && !pieces.Any(p => HostObjectUtils.GetTopFaces(p).Any(r => ((PlanarFace)p.GetGeometryObjectFromReference(r)).GetEdgesAsCurveLoops().Count > 1)))
                            throw new Exception("Column hole was filled");
                        if (name.Contains("notch") && !pieces.Any(p => HostObjectUtils.GetTopFaces(p).Any(r => ((PlanarFace)p.GetGeometryObjectFromReference(r)).GetEdgesAsCurveLoops().Any(l => l.Count() > 4))))
                            throw new Exception("Column notch missing");
                    }
                    record("PASS " + name + " native geometry, selection, parameter inheritance and transaction");
                    group.RollBack();
                }
                record("PASS ALL");
            }
            catch (Exception ex) { record("FAIL " + ex); }
            finally { if (doc != null && doc.IsValidObject) doc.Close(false); }
        }
        private static CurveLoop Rectangle(double x1, double y1, double x2, double y2, double z)
        {
            var p = new[] { new XYZ(x1,y1,z), new XYZ(x2,y1,z), new XYZ(x2,y2,z), new XYZ(x1,y2,z) };
            var loop = new CurveLoop(); for (int i=0;i<4;i++) loop.Append(Line.CreateBound(p[i],p[(i+1)%4])); return loop;
        }
    }
}
