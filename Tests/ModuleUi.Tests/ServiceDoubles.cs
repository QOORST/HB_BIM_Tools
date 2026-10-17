using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

// UI fixture data only; these doubles do not validate geometry or model writes.
namespace YD_RevitTools.LicenseManager.Commands.Drawing
{
    internal class LegendRecord { public LegendSettings Settings { get; set; } = new LegendSettings(); }
    internal class LegendResult
    {
        public string View => "preview";
        public bool Success => true;
        public List<LegendChange> Changes { get; } = new List<LegendChange> { new LegendChange { Action = "新增", Name = "測試管線", Detail = "離線介面預覽" } };
        public List<string> Problems { get; } = new List<string>(); public List<string> Notes { get; } = new List<string>();
    }
    internal static class LegendService
    {
        public static bool IsManaged(View view) => false;
        public static LegendRecord Read(View view) => new LegendRecord();
        public static List<ElementType> CollectTypes(Document doc, View view, ICollection<ElementId> ids) => doc.Elements.OfType<ElementType>().ToList();
        public static LegendResult Run(Document doc, LegendSettings settings, IList<string> types, bool preview) => new LegendResult();
    }
}
namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal class AutoTagSavedSettings { public AutoTagOptions Options { get; set; } = new AutoTagOptions(); public List<AutoTagSavedRule> Rules { get; } = new List<AutoTagSavedRule>(); }
    internal class AutoTagSavedRule { public string CategoryName { get; set; } public bool Enabled { get; set; } public string TagTypeLabel { get; set; } }
    internal static class AutoTagSettingsStore
    {
        public static AutoTagSavedSettings Load(Document doc, AutoTagMode mode) => null;
        public static void Reset(Document doc, AutoTagMode mode) { }
        public static void Save(Document doc, AutoTagMode mode, AutoTagOptions options, IReadOnlyList<AutoTagSavedRule> rules) { }
    }
    internal static class AutoTagService
    {
        public static AutoTagCategoryRule[] CategoryRules = Enumerable.Range(1, 12).Select(i => new AutoTagCategoryRule("測試分類 " + i, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeTags, true, true)).ToArray();
        public static IReadOnlyList<AutoTagTypeOption> GetTagTypeOptions(Document doc, BuiltInCategory category) => new[] { new AutoTagTypeOption(new ElementId(100), "測試標籤：型別 A") };
    }
}
