#if REVIT2026
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DrawingColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    internal static class RoomFinish2026ModelessController
    {
        private static RoomFinish2026Form _form;
        private static ExternalEvent _externalEvent;
        private static RoomFinish2026Handler _handler;

        internal static Result Show(UIApplication uiApp, ref string message)
        {
            UIDocument uiDoc = uiApp?.ActiveUIDocument;
            if (uiDoc?.Document == null)
            {
                message = "目前沒有可用的 Revit 文件。";
                return Result.Failed;
            }

            if (_form != null && !_form.IsDisposed)
            {
                _form.Activate();
                _handler.UpdateContext(uiDoc);
                _form.Reload(uiDoc);
                return Result.Succeeded;
            }

            try
            {
                _handler = new RoomFinish2026Handler(uiDoc);
                _externalEvent = ExternalEvent.Create(_handler);
                _form = new RoomFinish2026Form(uiDoc, Request);
                _handler.Attach(_form);
                _form.FormClosed += (_, __) => Dispose();
                _form.Show(new RevitWindow(uiApp.MainWindowHandle));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Dispose();
                return Result.Failed;
            }
        }

        private static void Request(RoomFinishRequest request, FinishSettings settings)
        {
            if (_handler == null || _externalEvent == null)
                return;

            _handler.Request(request, settings);
            if (_externalEvent.Raise() != ExternalEventRequest.Accepted)
                _form?.Complete("目前無法送出 Revit 動作，請稍後再試。", true);
        }

        private static void Dispose()
        {
            RoomFinish2026Form form = _form;
            ExternalEvent externalEvent = _externalEvent;
            _form = null;
            _externalEvent = null;
            _handler = null;
            if (form != null && !form.IsDisposed)
                form.Dispose();
            externalEvent?.Dispose();
        }

        private sealed class RevitWindow : WinForms.IWin32Window
        {
            internal RevitWindow(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }
    }

    internal enum RoomFinishRequest
    {
        None,
        PickRooms,
        Run
    }

    internal sealed class RoomFinish2026Handler : IExternalEventHandler
    {
        private UIDocument _uiDoc;
        private RoomFinish2026Form _form;
        private RoomFinishRequest _request;
        private FinishSettings _settings;

        internal RoomFinish2026Handler(UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
        }

        internal void Attach(RoomFinish2026Form form)
        {
            _form = form;
        }

        internal void UpdateContext(UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
        }

        internal void Request(RoomFinishRequest request, FinishSettings settings)
        {
            _request = request;
            _settings = settings;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument current = app.ActiveUIDocument;
                if (current?.Document == null)
                {
                    _form?.Complete("目前沒有可用的 Revit 文件。", true);
                    return;
                }

                _uiDoc = current;
                if (_request == RoomFinishRequest.PickRooms)
                {
                    _form?.SetVisibleForPick(false);
                    IList<Reference> refs = current.Selection.PickObjects(
                        ObjectType.Element,
                        new RoomSelectionFilter(),
                        "請選擇要處理的房間，完成後按 Finish");
                    List<ElementId> ids = refs
                        .Select(x => x.ElementId)
                        .Distinct()
                        .ToList();
                    current.Selection.SetElementIds(ids);
                    _form?.SetPickedRooms(ids);
                    _form?.Complete($"已選取 {ids.Count} 間房間。");
                    return;
                }

                if (_request == RoomFinishRequest.Run)
                {
                    FinishSettings settings = _settings;
                    if (settings == null)
                    {
                        _form?.Complete("找不到執行設定。", true);
                        return;
                    }

                    settings.TargetRoomIds = ResolveTargetRooms(current, settings.TargetRoomIds);
                    if (settings.TargetRoomIds.Count == 0)
                    {
                        _form?.Complete("沒有可處理的房間。", true);
                        return;
                    }

                    string message = string.Empty;
                    Result result = CmdRoomFinish.ExecuteRoomFinish(current, settings, ref message);
                    _form?.Complete(
                        string.IsNullOrWhiteSpace(message)
                            ? (result == Result.Succeeded ? "房間裝修執行完成。" : "房間裝修未完成。")
                            : message,
                        result == Result.Failed);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                _form?.Complete("已取消模型選取。");
            }
            catch (Exception ex)
            {
                _form?.Complete(ex.Message, true);
            }
            finally
            {
                _request = RoomFinishRequest.None;
                _settings = null;
                _form?.SetVisibleForPick(true);
            }
        }

        public string GetName() => "HB_BIM Room Finish 2026";

        private static List<ElementId> ResolveTargetRooms(
            UIDocument uiDoc,
            IList<ElementId> requested)
        {
            Document doc = uiDoc.Document;
            if (requested != null && requested.Count > 0)
                return requested.Where(id => doc.GetElement(id) is Room).Distinct().ToList();

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .Where(id => doc.GetElement(id) is Room)
                .ToList();
        }

        private sealed class RoomSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Room;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }

    internal sealed class RoomFinish2026Form : WinForms.Form
    {
        private readonly Action<RoomFinishRequest, FinishSettings> _request;
        private readonly WinForms.ComboBox _wallType = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _floorType = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _ceilingType = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _skirtingType = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _boundary = new WinForms.ComboBox();
        private readonly WinForms.NumericUpDown _wallHeight = new WinForms.NumericUpDown();
        private readonly WinForms.NumericUpDown _ceilingHeight = new WinForms.NumericUpDown();
        private readonly WinForms.NumericUpDown _skirtingHeight = new WinForms.NumericUpDown();
        private readonly WinForms.CheckBox _generate = new WinForms.CheckBox();
        private readonly WinForms.CheckBox _update = new WinForms.CheckBox();
        private readonly WinForms.CheckBox _join = new WinForms.CheckBox();
        private readonly WinForms.CheckBox _usePicked = new WinForms.CheckBox();
        private readonly WinForms.DataGridView _roomsGrid = new WinForms.DataGridView();
        private readonly WinForms.Label _pickedLabel = new WinForms.Label();
        private readonly WinForms.Label _status = new WinForms.Label();
        private readonly WinForms.Button _pickButton = new WinForms.Button();
        private readonly WinForms.Button _reloadRoomsButton = new WinForms.Button();
        private readonly WinForms.Button _selectAllButton = new WinForms.Button();
        private readonly WinForms.Button _clearSelectionButton = new WinForms.Button();
        private readonly WinForms.Button _exportButton = new WinForms.Button();
        private readonly WinForms.Button _updateOnlyButton = new WinForms.Button();
        private readonly WinForms.Button _generateButton = new WinForms.Button();
        private readonly WinForms.Button _runButton = new WinForms.Button();
        private List<ElementId> _pickedRoomIds = new List<ElementId>();
        private Document _document;

        internal RoomFinish2026Form(
            UIDocument uiDoc,
            Action<RoomFinishRequest, FinishSettings> request)
        {
            _request = request;
            Text = "HB_BIM Tools - 房間裝修管理";
            StartPosition = WinForms.FormStartPosition.CenterParent;
            MinimumSize = new Size(980, 680);
            ClientSize = new Size(1120, 720);
            Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5f);
            MaximizeBox = false;
            BuildUi();
            Reload(uiDoc);
        }

        internal void Reload(UIDocument uiDoc)
        {
            if (uiDoc?.Document == null)
                return;

            _document = uiDoc.Document;
            FinishSettings saved = FinishSettings.LoadFromFile() ?? new FinishSettings();
            FillTypes(_wallType, typeof(WallType), saved.SelectedWallTypeId, x => x is WallType wt && wt.Kind == WallKind.Basic);
            FillTypes(_floorType, typeof(FloorType), saved.SelectedFloorTypeId);
            FillTypes(_ceilingType, typeof(CeilingType), saved.SelectedCeilingTypeId);
            FillTypes(_skirtingType, typeof(WallType), saved.SelectedSkirtingTypeId, x => x is WallType wt && wt.Kind == WallKind.Basic);
            _boundary.SelectedIndex = saved.BoundaryMode == FloorBoundaryMode.Centerline
                ? 1
                : saved.BoundaryMode == FloorBoundaryMode.OuterFinish ? 2 : 0;
            _wallHeight.Value = Clamp(saved.WallHeightMm, _wallHeight);
            _ceilingHeight.Value = Clamp(saved.CeilingHeightMm, _ceilingHeight);
            _skirtingHeight.Value = Clamp(saved.SkirtingHeightMm, _skirtingHeight);
            _generate.Checked = saved.GenerateGeometry;
            _update.Checked = saved.UpdateValues;
            _join.Checked = saved.AutoJoinWalls;
            FillRooms();
            SetPickedRooms(uiDoc.Selection.GetElementIds().Where(id => _document.GetElement(id) is Room).ToList());
            _status.Text = $"目前文件：{_document.Title}";
        }

        internal void SetPickedRooms(ICollection<ElementId> ids)
        {
            _pickedRoomIds = ids?.Distinct().ToList() ?? new List<ElementId>();
            _usePicked.Enabled = _roomsGrid.Rows.Count > 0;
            _usePicked.Checked = _pickedRoomIds.Count > 0;
            ApplyCheckedRooms(_pickedRoomIds);
            _pickedLabel.Text = _pickedRoomIds.Count > 0
                ? $"已選取 {_pickedRoomIds.Count} 間房間"
                : "尚未選取房間；執行時將處理全部房間";
        }

        internal void SetVisibleForPick(bool visible)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetVisibleForPick), visible);
                return;
            }
            if (visible)
            {
                Show();
                Activate();
            }
            else
            {
                Hide();
            }
        }

        internal void Complete(string text, bool isError = false)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, bool>(Complete), text, isError);
                return;
            }
            SetBusy(false);
            _status.ForeColor = isError ? DrawingColor.Firebrick : DrawingColor.DarkGreen;
            _status.Text = text;
        }

        private void BuildUi()
        {
            var root = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                Padding = new WinForms.Padding(16),
                ColumnCount = 2,
                RowCount = 15
            };
            root.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 150));
            root.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 100));
            root.RowStyles.Clear();
            for (int i = 0; i <= 10; i++)
                root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Percent, 100));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Absolute, 0));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            Controls.Add(root);

            AddRow(root, 0, "牆類型", _wallType);
            AddRow(root, 1, "樓板類型", _floorType);
            AddRow(root, 2, "天花板類型", _ceilingType);
            AddRow(root, 3, "踢腳板類型", _skirtingType);
            AddRow(root, 4, "邊界模式", _boundary);
            _boundary.Items.AddRange(new object[] { "內裝修面", "中心線", "外裝修面" });

            ConfigureNumeric(_wallHeight, 1000, 10000);
            ConfigureNumeric(_ceilingHeight, 1000, 10000);
            ConfigureNumeric(_skirtingHeight, 0, 1000);
            AddRow(root, 5, "牆高 (mm)", _wallHeight);
            AddRow(root, 6, "天花高度 (mm)", _ceilingHeight);
            AddRow(root, 7, "踢腳板高度 (mm)", _skirtingHeight);

            var options = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Fill, AutoSize = true };
            _generate.Text = "生成幾何";
            _update.Text = "更新參數";
            _join.Text = "自動接合牆";
            options.Controls.AddRange(new WinForms.Control[] { _generate, _update, _join });
            AddRow(root, 8, "處理內容", options);

            var selection = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Fill, AutoSize = true };
            _pickButton.Text = "從模型選房";
            _pickButton.AutoSize = true;
            _pickButton.Click += (_, __) =>
            {
                SetBusy(true);
                _request(RoomFinishRequest.PickRooms, null);
            };
            _reloadRoomsButton.Text = "回查模型";
            _reloadRoomsButton.AutoSize = true;
            _reloadRoomsButton.Click += (_, __) => ReloadRoomsFromDocument();
            _selectAllButton.Text = "全選";
            _selectAllButton.AutoSize = true;
            _selectAllButton.Click += (_, __) => SetAllRoomChecks(true);
            _clearSelectionButton.Text = "取消全選";
            _clearSelectionButton.AutoSize = true;
            _clearSelectionButton.Click += (_, __) => SetAllRoomChecks(false);
            _usePicked.Text = "只處理清單勾選房間";
            _usePicked.AutoSize = true;
            selection.Controls.AddRange(new WinForms.Control[] { _pickButton, _reloadRoomsButton, _selectAllButton, _clearSelectionButton, _usePicked });
            AddRow(root, 9, "處理範圍", selection);

            _pickedLabel.AutoSize = true;
            _pickedLabel.ForeColor = DrawingColor.DimGray;
            root.Controls.Add(_pickedLabel, 1, 10);

            ConfigureRoomsGrid();
            root.Controls.Add(_roomsGrid, 0, 11);
            root.SetColumnSpan(_roomsGrid, 2);
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Percent, 100));

            _status.AutoSize = true;
            _status.MaximumSize = new Size(900, 70);
            _status.ForeColor = DrawingColor.DimGray;
            root.Controls.Add(_status, 0, 12);
            root.SetColumnSpan(_status, 2);

            var buttons = new WinForms.FlowLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                FlowDirection = WinForms.FlowDirection.RightToLeft,
                AutoSize = true
            };
            var close = new WinForms.Button { Text = "關閉", Width = 90, Height = 34 };
            close.Click += (_, __) => Close();
            _exportButton.Text = "匯出表單";
            _exportButton.Width = 110;
            _exportButton.Height = 34;
            _exportButton.Click += (_, __) => ExportRoomSettings();
            _updateOnlyButton.Text = "僅更新參數";
            _updateOnlyButton.Width = 120;
            _updateOnlyButton.Height = 34;
            _updateOnlyButton.Click += (_, __) => RunWithOptions(false, true, false);
            _generateButton.Text = "產出裝修面";
            _generateButton.Width = 120;
            _generateButton.Height = 34;
            _generateButton.Click += (_, __) => RunWithOptions(true, true, _join.Checked);
            _runButton.Text = "套用並更新";
            _runButton.Width = 120;
            _runButton.Height = 34;
            _runButton.Click += (_, __) => RunWithOptions(_generate.Checked, _update.Checked, _join.Checked);
            buttons.Controls.AddRange(new WinForms.Control[] { close, _runButton, _generateButton, _updateOnlyButton, _exportButton });
            root.Controls.Add(buttons, 0, 14);
            root.SetColumnSpan(buttons, 2);
        }

        private void RunWithOptions(bool generateGeometry, bool updateValues, bool autoJoin)
        {
            if (!generateGeometry && !updateValues && !autoJoin)
            {
                Complete("請至少選擇一項處理內容。", true);
                return;
            }

            var settings = FinishSettings.LoadFromFile() ?? new FinishSettings();
            settings.GenerateGeometry = generateGeometry;
            settings.UpdateValues = updateValues;
            settings.SetValuesForGeometry = updateValues;
            settings.SetValuesForRooms = updateValues;
            settings.AutoJoinWalls = autoJoin;
            settings.SkipDoorsForSkirting = true;
            settings.SkipWindowsForSkirting = true;
            settings.SkipOpeningsForWalls = true;
            settings.SelectedWallTypeId = SelectedId(_wallType);
            settings.SelectedFloorTypeId = SelectedId(_floorType);
            settings.SelectedCeilingTypeId = SelectedId(_ceilingType);
            settings.SelectedSkirtingTypeId = SelectedId(_skirtingType);
            settings.WallHeightMm = (double)_wallHeight.Value;
            settings.CeilingHeightMm = (double)_ceilingHeight.Value;
            settings.SkirtingHeightMm = (double)_skirtingHeight.Value;
            settings.WallOffsetMm = Math.Max(0, settings.WallHeightMm - settings.CeilingHeightMm);
            settings.BoundaryMode = _boundary.SelectedIndex == 1
                ? FloorBoundaryMode.Centerline
                : _boundary.SelectedIndex == 2 ? FloorBoundaryMode.OuterFinish : FloorBoundaryMode.InnerFinish;
            settings.TargetRoomIds = _usePicked.Checked ? GetCheckedRoomIds() : new List<ElementId>();
            if (_usePicked.Checked && settings.TargetRoomIds.Count == 0)
            {
                Complete("已啟用只處理勾選房間，但目前沒有勾選任何房間。", true);
                return;
            }
            settings.RoomOverrides = new List<RoomFinishOverride>();
            try { settings.SaveToFile(); } catch { }

            SetBusy(true);
            _request(RoomFinishRequest.Run, settings);
        }

        private void SetBusy(bool busy)
        {
            _pickButton.Enabled = !busy;
            _reloadRoomsButton.Enabled = !busy;
            _selectAllButton.Enabled = !busy;
            _clearSelectionButton.Enabled = !busy;
            _exportButton.Enabled = !busy;
            _updateOnlyButton.Enabled = !busy;
            _generateButton.Enabled = !busy;
            _runButton.Enabled = !busy;
            if (busy)
            {
                _status.ForeColor = DrawingColor.DimGray;
                _status.Text = "等待 Revit 執行...";
            }
        }

        private void ConfigureRoomsGrid()
        {
            _roomsGrid.Dock = WinForms.DockStyle.Fill;
            _roomsGrid.AllowUserToAddRows = false;
            _roomsGrid.AllowUserToDeleteRows = false;
            _roomsGrid.AllowUserToResizeRows = false;
            _roomsGrid.MultiSelect = true;
            _roomsGrid.SelectionMode = WinForms.DataGridViewSelectionMode.FullRowSelect;
            _roomsGrid.RowHeadersVisible = false;
            _roomsGrid.AutoGenerateColumns = false;
            _roomsGrid.Columns.Clear();
            _roomsGrid.Columns.Add(new WinForms.DataGridViewCheckBoxColumn { Name = "Checked", HeaderText = "", Width = 36 });
            _roomsGrid.Columns.Add(new WinForms.DataGridViewTextBoxColumn { Name = "Number", HeaderText = "房間編號", Width = 110, ReadOnly = true });
            _roomsGrid.Columns.Add(new WinForms.DataGridViewTextBoxColumn { Name = "Name", HeaderText = "房間名稱", Width = 220, ReadOnly = true });
            _roomsGrid.Columns.Add(new WinForms.DataGridViewTextBoxColumn { Name = "Level", HeaderText = "樓層", Width = 90, ReadOnly = true });
            _roomsGrid.Columns.Add(new WinForms.DataGridViewTextBoxColumn { Name = "Area", HeaderText = "面積(m²)", Width = 90, ReadOnly = true });
            _roomsGrid.Columns.Add(new WinForms.DataGridViewTextBoxColumn { Name = "Status", HeaderText = "狀態", AutoSizeMode = WinForms.DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
            _roomsGrid.CurrentCellDirtyStateChanged += (_, __) =>
            {
                if (_roomsGrid.IsCurrentCellDirty)
                    _roomsGrid.CommitEdit(WinForms.DataGridViewDataErrorContexts.Commit);
            };
            _roomsGrid.CellValueChanged += (_, e) =>
            {
                if (e.RowIndex < 0 || _roomsGrid.Columns[e.ColumnIndex].Name != "Checked")
                    return;

                int count = GetCheckedRoomIds().Count;
                _usePicked.Enabled = _roomsGrid.Rows.Count > 0;
                _usePicked.Checked = count > 0;
                _pickedLabel.Text = count > 0
                    ? $"已勾選 {count} 間房間"
                    : "未勾選房間；執行時將處理全部房間";
            };
        }

        private void FillRooms()
        {
            _roomsGrid.Rows.Clear();
            if (_document == null)
                return;

            List<Room> rooms = new FilteredElementCollector(_document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .OrderBy(r => r.Level?.Name ?? string.Empty)
                .ThenBy(r => r.Number)
                .ThenBy(r => r.Name)
                .ToList();

            foreach (Room room in rooms)
            {
                int rowIndex = _roomsGrid.Rows.Add(
                    false,
                    room.Number,
                    room.Name,
                    room.Level?.Name ?? string.Empty,
                    (room.Area * 0.09290304).ToString("0.##"),
                    room.Area > 0 ? "可處理" : "未放置或面積為 0");
                _roomsGrid.Rows[rowIndex].Tag = room.Id;
            }

            _pickedLabel.Text = rooms.Count > 0
                ? $"房間清單：{rooms.Count} 間；未勾選時將處理全部房間"
                : "房間清單：0 間";
        }

        private void ReloadRoomsFromDocument()
        {
            List<ElementId> checkedIds = GetCheckedRoomIds();
            FillRooms();
            ApplyCheckedRooms(checkedIds);
            _usePicked.Enabled = checkedIds.Count > 0;
            _usePicked.Checked = checkedIds.Count > 0;
            _pickedLabel.Text = checkedIds.Count > 0
                ? $"已保留勾選 {checkedIds.Count} 間房間"
                : "已重新讀取房間清單；未勾選時將處理全部房間";
        }

        private void ApplyCheckedRooms(ICollection<ElementId> ids)
        {
            HashSet<long> targets = new HashSet<long>((ids ?? Array.Empty<ElementId>()).Select(x => x.Value));
            foreach (WinForms.DataGridViewRow row in _roomsGrid.Rows)
            {
                if (row.Tag is ElementId id)
                    row.Cells["Checked"].Value = targets.Contains(id.Value);
            }
        }

        private void SetAllRoomChecks(bool isChecked)
        {
            foreach (WinForms.DataGridViewRow row in _roomsGrid.Rows)
                row.Cells["Checked"].Value = isChecked;
            _usePicked.Checked = isChecked;
            _usePicked.Enabled = _roomsGrid.Rows.Count > 0;
            _pickedLabel.Text = isChecked
                ? $"已勾選 {_roomsGrid.Rows.Count} 間房間"
                : "未勾選房間；執行時將處理全部房間";
        }

        private List<ElementId> GetCheckedRoomIds()
        {
            _roomsGrid.EndEdit();
            var ids = new List<ElementId>();
            foreach (WinForms.DataGridViewRow row in _roomsGrid.Rows)
            {
                bool selected = row.Cells["Checked"].Value is bool value && value;
                if (selected && row.Tag is ElementId id)
                    ids.Add(id);
            }
            return ids.Distinct().ToList();
        }

        private List<Room> GetRoomsForExport()
        {
            HashSet<long> checkedIds = new HashSet<long>(GetCheckedRoomIds().Select(x => x.Value));
            bool exportCheckedOnly = checkedIds.Count > 0;
            var rooms = new List<Room>();
            foreach (WinForms.DataGridViewRow row in _roomsGrid.Rows)
            {
                if (!(row.Tag is ElementId id))
                    continue;
                if (exportCheckedOnly && !checkedIds.Contains(id.Value))
                    continue;
                if (_document?.GetElement(id) is Room room)
                    rooms.Add(room);
            }
            return rooms;
        }

        private void ExportRoomSettings()
        {
            try
            {
                var rooms = GetRoomsForExport();
                if (rooms.Count == 0)
                {
                    Complete("目前沒有可匯出的房間。", true);
                    return;
                }

                using (var dlg = new WinForms.SaveFileDialog())
                {
                    dlg.Title = "匯出房間裝修設定表";
                    dlg.Filter = "Excel Workbook (*.xlsx)|*.xlsx";
                    dlg.FileName = $"RoomFinishings_2026_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                    if (dlg.ShowDialog(this) != WinForms.DialogResult.OK)
                        return;

                    ExportRoomSettingsToXlsx(dlg.FileName, rooms);
                    Complete($"已匯出 {rooms.Count} 間房間裝修設定：{dlg.FileName}");
                    WinForms.MessageBox.Show(this, "匯出完成。", "房間裝修", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Complete($"匯出失敗：{ex.Message}", true);
                WinForms.MessageBox.Show(this, $"匯出失敗：{ex.Message}", "房間裝修", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            }
        }

        private void ExportRoomSettingsToXlsx(string filePath, IList<Room> rooms)
        {
            using (var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateExportStylesheet();
                stylesPart.Stylesheet.Save();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                string wallName = SelectedName(_wallType);
                string floorName = SelectedName(_floorType);
                string ceilingName = SelectedName(_ceilingType);
                string skirtingName = SelectedName(_skirtingType);
                string boundary = _boundary.SelectedItem?.ToString() ?? string.Empty;

                var modelFinishIndex = BuildModelFinishDataIndex(_document);
                var quantities = rooms
                    .Select(room =>
                    {
                        modelFinishIndex.TryGetValue(RevitCompat.GetElementIdValue(room.Id), out var modelData);
                        return BuildRoomQuantity(room, wallName, floorName, ceilingName, skirtingName, boundary, modelData);
                    })
                    .OrderBy(x => x.Level)
                    .ThenBy(x => x.Number)
                    .ThenBy(x => x.Name)
                    .ToList();

                uint sheetId = 1;
                BuildSettingsDetailSheet(workbookPart, sheets, ref sheetId, quantities);
                BuildSettingsSummarySheet(workbookPart, sheets, ref sheetId, quantities);
                BuildPracticalScheduleSheet(workbookPart, sheets, ref sheetId, quantities);

                workbookPart.Workbook.Save();
            }
        }

        private void BuildSettingsDetailSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, IList<RoomQuantityRow> rows)
        {
            var headers = new[]
            {
                "房間ID", "房間號碼", "房間名稱", "樓層", "已選取",
                "牆面類型ID", "牆面材料", "粉刷高度(mm)",
                "地坪類型ID", "地坪材料",
                "天花板類型ID", "天花板材料", "天花板高度(mm)",
                "踢腳板類型ID", "踢腳板材料", "踢腳板高度(mm)", "踢腳板長度(m)",
                "房間面積(m²)", "牆面積(m²)", "地坪面積(m²)", "天花面積(m²)", "狀態"
            };

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var columns = new Columns(
                new Column { Min = 1, Max = 1, Width = 12, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 12, CustomWidth = true },
                new Column { Min = 3, Max = 3, Width = 22, CustomWidth = true },
                new Column { Min = 4, Max = 4, Width = 12, CustomWidth = true },
                new Column { Min = 5, Max = 5, Width = 10, CustomWidth = true },
                new Column { Min = 6, Max = 6, Width = 12, CustomWidth = true },
                new Column { Min = 7, Max = 7, Width = 28, CustomWidth = true },
                new Column { Min = 8, Max = 17, Width = 14, CustomWidth = true },
                new Column { Min = 18, Max = 21, Width = 14, CustomWidth = true },
                new Column { Min = 22, Max = 22, Width = 26, CustomWidth = true }
            );
            var sheetView = new SheetView { WorkbookViewId = 0U };
            sheetView.Append(new Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
            var worksheet = new Worksheet(new SheetViews(sheetView), columns, sheetData);
            worksheetPart.Worksheet = worksheet;
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId++, Name = "明細表" });

            var headerRow = new Row();
            foreach (var header in headers)
                headerRow.Append(CreateTextCell(header, 2));
            sheetData.Append(headerRow);

            var exportedDataRowCount = 0;
            foreach (var row in rows)
            {
                var wallEntries = GetWallTypeAreaEntries(row);
                for (int i = 0; i < wallEntries.Count; i++)
                {
                    var wallEntry = wallEntries[i];
                    var isFirst = i == 0;
                    var dataRow = new Row();
                    dataRow.Append(CreateTextCell(row.RoomId.ToString(CultureInfo.InvariantCulture), 1));
                    dataRow.Append(CreateTextCell(row.Number, 1));
                    dataRow.Append(CreateTextCell(row.Name, 1));
                    dataRow.Append(CreateTextCell(row.Level, 1));
                    dataRow.Append(CreateNumberCell(1, 1));
                    dataRow.Append(wallEntry.TypeId > 0 ? CreateNumberCell(wallEntry.TypeId, 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(wallEntry.TypeName, 1));
                    dataRow.Append(CreateNumberCell(row.WallHeightMm, 1));
                    dataRow.Append(isFirst && row.FloorTypeId > 0 ? CreateNumberCell(row.FloorTypeId, 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(isFirst ? row.FloorMaterial : "", 1));
                    dataRow.Append(isFirst && row.CeilingTypeId > 0 ? CreateNumberCell(row.CeilingTypeId, 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(isFirst ? row.CeilingMaterial : "", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(row.CeilingHeightMm, 1) : CreateTextCell("", 1));
                    dataRow.Append(isFirst && row.SkirtingTypeId > 0 ? CreateNumberCell(row.SkirtingTypeId, 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(isFirst ? row.SkirtingMaterial : "", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(row.SkirtingHeightMm, 1) : CreateTextCell("", 1));
                    dataRow.Append(isFirst && row.SkirtingLengthM > 0 ? CreateNumberCell(Math.Round(row.SkirtingLengthM, 1), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(Math.Round(row.RoomAreaM2, 2), 1) : CreateTextCell("", 1));
                    dataRow.Append(wallEntry.AreaM2 > 0 ? CreateNumberCell(Math.Round(wallEntry.AreaM2, 2), 1) : CreateTextCell("-", 1));
                    dataRow.Append(isFirst && row.FloorAreaM2 > 0 ? CreateNumberCell(Math.Round(row.FloorAreaM2, 2), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                    dataRow.Append(isFirst && row.CeilingAreaM2 > 0 ? CreateNumberCell(Math.Round(row.CeilingAreaM2, 2), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                    dataRow.Append(CreateTextCell(row.ModelStatus, 1));
                    sheetData.Append(dataRow);
                    exportedDataRowCount++;
                }
            }

            worksheet.Append(new AutoFilter { Reference = $"A1:V{Math.Max(exportedDataRowCount + 1, 1)}" });
        }

        private void BuildSettingsSummarySheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, IList<RoomQuantityRow> rows)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var mergeCells = new MergeCells();
            var levels = rows.Select(x => x.Level ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetExportLevelSortKey)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!levels.Any()) levels.Add("未分層");

            var sheetView = new SheetView { WorkbookViewId = 0U };
            sheetView.Append(new Pane { VerticalSplit = 2D, TopLeftCell = "A3", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 48, CustomWidth = true });
            for (uint i = 2; i <= 12; i++)
                columns.Append(new Column { Min = i, Max = i, Width = 16, CustomWidth = true });
            var worksheet = new Worksheet(new SheetViews(sheetView), columns, sheetData, mergeCells);
            worksheetPart.Worksheet = worksheet;
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId++, Name = "統計表" });

            uint rowIndex = 1;
            var lastColumnName = GetExportColumnName(1 + levels.Count + 1);
            var titleRow = new Row { RowIndex = rowIndex };
            titleRow.Append(CreateTextCell("房間裝修設定總表", 7));
            sheetData.Append(titleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex++;
            var subtitleRow = new Row { RowIndex = rowIndex };
            subtitleRow.Append(CreateTextCell($"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}    房間數：{rows.Count}", 2));
            sheetData.Append(subtitleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex += 2;

            var sections = new List<(string Title, string Kind, uint Style)>
            {
                ("牆面材料明細", "Wall", 3),
                ("地坪材料明細", "Floor", 4),
                ("天花材料明細", "Ceiling", 5),
                ("手動裝修面明細", "Manual", 6)
            };
            foreach (var section in sections)
            {
                BuildSettingsSummarySection(sheetData, mergeCells, ref rowIndex, levels, section.Title, section.Kind, section.Style, lastColumnName, rows);
                rowIndex++;
            }
        }

        private void BuildSettingsSummarySection(SheetData sheetData, MergeCells mergeCells, ref uint rowIndex, List<string> levels,
            string title, string kind, uint style, string lastColumnName, IList<RoomQuantityRow> rows)
        {
            var titleRow = new Row { RowIndex = rowIndex };
            titleRow.Append(CreateTextCell(title, style));
            sheetData.Append(titleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex++;

            var headerRow = new Row { RowIndex = rowIndex };
            headerRow.Append(CreateTextCell("樓層", 2));
            headerRow.Append(CreateTextCell("項目", 2));
            headerRow.Append(CreateTextCell(kind == "Skirting" ? "長度(m)" : "面積(㎡)", 2));
            sheetData.Append(headerRow);
            rowIndex++;

            var grandTotal = 0.0;
            foreach (var level in levels)
            {
                var levelRows = rows.Where(x => string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase)).ToList();
                var groups = GetSummaryGroups(levelRows, kind).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
                if (!groups.Any()) continue;

                uint levelStartRow = rowIndex;
                var levelTotal = 0.0;
                foreach (var group in groups)
                {
                    var dataRow = new Row { RowIndex = rowIndex };
                    dataRow.Append(CreateTextCell(NormalizeExportLevelLabel(level), 1));
                    dataRow.Append(CreateTextCell(group.Key, 1));
                    dataRow.Append(CreateNumberCell(Math.Round(group.Value, 2), 1));
                    sheetData.Append(dataRow);
                    rowIndex++;
                    levelTotal += group.Value;
                }
                if (rowIndex - levelStartRow > 1)
                    mergeCells.Append(new MergeCell { Reference = new StringValue($"A{levelStartRow}:A{rowIndex - 1}") });

                var subtotalRow = new Row { RowIndex = rowIndex };
                subtotalRow.Append(CreateTextCell(string.Empty, 2));
                subtotalRow.Append(CreateTextCell($"{NormalizeExportLevelLabel(level)} 小計", 2));
                subtotalRow.Append(CreateNumberCell(Math.Round(levelTotal, 2), 2));
                sheetData.Append(subtotalRow);
                rowIndex++;
                grandTotal += levelTotal;
            }

            var totalRow = new Row { RowIndex = rowIndex };
            totalRow.Append(CreateTextCell(string.Empty, 2));
            totalRow.Append(CreateTextCell("總計", 2));
            totalRow.Append(CreateNumberCell(Math.Round(grandTotal, 2), 2));
            sheetData.Append(totalRow);
            rowIndex++;
        }

        private void BuildPracticalScheduleSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, IList<RoomQuantityRow> rows)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var mergeCells = new MergeCells();
            var sheetView = new SheetView { WorkbookViewId = 0U };
            sheetView.Append(new Pane { VerticalSplit = 3D, TopLeftCell = "A4", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
            var columns = new Columns(
                new Column { Min = 1, Max = 1, Width = 12, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 12, CustomWidth = true },
                new Column { Min = 3, Max = 3, Width = 30, CustomWidth = true },
                new Column { Min = 4, Max = 4, Width = 12, CustomWidth = true },
                new Column { Min = 5, Max = 5, Width = 56, CustomWidth = true },
                new Column { Min = 6, Max = 6, Width = 12, CustomWidth = true },
                new Column { Min = 7, Max = 7, Width = 12, CustomWidth = true },
                new Column { Min = 8, Max = 8, Width = 48, CustomWidth = true },
                new Column { Min = 9, Max = 9, Width = 12, CustomWidth = true },
                new Column { Min = 10, Max = 10, Width = 12, CustomWidth = true },
                new Column { Min = 11, Max = 11, Width = 48, CustomWidth = true },
                new Column { Min = 12, Max = 12, Width = 12, CustomWidth = true },
                new Column { Min = 13, Max = 13, Width = 12, CustomWidth = true },
                new Column { Min = 14, Max = 14, Width = 36, CustomWidth = true },
                new Column { Min = 15, Max = 15, Width = 14, CustomWidth = true },
                new Column { Min = 16, Max = 16, Width = 16, CustomWidth = true },
                new Column { Min = 17, Max = 17, Width = 22, CustomWidth = true }
            );
            var autoFilter = new AutoFilter { Reference = "A4:Q4" };
            var worksheet = new Worksheet(new SheetViews(sheetView), columns, sheetData, autoFilter, mergeCells);
            worksheetPart.Worksheet = worksheet;
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId++, Name = "施工明細表" });

            uint rowIndex = 1;
            var titleRow = new Row { RowIndex = rowIndex };
            titleRow.Append(CreateTextCell("各樓層房間天地牆材料明細表", 7));
            sheetData.Append(titleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:Q{rowIndex}") });
            rowIndex++;

            double totalRoomArea = rows.Sum(r => r.RoomAreaM2);
            double totalWallArea = rows.Sum(r => r.WallAreaM2);
            double totalFloorArea = rows.Sum(r => r.FloorAreaM2);
            double totalCeilingArea = rows.Sum(r => r.CeilingAreaM2);
            double totalSkirtingLength = rows.Sum(r => r.SkirtingLengthM);
            double totalManualArea = rows.Sum(r => r.ManualFaceAreaM2);

            var subtitleRow = new Row { RowIndex = rowIndex };
            subtitleRow.Append(CreateTextCell($"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}    房間數：{rows.Count}    房間面積：{totalRoomArea:F2} ㎡", 2));
            sheetData.Append(subtitleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:Q{rowIndex}") });
            rowIndex++;

            var summaryRow = new Row { RowIndex = rowIndex };
            summaryRow.Append(CreateTextCell($"牆面積：{totalWallArea:F2} ㎡ | 地坪面積：{totalFloorArea:F2} ㎡ | 天花面積：{totalCeilingArea:F2} ㎡ | 踢腳板總長：{totalSkirtingLength:F1} m | 手動面：{totalManualArea:F2} ㎡", 2));
            sheetData.Append(summaryRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:Q{rowIndex}") });
            rowIndex++;

            var headerRow = new Row { RowIndex = rowIndex };
            foreach (var header in new[]
            {
                "樓層", "房間編號", "房間名稱", "牆面代號", "牆面材料", "牆面積 (㎡)",
                "地坪代號", "地坪材料", "地面積 (㎡)", "天花代號", "天花材料", "天面積 (㎡)",
                "踢腳板代號", "踢腳板材料", "踢腳板長 (m)", "手動面積 (㎡)", "模型狀態"
            })
                headerRow.Append(CreateTextCell(header, 2));
            sheetData.Append(headerRow);
            rowIndex++;

            foreach (var levelGroup in rows
                .OrderBy(r => GetExportLevelSortKey(r.Level))
                .ThenBy(r => r.Level, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Number, StringComparer.OrdinalIgnoreCase)
                .GroupBy(r => r.Level ?? "未分層"))
            {
                var levelTitleRow = new Row { RowIndex = rowIndex };
                levelTitleRow.Append(CreateTextCell($"{NormalizeExportLevelLabel(levelGroup.Key)} 房間明細", 3));
                sheetData.Append(levelTitleRow);
                mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:Q{rowIndex}") });
                rowIndex++;

                foreach (var roomRow in levelGroup)
                {
                    var wallEntries = GetWallTypeAreaEntries(roomRow);
                    for (int i = 0; i < wallEntries.Count; i++)
                    {
                        var wallEntry = wallEntries[i];
                        var isFirst = i == 0;
                        var dataRow = new Row { RowIndex = rowIndex, Height = 32D, CustomHeight = true };
                        dataRow.Append(CreateTextCell(roomRow.Level ?? "未分層", 1));
                        dataRow.Append(CreateTextCell(roomRow.Number, 1));
                        dataRow.Append(CreateTextCell(roomRow.Name, 8));
                        dataRow.Append(CreateTextCell(SplitTypeCode(wallEntry.TypeName), 9));
                        dataRow.Append(CreateTextCell(SplitTypeName(wallEntry.TypeName), 8));
                        dataRow.Append(wallEntry.AreaM2 > 0 ? CreateNumberCell(Math.Round(wallEntry.AreaM2, 2), 1) : CreateTextCell("-", 1));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeCode(roomRow.FloorMaterial) : "", 9));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeName(roomRow.FloorMaterial) : "", 8));
                        dataRow.Append(isFirst && roomRow.FloorAreaM2 > 0 ? CreateNumberCell(Math.Round(roomRow.FloorAreaM2, 2), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeCode(roomRow.CeilingMaterial) : "", 9));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeName(roomRow.CeilingMaterial) : "", 8));
                        dataRow.Append(isFirst && roomRow.CeilingAreaM2 > 0 ? CreateNumberCell(Math.Round(roomRow.CeilingAreaM2, 2), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeCode(roomRow.SkirtingMaterial) : "", 9));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeName(roomRow.SkirtingMaterial) : "", 8));
                        dataRow.Append(isFirst && roomRow.SkirtingLengthM > 0 ? CreateNumberCell(Math.Round(roomRow.SkirtingLengthM, 1), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                        dataRow.Append(isFirst && roomRow.ManualFaceAreaM2 > 0 ? CreateNumberCell(Math.Round(roomRow.ManualFaceAreaM2, 2), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                        dataRow.Append(CreateTextCell(isFirst ? roomRow.ModelStatus : "", 1));
                        sheetData.Append(dataRow);
                        rowIndex++;
                    }
                }

                rowIndex++;
            }

            autoFilter.Reference = $"A4:Q{Math.Max((int)rowIndex - 1, 4)}";
        }

        private void BuildSettingsSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, IList<RoomQuantityRow> rows)
        {
            var sheetData = AddWorksheet(workbookPart, sheets, ref sheetId, "房間設定明細");
            AppendTextRow(sheetData, "HB_BIM 房間裝修設定表", "", "", "", "", "", "", "", "", "", "", "");
            AppendTextRow(sheetData, $"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}", $"文件：{_document?.Title ?? ""}", $"房間數：{rows.Count}", "", "", "", "", "", "", "", "", "");
            AppendTextRow(sheetData,
                    "樓層", "房間編號", "房間名稱", "面積(m²)", "牆面材料", "牆高(mm)",
                    "樓板材料", "天花材料", "天花高度(mm)", "踢腳板材料", "踢腳高度(mm)", "邊界模式");

            foreach (var row in rows)
            {
                AppendTextRow(sheetData,
                    row.Level,
                    row.Number,
                    row.Name,
                    row.RoomAreaM2.ToString("0.##"),
                    row.WallMaterial,
                    row.WallHeightMm.ToString("0"),
                    row.FloorMaterial,
                    row.CeilingMaterial,
                    row.CeilingHeightMm.ToString("0"),
                    row.SkirtingMaterial,
                    row.SkirtingHeightMm.ToString("0"),
                    row.BoundaryMode);
            }
        }

        private void BuildQuantityDetailSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, IList<RoomQuantityRow> rows)
        {
            var sheetData = AddWorksheet(workbookPart, sheets, ref sheetId, "數量明細");
            AppendTextRow(sheetData, "HB_BIM 房間裝修數量明細表", "", "", "", "", "", "", "", "", "", "", "");
            AppendTextRow(sheetData, $"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}", "數量來源：模型實際粉刷元素；未找到實際粉刷元素時數量列為 0 並標示缺漏，不以估算量混充。", "", "", "", "", "", "", "", "", "", "");
            AppendTextRow(sheetData,
                "樓層", "房間編號", "房間名稱", "房間面積(m²)", "房間周長(m)",
                "牆面代號", "牆面材料", "牆面積(m²)",
                "地坪代號", "地坪材料", "地坪面積(m²)",
                "天花代號", "天花材料", "天花面積(m²)",
                "踢腳代號", "踢腳材料", "踢腳長度(m)",
                "手動面材料", "手動面面積(m²)", "模型狀態");

            foreach (var row in rows)
            {
                AppendTextRow(sheetData,
                    row.Level,
                    row.Number,
                    row.Name,
                    row.RoomAreaM2.ToString("0.##"),
                    row.PerimeterM.ToString("0.##"),
                    ExtractMaterialCode(row.WallMaterial),
                    row.WallMaterial,
                    row.WallAreaM2.ToString("0.##"),
                    ExtractMaterialCode(row.FloorMaterial),
                    row.FloorMaterial,
                    row.FloorAreaM2.ToString("0.##"),
                    ExtractMaterialCode(row.CeilingMaterial),
                    row.CeilingMaterial,
                    row.CeilingAreaM2.ToString("0.##"),
                    ExtractMaterialCode(row.SkirtingMaterial),
                    row.SkirtingMaterial,
                    row.SkirtingLengthM.ToString("0.##"),
                    row.ManualFaceMaterial,
                    row.ManualFaceAreaM2.ToString("0.##"),
                    row.ModelStatus);
            }
        }

        private void BuildQuantitySummarySheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, IList<RoomQuantityRow> rows)
        {
            var sheetData = AddWorksheet(workbookPart, sheets, ref sheetId, "材料彙總");
            AppendTextRow(sheetData, "HB_BIM 房間裝修材料彙總表", "", "", "", "", "");
            AppendTextRow(sheetData, $"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}", $"房間數：{rows.Count}", "數量來源：模型實際粉刷元素", "", "", "");
            AppendTextRow(sheetData, "樓層", "項目", "代號", "材料", "數量", "單位");

            var entries = rows.SelectMany(row => new[]
                {
                    new SummaryEntry(row.Level, "牆面", row.WallMaterial, row.WallAreaM2, "m²"),
                    new SummaryEntry(row.Level, "地坪", row.FloorMaterial, row.FloorAreaM2, "m²"),
                    new SummaryEntry(row.Level, "天花", row.CeilingMaterial, row.CeilingAreaM2, "m²"),
                    new SummaryEntry(row.Level, "踢腳", row.SkirtingMaterial, row.SkirtingLengthM, "m"),
                    new SummaryEntry(row.Level, "手動面", row.ManualFaceMaterial, row.ManualFaceAreaM2, "m²")
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Material) && x.Quantity > 0)
                .GroupBy(x => new { x.Level, x.Kind, x.Material, x.Unit })
                .OrderBy(g => g.Key.Level)
                .ThenBy(g => g.Key.Kind)
                .ThenBy(g => g.Key.Material);

            foreach (var group in entries)
            {
                AppendTextRow(sheetData,
                    group.Key.Level,
                    group.Key.Kind,
                    ExtractMaterialCode(group.Key.Material),
                    group.Key.Material,
                    group.Sum(x => x.Quantity).ToString("0.##"),
                    group.Key.Unit);
            }
        }

        private SheetData AddWorksheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, string name)
        {
            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            worksheetPart.Worksheet.Save();

            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = name
            });

            return sheetData;
        }

        private RoomQuantityRow BuildRoomQuantity(
            Room room,
            string wallMaterial,
            string floorMaterial,
            string ceilingMaterial,
            string skirtingMaterial,
            string boundaryMode,
            ModelFinishData modelData)
        {
            double roomAreaM2 = InternalAreaToSquareMeters(room.Area);
            double perimeterM = InternalLengthToMeters(room.get_Parameter(BuiltInParameter.ROOM_PERIMETER)?.AsDouble() ?? 0);
            double wallAreaM2 = modelData?.WallAreaM2 ?? 0;
            double floorAreaM2 = modelData?.FloorAreaM2 ?? 0;
            double ceilingAreaM2 = modelData?.CeilingAreaM2 ?? 0;
            double skirtingLengthM = modelData?.SkirtingLengthM ?? 0;
            double manualFaceAreaM2 = modelData?.ManualFaceAreaM2 ?? 0;
            string manualFaceMaterial = modelData?.ManualFaceMaterial ?? string.Empty;
            string modelStatus = modelData != null && modelData.HasAnyQuantity
                ? "已讀取模型實際粉刷量"
                : "未找到實際粉刷面／數量為 0";
            long wallTypeId = RevitCompat.GetElementIdValue(SelectedId(_wallType));
            long floorTypeId = RevitCompat.GetElementIdValue(SelectedId(_floorType));
            long ceilingTypeId = RevitCompat.GetElementIdValue(SelectedId(_ceilingType));
            long skirtingTypeId = RevitCompat.GetElementIdValue(SelectedId(_skirtingType));

            return new RoomQuantityRow
            {
                RoomId = RevitCompat.GetElementIdValue(room.Id),
                Level = room.Level?.Name ?? string.Empty,
                Number = room.Number ?? string.Empty,
                Name = room.Name ?? string.Empty,
                RoomAreaM2 = roomAreaM2,
                PerimeterM = perimeterM,
                WallTypeId = modelData?.WallTypeIds.FirstOrDefault() > 0 ? modelData.WallTypeIds.First() : wallTypeId,
                WallMaterial = wallMaterial,
                WallHeightMm = (double)_wallHeight.Value,
                WallAreaM2 = wallAreaM2,
                WallAreaByTypeM2 = modelData?.WallAreaByTypeM2 != null && modelData.WallAreaByTypeM2.Count > 0
                    ? new Dictionary<long, double>(modelData.WallAreaByTypeM2)
                    : new Dictionary<long, double>(),
                FloorTypeId = modelData?.FloorTypeIds.FirstOrDefault() > 0 ? modelData.FloorTypeIds.First() : floorTypeId,
                FloorMaterial = floorMaterial,
                FloorAreaM2 = floorAreaM2,
                CeilingTypeId = modelData?.CeilingTypeIds.FirstOrDefault() > 0 ? modelData.CeilingTypeIds.First() : ceilingTypeId,
                CeilingMaterial = ceilingMaterial,
                CeilingHeightMm = (double)_ceilingHeight.Value,
                CeilingAreaM2 = ceilingAreaM2,
                SkirtingTypeId = modelData?.SkirtingTypeIds.FirstOrDefault() > 0 ? modelData.SkirtingTypeIds.First() : skirtingTypeId,
                SkirtingMaterial = skirtingMaterial,
                SkirtingHeightMm = (double)_skirtingHeight.Value,
                SkirtingLengthM = skirtingLengthM,
                ManualFaceMaterial = manualFaceMaterial,
                ManualFaceAreaM2 = manualFaceAreaM2,
                ManualFaceAreaByMaterialM2 = modelData?.ManualFaceAreaByMaterialM2 != null && modelData.ManualFaceAreaByMaterialM2.Count > 0
                    ? new Dictionary<string, double>(modelData.ManualFaceAreaByMaterialM2, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                BoundaryMode = boundaryMode,
                ModelStatus = modelStatus
            };
        }

        private Dictionary<long, ModelFinishData> BuildModelFinishDataIndex(Document doc)
        {
            const double skirtingMaxHeightM = 0.5;
            var index = new Dictionary<long, ModelFinishData>();

            ModelFinishData GetOrCreate(long roomId)
            {
                if (!index.TryGetValue(roomId, out var data))
                {
                    data = new ModelFinishData();
                    index[roomId] = data;
                }
                return data;
            }

            foreach (var elem in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(FinishingElementGuard.IsManagedFinishingElement))
            {
                long roomId = ReadRoomIdFromElement(elem);
                if (roomId <= 0)
                    roomId = FindRoomIdBySpatialQuery(elem);
                if (roomId <= 0)
                    continue;

                var data = GetOrCreate(roomId);
                long categoryId = elem.Category != null ? RevitCompat.GetElementIdValue(elem.Category.Id) : 0;
                long typeId = RevitCompat.GetElementIdValue(elem.GetTypeId());

                if (categoryId == (long)BuiltInCategory.OST_Walls)
                {
                    double heightM = ReadWallHeightM(elem);
                    bool isSkirting = heightM > 0 && heightM <= skirtingMaxHeightM;
                    if (isSkirting)
                    {
                        if (elem.Location is LocationCurve lc)
                            data.SkirtingLengthM += InternalLengthToMeters(lc.Curve.Length);
                        if (typeId > 0) data.SkirtingTypeIds.Add(typeId);
                    }
                    else
                    {
                        double area = ReadElementAreaM2(elem);
                        data.WallAreaM2 += area;
                        if (typeId > 0 && area > 0)
                            data.WallAreaByTypeM2[typeId] = (data.WallAreaByTypeM2.TryGetValue(typeId, out var existing) ? existing : 0) + area;
                        if (heightM > 0) data.WallHeightMm = Math.Max(data.WallHeightMm, heightM * 1000.0);
                        if (typeId > 0) data.WallTypeIds.Add(typeId);
                    }
                }
                else if (categoryId == (long)BuiltInCategory.OST_Floors)
                {
                    double area = ReadElementAreaM2(elem);
                    data.FloorAreaM2 += area;
                    if (typeId > 0 && area > 0)
                        data.FloorAreaByTypeM2[typeId] = (data.FloorAreaByTypeM2.TryGetValue(typeId, out var existing) ? existing : 0) + area;
                    if (typeId > 0) data.FloorTypeIds.Add(typeId);
                }
                else if (categoryId == (long)BuiltInCategory.OST_Ceilings)
                {
                    double area = ReadElementAreaM2(elem);
                    data.CeilingAreaM2 += area;
                    if (typeId > 0 && area > 0)
                        data.CeilingAreaByTypeM2[typeId] = (data.CeilingAreaByTypeM2.TryGetValue(typeId, out var existing) ? existing : 0) + area;
                    if (typeId > 0) data.CeilingTypeIds.Add(typeId);
                }
                else if (categoryId == (long)BuiltInCategory.OST_GenericModel)
                {
                    double area = ReadElementAreaM2(elem);
                    if (IsRoomFinishColumnWallFace(elem, out var wallTypeId))
                    {
                        data.WallAreaM2 += area;
                        if (wallTypeId > 0)
                        {
                            data.WallTypeIds.Add(wallTypeId);
                            data.WallAreaByTypeM2[wallTypeId] = (data.WallAreaByTypeM2.TryGetValue(wallTypeId, out var existing) ? existing : 0) + area;
                        }
                    }
                    else
                    {
                        data.ManualFaceAreaM2 += area;
                        data.ManualFaceMaterial = GetManualFaceMaterialName(elem);
                        data.ManualFaceAreaByMaterialM2[data.ManualFaceMaterial] =
                            (data.ManualFaceAreaByMaterialM2.TryGetValue(data.ManualFaceMaterial, out var existing) ? existing : 0) + area;
                    }
                }
            }

            return index;
        }

        private static long ReadRoomIdFromElement(Element element)
        {
            var p = element.LookupParameter("房間ID(AR_RoomId)")
                 ?? element.LookupParameter("房間ID")
                 ?? element.LookupParameter("AR_RoomId");
            if (p == null) return 0L;
            if (p.StorageType == StorageType.Integer) return p.AsInteger();
            if (p.StorageType == StorageType.String && long.TryParse(p.AsString(), out var parsed)) return parsed;
            return 0L;
        }

        private static long FindRoomIdBySpatialQuery(Element element)
        {
            var matches = new HashSet<long>();
            try
            {
                var doc = element.Document;
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                foreach (var point in GetElementRoomProbePoints(element))
                {
                    foreach (var room in rooms)
                    {
                        if (RoomOverlapGuard.IsPointInRoomSafe(room, point))
                            matches.Add(RevitCompat.GetElementIdValue(room.Id));
                    }
                }
            }
            catch { }

            return matches.Count == 1 ? matches.First() : 0L;
        }

        private static IEnumerable<XYZ> GetElementRoomProbePoints(Element element)
        {
            if (element.Location is LocationPoint lp)
                yield return lp.Point;
            if (element.Location is LocationCurve lc)
                yield return lc.Curve.Evaluate(0.5, true);

            var bb = element.get_BoundingBox(null);
            if (bb == null)
                yield break;

            var center = new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, (bb.Min.Z + bb.Max.Z) / 2.0);
            yield return center;

            double probeOffset = 10.0 / 304.8;
            long categoryId = element.Category != null ? RevitCompat.GetElementIdValue(element.Category.Id) : 0;
            double z = center.Z;
            if (categoryId == (long)BuiltInCategory.OST_Floors) z = bb.Max.Z + probeOffset;
            if (categoryId == (long)BuiltInCategory.OST_Ceilings) z = bb.Min.Z - probeOffset;

            double x1 = bb.Min.X + (bb.Max.X - bb.Min.X) * 0.25;
            double x3 = bb.Min.X + (bb.Max.X - bb.Min.X) * 0.75;
            double y1 = bb.Min.Y + (bb.Max.Y - bb.Min.Y) * 0.25;
            double y3 = bb.Min.Y + (bb.Max.Y - bb.Min.Y) * 0.75;
            yield return new XYZ(x1, y1, z);
            yield return new XYZ(x1, y3, z);
            yield return new XYZ(x3, y1, z);
            yield return new XYZ(x3, y3, z);
        }

        private static double ReadWallHeightM(Element elem)
        {
            var heightParam = elem.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)
                ?? elem.get_Parameter(BuiltInParameter.WALL_ATTR_HEIGHT_PARAM);
            return heightParam != null && heightParam.StorageType == StorageType.Double
                ? InternalLengthToMeters(heightParam.AsDouble())
                : 0;
        }

        private static double ReadElementAreaM2(Element elem)
        {
            var areaParam = elem.LookupParameter("面積")
                ?? elem.LookupParameter("裝修面積")
                ?? elem.LookupParameter("Area");
            if (areaParam != null && areaParam.StorageType == StorageType.Double)
                return InternalAreaToSquareMeters(areaParam.AsDouble());

            var hostArea = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (hostArea != null && hostArea.StorageType == StorageType.Double)
                return InternalAreaToSquareMeters(hostArea.AsDouble());

            return 0;
        }

        private static bool IsRoomFinishColumnWallFace(Element elem, out long wallTypeId)
        {
            wallTypeId = 0;
            if (!(elem is DirectShape ds))
                return false;
            var appData = ds.ApplicationDataId ?? string.Empty;
            if (appData.IndexOf("RoomFinish_ColumnWall", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            const string token = "WallTypeId=";
            var idx = appData.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var start = idx + token.Length;
                var end = appData.IndexOf('|', start);
                var text = end >= 0 ? appData.Substring(start, end - start) : appData.Substring(start);
                long.TryParse(text, out wallTypeId);
            }
            return true;
        }

        private static string GetManualFaceMaterialName(Element elem)
        {
            var materialParam = elem.LookupParameter("裝修材質")
                ?? elem.LookupParameter("材料名稱")
                ?? elem.LookupParameter("材質")
                ?? elem.LookupParameter("Material");
            if (materialParam != null && materialParam.StorageType == StorageType.String)
            {
                var name = materialParam.AsString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            return "手動裝修面";
        }

        private static double InternalAreaToSquareMeters(double squareFeet)
        {
            return squareFeet * 0.09290304;
        }

        private static double InternalLengthToMeters(double feet)
        {
            return feet * 0.3048;
        }

        private static string ExtractMaterialCode(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return string.Empty;
            int dash = materialName.IndexOf('-');
            if (dash > 0)
                return materialName.Substring(0, dash).Trim();
            int space = materialName.IndexOf(' ');
            return space > 0 ? materialName.Substring(0, space).Trim() : materialName.Trim();
        }

        private List<(long TypeId, string TypeName, double AreaM2)> GetWallTypeAreaEntries(RoomQuantityRow row)
        {
            if (row.WallAreaByTypeM2 != null && row.WallAreaByTypeM2.Count > 0)
            {
                var entries = row.WallAreaByTypeM2
                    .Select(kv => (TypeId: kv.Key, TypeName: GetTypeNameById(kv.Key, row.WallMaterial), AreaM2: kv.Value))
                    .Where(x => x.AreaM2 > 0)
                    .OrderBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (entries.Count > 0)
                    return entries;
            }

            return new List<(long TypeId, string TypeName, double AreaM2)>
            {
                (row.WallTypeId, row.WallMaterial, row.WallAreaM2)
            };
        }

        private IEnumerable<KeyValuePair<string, double>> GetSummaryGroups(IList<RoomQuantityRow> rows, string kind)
        {
            var groups = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            void Add(string key, double value)
            {
                if (string.IsNullOrWhiteSpace(key) || key == "（不設定）" || value <= 0)
                    return;
                groups[key] = (groups.TryGetValue(key, out var existing) ? existing : 0) + value;
            }

            foreach (var row in rows)
            {
                switch (kind)
                {
                    case "Wall":
                        foreach (var entry in GetWallTypeAreaEntries(row))
                            Add(entry.TypeName, entry.AreaM2);
                        break;
                    case "Floor":
                        Add(row.FloorMaterial, row.FloorAreaM2);
                        break;
                    case "Ceiling":
                        Add(row.CeilingMaterial, row.CeilingAreaM2);
                        break;
                    case "Manual":
                        if (row.ManualFaceAreaByMaterialM2 != null && row.ManualFaceAreaByMaterialM2.Count > 0)
                        {
                            foreach (var kv in row.ManualFaceAreaByMaterialM2)
                                Add(kv.Key, kv.Value);
                        }
                        else
                        {
                            Add(row.ManualFaceMaterial, row.ManualFaceAreaM2);
                        }
                        break;
                }
            }

            return groups;
        }

        private string GetTypeNameById(long typeId, string fallback = "")
        {
            if (typeId > 0)
            {
                try
                {
                    var elem = _document?.GetElement(RevitCompat.CreateElementId(typeId));
                    if (!string.IsNullOrWhiteSpace(elem?.Name))
                        return elem.Name;
                }
                catch { }
            }

            return fallback ?? string.Empty;
        }

        private static string SplitTypeCode(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return string.Empty;
            if (fullName.Contains("/"))
                return string.Join("/", fullName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(x => SplitTypeCode(x.Trim())));
            return TrySplitTypeName(fullName, out var code, out _) ? code : fullName.Trim();
        }

        private static string SplitTypeName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return string.Empty;
            if (fullName.Contains("/"))
                return string.Join("/", fullName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(x => SplitTypeName(x.Trim())));
            return TrySplitTypeName(fullName, out _, out var name) ? name : fullName.Trim();
        }

        private static bool TrySplitTypeName(string fullName, out string code, out string name)
        {
            code = string.Empty;
            name = string.Empty;
            if (string.IsNullOrWhiteSpace(fullName))
                return false;

            var text = fullName.Trim();
            var dash = text.IndexOf('-');
            if (dash > 0 && dash < text.Length - 1)
            {
                code = text.Substring(0, dash).Trim();
                name = text.Substring(dash + 1).Trim();
                return true;
            }

            var space = text.IndexOf(' ');
            if (space > 0 && space < text.Length - 1)
            {
                code = text.Substring(0, space).Trim();
                name = text.Substring(space + 1).Trim();
                return true;
            }

            return false;
        }

        private static Stylesheet CreateExportStylesheet()
        {
            var fonts = new Fonts(
                new DocumentFormat.OpenXml.Spreadsheet.Font(new FontSize { Val = 11D }, new FontName { Val = "Microsoft JhengHei UI" }),
                new DocumentFormat.OpenXml.Spreadsheet.Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Microsoft JhengHei UI" }),
                new DocumentFormat.OpenXml.Spreadsheet.Font(new Bold(), new FontSize { Val = 14D }, new FontName { Val = "Microsoft JhengHei UI" })
            );

            var fills = new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE9EEF7" }) { PatternType = PatternValues.Solid }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFF4D7F3" }) { PatternType = PatternValues.Solid }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFFDE1CC" }) { PatternType = PatternValues.Solid }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFD8F2D1" }) { PatternType = PatternValues.Solid }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFD5EAF8" }) { PatternType = PatternValues.Solid })
            );

            var borders = new Borders(
                new DocumentFormat.OpenXml.Spreadsheet.Border(),
                new DocumentFormat.OpenXml.Spreadsheet.Border(
                    new LeftBorder { Style = BorderStyleValues.Thin },
                    new RightBorder { Style = BorderStyleValues.Thin },
                    new TopBorder { Style = BorderStyleValues.Thin },
                    new BottomBorder { Style = BorderStyleValues.Thin },
                    new DiagonalBorder())
            );

            var cellFormats = new CellFormats(
                new CellFormat(),
                new CellFormat { FontId = 0, FillId = 0, BorderId = 1, ApplyBorder = true },
                new CellFormat
                {
                    FontId = 1,
                    FillId = 2,
                    BorderId = 1,
                    ApplyFont = true,
                    ApplyFill = true,
                    ApplyBorder = true,
                    Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center }
                },
                new CellFormat { FontId = 1, FillId = 3, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
                new CellFormat { FontId = 1, FillId = 4, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
                new CellFormat { FontId = 1, FillId = 5, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
                new CellFormat
                {
                    FontId = 2,
                    FillId = 2,
                    BorderId = 1,
                    ApplyFont = true,
                    ApplyFill = true,
                    ApplyBorder = true,
                    Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center }
                },
                new CellFormat
                {
                    FontId = 2,
                    FillId = 6,
                    BorderId = 1,
                    ApplyFont = true,
                    ApplyFill = true,
                    ApplyBorder = true,
                    Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center }
                },
                new CellFormat
                {
                    FontId = 0,
                    FillId = 0,
                    BorderId = 1,
                    ApplyBorder = true,
                    Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Left, Vertical = VerticalAlignmentValues.Center, WrapText = true }
                },
                new CellFormat
                {
                    FontId = 0,
                    FillId = 0,
                    BorderId = 1,
                    ApplyBorder = true,
                    Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, ShrinkToFit = true }
                }
            );

            return new Stylesheet(fonts, fills, borders, cellFormats);
        }

        private static string GetExportColumnName(int columnNumber)
        {
            var dividend = columnNumber;
            var columnName = string.Empty;
            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modulo) + columnName;
                dividend = (dividend - modulo) / 26;
            }
            return columnName;
        }

        private static string NormalizeExportLevelLabel(string level)
        {
            if (string.IsNullOrWhiteSpace(level))
                return "未分層";
            return level.Replace("FL", "F").Replace("樓", string.Empty).Trim();
        }

        private static int GetExportLevelSortKey(string level)
        {
            if (string.IsNullOrWhiteSpace(level))
                return 9999;
            var text = level.Trim().ToUpperInvariant().Replace("樓", string.Empty);
            if (text == "RF") return 9000;
            if (text.StartsWith("B") && text.EndsWith("F"))
            {
                var numberText = new string(text.Skip(1).TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(numberText, out var basement)) return -basement;
            }
            var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var floor)) return floor;
            return 5000;
        }

        private static Cell CreateTextCell(string value, uint styleIndex = 0)
        {
            return new Cell
            {
                StyleIndex = styleIndex,
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(value ?? string.Empty))
            };
        }

        private static Cell CreateNumberCell(double value, uint styleIndex = 0)
        {
            return new Cell
            {
                StyleIndex = styleIndex,
                DataType = CellValues.Number,
                CellValue = new CellValue(value.ToString("G17", CultureInfo.InvariantCulture))
            };
        }

        private sealed class RoomQuantityRow
        {
            internal long RoomId { get; set; }
            internal string Level { get; set; }
            internal string Number { get; set; }
            internal string Name { get; set; }
            internal double RoomAreaM2 { get; set; }
            internal double PerimeterM { get; set; }
            internal long WallTypeId { get; set; }
            internal string WallMaterial { get; set; }
            internal double WallHeightMm { get; set; }
            internal double WallAreaM2 { get; set; }
            internal Dictionary<long, double> WallAreaByTypeM2 { get; set; } = new Dictionary<long, double>();
            internal long FloorTypeId { get; set; }
            internal string FloorMaterial { get; set; }
            internal double FloorAreaM2 { get; set; }
            internal long CeilingTypeId { get; set; }
            internal string CeilingMaterial { get; set; }
            internal double CeilingHeightMm { get; set; }
            internal double CeilingAreaM2 { get; set; }
            internal long SkirtingTypeId { get; set; }
            internal string SkirtingMaterial { get; set; }
            internal double SkirtingHeightMm { get; set; }
            internal double SkirtingLengthM { get; set; }
            internal string ManualFaceMaterial { get; set; }
            internal double ManualFaceAreaM2 { get; set; }
            internal Dictionary<string, double> ManualFaceAreaByMaterialM2 { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            internal string BoundaryMode { get; set; }
            internal string ModelStatus { get; set; }
        }

        private sealed class ModelFinishData
        {
            internal double WallAreaM2 { get; set; }
            internal double WallHeightMm { get; set; }
            internal double FloorAreaM2 { get; set; }
            internal double CeilingAreaM2 { get; set; }
            internal double ManualFaceAreaM2 { get; set; }
            internal double SkirtingLengthM { get; set; }
            internal string ManualFaceMaterial { get; set; }
            internal Dictionary<long, double> WallAreaByTypeM2 { get; } = new Dictionary<long, double>();
            internal Dictionary<long, double> FloorAreaByTypeM2 { get; } = new Dictionary<long, double>();
            internal Dictionary<long, double> CeilingAreaByTypeM2 { get; } = new Dictionary<long, double>();
            internal Dictionary<string, double> ManualFaceAreaByMaterialM2 { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            internal HashSet<long> WallTypeIds { get; } = new HashSet<long>();
            internal HashSet<long> FloorTypeIds { get; } = new HashSet<long>();
            internal HashSet<long> CeilingTypeIds { get; } = new HashSet<long>();
            internal HashSet<long> SkirtingTypeIds { get; } = new HashSet<long>();
            internal bool HasAnyQuantity =>
                WallAreaM2 > 0 || FloorAreaM2 > 0 || CeilingAreaM2 > 0 || SkirtingLengthM > 0 || ManualFaceAreaM2 > 0;
        }

        private sealed class SummaryEntry
        {
            internal SummaryEntry(string level, string kind, string material, double quantity, string unit)
            {
                Level = level ?? string.Empty;
                Kind = kind ?? string.Empty;
                Material = material ?? string.Empty;
                Quantity = quantity;
                Unit = unit ?? string.Empty;
            }

            internal string Level { get; }
            internal string Kind { get; }
            internal string Material { get; }
            internal double Quantity { get; }
            internal string Unit { get; }
        }

        private static void AppendTextRow(SheetData sheetData, params string[] values)
        {
            var row = new Row();
            foreach (string value in values)
            {
                row.Append(new Cell
                {
                    DataType = CellValues.String,
                    CellValue = new CellValue(value ?? string.Empty)
                });
            }
            sheetData.Append(row);
        }

        private void FillTypes(
            WinForms.ComboBox combo,
            Type type,
            ElementId selectedId,
            Func<Element, bool> predicate = null)
        {
            combo.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
            combo.Items.Clear();
            IEnumerable<Element> elements = new FilteredElementCollector(_document)
                .OfClass(type)
                .WhereElementIsElementType()
                .ToElements();
            if (predicate != null)
                elements = elements.Where(predicate);
            foreach (Element element in elements.OrderBy(x => x.Name))
                combo.Items.Add(new ElementOption(element.Id, element.Name));

            long selectedValue = selectedId == null ? -1 : selectedId.Value;
            int index = combo.Items.Cast<ElementOption>().ToList()
                .FindIndex(x => x.Id.Value == selectedValue);
            combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);
        }

        private static void AddRow(
            WinForms.TableLayoutPanel root,
            int row,
            string label,
            WinForms.Control control)
        {
            root.Controls.Add(new WinForms.Label
            {
                Text = label,
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left
            }, 0, row);
            control.Dock = WinForms.DockStyle.Fill;
            root.Controls.Add(control, 1, row);
        }

        private static void ConfigureNumeric(WinForms.NumericUpDown input, decimal min, decimal max)
        {
            input.Minimum = min;
            input.Maximum = max;
            input.DecimalPlaces = 0;
            input.ThousandsSeparator = true;
        }

        private static decimal Clamp(double value, WinForms.NumericUpDown input)
        {
            decimal candidate = (decimal)value;
            return Math.Min(input.Maximum, Math.Max(input.Minimum, candidate));
        }

        private static ElementId SelectedId(WinForms.ComboBox combo)
        {
            return (combo.SelectedItem as ElementOption)?.Id ?? ElementId.InvalidElementId;
        }

        private static string SelectedName(WinForms.ComboBox combo)
        {
            return (combo.SelectedItem as ElementOption)?.Name ?? string.Empty;
        }

        private sealed class ElementOption
        {
            internal ElementOption(ElementId id, string name)
            {
                Id = id;
                Name = name;
            }
            internal ElementId Id { get; }
            internal string Name { get; }
            public override string ToString() => Name;
        }
    }
}
#endif
