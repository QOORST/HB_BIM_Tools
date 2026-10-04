using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // Revit-independent rules shared by the preview and schedule projection.
    internal static class SleeveQuantityPolicy
    {
        internal static bool Include(string control, bool automatic)
        {
            if (control == "排除" || control == "作廢") return false;
            return control == "納入" || (string.IsNullOrWhiteSpace(control) && automatic);
        }

        internal static string Millimetres(double feet) => feet > 0 && !double.IsNaN(feet) && !double.IsInfinity(feet)
            ? (feet * 304.8).ToString("0.###", CultureInfo.InvariantCulture) : "待確認";

        internal static string Specification(double width, double height, double outer, double inner, double diameter)
        {
            if (width > 0 && height > 0) return Millimetres(width) + " × " + Millimetres(height);
            if (outer > 0) return "外徑 Ø" + Millimetres(outer) + (inner > 0 ? "／內徑 Ø" + Millimetres(inner) : "");
            if (inner > 0) return "內徑 Ø" + Millimetres(inner);
            return diameter > 0 ? "族群直徑 Ø" + Millimetres(diameter) : "待確認";
        }

        // Length prefixes prevent delimiter-containing values from merging unrelated groups.
        internal static string Key(IEnumerable<string> values) => string.Concat(values.Select(v =>
            (v ?? "").Length.ToString(CultureInfo.InvariantCulture) + ":" + (v ?? "")));
    }
}
