using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    // 視圖平面的座標；由呼叫端傳入 Revit 內部單位，避免世界 XY 與螢幕 XY 混用。
    internal sealed class ViewRect
    {
        internal double Left, Bottom, Right, Top;
        internal ViewRect(double left, double bottom, double right, double top)
        { Left = left; Bottom = bottom; Right = right; Top = top; }
        internal ViewRect Move(double x, double y) => new ViewRect(Left + x, Bottom + y, Right + x, Top + y);
        internal bool Overlaps(ViewRect other, double gap) =>
            Right + gap > other.Left && Left - gap < other.Right && Top + gap > other.Bottom && Bottom - gap < other.Top;
        internal ViewRect Pad(double minimum, double ratio)
        {
            double dx = Math.Max(minimum, (Right - Left) * ratio);
            double dy = Math.Max(minimum, (Top - Bottom) * ratio);
            return new ViewRect(Left - dx, Bottom - dy, Right + dx, Top + dy);
        }
    }

    internal static class ViewLayout
    {
        // 有界的簡單文字框避讓；不宣稱解決管線或引線交叉。
        internal static bool TryPlace(ViewRect original, IEnumerable<ViewRect> occupied,
            double step, double gap, out double dx, out double dy)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            var boxes = occupied.ToList();
            for (int row = 1; row <= 12; row++)
                foreach (int side in new[] { 1, -1 })
                    foreach (int column in new[] { 0, 1, -1, 2, -2 })
                    {
                        dx = column * step;
                        dy = side * row * step;
                        var candidate = original.Move(dx, dy);
                        if (!boxes.Any(box => candidate.Overlaps(box, gap))) return true;
                    }
            dx = 0;
            dy = step;
            return false;
        }
    }
}
