using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace HB.PumpFamilyBuilder
{
    public class Recipe { public List<Model> types { get; set; } }
    public class Model
    {
        public string assembly_model { get; set; }
        public Dictionary<string, double> assembly_dimensions { get; set; }
        public double pump_and_coupling_approximate_mass { get; set; }
        public double motor_hp { get; set; }
        public double catalog_motor_kw { get; set; }
    }

    // This application only acts on an explicit, one-shot request beside its DLL.
    public class Startup : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app) { app.Idling += RunOnce; return Result.Succeeded; }
        public Result OnShutdown(UIControlledApplication app) { return Result.Succeeded; }
        private void RunOnce(object sender, IdlingEventArgs e)
        {
            var ui = (UIApplication)sender;
            ui.Idling -= RunOnce;
            string caseTrigger = Path.Combine(Build.Bundle, "case.request.txt");
            if (File.Exists(caseTrigger))
            {
                string caseRequest = File.ReadAllText(caseTrigger).Trim();
                File.Delete(caseTrigger);
                string caseOutput = Path.Combine(Path.GetDirectoryName(caseRequest), "output");
                try { Build.RunRequest(ui.Application, caseRequest); }
                catch (Exception ex) { Directory.CreateDirectory(caseOutput); File.WriteAllText(Path.Combine(caseOutput, "failure.txt"), ex.ToString()); }
                finally
                {
                    string cleanup = Path.Combine(Build.Bundle, "case.manifest.txt");
                    if (File.Exists(cleanup))
                    {
                        string manifest = Path.GetFullPath(File.ReadAllText(cleanup).Trim());
                        string expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk/Revit/Addins/2024"));
                        if (Path.GetDirectoryName(manifest).Equals(expected, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(manifest).StartsWith("HB-EquipmentCase-", StringComparison.Ordinal) && Path.GetExtension(manifest)==".addin")
                            File.Delete(manifest);
                    }
                }
                return;
            }
            string request = Path.Combine(Build.Bundle, "run.request.txt");
            if (!File.Exists(request)) return;
            string output = File.ReadAllText(request).Trim();
            File.Delete(request);
            try { Build.Run(ui.Application, output); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                string output = Path.Combine(Build.Bundle, "output", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Build.Run(data.Application.Application, output);
                TaskDialog.Show("泵浦概念族群", "完成，請檢閱驗證報告：\n" + output);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.ToString(); return Result.Failed; }
        }
    }

    public class FailureGuard : IFailuresPreprocessor
    {
        public readonly List<string> Messages = new List<string>();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool error = false;
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                Messages.Add(failure.GetDescriptionText());
                if (failure.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(failure);
                else error = true;
            }
            return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }

    public sealed class Build
    {
        public static string Bundle => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static double Ft(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double Mm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        private Document doc;
        private FamilyManager fm;
        private ViewPlan plan;
        private readonly Dictionary<string, FamilyParameter> pars = new Dictionary<string, FamilyParameter>();
        private readonly List<string> warnings = new List<string>();
        private string output;
        private Extrusion pump, rail, flange;
        private ConnectorElement outlet;

        private FamilyParameter Length(string name, double mm, bool instance = false, string formula = null)
        {
            var p = fm.AddParameter(name, GroupTypeId.Geometry, SpecTypeId.Length, instance);
            pars.Add(name, p);
            fm.Set(p, Ft(mm));
            if (formula != null) fm.SetFormula(p, formula);
            return p;
        }
        private FamilyParameter Text(string name, string value)
        {
            var p = fm.AddParameter(name, GroupTypeId.IdentityData, SpecTypeId.String.Text, false);
            pars.Add(name, p); fm.Set(p, value); return p;
        }
        private void Transaction(string name, Action action)
        {
            File.AppendAllText(Path.Combine(output, "progress.log"), name + Environment.NewLine);
            using (var t = new Transaction(doc, name))
            {
                t.Start();
                var guard = new FailureGuard();
                t.SetFailureHandlingOptions(t.GetFailureHandlingOptions().SetFailuresPreprocessor(guard).SetClearAfterRollback(true));
                action();
                if (t.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException(name + ": " + string.Join("; ", guard.Messages));
                warnings.AddRange(guard.Messages);
            }
        }
        private void Associate(Element e, BuiltInParameter parameter, string name)
        {
            if (!fm.CanElementParameterBeAssociated(e.get_Parameter(parameter)))
                throw new InvalidOperationException("Cannot associate " + parameter + " to " + name);
            fm.AssociateElementParameterToFamilyParameter(e.get_Parameter(parameter), pars[name]);
        }
        private void Material(Element e, ElementId material)
        {
            e.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);
        }
        private Extrusion Extrude(CurveArray profile, double end, string startParam, string endParam, ElementId material)
        {
            var sketch = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
            var loops = new CurveArrArray(); loops.Append(profile);
            var ex = doc.FamilyCreate.NewExtrusion(true, loops, sketch, Ft(end));
            if (startParam != null) Associate(ex, BuiltInParameter.EXTRUSION_START_PARAM, startParam);
            if (endParam != null) Associate(ex, BuiltInParameter.EXTRUSION_END_PARAM, endParam);
            Material(ex, material); return ex;
        }
        private Extrusion Cylinder(double x, double y, double radius, double end, string start, string top, string radiusParam, ElementId mat)
        {
            var profile = new CurveArray();
            profile.Append(Arc.Create(new XYZ(Ft(x), Ft(y), 0), Ft(radius), 0, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
            var ex = Extrude(profile, end, start, top, mat);
            if (radiusParam != null)
            {
                doc.Regenerate();
                Curve circle = ex.Sketch.Profile.get_Item(0).get_Item(0);
                var dim = doc.FamilyCreate.NewRadialDimension(plan, circle.Reference, new XYZ(Ft(x + radius * 2), Ft(y), 0));
                dim.FamilyLabel = pars[radiusParam];
            }
            return ex;
        }
        private Extrusion Box(double x0, double y0, double x1, double y1, double end, string start, string top, ElementId mat)
        {
            var p = new[] { new XYZ(Ft(x0), Ft(y0), 0), new XYZ(Ft(x1), Ft(y0), 0), new XYZ(Ft(x1), Ft(y1), 0), new XYZ(Ft(x0), Ft(y1), 0) };
            var profile = new CurveArray();
            for (int i = 0; i < 4; i++) profile.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
            return Extrude(profile, end, start, top, mat);
        }
        private ElementId NewMaterial(string name, byte r, byte g, byte b)
        {
            var id = Autodesk.Revit.DB.Material.Create(doc, name);
            ((Autodesk.Revit.DB.Material)doc.GetElement(id)).Color = new Color(r, g, b); return id;
        }
        private ConnectorElement Connector => outlet;
        private void Geometry()
        {
            var iron = NewMaterial("概念_FC200鑄鐵", 55, 60, 65);
            var steel = NewMaterial("概念_SUS304", 175, 183, 190);
            Cylinder(0, 0, 81.5, 150, "底座上緣", "泵殼上緣", "泵體半徑", iron);
            pump = Cylinder(0, 0, 70, 426, "泵殼上緣", "馬達上緣", "馬達半徑", steel);
            Cylinder(0, 0, 81.5, 466, "馬達上緣", "頂蓋上緣", "泵體半徑", iron);
            Box(-35, -12, 35, 12, 496, "頂蓋上緣", "泵浦高度", iron);
            Box(-55, -55, -30, -30, 60, null, "底座上緣", iron);
            Box(30, 30, 55, 55, 60, null, "底座上緣", iron);
            Box(240, -70, 350, 70, 60, null, "底座上緣", iron);
            Box(65, -30, 303, 30, 170, "概念連接下緣", "概念連接上緣", iron);
            Cylinder(303, 0, 35, 243, "底座上緣", "出口高度", null, iron);
            flange = Cylinder(303, 0, 92.5, 243, "法蘭下緣", "出口高度", null, iron);
            rail = Cylinder(170, -35, 21, 1500, null, "導桿長度", "導桿半徑", steel);
            Cylinder(170, 35, 21, 1500, null, "導桿長度", "導桿半徑", steel);
            Box(130, -65, 210, 65, 1530, "導桿長度", "固定架上緣", iron);
            doc.Regenerate();
            var options = new Options { ComputeReferences = true };
            var face = flange.get_Geometry(options).OfType<Solid>().SelectMany(s => s.Faces.Cast<Face>()).OfType<PlanarFace>()
                .Where(f => f.FaceNormal.Z > 0.99).OrderByDescending(f => f.Origin.Z).First();
            outlet = ConnectorElement.CreatePipeConnector(doc, PipeSystemType.Sanitary, face.Reference);
            doc.Regenerate();
            if (fm.CanElementParameterBeAssociated(outlet.get_Parameter(BuiltInParameter.CONNECTOR_DIAMETER)))
                Associate(outlet, BuiltInParameter.CONNECTOR_DIAMETER, "出口名義直徑");
            else Associate(outlet, BuiltInParameter.CONNECTOR_RADIUS, "出口名義半徑");
            outlet.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION)?.Set("污廢水排放；名義口徑2英吋；概念族群");
            outlet.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_DIRECTION_PARAM)?.Set((int)FlowDirectionType.Out);
        }
        private void SetModel(Model model)
        {
            fm.Set(pars["泵浦高度"], Ft(model.assembly_dimensions["H"]));
            fm.Set(pars["泵體直徑"], Ft(model.assembly_dimensions["D"]));
            fm.Set(pars["出口高度"], Ft(model.assembly_dimensions["H3"]));
            fm.Set(pars["組合型號"], model.assembly_model);
            fm.Set(pars["原廠馬力_HP"], model.motor_hp);
            fm.Set(pars["型錄馬力欄_kW_非電氣負載"], model.catalog_motor_kw);
            fm.Set(pars["泵與著脫略重_kg"], model.pump_and_coupling_approximate_mass);
            foreach (var item in model.assembly_dimensions) fm.Set(pars["來源尺寸_" + item.Key], Ft(item.Value));
        }
        public class Receipt { public string file { get; set; } public string sha256 { get; set; } }
        public class Reviewed { public string reviewer { get; set; } public string note { get; set; } public bool accept_concept { get; set; } }
        public class Request
        {
            public int schema_version { get; set; }
            public string template_id { get; set; }
            public int target_revit { get; set; }
            public string recipe_file { get; set; }
            public string recipe_sha256 { get; set; }
            public string output_directory { get; set; }
            public Reviewed review { get; set; }
            public List<Receipt> source_receipts { get; set; }
        }
        private static string Hash(string file)
        {
            using(var sha=SHA256.Create()) using(var stream=File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static string PacketFile(string directory, string relative)
        {
            if(string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("工作包路徑必須為相對路徑。");
            string full=Path.GetFullPath(Path.Combine(directory,relative));
            if(!full.StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("工作包路徑超出範圍。");
            return full;
        }
        public static void RunRequest(Autodesk.Revit.ApplicationServices.Application app, string requestPath)
        {
            var request=new JavaScriptSerializer().Deserialize<Request>(File.ReadAllText(requestPath));
            if(request.schema_version!=1 || (request.template_id!="evergush_tos_ef_05_21" && request.template_id!="teco_ahu_hs") || request.target_revit!=2024 || request.output_directory!="output")
                throw new InvalidDataException("此工作包不在已支援範圍。");
            if(request.review==null || !request.review.accept_concept || string.IsNullOrWhiteSpace(request.review.reviewer) || string.IsNullOrWhiteSpace(request.review.note))
                throw new InvalidDataException("工作包尚未覆核。");
            string directory=Path.GetDirectoryName(Path.GetFullPath(requestPath));
            string recipe=PacketFile(directory,request.recipe_file);
            if(Hash(recipe)!=request.recipe_sha256)throw new InvalidDataException("尺寸資料雜湊不符，需重新覆核。");
            if(request.source_receipts==null || request.source_receipts.Count==0)throw new InvalidDataException("缺少來源圖資。");
            foreach(var receipt in request.source_receipts)
                if(Hash(PacketFile(directory,receipt.file))!=receipt.sha256)throw new InvalidDataException("來源圖資雜湊不符。");
            if(request.template_id=="teco_ahu_hs") {
                if(!request.source_receipts.Any(r=>r.sha256=="3f25f47263ba46ca7d39942828b761f061b5a8da5191e72dc794bc9146987e9e")) throw new InvalidDataException("空調箱原型錄缺失。");
                AhuBuild.Run(app,Path.Combine(directory,"output"),recipe);
            }
            else Run(app,Path.Combine(directory,"output"),recipe);
            File.Copy(requestPath,Path.Combine(directory,"output","build-request.used.json"),false);
        }

        public static void Run(Autodesk.Revit.ApplicationServices.Application app, string output, string recipeFile = null)
        {
            if (app.VersionNumber != "2024") throw new InvalidOperationException("此建族器僅可於 Revit 2024 執行。");
            Directory.CreateDirectory(output);
            if (Directory.EnumerateFiles(output, "*.rfa").Any()) throw new IOException("輸出目錄已有族群，請使用新目錄。");
            var recipe = new JavaScriptSerializer().Deserialize<Recipe>(File.ReadAllText(recipeFile ?? Path.Combine(Bundle, "pump-sample-001.input.json")));
            if (recipe?.types == null || recipe.types.Count != 2) throw new InvalidDataException("需要兩個樣本類型。");
            if (!new HashSet<string>(recipe.types.Select(m=>m.assembly_model)).SetEquals(new[]{"TOS-EF-05","TOS-EF-21"}))
                throw new InvalidDataException("本範本僅支援 TOS-EF-05／21 且不可重複。");
            // The v0.1 geometry has fixed XY positioning; reject a different catalog footprint.
            foreach (var m in recipe.types)
                if (m.assembly_dimensions["A"] != 477 || m.assembly_dimensions["A1"] != 303 || m.assembly_dimensions["D"] != 163 || m.assembly_dimensions["H3"] != 243)
                    throw new InvalidDataException("此初版僅支援已核對的 05/21 平面配置，不可套用其他系列。");
            var b = new Build { output = output };
            try
            {
                b.doc = app.NewFamilyDocument(@"C:\ProgramData\Autodesk\RVT 2024\Family Templates\English\Metric Mechanical Equipment.rft");
                b.fm = b.doc.FamilyManager;
                b.plan = new FilteredElementCollector(b.doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);
                b.Transaction("建立參數與概念幾何", () =>
                {
                    b.fm.NewType(recipe.types[0].assembly_model);
                    b.Length("泵浦高度", 496); b.Length("泵體直徑", 163); b.Length("出口高度", 243);
                    b.Length("導桿長度", 1500, true); b.Length("導桿概念外徑", 42);
                    b.Length("出口名義直徑", 50.8);
                    b.Length("泵體半徑", 81.5, false, "泵體直徑 / 2");
                    b.Length("馬達半徑", 70, false, "泵體直徑 * 0.43");
                    b.Length("導桿半徑", 21, false, "導桿概念外徑 / 2");
                    b.Length("出口名義半徑", 25.4, false, "出口名義直徑 / 2");
                    b.Length("底座上緣", 60, false, "60 mm"); b.Length("泵殼上緣", 150, false, "150 mm");
                    b.Length("馬達上緣", 426, false, "泵浦高度 - 70 mm");
                    b.Length("頂蓋上緣", 466, false, "泵浦高度 - 30 mm");
                    b.Length("法蘭下緣", 223, false, "出口高度 - 20 mm");
                    b.Length("固定架上緣", 1530, true, "導桿長度 + 30 mm");
                    b.Length("概念連接下緣", 120, false, "120 mm"); b.Length("概念連接上緣", 170, false, "170 mm");
                    foreach (var item in recipe.types[0].assembly_dimensions) b.Length("來源尺寸_" + item.Key, item.Value);
                    b.Text("組合型號", "TOS-EF-05");
                    b.Text("資料來源", "EVERGUSH EF 60Hz 202002 PDF 第4頁；本體性能第3頁");
                    b.Text("模型狀態", "概念模型：局部幾何簡化、電氣接頭未建立，不可作製造或最終碰撞依據");
                    b.Text("假設說明", "導桿長1500mm為試作值、外徑42mm未核定；馬達半徑比0.43；支架/把手/底座/連接段為簡化幾何");
                    b.Text("電源狀態", "60Hz；電壓/相數/輸入負載待確認");
                    foreach (string name in new[] { "原廠馬力_HP", "型錄馬力欄_kW_非電氣負載", "泵與著脫略重_kg" })
                        b.pars.Add(name, b.fm.AddParameter(name, GroupTypeId.IdentityData, SpecTypeId.Number, false));
                    b.SetModel(recipe.types[0]); b.Geometry();
                    b.fm.NewType(recipe.types[1].assembly_model); b.SetModel(recipe.types[1]);
                    b.doc.Regenerate();
                });
                b.Validate();
                b.Preview();
                string path = Path.Combine(output, "EVERGUSH_TOS-EF_05_21_CONCEPT_2024.rfa");
                b.doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = false, MaximumBackups = 1 });
                b.doc.Close(false); b.doc = null;
                using (var reopened = app.OpenDocumentFile(path))
                {
                    if (!reopened.IsFamilyDocument || reopened.FamilyManager.Types.Size != 2) throw new InvalidDataException("儲存後重開檢查失敗。");
                    reopened.Close(false);
                }
                string pipeTest;
                try { b.ProjectTest(app, path); pipeTest = "passed_two_types"; }
                catch (Exception ex) { pipeTest = "failed_or_unavailable"; File.WriteAllText(Path.Combine(output, "project-test-failure.txt"), ex.ToString()); }
                File.WriteAllText(Path.Combine(output, "result.json"), new JavaScriptSerializer().Serialize(new {
                    status = "family_saved_reopened", path, revit = app.VersionNumber,
                    warnings = b.warnings, electrical_connector = "not_created_pending_data",
                    project_pipe_connection = pipeTest, revit_2025_2026 = "not_tested",
                    limits = "Concept geometry; fixed XY arrangement; only height, diameter, discharge elevation and guide-rail controls flex-tested."
                }));
            }
            finally { if (b.doc != null && b.doc.IsValidObject) b.doc.Close(false); }
        }

        private void Preview()
        {
            View3D view = null;
            Transaction("建立概念模型預覽視圖", () => {
                var viewType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                view = View3D.CreateIsometric(doc, viewType.Id);
                view.Name = "概念模型預覽";
                view.DisplayStyle = DisplayStyle.Shading;
                view.DetailLevel = ViewDetailLevel.Fine;
                var forward = new XYZ(-1, 1, -0.6).Normalize();
                var right = forward.CrossProduct(XYZ.BasisZ).Normalize();
                view.SetOrientation(new ViewOrientation3D(new XYZ(8, -8, 6), right.CrossProduct(forward), forward));
                var references = new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Select(e => e.Id).ToList();
                if (references.Count > 0) view.HideElements(references);
                doc.Regenerate();
            });
            try {
                var options = new ImageExportOptions { ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(output, "preview"),
                    HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, PixelSize = 1400, FitDirection = FitDirectionType.Vertical, ZoomType = ZoomFitType.FitToPage };
                options.SetViewsAndSheets(new List<ElementId> { view.Id });
                doc.ExportImage(options);
            } catch (Exception ex) { warnings.Add("Preview export: " + ex.Message); }
        }

        private void ProjectTest(Autodesk.Revit.ApplicationServices.Application app, string familyPath)
        {
            Document project = null;
            try {
                project = app.NewProjectDocument(@"C:\ProgramData\Autodesk\RVT 2024\Templates\Traditional Chinese\Plumbing-DefaultTWNCHT.rte");
                Family family;
                using (var load = new Transaction(project, "載入樣本族群")) {
                    load.Start();
                    if (!project.LoadFamily(familyPath, out family)) throw new InvalidOperationException("測試專案無法載入族群。");
                    if (load.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("載入交易未完成。");
                }
                var level = new FilteredElementCollector(project).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).First();
                var pipeType = new FilteredElementCollector(project).OfClass(typeof(PipeType)).Cast<PipeType>().First();
                var checks = new List<object>();
                using (var t = new Transaction(project, "放置兩個類型並接管")) {
                    t.Start();
                    var guard = new FailureGuard();
                    t.SetFailureHandlingOptions(t.GetFailureHandlingOptions().SetFailuresPreprocessor(guard).SetClearAfterRollback(true));
                    int index = 0;
                    foreach (var id in family.GetFamilySymbolIds()) {
                        var symbol = (FamilySymbol)project.GetElement(id); symbol.Activate(); project.Regenerate();
                        var instance = project.Create.NewFamilyInstance(new XYZ(index++ * Ft(1000), 0, level.Elevation), symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        project.Regenerate();
                        var c = instance.MEPModel.ConnectorManager.Connectors.Cast<Connector>().Single();
                        var pipe = Pipe.Create(project, pipeType.Id, level.Id, c, c.Origin + XYZ.BasisZ * Ft(1000));
                        project.Regenerate();
                        bool connected = pipe.ConnectorManager.Connectors.Cast<Connector>().Any(pc => pc.IsConnectedTo(c));
                        if (!connected) throw new InvalidOperationException("建立管段後未實際連接。");
                        checks.Add(new { type = symbol.Name, connected, nominal_diameter_mm = Mm(c.Radius * 2), system = c.PipeSystemType.ToString() });
                    }
                    if(t.Commit() != TransactionStatus.Committed) throw new InvalidOperationException(string.Join(";", guard.Messages));
                    warnings.AddRange(guard.Messages);
                }
                project.SaveAs(Path.Combine(output, "Connection-QA-2024.rvt"), new SaveAsOptions { OverwriteExistingFile=false, MaximumBackups=1 });
                File.WriteAllText(Path.Combine(output, "connection-checks.json"), new JavaScriptSerializer().Serialize(checks));
            } finally { if(project != null && project.IsValidObject) project.Close(false); }
        }

        private void Validate()
        {
            var rows = new List<object>();
            foreach (FamilyType type in fm.Types)
            {
                Transaction("類型檢查 " + type.Name, () => { fm.CurrentType = type; doc.Regenerate(); });
                double expected = type.AsDouble(pars["出口高度"]).Value;
                var c = Connector;
                if (Math.Abs(c.Origin.Z - expected) > Ft(0.1) || c.CoordinateSystem.BasisZ.Z < 0.99)
                    throw new InvalidDataException("出口位置或方向不符。");
                rows.Add(new { type = type.Name, outlet_z_mm = Mm(c.Origin.Z), outlet_nominal_diameter_mm = Mm(c.Radius * 2) });
            }
            var initialOrigin = Connector.Origin;
            double initialH = fm.CurrentType.AsDouble(pars["泵浦高度"]).Value;
            double initialD = fm.CurrentType.AsDouble(pars["泵體直徑"]).Value;
            foreach (double length in new[] { 1000.0, 2000.0 })
                Transaction("導桿變形檢查 " + length, () => {
                    fm.Set(pars["導桿長度"], Ft(length)); doc.Regenerate();
                    if (Math.Abs(rail.get_BoundingBox(null).Max.Z - Ft(length)) > Ft(0.1) || Connector.Origin.DistanceTo(initialOrigin) > Ft(0.1))
                        throw new InvalidDataException("導桿變形或出口保持位置檢查失敗。");
                });
            Transaction("泵浦高度直徑變形檢查", () => {
                fm.Set(pars["泵浦高度"], Ft(600)); fm.Set(pars["泵體直徑"], Ft(180)); doc.Regenerate();
                var bounds = pump.get_BoundingBox(null);
                if (Math.Abs(bounds.Max.Z - Ft(530)) > Ft(0.1) || Math.Abs(bounds.Max.X - bounds.Min.X - Ft(180 * 0.86)) > Ft(0.1))
                    throw new InvalidDataException("泵浦尺寸變形檢查失敗。");
            });
            Transaction("出口高度變形檢查", () => {
                fm.Set(pars["出口高度"], Ft(280)); doc.Regenerate();
                if (Math.Abs(Connector.Origin.Z - Ft(280)) > Ft(0.1)) throw new InvalidDataException("出口高度未連動。");
            });
            Transaction("恢復型錄與試作初始值", () => {
                fm.Set(pars["導桿長度"], Ft(1500)); fm.Set(pars["泵浦高度"], initialH);
                fm.Set(pars["泵體直徑"], initialD); fm.Set(pars["出口高度"], Ft(243)); doc.Regenerate();
            });
            File.WriteAllText(Path.Combine(output, "geometry-checks.json"), new JavaScriptSerializer().Serialize(rows));
        }
    }
}
