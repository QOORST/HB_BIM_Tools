using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class MepQuickDrawingForm : Form
    {
        private static readonly Color Accent=Color.FromArgb(0,135,145);
        private static readonly Color Muted=Color.FromArgb(92,102,113);
        private readonly RadioButton relative=Segment("相對高度"), target=Segment("目標樓層");
        private readonly RadioButton ninety=Segment("90°"), fortyFive=Segment("45°"), custom=Segment("自訂");
        private readonly ComboBox levels=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Anchor=AnchorStyles.Left|AnchorStyles.Right};
        private readonly NumericUpDown height=Number(10,5000,0), offset=Number(-1000000,1000000,1), tail=Number(0,10000,0), angle=Number(1,89,1);
        private readonly Label currentValue=ValueLabel(), targetValue=ValueLabel(), directionValue=ValueLabel();
        private readonly Label error=new Label{AutoSize=true,ForeColor=Color.Firebrick,MaximumSize=new Size(540,0)};
        private readonly Panel diagram=new DiagramPanel{Height=92,Dock=DockStyle.Top};
        private readonly Control levelRow,offsetRow,heightRow,angleRow;
        private readonly double? startMm;
        private readonly bool? directionUp;
        private readonly bool storedDirectionUp;
        private bool ready;
        public MepUpDownOffsetOptions Options {get;private set;}

        public MepQuickDrawingForm(MepUpDownOffsetOptions options,List<MepTargetLevel> choices,double? startCenterMm=null,bool? directionUp=null)
        {
            startMm=startCenterMm;this.directionUp=directionUp;storedDirectionUp=options.OffsetUp;
            Text="HB_BIM｜快速上下行設定";
            Font=new Font("Microsoft JhengHei UI",10F);
            AutoScaleMode=AutoScaleMode.Dpi;
            BackColor=Color.White;ForeColor=Color.FromArgb(36,42,48);
            StartPosition=FormStartPosition.CenterScreen;MinimizeBox=false;MaximizeBox=false;
            ClientSize=new Size(620,670);MinimumSize=new Size(480,440);
            var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=62,FlowDirection=FlowDirection.RightToLeft,
                Padding=new Padding(12),BackColor=Color.FromArgb(245,246,247)};
            var apply=new Button{Text="套用設定",AutoSize=true,Height=36,Padding=new Padding(12,4,12,4),
                FlatStyle=FlatStyle.Flat,BackColor=Accent,ForeColor=Color.White};
            apply.FlatAppearance.BorderSize=0;
            var cancel=new Button{Text="取消",AutoSize=true,Height=36,Padding=new Padding(12,4,12,4),
                FlatStyle=FlatStyle.Flat,DialogResult=DialogResult.Cancel};
            cancel.FlatAppearance.BorderColor=Color.FromArgb(190,196,202);
            apply.Click+=(_,__)=>Save();footer.Controls.Add(apply);footer.Controls.Add(cancel);
            AcceptButton=apply;CancelButton=cancel;
            var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(20,12,20,12)};
            var body=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1};
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            scroll.Controls.Add(body);Controls.Add(scroll);Controls.Add(footer);
            Add(body,Row("定位方式",Segments(relative,target)));
            levelRow=Row("目標樓層",levels);Add(body,levelRow);
            offsetRow=Row("管中心偏移",Unit(offset,"mm"));Add(body,offsetRow);
            heightRow=Row("相對高度",Unit(height,"mm"));Add(body,heightRow);
            Add(body,Divider());
            Add(body,Row("繪製角度",Segments(ninety,fortyFive,custom)));
            angleRow=Row("自訂角度",Unit(angle,"°"));Add(body,angleRow);
            Add(body,Row("末端水平段",Unit(tail,"mm")));
            Add(body,diagram);Add(body,Divider());
            Add(body,new Label{Text="本次定位",AutoSize=true,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,8,0,12)});
            var summary=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=3};
            foreach(var label in new[]{"目前管中心","目標管中心","自動方向"})
            {
                summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));
                summary.Controls.Add(new Label{Text=label,AutoSize=true,ForeColor=Muted,Dock=DockStyle.Fill});
            }
            summary.Controls.Add(currentValue,0,1);summary.Controls.Add(targetValue,1,1);summary.Controls.Add(directionValue,2,1);
            directionValue.ForeColor=Accent;Add(body,summary);
            Add(body,new Label{Text="標高以專案內部原點為基準",AutoSize=true,ForeColor=Muted,Margin=new Padding(0,12,0,10)});
            Add(body,Divider());
            Add(body,new Label{Text="僅定位本次新增管段，不隨樓層後續移動。",AutoSize=true,MaximumSize=new Size(540,0),ForeColor=Muted,Margin=new Padding(0,12,0,8)});
            Add(body,error);
            foreach(var choice in choices)levels.Items.Add(choice);
            levels.SelectedItem=choices.FirstOrDefault(l=>l.UniqueId==options.TargetLevelUniqueId);
            height.Value=Clamp(options.OffsetHeightMm,height);offset.Value=Clamp(options.TargetLevelOffsetMm,offset);
            tail.Value=Clamp(options.MiddleLengthMm,tail);angle.Value=Clamp(options.AngleDegrees,angle);
            target.Checked=options.UseTargetLevel;relative.Checked=!options.UseTargetLevel;
            ninety.Checked=options.UseNinetyDegree;fortyFive.Checked=!options.UseNinetyDegree && Math.Abs(options.AngleDegrees-45)<1e-9;
            custom.Checked=!ninety.Checked && !fortyFive.Checked;
            foreach(var radio in new[]{relative,target,ninety,fortyFive,custom})radio.CheckedChanged+=(_,__)=>RefreshPreview();
            foreach(var numeric in new[]{height,offset,tail,angle})numeric.ValueChanged+=(_,__)=>RefreshPreview();
            levels.SelectedIndexChanged+=(_,__)=>RefreshPreview();diagram.Paint+=DrawDiagram;
            ready=true;RefreshPreview();
            var area=Screen.FromControl(this).WorkingArea;
            Size=new Size(Math.Min(Width,area.Width-24),Math.Min(Height,area.Height-24));
        }

        private void RefreshPreview()
        {
            if(!ready)return;
            levelRow.Visible=offsetRow.Visible=target.Checked;heightRow.Visible=relative.Checked;angleRow.Visible=custom.Checked;
            foreach(var radio in new[]{relative,target,ninety,fortyFive,custom})
            {
                radio.BackColor=radio.Checked?Accent:Color.FromArgb(245,246,247);
                radio.ForeColor=radio.Checked?Color.White:ForeColor;
            }
            error.Text="";
            double? goal=Goal();
            currentValue.Text=Format(startMm);targetValue.Text=target.Checked && !goal.HasValue?"待選取樓層":Format(goal);
            if(!startMm.HasValue)directionValue.Text="待選取端點";
            else if(!goal.HasValue)directionValue.Text=target.Checked?"待選取樓層":"依上／下行指令";
            else
            {
                double delta=goal.Value-startMm.Value;
                directionValue.Text=Math.Abs(delta)<10?"高差不足 10 mm":(delta>0?"↑ 上行 ":"↓ 下行 ")+Format(Math.Abs(delta));
            }
            diagram.Invalidate();
        }
        private double? Goal()
        {
            if(target.Checked)return levels.SelectedItem is MepTargetLevel level ? level.ElevationMm+(double)offset.Value : (double?)null;
            if(!startMm.HasValue || !directionUp.HasValue)return null;
            return startMm.Value+(directionUp.Value?1:-1)*(double)height.Value;
        }
        private void Save()
        {
            if(target.Checked && !(levels.SelectedItem is MepTargetLevel)){error.Text="請選擇目標樓層。";return;}
            var goal=Goal();
            if(startMm.HasValue && goal.HasValue && Math.Abs(goal.Value-startMm.Value)<10){error.Text="端點與目標高差不足 10 mm。";return;}
            Options=new MepUpDownOffsetOptions{
                OffsetUp=directionUp??storedDirectionUp,UseNinetyDegree=ninety.Checked,
                AngleDegrees=fortyFive.Checked?45:(double)angle.Value,
                OffsetHeightMm=(double)height.Value,MiddleLengthMm=(double)tail.Value,
                UseTargetLevel=target.Checked,TargetLevelUniqueId=(levels.SelectedItem as MepTargetLevel)?.UniqueId,
                TargetLevelOffsetMm=(double)offset.Value};
            DialogResult=DialogResult.OK;Close();
        }
        private void DrawDiagram(object sender,PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float jointX=diagram.Width*.52F,jointY=46;
            using(var pen=new Pen(Color.FromArgb(90,98,105),2))
            {
                e.Graphics.DrawLine(pen,jointX-100,jointY+13,jointX,jointY);
                pen.DashStyle=DashStyle.Dash;pen.Width=1;
                e.Graphics.DrawLine(pen,jointX,jointY,jointX+100,jointY);
            }
            var goal=Goal();bool known=startMm.HasValue && goal.HasValue;
            int sign=known?(goal.Value>=startMm.Value?-1:1):(directionUp==false?1:-1);
            double radians=(ninety.Checked?90:fortyFive.Checked?45:(double)angle.Value)*Math.PI/180;
            float endX=jointX+(float)(40*Math.Cos(radians)),endY=jointY+sign*(float)(40*Math.Sin(radians));
            bool directionKnown=known || (!target.Checked && directionUp.HasValue);
            using(var pen=new Pen(directionKnown?Accent:Muted,3))
            using(var cap=new AdjustableArrowCap(3,4))
            {
                pen.CustomEndCap=cap;e.Graphics.DrawLine(pen,jointX,jointY,endX,endY);
            }
            using(var brush=new SolidBrush(Muted))e.Graphics.DrawString(directionKnown?"水平面基準":"角度示意／方向待選取",Font,brush,8,65);
        }
        private static string Format(double? value)=>value.HasValue?value.Value.ToString("#,0.###",CultureInfo.InvariantCulture)+" mm":"待選取端點";
        private static decimal Clamp(double value,NumericUpDown control)=>Math.Max(control.Minimum,Math.Min(control.Maximum,(decimal)value));
        private static NumericUpDown Number(decimal min,decimal max,int places)=>new NumericUpDown{Minimum=min,Maximum=max,DecimalPlaces=places,Increment=places==0?50:0.5m,BorderStyle=BorderStyle.FixedSingle,Width=180};
        private static RadioButton Segment(string text)=>new RadioButton{Text=text,Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,AutoSize=true,TextAlign=ContentAlignment.MiddleCenter,Padding=new Padding(14,7,14,7),Margin=Padding.Empty};
        private static Label ValueLabel()=>new Label{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,8,10,8)};
        private static Control Segments(params RadioButton[] buttons)
        {
            var panel=new FlowLayoutPanel{Height=44,Dock=DockStyle.Fill,Margin=Padding.Empty};
            foreach(var button in buttons){button.FlatAppearance.BorderColor=Color.FromArgb(195,201,206);button.FlatAppearance.CheckedBackColor=Accent;panel.Controls.Add(button);}return panel;
        }
        private static Control Unit(Control number,string unit)
        {
            var panel=new FlowLayoutPanel{Height=32,Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(0,8,0,0)};
            panel.Controls.Add(number);panel.Controls.Add(new Label{Text=unit,AutoSize=true,Margin=new Padding(6,4,0,0)});return panel;
        }
        private static Control Row(string label,Control field)
        {
            var row=new TableLayoutPanel{Height=48,Dock=DockStyle.Top,ColumnCount=2,Margin=new Padding(0,4,0,4)};
            row.RowCount=1;row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            row.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,8,0,8)},0,0);
            row.Controls.Add(field,1,0);return row;
        }
        private static Control Divider()=>new Panel{Height=1,BackColor=Color.FromArgb(219,223,227),Dock=DockStyle.Top,Margin=new Padding(0,10,0,10)};
        private static void Add(TableLayoutPanel body,Control control)
        {
            body.RowCount++;body.RowStyles.Add(new RowStyle(SizeType.AutoSize));body.Controls.Add(control,0,body.RowCount-1);
        }
        private sealed class DiagramPanel : Panel
        {
            public DiagramPanel(){DoubleBuffered=true;}
        }
    }
}
