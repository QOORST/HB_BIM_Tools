using System;
using YD_RevitTools.LicenseManager.Commands.AR.AutoJoin;

internal static class PolicyTests
{
    private static int count;
    private static void Check(bool condition) { count++; if (!condition) throw new Exception("Check failed: " + count); }
    public static void Main()
    {
        var box = new double[] { 0, 0, 0, 2, 2, 2 };
        Check(VoidCutPolicy.Overlaps(box, box));
        Check(VoidCutPolicy.Overlaps(box, new double[] { 1, 1, 1, 3, 3, 3 }));
        Check(VoidCutPolicy.Overlaps(box, new double[] { 2, 0, 0, 4, 2, 2 }));
        Check(!VoidCutPolicy.Overlaps(box, new double[] { 0, 0, 3, 2, 2, 4 }));
        Check(!VoidCutPolicy.Overlaps(box, null));
        Check(!VoidCutPolicy.Overlaps(box, new double[] { 3, 0, 0, 1, 2, 2 }));
        Check(!VoidCutPolicy.Overlaps(box, new double[] { double.NaN, 0, 0, 2, 2, 2 }));
        Check(VoidCutPolicy.HasReduction(100, 99));
        Check(VoidCutPolicy.HasReduction(100, 0));
        Check(!VoidCutPolicy.HasReduction(100, 100));
        Check(!VoidCutPolicy.HasReduction(100, 101));
        Check(!VoidCutPolicy.HasReduction(0, 0));
        Check(!VoidCutPolicy.HasReduction(100, -1));
        Check(!VoidCutPolicy.HasReduction(double.NaN, 1));
        Check(!VoidCutPolicy.HasReduction(100, double.PositiveInfinity));
        Check(!VoidCutPolicy.HasReduction(100, 100 - 1e-11));
        for (int axis = 0; axis < 3; axis++)
        {
            var other = (double[])box.Clone(); other[axis] = 3; other[axis + 3] = 4;
            Check(!VoidCutPolicy.Overlaps(box, other));
            Check(!VoidCutPolicy.Overlaps(other, box));
        }
        Console.WriteLine($"PASS: {count} auto void cut policy checks; no native Revit geometry tested.");
    }
}
