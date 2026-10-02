using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal static class TagRowPairing
    {
        internal static bool TryPair(double[] reference, double[] target, double ambiguity, double maxMove,
            out List<(int Reference, int Target)> pairs, out string error)
        {
            pairs = new List<(int, int)>(); error = null;
            if (reference.Length != target.Length) { error = "此分類兩側標籤數量不同，未配對；請縮小範圍後重選。"; return false; }
            if (reference.Length < 1 || reference.Length > 500) { error = "請各框選 1 至 500 個標籤。"; return false; }
            if (reference.Concat(target).Any(v => double.IsNaN(v) || double.IsInfinity(v)) ||
                double.IsNaN(ambiguity) || double.IsInfinity(ambiguity) || ambiguity <= 0 ||
                double.IsNaN(maxMove) || double.IsInfinity(maxMove) || maxMove <= 0)
            { error = "配對座標或容差無效。"; return false; }
            var r = Enumerable.Range(0, reference.Length).OrderBy(i => reference[i]).ToArray();
            var t = Enumerable.Range(0, target.Length).OrderBy(i => target[i]).ToArray();
            for (int i = 1; i < r.Length; i++)
                if (reference[r[i]] - reference[r[i-1]] <= ambiguity || target[t[i]] - target[t[i-1]] <= ambiguity)
                { error = "同一配對位置有多個標籤或間距過近，未強制配對。"; return false; }
            for (int i = 0; i < r.Length; i++)
            {
                double gap = double.PositiveInfinity;
                if (i > 0) gap = Math.Min(reference[r[i]] - reference[r[i-1]], target[t[i]] - target[t[i-1]]);
                if (i + 1 < r.Length) gap = Math.Min(gap, Math.Min(reference[r[i+1]] - reference[r[i]], target[t[i+1]] - target[t[i]]));
                double distance = Math.Abs(reference[r[i]] - target[t[i]]);
                if (distance > Math.Min(maxMove, gap * 0.45))
                { pairs.Clear(); error = "配對位移過大，可能跨列或漏選；本次未移動。"; return false; }
                pairs.Add((r[i], t[i]));
            }
            return true;
        }
    }
}
