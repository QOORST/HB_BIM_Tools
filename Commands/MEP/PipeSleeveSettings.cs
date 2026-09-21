using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public class PipeSleeveSettings
    {
        public string DefaultWallSleeveDisplayName { get; set; } = string.Empty;
        public string DefaultFloorSleeveDisplayName { get; set; } = string.Empty;
        public double ClearanceMm { get; set; } = 50.0;
        public bool IncludeCurrentModel { get; set; } = false;
        public bool IncludeLinks { get; set; } = true;
        public bool ExcludeAdditionElements { get; set; } = true;
        public bool UseDiameterMap { get; set; } = true;
        public bool AutoNumber { get; set; } = true;
        public bool UpdateExisting { get; set; } = false;
        public bool HasExplicitSizeList { get; set; } = false;
        public List<PipeSleeveSizeSetting> SizeMappings { get; set; } = new List<PipeSleeveSizeSetting>();
    }

    public class PipeSleeveSizeSetting
    {
        public int NominalDiameterMm { get; set; }
        public string SleeveDisplayName { get; set; } = string.Empty;
    }

    internal static class PipeSleeveSettingsStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PipeSleeveSettings));

        public static string DefaultPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "HB_BIM_Tools", "PipeSleeveSettings.xml");
            }
        }

        public static PipeSleeveSettings Load()
        {
            try
            {
                if (!File.Exists(DefaultPath))
                {
                    return new PipeSleeveSettings();
                }

                using (StreamReader reader = new StreamReader(DefaultPath))
                {
                    return Serializer.Deserialize(reader) as PipeSleeveSettings ?? new PipeSleeveSettings();
                }
            }
            catch
            {
                return new PipeSleeveSettings();
            }
        }

        public static void Save(PipeSleeveSettings settings)
        {
            string error = PipeSleeveNominalRules.Validate((settings?.SizeMappings ?? new List<PipeSleeveSizeSetting>()).ConvertAll(x => x.NominalDiameterMm));
            if (error != null) throw new InvalidOperationException(error);
            string directory = Path.GetDirectoryName(DefaultPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (StreamWriter writer = new StreamWriter(DefaultPath))
            {
                Serializer.Serialize(writer, settings ?? new PipeSleeveSettings());
            }
        }
    }

}
