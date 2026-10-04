using System;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveRoundClearancePolicy
    {
        // The pipe cuts each sleeve end plane as an ellipse with major radius r/cos(theta).
        // Its enclosing circle gives a conservative bound for straight, constant-bore sleeves.
        internal static double Remaining(double innerMm, double outerMm, double cosine, double maxCenterOffsetMm)
        {
            if (!Finite(innerMm) || !Finite(outerMm) || !Finite(cosine) || !Finite(maxCenterOffsetMm) ||
                innerMm <= 0 || outerMm <= 0 || maxCenterOffsetMm < 0 || Math.Abs(cosine) < 1e-6 || Math.Abs(cosine) > 1 + 1e-9)
                throw new ArgumentException("無法確認圓管穿越淨空。");
            return innerMm / 2 - outerMm / (2 * Math.Min(1, Math.Abs(cosine))) - maxCenterOffsetMm;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
