using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class CmdPipeSleeveManager : IExternalCommand
    {
        private static PipeSleeveManagerForm _openForm;
        private static PipeSleeveManagerRequestHandler _openHandler;

public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!LicenseManager.Instance.HasFeatureAccess("MEP.PipeSleeve"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權不支援管線套管管理功能。\n\n" +
                        "請升級至 Standard 或 Professional 版本以使用此功能。\n\n" +
                        "點擊「授權管理」按鈕以查看或更新您的授權。");
                    return Result.Cancelled;
                }

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    message = "找不到目前開啟的 Revit 文件。";
                    return Result.Failed;
                }

                if (_openHandler != null && (_openForm == null || _openForm.IsDisposed || !_openHandler.IsCurrentDocument(commandData.Application)))
                {
                    _openHandler.ReleaseInApiContext();
                    _openHandler = null;
                    _openForm = null;
                }
                if (_openForm != null && !_openForm.IsDisposed)
                {
                    _openForm.Activate();
                    return Result.Succeeded;
                }

                var handler = new PipeSleeveManagerRequestHandler();
                ExternalEvent externalEvent = ExternalEvent.Create(handler);
                try
                {
                handler.Bind(commandData.Application, uidoc.Document, externalEvent);
                var form = new PipeSleeveManagerForm(commandData.Application, uidoc, handler, externalEvent);
                handler.Window = form;
                form.FormClosed += (s, e) =>
                {
                    if (ReferenceEquals(_openForm, form)) _openForm = null;
                    handler.CloseRequested = true;
                };
                _openForm = form;
                _openHandler = handler;
                form.Show(new RevitWindow(commandData.Application.MainWindowHandle));
                }
                catch
                {
                    handler.ReleaseInApiContext();
                    if (ReferenceEquals(_openHandler, handler)) { _openHandler = null; _openForm = null; }
                    throw;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("套管管理", "開啟套管管理失敗:\n" + ex.Message);
                return Result.Failed;
            }
        }

        private sealed class RevitWindow : WinForms.IWin32Window
        {
            public RevitWindow(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }
    }

    internal enum PipeSleeveManagerAction
    {
        None,
        Reload,
        Organize,
        Rebase,
        Focus,
        Delete,
        Update
    }

    internal sealed class PipeSleeveManagerRequestHandler : IExternalEventHandler
    {
        private PipeSleeveManagerAction _pendingAction = PipeSleeveManagerAction.None;

        public PipeSleeveManagerForm Window { get; set; }
        private UIApplication _app;
        private Document _document;
        private ExternalEvent _event;
        private bool _released;
        private bool _busy;
        private bool _refreshPending;
        private bool _staleShown;
        private long _lastChangeTick;
        private string _viewId;
        public bool CloseRequested { get; set; }
        internal void Bind(UIApplication app, Document document, ExternalEvent externalEvent)
        {
            _app = app; _document = document; _event = externalEvent;
            _app.Idling += CheckDocument;
            _app.Application.DocumentChanged += ModelChanged;
            _viewId = document.ActiveView?.UniqueId;
        }
        private void ModelChanged(object sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs args)
        {
            if (_released || CloseRequested || !_document.Equals(args.GetDocument())) return;
            MarkRefreshPending();
        }
        private void MarkRefreshPending()
        {
            _refreshPending = true;
            _staleShown = false;
            _lastChangeTick = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        internal void RowsRefreshed()
        {
            _refreshPending = false;
            _staleShown = false;
            _viewId = _document.ActiveView?.UniqueId;
        }
        internal bool IsCurrentDocument(UIApplication app) => _document != null && _document.IsValidObject &&
            app.ActiveUIDocument?.Document != null && _document.Equals(app.ActiveUIDocument.Document);
        private void CheckDocument(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs args)
        {
            if (_released || _busy) return;
            if (CloseRequested || Window == null || Window.IsDisposed || !IsCurrentDocument(_app))
            { ReleaseInApiContext(); return; }
            try
            {
                string viewId = _document.ActiveView?.UniqueId;
                if (_viewId != viewId) { _viewId = viewId; MarkRefreshPending(); }
                if (!_refreshPending) return;
                if (!_staleShown) { Window.ShowRefreshState("模型已變更，等待重新檢查"); _staleShown = true; }
                double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - _lastChangeTick) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (_pendingAction != PipeSleeveManagerAction.None || _document.IsModifiable || elapsed < 0.75) return;
                _busy = true;
                if (!LicenseManager.Instance.HasFeatureAccess("MEP.PipeSleeve"))
                    throw new InvalidOperationException("目前授權不包含套管管理功能。");
                Window.ReloadRowsInternal();
            }
            catch (Exception ex)
            {
                // Keep the old snapshot and wait for an explicit retry or another model change.
                _refreshPending = false;
                Window.ShowRefreshState("自動檢查失敗，請按重新檢查。" + ex.Message);
            }
            finally { _busy = false; }
        }
        internal void ReleaseInApiContext()
        {
            if (_released) return;
            CloseRequested = true;
            _pendingAction = PipeSleeveManagerAction.None;
            _app.Idling -= CheckDocument;
            _app.Application.DocumentChanged -= ModelChanged;
            if (Window != null && !Window.IsDisposed) Window.Close();
            _event.Dispose();
            _released = true;
        }

        public bool Request(PipeSleeveManagerAction action)
        {
            if (_released || CloseRequested || _busy || _pendingAction != PipeSleeveManagerAction.None) return false;
            _pendingAction = action;
            return true;
        }
        public void CancelRequest() { _pendingAction = PipeSleeveManagerAction.None; }

        public void Execute(UIApplication app)
        {
            if (_released) return;
            if (CloseRequested || !IsCurrentDocument(app)) { ReleaseInApiContext(); return; }
            PipeSleeveManagerAction action = _pendingAction;
            _pendingAction = PipeSleeveManagerAction.None;

            PipeSleeveManagerForm window = Window;
            if (window == null || window.IsDisposed || action == PipeSleeveManagerAction.None) return;

            try
            {
                _busy = true;
                if (!LicenseManager.Instance.HasFeatureAccess("MEP.PipeSleeve"))
                    throw new InvalidOperationException("目前授權不包含套管管理功能。");
                switch (action)
                {
                    case PipeSleeveManagerAction.Reload:
                        window.ReloadRowsInternal();
                        break;
                    case PipeSleeveManagerAction.Organize:
                        window.OrganizeSleevesInternal();
                        break;
                    case PipeSleeveManagerAction.Rebase:
                        window.RebaseSelectedInternal();
                        break;
                    case PipeSleeveManagerAction.Focus:
                        window.FocusSelectedInternal();
                        break;
                    case PipeSleeveManagerAction.Delete:
                        window.DeleteSelectedInternal();
                        break;
                    case PipeSleeveManagerAction.Update:
                        window.UpdateSelectedInternal();
                        break;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("套管管理", "執行管理動作失敗:\n" + ex.Message);
            }
            finally { _busy = false; }
        }

        public string GetName()
        {
            return "HB_BIM Tools Pipe Sleeve Manager";
        }
    }
    internal sealed class PipeSleeveManagerForm : WinForms.Form
    {
        private readonly UIApplication _uiapp;
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly PipeSleeveManagerRequestHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly WinForms.DataGridView _grid = new WinForms.DataGridView();
        private readonly WinForms.ComboBox _levelFilter = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _systemFilter = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _hostFilter = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _statusFilter = new WinForms.ComboBox();
        private readonly WinForms.TextBox _searchBox = new WinForms.TextBox();
        private readonly WinForms.CheckBox _actionOnly = new WinForms.CheckBox { Text = "僅顯示待處理", AutoSize = true };
        private readonly WinForms.CheckBox _actionFirst = new WinForms.CheckBox { Text = "待處理優先", AutoSize = true, Checked = true };
        private bool _loadingFilters;
        private readonly WinForms.Label _summary = new WinForms.Label();
        private readonly WinForms.Label _detail = new WinForms.Label();
        private readonly WinForms.Label _scopeNote = new WinForms.Label();
        private readonly WinForms.Label _syncState = new WinForms.Label();
        private readonly List<WinForms.Button> _selectionButtons = new List<WinForms.Button>();
        private List<PipeSleeveManagerRow> _allRows = new List<PipeSleeveManagerRow>();

        public PipeSleeveManagerForm(UIApplication uiapp, UIDocument uidoc, PipeSleeveManagerRequestHandler handler, ExternalEvent externalEvent)
        {
            _uiapp = uiapp;
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _handler = handler;
            _externalEvent = externalEvent;
            InitializeComponent();
            ReloadRowsInternal();
        }

        private void InitializeComponent()
        {
            Text = "HB_BIM｜套管管理";
            Width = 1120;
            Height = 720;
            MinimumSize = new Size(760, 560);
            AutoScaleMode = WinForms.AutoScaleMode.Dpi;
            StartPosition = WinForms.FormStartPosition.CenterParent;
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = System.Drawing.Color.White;

            var root = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new WinForms.Padding(16)
            };
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Percent, 100));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Absolute, 90));
            root.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent,100));
            Controls.Add(root);

            var title = new WinForms.Label
            {
                Text = "套管管理",
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
                AutoSize = true,
                Margin = new WinForms.Padding(0, 0, 0, 2)
            };
            var hint = new WinForms.Label
            {
                Text = "目前視圖範圍：管線來源套管與筏基 CAD 定位構件。",
                AutoSize = true,
                ForeColor = System.Drawing.Color.DimGray,
                Margin = new WinForms.Padding(0, 0, 0, 10)
            };
            var titleStack = new WinForms.FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = WinForms.FlowDirection.TopDown,
                WrapContents = false,
                Dock = WinForms.DockStyle.Fill
            };
            titleStack.Controls.Add(title);
            hint.Dispose();
            _scopeNote.AutoSize = true;
            _scopeNote.ForeColor = System.Drawing.Color.DimGray;
            _scopeNote.Margin = new WinForms.Padding(0, 0, 0, 8);
            titleStack.Controls.Add(_scopeNote);
            _syncState.AutoSize = true;
            _syncState.Margin = new WinForms.Padding(0,0,0,8);
            titleStack.Controls.Add(_syncState);
            root.Controls.Add(titleStack, 0, 0);

            var filters = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 8,
                AutoSize = true,
                Margin = new WinForms.Padding(0, 0, 0, 10)
            };
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 25));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 25));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 25));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 25));
            root.Controls.Add(filters, 0, 1);

            AddFilter(filters, "樓層", _levelFilter, 0);
            AddFilter(filters, "系統", _systemFilter, 2);
            AddFilter(filters, "穿越", _hostFilter, 4);
            AddFilter(filters, "狀態", _statusFilter, 6);
            filters.Controls.Add(new WinForms.Label { Text = "搜尋", AutoSize = true, Anchor = WinForms.AnchorStyles.Left, Margin = new WinForms.Padding(0, 8, 4, 4) }, 0, 1);
            _searchBox.Dock = WinForms.DockStyle.Fill;
            _searchBox.Margin = new WinForms.Padding(4, 8, 0, 2);
            _searchBox.BorderStyle = WinForms.BorderStyle.FixedSingle;
            _searchBox.TextChanged += (s, e) => ApplyFilters();
            filters.Controls.Add(_searchBox, 1, 1);
            filters.SetColumnSpan(_searchBox, 7);
            var quickFilters = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Top, AutoSize = true, WrapContents = true };
            quickFilters.Controls.Add(_actionOnly);
            quickFilters.Controls.Add(_actionFirst);
            _actionOnly.CheckedChanged += (s, e) => ApplyFilters();
            _actionFirst.CheckedChanged += (s, e) => ApplyFilters();
            filters.Controls.Add(quickFilters, 0, 2);
            filters.SetColumnSpan(quickFilters, 8);

            ConfigureGrid();
            var split = new WinForms.SplitContainer { Dock=WinForms.DockStyle.Fill, Orientation=WinForms.Orientation.Horizontal,
                Size=new Size(900,400), Panel1MinSize=60, Panel2MinSize=50, SplitterDistance=280 };
            split.Panel1.Controls.Add(_grid);
            root.Controls.Add(split, 0, 2);

            var bottom = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = false,
                Margin = new WinForms.Padding(0, 10, 0, 0)
            };
            bottom.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 100));
            bottom.RowCount = 2;
            bottom.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            bottom.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.Controls.Add(bottom, 0, 3);

            var infoStack = new WinForms.FlowLayoutPanel
            {
                AutoSize = false,
                AutoScroll = true,
                Dock = WinForms.DockStyle.Fill,
                FlowDirection = WinForms.FlowDirection.TopDown,
                WrapContents = false
            };
            _summary.AutoSize = true;
            _summary.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            _summary.Margin = new WinForms.Padding(0, 0, 0, 4);
            _detail.AutoSize = true;
            _detail.MaximumSize = new Size(680, 0);
            _detail.ForeColor = System.Drawing.Color.DimGray;
            infoStack.Controls.Add(_detail);
            split.Panel2.Controls.Add(infoStack);
            var summaryBar = new WinForms.FlowLayoutPanel { AutoSize=true, Dock=WinForms.DockStyle.Fill };
            var detailsToggle = new WinForms.CheckBox { Text="顯示明細", Checked=true, AutoSize=true };
            detailsToggle.CheckedChanged += (s,e) => split.Panel2Collapsed=!detailsToggle.Checked;
            summaryBar.Controls.Add(_summary); summaryBar.Controls.Add(detailsToggle);
            bottom.Controls.Add(summaryBar, 0, 0);

            var buttons = new WinForms.FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = WinForms.FlowDirection.RightToLeft,
                WrapContents = true,
                Dock = WinForms.DockStyle.Fill,
                Padding = new WinForms.Padding(0)
            };
            var actionBar = new WinForms.TableLayoutPanel { Dock=WinForms.DockStyle.Fill, AutoSize=true, AutoSizeMode=WinForms.AutoSizeMode.GrowAndShrink, ColumnCount=2 };
            actionBar.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent,55));
            actionBar.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent,45));
            var tools = new WinForms.FlowLayoutPanel { Dock=WinForms.DockStyle.Fill, AutoSize=true, WrapContents=true };
            actionBar.Controls.Add(tools,0,0); actionBar.Controls.Add(buttons,1,0);
            bottom.Controls.Add(actionBar, 0, 1);

            buttons.Controls.Add(MakeButton("關閉", 92, (s, e) => Close()));
            _selectionButtons.Add(MakeButton("更新", 92, (s, e) => Raise(PipeSleeveManagerAction.Update), true));
            _selectionButtons.Add(MakeButton("定位", 92, (s, e) => Raise(PipeSleeveManagerAction.Focus)));
            foreach (var button in _selectionButtons)
            {
                button.Enabled = false;
                if (button.Text == "定位") tools.Controls.Add(button);
                else buttons.Controls.Add(button);
                if (button.Text == "刪除") button.FlatAppearance.BorderSize = 0;
            }
            tools.Controls.Add(MakeButton("重新檢查", 110, (s, e) => Raise(PipeSleeveManagerAction.Reload)));
            var moreMenu = new WinForms.ContextMenuStrip();
            var rebaseItem = moreMenu.Items.Add("變更約束樓層", null, (s,e) => Raise(PipeSleeveManagerAction.Rebase));
            moreMenu.Items.Add("整理編號", null, (s, e) => {
                if (WinForms.MessageBox.Show(this, "重新整理目前視圖的套管編號？此操作會寫入模型。", "整理編號",
                    WinForms.MessageBoxButtons.OKCancel, WinForms.MessageBoxIcon.Question,
                    WinForms.MessageBoxDefaultButton.Button2) == WinForms.DialogResult.OK)
                    Raise(PipeSleeveManagerAction.Organize);
            });
            moreMenu.Items.Add(new WinForms.ToolStripSeparator());
            var deleteItem = moreMenu.Items.Add("刪除選取套管", null, (s,e) => Raise(PipeSleeveManagerAction.Delete));
            moreMenu.Opening += (s,e) => {
                bool selected = GetSelectedRows().Count > 0;
                rebaseItem.Enabled = selected; deleteItem.Enabled = selected;
            };
            var moreButton = MakeButton("更多 ▾", 92, (s,e) => {
                var button = (WinForms.Button)s;
                moreMenu.Show(button, new System.Drawing.Point(0, button.Height));
            });
            tools.Controls.Add(moreButton);
            Disposed += (s,e) => moreMenu.Dispose();
            root.SizeChanged += (s, e) => {
                root.RowStyles[3].Height = (root.ClientSize.Width < 900 * DeviceDpi / 96.0 ? 130 : 90) * DeviceDpi / 96f;
                _scopeNote.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - 40), 0);
                _syncState.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - 40), 0);
                _detail.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - 40), 0);
            };
            var area = WinForms.Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
        }
        private static void AddFilter(WinForms.TableLayoutPanel panel, string label, WinForms.ComboBox combo, int column)
        {
            panel.Controls.Add(new WinForms.Label
            {
                Text = label,
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left,
                Margin = new WinForms.Padding(column == 0 ? 0 : 10, 4, 4, 4)
            }, column, 0);
            combo.Dock = WinForms.DockStyle.Fill;
            combo.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
            combo.Margin = new WinForms.Padding(4, 2, 0, 2);
            combo.SelectedIndexChanged += (s, e) =>
            {
                WinForms.Form form = combo.FindForm();
                if (form is PipeSleeveManagerForm manager) manager.ApplyFilters();
            };
            panel.Controls.Add(combo, column + 1, 0);
        }

        private static WinForms.Button MakeButton(string text, int width, EventHandler handler, bool primary = false)
        {
            var button = new WinForms.Button
            {
                Text = text,
                Width = width,
                Height = 38,
                Margin = new WinForms.Padding(8, 0, 0, 0),
                FlatStyle = WinForms.FlatStyle.Flat,
                BackColor = primary ? System.Drawing.Color.FromArgb(0, 105, 180) : System.Drawing.Color.White,
                ForeColor = primary ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(40, 48, 54)
            };
            button.FlatAppearance.BorderColor = primary ? System.Drawing.Color.FromArgb(0, 105, 180) : System.Drawing.Color.FromArgb(200, 205, 210);
            if (primary)
            {
                button.Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
            }
            button.Click += handler;
            return button;
        }

        private void ConfigureGrid()
        {
            _grid.Dock = WinForms.DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.ReadOnly = true;
            _grid.MultiSelect = true;
            _grid.SelectionMode = WinForms.DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoGenerateColumns = false;
            _grid.RowHeadersVisible = false;
            _grid.BorderStyle = WinForms.BorderStyle.None;
            _grid.CellBorderStyle = WinForms.DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.ColumnHeadersBorderStyle = WinForms.DataGridViewHeaderBorderStyle.Single;
            _grid.BackgroundColor = System.Drawing.Color.White;
            _grid.GridColor = System.Drawing.Color.Gainsboro;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersHeightSizeMode = WinForms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.ScrollBars = WinForms.ScrollBars.Both;
            _grid.RowTemplate.Height = 31;
            _grid.AutoSizeRowsMode = WinForms.DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
            _grid.DefaultCellStyle.WrapMode = WinForms.DataGridViewTriState.False;
            _grid.DefaultCellStyle.Padding = new WinForms.Padding(4, 3, 4, 3);
            _grid.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(218, 239, 238);
            _grid.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.FromArgb(25, 49, 51);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(248, 249, 250);
            _grid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(240, 242, 244);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.Black;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new WinForms.Padding(4, 4, 4, 4);
            _grid.DoubleClick += (s, e) => Raise(PipeSleeveManagerAction.Focus);
            _grid.SelectionChanged += (s, e) => UpdateDetail();
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;

            _grid.Columns.Add(MakeColumn("Mark", "編號", 88));
            _grid.Columns.Add(MakeColumn("Level", "樓層", 95));
            _grid.Columns.Add(MakeColumn("SystemName", "系統", 115));
            _grid.Columns.Add(MakeColumn("HostType", "穿越", 70));
            _grid.Columns.Add(MakeColumn("NominalDiameter", "DN", 65));
            _grid.Columns.Add(MakeColumn("Status", "狀態", 85));
            _grid.Columns.Add(MakeColumn("CheckInfo", "主要問題", 360, true, true));
        }
        private static bool ContainsAny(string text, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(text) || terms == null) return false;
            return terms.Any(term => !string.IsNullOrWhiteSpace(term) && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        private void Grid_CellFormatting(object sender, WinForms.DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string propertyName = _grid.Columns[e.ColumnIndex].DataPropertyName;
            if (propertyName == "Status")
            {
                string status = Convert.ToString(e.Value);
                if (status == "正常" || status == "偏心可容納")
                {
                    e.CellStyle.ForeColor = System.Drawing.Color.SeaGreen;
                }
                else if (status == "需更新" || status == "提醒" || status == "待確認")
                {
                    e.CellStyle.ForeColor = System.Drawing.Color.DarkOrange;
                    e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                }
                else if (status == "來源遺失" || status == "需檢查" || status == "需結構確認" || status == "需處理")
                {
                    e.CellStyle.ForeColor = System.Drawing.Color.Firebrick;
                    e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                }
            }
            else if (propertyName == "CheckInfo")
            {
                string checkInfo = Convert.ToString(e.Value) ?? string.Empty;
                if (ContainsAny(checkInfo, "高風險", "需結構確認", "孔距", "梁深 1/3"))
                {
                    e.CellStyle.ForeColor = System.Drawing.Color.Firebrick;
                    e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                }
                else if (ContainsAny(checkInfo, "提醒"))
                {
                    e.CellStyle.ForeColor = System.Drawing.Color.DarkOrange;
                }
                else
                {
                    e.CellStyle.ForeColor = System.Drawing.Color.FromArgb(80, 80, 80);
                }
            }
        }

        private void Grid_CellToolTipTextNeeded(object sender, WinForms.DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            object value = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            string text = Convert.ToString(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                e.ToolTipText = text;
            }
        }

        private void UpdateDetail()
        {
            List<PipeSleeveManagerRow> selected = GetSelectedRows();
            foreach (var button in _selectionButtons) button.Enabled = selected.Count > 0;
            if (selected.Count == 0)
            {
                _detail.Text = _grid.Rows.Count == 0
                    ? "目前範圍或篩選條件下沒有套管；請調整視圖或篩選條件後重新整理。"
                    : "未選取套管";
                return;
            }

            PipeSleeveManagerRow row = selected[0];
            string multi = selected.Count > 1 ? $"，已選 {selected.Count} 筆" : string.Empty;
            _detail.Text = $"{row.Mark}｜{row.Status}{multi}\n{row.CheckDetails}\n" +
                $"長度 {Blank(row.LengthMm)} mm｜立面高程 {Blank(row.ElevationMm)} mm｜{row.Level}\n" +
                $"族型：{row.TypeName}\n元素 ID：{row.ElementIdValue}｜來源 ID：{row.SourcePipeIdValue}";
        }

        private static string Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
        private void Raise(PipeSleeveManagerAction action)
        {
            if (!_handler.Request(action)) return;
            try
            {
                var status = _externalEvent.Raise();
                if (status != ExternalEventRequest.Accepted && status != ExternalEventRequest.Pending)
                    throw new InvalidOperationException("Revit 尚無法接受操作，請結束目前指令後重試。");
            }
            catch (Exception ex)
            {
                _handler.CancelRequest();
                WinForms.MessageBox.Show(this, ex.Message, "套管管理");
            }
        }

        private static WinForms.DataGridViewTextBoxColumn MakeColumn(string property, string header, int width, bool fill = false, bool wrap = false)
        {
            var column = new WinForms.DataGridViewTextBoxColumn
            {
                DataPropertyName = property,
                HeaderText = header,
                Width = width,
                MinimumWidth = fill ? 140 : 60,
                AutoSizeMode = fill ? WinForms.DataGridViewAutoSizeColumnMode.Fill : WinForms.DataGridViewAutoSizeColumnMode.None,
                SortMode = WinForms.DataGridViewColumnSortMode.Automatic
            };
            if (wrap || !fill)
            {
                column.DefaultCellStyle.WrapMode = WinForms.DataGridViewTriState.True;
            }
            return column;
        }

        internal void ShowRefreshState(string message)
        {
            _syncState.Text = message;
            _syncState.ForeColor = message.Contains("失敗") ? System.Drawing.Color.Firebrick : System.Drawing.Color.DarkOrange;
        }

        internal void ReloadRowsInternal()
        {
            _loadingFilters = true;
            try {
            _allRows = CollectSleeves(_doc, _doc.ActiveView);
            _scopeNote.Text = BuildScopeNote(_doc.ActiveView, _allRows.Count);
            LoadFilter(_levelFilter, _allRows.Select(r => r.Level));
            LoadFilter(_systemFilter, _allRows.Select(r => r.SystemName));
            LoadFilter(_hostFilter, _allRows.Select(r => r.HostType));
            LoadFilter(_statusFilter, _allRows.Select(r => r.Status));
            } finally { _loadingFilters = false; }
            ApplyFilters();
            _handler.RowsRefreshed();
            _syncState.Text = "已檢查　" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "｜自動檢查開啟";
            _syncState.ForeColor = System.Drawing.Color.SeaGreen;
        }

        private static void LoadFilter(WinForms.ComboBox combo, IEnumerable<string> values)
        {
            object selected = combo.SelectedItem;
            combo.Items.Clear();
            combo.Items.Add("全部");
            foreach (string value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v))
            {
                combo.Items.Add(value);
            }

            combo.SelectedItem = selected != null && combo.Items.Contains(selected) ? selected : "全部";
        }

        private void ApplyFilters()
        {
            if (_loadingFilters) return;
            int scrollRow = _grid.FirstDisplayedScrollingRowIndex;
            var selectedIds = new HashSet<long>(GetSelectedRows().Select(r => r.ElementIdValue));
            string level = SelectedFilter(_levelFilter);
            string system = SelectedFilter(_systemFilter);
            string host = SelectedFilter(_hostFilter);
            string status = SelectedFilter(_statusFilter);
            string search = _searchBox.Text?.Trim() ?? string.Empty;

            IEnumerable<PipeSleeveManagerRow> rows = _allRows;
            if (_actionOnly.Checked) rows = rows.Where(NeedsAttention);
            if (!string.IsNullOrEmpty(level)) rows = rows.Where(r => r.Level == level);
            if (!string.IsNullOrEmpty(system)) rows = rows.Where(r => r.SystemName == system);
            if (!string.IsNullOrEmpty(host)) rows = rows.Where(r => r.HostType == host);
            if (!string.IsNullOrEmpty(status)) rows = rows.Where(r => r.Status == status);
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(r => r.SearchText.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

            if (_actionFirst.Checked) rows = rows.OrderByDescending(NeedsAttention);
            List<PipeSleeveManagerRow> visible = rows.ToList();
            _grid.DataSource = visible;
            _grid.ClearSelection();
            foreach (WinForms.DataGridViewRow gridRow in _grid.Rows)
                if (gridRow.DataBoundItem is PipeSleeveManagerRow item && selectedIds.Contains(item.ElementIdValue)) gridRow.Selected = true;
            if (scrollRow >= 0 && _grid.Rows.Count > 0)
                _grid.FirstDisplayedScrollingRowIndex = Math.Min(scrollRow, _grid.Rows.Count - 1);
            int needAction = visible.Count(NeedsAttention);
            _summary.Text = $"顯示 {visible.Count} / {_allRows.Count} 個｜待處理 {needAction} 個（全部 {_allRows.Count(NeedsAttention)} 個）";
            UpdateDetail();
        }

        private static bool NeedsAttention(PipeSleeveManagerRow row)
        {
            return row.Status != "正常" && row.Status != "偏心可容納";
        }

        private static string SelectedFilter(WinForms.ComboBox combo)
        {
            string value = combo.SelectedItem as string;
            return string.IsNullOrWhiteSpace(value) || value == "全部" ? string.Empty : value;
        }

        private List<PipeSleeveManagerRow> GetSelectedRows()
        {
            return _grid.SelectedRows
                .Cast<WinForms.DataGridViewRow>()
                .Select(row => row.DataBoundItem as PipeSleeveManagerRow)
                .Where(row => row != null)
                .Distinct()
                .ToList();
        }

        internal void FocusSelectedInternal()
        {
            List<ElementId> ids = GetSelectedRows().Select(r => RevitApiCompatibility.CreateElementId(r.ElementIdValue)).ToList();
            if (ids.Count == 0)
            {
                WinForms.MessageBox.Show(this, "請先選取套管。", "套管管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            _uidoc.Selection.SetElementIds(ids);
            _uidoc.ShowElements(ids);
            _uidoc.RefreshActiveView();
        }

        internal void DeleteSelectedInternal()
        {
            List<ElementId> ids = GetSelectedRows().Select(r => RevitApiCompatibility.CreateElementId(r.ElementIdValue)).ToList();
            if (ids.Count == 0)
            {
                WinForms.MessageBox.Show(this, "請先選取要刪除的套管。", "套管管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            WinForms.DialogResult confirm = WinForms.MessageBox.Show(this, $"確定刪除選取的 {ids.Count} 個套管？", "刪除套管", WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning);
            if (confirm != WinForms.DialogResult.Yes) return;

            using (Transaction tx = new Transaction(_doc, "刪除套管"))
            {
                tx.Start();
                _doc.Delete(ids);
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit 未提交刪除，請重新檢查模型。");
            }

            ReloadRowsInternal();
        }

        internal void UpdateSelectedInternal()
        {
            List<PipeSleeveManagerRow> selected = GetSelectedRows();
            if (selected.Count == 0)
            {
                WinForms.MessageBox.Show(this, "請先選取要更新的套管。", "套管管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            if (selected.Any(row => RaftCadRecord.IsManaged(_doc.GetElement(RevitApiCompatibility.CreateElementId(row.ElementIdValue)))))
            {
                WinForms.MessageBox.Show(this, "CAD 定位構件不使用管線更新。請先取消選取 CAD 定位列；其位置與高程需人工確認調整。", "套管管理");
                return;
            }
            List<long> sourceIds = selected
                .Select(row => row.SourcePipeIdValue)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            List<Element> pipes = sourceIds
                .Select(id => _doc.GetElement(RevitApiCompatibility.CreateElementId(id)))
                .Where(element => element is Pipe || element is Duct || IsElementOfCategory(element, BuiltInCategory.OST_Conduit) || IsElementOfCategory(element, BuiltInCategory.OST_CableTray))
                .ToList();

            if (pipes.Count == 0)
            {
                WinForms.MessageBox.Show(this, "選取套管找不到有效來源管線，無法更新。", "套管管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            List<ElementId> staleSleeveIds = selected
                .Select(row => RevitApiCompatibility.CreateElementId(row.ElementIdValue))
                .Where(id => _doc.GetElement(id) != null)
                .Distinct(new ElementIdComparer())
                .ToList();

            SleeveAnnotationGuard annotationGuard;
            try
            {
                annotationGuard = new SleeveAnnotationGuard(_doc, staleSleeveIds);
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(this, "無法完成既有標註檢查，已停止更新，模型未變更。\n" + ex.Message,
                    "套管更新檢查", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            PipeSleeveOptions updateOptions = BuildUpdateOptions();
            var previewRows = PipeSleeveService.PreviewSelectedUpdate(_doc, pipes,
                staleSleeveIds.Select(id => _doc.GetElement(id)).Cast<FamilyInstance>().ToList(), updateOptions);
            foreach (var previewRow in previewRows)
            {
                if (long.TryParse(previewRow.Id, out long id) &&
                    _doc.GetElement(RevitApiCompatibility.CreateElementId(id)) is FamilyInstance instance)
                {
                    var inspection = PipeSleeveManagerRow.From(_doc, instance);
                    previewRow.CurrentCheckStatus = inspection.Status;
                    previewRow.CurrentCheckSummary = inspection.CheckInfo;
                    previewRow.CurrentCheckDetails = inspection.CheckDetails;
                }
            }
            using (var preview = new SleeveUpdatePreviewForm(previewRows))
                if (preview.ShowDialog(this) != WinForms.DialogResult.OK) return;
            PipeSleeveResult result;
            try
            {
            using (TransactionGroup group = new TransactionGroup(_doc, "保留套管與標註更新"))
            {
            group.Start();
            using (Transaction tx = new Transaction(_doc, "更新選取套管"))
            {
                tx.Start();
                result = PipeSleeveService.UpdateSelectedInPlace(_doc, pipes,
                    staleSleeveIds.Select(id => _doc.GetElement(id)).Cast<FamilyInstance>().ToList(), updateOptions);
                // Replacement must be atomic: unresolved mappings must not remove old sleeves.
                if (result.FailedCount > 0 || result.CandidateCount == 0 ||
                    result.CreatedCount + result.UpdatedCount != result.CandidateCount)
                {
                    tx.RollBack();
                    WinForms.MessageBox.Show(this, "更新未完成，已還原原有套管。請確認建立套管的尺寸對應與模型範圍設定。\n\n以下為未提交的處理結果：\n" + result.ToTaskDialogText(), "套管更新", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                    return;
                }
                if (tx.Commit() != TransactionStatus.Committed)
                {
                    WinForms.MessageBox.Show(this, "Revit 未成功提交套管更新，請檢查模型警告。", "套管更新", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                    return;
                }
            }

            annotationGuard.Verify();
            if (group.Assimilate() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit 未完成更新交易群組。");
            }
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(this, "更新未完成，已回復本次變更。\n" + ex.Message,
                    "套管更新", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                ReloadRowsInternal();
                return;
            }
            ReloadRowsInternal();
            WinForms.MessageBox.Show(this, "已保留原套管更新，既有標註參考檢查通過。\n" + result.ToTaskDialogText(), "套管更新", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
        }

        private PipeSleeveOptions BuildUpdateOptions()
        {
            PipeSleeveSettings saved = PipeSleeveSettingsStore.Load();
            var symbols = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => IsElementOfCategory(s, BuiltInCategory.OST_GenericModel) ||
                    IsElementOfCategory(s, BuiltInCategory.OST_PipeAccessory))
                .ToList();
            ElementId Resolve(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
                var matches = symbols.Where(s => string.Equals(s.FamilyName + ": " + s.Name,
                    name, StringComparison.OrdinalIgnoreCase)).ToList();
                return matches.Count == 1 ? matches[0].Id : ElementId.InvalidElementId;
            }

            var map = new Dictionary<int, ElementId>();
            foreach (PipeSleeveSizeSetting entry in saved.SizeMappings ?? new List<PipeSleeveSizeSetting>())
            {
                ElementId id = Resolve(entry.SleeveDisplayName);
                map[entry.NominalDiameterMm] = id;
            }
            return new PipeSleeveOptions
            {
                PreserveNominalTypeDimensions = true,
                ClearanceMm = double.IsNaN(saved.ClearanceMm) || double.IsInfinity(saved.ClearanceMm)
                    ? 50.0 : Math.Max(0, Math.Min(1000, saved.ClearanceMm)),
                IncludeCurrentModel = saved.IncludeCurrentModel,
                IncludeLinks = saved.IncludeLinks,
                ExcludeAdditionElements = saved.ExcludeAdditionElements,
                UseDiameterSymbolMap = saved.UseDiameterMap,
                SleeveSymbolByDiameterMm = map,
                DefaultWallSleeveSymbolId = Resolve(saved.DefaultWallSleeveDisplayName),
                DefaultFloorSleeveSymbolId = Resolve(saved.DefaultFloorSleeveDisplayName),
                AutoNumber = false,
                SkipExisting = false,
                LimitToActiveView = true,
                ActiveViewId = _doc.ActiveView != null ? _doc.ActiveView.Id : ElementId.InvalidElementId
            };
        }

        internal void OrganizeSleevesInternal()
        {
            if (_allRows.Count == 0)
            {
                ReloadRowsInternal();
                return;
            }

            using (Transaction tx = new Transaction(_doc, "整理套管編號與樓層"))
            {
                tx.Start();
                PipeSleeveService.OrganizeSleeveNumbers(_doc, new PipeSleeveOptions
                {
                    LimitToActiveView = true,
                    ActiveViewId = _doc.ActiveView != null ? _doc.ActiveView.Id : ElementId.InvalidElementId
                });
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit 未提交編號整理，請重新檢查模型。");
            }

            ReloadRowsInternal();
        }

        private static bool IsElementOfCategory(Element element, BuiltInCategory category)
        {
            return element?.Category != null && element.Category.Id.GetIdValue() == (long)category;
        }

        private sealed class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.GetIdValue() == y.GetIdValue();
            }

            public int GetHashCode(ElementId obj)
            {
                return obj == null ? 0 : obj.GetIdValue().GetHashCode();
            }
        }

        private static List<PipeSleeveManagerRow> CollectSleeves(Document doc, View activeView)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfType<FamilyInstance>()
                .Where(IsSleeveInstance)
                .Where(fi => IsSleeveInActiveViewScope(activeView, fi))
                .Select(fi => PipeSleeveManagerRow.From(doc, fi))
                .OrderBy(r => r.Level)
                .ThenBy(r => r.SystemName)
                .ThenBy(r => r.HostType)
                .ThenBy(r => r.Mark)
                .ToList();
        }


        private static string BuildScopeNote(View activeView, int visibleCount)
        {
            string scope = "目前視圖";
            View3D view3D = activeView as View3D;
            if (view3D != null && view3D.IsSectionBoxActive)
            {
                scope = "目前 3D 視圖範圍框";
            }
            else if (activeView != null && activeView.CropBoxActive)
            {
                scope = "目前視圖裁剪範圍";
            }

            return "清單範圍：" + scope + "，目前列出 " + visibleCount.ToString(CultureInfo.InvariantCulture) + " 個。";
        }

        private static bool IsSleeveInActiveViewScope(View activeView, FamilyInstance sleeve)
        {
            if (activeView == null || sleeve == null) return true;

            LocationPoint location = sleeve.Location as LocationPoint;
            XYZ point = location?.Point;
            if (point == null)
            {
                BoundingBoxXYZ box = sleeve.get_BoundingBox(null);
                if (box == null) return true;
                point = (box.Min + box.Max) * 0.5;
            }

            View3D view3D = activeView as View3D;
            if (view3D != null && view3D.IsSectionBoxActive)
            {
                return IsPointInsideBoundingBox(point, view3D.GetSectionBox(), 1.0 / 304.8);
            }

            if (activeView.CropBoxActive)
            {
                return IsPointInsideBoundingBox(point, activeView.CropBox, 1.0 / 304.8);
            }

            return true;
        }

        private static bool IsPointInsideBoundingBox(XYZ point, BoundingBoxXYZ box, double tolerance)
        {
            if (point == null || box == null) return true;
            Transform transform = box.Transform ?? Transform.Identity;
            XYZ localPoint = transform.Inverse.OfPoint(point);
            return localPoint.X >= box.Min.X - tolerance && localPoint.X <= box.Max.X + tolerance
                && localPoint.Y >= box.Min.Y - tolerance && localPoint.Y <= box.Max.Y + tolerance
                && localPoint.Z >= box.Min.Z - tolerance && localPoint.Z <= box.Max.Z + tolerance;
        }
        private static bool IsSleeveInstance(FamilyInstance instance)
        {
            if (instance != null && RaftCadRecord.IsManaged(instance)) return true;
            if (instance?.Category == null) return false;
            long categoryId = instance.Category.Id.GetIdValue();
            bool categoryMatches = categoryId == (long)BuiltInCategory.OST_GenericModel || categoryId == (long)BuiltInCategory.OST_PipeAccessory;
            if (!categoryMatches) return false;

            string source = ReadString(instance, "來源管線Id", "Pipe Id", "Source Pipe Id");
            if (!long.TryParse(source, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourceId) || sourceId <= 0)
            {
                return false;
            }

            string text = ((instance.Symbol?.FamilyName ?? string.Empty) + " " + (instance.Symbol?.Name ?? string.Empty)).ToLowerInvariant();
            return text.Contains("套管")
                || text.Contains("開孔")
                || text.Contains("sleeve")
                || text.Contains("opening");
        }

        private static string ReadString(Element element, BuiltInParameter builtIn, params string[] names)
        {
            Parameter parameter = element?.get_Parameter(builtIn);
            if (parameter != null && parameter.StorageType == StorageType.String)
            {
                string value = parameter.AsString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return ReadString(element, names);
        }

        private static string ReadString(Element element, params string[] names)
        {
            foreach (string name in names ?? new string[0])
            {
                Parameter parameter = element?.LookupParameter(name);
                if (parameter != null && parameter.StorageType == StorageType.String)
                {
                    string value = parameter.AsString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }

            return string.Empty;
        }

        private static string GetSleeveLevelName(Document doc, FamilyInstance sleeve)
        {
            var actual = SleeveGlLevel.Actual(doc, sleeve);
            return actual?.Name ?? "約束樓層未確認";
        }

        internal void RebaseSelectedInternal()
        {
            var selected = GetSelectedRows().Select(r => _doc.GetElement(RevitApiCompatibility.CreateElementId(r.ElementIdValue))).ToList();
            if (selected.Count == 0) return;
            if (selected.Any(e => e == null))
            {
                ReloadRowsInternal();
                WinForms.MessageBox.Show(this, "選取套管已變更或刪除，請重新選取。", "變更約束樓層");
                return;
            }
            CmdSleeveGlRebase.ExecuteSelected(_uidoc, selected);
            ReloadRowsInternal();
        }

        private static string GetLegacySleeveLevelName(Document doc, FamilyInstance sleeve)
        {
            string value = ReadString(sleeve, "套管樓層", "樓層名稱", "Level Name", "Reference Level Name", "參考樓層名稱", "所屬樓層名稱", "樓層", "Level");
            if (!string.IsNullOrWhiteSpace(value)) return value;

            Level level = GetParameterLevel(doc, sleeve,
                BuiltInParameter.FAMILY_LEVEL_PARAM,
                BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
                BuiltInParameter.SCHEDULE_LEVEL_PARAM);
            if (level != null) return level.Name;

            if (sleeve?.LevelId != null && sleeve.LevelId.GetIdValue() > 0)
            {
                level = doc.GetElement(sleeve.LevelId) as Level;
                if (level != null) return level.Name;
            }

            LocationPoint location = sleeve?.Location as LocationPoint;
            if (location != null)
            {
                level = GetBaseLevelAtOrBelow(doc, location.Point) ?? GetNearestLevel(doc, location.Point);
                if (level != null) return level.Name;
            }

            return string.Empty;
        }

        private static Level GetParameterLevel(Document doc, Element element, params BuiltInParameter[] builtIns)
        {
            foreach (BuiltInParameter builtIn in builtIns ?? new BuiltInParameter[0])
            {
                try
                {
                    Parameter parameter = element?.get_Parameter(builtIn);
                    if (parameter != null && parameter.StorageType == StorageType.ElementId && parameter.HasValue)
                    {
                        ElementId id = parameter.AsElementId();
                        if (id != null && id.GetIdValue() > 0)
                        {
                            Level level = doc.GetElement(id) as Level;
                            if (level != null) return level;
                        }
                    }
                }
                catch
                {
                    // Some built-in level parameters are not available on every family/category.
                }
            }

            return null;
        }

        private static Level GetBaseLevelAtOrBelow(Document doc, XYZ point)
        {
            const double tolerance = 1.0 / 304.8;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .OfType<Level>()
                .Where(level => level.Elevation <= point.Z + tolerance)
                .OrderByDescending(level => level.Elevation)
                .FirstOrDefault();
        }

        private static Level GetNearestLevel(Document doc, XYZ point)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .OfType<Level>()
                .OrderBy(level => Math.Abs(level.Elevation - point.Z))
                .FirstOrDefault();
        }

        private static void SetString(Element element, string value, BuiltInParameter builtIn, params string[] names)
        {
            Parameter parameter = element?.get_Parameter(builtIn);
            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
            {
                parameter.Set(value ?? string.Empty);
                return;
            }

            SetString(element, value, names);
        }

        private static void SetString(Element element, string value, params string[] names)
        {
            foreach (string name in names ?? new string[0])
            {
                Parameter parameter = element?.LookupParameter(name);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value ?? string.Empty);
                    return;
                }
            }
        }
        private static double ReadDoubleMm(Element element, params string[] names)
        {
            foreach (string name in names ?? new string[0])
            {
                Parameter parameter = element?.LookupParameter(name);
                if (parameter != null && parameter.StorageType == StorageType.Double && parameter.HasValue)
                {
                    return parameter.AsDouble() * 304.8;
                }
            }

            return 0;
        }

        private static long ReadLong(Element element, params string[] names)
        {
            string value = ReadString(element, names);
            long id;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? id : -1;
        }

        private sealed class PipeSleeveManagerRow
        {
            public long ElementIdValue { get; set; }
            public long SourcePipeIdValue { get; set; }
            public string ElementId => ElementIdValue.ToString(CultureInfo.InvariantCulture);
            public string Mark { get; set; }
            public string Level { get; set; }
            public string SystemName { get; set; }
            public string HostType { get; set; }
            public string NominalDiameter { get; set; }
            public string LengthMm { get; set; }
            public string ElevationMm { get; set; }
            public string Status { get; set; }
            public string CheckInfo { get; set; }
            public string CheckDetails { get; set; }
            public string TypeName { get; set; }
            public string SearchText => string.Join(" ", ElementId, Mark, Level, SystemName, HostType, NominalDiameter, Status, CheckInfo, CheckDetails, TypeName);

            public static PipeSleeveManagerRow From(Document doc, FamilyInstance sleeve)
            {
                string sourceIdText = ReadString(sleeve, "來源管線Id", "Pipe Id", "Source Pipe Id");
                long sourceId = ReadLong(sleeve, "來源管線Id", "Pipe Id", "Source Pipe Id");
                Element source = sourceId > 0 ? doc.GetElement(RevitApiCompatibility.CreateElementId(sourceId)) : null;
                string status = GetStatus(sleeve, source, out string positionInfo, out string brief);
                if (RaftCadRecord.IsManaged(sleeve))
                {
                    status = RaftCadRecord.Inspect(sleeve, out positionInfo);
                    brief = "CAD 定位：" + status;
                }
                string checkInfo = ReadString(sleeve, "套管檢核資訊", "Sleeve Check", "Check Info", "穿梁檢核", "Beam Opening Check", "結構檢核", "Structural Check");
                if (status == "正常" || status == "偏心可容納")
                {
                    string risk = GetBeamRiskStatus(checkInfo);
                    if (risk != "正常") status = risk;
                }
                if (status == "來源遺失" && !RaftCadRecord.IsManaged(sleeve))
                {
                    checkInfo = GetSourceMissingReason(sourceIdText, sourceId);
                }
                if (!string.IsNullOrWhiteSpace(positionInfo))
                    checkInfo = positionInfo + (string.IsNullOrWhiteSpace(checkInfo) ? string.Empty : "；" + checkInfo);
                if (!RaftCadRecord.IsManaged(sleeve))
                {
                    var actual = SleeveGlLevel.Actual(doc, sleeve);
                    if (actual == null)
                    {
                        string reason = "無有效約束樓層，請使用更多 → 變更約束樓層";
                        brief = reason;
                        checkInfo = reason + "；" + checkInfo;
                        if (status == "正常" || status == "偏心可容納" || status == "待確認") status = "待確認";
                    }
                }

                double length = ReadDoubleMm(sleeve, "套管長度", "長度", "Length", "Sleeve Length", "深度", "Depth");
                double elevation = ReadDoubleMm(sleeve, "立面高程", "Elevation");

                return new PipeSleeveManagerRow
                {
                    ElementIdValue = sleeve.Id.GetIdValue(),
                    SourcePipeIdValue = sourceId,
                    Mark = ReadString(sleeve, BuiltInParameter.ALL_MODEL_MARK, "套管編號", "編號", "Sleeve Number"),
                    Level = GetSleeveLevelName(doc, sleeve),
                    SystemName = GetSleeveSystemName(sleeve),
                    HostType = ReadString(sleeve, "穿越構件", "Host Type"),
                    NominalDiameter = ReadString(sleeve, "管道標稱直徑", "Nominal Diameter"),
                    LengthMm = length > 0 ? length.ToString("0.#", CultureInfo.InvariantCulture) : string.Empty,
                    ElevationMm = Math.Abs(elevation) > 0.0001 ? elevation.ToString("0.#", CultureInfo.InvariantCulture) : string.Empty,
                    Status = status,
                    CheckInfo = status == "需結構確認" || status == "提醒" ? status :
                        !string.IsNullOrWhiteSpace(brief) ? brief : status,
                    CheckDetails = checkInfo,
                    TypeName = (sleeve.Symbol?.FamilyName ?? string.Empty) + ": " + (sleeve.Symbol?.Name ?? string.Empty)
                };
            }

            private static string GetSourceMissingReason(string sourceIdText, long sourceId)
            {
                if (string.IsNullOrWhiteSpace(sourceIdText)) return "未寫入來源管線Id，已排除於預設管理清單。";
                if (sourceId <= 0) return "來源管線Id 格式無法解析: " + sourceIdText;
                return "來源管線Id " + sourceId.ToString(CultureInfo.InvariantCulture) + " 在目前文件找不到，可能管線已刪除、重建，或套管由其他模型複製。";
            }

            private static string GetBeamRiskStatus(string checkInfo)
            {
                if (ContainsAny(checkInfo, "高風險", "需結構確認", "孔距", "梁深 1/3")) return "需結構確認";
                if (ContainsAny(checkInfo, "提醒")) return "提醒";
                return "正常";
            }

            private static bool ContainsAny(string text, params string[] terms)
            {
                if (string.IsNullOrWhiteSpace(text) || terms == null) return false;
                return terms.Any(term => !string.IsNullOrWhiteSpace(term) && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            private static string GetSleeveSystemName(FamilyInstance sleeve)
            {
                string value = ReadString(sleeve, "系統名稱", "System Name", "Source System Name");
                if (!string.IsNullOrWhiteSpace(value)) return value;
                return ReadString(sleeve, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "備註", "Comments");
            }
            private static string GetStatus(FamilyInstance sleeve, Element source, out string positionInfo, out string brief)
            {
                brief = string.Empty;
                positionInfo = string.Empty;
                if (source == null) { brief = "找不到來源管線"; return "來源遺失"; }
                LocationPoint sleevePoint = sleeve.Location as LocationPoint;
                LocationCurve sourceCurve = source.Location as LocationCurve;
                if (sleevePoint == null || sourceCurve == null) { brief = "無法取得定位幾何"; return "需檢查"; }

                try
                {
                    if (source is Pipe pipe)
                        return GetRoundPipeStatus(sleeve, pipe, out positionInfo, out brief);
                    Curve curve = sourceCurve.Curve;
                    IntersectionResult projected = curve.Project(sleevePoint.Point);
                    if (projected == null) { brief = "無法投影至來源管段"; return "需檢查"; }
                    XYZ nearest = projected.XYZPoint;
                    // Project may return a point on the unbounded extension of the curve.
                    if (curve.IsBound && (projected.Parameter < curve.GetEndParameter(0) ||
                        projected.Parameter > curve.GetEndParameter(1)))
                    {
                        XYZ start = curve.GetEndPoint(0);
                        XYZ end = curve.GetEndPoint(1);
                        nearest = start.DistanceTo(sleevePoint.Point) <= end.DistanceTo(sleevePoint.Point) ? start : end;
                    }
                    double distanceMm = nearest.DistanceTo(sleevePoint.Point) * 304.8;
                    if (distanceMm > 1.0)
                    {
                        brief = $"偏離來源 {distanceMm:0.##} mm";
                        positionInfo = "套管定位點偏離來源管段 " + distanceMm.ToString("0.##", CultureInfo.InvariantCulture) + " mm（容差 1 mm）";
                        return "需更新";
                    }
                    return "正常";
                }
                catch
                {
                    brief = "位置檢查未完成";
                    return "需檢查";
                }
            }

            private static string GetRoundPipeStatus(FamilyInstance sleeve, Pipe pipe, out string info, out string brief)
            {
                brief = "來源不是直線管段";
                info = "來源管線不是直線，尚無法完成圓管淨空檢查。";
                var line = (pipe.Location as LocationCurve)?.Curve as Line;
                if (line == null) return "待確認";
                var ports = sleeve.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>()
                    .Where(c => c.ConnectorType == ConnectorType.End).ToList();
                if (ports == null || ports.Count != 2)
                {
                    brief = $"端點接點 {ports?.Count ?? 0} 個，需 2 個";
                    info = brief + "。請檢查目前模型中的套管族群；更新套管位置不會重新載入族群接點。";
                    return "待確認";
                }
                if (ports.Any(c => c.Shape != ConnectorProfileType.Round))
                {
                    brief = "接點不是圓形";
                    info = "套管端點接點包含非圓形接點，無法套用圓管淨空檢查。";
                    return "待確認";
                }
                XYZ axis = ports[1].Origin - ports[0].Origin;
                if (axis.GetLength() < 1e-6)
                {
                    brief = "兩端接點重疊";
                    info = "套管兩端接點位置重疊，無法確認穿越方向。";
                    return "待確認";
                }
                if (Math.Abs(axis.Normalize().DotProduct(line.Direction)) < 0.999999)
                {
                    info = "管線與套管不同向，斜穿淨空需人工確認。";
                    brief = "斜穿，待確認";
                    return "待確認";
                }
                if (InsulationLiningBase.GetInsulationIds(pipe.Document, pipe.Id).Count > 0)
                {
                    info = "來源管線有保溫，需確認含保溫外徑與封堵間隙。";
                    brief = "有保溫，待確認";
                    return "待確認";
                }
                // Nominal sizes and connector radii do not establish the clear bore.
                double inner = ReadDoubleMm(sleeve, "有效內徑", "套管內徑", "內徑", "Clear Inside Diameter", "Inside Diameter", "Inner Diameter");
                if (inner <= 0) inner = ReadDoubleMm(sleeve.Symbol, "有效內徑", "套管內徑", "內徑", "Clear Inside Diameter", "Inside Diameter", "Inner Diameter");
                bool derivedInner = false;
                if (inner <= 0)
                {
                    double sleeveOuter = ReadDoubleMm(sleeve, "管外直徑", "Outer Diameter", "Outside Diameter");
                    if (sleeveOuter <= 0) sleeveOuter = ReadDoubleMm(sleeve.Symbol, "管外直徑", "Outer Diameter", "Outside Diameter");
                    double wall = ReadDoubleMm(sleeve, "厚度", "壁厚", "Wall Thickness");
                    if (wall <= 0) wall = ReadDoubleMm(sleeve.Symbol, "厚度", "壁厚", "Wall Thickness");
                    // Wall thickness is radial, so both sides reduce the clear diameter.
                    if (sleeveOuter > 0 && wall > 0 && sleeveOuter > 2 * wall)
                    {
                        inner = sleeveOuter - 2 * wall;
                        derivedInner = true;
                    }
                }
                double outer = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)?.AsDouble() * 304.8 ?? 0;
                if (inner <= 0 || double.IsNaN(inner) || double.IsInfinity(inner))
                {
                    brief = "缺少有效套管內徑";
                    info = "未取得有效內徑，也無法由管外直徑減兩倍壁厚推算。請確認實例或族型的長度參數；公稱尺寸與接點半徑不代替淨內徑。";
                    return "待確認";
                }
                if (outer <= 0 || double.IsNaN(outer) || double.IsInfinity(outer))
                {
                    brief = "缺少來源管外徑";
                    info = "來源管線的外徑參數無有效值，無法計算剩餘淨空。";
                    return "待確認";
                }
                double offset = 0;
                foreach (var port in ports)
                {
                    double along = (port.Origin - line.GetEndPoint(0)).DotProduct(line.Direction);
                    if (along < -1.0 / 304.8 || along > line.Length + 1.0 / 304.8)
                    {
                        info = "來源管段未涵蓋套管兩端，請確認管線是否縮短或移離。";
                        brief = "管段未涵蓋套管兩端";
                        return "需處理";
                    }
                    offset = Math.Max(offset, line.Project(port.Origin).Distance * 304.8);
                }
                double remaining = (inner - outer) / 2.0 - offset;
                info = $"有效內徑 {inner:0.##}／管外徑 {outer:0.##}／偏心 {offset:0.##}／最小淨空 {remaining:0.##} mm；未含防火、防水封堵要求。";
                if (derivedInner) info += " 內徑依套管外徑減兩倍單側壁厚推算。";
                brief = remaining < 0 ? $"淨空不足 {-remaining:0.##} mm" :
                    $"偏心 {offset:0.##} mm，剩餘淨空 {remaining:0.##} mm";
                if (remaining < 0) return "需處理";
                return offset > 1.0 ? "偏心可容納" : "正常";
            }
        }
    }
}






















