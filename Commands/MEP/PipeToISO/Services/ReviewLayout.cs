using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    // 所有座標與字框尺寸均為投影後的模型 mm，與 Revit 無關。
    public sealed class ReviewPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public ReviewPoint() { }
        public ReviewPoint(double x, double y) { X = x; Y = y; }
    }
    public sealed class ReviewLine
    {
        public string Owner { get; set; }
        public ReviewPoint A { get; set; }
        public ReviewPoint B { get; set; }
        public bool Fitting { get; set; }
    }
    public sealed class ReviewLabel
    {
        public string Code { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public ReviewPoint Anchor { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
    public sealed class ReviewScene
    {
        public int SchemaVersion { get; set; } = 1;
        public string Units { get; set; } = "mm";
        public string DocumentTitle { get; set; }
        public string SystemUniqueId { get; set; }
        public string CreatedAt { get; set; }
        public string Title { get; set; }
        public string Evidence { get; set; }
        public int Scale { get; set; } = 50;
        public List<ReviewLine> Lines { get; set; } = new List<ReviewLine>();
        public List<ReviewLabel> Labels { get; set; } = new List<ReviewLabel>();
    }
    internal sealed class ReviewPlacement
    {
        internal ReviewLabel Label;
        internal ViewRect Box;
        internal ReviewLine Leader;
        internal int Crossings;
    }
    internal sealed class LayoutCrowdedException : InvalidOperationException
    {
        internal LayoutCrowdedException(string message) : base(message) { }
    }
    internal static class ReviewLayout
    {
        internal static bool NeedsCompact(List<ReviewPlacement> placed) =>
            placed.Sum(p => p.Crossings) > Math.Max(4, placed.Count / 3);
        internal static void MakeCompact(ReviewScene scene)
        {
            int index = 0;
            foreach (var label in scene.Labels.OrderBy(l => l.Code, StringComparer.Ordinal))
            {
                index++;
                if (label.Detail != null) continue;
                string shortCode = "N" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                label.Detail = shortCode + " = " + label.Text;
                label.Text = shortCode;
                label.Width = shortCode.Sum(c => c > 255 ? 1.0 : 0.65) * 3.5 * scene.Scale;
                label.Height = 5.25 * scene.Scale;
            }
        }
        internal static List<ReviewPlacement> Arrange(ReviewScene scene)
        {
            if (scene == null || scene.SchemaVersion != 1 || scene.Units != "mm" || scene.Scale <= 0 || scene.Labels == null || scene.Lines == null || scene.Labels.Count == 0 || scene.Labels.Any(l => l == null))
                throw new InvalidOperationException("核對圖缺少標籤或比例無效。");
            if (scene.Labels.Select(l => l.Code).Distinct(StringComparer.Ordinal).Count() != scene.Labels.Count)
                throw new InvalidOperationException("核對圖有重複元件編號。");
            foreach (var label in scene.Labels)
                if (label == null || string.IsNullOrEmpty(label.Code) || !Finite(label.Width) || !Finite(label.Height) ||
                    label.Width <= 0 || label.Height <= 0 || !Valid(label.Anchor))
                    throw new InvalidOperationException("核對圖標籤尺寸或座標無效。");
            if (scene.Lines.Any(l => l == null || !Valid(l.A) || !Valid(l.B)))
                throw new InvalidOperationException("核對圖線段座標無效。");
            var placed = new List<ReviewPlacement>();
            double gap = 2 * scene.Scale, step = 8 * scene.Scale;
            foreach (var label in scene.Labels.OrderBy(l => l.Code, StringComparer.Ordinal))
            {
                ReviewPlacement best = null;
                double score = double.MaxValue;
                for (int ring = 1; ring <= 32; ring++)
                {
                    double distance = step * ring;
                    foreach (var offset in Ring(ring, step))
                    {
                        double x = label.Anchor.X + offset.X - label.Width / 2;
                        double y = label.Anchor.Y + offset.Y - label.Height / 2;
                        var rect = new ViewRect(x, y, x + label.Width, y + label.Height);
                        var candidate = Candidate(label, rect, scene, placed, gap);
                        if (candidate == null) continue;
                        double cost = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y) + candidate.Crossings * step * 12;
                        if (cost < score) { best = candidate; score = cost; }
                    }
                    // 找到無交叉候選後，遠處不可能改善距離。
                    if (best != null && best.Crossings == 0) break;
                }
                if (best == null)
                    throw new LayoutCrowdedException("元件 " + label.Code + " 周圍過密，無法安全排版；請縮小所選系統範圍。");
                placed.Add(best);
            }
            return placed;
        }
        private static IEnumerable<ReviewPoint> Ring(int ring, double step)
        {
            // 掃描整個方環，密集並行管不侷限於八個固定方向。
            for (int i = -ring; i <= ring; i++)
            {
                yield return new ReviewPoint(i * step, ring * step);
                yield return new ReviewPoint(i * step, -ring * step);
            }
            for (int i = -ring + 1; i < ring; i++)
            {
                yield return new ReviewPoint(ring * step, i * step);
                yield return new ReviewPoint(-ring * step, i * step);
            }
        }
        private static ReviewPlacement Candidate(ReviewLabel label, ViewRect box, ReviewScene scene,
            List<ReviewPlacement> placed, double gap)
        {
            if (placed.Any(p => box.Overlaps(p.Box, gap) || Hits(box, p.Leader, gap))) return null;
            if (scene.Lines.Any(l => Hits(box, l, gap))) return null;
            var anchors = new List<ReviewPoint> { label.Anchor };
            var own = scene.Lines.Where(l => l.Owner == label.Code && !l.Fitting).ToList();
            // 管標籤可接到實際端點，密集並行管可從端部引出，避免橫穿其他管線。
            if (own.Count > 0)
            {
                var ends = own.SelectMany(l => new[] { l.A, l.B }).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
                anchors.Add(ends[0]); anchors.Add(ends[ends.Count - 1]);
            }
            ReviewPlacement best = null;
            double bestLength = double.MaxValue;
            foreach (var anchor in anchors)
            {
                var end = new ReviewPoint(Math.Max(box.Left, Math.Min(box.Right, anchor.X)),
                    Math.Max(box.Bottom, Math.Min(box.Top, anchor.Y)));
                var leader = new ReviewLine { Owner = label.Code, A = anchor, B = end };
                if (placed.Any(p => Hits(p.Box, leader, gap))) continue;
                int crossings = scene.Lines.Count(l => l.Owner != label.Code && Intersects(l, leader)) +
                    placed.Count(p => Intersects(p.Leader, leader));
                double length = Math.Pow(anchor.X - end.X, 2) + Math.Pow(anchor.Y - end.Y, 2);
                if (best == null || crossings < best.Crossings || crossings == best.Crossings && length < bestLength)
                {
                    best = new ReviewPlacement { Label = label, Box = box, Leader = leader, Crossings = crossings };
                    bestLength = length;
                }
            }
            return best;
        }
        internal static bool Hits(ViewRect box, ReviewLine line, double gap)
        {
            var b = box.Pad(gap, 0);
            bool Inside(ReviewPoint p) => p.X >= b.Left && p.X <= b.Right && p.Y >= b.Bottom && p.Y <= b.Top;
            if (Inside(line.A) || Inside(line.B)) return true;
            var corners = new[] { new ReviewPoint(b.Left, b.Bottom), new ReviewPoint(b.Right, b.Bottom),
                new ReviewPoint(b.Right, b.Top), new ReviewPoint(b.Left, b.Top) };
            for (int i = 0; i < 4; i++)
                if (Intersects(line, new ReviewLine { A = corners[i], B = corners[(i + 1) % 4] })) return true;
            return false;
        }
        internal static bool Intersects(ReviewLine first, ReviewLine second)
        {
            const double eps = 1e-7;
            double Cross(ReviewPoint a, ReviewPoint b, ReviewPoint c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            bool On(ReviewPoint a, ReviewPoint b, ReviewPoint c) =>
                c.X >= Math.Min(a.X, b.X) - eps && c.X <= Math.Max(a.X, b.X) + eps &&
                c.Y >= Math.Min(a.Y, b.Y) - eps && c.Y <= Math.Max(a.Y, b.Y) + eps;
            double a1 = Cross(first.A, first.B, second.A), a2 = Cross(first.A, first.B, second.B);
            double b1 = Cross(second.A, second.B, first.A), b2 = Cross(second.A, second.B, first.B);
            if (((a1 > eps && a2 < -eps) || (a1 < -eps && a2 > eps)) &&
                ((b1 > eps && b2 < -eps) || (b1 < -eps && b2 > eps))) return true;
            return Math.Abs(a1) <= eps && On(first.A, first.B, second.A) ||
                Math.Abs(a2) <= eps && On(first.A, first.B, second.B) ||
                Math.Abs(b1) <= eps && On(second.A, second.B, first.A) ||
                Math.Abs(b2) <= eps && On(second.A, second.B, first.B);
        }
        private static bool Valid(ReviewPoint p) => p != null && Finite(p.X) && Finite(p.Y);
        private static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    }
}
