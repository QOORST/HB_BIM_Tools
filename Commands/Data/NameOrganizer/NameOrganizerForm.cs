using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.Data.NameOrganizer
{
    internal sealed class NameOrganizerForm : Form
    {
        private readonly Action<NameAction, List<NameRow>> _request;
        private List<NameRow> _rows = new List<NameRow>();
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        private readonly ComboBox _kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155 };
        private readonly TextBox _search = new TextBox { Width = 175 };
        private readonly CheckBox _changed = new CheckBox { Text = "只看待套用", AutoSize = true };
        private readonly CheckBox _fromAudit = new CheckBox { Text = "只看帶入項目", AutoSize = true };
        private readonly Label _summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly TextBox _detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private string _focusId;
        private bool _busy;
        internal NameOrganizerForm(Action<NameAction, List<NameRow>> request, string focusId = null)
        {
            _request = request; _focusId = focusId;
            Text = "HB｜模型名稱整理"; Font = new Font("Microsoft JhengHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(1100, 740); MinimumSize = new Size(860, 590); StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
            foreach (var height in new[] { 52, 38, 40, 30 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 85)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.Controls.Add(new Label { Text = "模型名稱整理\r\n選分類、勾選項目，再預覽與套用；圖紙只改名稱，圖號保持原值。", Dock = DockStyle.Fill }, 0, 0);
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            _kind.Items.Add("全部分類"); foreach (NameKind k in Enum.GetValues(typeof(NameKind))) _kind.Items.Add(NamePolicy.Label(k)); _kind.SelectedIndex = 1;
            filters.Controls.AddRange(new Control[] { _kind, new Label { Text = "搜尋", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, _search, _changed, _fromAudit });
            _fromAudit.Visible = focusId != null; _fromAudit.Checked = focusId != null; if (focusId != null) _kind.SelectedIndex = 0;
            root.Controls.Add(filters, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            actions.Controls.Add(MakeButton("勾選畫面項目", () => SelectVisible(true), 115));
            actions.Controls.Add(MakeButton("取消畫面勾選", () => SelectVisible(false), 115));
            actions.Controls.Add(MakeButton("批次改名…", EditBatch, 100));
            actions.Controls.Add(MakeButton("重設畫面預覽", Reset, 115));
            var advanced = MakeButton("更多 ▾", () => { }, 80);
            var menu = new ContextMenuStrip();
            menu.Items.Add("重新讀取", null, (_, _) => Reload());
            menu.Items.Add("刪除勾選項目（先預演）…", null, (_, _) => Submit(NameAction.Delete));
            advanced.Click += (_, _) => menu.Show(advanced, new Point(0, advanced.Height)); actions.Controls.Add(advanced);
            root.Controls.Add(actions, 0, 2); root.Controls.Add(_summary, 0, 3);
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Selected", HeaderText = "勾選", Width = 48 });
            AddColumn("CurrentName", "目前名稱", 180, true); AddColumn("NewName", "新名稱", 180, false);
            AddColumn("KindName", "分類", 100, true); AddColumn("Usage", "使用情況", 150, true);
            var status = new DataGridViewTextBoxColumn { DataPropertyName = "Status", HeaderText = "狀態", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 95, ReadOnly = true }; _grid.Columns.Add(status);
            _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += (_, _) => Summary(); _grid.CurrentCellChanged += (_, _) => Detail();
            _grid.CellFormatting += (_, e) => { if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is NameRow row) _grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = row.Changed ? Color.LightYellow : Color.White; };
            _grid.DataError += (_, e) => { e.ThrowException = false; _summary.Text = "欄位輸入無法套用，請確認內容。"; };
            root.Controls.Add(_grid, 0, 4); root.Controls.Add(_detail, 0, 5);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            footer.Controls.Add(MakeButton("套用勾選變更…", () => Submit(NameAction.Apply), 140));
            footer.Controls.Add(MakeButton("重新檢查…", Audit, 110));
            footer.Controls.Add(MakeButton("關閉", Close, 80)); root.Controls.Add(footer, 0, 6); Controls.Add(root);
            _kind.SelectedIndexChanged += (_, _) => Filter(); _search.TextChanged += (_, _) => Filter(); _changed.CheckedChanged += (_, _) => Filter(); _fromAudit.CheckedChanged += (_, _) => Filter();
            FormClosing += (_, e) => { if (_busy) { e.Cancel = true; return; } if (_rows.Any(r => r.Changed) && MessageBox.Show(this, "尚有未套用的名稱預覽，要放棄並關閉嗎？", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) e.Cancel = true; };
            FormClosed += (_, _) => menu.Dispose();
            Shown += (_, _) => { var area = Screen.FromHandle(Handle).WorkingArea; MinimumSize = new Size(Math.Min(860, area.Width), Math.Min(590, area.Height)); Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height)); };
        }
        private static Button MakeButton(string text, Action action, int width) { var button = new Button { Text = text, Width = width, Height = 29 }; button.Click += (_, _) => action(); return button; }
        private void AddColumn(string property, string title, int width, bool readOnly) => _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, Width = width, ReadOnly = readOnly });
        internal void SetRows(List<NameRow> rows, bool preservePreviews = false)
        {
            if (preservePreviews)
            {
                var previous = _rows.ToDictionary(r => r.UniqueId);
                foreach (var row in rows)
                    if (previous.TryGetValue(row.UniqueId, out var old))
                    {
                        row.Selected = old.Selected;
                        if (old.CurrentName == row.CurrentName) { row.NewName = old.NewName; row.Status = old.Status; }
                        else if (old.Changed) row.Status = "模型已更新";
                    }
            }
            _grid.DataSource = null; _rows = rows;
            if (_focusId != null) { var focus = rows.FirstOrDefault(r => r.UniqueId == _focusId); if (focus != null) focus.Selected = true; }
            Filter();
        }
        internal void FocusItem(string uniqueId)
        {
            if (!_rows.Any(r => r.UniqueId == uniqueId)) throw new InvalidOperationException("找不到可整理的項目，請重新檢查。");
            _focusId = uniqueId; _kind.SelectedIndex = 0; _search.Clear(); _changed.Checked = false;
            _fromAudit.Visible = true; _fromAudit.Checked = true; _rows.First(r => r.UniqueId == uniqueId).Selected = true;
            Filter(); Show(); Activate();
        }
        internal void SetBusy(bool busy) { _busy = busy; foreach (Control c in Controls) c.Enabled = !busy; UseWaitCursor = busy; }
        private List<NameRow> VisibleRows => (_grid.DataSource as List<NameRow>) ?? new List<NameRow>();
        private void Filter()
        {
            _grid.EndEdit(); var query = _search.Text.Trim();
            var selectedId = (_grid.CurrentRow?.DataBoundItem as NameRow)?.UniqueId;
            _grid.DataSource = null;
            _grid.DataSource = _rows.Where(r => (_kind.SelectedIndex == 0 || (int)r.Kind == _kind.SelectedIndex - 1) && (!_changed.Checked || r.Changed)
                && (!_fromAudit.Checked || r.UniqueId == _focusId)
                && (query.Length == 0 || ((r.CurrentName ?? "") + " " + r.Context + " " + r.NewName).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            foreach (DataGridViewRow row in _grid.Rows) if (((NameRow)row.DataBoundItem).UniqueId == selectedId) _grid.CurrentCell = row.Cells[1];
            Summary(); Detail();
        }
        private void Summary()
        {
            var visible = VisibleRows; _summary.Text = $"顯示 {visible.Count}｜畫面勾選 {visible.Count(r => r.Selected)}｜勾選待套用 {visible.Count(r => r.Selected && r.Changed)}｜其他畫面待套用 {_rows.Except(visible).Count(r => r.Changed)}";
        }
        private void Detail()
        {
            var row = _grid.CurrentRow?.DataBoundItem as NameRow;
            _detail.Text = row == null ? "只處理目前畫面已勾選的項目。使用數量供判斷影響，不作為可安全刪除的依據。" : row.KindName + "｜" + row.Context + "\r\n目前：" + row.CurrentName + "\r\n預覽：" + row.NewName + "\r\n" + row.Usage;
        }
        private void SelectVisible(bool selected) { _grid.EndEdit(); foreach (var row in VisibleRows) row.Selected = selected; _grid.Refresh(); Summary(); }
        private void Reset() { foreach (var row in VisibleRows) { row.NewName = row.CurrentName; row.Status = ""; } Filter(); }
        private void EditBatch()
        {
            _grid.EndEdit(); var selected = VisibleRows.Where(r => r.Selected).ToList();
            if (selected.Count == 0) { MessageBox.Show(this, "請先勾選要整理的項目。"); return; }
            using (var form = new NameBatchForm(selected.Count))
                if (form.ShowDialog(this) == DialogResult.OK) { NamePolicy.Preview(selected, form.Find, form.Replacement, form.Prefix, form.Suffix, form.Affix); _grid.Refresh(); Summary(); Detail(); }
        }
        private void Reload() { if (DiscardConfirmed()) Submit(NameAction.Reload); }
        private void Audit() { if (DiscardConfirmed()) Submit(NameAction.Audit); }
        private bool DiscardConfirmed() => !_rows.Any(r => r.Changed) || MessageBox.Show(this, "尚有未套用的預覽，繼續將放棄預覽。", Text, MessageBoxButtons.OKCancel) == DialogResult.OK;
        private void Submit(NameAction action)
        {
            _grid.EndEdit(); var targets = NamePolicy.Targets(VisibleRows, action == NameAction.Apply);
            if ((action == NameAction.Apply || action == NameAction.Delete) && targets.Count == 0) { MessageBox.Show(this, "目前畫面沒有符合條件的勾選項目。"); return; }
            SetBusy(true);
            try { _request(action, targets); } catch (Exception ex) { SetBusy(false); MessageBox.Show(this, ex.Message, Text); }
        }
        internal void DiscardPreviews() { foreach (var row in _rows) row.NewName = row.CurrentName; }
    }

    internal sealed class NameBatchForm : Form
    {
        private readonly ComboBox _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly TextBox _first = new TextBox { Dock = DockStyle.Fill }, _second = new TextBox { Dock = DockStyle.Fill };
        internal bool Affix => _mode.SelectedIndex == 1;
        internal string Find => _first.Text;
        internal string Replacement => _second.Text;
        internal string Prefix => _first.Text;
        internal string Suffix => _second.Text;
        internal NameBatchForm(int count)
        {
            Text = "批次改名預覽"; Size = new Size(520, 300); MinimumSize = Size; Font = new Font("Microsoft JhengHei UI", 9F); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 5 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 54, 36, 36, 36, 42 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var hint = new Label { Text = $"預覽目前勾選的 {count} 筆。每次從模型原名稱產生，區分英文字母大小寫；尚未修改模型。", Dock = DockStyle.Fill }; root.Controls.Add(hint, 0, 0); root.SetColumnSpan(hint, 2);
            root.Controls.Add(new Label { Text = "方式" }, 0, 1); root.Controls.Add(_mode, 1, 1); _mode.Items.AddRange(new object[] { "取代文字", "加前後綴" });
            var first = new Label { Text = "尋找" }; var second = new Label { Text = "取代為" }; root.Controls.Add(first, 0, 2); root.Controls.Add(second, 0, 3); root.Controls.Add(_first, 1, 2); root.Controls.Add(_second, 1, 3);
            _mode.SelectedIndexChanged += (_, _) => { first.Text = Affix ? "前綴" : "尋找"; second.Text = Affix ? "後綴" : "取代為"; }; _mode.SelectedIndex = 0;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; var ok = new Button { Text = "產生預覽" };
            ok.Click += (_, _) => { if (!Affix && Find.Length == 0) { MessageBox.Show(this, "請輸入尋找文字。"); return; } DialogResult = DialogResult.OK; };
            bar.Controls.Add(cancel); bar.Controls.Add(ok); root.Controls.Add(bar, 0, 4); root.SetColumnSpan(bar, 2); Controls.Add(root); CancelButton = cancel;
        }
    }

    internal sealed class NameImpactForm : Form
    {
        internal NameImpactForm(string title, string hint, IEnumerable<string[]> rows)
        {
            Text = title; Size = new Size(850, 570); MinimumSize = new Size(650, 430); Font = new Font("Microsoft JhengHei UI", 9F); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill }, 0, 0);
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            grid.Columns.Add("item", "項目"); grid.Columns.Add("before", "目前名稱"); grid.Columns.Add("after", "變更／影響"); foreach (var row in rows) grid.Rows.Add(row); root.Controls.Add(grid, 0, 1);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; var ok = new Button { Text = "確認執行", DialogResult = DialogResult.OK };
            bar.Controls.Add(cancel); bar.Controls.Add(ok); root.Controls.Add(bar, 0, 2); Controls.Add(root); CancelButton = cancel;
        }
    }
}
