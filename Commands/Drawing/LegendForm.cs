using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using UIDocument = Autodesk.Revit.UI.UIDocument;
using Form = System.Windows.Forms.Form;
using View = Autodesk.Revit.DB.View;
using Control = System.Windows.Forms.Control;

namespace YD_RevitTools.LicenseManager.Commands.Drawing
{
    internal sealed class LegendForm : Form
    {
        private sealed class Choice
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }
        private readonly Document _doc;
        private readonly View _sourceView;
        private readonly ICollection<ElementId> _selection;
        private readonly ComboBox _source = Combo(), _template = Combo(), _target = Combo(), _textType = Combo(), _sheet = Combo();
        private readonly TextBox _name = new TextBox { Text = "HB 智慧圖例", Dock = DockStyle.Fill };
        private readonly TextBox _fields = new TextBox { Text = "型別代碼,族群,型別,說明", Dock = DockStyle.Fill };
        private readonly NumericUpDown _symbolWidth = Number(35), _textWidth = Number(70), _gap = Number(4), _row = Number(15);
        private readonly DataGridView _types = Grid(), _changes = Grid();
        private readonly TextBox _status = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        private readonly Button _preview = new Button { Text = "檢查並預覽", AutoSize = true };
        private readonly Button _apply = new Button { Text = "建立／更新", AutoSize = true, Enabled = false };
        private bool _loading;
        private readonly TabControl _lists = new TabControl { Dock = DockStyle.Fill };
        internal string ResultView { get; private set; }
        private static ComboBox Combo() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private static NumericUpDown Number(decimal value) => new NumericUpDown { Minimum = 1, Maximum = 500, Value = value, Width = 65, DecimalPlaces = 1 };
        private static DataGridView Grid() => new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = SystemColors.Window };
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var area = Screen.FromHandle(Handle).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
            Location = new System.Drawing.Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }
        internal LegendForm(UIDocument uiDoc)
        {
            _doc = uiDoc.Document; _sourceView = _doc.ActiveView; _selection = uiDoc.Selection.GetElementIds();
            Text = "HB BIM｜智慧圖例"; Font = new Font("Microsoft JhengHei UI", 9F);
            var screen = Screen.FromHandle(uiDoc.Application.MainWindowHandle).WorkingArea;
            Size = new Size(Math.Min(1080, screen.Width - 32), Math.Min(860, screen.Height - 32));
            MinimumSize = new Size(Math.Min(880, screen.Width - 32), Math.Min(700, screen.Height - 32)); StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(new Label { Text = "由模型型別建立圖例；數量另由明細表統計。\n範本需恰好一個圖例元件。更新以本次勾選型別為準，未勾選的舊列將於預覽標示移除。", Dock = DockStyle.Fill }, 0, 0);
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < 3; i++) options.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
            void Add(string label, Control control, int col, int row)
            { options.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, col, row); options.Controls.Add(control, col + 1, row); }
            Add("來源範圍", _source, 0, 0);
            var reload = new Button { Text = "重新讀取來源", AutoSize = true }; options.Controls.Add(reload, 3, 0);
            Add("圖例範本", _template, 0, 1); Add("更新目標", _target, 2, 1);
            Add("圖例名稱", _name, 0, 2); Add("配置圖紙", _sheet, 2, 2);
            var layoutSettings = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 5, Dock = DockStyle.Top };
            layoutSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            layoutSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) layoutSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layoutSettings.Controls.Add(new Label { Text = "文字型式", AutoSize = true }, 0, 0); layoutSettings.Controls.Add(_textType, 1, 0);
            layoutSettings.Controls.Add(new Label { Text = "顯示欄位", AutoSize = true }, 0, 1); layoutSettings.Controls.Add(_fields, 1, 1);
            var fieldHint = new Label { Text = "逗號分隔：型別代碼、族群、型別、類別、說明，或模型中的型別參數名稱。", AutoSize = true };
            fieldHint.MaximumSize = new Size(450, 0);
            layoutSettings.Controls.Add(fieldHint, 1, 2);
            var sizes = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true };
            foreach (var pair in new[] { Tuple.Create("符號寬", _symbolWidth), Tuple.Create("文字寬", _textWidth), Tuple.Create("間距", _gap), Tuple.Create("最小列高", _row) })
            { sizes.Controls.Add(new Label { Text = pair.Item1, AutoSize = true, Margin = new Padding(3, 6, 3, 0) }); sizes.Controls.Add(pair.Item2); }
            layoutSettings.Controls.Add(new Label { Text = "紙面 mm", AutoSize = true }, 0, 3); layoutSettings.Controls.Add(sizes, 1, 3);
            layoutSettings.Controls.Add(new Label { Text = "設定變更後需重新預覽；此視窗不會寫入模型。", AutoSize = true, MaximumSize = new Size(450, 0) }, 1, 4);
            Disposed += (_, _) => layoutSettings.Dispose();
            root.Controls.Add(options, 0, 1);
            _types.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "納入", FillWeight = 12 });
            _types.Columns.Add("Category", "類別"); _types.Columns.Add("Family", "族群"); _types.Columns.Add("Type", "型別");
            foreach (DataGridViewColumn column in _types.Columns) column.ReadOnly = column.Index != 0;
            var typesTab = new TabPage("1 選擇型別"); typesTab.Controls.Add(_types); _lists.TabPages.Add(typesTab);
            root.Controls.Add(_lists, 0, 2);
            _changes.ReadOnly = true; _changes.Columns.Add("Action", "動作"); _changes.Columns[0].FillWeight = 15;
            _changes.Columns.Add("Name", "型別"); _changes.Columns[1].FillWeight = 45;
            _changes.Columns.Add("Detail", "預覽差異／處理結果");
            var changesTab = new TabPage("2 差異預覽"); changesTab.Controls.Add(_changes); _lists.TabPages.Add(changesTab);
            root.Controls.Add(_status, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "關閉", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(close); buttons.Controls.Add(_apply); buttons.Controls.Add(_preview);
            var all = new Button { Text = "全選", AutoSize = true }; var none = new Button { Text = "全不選", AutoSize = true };
            buttons.Controls.Add(none); buttons.Controls.Add(all);
            var layoutButton = new Button { Text = "版面設定…", AutoSize = true };
            layoutButton.Click += (_, _) => { using var dialog = Helpers.UI.ToolSettingsDialog.Create(layoutSettings, "圖例版面設定", new Size(700, 370)); dialog.ShowDialog(this); };
            buttons.Controls.Add(layoutButton); root.Controls.Add(buttons, 0, 4);
            _lists.SelectedIndexChanged += (_, _) => { all.Enabled = none.Enabled = _lists.SelectedIndex == 0; };
            Controls.Add(root); CancelButton = close;
            _loading = true;
            _source.Items.AddRange(new object[] { "目前選取的模型構件", "目前模型視圖可見構件" });
            _source.SelectedIndex = _selection.Count > 0 ? 0 : 1;
            var legends = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.Legend).OrderBy(v => v.Name).ToList();
            Fill(_template, legends.Where(v => !LegendService.IsManaged(v)).Select(v => new Choice { Id = v.UniqueId, Name = v.Name }));
            Fill(_target, new[] { new Choice { Id = "", Name = "建立新圖例" } }.Concat(legends.Where(LegendService.IsManaged).Select(v => new Choice { Id = v.UniqueId, Name = v.Name })));
            Fill(_textType, new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().OrderBy(t => t.Name).Select(t => new Choice { Id = t.UniqueId, Name = t.Name }));
            Fill(_sheet, new[] { new Choice { Id = "", Name = "暫不配置" } }.Concat(new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber).Select(s => new Choice { Id = s.UniqueId, Name = s.SheetNumber + " " + s.Name })));
            _loading = false;
            foreach (var combo in new[] { _template, _textType, _sheet }) combo.SelectedIndexChanged += (s, e) => InvalidatePreview();
            foreach (var text in new[] { _name, _fields }) text.TextChanged += (s, e) => InvalidatePreview();
            foreach (var number in new[] { _symbolWidth, _textWidth, _gap, _row }) number.ValueChanged += (s, e) => InvalidatePreview();
            _source.SelectedIndexChanged += (s, e) => ReadSource(); _target.SelectedIndexChanged += (s, e) => LoadTarget();
            _types.CurrentCellDirtyStateChanged += (s, e) => { if (_types.IsCurrentCellDirty) _types.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _types.CellValueChanged += (s, e) => InvalidatePreview();
            all.Click += (s, e) => { foreach (DataGridViewRow row in _types.Rows) row.Cells[0].Value = true; };
            none.Click += (s, e) => { foreach (DataGridViewRow row in _types.Rows) row.Cells[0].Value = false; };
            reload.Click += (s, e) => ReadSource(); _preview.Click += (s, e) => Run(true); _apply.Click += (s, e) => Run(false);
            ReadSource();
        }
        private static void Fill(ComboBox combo, IEnumerable<Choice> choices)
        { foreach (var choice in choices) combo.Items.Add(choice); if (combo.Items.Count > 0) combo.SelectedIndex = 0; }
        private static string Selected(ComboBox combo) => (combo.SelectedItem as Choice)?.Id ?? "";
        private static void Select(ComboBox combo, string id)
        { combo.SelectedItem = combo.Items.Cast<Choice>().FirstOrDefault(x => x.Id == id); }
        private void InvalidatePreview()
        {
            if (_loading) return;
            _apply.Enabled = false; _changes.Rows.Clear(); _status.Text = "設定已變更，請先按「檢查並預覽」。";
        }
        private void ReadSource()
        {
            InvalidatePreview(); _types.Rows.Clear();
            try
            {
                foreach (var type in LegendService.CollectTypes(_doc, _sourceView, _source.SelectedIndex == 0 ? _selection : null))
                { var index = _types.Rows.Add(true, type.Category?.Name, type.FamilyName, type.Name); _types.Rows[index].Tag = type.UniqueId; }
                _status.Text = $"已讀取 {_types.Rows.Count} 種型別。來源不含連結模型；支援性以範本元件方向實際檢查。";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        private void LoadTarget()
        {
            InvalidatePreview(); if (Selected(_target).Length == 0) return;
            try
            {
                var record = LegendService.Read((View)_doc.GetElement(Selected(_target))); var settings = record.Settings;
                _loading = true;
                _name.Text = settings.Name; Select(_template, settings.Template); Select(_textType, settings.TextType);
                _fields.Text = string.Join(",", settings.Fields); _symbolWidth.Value = (decimal)settings.SymbolWidth;
                _textWidth.Value = (decimal)settings.TextWidth; _gap.Value = (decimal)settings.Gap; _row.Value = (decimal)settings.MinimumRow;
                // Sheet placement is always an explicit choice in this run.
                _sheet.SelectedIndex = 0;
                _status.Text = "已載入圖例版面設定。本次來源與勾選型別將取代原列清單，請確認預覽中的移除項目。";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _loading = false; }
        }
        private void Run(bool preview)
        {
            _apply.Enabled = false; _preview.Enabled = false; UseWaitCursor = true;
            _status.Text = preview ? "正在檢查型別、文字排版與圖紙配置…" : "正在建立／更新圖例…"; _status.Refresh();
            try
            {
                var settings = new LegendSettings { Template = Selected(_template), Target = Selected(_target), Name = _name.Text.Trim(),
                    TextType = Selected(_textType), Sheet = Selected(_sheet), Source = _source.SelectedIndex == 0 ? "開啟工具時的選取構件" : _sourceView.Name,
                    Fields = _fields.Text.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray(),
                    SymbolWidth = (double)_symbolWidth.Value, TextWidth = (double)_textWidth.Value, Gap = (double)_gap.Value, MinimumRow = (double)_row.Value };
                var types = _types.Rows.Cast<DataGridViewRow>().Where(r => Equals(r.Cells[0].Value, true)).Select(r => (string)r.Tag).ToList();
                var result = LegendService.Run(_doc, settings, types, preview);
                _changes.Rows.Clear();
                foreach (var change in result.Changes)
                {
                    var index = _changes.Rows.Add(change.Action, change.Name, LegendPolicy.NormalizeText(change.Detail).Replace("\n", "；"));
                    _changes.Rows[index].Cells[2].ToolTipText = change.Detail;
                }
                _lists.SelectedIndex = 1;
                _status.Text = string.Join(Environment.NewLine, result.Problems.Concat(result.Notes));
                if (result.Success)
                {
                    _status.Text = (preview ? "預覽通過，尚未寫入模型。" : "圖例已完成。") + $" 新增 {result.Changes.Count(c => c.Action == "新增")}、更新 {result.Changes.Count(c => c.Action == "更新")}、移除 {result.Changes.Count(c => c.Action == "移除")} 列。\r\n" + _status.Text;
                    if (preview) _apply.Enabled = true;
                    else { ResultView = result.View; DialogResult = DialogResult.OK; Close(); }
                }
                else _status.Text = "未套用，模型維持原狀。\r\n" + _status.Text;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { UseWaitCursor = false; _preview.Enabled = true; }
        }
    }
}
