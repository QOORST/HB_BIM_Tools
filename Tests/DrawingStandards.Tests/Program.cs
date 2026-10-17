using System.Reflection;
using Newtonsoft.Json;
using YD_RevitTools.LicenseManager.Commands.Drawing.Standards;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); _checks++; }
    private static void Reject(Action action, string name) { try { action(); } catch (InvalidOperationException) { _checks++; return; } throw new Exception(name); }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static IEnumerable<Control> All(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));
    private static DrawingStandard Standard(int revision = 1) => new DrawingStandard { Key = "plumbing", Name = "給排水平面", Revision = revision,
        Note = "工程初版", References = new List<StandardReference> { Ref("視圖樣板", "VT_P_100"), Ref("文字樣式", "TXT_2.5"), Ref("尺寸樣式", "DIM_2.5"), Ref("標籤族型", "TAG_PIPE") } };
    private static StandardReference Ref(string kind, string uid) => new StandardReference { Kind = kind, UniqueId = uid, Name = uid,
        Values = new Dictionary<string, string> { ["名稱"] = uid, ["參數 / 字高"] = "0.008202099737532808" } };
    private static StandardViewRow Row(string uid, bool apply = false, string block = "", string exception = "") => new StandardViewRow {
        UniqueId = uid, ViewName = uid + " 給排水平面圖", Apply = apply, BlockReason = block, ExceptionReason = exception,
        CurrentTemplate = "原圖樣板", TargetTemplate = "VT_P_100", Action = exception != "" ? "保留例外" : block != "" ? "無法套用" : "指定樣板並登錄版本",
        Sheets = "P-101", Issues = exception != "" ? "專案例外：" + exception : block != "" ? block : "視圖樣板與組合不同" };
    private static StandardPreview Plan() => new StandardPreview { StandardKey = "plumbing", TargetTemplate = "VT_P_100", Revision = 1, Rows = new List<StandardViewRow> {
        Row("1F"), Row("2F"), Row("3F", block:"具有主從關係"), Row("4F", exception:"現場局部詳圖保留") }, Detail = "測試基準。尚需人工複核連結顯示與族內字高。" };

    [STAThread]
    private static void Main(string[] args)
    {
        string stage = "rules";
        using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("Timed out in test stage: " + stage); Environment.Exit(124); }, null, 45000, System.Threading.Timeout.Infinite);
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/drawing-standards-20261017/ui"); Directory.CreateDirectory(output);
        var baseline = Ref("尺寸樣式", "DIM_2.5");
        var current = JsonConvert.DeserializeObject<StandardReference>(JsonConvert.SerializeObject(baseline))!;
        Check(DrawingStandardPolicy.Compare(baseline, current).Count == 0, "Same content is stable");
        current.Values["參數 / 字高"] = "0.00984251968503937";
        var difference = DrawingStandardPolicy.Compare(baseline, current);
        Check(difference.Count == 1 && difference[0].Contains("字高") && difference[0].Contains(" → "), "Actual text size drift despite same name");
        current = Ref("尺寸樣式", "DIM_2.5"); current.Values["單位精度"] = "0.01";
        Check(DrawingStandardPolicy.Compare(baseline, current).Single().Contains("單位精度"), "New setting detected");
        current = Ref("尺寸樣式", "DIM_2.5"); current.Values.Remove("參數 / 字高");
        Check(DrawingStandardPolicy.Compare(baseline, current).Single().EndsWith("（無）"), "Deleted setting detected");
        Check(DrawingStandardPolicy.Compare(baseline, null).Count == 1, "Deleted type cannot pass");
        current = Ref("尺寸樣式", "replacement-uid"); current.Name = baseline.Name;
        Check(DrawingStandardPolicy.Compare(baseline, current).Single().Contains("身分"), "Same-name recreated type cannot silently match");

        var standard = Standard(); var plan = Plan();
        Reject(() => DrawingStandardPolicy.ValidateApply(plan, standard), "Empty checkbox selection must not apply all");
        plan.Rows[1].Apply = true;
        Check(DrawingStandardPolicy.ValidateApply(plan, standard).Single().UniqueId == "2F", "Only checked view selected");
        plan.DefinitionIssues.Add("篩選器條件已變更");
        Reject(() => DrawingStandardPolicy.ValidateApply(plan, standard), "Changed standard blocks apply"); plan.DefinitionIssues.Clear();
        Reject(() => DrawingStandardPolicy.ValidateApply(plan, Standard(2)), "Stale revision blocks apply");
        standard.Key = "fire"; Reject(() => DrawingStandardPolicy.ValidateApply(plan, standard), "Wrong-purpose preview blocks apply"); standard.Key = "plumbing";
        plan.Rows[2].Apply = true; Reject(() => DrawingStandardPolicy.ValidateApply(plan, standard), "Dependent view is blocked");
        plan.Rows[2].ExceptionReason = "保留原主從配置";
        Check(DrawingStandardPolicy.ValidateApply(plan, standard).Count == 2, "Blocked view can explicitly record exception without applying template");
        plan.Rows[2].ExceptionReason = "   "; Reject(() => DrawingStandardPolicy.ValidateApply(plan, standard), "Whitespace is not exception approval");
        plan = Plan(); plan.Rows[0].Apply = true; plan.Rows.Add(Row("1F", true));
        Reject(() => DrawingStandardPolicy.ValidateApply(plan, standard), "Duplicate target identity blocked");
        plan.Rows.RemoveAt(4); plan.Rows[0].UniqueId = "";
        Reject(() => DrawingStandardPolicy.ValidateApply(plan, standard), "Missing identity blocked");
        Reject(() => DrawingStandardPolicy.ValidateApply(Plan(), Standard(0)), "Unset package blocked");

        var assignment = new StandardAssignment { StandardKey = "plumbing", Revision = 1, ViewUniqueId = "1F" };
        Check(DrawingStandardPolicy.AssignmentIssue(assignment, standard) == "", "Current assignment matches");
        Check(DrawingStandardPolicy.AssignmentIssue(assignment, Standard(2)).Contains("舊版"), "Old assignment is not silently current");
        assignment.ExceptionReason = "比例需 1:50";
        Check(DrawingStandardPolicy.AssignmentIssue(assignment, Standard(2)).Contains("重新確認例外"), "New revision rechecks exception");
        Check(DrawingStandardPolicy.AssignmentIssue(null, standard).Contains("尚未登錄"), "Untracked view remains untracked");
        assignment.StandardKey = "fire";
        Check(DrawingStandardPolicy.AssignmentIssue(assignment, standard).Contains("其他用途"), "Different assigned purpose is visible");
        var data = new DrawingStandardData { Revisions = new List<DrawingStandard> { Standard(), Standard(2) }, Assignments = new List<StandardAssignment> { assignment } };
        var roundtrip = JsonConvert.DeserializeObject<DrawingStandardData>(JsonConvert.SerializeObject(data))!;
        Check(roundtrip.Latest("plumbing").Revision == 2 && roundtrip.Revisions.Count == 2, "Version history preserved across storage roundtrip");
        Check(roundtrip.Latest("fire").Revision == 0 && roundtrip.Latest("sleeve").Revision == 0, "Purpose configurations are independent");
        Check(roundtrip.Assignments[0].ExceptionReason == assignment.ExceptionReason, "Exception reason preserved");
        plan = Plan(); plan.Rows[0].ViewName = "=cmd()"; plan.Rows[0].ExceptionReason = "引號\"及逗號,\r\n換行";
        var csv = DrawingStandardForm.ReportCsv(plan);
        Check(csv.Contains("\"'=cmd()\""), "CSV formula injection escaped");
        Check(csv.Contains("引號\"\"及逗號,\r\n換行"), "CSV multiline and quotes escaped");
        Check(csv.Contains("人工複核"), "Report includes scope limits");

        var legacyJson = "{\"FormatVersion\":1,\"Revisions\":" + JsonConvert.SerializeObject(new[] { Standard() }) +
            ",\"Assignments\":" + JsonConvert.SerializeObject(new[] { assignment }) + "}";
        var legacy = JsonConvert.DeserializeObject<DrawingStandardData>(legacyJson)!; legacy.Upgrade();
        Check(legacy.FormatVersion == 2 && legacy.Latest("plumbing").Revision == 1, "V1 definitions migrate without resetting version");
        Check(legacy.Assignments.Single().ExceptionReason == assignment.ExceptionReason, "V1 exceptions survive catalog upgrade");
        Check(legacy.Definitions.Any(d => d.Key == "electrical") && legacy.Definitions.Any(d => d.Key == "hvac-duct"), "Other systems supplied as unconfigured starter entries");
        var migratedJson = JsonConvert.SerializeObject(legacy); legacy.Upgrade();
        Check(JsonConvert.SerializeObject(legacy) == migratedJson, "Upgrade is idempotent");
        Reject(() => DrawingStandardCatalog.Create(legacy, "  給排水平面 ", null, "QA"), "Trimmed duplicate rejected");
        Reject(() => DrawingStandardCatalog.Create(legacy, "\r\n", null, "QA"), "Blank name rejected");
        Reject(() => DrawingStandardCatalog.Create(legacy, "照明\n平面", null, "QA"), "Control character rejected");
        Reject(() => DrawingStandardCatalog.Create(legacy, new string('A', 121), null, "QA"), "Overlong name rejected");
        var customKey = DrawingStandardCatalog.Create(legacy, "空調－風管平面－1:100", null, "QA");
        Check(legacy.Latest(customKey).Revision == 0 && legacy.Latest(customKey).References.Count == 0, "Custom system begins as unconfigured draft");
        Reject(() => DrawingStandardCatalog.Create(legacy, "錯誤來源", "missing-key", "QA"), "Missing copy source cannot fallback");
        var sourceJson = JsonConvert.SerializeObject(legacy.Revisions);
        var copiedKey = DrawingStandardCatalog.Create(legacy, "給水－詳圖－1:50", "plumbing", "QA");
        Check(legacy.Latest(copiedKey).Revision == 0 && legacy.Latest(copiedKey).References.Count == 4, "Copy preserves choices but resets acceptance revision");
        Reject(() => DrawingStandardPolicy.ValidateApply(Plan(), legacy.Latest(copiedKey)), "Copied draft cannot be applied before saving");
        legacy.Definitions.Single(d => d.Key == copiedKey).DraftReferences[0].Values["參數 / 字高"] = "edited";
        Check(JsonConvert.SerializeObject(legacy.Revisions) == sourceJson, "Copy does not alias source baseline dictionaries");
        Check(!legacy.Assignments.Any(a => a.StandardKey == copiedKey), "Copy does not inherit view assignments or exceptions");
        DrawingStandardCatalog.Rename(legacy, "plumbing", "給水－平面－1:100", "QA");
        Check(legacy.Latest("plumbing").Name == "給水－平面－1:100" && legacy.Revisions[0].Name == "給排水平面", "Rename preserves historical version names");
        DrawingStandardCatalog.SetArchived(legacy, "plumbing", true, "QA");
        var archivedPlan = Plan(); archivedPlan.Rows[0].Apply = true;
        Reject(() => DrawingStandardPolicy.ValidateApply(archivedPlan, legacy.Latest("plumbing")), "Archive blocks even valid old previews");
        Check(legacy.Revisions.Count == 1 && legacy.Assignments.Count == 1, "Archive preserves history and assignments");
        Reject(() => DrawingStandardCatalog.Create(legacy, "給水－平面－1:100", null, "QA"), "Archived name cannot be silently reused");
        DrawingStandardCatalog.SetArchived(legacy, "plumbing", false, "QA");
        Check(DrawingStandardPolicy.ValidateApply(archivedPlan, legacy.Latest("plumbing")).Count == 1, "Reenable retains valid baseline");
        var persisted = JsonConvert.DeserializeObject<DrawingStandardData>(JsonConvert.SerializeObject(legacy))!;
        Check(persisted.Latest(customKey).Name == "空調－風管平面－1:100" && persisted.CatalogHistory.Count == 5, "Custom catalogs and operation history survive roundtrip");
        var detached = persisted.Latest("plumbing"); detached.References[0].Values.Clear();
        Check(persisted.Latest("plumbing").References[0].Values.Count > 0, "Read result cannot mutate stored historical baseline");

        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        stage = "main form";
        var previewCalls = 0; var applied = new List<string>();
        var uiData = new DrawingStandardData { Revisions = new List<DrawingStandard> { standard } };
        var inventory = new TemplateInventory { ActiveViewUniqueId = "active-1F", ActiveViewName = "1F 空調平面", CanCreateFromActive = true,
            Templates = new List<TemplateInventoryItem> {
                new TemplateInventoryItem { UniqueId = "template-1", Name = "VT_空調風管_1-100", ViewType = "平面", CanDuplicate = true,
                    Uses = new List<TemplateUsage> { new TemplateUsage { ViewUniqueId = "view-1", ViewName = "1F 空調風管平面", Sheets = "AC-101 · 空調平面" },
                        new TemplateUsage { ViewUniqueId = "view-2", ViewName = "2F 空調風管平面", Sheets = "" } }, StandardNames = new List<string> { "空調風管平面 · v1" } },
                new TemplateInventoryItem { UniqueId = "template-2", Name = "VT_消防_1-100", ViewType = "平面", CanDuplicate = false }
            } };
        var requests = new List<TemplateCreationRequest>();
        Func<TemplateCreationRequest, string> createTemplate = request => { requests.Add(request); var uid = "new-" + requests.Count;
            inventory.Templates.Add(new TemplateInventoryItem { UniqueId = uid, Name = request.Name, ViewType = "平面", CanDuplicate = true }); return uid; };
        var management = new DrawingStandardManagement { Create = (name, key) => DrawingStandardCatalog.Create(uiData, name, key, "QA"),
            Rename = (key, name) => DrawingStandardCatalog.Rename(uiData, key, name, "QA"), Archive = (key, archived) => DrawingStandardCatalog.SetArchived(uiData, key, archived, "QA"),
            Inventory = () => inventory, CreateTemplate = createTemplate };
        using var form = new DrawingStandardForm(() => uiData,
            () => standard.References.Select(r => new StandardChoice { Kind = r.Kind, Name = r.Name, UniqueId = r.UniqueId }).ToList(),
            (_, _, _) => { }, (_, _) => { previewCalls++; return Plan(); },
            p => { applied.AddRange(DrawingStandardPolicy.ValidateApply(p, standard).Select(r => r.UniqueId)); return applied.Count; }, management);
        form.Show(); Application.DoEvents();
        Check(previewCalls == 1, "Opening checks initial range");
        var grid = Field<DataGridView>(form, "_grid");
        var apply = Field<Button>(form, "_applyButton");
        Check(grid.Rows.Count == 4 && !apply.Enabled, "Nothing selected by default");
        Check(Field<Label>(form, "_targetTemplate").Text.Contains("VT_P_100"), "Target template is visible before apply");
        All(form).OfType<Button>().Single(b => b.Text == "勾選可套用項目").PerformClick(); Application.DoEvents();
        var uiPlan = Field<StandardPreview>(form, "_plan");
        Check(uiPlan.Rows.Where(r => r.Apply).Select(r => r.UniqueId).SequenceEqual(new[] { "1F", "2F" }), "Select all excludes exceptions and dependent views");
        Check(apply.Enabled, "Valid selection enables apply");
        All(form).OfType<Button>().Single(b => b.Text == "取消勾選").PerformClick();
        Check(!apply.Enabled && uiPlan.Rows.All(r => !r.Apply), "Clear resets all selection");
        grid.Rows[1].Cells["Apply"].Value = true; grid.EndEdit(); Application.DoEvents();
        Check(apply.Enabled, "Manual checkbox enables apply");
        apply.PerformClick(); Application.DoEvents();
        Check(applied.SequenceEqual(new[] { "2F" }), "UI apply honors exact checkbox scope");
        Check(previewCalls == 2 && !apply.Enabled, "Apply automatically rechecks and clears selection");
        Field<ComboBox>(form, "_scope").SelectedIndex = 1;
        Check(grid.DataSource == null && !apply.Enabled, "Scope switch invalidates stale preview");
        All(form).OfType<Button>().Single(b => b.Text == "檢查／更新預覽").PerformClick(); Application.DoEvents();
        Field<ComboBox>(form, "_standard").SelectedIndex = 1;
        Check(grid.DataSource == null && !apply.Enabled, "Purpose switch invalidates stale preview");
        Field<ComboBox>(form, "_standard").SelectedIndex = 0;
        All(form).OfType<Button>().Single(b => b.Text == "檢查／更新預覽").PerformClick(); Application.DoEvents();
        foreach (var size in new[] { new Size(1120, 740), new Size(900, 620) })
        {
            form.Size = size; Application.DoEvents();
            Check(grid.Height > 170 && apply.Bottom <= apply.Parent.ClientSize.Height, "Grid and footer fit " + size.Width);
            var scopeControl = Field<ComboBox>(form, "_scope");
            Check(Rectangle.Intersect(scopeControl.Bounds, scopeControl.Parent!.ClientRectangle) == scopeControl.Bounds &&
                Field<Label>(form, "_targetTemplate").Height >= 30, "Scope and target template remain visible below management actions " + size.Width);
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "standards-" + size.Width + ".png"));
        }
        using var editor = new DrawingStandardEditor(standard, standard.References.Select(r => new StandardChoice { Kind = r.Kind, Name = r.Name, UniqueId = r.UniqueId }).ToList());
        editor.Show(); Application.DoEvents();
        Check(editor.SelectedIds.Count == 4, "Editor preserves all reference selections");
        Check(Field<TextBox>(editor, "_note").Text == "", "New revision requires new note");
        using (var bitmap = new Bitmap(editor.Width, editor.Height)) { editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size)); bitmap.Save(Path.Combine(output, "standard-editor.png")); }
        editor.Close();
        var menu = Field<ContextMenuStrip>(form, "_managementMenu");
        ((ToolStripMenuItem)menu.Items[3]).PerformClick(); Application.DoEvents();
        Check(uiData.Latest("plumbing").Archived && !Field<ComboBox>(form, "_standard").Items.Cast<DrawingStandard>().Any(s => s.Key == "plumbing"), "Archive immediately hides entry from normal choices");
        ((ToolStripMenuItem)menu.Items[5]).Checked = true; Application.DoEvents();
        var standardBox = Field<ComboBox>(form, "_standard");
        Check(standardBox.Items.Cast<DrawingStandard>().Any(s => s.Key == "plumbing" && s.Archived), "Show archived makes recovery discoverable");
        standardBox.SelectedItem = standardBox.Items.Cast<DrawingStandard>().Single(s => s.Key == "plumbing");
        ((ToolStripMenuItem)menu.Items[3]).PerformClick();
        Check(!uiData.Latest("plumbing").Archived, "Archive action toggles archived entry back to active");
        foreach (var definition in uiData.Definitions) definition.Archived = true;
        ((ToolStripMenuItem)menu.Items[5]).Checked = false; Application.DoEvents();
        Check(standardBox.Items.Count == 0 && !apply.Enabled && Field<Label>(form, "_status").Text.Contains("沒有啟用"), "All archived leaves safe empty state with recovery instructions");
        form.Close();

        using var manager = new ViewTemplateManagerForm(() => inventory, createTemplate);
        stage = "template manager";
        manager.Show(); Application.DoEvents();
        var templateGrid = Field<DataGridView>(manager, "_grid");
        Check(templateGrid.Rows.Count == 2 && Field<TextBox>(manager, "_detail").Text.Contains("AC-101"), "Inventory shows assigned view and sheet details");
        Check(Field<TextBox>(manager, "_detail").Text.Contains("未上圖"), "Unplaced views remain visible in usage list");
        Check(Field<Button>(manager, "_duplicate").Enabled && Field<Button>(manager, "_fromView").Enabled, "Valid source actions enabled independently");
        using (var bitmap = new Bitmap(manager.Width, manager.Height)) { manager.DrawToBitmap(bitmap, new Rectangle(Point.Empty, manager.Size)); bitmap.Save(Path.Combine(output, "template-manager.png")); }
        void SubmitName(string name, Button trigger)
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (_, _) => { var dialog = Application.OpenForms.OfType<DrawingNameDialog>().FirstOrDefault(); if (dialog == null) return;
                timer.Stop(); Field<TextBox>(dialog, "_name").Text = name; ((Button)dialog.AcceptButton!).PerformClick(); };
            timer.Start(); trigger.PerformClick(); Application.DoEvents();
        }
        stage = "duplicate template";
        SubmitName("VT_空調風管_1-50", Field<Button>(manager, "_duplicate"));
        Check(requests.Single().SourceUniqueId == "template-1" && !requests[0].FromCurrentView, "Duplicate sends selected template UID, not view UID");
        Check(((TemplateInventoryItem)templateGrid.CurrentRow!.DataBoundItem!).UniqueId == "new-1", "Created template selected after refresh");
        stage = "template from active view";
        SubmitName("VT_1F_工作圖", Field<Button>(manager, "_fromView"));
        Check(requests[1].SourceUniqueId == "active-1F" && requests[1].FromCurrentView, "From-view action sends active view identity");
        Check(inventory.Templates[0].Uses.Count == 2 && inventory.Templates.Last().Uses.Count == 0, "UI workflow does not auto-assign new templates");
        stage = "invalid and empty sources";
        templateGrid.CurrentCell = templateGrid.Rows[1].Cells[0]; Application.DoEvents();
        Check(!Field<Button>(manager, "_duplicate").Enabled, "Cannot duplicate invalid source");
        inventory.CanCreateFromActive = false;
        All(manager).OfType<Button>().Single(b => b.Text == "更新清單").PerformClick();
        Check(!Field<Button>(manager, "_fromView").Enabled, "Invalid current view disables from-view creation");
        inventory.Templates = new List<TemplateInventoryItem>(); inventory.CanCreateFromActive = true;
        All(manager).OfType<Button>().Single(b => b.Text == "更新清單").PerformClick();
        Check(!Field<Button>(manager, "_duplicate").Enabled && Field<Button>(manager, "_fromView").Enabled, "Empty template library can start from active view");
        manager.Close();
        Console.WriteLine("PASS " + _checks + " checks; native Revit transactions not exercised.");
    }
}
