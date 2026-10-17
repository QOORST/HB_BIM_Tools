using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace YD_RevitTools.LicenseManager.Commands.Drawing.Standards
{
    internal sealed class StandardReference
    {
        public string Kind { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    internal sealed class DrawingStandard
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public int Revision { get; set; }
        public string Note { get; set; }
        public string SavedAt { get; set; }
        public string SavedBy { get; set; }
        public bool Archived { get; set; }
        public List<StandardReference> References { get; set; } = new List<StandardReference>();
        [JsonIgnore] public StandardReference Template => References.SingleOrDefault(r => r.Kind == "視圖樣板");
        public override string ToString() => Name + (Archived ? "（已停用）" : Revision == 0 ? "（尚未設定）" : " · v" + Revision);
    }

    internal sealed class StandardAssignment
    {
        public string ViewUniqueId { get; set; }
        public string StandardKey { get; set; }
        public int Revision { get; set; }
        public string ExceptionReason { get; set; }
        public string UpdatedAt { get; set; }
        public string UpdatedBy { get; set; }
    }

    internal sealed class DrawingStandardData
    {
        public int FormatVersion { get; set; } = 2;
        // JSON must replace seeded entries instead of appending another copy on every reopen.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<StandardDefinition> Definitions { get; set; } = Defaults().Select(s => new StandardDefinition { Key = s.Key, Name = s.Name }).ToList();
        public List<StandardCatalogEvent> CatalogHistory { get; set; } = new List<StandardCatalogEvent>();
        // Revisions are append-only so the definition used at issue time remains available.
        public List<DrawingStandard> Revisions { get; set; } = new List<DrawingStandard>();
        public List<StandardAssignment> Assignments { get; set; } = new List<StandardAssignment>();
        public static DrawingStandard[] Defaults() => new[] {
            new DrawingStandard { Key = "plumbing", Name = "給排水平面" },
            new DrawingStandard { Key = "fire", Name = "消防平面" },
            new DrawingStandard { Key = "sleeve", Name = "套管定位圖" },
            new DrawingStandard { Key = "hvac-duct", Name = "空調風管平面" },
            new DrawingStandard { Key = "hvac-water", Name = "空調水管平面" },
            new DrawingStandard { Key = "electrical", Name = "電力照明平面" },
            new DrawingStandard { Key = "low-voltage", Name = "弱電平面" },
            new DrawingStandard { Key = "special-pipe", Name = "特殊管路" },
            new DrawingStandard { Key = "coordination", Name = "綜合管線圖" }
        };
        public DrawingStandard Latest(string key)
        {
            var definition = Definitions.SingleOrDefault(d => d.Key == key)
                ?? throw new InvalidOperationException("設定組合已不存在，請重新載入清單。");
            var saved = Revisions.Where(r => r.Key == key).OrderByDescending(r => r.Revision).FirstOrDefault();
            // Return a detached value; renaming a catalog entry must not rewrite historical versions.
            var result = saved == null ? new DrawingStandard { Key = key, References = definition.DraftReferences } : saved;
            result = JsonConvert.DeserializeObject<DrawingStandard>(JsonConvert.SerializeObject(result));
            result.Name = definition.Name; result.Archived = definition.Archived;
            return result;
        }
        public void Upgrade()
        {
            if (FormatVersion == 1)
            {
                Definitions = Defaults().Select(s => new StandardDefinition { Key = s.Key, Name = s.Name }).ToList();
                CatalogHistory = new List<StandardCatalogEvent>();
                FormatVersion = 2;
            }
        }
    }

    internal sealed class StandardDefinition
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public bool Archived { get; set; }
        public List<StandardReference> DraftReferences { get; set; } = new List<StandardReference>();
    }
    internal sealed class StandardCatalogEvent
    {
        public string Key { get; set; }
        public string Action { get; set; }
        public string At { get; set; }
        public string By { get; set; }
    }

    internal static class DrawingStandardCatalog
    {
        public static string ValidateName(string name, IEnumerable<string> existing)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 120 || name.Any(char.IsControl))
                throw new InvalidOperationException("名稱須為 1～120 個字，且不得包含換行或控制字元。");
            if (existing.Any(n => string.Equals(n?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("名稱已存在（含停用項目），請使用不同名稱。");
            return name;
        }
        public static string Create(DrawingStandardData data, string name, string sourceKey, string user)
        {
            name = ValidateName(name, data.Definitions.Select(d => d.Name));
            var source = sourceKey == null ? null : data.Latest(sourceKey);
            var definition = new StandardDefinition { Key = Guid.NewGuid().ToString("N"), Name = name,
                DraftReferences = source?.References ?? new List<StandardReference>() };
            data.Definitions.Add(definition);
            Log(data, definition.Key, source == null ? "新增組合：" + name : "複製組合：" + source.Name + " → " + name, user);
            return definition.Key;
        }
        public static void Rename(DrawingStandardData data, string key, string name, string user)
        {
            var definition = Get(data, key);
            name = ValidateName(name, data.Definitions.Where(d => d.Key != key).Select(d => d.Name));
            var old = definition.Name; definition.Name = name;
            Log(data, key, "組合改名：" + old + " → " + name, user);
        }
        public static void SetArchived(DrawingStandardData data, string key, bool archived, string user)
        {
            var definition = Get(data, key);
            definition.Archived = archived;
            Log(data, key, (archived ? "停用組合：" : "啟用組合：") + definition.Name, user);
        }
        private static StandardDefinition Get(DrawingStandardData data, string key) => data.Definitions.SingleOrDefault(d => d.Key == key)
            ?? throw new InvalidOperationException("設定組合已不存在。");
        private static void Log(DrawingStandardData data, string key, string action, string user) => data.CatalogHistory.Add(new StandardCatalogEvent {
            Key = key, Action = action, By = user, At = DateTimeOffset.Now.ToString("o")
        });
    }

    internal sealed class TemplateUsage
    {
        public string ViewUniqueId { get; set; }
        public string ViewName { get; set; }
        public string Sheets { get; set; }
    }
    internal sealed class TemplateInventoryItem
    {
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string ViewType { get; set; }
        public bool CanDuplicate { get; set; }
        public List<TemplateUsage> Uses { get; set; } = new List<TemplateUsage>();
        public List<string> StandardNames { get; set; } = new List<string>();
        public int ViewCount => Uses.Count;
    }
    internal sealed class TemplateInventory
    {
        public string ActiveViewUniqueId { get; set; }
        public string ActiveViewName { get; set; }
        public bool CanCreateFromActive { get; set; }
        public List<TemplateInventoryItem> Templates { get; set; } = new List<TemplateInventoryItem>();
    }
    internal sealed class TemplateCreationRequest
    {
        public bool FromCurrentView { get; set; }
        public string SourceUniqueId { get; set; }
        public string Name { get; set; }
    }
    internal sealed class DrawingStandardManagement
    {
        public Func<string, string, string> Create { get; set; }
        public Action<string, string> Rename { get; set; }
        public Action<string, bool> Archive { get; set; }
        public Func<TemplateInventory> Inventory { get; set; }
        public Func<TemplateCreationRequest, string> CreateTemplate { get; set; }
    }

    internal sealed class StandardChoice
    {
        public string Kind { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    internal sealed class StandardViewRow
    {
        public bool Apply { get; set; }
        public string UniqueId { get; set; }
        public string ViewName { get; set; }
        public string Sheets { get; set; }
        public string CurrentTemplate { get; set; }
        public string CurrentTemplateUniqueId { get; set; }
        public string TargetTemplate { get; set; }
        public string Action { get; set; }
        public string Issues { get; set; }
        public string ExceptionReason { get; set; }
        public string BlockReason { get; set; }
    }

    internal sealed class StandardPreview
    {
        public string StandardKey { get; set; }
        public int Revision { get; set; }
        public string TargetTemplate { get; set; }
        public List<StandardViewRow> Rows { get; set; } = new List<StandardViewRow>();
        public List<string> DefinitionIssues { get; set; } = new List<string>();
        public string Detail { get; set; }
    }

    internal static class DrawingStandardPolicy
    {
        public static List<string> Compare(StandardReference baseline, StandardReference current)
        {
            if (current == null) return new List<string> { baseline.Kind + "「" + baseline.Name + "」已刪除或無法讀取" };
            if (baseline.UniqueId != current.UniqueId || baseline.Kind != current.Kind)
                return new List<string> { baseline.Kind + "「" + baseline.Name + "」身分不符" };
            return baseline.Values.Keys.Union(current.Values.Keys).OrderBy(k => k, StringComparer.Ordinal)
                .Where(k => Get(baseline.Values, k) != Get(current.Values, k))
                .Select(k => baseline.Kind + "「" + baseline.Name + "」 / " + k + ": " +
                    Get(baseline.Values, k) + " → " + Get(current.Values, k)).ToList();
        }
        private static string Get(Dictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var value) ? value : "（無）";

        public static List<StandardViewRow> ValidateApply(StandardPreview preview, DrawingStandard latest)
        {
            if (preview == null || latest == null || latest.Revision == 0)
                throw new InvalidOperationException("請先儲存設定組合並產生預覽。");
            if (latest.Archived) throw new InvalidOperationException("此設定組合已停用，請先啟用才能套用。");
            if (preview.StandardKey != latest.Key || preview.Revision != latest.Revision)
                throw new InvalidOperationException("設定組合版本已變更，請重新預覽。");
            if (preview.DefinitionIssues.Count > 0)
                throw new InvalidOperationException("標準來源已變更或缺少內容，請先檢查差異，再儲存新版。");
            var selected = preview.Rows.Where(r => r.Apply).ToList();
            if (selected.Count == 0) throw new InvalidOperationException("請勾選要套用或登錄例外的視圖。");
            if (selected.Select(r => r.UniqueId).Distinct().Count() != selected.Count)
                throw new InvalidOperationException("視圖清單重複，請重新預覽。");
            if (selected.Any(r => string.IsNullOrWhiteSpace(r.UniqueId)))
                throw new InvalidOperationException("視圖身分無效，請重新預覽。");
            var blocked = selected.FirstOrDefault(r => !string.IsNullOrEmpty(r.BlockReason) && string.IsNullOrWhiteSpace(r.ExceptionReason));
            if (blocked != null) throw new InvalidOperationException(blocked.ViewName + "：" + blocked.BlockReason);
            return selected;
        }

        public static string AssignmentIssue(StandardAssignment assignment, DrawingStandard standard)
        {
            if (assignment == null) return "尚未登錄此設定組合";
            if (assignment.StandardKey != standard.Key) return "已登錄其他用途；勾選套用將改為本用途";
            if (!string.IsNullOrWhiteSpace(assignment.ExceptionReason))
                return "專案例外（v" + assignment.Revision + "）：" + assignment.ExceptionReason +
                    (assignment.Revision == standard.Revision ? "" : "；版本已更新，需重新確認例外");
            return assignment.Revision == standard.Revision ? "" : "使用舊版 v" + assignment.Revision + "，目前 v" + standard.Revision;
        }
    }
}
