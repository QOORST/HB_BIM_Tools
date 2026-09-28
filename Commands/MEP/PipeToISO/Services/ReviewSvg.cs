using System;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    internal static class ReviewSvg
    {
        internal static string Render(ReviewScene scene)
        {
            var placed = ReviewLayout.Arrange(scene);
            var points = scene.Lines.SelectMany(l => new[] { l.A, l.B }).Concat(scene.Labels.Select(l => l.Anchor)).ToList();
            double margin = 12 * scene.Scale, font = 3.5 * scene.Scale;
            double left = Math.Min(points.Min(p => p.X), placed.Min(p => p.Box.Left)) - margin;
            double right = Math.Max(points.Max(p => p.X), placed.Max(p => p.Box.Right)) + margin;
            // 座標由向上為正改成 SVG 向下為正。
            double top = Math.Max(points.Max(p => p.Y), placed.Max(p => p.Box.Top)) + margin * 3;
            double bottom = Math.Min(points.Min(p => p.Y), placed.Min(p => p.Box.Bottom)) - margin * 4;
            var details = scene.Labels.Where(l => l.Detail != null).OrderBy(l => l.Code, StringComparer.Ordinal).ToList();
            int columns = Math.Max(1, (details.Count + 23) / 24), perColumn = (details.Count + columns - 1) / columns;
            if (details.Count > 0) bottom -= (perColumn + 2) * font * 1.5;
            double TextWidth(string value, double size) => (value ?? "").Sum(c => c > 255 ? 1.0 : 0.65) * size;
            double contentWidth = Math.Max(font * 65, Math.Max(TextWidth(scene.Title, font * 1.3), TextWidth(scene.Evidence, font * 0.8)));
            double columnWidth = details.Count > 0 ? details.Max(l => TextWidth(l.Detail.Replace("\n", "；"), font)) + margin : 0;
            if (details.Count > 0) contentWidth = Math.Max(contentWidth, columnWidth * columns);
            right = Math.Max(right, left + contentWidth + margin * 2);
            string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
            string E(string text) => SecurityElement.Escape(text ?? "");
            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1800\" height=\"{N(1800 * (top - bottom) / (right - left))}\" viewBox=\"{N(left)} {N(-top)} {N(right - left)} {N(top - bottom)}\">");
            sb.AppendLine($"<rect x=\"{N(left)}\" y=\"{N(-top)}\" width=\"{N(right-left)}\" height=\"{N(top-bottom)}\" fill=\"white\"/>");
            void Line(ReviewLine line, string color, double weight) => sb.AppendLine(
                $"<line x1=\"{N(line.A.X)}\" y1=\"{N(-line.A.Y)}\" x2=\"{N(line.B.X)}\" y2=\"{N(-line.B.Y)}\" stroke=\"{color}\" stroke-width=\"{N(weight)}\"/>");
            foreach (var line in scene.Lines) Line(line, line.Fitting ? "#777" : "#111", scene.Scale * (line.Fitting ? 0.2 : 0.4));
            foreach (var p in placed) Line(p.Leader, "#777", scene.Scale * 0.2);
            void Text(double x, double y, string text, double size) => sb.AppendLine(
                $"<text x=\"{N(x)}\" y=\"{N(-y)}\" font-family=\"Microsoft JhengHei,Arial,sans-serif\" font-size=\"{N(size)}\" fill=\"#111\">{E(text)}</text>");
            foreach (var p in placed)
            {
                var lines = p.Label.Text.Split('\n');
                // 預覽字體與 Revit 可能不同，將每行限制在同一量測字框之內。
                for (int i = 0; i < lines.Length; i++)
                    sb.AppendLine($"<text x=\"{N(p.Box.Left)}\" y=\"{N(-p.Box.Top + (i + 0.8) * p.Label.Height / lines.Length)}\" " +
                        $"font-family=\"Microsoft JhengHei,Arial,sans-serif\" font-size=\"{N(font)}\" textLength=\"{N(Math.Min(p.Label.Width, lines[i].Sum(c => c > 255 ? 1 : 0.65) * font))}\" lengthAdjust=\"spacingAndGlyphs\">{E(lines[i])}</text>");
            }
            Text(left + margin, top - margin, scene.Title, font * 1.3);
            if (details.Count > 0)
            {
                for (int column = 0; column < columns; column++)
                {
                    double y = bottom + margin * 4 + perColumn * font * 1.5;
                    double x = left + margin + columnWidth * column;
                    Text(x, y, $"密集區尺寸對照 {column + 1}/{columns}", font);
                    foreach (var label in details.Skip(column * perColumn).Take(perColumn))
                    { y -= font * 1.5; Text(x, y, label.Detail.Replace("\n", "；"), font); }
                }
            }
            Text(left + margin, bottom + margin * 2.6, "離線排版預覽：模型長度非加工切長；管件線為接頭示意。", font);
            Text(left + margin, bottom + margin * 1.8, scene.Evidence, font * 0.8);
            Text(left + margin, bottom + margin, $"{placed.Count} 個標籤；引線交叉候選 {placed.Sum(p => p.Crossings)} 處。字型外觀以 Revit 最終出圖為準。", font * 0.8);
            sb.AppendLine("</svg>");
            return sb.ToString();
        }
    }
}
