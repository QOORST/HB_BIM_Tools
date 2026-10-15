using System;
using System.Linq;
using YD_RevitTools.LicenseManager.Commands.Drawing;

internal static class Program
{
    private static int _passed;
    private static void Check(bool value, string description) { if (!value) throw new Exception(description); _passed++; Console.WriteLine("PASS " + description); }
    private static void Reject(Action action, string description) { try { action(); } catch (ArgumentException) { Check(true, description); return; } throw new Exception(description); }
    private static LegendEntry Entry(string id, string text = "內容", string name = "型別") => new LegendEntry { Type = id, Name = name, Text = text, Family = "族群" };
    public static void Main()
    {
        var settings = new LegendSettings(); var selected = new[] { Entry("A") };
        LegendPolicy.Validate(settings, selected); Check(true, "Default options accept a selected type");
        Reject(() => LegendPolicy.Validate(settings, Array.Empty<LegendEntry>()), "Empty selection cannot accidentally clear an existing legend");
        Reject(() => LegendPolicy.Validate(settings, new[] { Entry("A"), Entry("A") }), "Duplicate type identity rejected");
        Reject(() => LegendPolicy.Validate(settings, new[] { Entry("") }), "Missing identity rejected");
        foreach (double width in new[] { 0, -1, double.NaN, double.PositiveInfinity, 501 })
        { settings.TextWidth = width; Reject(() => LegendPolicy.Validate(settings, selected), "Invalid paper width rejected: " + width); }
        settings.TextWidth = 70; settings.Fields = Array.Empty<string>();
        Reject(() => LegendPolicy.Validate(settings, selected), "Empty fields rejected");
        settings.Fields = new[] { "族群", "族群" }; Reject(() => LegendPolicy.Validate(settings, selected), "Repeated fields rejected");
        settings.Fields = new[] { "族群", " " }; Reject(() => LegendPolicy.Validate(settings, selected), "Blank field rejected");
        settings.Fields = new[] { "型別" }; settings.Name = " "; Reject(() => LegendPolicy.Validate(settings, selected), "Blank view name rejected");
        var changes = LegendPolicy.Compare(new[] { Entry("A"), Entry("B") }, new[] { Entry("B", "新規格"), Entry("C") });
        Check(changes.Count(c => c.Action == "新增") == 1 && changes.Single(c => c.Action == "新增").Type == "C", "One new type creates one row");
        Check(changes.Single(c => c.Action == "移除").Type == "A", "Only absent type is removed");
        Check(changes.Single(c => c.Action == "更新").Type == "B", "Specification changes preserve type identity");
        Check(LegendPolicy.Compare(selected, selected).Single().Action == "保留", "Rerun is stable");
        Check(LegendPolicy.Compare(selected, new[] { Entry("A", name: "更名") }).Single().Action == "更新", "Rename uses unique id rather than display name");
        Check(LegendPolicy.Compare(new[] { Entry("A") }, new[] { Entry("B") }).Count == 2, "Identical names on different types remain distinct");
        Check(LegendPolicy.RowHeight(8, 22, 15, 4) == 30, "Long wrapped text determines row height");
        Check(LegendPolicy.RowHeight(31, 10, 15, 4) == 39, "Tall symbol determines row height");
        Check(LegendPolicy.RowHeight(1, 1, 15, 4) == 15, "Minimum row height retained");
        Check(LegendPolicy.NormalizeText("甲\r\n乙\r\n") == LegendPolicy.NormalizeText("甲\n乙"), "Revit terminal newlines do not cause false edit conflicts");
        Check(LegendPolicy.NormalizeText("甲\r乙\r") == LegendPolicy.NormalizeText("甲\n乙"), "Revit carriage-return paragraphs match generated newlines");
        Console.WriteLine($"{_passed} checks passed.");
    }
}
