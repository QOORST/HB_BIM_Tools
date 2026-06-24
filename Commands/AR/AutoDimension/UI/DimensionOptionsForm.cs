using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using RevitDB = Autodesk.Revit.DB;
using YDBIM.AutoDimension.Core;

#nullable enable

namespace YDBIM.AutoDimension.UI
{

internal sealed class DimensionOptionsForm : Form
{
    private readonly bool _isModeLocked;
    private readonly DimensionMode _initialMode;
    private readonly string _windowTitle;
    private readonly Action<DimensionOptions>? _applyAction;
    private readonly Action? _refreshAction;

    private TabControl _tabControl = null!;
    private ComboBox _placementModeCombo = null!;
    private ComboBox _typeCombo = null!;
    private TrackBar _offsetTrack = null!;
    private NumericUpDown _offsetNumeric = null!;
    private CheckedListBox _horizontalGridList = null!;
    private CheckedListBox _verticalGridList = null!;
    private Button _leftButton = null!;
    private Button _rightButton = null!;
    private Button _frontButton = null!;
    private Button _backButton = null!;
    private Button _applyButton = null!;
    private Button _refreshButton = null!;
    private Label _statusLabel = null!;

    private LeftRightSide _selectedLeftRight = LeftRightSide.None;
    private FrontBackSide _selectedFrontBack = FrontBackSide.Front;
    private bool _offsetSyncing;

    private sealed class GridItem
    {
        public GridItem(GridSelectionItem item)
        {
            Item = item;
        }

        public GridSelectionItem Item { get; }

        public override string ToString()
        {
            return Item.Label;
        }
    }

    private sealed class PlacementModeItem
    {
        public PlacementModeItem(PlacementMode mode, string label)
        {
            Mode = mode;
            Label = label;
        }

        public PlacementMode Mode { get; }

        public string Label { get; }

        public override string ToString()
        {
            return Label;
        }
    }

    public DimensionOptionsForm(
        IEnumerable<string> dimensionTypeNames,
        IEnumerable<GridSelectionItem> horizontalGrids,
        IEnumerable<GridSelectionItem> verticalGrids,
        DimensionMode initialMode = DimensionMode.ColumnSetout,
        bool lockMode = false,
        string? windowTitle = null,
        Action<DimensionOptions>? applyAction = null,
        Action? refreshAction = null)
    {
        _isModeLocked = lockMode;
        _initialMode = initialMode;
        _windowTitle = windowTitle ?? "YD BIM 自動標註";
        _applyAction = applyAction;
        _refreshAction = refreshAction;

        Text = _windowTitle;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(244, 247, 252);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(940, lockMode && initialMode == DimensionMode.ColumnSetout ? 840 : 780);
        MinimumSize = new Size(900, 760);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        root.Controls.Add(BuildHeader(), 0, 0);

        root.Controls.Add(BuildModeContent(horizontalGrids, verticalGrids), 0, 1);
        root.Controls.Add(BuildOffsetPanel(), 0, 2);
        root.Controls.Add(BuildTypePanel(dimensionTypeNames), 0, 3);
        root.Controls.Add(BuildButtonPanel(), 0, 4);
        Controls.Add(root);

        ApplyModernTheme();
    }

    private Control BuildModeContent(IEnumerable<GridSelectionItem> horizontalGrids, IEnumerable<GridSelectionItem> verticalGrids)
    {
        _tabControl = new TabControl
        {
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(112, 34),
            Padding = new Point(14, 6)
        };
        _tabControl.DrawItem += TabControl_DrawItem;

        if (_isModeLocked)
        {
            TabPage tab = _initialMode switch
            {
                DimensionMode.ColumnSetout => BuildColumnTab(),
                DimensionMode.BeamWidth => BuildBeamWidthTab(),
                _ => BuildGridTab(horizontalGrids, verticalGrids)
            };

            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 8),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };

            while (tab.Controls.Count > 0)
            {
                Control child = tab.Controls[0];
                tab.Controls.Remove(child);
                host.Controls.Add(child);
            }

            return host;
        }

