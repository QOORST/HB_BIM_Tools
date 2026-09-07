using Autodesk.Revit.DB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class AutoTagSavedSettings
    {
        public AutoTagOptions Options { get; set; } = new AutoTagOptions();

        public List<AutoTagSavedRule> Rules { get; set; } = new List<AutoTagSavedRule>();
    }

    internal sealed class AutoTagSavedRule
    {
        public string CategoryName { get; set; }

        public bool Enabled { get; set; }

        public string TagTypeLabel { get; set; }
    }

    internal static class AutoTagSettingsStore
    {
        private const string AppFolderName = "YD_BIM_Tools";
        private const string FileName = "AutoTagSettings.json";

        public static AutoTagSavedSettings Load(Document doc, AutoTagMode mode)
        {
            try
            {
                AutoTagSettingsData data = ReadData();
                string key = GetProjectKey(doc, mode);
                return data.ProjectSettings.TryGetValue(key, out AutoTagSavedSettings settings)
                    ? settings
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(Document doc, AutoTagMode mode, AutoTagOptions options, IReadOnlyList<AutoTagSavedRule> rules)
        {
            if (doc == null || options == null || rules == null)
                return;

            AutoTagSettingsData data = ReadData();
            data.ProjectSettings[GetProjectKey(doc, mode)] = new AutoTagSavedSettings
            {
                Options = options,
                Rules = rules.ToList()
            };

            string path = GetSettingsPath();
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        public static void Reset(Document doc, AutoTagMode mode)
        {
            AutoTagSettingsData data = ReadData();
            if (!data.ProjectSettings.Remove(GetProjectKey(doc, mode)))
                return;

            string path = GetSettingsPath();
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        private static AutoTagSettingsData ReadData()
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return new AutoTagSettingsData();

            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<AutoTagSettingsData>(json) ?? new AutoTagSettingsData();
        }

        private static string GetProjectKey(Document doc, AutoTagMode mode)
        {
            string projectId = null;

            try
            {
                projectId = doc?.ProjectInformation?.UniqueId;
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(projectId))
                projectId = string.IsNullOrWhiteSpace(doc?.PathName) ? doc?.Title : doc.PathName;

            return $"{mode}|{projectId}";
        }

        private static string GetSettingsPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, AppFolderName, FileName);
        }

        private sealed class AutoTagSettingsData
        {
            public Dictionary<string, AutoTagSavedSettings> ProjectSettings { get; set; } =
                new Dictionary<string, AutoTagSavedSettings>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
