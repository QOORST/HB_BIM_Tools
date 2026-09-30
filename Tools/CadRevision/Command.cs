using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;
using Acad=Autodesk.AutoCAD.ApplicationServices.Application;

namespace HB.CadRevision
{
    public sealed class Command
    {
        [CommandMethod("HBCADCOMPARE",CommandFlags.Modal)]
        public void Execute()
        {
            string stage="開啟設定",diagnosticFolder=null;
            try
            {
                using(var form=new Form { Text="HB｜CAD 版次比對（試用版）",Width=820,Height=720,StartPosition=FormStartPosition.CenterScreen,Font=new System.Drawing.Font("Microsoft JhengHei UI",10) })
                {
                    var layout=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=3,RowCount=12 };
                    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));
                    TextBox oldPath=new TextBox { Dock=DockStyle.Fill },newPath=new TextBox { Dock=DockStyle.Fill },layers=new TextBox { Dock=DockStyle.Fill };
                    var paths=new[]{oldPath,newPath};
                    for(int i=0;i<2;i++) { var box=paths[i];layout.Controls.Add(new Label { Text=i==0?"舊版 DWG":"新版 DWG",AutoSize=true },0,i);layout.Controls.Add(box,1,i);
                        var browse=new Button { Text="選擇" };browse.Click+=(s,e)=>{using(var picker=new OpenFileDialog { Filter="DWG|*.dwg",CheckFileExists=true }) if(picker.ShowDialog(form)==DialogResult.OK) box.Text=picker.FileName;};layout.Controls.Add(browse,2,i); }
                    var tolerance=new NumericUpDown { DecimalPlaces=2,Minimum=.01m,Maximum=100,Value=1 };
                    var radius=new NumericUpDown { Minimum=1,Maximum=10000,Value=500 };
                    var unitBoxes=new ComboBox[2];
                    for(int i=0;i<2;i++)
                    {
                        var units=new ComboBox { Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList };
                        units.Items.AddRange(new object[]{"依圖檔 INSUNITS（未設定時停止）","毫米 mm（1 公尺＝1000）","公分 cm（1 公尺＝100）","公尺 m（1 公尺＝1）"});
                        units.SelectedIndex=0;unitBoxes[i]=units;
                        layout.Controls.Add(new Label { Text=i==0?"舊版繪圖單位":"新版繪圖單位",AutoSize=true },0,i+2);layout.Controls.Add(units,1,i+2);
                    }
                    layout.Controls.Add(new Label { Text="幾何容差 mm",AutoSize=true },0,4);layout.Controls.Add(tolerance,1,4);
                    layout.Controls.Add(new Label { Text="位移候選範圍 mm",AutoSize=true },0,5);layout.Controls.Add(radius,1,5);
                    layout.Controls.Add(new Label { Text="圖層（分號分隔）",AutoSize=true },0,6);layout.Controls.Add(layers,1,6);
                    var includeXrefs=new CheckBox { Text="納入外部參考底圖（取消可先比對主圖；一般圖塊仍納入）",Checked=true,AutoSize=true };
                    layout.Controls.Add(includeXrefs,0,7);layout.SetColumnSpan(includeXrefs,3);
                    var capacity=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
                    capacity.Items.AddRange(new object[]{"一般圖面：每版 20 萬筆","大型圖面：每版 50 萬筆（較耗記憶體與時間）"});capacity.SelectedIndex=0;
                    layout.Controls.Add(new Label {Text="處理容量",AutoSize=true},0,8);layout.Controls.Add(capacity,1,8);
                    var projectPlan=new CheckBox {Text="平面比對（試用）：納入平行 XY 的非零高程幾何（不比較高程差異）",AutoSize=true};
                    layout.Controls.Add(projectPlan,0,9);layout.SetColumnSpan(projectPlan,3);
                    var confirmed=new CheckBox { Text="已確認兩圖單位、原點與方向，換算後位置對齊；圖層留白表示全部。",AutoSize=true };
                    layout.Controls.Add(confirmed,0,10);layout.SetColumnSpan(confirmed,3);
                    var run=new Button { Text="開始比對",AutoSize=true };layout.Controls.Add(run,2,11);form.Controls.Add(layout);
                    run.Click+=(s,e)=>{ if(!confirmed.Checked||!File.Exists(oldPath.Text)||!File.Exists(newPath.Text)) { MessageBox.Show(form,"請選取兩份圖檔並確認單位與對齊。手動單位僅用於本次換算，不修改 DWG。模型空間以外及不支援物件會列入略過清單。",form.Text);return;} form.DialogResult=DialogResult.OK; };
                    if(Acad.ShowModalDialog(form)!=DialogResult.OK) return;
                    var excludedXrefs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bool includeSharedNested=false;
                    var sharedXrefs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    if(includeXrefs.Checked)
                    {
                        stage="讀取兩版外部參考清單";
                        var oldRefs=DwgReader.ListXrefs(oldPath.Text);var newRefs=DwgReader.ListXrefs(newPath.Text);
                        var names=oldRefs.Concat(newRefs).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToArray();
                        if(names.Length>0)
                        using(var picker=new Form {Text="選擇要比對的外部參考",Width=780,Height=470,StartPosition=FormStartPosition.CenterScreen,Font=form.Font})
                        {
                            var list=new CheckedListBox {Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true};
                            foreach(var name in names)list.Items.Add(name+"  ["+(oldRefs.Contains(name,StringComparer.OrdinalIgnoreCase)?"舊":"")+(newRefs.Contains(name,StringComparer.OrdinalIgnoreCase)?"新":"")+"]",true);
                            var nested=new CheckBox {Text="共用底圖另納入巢狀外部參考（資料量可能大幅增加）",Dock=DockStyle.Bottom,Height=32,Checked=false};
                            var note=new Label {Text="勾選＝納入；一般圖塊及主圖一律保留。\n同名參考套用兩版；巢狀參考隨上層納入。名稱相同不保證檔案為正確歷史版本。",Dock=DockStyle.Top,Height=65,Padding=new Padding(10)};
                            var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=50,FlowDirection=FlowDirection.LeftToRight};
                            var all=new Button {Text="全部勾選",AutoSize=true};var none=new Button {Text="全部取消",AutoSize=true};
                            var shared=new Button {Text="指定兩版共用底圖",AutoSize=true};
                            shared.Click+=(s,e)=>{
                                int index=list.SelectedIndex;
                                if(index<0){MessageBox.Show(picker,"請先選取清單中的參考名稱。");return;}
                                using(var file=new OpenFileDialog {Filter="DWG|*.dwg",CheckFileExists=true,InitialDirectory=Path.GetDirectoryName(oldPath.Text),Title="選擇兩版共用底圖（不比對底圖歷史變更）"})
                                if(file.ShowDialog(picker)==DialogResult.OK){sharedXrefs[names[index]]=file.FileName;list.Items[index]=names[index]+" [兩版共用："+file.FileName+"]";list.SetItemChecked(index,true);}
                            };
                            all.Click+=(s,e)=>{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,true);};
                            none.Click+=(s,e)=>{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,false);};
                            var ok=new Button {Text="開始比對",DialogResult=DialogResult.OK,AutoSize=true};var cancel=new Button {Text="取消",DialogResult=DialogResult.Cancel,AutoSize=true};
                            buttons.Controls.AddRange(new Control[]{all,none,shared,ok,cancel});picker.Controls.Add(list);picker.Controls.Add(note);picker.Controls.Add(nested);picker.Controls.Add(buttons);picker.AcceptButton=ok;picker.CancelButton=cancel;
                            if(Acad.ShowModalDialog(picker)!=DialogResult.OK)return;
                            includeSharedNested=nested.Checked;
                            for(int i=0;i<names.Length;i++)if(!list.GetItemChecked(i))excludedXrefs.Add(names[i]);
                        }
                    }
                    using(var folder=new FolderBrowserDialog { Description="選擇報告輸出資料夾（將建立獨立子資料夾）" })
                    {
                        if(folder.ShowDialog()!=DialogResult.OK) return;
                        diagnosticFolder=folder.SelectedPath;
                        var filter=new HashSet<string>(layers.Text.Split(';').Select(x=>x.Trim()).Where(x=>x.Length>0),StringComparer.OrdinalIgnoreCase);
                        stage="讀取舊版："+oldPath.Text;
                        var a=DwgReader.Read(oldPath.Text,filter,GetUnit(unitBoxes[0].SelectedIndex),includeXrefs.Checked,excludedXrefs,sharedXrefs,includeSharedNested,capacity.SelectedIndex==1?500000:200000,projectPlan.Checked);
                        stage="讀取新版："+newPath.Text;
                        var b=DwgReader.Read(newPath.Text,filter,GetUnit(unitBoxes[1].SelectedIndex),includeXrefs.Checked,excludedXrefs,sharedXrefs,includeSharedNested,capacity.SelectedIndex==1?500000:200000,projectPlan.Checked);
                        if(a.Segments.Count+a.Curves.Count==0&&b.Segments.Count+b.Curves.Count==0) throw new InvalidOperationException("兩份圖都沒有可比對幾何，請確認圖層、圖塊、單位與高程。");
                        stage="比對直線";
                        var changes=CompareCore.Compare(a.Segments,b.Segments,(double)tolerance.Value,(double)radius.Value);
                        stage="比對圓與圓弧";
                        var curves=CurveCore.Compare(a.Curves,b.Curves,(double)tolerance.Value,(double)radius.Value);
                        stage="輸出報告";
                        var destination=Path.Combine(folder.SelectedPath,"CAD-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(destination);
                        if(a.Segments.Count+b.Segments.Count>0) Report.Write(destination,oldPath.Text,newPath.Text,a,b,changes,(double)tolerance.Value,(double)radius.Value);
                        else File.WriteAllText(Path.Combine(destination,"report.html"),"<!doctype html><meta charset='utf-8'><p>本次沒有直線段。</p><a href='curves.html'>圓與圓弧報告</a>");
                        CurveReport.Write(destination,a,b,curves);
                        UnifiedReport.Write(destination,oldPath.Text,newPath.Text,a,b,changes,curves,(double)tolerance.Value,(double)radius.Value);
                        Acad.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n報告："+Path.Combine(destination,"review.html"));
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(destination,"review.html")) {UseShellExecute=true}); }
                        catch(System.Exception openError) { Acad.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n報告已產生，瀏覽器未能開啟："+openError.Message); }
                        MessageBox.Show("比對完成。報告位置：\n"+destination+"\n\nreview.html：整合檢視（含滑桿）；scope.txt：範圍與略過項目。\n未修改來源 DWG；疑似位移須人工確認。","HB CAD 比對");
                    }
                }
            }
            catch(System.Exception ex)
            {
                var message="\nCAD 比對未完成；階段："+stage+"\n"+ex.Message;
                if(diagnosticFolder!=null)
                {
                    try
                    {
                        var log=Path.Combine(diagnosticFolder,"CAD-error-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".txt");
                        File.WriteAllText(log,"階段："+stage+Environment.NewLine+ex.ToString(),System.Text.Encoding.UTF8);
                        message+="\n診斷紀錄："+log;
                    }
                    catch(System.Exception logError) { message+="\n診斷紀錄無法寫入："+logError.Message; }
                }
                Acad.DocumentManager.MdiActiveDocument.Editor.WriteMessage(message);
            }
        }
        static Autodesk.AutoCAD.DatabaseServices.UnitsValue? GetUnit(int index)
        {
            switch(index)
            {
                case 1:return Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters;
                case 2:return Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters;
                case 3:return Autodesk.AutoCAD.DatabaseServices.UnitsValue.Meters;
                default:return null;
            }
        }
    }
}
