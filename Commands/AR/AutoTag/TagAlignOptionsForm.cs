using System;
using System.Drawing;
using System.Globalization;
using Forms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class TagAlignOptionsForm : Forms.Form
    {
        private readonly Forms.ComboBox _scopeCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _modeCombo = new Forms.ComboBox();
        private readonly Forms.ComboBox _baseCombo = new Forms.ComboBox();
        private readonly Forms.TextBox _spacingText = new Forms.TextBox();
        private readonly Forms.CheckBox _avoidOverlapCheck = new Forms.CheckBox();

        public TagAlignOptionsForm()
        {
            Text = "標籤輔助對齊";
            Width = 420;
            Height = 260;
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = Forms.FormStartPosition.CenterScreen;
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            BuildLayout();
        }

        public TagAlignOptions Options { get; private set; }

        private void BuildLayout()
        {
            var root = new Forms.TableLayoutPanel
            {
                Dock = Forms.DockStyle.Fill,
                Padding = new Forms.Padding(14),
                ColumnCount = 2,
                RowCount = 6
            };
            root.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 96));
            root.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 34));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 34));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 34));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 34));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 34));
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
            Controls.Add(root);

            AddLabel(root, "作用範圍", 0);
            ConfigureCombo(_scopeCombo, new[] { "目前選取", "目前視圖" }, 0);
            root.Controls.Add(_scopeCombo, 1, 0);

            AddLabel(root, "對齊方式", 1);
            ConfigureCombo(_modeCombo, new[] { "水平對齊", "垂直對齊", "等距水平", "等距垂直" }, 0);
            root.Controls.Add(_modeCombo, 1, 1);

            AddLabel(root, "對齊基準", 2);
            ConfigureCombo(_baseCombo, new[] { "平均位置", "第一個標籤" }, 0);
            root.Controls.Add(_baseCombo, 1, 2);

            AddLabel(root, "間距 mm", 3);
            _spacingText.Text = "300";
            _spacingText.Dock = Forms.DockStyle.Fill;
            root.Controls.Add(_spacingText, 1, 3);

            _avoidOverlapCheck.Text = "避免標籤互相重疊";
            _avoidOverlapCheck.Checked = true;
            _avoidOverlapCheck.Dock = Forms.DockStyle.Fill;
            root.Controls.Add(_avoidOverlapCheck, 1, 4);

            var buttons = new Forms.FlowLayoutPanel
            {
                Dock = Forms.DockStyle.Fill,
                FlowDirection = Forms.FlowDirection.RightToLeft,
                WrapContents = false
            };
            root.Controls.Add(buttons, 0, 5);
            root.SetColumnSpan(buttons, 2);

            var cancel = new Forms.Button
            {
                Text = "取消",
                Width = 86,
                Height = 30,
                DialogResult = Forms.DialogResult.Cancel
            };
            buttons.Controls.Add(cancel);

            var ok = new Forms.Button
            {
                Text = "執行",
                Width = 86,
                Height = 30,
                DialogResult = Forms.DialogResult.OK
            };
            ok.Click += OkClicked;
            buttons.Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OkClicked(object sender, EventArgs e)
        {
            if (!double.TryParse(_spacingText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double spacing) ||
                spacing < 0 ||
                spacing > 10000)
            {
                Forms.MessageBox.Show("間距請輸入 0 到 10000 mm 之間的數值。", Text, Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
                DialogResult = Forms.DialogResult.None;
                return;
            }

            Options = new TagAlignOptions
            {
                Scope = _scopeCombo.SelectedIndex == 1 ? TagAlignScope.ActiveView : TagAlignScope.Selection,
                Mode = IndexToMode(_modeCombo.SelectedIndex),
                Base = _baseCombo.SelectedIndex == 1 ? TagAlignBase.First : TagAlignBase.Average,
                SpacingMillimeters = spacing,
                AvoidOverlap = _avoidOverlapCheck.Checked
            };
        }

        private static TagAlignMode IndexToMode(int index)
        {
            switch (index)
            {
                case 1:
                    return TagAlignMode.Vertical;
                case 2:
                    return TagAlignMode.DistributeHorizontal;
                case 3:
                    return TagAlignMode.DistributeVertical;
                default:
                    return TagAlignMode.Horizontal;
            }
        }

        private static void ConfigureCombo(Forms.ComboBox comboBox, string[] items, int selectedIndex)
        {
            comboBox.Dock = Forms.DockStyle.Fill;
            comboBox.DropDownStyle = Forms.ComboBoxStyle.DropDownList;
            comboBox.Items.AddRange(items);
            comboBox.SelectedIndex = selectedIndex;
        }

        private static void AddLabel(Forms.TableLayoutPanel grid, string text, int row)
        {
            grid.Controls.Add(new Forms.Label
            {
                Text = text,
                Dock = Forms.DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);
        }
    }
}
