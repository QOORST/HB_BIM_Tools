using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class VoidCutSourceRow
    {
        internal string Key, Category, Family, Type;
        internal int Count;
        public override string ToString() => $"{Family} / {Type}（{Count} 個）";
    }
    internal sealed class VoidCutSourceForm : Form
    {
        private readonly ComboBox method = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox match = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly TextBox family = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox type = new TextBox { Dock = DockStyle.Fill };
        private readonly CheckedListBox rows = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
        private readonly Label count = new Label { Dock = DockStyle.Fill };
        private readonly Button next = new Button { Text = "下一步", AutoSize = true };
        private readonly List<VoidCutSourceRow> source;
        private readonly bool validView;
        internal bool Filtered => method.SelectedIndex == 1;
        internal HashSet<string> SelectedKeys => new HashSet<string>(rows.CheckedItems.Cast<VoidCutSourceRow>().Select(x => x.Key));
        internal VoidCutSourceFilter Settings => new VoidCutSourceFilter { Filtered = Filtered, Exact = match.SelectedIndex == 1,
            Category = category.SelectedIndex == 0 ? "" : category.Text, Family = family.Text.Trim(), Type = type.Text.Trim() };
        internal VoidCutSourceForm(List<VoidCutSourceRow> source, VoidCutSourceFilter settings, bool validView)
        {
            this.source = source; this.validView = validView;
            Text = "HB_BIM 自動切割｜切割來源";
            Font = new Font("Microsoft JhengHei UI", 10);
            ClientSize = new Size(560, 510); MinimumSize = new Size(480, 480);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            method.Items.AddRange(new object[] { "手動選取", "條件篩選（目前視圖）" });
            method.SelectedIndex = settings.Filtered ? 1 : 0;
            category.Items.Add("全部品類");
            foreach (var name in source.Select(x => x.Category).Distinct().OrderBy(x => x)) category.Items.Add(name);
            if (!string.IsNullOrEmpty(settings.Category) && !category.Items.Contains(settings.Category)) category.Items.Add(settings.Category);
            category.SelectedIndex = string.IsNullOrEmpty(settings.Category) ? 0 : category.Items.IndexOf(settings.Category);
            match.Items.AddRange(new object[] { "包含", "完全相符" }); match.SelectedIndex = settings.Exact ? 1 : 0;
            family.Text = settings.Family; type.Text = settings.Type;
            var labels = new[] { "來源方式", "品類", "名稱比對", "族群名稱", "類型名稱" };
            var fields = new Control[] { method, category, match, family, type };
            for (int i = 0; i < fields.Length; i++) { layout.Controls.Add(new Label { Text = labels[i], AutoSize = true }, 0, i); layout.Controls.Add(fields[i], 1, i); }
            layout.Controls.Add(rows, 0, 5); layout.SetColumnSpan(rows, 2);
            layout.Controls.Add(count, 0, 6); layout.SetColumnSpan(count, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(next); buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2); Controls.Add(layout);
            next.Click += (_, __) => { if (Filtered && (!validView || SelectedKeys.Count == 0)) return; DialogResult = DialogResult.OK; Close(); };
            method.SelectedIndexChanged += (_, __) => RefreshRows();
            category.SelectedIndexChanged += (_, __) => RefreshRows(); match.SelectedIndexChanged += (_, __) => RefreshRows();
            family.TextChanged += (_, __) => RefreshRows(); type.TextChanged += (_, __) => RefreshRows();
            rows.ItemCheck += (_, e) => {
                var selected = rows.CheckedItems.Cast<VoidCutSourceRow>().Sum(x => x.Count);
                selected += (e.NewValue == CheckState.Checked ? 1 : -1) * ((VoidCutSourceRow)rows.Items[e.Index]).Count;
                UpdateCount(selected);
            };
            AcceptButton = next; CancelButton = cancel; RefreshRows();
        }
        private void RefreshRows()
        {
            category.Enabled = match.Enabled = family.Enabled = type.Enabled = rows.Enabled = Filtered && validView;
            rows.BeginUpdate(); rows.Items.Clear();
            if (Filtered && validView)
                foreach (var row in source.Where(x => Settings.Matches(x.Category, x.Family, x.Type))) rows.Items.Add(row, true);
            rows.EndUpdate(); UpdateCount(rows.CheckedItems.Cast<VoidCutSourceRow>().Sum(x => x.Count));
        }
        private void UpdateCount(int selected)
        {
            count.Text = !Filtered ? "來源：手動選取" : !validView ? "目前視圖無法收集模型構件" :
                $"符合 {rows.Items.Cast<VoidCutSourceRow>().Sum(x => x.Count)} 個；勾選 {selected} 個";
            next.Enabled = !Filtered || (validView && selected > 0 && selected <= VoidCutPolicy.MaximumSelection);
        }
    }
}
