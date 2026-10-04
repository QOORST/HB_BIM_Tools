using System;
using System.Collections.Generic;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveIssueRequest
    {
        internal SleeveQuantityOptions Options;
        internal string BaselineName;
        internal Dictionary<long, string> Controls = new Dictionary<long, string>();
        internal static bool ValidControl(string value) => value == "納入" || value == "排除" || value == "作廢";
        internal static string ValidateName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 80) throw new InvalidOperationException("請輸入 1～80 字的發圖基準名稱。");
            return name;
        }
        internal static SleeveQuantityRow Preview(SleeveQuantityRow row, string control) => new SleeveQuantityRow
        {
            Id = row.Id, LevelKey = row.LevelKey, Level = row.Level, Zone = row.Zone, System = row.System,
            Type = row.Type, Specification = row.Specification, Length = row.Length, Material = row.Material,
            Host = row.Host, Mark = row.Mark, Elevation = row.Elevation, PipeDn = row.PipeDn,
            Control = control, Included = control == "納入",
            Status = (row.Status ?? "").StartsWith("統計設定無效", StringComparison.Ordinal) ? "統計設定已修正／其他檢核待重新確認" : row.Status
        };
    }
}
