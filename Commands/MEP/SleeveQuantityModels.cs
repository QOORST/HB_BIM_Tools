using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveQuantityRow
    {
        internal ElementId Id;
        internal string LevelKey;
        internal string Control;
        internal bool Included;
        public string Level { get; set; }
        public string Zone { get; set; }
        public string System { get; set; }
        public string Type { get; set; }
        public string Specification { get; set; }
        public string Length { get; set; }
        public string Status { get; set; }
        public string Mark { get; set; }
        public string Elevation { get; set; }
        public string PipeDn { get; set; }
        public string Material { get; set; }
        public string Host { get; set; }
        internal string GroupKey => SleeveQuantityPolicy.Key(new[] { LevelKey, Zone, System, Type, Specification, Length, Material, Host, Status });
    }

    internal sealed class SleeveQuantityOptions
    {
        internal string LevelKey = "";
        internal string LevelName = "整案";
        internal string Zone = "";
        internal bool Detailed;
        internal ElementId SheetId = ElementId.InvalidElementId;
        internal double LeftMm = 20;
        internal double TopMm = 20;
        internal HashSet<long> ManualIds = new HashSet<long>();
        internal bool Matches(SleeveQuantityRow row) => row.Included && (LevelKey.Length == 0 || row.LevelKey == LevelKey) && (Zone.Length == 0 || row.Zone == Zone);
        internal string Key => SleeveQuantityPolicy.Key(new[] { LevelKey, Zone, Detailed ? "detail" : "summary" });
    }
}
