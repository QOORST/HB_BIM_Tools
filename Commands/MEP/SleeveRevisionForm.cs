using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Form = System.Windows.Forms.Form;
using Control = System.Windows.Forms.Control;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveRevisionForm : Form
    {
        private readonly Document _doc;
        private readonly ComboBox _baseline = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        private readonly TextBox _name = new TextBox { Width = 240, MaxLength = 80 };
        private readonly ComboBox _sheet = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly Label _summary = new Label { Dock = DockStyle.Fill, AutoSize = false };
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AutoGenerateColumns = false, RowHeadersVisible = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
        private SleeveRevisionSnapshot _current;
        private sealed class SheetChoice
        {
            internal ElementId Id; internal string Name;
            public override string ToString() => Name;
        }
        internal SleeveRevisionForm(Document doc)
        {
            _doc = doc; Text = "套管版本基準與差量"; Width = 1120; Height = 740; MinimumSize = new System.Drawing.Size(850, 600);
            StartPosition = FormStartPosition.CenterParent; Font = new System.Drawing.Font("Microsoft JhengHei", 9F);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
            foreach (int height in new[] {60, 42, 42, 42, 55}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "比對整案本機已納入管理的套管，包含排除／作廢紀錄。連結模型套管不列入。\n儲存基準會補齊納入套管的編號；既有基準不覆寫。比對與 CSV 匯出不修改模型。" }, 0, 0);
            layout.Controls.Add(Row(new Label { Text = "新增基準", Width = 85, AutoSize = false }, _name,
                Button("補編號並儲存基準", () => { var saved = SleeveRevisionService.Save(_doc, _name.Text); LoadBaselines(saved.Id); _name.Clear(); })), 0, 1);
            layout.Controls.Add(Row(new Label { Text = "比較基準", Width = 85 }, _baseline, Button("重新比對", RefreshComparison)), 0, 2);
            _sheet.Items.Add(new SheetChoice { Id = ElementId.InvalidElementId, Name = "僅建立製圖視圖（可拖曳至圖紙）" });
            foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber))
                _sheet.Items.Add(new SheetChoice { Id = sheet.Id, Name = sheet.SheetNumber + " — " + sheet.Name });
            _sheet.SelectedIndex = 0;
            layout.Controls.Add(Row(new Label { Text = "圖表位置", Width = 85 }, _sheet,
                new Label { Text = "單頁可置中放入圖紙；多頁請分別配置。", AutoSize = true }), 0, 3);
            layout.Controls.Add(_summary, 0, 4);
            foreach (var column in new[] { new[] {"Mark", "編號"}, new[] {"Changes", "變更"}, new[] {"QuantityDelta", "支數增減"},
                new[] {"BeforeDetail", "基準內容"}, new[] {"AfterDetail", "目前內容"} })
                _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = column[0], HeaderText = column[1],
                    Width = column[0].EndsWith("Detail") ? 290 : 105, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
            layout.Controls.Add(_grid, 0, 5);
            layout.Controls.Add(Row(Button("匯出差量 CSV", Export), Button("建立差量圖表", Draw), Button("關閉", Close)), 0, 6);
            _baseline.SelectedIndexChanged += (s, e) => Run(RefreshComparison);
            LoadBaselines(null);
        }
        private FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; row.Controls.AddRange(controls); return row;
        }
        private Button Button(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 32, Padding = new Padding(7, 0, 7, 0) };
            button.Click += (s, e) => Run(action); return button;
        }
        private void Run(Action action)
        {
            try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "套管版本差量", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void LoadBaselines(string id)
        {
            _baseline.Items.Clear();
            foreach (var item in SleeveRevisionService.List(_doc)) _baseline.Items.Add(item);
            if (_baseline.Items.Count > 0) _baseline.SelectedItem = _baseline.Items.Cast<SleeveRevisionSnapshot>().FirstOrDefault(s => s.Id == id) ?? _baseline.Items[0];
            else { _summary.Text = "尚未建立基準。請於發圖前儲存一版，後續即可比對變更。"; _grid.DataSource = null; }
        }
        private SleeveRevisionSnapshot Baseline => _baseline.SelectedItem as SleeveRevisionSnapshot ?? throw new InvalidOperationException("請先儲存並選取比較基準。");
        private void RefreshComparison()
        {
            if (_baseline.SelectedItem == null) return;
            var baseline = Baseline; _current = SleeveRevisionService.Capture(_doc, baseline);
            var changes = SleeveRevisionPolicy.Compare(baseline, _current); _grid.DataSource = changes;
            _summary.Text = "基準納入 " + baseline.Records.Count(r => r.Included) + " 支 → 目前納入 " + _current.Records.Count(r => r.Included) +
                " 支；淨增減 " + changes.Sum(r => r.QuantityDelta) + " 支；變更 " + changes.Count + " 筆。\n圖表為固定快照；位移容差 0.5 mm；座標為內部原點 mm，樓層為約束樓層。待確認項目不等於核准施工量。";
        }
        private void Export()
        {
            var baseline = Baseline; RefreshComparison();
            using (var dialog = new SaveFileDialog { Filter = "CSV 檔案 (*.csv)|*.csv", FileName = "套管版本差量_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                { File.WriteAllText(dialog.FileName, SleeveRevisionPolicy.Csv(baseline, _current), new UTF8Encoding(true)); _summary.Text += "\nCSV 已匯出。"; }
        }
        private void Draw()
        {
            var baseline = Baseline; RefreshComparison();
            var views = SleeveRevisionDrawing.Create(_doc, baseline, _current, ((SheetChoice)_sheet.SelectedItem).Id);
            MessageBox.Show(this, "已建立 " + views.Count + " 頁差量圖表。\n" + string.Join("\n", views.Select(v => v.Name)) +
                "\n可從專案瀏覽器的製圖視圖拖曳至圖紙；圖表不隨模型自動更新。", "套管版本差量");
        }
    }
}
