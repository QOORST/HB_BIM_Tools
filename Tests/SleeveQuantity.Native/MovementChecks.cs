using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;

namespace HB.SleeveQuantity.Native
{
    public sealed partial class Startup
    {
        private XYZ Center(FamilyInstance instance)
        {
            var box = instance.get_BoundingBox(null);
            return box.Transform.OfPoint((box.Min + box.Max) * .5);
        }
        private void RunMovementChecks()
        {
            using (var group = new TransactionGroup(_doc, "QA pipe movement"))
            {
                group.Start();
                Pipe pipe = null;
                FamilyInstance sleeve = null;
                object options = Activator.CreateInstance(T("PipeSleeveOptions"));
                void Set(string name, object value) => options.GetType().GetProperty(name).SetValue(options, value);
                Set("UseDiameterSymbolMap", false); Set("IncludeLinks", false);
                Set("ExcludeAdditionElements", false); Set("LimitToActiveView", false);
                Set("AutoNumber", false); Set("PreserveNominalTypeDimensions", true);
                Tx("QA real pipe, two complete walls and one incomplete crossing", () =>
                {
                    var level = (Level)Call("SleeveGlLevel", "Actual", _doc, _doc.GetElement(_first));
                    var symbol = ((FamilyInstance)_doc.GetElement(_first)).Symbol;
                    Set("DefaultWallSleeveSymbolId", symbol.Id);
                    var wallType = new FilteredElementCollector(_doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                    foreach (double y in new[] { 0.0, 6.0, 9.99 })
                        Wall.Create(_doc, Line.CreateBound(new XYZ(-10,y,0), new XYZ(10,y,0)), wallType.Id, level.Id, 12, 0, false, false);
                    var system = PipingSystemType.Create(_doc, MEPSystemClassification.DomesticColdWater, "QA-MOVE");
                    var pipeType = new FilteredElementCollector(_doc).OfClass(typeof(PipeType)).Cast<PipeType>().First();
                    pipe = Pipe.Create(_doc, system.Id, pipeType.Id, level.Id, new XYZ(0,-10,5), new XYZ(0,10,5));
                    pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(100 / 304.8);
                    sleeve = _doc.Create.NewFamilyInstance(new XYZ(0,0,5), symbol, level, StructuralType.NonStructural);
                    sleeve.LookupParameter("來源管線Id").Set(pipe.Id.Value.ToString());
                    sleeve.LookupParameter("穿越構件").Set("穿牆");
                    sleeve.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("MOVE-001");
                    _doc.Regenerate();
                    ElementTransformUtils.MoveElement(_doc, sleeve.Id, new XYZ(0,0,5) - Center(sleeve));
                });
                string identity = sleeve.UniqueId;
                var levelId = sleeve.LevelId;
                var pipes = new List<Element> { pipe };
                var sleeves = new List<FamilyInstance> { sleeve };
                List<object> Preview() => Items(Call("PipeSleeveService", "PreviewSelectedUpdate", _doc, pipes, sleeves, options));
                object Inspect() => Call("PipeSleeveManagerForm+PipeSleeveManagerRow", "From", _doc, sleeve);
                Tx("QA move source 600 mm and 300 mm up", () => ElementTransformUtils.MoveElement(_doc, pipe.Id, new XYZ(600/304.8,0,300/304.8)));
                var diagnostics = new List<string>();
                var candidates = Items(Call("PipeSleeveService", "Analyze", _doc, pipes, options, diagnostics));
                Check(candidates.Count >= 2 && diagnostics.Count > 0, "Movement fixture has real valid crossings plus unrelated incomplete crossing diagnostic");
                var preview = Preview().Single();
                Check((bool)Property(preview,"CanApply") && (double)Property(preview,"MoveMm") > 600,
                    "Selected sleeve remains updateable beyond old proximity limit despite unrelated host diagnostic");
                Check((string)Property(Inspect(),"Status") == "需更新", "Zero-connector family reports source movement instead of hiding it behind clearance warning");
                int count = new FilteredElementCollector(_doc).OfClass(typeof(FamilyInstance)).Count();
                Tx("QA apply source movement to original sleeve", () => Call("PipeSleeveService", "UpdateSelectedInPlace", _doc, pipes, sleeves, options));
                Check(sleeve.UniqueId == identity && sleeve.LevelId == levelId && Mark(sleeve.Id) == "MOVE-001" &&
                    new FilteredElementCollector(_doc).OfClass(typeof(FamilyInstance)).Count() == count &&
                    Center(sleeve).DistanceTo(new XYZ(600/304.8,0,5+300/304.8)) * 304.8 < .5,
                    "Update moves original sleeve to correct wall crossing and preserves identity, count, mark and level");
                Check((string)Property(Inspect(),"Status") == "待確認", "Aligned zero-connector family still requires clearance confirmation");
                ElementId duplicate = null;
                Tx("QA duplicate original sleeve", () => duplicate = ElementTransformUtils.CopyElement(_doc, sleeve.Id, XYZ.Zero).Single());
                Check(!(bool)Property(Preview().Single(),"CanApply"), "Unselected duplicate sleeve prevents ambiguous update");
                Tx("QA remove duplicate and move pipe outside wall", () => { _doc.Delete(duplicate); ElementTransformUtils.MoveElement(_doc, pipe.Id, new XYZ(30,0,0)); });
                Check(!(bool)Property(Preview().Single(),"CanApply"), "Source no longer crosses host blocks update and preserves original sleeve");
                XYZ before = Center(sleeve);
                bool failed = false;
                try { Tx("QA rejected update", () => Call("PipeSleeveService", "UpdateSelectedInPlace", _doc, pipes, sleeves, options)); }
                catch (TargetInvocationException) { failed = true; }
                Check(failed && sleeve.IsValidObject && Center(sleeve).DistanceTo(before) < 1e-9,
                    "Unresolved execution rejects without deleting or moving original sleeve");
                group.RollBack();
            }
        }
    }
}
