using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.Data;

namespace YD_RevitTools.LicenseManager.Helpers.Data
{
    internal static class CobieConfigIO
    {
        internal static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YD_BIM_Tools", "COBieFieldConfig.xml");

        public static List<CmdCobieFieldManager.CobieFieldConfig> LoadConfig()
        {
            if (!File.Exists(ConfigPath)) return new List<CmdCobieFieldManager.CobieFieldConfig>();
            var list = new List<CmdCobieFieldManager.CobieFieldConfig>();
            var x = XDocument.Load(ConfigPath);
            foreach (var el in x.Root.Elements("Field"))
            {
                var c = new CmdCobieFieldManager.CobieFieldConfig
                {
                    DisplayName = el.Element("DisplayName")?.Value,
                    CobieName = el.Element("CobieName")?.Value,
                    SharedParameterName = el.Element("SharedParameterName")?.Value,
                    SharedParameterGuid = el.Element("SharedParameterGuid")?.Value,
                    IsBuiltIn = bool.Parse(el.Element("IsBuiltIn")?.Value ?? "false"),
                    IsRequired = bool.Parse(el.Element("IsRequired")?.Value ?? "false"),
                    ExportEnabled = bool.Parse(el.Element("ExportEnabled")?.Value ?? "true"),
                    ImportEnabled = bool.Parse(el.Element("ImportEnabled")?.Value ?? "true"),
                    DefaultValue = el.Element("DefaultValue")?.Value,
                    DataType = el.Element("DataType")?.Value ?? "Text",
                    Category = el.Element("Category")?.Value ?? "自定義",
                    IsInstance = bool.Parse(el.Element("IsInstance")?.Value ?? "false")
                };
                var bip = el.Element("BuiltInParam")?.Value;
                if (!string.IsNullOrEmpty(bip)) c.BuiltInParam = (BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), bip);
                // 過濾掉 UniqueId（不提供廠商填寫，不出現在欄位管理器）
                if (string.Equals(c.DisplayName, "UniqueId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.CobieName, "Component.UniqueId", StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(c);
            }
            return list;
        }
    }
}
