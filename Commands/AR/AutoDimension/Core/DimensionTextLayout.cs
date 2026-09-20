using System;
using System.Collections.Generic;
using System.Linq;

namespace YDBIM.AutoDimension.Core
{
    // Coordinates and sizes are paper millimetres in the dimension's view plane.
    internal sealed class DimensionTextBox
    {
        public int Index;
        public double Along, Across, Width, Height;
        public bool Movable;
    }

    internal static class DimensionTextLayout
    {
        internal static Dictionary<int, double> Plan(IList<DimensionTextBox> boxes, double gap, double offset, out int unresolved)
        {
            if (!Finite(gap) || gap < 0 || !Finite(offset) || offset <= 0)
                throw new ArgumentException("間距須為非負數，偏移須大於零，且皆為有限數值。");
            if (boxes.Any(b => !Finite(b.Along) || !Finite(b.Across) || !Finite(b.Width) || !Finite(b.Height) || b.Width <= 0 || b.Height <= 0)
                || boxes.Select(b => b.Index).Distinct().Count() != boxes.Count)
                throw new ArgumentException("文字範圍或索引無效。");
            var positions = boxes.ToDictionary(b => b.Index, b => b.Across);
            var placed = boxes.Where(b => !b.Movable).ToList();
            foreach (var box in boxes.Where(b => b.Movable).OrderBy(b => b.Along).ThenBy(b => b.Index))
            {
                // Preserve the original position whenever possible. Never move along the chain.
                var candidates = new[] { box.Across, box.Across - offset, box.Across + offset,
                    box.Across - 2 * offset, box.Across + 2 * offset };
                foreach (var across in candidates)
                {
                    if (placed.Any(other => Overlaps(box, across, other, positions[other.Index], gap))) continue;
                    positions[box.Index] = across;
                    break;
                }
                placed.Add(box);
            }
            var conflicts = new HashSet<int>();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                    if (Overlaps(boxes[i], positions[boxes[i].Index], boxes[j], positions[boxes[j].Index], gap))
                    { conflicts.Add(boxes[i].Index); conflicts.Add(boxes[j].Index); }
            unresolved = conflicts.Count;
            return positions;
        }

        internal static HashSet<int> FindConflicts(IList<DimensionTextBox> boxes, double gap)
        {
            if (!Finite(gap) || gap < 0) throw new ArgumentException("間距無效。");
            var conflicts = new HashSet<int>();
            for (int i=0;i<boxes.Count;i++)
                for(int j=i+1;j<boxes.Count;j++)
                    if(Overlaps(boxes[i],boxes[i].Across,boxes[j],boxes[j].Across,gap))
                    { conflicts.Add(boxes[i].Index); conflicts.Add(boxes[j].Index); }
            return conflicts;
        }

        private static bool Overlaps(DimensionTextBox a, double ay, DimensionTextBox b, double by, double gap)
            => Math.Abs(a.Along - b.Along) < (a.Width + b.Width) / 2 + gap
            && Math.Abs(ay - by) < (a.Height + b.Height) / 2 + gap;
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
