using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    public sealed class AutoJoinSettings
{
    private const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public AutoJoinScope Scope { get; set; } = AutoJoinScope.VisibleInView;

    public List<string> EnabledCategoryKeys { get; set; } = CategoryCatalog.DefaultEnabledKeys.ToList();

    public List<string> PriorityKeys { get; set; } = CategoryCatalog.DefaultPriorityKeys.ToList();

    public bool AllowSameCategoryJoin { get; set; } = false;

    public bool AllowStructuralNonStructuralJoin { get; set; } = true;

    public bool CheckInDetail { get; set; } = false;

    public void Normalize()
    {
        var validKeys = new HashSet<string>(CategoryCatalog.AllKeys, StringComparer.Ordinal);

        // One-time migration for old XML files created before the 5-category preset.
        if (SchemaVersion < CurrentSchemaVersion)
        {
            EnabledCategoryKeys = CategoryCatalog.DefaultEnabledKeys.ToList();
            PriorityKeys = CategoryCatalog.DefaultPriorityKeys.ToList();
            SchemaVersion = CurrentSchemaVersion;
        }

        EnabledCategoryKeys = (EnabledCategoryKeys ?? CategoryCatalog.DefaultEnabledKeys.ToList())
            .Where(k => validKeys.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (EnabledCategoryKeys.Count == 0)
        {
            EnabledCategoryKeys = CategoryCatalog.DefaultEnabledKeys.ToList();
        }

        var normalizedPriority = (PriorityKeys ?? CategoryCatalog.DefaultPriorityKeys.ToList())
            .Where(k => validKeys.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedPriority.Count == 0)
        {
            normalizedPriority = CategoryCatalog.DefaultPriorityKeys.ToList();
        }

        foreach (var key in CategoryCatalog.DefaultPriorityKeys)
        {
            if (!normalizedPriority.Contains(key, StringComparer.Ordinal))
            {
                normalizedPriority.Add(key);
            }
        }

        PriorityKeys = normalizedPriority;
        SchemaVersion = CurrentSchemaVersion;
    }
    }
}
