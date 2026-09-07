using System;
using System.Drawing;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP.MepCheck
{
    internal sealed class MepCheckOptionsForm : WinForms.Form
    {
        private readonly ElementId _preselectedOutletId;
        private readonly WinForms.RadioButton _rbCurrentView;
        private readonly WinForms.RadioButton _rbEntireModel;
        private readonly WinForms.CheckBox _chkPipeDrain;
        private readonly WinForms.CheckBox _chkDrainageOnly;
        private readonly WinForms.CheckBox _chkIgnoreFlatPipes;
        private readonly WinForms.CheckBox _chkEquipmentLevel;
        private readonly WinForms.CheckBox _chkConnectorCompleteness;
        private readonly WinForms.CheckBox _chkSystemData;
        private readonly WinForms.CheckBox _chkDuplicateEquipmentMarks;
        private readonly WinForms.ComboBox _cmbDrainRule;
        private readonly WinForms.ComboBox _cmbDirection;
        private readonly WinForms.NumericUpDown _numSlope;
        private readonly WinForms.NumericUpDown _numLevelTolerance;

        public MepCheckOptions Options { get; private set; }

        public MepCheckOptionsForm(Document doc, ElementId preselectedOutletId)
        {
            _preselectedOutletId = preselectedOutletId ?? ElementId.InvalidElementId;

            Text = "MEP 檢查設定";
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            Width = 760;
            Height = 680;
            MinimumSize = new Size(720, 640);
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            Font = new Font("Microsoft JhengHei UI", 10f);

            var root = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                Padding = new WinForms.Padding(18),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Percent, 100));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(new WinForms.Label
            {
                Text = "MEP 檢查",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(0, 45, 74),
                Margin = new WinForms.Padding(0, 0, 0, 4)
            }, 0, 0);

            root.Controls.Add(new WinForms.Label
            {
                Text = "檢查管線洩水方向、設備樓層分布，並保留後續擴充項目。",
                AutoSize = true,
                ForeColor = System.Drawing.Color.FromArgb(85, 100, 115),
                Margin = new WinForms.Padding(0, 0, 0, 12)
            }, 0, 1);

            var scopeGroup = BuildGroup("檢查範圍");
            var scopePanel = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Fill, AutoSize = true, WrapContents = false };
            _rbCurrentView = new WinForms.RadioButton { Text = "目前視圖", Checked = true, AutoSize = true, Margin = new WinForms.Padding(8, 8, 32, 8) };
            _rbEntireModel = new WinForms.RadioButton { Text = "整個模型", AutoSize = true, Margin = new WinForms.Padding(8, 8, 8, 8) };
            scopePanel.Controls.Add(_rbCurrentView);
            scopePanel.Controls.Add(_rbEntireModel);
            scopeGroup.Controls.Add(scopePanel);
            root.Controls.Add(scopeGroup, 0, 2);

            var content = new WinForms.TableLayoutPanel { Dock = WinForms.DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            content.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 62));
            content.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 38));
            root.Controls.Add(content, 0, 3);

            var pipeGroup = BuildGroup("管線洩水方向");
            var pipeLayout = new WinForms.TableLayoutPanel { Dock = WinForms.DockStyle.Fill, ColumnCount = 2, RowCount = 9, AutoSize = true };
            pipeLayout.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 100));
            pipeLayout.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 155));

            _chkPipeDrain = new WinForms.CheckBox { Text = "檢查管線洩水方向", Checked = true, AutoSize = true, Margin = new WinForms.Padding(8, 8, 8, 4) };
            _chkDrainageOnly = new WinForms.CheckBox { Text = "只檢查排水 / 污水 / 透氣等系統", Checked = true, AutoSize = true, Margin = new WinForms.Padding(26, 2, 8, 4) };
            _chkIgnoreFlatPipes = new WinForms.CheckBox { Text = "略過無洩水坡度的管", Checked = true, AutoSize = true, Margin = new WinForms.Padding(26, 2, 8, 8) };
            pipeLayout.Controls.Add(_chkPipeDrain, 0, 0);
            pipeLayout.SetColumnSpan(_chkPipeDrain, 2);
            pipeLayout.Controls.Add(_chkDrainageOnly, 0, 1);
            pipeLayout.SetColumnSpan(_chkDrainageOnly, 2);
            pipeLayout.Controls.Add(_chkIgnoreFlatPipes, 0, 2);
            pipeLayout.SetColumnSpan(_chkIgnoreFlatPipes, 2);

            pipeLayout.Controls.Add(CreateLabel("最小坡度 (%)"), 0, 3);
            _numSlope = new WinForms.NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 20, Increment = 0.1M, Value = 0.5M, Dock = WinForms.DockStyle.Fill, Margin = new WinForms.Padding(0, 6, 8, 6) };
            pipeLayout.Controls.Add(_numSlope, 1, 3);

            pipeLayout.Controls.Add(CreateLabel("下游判斷規則"), 0, 4);
            _cmbDrainRule = new WinForms.ComboBox { DropDownStyle = WinForms.ComboBoxStyle.DropDownList, Dock = WinForms.DockStyle.Fill, Margin = new WinForms.Padding(0, 6, 8, 6) };
            _cmbDrainRule.Items.AddRange(new object[] { "高程自動判斷", "指定排出口", "指定下游方向", "系統流向優先" });
            _cmbDrainRule.SelectedIndex = 0;
            pipeLayout.Controls.Add(_cmbDrainRule, 1, 4);

            pipeLayout.Controls.Add(CreateLabel("下游方向"), 0, 5);
            _cmbDirection = new WinForms.ComboBox { DropDownStyle = WinForms.ComboBoxStyle.DropDownList, Dock = WinForms.DockStyle.Fill, Margin = new WinForms.Padding(0, 6, 8, 6) };
            _cmbDirection.Items.AddRange(new object[] { "+X", "-X", "+Y", "-Y" });
            _cmbDirection.SelectedIndex = 0;
            pipeLayout.Controls.Add(_cmbDirection, 1, 5);

            string outletText = _preselectedOutletId != ElementId.InvalidElementId
                ? $"指定排出口：目前預選 ID {_preselectedOutletId.GetIdValue()}"
                : "指定排出口：未預選元素。可先選取管端、設備或排出口元素再啟動工具。";
            pipeLayout.Controls.Add(new WinForms.Label
            {
                Text = outletText,
                AutoSize = true,
                ForeColor = System.Drawing.Color.FromArgb(60, 90, 120),
                Margin = new WinForms.Padding(8, 4, 8, 4)
            }, 0, 6);
            pipeLayout.SetColumnSpan(pipeLayout.GetControlFromPosition(0, 6), 2);

            pipeLayout.Controls.Add(new WinForms.Label
            {
                Text = "規則說明：高程判斷只看高低點；指定排出口會要求低點朝向排出口；指定下游方向依模型座標；系統流向優先會嘗試讀取系統設備作為下游基準。",
                AutoSize = true,
                ForeColor = System.Drawing.Color.FromArgb(95, 105, 115),
                Margin = new WinForms.Padding(8, 4, 8, 8)
            }, 0, 7);
            pipeLayout.SetColumnSpan(pipeLayout.GetControlFromPosition(0, 7), 2);

            pipeGroup.Controls.Add(pipeLayout);
            content.Controls.Add(pipeGroup, 0, 0);

            var equipmentGroup = BuildGroup("設備樓層分布");
            var equipmentLayout = new WinForms.TableLayoutPanel { Dock = WinForms.DockStyle.Fill, ColumnCount = 2, RowCount = 7, AutoSize = true };
            equipmentLayout.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 100));
            equipmentLayout.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 120));
            _chkEquipmentLevel = new WinForms.CheckBox { Text = "檢查設備所在樓層", Checked = true, AutoSize = true, Margin = new WinForms.Padding(8, 8, 8, 10) };
            equipmentLayout.Controls.Add(_chkEquipmentLevel, 0, 0);
            equipmentLayout.SetColumnSpan(_chkEquipmentLevel, 2);
            equipmentLayout.Controls.Add(CreateLabel("樓層容許誤差 (mm)"), 0, 1);
            _numLevelTolerance = new WinForms.NumericUpDown { DecimalPlaces = 0, Minimum = 0, Maximum = 5000, Increment = 50, Value = 300, Dock = WinForms.DockStyle.Fill, Margin = new WinForms.Padding(0, 6, 8, 6) };
            equipmentLayout.Controls.Add(_numLevelTolerance, 1, 1);
            equipmentLayout.Controls.Add(new WinForms.Label
            {
                Text = "比對設備位置高程與指定樓層，找出可能放錯樓層的設備。",
                AutoSize = true,
                ForeColor = System.Drawing.Color.FromArgb(95, 105, 115),
                Margin = new WinForms.Padding(8, 4, 8, 8)
            }, 0, 2);
            equipmentLayout.SetColumnSpan(equipmentLayout.GetControlFromPosition(0, 2), 2);
            _chkConnectorCompleteness = new WinForms.CheckBox { Text = "檢查 Connector 連接完整性", Checked = true, AutoSize = true, Margin = new WinForms.Padding(8, 10, 8, 4) };
            equipmentLayout.Controls.Add(_chkConnectorCompleteness, 0, 3);
            equipmentLayout.SetColumnSpan(_chkConnectorCompleteness, 2);
            _chkSystemData = new WinForms.CheckBox { Text = "檢查已連接構件的系統資料", Checked = true, AutoSize = true, Margin = new WinForms.Padding(8, 4, 8, 4) };
            equipmentLayout.Controls.Add(_chkSystemData, 0, 4);
            equipmentLayout.SetColumnSpan(_chkSystemData, 2);
            _chkDuplicateEquipmentMarks = new WinForms.CheckBox { Text = "檢查設備編號重複", Checked = true, AutoSize = true, Margin = new WinForms.Padding(26, 2, 8, 4) };
            equipmentLayout.Controls.Add(_chkDuplicateEquipmentMarks, 0, 5);
            equipmentLayout.SetColumnSpan(_chkDuplicateEquipmentMarks, 2);
            equipmentLayout.Controls.Add(new WinForms.Label
            {
                Text = "Connector 檢查會略過 Logical Connector；系統資料僅檢查已有實體連接的構件。",
                AutoSize = true,
                ForeColor = System.Drawing.Color.FromArgb(95, 105, 115),
                Margin = new WinForms.Padding(8, 4, 8, 8)
            }, 0, 6);
            equipmentLayout.SetColumnSpan(equipmentLayout.GetControlFromPosition(0, 6), 2);
            equipmentGroup.Controls.Add(equipmentLayout);
            content.Controls.Add(equipmentGroup, 1, 0);

            var buttons = new WinForms.FlowLayoutPanel
            {
                FlowDirection = WinForms.FlowDirection.RightToLeft,
                Dock = WinForms.DockStyle.Fill,
                AutoSize = true,
                Margin = new WinForms.Padding(0, 14, 0, 0)
            };
            var btnCancel = new WinForms.Button { Text = "取消", Width = 96, Height = 34, DialogResult = WinForms.DialogResult.Cancel };
            var btnRun = new WinForms.Button { Text = "開始檢查", Width = 128, Height = 34 };
            btnRun.Click += Run_Click;
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnRun);
            root.Controls.Add(buttons, 0, 4);

            AcceptButton = btnRun;
            CancelButton = btnCancel;
        }

        private static WinForms.GroupBox BuildGroup(string title)
        {
            return new WinForms.GroupBox
            {
                Text = title,
                Dock = WinForms.DockStyle.Fill,
                AutoSize = true,
                Padding = new WinForms.Padding(10),
                Margin = new WinForms.Padding(0, 0, 10, 10)
            };
        }

        private static WinForms.Label CreateLabel(string text)
        {
            return new WinForms.Label { Text = text, AutoSize = true, Anchor = WinForms.AnchorStyles.Left, Margin = new WinForms.Padding(8, 10, 8, 6) };
        }

        private void Run_Click(object sender, EventArgs e)
        {
            if (!_chkPipeDrain.Checked &&
                !_chkEquipmentLevel.Checked &&
                !_chkConnectorCompleteness.Checked &&
                !_chkSystemData.Checked)
            {
                WinForms.MessageBox.Show("請至少啟用一個檢查項目。", "MEP 檢查", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            if (_chkPipeDrain.Checked && GetDrainFlowRule() == MepDrainFlowRule.OutletElement && _preselectedOutletId == ElementId.InvalidElementId)
            {
                WinForms.MessageBox.Show("使用「指定排出口」規則時，請先在模型中選取排出口或下游設備後再啟動工具。", "MEP 檢查", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            Options = new MepCheckOptions
            {
                Scope = _rbEntireModel.Checked ? MepCheckScope.EntireModel : MepCheckScope.CurrentView,
                CheckPipeDrainDirection = _chkPipeDrain.Checked,
                CheckEquipmentLevelDistribution = _chkEquipmentLevel.Checked,
                CheckConnectorCompleteness = _chkConnectorCompleteness.Checked,
                CheckSystemData = _chkSystemData.Checked,
                CheckDuplicateEquipmentMarks = _chkSystemData.Checked && _chkDuplicateEquipmentMarks.Checked,
                DrainageSystemOnly = _chkDrainageOnly.Checked,
                IgnoreFlatPipes = _chkIgnoreFlatPipes.Checked,
                MinimumDrainSlopePercent = (double)_numSlope.Value,
                EquipmentLevelToleranceMm = (double)_numLevelTolerance.Value,
                DrainFlowRule = GetDrainFlowRule(),
                DownstreamDirection = GetDownstreamDirection(),
                OutletElementId = _preselectedOutletId
            };

            DialogResult = WinForms.DialogResult.OK;
            Close();
        }

        private MepDrainFlowRule GetDrainFlowRule()
        {
            switch (_cmbDrainRule.SelectedIndex)
            {
                case 1: return MepDrainFlowRule.OutletElement;
                case 2: return MepDrainFlowRule.DownstreamDirection;
                case 3: return MepDrainFlowRule.SystemFlow;
                default: return MepDrainFlowRule.Elevation;
            }
        }

        private MepDownstreamDirection GetDownstreamDirection()
        {
            switch (_cmbDirection.SelectedIndex)
            {
                case 1: return MepDownstreamDirection.NegativeX;
                case 2: return MepDownstreamDirection.PositiveY;
                case 3: return MepDownstreamDirection.NegativeY;
                default: return MepDownstreamDirection.PositiveX;
            }
        }
    }
}
