using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

#nullable enable

namespace YDBIM.AutoDimension.Core
{

internal sealed class AutoDimensionSavedSettings
{
    public DimensionMode ModeType { get; set; } = DimensionMode.ColumnSetout;

    public PlacementMode Mode { get; set; } = PlacementMode.Auto;

    public PlacementDirection Direction { get; set; } = PlacementDirection.NorthEast;

    public LeftRightSide ColumnLeftRightSide { get; set; } = LeftRightSide.None;

    public FrontBackSide ColumnFrontBackSide { get; set; } = FrontBackSide.Front;

    public double OffsetMm { get; set; } = 900.0;

    public double GridPrimaryOffsetMm { get; set; } = 1000.0;

    public double GridOverallOffsetMm { get; set; } = 2000.0;

    public string? DimensionTypeName { get; set; }

    public List<int> SelectedHorizontalGridIds { get; set; } = new List<int>();

    public List<int> SelectedVerticalGridIds { get; set; } = new List<int>();

    public static AutoDimensionSavedSettings FromOptions(DimensionOptions options)
    {
        return new AutoDimensionSavedSettings
        {
            ModeType = options.ModeType,
            Mode = options.Mode,
            Direction = options.Direction,
            ColumnLeftRightSide = options.ColumnLeftRightSide,
            ColumnFrontBackSide = options.ColumnFrontBackSide,
            OffsetMm = UnitUtils.ConvertFromInternalUnits(options.OffsetInternal, UnitTypeId.Millimeters),
            GridPrimaryOffsetMm = UnitUtils.ConvertFromInternalUnits(options.GridPrimaryOffsetInternal, UnitTypeId.Millimeters),
            GridOverallOffsetMm = UnitUtils.ConvertFromInternalUnits(options.GridOverallOffsetInternal, UnitTypeId.Millimeters),
            DimensionTypeName = options.DimensionTypeName,
            SelectedHorizontalGridIds = options.SelectedHorizontalGridIds.Select(ElementIdCompat.ToInt32).ToList(),
            SelectedVerticalGridIds = options.SelectedVerticalGridIds.Select(ElementIdCompat.ToInt32).ToList()
        };
    }
}

internal static class AutoDimensionSettingsStore
{
    private const string AppFolderName = "HB_BIM_Tools";
    private const string LegacyAppFolderName = "YD_BIM_Tools";
    private const string FileName = "AutoDimensionSettings.json";

    public static AutoDimensionSavedSettings? Load(Document doc, DimensionMode mode)
    {
        try
        {
            AutoDimensionSettingsData data = ReadData();
            string key = GetProjectKey(doc, mode);
            return data.ProjectSettings.TryGetValue(key, out AutoDimensionSavedSettings settings)
                ? settings
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyDictionary<DimensionMode, AutoDimensionSavedSettings> LoadAll(Document doc)
    {
        var result = new Dictionary<DimensionMode, AutoDimensionSavedSettings>();
        foreach (DimensionMode mode in Enum.GetValues(typeof(DimensionMode)).Cast<DimensionMode>())
        {
            AutoDimensionSavedSettings? settings = Load(doc, mode);
            if (settings is not null)
            {
                result[mode] = settings;
            }
        }

        return result;
    }

    public static void Save(Document doc, DimensionOptions options)
    {
        if (doc is null || options is null)
        {
            return;
        }

        AutoDimensionSettingsData data = ReadData();
        data.ProjectSettings[GetProjectKey(doc, options.ModeType)] = AutoDimensionSavedSettings.FromOptions(options);

        string path = GetSettingsPath();
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    private static AutoDimensionSettingsData ReadData()
    {
        string path = GetSettingsPath();
        if (!File.Exists(path))
        {
            string legacyPath = GetSettingsPath(LegacyAppFolderName);
            if (!File.Exists(legacyPath))
            {
                return new AutoDimensionSettingsData();
            }

            string legacyJson = File.ReadAllText(legacyPath);
            AutoDimensionSettingsData legacyData =
                JsonConvert.DeserializeObject<AutoDimensionSettingsData>(legacyJson) ?? new AutoDimensionSettingsData();
            TryMigrateLegacySettings(path, legacyJson);
            return legacyData;
        }

        string json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<AutoDimensionSettingsData>(json) ?? new AutoDimensionSettingsData();
    }

    private static string GetProjectKey(Document? doc, DimensionMode mode)
    {
        if (doc is null)
        {
            return $"{mode}|UnsavedProject";
        }

        string? projectId = null;
        try
        {
            projectId = doc!.ProjectInformation?.UniqueId;
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            string pathName = doc!.PathName ?? string.Empty;
            string title = doc!.Title ?? string.Empty;
            projectId = string.IsNullOrWhiteSpace(pathName) ? title : pathName;
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            projectId = "UnsavedProject";
        }

        return $"{mode}|{projectId}";
    }

    private static string GetSettingsPath()
    {
        return GetSettingsPath(AppFolderName);
    }

    private static string GetSettingsPath(string appFolderName)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, appFolderName, FileName);
    }

    private static void TryMigrateLegacySettings(string targetPath, string json)
    {
        try
        {
            string? folder = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (!File.Exists(targetPath))
            {
                File.WriteAllText(targetPath, json);
            }
        }
        catch
        {
            // The legacy settings remain usable even when migration is not writable.
        }
    }

    private sealed class AutoDimensionSettingsData
    {
        public Dictionary<string, AutoDimensionSavedSettings> ProjectSettings { get; set; } =
            new Dictionary<string, AutoDimensionSavedSettings>(StringComparer.OrdinalIgnoreCase);
    }
}
}
