using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveUpdatePreviewRow
    {
        public string Id { get; set; }
        public string Mark { get; set; }
        public bool CanApply { get; set; }
        public string CurrentCheckStatus { get; set; }
        public string CurrentCheckSummary { get; set; }
        public string CurrentCheckDetails { get; set; }
        public string Status => !CanApply ? "待確認" : HasMeasuredChanges ? "可更新" : "可同步";
        public double? MoveMm { get; set; }
        public double? CurrentLengthMm { get; set; }
        public double? TargetLengthMm { get; set; }
        public string CurrentLevel { get; set; }
        public string TargetLevel { get; set; }
        public double? TargetElevationMm { get; set; }
        public double? CurrentElevationMm { get; set; }
        public string Reason { get; set; }
        private static bool Different(double? a, double? b) => a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) > 0.5;
        public bool HasMeasuredChanges => MoveMm > 0.5 || Different(CurrentLengthMm, TargetLengthMm) || Different(CurrentElevationMm, TargetElevationMm);
        public string ChangeSummary
        {
            get
            {
                if (!CanApply) return Reason;
                var changes = new List<string>();
                if (MoveMm > 0.5) changes.Add($"中心位移 {MoveMm:0.##} mm");
                if (Different(CurrentLengthMm, TargetLengthMm)) changes.Add($"長度 {CurrentLengthMm:0.##} → {TargetLengthMm:0.##} mm");
                if (Different(CurrentElevationMm, TargetElevationMm)) changes.Add($"立面高程 {CurrentElevationMm:0.##} → {TargetElevationMm:0.##} mm");
                if (!CurrentLengthMm.HasValue) changes.Add("原長度未取得");
                if (!CurrentElevationMm.HasValue) changes.Add("原立面高程未取得");
                return changes.Count > 0 ? string.Join("；", changes) : "已比對項目未見明顯差異";
            }
        }
        public string ActionText => CanApply ? "保留原件及樓層" : "暫不更新";
    }

    internal sealed class SleeveUpdatePreviewForm : Form
    {
        internal SleeveUpdatePreviewForm(IList<SleeveUpdatePreviewRow> rows)
        {
            Text = "套管更新預覽";
            Size = new Size(1120, 620); MinimumSize = new Size(760, 440);
            AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft JhengHei UI", 10F); BackColor = Color.White;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            int blocked = rows.Count(r => !r.CanApply);
            var summary = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 10),
                Text = $"選取 {rows.Count} 支　可更新 {rows.Count(r => r.CanApply && r.HasMeasuredChanges)}　可同步 {rows.Count(r => r.CanApply && !r.HasMeasuredChanges)}　待確認 {blocked}" };
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                MultiSelect = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.DefaultCellStyle.Padding = new Padding(6, 5, 6, 5);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218,239,238);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240,242,244);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6,6,6,6);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248,249,250);
            AddColumn(grid, "Mark", "編號", 90); AddColumn(grid, "Status", "更新動作", 100);
            AddColumn(grid, "CurrentCheckStatus", "目前檢核", 110);
            AddColumn(grid, "ChangeSummary", "差異摘要", 300); AddColumn(grid, "ActionText", "處理方式", 160);
            grid.Columns["ChangeSummary"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns["ChangeSummary"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Columns["ActionText"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.CellFormatting += (_, e) => {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "Status")
                    e.CellStyle.ForeColor = !rows[e.RowIndex].CanApply ? Color.Firebrick : rows[e.RowIndex].HasMeasuredChanges ? Color.DarkOrange : Color.DimGray;
            };
            grid.DataSource = rows.ToList();
            var split = new SplitContainer { Dock=DockStyle.Fill, Orientation=Orientation.Horizontal, Panel1MinSize=60, Panel2MinSize=60, Size=new Size(900,400), SplitterDistance=240 };
            split.Panel1.Controls.Add(grid);
            var detail = new TextBox { Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, BorderStyle=BorderStyle.None,
                Dock=DockStyle.Fill, BackColor=Color.White, Margin=new Padding(8), WordWrap=true };
            split.Panel2.Padding=new Padding(4,8,4,0); split.Panel2.Controls.Add(detail);
            string Value(double? n) => n.HasValue ? n.Value.ToString("0.##") + " mm" : "未取得";
            void RefreshDetail()
            {
                detail.Text = grid.CurrentRow?.DataBoundItem is SleeveUpdatePreviewRow row
                    ? $"{row.Mark}　元素 ID：{row.Id}\r\n" +
                      $"目前檢核：{row.CurrentCheckStatus}　{row.CurrentCheckSummary}\r\n{row.CurrentCheckDetails}\r\n" +
                      "更新動作不代表檢核通過；更新後依模型現況重新檢查。\r\n" +
                      $"中心位移：{Value(row.MoveMm)}\r\n長度：{Value(row.CurrentLengthMm)} → {Value(row.TargetLengthMm)}\r\n" +
                      $"立面高程：{Value(row.CurrentElevationMm)} → {Value(row.TargetElevationMm)}\r\n" +
                      $"約束樓層：{row.CurrentLevel} → {row.TargetLevel}\r\n" +
                      (row.CanApply ? "方向及其他參數：未完整比對；同步仍可能寫入模型。\r\n" : "") + row.Reason : "";
            }
            grid.SelectionChanged += (_, __) => RefreshDetail();
            Shown += (_, __) => RefreshDetail();
            root.SizeChanged += (_, __) => summary.MaximumSize = new Size(Math.Max(200, root.ClientSize.Width - 24), 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            var apply = new Button { Text = "確認更新", AutoSize = true, Enabled = rows.Count > 0 && blocked == 0, DialogResult = DialogResult.OK,
                FlatStyle=FlatStyle.Flat, BackColor=Color.FromArgb(0,105,180), ForeColor=Color.White };
            buttons.Controls.Add(new Label { Text=blocked>0 ? "有待確認項目，本批暫不更新" : "新增 0｜刪除 0", AutoSize=true, Padding=new Padding(8) });
            buttons.Controls.Add(cancel); buttons.Controls.Add(apply);
            CancelButton = cancel; AcceptButton = cancel;
            root.Controls.Add(summary, 0, 0); root.Controls.Add(split, 0, 1); root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);
        }
        private static void AddColumn(DataGridView grid, string name, string title, int width, bool number = false)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, DataPropertyName = name, HeaderText = title,
                Width = width, MinimumWidth = 65, SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Format = number ? "0.##" : "", NullValue = "—" } });
        }
    }
}
