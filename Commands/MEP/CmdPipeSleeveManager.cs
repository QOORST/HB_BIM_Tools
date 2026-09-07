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

                if (_openForm != null && !_openForm.IsDisposed)
                {
                    _openForm.Activate();
                    return Result.Succeeded;
                }

                var handler = new PipeSleeveManagerRequestHandler();
                ExternalEvent externalEvent = ExternalEvent.Create(handler);
                var form = new PipeSleeveManagerForm(commandData.Application, uidoc, handler, externalEvent);
                handler.Window = form;
                form.FormClosed += (s, e) =>
                {
                    if (ReferenceEquals(_openForm, form)) _openForm = null;
                    externalEvent.Dispose();
                };
                _openForm = form;
                form.Show(new RevitWindow(commandData.Application.MainWindowHandle));

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
        Focus,
        Delete,
        Update
    }

    internal sealed class PipeSleeveManagerRequestHandler : IExternalEventHandler
    {
        private PipeSleeveManagerAction _pendingAction = PipeSleeveManagerAction.None;

        public PipeSleeveManagerForm Window { get; set; }

        public void Request(PipeSleeveManagerAction action)
        {
            _pendingAction = action;
        }

        public void Execute(UIApplication app)
        {
            PipeSleeveManagerAction action = _pendingAction;
            _pendingAction = PipeSleeveManagerAction.None;

            PipeSleeveManagerForm window = Window;
            if (window == null || window.IsDisposed || action == PipeSleeveManagerAction.None) return;

            try
            {
                switch (action)
                {
                    case PipeSleeveManagerAction.Reload:
                        window.OrganizeSleevesInternal();
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
        private readonly WinForms.Label _summary = new WinForms.Label();
        private readonly WinForms.Label _detail = new WinForms.Label();
        private readonly WinForms.Label _scopeNote = new WinForms.Label();
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
            Text = "套管管理";
            Width = 1120;
            Height = 720;
            MinimumSize = new Size(980, 620);
            StartPosition = WinForms.FormStartPosition.CenterParent;
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = System.Drawing.Color.White;

            var root = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new WinForms.Padding(18)
            };
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Percent, 100));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            Controls.Add(root);

            var title = new WinForms.Label
            {
                Text = "套管管理",
                Font = new Font(Font.FontFamily, 20F, FontStyle.Bold),
                AutoSize = true,
                Margin = new WinForms.Padding(0, 0, 0, 2)
            };
            var hint = new WinForms.Label
            {
                Text = "只列出目前視圖範圍內、自動生成且含來源管線 ID 的套管；雙擊清單定位，選取多筆可批次更新或刪除。",
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
            titleStack.Controls.Add(hint);
            _scopeNote.AutoSize = true;
            _scopeNote.ForeColor = System.Drawing.Color.DimGray;
            _scopeNote.Margin = new WinForms.Padding(0, 0, 0, 8);
            titleStack.Controls.Add(_scopeNote);
            root.Controls.Add(titleStack, 0, 0);

            var filters = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 10,
                AutoSize = true,
                Margin = new WinForms.Padding(0, 0, 0, 10)
            };
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 120));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 160));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 110));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 120));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            filters.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 100));
            root.Controls.Add(filters, 0, 1);

            AddFilter(filters, "樓層", _levelFilter, 0);
            AddFilter(filters, "系統", _systemFilter, 2);
            AddFilter(filters, "穿越", _hostFilter, 4);
            AddFilter(filters, "狀態", _statusFilter, 6);
            filters.Controls.Add(new WinForms.Label { Text = "搜尋", AutoSize = true, Anchor = WinForms.AnchorStyles.Left, Margin = new WinForms.Padding(10, 4, 4, 4) }, 8, 0);
            _searchBox.Dock = WinForms.DockStyle.Fill;
            _searchBox.Margin = new WinForms.Padding(4, 2, 0, 2);
            _searchBox.TextChanged += (s, e) => ApplyFilters();
            filters.Controls.Add(_searchBox, 9, 0);

            ConfigureGrid();
            root.Controls.Add(_grid, 0, 2);

            var bottom = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Margin = new WinForms.Padding(0, 10, 0, 0)
            };
            bottom.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.AutoSize));
            root.Controls.Add(bottom, 0, 3);

            var infoStack = new WinForms.FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = WinForms.FlowDirection.TopDown,
                WrapContents = false,
                Anchor = WinForms.AnchorStyles.Left
            };
            _summary.AutoSize = true;
            _summary.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            _summary.Margin = new WinForms.Padding(0, 0, 0, 4);
            _detail.AutoSize = true;
            _detail.MaximumSize = new Size(680, 0);
            _detail.ForeColor = System.Drawing.Color.DimGray;
            infoStack.Controls.Add(_summary);
            infoStack.Controls.Add(_detail);
            bottom.Controls.Add(infoStack, 0, 0);

            var buttons = new WinForms.FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = WinForms.FlowDirection.RightToLeft,
                WrapContents = false
            };
            bottom.Controls.Add(buttons, 1, 0);

            buttons.Controls.Add(MakeButton("關閉", 92, (s, e) => Close()));
            buttons.Controls.Add(MakeButton("刪除", 92, (s, e) => Raise(PipeSleeveManagerAction.Delete)));
            buttons.Controls.Add(MakeButton("更新", 92, (s, e) => Raise(PipeSleeveManagerAction.Update), true));
            buttons.Controls.Add(MakeButton("定位", 92, (s, e) => Raise(PipeSleeveManagerAction.Focus)));
            buttons.Controls.Add(MakeButton("整理", 92, (s, e) => Raise(PipeSleeveManagerAction.Reload)));
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
                FlatStyle = WinForms.FlatStyle.System
            };
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
            _grid.BorderStyle = WinForms.BorderStyle.FixedSingle;
            _grid.BackgroundColor = System.Drawing.Color.White;
            _grid.GridColor = System.Drawing.Color.Gainsboro;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersHeight = 34;
            _grid.RowTemplate.Height = 31;
            _grid.AutoSizeRowsMode = WinForms.DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
            _grid.DefaultCellStyle.WrapMode = WinForms.DataGridViewTriState.False;
            _grid.DefaultCellStyle.Padding = new WinForms.Padding(4, 3, 4, 3);
            _grid.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(0, 120, 215);
            _grid.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.White;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
            _grid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(235, 239, 243);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.Black;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new WinForms.Padding(4, 4, 4, 4);
            _grid.DoubleClick += (s, e) => Raise(PipeSleeveManagerAction.Focus);
            _grid.SelectionChanged += (s, e) => UpdateDetail();
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;

            _grid.Columns.Add(MakeColumn("Mark", "編號", 88));
            _grid.Columns.Add(MakeColumn("Level", "樓層", 82));
            _grid.Columns.Add(MakeColumn("SystemName", "系統", 210));
            _grid.Columns.Add(MakeColumn("HostType", "穿越", 78));
            _grid.Columns.Add(MakeColumn("NominalDiameter", "DN", 76));
            _grid.Columns.Add(MakeColumn("Status", "狀態", 98));
            _grid.Columns.Add(MakeColumn("CheckInfo", "檢核摘要", 360, true, true));
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
                if (status == "正常")
                {
                    e.CellStyle.ForeColor = System.Drawing.Color.SeaGreen;
                }
                else if (status == "需更新" || status == "提醒")
                {
                    e.CellStyle.ForeColor = System.Drawing.Color.DarkOrange;
                    e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                }
                else if (status == "來源遺失" || status == "需檢查" || status == "需結構確認")
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
            if (selected.Count == 0)
            {
                _detail.Text = "選取一筆套管可查看長度、立面高程與類型。";
                return;
            }

            PipeSleeveManagerRow row = selected[0];
            string multi = selected.Count > 1 ? $"，已選 {selected.Count} 筆" : string.Empty;
            _detail.Text = $"長度 {Blank(row.LengthMm)} mm，立面 {Blank(row.ElevationMm)}，類型 {row.TypeName}{multi}";
        }

        private static string Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
        private void Raise(PipeSleeveManagerAction action)
        {
            _handler.Request(action);
            _externalEvent.Raise();
        }

        private static WinForms.DataGridViewTextBoxColumn MakeColumn(string property, string header, int width, bool fill = false, bool wrap = false)
        {
            var column = new WinForms.DataGridViewTextBoxColumn
            {
                DataPropertyName = property,
                HeaderText = header,
                Width = width,
                MinimumWidth = Math.Min(width, 90),
                AutoSizeMode = fill ? WinForms.DataGridViewAutoSizeColumnMode.Fill : WinForms.DataGridViewAutoSizeColumnMode.None,
                SortMode = WinForms.DataGridViewColumnSortMode.Automatic
            };
            if (wrap)
            {
                column.DefaultCellStyle.WrapMode = WinForms.DataGridViewTriState.True;
            }
            return column;
        }

        internal void ReloadRowsInternal()
        {
            _allRows = CollectSleeves(_doc, _doc.ActiveView);
            _scopeNote.Text = BuildScopeNote(_doc.ActiveView, _allRows.Count);
            LoadFilter(_levelFilter, _allRows.Select(r => r.Level));
            LoadFilter(_systemFilter, _allRows.Select(r => r.SystemName));
            LoadFilter(_hostFilter, _allRows.Select(r => r.HostType));
            LoadFilter(_statusFilter, _allRows.Select(r => r.Status));
            ApplyFilters();
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
            string level = SelectedFilter(_levelFilter);
            string system = SelectedFilter(_systemFilter);
            string host = SelectedFilter(_hostFilter);
            string status = SelectedFilter(_statusFilter);
            string search = _searchBox.Text?.Trim() ?? string.Empty;

            IEnumerable<PipeSleeveManagerRow> rows = _allRows;
            if (!string.IsNullOrEmpty(level)) rows = rows.Where(r => r.Level == level);
            if (!string.IsNullOrEmpty(system)) rows = rows.Where(r => r.SystemName == system);
            if (!string.IsNullOrEmpty(host)) rows = rows.Where(r => r.HostType == host);
            if (!string.IsNullOrEmpty(status)) rows = rows.Where(r => r.Status == status);
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(r => r.SearchText.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

            List<PipeSleeveManagerRow> visible = rows.ToList();
            _grid.DataSource = visible;
            int needAction = visible.Count(r => r.Status != "正常");
            _summary.Text = $"{visible.Count} / {_allRows.Count} 個套管，需處理 {needAction} 個";
            UpdateDetail();
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
                tx.Commit();
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

            List<ElementId> staleSleeveIds = _allRows
                .Where(row => sourceIds.Contains(row.SourcePipeIdValue))
                .Select(row => RevitApiCompatibility.CreateElementId(row.ElementIdValue))
                .Where(id => _doc.GetElement(id) != null)
                .Distinct(new ElementIdComparer())
                .ToList();

            PipeSleeveResult result;
            using (Transaction tx = new Transaction(_doc, "更新選取套管"))
            {
                tx.Start();
                if (staleSleeveIds.Count > 0)
                {
                    _doc.Delete(staleSleeveIds);
                }

                result = PipeSleeveService.CreateSleeves(_doc, pipes, new PipeSleeveOptions
                {
                    ClearanceMm = 50.0,
                    IncludeCurrentModel = true,
                    IncludeLinks = true,
                    ExcludeAdditionElements = true,
                    UseDiameterSymbolMap = true,
                    AutoNumber = false,
                    SkipExisting = false,
                    LimitToActiveView = true,
                    ActiveViewId = _doc.ActiveView != null ? _doc.ActiveView.Id : ElementId.InvalidElementId
                });
                tx.Commit();
            }

            ReloadRowsInternal();
            WinForms.MessageBox.Show(this, "已清理舊套管: " + staleSleeveIds.Count.ToString(CultureInfo.InvariantCulture) + " 個\n" + result.ToTaskDialogText(), "套管更新", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
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
                tx.Commit();
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
            public string TypeName { get; set; }
            public string SearchText => string.Join(" ", ElementId, Mark, Level, SystemName, HostType, NominalDiameter, Status, CheckInfo, TypeName);

            public static PipeSleeveManagerRow From(Document doc, FamilyInstance sleeve)
            {
                string sourceIdText = ReadString(sleeve, "來源管線Id", "Pipe Id", "Source Pipe Id");
                long sourceId = ReadLong(sleeve, "來源管線Id", "Pipe Id", "Source Pipe Id");
                Element source = sourceId > 0 ? doc.GetElement(RevitApiCompatibility.CreateElementId(sourceId)) : null;
                string status = GetStatus(sleeve, source);
                string checkInfo = ReadString(sleeve, "套管檢核資訊", "Sleeve Check", "Check Info", "穿梁檢核", "Beam Opening Check", "結構檢核", "Structural Check");
                if (status == "正常")
                {
                    status = GetBeamRiskStatus(checkInfo);
                }
                if (status == "來源遺失")
                {
                    checkInfo = GetSourceMissingReason(sourceIdText, sourceId);
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
                    CheckInfo = checkInfo,
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
            private static string GetStatus(FamilyInstance sleeve, Element source)
            {
                if (source == null) return "來源遺失";
                LocationPoint sleevePoint = sleeve.Location as LocationPoint;
                LocationCurve sourceCurve = source.Location as LocationCurve;
                if (sleevePoint == null || sourceCurve == null) return "需檢查";

                try
                {
                    IntersectionResult projected = sourceCurve.Curve.Project(sleevePoint.Point);
                    if (projected == null) return "需檢查";
                    double distanceMm = projected.Distance * 304.8;
                    return distanceMm > 100.0 ? "需更新" : "正常";
                }
                catch
                {
                    return "需檢查";
                }
            }
        }
    }
}























