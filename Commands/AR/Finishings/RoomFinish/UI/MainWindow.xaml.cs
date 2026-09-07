using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;
using YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish.UI
{
    public class RoomSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem.Category != null && RevitCompat.GetElementIdValue(elem.Category.Id) == (int)BuiltInCategory.OST_Rooms;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    public class TypeOption
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }

    public class RoomFinishRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ElementId RoomId { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public string Level { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        private long _wallTypeId = -1;
        public long WallTypeId
        {
            get => _wallTypeId;
            set
            {
                if (_wallTypeId == value) return;
                _wallTypeId = value;
                UpdateStatus();
                OnPropertyChanged();
            }
        }

        private long _floorTypeId = -1;
        public long FloorTypeId
        {
            get => _floorTypeId;
            set
            {
                if (_floorTypeId == value) return;
                _floorTypeId = value;
                UpdateStatus();
                OnPropertyChanged();
            }
        }

        private long _ceilingTypeId = -1;
        public long CeilingTypeId
        {
            get => _ceilingTypeId;
            set
            {
                if (_ceilingTypeId == value) return;
                _ceilingTypeId = value;
                UpdateStatus();
                OnPropertyChanged();
            }
        }

        private double _wallHeightMm = 3000;
        public double WallHeightMm
        {
            get => _wallHeightMm;
            set
            {
                if (Math.Abs(_wallHeightMm - value) < 0.0001) return;
                _wallHeightMm = value;
                UpdateStatus();
                OnPropertyChanged();
            }
        }

        private double _ceilingHeightMm = 2700;
        public double CeilingHeightMm
        {
            get => _ceilingHeightMm;
            set
            {
                if (Math.Abs(_ceilingHeightMm - value) < 0.0001) return;
                _ceilingHeightMm = value;
                UpdateStatus();
                OnPropertyChanged();
            }
        }

        private long _skirtingTypeId = -1;
        public long SkirtingTypeId
        {
            get => _skirtingTypeId;
            set
            {
                if (_skirtingTypeId == value) return;
                _skirtingTypeId = value;
                UpdateStatus();
                OnPropertyChanged();
            }
        }

        private string _status;
        public string Status
        {
            get => _status;
            private set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
            }
        }

        private string _modelSyncStatus = "未檢查";
        public string ModelSyncStatus
        {
            get => _modelSyncStatus;
            set
            {
                if (_modelSyncStatus == value) return;
                _modelSyncStatus = value;
                OnPropertyChanged();
            }
        }

        private System.Windows.Media.Brush _modelSyncStatusBrush = System.Windows.Media.Brushes.DimGray;
        public System.Windows.Media.Brush ModelSyncStatusBrush
        {
            get => _modelSyncStatusBrush;
            set
            {
                if (_modelSyncStatusBrush == value) return;
                _modelSyncStatusBrush = value;
                OnPropertyChanged();
            }
        }

        private string _currentWallFinish;
        public string CurrentWallFinish
        {
            get => _currentWallFinish;
            set
            {
                if (_currentWallFinish == value) return;
                _currentWallFinish = value;
                OnPropertyChanged();
            }
        }

        private string _currentFloorFinish;
        public string CurrentFloorFinish
        {
            get => _currentFloorFinish;
            set
            {
                if (_currentFloorFinish == value) return;
                _currentFloorFinish = value;
                OnPropertyChanged();
            }
        }

        private string _currentCeilingFinish;
        public string CurrentCeilingFinish
        {
            get => _currentCeilingFinish;
            set
            {
                if (_currentCeilingFinish == value) return;
                _currentCeilingFinish = value;
                OnPropertyChanged();
            }
        }

        private double _currentCeilingHeightMm;
        public double CurrentCeilingHeightMm
        {
            get => _currentCeilingHeightMm;
            set
            {
                if (Math.Abs(_currentCeilingHeightMm - value) < 0.0001) return;
                _currentCeilingHeightMm = value;
                OnPropertyChanged();
            }
        }

        private double _currentWallHeightMm;
        public double CurrentWallHeightMm
        {
            get => _currentWallHeightMm;
            set
            {
                if (Math.Abs(_currentWallHeightMm - value) < 0.0001) return;
                _currentWallHeightMm = value;
                OnPropertyChanged();
            }
        }

        private string _replaceWallFinish;
        public string ReplaceWallFinish
        {
            get => _replaceWallFinish;
            set
            {
                if (_replaceWallFinish == value) return;
                _replaceWallFinish = value;
                OnPropertyChanged();
            }
        }

        private string _replaceFloorFinish;
        public string ReplaceFloorFinish
        {
            get => _replaceFloorFinish;
            set
            {
                if (_replaceFloorFinish == value) return;
                _replaceFloorFinish = value;
                OnPropertyChanged();
            }
        }

        private string _replaceCeilingFinish;
        public string ReplaceCeilingFinish
        {
            get => _replaceCeilingFinish;
            set
            {
                if (_replaceCeilingFinish == value) return;
                _replaceCeilingFinish = value;
                OnPropertyChanged();
            }
        }

        private double _replaceCeilingHeightMm;
        public double ReplaceCeilingHeightMm
        {
            get => _replaceCeilingHeightMm;
            set
            {
                if (Math.Abs(_replaceCeilingHeightMm - value) < 0.0001) return;
                _replaceCeilingHeightMm = value;
                OnPropertyChanged();
            }
        }

        public void UpdateStatus()
        {
            var hasAnyFinish = WallTypeId > 0 || FloorTypeId > 0 || CeilingTypeId > 0 || SkirtingTypeId > 0;
            if (!hasAnyFinish)
            {
                Status = "⚠ 未設定材料";
                return;
            }

            if (WallHeightMm <= 0 || CeilingHeightMm <= 0)
            {
                Status = "⚠ 高度設定無效";
                return;
            }

            Status = "✓ 設定完成";
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow : Window
    {
        private const string P_DynWallFinish = "AR_牆面塗層";
        private const string P_DynFloorFinish = "AR_樓板塗層";
        private const string P_DynCeilingFinish = "AR_天花板塗層";
        private const string P_DynCeilingHeight = "AR_天花板高度";
        private const string P_DynSkirtingFinish = "AR_踢腳板塗層";
        private const string P_LegacyDynWallFinish = "牆面塗層";
        private const string P_LegacyDynFloorFinish = "樓板塗層";
        private const string P_LegacyDynCeilingFinish = "天花板塗層";
        private const string P_LegacyDynCeilingHeight = "天花板高度";
        private const string P_LegacyDynSkirtingFinish = "踢腳板塗層";

        private readonly UIDocument _uiDoc;
        private readonly ObservableCollection<RoomFinishRow> _roomRows = new ObservableCollection<RoomFinishRow>();
        private ICollectionView _roomRowsView;
        private FinishSettings _loadedSettings;
        private bool _isBulkComboApplying;

        // 匯出時建立的模型面積索引（只在 ExportRoomSettingsToXlsx 期間有效）
        private Dictionary<long, ModelFinishData> _modelFinishIndex;

        private sealed class ModelFinishData
        {
            public double WallAreaM2      { get; set; }
            public double WallHeightMm    { get; set; }
            public double FloorAreaM2     { get; set; }
            public double CeilingAreaM2   { get; set; }
            public double ManualFaceAreaM2 { get; set; }
            public double SkirtingLengthM { get; set; }
            public long   WallTypeId      { get; set; }
            public long   FloorTypeId     { get; set; }
            public long   CeilingTypeId   { get; set; }
            public long   SkirtingTypeId  { get; set; }
            public HashSet<ElementId> WallElementIds { get; } = new HashSet<ElementId>();
            public HashSet<ElementId> FloorElementIds { get; } = new HashSet<ElementId>();
            public HashSet<ElementId> CeilingElementIds { get; } = new HashSet<ElementId>();
            public HashSet<ElementId> ManualFaceElementIds { get; } = new HashSet<ElementId>();
            public HashSet<ElementId> SkirtingElementIds { get; } = new HashSet<ElementId>();
            public HashSet<long> WallTypeIds { get; } = new HashSet<long>();
            public HashSet<long> FloorTypeIds { get; } = new HashSet<long>();
            public HashSet<long> CeilingTypeIds { get; } = new HashSet<long>();
            public HashSet<long> SkirtingTypeIds { get; } = new HashSet<long>();
            public Dictionary<long, double> WallAreaByTypeM2 { get; } = new Dictionary<long, double>();
            public Dictionary<long, double> FloorAreaByTypeM2 { get; } = new Dictionary<long, double>();
            public Dictionary<long, double> CeilingAreaByTypeM2 { get; } = new Dictionary<long, double>();
            public Dictionary<string, double> ManualFaceAreaByMaterialM2 { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ModelDifferenceRow
        {
            public long RoomId { get; set; }
            public string Severity { get; set; }
            public string RoomNumber { get; set; }
            public string RoomName { get; set; }
            public string Level { get; set; }
            public string Item { get; set; }
            public string RoomSetting { get; set; }
            public string ModelCurrent { get; set; }
            public string Detail { get; set; }
            public string CheckViewName { get; set; }
        }

        private void UpdateModelSyncStatus(RoomFinishRow row, ModelFinishData md)
        {
            bool needWall = row.WallTypeId > 0;
            bool needFloor = row.FloorTypeId > 0;
            bool needCeiling = row.CeilingTypeId > 0;
            bool needSkirting = row.SkirtingTypeId > 0;

            if (!(needWall || needFloor || needCeiling || needSkirting))
            {
                row.ModelSyncStatus = "未設定";
                row.ModelSyncStatusBrush = System.Windows.Media.Brushes.DimGray;
                return;
            }

            bool wallOk = !needWall || (md != null && md.WallAreaM2 > 0);
            bool floorOk = !needFloor || (md != null && md.FloorAreaM2 > 0);
            bool ceilingOk = !needCeiling || (md != null && md.CeilingAreaM2 > 0);
            bool skirtingOk = !needSkirting || (md != null && md.SkirtingLengthM > 0);

            if (wallOk && floorOk && ceilingOk && skirtingOk)
            {
                row.ModelSyncStatus = "成功";
                row.ModelSyncStatusBrush = System.Windows.Media.Brushes.ForestGreen;
            }
            else
            {
                row.ModelSyncStatus = "失敗";
                row.ModelSyncStatusBrush = System.Windows.Media.Brushes.Crimson;
            }
        }

        public ObservableCollection<TypeOption> WallTypeOptions { get; } = new ObservableCollection<TypeOption>();
        public ObservableCollection<TypeOption> FloorTypeOptions { get; } = new ObservableCollection<TypeOption>();
        public ObservableCollection<TypeOption> CeilingTypeOptions { get; } = new ObservableCollection<TypeOption>();
        public ObservableCollection<TypeOption> SkirtingTypeOptions { get; } = new ObservableCollection<TypeOption>();

        public FinishSettings ViewModel { get; private set; }

        // === Modeless ExternalEvent ===
        private SyncRoomsHandler _syncHandler;
        private ExternalEvent _syncEvent;
        private ApplyAndUpdateHandler _applyHandler;
        private ExternalEvent _applyEvent;
        private PickRoomsHandler _pickHandler;
        private ExternalEvent _pickEvent;
        private FocusRoomsHandler _focusHandler;
        private ExternalEvent _focusEvent;
        private UpdateValuesOnlyHandler _updateValuesHandler;
        private ExternalEvent _updateValuesEvent;
        private AutoJoinWallsHandler _autoJoinHandler;
        private ExternalEvent _autoJoinEvent;
        private AlignWallsToColumnsHandler _alignColumnsHandler;
        private ExternalEvent _alignColumnsEvent;
        private ToggleFinishWallJoinsHandler _toggleWallJoinsHandler;
        private ExternalEvent _toggleWallJoinsEvent;
        private ClearArFinishParamsHandler _clearArParamsHandler;
        private ExternalEvent _clearArParamsEvent;
        private CreateCheckViewsHandler _createCheckViewsHandler;
        private ExternalEvent _createCheckViewsEvent;
        private List<long> _pendingCheckViewRoomIds = new List<long>();
        private bool _activateFirstCheckView;
        private bool _finishWallJoinsAllowed = false; // 生成後預設禁止，按鈕可切換
        private bool _hasSyncedFromModelOnce = false;
        private DateTime? _lastModelSyncAt = null;

        public MainWindow(UIDocument uiDoc,
            SyncRoomsHandler syncHandler,         ExternalEvent syncEvent,
            ApplyAndUpdateHandler applyHandler,   ExternalEvent applyEvent,
            PickRoomsHandler pickHandler,          ExternalEvent pickEvent,
            FocusRoomsHandler focusHandler,        ExternalEvent focusEvent,
            UpdateValuesOnlyHandler updateValuesHandler, ExternalEvent updateValuesEvent,
            AutoJoinWallsHandler autoJoinHandler,  ExternalEvent autoJoinEvent,
            AlignWallsToColumnsHandler alignColumnsHandler, ExternalEvent alignColumnsEvent,
            ToggleFinishWallJoinsHandler toggleWallJoinsHandler, ExternalEvent toggleWallJoinsEvent,
            ClearArFinishParamsHandler clearArParamsHandler, ExternalEvent clearArParamsEvent,
            CreateCheckViewsHandler createCheckViewsHandler, ExternalEvent createCheckViewsEvent)
        {
            InitializeComponent();
            DataContext = this;
            _uiDoc = uiDoc;
            _syncHandler = syncHandler;             _syncEvent = syncEvent;
            _applyHandler = applyHandler;           _applyEvent = applyEvent;
            _pickHandler = pickHandler;             _pickEvent = pickEvent;
            _focusHandler = focusHandler;           _focusEvent = focusEvent;
            _updateValuesHandler = updateValuesHandler; _updateValuesEvent = updateValuesEvent;
            _autoJoinHandler = autoJoinHandler;     _autoJoinEvent = autoJoinEvent;
            _alignColumnsHandler = alignColumnsHandler; _alignColumnsEvent = alignColumnsEvent;
            _toggleWallJoinsHandler = toggleWallJoinsHandler; _toggleWallJoinsEvent = toggleWallJoinsEvent;
            _clearArParamsHandler = clearArParamsHandler; _clearArParamsEvent = clearArParamsEvent;
            _createCheckViewsHandler = createCheckViewsHandler; _createCheckViewsEvent = createCheckViewsEvent;
            syncHandler.Window = this;
            applyHandler.Window = this;
            pickHandler.Window = this;
            focusHandler.Window = this;
            updateValuesHandler.Window = this;
            autoJoinHandler.Window = this;
            alignColumnsHandler.Window = this;
            toggleWallJoinsHandler.Window = this;
            clearArParamsHandler.Window = this;
            createCheckViewsHandler.Window = this;

            LoadTypes();
            LoadSettings();
            LoadRooms();
            InitFilters();
            UpdatePickedCount();

            btnGenerate.Click      += (s, e) => _applyEvent.Raise();
            btnUpdateValues.Click  += (s, e) => _updateValuesEvent.Raise();
            btnPickRooms.Click     += (s, e) => _pickEvent.Raise();
            btnFocusRooms.Click    += (s, e) => _focusEvent.Raise();
            btnSelectRooms.Click   += (s, e) => _pickEvent.Raise(); // 選完後 PickRooms 內部自動切至房間管理頁
            btnClearFilters.Click  += (s, e) => ClearFilters();
            btnAutoJoinWalls.Click += (s, e) => _autoJoinEvent.Raise();
            btnAlignToColumns.Click += (s, e) => _alignColumnsEvent.Raise();
            btnToggleWallJoins.Click += (s, e) => _toggleWallJoinsEvent.Raise();
            btnImportExport.Click  += (s, e) => ShowImportExportDialog();
            btnLoadCurrentStatus.Click   += (s, e) => _syncEvent.Raise();
            btnCheckModelDiff.Click += (s, e) => ShowModelDifferenceReport();
            btnClearArParams.Click += (s, e) => _clearArParamsEvent.Raise();
            btnApplyReplaceToModel.Click += (s, e) => _applyEvent.Raise();
            btnSelectAll.Click    += (s, e) => SelectAllRooms();
            btnDeselectAll.Click  += (s, e) => DeselectAllRooms();
            btnInvertSelect.Click += (s, e) => InvertSelection();
            btnBatchApply.Click   += (s, e) => BatchApplyToSelectedRows();

            txtSearchRooms.TextChanged      += (s, e) => ApplyFilters();
            cmbLevelFilter.SelectionChanged  += (s, e) => ApplyFilters();
            cmbStatusFilter.SelectionChanged += (s, e) => ApplyFilters();
            chkOnlySelected.Checked   += (s, e) => ApplyFilters();
            chkOnlySelected.Unchecked += (s, e) => ApplyFilters();

            this.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                    Close(); // modeless: 不設定 DialogResult
            };

            this.Closing += (s, e) => SaveDraftSettings();
        }

        private void LoadTypes()
        {
            var doc = _uiDoc.Document;

            WallTypeOptions.Clear();
            FloorTypeOptions.Clear();
            CeilingTypeOptions.Clear();
            SkirtingTypeOptions.Clear();

            WallTypeOptions.Add(new TypeOption { Id = -1, Name = "（不設定）" });
            FloorTypeOptions.Add(new TypeOption { Id = -1, Name = "（不設定）" });
            CeilingTypeOptions.Add(new TypeOption { Id = -1, Name = "（不設定）" });
            SkirtingTypeOptions.Add(new TypeOption { Id = -1, Name = "（不設定）" });

            var wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .OrderBy(t => t.Name)
                .ToList();

            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(t => t.Name)
                .ToList();

            var ceilingTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .OrderBy(t => t.Name)
                .ToList();

            foreach (var wallType in wallTypes)
            {
                WallTypeOptions.Add(new TypeOption { Id = RevitCompat.GetElementIdValue(wallType.Id), Name = wallType.Name });
                SkirtingTypeOptions.Add(new TypeOption { Id = RevitCompat.GetElementIdValue(wallType.Id), Name = wallType.Name });
            }

            foreach (var floorType in floorTypes)
            {
                FloorTypeOptions.Add(new TypeOption { Id = RevitCompat.GetElementIdValue(floorType.Id), Name = floorType.Name });
            }

            foreach (var ceilingType in ceilingTypes)
            {
                CeilingTypeOptions.Add(new TypeOption { Id = RevitCompat.GetElementIdValue(ceilingType.Id), Name = ceilingType.Name });
            }

            cmbWalls.ItemsSource = WallTypeOptions;
            cmbFloors.ItemsSource = FloorTypeOptions;
            cmbCeilings.ItemsSource = CeilingTypeOptions;
            cmbSkirtings.ItemsSource = SkirtingTypeOptions;

            cmbWalls.SelectedIndex = 0;
            cmbFloors.SelectedIndex = 0;
            cmbCeilings.SelectedIndex = 0;
            cmbSkirtings.SelectedIndex = 0;
            cmbBoundary.SelectedIndex = 0;

            // 批次套用下拉選項：「（不變）」作為預設，後接一般選項
            var noChange = new TypeOption { Id = -2, Name = "（不變）" };
            var batchWallOpts = new List<TypeOption> { noChange };
            batchWallOpts.AddRange(WallTypeOptions);
            var batchFloorOpts = new List<TypeOption> { noChange };
            batchFloorOpts.AddRange(FloorTypeOptions);
            var batchCeilingOpts = new List<TypeOption> { noChange };
            batchCeilingOpts.AddRange(CeilingTypeOptions);
            var batchSkirtingOpts = new List<TypeOption> { noChange };
            batchSkirtingOpts.AddRange(SkirtingTypeOptions);
            cmbBatchWall.ItemsSource = batchWallOpts;
            cmbBatchFloor.ItemsSource = batchFloorOpts;
            cmbBatchCeiling.ItemsSource = batchCeilingOpts;
            cmbBatchSkirting.ItemsSource = batchSkirtingOpts;
            cmbBatchWall.SelectedIndex = 0;
            cmbBatchFloor.SelectedIndex = 0;
            cmbBatchCeiling.SelectedIndex = 0;
            cmbBatchSkirting.SelectedIndex = 0;

            txtStatus.Text = $"已載入 {wallTypes.Count} 種牆類型，可開始設定裝修。";
        }

        private void LoadRooms()
        {
            var doc = _uiDoc.Document;

            var previous = _roomRows.ToDictionary(x => RevitCompat.GetElementIdValue(x.RoomId), x => x);
            _roomRows.Clear();

            var defaultWallTypeId = GetSelectedOptionId(cmbWalls);
            var defaultFloorTypeId = GetSelectedOptionId(cmbFloors);
            var defaultCeilingTypeId = GetSelectedOptionId(cmbCeilings);
            var defaultSkirtingTypeId = GetSelectedOptionId(cmbSkirtings);
            var defaultWallHeight = ParseDouble(txtWallHeight.Text, 3000);
            var defaultCeilingHeight = ParseDouble(txtCeilingHeight.Text, 2700);

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .OrderBy(r => r.Number)
                .ToList();

            foreach (var room in rooms)
            {
                if (previous.TryGetValue(RevitCompat.GetElementIdValue(room.Id), out var existing))
                {
                    _roomRows.Add(existing);
                    continue;
                }

                var levelName = doc.GetElement(room.LevelId)?.Name ?? "";
                var row = new RoomFinishRow
                {
                    RoomId = room.Id,
                    Name = room.Name,
                    Number = room.Number,
                    Level = levelName,
                    IsSelected = false,
                    WallTypeId = defaultWallTypeId,
                    FloorTypeId = defaultFloorTypeId,
                    CeilingTypeId = defaultCeilingTypeId,
                    SkirtingTypeId = defaultSkirtingTypeId,
                    WallHeightMm = defaultWallHeight,
                    CeilingHeightMm = defaultCeilingHeight
                };

                var roomOverride = _loadedSettings?.GetRoomOverride(room.Id);
                if (roomOverride != null)
                {
                    if (roomOverride.WallTypeId > 0) row.WallTypeId = roomOverride.WallTypeId;
                    if (roomOverride.FloorTypeId > 0) row.FloorTypeId = roomOverride.FloorTypeId;
                    if (roomOverride.CeilingTypeId > 0) row.CeilingTypeId = roomOverride.CeilingTypeId;
                    if (roomOverride.SkirtingTypeId > 0) row.SkirtingTypeId = roomOverride.SkirtingTypeId;
                    if (roomOverride.WallHeightMm > 0) row.WallHeightMm = roomOverride.WallHeightMm;
                    if (roomOverride.CeilingHeightMm > 0) row.CeilingHeightMm = roomOverride.CeilingHeightMm;
                }

                if (_loadedSettings?.TargetRoomIds != null && _loadedSettings.TargetRoomIds.Any())
                {
                    var roomIdValue = RevitCompat.GetElementIdValue(room.Id);
                    row.IsSelected = _loadedSettings.TargetRoomIds.Any(x => RevitCompat.GetElementIdValue(x) == roomIdValue);
                }

                row.UpdateStatus();
                row.PropertyChanged += RoomRowOnPropertyChanged;
                _roomRows.Add(row);
            }

            _roomRowsView = CollectionViewSource.GetDefaultView(_roomRows);
            dgRooms.ItemsSource = _roomRowsView;

            ReloadLevelFilterItems();
            ApplyFilters();
            UpdatePickedCount();

            txtStatus.Text = $"已載入 {_roomRows.Count} 間房間。";

            // 開窗時只讀取房間參數與模型狀態，不用既有粉刷面反向覆蓋下拉欄位。
            // 使用者明確按「同步模型」時，才將模型粉刷面回填為目前設定。
            LoadCurrentRoomParameterStatus(applyModelValuesToRows: false, showCompletionMessage: false);
        }

        private void LoadCurrentRoomParameterStatus(bool applyModelValuesToRows = true, bool showCompletionMessage = true)
        {
            var doc = _uiDoc.Document;
            var syncCount = 0;
            _modelFinishIndex = BuildModelFinishDataIndex(doc);

            foreach (var row in _roomRows)
            {
                var room = doc.GetElement(row.RoomId) as Room;
                if (room == null)
                    continue;

                var wallParam = LookupRoomParameter(room, P_DynWallFinish);
                var floorParam = LookupRoomParameter(room, P_DynFloorFinish);
                var ceilingFinishParam = LookupRoomParameter(room, P_DynCeilingFinish);
                var ceilingHeightParam = LookupRoomParameter(room, P_DynCeilingHeight);
                var skirtingParam = LookupRoomParameter(room, P_DynSkirtingFinish);

                row.CurrentWallFinish    = wallParam?.AsString() ?? string.Empty;
                row.CurrentFloorFinish   = floorParam?.AsString() ?? string.Empty;
                row.CurrentCeilingFinish = ceilingFinishParam?.AsString() ?? string.Empty;

                // 天花板高度：Length 型共用參數以英尺儲存，AsDouble() 返回英尺 → 乘以 304.8 得 mm
                double rawFt = ceilingHeightParam?.AsDouble() ?? 0;
                row.CurrentCeilingHeightMm = rawFt * 304.8;

                // 同步房間參數時只以房間參數為準；空值必須清回「不設定」。
                row.WallTypeId     = ResolveRoomParameterTypeId(row.CurrentWallFinish, WallTypeOptions);
                row.FloorTypeId    = ResolveRoomParameterTypeId(row.CurrentFloorFinish, FloorTypeOptions);
                row.CeilingTypeId  = ResolveRoomParameterTypeId(row.CurrentCeilingFinish, CeilingTypeOptions);
                row.SkirtingTypeId = ResolveRoomParameterTypeId(skirtingParam?.AsString() ?? string.Empty, SkirtingTypeOptions);

                // 面生面/手動/既有模型粉刷元素回讀：
                // 開窗預覽時只做狀態檢查，不覆蓋設定欄位；使用者按「同步模型」時才回填。
                var roomIdVal = RevitCompat.GetElementIdValue(row.RoomId);
                ModelFinishData md = null;
                row.CurrentWallHeightMm = 0;
                if (_modelFinishIndex != null && _modelFinishIndex.TryGetValue(roomIdVal, out md))
                {
                    if (applyModelValuesToRows && md.WallTypeId > 0)
                    {
                        row.WallTypeId = md.WallTypeId;
                        row.CurrentWallFinish = JoinTypeNamesByIds(WallTypeOptions, md.WallTypeIds);
                    }

                    row.CurrentWallHeightMm = md.WallHeightMm;

                    if (applyModelValuesToRows && md.FloorTypeId > 0)
                    {
                        row.FloorTypeId = md.FloorTypeId;
                        row.CurrentFloorFinish = JoinTypeNamesByIds(FloorTypeOptions, md.FloorTypeIds);
                    }

                    if (applyModelValuesToRows && md.CeilingTypeId > 0)
                    {
                        row.CeilingTypeId = md.CeilingTypeId;
                        row.CurrentCeilingFinish = JoinTypeNamesByIds(CeilingTypeOptions, md.CeilingTypeIds);
                    }

                    if (applyModelValuesToRows && md.SkirtingTypeId > 0)
                    {
                        row.SkirtingTypeId = md.SkirtingTypeId;
                    }
                }

                UpdateModelSyncStatus(row, md);

                if (ceilingHeightParam != null && row.CurrentCeilingHeightMm > 0)
                    row.CeilingHeightMm = row.CurrentCeilingHeightMm;
                else if (ceilingHeightParam != null)
                    row.CeilingHeightMm = ParseDouble(txtCeilingHeight.Text, 2700);

                if (applyModelValuesToRows && row.CurrentWallHeightMm > 0)
                    row.WallHeightMm = row.CurrentWallHeightMm;

                row.UpdateStatus();
                syncCount++;
            }

            dgRooms.Items.Refresh();
            if (applyModelValuesToRows)
            {
                _hasSyncedFromModelOnce = true;
                _lastModelSyncAt = DateTime.Now;
                txtStatus.Text = $"已從房間參數/模型粉刷元素同步 {syncCount} 間房間到下拉設定欄。";
                if (showCompletionMessage)
                    MessageBox.Show($"同步模型完成，已更新 {syncCount} 間房間。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                txtStatus.Text = $"已載入 {syncCount} 間房間；尚未以模型粉刷面覆蓋設定欄，需按「同步模型」才會回填。";
            }
        }

        private void ApplyReplacementToModel()
        {
            var targetRows = GetDistinctSelectedRows();
            if (!targetRows.Any())
            {
                MessageBox.Show("請先至少選擇一個房間再套用取代。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var preserveManualAdjustments = chkPreserveManualAdjustments.IsChecked == true;
            var skippedMixedRows = new List<RoomFinishRow>();
            if (preserveManualAdjustments)
            {
                foreach (var row in targetRows)
                {
                    if (ContainsMultipleTypeTokens(row.CurrentWallFinish)
                        || ContainsMultipleTypeTokens(row.CurrentFloorFinish)
                        || ContainsMultipleTypeTokens(row.CurrentCeilingFinish))
                    {
                        skippedMixedRows.Add(row);
                    }
                }

                if (skippedMixedRows.Count > 0)
                {
                    targetRows = targetRows.Except(skippedMixedRows).ToList();
                    if (!targetRows.Any())
                    {
                        MessageBox.Show(
                            $"本次選取房間皆為混合品類，已依「保留手動調整」設定略過 {skippedMixedRows.Count} 間，未執行覆蓋。",
                            "提示",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }
                }
            }

            var doc = _uiDoc.Document;
            var targetRooms = targetRows
                .Select(r => doc.GetElement(r.RoomId) as Room)
                .Where(r => r != null && r.Area > 0)
                .ToList();
            var overlapIssues = RoomOverlapGuard.Detect(targetRooms);
            if (overlapIssues.Any())
            {
                var unsafeRoomIds = new HashSet<long>(overlapIssues.SelectMany(x => x.RoomIds));
                var beforeCount = targetRows.Count;
                targetRows = targetRows
                    .Where(r => !unsafeRoomIds.Contains(RevitCompat.GetElementIdValue(r.RoomId)))
                    .ToList();

                var message = new StringBuilder();
                message.AppendLine("偵測到房間重疊或空間歸屬不唯一，為避免誤改參數值或誤刪裝修面，已略過相關房間：");
                foreach (var issue in overlapIssues.Take(8))
                    message.AppendLine($"- {issue.Description}");
                if (overlapIssues.Count > 8)
                    message.AppendLine($"... 另有 {overlapIssues.Count - 8} 筆重疊風險未列出。");

                if (!targetRows.Any())
                {
                    message.AppendLine();
                    message.AppendLine("本次沒有安全可處理的房間，未執行任何模型變更。");
                    MessageBox.Show(message.ToString(), "房間重疊防呆", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                message.AppendLine();
                message.AppendLine($"本次將只處理 {targetRows.Count} / {beforeCount} 間安全房間。");
                MessageBox.Show(message.ToString(), "房間重疊防呆", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            var updated = 0;

            try
            {
                using (var t = new Transaction(doc, "套用下拉設定到房間參數"))
                {
                    t.Start();

                    foreach (var row in targetRows)
                    {
                        var room = doc.GetElement(row.RoomId) as Room;
                        if (room == null)
                            continue;

                        var changed = false;

                        var wallParam = LookupRoomParameter(room, P_DynWallFinish);
                        if (wallParam != null && !wallParam.IsReadOnly)
                        {
                            var target = GetTypeNameById(WallTypeOptions, GetEffectiveWallTypeId(row));
                            var current = wallParam.AsString() ?? string.Empty;
                            if (!string.Equals(target, current, StringComparison.Ordinal))
                            {
                                wallParam.Set(target);
                                changed = true;
                            }
                        }

                        var floorParam = LookupRoomParameter(room, P_DynFloorFinish);
                        if (floorParam != null && !floorParam.IsReadOnly)
                        {
                            var target = GetTypeNameById(FloorTypeOptions, GetEffectiveFloorTypeId(row));
                            var current = floorParam.AsString() ?? string.Empty;
                            if (!string.Equals(target, current, StringComparison.Ordinal))
                            {
                                floorParam.Set(target);
                                changed = true;
                            }
                        }

                        var ceilingFinishParam = LookupRoomParameter(room, P_DynCeilingFinish);
                        if (ceilingFinishParam != null && !ceilingFinishParam.IsReadOnly)
                        {
                            var target = GetTypeNameById(CeilingTypeOptions, GetEffectiveCeilingTypeId(row));
                            var current = ceilingFinishParam.AsString() ?? string.Empty;
                            if (!string.Equals(target, current, StringComparison.Ordinal))
                            {
                                ceilingFinishParam.Set(target);
                                changed = true;
                            }
                        }

                        var ceilingHeightParam = LookupRoomParameter(room, P_DynCeilingHeight);
                        if (ceilingHeightParam != null && !ceilingHeightParam.IsReadOnly)
                        {
                            var targetInternal = row.CeilingHeightMm / 304.8;
                            if (Math.Abs(ceilingHeightParam.AsDouble() - targetInternal) > 1e-9)
                            {
                                ceilingHeightParam.Set(targetInternal);
                                changed = true;
                            }
                        }

                        if (changed)
                            updated++;
                    }

                    t.Commit();
                }

                var selectedRoomIdValues = targetRows.Select(r => RevitCompat.GetElementIdValue(r.RoomId)).ToHashSet();

                // 建立跳過集合：已有同類型粉刷面的房間不需重建
                var skipWallRoomIds    = new HashSet<long>();
                var skipFloorRoomIds   = new HashSet<long>();
                var skipCeilingRoomIds = new HashSet<long>();
                BuildSkipSets(targetRows, skipWallRoomIds, skipFloorRoomIds, skipCeilingRoomIds);

                using (var t = new Transaction(doc, "清除舊粉刷面"))
                {
                    t.Start();
                    RemoveGeneratedFinishElementsForRooms(selectedRoomIdValues, skipWallRoomIds, skipFloorRoomIds, skipCeilingRoomIds);
                    t.Commit();
                }

                var settings = BuildGeometryUpdateSettings(targetRows);
                settings.SkipWallForRoomIds    = skipWallRoomIds;
                settings.SkipFloorForRoomIds   = skipFloorRoomIds;
                settings.SkipCeilingForRoomIds = skipCeilingRoomIds;

                using (var t = new Transaction(doc, "更新房間粉刷面"))
                {
                    t.Start();

                    var writer = new ValueWriter(_uiDoc);
                    writer.EnsureSharedParameters();

                    var generator = new GeometryGenerator(_uiDoc);
                    var genResults = generator.GenerateForRooms(settings);

                    // 生成後二次去重：同房間/同類型的重疊或重複粉刷面清理，避免疊層。
                    var removedDupCount = CleanupDuplicateManagedFinishesForRooms(selectedRoomIdValues);

                    writer.UpdateValues(settings);

                    t.Commit();

                    // 生成完成後：若有未完整房間，彈出狀態視窗
                    var incompleteCount = genResults?.RoomStatuses.Values.Count(s => s.FailedOperations > 0) ?? 0;
                    if (genResults != null && incompleteCount > 0)
                    {
                        var targetRoomIds = targetRows.Select(r => r.RoomId).Distinct(new ElementIdComparer()).ToList();
                        var statusWin = new ExecutionStatusWindow(_uiDoc, targetRoomIds, genResults, focusIncomplete: true)
                        {
                            Owner = this
                        };
                        statusWin.Show();
                    }

                    if (removedDupCount > 0)
                    {
                        txtStatus.Text = $"已清理重複粉刷面 {removedDupCount} 個。";
                    }
                }

                // 生成完成後，將實際使用的 TypeId 回寫到 row
                // 下次包出時就直接用 row.TypeId > 0，不再依賴 combo 當前狀態
                foreach (var row in targetRows)
                {
                    if (row.WallTypeId     <= 0) row.WallTypeId     = GetEffectiveWallTypeId(row);
                    if (row.FloorTypeId    <= 0) row.FloorTypeId    = GetEffectiveFloorTypeId(row);
                    if (row.CeilingTypeId  <= 0) row.CeilingTypeId  = GetEffectiveCeilingTypeId(row);
                    if (row.SkirtingTypeId <= 0) row.SkirtingTypeId = GetEffectiveSkirtingTypeId(row);
                }

                // 自動建立 Revit 內建明細表
                string scheduleMsg = string.Empty;
                try
                {
                    RevitScheduleBuilder.CreateOrUpdate(doc);
                    scheduleMsg = "\n已自動建立/更新「AR_粉刷明細表」明細表。";
                }
                catch (Exception exSched)
                {
                    scheduleMsg = $"\n（明細表建立失敗：{exSched.Message}）";
                }

                LoadCurrentRoomParameterStatus(applyModelValuesToRows: true, showCompletionMessage: false);
                var mixedInfo = skippedMixedRows.Count > 0
                    ? $"\n已略過混合品類 {skippedMixedRows.Count} 間（保留手動調整）。"
                    : string.Empty;

                txtStatus.Text = $"已套用下拉設定並更新粉刷面，更新 {updated} 間。{scheduleMsg}{mixedInfo}";
                MessageBox.Show($"套用並更新粉刷面完成。\n已更新 {updated} 間房間。{mixedInfo}{scheduleMsg}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"套用取代失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Public wrappers called by ExternalEvent handlers ──────────────
        public void SyncRoomsInternal()
        {
            LoadRooms();
            LoadCurrentRoomParameterStatus(applyModelValuesToRows: true, showCompletionMessage: true);
        }
        public void ApplyAndUpdateInternal()        => ApplyReplacementToModel();
        public void PickRoomsInternal()             => PickRooms();
        public void FocusRoomsInternal()            => FocusRoomsFromGrid();
        public void AutoJoinWallsInternal()         => AutoJoinWalls();
        public void AlignWallsToColumnsInternal()   => AlignWallsToColumns();
        public void ToggleFinishWallJoinsInternal() => ToggleFinishWallJoins();

        private void FocusRoomsFromGrid()
        {
            var picked = _roomRows.Where(r => r.IsSelected).Select(r => r.RoomId).Distinct().ToList();
            if (picked.Count == 0)
            {
                picked = dgRooms.SelectedItems.OfType<RoomFinishRow>().Select(r => r.RoomId).Distinct().ToList();
            }

            if (picked.Count == 0)
            {
                MessageBox.Show("請先勾選或選取清單中的房間。", "回查模型", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var wasVisible = IsVisible;
            try
            {
                if (wasVisible) Hide();
                _uiDoc.Selection.SetElementIds(picked);
                _uiDoc.ShowElements(picked);
                txtStatus.Text = $"已回查模型並定位 {picked.Count} 間房間。";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"回查模型失敗：{ex.Message}";
                MessageBox.Show($"回查模型失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (wasVisible)
                {
                    Show();
                    Activate();
                }
            }
        }

        public void ClearArFinishParamsInternal()
        {
            var doc = _uiDoc.Document;
            var selectedIds = _uiDoc.Selection.GetElementIds()?.ToList() ?? new List<ElementId>();
            var useSelection = selectedIds.Count > 0;

            var message = useSelection
                ? $"將清除目前選取 {selectedIds.Count} 個元素中「非房間裝修生成元素」上的 AR 裝修參數值。\n\n已生成粉刷面會自動保留，不會清除其 AR_RoomId / 驗算參數。\n\n是否繼續？"
                : "目前未選取元素。\n\n將掃描牆、樓板、天花、一般模型，清除「非 AR 裝修元素」上殘留的 AR 裝修參數值，避免結構牆/樓板被誤判。\n\n是否繼續？";

            if (MessageBox.Show(message, "清理AR參數", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var skippedManaged = 0;
            var targets = useSelection
                ? selectedIds.Select(id => doc.GetElement(id))
                    .Where(e => e != null)
                    .Where(e =>
                    {
                        if (IsProtectedGeneratedFinishElement(e))
                        {
                            skippedManaged++;
                            return false;
                        }
                        return HasAnyArFinishParameterValue(e);
                    })
                    .ToList()
                : CollectNonManagedElementsWithArParams(doc);

            if (targets.Count == 0)
            {
                var noTargetMsg = skippedManaged > 0
                    ? $"沒有找到需要清理的非粉刷元素。\n已保留 {skippedManaged} 個房間裝修生成粉刷面。"
                    : "沒有找到需要清理的元素。";
                MessageBox.Show(noTargetMsg, "清理AR參數", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int clearedElements = 0;
            int clearedValues = 0;

            using (var t = new Transaction(doc, "清理 AR 裝修參數"))
            {
                t.Start();

                foreach (var element in targets)
                {
                    var count = ClearArFinishParameterValues(element, clearStableMarker: true);
                    if (count > 0)
                    {
                        clearedElements++;
                        clearedValues += count;
                    }
                }

                t.Commit();
            }

            LoadCurrentRoomParameterStatus(applyModelValuesToRows: false, showCompletionMessage: false);
            txtStatus.Text = $"已清理 {clearedElements} 個元素、{clearedValues} 個 AR 參數值；保留 {skippedManaged} 個生成粉刷面。";
            MessageBox.Show(
                $"清理完成。\n已清理 {clearedElements} 個元素、{clearedValues} 個 AR 參數值。" +
                (skippedManaged > 0 ? $"\n已保留 {skippedManaged} 個房間裝修生成粉刷面。" : string.Empty),
                "清理AR參數", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void UpdateValuesOnlyInternal()
        {
            if (!ValidateInputs()) return;

            var targetRows = GetDistinctSelectedRows();
            var doc = _uiDoc.Document;

            try
            {
                using (var t = new Transaction(doc, "更新房間共享參數"))
                {
                    t.Start();
                    var writer = new ValueWriter(_uiDoc);
                    writer.EnsureSharedParameters();
                    var settings = BuildGeometryUpdateSettings(targetRows);
                    settings.GenerateGeometry = false;
                    settings.UpdateValues     = true;
                    writer.UpdateValues(settings);
                    t.Commit();
                }

                LoadCurrentRoomParameterStatus(applyModelValuesToRows: false, showCompletionMessage: false);
                txtStatus.Text = $"已更新 {targetRows.Count} 間房間的參數。";
                MessageBox.Show(
                    $"參數更新完成。\n已更新 {targetRows.Count} 間房間。",
                    "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新參數失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private FinishSettings BuildGeometryUpdateSettings(IList<RoomFinishRow> selectedRows)
        {
            var settings = new FinishSettings
            {
                GenerateGeometry = true,
                UpdateValues = true,
                SetValuesForGeometry = chkSetValuesGeom.IsChecked == true,
                SetValuesForRooms = chkSetValuesRooms.IsChecked == true,
                SkipDoorsForSkirting = true,
                SkipWindowsForSkirting = true,
                SkipOpeningsForWalls = chkSkipOpeningsForWalls.IsChecked == true,
                SelectedFloorTypeId = BuildElementId(GetSelectedOptionId(cmbFloors)),
                SelectedCeilingTypeId = BuildElementId(GetSelectedOptionId(cmbCeilings)),
                SelectedWallTypeId = BuildElementId(GetSelectedOptionId(cmbWalls)),
                SelectedSkirtingTypeId = BuildElementId(GetSelectedOptionId(cmbSkirtings)),
                CeilingHeightMm = ParseDouble(txtCeilingHeight.Text, 2700),
                WallHeightMm = ParseDouble(txtWallHeight.Text, 3000),
                SkirtingHeightMm = ParseDouble(txtSkirtingHeight.Text, 100),
                TargetRoomIds = selectedRows.Select(x => x.RoomId).ToList(),
                RoomOverrides = selectedRows.Select(row => new RoomFinishOverride
                {
                    RoomId = RevitCompat.GetElementIdValue(row.RoomId),
                    WallTypeId = row.WallTypeId,
                    FloorTypeId = row.FloorTypeId,
                    CeilingTypeId = row.CeilingTypeId,
                    SkirtingTypeId = row.SkirtingTypeId,
                    WallHeightMm = row.WallHeightMm,
                    CeilingHeightMm = row.CeilingHeightMm
                }).ToList()
            };

            settings.WallOffsetMm = Math.Max(0, settings.WallHeightMm - settings.CeilingHeightMm);

            if (cmbBoundary.SelectedItem is ComboBoxItem boundaryItem)
            {
                var tag = (boundaryItem.Tag as string) ?? "InnerFinish";
                settings.BoundaryMode = tag == "Centerline"
                    ? FloorBoundaryMode.Centerline
                    : tag == "OuterFinish"
                        ? FloorBoundaryMode.OuterFinish
                        : FloorBoundaryMode.InnerFinish;
            }

            return settings;
        }

        private void RemoveGeneratedFinishElementsForRooms(ISet<long> roomIdValues,
            HashSet<long> skipWallRoomIds = null,
            HashSet<long> skipFloorRoomIds = null,
            HashSet<long> skipCeilingRoomIds = null)
        {
            if (roomIdValues == null || roomIdValues.Count == 0)
                return;

            var doc = _uiDoc.Document;
            var categories = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_GenericModel
            };

            var idsToDelete = new List<ElementId>();

            foreach (var category in categories)
            {
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var element in elements)
                {
                    if (!IsProtectedGeneratedFinishElement(element))
                        continue;

                    var roomParam = element.LookupParameter("房間ID(AR_RoomId)") ?? element.LookupParameter("房間ID") ?? element.LookupParameter("AR_RoomId");
                    if (roomParam == null)
                        continue;

                    var roomId = roomParam.StorageType switch
                    {
                        StorageType.Integer => roomParam.AsInteger(),
                        StorageType.String => int.TryParse(roomParam.AsString(), out var parsed) ? parsed : 0,
                        _ => 0
                    };
                    if (roomId <= 0 || !roomIdValues.Contains(roomId))
                        continue;

                    // 若此房間在該類別的跳過名單中，保留元素不刪除
                    bool skip = category switch
                    {
                        BuiltInCategory.OST_Walls    => skipWallRoomIds?.Contains(roomId)    == true,
                        BuiltInCategory.OST_Floors   => skipFloorRoomIds?.Contains(roomId)   == true,
                        BuiltInCategory.OST_Ceilings => skipCeilingRoomIds?.Contains(roomId) == true,
                        _                            => false
                    };
                    if (skip) continue;

                    idsToDelete.Add(element.Id);
                }
            }

            if (idsToDelete.Any())
            {
                doc.Delete(idsToDelete);
            }
        }

        private void BuildSkipSets(IList<RoomFinishRow> targetRows,
            HashSet<long> skipWall, HashSet<long> skipFloor, HashSet<long> skipCeiling)
        {
            var doc = _uiDoc.Document;
            var wallTargets    = targetRows.ToDictionary(r => RevitCompat.GetElementIdValue(r.RoomId), r => r.WallTypeId);
            var floorTargets   = targetRows.ToDictionary(r => RevitCompat.GetElementIdValue(r.RoomId), r => r.FloorTypeId);
            var ceilingTargets = targetRows.ToDictionary(r => RevitCompat.GetElementIdValue(r.RoomId), r => r.CeilingTypeId);

            var foundWall    = new Dictionary<long, HashSet<long>>();
            var foundFloor   = new Dictionary<long, HashSet<long>>();
            var foundCeiling = new Dictionary<long, HashSet<long>>();

            var catChecks = new[]
            {
                (BuiltInCategory.OST_Walls,    wallTargets,    foundWall),
                (BuiltInCategory.OST_Floors,   floorTargets,   foundFloor),
                (BuiltInCategory.OST_Ceilings, ceilingTargets, foundCeiling),
            };

            foreach (var (cat, targets, found) in catChecks)
            {
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var elem in elements)
                {
                    if (!IsTrustedModelFinishElementForSync(elem))
                        continue;

                    var rp = elem.LookupParameter("房間ID(AR_RoomId)")
                          ?? elem.LookupParameter("房間ID")
                          ?? elem.LookupParameter("AR_RoomId");
                    if (rp == null) continue;

                    long roomId = rp.StorageType == StorageType.Integer
                        ? rp.AsInteger()
                        : long.TryParse(rp.AsString(), out var p) ? p : 0;
                    if (roomId <= 0 || !targets.ContainsKey(roomId)) continue;

                    long typeId = RevitCompat.GetElementIdValue(elem.GetTypeId());
                    if (!found.ContainsKey(roomId)) found[roomId] = new HashSet<long>();
                    found[roomId].Add(typeId);
                }
            }

            foreach (var row in targetRows)
            {
                long roomId = RevitCompat.GetElementIdValue(row.RoomId);
                // 牆高會影響幾何，不能只靠 Type 判斷可略過。
                // 為確保「更新粉刷面」在牆高變更時一定生效，牆面一律不加入 skipWall。

                if (row.FloorTypeId > 0
                    && foundFloor.TryGetValue(roomId, out var ft) && ft.Count > 0
                    && ft.All(t => t == row.FloorTypeId))
                    skipFloor.Add(roomId);

                // 天花高度會影響幾何，不能只靠 Type 判斷可略過。
                // 為確保天花高度調整一定更新，天花一律不加入 skipCeiling。
            }
        }

        private List<RoomFinishRow> GetDistinctSelectedRows()
        {
            // 同一房間可能在清單出現多列；更新時只執行一次，避免重覆生面造成重疊。
            var result = new List<RoomFinishRow>();
            var seen = new HashSet<long>();
            foreach (var row in _roomRows.Where(r => r.IsSelected))
            {
                var id = RevitCompat.GetElementIdValue(row.RoomId);
                if (id <= 0 || !seen.Add(id))
                    continue;
                result.Add(row);
            }
            return result;
        }

        private sealed class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
                => RevitCompat.GetElementIdValue(x) == RevitCompat.GetElementIdValue(y);

            public int GetHashCode(ElementId obj)
                => RevitCompat.GetElementIdValue(obj).GetHashCode();
        }

        private void InitFilters()
        {
            cmbStatusFilter.ItemsSource = new List<string> { "全部房間", "可產生", "未完成" };
            cmbStatusFilter.SelectedIndex = 0;
        }

        private void ReloadLevelFilterItems()
        {
            var levels = _roomRows.Select(x => x.Level).Distinct().OrderBy(x => x).ToList();
            levels.Insert(0, "全部樓層");
            cmbLevelFilter.ItemsSource = levels;
            cmbLevelFilter.SelectedIndex = 0;
        }

        private void ApplyFilters()
        {
            if (_roomRowsView == null) return;

            var searchText = (txtSearchRooms.Text ?? "").Trim();
            var selectedLevel = cmbLevelFilter.SelectedItem as string;
            var selectedStatus = cmbStatusFilter.SelectedItem as string;

            _roomRowsView.Filter = item =>
            {
                var row = item as RoomFinishRow;
                if (row == null) return false;

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var match = (row.Name ?? "").IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (row.Number ?? "").IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!match) return false;
                }

                if (!string.IsNullOrWhiteSpace(selectedLevel) && selectedLevel != "全部樓層" && row.Level != selectedLevel)
                    return false;

                if (selectedStatus == "可產生" && !row.Status.StartsWith("✓"))
                    return false;

                if (selectedStatus == "未完成" && row.Status.StartsWith("✓"))
                    return false;

                if (chkOnlySelected.IsChecked == true && !row.IsSelected)
                    return false;

                return true;
            };

            _roomRowsView.Refresh();

            // 更新篩選筆數顯示
            if (txtFilterCount != null)
            {
                int visible = _roomRowsView.Cast<object>().Count();
                txtFilterCount.Text = $"{visible} / {_roomRows.Count} 間";
            }
        }

        private void ClearFilters()
        {
            txtSearchRooms.Text = "";
            cmbLevelFilter.SelectedIndex = 0;
            cmbStatusFilter.SelectedIndex = 0;
            chkOnlySelected.IsChecked = false;
            ApplyFilters();
        }

        private static bool TryResolveTypeIdByName(string typeName, IEnumerable<TypeOption> options, out long id)
        {
            id = -1;
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            var trimmed = typeName.Trim();
            if (ContainsMultipleTypeTokens(trimmed))
                return false;

            var optList = options.ToList();

            // 1. 完全匹配（優先）
            var target = optList.FirstOrDefault(x =>
                string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase));

            // 2. 前綴匹配：儲存值 "W1" 應能對應選項 "W1-裝飾板"
            if (target == null)
            {
                var prefixMatches = optList
                    .Where(x => x.Name.StartsWith(trimmed + "-", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 前綴有多個候選（例如 W1 同時命中 W1-... 與 W1-A-...）時不自動判定，避免誤判。
                if (prefixMatches.Count == 1)
                    target = prefixMatches[0];
            }

            if (target == null)
                return false;

            id = target.Id;
            return true;
        }

        private void DgRooms_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // 僅在「生成狀態」欄雙擊時顯示細節，避免影響其他欄位編輯操作
                var header = dgRooms.CurrentColumn?.Header?.ToString() ?? string.Empty;
                if (!string.Equals(header, "生成狀態", StringComparison.Ordinal))
                    return;

                if (dgRooms.SelectedItem is not RoomFinishRow row)
                    return;

                var roomId = RevitCompat.GetElementIdValue(row.RoomId);
                _modelFinishIndex ??= BuildModelFinishDataIndex(_uiDoc.Document);
                _modelFinishIndex.TryGetValue(roomId, out var md);

                string okText(bool need, bool ok) => !need ? "未設定" : (ok ? "成功" : "失敗");

                var needWall = row.WallTypeId > 0;
                var needFloor = row.FloorTypeId > 0;
                var needCeiling = row.CeilingTypeId > 0;
                var needSkirting = row.SkirtingTypeId > 0;

                var wallOk = !needWall || (md != null && md.WallAreaM2 > 0);
                var floorOk = !needFloor || (md != null && md.FloorAreaM2 > 0);
                var ceilingOk = !needCeiling || (md != null && md.CeilingAreaM2 > 0);
                var skirtingOk = !needSkirting || (md != null && md.SkirtingLengthM > 0);

                var wallType = GetTypeNameById(WallTypeOptions, row.WallTypeId);
                var floorType = GetTypeNameById(FloorTypeOptions, row.FloorTypeId);
                var ceilingType = GetTypeNameById(CeilingTypeOptions, row.CeilingTypeId);
                var skirtingType = GetTypeNameById(SkirtingTypeOptions, row.SkirtingTypeId);

                var detail = new StringBuilder();
                detail.AppendLine($"房間：{row.Name} ({row.Number})");
                detail.AppendLine($"樓層：{row.Level}");
                detail.AppendLine();
                detail.AppendLine($"牆面：{okText(needWall, wallOk)}    設定：{(string.IsNullOrWhiteSpace(wallType) ? "（不設定）" : wallType)}");
                detail.AppendLine($"樓板：{okText(needFloor, floorOk)}    設定：{(string.IsNullOrWhiteSpace(floorType) ? "（不設定）" : floorType)}");
                detail.AppendLine($"天花：{okText(needCeiling, ceilingOk)}    設定：{(string.IsNullOrWhiteSpace(ceilingType) ? "（不設定）" : ceilingType)}");
                detail.AppendLine($"踢腳板：{okText(needSkirting, skirtingOk)}  設定：{(string.IsNullOrWhiteSpace(skirtingType) ? "（不設定）" : skirtingType)}");

                if (md != null)
                {
                    detail.AppendLine();
                    detail.AppendLine($"模型量測：牆 {md.WallAreaM2:0.###} m²｜地 {md.FloorAreaM2:0.###} m²｜天 {md.CeilingAreaM2:0.###} m²｜踢腳板 {md.SkirtingLengthM:0.###} m");
                }

                var title = wallOk && floorOk && ceilingOk && skirtingOk ? "生成狀態明細（成功）" : "生成狀態明細（未完整）";
                MessageBox.Show(detail.ToString(), title, MessageBoxButton.OK, (wallOk && floorOk && ceilingOk && skirtingOk) ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"讀取生成狀態明細失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowModelDifferenceReport()
        {
            try
            {
                var targetRows = _roomRows.Where(r => r.IsSelected).ToList();
                if (!targetRows.Any())
                    targetRows = dgRooms.Items.OfType<RoomFinishRow>().ToList();
                if (!targetRows.Any())
                    targetRows = _roomRows.ToList();

                _modelFinishIndex = BuildModelFinishDataIndex(_uiDoc.Document);
                foreach (var row in _roomRows)
                {
                    var roomId = RevitCompat.GetElementIdValue(row.RoomId);
                    _modelFinishIndex.TryGetValue(roomId, out var md);
                    UpdateModelSyncStatus(row, md);
                }

                var diffs = BuildModelDifferenceRows(targetRows);
                dgRooms.Items.Refresh();

                ShowModelDifferenceWindow(diffs);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"檢查模型差異失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<ModelDifferenceRow> BuildModelDifferenceRows(IList<RoomFinishRow> targetRows)
        {
            var rows = new List<ModelDifferenceRow>();

            void Add(RoomFinishRow room, string severity, string item, string setting, string model, string detail)
            {
                rows.Add(new ModelDifferenceRow
                {
                    RoomId = RevitCompat.GetElementIdValue(room.RoomId),
                    Severity = severity,
                    RoomNumber = room.Number,
                    RoomName = room.Name,
                    Level = room.Level,
                    Item = item,
                    RoomSetting = string.IsNullOrWhiteSpace(setting) ? "（不設定）" : setting,
                    ModelCurrent = string.IsNullOrWhiteSpace(model) ? "（無）" : model,
                    Detail = detail,
                    CheckViewName = BuildCheckViewName(room)
                });
            }

            string TypeNames(IEnumerable<TypeOption> options, IEnumerable<long> ids)
                => JoinTypeNamesByIds(options, ids);

            bool HasType(HashSet<long> ids, long targetId)
                => targetId <= 0 || ids.Contains(targetId);

            void CheckType(RoomFinishRow room, ModelFinishData md, string item, long settingId,
                IEnumerable<TypeOption> options, HashSet<long> modelTypeIds, double modelQuantity, string unit)
            {
                var settingName = GetTypeNameById(options, settingId);
                var modelNames = TypeNames(options, modelTypeIds);

                if (settingId > 0)
                {
                    if (md == null || modelQuantity <= 0)
                    {
                        Add(room, "缺少模型", item, settingName, modelNames, $"房間有設定 {item}，但模型沒有對應的 {unit}。");
                        return;
                    }

                    if (!HasType(modelTypeIds, settingId))
                    {
                        Add(room, "不一致", item, settingName, modelNames, $"模型已生成 {item}，但 Type 與房間設定不同。");
                        return;
                    }

                    if (modelTypeIds.Count > 1)
                    {
                        Add(room, "混合型", item, settingName, modelNames, $"同一房間有多種 {item} Type；設定 Type 有出現在模型中。");
                    }
                }
                else if (md != null && modelQuantity > 0)
                {
                    Add(room, "模型未同步", item, settingName, modelNames, $"房間未設定 {item}，但模型已有 {unit}。");
                }
            }

            var targetMap = targetRows
                .GroupBy(r => RevitCompat.GetElementIdValue(r.RoomId))
                .ToDictionary(g => g.Key, g => g.First());
            AddOwnershipAnomalyRows(targetMap, Add);
            var ownershipIssueRoomIds = new HashSet<long>(rows
                .Where(r => r.Severity == "歸戶異常" || r.Severity == "未歸戶")
                .Select(r => r.RoomId));

            foreach (var row in targetRows.Distinct().OrderBy(r => r.Level).ThenBy(r => r.Number).ThenBy(r => r.Name))
            {
                var roomId = RevitCompat.GetElementIdValue(row.RoomId);
                _modelFinishIndex.TryGetValue(roomId, out var md);
                var beforeCount = rows.Count;

                CheckType(row, md, "牆面", row.WallTypeId, WallTypeOptions,
                    md?.WallTypeIds ?? new HashSet<long>(), md?.WallAreaM2 ?? 0, "牆面粉刷面");

                if (row.WallTypeId > 0 && md != null && md.WallAreaM2 > 0 && md.WallHeightMm > 0)
                {
                    var delta = Math.Abs(row.WallHeightMm - md.WallHeightMm);
                    if (delta > 5.0)
                    {
                        Add(row, "不一致", "牆高",
                            $"{row.WallHeightMm:0.#} mm",
                            $"{md.WallHeightMm:0.#} mm",
                            $"房間管理牆高與模型裝修牆實際不連續高度差異 {delta:0.#} mm。");
                    }
                }

                CheckType(row, md, "樓板", row.FloorTypeId, FloorTypeOptions,
                    md?.FloorTypeIds ?? new HashSet<long>(), md?.FloorAreaM2 ?? 0, "樓板粉刷面");

                CheckType(row, md, "天花", row.CeilingTypeId, CeilingTypeOptions,
                    md?.CeilingTypeIds ?? new HashSet<long>(), md?.CeilingAreaM2 ?? 0, "天花粉刷面");

                CheckType(row, md, "踢腳板", row.SkirtingTypeId, SkirtingTypeOptions,
                    md?.SkirtingTypeIds ?? new HashSet<long>(), md?.SkirtingLengthM ?? 0, "踢腳板");

                if (rows.Count == beforeCount && !ownershipIssueRoomIds.Contains(roomId))
                {
                    Add(row, "通過", "總覽",
                        BuildRoomSettingSummary(row),
                        BuildModelCurrentSummary(md),
                        "房間設定與模型粉刷面 Type、生成狀態及裝修牆高檢查通過。可建立 AR_Check 3D 視圖作為驗算成果。");
                }
            }

            return rows
                .OrderBy(r => SeverityRank(r.Severity))
                .ThenBy(r => r.Level)
                .ThenBy(r => r.RoomNumber)
                .ThenBy(r => r.Item)
                .ToList();
        }

        private void AddOwnershipAnomalyRows(
            Dictionary<long, RoomFinishRow> targetMap,
            Action<RoomFinishRow, string, string, string, string, string> add)
        {
            var doc = _uiDoc.Document;
            var cats = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_GenericModel
            };

            foreach (var cat in cats)
            {
                foreach (var elem in new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements())
                {
                    if (!FinishingElementGuard.IsManagedFinishingElement(elem))
                        continue;

                    var taggedRoomId = ReadRoomIdFromElement(elem);
                    var spatialRoomIds = FindRoomIdsBySpatialQuery(elem);
                    var elemLabel = $"{elem.Category?.Name ?? "Element"} {elem.Id}";

                    if (taggedRoomId > 0 && targetMap.TryGetValue(taggedRoomId, out var taggedRow))
                    {
                        if (spatialRoomIds.Count == 1 && !spatialRoomIds.Contains(taggedRoomId))
                        {
                            var spatialRoom = doc.GetElement(RevitCompat.CreateElementId(spatialRoomIds.First())) as Room;
                            add(taggedRow, "歸戶異常", "房間歸戶",
                                $"AR_RoomId={taggedRow.Number}",
                                spatialRoom != null ? $"{spatialRoom.Number} {spatialRoom.Name}" : spatialRoomIds.First().ToString(),
                                $"{elemLabel} 的 AR_RoomId 指向本房間，但幾何取樣位於其他房間，可能為複製/移動後未更新參數。");
                        }
                        else if (spatialRoomIds.Count > 1)
                        {
                            add(taggedRow, "歸戶異常", "房間歸戶",
                                $"AR_RoomId={taggedRow.Number}",
                                string.Join(",", spatialRoomIds),
                                $"{elemLabel} 幾何取樣命中多間房間，驗算不應任意歸戶。");
                        }
                    }
                    else if (taggedRoomId <= 0 && spatialRoomIds.Count == 1
                             && targetMap.TryGetValue(spatialRoomIds.First(), out var spatialRow))
                    {
                        add(spatialRow, "未歸戶", "房間歸戶",
                            "（無 AR_RoomId）",
                            $"{spatialRow.Number} {spatialRow.Name}",
                            $"{elemLabel} 空間判斷屬於本房間，但缺少 AR_RoomId；報表可提示人工確認，避免交付明細漏算。");
                    }
                }
            }
        }

        private static int SeverityRank(string severity)
        {
            return severity switch
            {
                "歸戶異常" => 0,
                "不一致" => 0,
                "缺少模型" => 1,
                "未歸戶" => 1,
                "模型未同步" => 2,
                "混合型" => 3,
                "通過" => 8,
                _ => 9
            };
        }

        private string BuildRoomSettingSummary(RoomFinishRow row)
        {
            var parts = new List<string>();
            if (row.WallTypeId > 0) parts.Add($"牆:{GetTypeNameById(WallTypeOptions, row.WallTypeId)} / {row.WallHeightMm:0.#}mm");
            if (row.FloorTypeId > 0) parts.Add($"地:{GetTypeNameById(FloorTypeOptions, row.FloorTypeId)}");
            if (row.CeilingTypeId > 0) parts.Add($"天:{GetTypeNameById(CeilingTypeOptions, row.CeilingTypeId)} / {row.CeilingHeightMm:0.#}mm");
            if (row.SkirtingTypeId > 0) parts.Add($"踢:{GetTypeNameById(SkirtingTypeOptions, row.SkirtingTypeId)}");
            return parts.Any() ? string.Join("｜", parts) : "（未設定）";
        }

        private string BuildModelCurrentSummary(ModelFinishData md)
        {
            if (md == null)
                return "（無模型粉刷面）";

            var parts = new List<string>();
            if (md.WallAreaM2 > 0) parts.Add($"牆:{JoinTypeNamesByIds(WallTypeOptions, md.WallTypeIds)} / {md.WallHeightMm:0.#}mm / {md.WallAreaM2:0.###}m²");
            if (md.FloorAreaM2 > 0) parts.Add($"地:{JoinTypeNamesByIds(FloorTypeOptions, md.FloorTypeIds)} / {md.FloorAreaM2:0.###}m²");
            if (md.CeilingAreaM2 > 0) parts.Add($"天:{JoinTypeNamesByIds(CeilingTypeOptions, md.CeilingTypeIds)} / {md.CeilingAreaM2:0.###}m²");
            if (md.ManualFaceAreaM2 > 0) parts.Add($"手動面:{md.ManualFaceAreaM2:0.###}m²");
            if (md.SkirtingLengthM > 0) parts.Add($"踢:{JoinTypeNamesByIds(SkirtingTypeOptions, md.SkirtingTypeIds)} / {md.SkirtingLengthM:0.###}m");
            return parts.Any() ? string.Join("｜", parts) : "（無模型粉刷面）";
        }

        private void ShowModelDifferenceWindow(IList<ModelDifferenceRow> diffs)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                RowHeight = 30,
                ColumnHeaderHeight = 34,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = System.Windows.Media.Brushes.Gainsboro,
                ItemsSource = diffs
            };

            grid.Columns.Add(new DataGridTextColumn { Header = "狀態", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.Severity)), Width = 90 });
            grid.Columns.Add(new DataGridTextColumn { Header = "房間編號", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.RoomNumber)), Width = 100 });
            grid.Columns.Add(new DataGridTextColumn { Header = "房間名稱", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.RoomName)), Width = 220 });
            grid.Columns.Add(new DataGridTextColumn { Header = "樓層", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.Level)), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = "項目", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.Item)), Width = 90 });
            grid.Columns.Add(new DataGridTextColumn { Header = "房間設定", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.RoomSetting)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "模型現況", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.ModelCurrent)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "檢核視圖", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.CheckViewName)), Width = 180 });
            grid.Columns.Add(new DataGridTextColumn { Header = "說明", Binding = new System.Windows.Data.Binding(nameof(ModelDifferenceRow.Detail)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });

            var summary = new TextBlock
            {
                Text = BuildVerificationSummaryText(diffs),
                Foreground = System.Windows.Media.Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var closeButton = new Button
            {
                Content = "關閉",
                Width = 96,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var createAllButton = new Button
            {
                Content = "建立全部報表房間視圖",
                Width = 150,
                Height = 34,
                Margin = new Thickness(0, 8, 8, 0)
            };

            var createSelectedButton = new Button
            {
                Content = "建立選取列視圖",
                Width = 120,
                Height = 34,
                Margin = new Thickness(0, 8, 8, 0)
            };

            var openSelectedButton = new Button
            {
                Content = "開啟選取列視圖",
                Width = 120,
                Height = 34,
                Margin = new Thickness(0, 8, 8, 0)
            };

            var exportButton = new Button
            {
                Content = "匯出驗算報表",
                Width = 120,
                Height = 34,
                Margin = new Thickness(0, 8, 8, 0)
            };

            var buttonPanel = new DockPanel();
            DockPanel.SetDock(closeButton, Dock.Right);
            buttonPanel.Children.Add(closeButton);
            buttonPanel.Children.Add(createAllButton);
            buttonPanel.Children.Add(createSelectedButton);
            buttonPanel.Children.Add(openSelectedButton);
            buttonPanel.Children.Add(exportButton);

            var panel = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(summary, Dock.Top);
            DockPanel.SetDock(buttonPanel, Dock.Bottom);
            panel.Children.Add(summary);
            panel.Children.Add(buttonPanel);
            panel.Children.Add(grid);

            var window = new Window
            {
                Title = "模型差異檢查",
                Owner = this,
                Width = 1180,
                Height = 620,
                MinWidth = 960,
                MinHeight = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = panel
            };
            closeButton.Click += (s, e) => window.Close();
            createAllButton.Click += (s, e) =>
            {
                QueueCheckViews(diffs.Select(x => x.RoomId), activateFirst: false);
            };
            createSelectedButton.Click += (s, e) =>
            {
                var selected = grid.SelectedItems.OfType<ModelDifferenceRow>().ToList();
                QueueCheckViews((selected.Any() ? selected : diffs).Select(x => x.RoomId), activateFirst: false);
            };
            openSelectedButton.Click += (s, e) =>
            {
                var selected = grid.SelectedItems.OfType<ModelDifferenceRow>().ToList();
                QueueCheckViews((selected.Any() ? selected : diffs.Take(1)).Select(x => x.RoomId), activateFirst: true);
            };
            exportButton.Click += (s, e) => ExportVerificationReportToXlsx(diffs);
            window.ShowDialog();
        }

        private static string BuildVerificationSummaryText(IList<ModelDifferenceRow> rows)
        {
            var totalRooms = rows.Select(x => x.RoomId).Distinct().Count();
            var passRows = rows.Count(x => x.Severity == "通過");
            var issueRows = rows.Count - passRows;
            return $"驗算報表：{totalRooms} 間房間，通過 {passRows} 間，異常 {issueRows} 項。可建立 AR_Check 3D 視圖作為核對成果；裝修牆高讀取 Revit 實際不連續高度。";
        }

        private void ExportVerificationReportToXlsx(IList<ModelDifferenceRow> rows)
        {
            try
            {
                if (rows == null || rows.Count == 0)
                {
                    MessageBox.Show("目前沒有可匯出的驗算資料。", "匯出驗算報表", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var defaultName = $"AR_房間裝修驗算報表_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                var dlg = new SaveFileDialog
                {
                    Title = "匯出房間裝修驗算報表",
                    FileName = defaultName,
                    Filter = "Excel 活頁簿 (*.xlsx)|*.xlsx",
                    AddExtension = true,
                    DefaultExt = ".xlsx"
                };

                if (dlg.ShowDialog(this) != true)
                    return;

                using (var document = SpreadsheetDocument.Create(dlg.FileName, SpreadsheetDocumentType.Workbook))
                {
                    var workbookPart = document.AddWorkbookPart();
                    workbookPart.Workbook = new Workbook();

                    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                    var sheetData = new SheetData();
                    worksheetPart.Worksheet = new Worksheet(sheetData);

                    var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                    sheets.Append(new Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = 1,
                        Name = "驗算報表"
                    });

                    var checkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
                    var roomCount = rows.Select(x => x.RoomId).Distinct().Count();
                    var passCount = rows.Count(x => x.Severity == "通過");
                    var issueCount = rows.Count - passCount;

                    sheetData.Append(CreateReportRow("AR 房間裝修驗算報表"));
                    sheetData.Append(CreateReportRow($"檢核時間：{checkedAt}"));
                    sheetData.Append(CreateReportRow($"檢核房間：{roomCount}｜通過：{passCount}｜異常項：{issueCount}"));
                    sheetData.Append(new Row());

                    sheetData.Append(CreateReportRow(
                        "狀態", "房間編號", "房間名稱", "樓層", "項目",
                        "房間設定", "模型現況", "檢核視圖", "說明", "檢核時間"));

                    foreach (var row in rows.OrderBy(r => SeverityRank(r.Severity)).ThenBy(r => r.Level).ThenBy(r => r.RoomNumber).ThenBy(r => r.Item))
                    {
                        sheetData.Append(CreateReportRow(
                            row.Severity,
                            row.RoomNumber,
                            row.RoomName,
                            row.Level,
                            row.Item,
                            row.RoomSetting,
                            row.ModelCurrent,
                            row.CheckViewName,
                            row.Detail,
                            checkedAt));
                    }

                    BuildScheduleAcceptanceSheet(workbookPart, sheets, 2, rows, checkedAt);

                    workbookPart.Workbook.Save();
                }

                txtStatus.Text = $"已匯出房間裝修驗算報表：{dlg.FileName}";
                MessageBox.Show($"驗算報表已匯出：\n{dlg.FileName}", "匯出完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出驗算報表失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static Row CreateReportRow(params string[] values)
        {
            var row = new Row();
            foreach (var value in values)
                row.Append(CreateTextCell(value ?? string.Empty));
            return row;
        }

        private void BuildScheduleAcceptanceSheet(WorkbookPart workbookPart, Sheets sheets, uint sheetId, IList<ModelDifferenceRow> reportRows, string checkedAt)
        {
            if (_modelFinishIndex == null)
                _modelFinishIndex = BuildModelFinishDataIndex(_uiDoc.Document);

            var roomIds = reportRows.Select(x => x.RoomId).Where(x => x > 0).Distinct().ToHashSet();
            var rows = _roomRows
                .Where(r => roomIds.Contains(RevitCompat.GetElementIdValue(r.RoomId)))
                .GroupBy(r => RevitCompat.GetElementIdValue(r.RoomId))
                .Select(g => g.First())
                .OrderBy(r => GetExportLevelSortKey(r.Level))
                .ThenBy(r => r.Level, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Number, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId,
                Name = "明細表驗收"
            });

            sheetData.Append(CreateReportRow("交付明細表驗收"));
            sheetData.Append(CreateReportRow($"檢核時間：{checkedAt}"));
            sheetData.Append(CreateReportRow("用途：核對交付施工明細表中各房間/材料/數量是否與模型實測一致。"));
            sheetData.Append(new Row());
            sheetData.Append(CreateReportRow(
                "驗收狀態", "樓層", "房間編號", "房間名稱", "項目", "材料/代號",
                "模型實測量", "明細表應列量", "單位", "差異", "容許值", "檢核依據", "檢核視圖"));

            foreach (var row in rows)
            {
                var roomId = RevitCompat.GetElementIdValue(row.RoomId);
                _modelFinishIndex.TryGetValue(roomId, out var md);
                GetRoomMetrics(row, out var roomAreaSqm, out var wallAreaSqm, out var floorAreaSqm, out var ceilingAreaSqm);
                var viewName = BuildCheckViewName(row);

                foreach (var entry in GetWallTypeAreaEntries(row, wallAreaSqm))
                {
                    var modelQty = md?.WallAreaByTypeM2.TryGetValue(entry.TypeId, out var q) == true ? q : 0;
                    AppendAcceptanceRow(sheetData, row, "牆面", entry.TypeName, modelQty, entry.AreaM2, "㎡", 0.01, "HOST_AREA_COMPUTED / 牆面 Type 分組", viewName);
                }

                if (GetExportFloorTypeId(row) > 0)
                {
                    var typeName = GetTypeNameById(FloorTypeOptions, GetExportFloorTypeId(row));
                    var modelQty = md?.FloorAreaM2 ?? 0;
                    AppendAcceptanceRow(sheetData, row, "地坪", typeName, modelQty, floorAreaSqm, "㎡", 0.01, "HOST_AREA_COMPUTED / 房間地坪", viewName);
                }

                if (GetExportCeilingTypeId(row) > 0)
                {
                    var typeName = GetTypeNameById(CeilingTypeOptions, GetExportCeilingTypeId(row));
                    var modelQty = md?.CeilingAreaM2 ?? 0;
                    AppendAcceptanceRow(sheetData, row, "天花", typeName, modelQty, ceilingAreaSqm, "㎡", 0.01, "HOST_AREA_COMPUTED / 房間天花", viewName);
                }

                if (GetExportSkirtingTypeId(row) > 0)
                {
                    var typeName = GetTypeNameById(SkirtingTypeOptions, GetExportSkirtingTypeId(row));
                    var modelQty = md?.SkirtingLengthM ?? 0;
                    var reportQty = GetSkirtingLengthEstimateM(row);
                    AppendAcceptanceRow(sheetData, row, "踢腳板", typeName, modelQty, reportQty, "m", 0.01, "LocationCurve 長度 / 高度 ≤ 500mm 識別", viewName);
                }

                if (md != null && md.ManualFaceAreaM2 > 0)
                {
                    foreach (var kv in md.ManualFaceAreaByMaterialM2.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        AppendAcceptanceRow(
                            sheetData,
                            row,
                            "手動裝修面",
                            kv.Key,
                            kv.Value,
                            kv.Value,
                            "㎡",
                            0.01,
                            "一般模型/DirectShape 面積參數；多點空間歸戶；樓梯間/斜面手動面獨立列示",
                            viewName);
                    }
                }

                if (row.WallTypeId <= 0 && row.FloorTypeId <= 0 && row.CeilingTypeId <= 0 && row.SkirtingTypeId <= 0)
                {
                    sheetData.Append(CreateReportRow("未設定", row.Level, row.Number, row.Name, "總覽", "（未設定）", "", "", "", "", "", "房間未設定裝修材料", viewName));
                }
            }
        }

        private void AppendAcceptanceRow(
            SheetData sheetData,
            RoomFinishRow room,
            string item,
            string material,
            double modelQuantity,
            double reportQuantity,
            string unit,
            double tolerance,
            string basis,
            string checkViewName)
        {
            var delta = Math.Abs(modelQuantity - reportQuantity);
            var status = delta <= tolerance ? "通過" : "不一致";
            sheetData.Append(CreateReportRow(
                status,
                room.Level,
                room.Number,
                room.Name,
                item,
                string.IsNullOrWhiteSpace(material) ? "（無）" : material,
                modelQuantity > 0 ? modelQuantity.ToString("0.###", CultureInfo.InvariantCulture) : "0",
                reportQuantity > 0 ? reportQuantity.ToString("0.###", CultureInfo.InvariantCulture) : "0",
                unit,
                delta.ToString("0.###", CultureInfo.InvariantCulture),
                tolerance.ToString("0.###", CultureInfo.InvariantCulture),
                basis,
                checkViewName));
        }

        private void QueueCheckViews(IEnumerable<long> roomIds, bool activateFirst)
        {
            var ids = roomIds.Where(x => x > 0).Distinct().ToList();
            if (!ids.Any())
            {
                MessageBox.Show("沒有可建立驗算視圖的房間。", "驗算視圖", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _pendingCheckViewRoomIds = ids;
            _activateFirstCheckView = activateFirst;
            _createCheckViewsEvent?.Raise();
            txtStatus.Text = $"已排程建立 {ids.Count} 間房間的驗算視圖。";
        }

        public void CreateCheckViewsInternal()
        {
            var roomIds = (_pendingCheckViewRoomIds ?? new List<long>()).Where(x => x > 0).Distinct().ToList();
            _pendingCheckViewRoomIds = new List<long>();
            if (!roomIds.Any())
                return;

            var doc = _uiDoc.Document;
            _modelFinishIndex = BuildModelFinishDataIndex(doc);

            var targetRows = _roomRows
                .Where(r => roomIds.Contains(RevitCompat.GetElementIdValue(r.RoomId)))
                .GroupBy(r => RevitCompat.GetElementIdValue(r.RoomId))
                .Select(g => g.First())
                .ToList();

            var diffs = BuildModelDifferenceRows(targetRows);
            var diffsByRoom = diffs.GroupBy(d => d.RoomId).ToDictionary(g => g.Key, g => g.ToList());

            var viewType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);
            if (viewType == null)
            {
                MessageBox.Show("找不到 3D 視圖類型，無法建立驗算視圖。", "驗算視圖", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var createdViews = new List<View3D>();
            using (var t = new Transaction(doc, "建立房間裝修驗算視圖"))
            {
                t.Start();
                foreach (var row in targetRows)
                {
                    var roomId = RevitCompat.GetElementIdValue(row.RoomId);
                    var room = doc.GetElement(row.RoomId) as Room;
                    if (room == null)
                        continue;

                    _modelFinishIndex.TryGetValue(roomId, out var md);
                    diffsByRoom.TryGetValue(roomId, out var roomDiffs);
                    roomDiffs ??= new List<ModelDifferenceRow>();

                    var viewName = BuildCheckViewName(row);
                    var view = FindOrCreateCheckView(doc, viewType.Id, viewName);
                    ApplyCheckViewSectionBox(doc, view, room, md);
                    ApplyCheckViewOverrides(doc, view, md, roomDiffs);
                    createdViews.Add(view);
                }
                t.Commit();
            }

            if (createdViews.Any())
            {
                var first = createdViews.First();
                var focusIds = new HashSet<ElementId>();
                var firstRoomId = roomIds.FirstOrDefault();
                if (_modelFinishIndex.TryGetValue(firstRoomId, out var md))
                    foreach (var id in GetAllFinishElementIds(md)) focusIds.Add(id);

                if (focusIds.Any())
                    _uiDoc.Selection.SetElementIds(focusIds.ToList());

                if (_activateFirstCheckView)
                    _uiDoc.RequestViewChange(first);

                txtStatus.Text = $"已建立/更新 {createdViews.Count} 個 AR_Check 驗算視圖。";
                MessageBox.Show($"已建立/更新 {createdViews.Count} 個房間裝修驗算視圖。", "驗算視圖", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string BuildCheckViewName(RoomFinishRow row)
        {
            string Clean(string s)
            {
                var invalid = new[] { '\\', '/', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
                var text = string.IsNullOrWhiteSpace(s) ? "Room" : s.Trim();
                foreach (var ch in invalid)
                    text = text.Replace(ch, '_');
                return text.Length > 36 ? text.Substring(0, 36) : text;
            }

            return $"AR_Check_{Clean(row.Number)}_{Clean(row.Name)}";
        }

        private static View3D FindOrCreateCheckView(Document doc, ElementId viewTypeId, string viewName)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, viewName, StringComparison.Ordinal));
            if (existing != null)
                return existing;

            var view = View3D.CreateIsometric(doc, viewTypeId);
            view.Name = viewName;
            return view;
        }

        private void ApplyCheckViewSectionBox(Document doc, View3D view, Room room, ModelFinishData md)
        {
            var boxes = new List<BoundingBoxXYZ>();
            var roomBox = room.get_BoundingBox(null);
            if (roomBox != null) boxes.Add(roomBox);

            if (md != null)
            {
                foreach (var id in GetAllFinishElementIds(md))
                {
                    var elem = doc.GetElement(id);
                    var box = elem?.get_BoundingBox(null);
                    if (box != null) boxes.Add(box);
                }
            }

            if (!boxes.Any())
                return;

            var min = new XYZ(boxes.Min(b => b.Min.X), boxes.Min(b => b.Min.Y), boxes.Min(b => b.Min.Z));
            var max = new XYZ(boxes.Max(b => b.Max.X), boxes.Max(b => b.Max.Y), boxes.Max(b => b.Max.Z));
            var pad = 1000.0 / 304.8;
            var zPad = 500.0 / 304.8;
            var section = new BoundingBoxXYZ
            {
                Min = new XYZ(min.X - pad, min.Y - pad, min.Z - zPad),
                Max = new XYZ(max.X + pad, max.Y + pad, max.Z + zPad)
            };

            view.SetSectionBox(section);
        }

        private void ApplyCheckViewOverrides(Document doc, View3D view, ModelFinishData md, IList<ModelDifferenceRow> diffs)
        {
            if (md == null)
                return;

            var allIds = GetAllFinishElementIds(md).Where(id => doc.GetElement(id) != null).ToList();
            foreach (var id in allIds)
                view.SetElementOverrides(id, BuildOverride(doc, new Autodesk.Revit.DB.Color(80, 170, 90), 20));

            var red = BuildOverride(doc, new Autodesk.Revit.DB.Color(230, 60, 50), 0);
            var orange = BuildOverride(doc, new Autodesk.Revit.DB.Color(245, 145, 35), 0);
            var purple = BuildOverride(doc, new Autodesk.Revit.DB.Color(150, 80, 210), 0);

            foreach (var diff in diffs)
            {
                var ogs = diff.Severity == "混合型" ? orange
                    : diff.Severity == "模型未同步" ? purple
                    : red;

                foreach (var id in GetElementIdsForDifference(md, diff.Item))
                {
                    if (doc.GetElement(id) != null)
                        view.SetElementOverrides(id, ogs);
                }
            }
        }

        private static OverrideGraphicSettings BuildOverride(Document doc, Autodesk.Revit.DB.Color color, int transparency)
        {
            var ogs = new OverrideGraphicSettings();
            var solidFillId = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern()?.IsSolidFill == true)
                ?.Id;

            ogs.SetProjectionLineColor(color);
            if (solidFillId != null)
            {
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetCutForegroundPatternId(solidFillId);
            }
            ogs.SetSurfaceForegroundPatternColor(color);
            ogs.SetCutForegroundPatternColor(color);
            ogs.SetSurfaceTransparency(Math.Max(0, Math.Min(90, transparency)));
            return ogs;
        }

        private static IEnumerable<ElementId> GetElementIdsForDifference(ModelFinishData md, string item)
        {
            if (md == null)
                yield break;

            IEnumerable<ElementId> ids = item switch
            {
                "牆面" => md.WallElementIds,
                "牆高" => md.WallElementIds,
                "樓板" => md.FloorElementIds,
                "天花" => md.CeilingElementIds,
                "手動裝修面" => md.ManualFaceElementIds,
                "踢腳板" => md.SkirtingElementIds,
                _ => Enumerable.Empty<ElementId>()
            };

            foreach (var id in ids)
                yield return id;
        }

        private static IEnumerable<ElementId> GetAllFinishElementIds(ModelFinishData md)
        {
            if (md == null)
                yield break;

            foreach (var id in md.WallElementIds) yield return id;
            foreach (var id in md.FloorElementIds) yield return id;
            foreach (var id in md.CeilingElementIds) yield return id;
            foreach (var id in md.ManualFaceElementIds) yield return id;
            foreach (var id in md.SkirtingElementIds) yield return id;
        }

        private int CleanupDuplicateManagedFinishesForRooms(ISet<long> roomIdValues)
        {
            if (roomIdValues == null || roomIdValues.Count == 0)
                return 0;

            var doc = _uiDoc.Document;
            var idsToDelete = new HashSet<ElementId>();

            foreach (var cat in new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings })
            {
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(FinishingElementGuard.IsManagedFinishingElement)
                    .ToList();

                var groups = new Dictionary<string, List<Element>>();
                foreach (var e in elements)
                {
                    var roomId = ReadRoomIdValue(e);
                    if (roomId <= 0 || !roomIdValues.Contains(roomId))
                        continue;

                    var typeId = RevitCompat.GetElementIdValue(e.GetTypeId());
                    var key = BuildDuplicateKey(cat, e, roomId, typeId);
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!groups.TryGetValue(key, out var list))
                    {
                        list = new List<Element>();
                        groups[key] = list;
                    }
                    list.Add(e);
                }

                foreach (var kv in groups.Where(x => x.Value.Count > 1))
                {
                    var keep = kv.Value
                        .OrderByDescending(GetElementMetricForKeep)
                        .ThenBy(x => RevitCompat.GetElementIdValue(x.Id))
                        .FirstOrDefault();
                    foreach (var e in kv.Value)
                    {
                        if (keep == null || e.Id != keep.Id)
                            idsToDelete.Add(e.Id);
                    }
                }
            }

            if (idsToDelete.Count > 0)
                doc.Delete(idsToDelete.ToList());

            return idsToDelete.Count;
        }

        private static long ReadRoomIdValue(Element element)
        {
            var p = element.LookupParameter("房間ID(AR_RoomId)")
                    ?? element.LookupParameter("房間ID")
                    ?? element.LookupParameter("AR_RoomId");
            if (p == null) return 0;

            if (p.StorageType == StorageType.Integer)
                return p.AsInteger();

            if (p.StorageType == StorageType.String && long.TryParse((p.AsString() ?? "").Trim(), out var v))
                return v;

            return 0;
        }

        private static string BuildDuplicateKey(BuiltInCategory cat, Element e, long roomId, long typeId)
        {
            var bb = e.get_BoundingBox(null);
            if (bb == null) return null;

            var cx = Math.Round((bb.Min.X + bb.Max.X) * 0.5, 3);
            var cy = Math.Round((bb.Min.Y + bb.Max.Y) * 0.5, 3);
            var cz = Math.Round((bb.Min.Z + bb.Max.Z) * 0.5, 3);

            if (cat == BuiltInCategory.OST_Walls && e.Location is LocationCurve lc)
            {
                var p0 = lc.Curve.GetEndPoint(0);
                var p1 = lc.Curve.GetEndPoint(1);
                // 牆面去重只看平面線段（XY）+ 房間 + 類型，忽略高度差，避免舊270/新290並存。
                var a = $"{Math.Round(p0.X, 3)}|{Math.Round(p0.Y, 3)}";
                var b = $"{Math.Round(p1.X, 3)}|{Math.Round(p1.Y, 3)}";
                var pair = string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
                var levelId = RevitCompat.GetElementIdValue(e.LevelId);
                return $"{roomId}|{typeId}|W|L{levelId}|{pair}";
            }

            var area = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
            area = Math.Round(area, 3);
            var c = cat == BuiltInCategory.OST_Floors ? "F" : "C";
            return $"{roomId}|{typeId}|{c}|{cx}|{cy}|{cz}|{area}";
        }

        private static double GetElementMetricForKeep(Element e)
        {
            if (e.Location is LocationCurve lc)
            {
                // 牆面優先保留實際面積較大者（通常是最新高度設定後的元素）。
                var area = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
                if (area > 0) return area;
                return lc.Curve.Length;
            }
            return e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
        }

        private static long ResolveRoomParameterTypeId(string typeName, IEnumerable<TypeOption> options)
            => TryResolveTypeIdByName(typeName, options, out var id) ? id : -1;

        private static bool ContainsMultipleTypeTokens(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var separators = new[] { '、', ';', '；', '|', '\n', '\r' };
            return value.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .Count() > 1;
        }

        private void PickRooms()
        {
            var wasVisible = IsVisible;
            try
            {
                // 讓 Revit 視圖可直接操作，避免被目前視窗焦點干擾
                if (wasVisible)
                    Hide();

                var refs = _uiDoc.Selection.PickObjects(ObjectType.Element, new RoomSelectionFilter(), "請選擇房間");
                var pickedIds = refs.Select(r => r.ElementId).ToHashSet();

                foreach (var row in _roomRows)
                {
                    row.IsSelected = pickedIds.Contains(row.RoomId);
                }

                UpdatePickedCount();
                ApplyFilters();
                txtStatus.Text = $"已選取 {pickedIds.Count} 間房間。";
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                txtStatus.Text = "已取消模型選房。";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"模型選房失敗：{ex.Message}";
            }
            finally
            {
                if (wasVisible)
                {
                    Show();
                    Activate();
                    tabMain.SelectedIndex = 1; // 選完後切到房間管理頁
                }
            }
        }

        private void CollectViewModel(bool generateGeometry)
        {
            var selectedRows = _roomRows.Where(x => x.IsSelected).ToList();

            var vm = new FinishSettings
            {
                GenerateGeometry = generateGeometry,
                UpdateValues = !generateGeometry || chkSetValuesGeom.IsChecked == true || chkSetValuesRooms.IsChecked == true,
                SetValuesForGeometry = chkSetValuesGeom.IsChecked == true,
                SetValuesForRooms = chkSetValuesRooms.IsChecked == true,
                SkipDoorsForSkirting = true,
                SkipWindowsForSkirting = true,
                SkipOpeningsForWalls = chkSkipOpeningsForWalls.IsChecked == true,
                SelectedFloorTypeId = BuildElementId(GetSelectedOptionId(cmbFloors)),
                SelectedCeilingTypeId = BuildElementId(GetSelectedOptionId(cmbCeilings)),
                SelectedWallTypeId = BuildElementId(GetSelectedOptionId(cmbWalls)),
                SelectedSkirtingTypeId = BuildElementId(GetSelectedOptionId(cmbSkirtings)),
                CeilingHeightMm = ParseDouble(txtCeilingHeight.Text, 2700),
                WallHeightMm = ParseDouble(txtWallHeight.Text, 3000),
                SkirtingHeightMm = ParseDouble(txtSkirtingHeight.Text, 100),
                TargetRoomIds = selectedRows.Select(x => x.RoomId).ToList()
            };

            vm.WallOffsetMm = Math.Max(0, vm.WallHeightMm - vm.CeilingHeightMm);

            if (cmbBoundary.SelectedItem is ComboBoxItem boundaryItem)
            {
                var tag = (boundaryItem.Tag as string) ?? "InnerFinish";
                vm.BoundaryMode = tag == "Centerline"
                    ? FloorBoundaryMode.Centerline
                    : tag == "OuterFinish"
                        ? FloorBoundaryMode.OuterFinish
                        : FloorBoundaryMode.InnerFinish;
            }

            vm.RoomOverrides = selectedRows.Select(row => new RoomFinishOverride
            {
                RoomId = RevitCompat.GetElementIdValue(row.RoomId),
                WallTypeId = row.WallTypeId,
                FloorTypeId = row.FloorTypeId,
                CeilingTypeId = row.CeilingTypeId,
                SkirtingTypeId = row.SkirtingTypeId,
                WallHeightMm = row.WallHeightMm,
                CeilingHeightMm = row.CeilingHeightMm
            }).ToList();

            ViewModel = vm;
        }

        private void LoadSettings()
        {
            try
            {
                var settings = FinishSettings.LoadFromFile();
                _loadedSettings = settings;

                txtCeilingHeight.Text = settings.CeilingHeightMm.ToString("F0");
                txtWallHeight.Text = settings.WallHeightMm.ToString("F0");
                txtSkirtingHeight.Text = settings.SkirtingHeightMm.ToString("F0");
                chkSetValuesGeom.IsChecked = settings.SetValuesForGeometry;
                chkSetValuesRooms.IsChecked = settings.SetValuesForRooms;
                chkSkipOpeningsForWalls.IsChecked = settings.SkipOpeningsForWalls;

                SelectComboById(cmbWalls, settings.SelectedWallTypeId != null ? RevitCompat.GetElementIdValue(settings.SelectedWallTypeId) : -1);
                SelectComboById(cmbFloors, settings.SelectedFloorTypeId != null ? RevitCompat.GetElementIdValue(settings.SelectedFloorTypeId) : -1);
                SelectComboById(cmbCeilings, settings.SelectedCeilingTypeId != null ? RevitCompat.GetElementIdValue(settings.SelectedCeilingTypeId) : -1);
                SelectComboById(cmbSkirtings, settings.SelectedSkirtingTypeId != null ? RevitCompat.GetElementIdValue(settings.SelectedSkirtingTypeId) : -1);

                switch (settings.BoundaryMode)
                {
                    case FloorBoundaryMode.Centerline:
                        cmbBoundary.SelectedIndex = 1;
                        break;
                    case FloorBoundaryMode.OuterFinish:
                        cmbBoundary.SelectedIndex = 2;
                        break;
                    default:
                        cmbBoundary.SelectedIndex = 0;
                        break;
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"載入設定失敗: {ex.Message}";
            }
        }

        private void SaveDraftSettings()
        {
            try
            {
                if (!_roomRows.Any())
                    return;

                var draft = new FinishSettings
                {
                    GenerateGeometry = false,
                    UpdateValues = true,
                    SetValuesForGeometry = chkSetValuesGeom.IsChecked == true,
                    SetValuesForRooms = chkSetValuesRooms.IsChecked == true,
                    SkipDoorsForSkirting = true,
                    SkipWindowsForSkirting = true,
                    SkipOpeningsForWalls = chkSkipOpeningsForWalls.IsChecked == true,
                    SelectedFloorTypeId = BuildElementId(GetSelectedOptionId(cmbFloors)),
                    SelectedCeilingTypeId = BuildElementId(GetSelectedOptionId(cmbCeilings)),
                    SelectedWallTypeId = BuildElementId(GetSelectedOptionId(cmbWalls)),
                    SelectedSkirtingTypeId = BuildElementId(GetSelectedOptionId(cmbSkirtings)),
                    CeilingHeightMm = ParseDouble(txtCeilingHeight.Text, 2700),
                    WallHeightMm = ParseDouble(txtWallHeight.Text, 3000),
                    SkirtingHeightMm = ParseDouble(txtSkirtingHeight.Text, 100),
                    TargetRoomIds = _roomRows.Where(x => x.IsSelected).Select(x => x.RoomId).ToList(),
                    RoomOverrides = _roomRows.Select(row => new RoomFinishOverride
                    {
                        RoomId = RevitCompat.GetElementIdValue(row.RoomId),
                        WallTypeId = row.WallTypeId,
                        FloorTypeId = row.FloorTypeId,
                        CeilingTypeId = row.CeilingTypeId,
                        SkirtingTypeId = row.SkirtingTypeId,
                        WallHeightMm = row.WallHeightMm,
                        CeilingHeightMm = row.CeilingHeightMm
                    }).ToList()
                };

                draft.WallOffsetMm = Math.Max(0, draft.WallHeightMm - draft.CeilingHeightMm);

                if (cmbBoundary.SelectedItem is ComboBoxItem boundaryItem)
                {
                    var tag = (boundaryItem.Tag as string) ?? "InnerFinish";
                    draft.BoundaryMode = tag == "Centerline"
                        ? FloorBoundaryMode.Centerline
                        : tag == "OuterFinish"
                            ? FloorBoundaryMode.OuterFinish
                            : FloorBoundaryMode.InnerFinish;
                }

                draft.SaveToFile();
            }
            catch
            {
                // 草稿儲存失敗不阻斷視窗關閉
            }
        }

        private void SaveSettings()
        {
            try
            {
                ViewModel?.SaveToFile();
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"儲存設定失敗: {ex.Message}";
            }
        }

        private bool ValidateInputs()
        {
            var selectedRows = _roomRows.Where(x => x.IsSelected).ToList();
            if (!selectedRows.Any())
            {
                MessageBox.Show("請先在房間管理表中至少選擇一個房間。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var invalidHeightRows = selectedRows.Where(x => x.WallHeightMm <= 0 || x.CeilingHeightMm <= 0).ToList();
            if (invalidHeightRows.Any())
            {
                MessageBox.Show("選取房間中存在無效高度（牆高或天花高度 <= 0）。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var noFinishRows = selectedRows.Where(x => x.WallTypeId <= 0 && x.FloorTypeId <= 0 && x.CeilingTypeId <= 0).ToList();
            var hasSkirting = GetSelectedOptionId(cmbSkirtings) > 0;
            if (noFinishRows.Any() && !hasSkirting)
            {
                MessageBox.Show("有房間未設定牆/地板/天花任何裝修類型，且未設定踢腳板類型。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void AutoJoinWalls()
        {
            try
            {
                var selectedRoomIds = _roomRows.Where(x => x.IsSelected).Select(x => x.RoomId).ToList();
                if (!selectedRoomIds.Any())
                {
                    MessageBox.Show("請先選擇要接合的房間。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                using (var t = new Transaction(_uiDoc.Document, "修復牆端點並接合"))
                {
                    t.Start();
                    var generator = new GeometryGenerator(_uiDoc);

                    // Step 1：先修復結構牆端點對齊（修復端點跑掉導致房間偵測失敗）
                    var (alignFixed, alignFailed, alignErrors) =
                        generator.AlignWallEndpoints(selectedRoomIds, toleranceMm: 25.0);

                    // Step 2：再接合裝修牆面幾何
                    var joinResults = generator.AutoJoinExistingWalls(selectedRoomIds);

                    t.Commit();

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"▌ 端點對齊：修復 {alignFixed} 個（失敗 {alignFailed}）");
                    sb.AppendLine($"▌ 牆面接合：成功 {joinResults.SuccessCount} / {joinResults.TotalAttempts}");

                    var allErrors = alignErrors.Concat(joinResults.Errors).ToList();
                    if (allErrors.Any())
                    {
                        sb.AppendLine("\n錯誤（最多顯示 5 筆）：");
                        foreach (var err in allErrors.Take(5))
                            sb.AppendLine($"  · {err}");
                    }

                    MessageBox.Show(sb.ToString(), "修復牆面結果", MessageBoxButton.OK,
                        alignFixed > 0 || joinResults.SuccessCount > 0
                            ? MessageBoxImage.Information
                            : MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"自動接合牆面時發生錯誤: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AlignWallsToColumns()
        {
            try
            {
                var selectedRoomIds = _roomRows.Where(x => x.IsSelected).Select(x => x.RoomId).ToList();
                if (!selectedRoomIds.Any())
                {
                    MessageBox.Show("請先選擇要對齊的房間。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                using (var t = new Transaction(_uiDoc.Document, "結構牆端點對齊結構柱邊緣"))
                {
                    t.Start();
                    var generator = new GeometryGenerator(_uiDoc);
                    var (fixedCount, failedCount, errors) =
                        generator.AlignStructuralWallsToColumns(selectedRoomIds, toleranceMm: 200.0);
                    t.Commit();

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"▌ 端點對齊：成功 {fixedCount} 個（失敗 {failedCount}）");

                    if (errors.Any())
                    {
                        sb.AppendLine("\n訊息（最多顯示 5 筆）：");
                        foreach (var err in errors.Take(5))
                            sb.AppendLine($"  · {err}");
                    }

                    MessageBox.Show(sb.ToString(), "對齊結構柱邊 結果", MessageBoxButton.OK,
                        fixedCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"對齊結構柱邊時發生錯誤: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleFinishWallJoins()
        {
            try
            {
                var selectedRoomIds = _roomRows.Where(x => x.IsSelected).Select(x => x.RoomId).ToList();
                if (!selectedRoomIds.Any())
                {
                    MessageBox.Show("請先選擇要操作的房間。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    // 還原按鈕狀態
                    _finishWallJoinsAllowed = !_finishWallJoinsAllowed;
                    btnToggleWallJoins.Content = _finishWallJoinsAllowed ? "禁止端點接合" : "允許端點接合";
                    return;
                }

                bool allow = !_finishWallJoinsAllowed;
                var doc = _uiDoc.Document;
                int successCount = 0;
                int failCount = 0;

                // 收集選取房間範圍內的所有裝修牆
                var finishWalls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .Where(IsFinishWallCandidate)
                    .ToList();

                // 篩選屬於選取房間附近的牆（用房間 BoundingBox 過濾）
                var roomElements = selectedRoomIds
                    .Select(id => doc.GetElement(id))
                    .OfType<Autodesk.Revit.DB.Architecture.Room>()
                    .ToList();

                if (roomElements.Any())
                {
                    var roomBBs = roomElements
                        .Select(r => r.get_BoundingBox(null))
                        .Where(bb => bb != null)
                        .ToList();

                    finishWalls = finishWalls.Where(w =>
                    {
                        var wbb = w.get_BoundingBox(null);
                        if (wbb == null) return false;
                        const double expand = 0.5; // ~150mm
                        return roomBBs.Any(rbb =>
                            wbb.Min.X <= rbb.Max.X + expand && wbb.Max.X >= rbb.Min.X - expand &&
                            wbb.Min.Y <= rbb.Max.Y + expand && wbb.Max.Y >= rbb.Min.Y - expand);
                    }).ToList();
                }

                if (!finishWalls.Any())
                {
                    MessageBox.Show(
                        "選取房間範圍內找不到可切換端點接合的裝修牆。\n\n請先執行「自動產出裝修面」，或確認裝修牆已有房間 ID 標記。",
                        "端點接合切換", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using (var t = new Transaction(doc, allow ? "允許裝修牆端點接合" : "禁止裝修牆端點接合"))
                {
                    t.Start();
                    foreach (var wall in finishWalls)
                    {
                        try
                        {
                            if (allow)
                            {
                                WallUtils.AllowWallJoinAtEnd(wall, 0);
                                WallUtils.AllowWallJoinAtEnd(wall, 1);
                            }
                            else
                            {
                                WallUtils.DisallowWallJoinAtEnd(wall, 0);
                                WallUtils.DisallowWallJoinAtEnd(wall, 1);
                            }
                            successCount++;
                        }
                        catch { failCount++; }
                    }
                    t.Commit();
                }

                _finishWallJoinsAllowed = allow;
                btnToggleWallJoins.Content = _finishWallJoinsAllowed ? "禁止端點接合" : "允許端點接合";

                MessageBox.Show(
                    $"已{(allow ? "允許" : "禁止")} {successCount} 道裝修牆的端點接合" +
                    (failCount > 0 ? $"\n（失敗 {failCount} 道）" : string.Empty),
                    "端點接合切換", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                btnToggleWallJoins.Content = _finishWallJoinsAllowed ? "禁止端點接合" : "允許端點接合";
                MessageBox.Show($"切換端點接合時發生錯誤: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static List<Element> CollectNonManagedElementsWithArParams(Document doc)
        {
            var result = new List<Element>();
            var categories = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_GenericModel
            };

            foreach (var category in categories)
            {
                foreach (var element in new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements())
                {
                    if (IsProtectedGeneratedFinishElement(element))
                        continue;

                    if (HasAnyArFinishParameterValue(element))
                        result.Add(element);
                }
            }

            return result;
        }

        private static bool HasAnyArFinishParameterValue(Element element)
        {
            return ArFinishParameterNames.Any(name => HasParameterValue(element.LookupParameter(name)));
        }

        private static bool IsProtectedGeneratedFinishElement(Element element)
        {
            if (!FinishingElementGuard.IsSupportedFinishCategory(element))
                return false;

            if (element is DirectShape ds)
                return string.Equals(ds.ApplicationId, FinishingElementGuard.StableMarker, StringComparison.OrdinalIgnoreCase);

            return FinishingElementGuard.HasStableMarker(element);
        }

        private static bool IsTrustedModelFinishElementForSync(Element element)
        {
            // 同步模型只信任工具生成/標記過的粉刷面。
            // 單純殘留 AR_RoomId 的結構牆/樓板不再回填下拉欄位，避免讀到 AR_RC 等非粉刷牆型。
            return IsProtectedGeneratedFinishElement(element);
        }

        private static bool HasParameterValue(Autodesk.Revit.DB.Parameter parameter)
        {
            if (parameter == null)
                return false;

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return !string.IsNullOrWhiteSpace(parameter.AsString());
                case StorageType.Integer:
                    return parameter.AsInteger() != 0;
                case StorageType.Double:
                    return Math.Abs(parameter.AsDouble()) > 1e-9;
                case StorageType.ElementId:
                    return parameter.AsElementId() != ElementId.InvalidElementId;
                default:
                    return false;
            }
        }

        private static int ClearArFinishParameterValues(Element element, bool clearStableMarker)
        {
            var count = 0;
            foreach (var name in ArFinishParameterNames)
            {
                if (ClearParameterValue(element.LookupParameter(name)))
                    count++;
            }

            if (clearStableMarker)
            {
                var comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments != null && !comments.IsReadOnly)
                {
                    var existing = comments.AsString() ?? string.Empty;
                    var marker = FinishingElementGuard.StableMarker;
                    if (existing.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var cleaned = string.Join(" | ", existing
                            .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => !string.Equals(x, marker, StringComparison.OrdinalIgnoreCase)));
                        comments.Set(cleaned);
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool ClearParameterValue(Autodesk.Revit.DB.Parameter parameter)
        {
            if (parameter == null || parameter.IsReadOnly || !HasParameterValue(parameter))
                return false;

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    parameter.Set(string.Empty);
                    return true;
                case StorageType.Integer:
                    parameter.Set(0);
                    return true;
                case StorageType.Double:
                    parameter.Set(0.0);
                    return true;
                case StorageType.ElementId:
                    parameter.Set(ElementId.InvalidElementId);
                    return true;
                default:
                    return false;
            }
        }

        private static readonly string[] ArFinishParameterNames =
        {
            "房間ID(AR_RoomId)",
            "房間ID",
            "AR_RoomId",
            "房間名稱(AR_RoomNames)",
            "房間名稱",
            "AR_RoomNames",
            "房間編號(AR_RoomNumbers)",
            "房間編號",
            "AR_RoomNumbers",
            "AR_Summary",
            "AR_牆面塗層",
            "AR_樓板塗層",
            "AR_天花板塗層",
            "AR_天花板高度",
            "AR_踢腳板塗層",
            "牆面塗層",
            "樓板塗層",
            "天花板塗層",
            "天花板高度",
            "踢腳板塗層",
            "模板_主體ID",
            "材料名稱",
            "厚度",
            "面積"
        };

        private static Autodesk.Revit.DB.Parameter LookupRoomParameter(Room room, string preferredName, params string[] fallbackNames)
        {
            var parameter = room?.LookupParameter(preferredName);
            if (parameter != null)
                return parameter;

            if (fallbackNames != null)
            {
                foreach (var fallbackName in fallbackNames)
                {
                    parameter = room?.LookupParameter(fallbackName);
                    if (parameter != null)
                        return parameter;
                }
            }

            return null;
        }

        private static bool IsFinishWallCandidate(Wall wall)
        {
            if (wall == null)
                return false;

            return IsProtectedGeneratedFinishElement(wall);
        }

        private void ShowImportExportDialog()
        {
            var result = MessageBox.Show(
                "Yes：匯出目前房間設定為 Excel 檔\nNo：從 Excel/CSV 匯入房間設定",
                "匯入 / 匯出",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ExportRoomSettingsToExcel();
            }
            else if (result == MessageBoxResult.No)
            {
                ImportRoomSettingsFromFile();
            }
        }

        private void ExportRoomSettingsToExcel()
        {
            try
            {
                if (!_hasSyncedFromModelOnce)
                {
                    var syncFirst = MessageBox.Show(
                        "尚未執行「同步模型」，匯出內容可能與目前模型不一致。\n是否先按「同步模型」後再匯出？",
                        "匯入 / 匯出",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (syncFirst == MessageBoxResult.Yes)
                    {
                        MessageBox.Show("請先點擊「同步模型」，完成後再執行匯出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                var exportRows = GetExportRows();
                var saveDialog = new SaveFileDialog
                {
                    Title = "匯出房間裝修設定 (Excel)",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                    FileName = $"RoomFinishings_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };

                if (saveDialog.ShowDialog() != true)
                    return;

                System.Diagnostics.Debug.WriteLine($"[RoomFinish] 開始匯出房間設定，路徑：{saveDialog.FileName}，時間：{DateTime.Now:HH:mm:ss.fff}");
                ExportRoomSettingsToXlsx(saveDialog.FileName);
                var syncInfo = _lastModelSyncAt.HasValue ? $"（最近同步：{_lastModelSyncAt.Value:HH:mm:ss}）" : string.Empty;
                txtStatus.Text = $"已匯出 {exportRows.Count} 間房間到 Excel。{syncInfo}";
                System.Diagnostics.Debug.WriteLine($"[RoomFinish] 匯出完成，時間：{DateTime.Now:HH:mm:ss.fff}");
                MessageBox.Show("匯出完成（.xlsx）。", "匯入 / 匯出", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RoomFinish] 匯出失敗：{ex.Message}");
                MessageBox.Show($"匯出失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportRoomSettingsToXlsx(string filePath)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            var exportRows = GetExportRows();
            
            System.Diagnostics.Debug.WriteLine($"[RoomFinish] 匯出開始，房間數：{exportRows.Count}，時間：{DateTime.Now:HH:mm:ss.fff}");

            // 建立模型粉刷元素面積索引，優先使用實際面積取代幾何估算
            _modelFinishIndex = BuildModelFinishDataIndex(_uiDoc.Document);

            try
            {
            using (var document = SpreadsheetDocument.Create(filePath, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateExportStylesheet();
                stylesPart.Stylesheet.Save();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;
                BuildSettingsDetailSheet(workbookPart, sheets, ref sheetId, exportRows);
                BuildSettingsSummarySheet(workbookPart, sheets, ref sheetId, exportRows);
                BuildPracticalScheduleSheet(workbookPart, sheets, ref sheetId, exportRows);
                workbookPart.Workbook.Save();
                
                System.Diagnostics.Debug.WriteLine($"[RoomFinish] 匯出完成，路徑：{filePath}，時間：{DateTime.Now:HH:mm:ss.fff}");
            }
            }
            finally
            {
                _modelFinishIndex = null; // 匯出完成後清除索引
            }
        }

        private void BuildSettingsDetailSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, List<RoomFinishRow> exportRows)
        {
            var headers = new[]
            {
                "房間ID", "房間號碼", "房間名稱", "樓層", "已選取",
                "牆面類型ID", "牆面材料", "粉刷高度(mm)",
                "地坪類型ID", "地坪材料",
                "天花板類型ID", "天花板材料", "天花板高度(mm)",
                "踢腳板類型ID", "踢腳板材料", "踢腳板高度(mm)", "踢腳板長度估算(m)",
                "房間面積(m²)", "牆面積估算(m²)", "地坪面積估算(m²)", "天花面積估算(m²)", "狀態"
            };

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            var columns = new Columns(
                new Column { Min = 1, Max = 1, Width = 12, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 12, CustomWidth = true },
                new Column { Min = 3, Max = 3, Width = 20, CustomWidth = true },
                new Column { Min = 4, Max = 4, Width = 14, CustomWidth = true },
                new Column { Min = 5, Max = 5, Width = 10, CustomWidth = true },
                new Column { Min = 6, Max = 6, Width = 12, CustomWidth = true },
                new Column { Min = 7, Max = 7, Width = 24, CustomWidth = true },
                new Column { Min = 8, Max = 8, Width = 12, CustomWidth = true },
                new Column { Min = 9, Max = 9, Width = 12, CustomWidth = true },
                new Column { Min = 10, Max = 10, Width = 24, CustomWidth = true },
                new Column { Min = 11, Max = 11, Width = 12, CustomWidth = true },
                new Column { Min = 12, Max = 12, Width = 24, CustomWidth = true },
                new Column { Min = 13, Max = 13, Width = 12, CustomWidth = true },
                new Column { Min = 14, Max = 17, Width = 14, CustomWidth = true },
                new Column { Min = 18, Max = 21, Width = 14, CustomWidth = true },
                new Column { Min = 22, Max = 22, Width = 18, CustomWidth = true }
            );

            var sheetView = new SheetView { WorkbookViewId = 0U };
            sheetView.Append(new Pane
            {
                VerticalSplit = 1D,
                TopLeftCell = "A2",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            });

            var worksheet = new Worksheet();
            worksheet.Append(new SheetViews(sheetView));
            worksheet.Append(columns);
            worksheet.Append(sheetData);
            worksheetPart.Worksheet = worksheet;

            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = "明細表"
            });

            var headerRow = new Row();
            foreach (var header in headers)
                headerRow.Append(CreateTextCell(header, 2));
            sheetData.Append(headerRow);

            var exportedDataRowCount = 0;
            foreach (var row in exportRows)
            {
                GetRoomMetrics(row, out var roomAreaSqm, out var wallAreaEstimateSqm, out var floorAreaEstimateSqm, out var ceilingAreaEstimateSqm);

                var effWallId    = GetExportWallTypeId(row);
                var effFloorId   = GetExportFloorTypeId(row);
                var effCeilId    = GetExportCeilingTypeId(row);
                var effSkirtId   = GetExportSkirtingTypeId(row);
                var effSkirtHeightMm = GetExportSkirtingHeightMm(row);

                var wallEntries = GetWallTypeAreaEntries(row, wallAreaEstimateSqm);
                for (int i = 0; i < wallEntries.Count; i++)
                {
                    var wallEntry = wallEntries[i];
                    var wallTypeId = wallEntry.TypeId > 0 ? wallEntry.TypeId : effWallId;
                    var wallTypeName = !string.IsNullOrWhiteSpace(wallEntry.TypeName)
                        ? wallEntry.TypeName
                        : GetTypeNameById(WallTypeOptions, wallTypeId);
                    var isFirst = i == 0;

                    var dataRow = new Row();
                    dataRow.Append(CreateTextCell(RevitCompat.GetElementIdValue(row.RoomId).ToString(), 1));
                    dataRow.Append(CreateTextCell(row.Number, 1));
                    dataRow.Append(CreateTextCell(row.Name, 1));
                    dataRow.Append(CreateTextCell(row.Level, 1));
                    dataRow.Append(CreateNumberCell(row.IsSelected ? 1 : 0, 1));
                    dataRow.Append(wallTypeId > 0 ? CreateNumberCell(wallTypeId, 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(wallTypeName, 1));
                    dataRow.Append(CreateNumberCell(row.WallHeightMm, 1));
                    dataRow.Append(isFirst && effFloorId > 0 ? CreateNumberCell(effFloorId, 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(isFirst ? GetTypeNameById(FloorTypeOptions, effFloorId) : "", 1));
                    dataRow.Append(isFirst && effCeilId > 0 ? CreateNumberCell(effCeilId, 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(isFirst ? GetTypeNameById(CeilingTypeOptions, effCeilId) : "", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(row.CeilingHeightMm, 1) : CreateTextCell("", 1));
                    dataRow.Append(isFirst && effSkirtId > 0 ? CreateNumberCell(effSkirtId, 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(isFirst ? GetTypeNameById(SkirtingTypeOptions, effSkirtId) : "", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(effSkirtHeightMm, 1) : CreateTextCell("", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(Math.Round(GetSkirtingLengthEstimateM(row), 1), 1) : CreateTextCell("", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(Math.Round(roomAreaSqm, 2), 1) : CreateTextCell("", 1));
                    dataRow.Append(wallEntry.AreaM2 > 0 ? CreateNumberCell(Math.Round(wallEntry.AreaM2, 2), 1) : CreateTextCell("-", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(Math.Round(floorAreaEstimateSqm, 2), 1) : CreateTextCell("", 1));
                    dataRow.Append(isFirst ? CreateNumberCell(Math.Round(ceilingAreaEstimateSqm, 2), 1) : CreateTextCell("", 1));
                    dataRow.Append(CreateTextCell(row.Status, 1));
                    sheetData.Append(dataRow);
                    exportedDataRowCount++;
                }
            }

            var lastRow = Math.Max(exportedDataRowCount + 1, 1);
            worksheet.Append(new AutoFilter { Reference = $"A1:V{lastRow}" });
        }

        private void BuildSettingsSummarySheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, List<RoomFinishRow> exportRows)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var mergeCells = new MergeCells();

            var levels = exportRows.Select(x => x.Level ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetExportLevelSortKey)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!levels.Any())
                levels.Add("未分層");

            var sheetView = new SheetView { WorkbookViewId = 0U };
            sheetView.Append(new Pane
            {
                VerticalSplit = 2D,
                TopLeftCell = "A3",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            });

            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 48, CustomWidth = true });
            for (uint i = 2; i <= 12; i++)
                columns.Append(new Column { Min = i, Max = i, Width = 16, CustomWidth = true });

            var worksheet = new Worksheet();
            worksheet.Append(new SheetViews(sheetView));
            worksheet.Append(columns);
            worksheet.Append(sheetData);
            worksheet.Append(mergeCells);
            worksheetPart.Worksheet = worksheet;

            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = "統計表"
            });

            uint rowIndex = 1;
            var lastColumn = 1 + levels.Count + 1;
            var lastColumnName = GetExportColumnName(lastColumn);

            var titleRow = new Row { RowIndex = rowIndex };
            titleRow.Append(CreateTextCell("房間裝修設定總表", 7));
            sheetData.Append(titleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex++;

            var subtitleRow = new Row { RowIndex = rowIndex };
            subtitleRow.Append(CreateTextCell($"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}    房間數：{exportRows.Count}", 2));
            sheetData.Append(subtitleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex += 2;

            var sections = new List<(string Title, string Kind, uint Style)>
            {
                ("牆面材料明細", "Wall", 3),
                ("地坪材料明細", "Floor", 4),
                ("天花材料明細", "Ceiling", 5)
            };

            foreach (var section in sections)
            {
                BuildSettingsSummarySection(sheetData, mergeCells, ref rowIndex, levels, section.Title, section.Kind, section.Style, lastColumnName, exportRows);
                rowIndex += 1;
            }
        }

        private void BuildSettingsSummarySection(SheetData sheetData, MergeCells mergeCells, ref uint rowIndex, List<string> levels,
            string title, string kind, uint style, string lastColumnName, List<RoomFinishRow> exportRows)
        {
            var titleRow = new Row { RowIndex = rowIndex };
            titleRow.Append(CreateTextCell(title, style));
            sheetData.Append(titleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex++;

            var headerRow = new Row { RowIndex = rowIndex };
            headerRow.Append(CreateTextCell("樓層", 2));
            headerRow.Append(CreateTextCell("項目", 2));
            headerRow.Append(CreateTextCell("面積(㎡)", 2));
            sheetData.Append(headerRow);
            rowIndex++;

            var grandTotal = 0.0;

            foreach (var level in levels)
            {
                var levelLabel = NormalizeExportLevelLabel(level);
                var levelRows = exportRows
                    .Where(x => string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var groups = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var roomRow in levelRows)
                {
                    foreach (var entry in GetTypeAreaEntriesForSummary(roomRow, kind))
                    {
                        if (string.IsNullOrWhiteSpace(entry.TypeName) || entry.TypeName == "（不設定）")
                            continue;
                        groups[entry.TypeName] = (groups.TryGetValue(entry.TypeName, out var v) ? v : 0) + entry.AreaM2;
                    }
                }
                var orderedGroups = groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();

                if (!orderedGroups.Any())
                    continue;

                uint levelStartRow = rowIndex;
                var levelTotal = 0.0;

                foreach (var group in orderedGroups)
                {
                    var groupArea = group.Value;
                    var row = new Row { RowIndex = rowIndex };
                    row.Append(CreateTextCell(levelLabel, 1));
                    row.Append(CreateTextCell(group.Key, 1));
                    row.Append(CreateNumberCell(Math.Round(groupArea, 2), 1));
                    sheetData.Append(row);
                    rowIndex++;
                    levelTotal += groupArea;
                }

                if (rowIndex - levelStartRow > 1)
                    mergeCells.Append(new MergeCell { Reference = new StringValue($"A{levelStartRow}:A{rowIndex - 1}") });

                var levelSubtotalRow = new Row { RowIndex = rowIndex };
                levelSubtotalRow.Append(CreateTextCell(string.Empty, 2));
                levelSubtotalRow.Append(CreateTextCell($"{levelLabel} 小計", 2));
                levelSubtotalRow.Append(CreateNumberCell(Math.Round(levelTotal, 2), 2));
                sheetData.Append(levelSubtotalRow);
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

        private void BuildPracticalScheduleSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, List<RoomFinishRow> exportRows)
        {
            System.Diagnostics.Debug.WriteLine($"[BuildPracticalScheduleSheet] 開始，房間數：{exportRows.Count}");
            
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var mergeCells = new MergeCells();

            var sheetView = new SheetView { WorkbookViewId = 0U };
            sheetView.Append(new Pane
            {
                VerticalSplit = 3D,
                TopLeftCell = "A4",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            });

            var columns = new Columns(
                new Column { Min = 1,  Max = 1,  Width = 12, CustomWidth = true },  // 樓層
                new Column { Min = 2,  Max = 2,  Width = 12, CustomWidth = true },  // 房間編號
                new Column { Min = 3,  Max = 3,  Width = 30, CustomWidth = true },  // 房間名稱
                new Column { Min = 4,  Max = 4,  Width = 12, CustomWidth = true },  // 牆面代號
                new Column { Min = 5,  Max = 5,  Width = 56, CustomWidth = true },  // 牆面材料
                new Column { Min = 6,  Max = 6,  Width = 12, CustomWidth = true },  // 牆面積
                new Column { Min = 7,  Max = 7,  Width = 12, CustomWidth = true },  // 地坪代號
                new Column { Min = 8,  Max = 8,  Width = 48, CustomWidth = true },  // 地坪材料
                new Column { Min = 9,  Max = 9,  Width = 12, CustomWidth = true },  // 地面積
                new Column { Min = 10, Max = 10, Width = 12, CustomWidth = true },  // 天花代號
                new Column { Min = 11, Max = 11, Width = 48, CustomWidth = true },  // 天花材料
                new Column { Min = 12, Max = 12, Width = 12, CustomWidth = true },  // 天面積
                new Column { Min = 13, Max = 13, Width = 12, CustomWidth = true },  // 踢腳板代號
                new Column { Min = 14, Max = 14, Width = 36, CustomWidth = true },  // 踢腳板材料
                new Column { Min = 15, Max = 15, Width = 14, CustomWidth = true }   // 踢腳板長
            );

            var worksheet = new Worksheet();
            worksheet.Append(new SheetViews(sheetView));
            worksheet.Append(columns);
            worksheet.Append(sheetData);
            var autoFilter = new AutoFilter { Reference = "A4:O4" };
            worksheet.Append(autoFilter);
            worksheet.Append(mergeCells);
            worksheetPart.Worksheet = worksheet;

            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = "施工明細表"
            });

            uint rowIndex = 1;
            var titleRow = new Row { RowIndex = rowIndex };
            titleRow.Append(CreateTextCell("各樓層房間天地牆材料明細表", 7));
            sheetData.Append(titleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:O{rowIndex}") });
            rowIndex++;

            var totalRoomArea = exportRows.Sum(r =>
            {
                GetRoomMetrics(r, out var roomAreaSqm, out _, out _, out _);
                return roomAreaSqm;
            });
            var totalWallArea = exportRows.Sum(r =>
            {
                GetRoomMetrics(r, out _, out var wallArea, out _, out _);
                return wallArea;
            });
            var totalFloorArea = exportRows.Sum(r =>
            {
                GetRoomMetrics(r, out _, out _, out var floorArea, out _);
                return floorArea;
            });
            var totalCeilingArea = exportRows.Sum(r =>
            {
                GetRoomMetrics(r, out _, out _, out _, out var ceilingArea);
                return ceilingArea;
            });

            var totalSkirtingLength = exportRows.Sum(r => GetSkirtingLengthEstimateM(r));

            var subtitleRow = new Row { RowIndex = rowIndex };
            subtitleRow.Append(CreateTextCell($"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}    房間數：{exportRows.Count}    房間面積：{totalRoomArea:F2} ㎡", 2));
            sheetData.Append(subtitleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:O{rowIndex}") });
            rowIndex++;

            var summaryRow = new Row { RowIndex = rowIndex };
            summaryRow.Append(CreateTextCell($"牆面積：{totalWallArea:F2} ㎡ | 地坪面積：{totalFloorArea:F2} ㎡ | 天花面積：{totalCeilingArea:F2} ㎡ | 踢腳板總長：{totalSkirtingLength:F1} m", 2));
            sheetData.Append(summaryRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:O{rowIndex}") });
            rowIndex++;

            var headerRow = new Row { RowIndex = rowIndex };
            headerRow.Append(CreateTextCell("樓層", 2));
            headerRow.Append(CreateTextCell("房間編號", 2));
            headerRow.Append(CreateTextCell("房間名稱", 2));
            headerRow.Append(CreateTextCell("牆面代號", 2));
            headerRow.Append(CreateTextCell("牆面材料", 2));
            headerRow.Append(CreateTextCell("牆面積 (㎡)", 2));
            headerRow.Append(CreateTextCell("地坪代號", 2));
            headerRow.Append(CreateTextCell("地坪材料", 2));
            headerRow.Append(CreateTextCell("地面積 (㎡)", 2));
            headerRow.Append(CreateTextCell("天花代號", 2));
            headerRow.Append(CreateTextCell("天花材料", 2));
            headerRow.Append(CreateTextCell("天面積 (㎡)", 2));
            headerRow.Append(CreateTextCell("踢腳板代號", 2));
            headerRow.Append(CreateTextCell("踢腳板材料", 2));
            headerRow.Append(CreateTextCell("踢腳板長 (m)", 2));
            sheetData.Append(headerRow);
            rowIndex++;

            var groupedByLevel = exportRows
                .OrderBy(r => GetExportLevelSortKey(r.Level))
                .ThenBy(r => r.Level, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Number, StringComparer.OrdinalIgnoreCase)
                .GroupBy(r => r.Level ?? "未分層")
                .ToList();

            foreach (var levelGroup in groupedByLevel)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildPracticalScheduleSheet] 樓層：{levelGroup.Key}，房間數：{levelGroup.Count()}");
                
                var levelTitleRow = new Row { RowIndex = rowIndex };
                levelTitleRow.Append(CreateTextCell($"{NormalizeExportLevelLabel(levelGroup.Key)} 房間明細", 3));
                sheetData.Append(levelTitleRow);
                mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:O{rowIndex}") });
                rowIndex++;

                foreach (var roomRow in levelGroup)
                {
                    GetRoomMetrics(roomRow, out var roomAreaSqm, out var wallAreaSqm, out var floorAreaSqm, out var ceilingAreaSqm);
                    var skirtingLengthM = GetSkirtingLengthEstimateM(roomRow);
                    System.Diagnostics.Debug.WriteLine($"[BuildPracticalScheduleSheet] 房間：{roomRow.Number}，牆面積：{wallAreaSqm:F2}，地坪面積：{floorAreaSqm:F2}");

                    var wallEntries      = GetWallTypeAreaEntries(roomRow, wallAreaSqm);
                    var floorFullName    = GetExportTypeNameSummary(roomRow, "Floor");
                    var ceilFullName     = GetExportTypeNameSummary(roomRow, "Ceiling");
                    var skirtFullName    = GetTypeNameById(SkirtingTypeOptions, GetExportSkirtingTypeId(roomRow));

                    for (int i = 0; i < wallEntries.Count; i++)
                    {
                        var wallFullName = wallEntries[i].TypeName;
                        var wallArea = wallEntries[i].AreaM2;
                        var isFirst = i == 0;

                        var dataRow = new Row { RowIndex = rowIndex, Height = 32D, CustomHeight = true };
                        dataRow.Append(CreateTextCell(roomRow.Level ?? "未分層", 1));
                        dataRow.Append(CreateTextCell(roomRow.Number, 1));
                        dataRow.Append(CreateTextCell(roomRow.Name, 8));
                        dataRow.Append(CreateTextCell(SplitTypeCode(wallFullName),  9));  // 牆面代號
                        dataRow.Append(CreateTextCell(SplitTypeName(wallFullName),  8));  // 牆面材料
                        dataRow.Append(wallArea > 0 ? CreateNumberCell(Math.Round(wallArea, 2), 1) : CreateTextCell("-", 1));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeCode(floorFullName) : "", 9));  // 地坪代號
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeName(floorFullName) : "", 8));  // 地坪材料
                        dataRow.Append(isFirst && floorAreaSqm > 0 ? CreateNumberCell(Math.Round(floorAreaSqm, 2), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeCode(ceilFullName) : "", 9));  // 天花代號
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeName(ceilFullName) : "", 8));  // 天花材料
                        dataRow.Append(isFirst && ceilingAreaSqm > 0 ? CreateNumberCell(Math.Round(ceilingAreaSqm, 2), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeCode(skirtFullName) : "", 9));  // 踢腳板代號
                        dataRow.Append(CreateTextCell(isFirst ? SplitTypeName(skirtFullName) : "", 8));  // 踢腳板材料
                        dataRow.Append(isFirst && skirtingLengthM > 0 ? CreateNumberCell(Math.Round(skirtingLengthM, 1), 1) : CreateTextCell(isFirst ? "-" : "", 1));
                        sheetData.Append(dataRow);
                        rowIndex++;
                    }
                }

                rowIndex++;
            }

            System.Diagnostics.Debug.WriteLine($"[BuildPracticalScheduleSheet] 完成，共填入 {rowIndex} 列");
            var lastRow = Math.Max((int)rowIndex - 1, 4);
            autoFilter.Reference = $"A4:O{lastRow}";
        }

        private List<RoomFinishRow> GetExportRows()
        {
            var selectedRows = _roomRows.Where(x => x.IsSelected).ToList();
            return selectedRows.Any() ? selectedRows : _roomRows.ToList();
        }

        /// <summary>
        /// 回傳有效牆面類型 ID：優先用個別房間設定，否則 fallback 至全域下拉選擇。
        /// 解決「先載入房間、再選類型」時 WallTypeId = -1 的問題。
        /// </summary>
        private long GetEffectiveWallTypeId(RoomFinishRow row)
            => row.WallTypeId > 0 ? row.WallTypeId : GetSelectedOptionId(cmbWalls);

        private long GetEffectiveFloorTypeId(RoomFinishRow row)
            => row.FloorTypeId > 0 ? row.FloorTypeId : GetSelectedOptionId(cmbFloors);

        private long GetEffectiveCeilingTypeId(RoomFinishRow row)
            => row.CeilingTypeId > 0 ? row.CeilingTypeId : GetSelectedOptionId(cmbCeilings);

        private long GetEffectiveSkirtingTypeId(RoomFinishRow row)
            => row.SkirtingTypeId > 0 ? row.SkirtingTypeId : GetSelectedOptionId(cmbSkirtings);

        // 匯出用：類型以設定介面/房間參數為主，避免模型中既有結構元素類型覆寫匯出結果。
        // 面積仍由 _modelFinishIndex 取得實際值（GetRoomMetrics）。
        private long GetExportWallTypeId(RoomFinishRow row)
        {
            return GetEffectiveWallTypeId(row);
        }
        private long GetExportFloorTypeId(RoomFinishRow row)
        {
            return GetEffectiveFloorTypeId(row);
        }
        private long GetExportCeilingTypeId(RoomFinishRow row)
        {
            return GetEffectiveCeilingTypeId(row);
        }
        private long GetExportSkirtingTypeId(RoomFinishRow row)
        {
            return GetEffectiveSkirtingTypeId(row);
        }

        private string GetExportTypeNameSummary(RoomFinishRow row, string kind)
        {
            var roomId = RevitCompat.GetElementIdValue(row.RoomId);
            if (_modelFinishIndex != null && _modelFinishIndex.TryGetValue(roomId, out var md))
            {
                switch (kind)
                {
                    case "Wall":
                        if (md.WallTypeIds.Count > 0) return JoinTypeNamesByIds(WallTypeOptions, md.WallTypeIds);
                        break;
                    case "Floor":
                        if (md.FloorTypeIds.Count > 0) return JoinTypeNamesByIds(FloorTypeOptions, md.FloorTypeIds);
                        break;
                    case "Ceiling":
                        if (md.CeilingTypeIds.Count > 0) return JoinTypeNamesByIds(CeilingTypeOptions, md.CeilingTypeIds);
                        break;
                }
            }

            return kind switch
            {
                "Wall" => GetTypeNameById(WallTypeOptions, GetExportWallTypeId(row)),
                "Floor" => GetTypeNameById(FloorTypeOptions, GetExportFloorTypeId(row)),
                "Ceiling" => GetTypeNameById(CeilingTypeOptions, GetExportCeilingTypeId(row)),
                _ => string.Empty
            };
        }

        private List<(long TypeId, string TypeName, double AreaM2)> GetWallTypeAreaEntries(RoomFinishRow row, double fallbackWallAreaSqm)
        {
            var roomId = RevitCompat.GetElementIdValue(row.RoomId);
            if (_modelFinishIndex != null && _modelFinishIndex.TryGetValue(roomId, out var md))
            {
                var entries = md.WallAreaByTypeM2
                    .Select(kv => (TypeId: kv.Key, TypeName: GetTypeNameById(WallTypeOptions, kv.Key), AreaM2: kv.Value))
                    .Where(x => !string.IsNullOrWhiteSpace(x.TypeName) && x.AreaM2 > 0)
                    .OrderBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (entries.Count > 0)
                    return entries;
            }

            var single = GetExportTypeNameSummary(row, "Wall");
            if (!string.IsNullOrWhiteSpace(single))
                return new List<(long TypeId, string TypeName, double AreaM2)> { (GetExportWallTypeId(row), single, fallbackWallAreaSqm) };

            return new List<(long TypeId, string TypeName, double AreaM2)> { (GetExportWallTypeId(row), string.Empty, fallbackWallAreaSqm) };
        }

        private double GetExportSkirtingHeightMm(RoomFinishRow row)
        {
            var skirtTypeId = GetExportSkirtingTypeId(row);
            if (skirtTypeId <= 0) return 0;
            return ParseDouble(txtSkirtingHeight.Text, 100);
        }

        /// <summary>
        /// 讀取元素的 AR_RoomId 共享參數，回傳房間 ID（long）；找不到時回傳 0。
        /// </summary>
        private static long ReadRoomIdFromElement(Element element)
        {
            var p = element.LookupParameter("房間ID(AR_RoomId)")
                 ?? element.LookupParameter("房間ID")
                 ?? element.LookupParameter("AR_RoomId");
            if (p == null) return 0L;
            return p.StorageType switch
            {
                StorageType.Integer => p.AsInteger(),
                StorageType.String  => long.TryParse(p.AsString(), out var parsed) ? parsed : 0L,
                _ => 0L
            };
        }

        /// <summary>
        /// 手動建置元素（無 AR_RoomId 參數）的備援：以多個幾何取樣點查詢所在房間。
        /// 若同一元素可歸屬多間房間，回傳 0，避免樓梯間、挑空或重疊房間被任意歸戶。
        /// </summary>
        private static long FindRoomIdBySpatialQuery(Element element)
        {
            try
            {
                var matchedRoomIds = FindRoomIdsBySpatialQuery(element, useFallback: false);

                if (matchedRoomIds.Count == 1)
                {
                    var roomId = matchedRoomIds.First();
                    var room = element.Document.GetElement(RevitCompat.CreateElementId(roomId)) as Room;
                    System.Diagnostics.Debug.WriteLine(
                        $"[FindRoomIdBySpatialQuery] 元素 {element.Id} → 房間 {room?.Number ?? roomId.ToString()} (multi-point)");
                    return roomId;
                }

                if (matchedRoomIds.Count > 1)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FindRoomIdBySpatialQuery] 元素 {element.Id} 命中多間房間，已略過自動歸戶：{string.Join(",", matchedRoomIds)}");
                    return 0L;
                }

                // 備援：若 Room.IsPointInRoom 因階段或模型狀態沒有命中，才使用 Revit 的 GetRoomAtPoint。
                // 仍然以多點結果去重，避免單點中心剛好落在樓梯間/挑空邊界時誤判。
                var fallbackRoomIds = FindRoomIdsBySpatialQuery(element, useFallback: true);

                if (fallbackRoomIds.Count == 1)
                    return fallbackRoomIds.First();

                if (fallbackRoomIds.Count > 1)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FindRoomIdBySpatialQuery] 元素 {element.Id} 備援查詢命中多間房間，已略過自動歸戶：{string.Join(",", fallbackRoomIds)}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FindRoomIdBySpatialQuery] 空間查詢失敗：{ex.Message}");
            }
            return 0L;
        }

        private static HashSet<long> FindRoomIdsBySpatialQuery(Element element, bool useFallback = true)
        {
            var result = new HashSet<long>();
            try
            {
                var doc = element.Document;
                var probePoints = GetElementRoomProbePoints(element).ToList();
                if (probePoints.Count == 0) return result;

                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                foreach (var point in probePoints)
                {
                    foreach (var room in rooms)
                    {
                        if (RoomOverlapGuard.IsPointInRoomSafe(room, point))
                            result.Add(RevitCompat.GetElementIdValue(room.Id));
                    }
                }

                if (result.Count > 0 || !useFallback)
                    return result;

                var phases = doc.Phases;
                foreach (var point in probePoints)
                {
                    for (int i = phases.Size - 1; i >= 0; i--)
                    {
                        if (!(phases.get_Item(i) is Phase phase)) continue;
                        var room = doc.GetRoomAtPoint(point, phase);
                        if (room != null && room.Area > 0)
                        {
                            result.Add(RevitCompat.GetElementIdValue(room.Id));
                            break;
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private static IEnumerable<XYZ> GetElementRoomProbePoints(Element element)
        {
            if (element == null)
                yield break;

            if (element.Location is LocationPoint lp)
                yield return lp.Point;

            if (element.Location is LocationCurve lc)
                yield return lc.Curve.Evaluate(0.5, true);

            var bb = element.get_BoundingBox(null);
            if (bb == null)
                yield break;

            var center = new XYZ(
                (bb.Min.X + bb.Max.X) / 2.0,
                (bb.Min.Y + bb.Max.Y) / 2.0,
                (bb.Min.Z + bb.Max.Z) / 2.0);

            yield return center;

            var categoryId = element.Category != null ? RevitCompat.GetElementIdValue(element.Category.Id) : 0;
            const double probeOffset = 10.0 / 304.8; // 10mm，避免取樣點落在樓板/天花板厚度中心而不在房間內

            if (categoryId == (int)BuiltInCategory.OST_Floors)
                yield return new XYZ(center.X, center.Y, bb.Max.Z + probeOffset);
            else if (categoryId == (int)BuiltInCategory.OST_Ceilings)
                yield return new XYZ(center.X, center.Y, bb.Min.Z - probeOffset);

            // 樓梯間或不規則面可能中心點落在洞口/邊界上，補四象限點提升判讀穩定性。
            var z = center.Z;
            if (categoryId == (int)BuiltInCategory.OST_Floors)
                z = bb.Max.Z + probeOffset;
            else if (categoryId == (int)BuiltInCategory.OST_Ceilings)
                z = bb.Min.Z - probeOffset;

            var x1 = bb.Min.X + (bb.Max.X - bb.Min.X) * 0.25;
            var x3 = bb.Min.X + (bb.Max.X - bb.Min.X) * 0.75;
            var y1 = bb.Min.Y + (bb.Max.Y - bb.Min.Y) * 0.25;
            var y3 = bb.Min.Y + (bb.Max.Y - bb.Min.Y) * 0.75;

            yield return new XYZ(x1, y1, z);
            yield return new XYZ(x1, y3, z);
            yield return new XYZ(x3, y1, z);
            yield return new XYZ(x3, y3, z);
        }

        /// <summary>
        /// 掃描模型中所有粉刷元素，依 AR_RoomId 建立「房間 ID → 實際面積/類型」索引。
        /// 牆高 ≤ 500mm 的牆類元素視為踢腳板（累計長度），其餘視為牆面粉刷（累計面積）。
        /// </summary>
        private Dictionary<long, ModelFinishData> BuildModelFinishDataIndex(Document doc)
        {
            const double SKIRTING_MAX_HEIGHT_M = 0.5; // 500mm：高度 ≤ 此值視為踢腳板
            var index = new Dictionary<long, ModelFinishData>();

            ModelFinishData GetOrCreate(long roomId)
            {
                if (!index.TryGetValue(roomId, out var d))
                {
                    d = new ModelFinishData();
                    index[roomId] = d;
                }
                return d;
            }

            // 牆類（OST_Walls）：區分牆面粉刷 vs 踢腳板
            foreach (var elem in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .ToElements())
            {
                if (!IsTrustedModelFinishElementForSync(elem))
                    continue;

                long typeIdVal = RevitCompat.GetElementIdValue(elem.GetTypeId());
                long roomId = ReadRoomIdFromElement(elem);
                if (roomId <= 0)
                {
                    roomId = FindRoomIdBySpatialQuery(elem);
                    if (roomId <= 0) continue;
                }

                var heightParam = elem.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)
                    ?? elem.get_Parameter(BuiltInParameter.WALL_ATTR_HEIGHT_PARAM);
                double heightM = heightParam != null && heightParam.StorageType == StorageType.Double
                    ? InternalLengthToMeters(heightParam.AsDouble()) : double.MaxValue;

                var data = GetOrCreate(roomId);

                // 踢腳板識別以實際牆高為準。牆面塗層與踢腳板同屬 WallType，
                // 若用下拉清單的類型 ID 判斷，未設定踢腳板時會錯把牆面塗層帶入踢腳板欄位。
                bool isSkirting = heightM > 0 && heightM <= SKIRTING_MAX_HEIGHT_M;

                if (isSkirting)
                {
                    if (elem.Location is LocationCurve lc)
                        data.SkirtingLengthM += InternalLengthToMeters(lc.Curve.Length);
                    data.SkirtingElementIds.Add(elem.Id);
                    data.SkirtingTypeIds.Add(typeIdVal);
                    if (data.SkirtingTypeId <= 0) data.SkirtingTypeId = typeIdVal;
                }
                else
                {
                    if (heightM > 0 && heightM < double.MaxValue)
                        data.WallHeightMm = Math.Max(data.WallHeightMm, heightM * 1000.0);

                    var areaParam = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (areaParam != null && areaParam.StorageType == StorageType.Double)
                    {
                        var areaM2 = InternalAreaToSquareMeters(areaParam.AsDouble());
                        data.WallAreaM2 += areaM2;
                        if (typeIdVal > 0)
                            data.WallAreaByTypeM2[typeIdVal] = (data.WallAreaByTypeM2.TryGetValue(typeIdVal, out var v) ? v : 0) + areaM2;
                    }
                    data.WallElementIds.Add(elem.Id);
                    data.WallTypeIds.Add(typeIdVal);
                    if (data.WallTypeId <= 0) data.WallTypeId = typeIdVal;
                }
            }

            // 樓板類（OST_Floors）
            foreach (var elem in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElements())
            {
                if (!IsTrustedModelFinishElementForSync(elem))
                    continue;

                long typeIdVal = RevitCompat.GetElementIdValue(elem.GetTypeId());
                long roomId = ReadRoomIdFromElement(elem);
                if (roomId <= 0)
                {
                    roomId = FindRoomIdBySpatialQuery(elem);
                    if (roomId <= 0) continue;
                }
                var areaParam = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (areaParam == null || areaParam.StorageType != StorageType.Double) continue;
                var data = GetOrCreate(roomId);
                var areaM2 = InternalAreaToSquareMeters(areaParam.AsDouble());
                data.FloorAreaM2 += areaM2;
                if (typeIdVal > 0)
                    data.FloorAreaByTypeM2[typeIdVal] = (data.FloorAreaByTypeM2.TryGetValue(typeIdVal, out var v) ? v : 0) + areaM2;
                data.FloorElementIds.Add(elem.Id);
                data.FloorTypeIds.Add(typeIdVal);
                if (data.FloorTypeId <= 0) data.FloorTypeId = typeIdVal;
            }

            // 天花板類（OST_Ceilings）
            foreach (var elem in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType()
                .ToElements())
            {
                if (!IsTrustedModelFinishElementForSync(elem))
                    continue;

                long typeIdVal = RevitCompat.GetElementIdValue(elem.GetTypeId());
                long roomId = ReadRoomIdFromElement(elem);
                if (roomId <= 0)
                {
                    roomId = FindRoomIdBySpatialQuery(elem);
                    if (roomId <= 0) continue;
                }
                var areaParam = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (areaParam == null || areaParam.StorageType != StorageType.Double) continue;
                var data = GetOrCreate(roomId);
                var areaM2 = InternalAreaToSquareMeters(areaParam.AsDouble());
                data.CeilingAreaM2 += areaM2;
                if (typeIdVal > 0)
                    data.CeilingAreaByTypeM2[typeIdVal] = (data.CeilingAreaByTypeM2.TryGetValue(typeIdVal, out var v) ? v : 0) + areaM2;
                data.CeilingElementIds.Add(elem.Id);
                data.CeilingTypeIds.Add(typeIdVal);
                if (data.CeilingTypeId <= 0) data.CeilingTypeId = typeIdVal;
            }

            // 一般模型手動裝修面（例如樓梯間斜面、挑空側面、不規則面生面）。
            // 其中 RoomFinish_ColumnWall 是房間裝修自動補生的柱側粉刷面，
            // 應納入牆面粉刷驗算量；其他 DirectShape 仍獨立列為手動裝修面。
            foreach (var elem in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType()
                .ToElements())
            {
                if (!IsTrustedModelFinishElementForSync(elem))
                    continue;

                long roomId = ReadRoomIdFromElement(elem);
                if (roomId <= 0)
                {
                    roomId = FindRoomIdBySpatialQuery(elem);
                    if (roomId <= 0) continue;
                }

                var areaM2 = ReadManualFaceAreaM2(elem);
                if (areaM2 <= 0) continue;

                var data = GetOrCreate(roomId);
                var materialName = GetManualFaceMaterialName(elem);

                if (IsRoomFinishColumnWallFace(elem, out var wallTypeId))
                {
                    data.WallAreaM2 += areaM2;
                    data.WallElementIds.Add(elem.Id);

                    if (wallTypeId > 0)
                    {
                        data.WallTypeIds.Add(wallTypeId);
                        data.WallAreaByTypeM2[wallTypeId] =
                            (data.WallAreaByTypeM2.TryGetValue(wallTypeId, out var wallExisting) ? wallExisting : 0) + areaM2;
                        if (data.WallTypeId <= 0) data.WallTypeId = wallTypeId;
                    }

                    continue;
                }

                data.ManualFaceAreaM2 += areaM2;
                data.ManualFaceElementIds.Add(elem.Id);
                data.ManualFaceAreaByMaterialM2[materialName] =
                    (data.ManualFaceAreaByMaterialM2.TryGetValue(materialName, out var existing) ? existing : 0) + areaM2;
            }

            System.Diagnostics.Debug.WriteLine($"[BuildModelFinishDataIndex] 完成，共 {index.Count} 間房間有粉刷元素");
            return index;
        }

        private static bool IsRoomFinishColumnWallFace(Element elem, out long wallTypeId)
        {
            wallTypeId = 0;

            try
            {
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
            catch
            {
                return false;
            }
        }

        private static double ReadManualFaceAreaM2(Element elem)
        {
            try
            {
                var areaParam = elem.LookupParameter("面積")
                    ?? elem.LookupParameter("裝修面積")
                    ?? elem.LookupParameter("Area");
                if (areaParam != null && areaParam.StorageType == StorageType.Double)
                    return InternalAreaToSquareMeters(areaParam.AsDouble());

                var hostArea = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (hostArea != null && hostArea.StorageType == StorageType.Double)
                    return InternalAreaToSquareMeters(hostArea.AsDouble());
            }
            catch { }

            return 0;
        }

        private static string GetManualFaceMaterialName(Element elem)
        {
            try
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

                var materialIdParam = elem.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (materialIdParam != null && materialIdParam.StorageType == StorageType.ElementId)
                {
                    var material = elem.Document.GetElement(materialIdParam.AsElementId()) as Material;
                    if (!string.IsNullOrWhiteSpace(material?.Name))
                        return material.Name;
                }
            }
            catch { }

            return "手動裝修面";
        }

        private void GetRoomMetrics(RoomFinishRow row, out double roomAreaSqm, out double wallAreaEstimateSqm, out double floorAreaEstimateSqm, out double ceilingAreaEstimateSqm)
        {
            roomAreaSqm            = 0;
            wallAreaEstimateSqm    = 0;
            floorAreaEstimateSqm   = 0;
            ceilingAreaEstimateSqm = 0;

            var room = _uiDoc.Document.GetElement(row.RoomId) as Room;
            if (room == null) return;

            roomAreaSqm = InternalAreaToSquareMeters(room.Area);
            long roomId = RevitCompat.GetElementIdValue(row.RoomId);

            // 周長 + 牆高（備用估算）
            var perimeterParam  = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
            double perimeterM   = InternalLengthToMeters(perimeterParam?.AsDouble() ?? 0);
            double wallHeightM  = Math.Max(row.WallHeightMm, 0) / 1000.0;

            // 門窗開口面積（僅用於估算分支，模型元素後由 HOST_AREA_COMPUTED 自動扣除）
            double openingDeductM2 = wallHeightM > 0 ? CalcWallOpeningDeductionSqm(room, wallHeightM) : 0;
            double netWallEstimateM2 = Math.Max(0, perimeterM * wallHeightM - openingDeductM2);

            if (_modelFinishIndex != null && _modelFinishIndex.TryGetValue(roomId, out var md))
            {
                // 優先使用模型元素實際面積；若該項目尚未生成則退回幾何估算（需已選類型）
                wallAreaEstimateSqm    = md.WallAreaM2    > 0 ? md.WallAreaM2    : (GetEffectiveWallTypeId(row)    > 0 ? netWallEstimateM2 : 0);
                floorAreaEstimateSqm   = md.FloorAreaM2   > 0 ? md.FloorAreaM2   : (GetEffectiveFloorTypeId(row)   > 0 ? roomAreaSqm      : 0);
                ceilingAreaEstimateSqm = md.CeilingAreaM2 > 0 ? md.CeilingAreaM2 : (GetEffectiveCeilingTypeId(row) > 0 ? roomAreaSqm      : 0);
            }
            else
            {
                // 模型中無粉刷元素：退回幾何估算（需已選類型）
                wallAreaEstimateSqm    = GetEffectiveWallTypeId(row)    > 0 ? netWallEstimateM2 : 0;
                floorAreaEstimateSqm   = GetEffectiveFloorTypeId(row)   > 0 ? roomAreaSqm      : 0;
                ceilingAreaEstimateSqm = GetEffectiveCeilingTypeId(row) > 0 ? roomAreaSqm      : 0;
            }
        }

        private double GetSkirtingLengthEstimateM(RoomFinishRow row)
        {
            long roomId = RevitCompat.GetElementIdValue(row.RoomId);

            // 優先使用模型元素實際長度
            if (_modelFinishIndex != null && _modelFinishIndex.TryGetValue(roomId, out var data) && data.SkirtingLengthM > 0)
                return data.SkirtingLengthM;

            // 退回幾何估算（需已選踢腳板類型）
            if (GetEffectiveSkirtingTypeId(row) <= 0) return 0;
            var room = _uiDoc.Document.GetElement(row.RoomId) as Room;
            if (room == null) return 0;

            var doc = _uiDoc.Document;

            // 取得房間周長（英尺）
            var perimeterParam = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
            double perimeter = perimeterParam?.AsDouble() ?? 0;
            if (perimeter <= 0) return 0;

            // 取得圍護本房間的邊界牆 ID 集合
            var boundaryWallIds = new HashSet<long>();
            try
            {
                var sbo = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                var segs = room.GetBoundarySegments(sbo);
                if (segs != null)
                    foreach (var grp in segs)
                        foreach (var seg in grp)
                            boundaryWallIds.Add(RevitCompat.GetElementIdValue(seg.ElementId));
            }
            catch { }

            // 扣除屬於本房間邊界牆上的門開口寬度（踢腳板在門口處無實體）
            double totalDoorWidth = 0;
            try
            {
                var doors = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(d =>
                    {
                        if (d.Host == null) return false;
                        if (!boundaryWallIds.Contains(RevitCompat.GetElementIdValue(d.Host.Id))) return false;
                        return (d.ToRoom != null && d.ToRoom.Id == room.Id) ||
                               (d.FromRoom != null && d.FromRoom.Id == room.Id);
                    });

                foreach (var door in doors)
                    totalDoorWidth += GetDoorOpeningWidthFeet(door);
            }
            catch { }

            double netLength = Math.Max(0, perimeter - totalDoorWidth);
            return InternalLengthToMeters(netLength);
        }

        /// <summary>回傳門的開口寬度（Revit 內部單位：英尺）</summary>
        private static double GetDoorOpeningWidthFeet(FamilyInstance door)
        {
            var widthParam = door.Symbol?.LookupParameter("Width") ?? door.LookupParameter("Width");
            if (widthParam != null) return widthParam.AsDouble();
            var roughWidthParam = door.Symbol?.LookupParameter("Rough Width") ?? door.LookupParameter("Rough Width");
            if (roughWidthParam != null) return roughWidthParam.AsDouble();
            return 900.0 / 304.8; // 預設 900mm
        }

        /// <summary>回傳門窗開口高度（Revit 內部單位：英尺）；找不到則回傳 0</summary>
        private static double GetOpeningHeightFeet(FamilyInstance inst)
        {
            foreach (var name in new[] { "Height", "Rough Height", "Frame Height" })
            {
                var p = inst.Symbol?.LookupParameter(name) ?? inst.LookupParameter(name);
                if (p?.StorageType == StorageType.Double) return p.AsDouble();
            }
            return 0;
        }

        /// <summary>回傳窗戶開口寬度（Revit 內部單位：英尺）；找不到則回傳 0</summary>
        private static double GetOpeningWidthFeet(FamilyInstance inst)
        {
            foreach (var name in new[] { "Width", "Rough Width", "Frame Width" })
            {
                var p = inst.Symbol?.LookupParameter(name) ?? inst.LookupParameter(name);
                if (p?.StorageType == StorageType.Double) return p.AsDouble();
            }
            return 0;
        }

        /// <summary>
        /// 計算房間周圈牆面上門窗開口的總扣除面積（m²）。
        /// 僅用於未生成粉刷元素時的予估算；已生成後由 HOST_AREA_COMPUTED 自動處理。
        /// </summary>
        private double CalcWallOpeningDeductionSqm(Room room, double wallHeightM)
        {
            var doc = _uiDoc.Document;
            var boundaryWallIds = new HashSet<long>();
            try
            {
                var sbo = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                var segs = room.GetBoundarySegments(sbo);
                if (segs != null)
                    foreach (var grp in segs)
                        foreach (var seg in grp)
                            boundaryWallIds.Add(RevitCompat.GetElementIdValue(seg.ElementId));
            }
            catch { return 0; }
            if (boundaryWallIds.Count == 0) return 0;

            double deduction = 0;

            // 門：寬 × min(門高, 牆高)
            try
            {
                foreach (var door in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(d => d.Host != null &&
                                boundaryWallIds.Contains(RevitCompat.GetElementIdValue(d.Host.Id)) &&
                                ((d.ToRoom != null && d.ToRoom.Id == room.Id) ||
                                 (d.FromRoom != null && d.FromRoom.Id == room.Id))))
                {
                    double wFt = GetDoorOpeningWidthFeet(door);
                    double hFt = GetOpeningHeightFeet(door);
                    double hM  = hFt > 0 ? InternalLengthToMeters(hFt) : wallHeightM;
                    deduction += InternalLengthToMeters(wFt) * Math.Min(hM, wallHeightM);
                }
            }
            catch { }

            // 窗：寬 × min(窗高, 牆高)
            try
            {
                foreach (var win in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(w => w.Host != null &&
                                boundaryWallIds.Contains(RevitCompat.GetElementIdValue(w.Host.Id)) &&
                                ((w.ToRoom != null && w.ToRoom.Id == room.Id) ||
                                 (w.FromRoom != null && w.FromRoom.Id == room.Id))))
                {
                    double wFt = GetOpeningWidthFeet(win);
                    double hFt = GetOpeningHeightFeet(win);
                    if (wFt > 0 && hFt > 0)
                        deduction += InternalLengthToMeters(wFt) * Math.Min(InternalLengthToMeters(hFt), wallHeightM);
                }
            }
            catch { }

            return deduction;
        }

        private static double InternalAreaToSquareMeters(double areaInternal)
        {
            return UnitUtils.ConvertFromInternalUnits(areaInternal, UnitTypeId.SquareMeters);
        }

        private static double InternalLengthToMeters(double lengthInternal)
        {
            return UnitUtils.ConvertFromInternalUnits(lengthInternal, UnitTypeId.Meters);
        }

        private static Stylesheet CreateExportStylesheet()
        {
            var fonts = new Fonts(
                new Font(new FontSize { Val = 11D }, new FontName { Val = "Microsoft JhengHei UI" }),
                new Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Microsoft JhengHei UI" }),
                new Font(new Bold(), new FontSize { Val = 14D }, new FontName { Val = "Microsoft JhengHei UI" })
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
                    Alignment = new Alignment
                    {
                        Horizontal = HorizontalAlignmentValues.Left,
                        Vertical = VerticalAlignmentValues.Center,
                        WrapText = true
                    }
                },
                new CellFormat
                {
                    FontId = 0,
                    FillId = 0,
                    BorderId = 1,
                    ApplyBorder = true,
                    Alignment = new Alignment
                    {
                        Horizontal = HorizontalAlignmentValues.Center,
                        Vertical = VerticalAlignmentValues.Center,
                        ShrinkToFit = true
                    }
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
            if (text == "RF")
                return 9000;

            if (text.StartsWith("B") && text.EndsWith("F"))
            {
                var numberText = new string(text.Skip(1).TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(numberText, out var basement))
                    return -basement;
            }

            var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var floor))
                return floor;

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

        private string BuildExcelXml()
        {
            var headers = new[]
            {
                "RoomId", "RoomNumber", "RoomName", "Level", "IsSelected",
                "WallTypeId", "WallTypeName", "WallHeightMm",
                "FloorTypeId", "FloorTypeName",
                "CeilingTypeId", "CeilingTypeName", "CeilingHeightMm",
                "SkirtingTypeId", "SkirtingTypeName", "SkirtingHeightMm"
            };

            var widths = new[] { 90, 80, 160, 90, 70, 80, 160, 95, 80, 160, 85, 160, 110, 95, 160, 120 };
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
            sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");
            sb.AppendLine("  <Styles>");
            sb.AppendLine("    <Style ss:ID=\"Header\"><Font ss:Bold=\"1\"/><Interior ss:Color=\"#DCE6F1\" ss:Pattern=\"Solid\"/></Style>");
            sb.AppendLine("  </Styles>");
            sb.AppendLine("  <Worksheet ss:Name=\"RoomFinishings\">");
            sb.AppendLine("    <Table>");

            foreach (var width in widths)
            {
                sb.AppendLine($"      <Column ss:Width=\"{width}\"/>");
            }

            sb.AppendLine("      <Row ss:StyleID=\"Header\">");
            foreach (var header in headers)
            {
                sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{EscapeXml(header)}</Data></Cell>");
            }
            sb.AppendLine("      </Row>");

            var exportRows = GetExportRows();
            foreach (var row in exportRows)
            {
                var effWallId = GetExportWallTypeId(row);
                var effFloorId = GetExportFloorTypeId(row);
                var effCeilId = GetExportCeilingTypeId(row);
                var effSkirtId = GetExportSkirtingTypeId(row);
                var effSkirtHeightMm = GetExportSkirtingHeightMm(row);
                sb.AppendLine("      <Row>");
                AppendExcelCell(sb, RevitCompat.GetElementIdValue(row.RoomId).ToString(), true);
                AppendExcelCell(sb, row.Number, false);
                AppendExcelCell(sb, row.Name, false);
                AppendExcelCell(sb, row.Level, false);
                AppendExcelCell(sb, row.IsSelected ? "1" : "0", true);
                AppendExcelCell(sb, effWallId.ToString(), true);
                AppendExcelCell(sb, GetTypeNameById(WallTypeOptions, effWallId), false);
                AppendExcelCell(sb, row.WallHeightMm.ToString("F2", CultureInfo.InvariantCulture), true);
                AppendExcelCell(sb, effFloorId.ToString(), true);
                AppendExcelCell(sb, GetTypeNameById(FloorTypeOptions, effFloorId), false);
                AppendExcelCell(sb, effCeilId.ToString(), true);
                AppendExcelCell(sb, GetTypeNameById(CeilingTypeOptions, effCeilId), false);
                AppendExcelCell(sb, row.CeilingHeightMm.ToString("F2", CultureInfo.InvariantCulture), true);
                AppendExcelCell(sb, effSkirtId.ToString(), true);
                AppendExcelCell(sb, GetTypeNameById(SkirtingTypeOptions, effSkirtId), false);
                AppendExcelCell(sb, effSkirtHeightMm.ToString("F2", CultureInfo.InvariantCulture), true);
                sb.AppendLine("      </Row>");
            }

            sb.AppendLine("    </Table>");
            sb.AppendLine("  </Worksheet>");
            sb.AppendLine("</Workbook>");
            return sb.ToString();
        }

        private static void AppendExcelCell(StringBuilder sb, string value, bool numeric)
        {
            var raw = (value ?? string.Empty).Trim();

            if (numeric)
            {
                if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numericValue)
                    || double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out numericValue))
                {
                    sb.AppendLine($"        <Cell><Data ss:Type=\"Number\">{numericValue.ToString("G17", CultureInfo.InvariantCulture)}</Data></Cell>");
                    return;
                }
            }

            var safeValue = EscapeXml(raw);
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{safeValue}</Data></Cell>");
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private void ImportRoomSettingsFromFile()
        {
            try
            {
                var openDialog = new OpenFileDialog
                {
                    Title = "匯入房間裝修設定",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx|Excel XML Workbook (*.xml)|*.xml|CSV files (*.csv)|*.csv"
                };

                if (openDialog.ShowDialog() != true)
                    return;

                var extension = Path.GetExtension(openDialog.FileName)?.ToLowerInvariant();
                List<List<string>> rows;

                if (extension == ".xlsx")
                {
                    rows = ParseXlsxRows(openDialog.FileName);
                }
                else if (extension == ".xml")
                {
                    rows = ParseExcelXmlRows(openDialog.FileName);
                }
                else
                {
                    rows = ParseCsvRows(openDialog.FileName);
                }

                if (rows.Count <= 1)
                {
                    MessageBox.Show("匯入內容為空。", "匯入 / 匯出", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var updatedCount = 0;
                var unchangedCount = 0;
                var skippedCount = 0;
                var header = rows.FirstOrDefault() ?? new List<string>();

                int FindColumn(params string[] names)
                {
                    for (var i = 0; i < header.Count; i++)
                    {
                        var headerName = (header[i] ?? string.Empty).Trim();
                        if (names.Any(name => string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase)))
                            return i;
                    }
                    return -1;
                }

                string GetCell(IReadOnlyList<string> cells, int index)
                    => index >= 0 && index < cells.Count ? cells[index] : string.Empty;

                var roomIdCellIndex = FindColumn("房間ID", "RoomId");
                var roomNumberCellIndex = FindColumn("房間號碼", "RoomNumber");
                var selectedCellIndex = FindColumn("已選取", "IsSelected");
                var wallTypeCellIndex = FindColumn("牆面類型ID", "WallTypeId");
                var wallTypeNameCellIndex = FindColumn("牆面材料", "WallTypeName");
                var wallHeightCellIndex = FindColumn("粉刷高度(mm)", "WallHeightMm");
                var floorTypeCellIndex = FindColumn("地坪類型ID", "FloorTypeId");
                var floorTypeNameCellIndex = FindColumn("地坪材料", "FloorTypeName");
                var ceilingTypeCellIndex = FindColumn("天花板類型ID", "CeilingTypeId");
                var ceilingTypeNameCellIndex = FindColumn("天花板材料", "CeilingTypeName");
                var ceilingHeightCellIndex = FindColumn("天花板高度(mm)", "CeilingHeightMm");
                var skirtingTypeCellIndex = FindColumn("踢腳板類型ID", "SkirtingTypeId");
                var skirtingTypeNameCellIndex = FindColumn("踢腳板材料", "SkirtingTypeName");
                var skirtingHeightCellIndex = FindColumn("踢腳板高度(mm)", "SkirtingHeightMm");

                if (roomIdCellIndex < 0) roomIdCellIndex = 0;
                if (roomNumberCellIndex < 0) roomNumberCellIndex = 1;
                if (selectedCellIndex < 0) selectedCellIndex = 4;

                foreach (var cells in rows.Skip(1))
                {
                    if (cells.Count < 10)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (!long.TryParse(GetCell(cells, roomIdCellIndex), out var roomIdValue))
                    {
                        skippedCount++;
                        continue;
                    }

                    var row = _roomRows.FirstOrDefault(x => RevitCompat.GetElementIdValue(x.RoomId) == roomIdValue)
                              ?? _roomRows.FirstOrDefault(x => string.Equals(x.Number, GetCell(cells, roomNumberCellIndex), StringComparison.OrdinalIgnoreCase));

                    if (row == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var hasChanges = false;

                    var selectedText = GetCell(cells, selectedCellIndex);
                    var importedSelected = selectedText == "1" || selectedText.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (row.IsSelected != importedSelected)
                    {
                        row.IsSelected = importedSelected;
                        hasChanges = true;
                    }

                    if (cells.Count < 13)
                    {
                        wallTypeCellIndex = 5;
                        wallTypeNameCellIndex = -1;
                        wallHeightCellIndex = 6;
                        floorTypeCellIndex = 7;
                        floorTypeNameCellIndex = -1;
                        ceilingTypeCellIndex = 8;
                        ceilingTypeNameCellIndex = -1;
                        ceilingHeightCellIndex = 9;
                        skirtingTypeCellIndex = -1;
                        skirtingTypeNameCellIndex = -1;
                        skirtingHeightCellIndex = -1;
                    }

                    if (TryResolveTypeId(cells, wallTypeCellIndex, wallTypeNameCellIndex, WallTypeOptions, out var wallTypeId)
                        && row.WallTypeId != wallTypeId)
                    {
                        row.WallTypeId = wallTypeId;
                        hasChanges = true;
                    }

                    if (TryParseDoubleFlexible(GetCell(cells, wallHeightCellIndex), out var wallHeight) && wallHeight > 0
                        && Math.Abs(row.WallHeightMm - wallHeight) > 0.0001)
                    {
                        row.WallHeightMm = wallHeight;
                        hasChanges = true;
                    }

                    if (TryResolveTypeId(cells, floorTypeCellIndex, floorTypeNameCellIndex, FloorTypeOptions, out var floorTypeId)
                        && row.FloorTypeId != floorTypeId)
                    {
                        row.FloorTypeId = floorTypeId;
                        hasChanges = true;
                    }

                    if (TryResolveTypeId(cells, ceilingTypeCellIndex, ceilingTypeNameCellIndex, CeilingTypeOptions, out var ceilingTypeId)
                        && row.CeilingTypeId != ceilingTypeId)
                    {
                        row.CeilingTypeId = ceilingTypeId;
                        hasChanges = true;
                    }

                    if (TryParseDoubleFlexible(GetCell(cells, ceilingHeightCellIndex), out var ceilingHeight) && ceilingHeight > 0
                        && Math.Abs(row.CeilingHeightMm - ceilingHeight) > 0.0001)
                    {
                        row.CeilingHeightMm = ceilingHeight;
                        hasChanges = true;
                    }

                    if (TryResolveTypeId(cells, skirtingTypeCellIndex, skirtingTypeNameCellIndex, SkirtingTypeOptions, out var skirtingTypeId)
                        && row.SkirtingTypeId != skirtingTypeId)
                    {
                        row.SkirtingTypeId = skirtingTypeId;
                        hasChanges = true;
                    }

                    if (TryParseDoubleFlexible(GetCell(cells, skirtingHeightCellIndex), out var skirtingHeight) && skirtingHeight > 0)
                    {
                        var currentSkirtingHeight = ParseDouble(txtSkirtingHeight.Text, 100);
                        if (Math.Abs(currentSkirtingHeight - skirtingHeight) > 0.0001)
                        {
                            txtSkirtingHeight.Text = skirtingHeight.ToString("F0", CultureInfo.InvariantCulture);
                            hasChanges = true;
                        }
                    }

                    row.UpdateStatus();

                    if (hasChanges)
                        updatedCount++;
                    else
                        unchangedCount++;
                }

                ApplyFilters();
                UpdatePickedCount();
                txtStatus.Text = $"匯入完成：更新 {updatedCount}，相同略過 {unchangedCount}，錯誤略過 {skippedCount}。";

                MessageBox.Show($"匯入完成：更新 {updatedCount}，相同略過 {unchangedCount}，錯誤略過 {skippedCount}", "匯入 / 匯出", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯入失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static List<List<string>> ParseCsvRows(string filePath)
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            return lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(ParseCsvLine)
                .ToList();
        }

        private static List<List<string>> ParseExcelXmlRows(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var rowNodes = doc
                .Descendants()
                .Where(x => x.Name.LocalName == "Row")
                .ToList();

            var rows = new List<List<string>>();
            foreach (var rowNode in rowNodes)
            {
                var row = new List<string>();
                var colIndex = 1;

                foreach (var cellNode in rowNode.Elements().Where(x => x.Name.LocalName == "Cell"))
                {
                    var indexAttr = cellNode.Attributes().FirstOrDefault(a => a.Name.LocalName == "Index");
                    if (indexAttr != null && int.TryParse(indexAttr.Value, out var explicitIndex) && explicitIndex > colIndex)
                    {
                        while (colIndex < explicitIndex)
                        {
                            row.Add(string.Empty);
                            colIndex++;
                        }
                    }

                    var dataNode = cellNode.Elements().FirstOrDefault(x => x.Name.LocalName == "Data");
                    row.Add(dataNode?.Value ?? string.Empty);
                    colIndex++;
                }

                if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private static List<List<string>> ParseXlsxRows(string filePath)
        {
            var rows = new List<List<string>>();

            using (var document = SpreadsheetDocument.Open(filePath, false))
            {
                var workbookPart = document.WorkbookPart;
                if (workbookPart == null || workbookPart.Workbook?.Sheets == null)
                    return rows;

                var firstSheet = workbookPart.Workbook.Sheets.Elements<Sheet>().FirstOrDefault();
                if (firstSheet == null)
                    return rows;

                var worksheetPart = workbookPart.GetPartById(firstSheet.Id) as WorksheetPart;
                var sheetData = worksheetPart?.Worksheet?.GetFirstChild<SheetData>();
                if (sheetData == null)
                    return rows;

                foreach (var row in sheetData.Elements<Row>())
                {
                    var line = new List<string>();
                    var currentCol = 1;

                    foreach (var cell in row.Elements<Cell>())
                    {
                        var targetCol = GetExcelColumnIndex(cell.CellReference?.Value);
                        while (currentCol < targetCol)
                        {
                            line.Add(string.Empty);
                            currentCol++;
                        }

                        line.Add(ReadExcelCellValue(cell, workbookPart));
                        currentCol++;
                    }

                    if (line.Any(x => !string.IsNullOrWhiteSpace(x)))
                        rows.Add(line);
                }
            }

            return rows;
        }

        private static int GetExcelColumnIndex(string cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference))
                return 1;

            var index = 0;
            foreach (var ch in cellReference)
            {
                if (!char.IsLetter(ch))
                    break;

                index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }

            return Math.Max(index, 1);
        }

        private static string ReadExcelCellValue(Cell cell, WorkbookPart workbookPart)
        {
            if (cell == null)
                return string.Empty;

            if (cell.DataType != null)
            {
                if (cell.DataType == CellValues.InlineString)
                    return cell.InlineString?.InnerText ?? string.Empty;

                if (cell.DataType == CellValues.SharedString)
                {
                    if (int.TryParse(cell.CellValue?.Text, out var sharedIndex)
                        && workbookPart.SharedStringTablePart?.SharedStringTable != null)
                    {
                        return workbookPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>().ElementAtOrDefault(sharedIndex)?.InnerText ?? string.Empty;
                    }
                }
            }

            return cell.CellValue?.Text ?? cell.InnerText ?? string.Empty;
        }

        private static bool IsOptionExists(IEnumerable<TypeOption> options, long id)
        {
            return options.Any(x => x.Id == id);
        }

        private static string GetTypeNameById(IEnumerable<TypeOption> options, long id)
        {
            if (id <= 0)
                return "";

            return options.FirstOrDefault(x => x.Id == id)?.Name ?? "";
        }

        private IEnumerable<(string TypeName, double AreaM2)> GetTypeAreaEntriesForSummary(RoomFinishRow row, string kind)
        {
            var roomId = RevitCompat.GetElementIdValue(row.RoomId);
            if (_modelFinishIndex != null && _modelFinishIndex.TryGetValue(roomId, out var md))
            {
                Dictionary<long, double> map = null;
                IEnumerable<TypeOption> options = null;
                switch (kind)
                {
                    case "Wall":
                        map = md.WallAreaByTypeM2;
                        options = WallTypeOptions;
                        break;
                    case "Floor":
                        map = md.FloorAreaByTypeM2;
                        options = FloorTypeOptions;
                        break;
                    case "Ceiling":
                        map = md.CeilingAreaByTypeM2;
                        options = CeilingTypeOptions;
                        break;
                }

                if (map != null && map.Count > 0 && options != null)
                {
                    foreach (var kv in map)
                    {
                        var name = GetTypeNameById(options, kv.Key);
                        if (!string.IsNullOrWhiteSpace(name) && kv.Value > 0)
                            yield return (name, kv.Value);
                    }
                    yield break;
                }
            }

            // 模型無法分型時才回退到單一設定值
            GetRoomMetrics(row, out _, out var wallArea, out var floorArea, out var ceilArea);
            if (kind == "Wall")
            {
                var name = GetTypeNameById(WallTypeOptions, GetExportWallTypeId(row));
                if (!string.IsNullOrWhiteSpace(name) && wallArea > 0) yield return (name, wallArea);
            }
            else if (kind == "Floor")
            {
                var name = GetTypeNameById(FloorTypeOptions, GetExportFloorTypeId(row));
                if (!string.IsNullOrWhiteSpace(name) && floorArea > 0) yield return (name, floorArea);
            }
            else if (kind == "Ceiling")
            {
                var name = GetTypeNameById(CeilingTypeOptions, GetExportCeilingTypeId(row));
                if (!string.IsNullOrWhiteSpace(name) && ceilArea > 0) yield return (name, ceilArea);
            }
        }

        private static string JoinTypeNamesByIds(IEnumerable<TypeOption> options, IEnumerable<long> ids)
        {
            if (options == null || ids == null)
                return string.Empty;

            var names = ids
                .Where(x => x > 0)
                .Distinct()
                .Select(x => GetTypeNameById(options, x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            return names.Count == 0 ? string.Empty : string.Join("；", names);
        }

        private static string SplitTypeCode(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            if (fullName.Contains("；"))
            {
                return string.Join("；", fullName.Split(new[] { '；' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => SplitTypeCode(x.Trim()))
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return TrySplitTypeName(fullName, out var code, out _) ? code : fullName.Trim();
        }

        private static string SplitTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            if (fullName.Contains("；"))
            {
                return string.Join("；", fullName.Split(new[] { '；' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => SplitTypeName(x.Trim()))
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return TrySplitTypeName(fullName, out _, out var name) ? name : fullName.Trim();
        }

        private static bool TrySplitTypeName(string fullName, out string code, out string name)
        {
            code = string.Empty;
            name = string.Empty;

            var text = (fullName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // 支援代號本身含短尾碼，且材料以空白接續：W1-A 水泥漆、C2-1 油漆。
            var codeWithSuffix = System.Text.RegularExpressions.Regex.Match(
                text,
                @"^\s*([A-Za-z]+\d+[A-Za-z]?-[A-Za-z0-9]+)\s+(.+)$");
            if (codeWithSuffix.Success)
            {
                code = codeWithSuffix.Groups[1].Value.Trim();
                name = codeWithSuffix.Groups[2].Value.Trim();
                return !string.IsNullOrWhiteSpace(code);
            }

            // 主要規則：代號-材料、代號_材料、代號：材料。
            // 例如 W1-水泥漆、F1-EPOXY、C1-平頂、S1_踢腳板。
            var separated = System.Text.RegularExpressions.Regex.Match(
                text,
                @"^\s*([A-Za-z]+\d+[A-Za-z]?)\s*[-_：:]\s*(.+)$");
            if (separated.Success)
            {
                code = separated.Groups[1].Value.Trim();
                name = separated.Groups[2].Value.Trim();
                return !string.IsNullOrWhiteSpace(code);
            }

            // 空白分隔：W1 水泥漆。
            var spaced = System.Text.RegularExpressions.Regex.Match(
                text,
                @"^\s*([A-Za-z]+\d+[A-Za-z]?)\s+(.+)$");
            if (spaced.Success)
            {
                code = spaced.Groups[1].Value.Trim();
                name = spaced.Groups[2].Value.Trim();
                return !string.IsNullOrWhiteSpace(code);
            }

            // 只有代號時，代號欄回傳原文，材料欄也維持原文，避免使用者看不到完整類型。
            var codeOnly = System.Text.RegularExpressions.Regex.Match(
                text,
                @"^\s*([A-Za-z]+\d+[A-Za-z]?(?:-[A-Za-z0-9]+)*)\s*$");
            if (codeOnly.Success)
            {
                code = codeOnly.Groups[1].Value.Trim();
                name = text;
                return !string.IsNullOrWhiteSpace(code);
            }

            return false;
        }

        private static bool TryResolveTypeId(IReadOnlyList<string> cells, int idCellIndex, int nameCellIndex, IEnumerable<TypeOption> options, out long resolvedId)
        {
            resolvedId = -1;

            if (idCellIndex >= 0 && idCellIndex < cells.Count)
            {
                if (long.TryParse(cells[idCellIndex], out var idValue) && IsOptionExists(options, idValue))
                {
                    resolvedId = idValue;
                    return true;
                }
            }

            if (nameCellIndex >= 0 && nameCellIndex < cells.Count)
            {
                var name = (cells[nameCellIndex] ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var option = options.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (option != null)
                    {
                        resolvedId = option.Id;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryParseDoubleFlexible(string input, out double value)
        {
            value = 0;
            var text = (input ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value)
                   || double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
                return "";

            var escaped = value.Replace("\"", "\"\"");
            if (escaped.Contains(",") || escaped.Contains("\"") || escaped.Contains("\n") || escaped.Contains("\r"))
                return $"\"{escaped}\"";

            return escaped;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null)
                return result;

            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];

                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            result.Add(current.ToString());
            return result;
        }

        private void SelectAllRooms()
        {
            foreach (var row in _roomRows)
                row.IsSelected = true;
            UpdatePickedCount();
            if (chkOnlySelected.IsChecked == true)
                ApplyFilters();
        }

        private void DeselectAllRooms()
        {
            foreach (var row in _roomRows)
                row.IsSelected = false;
            UpdatePickedCount();
            if (chkOnlySelected.IsChecked == true)
                ApplyFilters();
        }

        private void InvertSelection()
        {
            foreach (var row in _roomRows)
                row.IsSelected = !row.IsSelected;
            UpdatePickedCount();
            if (chkOnlySelected.IsChecked == true)
                ApplyFilters();
        }

        private void ChkHeaderAll_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var chk = sender as System.Windows.Controls.CheckBox;
            bool newVal = chk?.IsChecked == true;
            foreach (var item in dgRooms.Items.OfType<RoomFinishRow>())
                item.IsSelected = newVal;
            UpdatePickedCount();
        }

        private void ChkRowSelect_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox chk) || !(chk.DataContext is RoomFinishRow clickedRow))
                return;

            var selectedRows = dgRooms.SelectedItems.OfType<RoomFinishRow>().ToList();
            if (selectedRows.Count <= 1 || !selectedRows.Contains(clickedRow))
                return;

            var newVal = chk.IsChecked == true;
            foreach (var row in selectedRows)
                row.IsSelected = newVal;
            UpdatePickedCount();
            if (chkOnlySelected.IsChecked == true)
                ApplyFilters();

            e.Handled = true;
        }

        private void DgRooms_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 複選僅作為操作範圍，不自動變更勾選。
        }

        private void RoomRowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isBulkComboApplying)
                return;

            if (!(sender is System.Windows.Controls.ComboBox combo) || !(combo.DataContext is RoomFinishRow sourceRow))
                return;

            var selectedRows = dgRooms.SelectedItems.OfType<RoomFinishRow>().ToList();
            if (selectedRows.Count <= 1 || !selectedRows.Contains(sourceRow))
                return;

            var tag = combo.Tag as string;
            if (string.IsNullOrWhiteSpace(tag))
                return;

            _isBulkComboApplying = true;
            try
            {
                foreach (var row in selectedRows)
                {
                    if (ReferenceEquals(row, sourceRow))
                        continue;

                    switch (tag)
                    {
                        case "WallTypeId":
                            row.WallTypeId = sourceRow.WallTypeId;
                            break;
                        case "FloorTypeId":
                            row.FloorTypeId = sourceRow.FloorTypeId;
                            break;
                        case "CeilingTypeId":
                            row.CeilingTypeId = sourceRow.CeilingTypeId;
                            break;
                        case "SkirtingTypeId":
                            row.SkirtingTypeId = sourceRow.SkirtingTypeId;
                            break;
                    }
                }
            }
            finally
            {
                _isBulkComboApplying = false;
            }
        }

        private void BatchApplyToSelectedRows()
        {
            var selectedRows = _roomRows.Where(r => r.IsSelected).ToList();
            if (!selectedRows.Any())
            {
                MessageBox.Show("請先選取要批次設定的房間。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            long?   batchWallId      = GetBatchSelectedId(cmbBatchWall);
            long?   batchFloorId     = GetBatchSelectedId(cmbBatchFloor);
            long?   batchCeilingId   = GetBatchSelectedId(cmbBatchCeiling);
            long?   batchSkirtingId  = GetBatchSelectedId(cmbBatchSkirting);
            double? batchWallH       = ParseDoubleNullable(txtBatchWallHeight.Text);
            double? batchCeilingH    = ParseDoubleNullable(txtBatchCeilingHeight.Text);

            foreach (var row in selectedRows)
            {
                if (batchWallId.HasValue)                               row.WallTypeId      = batchWallId.Value;
                if (batchFloorId.HasValue)                              row.FloorTypeId     = batchFloorId.Value;
                if (batchCeilingId.HasValue)                            row.CeilingTypeId   = batchCeilingId.Value;
                if (batchSkirtingId.HasValue)                           row.SkirtingTypeId  = batchSkirtingId.Value;
                if (batchWallH.HasValue    && batchWallH.Value > 0)     row.WallHeightMm    = batchWallH.Value;
                if (batchCeilingH.HasValue && batchCeilingH.Value > 0)  row.CeilingHeightMm = batchCeilingH.Value;
            }

            dgRooms.Items.Refresh();
            txtStatus.Text = $"已批次套用設定到 {selectedRows.Count} 間房間。";
        }

        private long? GetBatchSelectedId(System.Windows.Controls.ComboBox combo)
        {
            // Id == -2 表示「不變」，不套用；Id == -1 表示「不設定」，套用清除
            if (combo.SelectedItem is TypeOption opt && opt.Id != -2)
                return opt.Id;
            return null;
        }

        private static double? ParseDoubleNullable(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Number,
                                   System.Globalization.CultureInfo.InvariantCulture, out var d)
                   ? d : (double?)null;
        }

        private void UpdatePickedCount()
        {
            var selectedCount = _roomRows.Count(x => x.IsSelected);
            txtPickedCount.Text = $"已選取：{selectedCount}　總數：{_roomRows.Count} 間";
        }

        private void RoomRowOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RoomFinishRow.IsSelected))
            {
                UpdatePickedCount();

                if (chkOnlySelected.IsChecked == true)
                    ApplyFilters();
            }
        }

        private long GetSelectedOptionId(System.Windows.Controls.ComboBox combo)
        {
            if (combo.SelectedItem is TypeOption option)
                return option.Id;
            return -1;
        }

        private static ElementId BuildElementId(long id)
        {
            return RevitCompat.CreateElementId(id);
        }

        private static double ParseDouble(string text, double fallback)
        {
            return double.TryParse(text, out var value) ? value : fallback;
        }

        private static void SelectComboById(System.Windows.Controls.ComboBox combo, long id)
        {
            if (combo.ItemsSource == null) return;

            var option = ((IEnumerable<TypeOption>)combo.ItemsSource).FirstOrDefault(x => x.Id == id);
            combo.SelectedItem = option ?? ((IEnumerable<TypeOption>)combo.ItemsSource).FirstOrDefault();
        }
    }
}

