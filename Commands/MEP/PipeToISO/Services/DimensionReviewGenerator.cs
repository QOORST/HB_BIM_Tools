using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    // 靜態核對詳圖：實際管中心線投影，管件接頭至中心以示意線呈現。
    internal sealed class DimensionReviewGenerator
    {
        private readonly Document doc;
        internal ReviewScene Scene { get; private set; }
        internal string LayoutSummary { get; private set; }
        internal DimensionReviewGenerator(Document document) { doc = document; }
        internal ReviewScene CaptureScene(ISOData data)
        {
            var rows = data.Snapshot.Rows.Where(r => r.Included).ToList();
            var geometry = new Dictionary<long, List<XYZ>>();
            foreach (var row in rows)
            {
                if (row.Element is Pipe pipe && pipe.Location is LocationCurve location)
                    geometry.Add(row.Id, location.Curve.Tessellate().ToList());
                else if (row.Element is FamilyInstance family)
                {
                    var connectors = family.MEPModel?.ConnectorManager?.Connectors;
                    var points = connectors?.Cast<Connector>().Where(c => c.Domain == Domain.DomainPiping &&
                        c.ConnectorType != ConnectorType.Logical).Select(c => c.Origin).ToList();
                    if (points == null || points.Count == 0)
                        throw new InvalidOperationException("元件 " + row.Id + " 無接頭位置，已取消核對詳圖。");
                    geometry.Add(row.Id, points);
                }
                else throw new InvalidOperationException("元件 " + row.Id + " 無可用幾何，已取消核對詳圖。");
            }
            var origin = geometry.Values.First().First();
            var right = new XYZ(1, -1, 0).Normalize();
            var up = new XYZ(-1, -1, 2).Normalize();
            XYZ Project(XYZ point) { var delta = point - origin; return new XYZ(delta.DotProduct(right), delta.DotProduct(up), 0); }
            ReviewPoint Point(XYZ p) => new ReviewPoint(UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters),
                UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters));
            var scene = new ReviewScene { Title = data.ISONumber + " 尺寸核對", DocumentTitle = doc.Title,
                SystemUniqueId = data.Snapshot.SystemUniqueId, CreatedAt = data.CreatedDate.ToString("o"), Evidence =
                "Revit 模型投影；文字尺寸為估計，建立詳圖時改用實測字框。" };
            foreach (var row in rows)
            {
                var points = geometry[row.Id].Select(Project).ToList();
                var anchor = points.Aggregate(XYZ.Zero, (a, p) => a + p) / points.Count;
                if (row.Element is Pipe)
                    for (int i = 1; i < points.Count; i++) scene.Lines.Add(new ReviewLine {
                        Owner = row.Code, A = Point(points[i - 1]), B = Point(points[i]) });
                else foreach (var point in points) scene.Lines.Add(new ReviewLine {
                    Owner = row.Code, A = Point(anchor), B = Point(point), Fitting = true });
                string text = row.Measurement != null ? row.Measurement.Label(row.Code) : row.Code;
                scene.Labels.Add(new ReviewLabel { Code = row.Code, Text = text, Anchor = Point(anchor),
                    Width = text.Split('\n').Max(s => s.Sum(c => c > 255 ? 1.0 : 0.65)) * 3.5 * scene.Scale,
                    Height = text.Split('\n').Length * 5.25 * scene.Scale });
            }
            Scene = scene;
            return scene;
        }
        internal ViewDrafting Create(ISOData data)
        {
            var scene = CaptureScene(data);
            double Mm(double value) => UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters);
            XYZ Internal(ReviewPoint p) => new XYZ(UnitUtils.ConvertToInternalUnits(p.X, UnitTypeId.Millimeters),
                UnitUtils.ConvertToInternalUnits(p.Y, UnitTypeId.Millimeters), 0);
            using (var tx = new Transaction(doc, "建立管路尺寸核對詳圖"))
            {
                tx.Start();
                try
                {
                    var familyType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
                    if (familyType == null) throw new InvalidOperationException("專案沒有製圖視圖類型。");
                    var view = ViewDrafting.Create(doc, familyType.Id);
                    string baseName = "ISO尺寸核對 - " + data.SystemName;
                    var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Select(v => v.Name));
                    string name = baseName;
                    for (int i = 1; names.Contains(name); i++) name = baseName + " (" + i + ")";
                    view.Name = name;
                    view.Scale = 50;
                    var originalType = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                    if (originalType == null) throw new InvalidOperationException("專案沒有文字類型。");
                    // 獨立文字類型，避免更動既有圖面文字。
                    var textType = (TextNoteType)originalType.Duplicate("HB_ISO_" + view.Id.GetIdValue());
                    textType.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(UnitUtils.ConvertToInternalUnits(3.5, UnitTypeId.Millimeters));
                    textType.get_Parameter(BuiltInParameter.LINE_COLOR)?.Set(0);
                    var options = new TextNoteOptions(textType.Id) { Rotation = 0, HorizontalAlignment = HorizontalTextAlignment.Left };
                    double Paper(double mm) => UnitUtils.ConvertToInternalUnits(mm * view.Scale, UnitTypeId.Millimeters);
                    int collapsed = 0;
                    foreach (var line in scene.Lines)
                        if (!Draw(view, Internal(line.A), Internal(line.B), line.Fitting)) collapsed++;
                    var notes = new Dictionary<string, TextNote>();
                    foreach (var label in scene.Labels)
                    {
                        var note = TextNote.Create(doc, view.Id, Internal(label.Anchor), label.Text, options);
                        notes.Add(label.Code, note);
                    }
                    doc.Regenerate();
                    foreach (var label in scene.Labels)
                    {
                        var note = notes[label.Code];
                        var box = note.get_BoundingBox(view);
                        if (box == null) throw new InvalidOperationException("無法取得核對文字範圍。");
                        label.Width = Mm(box.Max.X - box.Min.X);
                        label.Height = Mm(box.Max.Y - box.Min.Y);
                    }
                    scene.Evidence = "Revit 模型投影＋實際 TextNote 字框；可離線重播本次排版。";
                    List<ReviewPlacement> placements = null;
                    try { placements = ReviewLayout.Arrange(scene); }
                    catch (LayoutCrowdedException) { /* 保留原尺寸，改用編號索引重排。 */ }
                    if (placements == null || ReviewLayout.NeedsCompact(placements))
                    {
                        ReviewLayout.MakeCompact(scene);
                        foreach (var label in scene.Labels) notes[label.Code].Text = label.Text;
                        doc.Regenerate();
                        foreach (var label in scene.Labels)
                        {
                            var box = notes[label.Code].get_BoundingBox(view);
                            label.Width = Mm(box.Max.X - box.Min.X);
                            label.Height = Mm(box.Max.Y - box.Min.Y);
                        }
                        placements = ReviewLayout.Arrange(scene);
                    }
                    foreach (var placed in placements)
                    {
                        var note = notes[placed.Label.Code];
                        var box = note.get_BoundingBox(view);
                        var target = Internal(new ReviewPoint(placed.Box.Left, placed.Box.Bottom));
                        ElementTransformUtils.MoveElement(doc, note.Id, target - new XYZ(box.Min.X, box.Min.Y, 0));
                        // 固定的詳圖引線與離線版使用相同幾何，不依賴 Revit 自動接點。
                        Draw(view, Internal(placed.Leader.A), Internal(placed.Leader.B), true);
                    }
                    doc.Regenerate();
                    foreach (var placed in placements)
                    {
                        var actual = notes[placed.Label.Code].get_BoundingBox(view);
                        if (actual == null || Math.Abs(Mm(actual.Min.X) - placed.Box.Left) > 0.1 ||
                            Math.Abs(Mm(actual.Min.Y) - placed.Box.Bottom) > 0.1 ||
                            Math.Abs(Mm(actual.Max.X) - placed.Box.Right) > 0.1 ||
                            Math.Abs(Mm(actual.Max.Y) - placed.Box.Top) > 0.1)
                            throw new InvalidOperationException("Revit 文字範圍與離線排版不一致，已取消詳圖。");
                    }
                    LayoutSummary = $"{placements.Count} 個水平標籤；文字已避開管線；引線交叉候選 {placements.Sum(p => p.Crossings)} 處需核對";
                    var allPoints = scene.Lines.SelectMany(l => new[] { Internal(l.A), Internal(l.B) }).ToList();
                    double left = Math.Min(allPoints.Min(p => p.X), placements.Min(p => Internal(new ReviewPoint(p.Box.Left, 0)).X));
                    double top = Math.Max(allPoints.Max(p => p.Y), placements.Max(p => Internal(new ReviewPoint(0, p.Box.Top)).Y));
                    double bottom = Math.Min(allPoints.Min(p => p.Y), placements.Min(p => Internal(new ReviewPoint(0, p.Box.Bottom)).Y));
                    TextNote.Create(doc, view.Id, new XYZ(left, top + Paper(18), 0), data.ISONumber + "  尺寸核對", options);
                    var details = scene.Labels.Where(l => l.Detail != null).OrderBy(l => l.Code, StringComparer.Ordinal).ToList();
                    if (details.Count > 0)
                    {
                        int columns = (details.Count + 23) / 24;
                        int perColumn = (details.Count + columns - 1) / columns;
                        double tableTop = bottom - Paper(15), columnLeft = left;
                        for (int column = 0; column < columns; column++)
                        {
                            var detail = TextNote.Create(doc, view.Id, new XYZ(columnLeft, tableTop, 0),
                                $"密集區尺寸對照 {column + 1}/{columns}\n" + string.Join("\n", details.Skip(column * perColumn)
                                    .Take(perColumn).Select(l => l.Detail.Replace("\n", "；"))), options);
                            doc.Regenerate();
                            var box = detail.get_BoundingBox(view);
                            bottom = Math.Min(bottom, box.Min.Y);
                            columnLeft = box.Max.X + Paper(12);
                        }
                    }
                    TextNote.Create(doc, view.Id, new XYZ(left, bottom - Paper(15), 0),
                        "模型長度非加工切長；管件灰線為接頭示意，不代表管件形狀。\n" +
                        "P／E 後數字為 ElementId。此為靜態投影，模型修改後須重新生成。\n" +
                        "投影線不可直接量取施工尺寸；逐管端點與坡度請對照 _Pipes.csv。\n" +
                        $"投影重合或過短而未畫線：{collapsed} 段（元件標籤仍保留）。\n" +
                        LayoutSummary + "\n" +
                        "產生時間：" + data.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"), options);
                    // 圖框把字框、圖線及說明全部納入，避免 PNG 緊貼邊界。
                    doc.Regenerate();
                    var bounds = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()
                        .Select(e => e.get_BoundingBox(view)).Where(b => b != null).ToList();
                    double margin = Paper(12);
                    double x0 = bounds.Min(b => b.Min.X) - margin, x1 = bounds.Max(b => b.Max.X) + margin;
                    double y0 = bounds.Min(b => b.Min.Y) - margin, y1 = bounds.Max(b => b.Max.Y) + margin;
                    var frame = new[] { new XYZ(x0, y0, 0), new XYZ(x1, y0, 0), new XYZ(x1, y1, 0), new XYZ(x0, y1, 0) };
                    for (int i = 0; i < 4; i++) Draw(view, frame[i], frame[(i + 1) % 4], true);
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("核對詳圖交易未提交。");
                    return view;
                }
                catch { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); throw; }
            }
        }
        private bool Draw(View view, XYZ a, XYZ b, bool fitting)
        {
            if (a.DistanceTo(b) <= doc.Application.ShortCurveTolerance) return false;
            var line = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
            var style = new OverrideGraphicSettings();
            style.SetProjectionLineColor(fitting ? new Color(110, 110, 110) : new Color(0, 0, 0));
            style.SetProjectionLineWeight(fitting ? 2 : 4);
            view.SetElementOverrides(line.Id, style);
            return true;
        }
    }
}
