using System.Reflection;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.Data.NameOrganizer;
using Form = System.Windows.Forms.Form;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string title) { if (!value) throw new Exception(title); Console.WriteLine("PASS " + ++_checks + " " + title); }
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
    private static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(obj, args);
    private static void Reject(Action action, string title) { try { action(); } catch (InvalidOperationException) { Check(true, title); return; } throw new Exception(title); }
    private static NameRow Row(string id, string name, NameKind kind = NameKind.Material) => new() { UniqueId = id, CurrentName = name, NewName = name, Scope = kind.ToString(), Kind = kind, Context = "分類｜測試", Usage = "使用情況未判定", Status = "" };
    private static void Save(Form form, string path) { Application.DoEvents(); using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(path); }
    [STAThread]
    private static int Main(string[] args)
    {
        using var guard = new System.Threading.Timer(_ => Environment.Exit(124), null, 60000, Timeout.Infinite);
        try
        {
            Application.EnableVisualStyles(); var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            var a = Row("1", "A"); var b = Row("2", "B"); a.Selected = true;
            NamePolicy.Preview(new[] { a, b }, "", "", "HB_", "_新", true);
            Check(a.NewName == "HB_A_新" && b.NewName == "B", "preview changes selected only");
            NamePolicy.Preview(new[] { a }, "", "", "HB_", "_新", true); Check(a.NewName == "HB_A_新", "repeated preview does not accumulate prefixes");
            NamePolicy.Preview(new[] { a }, "A", "機械", "", "", false); Check(a.NewName == "機械", "replacement starts from original name");
            Reject(() => NamePolicy.Preview(new[] { a }, "", "x", "", "", false), "empty find rejected");
            var snapshot = NamePolicy.Targets(new[] { a, b }, true); a.NewName = "Later";
            Check(snapshot.Count == 1 && snapshot[0].NewName == "機械", "pending request is immutable snapshot");
            a.NewName = "B"; b.NewName = "A";
            Check(NamePolicy.Validate(new[] { a, b }, new[] { Row("1", "A"), Row("2", "B") }).Count == 0, "swap permitted in one batch");
            Check(NamePolicy.Validate(new[] { a }, new[] { Row("1", "A"), Row("2", "B") }).Count > 0, "existing unselected duplicate blocked");
            b.NewName = "B"; Check(NamePolicy.Validate(new[] { a, b }, new[] { Row("1", "A"), Row("2", "B") }).Count > 0, "duplicate targets blocked");
            a.NewName = " "; Check(NamePolicy.Validate(new[] { a }, new[] { Row("1", "A") }).Count > 0, "empty name blocked");
            a.NewName = "X:Y"; Check(NamePolicy.Validate(new[] { a }, new[] { Row("1", "A") }).Count > 0, "invalid characters blocked");
            a.NewName = "New"; Check(NamePolicy.Validate(new[] { a }, new[] { Row("1", "ExternallyChanged") }).Count > 0, "stale model name blocked");
            var sheetA = Row("s1", "平面圖", NameKind.Sheet); var sheetB = Row("s2", "其他", NameKind.Sheet); sheetB.NewName = "平面圖";
            Check(NamePolicy.Validate(new[] { sheetB }, new[] { sheetA, Row("s2", "其他", NameKind.Sheet) }).Count == 0, "duplicate sheet titles allowed");
            var doc = new Document(); var m1 = doc.Add(new Material(), 1, "A"); var m2 = doc.Add(new Material(), 2, "B"); var notice = 0;
            var service = new NameOrganizerService(doc, (_, _, _, _) => true, (_, _) => notice++);
            var targets = service.Read(); targets[0].NewName = "B"; targets[1].NewName = "A";
            Check(service.Apply(targets, null) && m1.Name == "B" && m2.Name == "A" && notice == 1, "real service uses temporary names for swap");
            targets = service.Read(); targets[0].NewName = "first"; targets[1].NewName = "bad"; doc.GetElement(targets[1].UniqueId).FailName = "bad";
            var before = doc.Elements.ToDictionary(e => e.UniqueId, e => e.Name);
            Reject(() => service.Apply(targets, null), "setter failure aborts rename");
            Check(doc.Elements.All(e => e.Name == before[e.UniqueId]) && notice == 1, "partial rename rolled back without success notice");
            targets = service.Read(); targets[0].NewName = "committed_name"; doc.NextCommit = TransactionStatus.RolledBack;
            Reject(() => service.Apply(new() { targets[0] }, null), "commit rollback not reported as success"); Check(notice == 1 && m1.Name == before[m1.UniqueId], "commit rejection preserves old names"); doc.NextCommit = TransactionStatus.Committed;
            var cancelled = new NameOrganizerService(doc, (_, _, _, _) => false, (_, _) => notice++);
            Check(!cancelled.Apply(new() { targets[0] }, null), "cancel preview does not mutate");
            var changedDuringReview = new NameOrganizerService(doc, (_, _, _, _) => { m1.Name = "external"; return true; }, (_, _) => notice++);
            targets = changedDuringReview.Read(); targets[0].NewName = "next"; targets[1].NewName = "another";
            Reject(() => changedDuringReview.Apply(targets, null), "review-to-apply stale names revalidated");
            doc = new Document(); var parent = doc.Add(new Material(), 1, "材料"); var dependent = doc.Add(new Element(), 2, "連帶元素"); doc.Cascade.Add(dependent.Id);
            var previewRestored = false; var impactCount = 0;
            service = new NameOrganizerService(doc, (_, _, rows, _) => { previewRestored = doc.Elements.Count == 2; impactCount = rows.Count(); return false; }, (_, _) => notice++);
            Check(!service.Delete(service.Read(), new ElementId(99), null) && previewRestored && impactCount == 2 && doc.Elements.Count == 2, "delete preview lists cascade and rolls back before confirmation");
            service = new NameOrganizerService(doc, (_, _, _, _) => true, (_, _) => notice++);
            Reject(() => service.Delete(service.Read(), dependent.Id, null), "active view cascade blocked"); Check(doc.Elements.Count == 2, "active view guard leaves document intact");
            var extra = doc.Add(new Element(), 3, "額外元素");
            service = new NameOrganizerService(doc, (_, _, _, _) => { doc.Cascade.Add(extra.Id); return true; }, (_, _) => notice++);
            Reject(() => service.Delete(service.Read(), new ElementId(99), null), "changed cascade after preview blocked"); Check(doc.Elements.Count == 3, "changed cascade rolls back entire deletion");
            service = new NameOrganizerService(doc, (_, _, _, _) => { dependent.Name = "已由其他人改名"; return true; }, (_, _) => notice++);
            Reject(() => service.Delete(service.Read(), new ElementId(99), null), "changed dependent name invalidates reviewed impact");
            Check(doc.Elements.Count == 3, "stale dependent impact leaves all elements intact");
            doc.Cascade.Remove(extra.Id); doc.NextCommit = TransactionStatus.RolledBack;
            service = new NameOrganizerService(doc, (_, _, _, _) => true, (_, _) => notice++); var noticesBefore = notice;
            Reject(() => service.Delete(service.Read(), new ElementId(99), null), "delete commit rejection not reported as success"); Check(doc.Elements.Count == 3 && notice == noticesBefore, "delete rollback retains all elements");
            doc.NextCommit = TransactionStatus.Committed; doc.FailDelete = true;
            Reject(() => service.Delete(service.Read(), new ElementId(99), null), "partial probe failure surfaced"); Check(doc.Elements.Count == 3, "probe exception rolls back partial effects"); doc.FailDelete = false;
            Check(service.Delete(service.Read(), new ElementId(99), null) && doc.Elements.Count == 1 && notice == noticesBefore + 1, "confirmed deletion matches preview and reports after commit");
            InventoryChecks();
            var rowsUi = new List<NameRow> { Row("f", "AHU_空調箱", NameKind.Family), Row("t", "100A", NameKind.Type), Row("v", "一樓空調平面圖", NameKind.View), Row("m", "保溫材", NameKind.Material), Row("vt", "空調施工圖", NameKind.Template), Row("fl", "給水系統", NameKind.Filter), Row("sh", "一樓給水平面圖", NameKind.Sheet) };
            rowsUi[0].Usage = "放置 8 個實例"; rowsUi[2].Usage = "圖紙：M-101";
            List<NameRow> requested = null;
            using (var form = new NameOrganizerForm((_, rows) => requested = rows))
            {
                form.SetRows(rowsUi); form.Show(); Application.DoEvents();
                var grid = Field<DataGridView>(form, "_grid"); var kind = Field<ComboBox>(form, "_kind");
                Check(kind.SelectedIndex == 1 && grid.Rows.Count == 1, "default family scope without selecting everything");
                kind.SelectedIndex = 0; Check(grid.Rows.Count == 7 && kind.Items.Count == 8, "all seven supported groups available");
                rowsUi[0].Selected = true; rowsUi[0].NewName = "HB_AHU_空調箱"; rowsUi[1].Selected = true; rowsUi[1].NewName = "100A_新";
                kind.SelectedIndex = 1; Call(form, "Submit", NameAction.Apply);
                Check(requested.Count == 1 && requested[0].UniqueId == "f", "apply excludes selected hidden rows"); form.SetBusy(false);
                var refreshed = rowsUi.Select(r => { var n = r.Snapshot(); n.NewName = n.CurrentName; return n; }).ToList(); refreshed[0].CurrentName = "HB_AHU_空調箱"; refreshed[0].NewName = refreshed[0].CurrentName;
                form.SetRows(refreshed, true); Check(Field<List<NameRow>>(form, "_rows").First(r => r.UniqueId == "t").NewName == "100A_新", "other pending previews survive applied refresh");
                form.FocusItem("vt"); Check(grid.Rows.Count == 1 && ((NameRow)grid.Rows[0].DataBoundItem).UniqueId == "vt", "audit focus isolates exact UID");
                Check(Field<List<NameRow>>(form, "_rows").First(r => r.UniqueId == "t").Changed, "audit focus preserves existing previews");
                Field<CheckBox>(form, "_fromAudit").Checked = false; kind.SelectedIndex = 0; form.Size = new Size(900, 620); Application.DoEvents();
                Check(grid.Height >= 180 && grid.Width > 800, "compact results remain usable"); Save(form, Path.Combine(output, "name-organizer-900.png"));
                form.Size = new Size(1100, 740); Save(form, Path.Combine(output, "name-organizer-1100.png"));
                form.SetBusy(true); form.Close(); Check(!form.IsDisposed, "cannot close while request pending"); form.SetBusy(false); form.DiscardPreviews(); form.Close();
            }
            using (var form = new NameBatchForm(8)) { form.Show(); Save(form, Path.Combine(output, "name-batch.png")); form.Close(); }
            using (var form = new NameImpactForm("刪除影響預演範例", "勾選 1 筆，預演共 3 個元素（含連帶項目）。預演已回復模型。", new[] { new[] { "勾選｜ID 101", "AHU_空調箱", "機械設備族群" }, new[] { "連帶刪除｜ID 102", "AHU-01", "機械設備實例" }, new[] { "連帶刪除｜ID 103", "設備標籤", "標籤" } })) { form.Show(); Save(form, Path.Combine(output, "name-delete-impact.png")); form.Close(); }
            Console.WriteLine($"PASS {_checks} checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void InventoryChecks()
    {
        var doc = new Document(); var family = doc.Add(new Family(), 1, "AHU"); var type = doc.Add(new FamilySymbol { Family = family }, 2, "100A");
        doc.Add(new Element { TypeId = type.Id }, 3, "實例"); var template = doc.Add(new Autodesk.Revit.DB.View { IsTemplate = true }, 4, "樣板");
        var view = doc.Add(new Autodesk.Revit.DB.View { ViewTemplateId = template.Id }, 5, "平面"); var filter = doc.Add(new FilterElement(), 6, "選取篩選"); view.Filters.Add(filter.Id);
        var sheet = doc.Add(new ViewSheet(), 7, "圖紙"); sheet.Placed.Add(view.Id); var material = doc.Add(new Material(), 8, "材質");
        var schedule = doc.Add(new ViewSchedule(), 9, "明細表"); doc.Add(new ScheduleSheetInstance { OwnerViewId = sheet.Id, ScheduleId = schedule.Id }, 10, "放置明細表");
        doc.Add(new Autodesk.Revit.DB.View { ViewType = ViewType.Internal }, 11, "內部視圖");
        var service = new NameOrganizerService(doc); var rows = service.Read();
        Check(rows.Select(r => r.Kind).Distinct().Count() == 7, "inventory includes templates filters and separate sheet titles");
        Check(!rows.Any(r => r.UniqueId == "uid11"), "internal views excluded");
        Check(rows.Single(r => r.UniqueId == family.UniqueId).Usage.Contains("1 個實例") && rows.Single(r => r.UniqueId == type.UniqueId).Usage.Contains("1 個實例"), "family and type placed counts aggregated");
        Check(rows.Single(r => r.UniqueId == template.UniqueId).Usage.Contains("1 個視圖"), "template direct usage counted");
        Check(rows.Single(r => r.UniqueId == filter.UniqueId).Usage.Contains("1 個視圖"), "selection filter usage counted");
        Check(rows.Single(r => r.UniqueId == view.UniqueId).Usage.Contains("A01") && rows.Single(r => r.UniqueId == schedule.UniqueId).Usage.Contains("A01"), "views and schedules identify sheets");
        Check(rows.Single(r => r.UniqueId == material.UniqueId).Usage == "使用情況未判定", "unknown material usage not mislabeled unused");
        view.FilterReadFailed = true; Check(service.Read().Single(r => r.UniqueId == filter.UniqueId).Usage.Contains("未完整"), "failed usage read never returns unused");
        var sheetRow = service.Read().Single(r => r.Kind == NameKind.Sheet); sheetRow.NewName = "新的圖紙名稱";
        var mutation = new NameOrganizerService(doc, (_, _, _, _) => true, (_, _) => { });
        Check(mutation.Apply(new() { sheetRow }, null) && sheet.Name == "新的圖紙名稱" && sheet.SheetNumber == "A01", "sheet rename preserves number");
    }
}
