using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    internal sealed class BoundedPanelCut<T> where T : class
    {
        private readonly Func<T, T, T> difference;
        private readonly Func<T, IList<T>> split;
        private readonly Func<T, double> volume;
        private readonly Func<Exception, bool> recoverable;
        private readonly Action checkpoint;
        private readonly Action<string> trace;
        private int splits;

        internal BoundedPanelCut(Func<T, T, T> difference, Func<T, IList<T>> split,
            Func<T, double> volume, Func<Exception, bool> recoverable, Action checkpoint, Action<string> trace)
        {
            this.difference = difference; this.split = split; this.volume = volume;
            this.recoverable = recoverable; this.checkpoint = checkpoint; this.trace = trace;
        }

        internal IList<T> Cut(T panel, T cutter, int depth = 0)
        {
            checkpoint();
            T result;
            try { result = difference(panel, cutter); }
            catch (Exception ex) when (recoverable(ex))
            {
                if (depth >= 5 || splits >= 32)
                    throw new InvalidOperationException("弧面分片裁切已達上限，未輸出此面。", ex);
                splits++;
                trace($"Split recovery depth={depth}; total={splits}; cause={ex.Message}");
                checkpoint();
                var parts = split(panel);
                double original = volume(panel);
                double tolerance = Math.Max(1e-9, original * 1e-6);
                if (parts == null || parts.Count != 2 || parts.Any(p => p == null ||
                    !CurvedCutValidation.IsValidVolume(original, volume(p)) ||
                    volume(p) <= 1e-9 || volume(p) >= original - tolerance) ||
                    Math.Abs(parts.Sum(volume) - original) > tolerance)
                    throw new InvalidOperationException("弧面分片未通過體積守恆檢查，未輸出此面。", ex);
                // Do not return partial output if either child fails.
                var output = new List<T>();
                foreach (var part in parts) output.AddRange(Cut(part, cutter, depth + 1));
                return output;
            }
            if (result == null || !CurvedCutValidation.IsValidVolume(volume(panel), volume(result)))
                throw new InvalidOperationException("弧面差集體積無效，未輸出此面。");
            trace($"Difference before={volume(panel)}; after={volume(result)}; depth={depth}");
            if (volume(result) == 0) return new List<T>();
            // Preserve topology on exact no-op cuts rather than replacing it with an equivalent BREP.
            return new List<T> { volume(result) == volume(panel) ? panel : result };
        }
    }
}
