using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Helpers.Data
{
    /// <summary>Pure, fail-closed matching policy. Supplied identifiers are assertions, never fallback hints.</summary>
    internal sealed class CobieImportIdentity
    {
        public string UniqueId, ElementId, Mark, FamilyName, TypeName;
    }

    internal sealed class CobieImportMatcher
    {
        private readonly Dictionary<string, List<CobieImportIdentity>> uniqueIds;
        private readonly Dictionary<string, List<CobieImportIdentity>> elementIds;
        private readonly Dictionary<string, List<CobieImportIdentity>> marks;

        public CobieImportMatcher(IEnumerable<CobieImportIdentity> elements)
        {
            var snapshot = elements.ToList();
            uniqueIds = Index(snapshot, e => e.UniqueId);
            elementIds = Index(snapshot, e => e.ElementId);
            marks = Index(snapshot, e => e.Mark);
        }

        private static Dictionary<string, List<CobieImportIdentity>> Index(
            IEnumerable<CobieImportIdentity> elements, Func<CobieImportIdentity, string> key)
        {
            return elements.Where(e => !string.IsNullOrWhiteSpace(key(e)))
                .GroupBy(e => Clean(key(e)), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        }

        public CobieImportIdentity Match(CobieImportIdentity input, out string reason)
        {
            reason = null;
            string key;
            Dictionary<string, List<CobieImportIdentity>> index;
            if (!string.IsNullOrWhiteSpace(input.UniqueId)) { key = input.UniqueId; index = uniqueIds; }
            else if (!string.IsNullOrWhiteSpace(input.ElementId))
            {
                // Numeric Revit IDs are document-local and can identify unrelated elements in another model.
                if (string.IsNullOrWhiteSpace(input.Mark) &&
                    (string.IsNullOrWhiteSpace(input.FamilyName) || string.IsNullOrWhiteSpace(input.TypeName)))
                {
                    reason = "ElementId alone is unsafe across models; supply UniqueId, Mark, or both FamilyName and TypeName";
                    return null;
                }
                key = input.ElementId; index = elementIds;
            }
            else if (!string.IsNullOrWhiteSpace(input.Mark)) { key = input.Mark; index = marks; }
            else { reason = "Missing identity: supply UniqueId, ElementId with corroboration, or unique Mark"; return null; }

            if (!index.TryGetValue(Clean(key), out var candidates) || candidates.Count == 0)
            {
                reason = "Supplied identifier not found; no fallback allowed (check source model or stale IDs)";
                return null;
            }
            if (candidates.Count != 1)
            {
                reason = "Ambiguous identifier: " + candidates.Count + " matching elements; no element selected";
                return null;
            }
            var result = candidates[0];
            if (!Agrees(input.UniqueId, result.UniqueId) || !Agrees(input.ElementId, result.ElementId) ||
                !Agrees(input.Mark, result.Mark) || !Agrees(input.FamilyName, result.FamilyName) ||
                !Agrees(input.TypeName, result.TypeName))
            {
                reason = "Identity conflict: supplied UniqueId/ElementId/Mark/FamilyName/TypeName disagree with model";
                return null;
            }
            return result;
        }

        private static string Clean(string value) => (value ?? "").Trim();
        private static bool Agrees(string supplied, string actual) => string.IsNullOrWhiteSpace(supplied) ||
            string.Equals(Clean(supplied), Clean(actual), StringComparison.OrdinalIgnoreCase);
    }
}
