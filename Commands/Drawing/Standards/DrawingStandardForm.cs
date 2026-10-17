using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.Drawing.Standards
{
    internal sealed class DrawingStandardForm : Form
    {
        private readonly Func<DrawingStandardData> _read;
        private readonly Func<List<StandardChoice>> _choices;
        private readonly Action<string, IEnumerable<string>, string> _save;
        private readonly Func<string, int, StandardPreview> _preview;
        private readonly Func<StandardPreview, int> _apply;
        private readonly DrawingStandardManagement _management;
        private readonly ContextMenuStrip _managementMenu = new ContextMenuStrip();
        private bool _showArchived;
        private readonly ComboBox _standard = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        private readonly ComboBox _scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            BackgroundColor = Color.White, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        private readonly TextBox _detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true };
        private readonly Label _status = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label _targetTemplate = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Margin = new Padding(3, 3, 0, 0) };
        private readonly Button _applyButton = Button("套用勾選項目", 132);
        private StandardPreview _plan;
        private string Key => (_standard.SelectedItem as DrawingStandard)?.Key;

        internal DrawingStandardForm(Func<DrawingStandardData> read, Func<List<StandardChoice>> choices,
            Action<string, IEnumerable<string>, string> save, Func<string, int, StandardPreview> preview, Func<StandardPreview, int> apply,
            DrawingStandardManagement management = null)
        {
            _read = read; _choices = choices; _save = save; _preview = preview; _apply = apply;
            _management = management;
            Text = "HB｜施工圖樣板管理"; Font = new Font("Microsoft JhengHei UI", 9F);
            Size = new Size(1120, 740); MinimumSize = new Size(880, 580); AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.Controls.Add(new Label { Text = "施工圖設定組合", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 16, FontStyle.Bold) }, 0, 0);
            var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            settings.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            settings.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settings.Controls.Add(new Label { Text = "施工圖用途", AutoSize = true, Margin = new Padding(0, 7, 0, 0) }, 0, 0);
            settings.Controls.Add(_standard, 1, 0);
            var configure = Button("編輯設定組合…", 142); configure.Click += (_, _) => Run(Edit);
            var manage = Button("組合管理 ▾", 105); manage.Enabled = management != null;
            manage.Click += (_, _) => _managementMenu.Show(manage, new Point(0, manage.Height));
            BuildManagementMenu();
            var templates = Button("視圖樣板…", 110); templates.Enabled = management?.Inventory != null && management?.CreateTemplate != null;
            templates.Click += (_, _) => Run(() => {
                using (var manager = new ViewTemplateManagerForm(_management.Inventory, _management.CreateTemplate)) manager.ShowDialog(this);
                InvalidatePreview();
            });
            var settingActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            settingActions.Controls.AddRange(new Control[] { configure, manage, templates }); settings.Controls.Add(settingActions, 2, 0);
            settings.Controls.Add(new Label { Text = "檢查範圍", AutoSize = true, Margin = new Padding(0, 7, 0, 0) }, 0, 1);
            _scope.Items.AddRange(new object[] { "目前視圖／目前圖紙", "已選取的視圖、圖紙或視埠", "全部已上圖的模型視圖" }); _scope.SelectedIndex = 0;
            settings.Controls.Add(_scope, 1, 1);
            settings.Controls.Add(_targetTemplate, 2, 1);
            root.Controls.Add(settings, 0, 1);
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
            var check = Button("檢查／更新預覽", 140); check.Click += (_, _) => Run(RefreshPreview);
            var select = Button("勾選可套用項目", 140); select.Click += (_, _) => {
                if (_plan == null) return;
                foreach (var row in _plan.Rows) row.Apply = string.IsNullOrEmpty(row.BlockReason) && string.IsNullOrWhiteSpace(row.ExceptionReason);
                _grid.Refresh(); UpdateStatus();
            };
            var clear = Button("取消勾選", 96); clear.Click += (_, _) => {
                if (_plan == null) return; foreach (var row in _plan.Rows) row.Apply = false; _grid.Refresh(); UpdateStatus();
            };
            var export = Button("匯出檢查 CSV", 130); export.Click += (_, _) => Run(Export);
            var exception = Button("設定例外…", 110); exception.Click += (_, _) => Run(EditException);
            commands.Controls.AddRange(new Control[] { check, select, clear, exception, export }); root.Controls.Add(commands, 0, 2);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var previewTab = new TabPage("視圖預覽／出圖檢查"); var detailTab = new TabPage("設定差異／檢查範圍");
            tabs.TabPages.AddRange(new[] { previewTab, detailTab });
            AddColumn("Apply", "套用", 48, false, true); AddColumn("ViewName", "視圖", 145); AddColumn("Sheets", "圖號", 75);
            AddColumn("CurrentTemplate", "目前樣板", 130);
            AddColumn("Action", "預計處理", 140); AddColumn("Issues", "檢查結果", 230);
            _grid.Columns["Issues"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns["Issues"].MinimumWidth = 140;
            _grid.CellToolTipTextNeeded += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) e.ToolTipText = Convert.ToString(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value); };
            _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) UpdateStatus(); };
            var help = new Label { Dock = DockStyle.Bottom, Height = 46, Text = "保留例外：選取視圖 → 設定例外 → 填寫原因 → 勾選並套用。清空原因再套用可解除例外。\r\n文字、尺寸、標籤的允許清單用於檢查；既有標註與圖紙配置須依結果另行調整。", Padding = new Padding(4) };
            previewTab.Controls.Add(_grid); previewTab.Controls.Add(help); detailTab.Controls.Add(_detail); root.Controls.Add(tabs, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 10, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            footer.Controls.Add(_status, 0, 0); footer.Controls.Add(_applyButton, 1, 0);
            var close = Button("關閉", 82); close.DialogResult = DialogResult.Cancel; footer.Controls.Add(close, 2, 0); CancelButton = close;
            _applyButton.Enabled = false; _applyButton.Click += (_, _) => Run(Apply);
            root.Controls.Add(footer, 0, 4); Controls.Add(root);
            _standard.SelectedIndexChanged += (_, _) => InvalidatePreview(); _scope.SelectedIndexChanged += (_, _) => InvalidatePreview();
            ReloadStandards(null);
            Shown += (_, _) => { FitScreen(this); Run(RefreshPreview); };
        }

        private void AddColumn(string property, string title, int width, bool readOnly = true, bool check = false)
        {
            DataGridViewColumn column = check ? (DataGridViewColumn)new DataGridViewCheckBoxColumn() : new DataGridViewTextBoxColumn();
            column.DataPropertyName = property; column.Name = property; column.HeaderText = title;
            column.Width = width; column.ReadOnly = readOnly; column.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(column);
        }
        private static Button Button(string label, int width) => new Button { Text = label, Width = width, Height = 30, Margin = new Padding(0, 0, 8, 0) };
        private void Run(Action action)
        {
            try { UseWaitCursor = true; action(); }
            catch (Exception ex) { _applyButton.Enabled = false; MessageBox.Show(this, ex.Message, "施工圖樣板管理", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { UseWaitCursor = false; }
        }
        private void ReloadStandards(string key)
        {
            var data = _read(); _standard.Items.Clear();
            foreach (var definition in data.Definitions.Where(d => _showArchived || !d.Archived)) _standard.Items.Add(data.Latest(definition.Key));
            _standard.SelectedIndex = _standard.Items.Count == 0 ? -1 : Math.Max(0, _standard.Items.Cast<DrawingStandard>().ToList().FindIndex(s => s.Key == key));
        }
        private void InvalidatePreview()
        {
            _plan = null; _grid.DataSource = null; _detail.Clear(); _applyButton.Enabled = false;
            _targetTemplate.Text = "跨用途請分批處理；只有勾選的視圖會變更。";
            _status.Text = "請檢查並預覽所選範圍。";
        }
        private void RefreshPreview()
        {
            InvalidatePreview();
            if (Key == null) { _status.Text = "目前沒有啟用的組合；可新增或在組合管理顯示停用組合。"; return; }
            _plan = _preview(Key, _scope.SelectedIndex);
            _grid.DataSource = new BindingList<StandardViewRow>(_plan.Rows);
            _detail.Text = _plan.Detail ?? string.Join("\r\n", _plan.DefinitionIssues);
            _targetTemplate.Text = "指定樣板：" + (_plan.TargetTemplate ?? "（尚未設定）") + "\r\n只處理勾選的視圖；跨用途請分批。";
            UpdateStatus();
        }
        private void UpdateStatus()
        {
            if (_plan == null) return;
            var count = _plan.Rows.Count(r => r.Apply);
            _applyButton.Enabled = count > 0 && _plan.DefinitionIssues.Count == 0 && _plan.Revision > 0;
            _status.Text = _plan.DefinitionIssues.Count > 0 ? "標準來源待處理；請查看設定差異。" :
                _plan.Rows.Count == 0 ? "此範圍沒有可處理視圖。請先選取視圖／圖紙。" : $"預覽 {_plan.Rows.Count} 個視圖｜勾選 {count} 個｜不含未列出的視圖";
        }
        private void Apply()
        {
            _grid.EndEdit();
            DrawingStandardPolicy.ValidateApply(_plan, _read().Latest(Key));
            int changed = _plan.Rows.Count(r => r.Apply && string.IsNullOrWhiteSpace(r.ExceptionReason));
            int exceptions = _plan.Rows.Count(r => r.Apply && !string.IsNullOrWhiteSpace(r.ExceptionReason));
            // The editable preview is the confirmation surface; this is a normal reversible Revit transaction.
            _apply(_plan); RefreshPreview();
            _status.Text = $"已套用／登錄 {changed} 個視圖、保留 {exceptions} 個例外，並重新檢查。可使用 Revit 復原。";
        }
        private void Edit()
        {
            var standard = _read().Latest(Key);
            if (standard.Archived) throw new InvalidOperationException("請先啟用設定組合，再編輯內容。");
            using (var editor = new DrawingStandardEditor(standard, _choices()))
                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    _save(standard.Key, editor.SelectedIds, editor.RevisionNote);
                    ReloadStandards(standard.Key); RefreshPreview();
                }
        }
        private void BuildManagementMenu()
        {
            _managementMenu.Items.Add("新增設定組合…", null, (_, _) => Run(() => CreateStandard(false)));
            var copy = _managementMenu.Items.Add("複製目前組合…", null, (_, _) => Run(() => CreateStandard(true)));
            var rename = _managementMenu.Items.Add("重新命名…", null, (_, _) => Run(RenameStandard));
            var archive = _managementMenu.Items.Add("停用目前組合", null, (_, _) => Run(ToggleArchive));
            _managementMenu.Items.Add(new ToolStripSeparator());
            var show = new ToolStripMenuItem("顯示停用組合") { CheckOnClick = true };
            show.CheckedChanged += (_, _) => { var key = Key; _showArchived = show.Checked; Run(() => { ReloadStandards(key); RefreshPreview(); }); };
            _managementMenu.Items.Add(show);
            _managementMenu.Opening += (_, _) => {
                copy.Enabled = rename.Enabled = archive.Enabled = Key != null;
                archive.Text = (_standard.SelectedItem as DrawingStandard)?.Archived == true ? "重新啟用目前組合" : "停用目前組合";
            };
            FormClosed += (_, _) => _managementMenu.Dispose();
        }
        private void CreateStandard(bool copy)
        {
            var data = _read(); var source = copy ? data.Latest(Key) : null;
            var name = DrawingNameDialog.Ask(this, copy ? "複製設定組合" : "新增設定組合",
                "名稱建議包含系統、圖種與比例，例如「空調－風管平面－1:100」。\r\n新增後先確認內容並儲存；複製不沿用視圖指派、例外及核對版本。",
                copy ? source.Name + "－複本" : "", data.Definitions.Select(d => d.Name));
            if (name == null) return;
            var key = _management.Create(name, source?.Key); ReloadStandards(key); RefreshPreview(); Edit();
        }
        private void RenameStandard()
        {
            var data = _read(); var standard = data.Latest(Key);
            var name = DrawingNameDialog.Ask(this, "重新命名設定組合", "只變更組合名稱；既有版本、例外及視圖指派會保留。", standard.Name,
                data.Definitions.Where(d => d.Key != standard.Key).Select(d => d.Name));
            if (name == null) return;
            _management.Rename(standard.Key, name); ReloadStandards(standard.Key); RefreshPreview();
        }
        private void ToggleArchive()
        {
            var standard = _read().Latest(Key);
            _management.Archive(standard.Key, !standard.Archived); ReloadStandards(standard.Key); RefreshPreview();
            _status.Text = standard.Archived ? "已重新啟用設定組合。" : "已停用；既有視圖指派保留，可由組合管理顯示停用組合後重新啟用。";
        }
        private void EditException()
        {
            if (!(_grid.CurrentRow?.DataBoundItem is StandardViewRow row)) throw new InvalidOperationException("請先選取一個視圖列。");
            using (var dialog = new Form { Text = "專案例外｜" + row.ViewName, Size = new Size(640, 400), MinimumSize = new Size(560, 340),
                Font = Font, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi })
            {
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                root.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                    Text = row.ViewName + "\r\n目前樣板：" + row.CurrentTemplate + "\r\n指定樣板：" + row.TargetTemplate + "\r\n" + row.Issues }, 0, 0);
                root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "例外原因（留白表示解除例外）\r\n完成後仍須勾選該視圖並套用，才會寫入模型。" }, 0, 1);
                var reason = new TextBox { Dock = DockStyle.Fill, Multiline = true, Text = row.ExceptionReason, ScrollBars = ScrollBars.Vertical };
                root.Controls.Add(reason, 0, 2);
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
                var cancel = Button("取消", 85); cancel.DialogResult = DialogResult.Cancel;
                var done = Button("完成", 85); done.DialogResult = DialogResult.OK;
                buttons.Controls.Add(cancel); buttons.Controls.Add(done); root.Controls.Add(buttons, 0, 3); dialog.Controls.Add(root); dialog.CancelButton = cancel;
                dialog.Shown += (_, _) => FitScreen(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                row.ExceptionReason = reason.Text.Trim();
                row.Action = !string.IsNullOrWhiteSpace(row.ExceptionReason) ? "保留例外（待登錄）" :
                    !string.IsNullOrEmpty(row.BlockReason) ? "無法套用" : "指定樣板／更新版本";
                _grid.Refresh(); UpdateStatus();
            }
        }
        private void Export()
        {
            if (_plan == null) throw new InvalidOperationException("請先執行檢查。");
            _grid.EndEdit();
            using (var dialog = new SaveFileDialog { Filter = "CSV 檢查報告|*.csv", FileName = "施工圖檢查-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(dialog.FileName, ReportCsv(_plan), new UTF8Encoding(true)); _status.Text = "已匯出檢查報告（含檢查範圍與未套用的例外草稿）。";
                }
        }
        internal static string ReportCsv(StandardPreview plan)
        {
            string Q(string text) { text = text ?? ""; if (text.TrimStart().Length > 0 && "=+-@".Contains(text.TrimStart()[0])) text = "'" + text; return "\"" + text.Replace("\"", "\"\"") + "\""; }
            var lines = new List<string> { "用途,版本,視圖,圖號,目前樣板,指定樣板,預計處理,檢查結果,例外草稿（未必已登錄）" };
            foreach (var row in plan.Rows)
                lines.Add(string.Join(",", new[] { plan.StandardKey, plan.Revision.ToString(), row.ViewName, row.Sheets, row.CurrentTemplate,
                    row.TargetTemplate, row.Action, row.Issues, row.ExceptionReason }.Select(Q)));
            lines.Add(Q("來源差異") + "," + Q(string.Join("\r\n", plan.DefinitionIssues)));
            lines.Add(Q("檢查說明") + "," + Q(plan.Detail));
            return string.Join("\r\n", lines);
        }
        internal static void FitScreen(Form form)
        {
            var area = Screen.FromHandle(form.Handle).WorkingArea;
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width - 24), Math.Min(form.MinimumSize.Height, area.Height - 24));
            form.Size = new Size(Math.Min(form.Width, area.Width - 24), Math.Min(form.Height, area.Height - 24));
        }
    }

    internal sealed class DrawingStandardEditor : Form
    {
        private readonly List<CheckedListBox> _lists = new List<CheckedListBox>();
        private readonly ComboBox _template = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly TextBox _note = new TextBox { Dock = DockStyle.Fill };
        internal List<string> SelectedIds => _lists.SelectMany(list => list.CheckedItems.Cast<StandardChoice>()).Select(c => c.UniqueId)
            .Concat(_template.SelectedItem is StandardChoice choice ? new[] { choice.UniqueId } : Array.Empty<string>()).ToList();
        internal string RevisionNote => _note.Text;
        internal DrawingStandardEditor(DrawingStandard standard, List<StandardChoice> choices)
        {
            Text = standard.Name + "｜設定組合 v" + (standard.Revision + 1); Size = new Size(820, 620); MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Microsoft JhengHei UI", 9F); ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.Controls.Add(new Label { Text = "以本專案既有內容建立標準。文字、尺寸與標籤可勾選多個允許類型。\r\n儲存新版會重新擷取實際設定基準；舊版紀錄仍保留在模型。", Dock = DockStyle.Fill }, 0, 0);
            foreach (var item in choices.Where(c => c.Kind == "視圖樣板")) _template.Items.Add(item);
            _template.SelectedItem = _template.Items.Cast<StandardChoice>().FirstOrDefault(c => c.UniqueId == standard.Template?.UniqueId);
            root.Controls.Add(new Label { Text = "指定視圖樣板（必填）", Dock = DockStyle.Fill }, 0, 1); root.Controls.Add(_template, 0, 2);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var existing = new HashSet<string>(standard.References.Select(r => r.UniqueId));
            foreach (var kind in new[] { "文字樣式", "尺寸樣式", "標籤族型", "圖例", "明細表" })
            {
                var page = new TabPage(kind + (kind == "圖例" || kind == "明細表" ? "（選用）" : "（必填）"));
                var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true, IntegralHeight = false };
                foreach (var choice in choices.Where(c => c.Kind == kind)) list.Items.Add(choice, existing.Contains(choice.UniqueId));
                page.Controls.Add(list); tabs.TabPages.Add(page); _lists.Add(list);
            }
            root.Controls.Add(tabs, 0, 3);
            var notePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            notePanel.Controls.Add(new Label { Text = "本版用途／變更說明（必填）", AutoSize = true }); notePanel.Controls.Add(_note); root.Controls.Add(notePanel, 0, 4);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 85, Height = 30 };
            var save = new Button { Text = "儲存新版", Width = 110, Height = 30 };
            save.Click += (_, _) => {
                if (_template.SelectedItem == null || string.IsNullOrWhiteSpace(_note.Text) || _lists.Take(3).Any(l => l.CheckedItems.Count == 0))
                { MessageBox.Show(this, "請指定視圖樣板、至少一個文字／尺寸／標籤類型，並填寫變更說明。", "設定尚未完成"); return; }
                DialogResult = DialogResult.OK;
            };
            footer.Controls.Add(cancel); footer.Controls.Add(save); root.Controls.Add(footer, 0, 5);
            Controls.Add(root); CancelButton = cancel;
            Shown += (_, _) => DrawingStandardForm.FitScreen(this);
        }
    }
}
