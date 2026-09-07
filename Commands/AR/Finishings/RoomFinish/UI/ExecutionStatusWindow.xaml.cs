using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Win32;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish.UI
{
    internal class JumpToRoom3DHandler : IExternalEventHandler
    {
        public UIDocument UiDoc { get; set; }
        public ElementId TargetRoomId { get; set; }
        public string LastMessage { get; private set; } = string.Empty;

        public string GetName() => "AR RoomFinish JumpToRoom3D";

        public void Execute(UIApplication app)
        {
            LastMessage = string.Empty;
            try
            {
                if (UiDoc == null || TargetRoomId == null || TargetRoomId == ElementId.InvalidElementId)
                {
                    LastMessage = "跳轉失敗：目標房間無效。";
                    return;
                }

                var doc = UiDoc.Document;
                var room = doc.GetElement(TargetRoomId) as Room;
                if (room == null)
                {
                    LastMessage = "跳轉失敗：找不到房間元素。";
                    return;
                }

                var view = GetOrCreateRoomPreview3DView(doc);
                if (view == null)
                {
                    LastMessage = "跳轉失敗：找不到可用 3D 視圖。";
                    return;
                }

                UiDoc.ActiveView = view;

                var roomBox = room.get_BoundingBox(view) ?? room.get_BoundingBox(null);
                if (roomBox != null)
                {
                    var margin = 1000.0 / 304.8;
                    var expandedBox = new BoundingBoxXYZ
                    {
                        Min = new XYZ(roomBox.Min.X - margin, roomBox.Min.Y - margin, roomBox.Min.Z - margin),
                        Max = new XYZ(roomBox.Max.X + margin, roomBox.Max.Y + margin, roomBox.Max.Z + margin),
                        Transform = roomBox.Transform
                    };

                    using var t = new Transaction(doc, "AR 房間預覽");
                    t.Start();
                    view.IsSectionBoxActive = true;
                    view.SetSectionBox(expandedBox);
                    t.Commit();
                }

                UiDoc.Selection.SetElementIds(new List<ElementId> { TargetRoomId });
                UiDoc.ShowElements(TargetRoomId);
                LastMessage = $"已跳轉房間 {room.Number} 的 3D 視圖。";
            }
            catch (Exception ex)
            {
                LastMessage = $"跳轉失敗：{ex.Message}";
            }
        }

        private static View3D GetOrCreateRoomPreview3DView(Document doc)
        {
            const string roomPreviewViewName = "AR_RoomPreview_3D";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, roomPreviewViewName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;

            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);
            if (vft == null)
                return null;

            using var t = new Transaction(doc, "建立房間預覽 3D 視圖");
            t.Start();
            var view3d = View3D.CreateIsometric(doc, vft.Id);
            view3d.Name = roomPreviewViewName;
            view3d.DetailLevel = ViewDetailLevel.Fine;
            t.Commit();
            return view3d;
        }
    }

    public class ExecutionStatusRow
    {
        public ElementId RoomId { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string Level { get; set; }
        public string WallStatus { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public string FailureReason { get; set; }
        /// <summary>未生成的粉刷項目，例如「牆面、踢腳板」，若全部成功則為空字串。</summary>
        public string MissingItems { get; set; }
        /// <summary>是否有任何粉刷項目未成功生成。</summary>
        public bool IsIncomplete => !string.IsNullOrEmpty(MissingItems);
        public string CurrentWallFinish { get; set; }
        public string CurrentFloorFinish { get; set; }
        public string CurrentCeilingFinish { get; set; }
        public double CurrentCeilingHeightMm { get; set; }
        public string ReplaceWallFinish { get; set; }
        public string ReplaceFloorFinish { get; set; }
        public string ReplaceCeilingFinish { get; set; }
        public double ReplaceCeilingHeightMm { get; set; }
        public bool WallGenerated { get; set; }
        public bool FloorGenerated { get; set; }
        public bool CeilingGenerated { get; set; }
        public string IncludeInStats => (WallGenerated || FloorGenerated || CeilingGenerated) ? "是" : "否";
    }

    public partial class ExecutionStatusWindow : Window
    {
        private const string RoomPreviewViewName = "AR_RoomPreview_3D";
        private const string P_DynWallFinish = "AR_牆面塗層";
        private const string P_DynFloorFinish = "AR_樓板塗層";
        private const string P_DynCeilingFinish = "AR_天花板塗層";
        private const string P_DynCeilingHeight = "AR_天花板高度";
        private const string P_LegacyDynWallFinish = "牆面塗層";
        private const string P_LegacyDynFloorFinish = "樓板塗層";
        private const string P_LegacyDynCeilingFinish = "天花板塗層";
        private const string P_LegacyDynCeilingHeight = "天花板高度";
        private readonly UIDocument _uiDoc;
        private readonly ObservableCollection<ExecutionStatusRow> _rows = new ObservableCollection<ExecutionStatusRow>();
        private List<ExecutionStatusRow> _allRows = new List<ExecutionStatusRow>();
        private bool _showOnlyIncomplete;
        private readonly GenerationResults _generationResults;
        private readonly JumpToRoom3DHandler _jumpHandler;
        private readonly ExternalEvent _jumpEvent;

        /// <param name="focusIncomplete">若為 true，開窗時預設只顯示未完整房間。</param>
        public ExecutionStatusWindow(UIDocument uiDoc, IList<ElementId> roomIds, GenerationResults generationResults = null, bool focusIncomplete = false)
        {
            InitializeComponent();
            _uiDoc = uiDoc;
            _generationResults = generationResults;
            _showOnlyIncomplete = focusIncomplete;
            _jumpHandler = new JumpToRoom3DHandler { UiDoc = _uiDoc };
            _jumpEvent = ExternalEvent.Create(_jumpHandler);
            LoadRows(roomIds);
            TryAutoExportStatusReport();
        }

        private void LoadRows(IList<ElementId> roomIds)
        {
            var doc = _uiDoc.Document;
            IEnumerable<Room> rooms;

            if (roomIds != null && roomIds.Any())
            {
                rooms = roomIds.Select(id => doc.GetElement(id)).OfType<Room>().Where(r => r.Area > 0);
            }
            else
            {
                rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0);
            }

            foreach (var room in rooms.OrderBy(r => r.Number))
            {
                var statusText = "已執行";
                var detailText = string.Empty;
                var failureReasonText = string.Empty;
                var wallStatusText = "未執行";
                var floorStatusText = "未執行";
                var ceilingStatusText = "未執行";
                var missingItems = string.Empty;

                if (_generationResults != null && _generationResults.RoomStatuses.TryGetValue(RevitCompat.GetElementIdValue(room.Id), out var roomStatus))
                {
                    statusText = roomStatus.Status;
                    detailText = roomStatus.Detail;
                    failureReasonText = roomStatus.FailureReasonSummary;
                    wallStatusText = roomStatus.GetOperationStatus("牆面");
                    floorStatusText = roomStatus.GetOperationStatus("地板");
                    ceilingStatusText = roomStatus.GetOperationStatus("天花板");
                    if (roomStatus.FailedOperationNames.Any())
                        missingItems = string.Join("、", roomStatus.FailedOperationNames);
                }

                _allRows.Add(new ExecutionStatusRow
                {
                    RoomId = room.Id,
                    Number = room.Number,
                    Name = room.Name,
                    Level = doc.GetElement(room.LevelId)?.Name ?? string.Empty,
                    WallStatus = wallStatusText,
                    Status = statusText,
                    Detail = detailText,
                    FailureReason = failureReasonText,
                    MissingItems = missingItems,
                    CurrentWallFinish = string.Empty,
                    CurrentFloorFinish = string.Empty,
                    CurrentCeilingFinish = string.Empty,
                    CurrentCeilingHeightMm = 0,
                    ReplaceWallFinish = string.Empty,
                    ReplaceFloorFinish = string.Empty,
                    ReplaceCeilingFinish = string.Empty,
                    ReplaceCeilingHeightMm = 0,
                    WallGenerated = string.Equals(wallStatusText, "成功", StringComparison.OrdinalIgnoreCase),
                    FloorGenerated = string.Equals(floorStatusText, "成功", StringComparison.OrdinalIgnoreCase),
                    CeilingGenerated = string.Equals(ceilingStatusText, "成功", StringComparison.OrdinalIgnoreCase)
                });
            }

            ApplyFilter();
            dgStatusRooms.ItemsSource = _rows;
            LoadModelCurrentValues();

            if (_generationResults != null && _generationResults.RoomStatuses.Any())
            {
                var successRooms = _generationResults.RoomStatuses.Values.Count(x => x.FailedOperations == 0);
                var failedRooms  = _generationResults.RoomStatuses.Values.Count(x => x.FailedOperations > 0);
                txtStatus.Text = $"共 {_allRows.Count} 間房間；成功 {successRooms}，含未完整 {failedRooms}。";
                chkOnlyIncomplete.IsChecked = _showOnlyIncomplete;
            }
            else
            {
                txtStatus.Text = $"共 {_allRows.Count} 間房間可跳轉。";
                chkOnlyIncomplete.IsChecked = false;
            }
        }

        private void ApplyFilter()
        {
            _rows.Clear();
            var source = _showOnlyIncomplete
                ? _allRows.Where(r => r.IsIncomplete)
                : _allRows;
            foreach (var r in source)
                _rows.Add(r);
        }

        private void ChkOnlyIncomplete_Changed(object sender, RoutedEventArgs e)
        {
            _showOnlyIncomplete = chkOnlyIncomplete.IsChecked == true;
            ApplyFilter();
            var incompleteCount = _allRows.Count(r => r.IsIncomplete);
            txtStatus.Text = _showOnlyIncomplete
                ? $"顯示未完整房間：{_rows.Count} 間（共 {_allRows.Count} 間）"
                : $"顯示全部 {_allRows.Count} 間房間，含未完整 {incompleteCount} 間。";
        }

        private void BtnJump3D_Click(object sender, RoutedEventArgs e)
        {
            JumpToSelectedRoom();
        }

        private void DgStatusRooms_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            JumpToSelectedRoom();
        }

        private void JumpToSelectedRoom()
        {
            if (dgStatusRooms.SelectedItem is not ExecutionStatusRow row)
            {
                txtStatus.Text = "請先選擇房間。";
                return;
            }

            try
            {
                _jumpHandler.TargetRoomId = row.RoomId;
                _jumpEvent.Raise();
                txtStatus.Text = string.IsNullOrWhiteSpace(_jumpHandler.LastMessage)
                    ? "已送出跳轉請求。"
                    : _jumpHandler.LastMessage;
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"跳轉失敗：{ex.Message}";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnReloadModel_Click(object sender, RoutedEventArgs e)
        {
            LoadModelCurrentValues();
            dgStatusRooms.Items.Refresh();
            txtStatus.Text = "已重新載入模型天地牆現況。";
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "匯出房間裝修明細 (Excel)",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                    FileName = $"RoomFinish_Detail_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };

                if (dialog.ShowDialog() != true)
                    return;

                ExportStatusToXlsx(dialog.FileName);
                txtStatus.Text = $"已匯出明細：{_rows.Count} 間房間";
                MessageBox.Show("明細表已匯出為 Excel。", "匯出完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出明細失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TryAutoExportStatusReport()
        {
            try
            {
                var outputFolder = ResolveDefaultOutputFolder();
                Directory.CreateDirectory(outputFolder);

                var filePath = Path.Combine(outputFolder, $"RoomFinish_明細表_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                ExportStatusToXlsx(filePath);

                txtStatus.Text = txtStatus.Text + $" 已自動產生明細表：{filePath}";
            }
            catch (Exception ex)
            {
                txtStatus.Text = txtStatus.Text + $" 自動產生明細表失敗：{ex.Message}";
            }
        }

        private static string ResolveDefaultOutputFolder()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                var hasProjectFile = current.EnumerateFiles("YD_RevitTools.LicenseManager.csproj", SearchOption.TopDirectoryOnly).Any();
                if (hasProjectFile)
                    return Path.Combine(current.FullName, "Output");

                current = current.Parent;
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, "YD_RevitTools_Output");
        }

        private void BtnApplyReplace_Click(object sender, RoutedEventArgs e)
        {
            ApplyReplacementToRows(GetTargetRows(false), "套用取代");
        }

        private void BtnApplySelectedReplace_Click(object sender, RoutedEventArgs e)
        {
            var selectedRows = GetTargetRows(true).ToList();
            if (!selectedRows.Any())
            {
                txtStatus.Text = "請先在表格選取至少一列。";
                return;
            }

            ApplyReplacementToRows(selectedRows, "只套用選取列");
        }

        private IEnumerable<ExecutionStatusRow> GetTargetRows(bool selectedOnly)
        {
            if (!selectedOnly)
                return _rows;

            return dgStatusRooms.SelectedItems.Cast<object>().OfType<ExecutionStatusRow>();
        }

        private void ApplyReplacementToRows(IEnumerable<ExecutionStatusRow> targetRows, string actionName)
        {
            var doc = _uiDoc.Document;
            int updated = 0;
            var rows = targetRows?.ToList() ?? new List<ExecutionStatusRow>();

            if (!rows.Any())
            {
                txtStatus.Text = $"{actionName}：無可處理列。";
                return;
            }

            try
            {
                using var t = new Transaction(doc, $"AR {actionName}");
                t.Start();

                foreach (var row in rows)
                {
                    var room = doc.GetElement(row.RoomId) as Room;
                    if (room == null)
                        continue;

                    var changed = false;

                    var wallParam = LookupRoomParameter(room, P_DynWallFinish);
                    if (wallParam != null && !wallParam.IsReadOnly)
                    {
                        var targetWall = row.ReplaceWallFinish ?? string.Empty;
                        var currentWall = wallParam.AsString() ?? string.Empty;
                        if (!string.Equals(targetWall, currentWall, StringComparison.Ordinal))
                        {
                            wallParam.Set(targetWall);
                            changed = true;
                        }
                    }

                    var floorParam = LookupRoomParameter(room, P_DynFloorFinish);
                    if (floorParam != null && !floorParam.IsReadOnly)
                    {
                        var targetFloor = row.ReplaceFloorFinish ?? string.Empty;
                        var currentFloor = floorParam.AsString() ?? string.Empty;
                        if (!string.Equals(targetFloor, currentFloor, StringComparison.Ordinal))
                        {
                            floorParam.Set(targetFloor);
                            changed = true;
                        }
                    }

                    var ceilingFinishParam = LookupRoomParameter(room, P_DynCeilingFinish);
                    if (ceilingFinishParam != null && !ceilingFinishParam.IsReadOnly)
                    {
                        var targetCeilingFinish = row.ReplaceCeilingFinish ?? string.Empty;
                        var currentCeilingFinish = ceilingFinishParam.AsString() ?? string.Empty;
                        if (!string.Equals(targetCeilingFinish, currentCeilingFinish, StringComparison.Ordinal))
                        {
                            ceilingFinishParam.Set(targetCeilingFinish);
                            changed = true;
                        }
                    }

                    var ceilingParam = LookupRoomParameter(room, P_DynCeilingHeight);
                    if (ceilingParam != null && !ceilingParam.IsReadOnly)
                    {
                        var targetHeightInternal = row.ReplaceCeilingHeightMm / 304.8;
                        if (Math.Abs(ceilingParam.AsDouble() - targetHeightInternal) > 1e-9)
                        {
                            ceilingParam.Set(targetHeightInternal);
                            changed = true;
                        }
                    }

                    if (changed)
                        updated++;
                }

                t.Commit();
                LoadModelCurrentValues();
                dgStatusRooms.Items.Refresh();
                txtStatus.Text = $"{actionName}完成，更新 {updated} 間房間。";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"{actionName}失敗：{ex.Message}";
            }
        }

        private void BtnBatchReplace_Click(object sender, RoutedEventArgs e)
        {
            var oldWall = txtBatchOldWall.Text?.Trim() ?? string.Empty;
            var newWall = txtBatchNewWall.Text?.Trim() ?? string.Empty;
            var oldFloor = txtBatchOldFloor.Text?.Trim() ?? string.Empty;
            var newFloor = txtBatchNewFloor.Text?.Trim() ?? string.Empty;
            var oldCeiling = txtBatchOldCeiling.Text?.Trim() ?? string.Empty;
            var newCeiling = txtBatchNewCeiling.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(oldWall) && string.IsNullOrWhiteSpace(oldFloor) && string.IsNullOrWhiteSpace(oldCeiling))
            {
                txtStatus.Text = "請至少輸入牆舊值、地舊值或天舊值。";
                return;
            }

            var targetRows = GetTargetRows(true).ToList();
            if (!targetRows.Any())
                targetRows = _rows.ToList();

            var changed = 0;
            foreach (var row in targetRows)
            {
                var rowChanged = false;

                if (!string.IsNullOrWhiteSpace(oldWall) && string.Equals((row.CurrentWallFinish ?? string.Empty).Trim(), oldWall, StringComparison.Ordinal))
                {
                    row.ReplaceWallFinish = newWall;
                    rowChanged = true;
                }

                if (!string.IsNullOrWhiteSpace(oldFloor) && string.Equals((row.CurrentFloorFinish ?? string.Empty).Trim(), oldFloor, StringComparison.Ordinal))
                {
                    row.ReplaceFloorFinish = newFloor;
                    rowChanged = true;
                }

                if (!string.IsNullOrWhiteSpace(oldCeiling) && string.Equals((row.CurrentCeilingFinish ?? string.Empty).Trim(), oldCeiling, StringComparison.Ordinal))
                {
                    row.ReplaceCeilingFinish = newCeiling;
                    rowChanged = true;
                }

                if (rowChanged)
                    changed++;
            }

            dgStatusRooms.Items.Refresh();
            txtStatus.Text = $"批次套入完成，影響 {changed} 間房間。";
        }

        private void LoadModelCurrentValues()
        {
            var doc = _uiDoc.Document;
            var modelTypeSummary = BuildModelTypeSummaryByRoom(doc);

            foreach (var row in _rows)
            {
                var room = doc.GetElement(row.RoomId) as Room;
                if (room == null)
                    continue;

                var wallParam = LookupRoomParameter(room, P_DynWallFinish);
                var floorParam = LookupRoomParameter(room, P_DynFloorFinish);
                var ceilingFinishParam = LookupRoomParameter(room, P_DynCeilingFinish);
                var ceilingParam = LookupRoomParameter(room, P_DynCeilingHeight);

                row.CurrentWallFinish = wallParam?.AsString() ?? string.Empty;
                row.CurrentFloorFinish = floorParam?.AsString() ?? string.Empty;
                row.CurrentCeilingFinish = ceilingFinishParam?.AsString() ?? string.Empty;
                row.CurrentCeilingHeightMm = (ceilingParam?.AsDouble() ?? 0) * 304.8;

                var roomIdVal = RevitCompat.GetElementIdValue(row.RoomId);
                if (modelTypeSummary.TryGetValue(roomIdVal, out var modelText))
                {
                    if (!string.IsNullOrWhiteSpace(modelText.Wall))
                        row.CurrentWallFinish = modelText.Wall;
                    if (!string.IsNullOrWhiteSpace(modelText.Floor))
                        row.CurrentFloorFinish = modelText.Floor;
                    if (!string.IsNullOrWhiteSpace(modelText.Ceiling))
                        row.CurrentCeilingFinish = modelText.Ceiling;
                }

                if (string.IsNullOrWhiteSpace(row.ReplaceWallFinish))
                    row.ReplaceWallFinish = row.CurrentWallFinish;

                if (string.IsNullOrWhiteSpace(row.ReplaceFloorFinish))
                    row.ReplaceFloorFinish = row.CurrentFloorFinish;

                if (string.IsNullOrWhiteSpace(row.ReplaceCeilingFinish))
                    row.ReplaceCeilingFinish = row.CurrentCeilingFinish;

                if (row.ReplaceCeilingHeightMm <= 0)
                    row.ReplaceCeilingHeightMm = row.CurrentCeilingHeightMm;
            }
        }

        private void ExportStatusToXlsx(string filePath)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            using (var document = SpreadsheetDocument.Create(filePath, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStylesheet();
                stylesPart.Stylesheet.Save();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;
                BuildDetailSheet(workbookPart, sheets, ref sheetId);
                BuildSummarySheet(workbookPart, sheets, ref sheetId);
                BuildLevelSheets(workbookPart, sheets, ref sheetId);
                workbookPart.Workbook.Save();
            }
        }

        private void BuildDetailSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId)
        {
            var headers = new[]
            {
                "RoomId", "房間編號", "房間名稱", "樓層", "牆面狀態", "總狀態", "明細", "失敗原因",
                "牆生成", "地生成", "天生成", "納入統計",
                "牆(現況)", "地(現況)", "天(現況)", "天高(現況mm)",
                "牆(取代)", "地(取代)", "天(取代)", "天高(取代mm)"
            };

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            var sheetView = new SheetView { WorkbookViewId = 0U };
            sheetView.Append(new Pane
            {
                VerticalSplit = 1D,
                TopLeftCell = "A2",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            });

            var worksheet = new Worksheet();
            worksheet.Append(new Columns(
                new Column { Min = 1, Max = 1, Width = 12, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 12, CustomWidth = true },
                new Column { Min = 3, Max = 3, Width = 20, CustomWidth = true },
                new Column { Min = 4, Max = 4, Width = 14, CustomWidth = true },
                new Column { Min = 5, Max = 6, Width = 12, CustomWidth = true },
                new Column { Min = 7, Max = 8, Width = 24, CustomWidth = true },
                new Column { Min = 9, Max = 12, Width = 12, CustomWidth = true },
                new Column { Min = 13, Max = 15, Width = 18, CustomWidth = true },
                new Column { Min = 16, Max = 16, Width = 14, CustomWidth = true },
                new Column { Min = 17, Max = 19, Width = 18, CustomWidth = true },
                new Column { Min = 20, Max = 20, Width = 14, CustomWidth = true }
            ));
            worksheet.Append(new SheetViews(sheetView));
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

            foreach (var row in _rows)
            {
                var dataRow = new Row();
                dataRow.Append(CreateNumberCell(RevitCompat.GetElementIdValue(row.RoomId), 1));
                dataRow.Append(CreateTextCell(row.Number, 1));
                dataRow.Append(CreateTextCell(row.Name, 1));
                dataRow.Append(CreateTextCell(row.Level, 1));
                dataRow.Append(CreateTextCell(row.WallStatus, 1));
                dataRow.Append(CreateTextCell(row.Status, 1));
                dataRow.Append(CreateTextCell(row.Detail, 1));
                dataRow.Append(CreateTextCell(row.FailureReason, 1));
                dataRow.Append(CreateTextCell(row.WallGenerated ? "成功" : "失敗", 1));
                dataRow.Append(CreateTextCell(row.FloorGenerated ? "成功" : "失敗", 1));
                dataRow.Append(CreateTextCell(row.CeilingGenerated ? "成功" : "失敗", 1));
                dataRow.Append(CreateTextCell(row.IncludeInStats, 1));
                dataRow.Append(CreateTextCell(row.CurrentWallFinish, 1));
                dataRow.Append(CreateTextCell(row.CurrentFloorFinish, 1));
                dataRow.Append(CreateTextCell(row.CurrentCeilingFinish, 1));
                dataRow.Append(CreateNumberCell(row.CurrentCeilingHeightMm, 1));
                dataRow.Append(CreateTextCell(row.ReplaceWallFinish, 1));
                dataRow.Append(CreateTextCell(row.ReplaceFloorFinish, 1));
                dataRow.Append(CreateTextCell(row.ReplaceCeilingFinish, 1));
                dataRow.Append(CreateNumberCell(row.ReplaceCeilingHeightMm, 1));
                sheetData.Append(dataRow);
            }

            var lastRow = Math.Max(_rows.Count + 1, 1);
            worksheet.Append(new AutoFilter { Reference = $"A1:T{lastRow}" });
        }

        private void BuildSummarySheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var mergeCells = new MergeCells();

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

            var levels = _rows.Select(x => x.Level ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetLevelSortKey)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!levels.Any())
                levels.Add("未分層");

            uint rowIndex = 1;
            var lastColumn = 1 + levels.Count + 1;
            var lastColumnName = GetColumnName(lastColumn);

            var titleRow = new Row { RowIndex = rowIndex };
            titleRow.Append(CreateTextCell("粉刷明細表  總表", 7));
            sheetData.Append(titleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex++;

            var subtitleRow = new Row { RowIndex = rowIndex };
            subtitleRow.Append(CreateTextCell($"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}    房間數：{_rows.Count}", 2));
            sheetData.Append(subtitleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex += 2;

            var sections = new List<(string Title, Func<ExecutionStatusRow, string> Selector, Func<ExecutionStatusRow, bool> Include, uint Style)>
            {
                ("牆面材料明細（現況-僅成功）", r => r.CurrentWallFinish, r => r.WallGenerated, 3),
                ("地坪材料明細（現況-僅成功）", r => r.CurrentFloorFinish, r => r.FloorGenerated, 4),
                ("天花材料明細（現況-僅成功）", r => r.CurrentCeilingFinish, r => r.CeilingGenerated, 5),
                ("牆面材料明細（取代）", r => r.ReplaceWallFinish, r => true, 6),
                ("地坪材料明細（取代）", r => r.ReplaceFloorFinish, r => true, 6),
                ("天花材料明細（取代）", r => r.ReplaceCeilingFinish, r => true, 6)
            };

            foreach (var section in sections)
            {
                AppendSection(sheetData, mergeCells, ref rowIndex, levels, section.Title, section.Selector, section.Include, section.Style, lastColumnName);
                rowIndex += 1;
            }
        }

        private Dictionary<long, (string Wall, string Floor, string Ceiling)> BuildModelTypeSummaryByRoom(Document doc)
        {
            var walls = new Dictionary<long, HashSet<string>>();
            var floors = new Dictionary<long, HashSet<string>>();
            var ceilings = new Dictionary<long, HashSet<string>>();

            void add(Dictionary<long, HashSet<string>> map, long roomId, string name)
            {
                if (roomId <= 0 || string.IsNullOrWhiteSpace(name))
                    return;
                if (!map.TryGetValue(roomId, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[roomId] = set;
                }
                set.Add(name.Trim());
            }

            long readRoomId(Element element)
            {
                var p = element.LookupParameter("房間ID(AR_RoomId)")
                    ?? element.LookupParameter("房間ID")
                    ?? element.LookupParameter("AR_RoomId");
                if (p == null) return 0;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                return long.TryParse(p.AsString(), out var v) ? v : 0;
            }

            foreach (var elem in new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType().ToElements())
            {
                if (!FinishingElementGuard.IsManagedFinishingElement(elem)) continue;
                add(walls, readRoomId(elem), doc.GetElement(elem.GetTypeId())?.Name ?? string.Empty);
            }
            foreach (var elem in new FilteredElementCollector(doc).OfClass(typeof(Floor)).WhereElementIsNotElementType().ToElements())
            {
                if (!FinishingElementGuard.IsManagedFinishingElement(elem)) continue;
                add(floors, readRoomId(elem), doc.GetElement(elem.GetTypeId())?.Name ?? string.Empty);
            }
            foreach (var elem in new FilteredElementCollector(doc).OfClass(typeof(Ceiling)).WhereElementIsNotElementType().ToElements())
            {
                if (!FinishingElementGuard.IsManagedFinishingElement(elem)) continue;
                add(ceilings, readRoomId(elem), doc.GetElement(elem.GetTypeId())?.Name ?? string.Empty);
            }

            var roomIds = walls.Keys.Union(floors.Keys).Union(ceilings.Keys);
            var result = new Dictionary<long, (string Wall, string Floor, string Ceiling)>();
            foreach (var id in roomIds)
            {
                var wall = walls.TryGetValue(id, out var w) ? string.Join("；", w) : string.Empty;
                var floor = floors.TryGetValue(id, out var f) ? string.Join("；", f) : string.Empty;
                var ceiling = ceilings.TryGetValue(id, out var c) ? string.Join("；", c) : string.Empty;
                result[id] = (wall, floor, ceiling);
            }
            return result;
        }

        private void BuildLevelSheets(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId)
        {
            var levels = _rows.Select(x => x.Level ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetLevelSortKey)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var level in levels)
            {
                var levelRows = _rows.Where(x => string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!levelRows.Any())
                    continue;

                BuildSingleLevelSheet(workbookPart, sheets, ref sheetId, level, levelRows);
            }
        }

        private void BuildSingleLevelSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, string level, List<ExecutionStatusRow> levelRows)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var mergeCells = new MergeCells();

            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 48, CustomWidth = true });
            columns.Append(new Column { Min = 2, Max = 2, Width = 16, CustomWidth = true });

            var worksheet = new Worksheet();
            worksheet.Append(columns);
            worksheet.Append(sheetData);
            worksheet.Append(mergeCells);
            worksheetPart.Worksheet = worksheet;

            var safeSheetName = NormalizeLevelLabel(level);
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = safeSheetName.Length > 28 ? safeSheetName.Substring(0, 28) : safeSheetName
            });

            uint rowIndex = 1;
            var titleRow = new Row { RowIndex = rowIndex };
            titleRow.Append(CreateTextCell($"{NormalizeLevelLabel(level)} 粉刷明細表", 7));
            sheetData.Append(titleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:B{rowIndex}") });
            rowIndex += 2;

            var sections = new List<(string Title, Func<ExecutionStatusRow, string> Selector, Func<ExecutionStatusRow, bool> Include, uint Style)>
            {
                ("牆面材料（僅成功）", r => r.CurrentWallFinish, r => r.WallGenerated, 3),
                ("地坪材料（僅成功）", r => r.CurrentFloorFinish, r => r.FloorGenerated, 4),
                ("天花材料（僅成功）", r => r.CurrentCeilingFinish, r => r.CeilingGenerated, 5)
            };

            foreach (var section in sections)
            {
                var sectionTitleRow = new Row { RowIndex = rowIndex };
                sectionTitleRow.Append(CreateTextCell(section.Title, section.Style));
                sheetData.Append(sectionTitleRow);
                mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:B{rowIndex}") });
                rowIndex++;

                var headerRow = new Row { RowIndex = rowIndex };
                headerRow.Append(CreateTextCell("項目", 2));
                headerRow.Append(CreateTextCell("明細數", 2));
                sheetData.Append(headerRow);
                rowIndex++;

                var groups = levelRows
                    .Where(section.Include)
                    .GroupBy(r => (section.Selector(r) ?? string.Empty).Trim())
                    .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var group in groups)
                {
                    var dataRow = new Row { RowIndex = rowIndex };
                    dataRow.Append(CreateTextCell(group.Key, 1));
                    dataRow.Append(CreateNumberCell(group.Count(), 1));
                    sheetData.Append(dataRow);
                    rowIndex++;
                }

                var totalRow = new Row { RowIndex = rowIndex };
                totalRow.Append(CreateTextCell("小計", 2));
                totalRow.Append(CreateNumberCell(groups.Sum(x => x.Count()), 2));
                sheetData.Append(totalRow);
                rowIndex += 2;
            }
        }

        private void AppendSection(SheetData sheetData, MergeCells mergeCells, ref uint rowIndex, List<string> levels,
            string title, Func<ExecutionStatusRow, string> selector, Func<ExecutionStatusRow, bool> includePredicate, uint sectionStyle, string lastColumnName)
        {
            var sectionTitleRow = new Row { RowIndex = rowIndex };
            sectionTitleRow.Append(CreateTextCell(title, sectionStyle));
            sheetData.Append(sectionTitleRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:{lastColumnName}{rowIndex}") });
            rowIndex++;

            var headerRow = new Row { RowIndex = rowIndex };
            headerRow.Append(CreateTextCell("項目", 2));
            foreach (var level in levels)
                headerRow.Append(CreateTextCell($"{NormalizeLevelLabel(level)}明細數", 2));
            headerRow.Append(CreateTextCell("總明細數", 2));
            sheetData.Append(headerRow);
            rowIndex++;

            var groups = _rows
                .Where(includePredicate)
                .GroupBy(r => (selector(r) ?? string.Empty).Trim())
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in groups)
            {
                var dataRow = new Row { RowIndex = rowIndex };
                dataRow.Append(CreateTextCell(group.Key, 1));

                var total = 0;
                foreach (var level in levels)
                {
                    var count = group.Count(x => includePredicate(x) && string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase));
                    dataRow.Append(CreateNumberCell(count, 1));
                    total += count;
                }

                dataRow.Append(CreateNumberCell(total, 1));
                sheetData.Append(dataRow);
                rowIndex++;
            }

            var totalRow = new Row { RowIndex = rowIndex };
            totalRow.Append(CreateTextCell("小計", 2));
            var grandTotal = 0;
            foreach (var level in levels)
            {
                var count = _rows.Count(x => includePredicate(x) &&
                                           !string.IsNullOrWhiteSpace((selector(x) ?? string.Empty).Trim()) &&
                                           string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase));
                totalRow.Append(CreateNumberCell(count, 2));
                grandTotal += count;
            }
            totalRow.Append(CreateNumberCell(grandTotal, 2));
            sheetData.Append(totalRow);
            rowIndex++;
        }

        private static Stylesheet CreateStylesheet()
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
                new Border(),
                new Border(
                    new LeftBorder { Style = BorderStyleValues.Thin },
                    new RightBorder { Style = BorderStyleValues.Thin },
                    new TopBorder { Style = BorderStyleValues.Thin },
                    new BottomBorder { Style = BorderStyleValues.Thin },
                    new DiagonalBorder())
            );

            var cellFormats = new CellFormats(
                new CellFormat(),
                new CellFormat { FontId = 0, FillId = 0, BorderId = 1, ApplyBorder = true },
                new CellFormat { FontId = 1, FillId = 2, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
                new CellFormat { FontId = 1, FillId = 3, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
                new CellFormat { FontId = 1, FillId = 4, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
                new CellFormat { FontId = 1, FillId = 5, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
                new CellFormat { FontId = 1, FillId = 6, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
                new CellFormat { FontId = 2, FillId = 2, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true }
            );

            return new Stylesheet(fonts, fills, borders, cellFormats);
        }

        private static string GetColumnName(int columnNumber)
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

        private static string NormalizeLevelLabel(string level)
        {
            if (string.IsNullOrWhiteSpace(level))
                return "未分層";

            return level.Replace("FL", "F").Replace("樓", string.Empty).Trim();
        }

        private static int GetLevelSortKey(string level)
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

            var digits = new string(text.TakeWhile(c => char.IsDigit(c)).ToArray());
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
                CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
            };
        }

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
    }
}

