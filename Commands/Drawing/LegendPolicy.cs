using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.Drawing
{
    internal sealed class LegendSettings
    {
        public string Template { get; set; }
        public string Target { get; set; }
        public string Name { get; set; } = "HB 智慧圖例";
        public string TextType { get; set; }
        public string Sheet { get; set; }
        public string Source { get; set; }
        public string[] Fields { get; set; } = { "型別代碼", "族群", "型別", "說明" };
        public double SymbolWidth { get; set; } = 35;
        public double TextWidth { get; set; } = 70;
        public double Gap { get; set; } = 4;
        public double MinimumRow { get; set; } = 15;
    }

    internal sealed class LegendEntry
    {
        public string Type { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
    }

    internal sealed class LegendChange
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string Action { get; set; }
        public string Detail { get; set; }
    }

    // Paper sizes are millimetres. This module deliberately has no Revit dependency.
    internal static class LegendPolicy
    {
        internal static void Validate(LegendSettings settings, IEnumerable<LegendEntry> entries)
        {
            if (settings == null) throw new ArgumentException("請先指定圖例設定。");
            if (string.IsNullOrWhiteSpace(settings.Name)) throw new ArgumentException("請輸入圖例名稱。");
            if (settings.Fields == null || settings.Fields.Length == 0 || settings.Fields.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("至少選擇一個文字欄位，欄位名稱不可空白。");
            if (settings.Fields.Distinct(StringComparer.Ordinal).Count() != settings.Fields.Length)
                throw new ArgumentException("文字欄位不可重複。");
            foreach (var size in new[] { settings.SymbolWidth, settings.TextWidth, settings.Gap, settings.MinimumRow })
                if (double.IsNaN(size) || double.IsInfinity(size) || size < 1 || size > 500)
                    throw new ArgumentException("版面尺寸須介於 1～500 紙面 mm。");
            var list = entries?.ToList() ?? new List<LegendEntry>();
            if (list.Count == 0) throw new ArgumentException("請勾選至少一個構件型別。");
            if (list.Any(x => string.IsNullOrEmpty(x.Type)) || list.Select(x => x.Type).Distinct().Count() != list.Count)
                throw new ArgumentException("型別識別資料有重複或遺失，請重新讀取來源。");
        }

        internal static double RowHeight(double symbolHeight, double textHeight, double minimum, double gap)
            => Math.Max(minimum, Math.Max(symbolHeight, textHeight) + 2 * gap);

        internal static string NormalizeText(string text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');

        internal static List<LegendChange> Compare(IEnumerable<LegendEntry> previous, IEnumerable<LegendEntry> current)
        {
            var before = previous.ToDictionary(x => x.Type);
            var after = current.ToDictionary(x => x.Type);
            var result = new List<LegendChange>();
            foreach (var item in current)
            {
                before.TryGetValue(item.Type, out var old);
                result.Add(new LegendChange { Type = item.Type, Name = item.Family + " : " + item.Name,
                    Action = old == null ? "新增" : old.Text != item.Text || old.Name != item.Name ? "更新" : "保留",
                    Detail = old == null ? item.Text : old.Text == item.Text ? "文字相同；依版面設定檢查位置" : old.Text + " → " + item.Text });
            }
            foreach (var old in previous.Where(x => !after.ContainsKey(x.Type)))
                result.Add(new LegendChange { Type = old.Type, Name = old.Family + " : " + old.Name, Action = "移除", Detail = "只移除 HB 記錄的這列元件與文字" });
            return result;
        }
    }
}
