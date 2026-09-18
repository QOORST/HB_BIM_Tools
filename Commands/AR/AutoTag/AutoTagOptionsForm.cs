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
        private int _viewScale;
        private readonly Action<AutoTagOptions, IReadOnlyList<AutoTagRuleSelection>> _applyAction;
        private readonly Action _refreshAction;
        private readonly string _windowTitle;
        private readonly Forms.ComboBox _templateCombo = new Forms.ComboBox();
        private readonly Forms.NumericUpDown _maxMove = new Forms.NumericUpDown();
        private readonly Forms.CheckBox _crossSide = new Forms.CheckBox();
        private readonly Forms.CheckBox _presentOnly = new Forms.CheckBox();
        private readonly Forms.Label _categoryHint = new Forms.Label();
        private bool _refreshingSources;
        private HashSet<RevitDB.BuiltInCategory> _presentCategories = new HashSet<RevitDB.BuiltInCategory>();
        private readonly Forms.ComboBox _linkCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _scopeCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _placementCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _directionCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _textDirectionCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _layoutCombo = new Forms.ComboBox();
        private readonly Forms.NumericUpDown _bankGap = new Forms.NumericUpDown();
        private readonly Forms.NumericUpDown _bankLeader = new Forms.NumericUpDown();
        private readonly Forms.ComboBox _unitsCombo = new Forms.ComboBox();
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
            _viewScale = Math.Max(1, doc.ActiveView.Scale);
            _applyAction = applyAction ?? throw new ArgumentNullException(nameof(applyAction));
            _refreshAction = refreshAction ?? throw new ArgumentNullException(nameof(refreshAction));
            _windowTitle = mode == AutoTagMode.Unified ? "HB_BIM 自動標籤" : mode == AutoTagMode.Vertical ? "自動標籤 - 垂直元素" : "自動標籤 - 水平元素";

            Text = _windowTitle;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = Forms.AutoScaleMode.Dpi;
            Width = 780;
            Height = 800;
            MinimizeBox = false;
            MaximizeBox = true;
            MinimumSize = new Size(640, 420);
            FormBorderStyle = Forms.FormBorderStyle.Sizable;
            StartPosition = Forms.FormStartPosition.CenterScreen;
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            BuildLayout(doc);
            ApplyFlatStyle(this);
            UpdateLinks(doc);
            if (!ApplySavedSettings(AutoTagSettingsStore.Load(doc, mode)))
            {
                ApplyTemplate();
                if (_mode == AutoTagMode.Unified) _directionCombo.SelectedIndex = 1;
            }

            UpdateSummary();
            Shown += (_, __) => FitWorkingArea();
            ResizeEnd += (_, __) => FitWorkingArea();
            DpiChanged += (_, __) => BeginInvoke(new Action(FitWorkingArea));
        }

        private void FitWorkingArea()
        {
            if (IsDisposed || WindowState != Forms.FormWindowState.Normal) return;
            Rectangle work = Forms.Screen.FromHandle(Handle).WorkingArea;
            int margin = Math.Max(4, (int)(8 * DeviceDpi / 96.0));
            int width = Math.Max(1, work.Width - margin * 2);
            int height = Math.Max(1, work.Height - margin * 2);
            MinimumSize = new Size(Math.Min(width, (int)(640 * DeviceDpi / 96.0)),
                Math.Min(height, (int)(420 * DeviceDpi / 96.0)));
            Size = new Size(Math.Min(Width, width), Math.Min(Height, height));
            Location = new Point(Math.Max(work.Left + margin, Math.Min(Left, work.Right - margin - Width)),
                Math.Max(work.Top + margin, Math.Min(Top, work.Bottom - margin - Height)));
        }

        public void UpdateTagTypes(RevitDB.Document doc)
        {
            _viewScale = Math.Max(1, doc.ActiveView.Scale);
            _refreshingSources = true;
            try { UpdateLinks(doc); } finally { _refreshingSources = false; }
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
                    row.ComboBox.Items.Add(new AutoTagTypeOption(RevitDB.ElementId.InvalidElementId, "缺少標籤族型，請載入後重新整理"));
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

            RefreshPresentCategories(doc);

            ApplyCategoryVisibility();
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
            BackColor = Color.FromArgb(245, 247, 250);
            var shell = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Fill, Padding = new Forms.Padding(8), ColumnCount = 1, RowCount = 3 };
            shell.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
            shell.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
            shell.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
            Controls.Add(shell);
            var scroll = new Forms.Panel { Dock = Forms.DockStyle.Fill, AutoScroll = true };
            shell.Controls.Add(scroll, 0, 0);
            var root = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Top, AutoSize = true, Padding = new Forms.Padding(8),
                ColumnCount = 1, RowCount = 4 };
            root.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 98));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 280));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 102));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
            scroll.Controls.Add(root);

            // Initialize existing settings and handlers before arranging the controls into sections.
            var oldTop = BuildTopPanel();
            var oldFlags = BuildFlagPanel();
            var source = SettingsGrid();
            AddLabel(source, "標註範圍", 0, 0); source.Controls.Add(_scopeCombo, 1, 0); source.SetColumnSpan(_scopeCombo, 3);
            AddLabel(source, "連結實例", 0, 1); source.Controls.Add(_linkCombo, 1, 1); source.SetColumnSpan(_linkCombo, 3);
            root.Controls.Add(Section("來源與範圍", source), 0, 0);

            var rules = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            rules.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 66));
            rules.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
            var toolbar = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, WrapContents = true };
            toolbar.Controls.Add(new Forms.Label { Text = "套用分類預設", AutoSize = true, Margin = new Forms.Padding(0, 7, 8, 0) });
            _templateCombo.Dock = Forms.DockStyle.None; _templateCombo.Width = 110;
            toolbar.Controls.Add(_templateCombo);
            foreach (bool selected in new[] { true, false })
            {
                var button = new Forms.Button { Text = selected ? "全選" : "清除", AutoSize = true };
                button.Click += (_, __) => { foreach (var row in _rows) row.CheckBox.Checked = selected; };
                toolbar.Controls.Add(button);
            }
            _presentOnly.Text = "只顯示目前視圖中的分類";
            _presentOnly.AutoSize = true;
            _presentOnly.CheckedChanged += (_, __) =>
            {
                if (_presentOnly.Checked) RefreshClicked(null, EventArgs.Empty);
                else ApplyCategoryVisibility();
            };
            toolbar.Controls.Add(_presentOnly);
            toolbar.SetFlowBreak(_presentOnly, true);
            _categoryHint.AutoSize = true;
            _categoryHint.ForeColor = Color.FromArgb(75, 88, 105);
            toolbar.Controls.Add(_categoryHint);
            rules.Controls.Add(toolbar, 0, 0);
            rules.Controls.Add(BuildRuleGrid(doc), 0, 1);
            root.Controls.Add(Section("標籤規則", rules), 0, 1);

            var placement = SettingsGrid();
            AddLabel(placement, "放置位置", 0, 0); placement.Controls.Add(_placementCombo, 1, 0);
            AddLabel(placement, "偏移距離", 2, 0); placement.Controls.Add(_offsetText, 3, 0);
            AddLabel(placement, "距離單位", 0, 1); placement.Controls.Add(_unitsCombo, 1, 1);
            _leaderCheck.AutoSize = true; placement.Controls.Add(_leaderCheck, 3, 1);
            root.Controls.Add(Section("放置設定", placement), 0, 2);

            var advanced = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
            var toggle = new Forms.Button { Text = "▸ 進階設定", AutoSize = true, FlatStyle = Forms.FlatStyle.Flat };
            var detail = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Top, AutoSize = true, Visible = false };
            _directionCombo.Dock = Forms.DockStyle.None; _directionCombo.Width = 180;
            detail.Controls.Add(new Forms.Label { Text = "梁／管線方向", AutoSize = true, Margin = new Forms.Padding(0, 7, 6, 0) });
            detail.Controls.Add(_directionCombo); detail.Controls.Add(_skipExistingCheck); detail.Controls.Add(_avoidOverlapCheck);
            detail.SetFlowBreak(_avoidOverlapCheck, true);
            detail.Controls.Add(new Forms.Label { Text = "文字方向", AutoSize = true, Margin = new Forms.Padding(0, 7, 6, 0) });
            ConfigureCombo(_textDirectionCombo, new[] { "水平", "垂直", "跟隨管線方向" });
            _textDirectionCombo.Dock = Forms.DockStyle.None;
            _textDirectionCombo.Width = 180;
            _textDirectionCombo.SelectedIndex = 0;
            detail.Controls.Add(_textDirectionCombo);
            detail.SetFlowBreak(_textDirectionCombo, true);
            detail.Controls.Add(new Forms.Label { Text = "配置模式", AutoSize = true });
            ConfigureCombo(_layoutCombo, new[] { "單管", "管排（目前選取）" });
            _layoutCombo.Dock = Forms.DockStyle.None;
            _layoutCombo.Width = 180;
            _layoutCombo.SelectedIndex = 0;
            detail.Controls.Add(_layoutCombo);
            detail.Controls.Add(new Forms.Label { Text = "淨距（紙面 mm）", AutoSize = true });
            _bankGap.Minimum = 0.5m; _bankGap.Maximum = 50; _bankGap.DecimalPlaces = 1; _bankGap.Value = 2; _bankGap.Width = 65;
            detail.Controls.Add(_bankGap);
            detail.Controls.Add(new Forms.Label { Text = "引線退距（紙面 mm）", AutoSize = true });
            _bankLeader.Minimum = 2; _bankLeader.Maximum = 100; _bankLeader.DecimalPlaces = 1; _bankLeader.Value = 5; _bankLeader.Width = 65;
            detail.Controls.Add(_bankLeader);
            detail.SetFlowBreak(_bankLeader, true);
            _layoutCombo.SelectedIndexChanged += (_, __) =>
            {
                bool bank = _layoutCombo.SelectedIndex == 1;
                _bankGap.Enabled = _bankLeader.Enabled = bank;
                if (bank) { _scopeCombo.SelectedIndex = 1; _leaderCheck.Checked = true; _textDirectionCombo.SelectedIndex = 0; }
                _textDirectionCombo.Enabled = !bank;
            };
            _bankGap.Enabled = _bankLeader.Enabled = false;
            detail.Controls.Add(new Forms.Label { Text = "最大移動（紙面 mm）", AutoSize = true, Margin = new Forms.Padding(0, 7, 6, 0) });
            _maxMove.Minimum = 0; _maxMove.Maximum = 100; _maxMove.DecimalPlaces = 1; _maxMove.Value = 10; _maxMove.Width = 70;
            detail.Controls.Add(_maxMove);
            _crossSide.Text = "允許跨側"; _crossSide.AutoSize = true;
            detail.Controls.Add(_crossSide);
            detail.Controls.Add(new Forms.Label { Text = "無引線最多移動 3 mm；中心固定；無空位保留原位。", AutoSize = true, Margin = new Forms.Padding(8, 7, 0, 0) });
            toggle.Click += (_, __) => { detail.Visible = !detail.Visible; toggle.Text = detail.Visible ? "▾ 進階設定" : "▸ 進階設定"; };
            advanced.Controls.Add(toggle, 0, 0); advanced.Controls.Add(detail, 0, 1);
            root.Controls.Add(advanced, 0, 3);
            shell.Controls.Add(_statusLabel, 0, 1);
            shell.Controls.Add(BuildButtons(), 0, 2);
            _statusLabel.Dock = Forms.DockStyle.Fill;
            _statusLabel.AutoSize = false;
            _statusLabel.Height = 42;
            oldTop.Dispose(); oldFlags.Dispose();
        }

        private static Forms.TableLayoutPanel SettingsGrid()
        {
            var grid = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
            grid.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 90));
            grid.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 50));
            grid.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 90));
            grid.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 50));
            grid.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 50));
            grid.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 50));
            return grid;
        }

        private static Forms.Control Section(string title, Forms.Control content)
        {
            var section = new Forms.TableLayoutPanel
            {
                Dock = Forms.DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = Color.White, Padding = new Forms.Padding(12, 0, 12, 6),
                Margin = new Forms.Padding(0, 0, 0, 8)
            };
            section.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 24));
            section.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 1));
            section.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
            section.Controls.Add(new Forms.Label
            {
                Text = title, Dock = Forms.DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(38, 54, 72), Margin = new Forms.Padding(0)
            }, 0, 0);
            section.Controls.Add(new Forms.Panel
            {
                Dock = Forms.DockStyle.Fill, BackColor = Color.FromArgb(229, 234, 240), Margin = new Forms.Padding(0)
            }, 0, 1);
            section.Controls.Add(content, 0, 2);
            return section;
        }

        private void ApplyFlatStyle(Forms.Control root)
        {
            foreach (Forms.Control control in root.Controls)
            {
                if (control is Forms.Button button)
                {
                    bool primary = ReferenceEquals(button, _applyButton);
                    button.FlatStyle = Forms.FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    button.BackColor = primary ? Color.FromArgb(0, 105, 180) : Color.FromArgb(245, 247, 250);
                    button.ForeColor = primary ? Color.White : Color.FromArgb(48, 65, 84);
                    button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 85, 150) : Color.FromArgb(226, 235, 244);
                    button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0, 70, 125) : Color.FromArgb(210, 225, 240);
                    button.UseVisualStyleBackColor = false;
                }
                else if (control is Forms.ComboBox combo)
                {
                    combo.FlatStyle = Forms.FlatStyle.Flat;
                    combo.BackColor = Color.FromArgb(244, 247, 250);
                    combo.ForeColor = Color.FromArgb(38, 54, 72);
                }
                else if (control is Forms.TextBox text)
                {
                    text.BorderStyle = Forms.BorderStyle.FixedSingle;
                    text.BackColor = Color.White;
                    text.ForeColor = Color.FromArgb(38, 54, 72);
                }
                else if (control is Forms.CheckBox check)
                {
                    check.FlatStyle = Forms.FlatStyle.Flat;
                    check.ForeColor = Color.FromArgb(48, 65, 84);
                }
                ApplyFlatStyle(control);
            }
        }

        private Forms.Control BuildTopPanel()
        {
            var top = new Forms.TableLayoutPanel
            {
                Dock = Forms.DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4
            };
            top.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 94));
            top.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 50));
            top.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 104));
            top.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 50));

            AddLabel(top, "樣板分類", 0, 0);
            ConfigureCombo(_templateCombo, new[] { "結構", "機電", "建築" });
            _templateCombo.SelectedIndex = 1;
            _templateCombo.SelectedIndexChanged += (sender, args) =>
            {
                ApplyTemplate();
                UpdateSummary();
            };
            top.Controls.Add(_templateCombo, 1, 0);

            AddLabel(top, "標註範圍", 2, 0);
            #if REVIT2024 || REVIT2025 || REVIT2026
            ConfigureCombo(_scopeCombo, new[] { "目前視圖", "目前選取", "指定連結（目前視圖）" });
