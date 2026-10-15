using System;
using System.Collections.Generic;
using System.Linq;

namespace YDBIM.AutoDimension.Core
{
    internal static class BeamChainProjectionPolicy
    {
        // Keep real segment pairs: joining unrelated endpoints invents coverage.
        internal static List<(int Start, int End)> MissingRuns(IReadOnlyList<double> positions,
            IReadOnlyList<(double Start, double End)> existing, double tolerance)
        {
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            for (int i = 0; i < positions.Count; i++)
                if (double.IsNaN(positions[i]) || double.IsInfinity(positions[i]) ||
                    (i > 0 && positions[i] <= positions[i - 1]))
                    throw new ArgumentException("Projection positions must be finite and increasing.", nameof(positions));

            var result = new List<(int Start, int End)>();
            int runStart = -1;
            for (int i = 0; i + 1 < positions.Count; i++)
            {
                bool covered = existing.Any(s =>
                    Math.Abs(Math.Min(s.Start, s.End) - positions[i]) <= tolerance &&
                    Math.Abs(Math.Max(s.Start, s.End) - positions[i + 1]) <= tolerance);
                if (!covered && runStart < 0) runStart = i;
                if (covered && runStart >= 0)
                {
                    result.Add((runStart, i));
                    runStart = -1;
                }
            }
            if (runStart >= 0) result.Add((runStart, positions.Count - 1));
            return result;
        }

        internal static List<List<int>> MergeRanges(IReadOnlyList<(double Min, double Max)> ranges, double tolerance)
        {
            var result = new List<List<int>>();
            double end = double.NegativeInfinity;
            foreach (int index in Enumerable.Range(0, ranges.Count).OrderBy(i => ranges[i].Min).ThenBy(i => ranges[i].Max))
            {
                var range = ranges[index];
                if (double.IsNaN(range.Min) || double.IsNaN(range.Max) || double.IsInfinity(range.Min) ||
                    double.IsInfinity(range.Max) || range.Max < range.Min) throw new ArgumentException("Invalid projection range");
                if (result.Count == 0 || range.Min > end + tolerance)
                { result.Add(new List<int>()); end = range.Max; }
                result[result.Count - 1].Add(index);
                end = Math.Max(end, range.Max);
            }
            return result;
        }

        internal static bool SamePositions(IReadOnlyList<double> first, IReadOnlyList<double> second, double tolerance)
        {
            if (first.Count < 2 || first.Count != second.Count) return false;
            var a = first.OrderBy(v => v).ToArray();
            var b = second.OrderBy(v => v).ToArray();
            return a.Zip(b, (x, y) => !double.IsNaN(x) && !double.IsNaN(y) && Math.Abs(x - y) <= tolerance).All(v => v);
        }
    }
}
