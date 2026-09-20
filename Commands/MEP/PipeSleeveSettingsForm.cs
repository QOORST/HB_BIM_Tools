using System.Drawing;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // WinForms entry for versions whose build excludes the legacy XAML window.
    internal sealed class PipeSleeveSettingsForm : Form
    {
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
        internal PipeSleeveSettingsForm(int selectedCount)
        {
            Text="HB_BIM｜自動套管設定"; Font=new Font("Microsoft JhengHei UI",10);
            ClientSize=new Size(520,480); BackColor=Color.FromArgb(246,247,249);
            FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false;
            StartPosition=FormStartPosition.CenterParent;
            var root=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(24), ColumnCount=1, RowCount=5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
            root.Controls.Add(new Label { Text=$"已選取 {selectedCount} 個管線構件", AutoSize=true, Font=new Font(Font.FontFamily,13,FontStyle.Bold) },0,0);
            var spacing=new FlowLayoutPanel { Dock=DockStyle.Fill };
            spacing.Controls.Add(new Label { Text="預留間隙（mm）", AutoSize=true, Margin=new Padding(0,5,16,0) }); spacing.Controls.Add(clearance);
            root.Controls.Add(spacing,0,1);
            var checks=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, AutoScroll=true };
            checks.Controls.AddRange(new Control[] { current,links,exclude,number,existing,view }); root.Controls.Add(checks,0,2);
            root.Controls.Add(new Label { Text="族型依既有自動辨識規則選用。\n取消「略過既有套管」會更新套管，並可能清理失效的舊套管。", Dock=DockStyle.Fill },0,3);
            var actions=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.RightToLeft };
            var run=new Button { Text="建立套管", Size=new Size(110,34), FlatStyle=FlatStyle.Flat, BackColor=Color.FromArgb(32,113,206), ForeColor=Color.White };
            var cancel=new Button { Text="取消", Size=new Size(90,34), FlatStyle=FlatStyle.Flat, DialogResult=DialogResult.Cancel };
            run.Click+=(s,e)=> {
                if(!current.Checked && !links.Checked) { MessageBox.Show(this,"請至少選擇本機模型或連結模型。",Text,MessageBoxButtons.OK,MessageBoxIcon.Information); return; }
                DialogResult=DialogResult.OK; Close();
            };
            actions.Controls.Add(run); actions.Controls.Add(cancel); root.Controls.Add(actions,0,4);
            Controls.Add(root); AcceptButton=run; CancelButton=cancel;
        }
    }
}
