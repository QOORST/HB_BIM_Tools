using System;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    internal static class AvoidRoutePolicy
    {
        // Feet throughout. Preserve the legacy along-axis run and world-Z offset for sloped lines.
        internal static double Validate(double length, double first, double last, double verticalDirection,
            double offset, double angle, double minimumSegment)
        {
            foreach (double v in new[] { length, first, last, verticalDirection, offset, angle, minimumSegment })
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("避讓參數無效。");
            if (Math.Abs(verticalDirection) >= 1 - 1e-10) throw new ArgumentException("立管無法以上下翻彎避讓；請選取水平或帶坡度的直線管段。");
            if (minimumSegment <= 0 || offset <= minimumSegment || length <= 0 ||
                (angle != 22.5 && angle != 45 && angle != 90)) throw new ArgumentException("避讓高度或角度無效。");
            double run = angle == 90 ? 0 : offset / Math.Tan(angle * Math.PI / 180);
            if (first < minimumSegment || length - last < minimumSegment || last - first - 2 * run < minimumSegment)
                throw new ArgumentException("翻彎區段不足，或選點位於端點之外；請增加兩點距離或降低高度。");
            return run;
        }
    }
}
