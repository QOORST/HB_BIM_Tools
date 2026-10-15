using System;
using System.Collections.Generic;

namespace YDBIM.AutoDimension.Core
{
    internal static class LinkedDimensionPolicy
    {
        internal static bool SamePair(string first, string second, string otherFirst, string otherSecond)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second) || first == second) return false;
            return (first == otherFirst && second == otherSecond) || (first == otherSecond && second == otherFirst);
        }

        internal static bool SegmentsMatch(IReadOnlyList<double> expected, IReadOnlyList<double?> actual, double tolerance)
        {
            if (expected.Count == 0 || expected.Count != actual.Count || double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0) return false;
            for (int i = 0; i < expected.Count; i++)
                if (double.IsNaN(expected[i]) || double.IsInfinity(expected[i]) || expected[i] <= 0 ||
                    !actual[i].HasValue || double.IsNaN(actual[i]!.Value) || double.IsInfinity(actual[i]!.Value) ||
                    actual[i]!.Value <= 0 || Math.Abs(expected[i] - actual[i]!.Value) > tolerance) return false;
            return true;
        }
    }
}
