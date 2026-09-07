using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers;
using DrawingColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP.MepCheck
{
    internal sealed class MepCheckResultsForm : WinForms.Form
    {
        private MepCheckResult _result;
        private readonly MepCheckNavigationHandler _navigationHandler;
        private readonly ExternalEvent _navigationEvent;
        private readonly WinForms.DataGridView _grid;
        private readonly WinForms.ComboBox _severityFilter;
        private readonly WinForms.Label _summary;
        private readonly WinForms.Button _btnFix;
        private readonly WinForms.Button _btnRecheck;

        public MepCheckResultsForm(
            MepCheckResult result,
            MepCheckNavigationHandler navigationHandler,
            ExternalEvent navigationEvent)
        {
            _result = result;
            _navigationHandler = navigationHandler;
            _navigationEvent = navigationEvent;

            Text = "MEP 檢查結果";
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            Width = 1360;
            Height = 760;
            MinimumSize = new Size(1040, 600);
            Font = new Font("Microsoft JhengHei UI", 9.5f);

            var root = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new WinForms.Padding(16)
            };
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Percent, 100));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(new WinForms.Label
            {
                Text = "MEP 檢查結果",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
                ForeColor = DrawingColor.FromArgb(0, 45, 74),
                Margin = new WinForms.Padding(0, 0, 0, 6)
            }, 0, 0);

            var toolbar = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true,
                Margin = new WinForms.Padding(0, 0, 0, 10)
            };
            toolbar.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 130));

            _summary = new WinForms.Label
            {
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left,
                ForeColor = DrawingColor.FromArgb(55, 78, 102),
                Margin = new WinForms.Padding(0, 6, 20, 0)
            };
            toolbar.Controls.Add(_summary, 0, 0);
            toolbar.Controls.Add(new WinForms.Label
            {
                Text = "篩選：",
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Right,
                Margin = new WinForms.Padding(0, 6, 4, 0)
            }, 1, 0);

            _severityFilter = new WinForms.ComboBox
            {
                DropDownStyle = WinForms.ComboBoxStyle.DropDownList,
                Dock = WinForms.DockStyle.Fill
            };
            _severityFilter.Items.AddRange(new object[] { "全部", "錯誤", "警告", "資訊", "通過" });
            _severityFilter.SelectedIndex = 0;
            _severityFilter.SelectedIndexChanged += (s, e) => BindGrid();
            toolbar.Controls.Add(_severityFilter, 2, 0);
            root.Controls.Add(toolbar, 0, 1);

            _grid = new WinForms.DataGridView
            {
                Dock = WinForms.DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = WinForms.DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                BackgroundColor = DrawingColor.White,
                BorderStyle = WinForms.BorderStyle.FixedSingle,
                AutoSizeRowsMode = WinForms.DataGridViewAutoSizeRowsMode.None,
                ColumnHeadersHeightSizeMode = WinForms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 34
            };
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) RaiseNavigation(MepCheckNavigationAction.SelectAndZoom);
            };
            ConfigureGrid();
            root.Controls.Add(_grid, 0, 2);

            var buttons = new WinForms.FlowLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                FlowDirection = WinForms.FlowDirection.RightToLeft,
                AutoSize = true,
                Margin = new WinForms.Padding(0, 12, 0, 0)
            };
            var btnClose = new WinForms.Button { Text = "關閉", Width = 96, Height = 34 };
            var btnZoom = new WinForms.Button { Text = "跳轉元素", Width = 112, Height = 34 };
            var btnSelect = new WinForms.Button { Text = "選取元素", Width = 112, Height = 34 };
            var btnCopy = new WinForms.Button { Text = "複製結果", Width = 112, Height = 34 };
            var btnExport = new WinForms.Button { Text = "匯出 CSV", Width = 112, Height = 34 };
            _btnFix = new WinForms.Button { Text = "修復選取", Width = 112, Height = 34 };
            _btnRecheck = new WinForms.Button { Text = "重新檢查", Width = 112, Height = 34 };
            btnClose.Click += (s, e) => Close();
            btnSelect.Click += (s, e) => RaiseNavigation(MepCheckNavigationAction.Select);
            btnZoom.Click += (s, e) => RaiseNavigation(MepCheckNavigationAction.SelectAndZoom);
            btnCopy.Click += (s, e) => CopyResults();
            btnExport.Click += (s, e) => ExportCsv();
            _btnFix.Click += (s, e) => FixSelected();
            _btnRecheck.Click += (s, e) => Recheck();
            buttons.Controls.Add(btnClose);
            buttons.Controls.Add(btnZoom);
            buttons.Controls.Add(btnSelect);
            buttons.Controls.Add(btnCopy);
            buttons.Controls.Add(btnExport);
            buttons.Controls.Add(_btnFix);
            buttons.Controls.Add(_btnRecheck);
            root.Controls.Add(buttons, 0, 3);

            FormClosed += (s, e) => _navigationEvent.Dispose();
            BindGrid();
        }

        private void ConfigureGrid()
        {
            AddColumn("狀態", "SeverityText", 72, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("檢查項目", "CheckText", 112, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("ID", "ElementIdText", 72, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("類別", "Category", 118, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("元素名稱", "ElementName", 180, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("樓層", "LevelName", 96, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("系統", "SystemName", 128, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("目前值", "CurrentValue", 280, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("預期值", "ExpectedValue", 210, WinForms.DataGridViewAutoSizeColumnMode.None);
            AddColumn("說明", "Message", 280, WinForms.DataGridViewAutoSizeColumnMode.Fill);
        }

        private void AddColumn(string header, string property, int width, WinForms.DataGridViewAutoSizeColumnMode mode)
        {
            _grid.Columns.Add(new WinForms.DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = property,
                Width = width,
                MinimumWidth = Math.Min(width, 90),
                AutoSizeMode = mode,
                SortMode = WinForms.DataGridViewColumnSortMode.Automatic
            });
        }

        private void BindGrid()
        {
            List<RowItem> rows = _result.Issues
                .Where(MatchesFilter)
                .Select(RowItem.FromIssue)
                .ToList();

            _grid.DataSource = rows;
            foreach (WinForms.DataGridViewRow row in _grid.Rows)
            {
                if (!(row.DataBoundItem is RowItem item)) continue;
                row.DefaultCellStyle.ForeColor = GetColor(item.Severity);
                row.DefaultCellStyle.WrapMode = WinForms.DataGridViewTriState.False;
                if (item.Severity == MepCheckSeverity.Error || item.Severity == MepCheckSeverity.Warning)
                {
                    row.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                }
            }

            int pass = _result.Issues.Count(x => x.Severity == MepCheckSeverity.Pass);
            int warn = _result.Issues.Count(x => x.Severity == MepCheckSeverity.Warning);
            int error = _result.Issues.Count(x => x.Severity == MepCheckSeverity.Error);
            int info = _result.Issues.Count(x => x.Severity == MepCheckSeverity.Info);
            _summary.Text = $"管線 {_result.PipeCount} 支，設備 {_result.EquipmentCount} 個，Connector 構件 {_result.ConnectorElementCount} 個，系統資料構件 {_result.SystemDataElementCount} 個。通過 {pass}、資訊 {info}、警告 {warn}、錯誤 {error}";
        }

        private bool MatchesFilter(MepCheckIssue issue)
        {
            string selected = _severityFilter.SelectedItem?.ToString() ?? "全部";
            switch (selected)
            {
                case "錯誤": return issue.Severity == MepCheckSeverity.Error;
                case "警告": return issue.Severity == MepCheckSeverity.Warning;
                case "資訊": return issue.Severity == MepCheckSeverity.Info;
                case "通過": return issue.Severity == MepCheckSeverity.Pass;
                default: return true;
            }
        }

        private void RaiseNavigation(MepCheckNavigationAction action)
        {
            List<ElementId> ids = _grid.SelectedRows
                .Cast<WinForms.DataGridViewRow>()
                .Select(r => r.DataBoundItem as RowItem)
                .Where(r => r != null && r.ElementId != ElementId.InvalidElementId)
                .Select(r => r.ElementId)
                .Distinct(new ElementIdComparer())
                .ToList();

            if (!ids.Any())
            {
                WinForms.MessageBox.Show("請先選取要定位的檢查結果。", "MEP 檢查", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            _navigationHandler.Request(ids, action);
            _navigationEvent.Raise();
        }

        private void FixSelected()
        {
            List<RowItem> rows = _grid.SelectedRows
                .Cast<WinForms.DataGridViewRow>()
                .Select(row => row.DataBoundItem as RowItem)
                .Where(row => row != null &&
                    row.CheckKey.Equals("SystemData.DuplicateMark", StringComparison.OrdinalIgnoreCase) &&
                    row.ElementId != ElementId.InvalidElementId)
                .ToList();
            if (rows.Count == 0)
            {
                WinForms.MessageBox.Show(
                    "目前只支援自動修復「設備編號重複」。請先選取對應結果列。",
                    "MEP 檢查",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Information);
                return;
            }

            if (WinForms.MessageBox.Show(
                    $"將修復選取的 {rows.Count} 筆重複設備編號。\n同組 ID 最小的第一筆會保留，其餘改為 -02、-03…，是否繼續？",
                    "MEP 檢查",
                    WinForms.MessageBoxButtons.YesNo,
                    WinForms.MessageBoxIcon.Question) != WinForms.DialogResult.Yes)
            {
                return;
            }

            SetRequestPending();
            _navigationHandler.Request(
                rows.Select(row => row.ElementId),
                MepCheckNavigationAction.FixDuplicateMarks);
            if (_navigationEvent.Raise() != ExternalEventRequest.Accepted)
            {
                SetRequestCompleted("上一個 Revit 動作仍在處理中，請稍候。", true);
            }
        }

        private void Recheck()
        {
            SetRequestPending();
            _navigationHandler.RequestRecheck();
            if (_navigationEvent.Raise() != ExternalEventRequest.Accepted)
            {
                SetRequestCompleted("上一個 Revit 動作仍在處理中，請稍候。", true);
            }
        }

        private void SetRequestPending()
        {
            _btnFix.Enabled = false;
            _btnRecheck.Enabled = false;
            _summary.Text = "正在等候 Revit 執行...";
        }

        public void SetRequestCompleted(string status, bool isError)
        {
            if (IsDisposed)
            {
                return;
            }

            _btnFix.Enabled = true;
            _btnRecheck.Enabled = true;
            _summary.ForeColor = isError
                ? DrawingColor.FromArgb(190, 35, 35)
                : DrawingColor.FromArgb(55, 78, 102);
            _summary.Text = status;
        }

        public void UpdateResult(MepCheckResult result, string status)
        {
            if (IsDisposed)
            {
                return;
            }

            _result = result;
            _btnFix.Enabled = true;
            _btnRecheck.Enabled = true;
            _summary.ForeColor = DrawingColor.FromArgb(55, 78, 102);
            BindGrid();
            Text = $"MEP 檢查結果 - {status}";
        }

        private void CopyResults()
        {
            IEnumerable<RowItem> rows = (_grid.DataSource as IEnumerable<RowItem>) ?? Enumerable.Empty<RowItem>();
            string text = string.Join(Environment.NewLine, rows.Select(r =>
                $"{r.SeverityText}\t{r.CheckText}\t{r.ElementIdText}\t{r.Category}\t{r.ElementName}\t{r.LevelName}\t{r.SystemName}\t{r.CurrentValue}\t{r.ExpectedValue}\t{r.Message}"));
            if (!string.IsNullOrWhiteSpace(text))
            {
                WinForms.Clipboard.SetText(text);
            }
        }

        private void ExportCsv()
        {
            IEnumerable<RowItem> rows = (_grid.DataSource as IEnumerable<RowItem>) ?? Enumerable.Empty<RowItem>();
            using (var dialog = new WinForms.SaveFileDialog
            {
                Title = "匯出 MEP 檢查報告",
                Filter = "CSV 檔案 (*.csv)|*.csv",
                FileName = $"MEP_Check_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            })
            {
                if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return;
                }

                var lines = new List<string>
                {
                    string.Join(",", new[]
                    {
                        "嚴重度", "檢查項目", "元素ID", "類別", "元素名稱",
                        "樓層", "系統", "目前值", "預期值", "說明"
                    }.Select(EscapeCsv))
                };
                lines.AddRange(rows.Select(row => string.Join(",", new[]
                {
                    row.SeverityText,
                    row.CheckText,
                    row.ElementIdText,
                    row.Category,
                    row.ElementName,
                    row.LevelName,
                    row.SystemName,
                    row.CurrentValue,
                    row.ExpectedValue,
                    row.Message
                }.Select(EscapeCsv))));

                File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
                WinForms.MessageBox.Show(
                    $"已匯出 {lines.Count - 1} 筆檢查結果。",
                    "MEP 檢查",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Information);
            }
        }

        private static string EscapeCsv(string value)
        {
            string text = value ?? string.Empty;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static DrawingColor GetColor(MepCheckSeverity severity)
        {
            switch (severity)
            {
                case MepCheckSeverity.Error: return DrawingColor.FromArgb(190, 35, 35);
                case MepCheckSeverity.Warning: return DrawingColor.FromArgb(196, 105, 0);
                case MepCheckSeverity.Pass: return DrawingColor.FromArgb(20, 125, 65);
                default: return DrawingColor.FromArgb(45, 62, 80);
            }
        }

        private sealed class RowItem
        {
            public MepCheckSeverity Severity { get; set; }
            public string CheckKey { get; set; }
            public ElementId ElementId { get; set; }
            public string SeverityText { get; set; }
            public string CheckText { get; set; }
            public string ElementIdText { get; set; }
            public string Category { get; set; }
            public string ElementName { get; set; }
            public string LevelName { get; set; }
            public string SystemName { get; set; }
            public string CurrentValue { get; set; }
            public string ExpectedValue { get; set; }
            public string Message { get; set; }

            public static RowItem FromIssue(MepCheckIssue issue)
            {
                return new RowItem
                {
                    Severity = issue.Severity,
                    CheckKey = issue.CheckKey,
                    ElementId = issue.ElementId,
                    SeverityText = GetSeverityText(issue.Severity),
                    CheckText = GetCheckText(issue.CheckKey),
                    ElementIdText = issue.ElementId == ElementId.InvalidElementId ? string.Empty : issue.ElementId.GetIdValue().ToString(),
                    Category = issue.Category,
                    ElementName = issue.ElementName,
                    LevelName = issue.LevelName,
                    SystemName = issue.SystemName,
                    CurrentValue = issue.CurrentValue,
                    ExpectedValue = issue.ExpectedValue,
                    Message = issue.Message
                };
            }

            private static string GetSeverityText(MepCheckSeverity severity)
            {
                switch (severity)
                {
                    case MepCheckSeverity.Error: return "錯誤";
                    case MepCheckSeverity.Warning: return "警告";
                    case MepCheckSeverity.Pass: return "通過";
                    default: return "資訊";
                }
            }

            private static string GetCheckText(string key)
            {
                if (key.StartsWith("PipeDrain", StringComparison.OrdinalIgnoreCase)) return "洩水方向";
                if (key.StartsWith("Equipment", StringComparison.OrdinalIgnoreCase)) return "設備樓層";
                if (key.StartsWith("Connector", StringComparison.OrdinalIgnoreCase)) return "Connector 連接";
                if (key.StartsWith("SystemData.DuplicateMark", StringComparison.OrdinalIgnoreCase)) return "設備編號重複";
                if (key.StartsWith("SystemData", StringComparison.OrdinalIgnoreCase)) return "系統資料";
                return key;
            }
        }

        private sealed class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.GetIdValue() == y.GetIdValue();
            }

            public int GetHashCode(ElementId obj)
            {
                return obj?.GetIdValue().GetHashCode() ?? 0;
            }
        }
    }
}
