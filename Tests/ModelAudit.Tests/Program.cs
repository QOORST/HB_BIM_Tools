using System.Reflection;
using OfficeOpenXml;
using YD_RevitTools.LicenseManager.Commands.Data.BimStandard;

internal static class Program
{
    private static int _count;
    private static void Verify(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + ++_count + " " + name); }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static ModelAuditIssue Issue(string id = "a", string message = "名稱較簡略，建議確認用途是否易辨識。") => new()
    { UniqueId = id, ElementId = "100", ElementName = "Type 1", Category = "機械設備", Rule = "類型可讀性", Message = message, Recommendation = "依專案約定補充用途或規格；既有標準名稱可保留並略過。" };
    private static void Snapshot(Form form, string path)
    { Application.DoEvents(); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path); }
    [STAThread]
    private static int Main(string[] args)
    {
        using var watchdog = new System.Threading.Timer(_ => Environment.Exit(124), null, 60000, Timeout.Infinite);
        try
        {
            Application.EnableVisualStyles();
            var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/model-audit-simple-20261017/ui"); Directory.CreateDirectory(output);
            var options = new ModelAuditOptions();
            Verify(!ModelAuditOptions.PrefixMismatch("機械設備", options.FamilyPrefix), "default does not impose company prefix");
            Verify(!ModelAuditOptions.PrefixMismatch("hb_pump", " HB_ "), "configured prefix trimmed and case insensitive");
            Verify(ModelAuditOptions.PrefixMismatch("Pump", "HB_"), "configured mismatch found");
            Verify(!ModelAuditOptions.PrefixMismatch(null, " "), "empty prefix skips even empty name");
            var review = new ModelAuditReview(); var a = Issue(); var b = Issue("b"); b.NeedsAction = true;
            Verify(a.Status == "建議確認" && b.Status == "需處理", "advisory and actionable distinguished");
            try { review.Ignore(a, " "); throw new Exception("blank reason accepted"); } catch (InvalidOperationException) { Verify(!a.Ignored, "blank reason rejected"); }
            review.Ignore(a, " 專案既有約定 ");
            Verify(a.Status == "已略過" && a.IgnoredReason == "專案既有約定", "reason recorded");
            var repeat = Issue(); var changed = Issue(message: "新問題");
            review.Restore(new[] { repeat, changed, b });
            Verify(repeat.Ignored && !changed.Ignored && !b.Ignored, "only same finding restored");
            review.Reopen(repeat); review.Restore(new[] { a }); Verify(!a.Ignored, "reopen clears saved reason");
            Verify(ModelAuditReview.Summary(new[] { a, b }) == "需處理 1｜建議確認 1｜本次略過 0", "counts replace score");
            Verify(ModelAuditReview.Summary(Array.Empty<ModelAuditIssue>()).Contains("已啟用規則"), "empty result scoped to enabled rules");
            var calls = 0; var fail = false; ModelAuditGroup checkedGroup = ModelAuditGroup.Project;
            IReadOnlyList<ModelAuditIssue> exported = null;
            using (var form = new ModelAuditForm("測試模型", (group, _) => { calls++; checkedGroup = group; if (fail) throw new InvalidOperationException("test failure"); return new() { Issue(), b }; }, _ => { }, (_, _, rows, _) => exported = rows.ToList()))
            {
                form.Show(); Application.DoEvents();
                var group = Field<ComboBox>(form, "_group"); var grid = Field<DataGridView>(form, "_grid"); var export = Field<Button>(form, "_exportButton");
                Verify(calls == 0 && group.SelectedIndex == 0 && !export.Enabled, "opens project group without auto scan");
                Verify(!Field<Button>(form, "_rulesButton").Visible, "naming settings hidden in project group");
                Invoke(form, "Check"); Verify(calls == 1 && checkedGroup == ModelAuditGroup.Project && grid.Rows.Count == 2, "only selected group runs");
                Verify(grid.Columns.Count == 3 && export.Enabled, "three columns and export after completed check");
                var rows = Field<List<ModelAuditIssue>>(form, "_issues"); Field<ModelAuditReview>(form, "_review").Ignore(rows[0], "保留既有命名"); Invoke(form, "BindResults");
                Verify(grid.Rows.Count == 1, "ignored hidden by default"); export.PerformClick();
                Verify(exported.Count == 2 && exported.Any(x => x.IgnoredReason == "保留既有命名"), "export includes hidden ignored item and reason");
                Field<ModelAuditReview>(form, "_review").Ignore(rows[1], "待另案處理"); Invoke(form, "BindResults");
                Verify(grid.Rows.Count == 0 && Field<Label>(form, "_summary").Text.Contains("可還原"), "all ignored remains recoverable");
                Field<CheckBox>(form, "_showIgnored").Checked = true;
                Verify(grid.Rows.Count == 2, "show ignored restores visibility");
                grid.CurrentCell = grid.Rows[0].Cells[0]; Invoke(form, "ReviewSelected");
                Verify(((ModelAuditIssue)grid.CurrentRow.DataBoundItem).Ignored == false, "reopen through UI");
                group.SelectedIndex = 1; Verify(grid.Rows.Count == 0 && !export.Enabled && Field<Button>(form, "_rulesButton").Visible, "group change clears stale results and shows rules");
                Invoke(form, "Check"); Verify(checkedGroup == ModelAuditGroup.Naming, "naming group selected explicitly");
                form.Size = new Size(900, 620); Application.DoEvents();
                Verify(grid.Width > 780 && grid.Height >= 150, "compact window retains useful results area");
                Snapshot(form, Path.Combine(output, "model-audit-900.png"));
                form.Size = new Size(1040, 720); Snapshot(form, Path.Combine(output, "model-audit-1040.png"));
                fail = true; try { Invoke(form, "Check"); throw new Exception("expected callback failure"); } catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
                Verify(!export.Enabled && grid.Rows.Count == 0, "failed check cannot export prior results");
                form.Close();
            }
            using (var rules = new ModelAuditRulesForm(options))
            {
                rules.Show(); Application.DoEvents();
                Verify(rules.Options.FamilyPrefix == "" && rules.Options.TemplatePrefix == "" && rules.Options.MaterialPrefix == "" && rules.Options.FilterPrefix == "", "all four prefixes blank by default");
                Field<TextBox[]>(rules, "_prefixes")[0].Text = " HB_ "; Verify(options.FamilyPrefix == "" && rules.Options.FamilyPrefix == "HB_", "editing rules does not mutate original until accepted");
                Snapshot(rules, Path.Combine(output, "model-audit-rules.png")); rules.Close();
            }
            var located = false;
            using (var form = new ModelAuditForm("test", (_, _) => new() { Issue() }, _ => located = true, (_, _, _, _) => { }))
            { form.Show(); Invoke(form, "Check"); Invoke(form, "LocateSelected"); Verify(located && !form.Visible, "location callback resolved before window closes"); }
            using (var form = new ModelAuditForm("test", (_, _) => new() { Issue() }, _ => throw new InvalidOperationException("no instance"), (_, _, _, _) => { }))
            {
                form.Show(); Invoke(form, "Check");
                try { Invoke(form, "LocateSelected"); } catch (TargetInvocationException) { }
                Verify(form.Visible, "unlocatable item leaves results open"); form.Close();
            }
            string organizedId = null;
            var organizerIssue = Issue("organize"); organizerIssue.CanOrganize = true;
            using (var form = new ModelAuditForm("帶入測試", (_, _) => new() { organizerIssue }, _ => { }, (_, _, _, _) => { }, issue => organizedId = issue.UniqueId))
            {
                form.Show(); Invoke(form, "Check");
                var button = Field<Button>(form, "_organizeButton");
                Verify(button.Visible && button.Enabled, "eligible issue offers organizer handoff");
                form.Size = new Size(900, 620); Snapshot(form, Path.Combine(output, "model-audit-handoff.png"));
                button.PerformClick(); Verify(organizedId == "organize" && !form.Visible, "handoff carries exact UID and closes audit");
            }
            using (var form = new ModelAuditForm("不支援項目", (_, _) => new() { Issue() }, _ => { }, (_, _, _, _) => { }, _ => { }))
            { form.Show(); Invoke(form, "Check"); Verify(!Field<Button>(form, "_organizeButton").Enabled, "non-name issue cannot open organizer"); form.Close(); }
            var context = new ModelAuditContext { Group = ModelAuditGroup.Naming, Options = new ModelAuditOptions { FamilyPrefix = "HB_", ReadabilityHints = false } };
            using (var form = new ModelAuditForm("重新檢查", (_, _) => new(), _ => { }, (_, _, _, _) => { }, null, context))
            {
                Verify(form.Context.Group == ModelAuditGroup.Naming && form.Context.Options.FamilyPrefix == "HB_" && !form.Context.Options.ReadabilityHints, "recheck retains chosen group and optional rules");
                var copy = form.Context; copy.Options.FamilyPrefix = "Other";
                Verify(form.Context.Options.FamilyPrefix == "HB_" && context.Options.FamilyPrefix == "HB_", "handoff settings copied without shared mutation");
            }
            a.ElementName = "=1+1"; review.Ignore(a, "專案約定");
            var report = Path.Combine(output, "model-audit-sample.xlsx");
            ModelAuditReport.Write(report, "測試模型", ModelAuditGroup.Naming, options, new[] { a, b });
            using (var package = new ExcelPackage(new FileInfo(report)))
            {
                var sheet = package.Workbook.Worksheets["檢查結果"]; var summary = package.Workbook.Worksheets["檢查說明"];
                Verify(sheet.Dimension.Rows == 3 && sheet.Dimension.Columns == 8, "report includes every item and all eight fields");
                Verify(sheet.Cells[2, 7].Text == "專案約定" && sheet.Cells[2, 1].Text == "已略過", "report preserves reason and status");
                Verify(sheet.Cells[2, 2].Text == "=1+1" && string.IsNullOrEmpty(sheet.Cells[2, 2].Formula), "model names exported as literal text");
                Verify(summary.Cells[2, 2].Text == "命名規則（選用）" && !summary.Cells[4, 2].Text.Contains("分數"), "report records selected group with counts");
            }
            ModelAuditReport.Write(Path.Combine(output, "model-audit-empty.xlsx"), "empty", ModelAuditGroup.Project, options, Array.Empty<ModelAuditIssue>());
            using (var package = new ExcelPackage(new FileInfo(Path.Combine(output, "model-audit-empty.xlsx"))))
            { Verify(package.Workbook.Worksheets["檢查結果"].Dimension.Rows == 1, "empty export has header without fabricated pass row"); }
            Console.WriteLine($"PASS {_count} checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
