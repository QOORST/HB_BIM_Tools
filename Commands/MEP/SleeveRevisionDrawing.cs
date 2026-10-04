using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveRevisionDrawing
    {
        private const double Mm = 1.0 / 304.8;
        // Paper dimensions at view scale 1. TextNotes wrap; row height follows actual Revit text bounds.
        internal static List<ViewDrafting> Create(Document doc, SleeveRevisionSnapshot baseline, SleeveRevisionSnapshot current, ElementId sheetId)
        {
            var changes = SleeveRevisionPolicy.Compare(baseline, current);
            var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting) ?? throw new InvalidOperationException("找不到製圖視圖類型。");
            using (var tx = new Transaction(doc, "建立套管版本差量圖表"))
            {
                tx.Start();
                var textType = (TextNoteType)((TextNoteType)doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)))
                    .Duplicate("HB套管差量文字_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                textType.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(2 * Mm);
                var font = textType.get_Parameter(BuiltInParameter.TEXT_FONT);
                if (font != null && !font.IsReadOnly) font.Set("Microsoft JhengHei");
                var views = new List<ViewDrafting>();
                string run = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                ViewDrafting view = null; double y = 0;
                Action newPage = () =>
                {
                    view = ViewDrafting.Create(doc, type.Id); view.Scale = 1;
                    view.Name = "HB_套管版本差量_" + run + "_" + (views.Count + 1);
                    views.Add(view);
                    var heading = Add(doc, view, textType.Id, 0, 0, 380,
                        "套管版本差量｜基準：" + baseline.Name + "｜第 " + views.Count + " 頁\n" +
                        "比較時間：" + current.CapturedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") + "｜整案本機套管（含排除／作廢；不含連結模型套管）\n" +
                        "基準納入 " + baseline.Records.Count(r => r.Included) + " 支 → 目前納入 " + current.Records.Count(r => r.Included) +
                        " 支；淨增減 " + changes.Sum(r => r.QuantityDelta) + " 支；變更 " + changes.Count + " 筆\n" +
                        "固定快照；座標為內部原點 mm、樓層為約束樓層；待確認項目不等於核准施工量。移動容差 0.5 mm。");
                    doc.Regenerate(); y = heading.get_BoundingBox(view).Min.Y / Mm - 4;
                    foreach (var cell in Cells("編號／識別尾碼", "變更／支數增減", "基準內容", "目前內容"))
                        Add(doc, view, textType.Id, cell.Item1, y, cell.Item2, cell.Item3);
                    y -= 8;
                };
                newPage();
                foreach (var change in changes)
                {
                    bool placed = false;
                    for (int attempt = 0; attempt < 2 && !placed; attempt++)
                    {
                        var notes = Cells(change.Mark + "\n" + change.Id.Substring(Math.Max(0, change.Id.Length - 8)),
                            change.Changes + "\n支數 " + change.QuantityDelta, change.BeforeDetail, change.AfterDetail)
                            .Select(c => Add(doc, view, textType.Id, c.Item1, y, c.Item2, c.Item3)).ToList();
                        doc.Regenerate();
                        double bottom = notes.Min(n => n.get_BoundingBox(view).Min.Y / Mm);
                        if (bottom < -240)
                        {
                            foreach (var note in notes) doc.Delete(note.Id);
                            if (attempt == 1) throw new InvalidOperationException("單筆差量內容超過圖表頁高，請改用 CSV 查看完整內容。");
                            newPage(); continue;
                        }
                        double lineY = (bottom - 2) * Mm;
                        doc.Create.NewDetailCurve(view, Line.CreateBound(new XYZ(0, lineY, 0), new XYZ(380 * Mm, lineY, 0)));
                        y = bottom - 5; placed = true;
                    }
                }
                if (changes.Count == 0) Add(doc, view, textType.Id, 0, y, 380, "與基準相比無變更。");
                if (sheetId != null && sheetId != ElementId.InvalidElementId)
                {
                    if (views.Count > 1) throw new InvalidOperationException("差量共有 " + views.Count + " 頁，請選「僅建立視圖」，再分別拖曳至圖紙；本次未建立任何圖表。");
                    var sheet = doc.GetElement(sheetId) as ViewSheet;
                    if (sheet == null || sheet.IsPlaceholder || !Viewport.CanAddViewToSheet(doc, sheetId, view.Id))
                        throw new InvalidOperationException("無法放入選定圖紙。");
                    var outline = sheet.Outline;
                    var viewport = Viewport.Create(doc, sheet.Id, view.Id,
                        new XYZ((outline.Min.U + outline.Max.U) / 2, (outline.Min.V + outline.Max.V) / 2, 0));
                    doc.Regenerate();
                    var box = viewport.GetBoxOutline();
                    if (box.MinimumPoint.X < outline.Min.U || box.MaximumPoint.X > outline.Max.U ||
                        box.MinimumPoint.Y < outline.Min.V || box.MaximumPoint.Y > outline.Max.V)
                        throw new InvalidOperationException("差量圖表超出圖紙邊界，請改用較大的圖紙或僅建立視圖。");
                    foreach (var existing in new FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType()
                        .Where(e => e.Id != viewport.Id && (e is Viewport || e is ScheduleSheetInstance)))
                    {
                        var occupied = existing.get_BoundingBox(sheet);
                        if (occupied != null && occupied.Min.X < box.MaximumPoint.X && occupied.Max.X > box.MinimumPoint.X &&
                            occupied.Min.Y < box.MaximumPoint.Y && occupied.Max.Y > box.MinimumPoint.Y)
                            throw new InvalidOperationException("差量圖表會覆蓋圖紙上的既有視圖或明細表，請選空白圖紙或僅建立視圖。");
                    }
                }
                if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 未提交差量圖表。");
                return views;
            }
        }
        private static TextNote Add(Document doc, View view, ElementId type, double x, double y, double width, string text) =>
            TextNote.Create(doc, view.Id, new XYZ(x * Mm, y * Mm, 0), width * Mm, string.IsNullOrEmpty(text) ? "—" : text,
                new TextNoteOptions(type) { HorizontalAlignment = HorizontalTextAlignment.Left });
        private static IEnumerable<Tuple<double, double, string>> Cells(string a, string b, string c, string d)
        {
            yield return Tuple.Create(0.0, 34.0, a); yield return Tuple.Create(38.0, 42.0, b);
            yield return Tuple.Create(84.0, 146.0, c); yield return Tuple.Create(234.0, 146.0, d);
        }
    }
}
