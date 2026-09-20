using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // WinForms entry for versions whose build excludes the legacy XAML window.
    internal sealed class PipeSleeveSettingsForm : Form
    {
        internal sealed class SizeRow
        {
            public int DN { get; set; }
            public string Target => PipeSleeveNominalRules.Mapping.TryGetValue(DN,out int size) ? size+"A" : "待指定";
            public string SymbolId { get; set; } = "";
        }
        internal sealed class SymbolChoice
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Family { get; set; }
            public string Type { get; set; }
        }
        private readonly Label summary = new Label { AutoSize=false, Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft, ForeColor=Color.FromArgb(90,100,110) };
        private int selectedCount;
        private void RefreshSummary()
        {
            int pending=SizeRows.Count(r=>string.IsNullOrEmpty(r.SymbolId));
            summary.Text=$"已選取 {selectedCount} 個構件 ｜ {SizeRows.Count} 種管徑 ｜ {pending} 項族型待指定";
            sizes.Invalidate();
        }
        private readonly ComboBox wall = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=470 };
        private readonly ComboBox floor = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=470 };
        private readonly DataGridView sizes = new DataGridView { Dock=DockStyle.Fill, AutoGenerateColumns=false, AllowUserToAddRows=false, AllowUserToDeleteRows=false, RowHeadersVisible=false, BackgroundColor=Color.White, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
        internal List<SizeRow> SizeRows { get; private set; } = new List<SizeRow>();
        internal string WallSymbolId => wall.SelectedValue as string;
        internal string FloorSymbolId => floor.SelectedValue as string;
        private List<SymbolChoice> choices = new List<SymbolChoice>();
        private readonly CheckBox useMap = Option("依管徑對應套管類型",true);
        internal bool UseDiameterMap => useMap.Checked;
        internal void ConfigureSizes(List<SizeRow> rows, List<SymbolChoice> symbols, PipeSleeveSettings settings = null)
        {
            choices=symbols; SizeRows=rows; sizes.DataSource=SizeRows;
            foreach(var combo in new[]{wall,floor}) {
                combo.DisplayMember="Name"; combo.ValueMember="Id";
                combo.DataSource=new[]{new SymbolChoice { Id="",Name="未指定" }}.Concat(symbols).ToList();
            }
            sizes.Columns.Add(new DataGridViewComboBoxColumn { Name="SymbolId",DataPropertyName="SymbolId",HeaderText="套管類型",FillWeight=230,MinimumWidth=260,
                DisplayMember="Name",ValueMember="Id",DataSource=new[]{new SymbolChoice { Id="",Name="待指定" }}.Concat(symbols).ToList() });
            var saved=settings ?? PipeSleeveSettingsStore.Load();
            foreach(var entry in saved.SizeMappings ?? new List<PipeSleeveSizeSetting>())
                if(!rows.Any(r=>r.DN==entry.NominalDiameterMm)) rows.Add(new SizeRow { DN=entry.NominalDiameterMm });
            SizeRows=rows.OrderBy(r=>r.DN).ToList(); rows=SizeRows; sizes.DataSource=SizeRows;
            current.Checked=saved.IncludeCurrentModel; links.Checked=saved.IncludeLinks; exclude.Checked=saved.ExcludeAdditionElements;
            number.Checked=saved.AutoNumber; existing.Checked=!saved.UpdateExisting; useMap.Checked=saved.UseDiameterMap;
            if(!double.IsNaN(saved.ClearanceMm)&&!double.IsInfinity(saved.ClearanceMm)) clearance.Value=(decimal)Math.Max(0,Math.Min(1000,saved.ClearanceMm));
            wall.SelectedValue=FindId(saved.DefaultWallSleeveDisplayName); floor.SelectedValue=FindId(saved.DefaultFloorSleeveDisplayName);
            foreach(var row in rows) {
                var entry=saved.SizeMappings?.FirstOrDefault(x=>x.NominalDiameterMm==row.DN);
                if(entry!=null) { row.SymbolId=FindId(entry.SleeveDisplayName); continue; }
                if(!PipeSleeveNominalRules.Mapping.TryGetValue(row.DN,out int size)) continue;
                var matches=symbols.Where(x=>PipeSleeveNominalRules.Matches(x.Type,size)).ToList();
                var family=(wall.SelectedItem as SymbolChoice)?.Family;
                if(!string.IsNullOrEmpty(family)) matches=matches.Where(x=>x.Family==family).ToList();
                else { var preferred=matches.Where(x=>x.Family=="套管-圓形_無").ToList(); if(preferred.Count>0) matches=preferred; }
                if(matches.Count==1) row.SymbolId=matches[0].Id;
            }
            RefreshSummary();
        }
        private string FindId(string name) => choices.FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase))?.Id ?? "";
        private string FindName(string id) => choices.FirstOrDefault(x=>x.Id==id)?.Name ?? "";
        internal bool ValidateSizes(out string error)
        {
            if(!sizes.EndEdit()) { error="請完成族型選擇。"; return false; }
            RefreshSummary(); error=null; return true;
        }
        internal void SaveSettings()
        {
            if(!ValidateSizes(out var error)) throw new InvalidOperationException(error);
            PipeSleeveSettingsStore.Save(new PipeSleeveSettings {
                DefaultWallSleeveDisplayName=FindName(WallSymbolId),DefaultFloorSleeveDisplayName=FindName(FloorSymbolId),
                ClearanceMm=ClearanceMm,IncludeCurrentModel=IncludeCurrentModel,IncludeLinks=IncludeLinks,ExcludeAdditionElements=ExcludeAdditionElements,
                UseDiameterMap=UseDiameterMap,AutoNumber=AutoNumber,UpdateExisting=!SkipExisting,
                SizeMappings=SizeRows.Select(r=>new PipeSleeveSizeSetting { NominalDiameterMm=r.DN,SleeveDisplayName=FindName(r.SymbolId) }).ToList()
            });
        }
        private readonly NumericUpDown clearance = new NumericUpDown { Minimum=0, Maximum=1000, DecimalPlaces=1, Value=50, Width=110 };
        private readonly CheckBox current = Option("包含本機模型", true);
        private readonly CheckBox links = Option("包含連結模型", true);
        private readonly CheckBox exclude = Option("排除增建構件", true);
        private readonly CheckBox number = Option("自動編號", true);
        private readonly CheckBox existing = Option("略過既有套管", true);
        private readonly CheckBox view = Option("限制於目前視圖", true);
        internal double ClearanceMm => (double)clearance.Value;
        internal bool IncludeCurrentModel => current.Checked;
        internal bool IncludeLinks => links.Checked;
        internal bool ExcludeAdditionElements => exclude.Checked;
        internal bool AutoNumber => number.Checked;
        internal bool SkipExisting => existing.Checked;
        internal bool LimitToActiveView => view.Checked;
        private static CheckBox Option(string text, bool value) => new CheckBox { Text=text, Checked=value, AutoSize=true, Margin=new Padding(0,6,0,6) };
        private static Label Caption(string text) => new Label { Text=text,AutoSize=true,Margin=new Padding(0,6,0,8),ForeColor=Color.FromArgb(45,55,65) };
        private static Control Section(string title, params Control[] children)
        {
            var panel=new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16),Margin=new Padding(0,0,0,12),BackColor=Color.White };
            var heading=Caption(title); heading.Font=new Font("Microsoft JhengHei UI",10,FontStyle.Bold); panel.Controls.Add(heading);
            panel.Controls.AddRange(children); return panel;
        }
        internal PipeSleeveSettingsForm(int selectedCount)
        {
            this.selectedCount=selectedCount;
            Text="HB_BIM｜自動套管設定"; Font=new Font("Microsoft JhengHei UI",10);
            ClientSize=new Size(1240,760); MinimumSize=new Size(1120,700); BackColor=Color.FromArgb(246,247,249);
            StartPosition=FormStartPosition.CenterParent; MinimizeBox=false;
            var root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,66)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
            var header=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false };
            header.Controls.Add(new Label { Text="建立套管",AutoSize=true,Font=new Font(Font.FontFamily,17,FontStyle.Bold) });
            header.Controls.Add(Caption("先確認尺寸與族型，再檢查處理範圍。")); root.Controls.Add(header,0,0);
            var tabs=new TabControl { Dock=DockStyle.Fill,Padding=new Point(22,9) };
            var mappingPage=new TabPage("尺寸與族型") { BackColor=BackColor,Padding=new Padding(16) };
            var optionsPage=new TabPage("處理設定") { BackColor=BackColor,Padding=new Padding(16),AutoScroll=true };
            tabs.TabPages.AddRange(new[]{mappingPage,optionsPage});
            var body=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
            body.Controls.Add(tabs,0,0); root.Controls.Add(body,0,1);
            var referenceLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(14,32,8,12),BackColor=Color.White,Margin=new Padding(16,0,0,0) };
            referenceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,210));
            referenceLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            referenceLayout.Controls.Add(new SleeveBeamReferencePanel { Dock=DockStyle.Fill },0,0);
            referenceLayout.Controls.Add(new TextBox { Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,
                Dock=DockStyle.Fill,BackColor=Color.White,ScrollBars=ScrollBars.Vertical,
                Text="穿梁複核重點（沿用 2024）\r\n\r\n• 梁端／柱邊 2H 範圍需複核。\r\n• 孔徑 ≥ 100 mm：提醒。\r\n• 孔徑 ≥ 200 mm 或 > 梁深 H/3：需結構確認。\r\n• 孔距 < 3D 或 300 mm：需複核。\r\n\r\n既有流程可能仍生成套管並標示風險；生成成功不代表結構核可。\r\n本圖為複核參考，不能取代專案結構設計要求；實際孔距定義與允許位置須依核定圖說確認。" },0,1);
            body.Controls.Add(referenceLayout,1,0);
            var mapping=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4 };
            mapping.RowStyles.Add(new RowStyle(SizeType.Absolute,142)); mapping.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            mapping.RowStyles.Add(new RowStyle(SizeType.Percent,100)); mapping.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
            var family=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Padding=new Padding(12),BackColor=Color.White };
            family.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125)); family.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            family.RowStyles.Add(new RowStyle(SizeType.Absolute,30)); family.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); family.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            family.Controls.Add(Caption("01  共用套管族型"),0,0); family.SetColumnSpan(family.GetControlFromPosition(0,0),2);
            family.Controls.Add(Caption("穿牆預設類型"),0,1); wall.Dock=DockStyle.Fill; family.Controls.Add(wall,1,1);
            family.Controls.Add(Caption("樓板／樑預設類型"),0,2); floor.Dock=DockStyle.Fill; family.Controls.Add(floor,1,2);
            wall.DropDownWidth=700; floor.DropDownWidth=700; mapping.Controls.Add(family,0,0);
            mapping.Controls.Add(Caption("02  進階尺寸對應 · 依公稱尺寸選用既有套管類型"),0,1);
            sizes.BorderStyle=BorderStyle.None; sizes.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
            sizes.GridColor=Color.FromArgb(226,231,235); sizes.EnableHeadersVisualStyles=false;
            sizes.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(234,239,243);
            sizes.ColumnHeadersDefaultCellStyle.ForeColor=Color.FromArgb(45,55,65);
            sizes.ColumnHeadersHeight=38; sizes.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            sizes.RowTemplate.Height=36; sizes.DefaultCellStyle.Padding=new Padding(6,2,6,2);
            sizes.DefaultCellStyle.SelectionBackColor=Color.FromArgb(222,237,251); sizes.DefaultCellStyle.SelectionForeColor=Color.Black;
            sizes.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(248,250,252);
            sizes.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName="DN", HeaderText="管徑 DN", ReadOnly=true,FillWeight=55,MinimumWidth=70 });
            sizes.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName="Target",HeaderText="預設公稱尺寸",ReadOnly=true,FillWeight=90 });
            sizes.Columns.Add(new DataGridViewTextBoxColumn { Name="State",HeaderText="對應狀態",ReadOnly=true,FillWeight=85 });
            sizes.CellFormatting+=(sender,e)=> {
                if(e.RowIndex<0 || sizes.Columns[e.ColumnIndex].Name!="State" || e.RowIndex>=SizeRows.Count) return;
                bool valid=!string.IsNullOrEmpty(SizeRows[e.RowIndex].SymbolId);
                e.Value=valid ? "族型已對應" : "待指定"; e.CellStyle.ForeColor=valid ? Color.SeaGreen : Color.DarkOrange; e.FormattingApplied=true;
            };
            sizes.CellValueChanged+=(sender,e)=>RefreshSummary(); clearance.ValueChanged+=(sender,e)=>RefreshSummary();
            sizes.DataError+=(sender,e)=> { e.ThrowException=false; e.Cancel=true; if(e.RowIndex>=0) sizes.Rows[e.RowIndex].ErrorText="請輸入有效數值。"; };
            mapping.Controls.Add(sizes,0,2);
            mapping.Controls.Add(new Label { Text="啟用對應表時，以各列指定族型為準；未指定項目會略過。\n50A、100A 為公稱尺寸，不代表淨內徑；保留族型既有直徑。",Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0),ForeColor=Color.DimGray },0,3);
            mappingPage.Controls.Add(mapping);
            var options=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=3 };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var spacing=new FlowLayoutPanel { AutoSize=true }; spacing.Controls.Add(Caption("直徑增加量（mm）")); spacing.Controls.Add(clearance);
            options.Controls.Add(Section("套管尺寸計算",useMap,spacing,Caption("圓形套管依族型尺寸，不以增加量改寫直徑。"),Caption("增加量保留供矩形開孔計算使用。")));
            options.Controls.Add(Section("穿越位置與模型範圍",current,links,exclude,view));
            options.Controls.Add(Section("編號與更新",number,existing,Caption("取消略過後更新既有套管；可能清理失效的舊套管，與 2024 流程一致。")));
            var sections=options.Controls.Cast<Control>().ToList(); options.Controls.Clear();
            for(int i=0;i<sections.Count;i++) {
                var container=new Panel { Dock=DockStyle.Top,AutoSize=true,BackColor=Color.White,Margin=new Padding(0,0,0,12) };
                sections[i].Dock=DockStyle.Top; container.Controls.Add(sections[i]); options.Controls.Add(container,0,i);
            }
            optionsPage.Controls.Add(options);
            var footer=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(0,8,0,0) };
            footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,370));
            footer.Controls.Add(summary,0,0);
            var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Margin=Padding.Empty };
            var run=new Button { Text="檢查並建立",Size=new Size(140,36),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(32,113,206),ForeColor=Color.White };
            var cancel=new Button { Text="取消",Size=new Size(94,36),FlatStyle=FlatStyle.Flat,DialogResult=DialogResult.Cancel };
            run.Click+=(sender,e)=> {
                if(!current.Checked && !links.Checked) { tabs.SelectedTab=optionsPage; MessageBox.Show(this,"請至少選擇本機模型或連結模型。",Text); return; }
                if(!ValidateSizes(out var error)) { tabs.SelectedTab=mappingPage; MessageBox.Show(this,error,Text); return; }
                if(string.IsNullOrEmpty(WallSymbolId) && string.IsNullOrEmpty(FloorSymbolId) && !SizeRows.Any(r=>!string.IsNullOrEmpty(r.SymbolId))) { tabs.SelectedTab=mappingPage; MessageBox.Show(this,"請至少指定一種穿越族型。",Text); return; }
                try { SaveSettings(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,Text); return; }
                DialogResult=DialogResult.OK; Close();
            };
            var save=new Button { Text="儲存預設",Size=new Size(100,36),FlatStyle=FlatStyle.Flat };
            save.Click+=(sender,e)=> { try { SaveSettings(); MessageBox.Show(this,"已儲存，與 2024 共用設定。",Text); } catch(Exception ex) { MessageBox.Show(this,ex.Message,Text); } };
            actions.Controls.Add(run); actions.Controls.Add(save); actions.Controls.Add(cancel); footer.Controls.Add(actions,1,0); root.Controls.Add(footer,0,2);
            Controls.Add(root); AcceptButton=run; CancelButton=cancel; RefreshSummary();
        }
    }
}
