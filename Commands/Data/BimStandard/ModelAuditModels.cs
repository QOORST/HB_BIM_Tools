using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.Data.BimStandard
{
    internal enum ModelAuditGroup { Project, Naming, Families }

    internal sealed class ModelAuditOptions
    {
        public string FamilyPrefix { get; set; } = "";
        public string TemplatePrefix { get; set; } = "";
        public string FilterPrefix { get; set; } = "";
        public string MaterialPrefix { get; set; } = "";
        public bool ReadabilityHints { get; set; } = true;
        public ModelAuditOptions Copy() => (ModelAuditOptions)MemberwiseClone();
        public static bool PrefixMismatch(string name, string prefix) => !string.IsNullOrWhiteSpace(prefix) &&
            !(name ?? "").StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase);
        public string Description => "族群前綴：" + Show(FamilyPrefix) + "；樣板前綴：" + Show(TemplatePrefix) +
            "；篩選器前綴：" + Show(FilterPrefix) + "；材質前綴：" + Show(MaterialPrefix) + "；名稱可讀性提醒：" + (ReadabilityHints ? "開啟" : "關閉");
        private static string Show(string text) => string.IsNullOrWhiteSpace(text) ? "不檢查" : text.Trim();
        public static string GroupName(ModelAuditGroup group) => group == ModelAuditGroup.Project ? "專案資料" : group == ModelAuditGroup.Naming ? "命名規則（選用）" : "族群與類型";
    }

    internal sealed class ModelAuditContext
    {
        internal ModelAuditGroup Group { get; set; }
        internal ModelAuditOptions Options { get; set; } = new ModelAuditOptions();
    }

    internal sealed class ModelAuditIssue
    {
        public bool NeedsAction { get; set; }
        public bool CanOrganize { get; set; }
        public string Category { get; set; }
        public string ElementName { get; set; }
        public string Rule { get; set; }
        public string Message { get; set; }
        public string Recommendation { get; set; }
        public string UniqueId { get; set; }
        public string ElementId { get; set; }
        public string IgnoredReason { get; set; } = "";
        public bool Ignored => !string.IsNullOrWhiteSpace(IgnoredReason);
        public string Status => Ignored ? "已略過" : NeedsAction ? "需處理" : "建議確認";
        // Include finding content: changed values must be reviewed again instead of silently ignored.
        public string ReviewKey => Part(UniqueId) + Part(Rule) + Part(Message);
        private static string Part(string text) => (text ?? "").Length + ":" + text;
    }

    internal sealed class ModelAuditReview
    {
        private readonly Dictionary<string, string> _reasons = new Dictionary<string, string>();
        public void Restore(IEnumerable<ModelAuditIssue> rows)
        {
            foreach (var row in rows) row.IgnoredReason = _reasons.TryGetValue(row.ReviewKey, out var reason) ? reason : "";
        }
        public void Ignore(ModelAuditIssue row, string reason)
        {
            if (row == null || string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("請填寫本次略過的原因。");
            row.IgnoredReason = reason.Trim(); _reasons[row.ReviewKey] = row.IgnoredReason;
        }
        public void Reopen(ModelAuditIssue row)
        {
            if (row == null) return;
            _reasons.Remove(row.ReviewKey); row.IgnoredReason = "";
        }
        public static string Summary(IReadOnlyList<ModelAuditIssue> rows) => rows.Count == 0 ?
            "本次未發現符合已啟用規則的待確認項目。" :
            $"需處理 {rows.Count(r => !r.Ignored && r.NeedsAction)}｜建議確認 {rows.Count(r => !r.Ignored && !r.NeedsAction)}｜本次略過 {rows.Count(r => r.Ignored)}";
    }
}
