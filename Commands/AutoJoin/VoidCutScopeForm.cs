using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class VoidCutScopeForm : Form
    {
        private readonly ComboBox scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly CheckedListBox categories = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        internal bool CurrentView => scope.SelectedIndex == 1;
        internal HashSet<string> TargetKeys => new HashSet<string>(categories.CheckedItems.Cast<Item>().Select(x => x.Key));
        private sealed class Item
        {
            internal string Key, Text;
            public override string ToString() => Text;
        }
        internal VoidCutScopeForm()
        {
            Text = "HB_BIM 自動切割";
            Font = new Font("Microsoft JhengHei UI", 10);
            ClientSize = new Size(380, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            scope.Items.AddRange(new object[] { "手動選取", "目前視圖可見" });
            scope.SelectedIndex = 0;
            var names = new Dictionary<string, string> { ["Ceiling"] = "天花板", ["Column"] = "柱",
                ["Floor"] = "樓板", ["GenericModel"] = "一般模型", ["Roof"] = "屋頂", ["Wall"] = "牆",
                ["StructuralColumn"] = "結構柱", ["StructuralFloor"] = "結構樓板",
                ["StructuralFoundation"] = "結構基礎", ["StructuralFraming"] = "結構構架", ["StructuralWall"] = "結構牆" };
            foreach (var spec in CategoryCatalog.Specs)
                categories.Items.Add(new Item { Key = spec.Key, Text = names.TryGetValue(spec.Key, out var name) ? name : spec.DisplayName },
                    spec.Key == "Floor" || spec.Key == "StructuralFloor");
#if REVIT2024 || REVIT2025 || REVIT2026
            categories.Items.Add(new Item { Key = "Toposolid", Text = "地形實體（母體）" });
            categories.Items.Add(new Item { Key = "ToposolidSubdivision", Text = "地形實體子分區" });
#endif
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var next = new Button { Text = "下一步", AutoSize = true };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            next.Click += (_, __) => {
                if (CurrentView && TargetKeys.Count == 0) return;
                DialogResult = DialogResult.OK; Close();
            };
            buttons.Controls.Add(next); buttons.Controls.Add(cancel);
            categories.Enabled = false;
            scope.SelectedIndexChanged += (_, __) => { categories.Enabled = CurrentView; next.Enabled = !CurrentView || categories.CheckedItems.Count > 0; };
            categories.ItemCheck += (_, e) => {
                int count = categories.CheckedItems.Count + (e.NewValue == CheckState.Checked ? 1 : -1);
                next.Enabled = !CurrentView || count > 0;
            };
            layout.Controls.Add(scope, 0, 0);
            layout.Controls.Add(new Label { Text = "被切割類別", AutoSize = true }, 0, 1);
            layout.Controls.Add(categories, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            AcceptButton = next; CancelButton = cancel;
        }
    }
}
