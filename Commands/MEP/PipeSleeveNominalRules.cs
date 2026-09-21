using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.RegularExpressions;
namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class PipeSleeveNominalRules
    {
        internal static readonly Dictionary<int,int> Mapping = new Dictionary<int,int>
        { {25,50},{32,50},{40,50},{50,80},{65,80},{80,100},{100,125},{125,150},{150,200},{200,250} };
        // Small pipe sizes are configurable without assuming a sleeve type.
        internal static IEnumerable<int> ConfigurableSizes => new[] { 15, 20 }.Concat(Mapping.Keys).Distinct().OrderBy(x => x);
        internal static readonly int[] StandardSizes = { 15, 20, 25, 32, 40, 50, 65, 80, 100, 125, 150, 200, 250, 300 };
        internal static string Validate(IEnumerable<int> sizes)
        {
            var values = sizes.ToList();
            if (values.Any(x => x < 1 || x > 10000)) return "DN 必須為 1 至 10000 的整數。";
            if (values.Distinct().Count() != values.Count) return "管徑 DN 不可重複。";
            return null;
        }
        internal static IEnumerable<int> GetSizes(PipeSleeveSettings settings)
        {
            var saved = (settings.SizeMappings ?? new List<PipeSleeveSizeSetting>()).Select(x => x.NominalDiameterMm);
            return (settings.HasExplicitSizeList ? saved : ConfigurableSizes.Concat(saved)).Distinct().OrderBy(x => x);
        }
        internal static int Resolve(double measuredMm, IEnumerable<int> configured)
        {
            if (double.IsNaN(measuredMm) || double.IsInfinity(measuredMm) || measuredMm <= 0) return 0;
            var exact = configured.Distinct().Where(x => Math.Abs(x - measuredMm) <= 0.1).ToList();
            if (exact.Count == 1) return exact[0];
            int standard = StandardSizes.FirstOrDefault(x => Math.Abs(x - measuredMm) <= 0.1);
            if (standard > 0) return standard;
            double[] inchMm = { 12.7, 19.05, 25.4, 31.75, 38.1, 50.8, 63.5, 76.2, 101.6, 127, 152.4, 203.2, 254, 304.8 };
            for (int i = 0; i < inchMm.Length; i++)
                if (Math.Abs(inchMm[i] - measuredMm) <= 0.1) return StandardSizes[i];
            return 0;
        }
        internal static bool Matches(string typeName,int size)
            => Regex.IsMatch(typeName ?? "", @"^\s*"+size+@"\s*A\s*$", RegexOptions.IgnoreCase);
    }
}
