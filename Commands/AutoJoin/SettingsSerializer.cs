using System;
using System.IO;
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
            var serializer = new XmlSerializer(typeof(AutoJoinSettings));
            if (serializer.Deserialize(stream) is AutoJoinSettings settings)
            {
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
