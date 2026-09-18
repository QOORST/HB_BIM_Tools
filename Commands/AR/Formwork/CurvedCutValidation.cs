using System;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    internal static class CurvedCutValidation
    {
        // Volume sanity is not an independent proof of geometric subtraction correctness.
        internal static bool IsValidVolume(double before, double after)
        {
            if (double.IsNaN(before) || double.IsInfinity(before) || before <= 0 ||
                double.IsNaN(after) || double.IsInfinity(after) || after < 0) return false;
            return after <= before + Math.Max(1e-9, before * 1e-6);
        }
    }
}
