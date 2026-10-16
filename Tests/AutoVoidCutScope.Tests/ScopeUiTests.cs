using System;
using System.Linq;
using System.Windows.Forms;
using YD_RevitTools.LicenseManager.Commands.AR.AutoJoin;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    // The UI only consumes keys and labels; no Revit objects are fabricated here.
    internal static class CategoryCatalog
    {
        internal sealed class Spec { public string Key; public string DisplayName => Key; }
        public static readonly Spec[] Specs = new[] { "Floor", "StructuralFloor", "Wall", "StructuralFraming" }
            .Select(x => new Spec { Key = x }).ToArray();
    }
}
internal static class ScopeUiTests
{
    private static int count;
    private static void Check(bool value) { count++; if (!value) throw new Exception("UI check failed: " + count); }
    [STAThread]
    private static void Main()
    {
        using (var form = new VoidCutScopeForm())
        {
            var controls = form.Controls[0].Controls;
            var scope = controls.OfType<ComboBox>().Single();
            var list = controls.OfType<CheckedListBox>().Single();
            Check(!form.CurrentView);
            Check(!list.Enabled);
            Check(form.TargetKeys.SetEquals(new[] { "Floor", "StructuralFloor" }));
            scope.SelectedIndex = 1;
            Check(form.CurrentView && list.Enabled);
            Check(!form.TargetKeys.Contains("Toposolid"));
            Check(!form.TargetKeys.Contains("ToposolidSubdivision"));
            for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false);
            var next = (Button)form.AcceptButton;
            Check(!next.Enabled);
            list.SetItemChecked(list.Items.Count - 1, true);
            Check(form.TargetKeys.SetEquals(new[] { "ToposolidSubdivision" }));
            Check(next.Enabled);
            scope.SelectedIndex = 0;
            Check(!list.Enabled && next.Enabled);
        }
        Console.WriteLine($"PASS: {count} scope UI state checks; no native Revit visibility tested.");
    }
}
