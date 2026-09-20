using System;
using Newtonsoft.Json;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using YDBIM.AutoDimension.Core;
using YDBIM.AutoDimension.UI;
internal static class Program {
    static int assertions;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; }
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    static IEnumerable<Control> Children(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[]{x}.Concat(Children(x)));
    static Button Button(Form f, string text) => Children(f).OfType<Button>().Single(b => b.Text == text);
    static GridSelectionItem[] H() => new[]{new GridSelectionItem(new ElementId(1),"A",0), new GridSelectionItem(new ElementId(2),"B",1)};
    static GridSelectionItem[] V() => new[]{new GridSelectionItem(new ElementId(3),"1",0), new GridSelectionItem(new ElementId(4),"2",1)};
    static void Render(Form f, string path) { Application.DoEvents(); using var b = new Bitmap(f.Width,f.Height); f.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size)); b.Save(path); }
    [STAThread] static void Main(string[] args) {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        string output = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory,"screenshots"); Directory.CreateDirectory(output);
        foreach (bool locked in new[]{false,true}) {
            DimensionOptions? applied = null;
            using var form = new DimensionOptionsForm(new[]{"線性標註"},H(),V(), DimensionMode.BeamGrid,locked,applyAction:o=>applied=o,refreshAction:()=>{});
            form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-16000,-16000);
            form.Show(); form.Location=new Point(-16000,-16000); Application.DoEvents();
            var tabs=Field<TabControl>(form,"_tabControl");
            Check(tabs.TabPages.Count==3,"three tabs");
            var h=Field<CheckedListBox>(form,"_horizontalGridList");
            h.SetItemChecked(1,false);
            tabs.SelectedIndex=2; Application.DoEvents();
            Check(Field<Button>(form,"_applyButton").Text=="套用標頭","bubble primary action");
            Check(ReferenceEquals(form.AcceptButton,Field<Button>(form,"_applyButton")),"Enter follows primary action");
            Check(h.CheckedItems.Count==1,"shared selection");
            Button(form,"全部取消勾選").PerformClick();
            Check(form.TryGetOptions(out var none,out _) && none.GridBubblesOnly,"hide all valid request");
            Check(!none.HorizontalBubbleLeft && !none.HorizontalBubbleRight && !none.VerticalBubbleTop && !none.VerticalBubbleBottom,"all ends hidden");
            Check(h.CheckedItems.Count==1,"display action preserves grid selection");
            Field<Button>(form,"_applyButton").PerformClick();
            Check(applied!=null && applied.GridBubblesOnly && !applied.HorizontalBubbleRight,"apply dispatches hide request");
            Check(!Field<Button>(form,"_applyButton").Enabled,"pending disables action");
            form.SetRequestCompleted();
            Button(form,"全部勾選").PerformClick();
            Check(form.TryGetOptions(out var all,out _) && all.HorizontalBubbleLeft && all.HorizontalBubbleRight && all.VerticalBubbleTop && all.VerticalBubbleBottom,"all ends visible");
            Field<CheckBox>(form,"_bubbleLeft").Checked=false;
            Check(form.TryGetOptions(out var mixed,out _) && !mixed.HorizontalBubbleLeft && mixed.HorizontalBubbleRight,"independent end settings");
            foreach(int width in new[]{980,1120}) { form.ClientSize=new Size(width,locked?560:760); Render(form,Path.Combine(output,$"bubbles-{locked}-{width}.png")); }
            int expandedHeight = h.Height;
            form.ClientSize = new Size(820, 500); Application.DoEvents();
            Check(h.Height < expandedHeight, "grid list shrinks with host height");
            Check(h.Height >= 80, "compact grid list remains usable");
            Check(Field<Button>(form,"_applyButton").Bottom <= Field<Button>(form,"_applyButton").Parent!.ClientSize.Height, "primary action stays in footer");
            Render(form,Path.Combine(output,$"bubbles-compact-{locked}.png"));
            form.ClientSize = new Size(1120,locked?560:760); Application.DoEvents();
            Check(h.Height >= expandedHeight, "grid list expands again");
            tabs.SelectedIndex=0; Application.DoEvents();
            Check(h.CheckedItems.Count==1 && Field<Button>(form,"_applyButton").Text=="執行標註","return restores grid page");
            Render(form,Path.Combine(output,$"dimensions-{locked}.png"));
            tabs.SelectedIndex=2;
            foreach(var list in new[]{h,Field<CheckedListBox>(form,"_verticalGridList")}) for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,false);
            form.UpdateSources(new[]{"線性標註"},H(),V(),100);
            Check(h.CheckedItems.Count==0 && !form.TryGetOptions(out _,out _),"refresh preserves empty selection and validates");
            form.UpdateSources(Array.Empty<string>(),Array.Empty<GridSelectionItem>(),Array.Empty<GridSelectionItem>(),100);
            Check(h.Items.Count==0,"empty list has no fake checkbox");
            Check(Children(form).OfType<Button>().Where(b=>b.Text=="全選" || b.Text=="全不選").All(b=>!b.Enabled),"empty list actions disabled");
            Render(form,Path.Combine(output,$"empty-{locked}.png"));
            form.Close();
        }
        using (var remembered = new DimensionOptionsForm(Array.Empty<string>(),H(),V(),applyAction:_=>{})) {
            remembered.ShowInTaskbar=false; remembered.Show(); remembered.Location=new Point(-16000,-16000);
            Field<TabControl>(remembered,"_tabControl").SelectedIndex=2;
            for(int mask=0;mask<16;mask++) {
                var saved = new GridBubbleSavedSettings { Left=(mask&1)!=0, Right=(mask&2)!=0, Top=(mask&4)!=0, Bottom=(mask&8)!=0 };
                var restored=JsonConvert.DeserializeObject<GridBubbleSavedSettings>(JsonConvert.SerializeObject(saved))!;
                remembered.ApplyBubbleSettings(restored);
                Check(remembered.TryGetOptions(out var o,out _) && o.HorizontalBubbleLeft==saved.Left && o.HorizontalBubbleRight==saved.Right && o.VerticalBubbleTop==saved.Top && o.VerticalBubbleBottom==saved.Bottom,"saved visibility round trip");
            }
            var legacy=JsonConvert.DeserializeObject<GridBubbleSavedSettings>("{}")!;
            Check(!legacy.Left && legacy.Right && legacy.Top && !legacy.Bottom,"legacy default right and top");
            remembered.Close();
        }
        var distanceSaved = new Dictionary<DimensionMode,AutoDimensionSavedSettings> {
            [DimensionMode.ColumnSetout]=new AutoDimensionSavedSettings { OffsetMm=700 },
            [DimensionMode.BeamWidth]=new AutoDimensionSavedSettings { OffsetMm=900,BeamSpacingOffsetMm=1300 },
            [DimensionMode.BeamGrid]=new AutoDimensionSavedSettings { GridOffsetsInPaperSpace=true,GridPrimaryOffsetMm=8,GridOverallOffsetMm=20 }
        };
        using (var f=new DimensionOptionsForm(Array.Empty<string>(),H(),V(),savedSettings:distanceSaved,applyAction:_=>{})) {
            f.ShowInTaskbar=false; f.Show(); f.Location=new Point(-16000,-16000);
            Field<NumericUpDown>(f,"_offsetNumeric").Value=750;
            f.SelectMode(DimensionMode.BeamWidth);
            Check(f.TryGetOptions(out var beam,out _) && Math.Abs(beam.OffsetInternal*304.8-900)<0.01 && Math.Abs(beam.BeamSpacingOffsetInternal!.Value*304.8-1300)<0.01,"beam independent values");
            Check(!beam.IncludeBeamWidth && beam.IncludeBeamSpacing && beam.SelectedBeamsOnly,"sparse beam defaults");
            Field<CheckBox>(f,"_includeBeamSpacing").Checked=false;
            Check(!f.TryGetOptions(out _,out _),"empty beam content rejected");
            Field<CheckBox>(f,"_includeBeamWidth").Checked=true;
            Field<ComboBox>(f,"_beamScope").SelectedIndex=1;
            Check(f.TryGetOptions(out var widthOnly,out _) && widthOnly.IncludeBeamWidth && !widthOnly.IncludeBeamSpacing && !widthOnly.SelectedBeamsOnly,"width only whole view");
            var contentSaved=AutoDimensionSavedSettings.FromOptions(widthOnly);
            Check(contentSaved.IncludeBeamWidth && !contentSaved.IncludeBeamSpacing && !contentSaved.SelectedBeamsOnly,"beam content persisted");
            var persisted=JsonConvert.DeserializeObject<AutoDimensionSavedSettings>(JsonConvert.SerializeObject(AutoDimensionSavedSettings.FromOptions(beam)))!;
            Check(persisted.BeamSpacingOffsetMm==1300,"beam spacing persists");
            f.SelectMode(DimensionMode.ColumnSetout);
            Check(f.TryGetOptions(out var col,out _) && Math.Abs(col.OffsetInternal*304.8-750)<0.01,"column survives mode switch");
            f.SelectMode(DimensionMode.BeamGrid);
            Check(Field<NumericUpDown>(f,"_gridPrimaryOffsetNumeric").Value==20 && Field<NumericUpDown>(f,"_gridOverallOffsetNumeric").Value==-12,"grid independent saved offsets");
            f.UpdateSources(Array.Empty<string>(),H(),V(),50);
            Check(Field<Label>(f,"_gridScaleHint").Text.Contains("1000") && Field<Label>(f,"_gridScaleHint").Text.Contains("600"),"scale hint updates");
            Field<TabControl>(f,"_tabControl").SelectedIndex=1; Application.DoEvents();
            var dt=Field<TabControl>(f,"_distanceTabs"); dt.SelectedIndex=2; Application.DoEvents();
            Check(Field<TabControl>(f,"_modeTabControl").SelectedTab!.Tag!.Equals(DimensionMode.BeamWidth),"distance mode sync");
            f.Close();
        }
        using(var legacyForm=new DimensionOptionsForm(Array.Empty<string>(),H(),V(),savedSettings:new Dictionary<DimensionMode,AutoDimensionSavedSettings> { [DimensionMode.BeamWidth]=new AutoDimensionSavedSettings { OffsetMm=850 } })) {
            Check(Field<NumericUpDown>(legacyForm,"_beamSpacingOffset").Value==850,"legacy beam spacing fallback");
        }
        // Render the remaining pages from a fresh form with representative offline data.
        using (var preview = new DimensionOptionsForm(new[]{"線性標註"}, H(), V(), applyAction:_=>{}, refreshAction:()=>{})) {
            preview.ShowInTaskbar=false;
            preview.Show(); preview.Location=new Point(-16000,-16000);
            preview.ClientSize=new Size(1120,760);
            foreach (var mode in new[]{DimensionMode.ColumnSetout,DimensionMode.BeamGrid,DimensionMode.BeamWidth}) {
                preview.SelectMode(mode);
                Render(preview,Path.Combine(output,$"preview-{mode}.png"));
            }
            preview.SelectMode(DimensionMode.BeamGrid);
            Check(preview.TryGetOptions(out var defaults,out _) && defaults.GridOverallOffsetInternal < defaults.GridPrimaryOffsetInternal,"default overall closer to head");
            Field<TabControl>(preview,"_tabControl").SelectedIndex=1;
            Render(preview,Path.Combine(output,"preview-offsets.png"));
            preview.Close();
        }
        // Every display combination on forward/reversed model lines and reversed view curves.
        foreach(bool vertical in new[]{false,true}) foreach(bool reverse in new[]{false,true}) foreach(bool reverseView in new[]{false,true}) foreach(bool reverseDatum in new[]{false,true}) foreach(bool negative in new[]{false,true}) foreach(bool positive in new[]{false,true}) {
            var a=new XYZ(0,0); var b=vertical ? new XYZ(0,10):new XYZ(10,0);
            var g=new Grid { Curve=new Line(reverse?b:a,reverse?a:b), ReverseDatumEnds=reverseDatum };
            if(reverseView) g.ViewCurve=new Line(g.Curve.GetEndPoint(1),g.Curve.GetEndPoint(0));
            var d=new Document(); d.Grids[1]=g;
            var o=new DimensionOptions { GridBubblesOnly=true,SelectedHorizontalGridIds=new[]{new ElementId(1)},HorizontalBubbleLeft=negative,HorizontalBubbleRight=positive,VerticalBubbleBottom=negative,VerticalBubbleTop=positive };
            string result=GridBubbleService.Apply(d,new ViewPlan(),o);
            Check(result.Contains("完成：1 條") && g.Visible[0]==((reverse ^ reverseDatum)?positive:negative) && g.Visible[1]==((reverse ^ reverseDatum)?negative:positive),"service maps display ends");
            Check(g.Leaders.All(l=>l==null),"probe leaves no leaders");
        }
        Console.WriteLine($"PASS: {assertions} assertions; screenshots: {output}");
    }
}
