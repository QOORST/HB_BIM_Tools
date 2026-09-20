using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public partial class CmdMepPositionDimension : IExternalCommand
    {
        private static bool IsBeam(Element e) => e is FamilyInstance beam &&
            beam.StructuralType == Autodesk.Revit.DB.Structure.StructuralType.Beam &&
            (beam.Location as LocationCurve)?.Curve is Line;

        private static bool FindFace(GeometryElement geometry, Transform transform, string key, Document doc,
            out XYZ origin, out XYZ normal)
        {
            origin = normal = null;
            if (geometry == null) return false;
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid)
                    foreach (Face f in solid.Faces)
                        if (f is PlanarFace face && face.Reference != null &&
                            face.Reference.ConvertToStableRepresentation(doc) == key)
                        {
                            origin = transform.OfPoint(face.Origin);
                            normal = transform.OfVector(face.FaceNormal).Normalize();
                            return true;
                        }
                if (obj is GeometryInstance instance && FindFace(instance.GetSymbolGeometry(),
                    transform.Multiply(instance.Transform), key, doc, out origin, out normal)) return true;
            }
            return false;
        }
        private sealed class CurveFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => MepConnectionUi.Supported(e) && (e.Location as LocationCurve)?.Curve is Line;
            public bool AllowReference(Reference r, XYZ p) => false;
        }
        private sealed class DatumFilter : ISelectionFilter
        {
            private readonly Document _document;
            internal DatumFilter(Document document) { _document = document; }
            public bool AllowElement(Element e) => e is Grid || e is Wall || IsBeam(e) || e is RevitLinkInstance;
            public bool AllowReference(Reference r, XYZ p)
            {
                var element = _document.GetElement(r.ElementId);
                if (element is RevitLinkInstance link)
                {
                    var linkedDocument = link.GetLinkDocument();
                    if (linkedDocument == null || r.LinkedElementId == ElementId.InvalidElementId) return false;
                    element = linkedDocument.GetElement(r.LinkedElementId);
                }
                return element is Grid || element is Wall || IsBeam(element);
            }
        }
        private sealed class Item
        {
            internal Reference Reference;
            internal double Station;
            internal double Start, End;
            internal string Id;
        }
        private static decimal savedOffset = 10;
        private static decimal savedGap = 2000;
        private static string savedType;
        private static Document savedDocument;
        private static bool automaticBeam = true;
        private static string sourceLink;
        private static decimal searchDistance = 3000;
        private static decimal heightDistance = 1000;
        private static int objectMode = 4;
        private static decimal expandedDistance = 6000;
        private static bool staggerText = true;
        private static decimal textOffset = 3;
        private static string openingFamily;

        private static DimensionType Settings(Document doc, bool force)
        {
            if (savedDocument == null || !savedDocument.IsValidObject || !savedDocument.Equals(doc))
            { savedDocument = doc; savedType = null; sourceLink = null; openingFamily = null; }
            var types = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear).OrderBy(t => t.Name).ToList();
            var current = types.FirstOrDefault(t => t.UniqueId == savedType);
            bool sourceAvailable = sourceLink == null || (doc.GetElement(sourceLink) as RevitLinkInstance)?.GetLinkDocument() != null;
            if (!force && current != null && (!automaticBeam || sourceAvailable) &&
                (objectMode != 2 || (openingFamily != null && doc.GetElement(openingFamily) is Autodesk.Revit.DB.Family))) return current;
            if (types.Count == 0) throw new InvalidOperationException("找不到線性尺寸類型。");
            var typeBox = MepConnectionUi.Choices(types.Select(t => new MepConnectionUi.Choice { Text = t.Name, Value = t }));
            foreach (MepConnectionUi.Choice choice in typeBox.Items)
                if (((DimensionType)choice.Value).UniqueId == savedType) typeBox.SelectedItem = choice;
            var offset = new System.Windows.Forms.NumericUpDown { Minimum = 2, Maximum = 100, Value = savedOffset, Dock = System.Windows.Forms.DockStyle.Fill };
            var gap = new System.Windows.Forms.NumericUpDown { Minimum = 100, Maximum = 20000, Increment = 100, Value = savedGap, Dock = System.Windows.Forms.DockStyle.Fill };
            var mode = MepConnectionUi.Choices(new[] {
                new MepConnectionUi.Choice { Text = "自動梁側面", Value = true },
                new MepConnectionUi.Choice { Text = "手選基準", Value = false } });
            mode.SelectedIndex = automaticBeam ? 0 : 1;
            var sources = new List<MepConnectionUi.Choice> { new MepConnectionUi.Choice { Text = "本機模型", Value = null } };
            sources.AddRange(new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null).OrderBy(l => l.Name)
                .Select(l => new MepConnectionUi.Choice { Text = l.Name + " [" + l.Id + "]", Value = l.UniqueId }));
            var sourceBox = MepConnectionUi.Choices(sources);
            if (!sourceAvailable) sourceBox.SelectedIndex = -1;
            else sourceBox.SelectedIndex = Math.Max(0, sources.FindIndex(s => (string)s.Value == sourceLink));
            var distance = new System.Windows.Forms.NumericUpDown { Minimum = 100, Maximum = 20000, Increment = 100, Value = searchDistance, Dock = System.Windows.Forms.DockStyle.Fill };
            var height = new System.Windows.Forms.NumericUpDown { Minimum = 0, Maximum = 5000, Increment = 100, Value = heightDistance, Dock = System.Windows.Forms.DockStyle.Fill };
            var expanded = new System.Windows.Forms.NumericUpDown { Minimum = 100, Maximum = 30000, Increment = 100, Value = expandedDistance, Dock = System.Windows.Forms.DockStyle.Fill };
            var stagger = new System.Windows.Forms.CheckBox { Text = "重疊文字局部交錯", Checked = staggerText, AutoSize = true };
            var textGap = new System.Windows.Forms.NumericUpDown { Minimum = 1, Maximum = 20, DecimalPlaces = 1, Increment = .5m, Value = textOffset, Dock = System.Windows.Forms.DockStyle.Fill };
            textGap.Enabled = stagger.Checked;
            stagger.CheckedChanged += (s,e) => textGap.Enabled = stagger.Checked;
            var objects = MepConnectionUi.Choices(new[] { "管線", "套管／管附件", "開孔族", "設備", "自動辨識" }
                .Select((name,i) => new MepConnectionUi.Choice { Text = name, Value = i }));
            objects.SelectedIndex = objectMode;
            var openingChoices = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => s.Category != null && (s.Category.Id == new ElementId(BuiltInCategory.OST_GenericModel) || s.Category.Id == new ElementId(BuiltInCategory.OST_PipeAccessory)))
                .Select(s => s.Family).GroupBy(f => f.UniqueId).Select(g => g.First()).OrderBy(f => f.Name)
                .Select(f => new MepConnectionUi.Choice { Text = f.Name, Value = f.UniqueId }).ToList();
            var openingBox = MepConnectionUi.Choices(openingChoices);
            openingBox.SelectedIndex = openingChoices.FindIndex(c => (string)c.Value == openingFamily);
            Action objectChanged = () => { openingBox.Enabled = objects.SelectedIndex == 2 || objects.SelectedIndex == 4;
                if (objects.SelectedIndex != 0) mode.SelectedIndex = 0; mode.Enabled = objects.SelectedIndex == 0; };
            objects.SelectedIndexChanged += (s,e) => objectChanged(); objectChanged();
            Action enable = () => { bool on = mode.SelectedIndex == 0; sourceBox.Enabled = distance.Enabled = height.Enabled = expanded.Enabled = on; };
            mode.SelectedIndexChanged += (s,e) => enable(); enable();
            using (var form = MepConnectionUi.Form("HB_BIM｜管排定位設定", "尺寸類型", typeBox,
                "標註物件", objects, "開孔族", openingBox,
                "基準模式", mode, "參考模型", sourceBox, "搜尋距離 (mm)", distance,
                "文字排列", stagger, "擴大上限 (mm)", expanded, "高程搜尋差 (mm)", height,
                "尺寸線間距 (mm)", offset, "分組最大間距 (mm)", gap, "文字紙面偏移 (mm)", textGap))
            {
                form.Width = 650;
                ((System.Windows.Forms.Button)form.AcceptButton).Text = "套用設定";
                form.Height = Math.Min(560, System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea.Height - 40);
                MepConnectionUi.CollapseAdvanced(form, 7);
                if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
                if (mode.SelectedIndex == 0 && sourceBox.SelectedItem == null)
                    throw new InvalidOperationException("原參考連結未載入，請在定位尺寸設定中指定有效模型；不會自動改用本機。");
                if (objects.SelectedIndex == 2 && openingBox.SelectedItem == null)
                    throw new InvalidOperationException("請指定代表開孔的族，避免將一般模型全部視為開孔。");
                if (expanded.Value < distance.Value) throw new InvalidOperationException("擴大上限不得小於搜尋距離；兩者相同表示不擴大。");
                current = (DimensionType)((MepConnectionUi.Choice)typeBox.SelectedItem).Value;
                savedType = current.UniqueId; savedOffset = offset.Value; savedGap = gap.Value;
                automaticBeam = mode.SelectedIndex == 0;
                if (sourceBox.SelectedItem != null) sourceLink = (string)((MepConnectionUi.Choice)sourceBox.SelectedItem).Value;
                searchDistance = distance.Value; heightDistance = height.Value;
                expandedDistance = expanded.Value;
                staggerText = stagger.Checked; textOffset = textGap.Value;
                objectMode = objects.SelectedIndex;
                if (openingBox.SelectedItem != null) openingFamily = (string)((MepConnectionUi.Choice)openingBox.SelectedItem).Value;
            }
            return current;
        }

        internal static Result EditSettings(ExternalCommandData data)
        {
            var doc = data.Application.ActiveUIDocument?.Document;
            if (doc == null) return Result.Cancelled;
            try { return Settings(doc, true) == null ? Result.Cancelled : Result.Succeeded; }
            catch (Exception ex) { TaskDialog.Show("管排定位設定", ex.Message); return Result.Cancelled; }
        }

        private static string Signature(Document doc, IEnumerable<Reference> refs)
            => string.Join("\n", refs.Select(r => r.ConvertToStableRepresentation(doc)).OrderBy(s => s, StringComparer.Ordinal));

        private static double? ChooseSection(double start, double end, double spacing, IEnumerable<double> occupied, double? preferred = null)
        {
            if (end - start <= 1 / 304.8 || spacing <= 0) return null;
            double middle = (start + end) / 2;
            double margin = Math.Min(spacing, (end - start) / 4);
            double low = start + margin, high = end - margin;
            if (preferred.HasValue) middle = Math.Max(low, Math.Min(high, preferred.Value));
            var blocked = occupied.ToList();
            var candidates = new List<double> { middle, low, high };
            foreach (double position in blocked)
            { candidates.Add(position - spacing); candidates.Add(position + spacing); }
            foreach (double candidate in candidates.OrderBy(p => Math.Abs(p - middle)).ThenBy(p => p))
                if (candidate >= low && candidate <= high && blocked.All(p => Math.Abs(p - candidate) >= spacing - 1e-9))
                    return candidate;
            return null;
        }

        private static List<List<Item>> GroupRuns(List<Item> source, double gap)
        {
            var groups = new List<List<Item>>();
            foreach (var item in source.OrderBy(i => i.Station).ThenBy(i => i.Start).ThenBy(i => i.Id))
            {
                // Every member must cross the same section, not merely overlap a neighbour.
                var group = groups.FirstOrDefault(g => item.Station - g.Max(i => i.Station) <= gap &&
                    Math.Min(item.End, g.Min(i => i.End)) - Math.Max(item.Start, g.Max(i => i.Start)) > 1 / 304.8);
                if (group == null) groups.Add(new List<Item> { item });
                else group.Add(item);
            }
            return groups;
        }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || !(ui.ActiveView is ViewPlan)) return Result.Cancelled;
            var doc = ui.Document; var view = ui.ActiveView;
            try
            {
                var dimType = Settings(doc, false);
                if (dimType == null) return Result.Cancelled;
                if (objectMode == 4)
                {
                    var selected = ui.Selection.PickElementsByRectangle(new MixedFilter(), "框選管線、管附件、設備或已指定的開孔族");
                    var pipes = selected.Where(e => new CurveFilter().AllowElement(e)).ToList();
                    var families = selected.OfType<FamilyInstance>().ToList();
                    var pipeResult = pipes.Count > 0 ? ExecuteAutomatic(doc, view, pipes, dimType) : Result.Cancelled;
                    var familyResult = families.Count > 0 ? ExecuteFamilies(ui, dimType, families) : Result.Cancelled;
                    return pipeResult == Result.Succeeded || familyResult == Result.Succeeded ? Result.Succeeded : Result.Cancelled;
                }
                if (objectMode != 0) return ExecuteFamilies(ui, dimType);
                var curves = ui.Selection.PickElementsByRectangle(new CurveFilter(), "框選施工定位範圍內的管線").GroupBy(e => e.Id).Select(g => g.First()).ToList();
                if (curves.Count == 0) return Result.Cancelled;
                if (automaticBeam) return ExecuteAutomatic(doc, view, curves, dimType);
                Reference baseline = ui.Selection.PickObject(ObjectType.PointOnElement, new DatumFilter(doc), "選取本機或連結模型的軸線／牆側面／直梁側面；可按 Tab 切換參考");
                XYZ pickedPoint = baseline.GlobalPoint;
                var host = doc.GetElement(baseline);
                var linkInstance = host as RevitLinkInstance;
                Transform toHost = Transform.Identity;
                Reference geometryReference = baseline;
                if (linkInstance != null)
                {
                    var linkedDocument = linkInstance.GetLinkDocument();
                    if (linkedDocument == null) throw new InvalidOperationException("連結模型尚未載入。");
                    if (baseline.LinkedElementId == ElementId.InvalidElementId)
                        throw new InvalidOperationException("請選取連結模型內的軸線、牆側面或直梁側面，而非整個連結。");
                    host = linkedDocument.GetElement(baseline.LinkedElementId);
                    if (host == null) throw new InvalidOperationException("連結參考元素已不存在。");
                    toHost = linkInstance.GetTotalTransform();
                    geometryReference = baseline.CreateReferenceInLink();
                }
                XYZ normal = view.ViewDirection.Normalize();
                Func<XYZ, XYZ> projectVector = v => v - normal * v.DotProduct(normal);
                XYZ direction, anchor;
                if (IsBeam(host))
                {
                    var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
                    if (!FindFace(host.get_Geometry(options), toHost,
                        geometryReference.ConvertToStableRepresentation(host.Document), host.Document, out anchor, out XYZ faceNormal))
                        throw new InvalidOperationException("無法解析所選梁平面參考，請重新選取梁側面。");
                    var beamLine = (Line)((LocationCurve)host.Location).Curve;
                    var beamDirection = toHost.OfVector(beamLine.Direction).Normalize();
                    if (Math.Abs(faceNormal.DotProduct(normal)) > 1e-6 || Math.Abs(faceNormal.DotProduct(beamDirection)) > 1e-6)
                        throw new InvalidOperationException("請選取直梁垂直側面；梁端面、頂底面與傾斜面不作定位基準。");
                    direction = normal.CrossProduct(faceNormal).Normalize();
                    baseline = linkInstance == null ? geometryReference : geometryReference.CreateLinkReference(linkInstance);
                }
                else if (host is Grid grid && grid.Curve is Line gridLine)
                {
                    XYZ gridDirection = projectVector(toHost.OfVector(gridLine.Direction));
                    if (gridDirection.GetLength() < 1e-6) throw new InvalidOperationException("軸線無有效平面方向。");
                    direction = gridDirection.Normalize(); anchor = toHost.OfPoint(gridLine.GetEndPoint(0));
                    baseline = linkInstance == null ? new Reference(grid) : new Reference(grid).CreateLinkReference(linkInstance);
                }
                else if (host is Wall && host.GetGeometryObjectFromReference(geometryReference) is PlanarFace face)
                {
                    XYZ faceNormal = toHost.OfVector(face.FaceNormal).Normalize();
                    if (Math.Abs(faceNormal.DotProduct(normal)) >= 1e-6)
                        throw new InvalidOperationException("請選取垂直於目前視圖的平面牆側面。");
                    direction = normal.CrossProduct(faceNormal).Normalize(); anchor = toHost.OfPoint(face.Origin);
                    baseline = linkInstance == null ? geometryReference : geometryReference.CreateLinkReference(linkInstance);
                }
                else throw new InvalidOperationException("基準須為直線軸線、平面牆側面或直梁垂直側面；弧面與頂底面不支援。");
                XYZ measure = normal.CrossProduct(direction).Normalize();
                var datum = new Item { Reference = baseline, Station = anchor.DotProduct(measure) };
                var candidates = new List<Item>();
                var notes = new List<string>();
                foreach (var element in curves)
                {
                    var line = (Line)((LocationCurve)element.Location).Curve;
                    var planar = projectVector(line.Direction);
                    if (planar.GetLength() < 1e-6 || Math.Abs(planar.Normalize().DotProduct(direction)) < 0.999999)
                    { notes.Add($"{element.Id}：方向不符或為立管，需另選基準。"); continue; }
                    double a = line.GetEndPoint(0).DotProduct(direction), b = line.GetEndPoint(1).DotProduct(direction);
                    candidates.Add(new Item { Reference = new Reference(element), Station = line.Evaluate(.5, true).DotProduct(measure),
                        Start = Math.Min(a,b), End = Math.Max(a,b), Id = element.Id.ToString() });
                }
                var groups = GroupRuns(candidates, (double)savedGap / 304.8);
                var existingSignatures = new HashSet<string>(StringComparer.Ordinal);
                foreach (Dimension existing in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)))
                {
                    var refs = existing.References;
                    if (refs == null) continue;
                    try { existingSignatures.Add(Signature(doc, refs.Cast<Reference>())); }
                    catch { notes.Add($"既有尺寸 {existing.Id}：參考無法讀取，請檢查。"); }
                }
                var plans = new List<List<Item>>();
                var axes = new List<Line>();
                double offset = (double)savedOffset * view.Scale / 304.8;
                foreach (var group in groups)
                {
                    var items = group.Concat(new[] { datum }).OrderBy(i => i.Station).ToList();
                    string ids = string.Join(", ", group.Select(i => i.Id));
                    if (items.Zip(items.Skip(1), (a,b) => b.Station-a.Station).Any(d => d < .5/304.8))
                    { notes.Add($"{ids}：中心重疊或基準重合，整組略過。"); continue; }
                    if (existingSignatures.Contains(Signature(doc, items.Select(i => i.Reference))))
                    { notes.Add($"{ids}：已有相同尺寸，略過。"); continue; }
                    // Keep the entire chain on a section shared by every selected run.
                    var occupied = axes.Where(ax =>
                        ax.GetEndPoint(1).DotProduct(measure) >= items.First().Station &&
                        ax.GetEndPoint(0).DotProduct(measure) <= items.Last().Station)
                        .Select(ax => ax.GetEndPoint(0).DotProduct(direction));
                    double? section = ChooseSection(group.Max(i => i.Start), group.Min(i => i.End), offset, occupied,
                        pickedPoint?.DotProduct(direction));
                    if (!section.HasValue)
                    { notes.Add($"{ids}：共同直線區段內無足夠尺寸線間距，未向區段外推移。"); continue; }
                    double along = section.Value;
                    XYZ origin = normal * view.Origin.DotProduct(normal) + direction * along;
                    axes.Add(Line.CreateBound(origin + measure * items.First().Station, origin + measure * items.Last().Station));
                    plans.Add(items);
                }
                if (plans.Count == 0)
                { TaskDialog.Show("管排批次定位", "沒有可建立的尺寸。\n" + string.Join("\n", notes)); return Result.Cancelled; }
                var preview = new TaskDialog("管排批次定位｜確認") {
                    MainInstruction = $"預計建立 {plans.Count} 組尺寸",
                    MainContent = $"基準：{host.Name}（{(linkInstance == null ? "本機" : "連結模型")}）\n位置：管排共同直線區段內\n尺寸線紙面間距：{savedOffset} mm\n略過／待確認：{notes.Count} 項\n目前未檢查與既有文字或圖元的碰撞。",
                    ExpandedContent = string.Join("\n", plans.Select((p,i) => $"第 {i+1} 組：" + string.Join(", ", p.Where(x => x != datum).Select(x => x.Id))).Concat(notes)),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel };
                if (preview.Show() != TaskDialogResult.Ok) return Result.Cancelled;
                int created = 0;
                using (var batch = new TransactionGroup(doc, "管排批次施工定位"))
                {
                    batch.Start();
                    for (int index = 0; index < plans.Count; index++)
                    {
                        var items = plans[index];
                        using (var tx = new Transaction(doc, "管排定位尺寸"))
                        {
                            try
                            {
                                tx.Start();
                                var options = tx.GetFailureHandlingOptions();
                                options.SetFailuresPreprocessor(new RollBackErrors());
                                options.SetClearAfterRollback(true); tx.SetFailureHandlingOptions(options);
                                var refs = new ReferenceArray(); foreach (var item in items) refs.Append(item.Reference);
                                var dim = doc.Create.NewDimension(view, axes[index], refs, dimType);
                                doc.Regenerate();
                                if (dim == null || !dim.AreReferencesAvailable) throw new InvalidOperationException("無有效關聯參考。");
                                var values = dim.Segments.Size > 0 ? dim.Segments.Cast<DimensionSegment>().Select(s => s.Value).ToList() : new List<double?> { dim.Value };
                                var expected = items.Zip(items.Skip(1), (a,b) => b.Station-a.Station).ToList();
                                if (values.Count != expected.Count || values.Where((v,i) => !v.HasValue || Math.Abs(v.Value-expected[i]) > .5/304.8).Any())
                                    throw new InvalidOperationException("尺寸值與中心距不符。");
                                string layoutWarning = ArrangeDimensionText(doc, view, dim);
                                if (layoutWarning != null) notes.Add(layoutWarning);
                                if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 拒絕提交。");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                                notes.Add($"第 {index+1} 組未建立：{ex.Message}");
                            }
                        }
                    }
                    if (batch.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("批次未成功提交。");
                }
                if (notes.Count > 0) TaskDialog.Show("管排批次定位", $"建立 {created} 組。\n" + string.Join("\n", notes));
                return created > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch(Autodesk.Revit.Exceptions.OperationCanceledException){return Result.Cancelled;}
            catch(Exception ex){TaskDialog.Show("管線定位尺寸",ex.Message);return Result.Cancelled;}
        }
        private sealed class RollBackErrors : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
                => accessor.GetFailureMessages().Any(f => f.GetSeverity() == FailureSeverity.Error)
                    ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdMepPositionDimensionSettings : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => CmdMepPositionDimension.EditSettings(data);
    }
}
