using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.Drawing.Standards
{
    internal sealed class ViewTemplateManagerForm : Form
    {
        private readonly Func<TemplateInventory> _read;
        private readonly Func<TemplateCreationRequest, string> _create;
        private TemplateInventory _inventory;
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White };
        private readonly TextBox _detail = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Button _duplicate = new Button { Text = "複製選取樣板…", Width = 140, Height = 30 };
        private readonly Button _fromView = new Button { Text = "從目前視圖建立…", Width = 155, Height = 30 };
        internal ViewTemplateManagerForm(Func<TemplateInventory> read, Func<TemplateCreationRequest, string> create)
        {
            _read = read; _create = create;
            Text = "HB｜Revit 視圖樣板"; Font = new Font("Microsoft JhengHei UI", 9F);
            Size = new Size(940, 680); MinimumSize = new Size(760, 520); StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "讀取目前專案的視圖樣板（不含明細表樣板）。選取一列可查看使用視圖及圖紙。\r\n新增樣板後，請回到「編輯設定組合」選用，再預覽與套用。" }, 0, 0);
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var refresh = new Button { Text = "更新清單", Width = 100, Height = 30 };
            refresh.Click += (_, _) => Run(() => RefreshInventory(Selected?.UniqueId));
            _duplicate.Click += (_, _) => Run(() => Create(false));
            _fromView.Click += (_, _) => Run(() => Create(true));
            tools.Controls.AddRange(new Control[] { refresh, _duplicate, _fromView }); root.Controls.Add(tools, 0, 1);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "樣板名稱", DataPropertyName = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "視圖類型", DataPropertyName = "ViewType", Width = 130 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "使用視圖數", DataPropertyName = "ViewCount", Width = 95 });
            _grid.SelectionChanged += (_, _) => ShowUsage();
            _grid.CurrentCellChanged += (_, _) => ShowUsage();
            _grid.CellToolTipTextNeeded += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) e.ToolTipText = Convert.ToString(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value); };
            root.Controls.Add(_grid, 0, 2);
            root.Controls.Add(new Label { Text = "使用範圍／設定組合", Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) }, 0, 3);
            root.Controls.Add(_detail, 0, 4);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            footer.Controls.Add(_status, 0, 0);
            var close = new Button { Text = "關閉", DialogResult = DialogResult.Cancel, Width = 85, Height = 30 };
            footer.Controls.Add(close, 1, 0); root.Controls.Add(footer, 0, 5); Controls.Add(root); CancelButton = close;
            Shown += (_, _) => { DrawingStandardForm.FitScreen(this); Run(() => RefreshInventory(null)); };
        }
        private TemplateInventoryItem Selected => _grid.CurrentRow?.DataBoundItem as TemplateInventoryItem;
        private void Run(Action action)
        {
            try { UseWaitCursor = true; action(); }
            catch (Exception ex) { _duplicate.Enabled = _fromView.Enabled = false; MessageBox.Show(this, ex.Message, "視圖樣板", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { UseWaitCursor = false; }
        }
        private void RefreshInventory(string selectedId)
        {
            _inventory = _read(); _grid.DataSource = null; _grid.DataSource = _inventory.Templates;
            var row = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (r.DataBoundItem as TemplateInventoryItem)?.UniqueId == selectedId);
            if (row != null) _grid.CurrentCell = row.Cells[0];
            ShowUsage();
            _status.Text = "目前視圖：" + _inventory.ActiveViewName + (_inventory.CanCreateFromActive ? "" : "（不支援建立樣板）");
        }
        private void ShowUsage()
        {
            _fromView.Enabled = _inventory?.CanCreateFromActive == true;
            _duplicate.Enabled = Selected?.CanDuplicate == true;
            var selected = Selected;
            _detail.Text = selected == null ? "專案尚無視圖樣板，可從支援的目前視圖建立。" :
                "樣板：" + selected.Name + "\r\n使用於 " + selected.ViewCount + " 個視圖\r\n" +
                string.Join("\r\n", selected.Uses.Select(u => "• " + u.ViewName + "｜" + (string.IsNullOrEmpty(u.Sheets) ? "未上圖" : u.Sheets))) +
                "\r\n\r\n引用此樣板的 HB 設定組合：" + (selected.StandardNames.Count == 0 ? "（無）" : string.Join("、", selected.StandardNames)) +
                "\r\n\r\n複製會新增獨立樣板，不改原視圖指派；篩選器、線型等參照內容仍可能共用。修改共用內容前須另行檢查影響範圍。";
        }
        private void Create(bool fromCurrent)
        {
            var source = Selected;
            if (fromCurrent ? _inventory?.CanCreateFromActive != true : source?.CanDuplicate != true)
                throw new InvalidOperationException("請選擇可複製的樣板，或切換到可建立樣板的視圖。");
            var sourceId = fromCurrent ? _inventory.ActiveViewUniqueId : source.UniqueId;
            var sourceName = fromCurrent ? _inventory.ActiveViewName : source.Name;
            var name = DrawingNameDialog.Ask(this, fromCurrent ? "從目前視圖建立樣板" : "複製視圖樣板",
                "來源：" + sourceName + "\r\n只新增樣板；原視圖及 HB 設定組合維持目前指派。", "VT_" + sourceName + "_新樣板",
                _inventory.Templates.Select(t => t.Name));
            if (name == null) return;
            var uid = _create(new TemplateCreationRequest { FromCurrentView = fromCurrent, SourceUniqueId = sourceId, Name = name });
            RefreshInventory(uid); _status.Text = "已新增「" + name + "」，可回到編輯設定組合選用。";
        }
    }

    internal sealed class DrawingNameDialog : Form
    {
        private readonly TextBox _name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
        internal string ResultName { get; private set; }
        internal DrawingNameDialog(string title, string description, string initial, IEnumerable<string> existing)
        {
            var names = existing.ToList();
            Text = title; Font = new Font("Microsoft JhengHei UI", 9F); Size = new Size(620, 250); MinimumSize = new Size(500, 230);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill }, 0, 0);
            _name.Text = initial; root.Controls.Add(_name, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Height = 30 };
            var save = new Button { Text = "確定", Height = 30 };
            save.Click += (_, _) => {
                try { ResultName = DrawingStandardCatalog.ValidateName(_name.Text, names); DialogResult = DialogResult.OK; }
                catch (InvalidOperationException ex) { MessageBox.Show(this, ex.Message, "名稱需調整"); }
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save); root.Controls.Add(buttons, 0, 2); Controls.Add(root); AcceptButton = save; CancelButton = cancel;
            Shown += (_, _) => { DrawingStandardForm.FitScreen(this); _name.SelectAll(); _name.Focus(); };
        }
        internal static string Ask(IWin32Window owner, string title, string description, string initial, IEnumerable<string> existing)
        {
            using (var dialog = new DrawingNameDialog(title, description, initial, existing))
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.ResultName : null;
        }
    }
}
