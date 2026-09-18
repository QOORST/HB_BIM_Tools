using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    internal static class ExportTemplateRules
    {
        private static readonly HashSet<string> LegacyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "PickFace", "AccurateFace", "CurvedFace", "Wall-Side", "Column-Side",
            "Beam-Side", "Beam-Bottom", "Slab-Side", "Slab-Bottom", "Stair-Side", "Stair-Bottom"
        };

        internal static bool IsTemplate(string appId, string name, bool hasFormworkMetadata = false)
        {
            name = NormalizeName(name);
            return appId == "HB_BIM_Formwork" ||
                   (appId == "HB_BIM_Tools" && (hasFormworkMetadata || (name != null && LegacyNames.Contains(name))));
        }

        internal static string Source(string dataId, string name)
        {
            name = NormalizeName(name);
            if (dataId == "SingleFace" || dataId == "ImprovedPickFace" || name == "PickFace")
                return "面生面";
            if (dataId == "ImprovedEngine") return "模板生成";
            if (name == "AccurateFace") return "精確備援／來源未完整記錄";
            if (name == "CurvedFace") return "曲面／來源未完整記錄";
            if (name != null && LegacyNames.Contains(name)) return "傳統模板引擎";
            return "來源未記錄";
        }

        internal static bool IsValidArea(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        }

        internal static string BuildAreaFormula(IEnumerable<KeyValuePair<string, double>> areas)
        {
            const int displayLimit = 50;
            var terms = new List<string>();
            int count = 0;
            foreach (var area in areas)
            {
                if (count < displayLimit)
                    terms.Add(area.Value.ToString("F3", CultureInfo.InvariantCulture) + "(ID:" + area.Key + ")");
                count++;
            }
            string formula = string.Join(" + ", terms);
            if (count > displayLimit)
                formula += $" + 另 {count - displayLimit} 片（完整ID與面積見逐片模板明細）";
            return formula;
        }

        private static string NormalizeName(string name)
        {
            while (name != null && name.EndsWith("m²", StringComparison.Ordinal))
            {
                int suffix = name.LastIndexOf("_面積", StringComparison.Ordinal);
                if (suffix < 0) break;
                var value = name.Substring(suffix + 3, name.Length - suffix - 5);
                double area;
                if (!double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out area) &&
                    !double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out area)) break;
                if (!IsValidArea(area)) break;
                name = name.Substring(0, suffix);
            }
            return name;
        }

        internal static long? ParseHostId(string value)
        {
            long id;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0
                ? (long?)id : null;
        }

        internal static bool CanDeleteTemplate(string appId, string name, bool validHostId, bool validAreaMetadata)
        {
            if (appId == "HB_BIM_Formwork") return true;
            return appId == "HB_BIM_Tools" && validHostId && validAreaMetadata && IsTemplate(appId, name, true);
        }

        internal static bool MatchesSelection(long templateId, long? hostId, ISet<long> selection)
        {
            return selection != null && (selection.Contains(templateId) ||
                (hostId.HasValue && selection.Contains(hostId.Value)));
        }

        // A candidate fingerprint, not geometric equality or overlap proof.
        internal static string BoundsKey(string hostId, IEnumerable<double> bounds)
        {
            if (string.IsNullOrWhiteSpace(hostId) || bounds == null) return null;
            var values = bounds.ToArray();
            if (values.Length != 6 || values.Any(v => double.IsNaN(v) || double.IsInfinity(v))) return null;
            return hostId + ":" + string.Join(":", values.Select(v =>
                Math.Round(v, 6).ToString("F6", CultureInfo.InvariantCulture)));
        }
    }
}
