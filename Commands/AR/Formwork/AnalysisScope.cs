using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    internal static class AnalysisScope
    {
        internal static HashSet<long> ResolveCandidateIds(
            IEnumerable<long> candidateIds,
            IEnumerable<long> requestedTargetIds)
        {
            var candidates = new HashSet<long>(candidateIds ?? Enumerable.Empty<long>());

            // null 表示沿用完整分析；空集合表示本次沒有可分析的指定宿主。
            if (requestedTargetIds == null)
                return candidates;

            candidates.IntersectWith(requestedTargetIds);
            return candidates;
        }
    }
}
