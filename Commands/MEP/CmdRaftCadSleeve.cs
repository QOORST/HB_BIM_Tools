using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using F = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public sealed class RaftCadSettings
    {
        public string Purpose { get; set; } = "通氣管";
        public string FamilyType { get; set; } = "";
        public bool Top { get; set; } = true;
        public double OffsetMm { get; set; }
        public double LengthMm { get; set; } = 400;
        public int Count { get; set; } = 1;
        public double SpacingMm { get; set; } = 200;
    }

    internal static class RaftCadRecord
    {
        private static readonly Guid Id = new Guid("161af549-076a-41e8-af67-f00689cdb5a3");
        internal static Schema Schema()
        {
            var schema = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(Id);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(Id);
            builder.SetSchemaName("HBRaftCadSleeveV1");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            foreach (string field in new[] { "CadId", "Purpose", "Target", "Transform" }) builder.AddSimpleField(field, typeof(string));
            return builder.Finish();
        }
        internal static bool IsManaged(Element e)
        {
            if (e == null) return false;
            var schema = Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(Id);
            return schema != null && e.GetEntity(schema).IsValid();
        }
        internal static string TransformKey(ImportInstance cad)
        {
            Transform t = cad.GetTotalTransform();
            return string.Join("|", new[] { t.Origin, t.BasisX, t.BasisY, t.BasisZ }.SelectMany(p => new[] { p.X, p.Y, p.Z }).Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
        }
        internal static void Save(FamilyInstance sleeve, ImportInstance cad, string purpose, XYZ center)
        {
            var s = Schema(); var entity = new Entity(s);
            entity.Set(s.GetField("CadId"), cad.UniqueId);
            entity.Set(s.GetField("Purpose"), purpose);
            entity.Set(s.GetField("Target"), string.Join("|", new[] { center.X, center.Y, center.Z }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
            entity.Set(s.GetField("Transform"), TransformKey(cad));
            sleeve.SetEntity(entity);
        }
        internal static string Inspect(FamilyInstance sleeve, out string detail)
        {
            var s = Schema(); var e = sleeve.GetEntity(s);
            var cad = sleeve.Document.GetElement(e.Get<string>(s.GetField("CadId"))) as ImportInstance;
            detail = "CAD 定位／" + e.Get<string>(s.GetField("Purpose")) + "；未檢核宿主穿越、防火與防水條件。CAD 圖面內容更新需人工重新確認。";
            if (cad == null) { detail = "CAD 來源遺失；" + detail; return "來源遺失"; }
            if (TransformKey(cad) != e.Get<string>(s.GetField("Transform"))) { detail = "CAD 位置或方向已改變；" + detail; return "待確認"; }
            var ports = CmdRaftCadSleeve.Ports(sleeve);
            if (ports.Count != 2) return "待確認";
            var values = e.Get<string>(s.GetField("Target" )).Split('|').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            XYZ expected = new XYZ(values[0], values[1], values[2]);
            if (((ports[0].Origin + ports[1].Origin) * .5).DistanceTo(expected) > 1 / 304.8)
            { detail = "構件已偏離記錄定位點；" + detail; return "需處理"; }
            return "待確認";
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdRaftCadSleeve : IExternalCommand
    {
        private const double Mm = 1 / 304.8;
        internal static List<Connector> Ports(FamilyInstance f) => f.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>()
            .Where(c => c.ConnectorType == ConnectorType.End).ToList() ?? new List<Connector>();
        private static double Size(FamilyInstance f, params string[] names)
        {
            foreach (Element e in new Element[] { f, f.Symbol }) foreach (string name in names)
            {
                var p = e.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.Double && p.HasValue && p.AsDouble() > 0) return p.AsDouble();
            }
            return 0;
        }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            var doc = ui.Document;
            int created = 0;
            try
            {
                if (!LicenseManager.Instance.HasFeatureAccess("MEP.PipeSleeve")) throw new InvalidOperationException("授權未包含套管功能。");
                if (!(doc.ActiveView is ViewPlan)) throw new InvalidOperationException("請在平面視圖執行 CAD 定位。");
                var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(s => s.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased &&
                        (s.Category?.Id == new ElementId(BuiltInCategory.OST_PipeAccessory) || s.Category?.Id == new ElementId(BuiltInCategory.OST_GenericModel)))
                    .OrderBy(s => s.FamilyName).ThenBy(s => s.Name).ToList();
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
                var cads = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().Where(c => c.IsLinked).ToList();
                if (symbols.Count == 0 || levels.Count == 0 || cads.Count == 0) throw new InvalidOperationException("需要 CAD 連結、樓層及非宿主式單樓層套管族型。");
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HB_BIM_Tools", "RaftCadSleeve.xml");
                var serializer = new XmlSerializer(typeof(RaftCadSettings));
                var saved = new RaftCadSettings();
                if (File.Exists(path)) { try { using (var reader = File.OpenRead(path)) saved = (RaftCadSettings)serializer.Deserialize(reader); } catch { } }
                using (var form = new F.Form { Text = "HB_BIM｜筏基 CAD 定位", Width = 620, Height = 650, MinimumSize = new System.Drawing.Size(500, 400),
                    Font = new System.Drawing.Font("Microsoft JhengHei UI", 10), AutoScaleMode = F.AutoScaleMode.Dpi, StartPosition = F.FormStartPosition.CenterScreen, BackColor = System.Drawing.Color.White })
                {
                    var body = new F.TableLayoutPanel { Dock = F.DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new F.Padding(16) };
                    body.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.AutoSize)); body.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent, 100));
                    var footer = new F.FlowLayoutPanel { Dock = F.DockStyle.Bottom, AutoSize = true, FlowDirection = F.FlowDirection.RightToLeft, Padding = new F.Padding(12) };
                    var ok = new F.Button { Text = "開始定位", AutoSize = true, DialogResult = F.DialogResult.OK };
                    footer.Controls.Add(ok); footer.Controls.Add(new F.Button { Text = "取消", AutoSize = true, DialogResult = F.DialogResult.Cancel });
                    form.Controls.Add(body); form.Controls.Add(footer); form.AcceptButton = ok;
                    Action<string, F.Control> add = (caption, control) => { int row = body.RowCount++; body.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));
                        body.Controls.Add(new F.Label { Text = caption, AutoSize = true, Margin = new F.Padding(0, 9, 16, 9) }, 0, row);
                        control.Dock = F.DockStyle.Top; control.Margin = new F.Padding(0, 5, 0, 5); body.Controls.Add(control, 1, row); };
                    Func<IEnumerable<string>, F.ComboBox> combo = labels => { var c = new F.ComboBox { DropDownStyle = F.ComboBoxStyle.DropDownList }; c.Items.AddRange(labels.Cast<object>().ToArray()); c.SelectedIndex = 0; return c; };
                    Func<double, double, double, F.NumericUpDown> number = (value, min, max) => new F.NumericUpDown { DecimalPlaces = 1, Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Max(min, Math.Min(max, value)) };
                    var cadBox = combo(cads.Select(c => c.Name)); var family = combo(symbols.Select(s => s.FamilyName + ": " + s.Name));
                    int familyIndex = family.FindStringExact(saved.FamilyType); if (familyIndex >= 0) family.SelectedIndex = familyIndex;
                    var purpose = combo(new[] { "通氣管", "連通管", "溢水管" }); int purposeIndex = purpose.FindStringExact(saved.Purpose); if (purposeIndex >= 0) purpose.SelectedIndex = purposeIndex;
                    var level = combo(levels.Select(l => l.Name)); var mode = combo(new[] { "TOP：板下 − X", "BOP：大底完成面 ＋ Y" }); mode.SelectedIndex = saved.Top ? 0 : 1;
                    var datum = number(0, -1000000, 1000000); var offset = number(saved.OffsetMm, 0, 100000);
                    var length = number(saved.LengthMm, 1, 100000); var count = number(saved.Count, 1, 20); count.DecimalPlaces = 0;
                    var spacing = number(saved.SpacingMm, 1, 100000);
                    add("CAD 連結", cadBox); add("用途", purpose); add("套管族型", family); add("參考樓層", level); add("外緣定位", mode);
                    add("基準高程 mm（專案座標）", datum); add("X／Y 距離 mm", offset); add("套管長度 mm", length); add("支數", count); add("中心間距 mm", spacing);
                    if (form.ShowDialog() != F.DialogResult.OK) return Result.Cancelled;
                    var symbol = symbols[family.SelectedIndex]; var targetLevel = levels[level.SelectedIndex]; var cad = cads[cadBox.SelectedIndex];
                    double edge = (double)datum.Value * Mm + (mode.SelectedIndex == 0 ? -1 : 1) * (double)offset.Value * Mm;
                    if (TaskDialog.Show("確認高程基準", $"{purpose.Text}／{symbol.FamilyName}: {symbol.Name}\n套管外{(mode.SelectedIndex == 0 ? "頂" : "底")}高程：{edge / Mm:0.##} mm（專案座標）\n長度 {length.Value} mm，{count.Value} 支，中心間距 {spacing.Value} mm。\n基準為手動指定，尚未檢核宿主穿越。", TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel) != TaskDialogResult.Ok) return Result.Cancelled;
                    saved = new RaftCadSettings { Purpose = purpose.Text, FamilyType = family.Text, Top = mode.SelectedIndex == 0, OffsetMm = (double)offset.Value, LengthMm = (double)length.Value, Count = (int)count.Value, SpacingMm = (double)spacing.Value };
                    Directory.CreateDirectory(Path.GetDirectoryName(path)); using (var writer = File.Create(path)) serializer.Serialize(writer, saved);
                    while (true)
                    {
                        XYZ point = ui.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Intersections, "點選 CAD 套管中心；Esc 結束");
                        XYZ heading = ui.Selection.PickPoint("點選穿越方向；多支套管往左側排列");
                        XYZ direction = new XYZ(heading.X - point.X, heading.Y - point.Y, 0);
                        if (direction.GetLength() < Mm) throw new InvalidOperationException("方向兩點距離太短。");
                        direction = direction.Normalize(); XYZ side = XYZ.BasisZ.CrossProduct(direction);
                        using (var tx = new Transaction(doc, "筏基 CAD 定位建立"))
                        {
                            tx.Start(); if (!symbol.IsActive) symbol.Activate(); doc.Regenerate();
                            for (int i = 0; i < saved.Count; i++)
                            {
                                XYZ xy = point + side * (i * saved.SpacingMm * Mm);
                                var f = doc.Create.NewFamilyInstance(new XYZ(xy.X, xy.Y, edge), symbol, targetLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                var lp = new[] { "套管長度", "長度", "Length", "Sleeve Length" }.Select(n => f.LookupParameter(n)).FirstOrDefault(p => p != null && !p.IsReadOnly && p.StorageType == StorageType.Double);
                                if (lp == null || !lp.Set(saved.LengthMm * Mm)) throw new InvalidOperationException("族需提供可寫入的實例長度參數。");
                                doc.Regenerate(); var ports = Ports(f);
                                double diameter = Size(f, "管外直徑", "Outer Diameter", "Outside Diameter");
                                if (ports.Count != 2 || ports.Any(c => c.Shape != ConnectorProfileType.Round) || diameter <= 0)
                                    throw new InvalidOperationException("族需提供兩個圓形端接點及實際管外直徑；本批已取消。");
                                if (saved.Count > 1 && saved.SpacingMm * Mm <= diameter) throw new InvalidOperationException("中心間距必須大於套管外徑。");
                                XYZ center = (ports[0].Origin + ports[1].Origin) * .5;
                                XYZ axis = (ports[1].Origin - ports[0].Origin).Normalize(); double angle = axis.AngleTo(direction);
                                XYZ rotation = axis.CrossProduct(direction);
                                if (angle > 1e-8) { if (rotation.GetLength() < 1e-8) rotation = XYZ.BasisZ; ElementTransformUtils.RotateElement(doc, f.Id, Line.CreateUnbound(center, rotation.Normalize()), angle); }
                                doc.Regenerate(); ports = Ports(f); center = (ports[0].Origin + ports[1].Origin) * .5;
                                XYZ target = new XYZ(xy.X, xy.Y, RaftCadElevation.Center(edge, diameter, saved.Top));
                                ElementTransformUtils.MoveElement(doc, f.Id, target - center); doc.Regenerate(); ports = Ports(f);
                                if (((ports[0].Origin + ports[1].Origin) * .5).DistanceTo(target) > Mm || Math.Abs(ports[0].Origin.DistanceTo(ports[1].Origin) - saved.LengthMm * Mm) > Mm)
                                    throw new InvalidOperationException("族幾何與長度／中心設定不一致，本批已取消。");
                                RaftCadRecord.Save(f, cad, saved.Purpose, target);
                                var mark = f.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                                if (mark != null && !mark.IsReadOnly) mark.Set("RC-" + f.Id.ToString());
                            }
                            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("模型未提交。");
                            created += saved.Count;
                        }
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return created > 0 ? Result.Succeeded : Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("筏基 CAD 定位", ex.Message); return Result.Failed; }
        }
    }
}
