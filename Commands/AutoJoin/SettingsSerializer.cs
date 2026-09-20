using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal static class SettingsSerializer
{
    public static string DefaultPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "HB_BIM", "AutoJoin", "settings.xml");
        }
    }

    public static AutoJoinSettings LoadOrDefault(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AutoJoinSettings();
            }

            using var stream = File.OpenRead(path);
            var xml = XDocument.Load(stream);
            var serializer = new XmlSerializer(typeof(AutoJoinSettings));
            using var reader = xml.CreateReader();
            if (serializer.Deserialize(reader) is AutoJoinSettings settings)
            {
                // XmlSerializer appends to initialized lists. Replace with the saved
                // collections so defaults cannot re-enable categories or reorder priority.
                var enabled = xml.Root?.Element(nameof(AutoJoinSettings.EnabledCategoryKeys));
                var priority = xml.Root?.Element(nameof(AutoJoinSettings.PriorityKeys));
                if (enabled != null) settings.EnabledCategoryKeys = enabled.Elements("string").Select(x => x.Value).ToList();
                if (priority != null) settings.PriorityKeys = priority.Elements("string").Select(x => x.Value).ToList();
                settings.Normalize();
                return settings;
            }
        }
        catch
        {
            // Swallow and fallback to defaults.
        }

        return new AutoJoinSettings();
    }

    public static void Save(string path, AutoJoinSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        settings.Normalize();
        using var stream = File.Create(path);
        var serializer = new XmlSerializer(typeof(AutoJoinSettings));
        serializer.Serialize(stream, settings);
    }
    }
}
