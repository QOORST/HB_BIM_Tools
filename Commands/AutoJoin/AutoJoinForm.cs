using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class AutoJoinForm : Form
{
    private sealed class ScopeOption
    {
        public AutoJoinScope Value { get; set; }

        public string Text { get; set; }

        public override string ToString() => Text;
    }

    private sealed class LabeledItem
    {
        public string Key { get; set; }

        public string Text { get; set; }

        public override string ToString() => Text;
    }

    private static readonly Dictionary<string, string> CategoryTextMap = new(StringComparer.Ordinal)
    {
        ["Ceiling"] = "天花板",
        ["Column"] = "柱",
        ["Floor"] = "樓板",
        ["GenericModel"] = "一般模型",
        ["Roof"] = "屋頂",
        ["Wall"] = "牆",
        ["StructuralColumn"] = "結構柱",
        ["StructuralFloor"] = "結構樓板",
        ["StructuralFoundation"] = "結構基礎",
        ["StructuralFraming"] = "結構構架",
        ["StructuralWall"] = "結構牆",
    };

    private readonly ComboBox _scopeCombo = new();
    private readonly FlowLayoutPanel _categoryChecklist = new() { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true };
    private readonly Label _scopeSummary = new() { Dock = DockStyle.Fill, AutoSize = false, Text = "檢查範圍後顯示數量；符合條件不代表一定能接合。" };
    private Button _primaryButton;
    private Button _unjoinButton;
    private bool _scopeValid = true;
    public bool NeedsScopeCheck { get; private set; } = true;
    public bool ScopeCanRun => _scopeValid;
    public event System.Action ScopeCheckRequested;
    internal static readonly string[] WallAlignKeys = { "Wall", "StructuralWall", "Floor", "StructuralFloor", "StructuralFraming" };

    private void InvalidateScope()
    {
        NeedsScopeCheck = true;
        _scopeSummary.Text = "範圍或類別已變更，請重新檢查數量。";
        // Keep the check button available even when the previous scope was empty.
        _scopeValid = true;
        if (_primaryButton != null) _primaryButton.Enabled = !_busy;
        if (_unjoinButton != null) _unjoinButton.Enabled = !_busy;
    }

    public void SetScopeSummary(string text, bool canRun)
    {
        NeedsScopeCheck = false;
        _scopeSummary.Text = text;
        _scopeValid = canRun;
        _primaryButton.Enabled = canRun;
        _unjoinButton.Enabled = canRun;
    }
    private readonly List<CheckBox> _categoryBoxes = new();
    private readonly Label _categoryCount = new() { AutoSize = true, Margin = new Padding(10, 8, 0, 0), ForeColor = Color.DimGray };
    private readonly ListBox _priorityList = new();
    private readonly CheckBox _sameCategoryCheckbox = new();
    private readonly CheckBox _typeRulesEnabled = new() { Text = "啟用類型規則", AutoSize = true };
    private readonly Button _typeRulesButton = new() { Text = "類型規則…", AutoSize = true };
    private readonly Label _typeRulesCount = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
    private List<TypeJoinRule> _typeRules = new();
    private IReadOnlyList<TypeJoinChoice> _typeChoices = Array.Empty<TypeJoinChoice>();
    public event System.Action TypeChoicesRefreshRequested;

    public void SetTypeChoices(IReadOnlyList<TypeJoinChoice> choices)
    {
        _typeChoices = choices ?? Array.Empty<TypeJoinChoice>();
        _typeRulesButton.Enabled = !_alignOnlyMode;
    }

    private void EditTypeRules()
    {
        using var editor = new TypeJoinRuleEditor(_typeChoices, _typeRules);
        if (editor.ShowDialog(_typeRulesButton.FindForm() ?? this) != DialogResult.OK) return;
        _typeRules = editor.Rules.Select(x => x.Clone()).ToList();
        if (_typeRules.Count == 0) _typeRulesEnabled.Checked = false;
        _typeRulesCount.Text = $"{_typeRules.Count} 條";
    }
    private readonly CheckBox _structuralBridgeCheckbox = new();
    private readonly CheckBox _detailCheckbox = new();
    private readonly bool _alignOnlyMode;
    private bool _modeless;
    private bool _busy;
    private TableLayoutPanel _root;
    private Control _advancedContent;
    private readonly FlowLayoutPanel _selectionBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Visible = false, WrapContents = false };
    private readonly Panel _resultPanel = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(12, 28, 12, 8), BackColor = Color.FromArgb(245, 248, 251) };
    private readonly TextBox _modelessStatus = new TextBox { Dock = DockStyle.Bottom, Height = 105, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.WhiteSmoke };
    public event System.Action RunRequested;
    public event System.Action PickRequested;
    public event System.Action SelectionRequested;

    public void EnableModeless()
    {
        _modeless = true;
        var workArea = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(840, workArea.Width - 32), Math.Min(700, workArea.Height - 48));
        if (CancelButton is Button closeButton) closeButton.Text = "關閉";
        Height = Math.Min(760, workArea.Height - 48);
        var current = new Button { Text = "使用目前選取", AutoSize=true, MinimumSize=new Size(125,30) };
        var pick = new Button { Text = "重新選取", AutoSize=true, MinimumSize=new Size(100,30) };
        current.Click += (_, _) => SelectionRequested?.Invoke();
        pick.Click += (_, _) => PickRequested?.Invoke();
        StyleButton(current);
        StyleButton(pick);
        _selectionBar.Controls.Add(current);
        _selectionBar.Controls.Add(pick);
        _selectionBar.Visible = true;
        _root.RowStyles[1].Height = 126;
        _resultPanel.Visible = true;
        _root.RowStyles[5].Height = 64;
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        SetStatus("執行時記住本機接合設定；關閉不儲存尚未執行的調整。\n執行前會重新讀取所選範圍，不記住構件選取。");
    }

    public void SetStatus(string text)
    {
        _modelessStatus.Text = text;
        _modelessStatus.Refresh();
    }
    public void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (Control control in _root.Controls)
            if (control != _resultPanel) control.Enabled = !busy;
        _modelessStatus.Enabled = true;
        _primaryButton.Enabled = !busy && _scopeValid;
        _unjoinButton.Enabled = !busy && _scopeValid;
    }
    public void UseSelectedScope()
    {
        _scopeCombo.SelectedItem = _scopeCombo.Items.Cast<ScopeOption>().First(x => x.Value == AutoJoinScope.SelectedElements);
    }

    private void RequestRun(ExecutionAction action)
    {
        if (!_modeless && NeedsScopeCheck) ScopeCheckRequested?.Invoke();
        if (_busy || !_scopeValid || !ValidateBeforeRun()) return;
        Action = action;
        if (_modeless) RunRequested?.Invoke();
        else { DialogResult = DialogResult.OK; Close(); }
    }

    public AutoJoinForm(AutoJoinSettings settings)
        : this(settings, false)
    {
    }

    public AutoJoinForm(AutoJoinSettings settings, bool alignOnlyMode)
    {
        _alignOnlyMode = alignOnlyMode;
        Text = alignOnlyMode ? "HB_BIM 對齊牆輪廓" : "HB_BIM 自動接合";
        Width = 920;
        Height = alignOnlyMode ? 580 : 750;
        MinimumSize = new Size(800, alignOnlyMode ? 540 : 650);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft JhengHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(244, 246, 248);
        ShowIcon = true;
        Icon = CreateBrandIcon();

        BuildUi();
        LoadSettings(settings);
    }

    public ExecutionAction Action { get; private set; } = ExecutionAction.None;

    public AutoJoinSettings BuildSettings()
    {
        var settings = new AutoJoinSettings
        {
            Scope = _scopeCombo.SelectedItem is ScopeOption scopeOption
                ? scopeOption.Value
                : AutoJoinScope.VisibleInView,
            AllowSameCategoryJoin = _sameCategoryCheckbox.Checked,
            UseSameCategoryTypeRules = _typeRulesEnabled.Checked,
            SameCategoryTypeRules = _typeRules.Select(x => x.Clone()).ToList(),
            AllowStructuralNonStructuralJoin = _structuralBridgeCheckbox.Checked,
            CheckInDetail = _detailCheckbox.Checked,
            EnabledCategoryKeys = GetCheckedCategoryKeys(),
            PriorityKeys = _priorityList.Items.Cast<LabeledItem>().Select(i => i.Key).ToList()
        };

        settings.Normalize();
        // Normalize restores defaults for empty saved lists; a live empty selection must stay empty.
        settings.EnabledCategoryKeys = GetCheckedCategoryKeys();
        if (_alignOnlyMode)
        {
            settings.UseSameCategoryTypeRules = false;
            settings.SameCategoryTypeRules.Clear();
        }
        return settings;
    }

    private void BuildUi()
    {
        var accentColor = Color.FromArgb(0, 105, 180);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12)
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));

        var guideLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = _alignOnlyMode
                ? "操作流程：1. 選範圍 → 2. 選類別 → 3. 執行對齊牆輪廓"
                : "操作流程：1. 選範圍 → 2. 選類別 → 3. 調整優先序 → 4. 執行接合或解除接合",
            ForeColor = Color.DimGray,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var scopeGroup = new GroupBox { Text = "套用範圍", Dock = DockStyle.Fill };
        _scopeCombo.Dock = DockStyle.Top;
        _scopeCombo.Height = 28;
        _scopeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _scopeCombo.Items.AddRange(new object[]
        {
            new ScopeOption { Value = AutoJoinScope.All, Text = "全部元素" },
            new ScopeOption { Value = AutoJoinScope.VisibleInView, Text = "目前視圖可見" },
            new ScopeOption { Value = AutoJoinScope.SelectedElements, Text = "已選元素" }
        });
        _scopeCombo.SelectedIndexChanged += (_, _) => InvalidateScope();
        scopeGroup.Padding = new Padding(10, 22, 10, 8);
        scopeGroup.Controls.Add(_scopeCombo);

        var categoryGroup = new GroupBox { Text = "類別選擇", Dock = DockStyle.Fill, Padding = new Padding(8, 22, 8, 8) };
        var categoryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        categoryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        categoryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var categoryQuickPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };

        var selectAllButton = new Button { Text = "全選", Width = 70, Height = 26 };
        var clearAllButton = new Button { Text = "全不選", Width = 70, Height = 26 };
        selectAllButton.Click += (_, _) => SetAllCategoriesChecked(true);
        clearAllButton.Click += (_, _) => SetAllCategoriesChecked(false);
        categoryQuickPanel.Controls.Add(selectAllButton);
        categoryQuickPanel.Controls.Add(clearAllButton);
        var commonButton = new Button { Text = _alignOnlyMode ? "牆與梁板" : "梁柱牆板", Width = 90, Height = 26 };
        commonButton.Click += (_, _) =>
        {
            var keys = new HashSet<string>(WallAlignKeys.Concat(new[] { "Column", "StructuralColumn" }));
            foreach (var box in _categoryBoxes) box.Checked = keys.Contains((string)box.Tag);
        };
        categoryQuickPanel.Controls.Add(commonButton);
        categoryQuickPanel.Controls.Add(_categoryCount);

        _categoryChecklist.Dock = DockStyle.Fill;
        foreach (var spec in CategoryCatalog.Specs)
        {
            if (_alignOnlyMode && !WallAlignKeys.Contains(spec.Key)) continue;
            var row = new CheckBox
            {
                Text = LocalizeCategoryText(spec.Key, spec.DisplayName),
                Tag = spec.Key, Checked = true, AutoSize = false, Height = 32,
                Padding = new Padding(8, 0, 8, 0), Margin = new Padding(0, 0, 0, 3),
                BackColor = Color.FromArgb(232, 242, 252), Cursor = Cursors.Hand,
                AccessibleName = LocalizeCategoryText(spec.Key, spec.DisplayName)
            };
            row.CheckedChanged += (_, _) =>
            {
                row.BackColor = row.Checked ? Color.FromArgb(232, 242, 252) : Color.White;
                UpdateCategoryCount();
                InvalidateScope();
            };
            _categoryBoxes.Add(row);
            _categoryChecklist.Controls.Add(row);
        }
        _categoryChecklist.SizeChanged += (_, _) =>
        {
            foreach (var row in _categoryBoxes)
                row.Width = Math.Max(100, (_categoryChecklist.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8) / 2);
        };
        UpdateCategoryCount();

        categoryLayout.Controls.Add(categoryQuickPanel, 0, 0);
        categoryLayout.Controls.Add(_categoryChecklist, 0, 1);
        categoryGroup.Controls.Add(categoryLayout);

        var priorityGroup = new GroupBox { Text = "接合優先序（越上方優先序越高）", Dock = DockStyle.Fill, Padding = new Padding(8, 22, 8, 8) };
        var priorityLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        priorityLayout.Padding = new Padding(2);
        priorityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        priorityLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        priorityLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        priorityLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var ruleBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _typeRulesButton.Enabled = false;
        _typeRulesEnabled.Enabled = !_alignOnlyMode;
        _typeRulesButton.Click += (_, _) => EditTypeRules();
        _typeRulesEnabled.CheckedChanged += (_, _) => _sameCategoryCheckbox.Enabled = _alignOnlyMode || !_typeRulesEnabled.Checked;
        var refreshTypes = new Button { Text = "↻", Width = 30, Height = 28, AccessibleName = "重新讀取專案類型" };
        var refreshTip = new ToolTip();
        refreshTip.SetToolTip(refreshTypes, "關閉設定視窗並重新讀取專案類型");
        refreshTip.SetToolTip(_typeRulesEnabled, "同類別僅接合符合規則的類型配對；未符合的配對會略過。");
        Disposed += (_, _) => refreshTip.Dispose();
        refreshTypes.Enabled = !_alignOnlyMode;
        refreshTypes.Click += (_, _) =>
        {
            // Revit handles ExternalEvent only after the modal settings dialog closes.
            var settingsDialog = refreshTypes.FindForm();
            if (settingsDialog != null && settingsDialog != this) settingsDialog.Close();
            TypeChoicesRefreshRequested?.Invoke();
        };
        ruleBar.Controls.AddRange(new Control[] { _typeRulesEnabled, _typeRulesButton, _typeRulesCount, refreshTypes });
        priorityLayout.Controls.Add(ruleBar, 0, 2);
        _priorityList.Dock = DockStyle.Fill;
        _priorityList.DrawMode = DrawMode.OwnerDrawFixed;
        _priorityList.ItemHeight = 28;
        _priorityList.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, $"{e.Index + 1}.  {_priorityList.Items[e.Index]}", e.Font, e.Bounds, e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        };
        _priorityList.Margin = new Padding(3);
        foreach (var key in CategoryCatalog.DefaultPriorityKeys)
        {
            _priorityList.Items.Add(new LabeledItem { Key = key, Text = LocalizeCategoryText(key, key) });
        }

        var movePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(0),
            Margin = new Padding(2)
        };
        var upButton = new Button { Text = "上移", Width = 80, Height = 28, Margin = new Padding(2) };
        var downButton = new Button { Text = "下移", Width = 80, Height = 28, Margin = new Padding(2) };
        var resetPriorityButton = new Button { Text = "預設", Width = 80, Height = 28, Margin = new Padding(2) };
        upButton.Click += (_, _) => MovePriority(-1);
        downButton.Click += (_, _) => MovePriority(1);
        resetPriorityButton.Click += (_, _) => ResetPriorityToDefault();
        movePanel.Controls.Add(upButton);
        movePanel.Controls.Add(downButton);
        movePanel.Controls.Add(resetPriorityButton);

        priorityLayout.Controls.Add(_priorityList, 0, 0);
        priorityLayout.Controls.Add(movePanel, 0, 1);
        priorityGroup.Controls.Add(priorityLayout);

        var detailGroup = new GroupBox { Text = "進階選項", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 6) };
        var detailLayout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(4, 0, 4, 2) };

        _sameCategoryCheckbox.Text = "允許同類別接合";
        _sameCategoryCheckbox.AutoSize = true;

        _structuralBridgeCheckbox.Text = "允許結構與非結構接合";
        _structuralBridgeCheckbox.AutoSize = true;

        _detailCheckbox.Text = "包含邊界相接的元素（較完整，速度較慢）";
        _detailCheckbox.AutoSize = true;

        detailLayout.Controls.Add(_sameCategoryCheckbox);
        detailLayout.Controls.Add(_structuralBridgeCheckbox);
        detailLayout.Controls.Add(_detailCheckbox);
        detailGroup.Controls.Add(detailLayout);

        var ioPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(2) };
        var importButton = new Button { Text = "匯入設定", Width = 100, Height = 32 };
        var exportButton = new Button { Text = "匯出設定", Width = 100, Height = 32 };
        importButton.Click += (_, _) => ImportSettings();
        exportButton.Click += (_, _) => ExportSettings();
        ioPanel.WrapContents = false;
        ioPanel.Controls.Add(importButton);
        ioPanel.Controls.Add(exportButton);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(2)
        };

        var cancelButton = new Button { Text = "取消", Width = 90, Height = 32 };
        var unjoinButton = new Button { Text = "解除接合", Width = 100, Height = 32 };
        var alignWallButton = new Button { Text = "對齊牆輪廓", Width = 110, Height = 32 };
        var joinButton = new Button { Text = "自動接合", Width = 110, Height = 32, BackColor = accentColor, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _primaryButton = _alignOnlyMode ? alignWallButton : joinButton;
        _unjoinButton = unjoinButton;
        joinButton.FlatAppearance.BorderSize = 0;
        if (_alignOnlyMode)
        {
            alignWallButton.Text = "執行對齊";
            alignWallButton.Width = 120;
            alignWallButton.BackColor = accentColor;
            alignWallButton.ForeColor = Color.White;
            alignWallButton.FlatStyle = FlatStyle.Flat;
            alignWallButton.FlatAppearance.BorderSize = 0;
        }

        cancelButton.Click += (_, _) =>
        {
            Action = ExecutionAction.None;
            DialogResult = DialogResult.Cancel;
            Close();
        };

        unjoinButton.Click += (_, _) =>
        {
            RequestRun(ExecutionAction.Unjoin);
        };

        alignWallButton.Click += (_, _) =>
        {
            RequestRun(ExecutionAction.AlignWallProfile);
        };

        joinButton.Click += (_, _) =>
        {
            RequestRun(ExecutionAction.AutoJoin);
        };

        AcceptButton = _alignOnlyMode ? alignWallButton : joinButton;
        CancelButton = cancelButton;

        actionPanel.Controls.Add(cancelButton);
        if (_alignOnlyMode) actionPanel.Controls.Add(alignWallButton);
        if (!_alignOnlyMode)
        {
            actionPanel.Controls.Add(unjoinButton);
            actionPanel.Controls.Add(joinButton);
        }

        _root = root;
        root.Padding = new Padding(16);
        root.RowStyles.Clear();
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

        var header = new FlowLayoutPanel { Dock=DockStyle.Top, AutoSize=true, FlowDirection=FlowDirection.TopDown, WrapContents=false, Padding=new Padding(0,0,0,8) };
        header.Controls.Add(new Label { Text = _alignOnlyMode ? "對齊牆輪廓" : "自動接合", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(28, 44, 60) });
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);
        scopeGroup.Controls.Clear();
        var scopeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        scopeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scopeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        scopeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var scopeActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _scopeCombo.Dock = DockStyle.None;
        _scopeCombo.Width = 200;
        _scopeCombo.Margin = new Padding(0, 4, 12, 0);
        _selectionBar.Dock = DockStyle.None;
        _selectionBar.AutoSize = true;
        _selectionBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _selectionBar.Margin = Padding.Empty;
        var checkScope = new Button { Text = "檢查範圍", Width = 100, Height = 30 };
        checkScope.Click += (_, _) => { InvalidateScope(); ScopeCheckRequested?.Invoke(); };
        scopeActions.Controls.Add(_scopeCombo);
        scopeActions.Controls.Add(_selectionBar);
        scopeActions.Controls.Add(checkScope);
        scopeLayout.Controls.Add(scopeActions, 0, 0);
        scopeLayout.Controls.Add(_scopeSummary, 0, 1);
        scopeGroup.Controls.Add(scopeLayout);
        root.Controls.Add(FlatSection(scopeGroup, "01  處理範圍"), 0, 1);
        root.SetColumnSpan(root.GetControlFromPosition(0, 1), 2);
        root.Controls.Add(FlatSection(categoryGroup, _alignOnlyMode ? "02  處理牆與參考構件" : "02  處理類別"), 0, 2);
        if (_alignOnlyMode)
        {
            Disposed += (_, _) => priorityGroup.Dispose();
            var help = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "處理對象：基本牆\n\n參考構件：梁、樓板及結構樓板\n\n依參考構件調整牆頂／牆底。請將要調整的牆與參考梁板一起納入範圍。\n\n此工具不需要先執行自動接合。", BackColor = Color.White };
            root.Controls.Add(help, 1, 2);
        }
        else root.Controls.Add(FlatSection(priorityGroup, "03  接合優先序 · 上方優先"), 1, 2);
        // Keep infrequent settings out of the primary workflow.
        priorityLayout.Controls.Remove(ruleBar);
        priorityLayout.Controls.Add(new Label { Text = "上方構件切割下方構件；同類別依類型規則判定。", Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(340, 0), Padding = new Padding(2, 6, 2, 0) }, 0, 2);
        detailLayout.Controls.Add(ruleBar);
        var detailSection = FlatSection(detailGroup, "接合選項");
        detailSection.AutoSize = true;
        detailSection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        if (!_alignOnlyMode)
        {
            _advancedContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            detailSection.Dock = DockStyle.Top;
            ioPanel.Dock = DockStyle.Bottom;
            _advancedContent.Controls.Add(detailSection);
            _advancedContent.Controls.Add(ioPanel);
            Disposed += (_, _) => _advancedContent.Dispose();
        }
        else Disposed += (_, _) => detailSection.Dispose();
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ioPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        actionPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (_alignOnlyMode) footer.Controls.Add(ioPanel, 0, 0);
        else
        {
            var settingsButton = new Button { Text = "進階設定…", Width = 110, Height = 32, Margin = new Padding(4) };
            settingsButton.Click += (_, _) => { using var dialog = CreateAdvancedDialog(); dialog.ShowDialog(this); };
            footer.Controls.Add(settingsButton, 0, 0);
        }
        footer.Controls.Add(actionPanel, 1, 0);
        root.Controls.Add(footer, 0, 4);
        root.SetColumnSpan(footer, 2);
        _modelessStatus.Dock = DockStyle.Fill;
        _modelessStatus.BorderStyle = BorderStyle.None;
        _modelessStatus.BackColor = _resultPanel.BackColor;
        _resultPanel.Controls.Add(_modelessStatus);
        _resultPanel.Controls.Add(new Label { Text = "執行狀態", AutoSize = true, Location = new Point(12, 8), ForeColor = Color.FromArgb(67, 87, 107) });
        root.Controls.Add(_resultPanel, 0, 5);
        root.SetColumnSpan(_resultPanel, 2);

        _categoryChecklist.BorderStyle = BorderStyle.None;
        _priorityList.BorderStyle = BorderStyle.None;
        _priorityList.IntegralHeight = false;
        _scopeCombo.FlatStyle = FlatStyle.Flat;
        foreach (var button in AllControls(root).OfType<Button>()) StyleButton(button);
        var primary = _alignOnlyMode ? alignWallButton : joinButton;
        primary.BackColor = accentColor;
        primary.ForeColor = Color.White;
        primary.FlatAppearance.BorderSize = 0;
        actionPanel.Controls.Clear();
        actionPanel.WrapContents = true;
        actionPanel.Controls.Add(primary);
        actionPanel.Controls.Add(cancelButton);
        if (!_alignOnlyMode) actionPanel.Controls.Add(unjoinButton);

        Controls.Add(root);
    }

    internal Form CreateAdvancedDialog()
    {
        var dialog = new Form { Text = "自動接合－進階設定", Size = new Size(610, 370), MinimumSize = new Size(570, 350), Font = Font, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, AutoScaleMode = AutoScaleMode.Dpi };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var done = new Button { Text = "完成", Width = 90, Height = 30, DialogResult = DialogResult.OK };
        footer.Controls.Add(done);
        var hint = new Label { Text = "設定立即套用至主畫面；執行接合時才儲存。", Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 10, 0, 0) };
        dialog.Controls.Add(_advancedContent);
        dialog.Controls.Add(hint);
        dialog.Controls.Add(footer);
        foreach (var button in AllControls(dialog).OfType<Button>()) StyleButton(button);
        dialog.AcceptButton = done;
        dialog.CancelButton = done;
        // Preserve the shared controls when this temporary dialog closes.
        dialog.FormClosed += (_, _) => dialog.Controls.Remove(_advancedContent);
        return dialog;
    }

    private static IEnumerable<Control> AllControls(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(AllControls(c)));

    private static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(217, 224, 231);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 241, 249);
        button.BackColor = Color.White;
        button.ForeColor = Color.FromArgb(47, 64, 80);
        button.Cursor = Cursors.Hand;
    }

    private static Panel FlatSection(GroupBox source, string title)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 32, 12, 10), Margin = new Padding(0, 0, 8, 10) };
        foreach (Control child in source.Controls.Cast<Control>().ToArray()) panel.Controls.Add(child);
        panel.Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(12, 10), ForeColor = Color.FromArgb(67, 87, 107) });
        source.Dispose();
        return panel;
    }

    private void LoadSettings(AutoJoinSettings settings)
    {
        settings.Normalize();

        var scopeItem = _scopeCombo.Items
            .Cast<ScopeOption>()
            .FirstOrDefault(i => i.Value == settings.Scope);

        _scopeCombo.SelectedItem = scopeItem ?? _scopeCombo.Items.Cast<ScopeOption>().First();

        if (_scopeCombo.SelectedIndex < 0)
        {
            _scopeCombo.SelectedItem = _scopeCombo.Items.Cast<ScopeOption>().First();
        }

        var enabled = new HashSet<string>(settings.EnabledCategoryKeys);
        foreach (var row in _categoryBoxes)
        {
            row.Checked = enabled.Contains((string)row.Tag);
        }

        _priorityList.Items.Clear();
        foreach (var key in settings.PriorityKeys)
        {
            _priorityList.Items.Add(new LabeledItem { Key = key, Text = LocalizeCategoryText(key, key) });
        }

        _sameCategoryCheckbox.Checked = settings.AllowSameCategoryJoin;
        _typeRules = (settings.SameCategoryTypeRules ?? new List<TypeJoinRule>()).Select(x => x.Clone()).ToList();
        _typeRulesEnabled.Checked = settings.UseSameCategoryTypeRules;
        _typeRulesCount.Text = $"{_typeRules.Count} 條";
        _structuralBridgeCheckbox.Checked = settings.AllowStructuralNonStructuralJoin;
        _detailCheckbox.Checked = settings.CheckInDetail;
    }

    private List<string> GetCheckedCategoryKeys()
    {
        var keys = new List<string>();
        foreach (var row in _categoryBoxes.Where(row => row.Checked))
        {
            keys.Add((string)row.Tag);
        }

        return keys;
    }

    private bool ValidateBeforeRun()
    {
        if (!_alignOnlyMode && !ValidateTypeRules()) return false;
        if (GetCheckedCategoryKeys().Count > 0)
            return true;

        MessageBox.Show(
            "請至少勾選一個要處理的類別。",
            "自動接合",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    private void MovePriority(int direction)
    {
        var selectedIndex = _priorityList.SelectedIndex;
        if (selectedIndex < 0)
        {
            return;
        }

        var targetIndex = selectedIndex + direction;
        if (targetIndex < 0 || targetIndex >= _priorityList.Items.Count)
        {
            return;
        }

        var item = _priorityList.Items[selectedIndex];
        _priorityList.Items.RemoveAt(selectedIndex);
        _priorityList.Items.Insert(targetIndex, item);
        _priorityList.SelectedIndex = targetIndex;
    }

    private void ResetPriorityToDefault()
    {
        _priorityList.Items.Clear();
        foreach (var key in CategoryCatalog.DefaultPriorityKeys)
        {
            _priorityList.Items.Add(new LabeledItem { Key = key, Text = LocalizeCategoryText(key, key) });
        }

        if (_priorityList.Items.Count > 0)
        {
            _priorityList.SelectedIndex = 0;
        }
    }

    private void SetAllCategoriesChecked(bool isChecked)
    {
        foreach (var row in _categoryBoxes) row.Checked = isChecked;
    }

    private void UpdateCategoryCount() => _categoryCount.Text = $"已選 {_categoryBoxes.Count(row => row.Checked)}／{_categoryBoxes.Count}";

    private void ImportSettings()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|所有檔案 (*.*)|*.*",
            Title = _alignOnlyMode ? "匯入牆輪廓設定" : "匯入自動接合設定"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var imported = SettingsSerializer.LoadOrDefault(dialog.FileName);
        LoadSettings(imported);
        MessageBox.Show("設定已匯入。", "自動接合", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportSettings()
    {
        if (!_alignOnlyMode && !ValidateTypeRules()) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "XML files (*.xml)|*.xml|所有檔案 (*.*)|*.*",
            Title = _alignOnlyMode ? "匯出牆輪廓設定" : "匯出自動接合設定",
            FileName = _alignOnlyMode ? "WallAlign.Settings.xml" : "AutoJoin.Settings.xml"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var settings = BuildSettings();
        SettingsSerializer.Save(dialog.FileName, settings);

        var directory = Path.GetDirectoryName(dialog.FileName);
        MessageBox.Show($"設定已匯出至：{Environment.NewLine}{directory}", "自動接合", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private bool ValidateTypeRules()
    {
        try
        {
            TypeJoinPolicy.Validate(_typeRules);
            if (_typeRulesEnabled.Checked && _typeRules.Count == 0)
                throw new InvalidOperationException("請新增類型規則，或取消啟用類型規則。");
            return true;
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "類型規則", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    internal static string LocalizeCategoryText(string key, string fallback)
    {
        return CategoryTextMap.TryGetValue(key, out var text) ? text : fallback;
    }

    private static Icon CreateBrandIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        using var stroke = new Pen(Color.Black, 1.4f);
        g.DrawRectangle(stroke, 2, 2, 27, 27);
        g.DrawLine(stroke, 5, 10, 14, 18);
        g.DrawLine(stroke, 14, 18, 23, 10);
        g.DrawLine(stroke, 8, 22, 23, 22);
        using var ydFont = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.Black);
        g.DrawString("YD", ydFont, brush, new PointF(5f, 4f));

        return Icon.FromHandle(bmp.GetHicon());
    }
    }
}
