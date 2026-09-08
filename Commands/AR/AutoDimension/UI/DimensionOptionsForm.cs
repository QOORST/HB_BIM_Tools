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
    private readonly IReadOnlyDictionary<DimensionMode, AutoDimensionSavedSettings> _savedSettings;
    private readonly HashSet<DimensionMode> _appliedSavedModes = new HashSet<DimensionMode>();
    private const bool UseDarkTheme = true;
    private const string UiFontFamily = "Microsoft JhengHei UI";

    private TabControl _tabControl = null!;
    private TabControl _modeTabControl = null!;
    private ComboBox _placementModeCombo = null!;
    private ComboBox _typeCombo = null!;
    private NumericUpDown _offsetNumeric = null!;
    private NumericUpDown _gridPrimaryOffsetNumeric = null!;
    private NumericUpDown _gridOverallOffsetNumeric = null!;
    private CheckedListBox _horizontalGridList = null!;
    private CheckedListBox _verticalGridList = null!;
    private Button _leftButton = null!;
    private Button _rightButton = null!;
    private Button _frontButton = null!;
    private Button _backButton = null!;
    private Button _applyButton = null!;
    private Button _refreshButton = null!;
    private Label _statusLabel = null!;
    private readonly Dictionary<Button, PlacementDirection> _gridDirectionButtons = new Dictionary<Button, PlacementDirection>();
    private Button? _selectedGridDirectionButton;

    private LeftRightSide _selectedLeftRight = LeftRightSide.None;
    private FrontBackSide _selectedFrontBack = FrontBackSide.Front;
    private PlacementDirection _selectedGridDirection = PlacementDirection.NorthEast;

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
        IReadOnlyDictionary<DimensionMode, AutoDimensionSavedSettings>? savedSettings = null,
        Action<DimensionOptions>? applyAction = null,
        Action? refreshAction = null)
    {
        _isModeLocked = lockMode;
        _initialMode = initialMode;
        _windowTitle = windowTitle ?? "HB_BIM 自動標註";
        _savedSettings = savedSettings ?? new Dictionary<DimensionMode, AutoDimensionSavedSettings>();
        _applyAction = applyAction;
        _refreshAction = refreshAction;

        Text = _windowTitle;
        Font = new Font(UiFontFamily, 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(39, 39, 39);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = lockMode ? new Size(980, 560) : new Size(1120, 760);
        MinimumSize = lockMode ? new Size(720, 500) : new Size(820, 600);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Shown += (_, _) => FitToCurrentScreen();
        DpiChanged += (_, _) => BeginInvoke((Action)FitToCurrentScreen);

        if (lockMode)
        {
            var lockedRoot = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(20, 18, 20, 14),
                BackColor = Color.FromArgb(39, 39, 39),
                AutoScroll = true
            };
            lockedRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lockedRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lockedRoot.Controls.Add(BuildLockedSettingsContent(dimensionTypeNames, horizontalGrids, verticalGrids), 0, 0);
            lockedRoot.Controls.Add(BuildButtonPanel(), 0, 1);
            Controls.Add(lockedRoot);
            ApplyModernTheme();
            ApplySavedSettingsForMode(_initialMode);
            return;
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(39, 39, 39),
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildUnifiedSettingsContent(dimensionTypeNames, horizontalGrids, verticalGrids), 0, 1);
        root.Controls.Add(BuildButtonPanel(), 0, 2);
        Controls.Add(root);

        ApplyModernTheme();
        ApplySavedSettingsForMode(_initialMode);
    }

    private void FitToCurrentScreen()
    {
        if (!IsHandleCreated || WindowState == FormWindowState.Maximized)
        {
            return;
        }

        Rectangle workingArea = Screen.FromHandle(Handle).WorkingArea;
        int margin = Math.Max(12, (int)Math.Round(12D * DeviceDpi / 96D));
        int maxWidth = Math.Max(640, workingArea.Width - margin * 2);
        int maxHeight = Math.Max(480, workingArea.Height - margin * 2);

        MaximumSize = new Size(maxWidth, maxHeight);
        MinimumSize = new Size(
            Math.Min(MinimumSize.Width, maxWidth),
            Math.Min(MinimumSize.Height, maxHeight));
        Size = new Size(Math.Min(Width, maxWidth), Math.Min(Height, maxHeight));
        Location = new Point(
            workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
    }

    private Control BuildUnifiedSettingsContent(
        IEnumerable<string> dimensionTypeNames,
        IEnumerable<GridSelectionItem> horizontalGrids,
        IEnumerable<GridSelectionItem> verticalGrids)
    {
        _tabControl = CreateDarkTabControl();
        _tabControl.Dock = DockStyle.Fill;
        _tabControl.Margin = new Padding(0, 14, 0, 8);
        _tabControl.ItemSize = new Size(126, 34);

        var dimensionsTab = new TabPage("標註設定") { BackColor = Color.FromArgb(39, 39, 39) };
        var dimensionsRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 18, 18, 14),
            BackColor = Color.FromArgb(39, 39, 39)
        };
        dimensionsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dimensionsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dimensionsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        dimensionsRoot.Controls.Add(BuildTypePanel(dimensionTypeNames), 0, 0);
        dimensionsRoot.Controls.Add(BuildModeContent(horizontalGrids, verticalGrids), 0, 1);
        dimensionsRoot.Controls.Add(CreateModeHint(), 0, 2);
        dimensionsTab.Controls.Add(dimensionsRoot);

        var offsetsTab = new TabPage("距離設定") { BackColor = Color.FromArgb(39, 39, 39) };
        var offsetsRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(18, 18, 18, 14),
            BackColor = Color.FromArgb(39, 39, 39)
        };
        offsetsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        offsetsRoot.Controls.Add(BuildOffsetPanel(), 0, 0);
        offsetsTab.Controls.Add(offsetsRoot);

        _tabControl.TabPages.Add(dimensionsTab);
        _tabControl.TabPages.Add(offsetsTab);
        return _tabControl;
    }

    private Control BuildModeContent(IEnumerable<GridSelectionItem> horizontalGrids, IEnumerable<GridSelectionItem> verticalGrids)
    {
        _modeTabControl = new ThemedTabControl
        {
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(112, 34),
            Padding = new Point(14, 6)
        };
        _modeTabControl.DrawItem += TabControl_DrawItem;
        _modeTabControl.SelectedIndexChanged += (_, _) =>
        {
            if (_modeTabControl.SelectedTab?.Tag is DimensionMode selectedMode)
            {
                ApplySavedSettingsForMode(selectedMode);
            }
        };

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

        _modeTabControl.Dock = DockStyle.Fill;
        _modeTabControl.Margin = new Padding(0, 10, 0, 0);
        _modeTabControl.TabPages.Add(BuildColumnTab());
        _modeTabControl.TabPages.Add(BuildGridTab(horizontalGrids, verticalGrids));
        _modeTabControl.TabPages.Add(BuildBeamWidthTab());
        SelectInitialTab();
        return _modeTabControl;
    }

    private void SelectInitialTab()
    {
        if (_modeTabControl is null)
        {
            return;
        }

        for (int i = 0; i < _modeTabControl.TabPages.Count; i++)
        {
            if (_modeTabControl.TabPages[i].Tag is DimensionMode mode && mode == _initialMode)
            {
                _modeTabControl.SelectedIndex = i;
                return;
            }
        }
    }

    private static Control CreateModeHint()
    {
        return new Label
        {
            Text = "標註放置方式可依標註項目切換；偏移距離請至「距離設定」頁籤調整。",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(205, 205, 205),
            BackColor = Color.FromArgb(39, 39, 39),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Padding = new Padding(6, 0, 0, 0)
        };
    }

    private static Control CreateOffsetGuidePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(39, 39, 39),
            Padding = new Padding(8)
        };

        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.FromArgb(39, 39, 39));

            Rectangle area = panel.ClientRectangle;
            area.Inflate(-34, -28);
            if (area.Width < 220 || area.Height < 140)
            {
                return;
            }

            using var solidPen = new Pen(Color.FromArgb(205, 205, 205), 2f);
            using var dashPen = new Pen(Color.FromArgb(150, 150, 150), 1.5f) { DashStyle = DashStyle.Dash };
            using var accentPen = new Pen(Color.FromArgb(45, 132, 247), 3f);
            using var textBrush = new SolidBrush(Color.FromArgb(225, 225, 225));
            using var dimBrush = new SolidBrush(Color.FromArgb(170, 170, 170));
            using var font = new Font(UiFontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);

            int gridTop = area.Top + 24;
            int gridBottom = area.Bottom - 36;
            int left = area.Left + 28;
            int mid = area.Left + area.Width / 2;
            int right = area.Right - 28;

            e.Graphics.DrawLine(dashPen, left, gridTop, left, gridBottom);
            e.Graphics.DrawLine(dashPen, mid, gridTop, mid, gridBottom);
            e.Graphics.DrawLine(dashPen, right, gridTop, right, gridBottom);

            e.Graphics.DrawLine(solidPen, left, gridBottom - 34, right, gridBottom - 34);
            e.Graphics.DrawLine(solidPen, mid, gridTop + 44, mid, gridBottom - 34);

            e.Graphics.DrawLine(accentPen, left, area.Bottom - 16, right, area.Bottom - 16);
            e.Graphics.DrawString("Offset between grids and dimensions", font, textBrush, area.Left, area.Top);
            e.Graphics.DrawString("1000 mm", font, dimBrush, right - 62, area.Bottom - 40);

            DrawGridBubble(e.Graphics, "X1", left, gridTop, font, textBrush);
            DrawGridBubble(e.Graphics, "X2", mid, gridTop, font, textBrush);
            DrawGridBubble(e.Graphics, "X3", right, gridTop, font, textBrush);
        };

        return panel;
    }

    private static void DrawGridBubble(Graphics graphics, string text, int x, int y, Font font, Brush brush)
    {
        var rect = new Rectangle(x - 17, y - 17, 34, 34);
        using var pen = new Pen(Color.FromArgb(220, 220, 220), 1.5f);
        graphics.DrawEllipse(pen, rect);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            rect,
            Color.FromArgb(225, 225, 225),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        graphics.DrawLine(pen, x, y + 17, x, y + 54);
    }

    private Control BuildLockedSettingsContent(
        IEnumerable<string> dimensionTypeNames,
        IEnumerable<GridSelectionItem> horizontalGrids,
        IEnumerable<GridSelectionItem> verticalGrids)
    {
        _tabControl = CreateDarkTabControl();
        _tabControl.Dock = DockStyle.Fill;

        var dimensionsTab = new TabPage("標註設定") { BackColor = Color.FromArgb(39, 39, 39) };
        var dimensionsRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18, 18, 18, 14),
            BackColor = Color.FromArgb(39, 39, 39)
        };
        dimensionsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dimensionsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dimensionsRoot.Controls.Add(BuildLockedModePanel(horizontalGrids, verticalGrids), 0, 0);
        dimensionsRoot.Controls.Add(BuildTypePanel(dimensionTypeNames), 0, 1);
        dimensionsTab.Controls.Add(dimensionsRoot);

        var offsetsTab = new TabPage("距離設定") { BackColor = Color.FromArgb(39, 39, 39) };
        var offsetsRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(18, 18, 18, 14),
            BackColor = Color.FromArgb(39, 39, 39)
        };
        offsetsRoot.Controls.Add(BuildOffsetPanel(), 0, 0);
        offsetsTab.Controls.Add(offsetsRoot);

        _tabControl.TabPages.Add(dimensionsTab);
        _tabControl.TabPages.Add(offsetsTab);
        return _tabControl;
    }

    private Control BuildLockedModePanel(IEnumerable<GridSelectionItem> horizontalGrids, IEnumerable<GridSelectionItem> verticalGrids)
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
            BackColor = Color.FromArgb(39, 39, 39),
            AutoScroll = true
        };

        while (tab.Controls.Count > 0)
        {
            Control child = tab.Controls[0];
            tab.Controls.Remove(child);
            child.Dock = DockStyle.Fill;
            ApplyDarkTheme(child);
            host.Controls.Add(child);
        }

        return host;
    }

    private sealed class ThemedTabControl : TabControl
    {
        public ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White);
            for (int i = 0; i < TabCount; i++)
                OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, GetTabRect(i), i,
                    i == SelectedIndex ? DrawItemState.Selected : DrawItemState.None));
        }
    }

    private TabControl CreateDarkTabControl()
    {
        var control = new ThemedTabControl
        {
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(118, 34),
            Padding = new Point(14, 6)
        };
        control.DrawItem += TabControl_DrawItem;
        return control;
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(37, 54, 78),
            Padding = new Padding(22, 12, 22, 12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = _windowTitle,
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font(UiFontFamily, 14F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 4)
        };

        var subtitle = new Label
        {
            Text = "依可見構件建立標註，可設定偏移、標註型式與放置方向。",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = Color.FromArgb(221, 231, 241),
            Font = new Font(UiFontFamily, 9F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildOffsetPanel()
    {
        _offsetNumeric = CreateOffsetNumeric(1000);
        _gridPrimaryOffsetNumeric = CreateOffsetNumeric(1000);
        _gridOverallOffsetNumeric = CreateOffsetNumeric(1000);

        var board = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 500,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(22),
            BackColor = UseDarkTheme ? Color.FromArgb(34, 34, 34) : Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            MinimumSize = new Size(0, 500)
        };
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        board.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        board.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

        Control levelOffsetPanel = BuildLevelOffsetPanel();
        Control gridOffsetPanel = BuildGridOffsetPanel();
        board.Controls.Add(levelOffsetPanel, 0, 0);
        board.Controls.Add(gridOffsetPanel, 1, 0);

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        host.Controls.Add(board);

        Action updateResponsiveLayout = () =>
        {
            int stackingThreshold = (int)Math.Round(900D * board.DeviceDpi / 96D);
            bool stackPanels = host.ClientSize.Width > 0 && host.ClientSize.Width < stackingThreshold;
            int minimumHeight = stackPanels ? 920 : 500;

            board.SuspendLayout();
            board.ColumnStyles[0].Width = stackPanels ? 100 : 50;
            board.ColumnStyles[1].Width = stackPanels ? 0 : 50;
            board.RowStyles[0].SizeType = stackPanels ? SizeType.Absolute : SizeType.Percent;
            board.RowStyles[0].Height = stackPanels ? 460 : 100;
            board.RowStyles[1].SizeType = SizeType.Absolute;
            board.RowStyles[1].Height = stackPanels ? 460 : 0;
            board.SetCellPosition(levelOffsetPanel, new TableLayoutPanelCellPosition(0, 0));
            board.SetCellPosition(gridOffsetPanel, new TableLayoutPanelCellPosition(stackPanels ? 0 : 1, stackPanels ? 1 : 0));
            board.MinimumSize = new Size(0, minimumHeight);
            board.Height = Math.Max(minimumHeight, host.ClientSize.Height);
            board.ResumeLayout(true);
        };
        host.Resize += (_, _) => updateResponsiveLayout();
        updateResponsiveLayout();
        return host;
    }

    private static Label CreateOffsetUnitLabel()
    {
        return new Label
        {
            Text = "mm",
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            BackColor = UseDarkTheme ? Color.FromArgb(34, 34, 34) : Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(UiFontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point),
            AutoEllipsis = false
        };
    }

    private static void DrawOffsetSectionBackground(Graphics graphics, Rectangle area)
    {
        Rectangle fill = area;
        fill.Inflate(-2, -2);
        using var brush = new SolidBrush(UseDarkTheme ? Color.FromArgb(38, 38, 38) : Color.FromArgb(250, 252, 255));
        using var borderPen = new Pen(UseDarkTheme ? Color.FromArgb(76, 76, 76) : Color.FromArgb(202, 214, 228), 1f);
        graphics.FillRectangle(brush, fill);
        graphics.DrawRectangle(borderPen, fill);
    }

    private static void DrawLevelOffsetExample(Graphics graphics, Rectangle area)
    {
        using var mainPen = new Pen(Color.FromArgb(220, 220, 220), 2f);
        using var dashPen = new Pen(Color.FromArgb(170, 170, 170), 1.4f) { DashStyle = DashStyle.Dot };
        using var accentPen = new Pen(Color.FromArgb(45, 132, 247), 2.4f);
        using var font = new Font(UiFontFamily, 11F, FontStyle.Bold, GraphicsUnit.Point);
        using var valueFont = new Font(UiFontFamily, 12F, FontStyle.Regular, GraphicsUnit.Point);

        DrawText(graphics, "樓層與標註偏移", area.Left + 18, area.Top + 16, font, ContentAlignment.MiddleLeft);

        int left = area.Left + 36;
        int mid = area.Left + 180;
        int right = area.Right - 82;
        int topY = area.Top + 108;
        int midY = area.Top + 180;
        int lowY = area.Top + 252;
        int dimY = area.Bottom - 84;

        graphics.DrawLine(mainPen, left, topY, right, topY);
        graphics.DrawLine(mainPen, left, midY, right, midY);
        graphics.DrawLine(mainPen, left, lowY, right, lowY);
        graphics.DrawLine(mainPen, mid, topY, mid, lowY);
        graphics.DrawLine(dashPen, left, lowY, left, dimY);
        graphics.DrawLine(dashPen, mid, lowY, mid, dimY);
        graphics.DrawLine(dashPen, right - 54, lowY, right - 54, dimY);
        DrawDimensionLine(graphics, left, dimY, right - 54, dimY, accentPen);

        DrawVerticalText(graphics, "4000", left + 12, topY + 28, valueFont);
        DrawVerticalText(graphics, "4000", left + 12, midY + 28, valueFont);
        DrawVerticalText(graphics, "8000", mid + 10, topY + 64, valueFont);
        DrawText(graphics, "4F", right - 116, topY - 32, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "12100", right - 116, topY + 6, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "3F", right - 116, midY - 32, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "8100", right - 116, midY + 6, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "2F", right - 116, lowY - 32, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "4100", right - 116, lowY + 6, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "1000", left + 18, dimY + 14, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "2500", mid + 34, dimY + 14, valueFont, ContentAlignment.MiddleLeft);

        DrawLevelMarker(graphics, right, topY - 16, mainPen);
        DrawLevelMarker(graphics, right, midY - 16, mainPen);
        DrawLevelMarker(graphics, right, lowY - 16, mainPen);
    }

    private static void DrawGridOffsetExample(Graphics graphics, Rectangle area)
    {
        using var mainPen = new Pen(Color.FromArgb(220, 220, 220), 2f);
        using var dashPen = new Pen(Color.FromArgb(170, 170, 170), 1.4f) { DashStyle = DashStyle.Dot };
        using var accentPen = new Pen(Color.FromArgb(45, 132, 247), 2.4f);
        using var font = new Font(UiFontFamily, 11F, FontStyle.Bold, GraphicsUnit.Point);
        using var valueFont = new Font(UiFontFamily, 12F, FontStyle.Regular, GraphicsUnit.Point);

        DrawText(graphics, "軸線與標註偏移", area.Left + 18, area.Top + 16, font, ContentAlignment.MiddleLeft);

        int x1 = area.Left + 56;
        int x2 = area.Left + 160;
        int x3 = area.Left + 264;
        int headY = area.Top + 124;
        int beamTopY = area.Top + 210;
        int beamBottomY = area.Top + 270;
        int dashRight = area.Right - 170;
        int firstY = area.Top + 198;
        int secondY = area.Top + 264;
        int dimX = dashRight - 36;

        DrawGridBubble(graphics, "X1", x1, headY, valueFont, Brushes.White);
        DrawGridBubble(graphics, "X2", x2, headY, valueFont, Brushes.White);
        DrawGridBubble(graphics, "X3", x3, headY, valueFont, Brushes.White);
        graphics.DrawLine(mainPen, x1, headY + 18, x1, headY + 54);
        graphics.DrawLine(mainPen, x2, headY + 18, x2, headY + 54);
        graphics.DrawLine(mainPen, x3, headY + 18, x3, headY + 54);

        graphics.DrawRectangle(mainPen, x1, beamTopY, x3 - x1, beamBottomY - beamTopY);
        graphics.DrawLine(mainPen, x1, beamTopY + 34, x3, beamTopY + 34);
        graphics.DrawLine(mainPen, x2, beamTopY + 34, x2, beamBottomY + 34);
        graphics.DrawLine(mainPen, x1, beamBottomY, x1, beamBottomY + 46);
        graphics.DrawLine(mainPen, x3, beamBottomY, x3, beamBottomY + 46);
        DrawText(graphics, "3000", x2 - 24, beamTopY - 28, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "1500", x1 + 28, beamTopY + 32, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "1500", x2 + 28, beamTopY + 32, valueFont, ContentAlignment.MiddleLeft);

        graphics.DrawLine(dashPen, x3, headY, dashRight, headY);
        graphics.DrawLine(dashPen, x3, firstY, dashRight, firstY);
        graphics.DrawLine(dashPen, x3, secondY, dashRight, secondY);
        DrawOffsetDimension(graphics, dimX, headY, firstY, accentPen);
        DrawOffsetDimension(graphics, dimX + 18, firstY, secondY, accentPen);
        DrawText(graphics, "+ 內縮", area.Left + 18, area.Bottom - 34, valueFont, ContentAlignment.MiddleLeft);
        DrawText(graphics, "- 外移", area.Left + 116, area.Bottom - 34, valueFont, ContentAlignment.MiddleLeft);
    }

    private static void DrawDimensionLine(Graphics graphics, int x1, int y1, int x2, int y2, Pen pen)
    {
        graphics.DrawLine(pen, x1, y1, x2, y2);
        graphics.DrawLine(pen, x1, y1, x1 + 8, y1 - 8);
        graphics.DrawLine(pen, x1, y1, x1 + 8, y1 + 8);
        graphics.DrawLine(pen, x2, y2, x2 - 8, y2 - 8);
        graphics.DrawLine(pen, x2, y2, x2 - 8, y2 + 8);
    }

    private static void DrawOffsetDimension(Graphics graphics, int x, int y1, int y2, Pen pen)
    {
        graphics.DrawLine(pen, x, y1, x, y2);
        graphics.DrawLine(pen, x - 7, y1 + 10, x, y1);
        graphics.DrawLine(pen, x + 7, y1 + 10, x, y1);
        graphics.DrawLine(pen, x - 7, y2 - 10, x, y2);
        graphics.DrawLine(pen, x + 7, y2 - 10, x, y2);
    }

    private static void DrawVerticalText(Graphics graphics, string text, int x, int y, Font font)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.RotateTransform(90);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            new Rectangle(0, 0, 72, 28),
            UseDarkTheme ? Color.FromArgb(225, 225, 225) : Color.FromArgb(45, 45, 45),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        graphics.Restore(state);
    }

    private Control BuildLevelOffsetPanel()
    {
        var container = CreateOffsetDiagramContainer("一般標註距離", "柱、梁與第一道柱線標註使用此距離。");
        var diagram = CreateLevelOffsetDiagram();
        container.Controls.Add(diagram, 0, 1);
        var inputs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        inputs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inputs.Controls.Add(CreateOffsetInputRow("第一道距離", _offsetNumeric), 0, 0);
        container.Controls.Add(inputs, 0, 2);
        return container;
    }

    private Control BuildGridOffsetPanel()
    {
        var container = CreateOffsetDiagramContainer("柱線/軸線距離", "以軸線標頭為基準；正值往內縮，負值往外。");
        var diagram = CreateGridOffsetDiagram();
        container.Controls.Add(diagram, 0, 1);

        var inputStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White,
            Margin = new Padding(0)
        };
        inputStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        inputStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        inputStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        inputStack.Controls.Add(CreateOffsetInputRow("第一道", _gridPrimaryOffsetNumeric), 0, 0);
        inputStack.Controls.Add(CreateOffsetInputRow("第二道間距", _gridOverallOffsetNumeric), 0, 1);
        inputStack.Controls.Add(new Label
        {
            Text = "+ 內縮 / - 外移",
            Dock = DockStyle.Fill,
            ForeColor = UseDarkTheme ? Color.FromArgb(205, 205, 205) : Color.FromArgb(80, 80, 80),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 2);
        container.Controls.Add(inputStack, 0, 2);
        return container;
    }

    private static TableLayoutPanel CreateOffsetDiagramContainer(string title, string subtitle)
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(6, 0, 6, 0),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White
        };
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font(UiFontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            ForeColor = UseDarkTheme ? Color.FromArgb(205, 205, 205) : Color.FromArgb(80, 80, 80),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 1);
        container.Controls.Add(header, 0, 0);
        return container;
    }

    private static Control CreateLevelOffsetDiagram()
    {
        var panel = CreateDiagramPanel();
        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White);

            Rectangle area = panel.ClientRectangle;
            area.Inflate(-26, -18);
            if (area.Width < 220 || area.Height < 130)
            {
                return;
            }

            using var mainPen = new Pen(Color.FromArgb(210, 210, 210), 2f);
            using var thinPen = new Pen(Color.FromArgb(160, 160, 160), 1.5f);
            using var dashPen = new Pen(Color.FromArgb(150, 150, 150), 1.4f) { DashStyle = DashStyle.Dash };
            using var accentPen = new Pen(Color.FromArgb(45, 132, 247), 3f);
            using var font = new Font(UiFontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);

            int left = area.Left + 24;
            int right = area.Right - 42;
            int levelX = area.Right - 72;
            int topY = area.Top + 30;
            int midY = area.Top + area.Height / 2;
            int lowY = area.Bottom - 38;
            int dimY = area.Bottom - 12;

            e.Graphics.DrawLine(mainPen, left, topY, right, topY);
            e.Graphics.DrawLine(mainPen, left, midY, right, midY);
            e.Graphics.DrawLine(mainPen, left, lowY, right, lowY);
            e.Graphics.DrawLine(thinPen, levelX, topY, levelX, lowY);
            e.Graphics.DrawLine(dashPen, left, topY, left, dimY);
            e.Graphics.DrawLine(dashPen, levelX, lowY, levelX, dimY);
            e.Graphics.DrawLine(accentPen, left, dimY, levelX, dimY);

            DrawLevelMarker(e.Graphics, right, topY, mainPen);
            DrawLevelMarker(e.Graphics, right, midY, mainPen);
            DrawLevelMarker(e.Graphics, right, lowY, mainPen);
            DrawText(e.Graphics, "4F", levelX + 10, topY - 24, font, ContentAlignment.MiddleLeft);
            DrawText(e.Graphics, "3F", levelX + 10, midY - 24, font, ContentAlignment.MiddleLeft);
            DrawText(e.Graphics, "2F", levelX + 10, lowY - 24, font, ContentAlignment.MiddleLeft);
            DrawText(e.Graphics, "第一道", left + 8, dimY - 24, font, ContentAlignment.MiddleLeft);
        };
        return panel;
    }

    private static Control CreateGridOffsetDiagram()
    {
        var panel = CreateDiagramPanel();
        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White);

            Rectangle area = panel.ClientRectangle;
            area.Inflate(-28, -20);
            if (area.Width < 240 || area.Height < 140)
            {
                return;
            }

            using var mainPen = new Pen(Color.FromArgb(210, 210, 210), 2f);
            using var dashPen = new Pen(Color.FromArgb(150, 150, 150), 1.4f) { DashStyle = DashStyle.Dash };
            using var accentPen = new Pen(Color.FromArgb(45, 132, 247), 3f);
            using var font = new Font(UiFontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);

            int baseY = area.Bottom - 58;
            int headY = area.Top + 44;
            int firstY = headY + 64;
            int overallY = headY + 102;
            int x1 = area.Left + 42;
            int x2 = area.Left + (area.Width - 108) / 2;
            int x3 = area.Right - 150;

            e.Graphics.DrawLine(mainPen, x1, baseY, x3, baseY);
            e.Graphics.DrawLine(mainPen, x2, headY + 18, x2, baseY);
            e.Graphics.DrawLine(mainPen, x1, headY + 18, x1, baseY + 22);
            e.Graphics.DrawLine(mainPen, x3, headY + 18, x3, baseY + 22);
            e.Graphics.DrawLine(dashPen, x3, headY, x3 + 86, headY);
            e.Graphics.DrawLine(dashPen, x3, firstY, x3 + 86, firstY);
            e.Graphics.DrawLine(accentPen, x3, overallY, x3 + 86, overallY);
            e.Graphics.DrawLine(mainPen, x3 + 52, headY, x3 + 52, firstY);
            e.Graphics.DrawLine(mainPen, x3 + 70, firstY, x3 + 70, overallY);

            DrawGridBubble(e.Graphics, "X1", x1, headY, font, Brushes.White);
            DrawGridBubble(e.Graphics, "X2", x2, headY, font, Brushes.White);
            DrawGridBubble(e.Graphics, "X3", x3, headY, font, Brushes.White);
            DrawText(e.Graphics, "3000", x2 - 20, baseY - 28, font, ContentAlignment.MiddleCenter);
            DrawText(e.Graphics, "第一道", x3 + 92, firstY - 12, font, ContentAlignment.MiddleLeft);
            DrawText(e.Graphics, "外框", x3 + 92, overallY - 12, font, ContentAlignment.MiddleLeft);
        };
        return panel;
    }

    private static Panel CreateDiagramPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White,
            Margin = new Padding(0, 4, 0, 8),
            MinimumSize = new Size(0, 200)
        };
    }

    private static Control CreateOffsetInputRow(string label, NumericUpDown numeric)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        row.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        row.Controls.Add(numeric, 1, 0);
        row.Controls.Add(new Label
        {
            Text = "mm",
            Dock = DockStyle.Fill,
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            TextAlign = ContentAlignment.MiddleLeft
        }, 2, 0);
        return row;
    }

    private static Control CreateReadOnlyOffsetRow(string label, string value)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        row.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(205, 205, 205),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 1, 0);
        return row;
    }

    private static void DrawLevelMarker(Graphics graphics, int x, int y, Pen pen)
    {
        Point[] marker =
        {
            new Point(x - 28, y - 14),
            new Point(x + 18, y - 14),
            new Point(x - 5, y + 12)
        };
        graphics.DrawPolygon(pen, marker);
    }

    private static void DrawText(Graphics graphics, string text, int x, int y, Font font, ContentAlignment alignment)
    {
        Size size = TextRenderer.MeasureText(text, font);
        int left = alignment == ContentAlignment.MiddleCenter ? x - size.Width / 2 : x;
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            new Rectangle(left, y, size.Width + 8, size.Height + 4),
            UseDarkTheme ? Color.FromArgb(225, 225, 225) : Color.FromArgb(45, 45, 45),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private static NumericUpDown CreateOffsetNumeric(decimal value)
    {
        var numeric = new NumericUpDown
        {
            Minimum = -6000,
            Maximum = 6000,
            Increment = 100,
            Value = value,
            DecimalPlaces = 0,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = 30,
            TextAlign = HorizontalAlignment.Right,
            BackColor = UseDarkTheme ? Color.FromArgb(74, 74, 74) : Color.White,
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32)
        };
        numeric.BorderStyle = BorderStyle.FixedSingle;
        return numeric;
    }

    private Control BuildTypePanel(IEnumerable<string> dimensionTypeNames)
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Text = "標註型式",
            Padding = new Padding(14, 22, 14, 10),
            Margin = new Padding(0, 4, 0, 4),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(28, 48, 74),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 92)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 42),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White
        };
        layout.RowStyles.Clear();
        layout.ColumnStyles.Clear();
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = "型式",
            Font = new Font(UiFontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White,
            Margin = new Padding(6, 0, 8, 0),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(label, 0, 0);

        _typeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = UseDarkTheme ? Color.FromArgb(74, 74, 74) : Color.White,
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            Margin = new Padding(0, 5, 0, 5),
            Dock = DockStyle.Fill
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
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 8, 0, 2),
            WrapContents = true,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent
        };

        _applyButton = new Button
        {
            Text = _applyAction is null ? "確定" : "執行標註",
            Width = 118,
            Height = 34,
            Font = new Font(UiFontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point),
            FlatStyle = FlatStyle.Flat,
            BackColor = UseDarkTheme ? Color.FromArgb(45, 132, 247) : Color.FromArgb(36, 99, 176),
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
            AcceptButton = _applyButton;
        }

        var close = new Button
        {
            Text = _applyAction is null ? "取消" : "關閉",
            Width = 108,
            Height = 34,
            Font = new Font(UiFontFamily, 10F, FontStyle.Regular, GraphicsUnit.Point),
            FlatStyle = FlatStyle.Flat,
            BackColor = UseDarkTheme ? Color.FromArgb(64, 64, 64) : Color.White,
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(48, 60, 78)
        };
        close.FlatAppearance.BorderColor = UseDarkTheme ? Color.FromArgb(130, 130, 130) : Color.FromArgb(174, 186, 202);
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
            Font = new Font(UiFontFamily, 10F, FontStyle.Regular, GraphicsUnit.Point),
            FlatStyle = FlatStyle.Flat,
            BackColor = UseDarkTheme ? Color.FromArgb(64, 64, 64) : Color.White,
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(48, 60, 78),
            Visible = _refreshAction is not null
        };
        _refreshButton.FlatAppearance.BorderColor = UseDarkTheme ? Color.FromArgb(130, 130, 130) : Color.FromArgb(174, 186, 202);
        _refreshButton.Click += (_, _) => BeginRefresh();

        _statusLabel = new Label
        {
            Text = _applyAction is null ? string.Empty : "可操作 Revit；調整設定後按「執行標註」。",
            AutoSize = true,
            ForeColor = UseDarkTheme ? Color.FromArgb(205, 205, 205) : Color.FromArgb(75, 88, 105),
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
        _statusLabel.ForeColor = UseDarkTheme ? Color.FromArgb(205, 205, 205) : Color.FromArgb(75, 88, 105);
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
        _statusLabel.ForeColor = isError
            ? Color.FromArgb(255, 130, 130)
            : Color.FromArgb(76, 175, 80);
        _statusLabel.Text = status ?? "完成。可繼續調整並再次執行。";
    }

    public void UpdateSources(
        IEnumerable<string> dimensionTypeNames,
        IEnumerable<GridSelectionItem> horizontalGrids,
        IEnumerable<GridSelectionItem> verticalGrids)
    {
        string? selectedType = _typeCombo.SelectedIndex > 0 ? _typeCombo.SelectedItem?.ToString() : null;
        HashSet<int> selectedHorizontalIds = GetCheckedGridIds(_horizontalGridList);
        HashSet<int> selectedVerticalIds = GetCheckedGridIds(_verticalGridList);

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
            ApplySavedGridSelections(_horizontalGridList, selectedHorizontalIds);
        }

        if (_verticalGridList is not null)
        {
            PopulateGridList(_verticalGridList, verticalGrids, "目前視圖沒有可用的垂直軸線");
            ApplySavedGridSelections(_verticalGridList, selectedVerticalIds);
        }
    }

    public void SelectMode(DimensionMode mode)
    {
        if (_modeTabControl is null || _isModeLocked)
        {
            return;
        }

        if (_tabControl is not null && _tabControl.TabPages.Count > 0)
        {
            _tabControl.SelectedIndex = 0;
        }

        for (int i = 0; i < _modeTabControl.TabPages.Count; i++)
        {
            if (_modeTabControl.TabPages[i].Tag is DimensionMode tabMode && tabMode == mode)
            {
                _modeTabControl.SelectedIndex = i;
                ApplySavedSettingsForMode(mode);
                return;
            }
        }
    }

    private void ApplySavedSettingsForMode(DimensionMode mode)
    {
        if (_appliedSavedModes.Contains(mode) ||
            !_savedSettings.TryGetValue(mode, out AutoDimensionSavedSettings? settings))
        {
            return;
        }

        ApplySavedSettings(settings);
        _appliedSavedModes.Add(mode);
        if (_statusLabel is not null && !IsDisposed)
        {
            _statusLabel.Text = "已載入此專案上次使用的設定。";
        }
    }

    private void ApplySavedSettings(AutoDimensionSavedSettings settings)
    {
        SetNumericValue(_offsetNumeric, settings.OffsetMm);
        SetNumericValue(_gridPrimaryOffsetNumeric, settings.GridPrimaryOffsetMm);
        SetNumericValue(_gridOverallOffsetNumeric, settings.GridOverallOffsetMm - settings.GridPrimaryOffsetMm);

        SelectDimensionType(settings.DimensionTypeName);
        SelectPlacementMode(settings.Mode);

        _selectedLeftRight = settings.ColumnLeftRightSide;
        _selectedFrontBack = settings.ColumnFrontBackSide;
        RefreshDirectionButtons();

        _selectedGridDirection = settings.Direction;
        _selectedGridDirectionButton = FindGridDirectionButton(settings.Direction);
        RefreshGridDirectionButtons();

        ApplySavedGridSelections(_horizontalGridList, settings.SelectedHorizontalGridIds);
        ApplySavedGridSelections(_verticalGridList, settings.SelectedVerticalGridIds);
    }

    private void SelectDimensionType(string? dimensionTypeName)
    {
        if (_typeCombo is null)
        {
            return;
        }

        int selectedIndex = string.IsNullOrWhiteSpace(dimensionTypeName)
            ? 0
            : _typeCombo.FindStringExact(dimensionTypeName);
        _typeCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
    }

    private void SelectPlacementMode(PlacementMode mode)
    {
        if (_placementModeCombo is null)
        {
            return;
        }

        for (int i = 0; i < _placementModeCombo.Items.Count; i++)
        {
            if (_placementModeCombo.Items[i] is PlacementModeItem item && item.Mode == mode)
            {
                _placementModeCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private Button? FindGridDirectionButton(PlacementDirection direction)
    {
        Button? fallback = null;
        foreach (KeyValuePair<Button, PlacementDirection> pair in _gridDirectionButtons)
        {
            if (pair.Value != direction)
            {
                continue;
            }

            fallback ??= pair.Key;
            if (pair.Key.Text.IndexOf("預設", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return pair.Key;
            }
        }

        return fallback;
    }

    private static void ApplySavedGridSelections(CheckedListBox list, IReadOnlyCollection<int> selectedIds)
    {
        if (list is null || selectedIds is null || selectedIds.Count == 0)
        {
            return;
        }

        var ids = new HashSet<int>(selectedIds);
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is GridItem item)
            {
                list.SetItemChecked(i, ids.Contains(ElementIdCompat.ToInt32(item.Item.Id)));
            }
        }
    }

    private static HashSet<int> GetCheckedGridIds(CheckedListBox list)
    {
        var ids = new HashSet<int>();
        if (list is null)
        {
            return ids;
        }

        foreach (object selected in list.CheckedItems)
        {
            if (selected is GridItem item)
            {
                ids.Add(ElementIdCompat.ToInt32(item.Item.Id));
            }
        }

        return ids;
    }

    private static void SetNumericValue(NumericUpDown numeric, double value)
    {
        if (numeric is null || double.IsNaN(value) || double.IsInfinity(value))
        {
            return;
        }

        decimal decimalValue = (decimal)Math.Round(value, 0);
        if (decimalValue < numeric.Minimum)
        {
            decimalValue = numeric.Minimum;
        }
        else if (decimalValue > numeric.Maximum)
        {
            decimalValue = numeric.Maximum;
        }

        numeric.Value = decimalValue;
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
        var tab = new TabPage("柱")
        {
            Tag = DimensionMode.ColumnSetout,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent,
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
            Font = new Font(UiFontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32)
        }, 0, 0);

        var directionLayout = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            Width = 312,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(0, 10, 0, 8),
            AutoSize = true,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent
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
            Width = 56,
            Height = 40,
            Margin = new Padding(4),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = UseDarkTheme ? Color.FromArgb(48, 48, 48) : Color.FromArgb(245, 247, 250),
            Anchor = AnchorStyles.None
        };
        centerBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(10, 10, centerBox.Width - 20, centerBox.Height - 20);
            using var pen = new Pen(UseDarkTheme ? Color.FromArgb(230, 230, 230) : Color.FromArgb(110, 110, 110), 1.5f);
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
            Padding = new Padding(0, 6, 0, 0),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent
        };
        modePanel.Controls.Add(new Label
        {
            Text = "模式",
            AutoSize = true,
            Margin = new Padding(0, 7, 8, 0),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32)
        });
        _placementModeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _placementModeCombo.FlatStyle = FlatStyle.Flat;
        _placementModeCombo.BackColor = UseDarkTheme ? Color.FromArgb(74, 74, 74) : Color.White;
        _placementModeCombo.ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32);
        _placementModeCombo.Items.Add(new PlacementModeItem(PlacementMode.Auto, "自動"));
        _placementModeCombo.Items.Add(new PlacementModeItem(PlacementMode.Manual, "手動"));
        _placementModeCombo.SelectedIndex = 0;
        modePanel.Controls.Add(_placementModeCombo);
        panel.Controls.Add(modePanel, 0, 2);

        var placementHint = new Label
        {
            Text = "提示：可選擇一個水平側與/或一個垂直側；手動模式會再請你點選放置側。",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = UseDarkTheme ? Color.FromArgb(205, 205, 205) : Color.FromArgb(80, 80, 80),
            Margin = new Padding(0, 10, 0, 0)
        };
        panel.Controls.Add(placementHint, 0, 3);
        panel.SizeChanged += (_, _) => placementHint.MaximumSize = new Size(Math.Max(100, panel.ClientSize.Width - panel.Padding.Horizontal - 12), 0);

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent };
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
            Width = 88,
            Height = 40,
            Anchor = AnchorStyles.None,
            FlatStyle = FlatStyle.Flat,
            BackColor = UseDarkTheme ? Color.FromArgb(45, 132, 247) : Color.White,
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(40, 52, 70),
            Margin = new Padding(4),
            Font = new Font(UiFontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point)
        };
        button.FlatAppearance.BorderColor = UseDarkTheme ? Color.FromArgb(75, 155, 255) : Color.FromArgb(190, 202, 218);
        button.FlatAppearance.MouseOverBackColor = UseDarkTheme ? Color.FromArgb(65, 150, 255) : Color.FromArgb(232, 242, 255);
        button.FlatAppearance.MouseDownBackColor = UseDarkTheme ? Color.FromArgb(38, 116, 220) : Color.FromArgb(214, 233, 255);
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

        if (IsDarkDirectionButton(button))
        {
            button.BackColor = selected ? Color.FromArgb(45, 132, 247) : Color.FromArgb(66, 66, 66);
            button.ForeColor = selected ? Color.White : Color.FromArgb(210, 210, 210);
            return;
        }

        button.BackColor = selected ? Color.FromArgb(206, 236, 255) : Color.White;
        button.ForeColor = selected ? Color.FromArgb(17, 90, 150) : Color.FromArgb(32, 32, 32);
    }

    private static bool IsDarkDirectionButton(Button button)
    {
        return UseDarkTheme;
    }

    private Control BuildGridPlacementPanel()
    {
        _gridDirectionButtons.Clear();

        var group = new GroupBox
        {
            Text = "柱線生成方向",
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 24, 14, 10),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(28, 48, 74),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent,
            Margin = new Padding(0, 0, 0, 0)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 256));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = new Padding(0),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent
        };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        }

        AddGridDirectionButton(grid, PlacementDirection.NorthWest, "↖ 左上", 0, 0);
        AddGridDirectionButton(grid, PlacementDirection.North, "↑ 上", 1, 0);
        AddGridDirectionButton(grid, PlacementDirection.NorthEast, "↗ 右上", 2, 0);
        AddGridDirectionButton(grid, PlacementDirection.West, "← 左", 0, 1);
        AddGridDirectionButton(grid, PlacementDirection.NorthEast, "預設", 1, 1, isDefault: true);
        AddGridDirectionButton(grid, PlacementDirection.East, "→ 右", 2, 1);
        AddGridDirectionButton(grid, PlacementDirection.SouthWest, "↙ 左下", 0, 2);
        AddGridDirectionButton(grid, PlacementDirection.South, "↓ 下", 1, 2);
        AddGridDirectionButton(grid, PlacementDirection.SouthEast, "↘ 右下", 2, 2);
        root.Controls.Add(grid, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "選擇標註生成側。角落同時控制水平與垂直軸線；中心為上方與右側預設。",
            Dock = DockStyle.Fill,
            ForeColor = UseDarkTheme ? Color.FromArgb(205, 205, 205) : Color.FromArgb(80, 80, 80),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Padding = new Padding(18, 0, 0, 0)
        }, 1, 0);

        group.Controls.Add(root);
        RefreshGridDirectionButtons();
        return group;
    }

    private void AddGridDirectionButton(TableLayoutPanel grid, PlacementDirection direction, string text, int column, int row, bool isDefault = false)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            FlatStyle = FlatStyle.Flat,
            Font = new Font(UiFontFamily, isDefault ? 8.5F : 9F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = UseDarkTheme ? Color.FromArgb(66, 66, 66) : Color.White,
            ForeColor = UseDarkTheme ? Color.FromArgb(210, 210, 210) : Color.FromArgb(32, 32, 32)
        };
        button.FlatAppearance.BorderColor = UseDarkTheme ? Color.FromArgb(100, 100, 100) : Color.FromArgb(190, 202, 218);
        button.FlatAppearance.MouseOverBackColor = UseDarkTheme ? Color.FromArgb(65, 150, 255) : Color.FromArgb(232, 242, 255);
        button.FlatAppearance.MouseDownBackColor = UseDarkTheme ? Color.FromArgb(38, 116, 220) : Color.FromArgb(214, 233, 255);
        button.Click += (_, _) =>
        {
            _selectedGridDirection = direction;
            _selectedGridDirectionButton = button;
            RefreshGridDirectionButtons();
        };
        grid.Controls.Add(button, column, row);

        _gridDirectionButtons[button] = direction;
        if (isDefault)
        {
            _selectedGridDirectionButton = button;
        }
    }

    private void RefreshGridDirectionButtons()
    {
        foreach (KeyValuePair<Button, PlacementDirection> pair in _gridDirectionButtons)
        {
            bool selected = ReferenceEquals(pair.Key, _selectedGridDirectionButton);
            pair.Key.BackColor = selected ? Color.FromArgb(45, 132, 247) : UseDarkTheme ? Color.FromArgb(66, 66, 66) : Color.White;
            pair.Key.ForeColor = selected ? Color.White : UseDarkTheme ? Color.FromArgb(210, 210, 210) : Color.FromArgb(32, 32, 32);
        }
    }

    private TabPage BuildGridTab(IEnumerable<GridSelectionItem> horizontalGrids, IEnumerable<GridSelectionItem> verticalGrids)
    {
        var tab = new TabPage("軸線")
        {
            Tag = DimensionMode.BeamGrid,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(14, 12, 14, 8),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

        var placementPanel = BuildGridPlacementPanel();
        panel.SetColumnSpan(placementPanel, 2);
        panel.Controls.Add(placementPanel, 0, 0);

        var horizontalGroup = new GroupBox
        {
            Text = "水平軸線",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 24, 10, 8),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(28, 48, 74),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent,
            Margin = new Padding(0, 0, 10, 0)
        };

        _horizontalGridList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        _horizontalGridList.BackColor = UseDarkTheme ? Color.FromArgb(58, 58, 58) : Color.FromArgb(252, 253, 255);
        _horizontalGridList.ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32);
        foreach (GridSelectionItem item in horizontalGrids)
        {
            _horizontalGridList.Items.Add(new GridItem(item), true);
        }

        if (_horizontalGridList.Items.Count == 0)
        {
            _horizontalGridList.Items.Add("目前視圖沒有可用的水平軸線");
        }

        horizontalGroup.Controls.Add(_horizontalGridList);
        panel.Controls.Add(horizontalGroup, 0, 2);

        var checkH = new CheckBox
        {
            Text = "全選水平軸線",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(4, 8, 0, 0),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent
        };
        checkH.CheckedChanged += (_, _) => SetAllChecked(_horizontalGridList, checkH.Checked);
        panel.Controls.Add(checkH, 0, 3);

        var verticalGroup = new GroupBox
        {
            Text = "垂直軸線",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 24, 10, 8),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(28, 48, 74),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent,
            Margin = new Padding(10, 0, 0, 0)
        };

        _verticalGridList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        _verticalGridList.BackColor = UseDarkTheme ? Color.FromArgb(58, 58, 58) : Color.FromArgb(252, 253, 255);
        _verticalGridList.ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32);
        foreach (GridSelectionItem item in verticalGrids)
        {
            _verticalGridList.Items.Add(new GridItem(item), true);
        }

        if (_verticalGridList.Items.Count == 0)
        {
            _verticalGridList.Items.Add("目前視圖沒有可用的垂直軸線");
        }

        verticalGroup.Controls.Add(_verticalGridList);
        panel.Controls.Add(verticalGroup, 1, 2);

        var checkV = new CheckBox
        {
            Text = "全選垂直軸線",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(14, 8, 0, 0),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent
        };
        checkV.CheckedChanged += (_, _) => SetAllChecked(_verticalGridList, checkV.Checked);
        panel.Controls.Add(checkV, 1, 3);

        Action updateResponsiveLayout = () =>
        {
            int stackingThreshold = (int)Math.Round(900D * panel.DeviceDpi / 96D);
            bool stackSelections = panel.ClientSize.Width > 0 && panel.ClientSize.Width < stackingThreshold;

            panel.SuspendLayout();
            panel.ColumnStyles[0].SizeType = SizeType.Percent;
            panel.ColumnStyles[0].Width = stackSelections ? 100 : 50;
            panel.ColumnStyles[1].SizeType = SizeType.Percent;
            panel.ColumnStyles[1].Width = stackSelections ? 0 : 50;
            panel.RowStyles[4].Height = stackSelections ? 112 : 0;
            panel.RowStyles[5].Height = stackSelections ? 34 : 0;

            panel.SetCellPosition(horizontalGroup, new TableLayoutPanelCellPosition(0, 2));
            panel.SetCellPosition(checkH, new TableLayoutPanelCellPosition(0, 3));
            panel.SetCellPosition(verticalGroup, new TableLayoutPanelCellPosition(stackSelections ? 0 : 1, stackSelections ? 4 : 2));
            panel.SetCellPosition(checkV, new TableLayoutPanelCellPosition(stackSelections ? 0 : 1, stackSelections ? 5 : 3));
            horizontalGroup.Margin = stackSelections ? new Padding(0) : new Padding(0, 0, 10, 0);
            verticalGroup.Margin = stackSelections ? new Padding(0) : new Padding(10, 0, 0, 0);
            checkV.Margin = stackSelections ? new Padding(4, 8, 0, 0) : new Padding(14, 8, 0, 0);
            panel.ResumeLayout(true);
        };
        panel.SizeChanged += (_, _) => updateResponsiveLayout();
        updateResponsiveLayout();

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White };
        host.Controls.Add(panel);
        tab.Controls.Add(host);
        return tab;
    }

    private TabPage BuildBeamWidthTab()
    {
        var tab = new TabPage("梁")
        {
            Tag = DimensionMode.BeamWidth,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            AutoSize = true,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = "建立梁寬與梁對梁間距標註。",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Font = new Font(UiFontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = UseDarkTheme ? Color.White : Color.FromArgb(32, 32, 32),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent
        }, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = "間距標註會依梁方向分組；偏移量可控制標註線與梁軸的距離。",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = UseDarkTheme ? Color.FromArgb(205, 205, 205) : Color.FromArgb(80, 80, 80),
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.Transparent,
            Margin = new Padding(0, 8, 0, 0)
        }, 0, 1);

        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var offsetButton = new Button
        {
            Text = "調整標註距離（距離設定）",
            AutoSize = true,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 16, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 132, 247),
            ForeColor = Color.White
        };
        offsetButton.Click += (_, _) => _tabControl.SelectedIndex = 1;
        panel.Controls.Add(offsetButton, 0, 2);
        panel.SizeChanged += (_, _) =>
        {
            foreach (Control child in panel.Controls)
                if (child is Label label)
                    label.MaximumSize = new Size(Math.Max(100, panel.ClientSize.Width - panel.Padding.Horizontal - 12), 0);
        };

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White
        };
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

    private static void ApplyDarkTheme(Control control)
    {
        if (control is null)
        {
            return;
        }

        if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel)
        {
            control.BackColor = Color.FromArgb(39, 39, 39);
        }

        if (control is Label || control is CheckBox || control is GroupBox)
        {
            control.ForeColor = Color.White;
            control.BackColor = Color.FromArgb(39, 39, 39);
        }

        if (control is ComboBox || control is NumericUpDown || control is CheckedListBox)
        {
            control.BackColor = Color.FromArgb(58, 58, 58);
            control.ForeColor = Color.White;
        }

        foreach (Control child in control.Controls)
        {
            ApplyDarkTheme(child);
        }
    }

    private void ApplyModernTheme()
    {
        _tabControl.BackColor = UseDarkTheme ? Color.FromArgb(39, 39, 39) : Color.White;
        _tabControl.Appearance = TabAppearance.Normal;
    }

    private void TabControl_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabControl || tabControl.TabPages.Count <= e.Index)
        {
            return;
        }

        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Rectangle bounds = e.Bounds;
        Color fill = UseDarkTheme
            ? isSelected ? Color.FromArgb(39, 39, 39) : Color.FromArgb(64, 64, 64)
            : isSelected ? Color.FromArgb(42, 106, 188) : Color.FromArgb(232, 238, 247);
        Color text = UseDarkTheme
            ? isSelected ? Color.White : Color.FromArgb(175, 175, 175)
            : isSelected ? Color.White : Color.FromArgb(44, 59, 80);

        using (var brush = new SolidBrush(fill))
        {
            e.Graphics.FillRectangle(brush, bounds);
        }

        TextRenderer.DrawText(
            e.Graphics,
            tabControl.TabPages[e.Index].Text,
            Font,
            bounds,
            text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    public bool TryGetOptions(out DimensionOptions options, out string error)
    {
        double gridPrimaryOffsetMm = (double)_gridPrimaryOffsetNumeric.Value;
        double gridOverallOffsetMm = gridPrimaryOffsetMm + (double)_gridOverallOffsetNumeric.Value;

        options = new DimensionOptions
        {
            OffsetInternal = RevitDB.UnitUtils.ConvertToInternalUnits((double)_offsetNumeric.Value, RevitDB.UnitTypeId.Millimeters),
            GridPrimaryOffsetInternal = RevitDB.UnitUtils.ConvertToInternalUnits(gridPrimaryOffsetMm, RevitDB.UnitTypeId.Millimeters),
            GridOverallOffsetInternal = RevitDB.UnitUtils.ConvertToInternalUnits(gridOverallOffsetMm, RevitDB.UnitTypeId.Millimeters),
            DimensionTypeName = _typeCombo.SelectedIndex <= 0 ? null : _typeCombo.SelectedItem?.ToString()
        };
        error = string.Empty;

        DimensionMode selectedMode = _isModeLocked
            ? _initialMode
            : _modeTabControl.SelectedTab?.Tag is DimensionMode mode ? mode : DimensionMode.ColumnSetout;

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
        options.Direction = _selectedGridDirection;
        options.SelectedHorizontalGridIds = horizontal;
        options.SelectedVerticalGridIds = vertical;
        return true;
    }
}
}
