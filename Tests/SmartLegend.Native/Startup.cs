using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Newtonsoft.Json;
using YD_RevitTools.LicenseManager.Commands.Drawing;

namespace HB.SmartLegend.Native
{
    public sealed class Startup : IExternalApplication
    {
        private ExternalEvent _event;
        private Document _doc;
        private string _output;
        private int _stage;
        private readonly List<string> _checks = new List<string>();
        private sealed class Handler : IExternalEventHandler
        {
            internal Startup Owner;
            public void Execute(UIApplication app) => Owner.Run(app);
            public string GetName() => "HB isolated smart legend tests";
        }
        public Result OnStartup(UIControlledApplication app)
        {
            _output = Path.Combine(Path.GetDirectoryName(typeof(Startup).Assembly.Location), "output");
            Directory.CreateDirectory(_output);
            _event = ExternalEvent.Create(new Handler { Owner = this }); app.Idling += Idle;
            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication app) { app.Idling -= Idle; return Result.Succeeded; }
        private void Idle(object sender, IdlingEventArgs e) { if (!_event.IsPending) _event.Raise(); }
        private void Log(string value) => File.AppendAllText(Path.Combine(_output, "progress.log"), DateTime.Now.ToString("o") + " " + value + Environment.NewLine);
        private void Check(bool value, string name) { if (!value) throw new Exception(name); _checks.Add(name); Log("PASS " + name); }
        private void Tx(Action body) { using (var tx = new Transaction(_doc, "Isolated legend QA")) { tx.Start(); body(); if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Fixture transaction failed"); } }
        private void Case(Action body) { using (var group = new TransactionGroup(_doc, "QA case")) { group.Start(); body(); group.RollBack(); } }
        private void Good(LegendResult result, string name) => Check(result.Success, name + (result.Success ? "" : ": " + string.Join(" | ", result.Problems)));
        private sealed class WindowOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; set; }
        }
        private void Run(UIApplication ui)
        {
            try
            {
                var rerun = Path.Combine(_output, "rerun.txt");
                if (_stage >= 2 && File.Exists(rerun))
                {
                    var lines = File.ReadAllLines(rerun); File.Delete(rerun);
                    var assembly = System.Reflection.Assembly.LoadFrom(lines[0]);
                    assembly.GetType(typeof(Startup).FullName).GetMethod("RunAgain").Invoke(null, new object[] { ui, lines[1], lines[2] });
                    return;
                }
                if (_stage == 0)
                {
                    _stage = 1;
                    var seedFile = Path.Combine(Path.GetDirectoryName(typeof(Startup).Assembly.Location), "seed.rvt");
                    if (File.Exists(seedFile))
                    {
                        var copy = Path.Combine(_output, "SmartLegend-QA.rvt"); File.Copy(seedFile, copy);
                        _doc = ui.OpenAndActivateDocument(copy).Document;
                        File.WriteAllText(Path.Combine(_output, "continue.flag"), ""); return;
                    }
                    _doc = ui.Application.NewProjectDocument("C:/ProgramData/Autodesk/RVT 2024/Templates/Default_M_CHT.rte");
                    Tx(() =>
                    {
                        var wall = new FilteredElementCollector(_doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                        foreach (var suffix in new[] { "A", "B", "C" }) wall.Duplicate("HB QA " + suffix);
                    });
                    var model = Path.Combine(_output, "SmartLegend-QA.rvt");
                    _doc.SaveAs(model); _doc.Close(false); _doc = ui.OpenAndActivateDocument(model).Document;
                    Log("Created isolated fixture. Awaiting UI legend seed; no user document accessed.");
                    ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Legend));
                    return;
                }
                if (_stage == 1 && File.Exists(Path.Combine(_output, "continue.flag")))
                {
                    File.Delete(Path.Combine(_output, "continue.flag")); _stage = 2;
                    Checks(ui);
                    _doc.Save();
                    File.WriteAllText(Path.Combine(_output, "result.json"), JsonConvert.SerializeObject(new { success = true, checks = _checks }, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString()); File.WriteAllText(Path.Combine(_output, "result.json"), JsonConvert.SerializeObject(new { success = false, checks = _checks, error = ex.ToString() }, Formatting.Indented));
                _stage = 2;
            }
        }
        public static void RunAgain(UIApplication ui, string output, string seed)
        {
            Directory.CreateDirectory(output); var runner = new Startup { _output = output };
            try
            {
                var copy = Path.Combine(output, "SmartLegend-QA.rvt"); File.Copy(seed, copy);
                runner._doc = ui.OpenAndActivateDocument(copy).Document;
                var root = Path.GetFullPath(Path.GetDirectoryName(seed)) + Path.DirectorySeparatorChar;
                foreach (var previous in ui.Application.Documents.Cast<Document>().Where(d =>
                    !string.Equals(d.PathName, runner._doc.PathName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(d.PathName, ui.ActiveUIDocument.Document.PathName, StringComparison.OrdinalIgnoreCase) &&
                    d.PathName.StartsWith(root + "native-20261010-", StringComparison.OrdinalIgnoreCase) &&
                    d.PathName.EndsWith("\\output\\SmartLegend-QA.rvt", StringComparison.OrdinalIgnoreCase)).ToList()) previous.Close(false);
                runner.Checks(ui); runner._doc.Save();
                if (File.Exists(Path.Combine(output, "show-ui.flag"))) runner.ShowForm(ui);
                File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { success = true, checks = runner._checks }, Formatting.Indented));
            }
            catch (Exception ex)
            {
                runner.Log(ex.ToString()); File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { success = false, checks = runner._checks, error = ex.ToString() }, Formatting.Indented));
            }
        }
        private void ShowForm(UIApplication ui)
        {
            var types = new FilteredElementCollector(_doc).OfClass(typeof(WallType)).Cast<WallType>().Where(t => t.Name.StartsWith("HB QA ")).ToList();
            var plan = new FilteredElementCollector(_doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate && v.Name == "FL1");
            var ids = new List<ElementId>();
            Tx(() => { for (int i = 0; i < types.Count; i++) ids.Add(Wall.Create(_doc, Line.CreateBound(new XYZ(0, i * 12, 0), new XYZ(15, i * 12, 0)), types[i].Id, plan.GenLevel.Id, 10, 0, false, false).Id); });
            var uiDoc = ui.ActiveUIDocument; uiDoc.ActiveView = plan; uiDoc.Selection.SetElementIds(ids); _doc.Save();
            var count = new FilteredElementCollector(_doc).WhereElementIsNotElementType().GetElementCount();
            Log("Awaiting UI preview/cancel check");
            using (var form = new LegendForm(uiDoc))
            {
                form.ShowDialog(new WindowOwner { Handle = ui.MainWindowHandle });
                Check(form.ResultView == null && count == new FilteredElementCollector(_doc).WhereElementIsNotElementType().GetElementCount(), "UI preview and close leave model unchanged");
            }
        }
        private void Checks(UIApplication ui)
        {
            var template = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .Single(v => v.ViewType == ViewType.Legend && LegendService.Components(_doc, v).Count == 1);
            var textType = new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType)).FirstElement();
            var types = new FilteredElementCollector(_doc).OfClass(typeof(WallType)).Cast<WallType>().Where(t => t.Name.StartsWith("HB QA ")).OrderBy(t => t.Name).Select(t => t.UniqueId).ToList();
            var settings = new LegendSettings { Template = template.UniqueId, TextType = textType.UniqueId, Name = "HB 智慧圖例 QA", SymbolWidth = 100 };
            Tx(() => { template.Scale = 100; TextNote.Create(_doc, template.Id, new XYZ(-20, 20, 0), "人工標題保留", new TextNoteOptions(textType.Id)); });
            Case(() => Tx(() =>
            {
                var seed = LegendService.Components(_doc, template).Single();
                var info = new FilteredElementCollector(_doc).WhereElementIsNotElementType().ToDictionary(e => e.Id, e => e.GetType().FullName + ":" + e.Category?.Name + ":" + e.Name + ":type=" + e.GetTypeId());
                var deleted = _doc.Delete(seed.Id); Log("Seed delete dependency audit: " + string.Join(" | ", deleted.Select(id => id + ":" + info[id])));
            }));
            int Count() => new FilteredElementCollector(_doc).WhereElementIsNotElementType().GetElementCount();
            var before = Count();
            Good(LegendService.Run(_doc, settings, types, true), "Native preview succeeds");
            Check(Count() == before, "Preview leaves no view, text or component");
            var create = LegendService.Run(_doc, settings, types, false); Good(create, "Create three actual legend types");
            var view = (View)_doc.GetElement(create.View); settings.Target = view.UniqueId;
            var record = LegendService.Read(view);
            Check(record.Rows.Count == 3 && LegendService.Components(_doc, view).Count == 3, "Exactly one component per type; placeholder removed");
            Check(new FilteredElementCollector(_doc, view.Id).OfClass(typeof(TextNote)).Cast<TextNote>().Any(n => n.Text.Contains("人工標題保留")), "Template manual header retained");
            var ids = record.Rows.Select(r => r.Component + r.Note).ToArray(); before = Count();
            Good(LegendService.Run(_doc, settings, types, false), "Rerun succeeds");
            Check(before == Count() && ids.SequenceEqual(LegendService.Read(view).Rows.Select(r => r.Component + r.Note)), "Rerun keeps element IDs and counts");
            Case(() =>
            {
                Tx(() => { _doc.GetElement(types[0]).Name = "HB QA renamed"; _doc.GetElement(types[0]).get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).Set("新規格\n很長的規格文字，應自動換行並保留足夠的列高，不可以重疊。" + new string('長', 80)); });
                var result = LegendService.Run(_doc, settings, types, false); Good(result, "Rename and long specification update succeeds");
                Check(result.Changes.Count(c => c.Action == "更新") == 1, "Rename/specification recognized as update, not replacement");
            });
            Case(() =>
            {
                string manual = null;
                Tx(() => { manual = TextNote.Create(_doc, view.Id, new XYZ(-25, 15, 0), "後加人工註記", new TextNoteOptions(textType.Id)).UniqueId; });
                var result = LegendService.Run(_doc, settings, types.Skip(1).ToList(), false); Good(result, "Remove one managed row");
                Check(_doc.GetElement(record.Rows[0].Component) == null && _doc.GetElement(manual) != null, "Only removed owned row deleted; manual note retained");
            });
            Case(() =>
            {
                var row = LegendService.Read(view).Rows[0]; var component = _doc.GetElement(row.Component); var note = (TextNote)_doc.GetElement(row.Note);
                var delta = new XYZ(-30, 0, 0);
                Tx(() => { ElementTransformUtils.MoveElement(_doc, component.Id, delta); ElementTransformUtils.MoveElement(_doc, note.Id, delta); });
                var position = note.Coord;
                Good(LegendService.Run(_doc, settings, types, false), "Manual movement survives update");
                Check(position.DistanceTo(note.Coord) < 1e-8, "Manual text position retained exactly");
                Good(LegendService.Run(_doc, settings, types, false), "Manual movement remains stable on second update");
            });
            Case(() =>
            {
                var note = (TextNote)_doc.GetElement(record.Rows[0].Note); Tx(() => note.Text = "人工改寫"); before = Count();
                Check(!LegendService.Run(_doc, settings, types, false).Success && note.Text.Contains("人工改寫") && before == Count(), "Manual text edits block atomically and remain intact");
            });
            Case(() =>
            {
                var note = (TextNote)_doc.GetElement(record.Rows[0].Note);
                Tx(() => { var text = note.GetFormattedText(); text.SetBoldStatus(new TextRange(0, 1), true); note.SetFormattedText(text); });
                Check(!LegendService.Run(_doc, settings, types, false).Success, "Manual rich text formatting is protected");
            });
            Case(() =>
            {
                Tx(() => _doc.Delete(_doc.GetElement(record.Rows[0].Note).Id)); before = Count();
                Check(!LegendService.Run(_doc, settings, types, false).Success && Count() == before, "Deleted owned text produces conflict without partial output");
            });
            Case(() =>
            {
                View copy = null; Tx(() => copy = (View)_doc.GetElement(view.Duplicate(ViewDuplicateOption.WithDetailing)));
                settings.Target = copy.UniqueId;
                Check(!LegendService.Run(_doc, settings, types, false).Success, "Copied managed view cannot reuse original ownership"); settings.Target = view.UniqueId;
            });
            Case(() =>
            {
                var oldWidth = settings.SymbolWidth; settings.SymbolWidth = 1; before = Count();
                Check(!LegendService.Run(_doc, settings, types, false).Success && Count() == before, "Narrow symbol column rolls back completely"); settings.SymbolWidth = oldWidth;
            });
            Case(() =>
            {
                var invalid = types.Concat(new[] { textType.UniqueId }).ToList(); before = Count();
                Check(!LegendService.Run(_doc, settings, invalid, true).Success && Count() == before, "Unsupported type is reported without mutation");
            });
            Tx(() => _doc.LoadFamily("C:/HB/_BIM/HB_BIM_Tools/Resources/Families/套管-圓形_無.rfa"));
            var categoryEvidence = new List<object>();
            foreach (var category in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_PipeAccessory })
            {
                var sample = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(category).Cast<FamilySymbol>().FirstOrDefault();
                if (sample == null) { categoryEvidence.Add(new { category = category.ToString(), status = "no fixture" }); continue; }
                Case(() =>
                {
                    var options = new LegendSettings { Template = template.UniqueId, TextType = textType.UniqueId, Name = "QA category " + category, SymbolWidth = 100 };
                    var result = LegendService.Run(_doc, options, new[] { sample.UniqueId }, false);
                    categoryEvidence.Add(new { category = category.ToString(), sample = sample.FamilyName + ":" + sample.Name, success = result.Success, problems = result.Problems });
                    Good(result, "Native category example: " + category);
                    Check(LegendService.Read((View)_doc.GetElement(result.View)).Rows.Count == 1, "Category creates exactly one row: " + category);
                });
            }
            File.WriteAllText(Path.Combine(_output, "categories.json"), JsonConvert.SerializeObject(categoryEvidence, Formatting.Indented));
            var titleblock = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().FirstElementId();
            if (titleblock == ElementId.InvalidElementId)
            {
                Tx(() => _doc.LoadFamily("C:/ProgramData/Autodesk/RVT 2024/Libraries/Traditional Chinese/結構預鑄/圖框/雙牆 - A3.rfa"));
                titleblock = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().FirstElementId();
            }
            ViewSheet sheet = null; Tx(() => sheet = ViewSheet.Create(_doc, titleblock)); settings.Sheet = sheet.UniqueId;
            Good(LegendService.Run(_doc, settings, types, true), "Sheet preview checks actual viewport");
            Check(sheet.GetAllViewports().Count == 0, "Sheet preview does not leave viewport");
            Good(LegendService.Run(_doc, settings, types, false), "Actual placement on sheet");
            var vpId = sheet.GetAllViewports().Single();
            Good(LegendService.Run(_doc, settings, types, false), "Sheet placement rerun succeeds");
            Check(sheet.GetAllViewports().Single() == vpId, "Sheet rerun reuses viewport without duplicates");
            Case(() =>
            {
                var viewport = (Viewport)_doc.GetElement(vpId); Tx(() => viewport.SetBoxCenter(new XYZ(100, 100, 0)));
                var oldSheet = settings.Sheet; settings.Sheet = ""; before = Count();
                Check(!LegendService.Run(_doc, settings, types, false).Success && before == Count(), "Existing sheet overflow checked even without new placement"); settings.Sheet = oldSheet;
            });
            var export = new ImageExportOptions { ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(_output, "legend"),
                HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ZoomType = ZoomFitType.FitToPage, PixelSize = 1600 };
            export.SetViewsAndSheets(new[] { view.Id, sheet.Id }); _doc.ExportImage(export);
            ui.ActiveUIDocument.ActiveView = view;
        }
    }
}
