using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using F = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class MepConnectionUi
    {
        internal sealed class Choice
        {
            internal object Value;
            internal string Text;
            public override string ToString() => Text;
        }
        internal static F.ComboBox Choices(IEnumerable<Choice> items)
        {
            var box = new F.ComboBox { DropDownStyle = F.ComboBoxStyle.DropDownList, Dock = F.DockStyle.Fill };
            box.Items.AddRange(items.Cast<object>().ToArray());
            if (box.Items.Count > 0) box.SelectedIndex = 0;
            return box;
        }
        internal static F.Form Form(string title, params object[] rows)
        {
            var form = new F.Form { Text = title, Width = 560, Height = 360,
                Font = new System.Drawing.Font("Microsoft JhengHei UI", 10),
                BackColor = System.Drawing.Color.White, StartPosition = F.FormStartPosition.CenterScreen,
                MinimizeBox = false, MaximizeBox = false, AutoScaleMode = F.AutoScaleMode.Dpi };
            var footer = new F.FlowLayoutPanel { Dock = F.DockStyle.Bottom, Height = 52,
                FlowDirection = F.FlowDirection.RightToLeft, Padding = new F.Padding(8) };
            var ok = new F.Button { Text = "確定", DialogResult = F.DialogResult.OK, AutoSize = true,
                FlatStyle = F.FlatStyle.Flat, BackColor = System.Drawing.Color.FromArgb(0, 105, 180), ForeColor = System.Drawing.Color.White };
            var cancel = new F.Button { Text = "取消", DialogResult = F.DialogResult.Cancel, AutoSize = true,
                FlatStyle = F.FlatStyle.Flat, BackColor = System.Drawing.Color.White };
            footer.Controls.Add(ok); footer.Controls.Add(cancel);
            var table = new F.TableLayoutPanel { Dock = F.DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new F.Padding(16) };
            table.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Absolute, 180));
            table.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent, 100));
            for (int i = 0; i < rows.Length; i += 2)
            {
                table.RowStyles.Add(new F.RowStyle(F.SizeType.Absolute, 48));
                table.Controls.Add(new F.Label { Text = (string)rows[i], AutoSize = true }, 0, i / 2);
                table.Controls.Add((F.Control)rows[i + 1], 1, i / 2);
            }
            form.Controls.Add(table); form.Controls.Add(footer); form.AcceptButton = ok; form.CancelButton = cancel;
            return form;
        }
        internal static void CollapseAdvanced(F.Form form, int firstAdvancedRow)
        {
            var table = form.Controls.OfType<F.TableLayoutPanel>().Single();
            int count = table.RowStyles.Count;
            for (int row = count - 1; row >= firstAdvancedRow; row--)
                for (int column = 0; column < 2; column++)
                    table.SetRow(table.GetControlFromPosition(column, row), row + 1);
            table.RowStyles.Insert(firstAdvancedRow, new F.RowStyle(F.SizeType.AutoSize));
            var toggle = new F.CheckBox { Text = "進階設定", AutoSize = true,
                Margin = new F.Padding(0, 8, 0, 8) };
            table.Controls.Add(toggle, 0, firstAdvancedRow);
            table.SetColumnSpan(toggle, 2);
            toggle.CheckedChanged += (sender, args) =>
            {
                table.SuspendLayout();
                for (int row = firstAdvancedRow + 1; row <= count; row++)
                {
                    table.RowStyles[row].Height = toggle.Checked ? 48 : 0;
                    for (int column = 0; column < 2; column++)
                        table.GetControlFromPosition(column, row).Visible = toggle.Checked;
                }
                table.ResumeLayout(true);
            };
            toggle.Checked = true;
            toggle.Checked = false;
        }
        internal static List<Connector> Ports(Element e)
        {
            var manager = (e as MEPCurve)?.ConnectorManager ?? (e as FamilyInstance)?.MEPModel?.ConnectorManager;
            return manager == null ? new List<Connector>() : manager.Connectors.Cast<Connector>()
                .Where(c => c.ConnectorType == ConnectorType.End).ToList();
        }
        internal static bool Supported(Element e) => e is Pipe || e is Duct || e is Conduit || e is CableTray;
        internal static double Mm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdMepFromConnector : IExternalCommand
    {
        private sealed class Preference
        {
            internal string TypeUniqueId;
            internal decimal Length;
        }
        private static Document cachedDocument;
        private static readonly Dictionary<string, Preference> preferences = new Dictionary<string, Preference>();
        private static string Key(Connector port) => port.Domain + ":" + port.Shape;
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => ExecuteCore(data, ref message, elements, false);

        internal static Result ExecuteCore(ExternalCommandData data, ref string message, ElementSet elements, bool forceSettings)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            var doc = ui.Document;
            if (cachedDocument == null || !cachedDocument.IsValidObject || !cachedDocument.Equals(doc))
            {
                preferences.Clear(); cachedDocument = doc;
            }
            try
            {
                var picked = ui.Selection.PickObject(ObjectType.Element, "選取配件或設備，再指定未連接接點");
                var owner = doc.GetElement(picked);
                var ports = MepConnectionUi.Ports(owner).Where(c => !c.IsConnected &&
                    (c.Domain == Domain.DomainPiping || c.Domain == Domain.DomainHvac || c.Domain == Domain.DomainCableTrayConduit))
                    .OrderBy(c => c.Origin.DistanceTo(picked.GlobalPoint ?? XYZ.Zero)).ToList();
                if (ports.Count == 0) throw new InvalidOperationException("所選元素沒有可用的未連接接點。");
                preferences.TryGetValue(Key(ports[0]), out var saved);
                var portBox = MepConnectionUi.Choices(ports.Select((p, i) => new MepConnectionUi.Choice {
                    Value = p, Text = $"接點 {i + 1}｜{p.Shape}｜({MepConnectionUi.Mm(p.Origin.X):0.#}, {MepConnectionUi.Mm(p.Origin.Y):0.#}, {MepConnectionUi.Mm(p.Origin.Z):0.#}) mm" }));
                var typeBox = MepConnectionUi.Choices(new MepConnectionUi.Choice[0]);
                Action refresh = () => {
                    var p = (Connector)((MepConnectionUi.Choice)portBox.SelectedItem).Value;
                    Type type = p.Domain == Domain.DomainPiping ? typeof(PipeType) : p.Domain == Domain.DomainHvac ? typeof(DuctType) :
                        p.Shape == ConnectorProfileType.Round ? typeof(ConduitType) : typeof(CableTrayType);
                    typeBox.Items.Clear();
                    foreach (var t in new FilteredElementCollector(doc).OfClass(type).Cast<ElementType>().OrderBy(t => t.Name))
                        typeBox.Items.Add(new MepConnectionUi.Choice { Value = t.Id, Text = t.Name });
                    if (typeBox.Items.Count > 0) typeBox.SelectedIndex = 0;
                };
                portBox.SelectedIndexChanged += (_, __) => refresh(); refresh();
                var length = new F.NumericUpDown { Minimum = 10, Maximum = 100000, Value = 1000, Increment = 100, Dock = F.DockStyle.Fill };
                Action restore = () => {
                    var selected = (Connector)((MepConnectionUi.Choice)portBox.SelectedItem).Value;
                    if (!preferences.TryGetValue(Key(selected), out var setting)) return;
                    length.Value = setting.Length;
                    foreach (MepConnectionUi.Choice choice in typeBox.Items)
                        if (doc.GetElement((ElementId)choice.Value)?.UniqueId == setting.TypeUniqueId) typeBox.SelectedItem = choice;
                };
                portBox.SelectedIndexChanged += (_, __) => restore(); restore();
                bool validSaved = saved != null && typeBox.Items.Cast<MepConnectionUi.Choice>()
                    .Any(c => doc.GetElement((ElementId)c.Value)?.UniqueId == saved.TypeUniqueId);
                bool ambiguous = picked.GlobalPoint == null || (ports.Count > 1 &&
                    Math.Abs(ports[0].Origin.DistanceTo(picked.GlobalPoint) - ports[1].Origin.DistanceTo(picked.GlobalPoint)) < 1.0 / 304.8);
                if (forceSettings || !validSaved || ambiguous)
                {
                    using (var form = MepConnectionUi.Form("HB_BIM｜接點生成管", "未連接接點", portBox, "管線類型", typeBox, "長度（mm）", length))
                    {
                        ((F.Button)form.AcceptButton).Text = forceSettings ? "儲存設定" : "建立管段";
                        if (form.ShowDialog() != F.DialogResult.OK) return Result.Cancelled;
                    }
                }
                if (typeBox.SelectedItem == null) throw new InvalidOperationException("專案沒有可用的管線類型。");
                var source = (Connector)((MepConnectionUi.Choice)portBox.SelectedItem).Value;
                var typeId = (ElementId)((MepConnectionUi.Choice)typeBox.SelectedItem).Value;
                preferences[Key(source)] = new Preference { TypeUniqueId = doc.GetElement(typeId).UniqueId, Length = length.Value };
                if (forceSettings) return Result.Succeeded;
                var start = source.Origin;
                var end = start + source.CoordinateSystem.BasisZ.Normalize() * UnitUtils.ConvertToInternalUnits((double)length.Value, UnitTypeId.Millimeters);
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
                var level = levels.LastOrDefault(l => l.ProjectElevation <= start.Z) ?? levels.FirstOrDefault();
                if (level == null) throw new InvalidOperationException("專案沒有可用樓層。");
                using (var tx = new Transaction(doc, "接點生成管"))
                {
                    tx.Start();
                    MEPCurve curve;
                    if (source.Domain == Domain.DomainPiping) curve = Pipe.Create(doc, typeId, level.Id, source, end);
                    else if (source.Domain == Domain.DomainHvac) curve = Duct.Create(doc, typeId, level.Id, source, end);
                    else
                    {
                        curve = source.Shape == ConnectorProfileType.Round ? (MEPCurve)Conduit.Create(doc, typeId, start, end, level.Id) : CableTray.Create(doc, typeId, start, end, level.Id);
                        if (curve is Conduit) curve.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).Set(source.Radius * 2);
                        else
                        {
                            curve.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).Set(source.Width);
                            curve.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).Set(source.Height);
                            doc.Regenerate();
                            var near = MepConnectionUi.Ports(curve).OrderBy(c => c.Origin.DistanceTo(start)).First();
                            var axis = (end - start).Normalize();
                            double turn = near.CoordinateSystem.BasisX.AngleOnPlaneTo(source.CoordinateSystem.BasisX, axis);
                            ElementTransformUtils.RotateElement(doc, curve.Id, Line.CreateUnbound(start, axis), turn);
                        }
                        doc.Regenerate();
                        MepConnectionUi.Ports(curve).OrderBy(c => c.Origin.DistanceTo(start)).First().ConnectTo(source);
                    }
                    doc.Regenerate();
                    if (!MepConnectionUi.Ports(curve).Any(c => c.IsConnectedTo(source)))
                        throw new InvalidOperationException("接點未成功接通，本次生成已取消。");
                    if (source.Origin.DistanceTo(start) > 1e-6 || !(((LocationCurve)curve.Location).Curve.GetEndPoint(0).DistanceTo(end) < 1e-6 ||
                        ((LocationCurve)curve.Location).Curve.GetEndPoint(1).DistanceTo(end) < 1e-6))
                        throw new InvalidOperationException("生成位置與指定端點不一致，本次生成已取消。");
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("模型未成功提交。");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("接點生成管", ex.Message); return Result.Cancelled; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdMepFromConnectorSettings : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => CmdMepFromConnector.ExecuteCore(data, ref message, elements, true);
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdMepLevelRebase : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            var doc = ui.Document;
            try
            {
                var selected = ui.Selection.GetElementIds().Select(doc.GetElement).ToList();
                if (selected.Count == 0) selected = ui.Selection.PickObjects(ObjectType.Element, "選取要歸位的直線管段").Select(doc.GetElement).ToList();
                var curves = selected.Where(MepConnectionUi.Supported).Cast<MEPCurve>().ToList();
                if (curves.Count == 0) throw new InvalidOperationException("請選取至少一個可處理的直線管段。");
                if (curves.Count != selected.Count || curves.Any(c => c.Pinned || c.GroupId != ElementId.InvalidElementId || !((c.Location as LocationCurve)?.Curve is Line)))
                    throw new InvalidOperationException("僅處理未釘住、非群組的本機直線管段；請排除其他元素。");
                var allLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
                var filter = MepConnectionUi.Choices(new[] { "全部樓層", "建築樓層", "結構樓層" }.Select(s => new MepConnectionUi.Choice { Text = s, Value = s }));
                var target = MepConnectionUi.Choices(new MepConnectionUi.Choice[0]);
                Action refresh = () => {
                    target.Items.Clear(); target.Items.Add(new MepConnectionUi.Choice { Text = "自動：中點以下最近樓層", Value = null });
                    foreach (var l in allLevels.Where(l => filter.SelectedIndex == 0 ||
                        l.get_Parameter(filter.SelectedIndex == 1 ? BuiltInParameter.LEVEL_IS_BUILDING_STORY : BuiltInParameter.LEVEL_IS_STRUCTURAL)?.AsInteger() == 1))
                        target.Items.Add(new MepConnectionUi.Choice { Text = l.Name, Value = l });
                    target.SelectedIndex = 0;
                };
                filter.SelectedIndexChanged += (_, __) => refresh(); refresh();
                using (var form = MepConnectionUi.Form("HB_BIM｜MEP 樓層歸位", "樓層篩選", filter, "目標樓層", target))
                {
                    ((F.Button)form.AcceptButton).Text = "預覽歸位";
                    if (form.ShowDialog() != F.DialogResult.OK) return Result.Cancelled;
                }
                var eligible = target.Items.Cast<MepConnectionUi.Choice>().Select(x => x.Value).OfType<Level>().ToList();
                var fixedLevel = ((MepConnectionUi.Choice)target.SelectedItem).Value as Level;
                var plan = curves.Select(c => new { Curve = c, Level = fixedLevel ?? eligible.LastOrDefault(l => l.ProjectElevation <= ((LocationCurve)c.Location).Curve.Evaluate(.5, true).Z) }).ToList();
                if (plan.Any(p => p.Level == null || p.Curve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM) == null || p.Curve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM).IsReadOnly))
                    throw new InvalidOperationException("部分管段找不到適用樓層，或參考樓層不可寫入；未進行修改。");
                var preview = new TaskDialog("樓層歸位預覽") { MainInstruction = $"確認歸位 {plan.Count} 個管段？", MainContent = "維持實際位置與坡度；驗證不符時整批回復。",
                    ExpandedContent = string.Join("\n", plan.Select(p => $"{p.Curve.Id}：{p.Curve.ReferenceLevel?.Name ?? "未指定"} → {p.Level.Name}")),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel, DefaultButton = TaskDialogResult.Cancel };
                if (preview.Show() != TaskDialogResult.Ok) return Result.Cancelled;
                // Snapshot the connected network too: Revit can propagate a level edit to neighbours.
                var snapshots = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfClass(typeof(MEPCurve)).Cast<MEPCurve>()
                    .Select(c => new { Element = c, Shape = ((LocationCurve)c.Location).Curve.Clone(), Ports = MepConnectionUi.Ports(c).Select(p => new { p.Id, p.Origin,
                        Peers = string.Join(",", p.AllRefs.Cast<Connector>().Where(r => r.Owner.Id != c.Id && r.ConnectorType == ConnectorType.End).Select(r => r.Owner.Id + ":" + r.Id).OrderBy(x => x)) }).ToList() }).ToList();
                using (var tx = new Transaction(doc, "MEP 樓層歸位"))
                {
                    tx.Start();
                    foreach (var p in plan)
                    {
                        var original = ((LocationCurve)p.Curve.Location).Curve.Clone();
                        p.Curve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM).Set(p.Level.Id);
                        doc.Regenerate();
                        ((LocationCurve)p.Curve.Location).Curve = original;
                    }
                    doc.Regenerate();
                    foreach (var s in snapshots)
                    {
                        var current = ((LocationCurve)s.Element.Location).Curve;
                        if (new[] { 0.0, .5, 1.0 }.Any(t => current.Evaluate(t, true).DistanceTo(s.Shape.Evaluate(t, true)) > 1e-6))
                            throw new InvalidOperationException("偵測到管段位置或坡度改變，已整批回復。");
                        foreach (var old in s.Ports)
                        {
                            var now = MepConnectionUi.Ports(s.Element).FirstOrDefault(c => c.Id == old.Id);
                            if (now == null || now.Origin.DistanceTo(old.Origin) > 1e-6 || old.Peers != string.Join(",", now.AllRefs.Cast<Connector>().Where(r => r.Owner.Id != s.Element.Id && r.ConnectorType == ConnectorType.End).Select(r => r.Owner.Id + ":" + r.Id).OrderBy(x => x)))
                                throw new InvalidOperationException("偵測到連接狀態改變，已整批回復。");
                        }
                    }
                    if (plan.Any(p => p.Curve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM).AsElementId() != p.Level.Id)) throw new InvalidOperationException("參考樓層驗證失敗。");
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("模型未成功提交。");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("MEP 樓層歸位", ex.Message); return Result.Cancelled; }
        }
    }
}
