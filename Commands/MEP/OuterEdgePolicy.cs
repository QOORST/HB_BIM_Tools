using System;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class OuterEdgePolicy
    {
        internal static int PlacementSign(int choice, bool horizontal, double screenDirection, double outward)
        {
            if (choice == 0 && Math.Abs(outward) > .999999) return Math.Sign(outward);
            int screenSign = horizontal ? (choice == 1 ? 1 : -1) : (choice == 1 ? -1 : 1);
            return screenSign * (screenDirection >= 0 ? 1 : -1);
        }

        internal static bool IsExterior(double station, double boundary, double alignment, int outward)
        {
            if (double.IsNaN(station) || double.IsInfinity(station) ||
                double.IsNaN(boundary) || double.IsInfinity(boundary) ||
                double.IsNaN(alignment) || double.IsInfinity(alignment)) return false;
            return (outward == -1 || outward == 1) && alignment * outward >= .999999 &&
                Math.Abs(station - boundary) <= .05 / 304.8;
        }
    }
}
