using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class PipeSleeveNominalRules
    {
        internal static readonly Dictionary<int,int> Mapping = new Dictionary<int,int>
        { {25,50},{32,50},{40,50},{50,80},{65,80},{80,100},{100,125},{125,150},{150,200},{200,250} };
        internal static bool Matches(string typeName,int size)
            => Regex.IsMatch(typeName ?? "", @"^\s*"+size+@"\s*A\s*$", RegexOptions.IgnoreCase);
    }
}
