using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.Data.BimStandard
{
    internal sealed class ModelAuditForm : Form
    {
        private readonly Func<ModelAuditGroup, ModelAuditOptions, List<ModelAuditIssue>> _check;
        private readonly Action<ModelAuditIssue> _locate;
        private readonly Action<ModelAuditIssue> _organize;
        private readonly Button _organizeButton = Button("前往名稱整理…", 130);
        private readonly Action<ModelAuditGroup, ModelAuditOptions, IReadOnlyList<ModelAuditIssue>, IWin32Window> _export;
        private readonly ModelAuditReview _review = new ModelAuditReview();
        private List<ModelAuditIssue> _issues = new List<ModelAuditIssue>();
        private ModelAuditOptions _options = new ModelAuditOptions();
        private readonly ComboBox _group = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _showIgnored = new CheckBox { Text = "顯示已略過", AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
        private readonly Label _summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label _hint = new Label { Dock = DockStyle.Fill };
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        private readonly TextBox _detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly Button _locateButton = Button("定位項目", 100);
        private readonly Button _ignoreButton = Button("本次略過…", 110);
        private readonly Button _exportButton = Button("匯出報告", 105);
        private readonly Button _rulesButton = Button("命名設定…", 110);
        private bool _hasRun;
        private ModelAuditGroup Group => (ModelAuditGroup)_group.SelectedIndex;
        private ModelAuditIssue Selected => _grid.CurrentRow?.DataBoundItem as ModelAuditIssue;
        internal ModelAuditContext Context => new ModelAuditContext { Group = Group, Options = _options.Copy() };

        internal ModelAuditForm(string documentTitle, Func<ModelAuditGroup, ModelAuditOptions, List<ModelAuditIssue>> check,
            Action<ModelAuditIssue> locate, Action<ModelAuditGroup, ModelAuditOptions, IReadOnlyList<ModelAuditIssue>, IWin32Window> export, Action<ModelAuditIssue> organize = null, ModelAuditContext context = null)
        {
            _check = check; _locate = locate; _export = export; _organize = organize;
            _options = context?.Options.Copy() ?? new ModelAuditOptions();
            Text = "HB｜模型管理檢查｜" + documentTitle; Font = new Font("Microsoft JhengHei UI", 9F);
            Size = new Size(1040, 720); MinimumSize = new Size(800, 550); StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16) };
            foreach (var height in new[] { 36, 48, 42, 30 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.Controls.Add(new Label { Text = "模型管理檢查", Font = new Font(Font.FontFamily, 16, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
            root.Controls.Add(_hint, 0, 1);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _group.Items.AddRange(new object[] { "專案資料", "命名規則（選用）", "族群與類型" }); _group.SelectedIndex = (int)(context?.Group ?? ModelAuditGroup.Project);
            var run = Button("開始檢查", 105); run.Click += (_, _) => Run(Check);
            _rulesButton.Click += (_, _) => Run(EditRules);
            bar.Controls.AddRange(new Control[] { _group, run, _rulesButton, _showIgnored }); root.Controls.Add(bar, 0, 2);
            root.Controls.Add(_summary, 0, 3);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "狀態", DataPropertyName = "Status", Width = 95 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "項目", DataPropertyName = "ElementName", Width = 225 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "待確認內容", DataPropertyName = "Message", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _grid.CurrentCellChanged += (_, _) => ShowDetail();
            _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _locateButton.Enabled) Run(LocateSelected); };
            _grid.CellToolTipTextNeeded += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) e.ToolTipText = Convert.ToString(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value); };
            root.Controls.Add(_grid, 0, 4); root.Controls.Add(_detail, 0, 5);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
            _locateButton.Click += (_, _) => Run(LocateSelected);
            _ignoreButton.Click += (_, _) => Run(ReviewSelected);
            _exportButton.Click += (_, _) => Run(() => _export(Group, _options, _issues, this));
            _organizeButton.Visible = organize != null;
            _organizeButton.Click += (_, _) => Run(() => { if (Selected == null) return; _organize(Selected); DialogResult = DialogResult.OK; Close(); });
            var close = Button("關閉", 90); close.DialogResult = DialogResult.Cancel;
            footer.Controls.AddRange(new Control[] { _locateButton, _organizeButton, _ignoreButton, _exportButton, close }); root.Controls.Add(footer, 0, 6);
            Controls.Add(root); CancelButton = close;
            _showIgnored.CheckedChanged += (_, _) => BindResults();
            _group.SelectedIndexChanged += (_, _) => InvalidateResults(documentTitle);
            InvalidateResults(documentTitle);
            Shown += (_, _) => {
                var area = Screen.FromHandle(Handle).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 24), Math.Min(MinimumSize.Height, area.Height - 24));
                Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
            };
        }
        private static Button Button(string text, int width) => new Button { Text = text, Width = width, Height = 30, Margin = new Padding(0, 0, 8, 0) };
        private void Run(Action action)
        {
            try { UseWaitCursor = true; action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "模型管理檢查", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { UseWaitCursor = false; }
        }
        private void InvalidateResults(string title = null)
        {
            _hasRun = false; _issues.Clear(); _grid.DataSource = null; _detail.Clear();
            _summary.Text = "選擇檢查項目後，按「開始檢查」。";
            _rulesButton.Visible = Group == ModelAuditGroup.Naming;
            _hint.Text = Group == ModelAuditGroup.Project ? "檢查專案基本資訊及明細表是否可讀取。欄位需求仍依專案決定。" :
                Group == ModelAuditGroup.Naming ? "依選用規則提醒命名一致性；前綴留白就不檢查，不強制公司命名。" :
                "整理族群類型數量與同名類型；提醒供管理人員確認，不代表族群有錯。";
            _hint.Text += "\r\n這是模型資料管理檢查，不評分、不判定模型或施工圖已驗收。";
            ShowDetail();
        }
        private void Check()
        {
            InvalidateResults();
            _issues = _check(Group, _options) ?? throw new InvalidOperationException("無法取得完整檢查結果。");
            _review.Restore(_issues); _hasRun = true; BindResults();
        }
        private void BindResults()
        {
            var selectedKey = Selected?.ReviewKey;
            _grid.DataSource = null;
            if (_hasRun)
            {
                _grid.DataSource = _issues.Where(i => _showIgnored.Checked || !i.Ignored).OrderBy(i => i.Ignored).ThenByDescending(i => i.NeedsAction).ToList();
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var issue = (ModelAuditIssue)row.DataBoundItem;
                    row.DefaultCellStyle.BackColor = issue.Ignored ? Color.Gainsboro : issue.NeedsAction ? Color.MistyRose : Color.White;
                    if (issue.ReviewKey == selectedKey) _grid.CurrentCell = row.Cells[0];
                }
                _summary.Text = ModelAuditReview.Summary(_issues) + (_issues.Count > 0 && _grid.Rows.Count == 0 ? "（勾選顯示已略過可還原）" : "");
            }
            ShowDetail();
        }
        private void ShowDetail()
        {
            var issue = Selected;
            _exportButton.Enabled = _hasRun; _ignoreButton.Enabled = issue != null;
            _locateButton.Enabled = issue != null && !string.IsNullOrEmpty(issue.UniqueId);
            _organizeButton.Enabled = _organize != null && issue?.CanOrganize == true;
            _ignoreButton.Text = issue?.Ignored == true ? "恢復檢查" : "本次略過…";
            _detail.Text = issue == null ? "選取項目可查看處理建議。略過只保留於本次視窗，可連同原因匯出報告。" :
                issue.ElementName + "｜" + issue.Category + "｜" + issue.Rule + "\r\n" + issue.Message + "\r\n建議：" + issue.Recommendation +
                (issue.Ignored ? "\r\n本次略過原因：" + issue.IgnoredReason : "") + "\r\n定位或前往名稱整理會關閉本視窗；若需保留結果與略過原因，請先匯出報告。";
        }
        private void LocateSelected() { if (Selected == null) return; _locate(Selected); DialogResult = DialogResult.OK; Close(); }
        private void ReviewSelected()
        {
            var issue = Selected; if (issue == null) return;
            if (issue.Ignored) { _review.Reopen(issue); BindResults(); return; }
            using (var dialog = new ModelAuditReasonForm())
                if (dialog.ShowDialog(this) == DialogResult.OK) { _review.Ignore(issue, dialog.Reason); BindResults(); }
        }
        private void EditRules()
        {
            using (var dialog = new ModelAuditRulesForm(_options))
                if (dialog.ShowDialog(this) == DialogResult.OK) { _options = dialog.Options; InvalidateResults(); }
        }
    }

    internal sealed class ModelAuditReasonForm : Form
    {
        private readonly TextBox _reason = new TextBox { Dock = DockStyle.Fill, Multiline = true, MaxLength = 500 };
        internal string Reason => _reason.Text.Trim();
        internal ModelAuditReasonForm()
        {
            Text = "本次略過原因"; Size = new Size(540, 250); MinimumSize = new Size(440, 230); Font = new Font("Microsoft JhengHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(new Label { Text = "請說明為何不處理此項目。原因會包含於匯出報告；關閉視窗後不保留。", Dock = DockStyle.Fill }, 0, 0);
            root.Controls.Add(_reason, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            var save = new Button { Text = "略過" };
            save.Click += (_, _) => { if (Reason.Length == 0) { MessageBox.Show(this, "請填寫原因。"); return; } DialogResult = DialogResult.OK; };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save); root.Controls.Add(buttons, 0, 2); Controls.Add(root); CancelButton = cancel;
        }
    }

    internal sealed class ModelAuditRulesForm : Form
    {
        private readonly TextBox[] _prefixes = Enumerable.Range(0, 4).Select(_ => new TextBox { Dock = DockStyle.Fill, MaxLength = 40 }).ToArray();
        private readonly CheckBox _readability = new CheckBox { Text = "提醒過於簡略或預設名稱（僅供確認）", AutoSize = true };
        internal ModelAuditOptions Options => new ModelAuditOptions { FamilyPrefix = _prefixes[0].Text.Trim(), TemplatePrefix = _prefixes[1].Text.Trim(),
            FilterPrefix = _prefixes[2].Text.Trim(), MaterialPrefix = _prefixes[3].Text.Trim(), ReadabilityHints = _readability.Checked };
        internal ModelAuditRulesForm(ModelAuditOptions options)
        {
            Text = "選用命名規則"; Size = new Size(560, 360); MinimumSize = new Size(480, 340); Font = new Font("Microsoft JhengHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 7 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var hint = new Label { Text = "只填寫專案有約定的前綴；留白表示不檢查。\r\n設定可帶入名稱整理後重新檢查，不會存入模型。", Dock = DockStyle.Fill };
            root.Controls.Add(hint, 0, 0); root.SetColumnSpan(hint, 2);
            var labels = new[] { "族群前綴", "視圖樣板前綴", "篩選器前綴", "材質前綴" };
            var values = new[] { options.FamilyPrefix, options.TemplatePrefix, options.FilterPrefix, options.MaterialPrefix };
            for (int i = 0; i < 4; i++) { root.Controls.Add(new Label { Text = labels[i], Dock = DockStyle.Fill }, 0, i + 1); _prefixes[i].Text = values[i]; root.Controls.Add(_prefixes[i], 1, i + 1); }
            _readability.Checked = options.ReadabilityHints; root.Controls.Add(_readability, 0, 5); root.SetColumnSpan(_readability, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; var save = new Button { Text = "完成", DialogResult = DialogResult.OK };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save); root.Controls.Add(buttons, 0, 6); root.SetColumnSpan(buttons, 2); Controls.Add(root); CancelButton = cancel;
        }
    }
}
