using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YDBIM.AutoDimension.UI
{
internal sealed class LinkedDimensionSourceForm : Form
{
    private readonly ComboBox _source;
    private readonly ComboBox _category;
    private readonly Button _ok;

    internal int SourceIndex => _source.SelectedIndex;
    internal int CategoryIndex => _category.SelectedIndex;

    internal LinkedDimensionSourceForm(IReadOnlyList<string> sources, int sourceIndex, int categoryIndex, bool lockCategory)
    {
        SuspendLayout();
        Text = "連結模型標註";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 160);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowOnly;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 3,
            Padding = new Padding(16), Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _source = CreateCombo("Source");
        foreach (string source in sources) _source.Items.Add(source);
        _source.SelectedIndex = sourceIndex >= 0 && sourceIndex < sources.Count ? sourceIndex : -1;
        _category = CreateCombo("Category");
        _category.Items.AddRange(new object[] { "柱", "軸線", "梁" });
        _category.SelectedIndex = categoryIndex >= 0 && categoryIndex < _category.Items.Count ? categoryIndex : -1;
        _category.Enabled = !lockCategory;
        layout.Controls.Add(CreateLabel("連結來源"), 0, 0);
        layout.Controls.Add(_source, 1, 0);
        layout.Controls.Add(CreateLabel("標註類別"), 0, 1);
        layout.Controls.Add(_category, 1, 1);

        var actions = new FlowLayoutPanel {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        _ok = CreateButton("確定", DialogResult.OK);
        var cancel = CreateButton("取消", DialogResult.Cancel);
        cancel.Margin = Padding.Empty;
        actions.Controls.Add(_ok);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 2);
        layout.SetColumnSpan(actions, 2);
        Controls.Add(layout);
        AcceptButton = _ok;
        CancelButton = cancel;
        _source.SelectedIndexChanged += (_, __) => UpdateConfirmation();
        _category.SelectedIndexChanged += (_, __) => UpdateConfirmation();
        FormClosing += (_, e) => {
            if (DialogResult == DialogResult.OK && !HasSelection) e.Cancel = true;
        };
        UpdateConfirmation();
        ResumeLayout(true);
    }

    private bool HasSelection => SourceIndex >= 0 && CategoryIndex >= 0;
    private void UpdateConfirmation() => _ok.Enabled = HasSelection;

    private static ComboBox CreateCombo(string name) => new ComboBox {
        Name = name, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(0, 0, 0, 10)
    };

    private static Label CreateLabel(string text) => new Label {
        Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 0, 12, 10)
    };

    private static Button CreateButton(string text, DialogResult result) => new Button {
        Text = text, DialogResult = result, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 28),
        Margin = new Padding(0, 0, 8, 0)
    };
}
}
