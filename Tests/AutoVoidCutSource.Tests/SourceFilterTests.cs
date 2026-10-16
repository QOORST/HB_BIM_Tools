using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using YD_RevitTools.LicenseManager.Commands.AR.AutoJoin;
internal static class SourceFilterTests
{
    static int count;
    static void Check(bool ok) { count++; if (!ok) throw new Exception("Failed: " + count); }
    [STAThread] static void Main()
    {
        var f = new VoidCutSourceFilter { Filtered = true, Category = "梁", Family = " 溝 ", Type = "a" };
        Check(f.Matches("梁", "淺溝", "A型"));
        Check(!f.Matches("樓板", "淺溝", "A型"));
        Check(!f.Matches("梁", "其他", "A型"));
        Check(!f.Matches("梁", "淺溝", "B型"));
        f.Exact = true; Check(!f.Matches("梁", "淺溝", "A"));
        f.Family = "淺溝"; Check(f.Matches("梁", "淺溝", "A"));
        Check(!f.Matches("梁", "淺溝", "AA"));
        string folder = Path.Combine(Path.GetTempPath(), "HBVoidFilterTest-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "filter.xml");
        try
        {
            Check(!VoidCutSourceFilter.Load(path).Filtered);
            f.Save(path); var loaded = VoidCutSourceFilter.Load(path);
            Check(loaded.Filtered && loaded.Exact && loaded.Family == "淺溝" && loaded.Category == "梁");
            f.Type = "B"; f.Save(path); Check(VoidCutSourceFilter.Load(path).Type == "B");
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(folder)) Directory.Delete(folder); }
        var rows = new List<VoidCutSourceRow> {
            new VoidCutSourceRow { Key="1", Category="梁", Family="淺溝", Type="A", Count=3 },
            new VoidCutSourceRow { Key="2", Category="梁", Family="淺溝", Type="B", Count=2 } };
        f.Type = "A";
        using(var form = new VoidCutSourceForm(rows, f, true))
        {
            Check(form.SelectedKeys.SetEquals(new[] { "1" }));
            var list = form.Controls[0].Controls.OfType<CheckedListBox>().Single();
            list.SetItemChecked(0, false); Check(!((Button)form.AcceptButton).Enabled);
            list.SetItemChecked(0, true); Check(((Button)form.AcceptButton).Enabled);
        }
        using(var form = new VoidCutSourceForm(rows, f, false)) Check(!((Button)form.AcceptButton).Enabled);
        f.Category = "不存在";
        using(var form = new VoidCutSourceForm(rows, f, true)) { Check(form.SelectedKeys.Count == 0); Check(!((Button)form.AcceptButton).Enabled); }
        f.Filtered = false;
        using(var form = new VoidCutSourceForm(rows, f, false)) Check(((Button)form.AcceptButton).Enabled);
        Console.WriteLine($"PASS: {count} source filter, persistence and UI checks; native Revit not tested.");
    }
}
