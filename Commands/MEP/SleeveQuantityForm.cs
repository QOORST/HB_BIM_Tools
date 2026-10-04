using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Autodesk.Revit.DB;
using F = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveQuantityForm : F.Form
    {
        private readonly Document _doc;
        private readonly HashSet<long> _selected;
        private readonly F.ComboBox _level = new F.ComboBox { DropDownStyle = F.ComboBoxStyle.DropDownList };
        private readonly F.ComboBox _kind = new F.ComboBox { DropDownStyle = F.ComboBoxStyle.DropDownList };
        private readonly F.ComboBox _sheet = new F.ComboBox { DropDownStyle = F.ComboBoxStyle.DropDownList };
        private readonly F.TextBox _zone = new F.TextBox();
        private readonly F.CheckBox _manual = new F.CheckBox { AutoSize = true };
        private readonly F.NumericUpDown _left = new F.NumericUpDown { Minimum = 0, Maximum = 5000, Value = 20 };
        private readonly F.NumericUpDown _top = new F.NumericUpDown { Minimum = 0, Maximum = 5000, Value = 20 };
        private readonly F.Label _summary = new F.Label { AutoSize = true };
        private readonly F.DataGridView _grid = new F.DataGridView { Dock = F.DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AutoGenerateColumns = true, AutoSizeColumnsMode = F.DataGridViewAutoSizeColumnsMode.DisplayedCells,
            RowHeadersVisible = false, BackgroundColor = System.Drawing.Color.White };
        private readonly F.Button _ok = new F.Button { Text = "建立／更新", AutoSize = true };
        private readonly bool _issue;
        private readonly Action<SleeveIssueRequest> _execute;
        private readonly F.TextBox _baseline = new F.TextBox { Width = 290, MaxLength = 80 };
        private readonly Dictionary<long, string> _controls = new Dictionary<long, string>();
        private readonly F.DataGridView _review = new F.DataGridView { Dock = F.DockStyle.Fill, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = System.Drawing.Color.White, AutoSizeColumnsMode = F.DataGridViewAutoSizeColumnsMode.DisplayedCells };
        private bool _fillingReview;
        private List<SleeveQuantityRow> _rows;
        internal SleeveQuantityOptions Options { get; private set; }
        private sealed class Choice
        {
            internal string Key; internal string Name; internal ElementId Id;
            public override string ToString() => Name;
        }

        internal SleeveQuantityForm(Document doc, IEnumerable<long> selected, bool issue = false, Action<SleeveIssueRequest> execute = null)
        {
            _doc = doc; _selected = new HashSet<long>(selected); _issue = issue; _execute = execute;
            Text = "套管數量表／放入圖紙"; Width = 1000; Height = 700; MinimumSize = new Size(740, 560);
            if (_issue) { Text = "套管發圖作業"; Height = 780; MinimumSize = new Size(900, 680); _ok.Text = "建立表格並存基準"; }
            AutoScaleMode = F.AutoScaleMode.Dpi; StartPosition = F.FormStartPosition.CenterParent;
            Font = new Font("Microsoft JhengHei UI", 10);
            int rowCount = _issue ? 7 : 6;
            var root = new F.TableLayoutPanel { Dock = F.DockStyle.Fill, Padding = new F.Padding(12), ColumnCount = 1, RowCount = rowCount };
            root.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent, 100));
            for (int i = 0; i < rowCount; i++) root.RowStyles.Add(new F.RowStyle(i == 4 ? F.SizeType.Percent : F.SizeType.AutoSize, i == 4 ? 100 : 0));
            Controls.Add(root);
            var hint = new F.Label { AutoSize = true, Text = "本機模型範圍，與管理清單／目前視圖裁剪無關。連結模型不納入。\n" +
                "樓層採模型約束樓層；統一約束 GL 的套管請以區域區分施工樓層。\n" +
                "區域取自實例參數「HB_套管表_區域」；留白代表全部。建立時補齊範圍內編號，保留既有編號。\n" +
                "「HB_套管表_統計設定」可填：納入、排除、作廢。待確認項目仍計支數，依狀態分列。" };
            if (_issue) hint.Text = "1 檢查套管與統計設定 → 2 預覽數量與選擇圖紙 → 3 保存發圖基準\n" +
                "表格依下方樓層／區域篩選；基準保存整案本機套管，並補齊整案納入套管的編號。連結模型不納入。\n" +
                "樓層採約束樓層；統一約束 GL 時請用區域區分施工樓層。所有設定在按下執行後才寫入模型。";
            root.Controls.Add(hint, 0, 0);
            var filters = new F.TableLayoutPanel { Dock = F.DockStyle.Top, AutoSize = true, ColumnCount = 4 };
            filters.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.AutoSize)); filters.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent, 50));
            filters.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.AutoSize)); filters.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent, 50));
            Add(filters, "表格", _kind, 0, 0); Add(filters, "約束樓層", _level, 2, 0);
            Add(filters, "區域", _zone, 0, 1); Add(filters, "圖紙", _sheet, 2, 1);
            Add(filters, "距圖紙左緣 mm", _left, 0, 2); Add(filters, "距圖紙上緣 mm", _top, 2, 2);
            root.Controls.Add(filters, 0, 1);
            _manual.Text = "將目前 Revit 選取的 " + _selected.Count + " 個構件明確納入套管統計（含手動套管）";
            _manual.Enabled = _selected.Count > 0;
            root.Controls.Add(_manual, 0, 2); root.Controls.Add(_summary, 0, 3);
            if (_issue)
            {
                var tabs = new F.TabControl { Dock = F.DockStyle.Fill };
                var reviewTab = new F.TabPage("1 檢查／統計設定"); reviewTab.Controls.Add(_review);
                var previewTab = new F.TabPage("2 數量表預覽"); previewTab.Controls.Add(_grid);
                tabs.TabPages.Add(reviewTab); tabs.TabPages.Add(previewTab); root.Controls.Add(tabs, 0, 4);
                var baselineRow = new F.FlowLayoutPanel { AutoSize = true, Dock = F.DockStyle.Fill, WrapContents = true };
                baselineRow.Controls.Add(new F.Label { Text = "3 發圖基準名稱", AutoSize = true, Margin = new F.Padding(3, 7, 6, 3) });
                baselineRow.Controls.Add(_baseline);
                baselineRow.Controls.Add(new F.Label { Text = "例如 A棟_R01_發圖（不覆寫舊版）", AutoSize = true, Margin = new F.Padding(3, 7, 3, 3) });
                root.Controls.Add(baselineRow, 0, 5);
                ConfigureReview();
            }
            else root.Controls.Add(_grid, 0, 4);
            var buttons = new F.FlowLayoutPanel { Dock = F.DockStyle.Fill, AutoSize = true, FlowDirection = F.FlowDirection.RightToLeft };
            var cancel = new F.Button { Text = "取消", DialogResult = F.DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(cancel); buttons.Controls.Add(_ok); root.Controls.Add(buttons, 0, rowCount - 1);
            CancelButton = cancel; AcceptButton = _ok;
            _kind.Items.AddRange(new object[] { "數量彙總表", "定位明細表" }); _kind.SelectedIndex = 0;
            _level.Items.Add(new Choice { Key = "", Name = "整案（全部樓層）" });
            foreach (var level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
                _level.Items.Add(new Choice { Key = level.UniqueId, Name = level.Name, Id = level.Id });
            _level.SelectedIndex = 0;
            _sheet.Items.Add(new Choice { Name = "只建立／更新明細表", Id = ElementId.InvalidElementId });
            foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber))
                _sheet.Items.Add(new Choice { Name = sheet.SheetNumber + " — " + sheet.Name, Id = sheet.Id });
            _sheet.SelectedIndex = 0;
            _level.SelectedIndexChanged += (s,e) => Preview(); _kind.SelectedIndexChanged += (s,e) => Preview(); _zone.TextChanged += (s,e) => Preview();
            _manual.CheckedChanged += (s,e) => { ReadRows(); Preview(); };
            _baseline.TextChanged += (s,e) => { if (_issue) Preview(false); };
            _sheet.SelectedIndexChanged += (s,e) => { _left.Enabled = _top.Enabled = _sheet.SelectedIndex > 0;
                if (_issue) _ok.Text = _sheet.SelectedIndex > 0 ? "補號、上圖並存基準" : "建立表格並存基準"; };
            _left.Enabled = _top.Enabled = false;
            _ok.Click += (s,e) => Submit();
            ReadRows(); Preview();
            var area = F.Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
        }
        private static void Add(F.TableLayoutPanel panel, string label, F.Control control, int col, int row)
        {
            panel.Controls.Add(new F.Label { Text = label, AutoSize = true, Anchor = F.AnchorStyles.Left }, col, row);
            control.Dock = F.DockStyle.Fill; panel.Controls.Add(control, col + 1, row);
        }
        private void ReadRows() => _rows = SleeveQuantityData.Read(_doc, _manual.Checked ? _selected : null);
        private SleeveQuantityOptions ReadOptions()
        {
            var level = (Choice)_level.SelectedItem;
            return new SleeveQuantityOptions { LevelKey = level.Key, LevelName = level.Key.Length == 0 ? "整案" : level.Name,
                Zone = _zone.Text.Trim(), Detailed = _kind.SelectedIndex == 1, SheetId = ((Choice)_sheet.SelectedItem).Id,
                LeftMm = (double)_left.Value, TopMm = (double)_top.Value, ManualIds = _manual.Checked ? new HashSet<long>(_selected) : new HashSet<long>() };
        }
        private void Preview(bool refreshReview = true)
        {
            if (_rows == null) return;
            var options = ReadOptions();
            var effective = _rows.Select(r => _controls.TryGetValue(Id(r), out var control) ? SleeveIssueRequest.Preview(r, control) : r).ToList();
            var included = effective.Where(options.Matches).ToList();
            if (_issue) _ok.Enabled = !string.IsNullOrWhiteSpace(_baseline.Text) && included.Count > 0;
            int pending = included.Count(r => r.Status != "正常" && r.Status != "偏心可容納");
            _summary.Text = $"本表 {included.Count} 支，其中待確認／待處理 {pending} 支。彙總 {included.Select(r => r.GroupKey).Distinct().Count()} 列。\n" +
                $"全案已識別但未納入 {effective.Count(r => !r.Included)} 個（含排除、作廢或無效設定）。\n" +
                (_issue ? $"統計設定待寫入 {_controls.Count(p => _rows.Any(r => Id(r) == p.Key))} 筆。模型量含待確認項目，不代表施工核准。" :
                "模型支數含待確認項目；重複執行會更新同範圍表格，保留欄寬及既有圖紙位置。");
            if (_issue && refreshReview) FillReview(options);
            _grid.DataSource = null;
            _grid.Columns.Clear();
            if (options.Detailed)
                _grid.DataSource = included.Select(r => new { 編號 = r.Mark, 約束樓層 = r.Level, 區域 = r.Zone, 系統 = r.System, 族型 = r.Type,
                    套管規格 = r.Specification, 長度mm = r.Length, 材質 = r.Material, 穿越構件 = r.Host, 距樓層標高mm = r.Elevation, 管線DN = r.PipeDn, 狀態 = r.Status, 支數 = 1 }).ToList();
            else
                _grid.DataSource = included.GroupBy(r => r.GroupKey).Select(g => new { 約束樓層 = g.First().Level, 區域 = g.First().Zone,
                    系統 = g.First().System, 族型 = g.First().Type, 套管規格 = g.First().Specification, 長度mm = g.First().Length,
                    材質 = g.First().Material, 穿越構件 = g.First().Host,
                    狀態 = g.First().Status, 支數 = g.Count() }).ToList();
        }
        private static long Id(SleeveQuantityRow row) => YD_RevitTools.LicenseManager.Helpers.RevitApiCompatibility.GetIdValue(row.Id);
        private static string DisplayControl(SleeveQuantityRow row) => string.IsNullOrWhiteSpace(row.Control)
            ? (row.Included ? "自動納入" : "自動排除") : SleeveIssueRequest.ValidControl(row.Control) ? row.Control : "設定無效（請修正）";
        private void ConfigureReview()
        {
            _review.Columns.Add(new F.DataGridViewComboBoxColumn { Name = "Control", HeaderText = "統計設定", FlatStyle = F.FlatStyle.Flat, Width = 155,
                AutoSizeMode = F.DataGridViewAutoSizeColumnMode.None });
            foreach (string heading in new[] {"編號", "約束樓層", "區域", "套管規格", "長度mm", "檢核狀態"})
                _review.Columns.Add(new F.DataGridViewTextBoxColumn { HeaderText = heading, ReadOnly = true });
            _review.CurrentCellDirtyStateChanged += (s,e) => { if (_review.IsCurrentCellDirty) _review.CommitEdit(F.DataGridViewDataErrorContexts.Commit); };
            _review.CellValueChanged += (s,e) =>
            {
                if (_fillingReview || e.ColumnIndex != 0 || e.RowIndex < 0) return;
                var row = (SleeveQuantityRow)_review.Rows[e.RowIndex].Tag;
                string value = _review.Rows[e.RowIndex].Cells[0].Value as string;
                if (value == DisplayControl(row)) _controls.Remove(Id(row));
                else if (SleeveIssueRequest.ValidControl(value)) _controls[Id(row)] = value;
                _review.Rows[e.RowIndex].Cells[6].Value = _controls.TryGetValue(Id(row), out var edited)
                    ? SleeveIssueRequest.Preview(row, edited).Status : row.Status;
                Preview(false);
            };
        }
        private void FillReview(SleeveQuantityOptions options)
        {
            _fillingReview = true;
            try
            {
                _review.Rows.Clear();
                foreach (var row in _rows.Where(r => (options.LevelKey.Length == 0 || r.LevelKey == options.LevelKey) && (options.Zone.Length == 0 || r.Zone == options.Zone)))
                {
                    int index = _review.Rows.Add(); var gridRow = _review.Rows[index]; gridRow.Tag = row;
                    var control = (F.DataGridViewComboBoxCell)gridRow.Cells[0];
                    control.Items.AddRange("納入", "排除", "作廢");
                    string original = DisplayControl(row); if (!control.Items.Contains(original)) control.Items.Add(original);
                    gridRow.SetValues(_controls.TryGetValue(Id(row), out var edited) ? edited : original, row.Mark, row.Level, row.Zone, row.Specification, row.Length, row.Status);
                }
            }
            finally { _fillingReview = false; }
        }
        private void Submit()
        {
            try
            {
                _review.EndEdit(); Options = ReadOptions();
                if (_issue)
                {
                    string name = SleeveIssueRequest.ValidateName(_baseline.Text);
                    if (_execute == null) throw new InvalidOperationException("發圖服務尚未連接。");
                    _ok.Enabled = false; UseWaitCursor = true;
                    _execute(new SleeveIssueRequest { Options = Options, BaselineName = name,
                        Controls = _controls.Where(p => _rows.Any(r => Id(r) == p.Key)).ToDictionary(p => p.Key, p => p.Value) });
                }
                DialogResult = F.DialogResult.OK; Close();
            }
            catch (Exception ex) { F.MessageBox.Show(this, ex.Message, "套管發圖作業", F.MessageBoxButtons.OK, F.MessageBoxIcon.Warning); }
            finally { _ok.Enabled = true; UseWaitCursor = false; }
        }
    }
}
