using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // All coordinates and distances are in paper mm; Across=0 is the dimension line.
    internal sealed class MepTextBox
    {
        internal double Along, Across, Width, Height;
    }

    internal sealed class MepTextLayout
    {
        internal double[] Across;
        internal int UnresolvedGroups;
    }

    internal static class MepDimensionTextPolicy
    {
        private const double Epsilon = 1e-7;

        internal static bool ClearsDimensionLine(MepTextBox box, double across, double clearance)
            => Math.Abs(across) + Epsilon >= box.Height / 2 + clearance;

        internal static bool Overlaps(MepTextBox a, double acrossA, MepTextBox b, double acrossB, double gap)
            => Math.Abs(a.Along - b.Along) + Epsilon < (a.Width + b.Width) / 2 + gap &&
               Math.Abs(acrossA - acrossB) + Epsilon < (a.Height + b.Height) / 2 + gap;

        internal static MepTextLayout Plan(IReadOnlyList<MepTextBox> boxes, double maximumMove,
            double step = .5, double gap = .3, double lineClearance = .5)
        {
            if (boxes == null || !Finite(maximumMove) || maximumMove < 0 || maximumMove > 20 ||
                !Finite(step) || step < .1 || !Finite(gap) || gap < 0 ||
                !Finite(lineClearance) || lineClearance < 0 || lineClearance > 5 ||
                boxes.Any(b => b == null || !Finite(b.Along) || !Finite(b.Across) ||
                    !Finite(b.Width) || !Finite(b.Height) || b.Width <= 0 || b.Height <= 0))
                throw new ArgumentException("Invalid dimension text layout input.");

            var result = new MepTextLayout { Across = boxes.Select(b => b.Across).ToArray() };
            var visited = new HashSet<int>();
            for (int seed = 0; seed < boxes.Count; seed++)
            {
                if (!visited.Add(seed)) continue;
                var group = new List<int> { seed };
                for (int cursor = 0; cursor < group.Count; cursor++)
                    for (int j = 0; j < boxes.Count; j++)
                        if (!visited.Contains(j) && Overlaps(boxes[group[cursor]], boxes[group[cursor]].Across,
                            boxes[j], boxes[j].Across, gap))
                        { visited.Add(j); group.Add(j); }
                if (group.Count == 1 && ClearsDimensionLine(boxes[seed], boxes[seed].Across, lineClearance)) continue;

                // Bounded search, two orderings. Unresolved groups remain entirely unchanged.
                // Other groups (including clear text) are obstacles at their current positions.
                double[] best = null;
                var orders = new[] { group.ToArray(), group.AsEnumerable().Reverse().ToArray() };
                foreach (bool allowCrossing in new[] { false, true })
                {
                    int budget = 12000;
                    for (double radius = Math.Min(step, maximumMove); ; radius = Math.Min(radius + step, maximumMove))
                    {
                        foreach (var order in orders)
                        {
                            var positions = (double[])result.Across.Clone();
                            var fixedIndices = new HashSet<int>(Enumerable.Range(0, boxes.Count).Except(group));
                            if (Search(0, order, boxes, positions, fixedIndices, radius, step, gap, lineClearance,
                                allowCrossing, ref budget) && (best == null || Better(positions, best, group, boxes)))
                                best = positions;
                        }
                        if (best != null || budget <= 0 || radius >= maximumMove - Epsilon) break;
                    }
                    if (best != null) break;
                }
                if (best == null) result.UnresolvedGroups++;
                else foreach (int i in group) result.Across[i] = best[i];
            }
            return result;
        }

        private static bool Search(int depth, int[] order, IReadOnlyList<MepTextBox> boxes,
            double[] positions, HashSet<int> fixedIndices, double limit, double step, double gap, double lineClearance,
            bool allowCrossing, ref int budget)
        {
            if (depth == order.Length) return true;
            // Avoid unbounded recursion on unusually dense dimensions.
            if (depth >= 64 || budget <= 0) return false;
            int index = order[depth];
            var box = boxes[index];
            var candidates = new List<double> { box.Across };
            for (double distance = Math.Min(step, limit); distance > 0; distance = Math.Min(distance + step, limit))
            {
                double outward = box.Across < 0 ? -distance : distance;
                candidates.Add(box.Across + outward);
                candidates.Add(box.Across - outward);
                if (distance >= limit - Epsilon) break;
            }
            foreach (double candidate in candidates.OrderBy(p => Crosses(box.Across, p))
                .ThenBy(p => Math.Abs(p - box.Across)))
            {
                if (--budget < 0) return false;
                if (!allowCrossing && Crosses(box.Across, candidate)) continue;
                if (!ClearsDimensionLine(box, candidate, lineClearance)) continue;
                if (fixedIndices.Any(j => Overlaps(box, candidate, boxes[j], positions[j], gap))) continue;
                positions[index] = candidate;
                fixedIndices.Add(index);
                if (Search(depth + 1, order, boxes, positions, fixedIndices, limit, step, gap, lineClearance,
                    allowCrossing, ref budget)) return true;
                fixedIndices.Remove(index);
            }
            positions[index] = box.Across;
            return false;
        }

        private static bool Better(double[] candidate, double[] current, List<int> group, IReadOnlyList<MepTextBox> boxes)
        {
            int crossing = group.Count(i => Crosses(boxes[i].Across, candidate[i]))
                .CompareTo(group.Count(i => Crosses(boxes[i].Across, current[i])));
            if (crossing != 0) return crossing < 0;
            double max = group.Max(i => Math.Abs(candidate[i] - boxes[i].Across));
            double oldMax = group.Max(i => Math.Abs(current[i] - boxes[i].Across));
            if (Math.Abs(max - oldMax) > Epsilon) return max < oldMax;
            return group.Sum(i => Math.Abs(candidate[i] - boxes[i].Across)) <
                group.Sum(i => Math.Abs(current[i] - boxes[i].Across)) - Epsilon;
        }

        private static bool Crosses(double original, double target) => original * target < -Epsilon;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
