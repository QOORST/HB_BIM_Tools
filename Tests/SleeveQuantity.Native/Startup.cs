using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace HB.SleeveQuantity.Native
{
    public sealed class Request { public string candidate { get; set; } public string output { get; set; } }
    public sealed partial class Startup : IExternalApplication
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly List<string> _checks = new List<string>();
        private Assembly _candidate;
        private string _output;
        private Document _doc;
        private int _step;
        private ElementId _first, _summary, _sheet;
        private ElementId _revisionSheet;
        private AddInId _addIn;
        private string _model;
        private int _undoQuantity;
        private ElementId _issueManual;
        private int _issueBeforeCount, _issueBeforeBaselines;
        private ExternalEvent _runEvent;
        private sealed class RunHandler : IExternalEventHandler
        {
            private readonly Startup _owner;
            public RunHandler(Startup owner) { _owner = owner; }
            public void Execute(UIApplication ui) { _owner.RunCore(ui); }
            public string GetName() => "HB sleeve isolated native QA";
        }
        public Result OnStartup(UIControlledApplication app)
        {
            _addIn = app.ActiveAddInId;
            _runEvent = ExternalEvent.Create(new RunHandler(this));
            app.Idling += Run;
            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication app) { app.Idling -= Run; return Result.Succeeded; }
        private Type T(string name) => _candidate.GetType("YD_RevitTools.LicenseManager.Commands.MEP." + name, true);
        private object Call(string type, string method, params object[] args) => T(type).GetMethod(method, Flags).Invoke(null, args);
        private object Options(string level = "", string zone = "", bool detail = false, IEnumerable<ElementId> manual = null, ElementId sheet = null)
        {
            var t = T("SleeveQuantityOptions"); var options = Activator.CreateInstance(t, true);
            void Set(string field, object value) => t.GetField(field, Flags).SetValue(options, value);
            Set("LevelKey", level); Set("LevelName", level.Length == 0 ? "整案" : "QA-L1"); Set("Zone", zone); Set("Detailed", detail);
            Set("ManualIds", new HashSet<long>((manual ?? Enumerable.Empty<ElementId>()).Select(id => id.Value)));
            Set("SheetId", sheet ?? ElementId.InvalidElementId);
            return options;
        }
        private Guid Param(int index) => (Guid)Call("SleeveQuantityData", "Id", index);
        private string Value(ElementId id, int index) => _doc.GetElement(id).get_Parameter(Param(index)).AsString();
        private int Members(ElementId schedule) => new FilteredElementCollector(_doc, schedule).OfClass(typeof(FamilyInstance)).Count();
        private ViewSchedule Build(object options) => (ViewSchedule)Call("SleeveQuantitySchedule", "CreateOrUpdate", _doc, options);
        private void Log(string message) { File.AppendAllText(Path.Combine(_output, "progress.log"), DateTime.Now.ToString("o") + " " + message + Environment.NewLine); }
        private void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); _checks.Add(name); Log("PASS " + name); }
        private void Tx(string name, Action body)
        {
            Log(name);
            using (var tx = new Transaction(_doc, name)) { tx.Start(); body(); if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Transaction failed: " + name); }
        }

        private void Run(object sender, IdlingEventArgs args)
        {
            // Document activation is forbidden inside API event handlers. Dispatch
            // out of Idling so startup add-ins cannot nest document activation here.
            if (!_runEvent.IsPending) _runEvent.Raise();
        }

        private void RunCore(UIApplication ui)
        {
            try
            {
                if (_step == 0)
                {
                    var requestPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "request.json");
                    if (!File.Exists(requestPath)) { ui.Idling -= Run; return; }
                    var request = new JavaScriptSerializer().Deserialize<Request>(File.ReadAllText(requestPath));
                    File.Delete(requestPath); _output = request.output; Directory.CreateDirectory(_output);
                    // Never operate on a model already opened by the user.
                    // Other startup add-ins may open documents. Never access or modify them;
                    // this harness operates only on the documents it creates below.
                    Log("Existing background documents: " + ui.Application.Documents.Size);
                    _candidate = Assembly.LoadFrom(request.candidate);
                    Log("Candidate: " + _candidate.FullName + " @ " + _candidate.Location);
                    Call("SleeveQuantityUpdater", "Register", _addIn);
                    CreateFixture(ui);
                    _model = Path.Combine(_output, "SleeveQuantity-QA.rvt");
                    _doc.SaveAs(_model, new SaveAsOptions { OverwriteExistingFile = false });
                    _doc.Close(false); _doc = ui.OpenAndActivateDocument(_model).Document;
                    RunChecks();
                    RunDefaultNumberChecks();
                    RunMovementChecks();
                    RunAxisChecks(ui);
                    RunRevisionChecks();
                    // A real Revit undo/redo, each verified on a later idle callback.
                    _undoQuantity = Members(_summary);
                    Tx("QA undo probe: exclude one sleeve", () => _doc.GetElement(_first).get_Parameter(Param(1)).Set("排除"));
                    Check(Members(_summary) == _undoQuantity - 1, "Exclusion updates native schedule in the source transaction");
                    _step = 1; ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo));
                    return;
                }
                if (_step == 1)
                {
                    Check(Members(_summary) == _undoQuantity, "Revit Undo restores quantities");
                    _step = 2; ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Redo)); return;
                }
                if (_step == 2)
                {
                    Check(Members(_summary) == _undoQuantity - 1, "Revit Redo reapplies quantities");
                    Tx("QA restore included", () => _doc.GetElement(_first).get_Parameter(Param(1)).Set("納入"));
                    RunIssueChecks();
                    _step = 3; ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); return;
                }
                if (_step == 3)
                {
                    Check(Members(_summary) == _issueBeforeCount && Value(_first, 1) == "納入" && string.IsNullOrEmpty(Value(_issueManual, 1)) &&
                        Items(Call("SleeveRevisionService", "List", _doc)).Count == _issueBeforeBaselines, "Single Undo restores entire issue operation including enrollment and baseline");
                    _step = 4; ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Redo)); return;
                }
                if (_step == 4)
                {
                    Check(Members(_summary) == _issueBeforeCount - 1 && Value(_first, 1) == "作廢" && Value(_issueManual, 1) == "排除" &&
                        Items(Call("SleeveRevisionService", "List", _doc)).Count == _issueBeforeBaselines + 1, "Single Redo restores complete issue operation");
                    _doc.Save();
                    var pdf = new PDFExportOptions { Combine = true, FileName = "SleeveQuantity-QA" };
                    Check(_doc.Export(_output, new List<ElementId> { _sheet, _revisionSheet }, pdf), "Native PDF export of quantity and revision tables");
                    Finish(ui, null);
                }
            }
            catch (Exception ex) { Finish(ui, ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex); }
        }

        private void CreateFixture(UIApplication ui)
        {
            string templates = @"C:\ProgramData\Autodesk\RVT 2024\Family Templates\English";
            string familyPath = Path.Combine(_output, "QA-Sleeve.rfa");
            using (var family = ui.Application.NewFamilyDocument(Path.Combine(templates, "Metric Generic Model.rft")))
            {
                using (var tx = new Transaction(family, "QA family"))
                {
                    tx.Start(); var fm = family.FamilyManager; fm.NewType("100A");
                    foreach (var pair in new[] { Tuple.Create("套管長度", 200.0), Tuple.Create("管外直徑", 114.3), Tuple.Create("有效內徑", 100.0) })
                        fm.Set(fm.AddParameter(pair.Item1, GroupTypeId.Geometry, SpecTypeId.Length, true), pair.Item2 / 304.8);
                    fm.Set(fm.AddParameter("材質", GroupTypeId.Data, SpecTypeId.String.Text, true), "鋼");
                    fm.Set(fm.AddParameter("套管編號", GroupTypeId.Data, SpecTypeId.String.Text, true), "1");
                    fm.AddParameter("來源管線Id", GroupTypeId.Data, SpecTypeId.String.Text, true);
                    fm.AddParameter("穿越構件", GroupTypeId.Data, SpecTypeId.String.Text, true);
                    var profile = new CurveArray(); var points = new[] {new XYZ(0,0,0), new XYZ(.3,0,0), new XYZ(.3,.3,0), new XYZ(0,.3,0)};
                    for (int i=0;i<4;i++) profile.Append(Line.CreateBound(points[i], points[(i+1)%4]));
                    var loops = new CurveArrArray(); loops.Append(profile);
                    family.FamilyCreate.NewExtrusion(true, loops, SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero)), .6);
                    tx.Commit();
                }
                family.SaveAs(familyPath); family.Close(false);
            }
            string titlePath = Path.Combine(_output, "QA-A1.rfa");
            using (var family = ui.Application.NewFamilyDocument(Path.Combine(templates, "Titleblocks", "A1 metric.rft")))
            { family.SaveAs(titlePath); family.Close(false); }
            _doc = ui.Application.NewProjectDocument(UnitSystem.Metric);
            var ids = new List<ElementId>(); Level level = null;
            Tx("QA fixture", () =>
            {
                _doc.LoadFamily(familyPath, out Family family); _doc.LoadFamily(titlePath, out Family title);
                var symbol = (FamilySymbol)_doc.GetElement(family.GetFamilySymbolIds().First()); symbol.Activate();
                level = Level.Create(_doc, 0); level.Name = "QA-L1";
                for (int i=0; i<4; i++) ids.Add(_doc.Create.NewFamilyInstance(new XYZ(i*2,0,1), symbol, level, StructuralType.NonStructural).Id);
                _first = ids[0];
                _doc.GetElement(ids[2]).LookupParameter("套管長度").Set(300 / 304.8);
                var sheet = ViewSheet.Create(_doc, title.GetFamilySymbolIds().First()); sheet.SheetNumber = "QA-M101"; sheet.Name = "套管數量表原生測試"; _sheet = sheet.Id;
            });
            Check(ids.All(id => (string)Call("SleeveStableNumbering", "Mark", _doc.GetElement(id)) == "1"), "Real family instances inherit default sleeve mark 1");
            Tx("QA number known-new defaults through placement organizer", () =>
                Call("PipeSleeveService", "OrganizeSleeveNumbers", _doc, Activator.CreateInstance(T("PipeSleeveOptions")), ids.Select(id => (FamilyInstance)_doc.GetElement(id)).ToList()));
            Check(ids.Select(Mark).Distinct().Count() == 4 && ids.All(id => Mark(id).StartsWith("PS-")), "Placement organizer numbers all proven-new default-1 instances without duplicate abort");
            Check(ids.All(id => _doc.GetElement(id).LookupParameter("套管編號").AsString() == Mark(id)), "Family number used by tags matches assigned Revit mark");
            var schedule = Build(Options(manual:ids)); _summary = schedule.Id;
            Check(Members(_summary) == 4, "Four manually enrolled native family instances counted");
            Check(Value(ids[2], 8) == "300", "Native length parameter normalized to millimetres");
            Check(Value(_first, 7).Contains("114.3"), "Actual sleeve diameter used in native schedule");
            Tx("QA zones", () => { foreach (var id in ids) _doc.GetElement(id).get_Parameter(Param(2)).Set(id == ids[3] ? "B" : "A"); });
            var scoped = Build(Options(level.UniqueId, "A")); Check(Members(scoped.Id) == 3, "Native level and zone filters agree with preview");
            var detail = Build(Options(detail:true)); Check(detail.Definition.IsItemized && Members(detail.Id) == 4, "Native itemized detail table");
            var placed = Build(Options(sheet:_sheet)); Check(placed.Id == _summary, "Sheet placement reuses managed schedule identity");
            var instance = Placements().Single(); XYZ oldPoint = instance.Point; ElementId oldId = instance.Id;
            var field = schedule.Definition.GetField(0); double width = field.SheetColumnWidth;
            Tx("QA custom column width", () => field.SheetColumnWidth = width + .02);
            Build(Options(sheet:_sheet));
            Check(Placements().Count == 1 && Placements()[0].Id == oldId && Placements()[0].Point.IsAlmostEqualTo(oldPoint), "Rerun preserves sheet instance and location");
            Check(Math.Abs(field.SheetColumnWidth - width - .02) < 1e-9, "Rerun preserves customized column width");
            Call("SleeveRevisionService", "Save", _doc, "QA-A");
        }
        private List<ScheduleSheetInstance> Placements() => new FilteredElementCollector(_doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
            .Where(i => i.ScheduleId == _summary && i.OwnerViewId == _sheet).ToList();
        private void RunChecks()
        {
            Check(Members(_summary) == 4 && Placements().Count == 1, "Save/reopen preserves schedules and placement");
            Tx("QA live size change", () => _doc.GetElement(_first).LookupParameter("套管長度").Set(450 / 304.8));
            Check(Value(_first, 8) == "450", "Registered updater synchronizes model edits after reopen");
            var before = new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)).Count();
            var invalid = Options(zone:"empty-zone", sheet:_sheet); T("SleeveQuantityOptions").GetField("LeftMm", Flags).SetValue(invalid, 5000.0);
            bool failed = false; try { Build(invalid); } catch (TargetInvocationException) { failed = true; }
            Check(failed && new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)).Count() == before, "Out-of-sheet placement rolls back new schedule");
            ElementId copy = null;
            Tx("QA duplicate instance", () => copy = ElementTransformUtils.CopyElement(_doc, _first, new XYZ(0,2,0)).Single());
            Check(Members(_summary) == 5, "Copied enrolled sleeve counted once");
            Tx("QA delete duplicate", () => _doc.Delete(copy));
            Check(Members(_summary) == 4, "Deleted sleeve removed from native count");
        }
        private void RunDefaultNumberChecks()
        {
            using(var group = new TransactionGroup(_doc,"QA temporary numbering defaults"))
            {
                group.Start();
                Tx("QA existing custom mark and unrelated element share 1", () =>
                {
                    var prototype=(FamilyInstance)_doc.GetElement(_first);
                    var level=new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().First();
                    var old=_doc.Create.NewFamilyInstance(new XYZ(20,0,0),prototype.Symbol,level,StructuralType.NonStructural);
                    old.get_Parameter(Param(1)).Set("納入");
                    var unrelated=_doc.Create.NewFamilyInstance(new XYZ(25,0,0),prototype.Symbol,level,StructuralType.NonStructural);
                    _doc.Regenerate();
                    Call("SleeveStableNumbering","Assign",_doc,new[]{old},null);
                    Check((string)Call("SleeveStableNumbering","Mark",old)=="1", "Existing custom sleeve mark unaffected by unmanaged family mark 1");
                    var a=_doc.Create.NewFamilyInstance(new XYZ(30,0,0),prototype.Symbol,level,StructuralType.NonStructural);
                    var b=_doc.Create.NewFamilyInstance(new XYZ(35,0,0),prototype.Symbol,level,StructuralType.NonStructural);
                    _doc.Regenerate();
                    Call("PipeSleeveService","OrganizeSleeveNumbers",_doc,Activator.CreateInstance(T("PipeSleeveOptions")),new[]{a,b});
                    Check(Mark(a.Id)!=Mark(b.Id) && Mark(a.Id).StartsWith("PS-") && (string)Call("SleeveStableNumbering","Mark",old)=="1",
                        "Mixed old custom mark and new default-1 sleeves preserve old mark and assign unique PS numbers");
                    Check((string)Call("SleeveStableNumbering","Mark",unrelated)=="1", "Unmanaged same-mark instance remains untouched");
                });
                group.RollBack();
            }
        }
        private object Property(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value);
        private List<object> Items(object values) => ((System.Collections.IEnumerable)values).Cast<object>().ToList();
        private string Mark(ElementId id) => _doc.GetElement(id).get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString();
        private void RunRevisionChecks()
        {
            var baselines = Items(Call("SleeveRevisionService", "List", _doc));
            Check(baselines.Count == 1 && (string)Property(baselines[0], "Name") == "QA-A", "Revision baseline persists after native save/reopen");
            var baseline = baselines[0];
            var ids = new FilteredElementCollector(_doc, _summary).OfClass(typeof(FamilyInstance)).ToElementIds().ToList();
            var oldMarks = ids.ToDictionary(id => id, Mark);
            Build(Options());
            Check(ids.All(id => Mark(id) == oldMarks[id]) && oldMarks.Values.Distinct().Count() == 4, "Stable numbering preserves all marks through rerun and reopen");
            ElementId copy = null; var other = ids.Where(id => id != _first).ToList();
            Tx("QA revision edits", () =>
            {
                ElementTransformUtils.MoveElement(_doc, _first, new XYZ(2 / 304.8, 0, 0));
                _doc.GetElement(_first).LookupParameter("套管長度").Set(470 / 304.8);
                copy = ElementTransformUtils.CopyElement(_doc, _first, new XYZ(0,2,0)).Single();
                _doc.Delete(other[0]);
                _doc.GetElement(other[1]).get_Parameter(Param(1)).Set("作廢");
            });
            Build(Options());
            Check(Mark(copy) != oldMarks[_first] && Mark(_first) == oldMarks[_first], "Copy receives a new number while original retains its number");
            var current = Call("SleeveRevisionService", "Capture", _doc, baseline);
            var changes = Items(Call("SleeveRevisionPolicy", "Compare", baseline, current));
            Check(changes.Count == 4 && changes.Sum(c => (int)Property(c, "QuantityDelta")) == -1 &&
                changes.Any(c => ((string)Property(c, "Changes")).Contains("移動")) &&
                changes.Any(c => ((string)Property(c, "Changes")) == "刪除"), "Native snapshot detects add/delete/void/move/size and conserves quantity");
            File.WriteAllText(Path.Combine(_output, "revision.csv"), (string)Call("SleeveRevisionPolicy", "Csv", baseline, current), new System.Text.UTF8Encoding(true));
            Tx("QA revision sheet", () =>
            {
                var title = new FilteredElementCollector(_doc, _sheet).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
                var sheet = ViewSheet.Create(_doc, title.GetTypeId()); sheet.SheetNumber = "QA-M102"; sheet.Name = "套管版本差量測試"; _revisionSheet = sheet.Id;
            });
            var views = Items(Call("SleeveRevisionDrawing", "Create", _doc, baseline, current, _revisionSheet));
            Check(views.Count == 1 && new FilteredElementCollector(_doc, _revisionSheet).OfClass(typeof(Viewport)).Count() == 1, "Revision drawing fits and is placed on a native sheet");
            bool failed = false;
            try { Call("SleeveRevisionService", "Save", _doc, "QA-A"); } catch (TargetInvocationException) { failed = true; }
            Check(failed && Items(Call("SleeveRevisionService", "List", _doc)).Count == 1, "Duplicate revision name cannot overwrite issued baseline");
            string copyMark = Mark(copy);
            Tx("QA remove newest number", () => { _doc.Delete(copy); _doc.GetElement(_first).get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(""); });
            Build(Options());
            Check(Mark(_first) == oldMarks[_first], "Cleared native mark restored from persisted ledger");
            Tx("QA copy after deletion", () => copy = ElementTransformUtils.CopyElement(_doc, _first, new XYZ(0,3,0)).Single());
            Build(Options());
            Check(long.Parse(Mark(copy).Substring(3)) > long.Parse(copyMark.Substring(3)), "Deleted highest number not recycled");
            Call("SleeveRevisionService", "Save", _doc, "QA-B");
            Check(Items(Call("SleeveRevisionService", "List", _doc)).Count == 2, "New baseline appends while prior baseline is retained");
            string validMark = Mark(copy);
            Tx("QA conflicting manual number", () => _doc.GetElement(copy).get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(oldMarks[_first]));
            failed = false; try { Build(Options()); } catch (TargetInvocationException) { failed = true; }
            Check(failed && Mark(_first) == oldMarks[_first], "Existing manual number conflict rolls back schedule update");
            Tx("QA restore copy mark", () => _doc.GetElement(copy).get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(validMark));
        }
        private object IssueRequest(string name, object options, Dictionary<long,string> controls)
        {
            var type = T("SleeveIssueRequest"); var request = Activator.CreateInstance(type, true);
            type.GetField("BaselineName", Flags).SetValue(request,name); type.GetField("Options",Flags).SetValue(request,options);
            type.GetField("Controls",Flags).SetValue(request,controls); return request;
        }
        private void RunIssueChecks()
        {
            Tx("QA prepare issue fixture", () =>
            {
                _issueManual = ElementTransformUtils.CopyElement(_doc,_first,new XYZ(0,4,0)).Single();
                _doc.GetElement(_issueManual).get_Parameter(Param(1)).Set("");
                _doc.GetElement(_first).get_Parameter(Param(2)).Set("ISSUE-QA");
            });
            _issueBeforeCount = Members(_summary); _issueBeforeBaselines = Items(Call("SleeveRevisionService","List",_doc)).Count;
            var other = new FilteredElementCollector(_doc,_summary).OfClass(typeof(FamilyInstance)).ToElementIds().First(id=>id!=_first);
            int schedulesBefore = new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)).Count();
            var badOptions = Options(zone:"ISSUE-QA", manual:new[]{_issueManual},sheet:_sheet);
            T("SleeveQuantityOptions").GetField("LeftMm",Flags).SetValue(badOptions,5000.0);
            var bad = IssueRequest("QA-ISSUE-FAIL",badOptions,new Dictionary<long,string>{{other.Value,"作廢"},{_issueManual.Value,"排除"}});
            bool failed=false; try {Call("SleeveIssueService","Execute",_doc,bad);} catch(TargetInvocationException) {failed=true;}
            Check(failed && Value(other,1)=="納入" && string.IsNullOrEmpty(Value(_issueManual,1)) && Members(_summary)==_issueBeforeCount &&
                new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)).Count()==schedulesBefore &&
                Items(Call("SleeveRevisionService","List",_doc)).Count==_issueBeforeBaselines, "Issue sheet failure rolls back enrollment, controls, schedules and baseline");
            var duplicate=IssueRequest("QA-A",Options(),new Dictionary<long,string>{{_first.Value,"作廢"}});
            failed=false; try {Call("SleeveIssueService","Execute",_doc,duplicate);}catch(TargetInvocationException){failed=true;}
            Check(failed && Value(_first,1)=="納入", "Issue duplicate baseline rejected before editing controls");
            var valid=IssueRequest("QA-ISSUE-R01",Options(manual:new[]{_issueManual},sheet:_sheet),
                new Dictionary<long,string>{{_first.Value,"作廢"},{_issueManual.Value,"排除"}});
            Call("SleeveIssueService","Execute",_doc,valid);
            Check(Value(_issueManual,1)=="排除" && Value(_first,1)=="作廢" && Members(_summary)==_issueBeforeCount-1,
                "Issue preserves explicit exclusion of manually enrolled sleeve");
            var baseline=Items(Call("SleeveRevisionService","List",_doc)).Single(b=>(string)Property(b,"Name")=="QA-ISSUE-R01");
            Check(Items(Property(baseline,"Records")).Count(r=>(bool)Property(r,"Included"))==Members(_summary), "Issue baseline matches final quantity table after control changes");
            Check(Placements().Count==1, "Issue reuses existing schedule sheet placement");
        }
        private void Finish(UIApplication ui, Exception error)
        {
            ui.Idling -= Run;
            if (_output == null) return;
            File.WriteAllText(Path.Combine(_output, "result.json"), new JavaScriptSerializer().Serialize(new
            { success = error == null, checks = _checks, error = error?.ToString(), candidate = _candidate?.Location, model = _model, completed = DateTime.Now.ToString("o") }));
            // Keep the new QA model open for inspection. Never close or save any user document.
        }
    }
}