#else
            ConfigureCombo(_scopeCombo, new[] { "目前視圖", "目前選取" });
#endif
            _scopeCombo.SelectedIndex = 0;
            _scopeCombo.SelectedIndexChanged += (sender, args) => { _presentOnly.Checked = false; _linkCombo.Enabled = _scopeCombo.SelectedIndex == 2; UpdateSummary(); };
            top.Controls.Add(_scopeCombo, 3, 0);

            AddLabel(top, "標籤位置", 0, 1);
            ConfigureCombo(_placementCombo, new[] { "元素中心（固定，不避讓）", "上方", "下方", "左側", "右側" });
            _placementCombo.SelectedIndex = 1;
            _placementCombo.SelectedIndexChanged += (sender, args) => { _offsetText.Enabled = _placementCombo.SelectedIndex != 0; UpdateSummary(); };
            top.Controls.Add(_placementCombo, 1, 1);

            AddLabel(top, "偏移距離 mm", 2, 1);
            _offsetText.Text = "250";
            _offsetText.Dock = Forms.DockStyle.Fill;
            _offsetText.TextChanged += (sender, args) => UpdateSummary();
            top.Controls.Add(_offsetText, 3, 1);

            AddLabel(top, "元素方向", 0, 2);
            ConfigureCombo(_directionCombo, new[] { "水平", "全部（含斜向）", "垂直" });
            _directionCombo.SelectedIndex = _mode == AutoTagMode.Vertical ? 2 : _mode == AutoTagMode.Unified ? 1 : 0;
            top.Controls.Add(_directionCombo, 1, 2);
            AddLabel(top, "距離單位", 2, 2);
            ConfigureCombo(_unitsCombo, new[] { "模型 mm", "紙面 mm（依視圖比例）" });
            _unitsCombo.SelectedIndex = 0;
            _unitsCombo.SelectedIndexChanged += (_, __) =>
            {
                if (double.TryParse(_offsetText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    int scale = _viewScale;
                    _offsetText.Text = (_unitsCombo.SelectedIndex == 1 ? value / scale : value * scale)
                        .ToString("0.###", CultureInfo.InvariantCulture);
                }
            };
            top.Controls.Add(_unitsCombo, 3, 2);
            AddLabel(top, "連結實例", 0, 3);
            ConfigureCombo(_linkCombo, new string[0]);
            _linkCombo.Enabled = false;
            _linkCombo.SelectedIndexChanged += (_, __) => { if (!_refreshingSources) _presentOnly.Checked = false; };
            top.Controls.Add(_linkCombo, 1, 3);
            top.SetColumnSpan(_linkCombo, 3);
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

            _skipExistingCheck.Text = "略過已有標籤的元素";
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

            grid.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 32));
            AddHeader(grid, "支援的元素分類", 0, 0);
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
                checkBox.BackColor = i % 2 == 0 ? Color.FromArgb(248, 250, 252) : Color.White;
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
                WrapContents = true,
                AutoSize = true,
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


            _resetButton.Text = "重設";
            _resetButton.Width = 78;
            _resetButton.Height = 30;
            _resetButton.Click += ResetClicked;


            _refreshButton.Text = "重新讀取";
            _refreshButton.Width = 96;
            _refreshButton.Height = 30;
            _refreshButton.Click += RefreshClicked;


            _statusLabel.Text = "視窗可保持開啟；可回 Revit 調整選取後再執行。";
            _statusLabel.AutoSize = true;
            _statusLabel.ForeColor = Color.FromArgb(75, 88, 105);
            _statusLabel.Margin = new Forms.Padding(12, 7, 12, 0);


            _applyButton.BackColor = Color.FromArgb(0, 105, 180);
            _applyButton.ForeColor = Color.White;
            _applyButton.FlatStyle = Forms.FlatStyle.Flat;
            var more = new Forms.Button { Text = "更多 ▾", Width = 88, Height = 30 };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("重新整理族型與連結", null, (_, __) => { if (_refreshButton.Enabled) RefreshClicked(null, EventArgs.Empty); });
            menu.Items.Add("儲存專案預設", null, (_, __) => { if (_saveDefaultButton.Enabled) SaveDefaultClicked(null, EventArgs.Empty); });
            menu.Items.Add("重設設定", null, (_, __) => { if (_resetButton.Enabled) ResetClicked(null, EventArgs.Empty); });
            more.Click += (_, __) => menu.Show(more, new Point(0, more.Height));
            FormClosed += (_, __) => menu.Dispose();
            buttons.Controls.Add(more);
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
                Forms.MessageBox.Show("請至少勾選一個已有標籤族型的元素分類。", _windowTitle, Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
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
            if (!double.TryParse(_offsetText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double offset) || double.IsNaN(offset) || double.IsInfinity(offset) || offset < 0 || offset > 10000)
            {
                Forms.MessageBox.Show("偏移距離請輸入 0 到 10000 mm 之間的數值。", _windowTitle, Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
                return false;
            }

            if (_scopeCombo.SelectedIndex == 2 && !(_linkCombo.SelectedItem is LinkChoice selectedLink && selectedLink.Available))
            {
                Forms.MessageBox.Show("請重新整理並選擇已載入的連結實例。", _windowTitle);
                return false;
            }
            options = new AutoTagOptions
            {
                Template = (AutoTagTemplate)_templateCombo.SelectedIndex,
                Scope = (AutoTagScope)_scopeCombo.SelectedIndex,
                LinkInstanceUniqueId = (_linkCombo.SelectedItem as LinkChoice)?.UniqueId ?? string.Empty,
                Placement = IndexToPlacement(_placementCombo.SelectedIndex),
                AddLeader = _leaderCheck.Checked,
                TextDirection = (AutoTagTextDirection)Math.Max(0, _textDirectionCombo.SelectedIndex),
                PipeBank = _layoutCombo.SelectedIndex == 1,
                BankGapPaperMm = (double)_bankGap.Value,
                BankLeaderPaperMm = (double)_bankLeader.Value,
                SkipExistingTags = _skipExistingCheck.Checked,
                AvoidTagOverlap = _avoidOverlapCheck.Checked,
                VerticalOnly = _directionCombo.SelectedIndex == 2,
                AllDirections = _directionCombo.SelectedIndex == 1,
                UsePaperMillimeters = _unitsCombo.SelectedIndex == 1,
                MaxMovePaperMillimeters = (double)_maxMove.Value,
                AllowCrossSide = _crossSide.Checked,
                OffsetMillimeters = offset
            };
            if (options.PipeBank && (options.Scope != AutoTagScope.Selection || options.Placement == AutoTagPlacement.Center || !options.AddLeader))
            {
                Forms.MessageBox.Show("管排需使用目前選取、指定上／下／左／右側，並開啟引線。", _windowTitle);
                return false;
            }
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
                .Where(row => row.CheckBox.Checked && (!_presentOnly.Checked || _presentCategories.Contains(row.Rule.ElementCategory)) && row.ComboBox.SelectedItem is AutoTagTypeOption option && option.Id != RevitDB.ElementId.InvalidElementId)
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
                row.CheckBox.Checked = structure ? row.Rule.StructureDefault :
                    _templateCombo.SelectedIndex == 2 ? row.Rule.ArchitectureDefault : row.Rule.MepDefault;
            }
        }

        private static string NormalizeCategoryName(string name)
        {
            return name == "管" ? "管線" : name == "電纜架" ? "電纜橋架" : name;
        }

        private bool ApplySavedSettings(AutoTagSavedSettings settings)
        {
            if (settings == null || settings.Options == null)
                return false;

            ApplyOptions(settings.Options);

            Dictionary<string, AutoTagSavedRule> savedRules = settings.Rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.CategoryName))
                .GroupBy(rule => NormalizeCategoryName(rule.CategoryName))
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
            ApplyOptions(new AutoTagOptions { AllDirections = _mode == AutoTagMode.Unified, VerticalOnly = _mode == AutoTagMode.Vertical });
        }

        private void ApplyOptions(AutoTagOptions options)
        {
            _templateCombo.SelectedIndex = Math.Max(0, Math.Min(2, (int)options.Template));
            _scopeCombo.SelectedIndex = (int)options.Scope < _scopeCombo.Items.Count ? (int)options.Scope : 0;
            SelectLink(options.LinkInstanceUniqueId);
            _placementCombo.SelectedIndex = PlacementToIndex(options.Placement);
            _directionCombo.SelectedIndex = options.AllDirections ? 1 : (options.VerticalOnly || _mode == AutoTagMode.Vertical ? 2 : 0);
            _unitsCombo.SelectedIndex = options.UsePaperMillimeters ? 1 : 0;
            _maxMove.Value = double.IsNaN(options.MaxMovePaperMillimeters) || double.IsInfinity(options.MaxMovePaperMillimeters) ? 10 : (decimal)Math.Max(0, Math.Min(100, options.MaxMovePaperMillimeters));
            _crossSide.Checked = options.AllowCrossSide;
            _offsetText.Text = options.OffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
            _leaderCheck.Checked = options.AddLeader;
            _textDirectionCombo.SelectedIndex = Math.Max(0, Math.Min(2, (int)options.TextDirection));
            _layoutCombo.SelectedIndex = options.PipeBank ? 1 : 0;
            _bankGap.Value = double.IsNaN(options.BankGapPaperMm) ? 2 : (decimal)Math.Max(0.5, Math.Min(50, options.BankGapPaperMm));
            _bankLeader.Value = double.IsNaN(options.BankLeaderPaperMm) ? 5 : (decimal)Math.Max(2, Math.Min(100, options.BankLeaderPaperMm));
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

        private void RefreshPresentCategories(RevitDB.Document doc)
        {
            _presentCategories.Clear();
            try
            {
                using (var collector = CreateSourceViewCollector(doc))
                {
                    if (collector != null)
                        foreach (var element in collector.WhereElementIsNotElementType())
                            if (element.Category != null && element.Category.CategoryType == RevitDB.CategoryType.Model)
#if REVIT2024 || REVIT2025 || REVIT2026
                                _presentCategories.Add((RevitDB.BuiltInCategory)element.Category.Id.Value);
#else
                                _presentCategories.Add((RevitDB.BuiltInCategory)element.Category.Id.IntegerValue);
#endif
                }
                var supported = new HashSet<RevitDB.BuiltInCategory>(AutoTagService.CategoryRules.Select(r => r.ElementCategory));
                int other = _presentCategories.Count(c => !supported.Contains(c) && c != RevitDB.BuiltInCategory.OST_RvtLinks);
                _categoryHint.Text = $"支援 {supported.Count} 類；視圖含 {other} 類其他模型分類（未支援）。缺少族型請載入後重新整理。";
            }
            catch
            {
                _categoryHint.Text = "無法讀取視圖分類，已顯示所有支援分類；請切換可標註視圖後重新整理。";
                _presentOnly.Checked = false;
            }
        }

        private RevitDB.FilteredElementCollector CreateSourceViewCollector(RevitDB.Document doc)
        {
            if (_scopeCombo.SelectedIndex != 2)
                return new RevitDB.FilteredElementCollector(doc, doc.ActiveView.Id);
#if REVIT2024 || REVIT2025 || REVIT2026
            var choice = _linkCombo.SelectedItem as LinkChoice;
            var link = choice == null || !choice.Available ? null : doc.GetElement(choice.UniqueId) as RevitDB.RevitLinkInstance;
            return link?.GetLinkDocument() == null ? null : new RevitDB.FilteredElementCollector(doc, doc.ActiveView.Id, link.Id);
#else
            return null;
#endif
        }

        private void ApplyCategoryVisibility()
        {
            foreach (var row in _rows)
            {
                bool visible = !_presentOnly.Checked || _presentCategories.Contains(row.Rule.ElementCategory);
                row.CheckBox.Visible = visible;
                row.ComboBox.Visible = visible;
                if (row.CheckBox.Parent is Forms.TableLayoutPanel grid)
                {
                    int index = grid.GetRow(row.CheckBox);
                    while (grid.RowStyles.Count <= index) grid.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 32));
                    grid.RowStyles[index].SizeType = Forms.SizeType.Absolute;
                    grid.RowStyles[index].Height = visible ? 32 : 0;
                }
            }
        }

        private sealed class LinkChoice
        {
            public string UniqueId { get; set; }
            public string Label { get; set; }
            public bool Available { get; set; }
            public override string ToString() => Label;
        }

        private void UpdateLinks(RevitDB.Document doc)
        {
            string previous = (_linkCombo.SelectedItem as LinkChoice)?.UniqueId;
            _linkCombo.Items.Clear();
            foreach (var link in new RevitDB.FilteredElementCollector(doc).OfClass(typeof(RevitDB.RevitLinkInstance))
                .Cast<RevitDB.RevitLinkInstance>().Where(link => link.GetLinkDocument() != null).OrderBy(link => link.Name))
            {
                _linkCombo.Items.Add(new LinkChoice
                {
                    UniqueId = link.UniqueId,
                    Label = $"{link.Name} [ID {link.Id}]",
                    Available = true
                });
            }
            SelectLink(previous);
        }

        private void SelectLink(string uniqueId)
        {
            for (int i = 0; i < _linkCombo.Items.Count; i++)
                if (((LinkChoice)_linkCombo.Items[i]).UniqueId == uniqueId)
                {
                    _linkCombo.SelectedIndex = i;
                    return;
                }
            if (!string.IsNullOrWhiteSpace(uniqueId))
                _linkCombo.SelectedIndex = _linkCombo.Items.Add(new LinkChoice
                {
                    UniqueId = uniqueId, Label = "原指定連結不可用，請重新選擇", Available = false
                });
            else if (_linkCombo.Items.Count > 0)
                _linkCombo.SelectedIndex = 0;
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
