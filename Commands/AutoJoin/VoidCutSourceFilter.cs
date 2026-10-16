using System;
using System.IO;
using System.Xml.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class VoidCutSourceFilter
    {
        internal bool Filtered, Exact;
        internal string Category = "", Family = "", Type = "";
        internal bool Matches(string category, string family, string type) =>
            (string.IsNullOrEmpty(Category) || string.Equals(Category, category, StringComparison.Ordinal)) &&
            Match(Family, family) && Match(Type, type);
        private bool Match(string query, string value)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0) return true;
            return Exact ? string.Equals(query, value, StringComparison.OrdinalIgnoreCase) :
                (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HB_BIM", "AutoJoin", "void-cut-source.xml");
        internal static VoidCutSourceFilter Load(string path)
        {
            try
            {
                var root = XElement.Load(path);
                if (root.Name != "VoidCutSource" || (string)root.Attribute("version") != "1") return new VoidCutSourceFilter();
                return new VoidCutSourceFilter { Filtered = (string)root.Element("Filtered") == "true",
                    Exact = (string)root.Element("Exact") == "true", Category = (string)root.Element("Category") ?? "",
                    Family = (string)root.Element("Family") ?? "", Type = (string)root.Element("Type") ?? "" };
            }
            catch { return new VoidCutSourceFilter(); }
        }
        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                new XElement("VoidCutSource", new XAttribute("version", "1"),
                    new XElement("Filtered", Filtered), new XElement("Exact", Exact),
                    new XElement("Category", Category), new XElement("Family", Family), new XElement("Type", Type)).Save(temp);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
