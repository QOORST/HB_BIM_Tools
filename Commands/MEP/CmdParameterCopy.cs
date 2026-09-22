using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using F = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdParameterCopy : IExternalCommand
    {
        private const double Tolerance = 1e-7;
        private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HB_BIM_Tools", "ParameterCopy.xml");
        private sealed class Choice
        {
            internal string Key, Name;
            public override string ToString() => Name;
        }
        private sealed class Row
        {
            internal Element Element;
            internal Parameter Source, Target;
            internal object Value;
            internal string Before, After;
            internal string Status;
        }
        private static bool Supported(Parameter p) => p.StorageType == StorageType.Double ||
            p.StorageType == StorageType.Integer || p.StorageType == StorageType.String;
        private static object Read(Parameter p) => p.StorageType == StorageType.String ? (object)(p.AsString() ?? "") :
            p.StorageType == StorageType.Integer ? (object)p.AsInteger() : p.AsDouble();
        private static bool Equal(Parameter p, object value) => p.HasValue &&
            (p.StorageType == StorageType.Double ? Math.Abs(p.AsDouble() - (double)value) <= Tolerance : Equals(Read(p), value));
        private static bool Write(Parameter p, object value) => p.StorageType == StorageType.String ? p.Set((string)value) :
            p.StorageType == StorageType.Integer ? p.Set((int)value) : p.Set((double)value);
        private static string Display(Parameter p) => p == null ? "缺少參數" : !p.HasValue ? "未設定" :
            p.StorageType == StorageType.String ? (string.IsNullOrEmpty(p.AsString()) ? "（空白）" : p.AsString()) :
            p.AsValueString() ?? Convert.ToString(Read(p), System.Globalization.CultureInfo.CurrentCulture);
        private static string Key(Parameter p) => p.IsShared ? "guid:" + p.GUID :
            p.Id.ToString().StartsWith("-", StringComparison.Ordinal) ? "builtin:" + p.Id : "parameter:" + p.Id;
        private static Parameter Resolve(Element e, string key)
        {
            var matches = e.Parameters.Cast<Parameter>().Where(p => Supported(p) && Key(p) == key).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
        private static Row Preview(Element e, Choice source, Choice target)
        {
            var row = new Row { Element = e, Source = Resolve(e, source.Key), Target = Resolve(e, target.Key) };
            row.Before = Display(row.Target); row.After = Display(row.Source);
            if (e is ElementType || e is RevitLinkInstance || e.Category == null || e.Category.CategoryType != CategoryType.Model) row.Status = "僅支援本機模型實例";
            else if (e.Pinned || e.GroupId != ElementId.InvalidElementId) row.Status = "釘住或位於群組";
            else if (row.Source == null || row.Target == null) row.Status = "缺少參數或識別不唯一";
            else if (row.Source.StorageType != row.Target.StorageType || row.Source.Definition.GetDataType() != row.Target.Definition.GetDataType()) row.Status = "資料型別或單位種類不同";
            else if (!row.Source.HasValue) row.Status = "來源無值";
            else if (row.Target.IsReadOnly) row.Status = "目標唯讀";
            else
            {
                row.Value = Read(row.Source);
                row.Status = Equal(row.Target, row.Value) ? "無變更" : "可複製";
            }
            return row;
        }

        // Capture actual world-space solid surfaces, not just the bounding box.
        private static void Geometry(GeometryElement geometry, Transform transform, List<XYZ> points, List<double> measures)
        {
            if (geometry == null) return;
            foreach (var item in geometry)
            {
                if (item is GeometryInstance instance)
                    Geometry(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), points, measures);
                else if (item is Solid solid && solid.Faces.Size > 0)
                {
                    measures.Add(solid.Volume);
                    measures.Add(solid.SurfaceArea);
                    foreach (Face face in solid.Faces)
                        foreach (XYZ vertex in face.Triangulate().Vertices) points.Add(transform.OfPoint(vertex));
                }
                else if (item is Mesh mesh)
                    foreach (XYZ vertex in mesh.Vertices) points.Add(transform.OfPoint(vertex));
                else if (item is Curve curve)
                    foreach (XYZ vertex in curve.Tessellate()) points.Add(transform.OfPoint(vertex));
            }
        }
        private sealed class Shape
        {
            internal string Identity;
            internal List<XYZ> Points = new List<XYZ>();
            internal List<double> Measures = new List<double>();
        }
        private static Shape Capture(Element e)
        {
            var result = new Shape { Identity = e.UniqueId + ":" + e.GetTypeId() + ":" + e.LevelId };
            Geometry(e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true }), Transform.Identity, result.Points, result.Measures);
            if (result.Points.Count == 0) throw new InvalidOperationException("無可驗證幾何，未寫入");
            if (e.Location is LocationPoint point) { result.Points.Add(point.Point); result.Measures.Add(point.Rotation); }
            if (e.Location is LocationCurve curve) result.Points.AddRange(curve.Curve.Tessellate());
            if (e is FamilyInstance family)
            {
                var t = family.GetTransform();
                result.Points.AddRange(new[] { t.Origin, t.BasisX, t.BasisY, t.BasisZ });
            }
            var ports = MepConnectionUi.Ports(e).OrderBy(p => p.Id).ToList();
            foreach (var port in ports)
            {
                result.Points.Add(port.Origin);
                result.Points.Add(port.CoordinateSystem.BasisZ);
                result.Identity += "|" + port.Id + ":" + string.Join(",", port.AllRefs.Cast<Connector>().Select(p => p.Owner.UniqueId + ":" + p.Id).OrderBy(s => s));
            }
            result.Identity += "|" + string.Join(",", e.GetDependentElements(null).Select(id => id.ToString()).OrderBy(s => s));
            return result;
        }
        private static bool Same(Shape a, Shape b) => a.Identity == b.Identity && a.Points.Count == b.Points.Count &&
            a.Measures.Count == b.Measures.Count && a.Points.Zip(b.Points, (x, y) => x.DistanceTo(y) <= Tolerance).All(v => v) &&
            a.Measures.Zip(b.Measures, (x, y) => Math.Abs(x - y) <= Tolerance * Math.Max(1, Math.Abs(x))).All(v => v);

        private static List<Element> Network(Element root)
        {
            var found = new Dictionary<string, Element>();
            var pending = new Queue<Element>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                var e = pending.Dequeue();
                if (found.ContainsKey(e.UniqueId)) continue;
                if (found.Count >= 1000) throw new InvalidOperationException("連接系統超過 1000 個元素，略過以保護模型");
                found.Add(e.UniqueId, e);
                foreach (var p in MepConnectionUi.Ports(e))
                    foreach (Connector other in p.AllRefs)
                        if (other.ConnectorType == ConnectorType.End && other.Owner.Id != e.Id) pending.Enqueue(other.Owner);
            }
            return found.Values.ToList();
        }
        private sealed class Failures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a) => a.GetFailureMessages().Count > 0
                ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
        private static void Apply(Document doc, Row row)
        {
            using (var guard = new TransactionGroup(doc, "參數複製幾何保護"))
            using (var tx = new Transaction(doc, "參數複製 " + row.Element.Id))
            {
                guard.Start();
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new Failures()).SetClearAfterRollback(true));
                try
                {
                    var network = Network(row.Element);
                    var original = network.Select(Capture).ToList();
                    if (!Write(row.Target, row.Value)) throw new InvalidOperationException("參數寫入失敗");
                    doc.Regenerate();
                    if (!Equal(row.Target, row.Value) || !network.Select(Capture).Zip(original, Same).All(v => v))
                        throw new InvalidOperationException("位置、形狀或連接改變，已回復");
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 檢核未通過，已回復");
                    if (!Equal(row.Target, row.Value) || !network.Select(Capture).Zip(original, Same).All(v => v))
                        throw new InvalidOperationException("提交後幾何改變，已回復");
                    if (guard.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("無法完成幾何保護提交");
                    row.Status = "已複製";
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    if (guard.GetStatus() == TransactionStatus.Started) guard.RollBack();
                    row.Status = ex.Message;
                }
            }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                if (!LicenseManager.Instance.HasFeatureAccess("Family.ParameterCopy")) throw new InvalidOperationException("授權未包含參數複製功能。");
                var selected = ui.Selection.GetElementIds().Select(ui.Document.GetElement).ToList();
                if (selected.Count == 0) selected = ui.Selection.PickObjects(ObjectType.Element, "選取要複製參數的本機元素").Select(ui.Document.GetElement).ToList();
                if (selected.Count == 0) return Result.Cancelled;
                selected = selected.Where(e => e != null).GroupBy(e => e.UniqueId).Select(g => g.First()).ToList();
                var choices = selected.SelectMany(e => e.Parameters.Cast<Parameter>()).Where(Supported)
                    .GroupBy(Key).Select(g => new Choice { Key = g.Key, Name = g.First().Definition.Name }).OrderBy(p => p.Name).ToList();
                foreach (var group in choices.GroupBy(c => c.Name).Where(g => g.Count() > 1))
                    foreach (var c in group) c.Name += " [" + c.Key + "]";
                Show(ui, selected, choices);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("參數複製", ex.Message); return Result.Failed; }
        }

        private static void Show(UIDocument ui, List<Element> selected, List<Choice> choices)
        {
            var doc = ui.Document;
            using (var form = new F.Form { Text = "HB_BIM | 參數複製", Width = 1050, Height = 690,
                MinimumSize = new System.Drawing.Size(800, 500), BackColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("Microsoft JhengHei UI", 10), AutoScaleMode = F.AutoScaleMode.Dpi,
                StartPosition = F.FormStartPosition.CenterScreen })
            {
                var layout = new F.TableLayoutPanel { Dock = F.DockStyle.Fill, Padding = new F.Padding(20), ColumnCount = 1, RowCount = 5 };
                layout.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent, 100));
                layout.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));
                layout.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));
                layout.RowStyles.Add(new F.RowStyle(F.SizeType.Percent, 100));
                layout.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));
                layout.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));
                var selectionPanel = new F.FlowLayoutPanel { AutoSize = true, Dock = F.DockStyle.Fill };
                var useSelection = new F.Button { Text = "使用目前選取", AutoSize = true, FlatStyle = F.FlatStyle.Flat };
                var reselect = new F.Button { Text = "重新選取", AutoSize = true, FlatStyle = F.FlatStyle.Flat };
                var selectionSummary = new F.Label { AutoSize = true, Margin = new F.Padding(8), MaximumSize = new System.Drawing.Size(500, 0) };
                selectionPanel.Controls.AddRange(new F.Control[] { useSelection, reselect, selectionSummary });
                layout.Controls.Add(selectionPanel);
                var fields = new F.TableLayoutPanel { Dock = F.DockStyle.Top, AutoSize = true, ColumnCount = 2 };
                fields.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent, 50));
                fields.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent, 50));
                var source = new F.ComboBox { Dock = F.DockStyle.Fill, DropDownStyle = F.ComboBoxStyle.DropDownList };
                var target = new F.ComboBox { Dock = F.DockStyle.Fill, DropDownStyle = F.ComboBoxStyle.DropDownList };
                source.DropDownWidth = 480; target.DropDownWidth = 480;
                source.Items.AddRange(choices.ToArray()); target.Items.AddRange(choices.ToArray());
                fields.Controls.Add(new F.Label { Text = "來源參數", AutoSize = true }, 0, 0);
                fields.Controls.Add(new F.Label { Text = "目標參數", AutoSize = true }, 1, 0);
                fields.Controls.Add(source, 0, 1); fields.Controls.Add(target, 1, 1);
                var remember = new F.CheckBox { Text = "記住本次設定", Checked = true, AutoSize = true, Margin = new F.Padding(3, 12, 3, 12) };
                fields.Controls.Add(remember, 0, 2);
                layout.Controls.Add(fields);
                var grid = new F.DataGridView { Dock = F.DockStyle.Fill, AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeRowsMode = F.DataGridViewAutoSizeRowsMode.AllCells,
                    BackgroundColor = System.Drawing.Color.White, BorderStyle = F.BorderStyle.FixedSingle,
                    AutoSizeColumnsMode = F.DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = F.DataGridViewSelectionMode.FullRowSelect };
                grid.ColumnHeadersHeightSizeMode = F.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
                grid.DefaultCellStyle.Padding = new F.Padding(4);
                foreach (string name in new[] { "元素 ID", "類型", "目前值", "寫入值", "狀態" }) grid.Columns.Add(name, name);
                grid.DefaultCellStyle.WrapMode = F.DataGridViewTriState.True;
                foreach (F.DataGridViewColumn column in grid.Columns) column.MinimumWidth = 100;
                foreach (F.DataGridViewColumn column in grid.Columns) { column.ReadOnly = true; column.SortMode = F.DataGridViewColumnSortMode.NotSortable; }
                grid.Columns[4].FillWeight = 180;
                grid.Columns.Insert(0, new F.DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "套用", Width = 55, MinimumWidth = 55, AutoSizeMode = F.DataGridViewAutoSizeColumnMode.None });
                layout.Controls.Add(grid);
                var summary = new F.Label { AutoSize = true, Dock = F.DockStyle.Fill, Padding = new F.Padding(0, 12, 0, 12) };
                layout.Controls.Add(summary);
                var footer = new F.FlowLayoutPanel { AutoSize = true, Dock = F.DockStyle.Fill, FlowDirection = F.FlowDirection.RightToLeft };
                var apply = new F.Button { Text = "套用複製", AutoSize = true, FlatStyle = F.FlatStyle.Flat,
                    BackColor = System.Drawing.Color.FromArgb(0, 105, 180), ForeColor = System.Drawing.Color.White };
                var close = new F.Button { Text = "關閉", AutoSize = true, DialogResult = F.DialogResult.Cancel, FlatStyle = F.FlatStyle.Flat };
                footer.Controls.Add(apply); footer.Controls.Add(close); layout.Controls.Add(footer); form.Controls.Add(layout); form.CancelButton = close;
                var rows = new List<Row>();
                var excluded = new HashSet<string>();
                bool refreshing = false;
                Action updateSummary = () =>
                {
                    int count = rows.Count(r => r.Status == "可複製" && !excluded.Contains(r.Element.UniqueId));
                    summary.Text = "勾選 " + count + " 個；可複製 " + rows.Count(r => r.Status == "可複製") +
                        " 個；無變更 " + rows.Count(r => r.Status == "無變更") + " 個；已複製 " + rows.Count(r => r.Status == "已複製") +
                        " 個；未通過 " + rows.Count(r => r.Status != "可複製" && r.Status != "無變更" && r.Status != "已複製") + " 個";
                    apply.Enabled = count > 0;
                };
                Action render = () =>
                {
                    refreshing = true;
                    grid.Rows.Clear();
                    foreach (var r in rows)
                    {
                        bool eligible = r.Status == "可複製";
                        int index = grid.Rows.Add(eligible && !excluded.Contains(r.Element.UniqueId), r.Element.Id.ToString(), r.Element.Name, r.Before, r.After, r.Status);
                        grid.Rows[index].Tag = r;
                        grid.Rows[index].Cells[0].ReadOnly = !eligible;
                        if (!eligible) grid.Rows[index].Cells[0].Style.BackColor = System.Drawing.Color.Gainsboro;
                    }
                    refreshing = false;
                    updateSummary();
                };
                grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(F.DataGridViewDataErrorContexts.Commit); };
                grid.CellValueChanged += (s, e) =>
                {
                    if (refreshing || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                    var r = grid.Rows[e.RowIndex].Tag as Row;
                    if (r == null || r.Status != "可複製") return;
                    if (Equals(grid.Rows[e.RowIndex].Cells[0].Value, true)) excluded.Remove(r.Element.UniqueId);
                    else excluded.Add(r.Element.UniqueId);
                    updateSummary();
                };
                Action preview = () => { if (refreshing) return;
                    if (source.SelectedItem == null || target.SelectedItem == null) { rows.Clear(); render(); return; }
                    rows = selected.Select(e => Preview(e, (Choice)source.SelectedItem, (Choice)target.SelectedItem)).ToList(); render(); };
                source.SelectedIndexChanged += (s, e) => preview(); target.SelectedIndexChanged += (s, e) => preview();
                string sourceKey = null, targetKey = null;
                try { var saved = XElement.Load(SettingsPath); sourceKey = (string)saved.Attribute("source"); targetKey = (string)saved.Attribute("target"); }
                catch (Exception) { /* Missing or invalid preferences use defaults. */ }
                source.SelectedItem = choices.FirstOrDefault(c => c.Key == sourceKey) ?? choices.FirstOrDefault(c => c.Name == "距離樓層的高程") ?? choices.FirstOrDefault();
                target.SelectedItem = choices.FirstOrDefault(c => c.Key == targetKey) ?? choices.FirstOrDefault(c => c.Name == "立面高程") ?? choices.Skip(1).FirstOrDefault();
                Action<List<Element>> replaceSelection = incoming =>
                {
                    selected = incoming.Where(e => e != null && e.IsValidObject).GroupBy(e => e.UniqueId).Select(g => g.First()).ToList();
                    var oldSource = source.SelectedItem as Choice;
                    var oldTarget = target.SelectedItem as Choice;
                    var available = selected.SelectMany(e => e.Parameters.Cast<Parameter>()).Where(Supported)
                        .GroupBy(Key).Select(g => new Choice { Key = g.Key, Name = g.First().Definition.Name }).ToList();
                    // Preserve a missing choice so changing the selection never silently changes the mapping.
                    foreach (var old in new[] { oldSource, oldTarget })
                        if (old != null && !available.Any(c => c.Key == old.Key)) available.Add(old);
                    refreshing = true;
                    source.Items.Clear(); target.Items.Clear();
                    choices = available.OrderBy(c => c.Name).ToList();
                    foreach (var group in choices.GroupBy(c => c.Name).Where(g => g.Count() > 1))
                        foreach (var c in group) c.Name += " [" + c.Key + "]";
                    source.Items.AddRange(choices.ToArray()); target.Items.AddRange(choices.ToArray());
                    source.SelectedItem = choices.FirstOrDefault(c => c.Key == oldSource?.Key);
                    target.SelectedItem = choices.FirstOrDefault(c => c.Key == oldTarget?.Key);
                    refreshing = false;
                    selectionSummary.Text = "已選取 " + selected.Count + " 個：" + string.Join("、", selected.GroupBy(e => e.Category?.Name ?? "其他").Select(g => g.Key + " " + g.Count()));
                    preview();
                };
                useSelection.Click += (s, e) =>
                {
                    try { replaceSelection(ui.Selection.GetElementIds().Select(doc.GetElement).ToList()); }
                    catch (Exception ex) { F.MessageBox.Show(form, ex.Message, "選取失敗"); }
                };
                reselect.Click += (s, e) => { form.DialogResult = F.DialogResult.Retry; };
                replaceSelection(selected);
                apply.Click += (s, e) =>
                {
                    try
                    {
                    grid.EndEdit();
                    preview();
                    var candidates = rows.Where(r => r.Status == "可複製" && !excluded.Contains(r.Element.UniqueId)).ToList();
                    if (candidates.Count == 0) return;
                    if (F.MessageBox.Show(form, "將寫入 " + candidates.Count + " 個元素；無法通過幾何檢核的項目會回復。", "確認參數複製",
                        F.MessageBoxButtons.OKCancel, F.MessageBoxIcon.Question, F.MessageBoxDefaultButton.Button2) != F.DialogResult.OK) return;
                    using (var group = new TransactionGroup(doc, "參數複製"))
                    {
                        group.Start();
                        foreach (var r in candidates) Apply(doc, r);
                        if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("參數複製未完成提交。");
                    }
                    render();
                    try
                    {
                        if (remember.Checked) { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                            new XElement("ParameterCopy", new XAttribute("source", ((Choice)source.SelectedItem).Key), new XAttribute("target", ((Choice)target.SelectedItem).Key)).Save(SettingsPath); }
                        else if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
                    }
                    catch (Exception ex) { F.MessageBox.Show(form, "模型處理完成，但無法儲存偏好：" + ex.Message); }
                    }
                    catch (Exception ex)
                    {
                        preview();
                        F.MessageBox.Show(form, "本次未完成，未提交的變更已回復：" + ex.Message, "參數複製");
                    }
                };
                // Exit the modal loop before Revit selection, keeping controls and choices alive.
                while (form.ShowDialog() == F.DialogResult.Retry)
                {
                    try
                    {
                        var picked = ui.Selection.PickObjects(ObjectType.Element, "選取要複製參數的本機元素；Esc 保留原清單");
                        replaceSelection(picked.Select(doc.GetElement).ToList());
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                    catch (Exception ex) { F.MessageBox.Show(ex.Message, "選取失敗"); }
                    form.DialogResult = F.DialogResult.None;
                }
            }
        }
    }
}
