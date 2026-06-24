using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class CategorySpec
{
    public CategorySpec(string key, string displayName, BuiltInCategory builtInCategory, bool isStructural)
    {
        Key = key;
        DisplayName = displayName;
        BuiltInCategory = builtInCategory;
        IsStructural = isStructural;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public BuiltInCategory BuiltInCategory { get; }

    public bool IsStructural { get; }
}

internal static class CategoryCatalog
{
    public static readonly IReadOnlyList<CategorySpec> Specs = new List<CategorySpec>
    {
        new("Ceiling", "Ceiling", BuiltInCategory.OST_Ceilings, false),
        new("Column", "Column", BuiltInCategory.OST_Columns, false),
        new("Floor", "Floor", BuiltInCategory.OST_Floors, false),
        new("GenericModel", "Generic Model", BuiltInCategory.OST_GenericModel, false),
        new("Roof", "Roof", BuiltInCategory.OST_Roofs, false),
        new("Wall", "Wall", BuiltInCategory.OST_Walls, false),
        new("StructuralColumn", "Structural Column", BuiltInCategory.OST_StructuralColumns, true),
        new("StructuralFloor", "Structural Floor", BuiltInCategory.OST_Floors, true),
        new("StructuralFoundation", "Structural Foundation", BuiltInCategory.OST_StructuralFoundation, true),
        new("StructuralFraming", "Structural Framing", BuiltInCategory.OST_StructuralFraming, true),
        new("StructuralWall", "Structural Wall", BuiltInCategory.OST_Walls, true),
    };

    public static readonly IReadOnlyList<string> AllKeys = Specs.Select(s => s.Key).ToList();

    public static readonly IReadOnlyList<string> DefaultEnabledKeys = new List<string>
    {
        "StructuralFoundation",
        "StructuralColumn",
        "StructuralFraming",
        "Floor",
        "Wall",
    };

    public static readonly IReadOnlyList<string> DefaultPriorityKeys = new List<string>
    {
        "StructuralFoundation",
        "StructuralColumn",
        "StructuralFraming",
        "Floor",
        "Wall",
    };

    public static readonly IReadOnlyList<string> LegacyDefaultPriorityKeys = new List<string>
    {
        "StructuralColumn",
        "StructuralFraming",
        "StructuralFloor",
        "StructuralWall",
        "StructuralFoundation",
        "Column",
        "Floor",
        "Wall",
        "Ceiling",
        "Roof",
        "GenericModel",
    };

    public static readonly IReadOnlyList<string> PreviousDefaultPriorityKeysV2 = new List<string>
    {
        "StructuralColumn",
        "Column",
        "StructuralFraming",
        "StructuralFloor",
        "Floor",
        "StructuralWall",
        "Wall",
        "StructuralFoundation",
        "Ceiling",
        "Roof",
        "GenericModel",
    };

    public static CategorySpec ByKey(string key)
    {
        return Specs.First(s => s.Key == key);
    }

    public static bool IsSupportedBuiltInCategory(BuiltInCategory bic)
    {
        return Specs.Any(s => s.BuiltInCategory == bic);
    }
    }
}
