using System;

namespace YDBIM.AutoDimension.Core
{
    internal static class DimensionQuickPolicy
    {
        internal static bool UsesWholeView(DimensionMode? mode)
            => mode == DimensionMode.ColumnSetout || mode == DimensionMode.BeamGrid;

        internal static bool SameMeasurement(string candidateKey, string existingKey, double directionDot)
            => !string.IsNullOrEmpty(candidateKey) && string.Equals(candidateKey, existingKey, StringComparison.Ordinal) &&
                !double.IsNaN(directionDot) && !double.IsInfinity(directionDot) && Math.Abs(directionDot) > .999999;

        internal static bool HasUnresolved(int created, int requested, int existing)
            => requested > 0 ? created + existing < requested : created == 0;
    }
}
