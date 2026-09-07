using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class JoinResultReviewForm : System.Windows.Forms.Form
{
    private readonly RevitNavigationRequestHandler _navigationHandler;
    private readonly Autodesk.Revit.UI.ExternalEvent _navigationEvent;
    private readonly ListBox _failureList = new();
    private readonly TextBox _summaryBox = new();
    private readonly Label _hintLabel = new();

    private readonly List<JoinFailureDetail> _failures = new();

    public JoinResultReviewForm(RevitNavigationRequestHandler navigationHandler, Autodesk.Revit.UI.ExternalEvent navigationEvent)
    {
        _navigationHandler = navigationHandler;
        _navigationEvent = navigationEvent;

        Text = "自動接合結果清單";
        Width = 920;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft JhengHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        TopMost = false;

        BuildUi();
    }

    public void LoadResult(string title, string summaryText, IList<JoinFailureDetail> failures)
    {
        Text = title + " - 問題清單";
        _summaryBox.Text = summaryText;

        _failures.Clear();
        _failureList.Items.Clear();

        foreach (var item in failures)
        {
            _failures.Add(item);
            _failureList.Items.Add(item);
        }

        if (_failureList.Items.Count > 0)
        {
            _failureList.SelectedIndex = 0;
            _hintLabel.Text = "提示: 可持續操作 Revit，點選項目後按「定位到 3D」檢視。";
        }
        else
        {
            _hintLabel.Text = "目前沒有失敗項目。";
        }
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 260f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));

        var summaryGroup = new GroupBox { Dock = DockStyle.Fill, Text = "執行摘要" };
        _summaryBox.Dock = DockStyle.Fill;
        _summaryBox.Multiline = true;
        _summaryBox.ReadOnly = true;
        _summaryBox.ScrollBars = ScrollBars.Vertical;
        summaryGroup.Controls.Add(_summaryBox);

        var failureGroup = new GroupBox { Dock = DockStyle.Fill, Text = "失敗清單（選一筆可跳轉 3D）" };
        _failureList.Dock = DockStyle.Fill;
        _failureList.HorizontalScrollbar = true;
        failureGroup.Controls.Add(_failureList);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var closeButton = new Button { Text = "關閉", Width = 110, Height = 30 };
        var showButton = new Button { Text = "定位到 3D", Width = 130, Height = 30 };
        _hintLabel.AutoSize = true;
        _hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        _hintLabel.Padding = new Padding(0, 8, 16, 0);
        _hintLabel.Text = "提示: 可持續操作 Revit，點選項目後按「定位到 3D」檢視。";

        closeButton.Click += (_, _) => Close();
        showButton.Click += (_, _) => NavigateSelectedFailureIn3D();

        actionPanel.Controls.Add(_hintLabel);
        actionPanel.Controls.Add(closeButton);
        actionPanel.Controls.Add(showButton);

        root.Controls.Add(summaryGroup, 0, 0);
        root.Controls.Add(failureGroup, 0, 1);
        root.Controls.Add(actionPanel, 0, 2);

        Controls.Add(root);
    }

    private void NavigateSelectedFailureIn3D()
    {
        if (_failureList.SelectedItem is not JoinFailureDetail selected)
        {
            MessageBox.Show("請先選擇一筆失敗項目。", "Auto Join", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var ids = new List<ElementId>();
        if (selected.FirstElementId != null && selected.FirstElementId != ElementId.InvalidElementId)
        {
            ids.Add(selected.FirstElementId);
        }

        if (selected.SecondElementId != null && selected.SecondElementId != ElementId.InvalidElementId)
        {
            ids.Add(selected.SecondElementId);
        }

        ids = ids.Distinct().ToList();
        if (ids.Count == 0)
        {
            MessageBox.Show("此項目沒有可定位元素。", "Auto Join", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _navigationHandler.RequestShowElements(ids);
        _navigationEvent.Raise();
    }
    }
}
