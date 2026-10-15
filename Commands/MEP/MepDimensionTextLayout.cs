using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class CmdMepPositionDimension
    {
        private static string ArrangeDimensionText(Document doc, View view, Dimension dimension)
        {
            if (!staggerText || dimension.Segments.Size < 2) return null;
            using (var layout = new SubTransaction(doc))
            {
                try
                {
                    var segments = dimension.Segments.Cast<DimensionSegment>().ToList();
                    if (segments.Any(s => !s.IsTextPositionAdjustable()))
                        return $"尺寸 {dimension.Id}：樣式不允許文字移位，保留原排版。";
                    var line = dimension.Curve as Line;
                    if (line == null) return $"尺寸 {dimension.Id}：非直線尺寸，保留原排版。";
                    XYZ direction = line.Direction.Normalize();
                    bool vertical = Math.Abs(direction.DotProduct(view.UpDirection)) >= Math.Abs(direction.DotProduct(view.RightDirection));
                    XYZ ordering = vertical ? view.UpDirection : view.RightDirection;
                    XYZ side = view.ViewDirection.CrossProduct(direction).Normalize();
                    XYZ positiveSide = vertical ? view.RightDirection : view.UpDirection;
                    if (side.DotProduct(positiveSide) < 0) side = -side;
                    // Screen order is stable even when reference or dimension line direction reverses.
                    segments = segments.OrderBy(s => s.Origin.DotProduct(ordering) * (vertical ? -1 : 1)).ToList();
                    var values = segments.Select(s => s.Value).ToList();
                    double modelPerPaperMm = view.Scale / 304.8;
                    var type = doc.GetElement(dimension.GetTypeId());
                    double size = type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
                    if (size <= 0) return $"尺寸 {dimension.Id}：無法讀取文字尺寸，保留原排版。";
                    string fontName = type.get_Parameter(BuiltInParameter.TEXT_FONT)?.AsString() ?? "Arial";
                    var widths = new double[segments.Count];
                    var heights = new double[segments.Count];
                    // GDI font metrics estimate paper-space extents; Revit exposes no per-segment text box.
                    using (var bitmap = new System.Drawing.Bitmap(1,1))
                    using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    using (var font = new System.Drawing.Font(fontName,100,System.Drawing.FontStyle.Regular,System.Drawing.GraphicsUnit.Pixel))
                    {
                        for (int i=0;i<segments.Count;i++)
                        {
                            string label = segments[i].Prefix + segments[i].ValueString + segments[i].Suffix;
                            if (!string.IsNullOrEmpty(segments[i].Above)) label = segments[i].Above + "\n" + label;
                            if (!string.IsNullOrEmpty(segments[i].Below)) label += "\n" + segments[i].Below;
                            var bounds = graphics.MeasureString(label,font);
                            widths[i] = bounds.Width/100 * size * 304.8;
                            heights[i] = bounds.Height/100 * size * 304.8;
                        }
                    }
                    var original = segments.Select(s=>s.TextPosition).ToList();
                    if (original.Any(p=>p==null)) return $"尺寸 {dimension.Id}：無可用文字位置，保留原排版。";
                    XYZ anchor = line.GetEndPoint(0);
                    var boxes = original.Select((p, i) => new MepTextBox {
                        Along = (p - anchor).DotProduct(direction) / modelPerPaperMm,
                        Across = (p - anchor).DotProduct(side) / modelPerPaperMm,
                        Width = widths[i], Height = heights[i]
                    }).ToList();
                    var plan = MepDimensionTextPolicy.Plan(boxes, (double)textMaximumMove,
                        lineClearance: (double)textLineClearance);
                    string warning = plan.UnresolvedGroups == 0 ? null :
                        $"尺寸 {dimension.Id}：{plan.UnresolvedGroups} 組文字未能在最大位移 {textMaximumMove:0.#} mm 內兼顧避讓與尺寸線淨距 {textLineClearance:0.#} mm（皆為紙面），已保留該組原位，請人工調整。";
                    if (!boxes.Where((b, i) => Math.Abs(plan.Across[i] - b.Across) > 1e-7).Any()) return warning;
                    layout.Start();
                    for (int i = 0; i < segments.Count; i++)
                        if (Math.Abs(plan.Across[i] - boxes[i].Across) > 1e-7)
                            segments[i].TextPosition = original[i] + side *
                                ((plan.Across[i] - boxes[i].Across) * modelPerPaperMm);
                    doc.Regenerate();
                    if (!dimension.AreReferencesAvailable || segments.Where((s,i) => s.Value != values[i]).Any())
                        throw new InvalidOperationException("文字移位後參考或量測值改變。");
                    // Check actual Revit positions before committing, including labels we left untouched.
                    for (int i = 0; i < segments.Count; i++)
                    {
                        var position = segments[i].TextPosition;
                        var expected = original[i] + side * ((plan.Across[i] - boxes[i].Across) * modelPerPaperMm);
                        if (position == null || position.DistanceTo(expected) / modelPerPaperMm > .01 ||
                            position.DistanceTo(original[i]) / modelPerPaperMm > (double)textMaximumMove + 1e-7)
                            throw new InvalidOperationException("實際文字位置與排版計畫不符或超過最大位移。");
                        if (MepDimensionTextPolicy.ClearsDimensionLine(boxes[i], plan.Across[i], (double)textLineClearance) &&
                            !MepDimensionTextPolicy.ClearsDimensionLine(boxes[i],
                                (position - anchor).DotProduct(side) / modelPerPaperMm, (double)textLineClearance))
                            throw new InvalidOperationException("實際文字位置未達尺寸線最小淨距。");
                    }
                    if (layout.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("文字排版未提交。");
                    return warning;
                }
                catch (Exception ex)
                {
                    if (layout.GetStatus() == TransactionStatus.Started) layout.RollBack();
                    return $"尺寸 {dimension.Id}：保留原文字排版（{ex.Message}）。";
                }
            }
        }
    }
}
