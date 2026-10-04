using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveNumberItem
    {
        public string Id { get; set; }
        public string Mark { get; set; }
        // Set only for instances created by the current placement operation, never inferred from a mark value.
        public bool CreatedInOperation { get; set; }
    }
    internal sealed class SleeveNumberLedger
    {
        public long LastNumber { get; set; }
        public Dictionary<string, string> Owners { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Assigned { get; set; } = new Dictionary<string, string>();
    }
    internal sealed class SleeveNumberPlan
    {
        internal SleeveNumberLedger Ledger;
        internal Dictionary<string, string> Writes = new Dictionary<string, string>();
    }
    internal static class SleeveNumberPolicy
    {
        internal static long Number(string mark)
        {
            string text = (mark ?? "").Trim();
            return text.StartsWith("PS-", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(text.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out long n) && n > 0 ? n : 0;
        }
        internal static string Key(string mark)
        {
            long n = Number(mark);
            return n > 0 ? "PS-" + n.ToString(CultureInfo.InvariantCulture) : (mark ?? "").Trim().ToUpperInvariant();
        }
        internal static SleeveNumberPlan Plan(IEnumerable<SleeveNumberItem> documentItems, IEnumerable<string> targets, SleeveNumberLedger previous)
        {
            if (previous == null || previous.Owners == null || previous.Assigned == null || previous.LastNumber < 0 ||
                previous.Owners.Any(p => string.IsNullOrEmpty(p.Key) || string.IsNullOrEmpty(p.Value)) ||
                previous.Assigned.Any(p => string.IsNullOrEmpty(p.Key) || string.IsNullOrEmpty(p.Value)))
                throw new InvalidOperationException("套管編號台帳不完整，請先檢查模型備份。");
            var supplied = documentItems.ToList();
            var targetIds = new HashSet<string>(targets);
            var registered = new HashSet<string>(previous.Assigned.Keys.Concat(previous.Owners.Values));
            if (supplied.Any(i => i.CreatedInOperation && (!targetIds.Contains(i.Id) || registered.Contains(i.Id))))
                throw new InvalidOperationException("新建套管識別不一致，已停止編號以保護既有構件。");
            // Family defaults (including "1" or "PS-001") are not issued numbers for proven-new instances.
            var all = supplied.Select(i => new SleeveNumberItem { Id = i.Id, Mark = i.CreatedInOperation ? "" : i.Mark }).ToList();
            if (all.GroupBy(i => i.Id).Any(g => g.Count() != 1)) throw new InvalidOperationException("套管識別重複，無法編號。");
            var ledger = new SleeveNumberLedger { LastNumber = previous.LastNumber,
                Owners = new Dictionary<string,string>(previous.Owners), Assigned = new Dictionary<string,string>(previous.Assigned) };
            var groups = all.Where(i => Key(i.Mark).Length > 0).GroupBy(i => Key(i.Mark)).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var group in groups)
            {
                ledger.LastNumber = Math.Max(ledger.LastNumber, Number(group.Key));
                if (group.Value.Count == 1 && !ledger.Owners.ContainsKey(group.Key))
                {
                    ledger.Owners.Add(group.Key, group.Value[0].Id);
                    ledger.Assigned[group.Value[0].Id] = group.Value[0].Mark;
                }
            }
            foreach (string mark in ledger.Owners.Keys) ledger.LastNumber = Math.Max(ledger.LastNumber, Number(mark));
            var result = new SleeveNumberPlan { Ledger = ledger };
            var byId = all.ToDictionary(i => i.Id);
            foreach (string id in targetIds.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!byId.TryGetValue(id, out var item)) throw new InvalidOperationException("套管已不存在，請重新整理。");
                string mark = item.Mark ?? "", key = Key(mark);
                if (key.Length > 0)
                {
                    ledger.Owners.TryGetValue(key, out string owner);
                    if (owner == null && groups[key].Count > 1)
                        throw new InvalidOperationException("既有套管編號「" + mark + "」重複，無法判定原件。請先確認重號構件；未自動重排。");
                    if (owner == null || owner == id) { ledger.Owners[key] = id; ledger.Assigned[id] = mark; continue; }
                    if (ledger.Assigned.ContainsKey(id))
                        throw new InvalidOperationException("既有套管被改成已占用編號「" + mark + "」，請先排除衝突。");
                    // A new element carrying another element's registered mark is a copy: issue a new mark.
                }
                else if (ledger.Assigned.TryGetValue(id, out string oldMark) &&
                    (!groups.TryGetValue(Key(oldMark), out var occupants) || occupants.All(i => i.Id == id)))
                {
                    result.Writes[id] = oldMark;
                    continue;
                }
                string next;
                do { ledger.LastNumber = checked(ledger.LastNumber + 1); next = "PS-" + ledger.LastNumber.ToString("D3", CultureInfo.InvariantCulture); }
                while (ledger.Owners.ContainsKey(Key(next)) || groups.ContainsKey(Key(next)));
                result.Writes[id] = next; ledger.Owners[Key(next)] = id; ledger.Assigned[id] = next;
            }
            return result;
        }
    }
}
