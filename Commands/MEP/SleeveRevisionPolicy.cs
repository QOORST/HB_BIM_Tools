using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveRevisionRecord
    {
        public string Id { get; set; }
        public string Mark { get; set; }
        public string LevelKey { get; set; }
        public string Level { get; set; }
        public string Zone { get; set; }
        public string System { get; set; }
        public string Type { get; set; }
        public string Specification { get; set; }
        public string Length { get; set; }
        public string Material { get; set; }
        public string Host { get; set; }
        public string Status { get; set; }
        public string Control { get; set; }
        public bool Included { get; set; }
        public bool Recognized { get; set; } = true;
        public double[] PositionMm { get; set; }
        public double[] Orientation { get; set; }
    }
    internal sealed class SleeveRevisionSnapshot
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
        public List<SleeveRevisionRecord> Records { get; set; } = new List<SleeveRevisionRecord>();
        public override string ToString() => Name + "（" + CapturedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm") + "）";
    }
    internal sealed class SleeveRevisionChange
    {
        internal SleeveRevisionRecord Before, After;
        internal string Id => (After ?? Before).Id;
        public string Mark => After?.Mark ?? Before?.Mark ?? "";
        public string Changes { get; internal set; }
        public int QuantityDelta => (After?.Included == true ? 1 : 0) - (Before?.Included == true ? 1 : 0);
        public string BeforeDetail => SleeveRevisionPolicy.Describe(Before);
        public string AfterDetail => SleeveRevisionPolicy.Describe(After);
    }
    internal static class SleeveRevisionPolicy
    {
        private static bool Equal(string a, string b) => (a ?? "") == (b ?? "");
        private static bool Moved(double[] a, double[] b, double tolerance)
        {
            if (a == null || b == null) return a != b;
            if (a.Length != b.Length) return true;
            return Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum()) > tolerance;
        }
        internal static void Validate(SleeveRevisionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Version != 1 || snapshot.Records == null ||
                snapshot.Records.Any(r => r == null || string.IsNullOrEmpty(r.Id)) ||
                snapshot.Records.GroupBy(r => r.Id).Any(g => g.Count() != 1) ||
                snapshot.Records.Any(r => (r.PositionMm != null && (r.PositionMm.Length != 3 || r.PositionMm.Any(v => double.IsNaN(v) || double.IsInfinity(v)))) ||
                    (r.Orientation != null && (r.Orientation.Length != 9 || r.Orientation.Any(v => double.IsNaN(v) || double.IsInfinity(v))))))
                throw new InvalidOperationException("套管基準版本不支援或資料不完整，已停止比對。");
        }
        internal static List<SleeveRevisionChange> Compare(SleeveRevisionSnapshot before, SleeveRevisionSnapshot after)
        {
            Validate(before); Validate(after);
            var old = before.Records.ToDictionary(r => r.Id); var current = after.Records.ToDictionary(r => r.Id);
            var result = new List<SleeveRevisionChange>();
            foreach (string id in old.Keys.Union(current.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                old.TryGetValue(id, out var a); current.TryGetValue(id, out var b);
                var changes = new List<string>();
                if (a == null) changes.Add("新增");
                else if (b == null) changes.Add("刪除");
                else
                {
                    if (a.Recognized != b.Recognized) changes.Add(b.Recognized ? "恢復辨識" : "失去套管辨識");
                    if (a.Included != b.Included) changes.Add(b.Included ? "恢復納入" : b.Control == "作廢" ? "作廢" : "排除");
                    else if (!Equal(a.Control, b.Control)) changes.Add(b.Control == "作廢" ? "作廢" : "統計設定");
                    if (!Equal(a.Mark, b.Mark)) changes.Add("編號");
                    if (a.Recognized && b.Recognized)
                    {
                    if (Moved(a.PositionMm, b.PositionMm, 0.5)) changes.Add("移動");
                    if (Moved(a.Orientation, b.Orientation, 0.000001)) changes.Add("方向");
                    if (!Equal(a.Specification, b.Specification) || !Equal(a.Length, b.Length)) changes.Add("尺寸");
                    if (!Equal(a.Type, b.Type)) changes.Add("族型");
                    if (!Equal(a.Material, b.Material)) changes.Add("材質");
                    if (!Equal(a.LevelKey, b.LevelKey) || !Equal(a.Level, b.Level)) changes.Add("約束樓層");
                    if (!Equal(a.Zone, b.Zone)) changes.Add("區域");
                    if (!Equal(a.System, b.System)) changes.Add("系統");
                    if (!Equal(a.Host, b.Host)) changes.Add("穿越構件");
                    }
                    if (!Equal(a.Status, b.Status)) changes.Add("檢核狀態");
                }
                if (changes.Count > 0) result.Add(new SleeveRevisionChange { Before = a, After = b, Changes = string.Join("／", changes) });
            }
            if (result.Sum(r => r.QuantityDelta) != after.Records.Count(r => r.Included) - before.Records.Count(r => r.Included))
                throw new InvalidOperationException("版本差量數量不平衡，已停止輸出。");
            return result.OrderBy(r => r.Mark, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        }
        internal static string Position(SleeveRevisionRecord r) => r?.PositionMm == null ? "無座標" :
            string.Join(",", r.PositionMm.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));
        internal static string Describe(SleeveRevisionRecord r) => r == null ? "不存在" :
            string.Join("；", new[] { r.Mark, r.Level, r.Zone, r.System, r.Type, r.Specification, "L=" + r.Length,
                r.Material, r.Host, r.Included ? "納入" : "未納入(" + r.Control + ")", r.Status, "XYZ(mm)=" + Position(r) });
        internal static string CsvCell(string value)
        {
            string s = value ?? "";
            string lead = s.TrimStart();
            if (lead.Length > 0 && "=+-@".IndexOf(lead[0]) >= 0 || s.StartsWith("\t") || s.StartsWith("\r") || s.StartsWith("\n")) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        internal static string Csv(SleeveRevisionSnapshot baseline, SleeveRevisionSnapshot current)
        {
            var rows = Compare(baseline, current);
            var sb = new StringBuilder();
            sb.AppendLine("基準,比較時間UTC,識別碼,編號,變更,支數增減,基準內容,目前內容,基準方向,目前方向");
            foreach (var r in rows)
                sb.AppendLine(string.Join(",", new[] { baseline.Name, current.CapturedUtc.ToString("o"), r.Id, r.Mark, r.Changes,
                    r.QuantityDelta.ToString(CultureInfo.InvariantCulture), r.BeforeDetail, r.AfterDetail,
                    Orientation(r.Before), Orientation(r.After) }.Select((value, index) => index == 5 ? value : CsvCell(value))));
            return sb.ToString();
        }
        private static string Orientation(SleeveRevisionRecord r) => r?.Orientation == null ? "" :
            string.Join(",", r.Orientation.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    }
}
