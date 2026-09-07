using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers;

using WinButton = System.Windows.Forms.Button;
using WinCheckBox = System.Windows.Forms.CheckBox;
using WinComboBox = System.Windows.Forms.ComboBox;
using WinFlowPanel = System.Windows.Forms.FlowLayoutPanel;
using WinForm = System.Windows.Forms.Form;
using WinLabel = System.Windows.Forms.Label;
using WinPanel = System.Windows.Forms.Panel;
using WinTextBox = System.Windows.Forms.TextBox;
using SysColor = System.Drawing.Color;
using SysPoint = System.Drawing.Point;

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.Manual)]
    public class CmdModelDataManager : IExternalCommand
    {
        private static readonly List<WinForm> OpenModelDataManagerForms = new List<WinForm>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null)
            {
                message = "目前沒有開啟的 Revit 文件。";
                return Result.Failed;
            }

            if (!LicenseManager.Instance.HasFeatureAccess("Data.ModelManager"))
            {
                TaskDialog.Show("模型資料管理",
                    "此功能需要 Standard 或 Professional 授權。\n請升級授權以使用模型資料管理工具。");
                return Result.Cancelled;
            }

            try
            {
                var handler = new ModelDataManagerExternalEventHandler();
                var externalEvent = ExternalEvent.Create(handler);
                var dialog = new ModelDataManagerDialog(doc, handler, externalEvent);
                handler.Attach(dialog);
                OpenModelDataManagerForms.Add(dialog);
                dialog.FormClosed += (s, e) =>
                {
                    OpenModelDataManagerForms.Remove(dialog);
                    externalEvent.Dispose();
                };
                dialog.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("模型資料管理", "執行失敗：\n" + ex.Message);
                return Result.Failed;
            }
        }

        private enum ModelDataKind
        {
            Family,
            Type,
            View,
            Material
        }

        private sealed class ModelDataRow
        {
            public bool Selected { get; set; }
            public ModelDataKind Kind { get; set; }
            public ElementId ElementId { get; set; }
            public string KindName { get; set; }
            public string Category { get; set; }
            public string FamilyName { get; set; }
            public string CurrentName { get; set; }
            public string NewName { get; set; }
            public string Status { get; set; }
            public bool IsChanged => !string.Equals(CurrentName ?? string.Empty, NewName ?? string.Empty, StringComparison.Ordinal);
        }

        private enum ModelDataManagerRequestKind
        {
            None,
            Reload,
            ApplyChanges,
            DeleteRows
        }

        private sealed class ModelDataManagerExternalEventHandler : IExternalEventHandler
        {
            private readonly object _syncRoot = new object();
            private ModelDataManagerDialog _dialog;
            private ModelDataManagerRequestKind _requestKind = ModelDataManagerRequestKind.None;
            private List<ModelDataRow> _rows = new List<ModelDataRow>();

            public void Attach(ModelDataManagerDialog dialog)
            {
                _dialog = dialog;
            }

            public void RequestReload()
            {
                lock (_syncRoot)
                {
                    _requestKind = ModelDataManagerRequestKind.Reload;
                    _rows = new List<ModelDataRow>();
                }
            }

            public void RequestApply(IEnumerable<ModelDataRow> rows)
            {
                lock (_syncRoot)
                {
                    _requestKind = ModelDataManagerRequestKind.ApplyChanges;
                    _rows = rows?.ToList() ?? new List<ModelDataRow>();
                }
            }

            public void RequestDelete(IEnumerable<ModelDataRow> rows)
            {
                lock (_syncRoot)
                {
                    _requestKind = ModelDataManagerRequestKind.DeleteRows;
                    _rows = rows?.ToList() ?? new List<ModelDataRow>();
                }
            }

            public void Execute(UIApplication app)
            {
                ModelDataManagerRequestKind requestKind;
                List<ModelDataRow> rows;
                lock (_syncRoot)
                {
                    requestKind = _requestKind;
                    rows = _rows.ToList();
                    _requestKind = ModelDataManagerRequestKind.None;
                    _rows = new List<ModelDataRow>();
                }

                if (_dialog == null || _dialog.IsDisposed)
                {
                    return;
                }

                try
                {
                    switch (requestKind)
                    {
                        case ModelDataManagerRequestKind.Reload:
                            _dialog.ReloadRowsFromApi();
                            break;
                        case ModelDataManagerRequestKind.ApplyChanges:
                            _dialog.ApplyChangesFromApi(rows);
                            break;
                        case ModelDataManagerRequestKind.DeleteRows:
                            _dialog.DeleteRowsFromApi(rows);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "執行模型資料管理動作失敗：\n" + ex.Message,
                        "模型資料管理",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }

            public string GetName()
            {
                return "HB_BIM Tools - 模型資料管理";
            }
        }

        private sealed class ModelDataManagerDialog : WinForm
        {
            private readonly Document _doc;
            private readonly ModelDataManagerExternalEventHandler _externalHandler;
            private readonly ExternalEvent _externalEvent;
            private readonly List<ModelDataRow> _allRows = new List<ModelDataRow>();
            private readonly DataGridView _grid = new DataGridView();
            private readonly WinComboBox _kindCombo = new WinComboBox();
            private readonly WinTextBox _searchBox = new WinTextBox();
            private readonly WinTextBox _replaceFindBox = new WinTextBox();
            private readonly WinTextBox _replaceWithBox = new WinTextBox();
            private readonly WinTextBox _prefixBox = new WinTextBox();
            private readonly WinTextBox _suffixBox = new WinTextBox();
            private readonly WinLabel _summaryLabel = new WinLabel();
            private readonly WinCheckBox _changedOnly = new WinCheckBox();
            private readonly WinCheckBox _replaceSelectedOnly = new WinCheckBox();
            private readonly WinButton _applySelectedButton = new WinButton();
            private readonly WinButton _applyAllButton = new WinButton();
            private readonly WinButton _deleteSelectedButton = new WinButton();
            private readonly WinButton _closeButton = new WinButton();
            private readonly Timer _filterDebounceTimer = new Timer { Interval = 300 };
            private int _lastCheckedRowIndex = -1;

            public ModelDataManagerDialog(
                Document doc,
                ModelDataManagerExternalEventHandler externalHandler,
                ExternalEvent externalEvent)
            {
                _doc = doc;
                _externalHandler = externalHandler;
                _externalEvent = externalEvent;
                Text = "HB_BIM Tools - 模型資料管理";
                StartPosition = FormStartPosition.CenterScreen;
                MinimumSize = new Size(1260, 740);
                Size = new Size(1480, 860);
                BackColor = SysColor.FromArgb(245, 248, 252);
                Font = new Font("Microsoft JhengHei UI", 9.5F);

                BuildLayout();
                _filterDebounceTimer.Tick += (s, e) =>
                {
                    _filterDebounceTimer.Stop();
                    ApplyFilter();
                };
                LoadRows();
                ApplyFilter();
            }

            private void BuildLayout()
            {
                SuspendLayout();

                var header = BuildHeader();
                var filterBar = BuildFilterBar();
                var replaceBar = BuildBatchReplaceBar();
                var bottomPanel = BuildBottomPanel();
                var gridHost = BuildGridHost();

                // WinForms docking uses reverse z-order; add Fill first, then Bottom/Top bars.
                Controls.Add(gridHost);
                Controls.Add(bottomPanel);
                Controls.Add(replaceBar);
                Controls.Add(filterBar);
                Controls.Add(header);

                ResumeLayout();
            }

            private WinPanel BuildHeader()
            {
                var header = new WinPanel
                {
                    Dock = DockStyle.Top,
                    Height = 76,
                    BackColor = SysColor.FromArgb(5, 43, 68),
                    Padding = new Padding(18, 12, 18, 10)
                };

                var title = new WinLabel
                {
                    Text = "模型資料管理",
                    AutoSize = true,
                    ForeColor = SysColor.White,
                    Font = new Font("Microsoft JhengHei UI", 17F, FontStyle.Bold),
                    Location = new SysPoint(18, 14)
                };

                var subtitle = new WinLabel
                {
                    Text = "檢視與調整族群、系統類型、視圖、材料名稱",
                    AutoSize = true,
                    ForeColor = SysColor.FromArgb(205, 222, 238),
                    Font = new Font("Microsoft JhengHei UI", 10F),
                    Location = new SysPoint(190, 25)
                };

                header.Controls.Add(title);
                header.Controls.Add(subtitle);
                return header;
            }

            private WinPanel BuildFilterBar()
            {
                var panel = new WinPanel
                {
                    Dock = DockStyle.Top,
                    Height = 66,
                    Padding = new Padding(16, 12, 16, 10),
                    BackColor = SysColor.FromArgb(238, 244, 251)
                };

                var flow = new WinFlowPanel
                {
                    Dock = DockStyle.Fill,
                    WrapContents = false,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.LeftToRight
                };

                flow.Controls.Add(MakeInlineLabel("管理項目："));
                _kindCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                _kindCombo.Items.AddRange(new object[] { "全部", "族群名稱", "類型/系統類型", "視圖名稱", "材料名稱" });
                _kindCombo.SelectedIndex = 0;
                _kindCombo.Width = 160;
                _kindCombo.Height = 28;
                _kindCombo.Margin = new Padding(0, 4, 20, 0);
                _kindCombo.SelectedIndexChanged += (s, e) => ApplyFilter();
                flow.Controls.Add(_kindCombo);

                flow.Controls.Add(MakeInlineLabel("搜尋："));
                _searchBox.Width = 320;
                _searchBox.Height = 28;
                _searchBox.Margin = new Padding(0, 4, 22, 0);
                _searchBox.TextChanged += (s, e) => ScheduleApplyFilter();
                flow.Controls.Add(_searchBox);

                _changedOnly.Text = "只看已修改";
                _changedOnly.AutoSize = true;
                _changedOnly.Margin = new Padding(0, 7, 20, 0);
                _changedOnly.CheckedChanged += (s, e) => ApplyFilter();
                flow.Controls.Add(_changedOnly);

                var selectAllButton = MakeToolbarButton("全選", 72);
                selectAllButton.Click += (s, e) => SetVisibleRowsSelected(true);
                flow.Controls.Add(selectAllButton);

                var clearButton = MakeToolbarButton("取消選取", 96);
                clearButton.Click += (s, e) => SetVisibleRowsSelected(false);
                flow.Controls.Add(clearButton);

                var reloadButton = MakeToolbarButton("重新讀取", 96);
                reloadButton.Click += (s, e) => RequestReloadRows();
                flow.Controls.Add(reloadButton);

                panel.Controls.Add(flow);
                return panel;
            }

            private WinPanel BuildBatchReplaceBar()
            {
                var panel = new WinPanel
                {
                    Dock = DockStyle.Top,
                    Height = 98,
                    Padding = new Padding(16, 10, 16, 10),
                    BackColor = SysColor.FromArgb(249, 252, 255)
                };

                var flow = new WinFlowPanel
                {
                    Dock = DockStyle.Fill,
                    WrapContents = true,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.LeftToRight
                };

                flow.Controls.Add(MakeInlineLabel("批次取代："));

                flow.Controls.Add(MakeInlineLabel("尋找"));
                _replaceFindBox.Width = 260;
                _replaceFindBox.Height = 28;
                _replaceFindBox.Margin = new Padding(0, 4, 18, 0);
                flow.Controls.Add(_replaceFindBox);

                flow.Controls.Add(MakeInlineLabel("取代為"));
                _replaceWithBox.Width = 260;
                _replaceWithBox.Height = 28;
                _replaceWithBox.Margin = new Padding(0, 4, 18, 0);
                flow.Controls.Add(_replaceWithBox);

                _replaceSelectedOnly.Text = "只處理已勾選";
                _replaceSelectedOnly.AutoSize = true;
                _replaceSelectedOnly.Margin = new Padding(0, 7, 18, 0);
                flow.Controls.Add(_replaceSelectedOnly);

                var previewButton = MakeToolbarButton("預覽取代", 96);
                previewButton.BackColor = SysColor.FromArgb(14, 110, 74);
                previewButton.ForeColor = SysColor.White;
                previewButton.FlatStyle = FlatStyle.Flat;
                previewButton.Click += (s, e) => PreviewBatchReplace();
                flow.Controls.Add(previewButton);

                flow.Controls.Add(MakeInlineLabel("前綴"));
                _prefixBox.Width = 180;
                _prefixBox.Height = 28;
                _prefixBox.Margin = new Padding(0, 4, 14, 0);
                flow.Controls.Add(_prefixBox);

                flow.Controls.Add(MakeInlineLabel("後綴"));
                _suffixBox.Width = 180;
                _suffixBox.Height = 28;
                _suffixBox.Margin = new Padding(0, 4, 14, 0);
                flow.Controls.Add(_suffixBox);

                var affixButton = MakeToolbarButton("預覽頭尾", 96);
                affixButton.BackColor = SysColor.FromArgb(14, 110, 74);
                affixButton.ForeColor = SysColor.White;
                affixButton.FlatStyle = FlatStyle.Flat;
                affixButton.Click += (s, e) => PreviewBatchAffix();
                flow.Controls.Add(affixButton);

                var resetButton = MakeToolbarButton("重設新名稱", 108);
                resetButton.Click += (s, e) => ResetPreviewNames();
                flow.Controls.Add(resetButton);

                var hint = MakeInlineLabel("先預覽至「新名稱」，確認後再套用。");
                hint.ForeColor = SysColor.FromArgb(90, 110, 130);
                hint.Margin = new Padding(10, 8, 0, 0);
                flow.Controls.Add(hint);

                panel.Controls.Add(flow);
                return panel;
            }

            private WinPanel BuildGridHost()
            {
                var host = new WinPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(14, 10, 14, 8),
                    BackColor = SysColor.FromArgb(245, 248, 252)
                };

                ConfigureGrid();
                host.Controls.Add(_grid);
                return host;
            }

            private WinPanel BuildBottomPanel()
            {
                var panel = new WinPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 76,
                    Padding = new Padding(16, 12, 16, 12),
                    BackColor = SysColor.White
                };

                _summaryLabel.AutoSize = true;
                _summaryLabel.ForeColor = SysColor.FromArgb(55, 90, 125);
                _summaryLabel.Font = new Font("Microsoft JhengHei UI", 10F);
                _summaryLabel.Location = new SysPoint(18, 27);

                _applySelectedButton.Text = "套用選取列";
                _applySelectedButton.Width = 126;
                _applySelectedButton.Height = 36;
                _applySelectedButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                _applySelectedButton.Click += (s, e) => ApplyChanges(selectedOnly: true);

                _applyAllButton.Text = "套用全部變更";
                _applyAllButton.Width = 138;
                _applyAllButton.Height = 36;
                _applyAllButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                _applyAllButton.BackColor = SysColor.FromArgb(0, 72, 112);
                _applyAllButton.ForeColor = SysColor.White;
                _applyAllButton.FlatStyle = FlatStyle.Flat;
                _applyAllButton.Click += (s, e) => ApplyChanges(selectedOnly: false);

                _deleteSelectedButton.Text = "刪除勾選列";
                _deleteSelectedButton.Width = 120;
                _deleteSelectedButton.Height = 36;
                _deleteSelectedButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                _deleteSelectedButton.BackColor = SysColor.FromArgb(170, 52, 52);
                _deleteSelectedButton.ForeColor = SysColor.White;
                _deleteSelectedButton.FlatStyle = FlatStyle.Flat;
                _deleteSelectedButton.Click += (s, e) => DeleteSelectedRows();

                _closeButton.Text = "關閉";
                _closeButton.Width = 86;
                _closeButton.Height = 36;
                _closeButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                _closeButton.Click += (s, e) => Close();

                panel.Resize += (s, e) => LayoutBottomButtons(panel);
                panel.Controls.Add(_summaryLabel);
                panel.Controls.Add(_applySelectedButton);
                panel.Controls.Add(_applyAllButton);
                panel.Controls.Add(_deleteSelectedButton);
                panel.Controls.Add(_closeButton);
                LayoutBottomButtons(panel);
                return panel;
            }

            private void LayoutBottomButtons(WinPanel panel)
            {
                _closeButton.Location = new SysPoint(panel.Width - _closeButton.Width - 16, 20);
                _deleteSelectedButton.Location = new SysPoint(_closeButton.Left - _deleteSelectedButton.Width - 10, 20);
                _applyAllButton.Location = new SysPoint(_deleteSelectedButton.Left - _applyAllButton.Width - 10, 20);
                _applySelectedButton.Location = new SysPoint(_applyAllButton.Left - _applySelectedButton.Width - 10, 20);
            }

            private static WinLabel MakeInlineLabel(string text)
            {
                return new WinLabel
                {
                    Text = text,
                    AutoSize = true,
                    Margin = new Padding(0, 8, 6, 0),
                    Font = new Font("Microsoft JhengHei UI", 10F)
                };
            }

            private static WinButton MakeToolbarButton(string text, int width)
            {
                return new WinButton
                {
                    Text = text,
                    Width = width,
                    Height = 30,
                    Margin = new Padding(0, 2, 8, 0)
                };
            }

            private void ConfigureGrid()
            {
                _grid.Dock = DockStyle.Fill;
                _grid.AllowUserToAddRows = false;
                _grid.AllowUserToDeleteRows = false;
                _grid.AutoGenerateColumns = false;
                _grid.BackgroundColor = SysColor.White;
                _grid.BorderStyle = BorderStyle.FixedSingle;
                _grid.RowHeadersVisible = false;
                _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                _grid.MultiSelect = true;
                _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
                _grid.RowTemplate.Height = 32;
                _grid.ColumnHeadersHeight = 38;
                _grid.EnableHeadersVisualStyles = false;
                _grid.ColumnHeadersDefaultCellStyle.BackColor = SysColor.FromArgb(232, 239, 247);
                _grid.ColumnHeadersDefaultCellStyle.ForeColor = SysColor.FromArgb(20, 44, 66);
                _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold);
                _grid.DefaultCellStyle.Font = new Font("Microsoft JhengHei UI", 10F);
                _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
                _grid.AlternatingRowsDefaultCellStyle.BackColor = SysColor.FromArgb(250, 252, 255);
                _grid.DataError += (s, e) => { e.ThrowException = false; };
                _grid.CellClick += HandleGridCellClick;
                _grid.CellEndEdit += (s, e) =>
                {
                    if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is ModelDataRow row)
                    {
                        row.Status = row.IsChanged ? "待套用" : "";
                        _grid.InvalidateRow(e.RowIndex);
                        UpdateSummary();
                    }
                };
                _grid.CellFormatting += (s, e) =>
                {
                    if (e.RowIndex < 0 || !(_grid.Rows[e.RowIndex].DataBoundItem is ModelDataRow row))
                        return;
                    _grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = row.IsChanged
                        ? SysColor.FromArgb(255, 252, 232)
                        : (e.RowIndex % 2 == 0 ? SysColor.White : SysColor.FromArgb(250, 252, 255));
                };

                _grid.Columns.Clear();
                _grid.Columns.Add(new DataGridViewCheckBoxColumn
                {
                    DataPropertyName = nameof(ModelDataRow.Selected),
                    HeaderText = "",
                    Width = 44,
                    MinimumWidth = 44,
                    ReadOnly = true
                });
                _grid.Columns.Add(MakeTextColumn(nameof(ModelDataRow.KindName), "項目", 86, true));
                _grid.Columns.Add(MakeTextColumn(nameof(ModelDataRow.Category), "分類", 180, true));
                _grid.Columns.Add(MakeTextColumn(nameof(ModelDataRow.FamilyName), "族群", 280, true));
                _grid.Columns.Add(MakeTextColumn(nameof(ModelDataRow.CurrentName), "目前名稱", 360, true));
                _grid.Columns.Add(MakeTextColumn(nameof(ModelDataRow.NewName), "新名稱", 420, false));
                _grid.Columns.Add(MakeTextColumn(nameof(ModelDataRow.Status), "狀態", 128, true));
            }

            private static DataGridViewTextBoxColumn MakeTextColumn(string property, string header, int width, bool readOnly)
            {
                return new DataGridViewTextBoxColumn
                {
                    DataPropertyName = property,
                    HeaderText = header,
                    Width = width,
                    MinimumWidth = Math.Min(width, 120),
                    ReadOnly = readOnly,
                    SortMode = DataGridViewColumnSortMode.Automatic,
                    AutoSizeMode = property == nameof(ModelDataRow.NewName)
                        ? DataGridViewAutoSizeColumnMode.Fill
                        : DataGridViewAutoSizeColumnMode.None
                };
            }

            private void HandleGridCellClick(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0)
                    return;

                _grid.EndEdit();
                if (!(_grid.Rows[e.RowIndex].DataBoundItem is ModelDataRow clickedRow))
                    return;

                var targetValue = !clickedRow.Selected;
                var modifiers = System.Windows.Forms.Control.ModifierKeys;
                var isShift = (modifiers & Keys.Shift) == Keys.Shift;
                var selectedGridRows = _grid.SelectedRows
                    .Cast<DataGridViewRow>()
                    .Where(r => r.Index >= 0 && r.DataBoundItem is ModelDataRow)
                    .ToList();

                if (isShift && _lastCheckedRowIndex >= 0 && _lastCheckedRowIndex < _grid.Rows.Count)
                {
                    var start = Math.Min(_lastCheckedRowIndex, e.RowIndex);
                    var end = Math.Max(_lastCheckedRowIndex, e.RowIndex);
                    for (var i = start; i <= end; i++)
                        SetRowChecked(i, targetValue);
                }
                else if (selectedGridRows.Count > 1 && selectedGridRows.Any(r => r.Index == e.RowIndex))
                {
                    foreach (var gridRow in selectedGridRows)
                        SetRowChecked(gridRow.Index, targetValue);
                }
                else
                {
                    SetRowChecked(e.RowIndex, targetValue);
                }

                _lastCheckedRowIndex = e.RowIndex;
                _grid.Refresh();
                UpdateSummary();
            }

            private void SetRowChecked(int rowIndex, bool selected)
            {
                if (rowIndex < 0 || rowIndex >= _grid.Rows.Count)
                    return;

                if (!(_grid.Rows[rowIndex].DataBoundItem is ModelDataRow row))
                    return;

                row.Selected = selected;
                _grid.Rows[rowIndex].Cells[0].Value = selected;
            }

            private void LoadRows()
            {
                _allRows.Clear();
                _allRows.AddRange(CollectFamilies());
                _allRows.AddRange(CollectDimensionTypes());
                _allRows.AddRange(CollectTypes());
                _allRows.AddRange(CollectViews());
                _allRows.AddRange(CollectMaterials());
            }

            private void RequestReloadRows()
            {
                _externalHandler.RequestReload();
                _externalEvent.Raise();
            }

            internal void ReloadRowsFromApi()
            {
                LoadRows();
                _lastCheckedRowIndex = -1;
                ApplyFilter();
            }

            private IEnumerable<ModelDataRow> CollectFamilies()
            {
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Family))
                    .Cast<Autodesk.Revit.DB.Family>()
                    .Where(f => !string.IsNullOrWhiteSpace(f.Name))
                    .OrderBy(f => f.FamilyCategory?.Name)
                    .ThenBy(f => f.Name)
                    .Select(f => new ModelDataRow
                    {
                        Kind = ModelDataKind.Family,
                        KindName = "族群",
                        ElementId = f.Id,
                        Category = f.FamilyCategory?.Name ?? "",
                        FamilyName = f.Name,
                        CurrentName = f.Name,
                        NewName = f.Name,
                        Status = ""
                    });
            }

            private IEnumerable<ModelDataRow> CollectTypes()
            {
                return new FilteredElementCollector(_doc)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .Where(t => !(t is DimensionType) && t.Category != null && !string.IsNullOrWhiteSpace(t.Name))
                    .OrderBy(GetTypeCategoryName)
                    .ThenBy(GetFamilyName)
                    .ThenBy(t => t.Name)
                    .Select(t => new ModelDataRow
                    {
                        Kind = ModelDataKind.Type,
                        KindName = GetTypeKindName(t),
                        ElementId = t.Id,
                        Category = GetTypeCategoryName(t),
                        FamilyName = GetTypeFamilyDisplayName(t),
                        CurrentName = t.Name,
                        NewName = t.Name,
                        Status = ""
                    });
            }

            private IEnumerable<ModelDataRow> CollectDimensionTypes()
            {
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                    .OrderBy(GetDimensionCategoryName)
                    .ThenBy(t => t.Name)
                    .Select(t => new ModelDataRow
                    {
                        Kind = ModelDataKind.Type,
                        KindName = "尺寸類型",
                        ElementId = t.Id,
                        Category = GetDimensionCategoryName(t),
                        FamilyName = "尺寸標註",
                        CurrentName = t.Name,
                        NewName = t.Name,
                        Status = ""
                    });
            }

            private IEnumerable<ModelDataRow> CollectViews()
            {
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .Cast<Autodesk.Revit.DB.View>()
                    .Where(v => !v.IsTemplate && !string.IsNullOrWhiteSpace(v.Name))
                    .OrderBy(v => v.ViewType.ToString())
                    .ThenBy(v => v.Name)
                    .Select(v => new ModelDataRow
                    {
                        Kind = ModelDataKind.View,
                        KindName = "視圖",
                        ElementId = v.Id,
                        Category = v.ViewType.ToString(),
                        FamilyName = "",
                        CurrentName = v.Name,
                        NewName = v.Name,
                        Status = ""
                    });
            }

            private IEnumerable<ModelDataRow> CollectMaterials()
            {
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                    .OrderBy(m => m.MaterialCategory)
                    .ThenBy(m => m.Name)
                    .Select(m => new ModelDataRow
                    {
                        Kind = ModelDataKind.Material,
                        KindName = "材料",
                        ElementId = m.Id,
                        Category = m.MaterialCategory ?? "",
                        FamilyName = "",
                        CurrentName = m.Name,
                        NewName = m.Name,
                        Status = ""
                    });
            }

            private static string GetFamilyName(ElementType type)
            {
                var familyNameParam = type.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
                var familyName = familyNameParam?.AsString();
                return string.IsNullOrWhiteSpace(familyName) ? "" : familyName;
            }

            private static string GetDimensionCategoryName(DimensionType type)
            {
                try
                {
                    var style = type.StyleType.ToString();
                    if (Contains(style, "Linear"))
                        return "線性尺寸標註";
                    if (Contains(style, "Angular"))
                        return "角度尺寸標註";
                    if (Contains(style, "Radial"))
                        return "半徑尺寸標註";
                    if (Contains(style, "Diameter"))
                        return "直徑尺寸標註";
                    if (Contains(style, "Arc"))
                        return "弧長尺寸標註";
                    if (Contains(style, "Spot"))
                        return "高程/座標標註";
                }
                catch
                {
                    // Older project data may not expose style metadata consistently.
                }

                return "尺寸標註";
            }

            private static string GetTypeFamilyDisplayName(ElementType type)
            {
                var familyName = GetFamilyName(type);
                if (!string.IsNullOrWhiteSpace(familyName))
                    return familyName;

                if (type is DimensionType)
                    return "尺寸標註";

                var categoryName = type.Category?.Name ?? "";
                if (IsArrowheadCategory(categoryName))
                    return "箭頭";

                return "系統類型";
            }

            private static string GetTypeCategoryName(ElementType type)
            {
                var categoryName = type.Category?.Name ?? "";
                if (type is DimensionType)
                    return "尺寸標註";
                if (IsArrowheadCategory(categoryName))
                    return "箭頭";
                return string.IsNullOrWhiteSpace(categoryName) ? "未分類類型" : categoryName;
            }

            private static string GetTypeKindName(ElementType type)
            {
                var categoryName = type.Category?.Name ?? "";
                if (type is DimensionType)
                    return "尺寸類型";
                if (IsArrowheadCategory(categoryName))
                    return "箭頭類型";
                return string.IsNullOrWhiteSpace(GetFamilyName(type)) ? "系統類型" : "類型";
            }

            private static bool IsArrowheadCategory(string categoryName)
            {
                return Contains(categoryName, "Arrow") ||
                       Contains(categoryName, "箭頭") ||
                       Contains(categoryName, "箭號");
            }

            private void ScheduleApplyFilter()
            {
                _filterDebounceTimer.Stop();
                _filterDebounceTimer.Start();
            }

            private void ApplyFilter()
            {
                var keyword = (_searchBox.Text ?? "").Trim();
                var kindIndex = _kindCombo.SelectedIndex;
                IEnumerable<ModelDataRow> rows = _allRows;

                if (kindIndex > 0)
                {
                    var kind = (ModelDataKind)(kindIndex - 1);
                    rows = rows.Where(r => r.Kind == kind);
                }

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    rows = rows.Where(r =>
                        Contains(r.Category, keyword) ||
                        Contains(r.FamilyName, keyword) ||
                        Contains(r.CurrentName, keyword) ||
                        Contains(r.NewName, keyword));
                }

                if (_changedOnly.Checked)
                    rows = rows.Where(r => r.IsChanged);

                _grid.DataSource = rows.ToList();
                UpdateSummary();
            }

            private static bool Contains(string text, string keyword)
            {
                return (text ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private void SetVisibleRowsSelected(bool selected)
            {
                _grid.EndEdit();
                foreach (DataGridViewRow viewRow in _grid.Rows)
                {
                    if (viewRow.DataBoundItem is ModelDataRow row)
                    {
                        row.Selected = selected;
                        viewRow.Cells[0].Value = selected;
                    }
                }
                _grid.Refresh();
                UpdateSummary();
            }

            private void PreviewBatchReplace()
            {
                _grid.EndEdit();
                var find = _replaceFindBox.Text ?? string.Empty;
                var replaceWith = _replaceWithBox.Text ?? string.Empty;

                if (string.IsNullOrWhiteSpace(find))
                {
                    MessageBox.Show("請輸入要尋找的文字。", "批次取代",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var rows = GetVisibleRows()
                    .Where(r => !_replaceSelectedOnly.Checked || r.Selected)
                    .ToList();

                if (rows.Count == 0)
                {
                    MessageBox.Show(_replaceSelectedOnly.Checked ? "目前沒有已勾選的列。" : "目前沒有可處理的列。",
                        "批次取代", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int changed = 0;
                foreach (var row in rows)
                {
                    var source = row.NewName ?? string.Empty;
                    var next = ReplaceText(source, find, replaceWith);
                    if (next == source)
                        continue;

                    row.NewName = next;
                    row.Status = "待套用";
                    changed++;
                }

                _grid.Refresh();
                UpdateSummary();
                MessageBox.Show($"已預覽取代 {changed} 筆。請確認「新名稱」後再套用。",
                    "批次取代", MessageBoxButtons.OK,
                    changed > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }

            private void PreviewBatchAffix()
            {
                _grid.EndEdit();
                var prefix = _prefixBox.Text ?? string.Empty;
                var suffix = _suffixBox.Text ?? string.Empty;

                if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(suffix))
                {
                    MessageBox.Show("請輸入前綴或後綴文字。", "批次加入文字",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var rows = GetVisibleRows()
                    .Where(r => !_replaceSelectedOnly.Checked || r.Selected)
                    .ToList();

                if (rows.Count == 0)
                {
                    MessageBox.Show(_replaceSelectedOnly.Checked ? "目前沒有已勾選的項目。" : "目前沒有可處理的項目。",
                        "批次加入文字", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int changed = 0;
                foreach (var row in rows)
                {
                    var source = row.NewName ?? string.Empty;
                    var next = prefix + source + suffix;
                    if (next == source)
                        continue;

                    row.NewName = next;
                    row.Status = "已預覽";
                    changed++;
                }

                _grid.Refresh();
                UpdateSummary();
                MessageBox.Show($"已預覽加入文字 {changed} 筆。請確認「新名稱」後再套用。",
                    "批次加入文字", MessageBoxButtons.OK,
                    changed > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }

            private void ResetPreviewNames()
            {
                _grid.EndEdit();
                var rows = GetVisibleRows()
                    .Where(r => !_replaceSelectedOnly.Checked || r.Selected)
                    .ToList();

                foreach (var row in rows)
                {
                    row.NewName = row.CurrentName;
                    row.Status = "";
                }

                _grid.Refresh();
                UpdateSummary();
            }

            private List<ModelDataRow> GetVisibleRows()
            {
                return _grid.Rows
                    .Cast<DataGridViewRow>()
                    .Select(r => r.DataBoundItem as ModelDataRow)
                    .Where(r => r != null)
                    .ToList();
            }

            private static string ReplaceText(string source, string find, string replaceWith)
            {
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(find))
                    return source ?? string.Empty;

                var result = new System.Text.StringBuilder();
                int start = 0;
                while (start < source.Length)
                {
                    var index = source.IndexOf(find, start, StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                    {
                        result.Append(source.Substring(start));
                        break;
                    }

                    result.Append(source.Substring(start, index - start));
                    result.Append(replaceWith);
                    start = index + find.Length;
                }

                return result.ToString();
            }

            private void ApplyChanges(bool selectedOnly)
            {
                _grid.EndEdit();
                var visibleRows = GetVisibleRows();

                var targetRows = visibleRows
                    .Where(r => r.IsChanged && (!selectedOnly || r.Selected))
                    .ToList();

                if (targetRows.Count == 0)
                {
                    MessageBox.Show(selectedOnly ? "選取列中沒有需要套用的變更。" : "目前沒有需要套用的變更。",
                        "模型資料管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var validationErrors = ValidateRenameTargets(targetRows);
                if (validationErrors.Count > 0)
                {
                    _grid.Refresh();
                    UpdateSummary();
                    MessageBox.Show(
                        "以下項目需要先修正，已列出前 10 筆：\n\n" + string.Join("\n", validationErrors.Take(10)),
                        "模型資料管理",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
                var confirm = MessageBox.Show(
                    $"即將套用 {targetRows.Count} 筆名稱變更。\n\n建議先確認沒有重名或命名規則衝突。是否繼續？",
                    "套用變更",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.OK)
                    return;

                RequestApplyChanges(targetRows);
            }

            private List<string> ValidateRenameTargets(List<ModelDataRow> targetRows)
            {
                var errors = new List<string>();
                var duplicateTargets = new HashSet<ModelDataRow>(targetRows
                    .GroupBy(GetRenameScopeKey, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .SelectMany(g => g));

                foreach (var row in targetRows)
                {
                    var newName = (row.NewName ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        row.Status = "名稱空白";
                        errors.Add($"{row.KindName}：{row.CurrentName} -> 名稱不可空白");
                        continue;
                    }

                    if (HasInvalidRevitNameChars(newName))
                    {
                        row.Status = "名稱含非法字元";
                        errors.Add($"{row.KindName}：{row.CurrentName} -> 名稱含非法字元");
                        continue;
                    }

                    if (duplicateTargets.Contains(row))
                    {
                        row.Status = "新名稱重複";
                        errors.Add($"{row.KindName}：{row.CurrentName} -> 新名稱與本次批次項目重複");
                        continue;
                    }

                    var key = GetRenameScopeKey(row);
                    var existing = _allRows.FirstOrDefault(r =>
                        !IsSameElement(r, row) &&
                        string.Equals(GetCurrentScopeKey(r), key, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        row.Status = "已存在同名項目";
                        errors.Add($"{row.KindName}：{row.CurrentName} -> 已存在同名項目：{existing.CurrentName}");
                        continue;
                    }

                    row.Status = row.IsChanged ? "待套用" : row.Status;
                }

                return errors;
            }

            private static string GetRenameScopeKey(ModelDataRow row)
            {
                return GetScopePrefix(row) + "|" + ((row.NewName ?? string.Empty).Trim());
            }

            private static string GetCurrentScopeKey(ModelDataRow row)
            {
                return GetScopePrefix(row) + "|" + ((row.CurrentName ?? string.Empty).Trim());
            }

            private static string GetScopePrefix(ModelDataRow row)
            {
                return row.Kind == ModelDataKind.Type
                    ? $"{row.Kind}|{row.Category}|{row.FamilyName}"
                    : row.Kind.ToString();
            }

            private static bool IsSameElement(ModelDataRow left, ModelDataRow right)
            {
                return left.ElementId.GetIdValue() == right.ElementId.GetIdValue();
            }

            private static bool HasInvalidRevitNameChars(string name)
            {
                return Regex.IsMatch(name ?? string.Empty, @"[\\:{}\[\]|;<>?`~]");
            }
            private void RequestApplyChanges(List<ModelDataRow> targetRows)
            {
                _externalHandler.RequestApply(targetRows);
                _externalEvent.Raise();
            }

            internal void ApplyChangesFromApi(List<ModelDataRow> targetRows)
            {
                int success = 0;
                var errors = new List<string>();
                using (var tx = new Transaction(_doc, "模型資料管理 - 批次改名"))
                {
                    tx.Start();
                    foreach (var row in targetRows)
                    {
                        try
                        {
                            var newName = (row.NewName ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(newName))
                            {
                                row.Status = "名稱空白";
                                continue;
                            }

                            var element = _doc.GetElement(row.ElementId);
                            if (element == null)
                            {
                                row.Status = "找不到元素";
                                continue;
                            }

                            element.Name = newName;
                            row.CurrentName = newName;
                            row.NewName = newName;
                            row.Status = "已套用";
                            success++;
                        }
                        catch (Exception ex)
                        {
                            row.Status = "失敗";
                            errors.Add($"{row.KindName}「{row.CurrentName}」：{ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                _grid.Refresh();
                UpdateSummary();

                var msg = $"套用完成：成功 {success} 筆，失敗 {errors.Count} 筆。";
                if (errors.Count > 0)
                    msg += "\n\n失敗前 10 筆：\n" + string.Join("\n", errors.Take(10));
                MessageBox.Show(msg, "模型資料管理", MessageBoxButtons.OK,
                    errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }

            private void DeleteSelectedRows()
            {
                _grid.EndEdit();
                var targetRows = GetVisibleRows()
                    .Where(r => r.Selected)
                    .ToList();

                if (targetRows.Count == 0)
                {
                    MessageBox.Show("目前沒有已勾選的列。", "刪除模型資料",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var preview = string.Join("\n", targetRows
                    .Take(12)
                    .Select(r => $"{r.KindName}｜{r.CurrentName}"));
                if (targetRows.Count > 12)
                    preview += $"\n...另有 {targetRows.Count - 12} 筆";

                var confirm = MessageBox.Show(
                    $"即將刪除已勾選的 {targetRows.Count} 筆模型資料。\n\n{preview}\n\n刪除後無法由工具復原，是否繼續？",
                    "刪除模型資料",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.OK)
                    return;

                RequestDeleteRows(targetRows);
            }

            private void RequestDeleteRows(List<ModelDataRow> targetRows)
            {
                _externalHandler.RequestDelete(targetRows);
                _externalEvent.Raise();
            }

            internal void DeleteRowsFromApi(List<ModelDataRow> targetRows)
            {
                int success = 0;
                var errors = new List<string>();
                using (var tx = new Transaction(_doc, "模型資料管理 - 刪除勾選列"))
                {
                    tx.Start();
                    foreach (var row in targetRows)
                    {
                        try
                        {
                            var element = _doc.GetElement(row.ElementId);
                            if (element == null)
                            {
                                row.Status = "找不到元素";
                                continue;
                            }

                            _doc.Delete(row.ElementId);
                            success++;
                        }
                        catch (Exception ex)
                        {
                            row.Status = "刪除失敗";
                            errors.Add($"{row.KindName}｜{row.CurrentName}：{ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                if (success > 0)
                {
                    var deletedIds = new HashSet<long>(targetRows
                        .Where(r => _doc.GetElement(r.ElementId) == null)
                        .Select(r => r.ElementId.GetIdValue()));
                    _allRows.RemoveAll(r => deletedIds.Contains(r.ElementId.GetIdValue()));
                    _lastCheckedRowIndex = -1;
                    ApplyFilter();
                }
                else
                {
                    _grid.Refresh();
                    UpdateSummary();
                }

                var msg = $"刪除完成：成功 {success} 筆，失敗 {errors.Count} 筆。";
                if (errors.Count > 0)
                    msg += "\n\n失敗前 10 筆：\n" + string.Join("\n", errors.Take(10));
                MessageBox.Show(msg, "刪除模型資料", MessageBoxButtons.OK,
                    errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }

            private void UpdateSummary()
            {
                var visible = GetVisibleRows();
                var changed = visible.Count(r => r.IsChanged);
                var selected = visible.Count(r => r.Selected);
                _summaryLabel.Text = $"顯示 {visible.Count} 筆，已選 {selected} 筆，待套用 {changed} 筆";
            }
        }
    }
}
