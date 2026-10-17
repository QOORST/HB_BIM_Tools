using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.Data.NameOrganizer
{
    internal enum NameKind { Family, Type, View, Material, Template, Filter, Sheet }
    internal enum NameAction { Reload, Apply, Delete, Audit }
    internal sealed class NameRow
    {
        public bool Selected { get; set; }
        public string UniqueId { get; set; }
        public NameKind Kind { get; set; }
        public string KindName => NamePolicy.Label(Kind);
        public string Scope { get; set; }
        public string Context { get; set; }
        public string CurrentName { get; set; }
        public string NewName { get; set; }
        public string Usage { get; set; }
        public string Status { get; set; }
        public bool Changed => !string.Equals(CurrentName, (NewName ?? "").Trim(), StringComparison.Ordinal);
        public NameRow Snapshot() => (NameRow)MemberwiseClone();
    }
    internal static class NamePolicy
    {
        public static string Label(NameKind kind) => new[] { "族群", "類型／系統類型", "視圖", "材質", "視圖樣板", "篩選器", "圖紙名稱" }[(int)kind];
        public static List<NameRow> Targets(IEnumerable<NameRow> visible, bool changedOnly) =>
            visible.Where(r => r.Selected && (!changedOnly || r.Changed)).Select(r => r.Snapshot()).ToList();
        public static void Preview(IEnumerable<NameRow> rows, string find, string replacement, string prefix, string suffix, bool affix)
        {
            if (!affix && string.IsNullOrEmpty(find)) throw new InvalidOperationException("請輸入要尋找的文字。");
            foreach (var row in rows.Where(r => r.Selected))
            {
                // Repeated preview is idempotent; always start from the recorded model name.
                row.NewName = affix ? (prefix ?? "") + row.CurrentName + (suffix ?? "") : row.CurrentName.Replace(find, replacement ?? "");
                row.Status = row.Changed ? "待套用" : "";
            }
        }
        public static List<string> Validate(IReadOnlyList<NameRow> targets, IReadOnlyList<NameRow> current)
        {
            var errors = new List<string>();
            var byId = current.ToDictionary(r => r.UniqueId);
            var targetIds = new HashSet<string>(targets.Select(r => r.UniqueId));
            foreach (var row in targets)
            {
                var name = (row.NewName ?? "").Trim();
                if (!byId.TryGetValue(row.UniqueId, out var fresh) || fresh.CurrentName != row.CurrentName || fresh.Scope != row.Scope)
                    errors.Add(row.CurrentName + "：模型內容已變更，請重新讀取。");
                if (name.Length == 0) errors.Add(row.CurrentName + "：名稱不可空白。");
                if (name.IndexOfAny(new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' }) >= 0)
                    errors.Add(row.CurrentName + "：名稱含不允許的字元。");
                // Revit sheet titles may repeat; sheet numbers are never edited here.
                if (row.Kind == NameKind.Sheet) continue;
                if (targets.Any(other => other.UniqueId != row.UniqueId && other.Scope == row.Scope && string.Equals((other.NewName ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)))
                    errors.Add(row.CurrentName + "：本次新名稱重複。");
                if (current.Any(other => !targetIds.Contains(other.UniqueId) && other.Scope == row.Scope && string.Equals(other.CurrentName, name, StringComparison.OrdinalIgnoreCase)))
                    errors.Add(row.CurrentName + "：模型已有同名項目。");
            }
            return errors;
        }
    }
}
