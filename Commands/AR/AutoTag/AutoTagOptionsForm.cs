using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using RevitDB = Autodesk.Revit.DB;
using Forms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class AutoTagOptionsForm : Forms.Form
    {
        private readonly RevitDB.Document _doc;
        private readonly AutoTagMode _mode;
        private readonly Action<AutoTagOptions, IReadOnlyList<AutoTagRuleSelection>> _applyAction;
        private readonly Action _refreshAction;
        private readonly string _windowTitle;
        private readonly Forms.ComboBox _templateCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _scopeCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _placementCombo = new Forms.ComboBox();
        private readonly Forms.TextBox _offsetText = new Forms.TextBox();
        private readonly Forms.CheckBox _leaderCheck = new Forms.CheckBox();
        private readonly Forms.CheckBox _skipExistingCheck = new Forms.CheckBox();
        private readonly Forms.CheckBox _avoidOverlapCheck = new Forms.CheckBox();
        private readonly Forms.Button _applyButton = new Forms.Button();
        private readonly Forms.Button _refreshButton = new Forms.Button();
        private readonly Forms.Button _saveDefaultButton = new Forms.Button();
        private readonly Forms.Button _resetButton = new Forms.Button();
        private readonly Forms.Label _statusLabel = new Forms.Label();
        private readonly Forms.Label _summaryLabel = new Forms.Label();
        private readonly List<RowBinding> _rows = new List<RowBinding>();

        public AutoTagOptionsForm(
            RevitDB.Document doc,
            AutoTagMode mode,
            Action<AutoTagOptions, IReadOnlyList<AutoTagRuleSelection>> applyAction,
            Action refreshAction)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mode = mode;
            _applyAction = applyAction ?? throw new ArgumentNullException(nameof(applyAction));
            _refreshAction = refreshAction ?? throw new ArgumentNullException(nameof(refreshAction));
            _windowTitle = mode == AutoTagMode.Vertical ? "自動標籤 - 垂直元素" : "自動標籤 - 水平元素";

            Text = _windowTitle;
            Width = 780;
            Height = 650;
            MinimizeBox = false;
            MaximizeBox = true;
            MinimumSize = new Size(720, 560);
            FormBorderStyle = Forms.FormBorderStyle.Sizable;
            StartPosition = Forms.FormStartPosition.CenterScreen;
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            BuildLayout(doc);
            if (!ApplySavedSettings(AutoTagSettingsStore.Load(doc, mode)))
                ApplyTemplate();

            UpdateSummary();
        }

        public void UpdateTagTypes(RevitDB.Document doc)
        {
            foreach (RowBinding row in _rows)
            {
                RevitDB.ElementId previous = row.SelectedTagTypeId;
                string preferredLabel = row.PreferredTagTypeLabel;
                row.ComboBox.BeginUpdate();
                row.ComboBox.Items.Clear();

                IReadOnlyList<AutoTagTypeOption> tagTypes = AutoTagService.GetTagTypeOptions(doc, row.Rule.TagCategory);
                foreach (AutoTagTypeOption tagType in tagTypes)
                    row.ComboBox.Items.Add(tagType);

                if (row.ComboBox.Items.Count == 0)
                {
                    row.ComboBox.Items.Add(new AutoTagTypeOption(RevitDB.ElementId.InvalidElementId, "尚未載入此分類標籤族"));
                    row.ComboBox.Enabled = false;
                    row.ComboBox.SelectedIndex = 0;
                }
                else
                {
                    row.ComboBox.Enabled = true;
                    int index = FindTagTypeIndex(row.ComboBox, previous);
                    if (index < 0 && !string.IsNullOrWhiteSpace(preferredLabel))
                        index = FindTagTypeIndex(row.ComboBox, preferredLabel);

                    row.ComboBox.SelectedIndex = index >= 0 ? index : 0;
                }

                row.ComboBox.EndUpdate();
            }

            UpdateSummary();
        }

        public void SetRequestCompleted(string status = null, bool isError = false)
        {
            if (IsDisposed)
                return;

            _applyButton.Enabled = true;
            _refreshButton.Enabled = true;
            _saveDefaultButton.Enabled = true;
            _resetButton.Enabled = true;
            _statusLabel.ForeColor = isError ? Color.FromArgb(176, 45, 45) : Color.FromArgb(75, 88, 105);
            _statusLabel.Text = status ?? "完成。可繼續調整並再次執行。";
        }

        private void BuildLayout(RevitDB.Document doc)
        {
            var root = new Forms.TableLayoutPanel
            {
                Dock = Forms.DockStyle.Fill,
                Padding = new Forms.Padding(14),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 76));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 52));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 52));
            Controls.Add(root);

            root.Controls.Add(BuildTopPanel(), 0, 0);
            root.Controls.Add(BuildFlagPanel(), 0, 1);
            root.Controls.Add(BuildRuleGrid(doc), 0, 2);
            root.Controls.Add(BuildButtons(), 0, 3);
        }

        private Forms.Control BuildTopPanel()
        {
            var top = new Forms.TableLayoutPanel
            {
                Dock = Forms.DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2
            };
            top.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 94));
            top.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 50));
            top.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 104));
            top.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 50));

            AddLabel(top, "樣板分類", 0, 0);
            ConfigureCombo(_templateCombo, new[] { "軀體圖", "機電圖" });
            _templateCombo.SelectedIndex = 1;
            _templateCombo.SelectedIndexChanged += (sender, args) =>
            {
                ApplyTemplate();
                UpdateSummary();
            };
            top.Controls.Add(_templateCombo, 1, 0);

            AddLabel(top, "標註範圍", 2, 0);
            ConfigureCombo(_scopeCombo, new[] { "目前視圖", "目前選取" });
            _scopeCombo.SelectedIndex = 0;
            _scopeCombo.SelectedIndexChanged += (sender, args) => UpdateSummary();
            top.Controls.Add(_scopeCombo, 3, 0);

            AddLabel(top, "標籤位置", 0, 1);
            ConfigureCombo(_placementCombo, new[] { "構件中心", "上方", "下方", "左側", "右側" });
            _placementCombo.SelectedIndex = 1;
            _placementCombo.SelectedIndexChanged += (sender, args) => UpdateSummary();
            top.Controls.Add(_placementCombo, 1, 1);

            AddLabel(top, "偏移距離 mm", 2, 1);
            _offsetText.Text = "250";
            _offsetText.Dock = Forms.DockStyle.Fill;
            _offsetText.TextChanged += (sender, args) => UpdateSummary();
            top.Controls.Add(_offsetText, 3, 1);

            return top;
        }

        private Forms.Control BuildFlagPanel()
        {
            var flags = new Forms.FlowLayoutPanel
            {
                Dock = Forms.DockStyle.Fill,
                FlowDirection = Forms.FlowDirection.LeftToRight
            };

            _leaderCheck.Text = "建立引線";
            _leaderCheck.Checked = true;
            _leaderCheck.Width = 120;
            _leaderCheck.CheckedChanged += (sender, args) => UpdateSummary();
            flags.Controls.Add(_leaderCheck);

            _skipExistingCheck.Text = "略過已標籤構件";
            _skipExistingCheck.Checked = true;
            _skipExistingCheck.Width = 160;
            _skipExistingCheck.CheckedChanged += (sender, args) => UpdateSummary();
            flags.Controls.Add(_skipExistingCheck);

            _avoidOverlapCheck.Text = "自動避讓重疊標籤";
            _avoidOverlapCheck.Checked = true;
            _avoidOverlapCheck.Width = 170;
            _avoidOverlapCheck.CheckedChanged += (sender, args) => UpdateSummary();
            flags.Controls.Add(_avoidOverlapCheck);

            _summaryLabel.AutoSize = true;
            _summaryLabel.ForeColor = Color.FromArgb(75, 88, 105);
            _summaryLabel.Margin = new Forms.Padding(8, 4, 0, 0);
            flags.Controls.Add(_summaryLabel);

            return flags;
        }

        private Forms.Control BuildRuleGrid(RevitDB.Document doc)
        {
            var panel = new Forms.Panel { Dock = Forms.DockStyle.Fill, AutoScroll = true };
            var grid = new Forms.TableLayoutPanel
            {
                Dock = Forms.DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = AutoTagService.CategoryRules.Length + 1
            };
            grid.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 140));
            grid.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100));
            panel.Controls.Add(grid);

            AddHeader(grid, "構件分類", 0, 0);
            AddHeader(grid, "標籤族型", 1, 0);

            for (int i = 0; i < AutoTagService.CategoryRules.Length; i++)
            {
                AutoTagCategoryRule rule = AutoTagService.CategoryRules[i];
                int row = i + 1;
                grid.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 32));

                var checkBox = new Forms.CheckBox
                {
                    Text = rule.Name,
                    Dock = Forms.DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                checkBox.CheckedChanged += (sender, args) => UpdateSummary();
                grid.Controls.Add(checkBox, 0, row);

                var comboBox = new Forms.ComboBox
                {
                    Dock = Forms.DockStyle.Fill,
                    DropDownStyle = Forms.ComboBoxStyle.DropDownList
                };
                comboBox.SelectedIndexChanged += (sender, args) => UpdateSummary();
                grid.Controls.Add(comboBox, 1, row);

                _rows.Add(new RowBinding(rule, checkBox, comboBox));
            }

            UpdateTagTypes(doc);
            return panel;
        }

        private Forms.Control BuildButtons()
        {
            var buttons = new Forms.FlowLayoutPanel
            {
                Dock = Forms.DockStyle.Fill,
                FlowDirection = Forms.FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Forms.Padding(0, 8, 0, 0)
            };

            var close = new Forms.Button
            {
                Text = "關閉",
                Width = 88,
                Height = 30
            };
            close.Click += (sender, args) => Close();
            buttons.Controls.Add(close);

            _applyButton.Text = "執行標籤";
            _applyButton.Width = 104;
            _applyButton.Height = 30;
            _applyButton.Click += ApplyClicked;
            buttons.Controls.Add(_applyButton);

            _saveDefaultButton.Text = "儲存預設";
            _saveDefaultButton.Width = 96;
            _saveDefaultButton.Height = 30;
            _saveDefaultButton.Click += SaveDefaultClicked;
            buttons.Controls.Add(_saveDefaultButton);

            _resetButton.Text = "重設";
            _resetButton.Width = 78;
            _resetButton.Height = 30;
            _resetButton.Click += ResetClicked;
            buttons.Controls.Add(_resetButton);

            _refreshButton.Text = "重新讀取";
            _refreshButton.Width = 96;
            _refreshButton.Height = 30;
            _refreshButton.Click += RefreshClicked;
            buttons.Controls.Add(_refreshButton);

            _statusLabel.Text = "視窗可保持開啟；可回 Revit 調整選取後再執行。";
            _statusLabel.AutoSize = true;
            _statusLabel.ForeColor = Color.FromArgb(75, 88, 105);
            _statusLabel.Margin = new Forms.Padding(12, 7, 12, 0);
            buttons.Controls.Add(_statusLabel);

            AcceptButton = _applyButton;
            return buttons;
        }

        private void ApplyClicked(object sender, EventArgs e)
        {
            if (!TryReadOptions(out AutoTagOptions options))
                return;

            IReadOnlyList<AutoTagRuleSelection> rules = GetSelectedRules();
            if (rules.Count == 0)
            {
                Forms.MessageBox.Show("請至少勾選一個已有標籤族型的構件分類。", _windowTitle, Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
                return;
            }

            SaveCurrentSettings("已套用並記住目前專案設定。");
            SetPending("正在等候 Revit 執行...");
            _applyAction(options, rules);
        }

        private void RefreshClicked(object sender, EventArgs e)
        {
            SetPending("正在重新讀取目前文件的標籤族型...");
            _refreshAction();
        }

        private void SaveDefaultClicked(object sender, EventArgs e)
        {
            SaveCurrentSettings("已儲存為此專案預設。");
        }

        private void ResetClicked(object sender, EventArgs e)
        {
            AutoTagSettingsStore.Reset(_doc, _mode);
            ApplyDefaultOptions();
            ApplyTemplate();
            UpdateSummary();
            _statusLabel.ForeColor = Color.FromArgb(75, 88, 105);
            _statusLabel.Text = "已重設為系統預設。";
        }

        private bool TryReadOptions(out AutoTagOptions options)
        {
            options = null;
            if (!double.TryParse(_offsetText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double offset) || offset < 0 || offset > 10000)
            {
                Forms.MessageBox.Show("偏移距離請輸入 0 到 10000 mm 之間的數值。", _windowTitle, Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
                return false;
            }

            options = new AutoTagOptions
            {
                Template = _templateCombo.SelectedIndex == 0 ? AutoTagTemplate.Structure : AutoTagTemplate.Mep,
                Scope = _scopeCombo.SelectedIndex == 1 ? AutoTagScope.Selection : AutoTagScope.ActiveView,
                Placement = IndexToPlacement(_placementCombo.SelectedIndex),
                AddLeader = _leaderCheck.Checked,
                SkipExistingTags = _skipExistingCheck.Checked,
                AvoidTagOverlap = _avoidOverlapCheck.Checked,
                OffsetMillimeters = offset
            };
            return true;
        }

        private void SaveCurrentSettings(string status)
        {
            if (!TryReadOptions(out AutoTagOptions options))
                return;

            AutoTagSettingsStore.Save(_doc, _mode, options, GetSavedRules());
            _statusLabel.ForeColor = Color.FromArgb(75, 88, 105);
            _statusLabel.Text = status;
        }

        private IReadOnlyList<AutoTagRuleSelection> GetSelectedRules()
        {
            return _rows
                .Where(row => row.CheckBox.Checked && row.ComboBox.SelectedItem is AutoTagTypeOption option && option.Id != RevitDB.ElementId.InvalidElementId)
                .Select(row => new AutoTagRuleSelection(row.Rule, ((AutoTagTypeOption)row.ComboBox.SelectedItem).Id))
                .ToList();
        }

        private void SetPending(string status)
        {
            _applyButton.Enabled = false;
            _refreshButton.Enabled = false;
            _saveDefaultButton.Enabled = false;
            _resetButton.Enabled = false;
            _statusLabel.ForeColor = Color.FromArgb(75, 88, 105);
            _statusLabel.Text = status;
        }

        private void ApplyTemplate()
        {
            bool structure = _templateCombo.SelectedIndex == 0;
            foreach (RowBinding row in _rows)
            {
                row.CheckBox.Checked = structure ? row.Rule.StructureDefault : row.Rule.MepDefault;
            }
        }

        private bool ApplySavedSettings(AutoTagSavedSettings settings)
        {
            if (settings == null || settings.Options == null)
                return false;

            ApplyOptions(settings.Options);

            Dictionary<string, AutoTagSavedRule> savedRules = settings.Rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.CategoryName))
                .GroupBy(rule => rule.CategoryName)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (RowBinding row in _rows)
            {
                if (!savedRules.TryGetValue(row.Rule.Name, out AutoTagSavedRule savedRule))
                    continue;

                row.CheckBox.Checked = savedRule.Enabled;
                row.PreferredTagTypeLabel = savedRule.TagTypeLabel;
                int index = FindTagTypeIndex(row.ComboBox, savedRule.TagTypeLabel);
                if (index >= 0)
                    row.ComboBox.SelectedIndex = index;
            }

            _statusLabel.Text = "已載入此專案上次使用的自動標籤設定。";
            return true;
        }

        private void ApplyDefaultOptions()
        {
            ApplyOptions(new AutoTagOptions());
        }

        private void ApplyOptions(AutoTagOptions options)
        {
            _templateCombo.SelectedIndex = options.Template == AutoTagTemplate.Structure ? 0 : 1;
            _scopeCombo.SelectedIndex = options.Scope == AutoTagScope.Selection ? 1 : 0;
            _placementCombo.SelectedIndex = PlacementToIndex(options.Placement);
            _offsetText.Text = options.OffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
            _leaderCheck.Checked = options.AddLeader;
            _skipExistingCheck.Checked = options.SkipExistingTags;
            _avoidOverlapCheck.Checked = options.AvoidTagOverlap;
        }

        private IReadOnlyList<AutoTagSavedRule> GetSavedRules()
        {
            return _rows
                .Select(row => new AutoTagSavedRule
                {
                    CategoryName = row.Rule.Name,
                    Enabled = row.CheckBox.Checked,
                    TagTypeLabel = row.SelectedTagTypeLabel
                })
                .ToList();
        }

        private void UpdateSummary()
        {
            if (_summaryLabel == null || _summaryLabel.IsDisposed)
                return;

            int enabled = _rows.Count(row => row.CheckBox.Checked);
            string scope = _scopeCombo.SelectedItem as string ?? string.Empty;
            string placement = _placementCombo.SelectedItem as string ?? string.Empty;
            string overlap = _avoidOverlapCheck.Checked ? "避讓開" : "避讓關";
            _summaryLabel.Text = $"{scope} / {placement} / {overlap} / 已選 {enabled} 類";
        }

        private static int FindTagTypeIndex(Forms.ComboBox comboBox, RevitDB.ElementId id)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is AutoTagTypeOption option && option.Id == id)
                    return i;
            }

            return -1;
        }

        private static int FindTagTypeIndex(Forms.ComboBox comboBox, string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return -1;

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is AutoTagTypeOption option &&
                    string.Equals(option.Label, label, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static AutoTagPlacement IndexToPlacement(int index)
        {
            switch (index)
            {
                case 1:
                    return AutoTagPlacement.Above;
                case 2:
                    return AutoTagPlacement.Below;
                case 3:
                    return AutoTagPlacement.Left;
                case 4:
                    return AutoTagPlacement.Right;
                default:
                    return AutoTagPlacement.Center;
            }
        }

        private static int PlacementToIndex(AutoTagPlacement placement)
        {
            switch (placement)
            {
                case AutoTagPlacement.Above:
                    return 1;
                case AutoTagPlacement.Below:
                    return 2;
                case AutoTagPlacement.Left:
                    return 3;
                case AutoTagPlacement.Right:
                    return 4;
                default:
                    return 0;
            }
        }

        private static void ConfigureCombo(Forms.ComboBox comboBox, string[] items)
        {
            comboBox.Dock = Forms.DockStyle.Fill;
            comboBox.DropDownStyle = Forms.ComboBoxStyle.DropDownList;
            comboBox.Items.AddRange(items);
        }

        private static void AddLabel(Forms.TableLayoutPanel grid, string text, int column, int row)
        {
            grid.Controls.Add(new Forms.Label
            {
                Text = text,
                Dock = Forms.DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, column, row);
        }

        private static void AddHeader(Forms.TableLayoutPanel grid, string text, int column, int row)
        {
            grid.Controls.Add(new Forms.Label
            {
                Text = text,
                Dock = Forms.DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point)
            }, column, row);
        }

        private sealed class RowBinding
        {
            public RowBinding(AutoTagCategoryRule rule, Forms.CheckBox checkBox, Forms.ComboBox comboBox)
            {
                Rule = rule;
                CheckBox = checkBox;
                ComboBox = comboBox;
            }

            public AutoTagCategoryRule Rule { get; }

            public Forms.CheckBox CheckBox { get; }

            public Forms.ComboBox ComboBox { get; }

            public RevitDB.ElementId SelectedTagTypeId
            {
                get
                {
                    return ComboBox.SelectedItem is AutoTagTypeOption option
                        ? option.Id
                        : RevitDB.ElementId.InvalidElementId;
                }
            }

            public string SelectedTagTypeLabel
            {
                get
                {
                    return ComboBox.SelectedItem is AutoTagTypeOption option
                        ? option.Label
                        : null;
                }
            }

            public string PreferredTagTypeLabel { get; set; }
        }
    }
}
