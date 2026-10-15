using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class TypeJoinRuleEditor : Form
    {
        private readonly IReadOnlyList<TypeJoinChoice> _choices;
        private readonly List<TypeJoinRule> _rules;
        private readonly ComboBox _category = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly SideEditor _winner = new();
        private readonly SideEditor _loser = new();
        private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White };
        public IReadOnlyList<TypeJoinRule> Rules => _rules;

        public TypeJoinRuleEditor(IReadOnlyList<TypeJoinChoice> choices, IEnumerable<TypeJoinRule> rules)
        {
            _choices = choices;
            _rules = rules.Select(x => x.Clone()).ToList();
            Text = "同類別類型規則";
            Font = new Font("Microsoft JhengHei UI", 9f);
            Size = new Size(900, 700);
            MinimumSize = new Size(700, 620);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var inputs = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3 };
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var categoryRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            categoryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            categoryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            categoryRow.Controls.Add(new Label { Text = "類別", AutoSize = true, Margin = new Padding(3, 7, 12, 7) }, 0, 0);
            categoryRow.Controls.Add(_category, 1, 0);
            inputs.Controls.Add(categoryRow, 0, 0);
            inputs.SetColumnSpan(categoryRow, 2);
            inputs.Controls.Add(_winner.Build("優先切割方", () => UpdatePreview(_winner)), 0, 1);
            inputs.Controls.Add(_loser.Build("被切割方", () => UpdatePreview(_loser)), 1, 1);
            var add = new Button { Text = "新增規則", AutoSize = true };
            add.Click += (_, _) => AddRule();
            inputs.Controls.Add(add, 0, 2);
            _category.DisplayMember = "Value";
            foreach (var key in choices.Select(x => x.CategoryKey).Distinct(StringComparer.Ordinal))
                _category.Items.Add(new KeyValuePair<string, string>(key, AutoJoinForm.LocalizeCategoryText(key, key)));
            _category.SelectedIndexChanged += (_, _) => PopulateTypes();
            if (_category.Items.Count > 0) _category.SelectedIndex = 0;
            add.Enabled = choices.Count > 0;
            _grid.Columns.Add("category", "類別");
            _grid.Columns.Add("winner", "優先切割類型");
            _grid.Columns.Add("loser", "被切割類型");
            _grid.Columns.Add("status", "專案狀態");
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            var remove = new Button { Text = "移除選取", AutoSize = true };
            remove.Click += (_, _) =>
            {
                foreach (var index in _grid.SelectedRows.Cast<DataGridViewRow>().Select(x => x.Index).OrderByDescending(x => x))
                    _rules.RemoveAt(index);
                RenderRules();
            };
            var clear = new Button { Text = "清除全部", AutoSize = true };
            clear.Click += (_, _) => { _rules.Clear(); RenderRules(); };
            var save = new Button { Text = "確定", AutoSize = true };
            save.Click += (_, _) => { if (ValidateRules(_rules)) { DialogResult = DialogResult.OK; Close(); } };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            footer.Controls.AddRange(new Control[] { remove, clear, save, cancel });
            CancelButton = cancel;
            root.Controls.Add(inputs, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
            RenderRules();
        }

        private void PopulateTypes()
        {
            foreach (var side in new[] { _winner, _loser })
            {
                side.Exact.Items.Clear();
                if (_category.SelectedItem is KeyValuePair<string, string> category)
                    foreach (var choice in _choices.Where(x => x.CategoryKey == category.Key))
                        side.Exact.Items.Add(choice);
                // Explicit selection avoids silently substituting a different type.
                side.Exact.SelectedIndex = -1;
                UpdatePreview(side);
            }
        }

        private void UpdatePreview(SideEditor side)
        {
            side.Preview.BeginUpdate();
            try
            {
                side.Preview.Items.Clear();
                if (_category.SelectedItem is KeyValuePair<string, string> category &&
                    side.TryRead(out var family, out var pattern))
                    foreach (var choice in _choices.Where(x => x.CategoryKey == category.Key &&
                        TypeJoinPolicy.Matches(side.Mode, family, pattern, x.FamilyName, x.TypeName)))
                        side.Preview.Items.Add(choice);
                side.Count.Text = $"符合類型：{side.Preview.Items.Count} 個";
            }
            finally { side.Preview.EndUpdate(); }
        }

        private void AddRule()
        {
            if (!(_category.SelectedItem is KeyValuePair<string, string> category) ||
                !_winner.TryRead(out var winnerFamily, out var winnerType) ||
                !_loser.TryRead(out var loserFamily, out var loserType))
            {
                MessageBox.Show(this, "請選取類別，並為兩側選取指定類型或輸入名稱包含的關鍵字。", Text);
                return;
            }
            var candidate = _rules.Select(x => x.Clone()).ToList();
            if (candidate.Any(x => x.CategoryKey == category.Key &&
                x.WinnerMatchMode == _winner.Mode && x.LoserMatchMode == _loser.Mode &&
                x.WinnerFamilyName == winnerFamily && x.WinnerTypeName == winnerType &&
                x.LoserFamilyName == loserFamily && x.LoserTypeName == loserType))
            {
                MessageBox.Show(this, "此類型規則已存在。", Text);
                return;
            }
            candidate.Add(new TypeJoinRule { CategoryKey = category.Key,
                WinnerMatchMode = _winner.Mode, LoserMatchMode = _loser.Mode,
                WinnerFamilyName = winnerFamily, WinnerTypeName = winnerType,
                LoserFamilyName = loserFamily, LoserTypeName = loserType });
            if (!ValidateRules(candidate)) return;
            _rules.Clear();
            _rules.AddRange(candidate);
            RenderRules();
        }

        private bool ValidateRules(IReadOnlyList<TypeJoinRule> rules)
        {
            try { TypeJoinPolicy.Validate(rules); return true; }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void RenderRules()
        {
            _grid.Rows.Clear();
            foreach (var rule in _rules)
            {
                int MatchCount(TypeJoinMatchMode mode, string family, string type) => _choices.Count(x =>
                    x.CategoryKey == rule.CategoryKey && TypeJoinPolicy.Matches(mode, family, type, x.FamilyName, x.TypeName));
                var winnerCount = MatchCount(rule.WinnerMatchMode, rule.WinnerFamilyName, rule.WinnerTypeName);
                var loserCount = MatchCount(rule.LoserMatchMode, rule.LoserFamilyName, rule.LoserTypeName);
                var row = _grid.Rows[_grid.Rows.Add(AutoJoinForm.LocalizeCategoryText(rule.CategoryKey, rule.CategoryKey),
                    Describe(rule.WinnerMatchMode, rule.WinnerFamilyName, rule.WinnerTypeName),
                    Describe(rule.LoserMatchMode, rule.LoserFamilyName, rule.LoserTypeName),
                    "切割方：" + Status(rule.WinnerMatchMode, winnerCount) + "；被切割方：" + Status(rule.LoserMatchMode, loserCount))];
                if ((rule.WinnerMatchMode == TypeJoinMatchMode.Exact && winnerCount == 0) ||
                    (rule.LoserMatchMode == TypeJoinMatchMode.Exact && loserCount == 0))
                    row.DefaultCellStyle.ForeColor = Color.Firebrick;
            }
        }

        private static string Describe(TypeJoinMatchMode mode, string family, string type)
            => mode == TypeJoinMatchMode.Contains
                ? "名稱包含「" + type + "」／" + (string.IsNullOrEmpty(family) ? "全部族群" : "族群＝「" + family + "」")
                : family + " / " + type;

        private static string Status(TypeJoinMatchMode mode, int count)
            => count > 0 ? $"符合 {count} 個" : mode == TypeJoinMatchMode.Contains
                ? "目前無符合類型（保留條件）" : "類型不存在（保留）";

        private sealed class SideEditor
        {
            private readonly ComboBox _mode = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            internal readonly ComboBox Exact = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            private readonly TextBox _pattern = new() { Dock = DockStyle.Fill };
            private readonly TextBox _family = new() { Dock = DockStyle.Fill };
            internal readonly ListBox Preview = new() { Dock = DockStyle.Fill, Height = 76,
                IntegralHeight = false, HorizontalScrollbar = true };
            internal readonly Label Count = new() { AutoSize = true, Text = "符合類型：0 個" };
            internal TypeJoinMatchMode Mode => _mode.SelectedIndex == 1 ? TypeJoinMatchMode.Contains : TypeJoinMatchMode.Exact;

            internal Control Build(string title, Action changed)
            {
                var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2,
                    RowCount = 7, Margin = new Padding(3, 3, 8, 3) };
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var heading = new Label { Text = title, AutoSize = true, Margin = new Padding(3, 6, 3, 6) };
                panel.Controls.Add(heading, 0, 0);
                panel.SetColumnSpan(heading, 2);
                void AddInput(string label, Control control, int row)
                {
                    panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
                    panel.Controls.Add(control, 1, row);
                }
                _mode.Items.AddRange(new object[] { "指定類型", "名稱包含" });
                _mode.SelectedIndex = 0;
                AddInput("比對模式", _mode, 1);
                AddInput("指定類型", Exact, 2);
                AddInput("名稱關鍵字", _pattern, 3);
                AddInput("族群（可留白）", _family, 4);
                panel.Controls.Add(Count, 0, 5);
                panel.SetColumnSpan(Count, 2);
                panel.Controls.Add(Preview, 0, 6);
                panel.SetColumnSpan(Preview, 2);
                void RefreshMode()
                {
                    Exact.Enabled = Mode == TypeJoinMatchMode.Exact;
                    _pattern.Enabled = _family.Enabled = Mode == TypeJoinMatchMode.Contains;
                    changed();
                }
                _mode.SelectedIndexChanged += (_, _) => RefreshMode();
                Exact.SelectedIndexChanged += (_, _) => changed();
                _pattern.TextChanged += (_, _) => changed();
                _family.TextChanged += (_, _) => changed();
                RefreshMode();
                return panel;
            }

            internal bool TryRead(out string family, out string pattern)
            {
                family = pattern = string.Empty;
                if (Mode == TypeJoinMatchMode.Contains)
                {
                    // Preserve literal text so preview and saved predicates are identical.
                    family = string.IsNullOrWhiteSpace(_family.Text) ? string.Empty : _family.Text;
                    pattern = _pattern.Text;
                    return !string.IsNullOrWhiteSpace(pattern);
                }
                if (!(Exact.SelectedItem is TypeJoinChoice choice)) return false;
                family = choice.FamilyName;
                pattern = choice.TypeName;
                return true;
            }
        }
    }
}
