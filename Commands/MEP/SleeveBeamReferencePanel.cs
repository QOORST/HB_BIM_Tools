using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // Presentation of the existing 2024 reference only; no design checks are performed here.
    internal sealed class SleeveBeamReferencePanel : Control
    {
        internal SleeveBeamReferencePanel()
        {
            DoubleBuffered=true; BackColor=Color.White;
            AccessibleName="穿梁複核參考：梁端柱邊 2H、孔徑與梁深比例、孔距";
            ResizeRedraw=true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g=e.Graphics;
            float scale=System.Math.Min(ClientSize.Width/900f,ClientSize.Height/350f);
            if(scale<=0) return;
            g.TranslateTransform((ClientSize.Width-900*scale)/2,(ClientSize.Height-350*scale)/2);
            g.ScaleTransform(scale,scale); g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var font=new Font("Microsoft JhengHei UI",12))
            using(var small=new Font("Microsoft JhengHei UI",10))
            using(var outline=new Pen(Color.FromArgb(110,125,140),2))
            using(var red=new Pen(Color.FromArgb(190,55,55),3))
            using(var amber=new Pen(Color.FromArgb(190,115,25),3))
            using(var beam=new SolidBrush(Color.FromArgb(226,233,239)))
            using(var column=new SolidBrush(Color.FromArgb(193,204,215)))
            using(var text=new SolidBrush(Color.FromArgb(45,55,65)))
            {
                g.DrawString("穿梁複核參考",font,text,24,18);
                g.FillRectangle(beam,70,125,760,90); g.DrawRectangle(outline,70,125,760,90);
                g.FillRectangle(column,40,90,60,170); g.FillRectangle(column,800,90,60,170);
                g.DrawRectangle(outline,40,90,60,170); g.DrawRectangle(outline,800,90,60,170);
                g.DrawLine(outline,875,125,875,215); g.DrawString("H",font,text,877,158);
                red.DashStyle=DashStyle.Dash;
                g.DrawRectangle(red,100,118,120,104); g.DrawRectangle(red,680,118,120,104);
                red.DashStyle=DashStyle.Solid;
                g.DrawString("梁端／柱邊 2H",small,Brushes.Firebrick,102,240);
                g.DrawString("梁端／柱邊 2H",small,Brushes.Firebrick,676,240);
                g.FillEllipse(Brushes.White,290,151,38,38); g.DrawEllipse(amber,290,151,38,38);
                g.DrawString("孔徑 ≥ 100 mm",small,Brushes.DarkGoldenrod,252,70);
                g.DrawString("提醒複核",small,Brushes.DarkGoldenrod,274,94);
                g.FillEllipse(Brushes.White,417,142,56,56); g.DrawEllipse(red,417,142,56,56);
                g.DrawString("孔徑 ≥ 200 mm",small,Brushes.Firebrick,390,46);
                g.DrawString("或孔徑 > H/3",small,Brushes.Firebrick,397,70);
                g.DrawString("需結構確認",small,Brushes.Firebrick,400,94);
                g.FillEllipse(Brushes.White,546,155,30,30); g.DrawEllipse(red,546,155,30,30);
                g.FillEllipse(Brushes.White,609,155,30,30); g.DrawEllipse(red,609,155,30,30);
                g.DrawLine(outline,561,232,624,232);
                g.DrawString("孔距 < 3D 或 300 mm",small,Brushes.Firebrick,505,263);
                g.DrawString("H：梁深　D：孔徑　｜　示意非比例圖",small,text,250,312);
            }
        }
    }
}
