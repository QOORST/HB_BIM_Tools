using System;
using System.Drawing;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Helpers.UI
{
    // Hosts existing controls temporarily; callers retain ownership and values.
    internal static class ToolSettingsDialog
    {
        private sealed class ContentForm : Form
        {
            internal Action Release;
            protected override void Dispose(bool disposing)
            {
                if (disposing) Release?.Invoke();
                base.Dispose(disposing);
            }
        }
        internal static Form Create(Control content, string title, Size size)
        {
            var parent = content.Parent;
            var dock = content.Dock;
            var visible = content.Visible;
            var dialog = new ContentForm { Text = title, Size = size, MinimumSize = new Size(540, 330),
                Font = content.Font, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false, MaximizeBox = true, AutoScaleMode = AutoScaleMode.Dpi };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16) };
            content.Dock = DockStyle.Top; content.Visible = true;
            scroll.Controls.Add(content);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
            var done = new Button { Text = "完成", AutoSize = true, DialogResult = DialogResult.OK };
            footer.Controls.Add(done);
            dialog.Controls.Add(scroll); dialog.Controls.Add(footer);
            dialog.AcceptButton = done; dialog.CancelButton = done;
            bool detached = false;
            void Detach()
            {
                if (detached) return;
                detached = true;
                scroll.Controls.Remove(content);
                if (parent != null && !parent.IsDisposed) parent.Controls.Add(content);
                content.Dock = dock; content.Visible = visible;
            }
            dialog.FormClosed += (_, _) => Detach();
            // Disposing without ShowDialog must also preserve the caller's controls.
            dialog.Release = Detach;
            dialog.Shown += (_, _) => {
                var area = Screen.FromHandle(dialog.Handle).WorkingArea;
                dialog.MinimumSize = new Size(Math.Min(540, area.Width - 24), Math.Min(330, area.Height - 24));
                dialog.Size = new Size(Math.Min(dialog.Width, area.Width - 24), Math.Min(dialog.Height, area.Height - 24));
            };
            return dialog;
        }
    }
}
