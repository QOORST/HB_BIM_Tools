using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using YD_RevitTools.LicenseManager.Commands.AR.AutoJoin;
using YD_RevitTools.LicenseManager.Commands.AR.AutoTag;

namespace Autodesk.Revit.DB {
    public enum BuiltInCategory { OST_Ceilings, OST_Columns, OST_Floors, OST_GenericModel,
        OST_Roofs, OST_Walls, OST_StructuralColumns, OST_StructuralFoundation, OST_StructuralFraming }
}
internal static class Program {
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
    static IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] {x}.Concat(Descendants(x)));
    static void ShowForPreview(Form form) {
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-16000,-16000);
        form.Show(); Application.DoEvents();
    }
    static void Render(Form form, string file) {
        Application.DoEvents();
        using var image = new Bitmap(form.Width,form.Height);
        form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(file);
    }
    static void RenderWpf(System.Windows.Window window, string file) {
        window.ShowInTaskbar=false; window.ShowActivated=false; window.Left=-16000; window.Top=-16000;
        window.Show(); window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using(var stream=File.Create(file)) encoder.Save(stream);
    }
    [STAThread] static void Main(string[] args) {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware); Application.EnableVisualStyles();
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/tool-suite-qa"); Directory.CreateDirectory(output);
        var boxes = new List<YDBIM.AutoDimension.Core.DimensionTextBox> {
            new() { Index=0, Along=0, Across=0, Width=6, Height=3, Movable=true },
            new() { Index=1, Along=2, Across=0, Width=6, Height=3, Movable=true },
            new() { Index=2, Along=30, Across=0, Width=6, Height=3, Movable=true }
        };
        var plan = YDBIM.AutoDimension.Core.DimensionTextLayout.Plan(boxes, .8, 5, out int conflicts);
        Check(plan[0]==0 && plan[1]==-5 && plan[2]==0 && conflicts==0,"crowded text shifts while clear text stays");
        foreach(var b in boxes) b.Across=plan[b.Index];
        var repeat=YDBIM.AutoDimension.Core.DimensionTextLayout.Plan(boxes,.8,5,out conflicts);
        Check(boxes.All(b=>repeat[b.Index]==b.Across),"repeat layout does not drift");
        boxes[0].Movable=false; boxes[1].Movable=false; boxes[1].Across=0;
        plan=YDBIM.AutoDimension.Core.DimensionTextLayout.Plan(boxes,.8,5,out conflicts);
        Check(plan[0]==0 && plan[1]==0 && conflicts==2,"fixed collisions remain reported");
        bool badLayout=false;
        try { YDBIM.AutoDimension.Core.DimensionTextLayout.Plan(boxes,double.NaN,5,out _); }
        catch(ArgumentException) { badLayout=true; }
        Check(badLayout,"reject nonfinite layout inputs");
        using(var sleeves=new YD_RevitTools.LicenseManager.Commands.MEP.PipeSleeveSettingsForm(12)) {
            ShowForPreview(sleeves);
            Check(sleeves.ClearanceMm==50 && sleeves.IncludeCurrentModel && sleeves.IncludeLinks && sleeves.AutoNumber && sleeves.SkipExisting && sleeves.LimitToActiveView,"sleeve settings preserve previous defaults");
            var toggles=Descendants(sleeves).OfType<CheckBox>().ToList();
            foreach(var toggle in toggles) toggle.Checked=false;
            Check(!sleeves.IncludeCurrentModel && !sleeves.IncludeLinks && !sleeves.ExcludeAdditionElements && !sleeves.AutoNumber && !sleeves.SkipExisting && !sleeves.LimitToActiveView,"sleeve settings read current choices");
            foreach(var toggle in toggles) toggle.Checked=true;
            Render(sleeves,Path.Combine(output,"pipe-sleeve-settings.png")); sleeves.Close();
        }
        var crossing = new List<YDBIM.AutoDimension.Core.DimensionTextBox> {
            new() { Index=10, Along=0, Across=0, Width=8, Height=3 },
            new() { Index=20, Along=2, Across=0, Width=3, Height=8 },
            new() { Index=30, Along=50, Across=50, Width=3, Height=8 }
        };
        Check(YDBIM.AutoDimension.Core.DimensionTextLayout.FindConflicts(crossing,.8).SetEquals(new[]{10,20}),"cross-chain perpendicular bounds detected without unrelated boxes");
        crossing[1].Along=6;
        Check(YDBIM.AutoDimension.Core.DimensionTextLayout.FindConflicts(crossing,0).Count==0,"separated bounds clear");
        Check(YDBIM.AutoDimension.Core.DimensionTextLayout.FindConflicts(crossing,.8).Count==2,"paper clearance detects near collision");
        using(var cleanup=new YDBIM.AutoDimension.UI.DimensionTextCleanupForm()) {
            ShowForPreview(cleanup);
            Check(cleanup.Gap==.8 && cleanup.Offset==5,"paper spacing defaults");
            Render(cleanup,Path.Combine(output,"dimension-text-cleanup.png")); cleanup.Close();
        }
        var csv = YD_RevitTools.LicenseManager.Helpers.Data.CsvRecordReader.Read(new StringReader(
            "\uFEFF# comment\r\nId,Note,Empty\r\n1,\"first, line\r\n# still content \"\"quoted\"\"\",\r\n\"#literal\",ok,last"));
        Check(csv.Count == 3, "CSV quoted multiline is one record");
        Check(csv[1][1] == "first, line\r\n# still content \"quoted\"", "CSV preserves newline, comma, escaped quotes and embedded comment");
        Check(csv[1].Count == 3 && csv[1][2] == "", "CSV preserves empty trailing field");
        Check(csv[2][0] == "#literal", "CSV quoted hash is not comment");
        foreach (var invalid in new[] {"1,\"unfinished", "1,\"closed\"oops", "1,unquoted\"quote"}) {
            bool rejected=false;
            try { YD_RevitTools.LicenseManager.Helpers.Data.CsvRecordReader.Read(new StringReader(invalid)); }
            catch (FormatException) { rejected=true; }
            Check(rejected,"malformed CSV rejected before model write");
        }
        foreach (string text in new[] {"NaN","Infinity","-Infinity","-1","10001","abc",""})
            Check(!TagAlignOptionsForm.TryParseSpacing(text,out _),"reject invalid spacing: " + text);
        foreach (string text in new[] {"NaN","Infinity","-Infinity","abc",""})
            Check(!YD_RevitTools.LicenseManager.Commands.Family.SliderSettingsWindow.TryParseFinite(text,out _),"reject nonfinite slider value: " + text);
        foreach (string text in new[] {"0","300","22.5","10000"})
            Check(TagAlignOptionsForm.TryParseSpacing(text,out _),"accept spacing: " + text);
        using (var form = new TagAlignOptionsForm()) {
            ShowForPreview(form);
            foreach (var button in Descendants(form).OfType<Button>()) {
                Rectangle bounds = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                Check(form.ClientRectangle.Contains(bounds),"alignment action fully visible: " + button.Text);
            }
            Render(form,Path.Combine(output,"tag-align.png"));
            ((Button)form.AcceptButton).PerformClick();
            Check(form.Options.SpacingMillimeters == 300 && form.Options.Scope == TagAlignScope.Selection,"alignment defaults preserved");
        }
        var all = new AutoJoinSettings { EnabledCategoryKeys = CategoryCatalog.AllKeys.ToList(), PriorityKeys = CategoryCatalog.LegacyDefaultPriorityKeys.ToList() };
        all.Normalize();
        Check(all.EnabledCategoryKeys.SequenceEqual(CategoryCatalog.AllKeys),"normalize preserves explicit all categories");
        Check(all.PriorityKeys.SequenceEqual(CategoryCatalog.LegacyDefaultPriorityKeys),"normalize preserves current-schema custom priority");
        string settingsFile = Path.Combine(output,"join-roundtrip.xml");
        SettingsSerializer.Save(settingsFile,all); var loaded = SettingsSerializer.LoadOrDefault(settingsFile);
        Check(loaded.EnabledCategoryKeys.SequenceEqual(all.EnabledCategoryKeys),"XML roundtrip preserves all categories");
        Check(loaded.PriorityKeys.SequenceEqual(all.PriorityKeys),"XML roundtrip preserves priority");
        var legacy = new AutoJoinSettings { SchemaVersion = 1, EnabledCategoryKeys = CategoryCatalog.AllKeys.ToList() }; legacy.Normalize();
        var subset = new AutoJoinSettings { EnabledCategoryKeys = new List<string> {"Ceiling"} };
        SettingsSerializer.Save(settingsFile,subset);
        Check(SettingsSerializer.LoadOrDefault(settingsFile).EnabledCategoryKeys.SequenceEqual(subset.EnabledCategoryKeys),"loading subset never adds default categories");
        Check(legacy.EnabledCategoryKeys.SequenceEqual(CategoryCatalog.DefaultEnabledKeys),"legacy migration retained");
        var malformed = new AutoJoinSettings { EnabledCategoryKeys = null, PriorityKeys = null }; malformed.Normalize();
        Check(malformed.EnabledCategoryKeys.Count > 0 && malformed.PriorityKeys.Count > 0,"null lists recover defaults");
        foreach (bool alignOnly in new[] {false,true}) {
            using var form = new AutoJoinForm(new AutoJoinSettings(),alignOnly); ShowForPreview(form);
            Descendants(form).OfType<Button>().Single(b=>b.Text=="全選").PerformClick();
            Check(form.BuildSettings().EnabledCategoryKeys.Count == CategoryCatalog.AllKeys.Count,"UI all-selection preserved");
            Check(((Button)form.AcceptButton).Text == (alignOnly ? "執行對齊" : "自動接合"),"correct primary action");
            Render(form,Path.Combine(output,alignOnly ? "wall-align.png" : "auto-join.png"));
        }
        using (var form = new AutoJoinForm(new AutoJoinSettings())) {
            form.EnableModeless(); ShowForPreview(form);
            int requests=0; form.RunRequested += () => requests++;
            var run=(Button)form.AcceptButton;
            run.PerformClick();
            Check(requests==1 && form.Visible && !form.IsDisposed,"modeless run leaves form open");
            form.SetBusy(true); run.PerformClick();
            Check(requests==1,"busy modeless form blocks duplicate run");
            form.Close(); Check(!form.IsDisposed,"busy modeless form cannot close pending request");
            form.SetBusy(false); run.PerformClick();
            Check(requests==2,"modeless form can run again after completion");
            int selections=0,picks=0;
            form.SelectionRequested += () => selections++;
            form.PickRequested += () => picks++;
            Descendants(form).OfType<Button>().Single(b=>b.Text=="使用目前選取").PerformClick();
            Descendants(form).OfType<Button>().Single(b=>b.Text=="重新選取").PerformClick();
            Check(selections==1 && picks==1,"modeless selection controls dispatch requests");
            form.UseSelectedScope(); Check(form.BuildSettings().Scope==AutoJoinScope.SelectedElements,"selection request updates scope");
            form.SetStatus("成功 2；失敗 0；可繼續操作模型。");
            var categoryRows=Descendants(form).OfType<CheckBox>().Where(c=>c.Tag is string).ToList();
            Check(categoryRows.Count==11 && categoryRows.All(c=>c.Height>=32),"category rows have large click targets");
            var categoryRow=categoryRows[0]; categoryRow.Checked=!categoryRow.Checked;
            Check(form.BuildSettings().EnabledCategoryKeys.Contains((string)categoryRow.Tag)==categoryRow.Checked,"row check updates settings");
            Check(Descendants(form).OfType<Label>().Any(l=>l.Text==$"已選 {categoryRows.Count(c=>c.Checked)}／11"),"selected category count updates");
            Render(form,Path.Combine(output,"auto-join-modeless.png"));
            form.Size = form.MinimumSize;
            Application.DoEvents();
            Render(form,Path.Combine(output,"auto-join-modeless-compact.png"));
            foreach(var button in Descendants(form).OfType<Button>().Where(b=>b.Visible))
                Check(form.ClientRectangle.Contains(form.PointToClient(button.PointToScreen(new Point(button.Width-1,button.Height-1)))),"modeless button within form: "+button.Text);
            foreach(var control in Descendants(form).Where(c=>c.Visible && (c is Button || c is CheckBox))) {
                if(control is CheckBox && control.Tag is string) continue; // 類別清單允許垂直捲動。
                for(var parent=control.Parent; parent!=null; parent=parent.Parent)
                    Check(parent.ClientRectangle.Contains(parent.PointToClient(control.PointToScreen(new Point(control.Width-1,control.Height-1)))),"control not clipped by parent: "+control.Text);
            }
            form.Close(); Check(form.IsDisposed,"idle modeless form closes normally");
        }
        var slider = new YD_RevitTools.LicenseManager.Commands.Family.SliderSettingsWindow(0,1000,10);
        RenderWpf(slider,Path.Combine(output,"slider-settings.png"));
        Check(slider.ActualHeight > 250,"slider expands to fit controls"); slider.Close();
        var rename = new YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.UI.FamilyParameterRenameWindow(
            new YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Services.FamilyParameterRenameService());
        RenderWpf(rename,Path.Combine(output,"family-rename.png"));
        var renameButton=(System.Windows.Controls.Button)typeof(YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.UI.FamilyParameterRenameWindow)
            .GetField("_updateButton",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(rename);
        var point=renameButton.TranslatePoint(new System.Windows.Point(),rename);
        Check(point.X>=0 && point.X+renameButton.ActualWidth<=rename.ActualWidth,"rename primary action fits window");
        rename.Close();
        Console.WriteLine($"PASS: {checks} checks; previews: {output}");
    }
}
