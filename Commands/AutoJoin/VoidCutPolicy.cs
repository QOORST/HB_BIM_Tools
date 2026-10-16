using System;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal static class VoidCutPolicy
    {
        internal const int MaximumSelection = 500;
        internal const int MaximumCandidates = 2000;
        internal static bool HasReduction(double before, double after)
        {
            if (double.IsNaN(before) || double.IsNaN(after) || double.IsInfinity(before) || double.IsInfinity(after)) return false;
            return before > 0 && after >= 0 && before - after > Math.Max(1e-9, before * 1e-10);
        }
        internal static bool Overlaps(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != 6 || b.Length != 6) return false;
            for (int i = 0; i < 3; i++)
            {
                if (!Finite(a[i]) || !Finite(a[i + 3]) || !Finite(b[i]) || !Finite(b[i + 3]) ||
                    a[i] > a[i + 3] || b[i] > b[i + 3] || a[i + 3] < b[i] || b[i + 3] < a[i]) return false;
            }
            return true;
        }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
