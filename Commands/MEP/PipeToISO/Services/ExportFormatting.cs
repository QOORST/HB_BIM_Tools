using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    internal static class ExportFormatting
    {
        // 裸數字沿用本工具的 mm 約定；只有明確的英吋單位才換算。
        internal static double ParseDiameterMm(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string value = text.Trim();
            bool inches = Regex.IsMatch(value, "(?:[\"″]|in(?:ch(?:es)?)?)$", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "(?:[\"″]|in(?:ch(?:es)?)?)$", "", RegexOptions.IgnoreCase).Trim();
            if (inches && Regex.IsMatch(value, "DN|mm", RegexOptions.IgnoreCase)) return 0;
            value = Regex.Replace(value, "^DN\\s*|\\s*mm$", "", RegexOptions.IgnoreCase).Trim();
            double number;
            var fraction = Regex.Match(value, @"^(?:(\d+)[ -]+)?(\d+)/(\d+)$");
            if (inches && fraction.Success)
            {
                double whole = fraction.Groups[1].Success ? double.Parse(fraction.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                double denominator = double.Parse(fraction.Groups[3].Value, CultureInfo.InvariantCulture);
                if (denominator == 0) return 0;
                number = whole + double.Parse(fraction.Groups[2].Value, CultureInfo.InvariantCulture) / denominator;
            }
            else if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number)) return 0;
            return number > 0 && !double.IsInfinity(number) ? number * (inches ? 25.4 : 1) : 0;
        }

        internal static string MaterialOrUnknown(string value) => string.IsNullOrWhiteSpace(value) ? "未指定" : value.Trim();
        internal static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
        internal static string CsvRow(params string[] cells) => string.Join(",", cells.Select(CsvCell));
        private static string CsvCell(string value)
        {
            value = value ?? "";
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
