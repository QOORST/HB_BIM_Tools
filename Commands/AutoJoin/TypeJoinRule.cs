using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    public enum TypeJoinMatchMode { Exact = 0, Contains = 1 }

    public sealed class TypeJoinRule
    {
        public string CategoryKey { get; set; } = string.Empty;
        public TypeJoinMatchMode WinnerMatchMode { get; set; } = TypeJoinMatchMode.Exact;
        public TypeJoinMatchMode LoserMatchMode { get; set; } = TypeJoinMatchMode.Exact;
        public string WinnerFamilyName { get; set; } = string.Empty;
        public string WinnerTypeName { get; set; } = string.Empty;
        public string LoserFamilyName { get; set; } = string.Empty;
        public string LoserTypeName { get; set; } = string.Empty;
        public TypeJoinRule Clone() => (TypeJoinRule)MemberwiseClone();
    }

    internal sealed class TypeJoinPolicy
    {
        private readonly IReadOnlyList<TypeJoinRule> _rules;
        private readonly Dictionary<string, int> _resolved = new(StringComparer.Ordinal);

        internal TypeJoinPolicy(IReadOnlyList<TypeJoinRule> rules)
        {
            Validate(rules);
            _rules = rules.Select(x => x.Clone()).ToList();
        }

        // Explicit pairs only; A > B and B > C do not silently authorize A > C.
        internal int Resolve(string category, string familyA, string typeA, string familyB, string typeB)
        {
            if (new[] { category, familyA, typeA, familyB, typeB }.Any(string.IsNullOrWhiteSpace)) return 0;
            string a = Key(category, familyA, typeA), b = Key(category, familyB, typeB);
            if (a == b) return 0;
            string pair = a.Length + ":" + a + b;
            if (_resolved.TryGetValue(pair, out int cached)) return cached;
            bool forward = false, reverse = false;
            foreach (var rule in _rules)
            {
                if (rule.CategoryKey != category) continue;
                forward |= Matches(rule.WinnerMatchMode, rule.WinnerFamilyName, rule.WinnerTypeName, familyA, typeA) &&
                    Matches(rule.LoserMatchMode, rule.LoserFamilyName, rule.LoserTypeName, familyB, typeB);
                reverse |= Matches(rule.WinnerMatchMode, rule.WinnerFamilyName, rule.WinnerTypeName, familyB, typeB) &&
                    Matches(rule.LoserMatchMode, rule.LoserFamilyName, rule.LoserTypeName, familyA, typeA);
            }
            if (forward && reverse) throw new InvalidOperationException("類型配對同時命中相反切割方向，未執行接合。");
            int result = forward ? -1 : reverse ? 1 : 0;
            // Bound cache growth on projects with many distinct type pairs.
            if (_resolved.Count < 10000) _resolved[pair] = result;
            return result;
        }

        internal static bool Matches(TypeJoinMatchMode mode, string familyFilter, string typePattern, string family, string type)
        {
            if (string.IsNullOrWhiteSpace(typePattern) || string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(type)) return false;
            if (mode == TypeJoinMatchMode.Exact) return familyFilter == family && typePattern == type;
            return mode == TypeJoinMatchMode.Contains &&
                (string.IsNullOrWhiteSpace(familyFilter) || familyFilter == family) &&
                type.IndexOf(typePattern.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Overlaps(TypeJoinMatchMode aMode, string aFamily, string aType,
            TypeJoinMatchMode bMode, string bFamily, string bType)
        {
            if (!string.IsNullOrWhiteSpace(aFamily) && !string.IsNullOrWhiteSpace(bFamily) && aFamily != bFamily) return false;
            if (aMode == TypeJoinMatchMode.Exact)
                return Matches(bMode, bFamily, bType, aFamily, aType);
            if (bMode == TypeJoinMatchMode.Exact)
                return Matches(aMode, aFamily, aType, bFamily, bType);
            // Two contains predicates may both match a future type name.
            return true;
        }

        private static string Key(string category, string family, string type)
            => category.Length + ":" + category + family.Length + ":" + family + type.Length + ":" + type;

        public static void Validate(IReadOnlyList<TypeJoinRule> rules)
        {
            if (rules == null) throw new InvalidOperationException("類型規則清單無效。");
            var graph = new Dictionary<int, HashSet<int>>();
            foreach (var rule in rules)
            {
                if (rule == null || !CategoryCatalog.AllKeys.Contains(rule.CategoryKey) ||
                    !ValidSide(rule.WinnerMatchMode, rule.WinnerFamilyName, rule.WinnerTypeName) ||
                    !ValidSide(rule.LoserMatchMode, rule.LoserFamilyName, rule.LoserTypeName))
                    throw new InvalidOperationException("請指定有效類別及完整的族群、類型名稱。");
            }
            for (int i = 0; i < rules.Count; i++)
            {
                graph[i] = new HashSet<int>();
                for (int j = 0; j < rules.Count; j++)
                {
                    var a = rules[i]; var b = rules[j];
                    if (a.CategoryKey == b.CategoryKey && Overlaps(a.LoserMatchMode, a.LoserFamilyName, a.LoserTypeName,
                        b.WinnerMatchMode, b.WinnerFamilyName, b.WinnerTypeName)) graph[i].Add(j);
                }
            }
            var visiting = new HashSet<int>();
            var visited = new HashSet<int>();
            bool Visit(int node)
            {
                if (visited.Contains(node)) return true;
                if (!visiting.Add(node)) return false;
                if (graph.TryGetValue(node, out var neighbors))
                    foreach (int neighbor in neighbors) if (!Visit(neighbor)) return false;
                visiting.Remove(node);
                visited.Add(node);
                return true;
            }
            foreach (int node in graph.Keys)
                if (!Visit(node)) throw new InvalidOperationException("類型接合條件可能重疊、方向衝突或形成循環。請限制族群、改用指定類型或調整規則。");
        }

        private static bool ValidSide(TypeJoinMatchMode mode, string family, string type)
            => !string.IsNullOrWhiteSpace(type) &&
               (mode == TypeJoinMatchMode.Exact ? !string.IsNullOrWhiteSpace(family) : mode == TypeJoinMatchMode.Contains);
    }
}
