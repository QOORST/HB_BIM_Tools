using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Forms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class AutoTagPreviewItem
    {
        internal ElementId Id;
        internal string Key;
        internal string Description;
        internal bool NeedsReview;
    }

    internal enum AutoTagPreviewAction { Cancel, Apply, Recalculate }

    internal sealed class AutoTagPreviewDecision
    {
        internal AutoTagPreviewAction Action;
        internal HashSet<string> Excluded = new HashSet<string>(StringComparer.Ordinal);
    }

    // The owning service keeps one rollback-capable group open during this modal preview.
    internal sealed class AutoTagPreviewForm : Forms.Form
    {
        private readonly Forms.CheckedListBox _list = new Forms.CheckedListBox();
        private readonly Forms.Label _status = new Forms.Label { AutoSize = true, Dock = Forms.DockStyle.Fill };
        private readonly IReadOnlyList<AutoTagPreviewItem> _items;
        private readonly UIDocument _ui;
        internal AutoTagPreviewDecision Decision { get; private set; } = new AutoTagPreviewDecision();

        internal AutoTagPreviewForm(UIDocument ui, IReadOnlyList<AutoTagPreviewItem> items)
        {
            _ui = ui; _items = items;
            Text = "HB_BIM | 標籤預覽（尚未套用）";
            Font = new Font("Microsoft JhengHei UI", 9F);
            AutoScaleMode = Forms.AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            Size = new Size(460, 430); MinimumSize = new Size(380, 300);
            StartPosition = Forms.FormStartPosition.Manual;
            var area = Forms.Screen.PrimaryScreen.WorkingArea;
            Location = new System.Drawing.Point(area.Right - Width - 20, area.Top + 90);
            MinimizeBox = false; ShowInTaskbar = false;
            var layout = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Fill, Padding = new Forms.Padding(12), RowCount = 3, ColumnCount = 1 };
            layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
            layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
            layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
            Controls.Add(layout);
            layout.Controls.Add(_status, 0, 0);
            _list.Dock = Forms.DockStyle.Fill; _list.CheckOnClick = false;
            _list.HorizontalScrollbar = true; _list.IntegralHeight = false;
            foreach (var item in items)
                _list.Items.Add((item.NeedsReview ? "[需複核] " : "[待目視確認] ") + item.Description, true);
            _list.ItemCheck += (_, __) => BeginInvoke(new Action(UpdateCount));
            _list.DoubleClick += (_, __) => Locate();
            layout.Controls.Add(_list, 0, 1);
            var actions = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, AutoSize = true, WrapContents = true };
            AddButton(actions, "定位", Locate);
            AddButton(actions, "重新計算", () => Finish(AutoTagPreviewAction.Recalculate));
            AddButton(actions, "套用", () => Finish(AutoTagPreviewAction.Apply));
            var cancel = AddButton(actions, "取消", () => Close());
            CancelButton = cancel;
            layout.Controls.Add(actions, 0, 2);
            UpdateCount();
        }

        private static Forms.Button AddButton(Forms.Control parent, string text, Action action)
        {
            var button = new Forms.Button { Text = text, AutoSize = true, MinimumSize = new Size(76, 30) };
            button.Click += (_, __) => action(); parent.Controls.Add(button); return button;
        }

        private void UpdateCount()
        {
            if (!IsDisposed) _status.Text = $"保留 {_list.CheckedItems.Count} / {_items.Count}；需複核 {_items.Count(i => i.NeedsReview)}";
        }

        private void Locate()
        {
            int index = _list.SelectedIndex;
            if (index < 0) return;
            try { _ui.ShowElements(_items[index].Id); _ui.RefreshActiveView(); }
            catch (Exception ex) { _status.Text = "無法定位：" + ex.Message; }
        }

        private void Finish(AutoTagPreviewAction action)
        {
            Decision = new AutoTagPreviewDecision { Action = action };
            for (int i = 0; i < _items.Count; i++)
                if (!_list.GetItemChecked(i)) Decision.Excluded.Add(_items[i].Key);
            DialogResult = Forms.DialogResult.OK;
            Close();
        }
    }
}
