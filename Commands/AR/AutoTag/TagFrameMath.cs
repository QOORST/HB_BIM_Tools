using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal static class TagFrameMath
    {
        // Input is family-local paper mm. Only unbranched closed components qualify.
        internal static bool TryCenter(List<double[][]> curves, out double[] center)
        {
            center = null;
            if (curves.Count == 0 || curves.Count > 500) return false;
            var nodes = new List<double[]>();
            var ends = new List<int[]>();
            foreach (var curve in curves)
            {
                if (curve.Length < 2 || curve.Any(p => p.Length < 3 || p.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || Math.Abs(p[2]) > 0.005)) return false;
                var edge = new int[2];
                for (int i = 0; i < 2; i++)
                {
                    var p = curve[i == 0 ? 0 : curve.Length - 1];
                    int n = nodes.FindIndex(q => Math.Abs(p[0] - q[0]) < 0.0001 && Math.Abs(p[1] - q[1]) < 0.0001);
                    if (n < 0) { n = nodes.Count; nodes.Add(p); }
                    edge[i] = n;
                }
                ends.Add(edge);
            }
            var seen = new HashSet<int>();
            var centers = new List<double[]>();
            for (int seed = 0; seed < curves.Count; seed++)
            {
                if (seen.Contains(seed)) continue;
                var component = new List<int>();
                var queue = new Queue<int>(); queue.Enqueue(seed); seen.Add(seed);
                while (queue.Count > 0)
                {
                    int edge = queue.Dequeue(); component.Add(edge);
                    for (int other = 0; other < ends.Count; other++)
                        if (!seen.Contains(other) && ends[other].Any(n => ends[edge].Contains(n)))
                        { seen.Add(other); queue.Enqueue(other); }
                }
                if (component.Count < 3) continue;
                var degrees = component.SelectMany(i => ends[i]).GroupBy(n => n);
                if (degrees.Any(g => g.Count() != 2)) continue;
                var points = component.SelectMany(i => curves[i]).ToList();
                double x0 = points.Min(p => p[0]), x1 = points.Max(p => p[0]), y0 = points.Min(p => p[1]), y1 = points.Max(p => p[1]);
                if (x1 - x0 < 0.1 || y1 - y0 < 0.1) continue;
                centers.Add(new[] { (x0 + x1) / 2, (y0 + y1) / 2 });
            }
            if (centers.Count == 0) return false;
            var first = centers[0];
            // Several size/visibility variants are safe only when all their centers agree.
            if (centers.Any(p => Math.Abs(p[0] - first[0]) > 0.005 || Math.Abs(p[1] - first[1]) > 0.005)) return false;
            center = new[] { centers.Average(p => p[0]), centers.Average(p => p[1]) };
            return true;
        }
        internal static double[] Offset(double[] center, double angle, int scale)
        {
            return new[] { (center[0] * Math.Cos(angle) - center[1] * Math.Sin(angle)) * scale,
                (center[0] * Math.Sin(angle) + center[1] * Math.Cos(angle)) * scale };
        }
    }
}
