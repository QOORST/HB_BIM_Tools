using System.Drawing;
using System.Windows.Forms;

namespace YDBIM.AutoDimension.UI
{
    internal sealed class DimensionTextCleanupForm : Form
    {
        private readonly NumericUpDown gap = new NumericUpDown { Minimum = 0, Maximum = 10, DecimalPlaces = 1, Increment = 0.1m, Value = 0.8m, Width = 100 };
        private readonly NumericUpDown offset = new NumericUpDown { Minimum = 0.5m, Maximum = 30, DecimalPlaces = 1, Increment = 0.5m, Value = 5, Width = 100 };
        internal double Gap => (double)gap.Value;
        internal double Offset => (double)offset.Value;
        internal DimensionTextCleanupForm()
        {
            Text = "HB_BIM｜尺寸文字整理";
            Font = new Font("Microsoft JhengHei UI", 10);
            BackColor = Color.FromArgb(246, 247, 249);
            ClientSize = new Size(510, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            var title = new Label { Text = "整理擁擠的尺寸文字", Font = new Font(Font.FontFamily, 14, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) };
            var hint = new Label { Text = "僅處理目前視圖中選取的直線連續尺寸。\n保留正常文字，擁擠處向尺寸線兩側交錯排列。", AutoSize = true, Location = new Point(24, 60) };
            var first = new Label { Text = "文字淨間距（紙面 mm）", AutoSize = true, Location = new Point(24, 123) };
            var second = new Label { Text = "每層偏移（紙面 mm）", AutoSize = true, Location = new Point(24, 163) };
            gap.Location = new Point(315, 119); offset.Location = new Point(315, 159);
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(270, 224), Size = new Size(94, 34), FlatStyle = FlatStyle.Flat };
            var preview = new Button { Text = "預覽結果", DialogResult = DialogResult.OK, Location = new Point(374, 224), Size = new Size(112, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(32, 113, 206), ForeColor = Color.White };
            Controls.AddRange(new Control[] { title, hint, first, second, gap, offset, cancel, preview });
            AcceptButton = preview; CancelButton = cancel;
        }
    }
}
