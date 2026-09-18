using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;

static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
        _checks++;
    }
    private static IEnumerable<T> Controls<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T item) yield return item;
            foreach (var descendant in Controls<T>(child)) yield return descendant;
        }
    }
    private static UiVm Vm(Document doc) => new UiVm(doc, new UIDocument(doc));
    private static UiVm.UiMain Window(UiVm vm) => new UiVm.UiMain(vm, new ExternalEvent(), new ExternalEvent());

    private static void RenderAndCheck(UiVm.UiMain window, double width, double height, double dpi, string name)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var footerButton = Controls<Button>(root).Single(b => Equals(b.Content, "開始生成"));
        var footerBounds = footerButton.TransformToAncestor(root).TransformBounds(new Rect(footerButton.RenderSize));
        Check(footerBounds.Bottom <= height && footerBounds.Right <= width && footerBounds.Top >= 0, name + " footer stays visible");
        foreach (var cb in Controls<CheckBox>(root))
        {
            var bounds = cb.TransformToAncestor(root).TransformBounds(new Rect(cb.RenderSize));
            Check(bounds.Width >= cb.DesiredSize.Width - 1 && bounds.Right <= width, name + " checkbox fits: " + cb.Content);
        }
        var scroll = Controls<ScrollViewer>(root).First();
        if (height < 500)
        {
            Check(scroll.ScrollableHeight > 0, name + " compact content scrolls");
            scroll.ScrollToEnd();
            root.UpdateLayout();
            var lastMaterial = Controls<ComboBox>(root).Last();
            var bounds = lastMaterial.TransformToAncestor(root).TransformBounds(new Rect(lastMaterial.RenderSize));
            Check(bounds.Bottom <= footerBounds.Top && bounds.Top >= 0, name + " last material reachable above footer");
        }
        var bitmap = new RenderTargetBitmap((int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../", name + ".png"));
        using (var stream = File.Create(path)) encoder.Save(stream);
        Console.WriteLine("IMAGE " + path);
    }

    [STAThread]
    public static void Main()
    {
        var doc = new Document();
        doc.Materials.Add(new Material { Name = "A", Id = new ElementId(101) });
        doc.Materials.Add(new Material { Name = "B", Id = new ElementId(102) });
        var first = Vm(doc);
        var window = Window(first);
        Check(first.ThicknessMm == 20 && first.BottomOffsetMm == 30, "initial numeric defaults");
        var checks = Controls<CheckBox>(window).ToArray();
        Check(checks.Length == 11, "all category and processing controls covered");
        foreach (var cb in checks) cb.IsChecked = !cb.IsChecked;
        var expected = typeof(UiVm).GetProperties().Where(p => p.PropertyType == typeof(bool)).ToDictionary(p => p.Name, p => p.GetValue(first));
        var inputs = Controls<TextBox>(window).ToArray();
        inputs[0].Text = "27.5";
        inputs[1].Text = "12";
        var combos = Controls<ComboBox>(window).ToArray();
        Check(combos.Length == 4, "all material controls covered");
        for (int i = 0; i < combos.Length; i++) combos[i].SelectedIndex = 1 + i % 2;
        first.SetPicked(new List<ElementId> { new ElementId(999) });
        window.Close();

        var second = Vm(doc);
        var reopened = Window(second);
        foreach (var entry in expected)
            Check(Equals(typeof(UiVm).GetProperty(entry.Key).GetValue(second), entry.Value), "retains " + entry.Key);
        Check(second.ThicknessMm == 27.5 && second.BottomOffsetMm == 12, "retains numeric edits without running");
        Check(Controls<TextBox>(reopened).Select(t => t.Text).SequenceEqual(new[] { "27.5", "12" }), "restored numbers displayed");
        Check(Controls<ComboBox>(reopened).Select(c => c.SelectedIndex).SequenceEqual(new[] { 1, 2, 1, 2 }), "restored material selections displayed");
        Check(second.WallMaterialId.Value == 101 && second.ColumnMaterialId.Value == 102 && second.BeamMaterialId.Value == 101 && second.SlabMaterialId.Value == 102, "restored material IDs used by generation");
        var picked = (IList<ElementId>)typeof(UiVm).GetField("_pickedHostIds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(second);
        Check(picked.Count == 0, "selected elements not persisted");
        Check(!second.IsRunPending, "run state not persisted");

        inputs = Controls<TextBox>(reopened).ToArray();
        foreach (string invalid in new[] { "", "abc", "NaN", "Infinity", "-1" })
        {
            inputs[0].Text = invalid;
            Check(second.ThicknessMm == 27.5, "invalid edit does not overwrite: " + invalid);
        }
        inputs[1].Text = "0";
        Check(second.BottomOffsetMm == 0, "zero offset retained");
        reopened.Close();
        doc.Materials.RemoveAt(0);
        var deletedMaterial = Vm(doc);
        var third = Window(deletedMaterial);
        Check(deletedMaterial.WallMaterialId.Value == -1 && deletedMaterial.BeamMaterialId.Value == -1, "deleted materials fall back to unspecified");
        Check(deletedMaterial.ColumnMaterialId.Value == 102, "remaining material selection retained");
        third.Close();

        var otherDoc = new Document();
        var other = Vm(otherDoc);
        Check(other.ThicknessMm == 20 && other.IncludeWall && !other.ActiveViewOnly, "other project uses defaults");
        other.ThicknessMm = 45;
        Check(Vm(doc).ThicknessMm == 27.5, "project settings isolated");
        var app = new UIApplication { ActiveUIDocument = new UIDocument(otherDoc) };
        Check(!second.IsActiveDocument(app) && other.IsActiveDocument(app), "cross-project operations rejected");
        doc.IsValidObject = false;
        app.ActiveUIDocument = new UIDocument(doc);
        Check(!second.IsActiveDocument(app), "closed document rejected");
        UiVm.ClearClosedSessions();
        var sessions = (System.Collections.IDictionary)typeof(UiVm).GetField("Sessions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Check(!sessions.Contains(doc), "closed document settings removed");
        Check(sessions.Contains(otherDoc) && Vm(otherDoc).ThicknessMm == 45, "other open project survives cleanup");
        Check(Vm(new Document()).ThicknessMm == 20, "new document session resets defaults");
        var previewDoc = new Document { Title = "興中 · 模板測試專案" };
        previewDoc.Materials.Add(new Material { Name = "覆膜合板 / 18 mm / 標準模板材質", Id = new ElementId(301) });
        var preview = Window(Vm(previewDoc));
        foreach (var combo in Controls<ComboBox>(preview)) combo.SelectedIndex = 1;
        RenderAndCheck(preview, 524, 650, 96, "formwork-settings-default");
        RenderAndCheck(preview, 524, 650, 144, "formwork-settings-150");
        RenderAndCheck(preview, 444, 400, 192, "formwork-settings-compact-200");
        preview.Close();
        Console.WriteLine($"{_checks} checks passed; offline Revit stubs, no live model validation.");
    }
}
