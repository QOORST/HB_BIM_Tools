using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using F=System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveConstructionForm:F.Form
    {
        private readonly Document _doc;
        private readonly F.ComboBox _source=Combo(),_level=Combo(),_a=Combo(),_b=Combo(),_title=Combo(),_packages=Combo();
        private readonly F.TextBox _name=new F.TextBox(),_revision=new F.TextBox{Text="R01"},_zone=new F.TextBox(),_person=new F.TextBox(),_approval=new F.TextBox();
        private readonly F.DataGridView _preview=Grid(),_field=Grid();
        private readonly F.Label _status=new F.Label{AutoSize=true,MaximumSize=new Size(1050,0)};
        private readonly F.CheckBox _reviewed=new F.CheckBox{AutoSize=true,Text="已人工核對定位尺寸、圖框、標註重疊及必要剖面／施工詳圖，並取得核准文件"};
        private SleeveConstructionPackage _loaded;
        private bool _fieldDirty,_loadingField,_restoringPackage;
        private sealed class Choice
        {
            internal ElementId Id;internal string Name;
            public override string ToString()=>Name;
        }
        private sealed class PackageChoice
        {
            internal SleeveConstructionPackage Package;
            public override string ToString()=>Package.Name+"／"+Package.Revision+"／"+Package.Status;
        }
        private static F.ComboBox Combo()=>new F.ComboBox{DropDownStyle=F.ComboBoxStyle.DropDownList,Dock=F.DockStyle.Fill};
        private static F.DataGridView Grid()=>new F.DataGridView{Dock=F.DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
            AutoSizeColumnsMode=F.DataGridViewAutoSizeColumnsMode.DisplayedCells,RowHeadersVisible=false,BackgroundColor=System.Drawing.Color.White};
        internal SleeveConstructionForm(Document doc)
        {
            _doc=doc;Text="HB_BIM｜套管施工定位與查驗包";Width=1180;Height=850;MinimumSize=new Size(940,690);
            Font=new Font("Microsoft JhengHei UI",10);AutoScaleMode=F.AutoScaleMode.Dpi;StartPosition=F.FormStartPosition.CenterParent;
            var root=new F.TableLayoutPanel{Dock=F.DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new F.Padding(12)};
            root.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));root.RowStyles.Add(new F.RowStyle(F.SizeType.Percent,100));
            root.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));root.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));Controls.Add(root);
            root.Controls.Add(new F.Label{AutoSize=true,Text="先預覽範圍，再建立定位圖與數量查驗表。既有來源平面圖、尺寸及人工標註保留。\n定位以圖面右／上為正；中心標高相對來源平面圖樓層。草稿不代表核准施工。"},0,0);
            var tabs=new F.TabControl{Dock=F.DockStyle.Fill};root.Controls.Add(tabs,0,1);
            var create=new F.TabPage("1 定位與發圖");var record=new F.TabPage("2 版次與現場查驗");tabs.TabPages.Add(create);tabs.TabPages.Add(record);
            var layout=new F.TableLayoutPanel{Dock=F.DockStyle.Fill,RowCount=3,ColumnCount=1};
            layout.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));layout.RowStyles.Add(new F.RowStyle(F.SizeType.Percent,100));layout.RowStyles.Add(new F.RowStyle(F.SizeType.AutoSize));create.Controls.Add(layout);
            var inputs=new F.TableLayoutPanel{Dock=F.DockStyle.Top,AutoSize=true,ColumnCount=4};
            inputs.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.AutoSize));inputs.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent,50));
            inputs.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.AutoSize));inputs.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent,50));
            Add(inputs,"施工包名稱",_name,0,0);Add(inputs,"版次",_revision,2,0);
            Add(inputs,"來源平面圖",_source,0,1);Add(inputs,"套管約束樓層",_level,2,1);
            Add(inputs,"A 軸（圖面上下）",_a,0,2);Add(inputs,"B 軸（圖面左右）",_b,2,2);
            Add(inputs,"區域（精確比對）",_zone,0,3);Add(inputs,"新圖紙圖框",_title,2,3);
            layout.Controls.Add(inputs,0,0);layout.Controls.Add(_preview,0,1);
            var actions=new F.FlowLayoutPanel{AutoSize=true,Dock=F.DockStyle.Fill};layout.Controls.Add(actions,0,2);
            actions.Controls.Add(Button("預覽定位與數量",()=>ShowPreview(SleeveConstructionService.Preview(_doc,Request()))));
            actions.Controls.Add(Button("建立／更新草稿",()=>{RequireSavedField();var p=SleeveConstructionService.CreateOrUpdate(_doc,Request());ShowPreview(p);ReloadPackages(p.Id);_status.Text="已建立／更新 "+p.Items.Count+" 支定位資料與 "+p.SheetIds.Count+" 張圖紙。請開啟定位圖核對及整理標註，儲存 RVT。";}));
            var fieldRoot=new F.TableLayoutPanel{Dock=F.DockStyle.Fill,RowCount=5,ColumnCount=1};
            for(int i=0;i<5;i++)fieldRoot.RowStyles.Add(new F.RowStyle(i==1?F.SizeType.Percent:F.SizeType.AutoSize,i==1?100:0));record.Controls.Add(fieldRoot);
            fieldRoot.Controls.Add(_packages,0,0);fieldRoot.Controls.Add(_field,0,1);
            var approval=new F.TableLayoutPanel{AutoSize=true,Dock=F.DockStyle.Fill,ColumnCount=4};
            approval.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.AutoSize));approval.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent,30));
            approval.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.AutoSize));approval.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent,70));
            Add(approval,"核准人",_person,0,0);Add(approval,"核准文件／簽核編號",_approval,2,0);fieldRoot.Controls.Add(approval,0,2);
            fieldRoot.Controls.Add(_reviewed,0,3);
            var tools=new F.FlowLayoutPanel{AutoSize=true,Dock=F.DockStyle.Fill};fieldRoot.Controls.Add(tools,0,4);
            tools.Controls.Add(Button("檢查模型變更",()=>{RequirePackage();var impacts=SleeveConstructionService.Impacts(_doc,_loaded);_status.Text=impacts.Count==0?"目前套管資料與此版一致；人工尺寸與詳圖仍須覆核。":string.Join("\n",impacts.Take(10));}));
            tools.Controls.Add(Button("儲存查驗紀錄",()=>{RequirePackage(true);SaveField();ReloadPackages(_loaded.Id);_status.Text="查驗紀錄已寫入模型；實測偏差不自動判定合格。請儲存 RVT。";}));
            tools.Controls.Add(Button("登錄人工核准",()=>{RequirePackage();if(!_reviewed.Checked)throw new InvalidOperationException("請先完成圖面人工核對與核准文件確認。");SleeveConstructionService.Approve(_doc,_loaded.Id,_person.Text,_approval.Text);ReloadPackages(_loaded.Id);_status.Text="已登錄人工核准結果；此版定位資料鎖定，設計變更請建立新版。";}));
            tools.Controls.Add(Button("匯出 PDF／查驗 CSV",()=>{RequirePackage();using(var picker=new F.FolderBrowserDialog{Description="選擇施工包輸出資料夾（每次建立獨立子資料夾）"})if(picker.ShowDialog(this)==F.DialogResult.OK){string folder=SleeveConstructionService.Export(_doc,_loaded.Id,picker.SelectedPath);_status.Text="已匯出："+folder;}}));
            tools.Controls.Add(Button("作廢此版",()=>{RequirePackage();if(F.MessageBox.Show(this,"將此版標記作廢並停止匯出？原圖紙、版次與查驗資料會保留。","施工包作廢",F.MessageBoxButtons.OKCancel,F.MessageBoxIcon.Warning)==F.DialogResult.OK){SleeveConstructionService.Void(_doc,_loaded.Id,_approval.Text);ReloadPackages(_loaded.Id);}}));
            root.Controls.Add(_status,0,2);var close=new F.Button{Text="關閉",AutoSize=true,DialogResult=F.DialogResult.Cancel};root.Controls.Add(close,0,3);CancelButton=close;
            var generatedPlans=new HashSet<string>(SleeveConstructionService.List(doc).Select(p=>p.PlanId));
            foreach(var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v=>!v.IsTemplate&&v.GenLevel!=null&&!generatedPlans.Contains(v.UniqueId)&&v.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing)).OrderBy(v=>v.Name))_source.Items.Add(new Choice{Id=v.Id,Name=v.Name});
            foreach(var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>l.Elevation))_level.Items.Add(new Choice{Id=l.Id,Name=l.Name});
            foreach(var g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().Where(g=>g.Curve is Line).OrderBy(g=>g.Name)){_a.Items.Add(new Choice{Id=g.Id,Name=g.Name});_b.Items.Add(new Choice{Id=g.Id,Name=g.Name});}
            foreach(var t in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(t=>t.FamilyName))_title.Items.Add(new Choice{Id=t.Id,Name=t.FamilyName+": "+t.Name});
            foreach(var combo in new[]{_source,_level,_a,_b,_title})if(combo.Items.Count>0)combo.SelectedIndex=0;
            var active=_source.Items.Cast<Choice>().FirstOrDefault(c=>c.Id==doc.ActiveView.Id);if(active!=null)_source.SelectedItem=active;
            _source.SelectedIndexChanged+=(s,e)=>SelectSourceDefaults();SelectSourceDefaults();
            _packages.SelectedIndexChanged+=(s,e)=>
            {
                if(_restoringPackage)return;
                if(_fieldDirty)
                {
                    _restoringPackage=true;_packages.SelectedItem=_packages.Items.Cast<PackageChoice>().FirstOrDefault(p=>p.Package.Id==_loaded?.Id);_restoringPackage=false;
                    F.MessageBox.Show(this,"請先儲存目前的查驗紀錄，再切換版次。",Text);return;
                }
                LoadField();
            };
            _field.CellValueChanged+=(s,e)=>{if(!_loadingField&&_loaded!=null){_fieldDirty=true;_status.Text="查驗紀錄尚未儲存。";}};
            _field.CurrentCellDirtyStateChanged+=(s,e)=>{if(_field.IsCurrentCellDirty)_field.CommitEdit(F.DataGridViewDataErrorContexts.Commit);};
            FormClosing+=(s,e)=>
            {
                _field.EndEdit();if(!_fieldDirty)return;
                var answer=F.MessageBox.Show(this,"查驗紀錄尚未儲存，是否先寫入模型？",Text,F.MessageBoxButtons.YesNoCancel,F.MessageBoxIcon.Question);
                if(answer==F.DialogResult.Cancel){e.Cancel=true;return;}
                if(answer==F.DialogResult.Yes)try{SaveField();}catch(Exception ex){e.Cancel=true;F.MessageBox.Show(this,ex.Message,Text);}
            };
            ReloadPackages(null);
            var area=F.Screen.FromControl(this).WorkingArea;Size=new Size(Math.Min(Width,area.Width-24),Math.Min(Height,area.Height-24));
        }
        private void SelectSourceDefaults()
        {
            var view=_doc.GetElement((_source.SelectedItem as Choice)?.Id??ElementId.InvalidElementId) as ViewPlan;
            if(view==null)return;
            var level=_level.Items.Cast<Choice>().FirstOrDefault(c=>c.Id==view.GenLevel.Id);if(level!=null)_level.SelectedItem=level;
            foreach(var pair in new[]{Tuple.Create(_a,view.UpDirection),Tuple.Create(_b,view.RightDirection)})
            {
                var grid=pair.Item1.Items.Cast<Choice>().FirstOrDefault(c=>_doc.GetElement(c.Id) is Grid g&&g.Curve is Line l&&Math.Abs(l.Direction.DotProduct(pair.Item2))>1-1e-8);
                if(grid!=null)pair.Item1.SelectedItem=grid;
            }
        }
        private static void Add(F.TableLayoutPanel p,string label,F.Control control,int col,int row){p.Controls.Add(new F.Label{Text=label,AutoSize=true,Anchor=F.AnchorStyles.Left},col,row);control.Dock=F.DockStyle.Fill;p.Controls.Add(control,col+1,row);}
        private F.Button Button(string text,Action action)
        {
            var button=new F.Button{Text=text,AutoSize=true};button.Click+=(s,e)=>{button.Enabled=false;UseWaitCursor=true;try{action();}catch(Exception ex){F.MessageBox.Show(this,ex.Message,Text,F.MessageBoxButtons.OK,F.MessageBoxIcon.Warning);}finally{UseWaitCursor=false;button.Enabled=true;}};return button;
        }
        private SleeveConstructionRequest Request()=>new SleeveConstructionRequest{Name=_name.Text,Revision=_revision.Text,Zone=_zone.Text,
            SourceViewId=(_source.SelectedItem as Choice)?.Id,ScopeLevelId=(_level.SelectedItem as Choice)?.Id,GridAId=(_a.SelectedItem as Choice)?.Id,
            GridBId=(_b.SelectedItem as Choice)?.Id,TitleBlockId=(_title.SelectedItem as Choice)?.Id};
        private void ShowPreview(SleeveConstructionPackage p)
        {
            _preview.DataSource=p.Items.Select(i=>new{編號=i.Mark,系統=i.System,規格=i.Specification,長度mm=i.Length,距A軸mm=i.OffsetA,距B軸mm=i.OffsetB,中心標高mm=i.Elevation,檢核=i.Check}).ToList();
            _status.Text=p.Items.Count+" 支；圖面樓層／高程基準："+p.DatumName+"；套管約束樓層："+p.LevelName+"。請確認兩者與施工範圍一致。";
        }
        private void ReloadPackages(string id)
        {
            _packages.Items.Clear();foreach(var p in SleeveConstructionService.List(_doc))_packages.Items.Add(new PackageChoice{Package=p});
            var item=_packages.Items.Cast<PackageChoice>().FirstOrDefault(x=>x.Package.Id==id)??_packages.Items.Cast<PackageChoice>().FirstOrDefault();
            if(item!=null)_packages.SelectedItem=item;
        }
        private void RequireSavedField(){_field.EndEdit();if(_fieldDirty)throw new InvalidOperationException("請先儲存查驗紀錄，再執行此操作。");}
        private void RequirePackage(bool allowEdits=false){if(_loaded==null)throw new InvalidOperationException("請先選擇已建立的施工包。");if(!allowEdits)RequireSavedField();}
        private void SaveField(){_field.EndEdit();SleeveConstructionService.SaveField(_doc,_loaded.Id,ReadField(),_loaded.UpdatedUtc);_fieldDirty=false;}
        private void LoadField()
        {
            _loadingField=true;
            try
            {
            _loaded=(_packages.SelectedItem as PackageChoice)?.Package;_field.Columns.Clear();_field.Rows.Clear();_reviewed.Checked=false;
            if(_loaded==null)return;
            _field.ReadOnly=false;
            _field.Columns.Add(new F.DataGridViewTextBoxColumn{HeaderText="編號",ReadOnly=true});
            var install=new F.DataGridViewComboBoxColumn{HeaderText="施工狀態"};install.Items.AddRange(SleeveConstructionPolicy.Installations);_field.Columns.Add(install);
            var inspection=new F.DataGridViewComboBoxColumn{HeaderText="查驗狀態"};inspection.Items.AddRange(SleeveConstructionPolicy.Inspections);_field.Columns.Add(inspection);
            foreach(var label in new[]{"實測A mm","實測B mm","實測中心標高 mm","查驗人","日期 yyyy-MM-dd","照片索引","備註"})_field.Columns.Add(new F.DataGridViewTextBoxColumn{HeaderText=label});
            foreach(var i in _loaded.Items)
            {
                var r=_loaded.FieldRecords.SingleOrDefault(f=>f.ElementId==i.Id)??new SleeveFieldRecord{ElementId=i.Id};
                int row=_field.Rows.Add(i.Mark,r.Installation,r.Inspection,r.MeasuredA,r.MeasuredB,r.MeasuredElevation,r.Person,r.Date,r.PhotoReference,r.Remark);_field.Rows[row].Tag=i.Id;
            }
            _person.Text=_loaded.ApprovedBy;_approval.Text=_loaded.ApprovalReference;
            }
            finally{_loadingField=false;_fieldDirty=false;}
        }
        private List<SleeveFieldRecord> ReadField()
        {
            var result=new List<SleeveFieldRecord>();
            foreach(F.DataGridViewRow row in _field.Rows)
            {
                string S(int i)=>Convert.ToString(row.Cells[i].Value)??"";
                double? N(int i){if(string.IsNullOrWhiteSpace(S(i)))return null;if(double.TryParse(S(i),NumberStyles.Float,CultureInfo.CurrentCulture,out double v))return v;throw new InvalidOperationException("實測值必須為數字："+S(0));}
                result.Add(new SleeveFieldRecord{ElementId=(string)row.Tag,Installation=S(1),Inspection=S(2),MeasuredA=N(3),MeasuredB=N(4),MeasuredElevation=N(5),Person=S(6),Date=S(7),PhotoReference=S(8),Remark=S(9)});
            }
            return result;
        }
    }
}