        _tabControl.Dock = DockStyle.Fill;
        _tabControl.Margin = new Padding(0, 12, 0, 10);
        _tabControl.TabPages.Add(BuildColumnTab());
        _tabControl.TabPages.Add(BuildGridTab(horizontalGrids, verticalGrids));
        _tabControl.TabPages.Add(BuildBeamWidthTab());
        return _tabControl;
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(37, 54, 78),
            Padding = new Padding(22, 12, 22, 12)
        };

        var title = new Label
        {
            Text = _windowTitle,
            Dock = DockStyle.Top,
            Height = 38,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point)
        };

        var subtitle = new Label
        {
            Text = "依可見構件建立標註，可設定偏移、標註型式與放置方向。",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(221, 231, 241),
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Padding = new Padding(0, 2, 0, 0)
        };

        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildOffsetPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "偏移量 (mm)",
            Padding = new Padding(16, 24, 16, 12),
            Margin = new Padding(0, 4, 0, 4),
            ForeColor = Color.FromArgb(28, 48, 74),
            BackColor = Color.White,
            MinimumSize = new Size(0, 120)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));

        layout.Controls.Add(new Label { Text = "100-3000", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

        _offsetTrack = new TrackBar
        {
            Minimum = 100,
            Maximum = 3000,
            TickFrequency = 100,
            SmallChange = 100,
            LargeChange = 500,
            Value = 800,
            Dock = DockStyle.Fill,
            TickStyle = TickStyle.None
        };
        _offsetTrack.ValueChanged += (_, _) =>
        {
            if (_offsetSyncing)
            {
                return;
            }

            _offsetSyncing = true;
            _offsetNumeric.Value = _offsetTrack.Value;
            _offsetSyncing = false;
        };
        layout.Controls.Add(_offsetTrack, 1, 0);

        layout.Controls.Add(new Label { Text = "mm", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);

        _offsetNumeric = new NumericUpDown
        {
            Minimum = 100,
            Maximum = 3000,
            Increment = 100,
            Value = 800,
            DecimalPlaces = 0,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = 30,
            TextAlign = HorizontalAlignment.Right
        };
        _offsetNumeric.BorderStyle = BorderStyle.FixedSingle;
        _offsetNumeric.ValueChanged += (_, _) =>
        {
            if (_offsetSyncing)
            {
                return;
            }

            _offsetSyncing = true;
            _offsetTrack.Value = Decimal.ToInt32(_offsetNumeric.Value);
            _offsetSyncing = false;
        };
        layout.Controls.Add(_offsetNumeric, 3, 0);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildTypePanel(IEnumerable<string> dimensionTypeNames)
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "標註型式",
            Padding = new Padding(16, 24, 16, 12),
            Margin = new Padding(0, 4, 0, 4),
            ForeColor = Color.FromArgb(28, 48, 74),
            BackColor = Color.White,
            MinimumSize = new Size(0, 96)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "型式", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

        _typeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = 30,
            FlatStyle = FlatStyle.Flat
        };
        _typeCombo.Items.Add("<預設>");
        foreach (string name in dimensionTypeNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            _typeCombo.Items.Add(name);
        }

        _typeCombo.SelectedIndex = 0;
        layout.Controls.Add(_typeCombo, 1, 0);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 2),
            WrapContents = false
        };

        _applyButton = new Button
        {
            Text = _applyAction is null ? "確定" : "套用",
            Width = 108,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(36, 99, 176),
            ForeColor = Color.White
        };
        _applyButton.FlatAppearance.BorderSize = 0;
        if (_applyAction is null)
        {
            _applyButton.DialogResult = DialogResult.OK;
            AcceptButton = _applyButton;
        }
        else
        {
            _applyButton.Click += (_, _) => BeginApply();
        }

        var close = new Button
        {
            Text = _applyAction is null ? "取消" : "關閉",
            Width = 108,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(48, 60, 78)
        };
        close.FlatAppearance.BorderColor = Color.FromArgb(174, 186, 202);
        if (_applyAction is null)
        {
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
        }
        else
        {
            close.Click += (_, _) => Close();
        }

        _refreshButton = new Button
        {
            Text = "重新整理",
            Width = 108,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(48, 60, 78),
            Visible = _refreshAction is not null
        };
        _refreshButton.FlatAppearance.BorderColor = Color.FromArgb(174, 186, 202);
        _refreshButton.Click += (_, _) => BeginRefresh();

        _statusLabel = new Label
        {
            Text = _applyAction is null ? string.Empty : "可操作 Revit；調整設定後按「套用」。",
            AutoSize = true,
            ForeColor = Color.FromArgb(75, 88, 105),
            Margin = new Padding(12, 9, 12, 0)
        };

        panel.Controls.Add(_applyButton);
        panel.Controls.Add(close);
        panel.Controls.Add(_refreshButton);
        panel.Controls.Add(_statusLabel);
        return panel;
    }

    private void BeginApply()
    {
        if (!TryGetOptions(out DimensionOptions options, out string error))
        {
            MessageBox.Show(error, _windowTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetRequestPending("正在等候 Revit 執行...");
        _applyAction?.Invoke(options);
    }

    private void BeginRefresh()
    {
        SetRequestPending("正在重新讀取目前視圖...");
        _refreshAction?.Invoke();
    }

    private void SetRequestPending(string status)
    {
        _applyButton.Enabled = false;
        _refreshButton.Enabled = false;
        _statusLabel.Text = status;
    }

    public void SetRequestCompleted(string? status = null, bool isError = false)
    {
        if (IsDisposed)
        {
            return;
        }

        _applyButton.Enabled = true;
        _refreshButton.Enabled = true;
        _statusLabel.ForeColor = isError ? Color.FromArgb(176, 45, 45) : Color.FromArgb(75, 88, 105);
        _statusLabel.Text = status ?? "完成。可繼續調整並再次套用。";
    }

    public void UpdateSources(
        IEnumerable<string> dimensionTypeNames,
        IEnumerable<GridSelectionItem> horizontalGrids,
        IEnumerable<GridSelectionItem> verticalGrids)
    {
        string? selectedType = _typeCombo.SelectedIndex > 0 ? _typeCombo.SelectedItem?.ToString() : null;
        _typeCombo.BeginUpdate();
        _typeCombo.Items.Clear();
        _typeCombo.Items.Add("<預設>");
        foreach (string name in dimensionTypeNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            _typeCombo.Items.Add(name);
        }

        int selectedIndex = selectedType is null ? 0 : _typeCombo.FindStringExact(selectedType);
        _typeCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        _typeCombo.EndUpdate();

        if (_horizontalGridList is not null)
        {
            PopulateGridList(_horizontalGridList, horizontalGrids, "目前視圖沒有可用的水平軸線");
        }

        if (_verticalGridList is not null)
        {
            PopulateGridList(_verticalGridList, verticalGrids, "目前視圖沒有可用的垂直軸線");
        }
    }

    private static void PopulateGridList(
        CheckedListBox list,
        IEnumerable<GridSelectionItem> items,
        string emptyMessage)
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (GridSelectionItem item in items)
        {
            list.Items.Add(new GridItem(item), true);
        }

        if (list.Items.Count == 0)
        {
            list.Items.Add(emptyMessage);
        }

        list.EndUpdate();
    }

    private TabPage BuildColumnTab()
    {
        var tab = new TabPage("柱") { Tag = DimensionMode.ColumnSetout };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            Text = "選擇標註放置側",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point)
        }, 0, 0);

        var directionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(0, 10, 0, 8),
            AutoSize = true
        };
        directionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        directionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        directionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        directionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        directionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        directionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _frontButton = CreateDirectionButton("前");
        _frontButton.Click += (_, _) =>
        {
            _selectedFrontBack = _selectedFrontBack == FrontBackSide.Front ? FrontBackSide.None : FrontBackSide.Front;
            RefreshDirectionButtons();
        };
        directionLayout.Controls.Add(_frontButton, 1, 0);

        _leftButton = CreateDirectionButton("左");
        _leftButton.Click += (_, _) =>
        {
            _selectedLeftRight = _selectedLeftRight == LeftRightSide.Left ? LeftRightSide.None : LeftRightSide.Left;
            RefreshDirectionButtons();
        };
        directionLayout.Controls.Add(_leftButton, 0, 1);

        var centerBox = new Panel
        {
            Width = 84,
            Height = 84,
            Margin = new Padding(8),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(245, 247, 250),
            Anchor = AnchorStyles.None
        };
        centerBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(10, 10, centerBox.Width - 20, centerBox.Height - 20);
            using var pen = new Pen(Color.FromArgb(110, 110, 110), 1.5f);
            e.Graphics.DrawRectangle(pen, rect);
            e.Graphics.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Bottom);
            e.Graphics.DrawLine(pen, rect.Right, rect.Top, rect.Left, rect.Bottom);
        };
        directionLayout.Controls.Add(centerBox, 1, 1);

        _rightButton = CreateDirectionButton("右");
        _rightButton.Click += (_, _) =>
        {
            _selectedLeftRight = _selectedLeftRight == LeftRightSide.Right ? LeftRightSide.None : LeftRightSide.Right;
            RefreshDirectionButtons();
        };
        directionLayout.Controls.Add(_rightButton, 2, 1);

        _backButton = CreateDirectionButton("後");
        _backButton.Click += (_, _) =>
        {
            _selectedFrontBack = _selectedFrontBack == FrontBackSide.Back ? FrontBackSide.None : FrontBackSide.Back;
            RefreshDirectionButtons();
        };
        directionLayout.Controls.Add(_backButton, 1, 2);
        panel.Controls.Add(directionLayout, 0, 1);

        var modePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        modePanel.Controls.Add(new Label { Text = "模式", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        _placementModeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _placementModeCombo.FlatStyle = FlatStyle.Flat;
        _placementModeCombo.Items.Add(new PlacementModeItem(PlacementMode.Auto, "自動"));
        _placementModeCombo.Items.Add(new PlacementModeItem(PlacementMode.Manual, "手動"));
        _placementModeCombo.SelectedIndex = 0;
        modePanel.Controls.Add(_placementModeCombo);
        panel.Controls.Add(modePanel, 0, 2);

        panel.Controls.Add(new Label
        {
            Text = "提示：可選擇一個水平側與/或一個垂直側；手動模式會再請你點選放置側。",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.FromArgb(80, 80, 80),
            Margin = new Padding(0, 10, 0, 0)
        }, 0, 3);

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        host.Controls.Add(panel);
        tab.Controls.Add(host);
        RefreshDirectionButtons();
        return tab;
    }

    private Button CreateDirectionButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Width = 124,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(40, 52, 70),
            Margin = new Padding(8),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(190, 202, 218);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 242, 255);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(214, 233, 255);
        return button;
    }

    private void RefreshDirectionButtons()
    {
        ApplyDirectionButtonState(_leftButton, _selectedLeftRight == LeftRightSide.Left);
        ApplyDirectionButtonState(_rightButton, _selectedLeftRight == LeftRightSide.Right);
        ApplyDirectionButtonState(_frontButton, _selectedFrontBack == FrontBackSide.Front);
        ApplyDirectionButtonState(_backButton, _selectedFrontBack == FrontBackSide.Back);
    }

    private static void ApplyDirectionButtonState(Button button, bool selected)
    {
        if (button is null)
        {
            return;
        }

        button.BackColor = selected ? Color.FromArgb(206, 236, 255) : Color.White;
        button.ForeColor = selected ? Color.FromArgb(17, 90, 150) : Color.FromArgb(32, 32, 32);
    }

    private TabPage BuildGridTab(IEnumerable<GridSelectionItem> horizontalGrids, IEnumerable<GridSelectionItem> verticalGrids)
    {
        var tab = new TabPage("軸線") { Tag = DimensionMode.BeamGrid };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 182));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 182));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var horizontalGroup = new GroupBox
        {
            Text = "水平軸線",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 24, 10, 8),
            ForeColor = Color.FromArgb(28, 48, 74),
            Margin = new Padding(0)
        };

        _horizontalGridList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            MinimumSize = new Size(0, 110),
            BorderStyle = BorderStyle.FixedSingle
        };
        _horizontalGridList.BackColor = Color.FromArgb(252, 253, 255);
        foreach (GridSelectionItem item in horizontalGrids)
        {
            _horizontalGridList.Items.Add(new GridItem(item), true);
        }

        if (_horizontalGridList.Items.Count == 0)
        {
            _horizontalGridList.Items.Add("目前視圖沒有可用的水平軸線");
        }

        horizontalGroup.Controls.Add(_horizontalGridList);
        panel.Controls.Add(horizontalGroup, 0, 0);

        var checkH = new CheckBox { Text = "全選水平軸線", AutoSize = true, Checked = true, Margin = new Padding(4, 6, 0, 4) };
        checkH.CheckedChanged += (_, _) => SetAllChecked(_horizontalGridList, checkH.Checked);
        panel.Controls.Add(checkH, 0, 1);

        var verticalGroup = new GroupBox
        {
            Text = "垂直軸線",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 24, 10, 8),
            ForeColor = Color.FromArgb(28, 48, 74),
            Margin = new Padding(0)
        };

        _verticalGridList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            MinimumSize = new Size(0, 110),
            BorderStyle = BorderStyle.FixedSingle
        };
        _verticalGridList.BackColor = Color.FromArgb(252, 253, 255);
        foreach (GridSelectionItem item in verticalGrids)
        {
            _verticalGridList.Items.Add(new GridItem(item), true);
        }

        if (_verticalGridList.Items.Count == 0)
        {
            _verticalGridList.Items.Add("目前視圖沒有可用的垂直軸線");
        }

        verticalGroup.Controls.Add(_verticalGridList);
        panel.Controls.Add(verticalGroup, 0, 3);

        var checkV = new CheckBox { Text = "全選垂直軸線", AutoSize = true, Checked = true, Margin = new Padding(4, 6, 0, 0) };
        checkV.CheckedChanged += (_, _) => SetAllChecked(_verticalGridList, checkV.Checked);
        panel.Controls.Add(checkV, 0, 4);

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        host.Controls.Add(panel);
        tab.Controls.Add(host);
        return tab;
    }

    private TabPage BuildBeamWidthTab()
    {
        var tab = new TabPage("梁") { Tag = DimensionMode.BeamWidth };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16),
            AutoSize = true
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = "建立梁寬與梁對梁間距標註。",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point)
        }, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = "間距標註會依梁方向分組；偏移量可控制標註線與梁軸的距離。",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.FromArgb(80, 80, 80),
            Margin = new Padding(0, 8, 0, 0)
        }, 0, 1);

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        host.Controls.Add(panel);
        tab.Controls.Add(host);
        return tab;
    }

    private static void SetAllChecked(CheckedListBox list, bool value)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is GridItem)
            {
                list.SetItemChecked(i, value);
            }
        }
    }

    private void ApplyModernTheme()
    {
        _tabControl.BackColor = Color.White;
        _tabControl.Appearance = TabAppearance.Normal;
    }

    private void TabControl_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (_tabControl.TabPages.Count <= e.Index)
        {
            return;
        }

        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Rectangle bounds = e.Bounds;
        Color fill = isSelected ? Color.FromArgb(42, 106, 188) : Color.FromArgb(232, 238, 247);
        Color text = isSelected ? Color.White : Color.FromArgb(44, 59, 80);

        using (var brush = new SolidBrush(fill))
        {
            e.Graphics.FillRectangle(brush, bounds);
        }

        TextRenderer.DrawText(
            e.Graphics,
            _tabControl.TabPages[e.Index].Text,
            Font,
            bounds,
            text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    public bool TryGetOptions(out DimensionOptions options, out string error)
    {
        options = new DimensionOptions
        {
            OffsetInternal = RevitDB.UnitUtils.ConvertToInternalUnits((double)_offsetNumeric.Value, RevitDB.UnitTypeId.Millimeters),
            DimensionTypeName = _typeCombo.SelectedIndex <= 0 ? null : _typeCombo.SelectedItem?.ToString()
        };
        error = string.Empty;

        DimensionMode selectedMode = _isModeLocked
            ? _initialMode
            : _tabControl.SelectedTab?.Tag is DimensionMode mode ? mode : DimensionMode.ColumnSetout;

        if (selectedMode == DimensionMode.ColumnSetout)
        {
            if (_placementModeCombo.SelectedItem is not PlacementModeItem placementModeItem)
            {
                error = "放置模式無效。";
                return false;
            }

            if (_selectedLeftRight == LeftRightSide.None && _selectedFrontBack == FrontBackSide.None)
            {
                error = "請至少選擇一個放置側。";
                return false;
            }

            options.ModeType = DimensionMode.ColumnSetout;
            options.Mode = placementModeItem.Mode;
            options.ColumnLeftRightSide = _selectedLeftRight;
            options.ColumnFrontBackSide = _selectedFrontBack;
            return true;
        }

        if (selectedMode == DimensionMode.BeamWidth)
        {
            options.ModeType = DimensionMode.BeamWidth;
            return true;
        }

        var horizontal = new List<RevitDB.ElementId>();
        foreach (object selected in _horizontalGridList.CheckedItems)
        {
            if (selected is GridItem item)
            {
                horizontal.Add(item.Item.Id);
            }
        }

        var vertical = new List<RevitDB.ElementId>();
        foreach (object selected in _verticalGridList.CheckedItems)
        {
            if (selected is GridItem item)
            {
                vertical.Add(item.Item.Id);
            }
        }

        if (horizontal.Count < 2 && vertical.Count < 2)
        {
            error = "軸線標註至少需要同方向兩條可見軸線。";
            return false;
        }

        options.ModeType = DimensionMode.BeamGrid;
        options.SelectedHorizontalGridIds = horizontal;
        options.SelectedVerticalGridIds = vertical;
        return true;
    }
}
}
