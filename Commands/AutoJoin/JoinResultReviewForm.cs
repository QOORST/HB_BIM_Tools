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
    private readonly TextBox _detailBox = new();

    private readonly List<JoinFailureDetail> _failures = new();

    public JoinResultReviewForm(RevitNavigationRequestHandler navigationHandler, Autodesk.Revit.UI.ExternalEvent navigationEvent)
    {
        _navigationHandler = navigationHandler;
        _navigationEvent = navigationEvent;

        Text = "自動接合結果清單";
        Width = 920;
        Height = 700;
        MinimumSize = new Size(760, 560);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft JhengHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        TopMost = false;

        BuildUi();
        _navigationHandler.StatusChanged += SetNavigationStatus;
        _navigationHandler.DiagnosticCompleted += SetDiagnostic;
        FormClosed += (_, _) => _navigationHandler.DiagnosticCompleted -= SetDiagnostic;
        FormClosed += (_, _) => _navigationHandler.StatusChanged -= SetNavigationStatus;
    }

    private void SetNavigationStatus(string message)
    {
        if (!IsDisposed) _hintLabel.Text = message;
    }

    private void SetDiagnostic(JoinFailureDetail item, string message)
    {
        if (IsDisposed || !ReferenceEquals(_failureList.SelectedItem, item)) return;
        _detailBox.Text = $"構件 A：{item.FirstElementId}　構件 B：{item.SecondElementId}\r\n" +
            "唯讀診斷（目前模型）：" + message + "\r\n\r\n原始訊息：\r\n" + item.Description;
    }

    public void LoadResult(string title, string summaryText, IList<JoinFailureDetail> failures)
    {
        Text = title + " - 問題清單";
        _summaryBox.Text = summaryText;

        _failures.Clear();
        _failureList.Items.Clear();

        _failureList.BeginUpdate();
        foreach (var item in failures)
        {
            _failures.Add(item);
            _failureList.Items.Add(item);
        }
        _failureList.EndUpdate();

        if (_failureList.Items.Count > 0)
        {
            _failureList.SelectedIndex = 0;
            _hintLabel.Text = "尚未定位";
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
            RowCount = 4,
            Padding = new Padding(12)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 43f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var summaryGroup = new GroupBox { Dock = DockStyle.Fill, Text = "執行摘要" };
        _summaryBox.Dock = DockStyle.Fill;
        _summaryBox.Multiline = true;
        _summaryBox.ReadOnly = true;
        _summaryBox.ScrollBars = ScrollBars.Vertical;
        summaryGroup.Controls.Add(_summaryBox);

        var failureGroup = new GroupBox { Dock = DockStyle.Fill, Text = "問題清單（完整失敗紀錄）" };
        _failureList.Dock = DockStyle.Fill;
        _failureList.HorizontalScrollbar = true;
        failureGroup.Controls.Add(_failureList);
        var detailGroup = new GroupBox { Dock = DockStyle.Fill, Text = "選取項目詳細資料" };
        _detailBox.Dock = DockStyle.Fill;
        _detailBox.Multiline = true;
        _detailBox.ReadOnly = true;
        _detailBox.ScrollBars = ScrollBars.Vertical;
        detailGroup.Controls.Add(_detailBox);
        _failureList.SelectedIndexChanged += (_, _) => {
            var item = _failureList.SelectedItem as JoinFailureDetail;
            _detailBox.Text = item == null ? string.Empty :
                $"構件 A：{item.FirstElementId}　構件 B：{item.SecondElementId}\r\n" +
                "目前訊息不足以判定建模錯誤或確切幾何原因。\r\n\r\n原始訊息：\r\n" + item.Description;
            _hintLabel.Text = "尚未定位此項目";
        };

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var closeButton = new Button { Text = "關閉", Width = 110, Height = 30 };
        var showButton = new Button { Text = "定位到 3D", Width = 130, Height = 30 };
        var showA = new Button { Text = "定位 A", AutoSize = true };
        var showB = new Button { Text = "定位 B", AutoSize = true };
        var diagnose = new Button { Text = "檢查幾何", AutoSize = true };
        diagnose.Click += (_, _) => {
            if (_failureList.SelectedItem is not JoinFailureDetail item) return;
            _navigationHandler.RequestDiagnostic(item);
            var status = _navigationEvent.Raise();
            _hintLabel.Text = status == Autodesk.Revit.UI.ExternalEventRequest.Accepted || status == Autodesk.Revit.UI.ExternalEventRequest.Pending
                ? "幾何診斷已排入 Revit。" : "Revit 未接受診斷請求，請結束目前指令後重試。";
        };
        _hintLabel.AutoSize = true;
        _hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        _hintLabel.Padding = new Padding(0, 8, 16, 0);
        _hintLabel.Text = "尚未定位";
        root.SizeChanged += (_, _) => _hintLabel.MaximumSize = new Size(System.Math.Max(120, root.ClientSize.Width - 48), 0);

        closeButton.Click += (_, _) => Close();
        showButton.Click += (_, _) => NavigateSelectedFailureIn3D();
        showA.Click += (_, _) => NavigateSelectedFailureIn3D(1);
        showB.Click += (_, _) => NavigateSelectedFailureIn3D(2);

        actionPanel.Controls.Add(closeButton);
        actionPanel.Controls.Add(showButton);
        actionPanel.Controls.Add(showB);
        actionPanel.Controls.Add(showA);
        actionPanel.Controls.Add(diagnose);
        actionPanel.Controls.Add(_hintLabel);

        root.Controls.Add(summaryGroup, 0, 0);
        root.Controls.Add(failureGroup, 0, 1);
        root.Controls.Add(detailGroup, 0, 2);
        root.Controls.Add(actionPanel, 0, 3);

        Controls.Add(root);
    }

    private void NavigateSelectedFailureIn3D(int target = 0)
    {
        if (_failureList.SelectedItem is not JoinFailureDetail selected)
        {
            MessageBox.Show("請先選擇一筆失敗項目。", "Auto Join", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var ids = new List<ElementId>();
        if (target != 2 && selected.FirstElementId != null && selected.FirstElementId != ElementId.InvalidElementId)
        {
            ids.Add(selected.FirstElementId);
        }

        if (target != 1 && selected.SecondElementId != null && selected.SecondElementId != ElementId.InvalidElementId)
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
        var status = _navigationEvent.Raise();
        if (status == Autodesk.Revit.UI.ExternalEventRequest.Accepted || status == Autodesk.Revit.UI.ExternalEventRequest.Pending)
            _hintLabel.Text = "定位請求已排入 Revit，等待處理。";
        else
            MessageBox.Show("Revit 未接受定位請求（" + status + "），請結束目前指令後重試。", "自動接合定位", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    }
}
