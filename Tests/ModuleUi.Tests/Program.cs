using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.MEP.MepCheck;
using YD_RevitTools.LicenseManager.Helpers.UI;

internal static class Program
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
    static IEnumerable<Control> Children(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Children(x)));
    static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
    static void Offscreen(Form form) { form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-16000, -16000); }
    static void Render(Form form, string path) { Application.DoEvents(); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); }
    static void RenderWpf(System.Windows.Window window, string path)
    {
        window.ShowInTaskbar = false; window.ShowActivated = false; window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual; window.Left = -16000; window.Top = -16000;
        window.Show(); window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
    static DialogResult ModalAction(Form form, Action action)
    {
        using var timer = new Timer { Interval = 80 };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start(); return form.ShowDialog();
    }
    [STAThread] static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware); Application.EnableVisualStyles();
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var document = new Document();
        document.Elements.Add(new Autodesk.Revit.DB.View { UniqueId = "legend", Name = "測試圖例範本", ViewType = ViewType.Legend });
        document.Elements.Add(new TextNoteType { UniqueId = "text", Name = "2.5 mm 文字" });
        for (int i = 1; i <= 24; i++) document.Elements.Add(new ElementType { UniqueId = "type" + i, Name = "型別 " + i });
        using (var legend = new YD_RevitTools.LicenseManager.Commands.Drawing.LegendForm(new Autodesk.Revit.UI.UIDocument(document)))
        {
            Offscreen(legend); legend.Show(); legend.Location = new Point(-16000, -16000); legend.Size = new Size(900, 700);
            Render(legend, Path.Combine(output, "legend-types.png"));
            Children(legend).OfType<Button>().Single(b => b.Text == "檢查並預覽").PerformClick();
            Check(Children(legend).OfType<TabControl>().Single().SelectedIndex == 1, "legend preview opens full-height differences");
            Check(Field<Button>(legend, "_apply").Enabled, "valid fixture preview enables apply");
            Render(legend, Path.Combine(output, "legend-preview.png"));
            using var timer = new Timer { Interval = 80 };
            timer.Tick += (_, _) => {
                timer.Stop(); var dialog = Application.OpenForms.Cast<Form>().Single(f => f.Text == "圖例版面設定");
                Field<TextBox>(legend, "_fields").Text = "型別,說明";
                Render(dialog, Path.Combine(output, "legend-settings.png")); dialog.Close();
            };
            timer.Start(); Children(legend).OfType<Button>().Single(b => b.Text == "版面設定…").PerformClick();
            Check(!Field<Button>(legend, "_apply").Enabled && !Field<TextBox>(legend, "_fields").IsDisposed, "layout edits invalidate preview and survive dialog close");
            legend.Close();
        }
        var linkedDocument = new Document();
        document.Elements.Add(new RevitLinkInstance { UniqueId = "link-A", Name = "機電連結 A", LinkedDocument = linkedDocument });
        document.Elements.Add(new RevitLinkInstance { UniqueId = "link-B", Name = "機電連結 B", LinkedDocument = linkedDocument });
        int pickRequests = 0;
        using (var tags = new YD_RevitTools.LicenseManager.Commands.AR.AutoTag.AutoTagOptionsForm(document, YD_RevitTools.LicenseManager.Commands.AR.AutoTag.AutoTagMode.Unified, (_, _) => { }, () => { }, () => pickRequests++))
        {
            Offscreen(tags); tags.Show(); tags.Location = new Point(-16000, -16000); tags.Size = new Size(800, 650);
            tags.UpdateTagTypes(document);
            var summary = Field<Label>(tags, "_summaryLabel");
            Check(!summary.IsDisposed && summary.Visible && summary.Text.Contains("已選"), "tag summary survives old container disposal");
            Render(tags, Path.Combine(output, "tag-sources.png"));
            Children(tags).OfType<TabControl>().Single().SelectedIndex = 1;
            Field<TextBox>(tags, "_offsetText").Text = "350";
            Check(summary.Text.Contains("350"), "tag summary follows offset edits");
            Render(tags, Path.Combine(output, "tag-placement.png"));
            Children(tags).OfType<TabControl>().Single().SelectedIndex = 0;
            var scope = Field<ComboBox>(tags, "_scopeCombo"); scope.SelectedItem = "連結內選取";
            var links = Field<ComboBox>(tags, "_linkCombo"); links.SelectedIndex = 0;
            Check(tags.PickRequest.Scope == YD_RevitTools.LicenseManager.Commands.AR.AutoTag.AutoTagScope.LinkedSelection && tags.PickRequest.LinkInstanceUniqueId == "link-A", "linked pick snapshots intended scope and exact instance");
            var pickLinked = Children(tags).OfType<Button>().Single(b => b.Text == "選取連結構件");
            Check(pickLinked.Visible && links.Visible, "linked mode exposes instance and element picker together");
            pickLinked.PerformClick(); tags.SetRequestCompleted();
            Check(pickRequests == 1, "linked picker dispatches modeless request");
            tags.SetLinkedSelection("link-A", new[] { "pipe-1", "pipe-1", "pipe-2" }, document.ActiveView.UniqueId);
            tags.SetSelectionCount(99);
            Check(Field<Label>(tags, "_selectionLabel").Text == "連結選取：2 個", "linked selection deduplicates and ignores local selection count");
            var arguments = new object[] { null };
            Check((bool)tags.GetType().GetMethod("TryReadOptions", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tags, arguments), "linked options can be read");
            var pickedOptions = (YD_RevitTools.LicenseManager.Commands.AR.AutoTag.AutoTagOptions)arguments[0];
            Check(pickedOptions.LinkedElementUniqueIds.SequenceEqual(new[] { "pipe-1", "pipe-2" }) && pickedOptions.LinkedSelectionViewUniqueId == document.ActiveView.UniqueId,
                "execution receives only selected identities and their view");
            string savedOptions = Newtonsoft.Json.JsonConvert.SerializeObject(pickedOptions);
            var reloadedOptions = Newtonsoft.Json.JsonConvert.DeserializeObject<YD_RevitTools.LicenseManager.Commands.AR.AutoTag.AutoTagOptions>(savedOptions);
            Check(!savedOptions.Contains("pipe-1") && reloadedOptions.LinkedElementUniqueIds.Count == 0 && reloadedOptions.LinkedSelectionViewUniqueId == "",
                "project defaults do not persist picked elements or view identity");
            tags.UpdateTagTypes(document);
            Check(Field<Label>(tags, "_selectionLabel").Text == "連結選取：2 個", "same-document refresh preserves selection");
            Render(tags, Path.Combine(output, "tag-linked-selection.png"));
            links.SelectedIndex = 1;
            Check(Field<Label>(tags, "_selectionLabel").Text == "連結選取：0 個", "switching instance clears old selection");
            tags.SetLinkedSelection("link-A", new[] { "stale" }, document.ActiveView.UniqueId);
            Check(Field<Label>(tags, "_selectionLabel").Text == "連結選取：0 個", "stale pick result cannot attach to another instance");
            tags.SetLinkedSelection("link-B", new[] { "pipe-1" }, document.ActiveView.UniqueId);
            tags.UpdateTagTypes(new Document());
            Check(Field<Label>(tags, "_selectionLabel").Text == "連結選取：0 個", "document change clears transient linked selection");
            tags.Close();
        }
        using (var content = new FlowLayoutPanel { AutoSize = true })
        {
            var value = new TextBox { Text = "原設定" }; content.Controls.Add(value);
            using (var dialog = ToolSettingsDialog.Create(content, "測試設定", new Size(600, 350))) { Offscreen(dialog); dialog.Show(); value.Text = "修改後"; dialog.Close(); }
            Check(!content.IsDisposed && value.Text == "修改後", "settings survive close");
            using (var unused = ToolSettingsDialog.Create(content, "未開啟", new Size(600, 350))) { }
            Check(!content.IsDisposed && !value.IsDisposed, "disposing unshown dialog preserves caller controls");
        }
        using (var form = new MepCheckOptionsForm(null, ElementId.InvalidElementId))
        {
            Offscreen(form); form.Show();
            var rule = Field<ComboBox>(form, "_cmbDrainRule"); var direction = Field<ComboBox>(form, "_cmbDirection");
            Check(!direction.Visible, "elevation hides downstream direction");
            rule.SelectedIndex = 2; Check(direction.Visible, "direction rule shows downstream direction");
            rule.SelectedIndex = 1;
            var pick = Children(form).OfType<Button>().Single(b => b.Text == "選取排出口…");
            Check(pick.Visible && !direction.Visible, "outlet rule shows only outlet controls");
            form.Size = new Size(680, 620); Render(form, Path.Combine(output, "mep-check-outlet.png"));
            Children(form).OfType<TabControl>().Single().SelectedIndex = 1;
            Render(form, Path.Combine(output, "mep-check-equipment.png"));
            Children(form).OfType<TabControl>().Single().SelectedIndex = 0;
            form.Hide();
            Check(ModalAction(form, () => pick.PerformClick()) == DialogResult.Retry && !form.IsDisposed, "picker returns control without disposing settings");
            form.SetOutlet(new ElementId(42)); form.SetOutlet(null, true);
            Check(ModalAction(form, () => ((Button)form.AcceptButton).PerformClick()) == DialogResult.OK, "reopened dialog can run");
            Check(form.Options.OutletElementId.Value == 42 && form.Options.DrainFlowRule == MepDrainFlowRule.OutletElement, "cancelled pick preserves outlet and selected rule");
        }
        var routing = new YD_RevitTools.LicenseManager.Commands.MEP.AutoPipeRouting.UI.MainWindow();
        var slope = (System.Windows.Controls.TextBox)routing.FindName("txtSlope"); slope.Text = "200";
        Check(((System.Windows.Controls.TextBlock)routing.FindName("SlopeHint")).Text.Contains("0.5%"), "slope percent uses denominator");
        RenderWpf(routing, Path.Combine(output, "routing.png")); routing.Close();
        foreach (var item in new[] { ("room-finish", "Commands/AR/Finishings/RoomFinish/UI/MainWindow.xaml"), ("pipe-iso", "Commands/MEP/PipeToISO/MainWindow.xaml") })
        {
            // Render the actual XAML without Revit-bound code-behind; these are layout checks only.
            var xml = XDocument.Load(item.Item2); xml.Root.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Remove();
            var events = new HashSet<string> { "Click", "SelectionChanged", "Checked", "Unchecked", "Loaded", "MouseDoubleClick", "CellEditEnding", "CurrentCellChanged", "PreviewKeyDown", "TextChanged", "PreviewMouseLeftButtonDown", "RowEditEnding", "Sorting" };
            foreach (var attribute in xml.Descendants().Attributes().Where(a => events.Contains(a.Name.LocalName)).ToList()) attribute.Remove();
            var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Parse(xml.ToString());
            window.Width = item.Item1 == "room-finish" ? 900 : 800; window.Height = 650;
            RenderWpf(window, Path.Combine(output, item.Item1 + ".png"));
            if (window.FindName("tabMain") is System.Windows.Controls.TabControl tabs) { tabs.SelectedIndex = 1; RenderWpf(window, Path.Combine(output, "room-list.png")); }
            window.Close();
        }
        Console.WriteLine($"PASS {count} module UI checks; Revit picking and model operations not exercised.");
    }
}
