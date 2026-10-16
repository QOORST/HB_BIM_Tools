using System;
using System.Collections.Generic;
using System.Drawing;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YDBIM.AutoDimension.Core;
using Forms = System.Windows.Forms;

namespace YDBIM.AutoDimension.App
{
    internal sealed class DimensionTextCleanupPreviewController : IExternalEventHandler
    {
        internal sealed class Change
        {
            internal string UniqueId;
            internal int Index, SegmentCount;
            internal XYZ Before, After;
            internal double? Value;
        }
        private static DimensionTextCleanupPreviewController current;
        private Document document;
        private ElementId viewId;
        private List<Change> changes;
        private List<DimensionTextPreview.Box> boxes;
        private DimensionTextPreview preview;
        private ExternalEvent externalEvent;
        private UIApplication application;
        private Forms.Form window;
        private Forms.Label status;
        private Forms.CheckBox visible;
        private int request;
        private bool closing;
        private bool stale;

        internal static bool ActivateExisting()
        {
            if (current == null) return false;
            current.window.Activate();
            return true;
        }

        internal static void Show(UIApplication app, Document doc, View view,
            List<DimensionTextPreview.Box> boxes, List<Change> changes, int moved, int skipped, int unresolved)
        {
            var c = new DimensionTextCleanupPreviewController {
                application = app, document = doc, viewId = view.Id, boxes = boxes, changes = changes };
            try
            {
                c.externalEvent = ExternalEvent.Create(c);
                c.window = new Forms.Form {
                    Text = "HB_BIM Tools - 尺寸文字整理｜結果預覽", ClientSize = new Size(460, 175),
                    MinimumSize = new Size(460, 210), ShowInTaskbar = false,
                    StartPosition = Forms.FormStartPosition.CenterParent, Font = new Font("Microsoft JhengHei UI", 10) };
                c.status = new Forms.Label { Dock = Forms.DockStyle.Fill, Padding = new Forms.Padding(12),
                    Text = $"已移動 {moved} 段；略過 {skipped} 條；待複核 {unresolved} 段。" };
                var actions = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 48, Padding = new Forms.Padding(8) };
                var keep = new Forms.Button { Text = "保留結果", AutoSize = true };
                var restore = new Forms.Button { Text = "還原本次", AutoSize = true };
                c.visible = new Forms.CheckBox { Text = "顯示預覽框", Checked = true, AutoSize = true, Margin = new Forms.Padding(12, 6, 0, 0) };
                actions.Controls.AddRange(new Forms.Control[] { keep, restore, c.visible });
                c.window.Controls.Add(c.status);
                c.window.Controls.Add(actions);
                keep.Click += (_, __) => c.Queue(1);
                restore.Click += (_, __) => c.Queue(2);
                c.visible.CheckedChanged += (_, __) => c.Queue(3);
                c.window.FormClosing += (_, e) => {
                    if (!c.closing) { e.Cancel = true; c.Queue(1); }
                };
                app.Idling += c.OnIdle;
                app.Application.DocumentChanged += c.OnDocumentChanged;
                current = c;
                c.TogglePreview();
                c.window.Show(new Owner(app.MainWindowHandle));
            }
            catch (Exception ex)
            {
                c.Cleanup();
                TaskDialog.Show("尺寸文字整理", "整理結果已保留，但預覽視窗無法開啟。可用 Revit 復原。\n" + ex.Message);
            }
        }

        private void Queue(int action)
        {
            if (request != 0) return;
            request = action;
            var result = externalEvent.Raise();
            if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending)
            {
                request = 0;
                status.Text = "Revit 忙碌中，請結束目前指令後重試。";
            }
        }

        public void Execute(UIApplication app)
        {
            int action = request;
            request = 0;
            try
            {
                if (!document.IsValidObject) { Cleanup(); return; }
                if (action == 1) { Cleanup(); return; }
                if (app.ActiveUIDocument == null || !app.ActiveUIDocument.Document.Equals(document))
                { status.Text = "請切回原文件再操作。"; return; }
                if (action == 3) { TogglePreview(); return; }
                if (action != 2) return;
                int skipped = 0;
                using (var tx = new Transaction(document, "還原本次尺寸文字整理"))
                {
                    tx.Start();
                    foreach (var change in changes)
                    {
                        using (var sub = new SubTransaction(document))
                        {
                            sub.Start();
                            try
                            {
                                var dim = document.GetElement(change.UniqueId) as Dimension;
                                if (dim == null || dim.NumberOfSegments != change.SegmentCount)
                                    throw new InvalidOperationException();
                                var segment = dim.Segments.get_Item(change.Index);
                                // Preserve later edits and topology changes instead of blindly replaying old positions.
                                if (!segment.IsTextPositionAdjustable() || segment.TextPosition == null ||
                                    !segment.TextPosition.IsAlmostEqualTo(change.After) || segment.Value != change.Value)
                                    throw new InvalidOperationException();
                                segment.TextPosition = change.Before;
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException();
                            }
                            catch
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped++;
                            }
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("還原未提交，請確認文件狀態。 ");
                }
                Cleanup();
                if (skipped > 0) TaskDialog.Show("尺寸文字整理", $"已略過 {skipped} 段已變更、刪除或無法還原的文字，未覆蓋其目前位置。");
            }
            catch (Exception ex) { status.Text = "操作未完成：" + ex.Message; }
            finally { if (app.ActiveUIDocument != null) app.ActiveUIDocument.RefreshActiveView(); }
        }

        private void TogglePreview()
        {
            preview?.Dispose();
            preview = null;
            if (!stale && visible.Checked && document.GetElement(viewId) is View view)
            {
                preview = new DimensionTextPreview(view, boxes);
                preview.Start();
            }
            application.ActiveUIDocument?.RefreshActiveView();
        }

        private void OnIdle(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs args)
        {
            if (!document.IsValidObject) { Cleanup(); return; }
            if (stale && preview != null)
            {
                preview.Dispose();
                preview = null;
                visible.Enabled = false;
                status.Text = "文件已變更，預覽框已清除。還原時將略過位置或尺寸值已變更的文字。";
                application.ActiveUIDocument?.RefreshActiveView();
            }
            if (preview?.RenderError != null) status.Text = "預覽框繪製失敗：" + preview.RenderError;
        }

        private void OnDocumentChanged(object sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs args)
        {
            if (args.GetDocument().Equals(document)) stale = true;
        }

        private void Cleanup()
        {
            application.Idling -= OnIdle;
            application.Application.DocumentChanged -= OnDocumentChanged;
            try { preview?.Dispose(); }
            finally
            {
                preview = null;
                closing = true;
                window?.Dispose();
                externalEvent?.Dispose();
                if (current == this) current = null;
            }
        }

        public string GetName() => "HB_BIM Dimension Text Preview";
        private sealed class Owner : Forms.IWin32Window
        {
            public IntPtr Handle { get; }
            internal Owner(IntPtr handle) { Handle = handle; }
        }
    }
}
