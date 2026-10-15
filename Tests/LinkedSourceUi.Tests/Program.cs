using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using YDBIM.AutoDimension.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        _assertions++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "screenshots"));
        Directory.CreateDirectory(output);
        foreach (int stale in new[] { -1, 99 })
        {
            using var form = new LinkedDimensionSourceForm(new[] { "結構模型 [123]", "結構模型 [456]" }, stale, 0, false);
            var ok = (Button)form.AcceptButton!;
            Check(form.SourceIndex == -1 && !ok.Enabled, "Missing or stale source must require explicit selection");
            var source = (ComboBox)form.Controls.Find("Source", true).Single();
            var category = (ComboBox)form.Controls.Find("Category", true).Single();
            source.SelectedIndex = 1;
            Check(ok.Enabled && form.SourceIndex == 1, "Explicit source enables confirmation");
            category.SelectedIndex = -1;
            Check(!ok.Enabled, "Missing category disables confirmation");
            category.SelectedIndex = 2;
            Check(ok.Enabled && form.CategoryIndex == 2, "Category selection restores confirmation");
            source.SelectedIndex = -1;
            Check(!ok.Enabled, "Cleared source disables confirmation");
            Check(((Button)form.CancelButton!).DialogResult == DialogResult.Cancel, "Escape cancels");
        }
        using (var empty = new LinkedDimensionSourceForm(Array.Empty<string>(), 0, 99, false))
            Check(empty.SourceIndex == -1 && empty.CategoryIndex == -1 && !((Button)empty.AcceptButton!).Enabled, "Empty sources and invalid category cannot submit");
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            using var form = new LinkedDimensionSourceForm(new[] { "結構模型 [123]", "建築模型 [456]" }, 1, 2, true);
            Check(form.SourceIndex == 1 && form.CategoryIndex == 2, "Remembered selections restored");
            Check(!form.Controls.Find("Category", true).Single().Enabled, "Requested category locked");
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-16000, -16000);
            form.Show();
            if (scale != 1)
            {
                form.Scale(new SizeF(scale, scale));
                form.Font = new Font(form.Font.FontFamily, form.Font.Size * scale, form.Font.Style);
            }
            Application.DoEvents();
            var controls = form.Controls.Find("Source", true).Concat(form.Controls.Find("Category", true))
                .Concat(new Control[] { (Button)form.AcceptButton!, (Button)form.CancelButton! }).ToArray();
            var bounds = controls.Select(c => form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle))).ToArray();
            for (int i = 0; i < controls.Length; i++)
            {
                Check(form.ClientRectangle.Contains(bounds[i]), "Control stays inside form at scale " + scale);
                Check(controls[i].Height >= controls[i].PreferredSize.Height, "Control text height fits at scale " + scale);
                for (int j = i + 1; j < controls.Length; j++)
                    Check(!bounds[i].IntersectsWith(bounds[j]), "Controls do not overlap at scale " + scale);
            }
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, "chooser-" + (int)(scale * 100) + ".png"));
            form.Close();
        }
        Console.WriteLine("PASS: " + _assertions + " assertions; screenshots: " + output);
    }
}
