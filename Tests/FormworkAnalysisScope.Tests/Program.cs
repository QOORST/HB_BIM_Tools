using System;
using System.Collections.Generic;
using System.Linq;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;

internal static class Program
{
    private static int Main()
    {
        AssertIds("selected subset", new long[] { 2, 4 },
            AnalysisScope.ResolveCandidateIds(new long[] { 1, 2, 3, 4 }, new long[] { 2, 4 }));

        AssertIds("empty requested scope", Array.Empty<long>(),
            AnalysisScope.ResolveCandidateIds(new long[] { 1, 2, 3 }, Array.Empty<long>()));

        AssertIds("full analysis", new long[] { 1, 2, 3 },
            AnalysisScope.ResolveCandidateIds(new long[] { 1, 2, 3 }, null));

        AssertIds("active-view boundary", new long[] { 2 },
            AnalysisScope.ResolveCandidateIds(new long[] { 1, 2 }, new long[] { 2, 3 }));

        // 模擬樓梯已由呼叫端展開為梯段/平台；父樓梯不得偷偷回到分析集合。
        AssertIds("expanded stair parts", new long[] { 101, 102 },
            AnalysisScope.ResolveCandidateIds(new long[] { 100, 101, 102, 200 }, new long[] { 101, 102 }));

        Console.WriteLine("All Formwork AnalysisScope tests passed.");
        return 0;
    }

    private static void AssertIds(string name, IEnumerable<long> expected, IEnumerable<long> actual)
    {
        var expectedIds = expected.OrderBy(x => x).ToArray();
        var actualIds = actual.OrderBy(x => x).ToArray();
        if (!expectedIds.SequenceEqual(actualIds))
        {
            throw new InvalidOperationException(
                $"{name}: expected [{string.Join(",", expectedIds)}], actual [{string.Join(",", actualIds)}]");
        }
    }
}
