using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Panel = System.Windows.Forms.Panel;
using Color = System.Drawing.Color;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.UI
{
    internal sealed class DetourPreviewItem
    {
        internal string Label;
        internal IList<XYZ> Path;
    }

    internal sealed class DetourPreviewForm : System.Windows.Forms.Form
    {
        internal DetourPreviewForm(List<DetourPreviewItem> routes, List<string> issues, double clearance)
        {
            Text = "HB_BIM｜避讓預覽與碰撞複查";
            Font = new Font("Microsoft JhengHei UI", 9F);
            Width = 850; Height = 620; MinimumSize = new Size(640, 480);
            StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(new Label { AutoSize = true, Text = $"側向高程預覽｜灰色原路徑、藍色候選路徑｜要求淨距 {clearance:0.#} mm\n確認前均為暫存；取消或關閉會還原整批。" }, 0, 0);
            var choice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            choice.Items.AddRange(routes.Select(r => (object)r.Label).ToArray());
            var canvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            canvas.Paint += (_, e) => {
                if (choice.SelectedIndex < 0) return;
                var path = routes[choice.SelectedIndex].Path;
                var origin = path.First(); var delta = path.Last() - origin;
                var direction = new XYZ(delta.X, delta.Y, 0).Normalize();
                var points = path.Select(p => new { X = (p - origin).DotProduct(direction) * 304.8, Y = (p.Z - origin.Z) * 304.8 }).ToList();
                double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
                double scale = Math.Min(Math.Max(1, canvas.Width - 90) / Math.Max(1, maxX - minX), Math.Max(1, canvas.Height - 80) / Math.Max(1, maxY - minY));
                var screen = points.Select(p => new PointF((float)(45 + (p.X - minX) * scale), (float)(canvas.Height - 40 - (p.Y - minY) * scale))).ToArray();
                using (var original = new Pen(Color.Gray, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) e.Graphics.DrawLine(original, screen.First(), screen.Last());
                using (var route = new Pen(Color.FromArgb(0, 105, 180), 2)) e.Graphics.DrawLines(route, screen);
                double slope = 100 * delta.Z / Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                e.Graphics.DrawString($"水平投影長 {maxX - minX:0} mm｜原管坡度 {slope:+0.##;-0.##;0}%（沿圖面左→右）", Font, Brushes.Black, 8, 8);
            };
            choice.SelectedIndexChanged += (_, _) => canvas.Invalidate();
            canvas.Resize += (_, _) => canvas.Invalidate();
            if (choice.Items.Count > 0) choice.SelectedIndex = 0;
            root.Controls.Add(choice, 0, 1); root.Controls.Add(canvas, 0, 2);
            var report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = (issues.Count == 0 ? "複查通過，可確認保留。" : "未通過，禁止建立；請取消後調整選點、高度或方向。") +
                    "\r\n翻彎中段保留原管坡度；洩水／重力排水管仍須依流向確認局部逆坡與積水風險，本次複查未判定排水可行性。\r\n範圍：本機與已載入直接連結的結構、建築、機電及保溫。原有直接接點排除；巢狀／卸載連結視為未完成。\r\n實體交集判斷碰撞；擴張外框僅作保守淨距篩檢，可能誤報。\r\n" + string.Join("\r\n", issues) };
            root.Controls.Add(report, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消並還原", AutoSize = true, DialogResult = DialogResult.Cancel };
            var confirm = new Button { Text = "確認保留", AutoSize = true, Enabled = routes.Count > 0 && issues.Count == 0, DialogResult = DialogResult.OK };
            actions.Controls.Add(cancel); actions.Controls.Add(confirm); root.Controls.Add(actions, 0, 4);
            Controls.Add(root); CancelButton = cancel;
            // Enter defaults to cancel, avoiding accidental acceptance before review.
            AcceptButton = cancel;
        }
    }
}
