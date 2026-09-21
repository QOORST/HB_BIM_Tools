using System;
namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class RaftCadElevation
    {
        internal static double Center(double edge, double outerDiameter, bool top)
        {
            if (double.IsNaN(edge) || double.IsInfinity(edge) || double.IsNaN(outerDiameter) ||
                double.IsInfinity(outerDiameter) || outerDiameter <= 0)
                throw new ArgumentOutOfRangeException(nameof(outerDiameter));
            return edge + (top ? -.5 : .5) * outerDiameter;
        }
    }
}
