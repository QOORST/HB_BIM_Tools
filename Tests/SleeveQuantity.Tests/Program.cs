using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.MEP;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static IEnumerable<Control> Children(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] {x}.Concat(Children(x)));
    private static SleeveQuantityRow Row(long id, string level = "L1", string zone = "A", string length = "200", string status = "正常", bool included = true) =>
        new SleeveQuantityRow { Id = new ElementId(id), LevelKey = level, Level = level, Zone = zone, System = "給水", Type = "鋼套管: 100A",
            Specification = "外徑 Ø114.3", Length = length, Status = status, Included = included, Control = "", Material = "鋼", Host = "牆",
            Mark = "PS-" + id, PipeDn = "80", Elevation = "2300" };
    [STAThread]
    private static void Main(string[] args)
    {
        RevisionChecks.Run(Check);
        Check(Math.Abs(SleeveRoundClearancePolicy.Remaining(150,100,1,5)-20)<1e-9,"parallel clearance retains radial offset");
        double slopeCos=1/Math.Sqrt(1+.01*.01);
        Check(SleeveRoundClearancePolicy.Remaining(150,100,slopeCos,1)>23.99,"one percent slope can retain adequate clearance");
        Check(SleeveRoundClearancePolicy.Remaining(150,100,.5,0)<0,"oblique pipe ellipse cannot pass undersized bore");
        Check(SleeveRoundClearancePolicy.Remaining(150,100,-slopeCos,1)==SleeveRoundClearancePolicy.Remaining(150,100,slopeCos,1),"reversed axis has identical clearance");
        foreach(double invalid in new[]{0.0,double.NaN,double.PositiveInfinity,1.1})
        {
            bool rejected=false; try{SleeveRoundClearancePolicy.Remaining(150,100,invalid,0);}catch(ArgumentException){rejected=true;}
            Check(rejected,"invalid axis does not report passing clearance");
        }
        foreach (var culture in new[] {"zh-TW", "en-US", "de-DE"})
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Check(SleeveQuantityPolicy.Millimetres(200 / 304.8) == "200", "length conversion " + culture);
            Check(SleeveQuantityPolicy.Millimetres(114.3 / 304.8) == "114.3", "culture-invariant decimal " + culture);
        }
        foreach (double invalid in new[] {0.0, -1.0, double.NaN, double.PositiveInfinity})
            Check(SleeveQuantityPolicy.Millimetres(invalid) == "待確認", "missing dimensions must not become zero quantities");
        Check(SleeveQuantityPolicy.Specification(0, 0, 0, 0, 0) == "待確認", "no pipe-DN fallback");
        Check(SleeveQuantityPolicy.Specification(200 / 304.8, 100 / 304.8, 0, 0, 0) == "200 × 100", "rectangular opening");
        Check(SleeveQuantityPolicy.Specification(0, 0, 114.3 / 304.8, 100 / 304.8, 0) == "外徑 Ø114.3／內徑 Ø100", "explicit inside/outside diameter");
        foreach (bool automatic in new[] {true, false})
        {
            Check(!SleeveQuantityPolicy.Include("排除", automatic), "excluded");
            Check(!SleeveQuantityPolicy.Include("作廢", automatic), "void");
            Check(!SleeveQuantityPolicy.Include("錯字", automatic), "invalid control fails closed");
            Check(SleeveQuantityPolicy.Include("納入", automatic), "explicit manual enrollment");
        }
        Check(SleeveQuantityPolicy.Include("", true) && !SleeveQuantityPolicy.Include("", false), "manual families require enrollment");
        Check(SleeveQuantityPolicy.Key(new[] {"a|b", "c"}) != SleeveQuantityPolicy.Key(new[] {"a", "b|c"}), "delimiter collision");
        var rows = new[] { Row(1), Row(2), Row(3, length:"300"), Row(4, status:"來源遺失"), Row(5, level:"L2"), Row(6, zone:"B"), Row(7, included:false) };
        var scope = new SleeveQuantityOptions { LevelKey = "L1", Zone = "A" };
        var scoped = rows.Where(scope.Matches).ToList();
        Check(scoped.Count == 4, "level + zone scope with excluded row");
        var groups = scoped.GroupBy(r => r.GroupKey).ToList();
        Check(groups.Count == 3 && groups.Sum(g => g.Count()) == 4 && groups.Max(g => g.Count()) == 2, "quantity conservation across lengths/statuses");
        rows[0].PipeDn = "50";
        Check(rows[0].GroupKey == rows[1].GroupKey, "same sleeve spec with different source DN remains same supply group");
        rows[0].Type = "不鏽鋼套管: 100A";
        Check(rows[0].GroupKey != rows[1].GroupKey, "different sleeve types remain separate");
        var materialRow = Row(8); materialRow.Material = "不鏽鋼";
        Check(materialRow.GroupKey != rows[1].GroupKey, "instance materials cannot merge");
        var hostRow = Row(9); hostRow.Host = "樓板";
        Check(hostRow.GroupKey != rows[1].GroupKey, "wall/floor quantities remain separate");
        string scopeKey = scope.Key; scope.LevelName = "renamed"; scope.SheetId = new ElementId(101);
        Check(scope.Key == scopeKey, "rerun identity survives level rename or new placement");
        scope.Detailed = true; Check(scope.Key != scopeKey, "summary and detail are distinct managed schedules");
        Check(new SleeveQuantityOptions().Matches(Row(1, status:"待確認")), "pending count remains visible separately");

        Application.SetHighDpiMode(HighDpiMode.DpiUnaware); Application.EnableVisualStyles();
        var doc = new Document(); doc.Rows.AddRange(rows);
        doc.Manual.Add(Row(20, status:"手動套管待確認"));
        doc.Elements.Add(new Level { Id = new ElementId(10), UniqueId = "L1", Name = "1F", Elevation = 0 });
        doc.Elements.Add(new ViewSheet { Id = new ElementId(100), Name = "套管配置", SheetNumber = "M-101" });
        doc.Elements.Add(new ViewSheet { Id = new ElementId(101), Name = "預留", SheetNumber = "M-102", IsPlaceholder = true });
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/sleeve-quantity"); Directory.CreateDirectory(output);
        using (var form = new SleeveQuantityForm(doc, new long[] {20}))
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-16000, -16000);
            form.Show(); Application.DoEvents();
            var controls = Children(form).ToList(); var grid = controls.OfType<DataGridView>().Single();
            Check(grid.Rows.Count == 6, "preview excludes void row");
            controls.OfType<CheckBox>().Single().Checked = true;
            Check(grid.Rows.Count == 7, "manual selection enters preview");
            var combos = controls.OfType<ComboBox>().ToList();
            var sheet = combos.Single(c => c.Items[0].ToString().Contains("只建立"));
            Check(sheet.Items.Count == 2, "placeholder sheet excluded");
            sheet.SelectedIndex = 1; Check(controls.OfType<NumericUpDown>().All(c => c.Enabled), "placement offsets enabled");
            var kind = combos.Single(c => c.Items[0].ToString() == "數量彙總表"); kind.SelectedIndex = 1;
            Check(grid.Columns.Contains("編號") && grid.Rows.Count == 7, "detail preview exposes individual marks");
            Check(grid.Columns[0].Name == "編號", "switching to detail resets column order");
            foreach (var size in new[] {new Size(1000,700), new Size(740,560)})
            {
                form.Size = size; Application.DoEvents();
                var ok = controls.OfType<Button>().Single(b => b.Text == "建立／更新");
                Check(form.ClientRectangle.Contains(form.PointToClient(ok.PointToScreen(Point.Empty))) && grid.Height > 60, "minimum-size usable controls");
                using (var bitmap = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "quantity-" + size.Width + ".png")); }
            }
            controls.OfType<Button>().Single(b => b.Text == "建立／更新").PerformClick();
            Check(form.Options.Detailed && form.Options.ManualIds.SetEquals(new long[] {20}) && form.Options.SheetId.Value == 100, "dialog returns reviewed choices");
        }
        SleeveIssueRequest issued = null;
        using (var form = new SleeveQuantityForm(doc, new long[] {20}, true, request => issued = request))
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-16000,-16000);
            form.Show(); Application.DoEvents();
            var controls = Children(form).ToList(); var tabs = controls.OfType<TabControl>().Single();
            var review = controls.OfType<DataGridView>().Single(g => g.Columns.Contains("Control"));
            var preview = controls.OfType<DataGridView>().Single(g => g != review);
            var execute = controls.OfType<Button>().Single(b => b.Text == "建立表格並存基準");
            Check(!execute.Enabled && review.Rows.Count == 7, "issue requires baseline name and shows excluded sleeves for review");
            review.Rows[0].Cells[0].Value = "作廢"; Application.DoEvents();
            Check(rows[0].Included && rows[0].Control == "", "issue editing is a draft; model source not mutated");
            Check(controls.OfType<Label>().Any(l => l.Text.StartsWith("本表 5 支")), "issue dropdown immediately updates included quantity");
            controls.OfType<CheckBox>().Single().Checked = true;
            var manualRow = review.Rows.Cast<DataGridViewRow>().Single(r => ((SleeveQuantityRow)r.Tag).Id.Value == 20);
            manualRow.Cells[0].Value = "排除";
            Check(controls.OfType<Label>().Any(l => l.Text.StartsWith("本表 5 支")), "manual enrollment can be excluded within same issue draft");
            var baselineText = controls.OfType<TextBox>().Single(t => t.MaxLength == 80); baselineText.Text = "A棟_R01_發圖";
            Check(execute.Enabled, "issue ready after baseline name entered");
            var sheet = controls.OfType<ComboBox>().Single(c => c.Items[0].ToString().Contains("只建立")); sheet.SelectedIndex = 1;
            Check(execute.Text == "補號、上圖並存基準", "issue action reflects sheet placement");
            foreach (var size in new[] {new Size(900,680), new Size(1000,780)})
            {
                form.Size = size; Application.DoEvents();
                Check(form.ClientRectangle.Contains(form.PointToClient(execute.PointToScreen(Point.Empty))) && review.Height > 100,
                    "issue minimum size keeps review and action visible");
                using(var bitmap = new Bitmap(form.Width,form.Height))
                { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(output,"issue-"+size.Width+".png")); }
            }
            execute.PerformClick();
            Check(issued != null && issued.Controls.Count == 2 && issued.Controls[1] == "作廢" && issued.Controls[20] == "排除" &&
                issued.Options.ManualIds.Contains(20) && issued.BaselineName == "A棟_R01_發圖", "single issue action submits exact reviewed choices");
        }
        issued = null;
        using (var form = new SleeveQuantityForm(doc,new long[0],true,request => issued=request))
        {
            form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-16000,-16000);
            form.Show(); Application.DoEvents();
            var review=Children(form).OfType<DataGridView>().Single(g=>g.Columns.Contains("Control")); review.Rows[0].Cells[0].Value="排除";
            form.Close(); Check(issued==null && rows[0].Included,"cancel issue discards all draft changes");
        }
        SleeveRevisionService.Baselines.Add(new SleeveRevisionSnapshot { Name = "A棟第一版", Records = new List<SleeveRevisionRecord> {
            new SleeveRevisionRecord {Id="1",Mark="PS-001", Included=true, Control="納入", Length="200",Level="GL",Zone="2F-A", Material="鋼"} } });
        SleeveRevisionService.Current = new SleeveRevisionSnapshot {Records = new List<SleeveRevisionRecord> {
            new SleeveRevisionRecord {Id="1",Mark="PS-001", Included=false, Control="作廢", Length="200",Level="GL",Zone="2F-A", Material="鋼"},
            new SleeveRevisionRecord {Id="2",Mark="PS-002", Included=true, Control="納入", Length="300",Level="GL",Zone="2F-B", Material="PVC"} } };
        using (var form = new SleeveRevisionForm(new Document()))
        {
            form.Show(); Application.DoEvents();
            var controls = Children(form).ToList();
            Check(controls.OfType<DataGridView>().Single().Rows.Count == 2, "revision preview distinguishes void and new records");
            Check(controls.OfType<Label>().Any(l => l.Text.Contains("淨增減 0 支")), "revision preview quantity conservation displayed");
            foreach (var size in new[] {new Size(850,600), new Size(1120,740)})
            {
                form.Size = size; Application.DoEvents();
                Check(controls.OfType<Button>().All(b => form.ClientRectangle.Contains(form.PointToClient(b.PointToScreen(Point.Empty))) &&
                    b.Parent.ClientRectangle.Contains(b.Bounds)), "revision buttons visible at supported size");
                using (var bitmap = new Bitmap(form.Width,form.Height))
                { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(args.Length > 0 ? args[0] : ".", "revision-"+size.Width+".png")); }
            }
        }
        Console.WriteLine("PASS: " + _checks + " quantity, numbering, revision and offline UI checks. Revit transactions and placement require native acceptance.");
    }
}
