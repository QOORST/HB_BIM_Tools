using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Helpers.Data
{
    internal static class CobieImportWritePlan
    {
        /// <summary>Same-value duplicates collapse; every write to a conflicting target is rejected.</summary>
        internal static List<T> Resolve<T>(IEnumerable<T> writes, Func<T, string> target,
            Func<T, object> value, out List<T> conflicts, out int duplicates)
        {
            var safe = new List<T>();
            conflicts = new List<T>();
            duplicates = 0;
            foreach (var group in writes.GroupBy(target, StringComparer.Ordinal))
            {
                var first = group.First();
                if (group.Any(w => !Equals(value(w), value(first)))) conflicts.AddRange(group);
                else
                {
                    safe.Add(first);
                    duplicates += group.Count() - 1;
                }
            }
            return safe;
        }
    }
}
