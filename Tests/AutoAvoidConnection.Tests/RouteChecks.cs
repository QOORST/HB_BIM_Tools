using System;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core;
internal static class RouteChecks
{
    internal static void Run()
    {
        int count = 0;
        Action<bool> check = ok => { if (!ok) throw new Exception("route policy assertion " + count); count++; };
        foreach (double angle in new[] {22.5, 45.0, 90.0})
        {
            double run = AvoidRoutePolicy.Validate(20, 2, 18, 0, 1, angle, .01);
            check(Math.Abs(run - (angle == 90 ? 0 : 1 / Math.Tan(angle * Math.PI / 180))) < 1e-9);
            check(18 - 2 - 2 * run >= .01);
        }
        Action<double, double, double, double, double, double, double> reject = (length, first, last, z, offset, angle, min) =>
        {
            bool failed = false;
            try { AvoidRoutePolicy.Validate(length, first, last, z, offset, angle, min); }
            catch (ArgumentException) { failed = true; }
            check(failed);
        };
        reject(20, -1, 18, 0, 1, 45, .01);
        reject(20, 2, 21, 0, 1, 45, .01);
        reject(20, 2, 3, 0, 1, 45, .01);
        reject(20, 18, 2, 0, 1, 45, .01);
        foreach (double slope in new[] { -.02, -.01, .01, .02, .1, -.1 })
            foreach (double angle in new[] {22.5, 45.0, 90.0})
            {
                double z = slope / Math.Sqrt(1 + slope * slope);
                double run = AvoidRoutePolicy.Validate(20, 2, 18, z, 1, angle, .01);
                check(Math.Abs(run - AvoidRoutePolicy.Validate(20, 2, 18, 0, 1, angle, .01)) < 1e-9);
            }
        reject(20, 2, 18, 1, 1, 45, .01);
        reject(20, 2, 18, -1, 1, 45, .01);
        reject(20, 2, 18, 1.1, 1, 45, .01);
        reject(20, 2, 18, 0, -1, 45, .01);
        reject(20, 2, 18, 0, 0, 45, .01);
        reject(20, 2, 18, 0, 1, 30, .01);
        reject(20, 2, 18, 0, 1, 45, 0);
        reject(double.NaN, 2, 18, 0, 1, 45, .01);
        reject(20, 2, 18, 0, double.PositiveInfinity, 45, .01);
        reject(20, 0, 18, 0, 1, 90, .01);
        reject(20, 2, 20, 0, 1, 90, .01);
        Console.WriteLine(count + " route geometry policy checks passed.");
    }
}
