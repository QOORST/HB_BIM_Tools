using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class CmdMepPositionDimension
    {
        private static double AlternatingOffset(int index, bool vertical, double distance)
            => (index % 2 == 0 ? 1 : -1) * (vertical ? -1 : 1) * distance;

        private static bool[] OverlappingLabels(double[] along, double[] across, double[] widths, double[] heights, double gap)
        {
            var result = new bool[along.Length];
            for (int i=0;i<along.Length;i++)
            for (int j=i+1;j<along.Length;j++)
                if (Math.Abs(along[i]-along[j]) < (widths[i]+widths[j])/2+gap &&
                    Math.Abs(across[i]-across[j]) < (heights[i]+heights[j])/2+gap)
                    result[i]=result[j]=true;
            return result;
        }

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
                    double distance = (double)textOffset * view.Scale / 304.8;
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
                            widths[i] = bounds.Width/100 * size * view.Scale;
                            heights[i] = bounds.Height/100 * size * view.Scale;
                        }
                    }
                    var original = segments.Select(s=>s.TextPosition).ToList();
                    if (original.Any(p=>p==null)) return $"尺寸 {dimension.Id}：無可用文字位置，保留原排版。";
                    double gap = .3 * view.Scale / 304.8;
                    var overlapping = OverlappingLabels(original.Select(p=>p.DotProduct(direction)).ToArray(),
                        original.Select(p=>p.DotProduct(side)).ToArray(),widths,heights,gap);
                    if (!overlapping.Any(x=>x)) return null;
                    distance = Math.Max(distance, heights.Max()/2 + gap);
                    layout.Start();
                    int moved = 0;
                    for (int i = 0; i < segments.Count; i++)
                        if (overlapping[i])
                        {
                            // Preserve the along-line position and leave every non-overlapping label untouched.
                            double target = segments[i].Origin.DotProduct(side) + AlternatingOffset(moved++, vertical, distance);
                            segments[i].TextPosition = original[i] + side * (target-original[i].DotProduct(side));
                        }
                    doc.Regenerate();
                    if (!dimension.AreReferencesAvailable || segments.Where((s,i) => s.Value != values[i]).Any())
                        throw new InvalidOperationException("文字移位後參考或量測值改變。");
                    if (layout.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("文字排版未提交。");
                    var positions = segments.Select(s=>s.TextPosition).ToList();
                    bool remaining = OverlappingLabels(positions.Select(p=>p.DotProduct(direction)).ToArray(),
                        positions.Select(p=>p.DotProduct(side)).ToArray(),widths,heights,gap).Any(x=>x);
                    return remaining ? $"尺寸 {dimension.Id}：局部交錯後仍可能重疊，請檢查文字。" : null;
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
