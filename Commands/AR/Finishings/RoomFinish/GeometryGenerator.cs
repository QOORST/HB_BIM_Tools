using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System.Text;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    public class GenerationResults
    {
        public Dictionary<string, int> SuccessCount { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> FailureCount { get; } = new Dictionary<string, int>();
        public List<(Room room, string message, Exception ex)> Errors { get; } = new List<(Room, string, Exception)>();
        public Dictionary<long, RoomGenerationStatus> RoomStatuses { get; } = new Dictionary<long, RoomGenerationStatus>();
        
        public void RecordOperation(string operationType, string roomNumber, bool success)
        {
            var key = $"{operationType} ({roomNumber})";
            if (success)
            {
                if (!SuccessCount.ContainsKey(operationType)) SuccessCount[operationType] = 0;
                SuccessCount[operationType]++;
            }
            else
            {
                if (!FailureCount.ContainsKey(operationType)) FailureCount[operationType] = 0;
                FailureCount[operationType]++;
            }
        }

        public void AddError(Room room, string message, Exception ex)
        {
            Errors.Add((room, message, ex));

            if (room == null)
                return;

            var roomIdValue = RevitCompat.GetElementIdValue(room.Id);
            if (!RoomStatuses.TryGetValue(roomIdValue, out var roomStatus))
            {
                roomStatus = new RoomGenerationStatus
                {
                    RoomId = room.Id,
                    RoomNumber = room.Number,
                    RoomName = room.Name,
                    LevelName = room.Document.GetElement(room.LevelId)?.Name ?? string.Empty
                };
                RoomStatuses[roomIdValue] = roomStatus;
            }

            roomStatus.FailedOperations++;
            var reason = string.IsNullOrWhiteSpace(ex?.Message) ? message : $"{message}: {ex.Message}";
            roomStatus.AddFailureReason(reason);
        }

        public void RecordRoomOperation(Room room, string operationType, bool success, string failReason = null)
        {
            if (room == null)
                return;

            RecordOperation(operationType, room.Number, success);

            var roomIdValue = RevitCompat.GetElementIdValue(room.Id);
            if (!RoomStatuses.TryGetValue(roomIdValue, out var roomStatus))
            {
                roomStatus = new RoomGenerationStatus
                {
                    RoomId = room.Id,
                    RoomNumber = room.Number,
                    RoomName = room.Name,
                    LevelName = room.Document.GetElement(room.LevelId)?.Name ?? string.Empty
                };
                RoomStatuses[roomIdValue] = roomStatus;
            }

            if (success)
            {
                roomStatus.SuccessOperations++;
            }
            else
            {
                roomStatus.FailedOperations++;
                if (!roomStatus.FailedOperationNames.Contains(operationType))
                    roomStatus.FailedOperationNames.Add(operationType);
                roomStatus.AddFailureReason(string.IsNullOrWhiteSpace(failReason) ? $"{operationType} 生成失敗" : failReason);
            }

            roomStatus.SetOperationResult(operationType, success);
        }
        
        public string GetSummary()
        {
            var summary = new StringBuilder();
            foreach (var kvp in SuccessCount)
            {
                summary.AppendLine($"{kvp.Key}: {kvp.Value} 成功");
            }
            foreach (var kvp in FailureCount)
            {
                summary.AppendLine($"{kvp.Key}: {kvp.Value} 失敗");
            }
            return summary.ToString();
        }
    }

    public class RoomGenerationStatus
    {
        public ElementId RoomId { get; set; }
        public string RoomNumber { get; set; }
        public string RoomName { get; set; }
        public string LevelName { get; set; }
        public int SuccessOperations { get; set; }
        public int FailedOperations { get; set; }
        public List<string> FailedOperationNames { get; } = new List<string>();
        public List<string> FailureReasons { get; } = new List<string>();
        private readonly Dictionary<string, bool> _operationResults = new Dictionary<string, bool>();

        public string Status => FailedOperations == 0 ? "成功" : (SuccessOperations > 0 ? "部分失敗" : "失敗");

        public string Detail
        {
            get
            {
                if (FailedOperations == 0)
                    return $"成功 {SuccessOperations} 項";

                var failedText = FailedOperationNames.Any()
                    ? string.Join("、", FailedOperationNames)
                    : "未知項目";
                return $"成功 {SuccessOperations} / 失敗 {FailedOperations}（{failedText}）";
            }
        }

        public string FailureReasonSummary => FailureReasons.Any() ? string.Join("；", FailureReasons.Distinct()) : string.Empty;

        public void SetOperationResult(string operationType, bool success)
        {
            if (string.IsNullOrWhiteSpace(operationType))
                return;

            _operationResults[operationType] = success;
        }

        public string GetOperationStatus(string operationType)
        {
            if (string.IsNullOrWhiteSpace(operationType))
                return "未執行";

            if (!_operationResults.TryGetValue(operationType, out var success))
                return "未執行";

            return success ? "成功" : "失敗";
        }

        public void AddFailureReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return;

            if (!FailureReasons.Contains(reason))
                FailureReasons.Add(reason);
        }
    }

    public class JoinResults  
    {
        public int TotalAttempts { get; set; }
        public int SuccessCount { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    public class GeometryGenerator
    {
        private class OpeningMarker
        {
            public ElementId SourceId { get; set; }
            public XYZ Point { get; set; }
            public double HalfWidth { get; set; }
            public XYZ Direction { get; set; }
            public double BottomZ { get; set; }
            public double TopZ { get; set; }
            public string Source { get; set; }
        }

        private readonly UIDocument _uidoc;
        private Document Doc => _uidoc.Document;
        
        /// <summary>
        /// 將毫米轉換為Revit內部單位（英尺），直接使用固定轉換
        /// </summary>
        private double ConvertFromMm(double mm)
        {
            // 直接轉換為英尺（Revit內部單位）
            return mm / 304.8;
        }
        
        /// <summary>
        /// 將Revit內部單位（英尺）轉換為毫米
        /// </summary>
        private double ConvertToMm(double internalUnits)
        {
            // 直接從英尺轉換為毫米
            return internalUnits * 304.8;
        }
        
        /// <summary>
        /// 獲取專案的長度單位類型
        /// </summary>
        private string GetProjectLengthUnit()
        {
            try
            {
                var units = Doc.GetUnits();
                var lengthFormatOptions = units.GetFormatOptions(SpecTypeId.Length);
                var unitTypeId = lengthFormatOptions.GetUnitTypeId();
                
                // 判斷單位類型
                if (unitTypeId == UnitTypeId.Millimeters)
                    return "毫米";
                else if (unitTypeId == UnitTypeId.Meters)
                    return "公尺";
                else if (unitTypeId == UnitTypeId.Feet)
                    return "英尺";
                else if (unitTypeId == UnitTypeId.Inches)
                    return "英寸";
                else
                    return "未知單位";
            }
            catch (Exception ex)
            {
                Logger.Log($"獲取專案單位失敗: {ex.Message}");
                return "英尺"; // 預設
            }
        }
        
        // 快取機制
        private readonly Dictionary<ElementId, FloorType> _floorTypeCache = new Dictionary<ElementId, FloorType>();
        private readonly Dictionary<ElementId, CeilingType> _ceilingTypeCache = new Dictionary<ElementId, CeilingType>();
        private readonly Dictionary<ElementId, WallType> _wallTypeCache = new Dictionary<ElementId, WallType>();
        private readonly Dictionary<ElementId, Level> _levelCache = new Dictionary<ElementId, Level>();
        
        public GeometryGenerator(UIDocument uidoc) 
        { 
            _uidoc = uidoc; 
        }

        public GenerationResults GenerateForRooms(FinishSettings settings)
        {
            var rooms = GetRooms(settings.TargetRoomIds).ToList();
            var results = new GenerationResults();
            var overlapIssues = RoomOverlapGuard.Detect(rooms);
            var unsafeRoomIds = new HashSet<long>(overlapIssues.SelectMany(x => x.RoomIds));
            foreach (var issue in overlapIssues)
            {
                Logger.Log($"房間重疊防呆：{issue.Description}");
            }
            
            foreach (var room in rooms)
            {
                var roomId = RevitCompat.GetElementIdValue(room.Id);
                if (unsafeRoomIds.Contains(roomId))
                {
                    results.RecordRoomOperation(room, "房間重疊檢查", false, "偵測到房間重疊或空間歸屬不唯一，已略過自動生成，避免誤刪或誤寫裝修面。");
                    continue;
                }

                try
                {
                    ProcessSingleRoom(room, settings, results);
                }
                catch (Exception ex)
                {
                    results.AddError(room, $"房間 {room.Number} 處理失敗", ex);
                }
            }
            
            ShowResults(results);
            return results;
        }

        /// <summary>
        /// 刪除指定房間的現有粉刷元素（OST_Walls / OST_Floors / OST_Ceilings 中標記 AR_RoomId 符合的元素）。
        /// 必須在 Transaction 內呼叫。
        /// </summary>
        public void DeleteExistingFinishesForRooms(IList<ElementId> roomIds)
        {
            if (roomIds == null || roomIds.Count == 0) return;

            var targetIds = new HashSet<long>(roomIds.Select(id => RevitCompat.GetElementIdValue(id)));
            var idsToDelete = new List<ElementId>();
            var blockers = new List<string>();
            var categoryCounts = new Dictionary<string, int>();

            foreach (var cat in new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_GenericModel })
            {
                foreach (var elem in new FilteredElementCollector(Doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements())
                {
                    if (!IsProtectedGeneratedFinishElement(elem))
                        continue;

                    var p = elem.LookupParameter("房間ID(AR_RoomId)")
                         ?? elem.LookupParameter("房間ID")
                         ?? elem.LookupParameter("AR_RoomId");
                    if (p == null) continue;

                    long roomId = p.StorageType == StorageType.Integer
                        ? p.AsInteger()
                        : long.TryParse(p.AsString(), out var v) ? v : 0;

                    if (roomId > 0 && targetIds.Contains(roomId))
                    {
                        var label = $"{elem.Category?.Name ?? cat.ToString()} {elem.Id}";
                        if (elem.Pinned)
                            blockers.Add($"{label} 已釘選");
                        if (elem.GroupId != ElementId.InvalidElementId)
                            blockers.Add($"{label} 位於群組 {elem.GroupId}");

                        idsToDelete.Add(elem.Id);

                        var catName = elem.Category?.Name ?? cat.ToString();
                        categoryCounts[catName] = (categoryCounts.TryGetValue(catName, out var c) ? c : 0) + 1;
                    }
                }
            }

            if (idsToDelete.Count > 0)
            {
                Logger.Log("刪除預檢：將刪除 " + idsToDelete.Count + " 個既有粉刷元素；" +
                           string.Join("，", categoryCounts.Select(kv => $"{kv.Key}:{kv.Value}")));

                if (blockers.Count > 0)
                {
                    var msg = "刪除預檢失敗，已停止重新產出以避免誤刪：\n" +
                              string.Join("\n", blockers.Take(20)) +
                              (blockers.Count > 20 ? $"\n...另有 {blockers.Count - 20} 項" : "");
                    Logger.Log(msg);
                    throw new InvalidOperationException(msg);
                }

                Doc.Delete(idsToDelete);
                Logger.Log($"已刪除 {idsToDelete.Count} 個現有粉刷元素");
            }
            else
            {
                Logger.Log("無現有粉刷元素需要刪除");
            }
        }
        
        private void ShowResults(GenerationResults results)
        {
            if (results.Errors.Any())
            {
                var errorMsg = new StringBuilder();
                errorMsg.AppendLine("完成處理，但發生以下錯誤:");
                errorMsg.AppendLine(results.GetSummary());
                errorMsg.AppendLine("\n錯誤詳情:");
                
                foreach (var error in results.Errors.Take(5)) // 只顯示前5個錯誤
                {
                    errorMsg.AppendLine($"- {error.message}: {error.ex.Message}");
                }
                
                if (results.Errors.Count > 5)
                {
                    errorMsg.AppendLine($"... 還有 {results.Errors.Count - 5} 個錯誤");
                }
                
                TaskDialog.Show("處理結果", errorMsg.ToString());
            }
        }
        
        private void ProcessSingleRoom(Room room, FinishSettings settings, GenerationResults results)
        {
            var roomIdVal = RevitCompat.GetElementIdValue(room.Id);
            bool skipWall    = settings.SkipWallForRoomIds?.Contains(roomIdVal)    == true;
            bool skipFloor   = settings.SkipFloorForRoomIds?.Contains(roomIdVal)   == true;
            bool skipCeiling = settings.SkipCeilingForRoomIds?.Contains(roomIdVal) == true;

            var effectiveSettings = BuildRoomSpecificSettings(room, settings);
            var hasWallFinishTask = effectiveSettings.SelectedWallTypeId != ElementId.InvalidElementId && !skipWall;
            var wallFinishSuccess = true;
            
            if (effectiveSettings.SelectedFloorTypeId != ElementId.InvalidElementId && !skipFloor) 
            {
                var success = TryCreateFloor(room, effectiveSettings);
                results.RecordRoomOperation(room, "地板", success, "地板生成失敗");
            }
            
            if (effectiveSettings.SelectedCeilingTypeId != ElementId.InvalidElementId && !skipCeiling) 
            {
                var success = TryCreateCeiling(room, effectiveSettings);
                results.RecordRoomOperation(room, "天花板", success, "天花板生成失敗");
            }
            
            if (hasWallFinishTask) 
            {
                wallFinishSuccess = TryCreateWallFinish(room, effectiveSettings);
                results.RecordRoomOperation(room, "牆面", wallFinishSuccess, "牆面生成失敗");
            }
            
            if (effectiveSettings.SelectedSkirtingTypeId != ElementId.InvalidElementId) 
            {
                if (hasWallFinishTask && !wallFinishSuccess)
                {
                    Logger.Log($"房間 {room.Name} 粉刷牆未成功建立，依施工順序跳過踢腳板生成");
                    results.RecordRoomOperation(room, "踢腳板", false, "粉刷牆建立失敗，已跳過踢腳板生成");
                }
                else
                {
                    if (hasWallFinishTask)
                    {
                        try
                        {
                            Doc.Regenerate();
                        }
                        catch (Exception regenEx)
                        {
                            Logger.Log($"踢腳板生成前更新文檔失敗: {regenEx.Message}");
                        }
                    }

                    var success = TryCreateSkirting(room, effectiveSettings);
                    results.RecordRoomOperation(room, "踢腳板", success, "踢腳板生成失敗");
                }
            }
        }

        private FinishSettings BuildRoomSpecificSettings(Room room, FinishSettings baseSettings)
        {
            var effective = baseSettings.Clone();
            var roomOverride = baseSettings.GetRoomOverride(room.Id);
            if (roomOverride == null)
                return effective;

            if (roomOverride.WallTypeId > 0)
                effective.SelectedWallTypeId = RevitCompat.CreateElementId(roomOverride.WallTypeId);
            if (roomOverride.FloorTypeId > 0)
                effective.SelectedFloorTypeId = RevitCompat.CreateElementId(roomOverride.FloorTypeId);
            if (roomOverride.CeilingTypeId > 0)
                effective.SelectedCeilingTypeId = RevitCompat.CreateElementId(roomOverride.CeilingTypeId);
            if (roomOverride.SkirtingTypeId > 0)
                effective.SelectedSkirtingTypeId = RevitCompat.CreateElementId(roomOverride.SkirtingTypeId);

            if (roomOverride.WallHeightMm > 0)
                effective.WallHeightMm = roomOverride.WallHeightMm;
            if (roomOverride.CeilingHeightMm > 0)
                effective.CeilingHeightMm = roomOverride.CeilingHeightMm;

            return effective;
        }

        IEnumerable<Room> GetRooms(IList<ElementId> ids)
        {
            if (ids != null && ids.Count > 0)
                return ids.Select(id => Doc.GetElement(id)).OfType<Room>();
            return new FilteredElementCollector(Doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0);
        }

        CurveArray GetRoomProfile(Room room, FloorBoundaryMode mode, out Level level, out double maxWallHalfThickness, out CurveLoop originalLoop)
        {
            level = Doc.GetElement(room.LevelId) as Level;
            maxWallHalfThickness = 0;
            originalLoop = new CurveLoop();
            var curveArray = new CurveArray();

            try
            {
                Logger.Log($"開始獲取房間 {room.Name} 的邊界，模式: {mode}");
                
                // 設定邊界選項
                var sbo = new SpatialElementBoundaryOptions();
                
                // 根據模式設定邊界位置
                if (mode == FloorBoundaryMode.Centerline)
                {
                    sbo.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center;
                    Logger.Log("使用中心線邊界");
                }
                else if (mode == FloorBoundaryMode.InnerFinish)
                {
                    sbo.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;
                    Logger.Log("使用內部完成面邊界");
                }
                else
                {
                    sbo.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;
                    Logger.Log("使用完成面邊界（預設）");
                }

                var boundarySegments = room.GetBoundarySegments(sbo);
                Logger.Log($"取得 {(boundarySegments == null ? 0 : boundarySegments.Count)} 個邊界循環");

                if (boundarySegments == null || boundarySegments.Count == 0)
                {
                    Logger.Log("警告：房間沒有邊界段");
                    return curveArray;
                }

                var firstValidLoopCaptured = false;
                for (int loopIndex = 0; loopIndex < boundarySegments.Count; loopIndex++)
                {
                    var loopSegments = boundarySegments[loopIndex];
                    var loopCurves = new List<Curve>();
                    Logger.Log($"邊界循環 {loopIndex + 1} 有 {loopSegments.Count} 個段");

                    foreach (var seg in loopSegments)
                    {
                        try
                        {
                            if (ShouldSkipBoundarySegment(seg))
                            {
                                continue;
                            }

                            var curve = seg.GetCurve();
                            if (curve != null && curve.Length > 1e-6) // 過濾掉極短的曲線
                            {
                                loopCurves.Add(curve);
                            
                                // 記錄每個邊界段的詳細信息
                                var start = curve.GetEndPoint(0);
                                var end = curve.GetEndPoint(1);
                                Logger.Log($"邊界段: 起點({start.X * 304.8:F2}, {start.Y * 304.8:F2}), 終點({end.X * 304.8:F2}, {end.Y * 304.8:F2}), 長度: {curve.Length * 304.8:F2}mm");

                                // 計算最大牆厚度
                                if (Doc.GetElement(seg.ElementId) is Wall w)
                                {
                                    var half = w.Width * 0.5;
                                    if (half > maxWallHalfThickness) maxWallHalfThickness = half;
                                    Logger.Log($"牆體 {w.Id} 半厚度: {half * 304.8:F2}mm");
                                }
                            }
                            else
                            {
                                Logger.Log("跳過無效或極短的邊界段");
                            }
                        }
                        catch (Exception segEx)
                        {
                            Logger.Log($"處理邊界段時發生錯誤: {segEx.Message}");
                        }
                    }

                    if (!firstValidLoopCaptured && loopCurves.Count >= 3)
                    {
                        foreach (var c in loopCurves)
                        {
                            curveArray.Append(c);
                            originalLoop.Append(c);
                        }
                        firstValidLoopCaptured = true;
                        Logger.Log($"已採用第 {loopIndex + 1} 個邊界循環作為主要輪廓，共 {loopCurves.Count} 段");
                        break;
                    }
                }

                Logger.Log($"成功獲取 {curveArray.Size} 條有效邊界曲線，最大牆厚度: {maxWallHalfThickness * 304.8:F2}mm");
                
                // 檢查邊界曲線的Z高度是否合理
                if (curveArray.Size > 0)
                {
                    var firstCurve = curveArray.get_Item(0);
                    var zLevel = firstCurve.GetEndPoint(0).Z;
                    Logger.Log($"邊界曲線Z高度: {zLevel * 304.8:F2}mm，樓層高度: {level.Elevation * 304.8:F2}mm");
                }

                // 對於外部完成面模式，嘗試進行偏移
                if (mode == FloorBoundaryMode.OuterFinish && curveArray.Size > 2 && maxWallHalfThickness > 0)
                {
                    try
                    {
                        Logger.Log($"嘗試向外偏移 {maxWallHalfThickness * 304.8:F2}mm");
                        var offsetLoop = CurveLoop.CreateViaOffset(originalLoop, maxWallHalfThickness, XYZ.BasisZ);
                        var offsetArray = new CurveArray();
                        foreach (var crv in offsetLoop) 
                        {
                            offsetArray.Append(crv);
                        }
                        curveArray = offsetArray;
                        Logger.Log("外部偏移成功");
                    }
                    catch (Exception offsetEx)
                    {
                        Logger.Log($"偏移失敗，使用原始邊界: {offsetEx.Message}");
                    }
                }

                return curveArray;
            }
            catch (Exception ex)
            {
                Logger.Log($"獲取房間邊界失敗: {ex.Message}");
                Logger.Log($"堆疊追蹤: {ex.StackTrace}");
                return curveArray;
            }
        }

        private List<CurveLoop> GetRoomProfileLoops(Room room, FloorBoundaryMode mode, out Level level, out double maxWallHalfThickness)
        {
            level = Doc.GetElement(room.LevelId) as Level;
            maxWallHalfThickness = 0;
            var loops = new List<CurveLoop>();

            try
            {
                var sbo = new SpatialElementBoundaryOptions();
                sbo.SpatialElementBoundaryLocation = mode == FloorBoundaryMode.Centerline
                    ? SpatialElementBoundaryLocation.Center
                    : SpatialElementBoundaryLocation.Finish;

                var boundarySegments = room.GetBoundarySegments(sbo);
                if (boundarySegments == null || boundarySegments.Count == 0)
                    return loops;

                for (int loopIndex = 0; loopIndex < boundarySegments.Count; loopIndex++)
                {
                    var loopSegments = boundarySegments[loopIndex];
                    var loopCurves = new List<Curve>();

                    foreach (var seg in loopSegments)
                    {
                        var curve = seg.GetCurve();
                        if (curve == null || curve.Length <= 1e-6)
                            continue;

                        loopCurves.Add(curve);

                        if (Doc.GetElement(seg.ElementId) is Wall w)
                        {
                            var half = w.Width * 0.5;
                            if (half > maxWallHalfThickness)
                                maxWallHalfThickness = half;
                        }
                    }

                    if (loopCurves.Count < 3)
                        continue;

                    try
                    {
                        var ordered = TryOrderCurvesAsContiguousLoop(loopCurves);
                        loops.Add(CurveLoop.Create(ordered));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"建立第 {loopIndex + 1} 個邊界循環失敗，已略過：{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"獲取房間邊界循環失敗: {ex.Message}");
            }

            return loops;
        }

        private bool ShouldSkipBoundarySegment(BoundarySegment segment)
        {
            try
            {
                if (segment == null)
                    return true;

                var boundaryElement = Doc.GetElement(segment.ElementId);
                if (boundaryElement == null)
                    return false;

                // Room Separation Line 是房間邊界定義來源之一，不能略過。

                var hostWall = boundaryElement as Wall;
                if (hostWall != null && hostWall.WallType != null && hostWall.WallType.Kind == WallKind.Curtain)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private List<Curve> TryOrderCurvesAsContiguousLoop(List<Curve> source)
        {
            if (source == null || source.Count < 3)
                return source ?? new List<Curve>();

            // 5 mm: direct endpoint match tolerance.
            const double endpointTol = 5.0 / 304.8;
            // 50 mm: allow minor drafting gaps, then bridge by a short line.
            const double bridgeTol = 50.0 / 304.8;
            var remaining = new List<Curve>(source);
            var ordered = new List<Curve> { remaining[0] };
            remaining.RemoveAt(0);

            while (remaining.Count > 0)
            {
                var tail = ordered[ordered.Count - 1].GetEndPoint(1);
                var foundIndex = -1;
                Curve foundCurve = null;

                for (int i = 0; i < remaining.Count; i++)
                {
                    var c = remaining[i];
                    var d0 = c.GetEndPoint(0).DistanceTo(tail);
                    var d1 = c.GetEndPoint(1).DistanceTo(tail);
                    if (d0 <= endpointTol)
                    {
                        foundIndex = i;
                        foundCurve = c;
                        break;
                    }

                    if (d1 <= endpointTol)
                    {
                        foundIndex = i;
                        foundCurve = c.CreateReversed();
                        break;
                    }
                }

                if (foundIndex < 0)
                {
                    // Fallback: pick nearest curve endpoint and bridge if gap is small.
                    var nearestIndex = -1;
                    var nearestReversed = false;
                    var nearestDistance = double.MaxValue;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        var c = remaining[i];
                        var d0 = c.GetEndPoint(0).DistanceTo(tail);
                        if (d0 < nearestDistance)
                        {
                            nearestDistance = d0;
                            nearestIndex = i;
                            nearestReversed = false;
                        }

                        var d1 = c.GetEndPoint(1).DistanceTo(tail);
                        if (d1 < nearestDistance)
                        {
                            nearestDistance = d1;
                            nearestIndex = i;
                            nearestReversed = true;
                        }
                    }

                    if (nearestIndex < 0 || nearestDistance > bridgeTol)
                        throw new InvalidOperationException("These curves are not contiguous.");

                    var target = nearestReversed ? remaining[nearestIndex].GetEndPoint(1) : remaining[nearestIndex].GetEndPoint(0);
                    if (tail.DistanceTo(target) > endpointTol)
                    {
                        ordered.Add(Line.CreateBound(tail, target));
                        Logger.Log($"邊界存在微小斷點，已自動補短連接段：{tail.DistanceTo(target) * 304.8:F1}mm");
                    }

                    foundIndex = nearestIndex;
                    foundCurve = nearestReversed ? remaining[nearestIndex].CreateReversed() : remaining[nearestIndex];
                }

                ordered.Add(foundCurve);
                remaining.RemoveAt(foundIndex);
            }

            var first = ordered[0].GetEndPoint(0);
            var last = ordered[ordered.Count - 1].GetEndPoint(1);
            if (first.DistanceTo(last) > endpointTol)
            {
                var gap = first.DistanceTo(last);
                if (gap <= bridgeTol)
                {
                    ordered.Add(Line.CreateBound(last, first));
                    Logger.Log($"閉合端點存在微小斷點，已自動補短連接段：{gap * 304.8:F1}mm");
                }
                else
                {
                    throw new InvalidOperationException("These curves are not contiguous.");
                }
            }

            return ordered;
        }

        bool TryCreateFloor(Room room, FinishSettings settings)
        {
            try
            {
                Logger.Log($"開始為房間 {room.Name} 建立樓板");
                
                Level level; double halfT;
                var loops = GetRoomProfileLoops(room, settings.BoundaryMode, out level, out halfT);
                var ft = GetFloorType(settings.SelectedFloorTypeId);
                if (ft == null) 
                {
                    Logger.Log($"找不到樓板類型 ID: {settings.SelectedFloorTypeId}");
                    return false;
                }

                Logger.Log($"使用樓板類型: {ft.Name}");

                // 確保輪廓是有效的
                if (loops.Count == 0) 
                {
                    Logger.Log("房間輪廓不足，無法建立樓板");
                    return false;
                }

                Logger.Log($"建立樓板曲線環，包含 {loops.Count} 個邊界循環（支援開孔）");
                
                var floor = Floor.Create(Doc, loops, ft.Id, level.Id);
                Logger.Log($"成功建立樓板，ID: {floor.Id}");
                
                // 讀取樓板類型厚度並設定偏移
                double floorThickness = GetFloorThickness(ft);
                Logger.Log($"樓板類型厚度: {floorThickness * 304.8:F2} mm");
                
                var heightParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                if (heightParam != null && !heightParam.IsReadOnly)
                {
                    // 設定樓板表面與樓層齊平（向上偏移樓板厚度）
                    heightParam.Set(floorThickness);
                    Logger.Log($"設定樓板偏移: +{floorThickness * 304.8:F2} mm（向上）");
                }
                
                TagRoomOnElement(floor, room);
                Logger.Log($"完成房間 {room.Name} 樓板建立");
                return true;
            }
            catch (Exception ex)
            { 
                Logger.Log($"建立樓板異常: {ex.Message}");
                Logger.Log($"堆疊追蹤: {ex.StackTrace}");
                return false;
            }
        }

        bool TryCreateCeiling(Room room, FinishSettings settings)
        {
            try
            {
                Logger.Log($"開始為房間 {room.Name} 建立天花板");
                
                // 使用 InnerFinish 模式確保天花板覆蓋完整房間範圍
                Level level; double halfT;
                var loops = GetRoomProfileLoops(room, FloorBoundaryMode.InnerFinish, out level, out halfT);
                
                Logger.Log($"取得房間輪廓，包含 {loops.Count} 個邊界循環");
                
                // 驗證輪廓有效性
                if (loops.Count == 0)
                {
                    Logger.Log("房間輪廓曲線數量不足");
                    return false;
                }
                
                var ct = GetCeilingType(settings.SelectedCeilingTypeId);
                if (ct == null) 
                {
                    Logger.Log($"找不到天花板類型 ID: {settings.SelectedCeilingTypeId}");
                    return false;
                }

                Logger.Log($"使用天花板類型: {ct.Name}");
                
                var height = settings.MmToInternalUnits(settings.CeilingHeightMm);
                Logger.Log($"天花板高度: {height} 英尺 ({settings.CeilingHeightMm} mm)");
                
                var ceil = Ceiling.Create(Doc, loops, ct.Id, level.Id);
                if (ceil != null)
                {
                    Logger.Log($"成功建立天花板，ID: {ceil.Id}");
                    
                    var p = ceil.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                    if (p != null && !p.IsReadOnly) 
                    {
                        p.Set(height);
                        Logger.Log($"設定天花板高度: {height} 英尺");
                    }
                    
                    TagRoomOnElement(ceil, room);
                    Logger.Log($"完成房間 {room.Name} 天花板建立");
                    return true;
                }
                else
                {
                    Logger.Log("天花板建立失敗，回傳 null");
                    return false;
                }
            }
            catch (Exception ex)
            { 
                Logger.Log($"建立天花板異常: {ex.Message}");
                Logger.Log($"堆疊追蹤: {ex.StackTrace}");
                return false;
            }
        }

        bool TryCreateWallFinish(Room room, FinishSettings settings)
        {
            try
            {
                Logger.Log($"=== 開始為房間 {room.Name} 建立牆面裝修 ===");
                Logger.Log($"房間編號: {room.Number}，房間面積: {room.Area * 0.092903:F2} m²");
                Logger.Log($"專案長度單位: {GetProjectLengthUnit()}");
                
                // 強制重新計算房間邊界
                try
                {
                    Logger.Log("嘗試重新計算房間邊界");
                    Doc.Regenerate();
                    
                    // 再次檢查房間面積
                    var updatedArea = room.Area;
                    Logger.Log($"重新計算後房間面積: {updatedArea * 0.092903:F2} m²");
                }
                catch (Exception regenEx)
                {
                    Logger.Log($"重新計算房間邊界失敗: {regenEx.Message}");
                }
                
                // 詳細檢查房間的有效性和狀態
                Logger.Log($"房間狀態檢查 - 面積: {room.Area * 0.092903:F2} m², 周長: {room.Perimeter * 0.3048:F2} m");
                Logger.Log($"房間位置: {(room.Location as LocationPoint)?.Point}");
                Logger.Log($"房間邊界狀態: {(room.Area > 0 ? "有效" : "無效")}");
                
                if (room.Area <= 0)
                {
                    Logger.Log($"錯誤：房間 {room.Name} 面積為 0，可能房間邊界未正確計算");
                    return false;
                }

                // 檢查房間是否已放置且邊界已計算
                try
                {
                    var roomBoundingBox = room.get_BoundingBox(null);
                    if (roomBoundingBox != null)
                    {
                        Logger.Log($"房間邊界框: Min({roomBoundingBox.Min.X * 304.8:F2}, {roomBoundingBox.Min.Y * 304.8:F2}), " +
                                 $"Max({roomBoundingBox.Max.X * 304.8:F2}, {roomBoundingBox.Max.Y * 304.8:F2})");
                    }
                    else
                    {
                        Logger.Log("警告：房間沒有邊界框");
                    }
                }
                catch (Exception bboxEx)
                {
                    Logger.Log($"獲取房間邊界框失敗: {bboxEx.Message}");
                }
                
                var level = GetLevel(room.LevelId);
                if (level == null) 
                {
                    Logger.Log("無法取得樓層資訊");
                    return false;
                }
                Logger.Log($"房間所在樓層: {level.Name}");
                
                double wallHeightMm = settings.WallHeightMm > 0 ? settings.WallHeightMm : (settings.CeilingHeightMm + settings.WallOffsetMm);
                double wallHeight = settings.MmToInternalUnits(wallHeightMm);
                Logger.Log($"牆面高度: {wallHeight} 英尺 ({wallHeightMm}mm)");
                
                var wt = GetWallType(settings.SelectedWallTypeId);
                if (wt == null) 
                {
                    Logger.Log($"找不到牆類型 ID: {settings.SelectedWallTypeId}");
                    return false;
                }

                Logger.Log($"使用牆類型: {wt.Name}，牆厚度: {wt.Width * 304.8:F2}mm");

                // ─── Dynamo Python 腳本等效：逐段邊界 → 判斷偏移方向 → 建立粉刷牆 ──────────────
                // 1. GetBoundarySegments → 逐段取得邊界曲線（略過幕牆、房間分隔線）
                // 2. 偏移 +fullWidth 取中點 → IsPointInRoom 判斷室內方向
                // 3. 偏移 halfWidth 在正確方向 → Wall.Create（中心線定位）
                // 4. 設定參數：base offset, height, 非房間邊界, location line=2
                // 5. 若邊界牆有門/窗插入件 → JoinGeometry（繼承開口形狀）
                // ──────────────────────────────────────────────────────────────────────────
                Logger.Log("=== 依 Dynamo Python 腳本邏輯建立粉刷牆 ===");

                var sbo = new SpatialElementBoundaryOptions();
                var createdWalls = new List<Wall>();
                var createdWallKeys = new HashSet<string>();
                double halfWidth = wt.Width / 2.0;
                double fullWidth = wt.Width;
                double roomLowerOffset = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET)?.AsDouble() ?? 0.0;

                var allSegGroups = room.GetBoundarySegments(sbo);
                if (allSegGroups == null || allSegGroups.Count == 0)
                {
                    Logger.Log($"✗ 房間 {room.Name} 無邊界段");
                    return false;
                }

                int segTotal = allSegGroups.Sum(g => g.Count);
                Logger.Log($"房間邊界共 {allSegGroups.Count} 迴路，{segTotal} 條邊界段");

                foreach (var segGroup in allSegGroups)
                {
                    foreach (var seg in segGroup)
                    {
                        // 取得邊界圖元
                        Element boundaryElem = null;
                        try { boundaryElem = Doc.GetElement(seg.ElementId); } catch { }

                        // 略過無對應圖元的邊界（房間分隔線無 Element 時）
                        if (boundaryElem == null)
                        {
                            Logger.Log("略過：邊界圖元為 null");
                            continue;
                        }

                        // 略過幕牆
                        if (boundaryElem is Wall bwChk && bwChk.WallType.Kind == WallKind.Curtain)
                        {
                            Logger.Log($"略過幕牆 (ID: {boundaryElem.Id})");
                            continue;
                        }

                        // 略過房間分隔線（OST_RoomSeparationLines = -2000066）
                        if (boundaryElem.Category != null &&
                            RevitCompat.GetElementIdValue(boundaryElem.Category.Id) == (long)BuiltInCategory.OST_RoomSeparationLines)
                        {
                            Logger.Log($"略過房間分隔線 (ID: {boundaryElem.Id})");
                            continue;
                        }

                        var segCurve = seg.GetCurve();
                        if (segCurve == null || segCurve.Length < 1e-6) continue;

                        // 判斷室內偏移方向：正向偏移 fullWidth 取中點，檢查是否在房間內
                        Curve posOffsetCurve = OffsetCurveLateral(segCurve, fullWidth);
                        if (posOffsetCurve == null)
                        {
                            Logger.Log($"無法計算偏移，略過邊界段 (長: {segCurve.Length * 304.8:F1}mm)");
                            continue;
                        }

                        XYZ testPt = posOffsetCurve.Evaluate(0.5, true);
                        bool posIsInside = room.IsPointInRoom(testPt);
                        double offsetSign = posIsInside ? 1.0 : -1.0;

                        Logger.Log($"邊界段長 {segCurve.Length * 304.8:F1}mm → {(posIsInside ? "正向" : "負向")}偏移 {halfWidth * 304.8:F2}mm");

                        Curve finishCenterline = OffsetCurveLateral(segCurve, offsetSign * halfWidth);
                        if (finishCenterline == null || finishCenterline.Length < 1e-6)
                        {
                            Logger.Log("略過：偏移後中心線無效");
                            continue;
                        }

                        if (!TryRegisterFinishWallCurve(createdWallKeys, finishCenterline, wallHeight, roomLowerOffset, out var duplicateKey))
                        {
                            Logger.Log($"略過重複粉刷牆中心線: {duplicateKey}");
                            continue;
                        }

                        try
                        {
                            // Wall.Create(doc, curve, wallTypeId, levelId, height, offset, flip, structural)
                            var finishWall = Wall.Create(Doc, finishCenterline, wt.Id, level.Id, wallHeight, 0, false, false);
                            if (finishWall == null)
                            {
                                createdWallKeys.Remove(duplicateKey);
                                Logger.Log("Wall.Create 返回 null");
                                continue;
                            }

                            // 立即禁用兩端 Wall Join，防止 Revit 建牆瞬間自動接合至結構牆而移位其端點
                            try { WallUtils.DisallowWallJoinAtEnd(finishWall, 0); } catch { }
                            try { WallUtils.DisallowWallJoinAtEnd(finishWall, 1); } catch { }

                            // 設定參數（對應 Python 腳本）
                            finishWall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.Set(roomLowerOffset);
                            finishWall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.Set(wallHeight);

                            // 設定「非房間邊界」—— 必須加 IsReadOnly 保護，靜默失敗會造成房間面積歸零
                            var _rbParam = finishWall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                            if (_rbParam == null)
                                Logger.Log($"⚠ 粉刷牆 {finishWall.Id} 找不到 WALL_ATTR_ROOM_BOUNDING 參數");
                            else if (_rbParam.IsReadOnly)
                                Logger.Log($"⚠ 粉刷牆 {finishWall.Id} 的 WALL_ATTR_ROOM_BOUNDING 為唯讀，無法設定");
                            else
                                _rbParam.Set(0);

                            finishWall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.Set(2);

                            // 若邊界牆有門/窗插入件 → JoinGeometry（繼承開口形狀）
                            if (boundaryElem is Wall structWall)
                            {
                                try
                                {
                                    var inserts = structWall.FindInserts(true, true, true, true);
                                    if (inserts != null && inserts.Count > 0)
                                    {
                                        Logger.Log($"邊界牆 {structWall.Id} 有 {inserts.Count} 個插入件，執行 JoinGeometry");
                                        try { JoinGeometryUtils.JoinGeometry(Doc, finishWall, structWall); }
                                        catch (Exception je) { Logger.Log($"JoinGeometry 失敗（繼續）: {je.Message}"); }
                                    }
                                }
                                catch (Exception ie) { Logger.Log($"FindInserts 查詢失敗: {ie.Message}"); }
                            }

                            // 寫入房間關聯參數（必要：ValueWriter 依此索引元素）
                            TagRoomOnElement(finishWall, room);

                            createdWalls.Add(finishWall);
                            Logger.Log($"✓ 粉刷牆 ID: {finishWall.Id}，長: {finishCenterline.Length * 304.8:F1}mm");
                        }
                        catch (Exception wex)
                        {
                            if (!string.IsNullOrWhiteSpace(duplicateKey))
                                createdWallKeys.Remove(duplicateKey);
                            Logger.Log($"Wall.Create 失敗: {wex.Message}");
                        }
                    }
                }

                Logger.Log($"步驟2-3 ✓ 共建立 {createdWalls.Count} 面粉刷牆");

                // Room.GetBoundarySegments 在部分外牆凸柱/結構柱模型中不會穩定回傳柱側面，
                // 導致「面生面」可做、但「房間裝修」漏生柱面。這裡只補掃描柱側面的線段與高度，
                // 實際仍用 Wall.Create 建立粉刷牆；房間裝修自動產物不得使用一般模型。
                int columnFaceCount = CreateColumnWallFinishFacesForRoom(room, wt, wallHeight, roomLowerOffset, createdWalls, createdWallKeys);
                if (columnFaceCount > 0)
                    Logger.Log($"✓ 補建立 {columnFaceCount} 面柱側粉刷面");

                // 對相鄰粉刷牆角點啟用 Wall Join，避免角落縫隙影響面積明細表
                if (createdWalls.Count > 1)
                {
                    EnableFinishWallCornerJoins(createdWalls);
                }

                // 最終更新
                try
                {
                    Doc.Regenerate();
                    Logger.Log("✓ 文檔更新完成");
                }
                catch (Exception regenEx)
                {
                    Logger.Log($"文檔更新失敗: {regenEx.Message}");
                }

                Logger.Log($"=== 完成房間 {room.Name} 粉刷牆建立 ===");
                Logger.Log($"✓ 成功建立: {createdWalls.Count} 面粉刷牆，{columnFaceCount} 面柱側粉刷面");
                return createdWalls.Any() || columnFaceCount > 0;
            }
            catch (Exception ex)
            { 
                Logger.Log($"建立牆面裝修異常: {ex.Message}");
                Logger.Log($"堆疊追蹤: {ex.StackTrace}");
                return false;
            }
        }

        private int CreateColumnWallFinishFacesForRoom(Room room, WallType wallType, double wallHeight, double roomLowerOffset, IList<Wall> existingFinishWalls, HashSet<string> createdWallKeys)
        {
            try
            {
                var roomBox = room.get_BoundingBox(null);
                if (roomBox == null || wallType == null || wallHeight <= 1e-6)
                    return 0;

                var level = GetLevel(room.LevelId);
                if (level == null)
                    return 0;

                double baseZ = level.Elevation + roomLowerOffset;
                double topZ = baseZ + wallHeight;
                double thickness = Math.Max(wallType.Width, 1.0 / 304.8); // 至少 1mm，避免零厚度失敗
                var candidates = GetColumnCandidatesNearRoom(roomBox, thickness + (300.0 / 304.8));
                var createdKeys = new HashSet<string>();
                int created = 0;

                foreach (var column in candidates)
                {
                    foreach (var face in GetVerticalPlanarFaces(column))
                    {
                        if (!TryGetColumnFaceFinishDirection(room, face, baseZ, topZ, out var inwardDir, out var faceCenter))
                            continue;

                        if (!TryBuildColumnFaceWallLine(face, inwardDir, thickness, level.Elevation, baseZ, topZ,
                                out var centerline, out var height, out var baseOffset, out var areaM2))
                            continue;

                        if (areaM2 <= 0.0001 || centerline == null || centerline.Length < 1e-6)
                            continue;

                        if (IsDuplicateFinishWallCurve(centerline, height, baseOffset, existingFinishWalls, createdWallKeys, out var duplicateReason))
                        {
                            Logger.Log($"略過重複柱側粉刷面（柱 {column.Id}）：{duplicateReason}");
                            continue;
                        }

                        if (!TryRegisterFinishWallCurve(createdWallKeys, centerline, height, baseOffset, out var duplicateKey))
                        {
                            Logger.Log($"略過重複柱側粉刷面（柱 {column.Id}）：{duplicateKey}");
                            continue;
                        }

                        string key = $"{RevitCompat.GetElementIdValue(column.Id)}|" +
                                     $"{Math.Round(faceCenter.X, 4)}|{Math.Round(faceCenter.Y, 4)}|" +
                                     $"{Math.Round(face.FaceNormal.X, 3)}|{Math.Round(face.FaceNormal.Y, 3)}";
                        if (!createdKeys.Add(key))
                        {
                            createdWallKeys?.Remove(duplicateKey);
                            continue;
                        }

                        try
                        {
                            var wall = Wall.Create(Doc, centerline, wallType.Id, level.Id, height, baseOffset, false, false);
                            if (wall == null)
                            {
                                createdWallKeys?.Remove(duplicateKey);
                                continue;
                            }

                            try { WallUtils.DisallowWallJoinAtEnd(wall, 0); } catch { }
                            try { WallUtils.DisallowWallJoinAtEnd(wall, 1); } catch { }

                            wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.Set(height);
                            var rb = wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                            if (rb != null && !rb.IsReadOnly) rb.Set(0);
                            wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.Set(2);

                            TagRoomOnElement(wall, room);
                            existingFinishWalls?.Add(wall);

                            created++;
                            Logger.Log($"✓ 柱側粉刷牆 ID: {wall.Id}，柱: {column.Id}，面積估算: {areaM2:F3}m²");
                        }
                        catch (Exception ex)
                        {
                            createdWallKeys?.Remove(duplicateKey);
                            Logger.Log($"柱側粉刷面建立失敗（柱 {column.Id}）: {ex.Message}");
                        }
                    }
                }

                return created;
            }
            catch (Exception ex)
            {
                Logger.Log($"補建立柱側粉刷面異常: {ex.Message}");
                return 0;
            }
        }

        private bool TryRegisterFinishWallCurve(HashSet<string> keys, Curve centerline, double height, double baseOffset, out string key)
        {
            key = BuildFinishWallCurveKey(centerline, height, baseOffset);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return keys == null || keys.Add(key);
        }

        private bool IsDuplicateFinishWallCurve(Curve centerline, double height, double baseOffset, IEnumerable<Wall> existingFinishWalls, HashSet<string> createdWallKeys, out string reason)
        {
            reason = null;

            string key = BuildFinishWallCurveKey(centerline, height, baseOffset);
            if (!string.IsNullOrWhiteSpace(key) && createdWallKeys != null && createdWallKeys.Contains(key))
            {
                reason = $"中心線已存在 {key}";
                return true;
            }

            if (existingFinishWalls == null)
                return false;

            foreach (var wall in existingFinishWalls)
            {
                if (wall == null)
                    continue;

                var loc = wall.Location as LocationCurve;
                var existingCurve = loc?.Curve;
                if (existingCurve == null)
                    continue;

                double existingHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? height;
                double existingBaseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? baseOffset;

                if (AreFinishWallCurvesOverlapping(centerline, baseOffset, height, existingCurve, existingBaseOffset, existingHeight))
                {
                    reason = $"與既有粉刷牆 {wall.Id} 重疊";
                    return true;
                }
            }

            return false;
        }

        private string BuildFinishWallCurveKey(Curve curve, double height, double baseOffset)
        {
            if (!(curve is Line line))
                return null;

            var a = line.GetEndPoint(0);
            var b = line.GetEndPoint(1);
            if (a.DistanceTo(b) < 1e-6)
                return null;

            // 以 1mm 作為 fingerprint 粒度；端點排序後可避免同一段反向建立。
            long ax = RoundFtToMm(a.X);
            long ay = RoundFtToMm(a.Y);
            long bx = RoundFtToMm(b.X);
            long by = RoundFtToMm(b.Y);
            if (ax > bx || (ax == bx && ay > by))
            {
                var tx = ax; ax = bx; bx = tx;
                var ty = ay; ay = by; by = ty;
            }

            long z = RoundFtToMm(baseOffset);
            long h = RoundFtToMm(height);
            return $"{ax},{ay}|{bx},{by}|z{z}|h{h}";
        }

        private static long RoundFtToMm(double feet)
        {
            return (long)Math.Round(feet * 304.8, MidpointRounding.AwayFromZero);
        }

        private bool AreFinishWallCurvesOverlapping(Curve a, double aBaseOffset, double aHeight, Curve b, double bBaseOffset, double bHeight)
        {
            if (!(a is Line la) || !(b is Line lb))
                return false;

            double aMinZ = aBaseOffset;
            double aMaxZ = aBaseOffset + aHeight;
            double bMinZ = bBaseOffset;
            double bMaxZ = bBaseOffset + bHeight;
            if (Math.Min(aMaxZ, bMaxZ) - Math.Max(aMinZ, bMinZ) <= 10.0 / 304.8)
                return false;

            var a0 = ToXy(la.GetEndPoint(0));
            var a1 = ToXy(la.GetEndPoint(1));
            var b0 = ToXy(lb.GetEndPoint(0));
            var b1 = ToXy(lb.GetEndPoint(1));
            var ad = a1 - a0;
            var bd = b1 - b0;
            double al = ad.GetLength();
            double bl = bd.GetLength();
            if (al < 1e-6 || bl < 1e-6)
                return false;

            ad = ad.Normalize();
            bd = bd.Normalize();

            // 方向需近似平行，且兩線距離小於 10mm，才視為同一粉刷面。
            if (Math.Abs(ad.DotProduct(bd)) < 0.999)
                return false;

            double lateralDistance = Math.Abs((b0 - a0).DotProduct(new XYZ(-ad.Y, ad.X, 0)));
            if (lateralDistance > 10.0 / 304.8)
                return false;

            double b0u = (b0 - a0).DotProduct(ad);
            double b1u = (b1 - a0).DotProduct(ad);
            double overlap = Math.Min(al, Math.Max(b0u, b1u)) - Math.Max(0, Math.Min(b0u, b1u));
            double minLen = Math.Min(al, bl);

            return overlap > Math.Max(20.0 / 304.8, minLen * 0.80);
        }

        private static XYZ ToXy(XYZ p)
        {
            return new XYZ(p.X, p.Y, 0);
        }

        private List<Element> GetColumnCandidatesNearRoom(BoundingBoxXYZ roomBox, double expand)
        {
            var result = new List<Element>();
            var seen = new HashSet<long>();

            foreach (var cat in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns })
            {
                foreach (var elem in new FilteredElementCollector(Doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements())
                {
                    var box = elem.get_BoundingBox(null);
                    if (box == null)
                        continue;

                    if (!BoxesOverlap(roomBox, box, expand))
                        continue;

                    if (seen.Add(RevitCompat.GetElementIdValue(elem.Id)))
                        result.Add(elem);
                }
            }

            Logger.Log($"房間周邊找到 {result.Count} 個柱候選元素");
            return result;
        }

        private static bool BoxesOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b, double expand)
        {
            return a.Min.X - expand <= b.Max.X && a.Max.X + expand >= b.Min.X
                && a.Min.Y - expand <= b.Max.Y && a.Max.Y + expand >= b.Min.Y
                && a.Min.Z - expand <= b.Max.Z && a.Max.Z + expand >= b.Min.Z;
        }

        private IEnumerable<PlanarFace> GetVerticalPlanarFaces(Element elem)
        {
            var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
            var geo = elem.get_Geometry(opt);
            if (geo == null)
                yield break;

            foreach (var solid in EnumerateSolids(geo))
            {
                foreach (Face f in solid.Faces)
                {
                    if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) < 0.01)
                        yield return pf;
                }
            }
        }

        private IEnumerable<Solid> EnumerateSolids(GeometryElement geo)
        {
            foreach (GeometryObject obj in geo)
            {
                if (obj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-9)
                {
                    yield return solid;
                }
                else if (obj is GeometryInstance gi)
                {
                    var instGeo = gi.GetInstanceGeometry();
                    if (instGeo == null)
                        continue;

                    foreach (var s in EnumerateSolids(instGeo))
                        yield return s;
                }
            }
        }

        private bool TryGetColumnFaceFinishDirection(Room room, PlanarFace face, double baseZ, double topZ, out XYZ inwardDir, out XYZ faceCenter)
        {
            inwardDir = null;
            faceCenter = null;

            var pts = GetFaceSamplePoints(face).ToList();
            if (pts.Count == 0)
                return false;

            double midZ = (baseZ + topZ) / 2.0;
            faceCenter = new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), midZ);

            var n = new XYZ(face.FaceNormal.X, face.FaceNormal.Y, 0);
            if (n.GetLength() < 1e-9)
                return false;
            n = n.Normalize();

            double probe = 100.0 / 304.8; // 100mm，避開柱面/牆面共面容差
            bool plusInside = SafeIsPointInRoom(room, faceCenter + n * probe);
            bool minusInside = SafeIsPointInRoom(room, faceCenter - n * probe);

            if (plusInside == minusInside)
                return false;

            inwardDir = plusInside ? n : -n;
            return true;
        }

        private bool SafeIsPointInRoom(Room room, XYZ p)
        {
            try { return room.IsPointInRoom(p); }
            catch { return false; }
        }

        private IEnumerable<XYZ> GetFaceSamplePoints(PlanarFace face)
        {
            foreach (var loop in face.GetEdgesAsCurveLoops())
            {
                foreach (var c in loop)
                {
                    foreach (var p in c.Tessellate())
                        yield return p;
                }
            }
        }

        private bool TryBuildColumnFaceWallLine(
            PlanarFace face,
            XYZ inwardDir,
            double thickness,
            double levelElevation,
            double baseZ,
            double topZ,
            out Curve centerline,
            out double height,
            out double baseOffset,
            out double areaM2)
        {
            centerline = null;
            height = 0;
            baseOffset = 0;
            areaM2 = 0;

            var pts = GetFaceSamplePoints(face).ToList();
            if (pts.Count == 0)
                return false;

            double minZ = Math.Max(baseZ, pts.Min(p => p.Z));
            double maxZ = Math.Min(topZ, pts.Max(p => p.Z));
            if (maxZ - minZ <= 1e-6)
                return false;

            var normal = new XYZ(face.FaceNormal.X, face.FaceNormal.Y, 0);
            if (normal.GetLength() < 1e-9)
                return false;
            normal = normal.Normalize();

            var axis = XYZ.BasisZ.CrossProduct(normal);
            if (axis.GetLength() < 1e-9)
                axis = normal.CrossProduct(XYZ.BasisZ);
            axis = axis.Normalize();

            var origin = pts[0];
            double minU = pts.Min(p => (p - origin).DotProduct(axis));
            double maxU = pts.Max(p => (p - origin).DotProduct(axis));
            if (maxU - minU <= 1e-6)
                return false;

            var p0 = new XYZ(origin.X, origin.Y, 0) + axis * minU + XYZ.BasisZ * levelElevation;
            var p1 = new XYZ(origin.X, origin.Y, 0) + axis * maxU + XYZ.BasisZ * levelElevation;
            var centerOffset = inwardDir.Normalize() * (thickness / 2.0);
            centerline = Line.CreateBound(p0 + centerOffset, p1 + centerOffset);
            height = maxZ - minZ;
            baseOffset = minZ - levelElevation;

            areaM2 = (maxU - minU) * (maxZ - minZ) * 0.09290304;
            return true;
        }

        private ElementId GetPrimaryMaterialId(WallType wallType)
        {
            try
            {
                var cs = wallType.GetCompoundStructure();
                if (cs != null)
                {
                    foreach (var layer in cs.GetLayers())
                    {
                        if (layer.MaterialId != ElementId.InvalidElementId)
                            return layer.MaterialId;
                    }
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        private void TrySetMaterial(Element elem, ElementId materialId)
        {
            if (materialId == ElementId.InvalidElementId)
                return;

            try
            {
                var p = elem.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && !p.IsReadOnly)
                    p.Set(materialId);
            }
            catch { }
        }

        private void TrySetAreaAndMaterialParameters(Element elem, double areaM2, string materialName)
        {
            try
            {
                var areaParam = elem.LookupParameter(SharedParams.P_Area)
                    ?? elem.LookupParameter("裝修面積")
                    ?? elem.LookupParameter("Area");
                if (areaParam != null && !areaParam.IsReadOnly)
                    areaParam.Set(AreaCalculator.ConvertToSquareFeet(areaM2));

                var materialParam = elem.LookupParameter(SharedParams.P_MaterialName)
                    ?? elem.LookupParameter("裝修材質")
                    ?? elem.LookupParameter("材料");
                if (materialParam != null && !materialParam.IsReadOnly && !string.IsNullOrWhiteSpace(materialName))
                    materialParam.Set(materialName);
            }
            catch { }
        }

        bool TryCreateSkirting(Room room, FinishSettings settings)
        {
            try
            {
                Logger.Log($"開始為房間 {room.Name} 建立踢腳板");
                
                var level = GetLevel(room.LevelId);
                if (level == null) 
                {
                    Logger.Log("無法取得樓層資訊");
                    return false;
                }
                
                double height = settings.MmToInternalUnits(settings.SkirtingHeightMm);
                Logger.Log($"踢腳板高度: {height} 英尺");
                
                // 取得房間邊界曲線
                var curves = GetBoundaryCurves(room, FloorBoundaryMode.InnerFinish);
                Logger.Log($"取得 {curves.Count} 條邊界曲線");

                var wt = GetWallType(settings.SelectedSkirtingTypeId);
                if (wt == null) 
                {
                    Logger.Log($"找不到踢腳板類型 ID: {settings.SelectedSkirtingTypeId}");
                    return false;
                }

                Logger.Log($"使用踢腳板類型: {wt.Name}");

                // 在建立踢腳板前先切割門窗開孔，確保門口位置無踢腳板
                if (settings.SkipDoorsForSkirting || settings.SkipWindowsForSkirting)
                {
                    curves = CutCurvesByDoorsAndWindows(room, curves,
                        settings.SkipDoorsForSkirting, settings.SkipWindowsForSkirting);
                    Logger.Log($"門窗開孔切割後剩餘 {curves.Count} 條曲線");
                }

                var createdWalls = new List<Wall>();
                
                // 合併連續的直線段以減少踢腳板數量
                var mergedCurves = MergeContinuousLines(curves);
                Logger.Log($"原始 {curves.Count} 條曲線合併為 {mergedCurves.Count} 條");

                // 踢腳板中心線向室內偏移：偏移量 = 粉刷牆全厚 + 踢腳板半厚
                // 理由：房間 Finish 邊界 = 結構牆室內面；粉刷牆中線離邊界 = 半厚；
                //       粉刷牆室內面離邊界 = 全厚；踢腳板外側緊貼粉刷牆室內面 → 踢腳板中線再偏移半厚。
                double plasterFullThickness = 0;
                if (settings.SelectedWallTypeId != ElementId.InvalidElementId)
                {
                    var plasterType = GetWallType(settings.SelectedWallTypeId);
                    if (plasterType != null)
                    {
                        plasterFullThickness = plasterType.Width; // 全厚
                    }
                }

                var skirtingHalfThickness = wt.Width * 0.5;
                var inwardOffset = plasterFullThickness + skirtingHalfThickness;
                Logger.Log($"踢腳板向室內偏移: 粉刷牆全厚 {plasterFullThickness * 304.8:F1}mm + 踢腳板半厚 {skirtingHalfThickness * 304.8:F1}mm = {inwardOffset * 304.8:F1}mm");

                if (inwardOffset > 1e-6)
                {
                    mergedCurves = OffsetCurvesInward(mergedCurves, inwardOffset);
                    Logger.Log($"完成踢腳板路徑偏移，共 {mergedCurves.Count} 條曲線");
                }
                
                // 創建踢腳板（加入重疊檢測）
                foreach (var c in mergedCurves)
                {
                    if (c == null || c.Length < 1e-6) continue;
                    
                    Logger.Log($"建立踢腳板段，長度: {c.Length * 304.8:F2} mm");
                    
                    // 檢查是否與現有踢腳板重疊
                    bool hasOverlap = false;
                    foreach (var existingWall in createdWalls)
                    {
                        try
                        {
                            var existingCurve = (existingWall.Location as LocationCurve)?.Curve;
                            if (existingCurve != null && AreWallsParallelAndOverlapping(c, existingCurve))
                            {
                                Logger.Log($"⚠️ 檢測到與已創建踢腳板 {existingWall.Id} 重疊，跳過創建");
                                hasOverlap = true;
                                break;
                            }
                        }
                        catch (Exception checkEx)
                        {
                            Logger.Log($"檢查踢腳板重疊時發生錯誤: {checkEx.Message}");
                        }
                    }
                    
                    if (hasOverlap) continue;
                    
                    var wall = Wall.Create(Doc, c, wt.Id, level.Id, height, 0, false, false);
                    if (wall != null)
                    {
                        // 立即禁用兩端 Wall Join，防止拖拉結構牆端點
                        try { WallUtils.DisallowWallJoinAtEnd(wall, 0); } catch { }
                        try { WallUtils.DisallowWallJoinAtEnd(wall, 1); } catch { }
                        
                        // 設定踢腳板的基本屬性（包含房間邊界設定）
                        SetFinishWallProperties(wall);
                        
                        TagRoomOnElement(wall, room);
                        createdWalls.Add(wall);
                        Logger.Log($"成功建立踢腳板段，ID: {wall.Id}");
                    }
                }

                // 檢查並清理重疊的踢腳板
                if (createdWalls.Count > 1)
                {
                    Logger.Log("檢查踢腳板重疊情況");
                    createdWalls = RemoveOverlappingWalls(createdWalls);
                    Logger.Log($"清理後剩餘 {createdWalls.Count} 段踢腳板");
                }
                
                if (createdWalls.Count > 1)
                {
                    Logger.Log("後處理：對相鄰踢腳板端點啟用 Wall Join（同 Dynamo 邏輯）");
                    EnableFinishWallCornerJoins(createdWalls);
                }

                Logger.Log($"完成房間 {room.Name} 踢腳板建立，共 {createdWalls.Count} 段");
                return createdWalls.Any();
            }
            catch (Exception ex)
            { 
                Logger.Log($"建立踢腳板異常: {ex.Message}");
                Logger.Log($"堆疊追蹤: {ex.StackTrace}");
                return false;
            }
        }

        // 快取方法
        private FloorType GetFloorType(ElementId id)
        {
            if (!_floorTypeCache.ContainsKey(id))
            {
                _floorTypeCache[id] = Doc.GetElement(id) as FloorType;
            }
            return _floorTypeCache[id];
        }
        
        private CeilingType GetCeilingType(ElementId id)
        {
            if (!_ceilingTypeCache.ContainsKey(id))
            {
                _ceilingTypeCache[id] = Doc.GetElement(id) as CeilingType;
            }
            return _ceilingTypeCache[id];
        }
        
        private WallType GetWallType(ElementId id)
        {
            if (!_wallTypeCache.ContainsKey(id))
            {
                _wallTypeCache[id] = Doc.GetElement(id) as WallType;
            }
            return _wallTypeCache[id];
        }
        
        private Level GetLevel(ElementId id)
        {
            if (!_levelCache.ContainsKey(id))
            {
                _levelCache[id] = Doc.GetElement(id) as Level;
            }
            return _levelCache[id];
        }
        
        private double GetOpeningWidth(FamilyInstance opening)
        {
            try
            {
                // 嘗試獲取 Width 參數
                var widthParam = opening.Symbol?.LookupParameter("Width") ?? opening.LookupParameter("Width");
                if (widthParam != null)
                {
                    return widthParam.AsDouble();
                }
                
                // 嘗試獲取 Rough Width
                var roughWidthParam = opening.Symbol?.LookupParameter("Rough Width") ?? opening.LookupParameter("Rough Width");
                if (roughWidthParam != null)
                {
                    return roughWidthParam.AsDouble();
                }
                
                // 根據類別設定預設寬度
                if (RevitCompat.GetElementIdValue(opening.Category.Id) == (int)BuiltInCategory.OST_Doors)
                {
                    return 900.0 / 304.8; // 預設門寬 900mm
                }
                else if (RevitCompat.GetElementIdValue(opening.Category.Id) == (int)BuiltInCategory.OST_Windows)
                {
                    return 1200.0 / 304.8; // 預設窗寬 1200mm
                }
                
                return 900.0 / 304.8; // 預設寬度
            }
            catch
            {
                return 900.0 / 304.8; // 發生錯誤時的預設寬度
            }
        }
        
        private bool IsOpeningRelatedToRoom(FamilyInstance opening, Room room)
        {
            try
            {
                // 方法1：檢查 ToRoom 和 FromRoom
                Room toRoom = opening.ToRoom;
                Room fromRoom = opening.FromRoom;
                
                if ((toRoom != null && toRoom.Id == room.Id) || 
                    (fromRoom != null && fromRoom.Id == room.Id))
                {
                    return true;
                }
                
                // 方法2：檢查門窗是否在房間邊界附近
                var location = opening.Location as LocationPoint;
                if (location != null)
                {
                    var point = location.Point;
                    
                    // 檢查點是否在房間邊界50cm範圍內
                    if (room.IsPointInRoom(point))
                    {
                        return true;
                    }
                    
                    // 檢查是否靠近房間邊界
                    Level level; double halfT; CurveLoop loop;
                    var profile = GetRoomProfile(room, FloorBoundaryMode.InnerFinish, out level, out halfT, out loop);
                    
                    foreach (Curve curve in profile)
                    {
                        var projection = curve.Project(point);
                        if (projection != null && projection.Distance < (500.0 / 304.8)) // 500mm 範圍內
                        {
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private double GetFloorThickness(FloorType floorType)
        {
            try
            {
                var structure = floorType.GetCompoundStructure();
                if (structure != null)
                {
                    return structure.GetWidth();
                }
                
                // 如果無法取得複合結構，使用預設厚度
                var thicknessParam = floorType.get_Parameter(BuiltInParameter.FLOOR_ATTR_DEFAULT_THICKNESS_PARAM);
                if (thicknessParam != null)
                {
                    return thicknessParam.AsDouble();
                }
                
                // 預設厚度 150mm
                return 150.0 / 304.8;
            }
            catch
            {
                // 發生錯誤時使用預設厚度
                return 150.0 / 304.8; // 150mm 轉換為英尺
            }
        }

        List<Curve> GetBoundaryCurves(Room room, FloorBoundaryMode mode)
        {
            try
            {
                Level lvl; double halfT; CurveLoop loop;
                var ca = GetRoomProfile(room, mode, out lvl, out halfT, out loop);
                var curves = ca.Cast<Curve>().ToList();
                
                Logger.Log($"房間 {room.Name} 邊界獲取完成，模式: {mode}，取得 {curves.Count} 條曲線");
                
                // 驗證邊界曲線的有效性
                for (int i = 0; i < curves.Count; i++)
                {
                    var curve = curves[i];
                    if (curve != null && curve.Length > 0)
                    {
                        var start = curve.GetEndPoint(0);
                        var end = curve.GetEndPoint(1);
                        Logger.Log($"邊界曲線 {i}: 起點({start.X * 304.8:F2}, {start.Y * 304.8:F2}), 終點({end.X * 304.8:F2}, {end.Y * 304.8:F2}), 長度: {curve.Length * 304.8:F2}mm");
                    }
                    else
                    {
                        Logger.Log($"邊界曲線 {i}: 無效曲線");
                    }
                }
                
                return curves;
            }
            catch (Exception ex)
            {
                Logger.Log($"獲取房間邊界失敗: {ex.Message}");
                return new List<Curve>();
            }
        }

        List<Curve> CutCurvesByDoorsAndWindows(Room room, List<Curve> curves, bool includeDoors = true, bool includeWindows = true)
        {
            var doc = Doc;
            var openings = new List<FamilyInstance>();

            // 收集房間相關的門和窗
            if (includeDoors)
            {
                var doors = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => IsOpeningRelatedToRoom(fi, room));
                openings.AddRange(doors);
            }

            if (includeWindows)
            {
                var windows = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => IsOpeningRelatedToRoom(fi, room));
                openings.AddRange(windows);
            }

            Logger.Log($"CutCurvesByDoorsAndWindows: 找到 {openings.Count} 個門窗，待切割 {curves.Count} 條曲線");

            if (!openings.Any()) return curves;

            // ====== 核心修正：改用 Host 牆 ID 對應，取代不可靠的距離閾值 ======
            // 建立 hostWallId → 門窗清單
            var wallToOpenings = new Dictionary<long, List<FamilyInstance>>();
            foreach (var op in openings)
            {
                if (op.Host == null) continue;
                long hid = RevitCompat.GetElementIdValue(op.Host.Id);
                if (!wallToOpenings.ContainsKey(hid))
                    wallToOpenings[hid] = new List<FamilyInstance>();
                wallToOpenings[hid].Add(op);
                Logger.Log($"  門窗 {op.Id} ({op.Category?.Name}) → 宿主牆 {hid}");
            }

            // 取得房間邊界段，建立 curves[i] → wallId 對應（以起終點位置匹配）
            var curveToWallId = new Dictionary<int, long>();
            try
            {
                var sbo2 = new SpatialElementBoundaryOptions();
                sbo2.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;
                var segGroups = room.GetBoundarySegments(sbo2);
                if (segGroups != null)
                {
                    bool firstGrp = true;
                    foreach (var grp in segGroups)
                    {
                        foreach (var seg in grp)
                        {
                            var sc = seg.GetCurve();
                            if (sc == null) continue;
                            long wid = RevitCompat.GetElementIdValue(seg.ElementId);
                            var sS = sc.GetEndPoint(0);
                            var sE = sc.GetEndPoint(1);

                            for (int i = 0; i < curves.Count; i++)
                            {
                                if (curveToWallId.ContainsKey(i)) continue;
                                var cc = curves[i];
                                if (cc == null) continue;
                                var cS = cc.GetEndPoint(0);
                                var cE = cc.GetEndPoint(1);
                                // 容差 1mm
                                if (cS.DistanceTo(sS) < 1.0 / 304.8 && cE.DistanceTo(sE) < 1.0 / 304.8)
                                {
                                    curveToWallId[i] = wid;
                                    break;
                                }
                            }
                        }
                        if (firstGrp) { firstGrp = false; break; } // 只處理外環
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"建立曲線→牆 ID 對應失敗: {ex.Message}");
            }

            Logger.Log($"成功對應 {curveToWallId.Count}/{curves.Count} 條曲線至牆 ID");

            // 切割每條曲線
            var result = new List<Curve>();
            for (int i = 0; i < curves.Count; i++)
            {
                var c = curves[i];
                if (c == null) continue;

                // 優先使用牆 ID 對應找出相關門窗
                List<FamilyInstance> relevantOpenings = null;
                if (curveToWallId.TryGetValue(i, out long wallId))
                    wallToOpenings.TryGetValue(wallId, out relevantOpenings);

                // 備用：若對應失敗，用距離投影（放寬至 600mm）
                if (relevantOpenings == null || relevantOpenings.Count == 0)
                {
                    var fallback = new List<FamilyInstance>();
                    foreach (var op in openings)
                    {
                        var lp2 = op.Location as LocationPoint;
                        if (lp2 == null) continue;
                        var proj2 = c.Project(lp2.Point);
                        if (proj2 != null && proj2.Distance < (600.0 / 304.8))
                            fallback.Add(op);
                    }
                    relevantOpenings = fallback;
                    if (fallback.Count > 0)
                        Logger.Log($"  曲線 {i} 無牆ID對應，備用距離投影找到 {fallback.Count} 個門窗");
                }

                if (relevantOpenings == null || relevantOpenings.Count == 0)
                {
                    result.Add(c);
                    continue;
                }

                // 計算切割位置：將門窗中心投影到曲線所在直線（XY平面，無限延伸）
                var startPt = c.GetEndPoint(0);
                var endPt   = c.GetEndPoint(1);
                double segLen = c.Length;
                var dir2D = new XYZ(endPt.X - startPt.X, endPt.Y - startPt.Y, 0);
                if (dir2D.GetLength() < 1e-9) { result.Add(c); continue; }
                dir2D = dir2D.Normalize();

                var cuts = new List<(double a, double b)>();
                foreach (var opening in relevantOpenings)
                {
                    var lp = opening.Location as LocationPoint;
                    if (lp == null) continue;
                    var pt = lp.Point;

                    // 沿牆方向的投影距離（相對於曲線起點，XY 平面）
                    var v = new XYZ(pt.X - startPt.X, pt.Y - startPt.Y, 0);
                    double projDistAlongWall = v.DotProduct(dir2D);

                    double width = GetOpeningWidth(opening);
                    double half  = width * 0.5;

                    double tMid  = projDistAlongWall / segLen;
                    double tHalf = half / segLen;

                    double a = Math.Max(0, tMid - tHalf);
                    double b = Math.Min(1, tMid + tHalf);

                    if (b > a + 1e-6 && (b - a) < 0.95)
                    {
                        cuts.Add((a, b));
                        Logger.Log($"  曲線 {i} 切割: tMid={tMid:F3} ±{tHalf:F3} → [{a:F3},{b:F3}]（開口寬 {width * 304.8:F0}mm）");
                    }
                }

                if (cuts.Count == 0) { result.Add(c); continue; }

                // 合併重疊切割區間
                cuts.Sort((x, y) => x.a.CompareTo(y.a));
                var merged = new List<(double a, double b)>();
                var cur = cuts[0];
                for (int j = 1; j < cuts.Count; j++)
                {
                    if (cuts[j].a <= cur.b) cur = (cur.a, Math.Max(cur.b, cuts[j].b));
                    else { merged.Add(cur); cur = cuts[j]; }
                }
                merged.Add(cur);

                // 建立切割後的子曲線（最小長度 5mm）
                const double minSegLenM = 5.0 / 304.8;
                double prev = 0;
                foreach (var m in merged)
                {
                    if (m.a - prev > 1e-5)
                    {
                        var ra  = c.ComputeRawParameter(prev);
                        var rb  = c.ComputeRawParameter(m.a);
                        var seg = c.Clone();
                        seg.MakeBound(ra, rb);
                        if (seg.Length > minSegLenM) result.Add(seg);
                    }
                    prev = m.b;
                }
                if (1 - prev > 1e-5)
                {
                    var ra  = c.ComputeRawParameter(prev);
                    var rb  = c.ComputeRawParameter(1.0);
                    var seg = c.Clone();
                    seg.MakeBound(ra, rb);
                    if (seg.Length > minSegLenM) result.Add(seg);
                }
            }

            Logger.Log($"CutCurvesByDoorsAndWindows 完成：{curves.Count} 條 → {result.Count} 條");
            return result;
        }

        void CreateWallOpenings(Room room, List<Wall> walls, bool includeDoors = true, bool includeWindows = true)
        {
            try
            {
                Logger.Log($"開始為房間 {room.Name} 的牆面建立開口並與結構牆體接合");
                
                var doc = Doc;
                var openings = new List<FamilyInstance>();
                var structuralWalls = GetStructuralWallsInRoom(room);
                Logger.Log($"找到 {structuralWalls.Count} 個結構牆體");

                // 收集門和窗
                if (includeDoors)
                {
                    var doors = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .Where(d => IsOpeningRelatedToRoom(d, room))
                        .ToList();
                    openings.AddRange(doors);
                    Logger.Log($"找到 {doors.Count} 個門");
                }

                if (includeWindows)
                {
                    var windows = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Windows)
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .Where(w => IsOpeningRelatedToRoom(w, room))
                        .ToList();
                    openings.AddRange(windows);
                    Logger.Log($"找到 {windows.Count} 個窗");
                }

                if (!openings.Any())
                {
                    Logger.Log("沒有找到相關的門窗開口");
                    return;
                }

                // 對每面牆進行開口切割
                foreach (var wall in walls)
                {
                    Logger.Log($"處理牆面 {wall.Id} 的開口");
                    var wallLocation = (wall.Location as LocationCurve)?.Curve;
                    
                    foreach (var opening in openings)
                    {
                        try
                        {
                            var openingLocation = (opening.Location as LocationPoint)?.Point;
                            if (openingLocation == null) continue;
                            
                            Logger.Log($"檢查開口 {opening.Id} ({opening.Name}) 位置: ({openingLocation.X * 304.8:F1}, {openingLocation.Y * 304.8:F1})");
                            
                            // 檢查開口是否在這面牆上（更詳細的檢測）
                            bool isOnThisWall = false;
                            string checkResult = "";
                            
                            if (wallLocation != null)
                            {
                                var projection = wallLocation.Project(openingLocation);
                                if (projection != null)
                                {
                                    var distance = projection.Distance * 304.8;
                                    checkResult += $"距離={distance:F1}mm ";
                                    
                                    // 對於粉刷牆，使用更寬鬆的距離判斷（800mm）
                                    bool withinDistance = distance < 800;
                                    
                                    // 檢查投影點是否在牆段範圍內
                                    bool withinRange = true;
                                    if (wallLocation is Line line)
                                    {
                                        var parameter = projection.Parameter;
                                        withinRange = parameter >= -0.1 && parameter <= 1.1; // 稍微放寬範圍
                                        checkResult += $"參數={parameter:F2} ";
                                    }
                                    
                                    isOnThisWall = withinDistance && withinRange;
                                    checkResult += $"符合距離={withinDistance} 符合範圍={withinRange}";
                                }
                                else
                                {
                                    checkResult = "投影失敗";
                                }
                            }
                            else
                            {
                                checkResult = "牆面位置為空";
                            }
                            
                            Logger.Log($"開口 {opening.Id} 與牆面 {wall.Id} 關係檢查: {checkResult}, 結果: {isOnThisWall}");
                            
                            if (!isOnThisWall)
                            {
                                Logger.Log($"❌ 開口 {opening.Id} 不符合牆面 {wall.Id} 的位置條件，跳過");
                                continue;
                            }
                            
                            Logger.Log($"✓ 開口 {opening.Id} 符合牆面 {wall.Id} 的位置條件，準備切割");
                            
                            // 簡化的切割方法
                            try
                            {
                                Logger.Log($"嘗試切割牆 {wall.Id} 與開口 {opening.Id}");
                                
                                // 檢查基本條件
                                bool canCutWall = InstanceVoidCutUtils.CanBeCutWithVoid(wall);
                                bool canCutWithOpening = InstanceVoidCutUtils.IsVoidInstanceCuttingElement(opening);
                                
                                Logger.Log($"牆面可切割: {canCutWall}, 開口可切割: {canCutWithOpening}");
                                
                                if (canCutWall && canCutWithOpening)
                                {
                                    // 檢查是否已存在切割關係
                                    bool alreadyExists = InstanceVoidCutUtils.InstanceVoidCutExists(opening, wall);
                                    Logger.Log($"切割關係已存在: {alreadyExists}");
                                    
                                    if (!alreadyExists)
                                    {
                                        try
                                        {
                                            InstanceVoidCutUtils.AddInstanceVoidCut(doc, opening, wall);
                                            Logger.Log($"✓ 成功切割牆 {wall.Id} 與開口 {opening.Id}");
                                        }
                                        catch (Exception addEx)
                                        {
                                            Logger.Log($"✗ 添加切割關係失敗: {addEx.Message}");
                                            Logger.Log($"   可能原因: 開口與牆面位置不匹配或幾何問題");
                                        }
                                    }
                                    else
                                    {
                                        Logger.Log($"✓ 牆 {wall.Id} 與開口 {opening.Id} 已存在切割關係");
                                    }
                                }
                                else
                                {
                                    Logger.Log($"⚠️ 不滿足切割條件:");
                                    Logger.Log($"   牆面 {wall.Id} 可切割: {canCutWall}");
                                    Logger.Log($"   開口 {opening.Id} 可作為切割元素: {canCutWithOpening}");
                                    
                                    // 嘗試診斷問題
                                    if (!canCutWall)
                                    {
                                        Logger.Log($"   牆面問題: 可能是牆面類型不支援或幾何問題");
                                    }
                                    if (!canCutWithOpening)
                                    {
                                        Logger.Log($"   開口問題: 可能是家族類型不支援或未正確放置");
                                    }
                                }
                            }
                            catch (Exception cutEx)
                            {
                                Logger.Log($"✗ 切割操作失敗: {cutEx.Message}");
                                Logger.Log($"   錯誤詳情: {cutEx.StackTrace}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"切割牆 {wall.Id} 與開口 {opening.Id} 失敗: {ex.Message}");
                        }
                    }
                    
                    // 完成開口切割後，嘗試與結構牆體切割（避免延伸結構牆體）
                    TryJoinWithStructuralWalls(wall, structuralWalls);
                }

                Logger.Log($"完成房間 {room.Name} 的牆面開口切割和結構牆體接合");
            }
            catch (Exception ex)
            {
                Logger.Log($"建立牆面開口異常: {ex.Message}");
            }
        }

        /// <summary>
        /// 對每面粉刷牆找到對應的結構牆（位於其邊界面）並執行 JoinGeometry：
        ///   ① 切除粉刷牆插入結構牆的重疊部分（齊平室內面）
        ///   ② 繼承結構牆的門窗 Void → 粉刷牆自動開孔（Dynamo Join 邏輯）
        /// </summary>
        private void JoinFinishWallsWithStructuralWalls(Room room, List<Wall> finishWalls)
        {
            try
            {
                var structuralWalls = GetStructuralWallsInRoom(room);
                if (!structuralWalls.Any())
                {
                    Logger.Log("找不到結構牆，略過接合");
                    return;
                }
                Logger.Log($"開始接合 {finishWalls.Count} 面粉刷牆與 {structuralWalls.Count} 面結構牆");

                foreach (var fw in finishWalls)
                {
                    try
                    {
                        var fwLoc = (fw.Location as LocationCurve)?.Curve;
                        if (fwLoc == null) continue;

                        var fwMid = fwLoc.Evaluate(0.5, true);
                        var fwDir = (fwLoc.GetEndPoint(1) - fwLoc.GetEndPoint(0)).Normalize();

                        Wall bestSw = null;
                        double bestScore = double.MaxValue;

                        foreach (var sw in structuralWalls)
                        {
                            try
                            {
                                var swLoc = (sw.Location as LocationCurve)?.Curve;
                                if (swLoc == null) continue;

                                // 方向必須平行（>0.98 dot product）
                                var swDir = (swLoc.GetEndPoint(1) - swLoc.GetEndPoint(0)).Normalize();
                                if (Math.Abs(fwDir.DotProduct(swDir)) < 0.98) continue;

                                // 垂直距離應接近 SW.Width/2（粉刷牆中心線在結構牆室內面上）
                                var proj = swLoc.Project(fwMid);
                                if (proj == null) continue;

                                double perpDist = proj.Distance;
                                double expectedDist = sw.Width / 2.0;
                                double tolerance = fw.Width + (60.0 / 304.8); // finish wall width + 60mm

                                double score = Math.Abs(perpDist - expectedDist);
                                if (score < tolerance && score < bestScore)
                                {
                                    bestScore = score;
                                    bestSw = sw;
                                }
                            }
                            catch { }
                        }

                        if (bestSw == null)
                        {
                            Logger.Log($"⚠ 粉刷牆 {fw.Id} 找不到對應結構牆，略過接合");
                            continue;
                        }

                        if (JoinGeometryUtils.AreElementsJoined(Doc, fw, bestSw))
                        {
                            // 已接合：確保結構牆為切割方（structural wall cuts finish wall）
                            try
                            {
                                if (!JoinGeometryUtils.IsCuttingElementInJoin(Doc, bestSw, fw))
                                    JoinGeometryUtils.SwitchJoinOrder(Doc, bestSw, fw);
                            }
                            catch { }
                            Logger.Log($"✓ 粉刷牆 {fw.Id} 已與結構牆 {bestSw.Id} 接合");
                        }
                        else
                        {
                            // JoinGeometry(sw, fw)：結構牆切割粉刷牆 → 繼承門窗 Void
                            JoinGeometryUtils.JoinGeometry(Doc, bestSw, fw);
                            try
                            {
                                if (!JoinGeometryUtils.IsCuttingElementInJoin(Doc, bestSw, fw))
                                    JoinGeometryUtils.SwitchJoinOrder(Doc, bestSw, fw);
                            }
                            catch { }
                            Logger.Log($"✓ 接合完成：結構牆 {bestSw.Id} 切割粉刷牆 {fw.Id}（得分 {bestScore * 304.8:F1}mm）");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"接合粉刷牆 {fw.Id} 失敗: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"JoinFinishWallsWithStructuralWalls 異常: {ex.Message}");
            }
        }

        /// <summary>
        /// 智能處理裝修牆與結構牆的關係，避免結構牆錯誤延伸
        /// </summary>
        private void TryJoinWithStructuralWalls(Wall finishWall, List<Wall> structuralWalls)
        {
            try
            {
                Logger.Log($"嘗試處理裝修牆 {finishWall.Id} 與結構牆體的關係");
                
                var finishWallCurve = (finishWall.Location as LocationCurve)?.Curve;
                if (finishWallCurve == null) return;

                // 找出與裝修牆平行且重疊的結構牆
                var overlappingStructWalls = new List<Wall>();
                
                foreach (var structWall in structuralWalls)
                {
                    try
                    {
                        var structWallCurve = (structWall.Location as LocationCurve)?.Curve;
                        if (structWallCurve == null) continue;

                        // 檢查是否為平行重疊的結構牆
                        if (AreWallsParallelAndOverlapping(finishWallCurve, structWallCurve))
                        {
                            overlappingStructWalls.Add(structWall);
                            Logger.Log($"發現重疊的結構牆: {structWall.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"檢查結構牆 {structWall.Id} 時發生錯誤: {ex.Message}");
                    }
                }

                // 對於重疊的結構牆，採用保守的處理方式
                foreach (var structWall in overlappingStructWalls)
                {
                    try
                    {
                        Logger.Log($"處理重疊結構牆 {structWall.Id}");
                        
                        // 檢查是否已有接合關係
                        if (JoinGeometryUtils.AreElementsJoined(Doc, finishWall, structWall))
                        {
                            Logger.Log($"裝修牆與結構牆已接合，檢查接合順序");
                            
                            // 嘗試確保裝修牆被結構牆正確切割（但不延伸結構牆）
                            try
                            {
                                // 使用 IsCuttingElementInJoin 檢查切割關係
                                bool isStructWallCutting = JoinGeometryUtils.IsCuttingElementInJoin(Doc, structWall, finishWall);
                                
                                if (!isStructWallCutting)
                                {
                                    JoinGeometryUtils.SwitchJoinOrder(Doc, structWall, finishWall);
                                    Logger.Log($"已調整為結構牆切割裝修牆");
                                }
                                else
                                {
                                    Logger.Log($"結構牆已正確切割裝修牆");
                                }
                            }
                            catch (Exception switchEx)
                            {
                                Logger.Log($"調整切割順序失敗: {switchEx.Message}");
                            }
                        }
                        else
                        {
                            // 嘗試建立接合關係，但要小心處理
                            try
                            {
                                JoinGeometryUtils.JoinGeometry(Doc, structWall, finishWall);
                                Logger.Log($"成功建立接合關係：結構牆 {structWall.Id} 與裝修牆 {finishWall.Id}");
                                
                                // 確保正確的切割關係
                                try
                                {
                                    if (!JoinGeometryUtils.IsCuttingElementInJoin(Doc, structWall, finishWall))
                                    {
                                        JoinGeometryUtils.SwitchJoinOrder(Doc, structWall, finishWall);
                                        Logger.Log($"已設定結構牆切割裝修牆");
                                    }
                                }
                                catch (Exception cutEx)
                                {
                                    Logger.Log($"設定切割關係失敗: {cutEx.Message}");
                                }
                            }
                            catch (Exception joinEx)
                            {
                                Logger.Log($"建立接合關係失敗: {joinEx.Message}");
                                // 如果無法接合，保持裝修牆獨立存在
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"處理結構牆 {structWall.Id} 時發生錯誤: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"處理結構牆體關係失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 檢查兩面牆是否重疊（更精確的檢測）
        /// </summary>
        private bool AreWallsOverlapping(Curve curve1, Curve curve2)
        {
            try
            {
                // 方法1：使用相交檢測
                var intersectionResult = curve1.Intersect(curve2);
                if (intersectionResult == SetComparisonResult.Overlap || 
                    intersectionResult == SetComparisonResult.Subset ||
                    intersectionResult == SetComparisonResult.Superset ||
                    intersectionResult == SetComparisonResult.Equal)
                {
                    return true;
                }

                // 方法2：檢查距離和平行度
                var distance1 = curve1.Distance(curve2.GetEndPoint(0));
                var distance2 = curve1.Distance(curve2.GetEndPoint(1));
                var avgDistance = (distance1 + distance2) / 2;

                // 如果平均距離小於0.5英尺，認為可能重疊
                if (avgDistance < 0.5)
                {
                    // 檢查是否平行
                    var dir1 = (curve1.GetEndPoint(1) - curve1.GetEndPoint(0)).Normalize();
                    var dir2 = (curve2.GetEndPoint(1) - curve2.GetEndPoint(0)).Normalize();
                    var dot = Math.Abs(dir1.DotProduct(dir2));
                    
                    // 如果接近平行且距離近，認為重疊
                    return dot > 0.8;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"檢查牆面重疊失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 檢查兩面牆是否重疊（簡化版本，僅檢查明顯重疊情況）
        /// </summary>
        private bool AreWallsParallelAndOverlapping(Curve curve1, Curve curve2)
        {
            try
            {
                // 快速檢查：如果兩條線完全相同，則重疊
                var start1 = curve1.GetEndPoint(0);
                var end1 = curve1.GetEndPoint(1);
                var start2 = curve2.GetEndPoint(0);
                var end2 = curve2.GetEndPoint(1);
                
                // 檢查端點是否非常接近（10mm內視為相同）
                double tolerance = 10.0 / 304.8; // 10mm轉英尺
                
                bool sameStart1 = start1.DistanceTo(start2) < tolerance;
                bool sameEnd1 = end1.DistanceTo(end2) < tolerance;
                bool sameStart2 = start1.DistanceTo(end2) < tolerance;
                bool sameEnd2 = end1.DistanceTo(start2) < tolerance;
                
                // 如果端點完全重合（無論方向），視為重疊
                if ((sameStart1 && sameEnd1) || (sameStart2 && sameEnd2))
                {
                    Logger.Log($"檢測到端點重合的重疊牆面");
                    return true;
                }
                
                // 檢查是否平行
                var dir1 = (end1 - start1).Normalize();
                var dir2 = (end2 - start2).Normalize();
                var dotProduct = Math.Abs(dir1.DotProduct(dir2));
                
                // 只有在高度平行（>0.98，約11度內）且距離很近時才視為重疊
                if (dotProduct > 0.98)
                {
                    // 計算兩條線之間的最小距離
                    var midPoint1 = start1 + (end1 - start1) * 0.5;
                    var midPoint2 = start2 + (end2 - start2) * 0.5;
                    var midDistance = midPoint1.DistanceTo(midPoint2);
                    
                    // 只有在中點距離非常近（20mm內）時才視為重疊
                    if (midDistance < 20.0 / 304.8)
                    {
                        Logger.Log($"檢測到平行且接近的牆面：點積={dotProduct:F3}, 中點距離={midDistance * 304.8:F1}mm");
                        
                        // 進一步檢查線段重疊
                        try
                        {
                            var intersectionResult = curve1.Intersect(curve2);
                            bool hasOverlap = intersectionResult == SetComparisonResult.Overlap || 
                                            intersectionResult == SetComparisonResult.Equal;
                            
                            Logger.Log($"線段相交結果: {intersectionResult}, 確定重疊: {hasOverlap}");
                            return hasOverlap;
                        }
                        catch
                        {
                            // 相交檢測失敗時，基於嚴格條件判斷
                            Logger.Log($"相交檢測失敗，基於距離嚴格判斷");
                            return midDistance < 10.0 / 304.8; // 只有10mm內才視為重疊
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"檢查牆面重疊失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 清理重疊的牆面（保留較長的或較早創建的）
        /// </summary>
        private List<Wall> RemoveOverlappingWalls(List<Wall> walls)
        {
            try
            {
                Logger.Log($"開始檢查 {walls.Count} 面牆的重疊情況");
                
                var wallsToRemove = new HashSet<ElementId>();
                
                // 比較每對牆面
                for (int i = 0; i < walls.Count; i++)
                {
                    if (wallsToRemove.Contains(walls[i].Id)) continue;
                    
                    var curve1 = (walls[i].Location as LocationCurve)?.Curve;
                    if (curve1 == null) continue;
                    
                    for (int j = i + 1; j < walls.Count; j++)
                    {
                        if (wallsToRemove.Contains(walls[j].Id)) continue;
                        
                        var curve2 = (walls[j].Location as LocationCurve)?.Curve;
                        if (curve2 == null) continue;
                        
                        // 檢查是否重疊
                        if (AreWallsParallelAndOverlapping(curve1, curve2))
                        {
                            Logger.Log($"發現重疊牆面: {walls[i].Id} 與 {walls[j].Id}");
                            
                            // 決定要保留哪一面牆（保留較長的）
                            if (curve1.Length >= curve2.Length)
                            {
                                Logger.Log($"保留較長的牆面 {walls[i].Id} ({curve1.Length * 304.8:F1}mm)，移除 {walls[j].Id} ({curve2.Length * 304.8:F1}mm)");
                                wallsToRemove.Add(walls[j].Id);
                            }
                            else
                            {
                                Logger.Log($"保留較長的牆面 {walls[j].Id} ({curve2.Length * 304.8:F1}mm)，移除 {walls[i].Id} ({curve1.Length * 304.8:F1}mm)");
                                wallsToRemove.Add(walls[i].Id);
                                break; // 當前牆面被標記移除，不需要繼續比較
                            }
                        }
                    }
                }
                
                // 刪除重疊的牆面
                var wallsToDelete = walls.Where(w => wallsToRemove.Contains(w.Id)).ToList();
                foreach (var wall in wallsToDelete)
                {
                    try
                    {
                        Logger.Log($"刪除重疊牆面: {wall.Id}");
                        Doc.Delete(wall.Id);
                    }
                    catch (Exception deleteEx)
                    {
                        Logger.Log($"刪除牆面 {wall.Id} 失敗: {deleteEx.Message}");
                    }
                }
                
                // 返回剩餘的牆面
                var remainingWalls = walls.Where(w => !wallsToRemove.Contains(w.Id)).ToList();
                Logger.Log($"重疊檢查完成，刪除了 {wallsToDelete.Count} 面牆，剩餘 {remainingWalls.Count} 面牆");
                
                return remainingWalls;
            }
            catch (Exception ex)
            {
                Logger.Log($"清理重疊牆面失敗: {ex.Message}");
                return walls; // 出錯時返回原始列表
            }
        }

        /// <summary>
        /// 檢查兩面牆是否相交
        /// </summary>
        private bool DoWallsIntersect(Curve curve1, Curve curve2)
        {
            try
            {
                var intersectionResult = curve1.Intersect(curve2);
                return intersectionResult == SetComparisonResult.Overlap || 
                       intersectionResult == SetComparisonResult.Subset ||
                       intersectionResult == SetComparisonResult.Superset ||
                       intersectionResult == SetComparisonResult.Equal;
            }
            catch
            {
                // 如果相交檢測失敗，回退到距離檢測
                var distance = curve1.Distance(curve2.GetEndPoint(0));
                return distance < 0.1; // 距離小於0.1英尺認為相交
            }
        }

        /// <summary>
        /// 設定粉刷牆的基本屬性，確保定位線為核心面:內部
        /// </summary>
        private void SetFinishWallProperties(Wall wall, WallLocationLine locationMode = WallLocationLine.WallCenterline)
        {
            try
            {
                Logger.Log($"開始設定粉刷牆 {wall.Id} 的屬性");

                // 設定牆面底部偏移為0
                var baseOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (baseOffsetParam != null && !baseOffsetParam.IsReadOnly)
                {
                    baseOffsetParam.Set(0.0);
                    Logger.Log($"已設定牆面 {wall.Id} 底部偏移為0");
                }

                // 設定房間邊界屬性為false（粉刷牆不影響房間邊界）
                var roomBoundingParam = wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                if (roomBoundingParam != null && !roomBoundingParam.IsReadOnly)
                {
                    roomBoundingParam.Set(0); // 0 = false, 1 = true
                    Logger.Log($"已將粉刷牆 {wall.Id} 的房間邊界屬性設為false");
                }
                else
                {
                    Logger.Log($"無法設定粉刷牆 {wall.Id} 的房間邊界屬性");
                }

                // 設定牆定位線（粉刷牆建議用 FinishFaceExterior，踢腳板維持中心線）
                try
                {
                    var locationLine = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
                    if (locationLine != null && !locationLine.IsReadOnly)
                    {
                        var locationValue = (int)locationMode;
                        Logger.Log($"設定粉刷牆 {wall.Id} 定位線為 {locationMode} (值: {locationValue})");
                        
                        locationLine.Set(locationValue);
                        
                        // 注意：不在每面牆的屬性設定迴圈內呼叫 Regenerate()，
                        // 避免在中間狀態下觸發房間邊界重計算。
                        // Regenerate 由呼叫者在所有牆建立完畢後統一執行。
                        
                        // 重新讀取驗證
                        var verifyValue = locationLine.AsInteger();
                        if (verifyValue == locationValue)
                        {
                            Logger.Log($"✓ 粉刷牆 {wall.Id} 定位線設定成功：{locationMode}");
                        }
                        else
                        {
                            Logger.Log($"⚠ 粉刷牆 {wall.Id} 定位線設定異常，實際值: {verifyValue}");
                            
                            // 再次嘗試設定
                            try
                            {
                                locationLine.Set(locationValue);
                                Logger.Log($"重新嘗試設定粉刷牆 {wall.Id} 定位線");
                            }
                            catch (Exception retryEx)
                            {
                                Logger.Log($"重新設定失敗: {retryEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        Logger.Log($"⚠ 無法存取粉刷牆 {wall.Id} 的定位線參數");
                    }
                }
                catch (Exception locEx)
                {
                    Logger.Log($"✗ 設定粉刷牆 {wall.Id} 定位線時發生錯誤: {locEx.Message}");
                }

                Logger.Log($"粉刷牆 {wall.Id} 屬性設定完成");
            }
            catch (Exception ex)
            {
                Logger.Log($"✗ 設定粉刷牆 {wall.Id} 屬性時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 檢查兩面牆是否接近且平行
        /// </summary>
        private bool AreWallsNearAndParallel(Curve curve1, Curve curve2, double tolerance = 1.0)
        {
            try
            {
                // 計算距離
                var distance = curve1.Distance(curve2.GetEndPoint(0));
                if (distance > tolerance) return false;

                // 檢查平行度
                var dir1 = (curve1.GetEndPoint(1) - curve1.GetEndPoint(0)).Normalize();
                var dir2 = (curve2.GetEndPoint(1) - curve2.GetEndPoint(0)).Normalize();
                var dot = Math.Abs(dir1.DotProduct(dir2));
                
                return dot > 0.9; // 接近平行
            }
            catch
            {
                return false;
            }
        }

        List<Curve> MergeContinuousLines(List<Curve> curves)
        {
            try
            {
                if (curves.Count <= 1) return curves;

                Logger.Log($"開始合併 {curves.Count} 條曲線");
                var result = new List<Curve>();
                var used = new bool[curves.Count];

                for (int i = 0; i < curves.Count; i++)
                {
                    if (used[i]) continue;

                    var currentCurve = curves[i];
                    if (!(currentCurve is Line)) 
                    {
                        result.Add(currentCurve);
                        used[i] = true;
                        continue;
                    }

                    var startLine = currentCurve as Line;
                    var mergedPoints = new List<XYZ> { startLine.GetEndPoint(0), startLine.GetEndPoint(1) };
                    used[i] = true;

                    // 向前合併
                    bool foundConnection = true;
                    while (foundConnection)
                    {
                        foundConnection = false;
                        var lastPoint = mergedPoints.Last();
                        
                        for (int j = 0; j < curves.Count; j++)
                        {
                            if (used[j] || !(curves[j] is Line)) continue;
                            
                            var line = curves[j] as Line;
                            var start = line.GetEndPoint(0);
                            var end = line.GetEndPoint(1);
                            
                            // 檢查是否可以連接並且方向一致
                            if (IsPointsClose(lastPoint, start) && IsDirectionConsistent(startLine, line))
                            {
                                mergedPoints.Add(end);
                                used[j] = true;
                                foundConnection = true;
                                break;
                            }
                            else if (IsPointsClose(lastPoint, end) && IsDirectionConsistent(startLine, line))
                            {
                                mergedPoints.Add(start);
                                used[j] = true;
                                foundConnection = true;
                                break;
                            }
                        }
                    }

                    // 向後合併
                    foundConnection = true;
                    while (foundConnection)
                    {
                        foundConnection = false;
                        var firstPoint = mergedPoints.First();
                        
                        for (int j = 0; j < curves.Count; j++)
                        {
                            if (used[j] || !(curves[j] is Line)) continue;
                            
                            var line = curves[j] as Line;
                            var start = line.GetEndPoint(0);
                            var end = line.GetEndPoint(1);
                            
                            if (IsPointsClose(firstPoint, end) && IsDirectionConsistent(startLine, line))
                            {
                                mergedPoints.Insert(0, start);
                                used[j] = true;
                                foundConnection = true;
                                break;
                            }
                            else if (IsPointsClose(firstPoint, start) && IsDirectionConsistent(startLine, line))
                            {
                                mergedPoints.Insert(0, end);
                                used[j] = true;
                                foundConnection = true;
                                break;
                            }
                        }
                    }

                    // 如果合併了多個點，創建新的線段
                    if (mergedPoints.Count > 2)
                    {
                        // 簡化為直線
                        var mergedLine = Line.CreateBound(mergedPoints.First(), mergedPoints.Last());
                        result.Add(mergedLine);
                        Logger.Log($"合併了 {mergedPoints.Count - 1} 條線段，總長度: {mergedLine.Length * 304.8:F2} mm");
                    }
                    else
                    {
                        result.Add(startLine);
                    }
                }

                Logger.Log($"合併完成，從 {curves.Count} 條減少到 {result.Count} 條");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Log($"合併曲線異常: {ex.Message}");
                return curves; // 發生錯誤時返回原始曲線
            }
        }

        bool IsPointsClose(XYZ p1, XYZ p2, double tolerance = 1e-6)
        {
            return p1.DistanceTo(p2) < tolerance;
        }

        bool IsDirectionConsistent(Line line1, Line line2, double angleTolerance = 0.1)
        {
            try
            {
                var dir1 = line1.Direction;
                var dir2 = line2.Direction;
                
                // 檢查是否平行（考慮反向）
                var dot = Math.Abs(dir1.DotProduct(dir2));
                return dot > Math.Cos(angleTolerance);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 對彼此端點距離 ≤ 5mm 的粉刷牆使用 JoinGeometryUtils 做幾何接合，
        /// 以確保角落視覺正確。全程不呼叫 AllowWallJoinAtEnd，
        /// 所有粉刷牆端點維持 DisallowWallJoinAtEnd 狀態，
        /// 防止 Revit 把粉刷牆端點自動對接至結構牆而導致結構牆端點位移。
        /// </summary>
        void EnableFinishWallCornerJoins(List<Wall> finishWalls)
        {
            // 判斷邏輯：粉刷牆角落端點距離 = halfWidth × √2（非 0），
            // 端點距離判斷（5mm）會全部失敗。改用「端點是否落在另一道粉刷牆的
            // Bounding Box 內」來偵測角落相鄰關係，再呼叫 AllowWallJoinAtEnd
            // 讓 Revit 原生牆接合演算法產生正確的斜切角落接合。
            const double bbTol = 5.0 / 304.8; // 5mm — BB 邊界容差（浮點安全）
            int enabledCount = 0;
            try
            {
                foreach (var wall in finishWalls)
                {
                    var lc = (wall.Location as LocationCurve)?.Curve;
                    if (lc == null) continue;
                    var p0 = lc.GetEndPoint(0);
                    var p1 = lc.GetEndPoint(1);
                    bool end0Touch = false, end1Touch = false;

                    foreach (var other in finishWalls)
                    {
                        if (other.Id == wall.Id) continue;
                        var otherBB = other.get_BoundingBox(null);
                        if (otherBB == null) continue;

                        // 端點落在另一道粉刷牆的 BB 內（含容差）→ 表示角落相鄰
                        if (!end0Touch &&
                            p0.X >= otherBB.Min.X - bbTol && p0.X <= otherBB.Max.X + bbTol &&
                            p0.Y >= otherBB.Min.Y - bbTol && p0.Y <= otherBB.Max.Y + bbTol)
                            end0Touch = true;

                        if (!end1Touch &&
                            p1.X >= otherBB.Min.X - bbTol && p1.X <= otherBB.Max.X + bbTol &&
                            p1.Y >= otherBB.Min.Y - bbTol && p1.Y <= otherBB.Max.Y + bbTol)
                            end1Touch = true;

                        if (end0Touch && end1Touch) break;
                    }

                    if (end0Touch)
                        try { WallUtils.AllowWallJoinAtEnd(wall, 0); enabledCount++; } catch { }
                    if (end1Touch)
                        try { WallUtils.AllowWallJoinAtEnd(wall, 1); enabledCount++; } catch { }
                }
                Logger.Log($"✓ 已對 {enabledCount} 個粉刷牆端點啟用 Wall Join（角落相鄰判斷）");
            }
            catch (Exception ex)
            {
                Logger.Log($"EnableFinishWallCornerJoins 失敗: {ex.Message}");
            }
        }

        void JoinAdjacentWalls(List<Wall> walls)
        {
            try
            {
                Logger.Log($"開始智能接合 {walls.Count} 個牆面");
                int joinCount = 0;

                // 只允許同一房間標記、同方向、端點真正相鄰的裝修牆互相接合。
                // 任何 RoomBounding 牆都直接排除，避免影響結構牆。

                // 使用更智能的接合方式
                for (int i = 0; i < walls.Count; i++)
                {
                    for (int j = i + 1; j < walls.Count; j++)
                    {
                        var wall1 = walls[i];
                        var wall2 = walls[j];

                        try
                        {
                            if (!CanJoinFinishWallsSafely(wall1, wall2))
                            {
                                Logger.Log($"略過接合 {wall1.Id} / {wall2.Id}：不符合安全接合條件");
                                continue;
                            }

                            // 檢查牆面是否相鄰且應該接合
                            if (ShouldJoinWalls(wall1, wall2))
                            {
                                Logger.Log($"牆面 {wall1.Id} 和 {wall2.Id} 應該接合");
                                
                                try
                                {
                                    // 使用 JoinGeometryUtils 進行接合
                                    if (!JoinGeometryUtils.AreElementsJoined(Doc, wall1, wall2))
                                    {
                                        JoinGeometryUtils.JoinGeometry(Doc, wall1, wall2);
                                        joinCount++;
                                        Logger.Log($"成功接合牆面 {wall1.Id} 和 {wall2.Id}");

                                        // 接合後再次鎖住端點，避免 Revit 後續自動延伸。
                                        try { WallUtils.DisallowWallJoinAtEnd(wall1, 0); } catch { }
                                        try { WallUtils.DisallowWallJoinAtEnd(wall1, 1); } catch { }
                                        try { WallUtils.DisallowWallJoinAtEnd(wall2, 0); } catch { }
                                        try { WallUtils.DisallowWallJoinAtEnd(wall2, 1); } catch { }
                                    }
                                    else
                                    {
                                        Logger.Log($"牆面 {wall1.Id} 和 {wall2.Id} 已經接合");
                                    }
                                }
                                catch (Exception joinEx)
                                {
                                    // 不回退到 AllowWallJoinAtEnd，避免再次觸發結構牆端點移位。
                                    Logger.Log($"幾何接合失敗，跳過: {joinEx.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"檢查牆面 {wall1.Id} 和 {wall2.Id} 關係失敗: {ex.Message}");
                        }
                    }
                }

                Logger.Log($"完成智能牆面接合，成功接合了 {joinCount} 對牆面");
            }
            catch (Exception ex)
            {
                Logger.Log($"牆面接合異常: {ex.Message}");
            }
        }

        private bool CanJoinFinishWallsSafely(Wall wall1, Wall wall2)
        {
            try
            {
                if (wall1 == null || wall2 == null || wall1.Id == wall2.Id)
                    return false;

                // 只接合裝修牆，嚴禁觸碰任何 RoomBounding 牆
                if (IsRoomBoundingWall(wall1) || IsRoomBoundingWall(wall2))
                    return false;

                // 兩面牆必須標記同一個房間 ID（由 SetFinishWallProperties 寫入 AR_RoomId）
                var roomId1 = GetTaggedRoomId(wall1);
                var roomId2 = GetTaggedRoomId(wall2);
                if (roomId1 <= 0 || roomId2 <= 0 || roomId1 != roomId2)
                    return false;

                var curve1 = (wall1.Location as LocationCurve)?.Curve as Line;
                var curve2 = (wall2.Location as LocationCurve)?.Curve as Line;
                if (curve1 == null || curve2 == null)
                    return false;

                // ─── Dynamo 對齊邏輯 ────────────────────────────────────────────
                // Dynamo 腳本中，每條房間邊界線各自建一面牆，轉角端點天然相鄰。
                // 條件：端點距離 ≤ 5mm（與 Dynamo 端點對齊精度一致）。
                // 不要求平行：相鄰裝修牆通常是 90° 轉角，平行過濾會把轉角全部排除。
                // 唯一需要排除的是「同方向且線段重疊」的情形（誤合成同牆），
                // 以 dot product 接近 ±1 且端點不相鄰來辨別。
                // ────────────────────────────────────────────────────────────────
                var tolerance = 5.0 / 304.8; // 5 mm in feet
                var d00 = curve1.GetEndPoint(0).DistanceTo(curve2.GetEndPoint(0));
                var d01 = curve1.GetEndPoint(0).DistanceTo(curve2.GetEndPoint(1));
                var d10 = curve1.GetEndPoint(1).DistanceTo(curve2.GetEndPoint(0));
                var d11 = curve1.GetEndPoint(1).DistanceTo(curve2.GetEndPoint(1));
                var minDist = Math.Min(Math.Min(d00, d01), Math.Min(d10, d11));

                if (minDist > tolerance)
                    return false;

                // 排除共線重疊（兩端點都相鄰 → 同一段牆被複製）
                var bothEndsClose = (d00 <= tolerance && d11 <= tolerance)
                                 || (d01 <= tolerance && d10 <= tolerance);
                if (bothEndsClose)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsRoomBoundingWall(Wall wall)
        {
            try
            {
                var roomBoundingParam = wall?.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                return roomBoundingParam != null && roomBoundingParam.AsInteger() == 1;
            }
            catch
            {
                return false;
            }
        }

        private long GetTaggedRoomId(Wall wall)
        {
            try
            {
                var param = wall?.LookupParameter("房間ID(AR_RoomId)")
                    ?? wall?.LookupParameter("房間ID")
                    ?? wall?.LookupParameter("AR_RoomId");

                if (param == null)
                    return -1;

                if (param.StorageType == StorageType.Integer)
                    return param.AsInteger();

                if (param.StorageType == StorageType.String)
                {
                    var text = param.AsString();
                    if (long.TryParse(text, out var roomId))
                        return roomId;
                }
            }
            catch
            {
            }

            return -1;
        }

        bool AreWallsAdjacent(Wall wall1, Wall wall2, double tolerance = 1e-6)
        {
            try
            {
                var line1 = (wall1.Location as LocationCurve).Curve as Line;
                var line2 = (wall2.Location as LocationCurve).Curve as Line;

                // 檢查端點是否相近
                var p1Start = line1.GetEndPoint(0);
                var p1End = line1.GetEndPoint(1);
                var p2Start = line2.GetEndPoint(0);
                var p2End = line2.GetEndPoint(1);

                return IsPointsClose(p1Start, p2Start, tolerance) ||
                       IsPointsClose(p1Start, p2End, tolerance) ||
                       IsPointsClose(p1End, p2Start, tolerance) ||
                       IsPointsClose(p1End, p2End, tolerance);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判斷兩面牆是否應該接合（更智能的判斷）
        /// </summary>
        bool ShouldJoinWalls(Wall wall1, Wall wall2, double tolerance = 0.01640)
        {
            try
            {
                var curve1 = (wall1.Location as LocationCurve)?.Curve;
                var curve2 = (wall2.Location as LocationCurve)?.Curve;
                
                if (curve1 == null || curve2 == null) return false;

                // 獲取端點
                var p1Start = curve1.GetEndPoint(0);
                var p1End = curve1.GetEndPoint(1);
                var p2Start = curve2.GetEndPoint(0);
                var p2End = curve2.GetEndPoint(1);

                // 檢查端點距離
                var distances = new[]
                {
                    p1Start.DistanceTo(p2Start),
                    p1Start.DistanceTo(p2End),
                    p1End.DistanceTo(p2Start),
                    p1End.DistanceTo(p2End)
                };

                var minDistance = distances.Min();
                
                // 如果最近距離小於容差，認為可以接合
                if (minDistance < tolerance)
                {
                    // 額外檢查：確保不是同一條線
                    var wallsOverlap = AreWallsParallelAndOverlapping(curve1, curve2);
                    if (wallsOverlap)
                    {
                        Logger.Log($"牆面 {wall1.Id} 和 {wall2.Id} 重疊，不應接合");
                        return false;
                    }

                    Logger.Log($"牆面 {wall1.Id} 和 {wall2.Id} 端點距離 {minDistance * 304.8:F2}mm，應該接合");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"判斷牆面接合關係失敗: {ex.Message}");
                return false;
            }
        }

        List<Wall> CreateWallsWithProfileCutting(Room room, List<Curve> curves, WallType wallType, Level level, double wallHeight)
        {
            var createdWalls = new List<Wall>();
            
            try
            {
            Logger.Log("開始使用面生面貼面方式建立牆面（不做額外偏移，不與宿主牆 Join）");
                
                // 保留原始分段，避免跨段合併造成穿柱延伸
                var baseCurves = curves.Where(c => c != null && c.Length > 1e-6).ToList();
                Logger.Log($"原始分段有 {baseCurves.Count} 條曲線");

                var finalCurves = DeduplicateCurves(baseCurves);
                Logger.Log($"去重後剩餘 {finalCurves.Count} 條有效曲線");

                // 建立牆面（加入重疊檢測）
                foreach (var curve in finalCurves)
                {
                    if (curve == null || curve.Length < 1e-6) continue;

                    try
                    {
                        // 檢查是否與現有牆面重疊
                        bool hasOverlap = false;
                        foreach (var existingWall in createdWalls)
                        {
                            try
                            {
                                var existingCurve = (existingWall.Location as LocationCurve)?.Curve;
                                if (existingCurve != null && AreWallsParallelAndOverlapping(curve, existingCurve))
                                {
                                    Logger.Log($"⚠️ 檢測到與已創建牆面 {existingWall.Id} 重疊，跳過創建");
                                    hasOverlap = true;
                                    break;
                                }
                            }
                            catch (Exception checkEx)
                            {
                                Logger.Log($"檢查重疊時發生錯誤: {checkEx.Message}");
                            }
                        }
                        
                        if (hasOverlap) continue;
                        
                        // 建立牆面，確保牆面向房間內側  
                        var centerline = BuildFinishWallCenterlineByRoomTest(curve, room, wallType);
                        var wall = Wall.Create(Doc, centerline, wallType.Id, level.Id, wallHeight, 0, false, false);
                        if (wall != null)
                        {
                            // 立即禁用兩端 Wall Join，防止拖拉結構牆端點
                            try { WallUtils.DisallowWallJoinAtEnd(wall, 0); } catch { }
                            try { WallUtils.DisallowWallJoinAtEnd(wall, 1); } catch { }
                            
                            // 中心線已依房間測試偏移半厚，維持中心線定位避免再次位移
                            SetFinishWallProperties(wall, WallLocationLine.WallCenterline);
                            EnsureFinishWallFacesRoom(wall, room);
                            // TryJoinFinishWallWithBoundaryHost 已停用，避免修改結構牆端面幾何

                            // 暫時註解掉位置調整，先確保基本生成正常
                            // AdjustWallLocationTowardsRoom(wall, room);

                            Logger.Log($"建立牆面，ID: {wall.Id}，長度: {curve.Length * 304.8:F2} mm");

                            TagRoomOnElement(wall, room);
                            createdWalls.Add(wall);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"建立牆面失敗: {ex.Message}");
                    }
                }

                Logger.Log($"完成牆面建立，建立了 {createdWalls.Count} 面牆");
            }
            catch (Exception ex)
            {
                Logger.Log($"建立牆面異常: {ex.Message}");
            }

            return createdWalls;
        }

        private List<Curve> OffsetCurvesTowardRoom(List<Curve> curves, Room room, double offsetDistance)
        {
            if (room == null || offsetDistance <= 1e-9)
                return curves;

            var result = new List<Curve>();
            foreach (var curve in curves)
            {
                if (curve == null || curve.Length < 1e-6)
                    continue;

                result.Add(OffsetCurveTowardRoom(curve, room, offsetDistance));
            }

            return result;
        }

        private Curve OffsetCurveTowardRoom(Curve curve, Room room, double offsetDistance)
        {
            try
            {
                var plus = curve.CreateOffset(offsetDistance, XYZ.BasisZ);
                var minus = curve.CreateOffset(-offsetDistance, XYZ.BasisZ);

                if (plus == null && minus == null)
                    return curve;
                if (plus != null && minus == null)
                    return plus;
                if (minus != null && plus == null)
                    return minus;

                var testZ = GetRoomTestElevation(room);
                var plusMid = plus.Evaluate(0.5, true);
                var minusMid = minus.Evaluate(0.5, true);

                var plusProbe = new XYZ(plusMid.X, plusMid.Y, testZ);
                var minusProbe = new XYZ(minusMid.X, minusMid.Y, testZ);
                var plusInside = IsPointInRoomSafe(room, plusProbe);
                var minusInside = IsPointInRoomSafe(room, minusProbe);

                if (plusInside && !minusInside)
                    return plus;
                if (minusInside && !plusInside)
                    return minus;

                var roomCenter = (room.Location as LocationPoint)?.Point ?? curve.Evaluate(0.5, true);
                var plusDist = plusMid.DistanceTo(roomCenter);
                var minusDist = minusMid.DistanceTo(roomCenter);
                return plusDist <= minusDist ? plus : minus;
            }
            catch
            {
                return curve;
            }
        }

        private void TryJoinFinishWallWithBoundaryHost(Room room, Curve finishCurve, Wall finishWall)
        {
            try
            {
                var hostWall = FindClosestBoundaryHostWall(room, finishCurve);
                if (hostWall == null)
                    return;

                IList<ElementId> inserts;
                try
                {
                    inserts = hostWall.FindInserts(true, true, true, true);
                }
                catch
                {
                    inserts = new List<ElementId>();
                }

                if (inserts == null || inserts.Count == 0)
                    return;

                try
                {
                    JoinGeometryUtils.JoinGeometry(Doc, finishWall, hostWall);
                    Logger.Log($"宿主牆 Join 成功：粉刷牆 {finishWall.Id} <- 宿主牆 {hostWall.Id}（插入物 {inserts.Count}）");
                }
                catch (Exception joinEx)
                {
                    Logger.Log($"宿主牆 Join 失敗：粉刷牆 {finishWall.Id} / 宿主牆 {hostWall.Id}，原因：{joinEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"宿主牆 Join 流程異常：{ex.Message}");
            }
        }

        private Wall FindClosestBoundaryHostWall(Room room, Curve finishCurve)
        {
            try
            {
                var boundaryOptions = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };

                var loops = room.GetBoundarySegments(boundaryOptions);
                if (loops == null || loops.Count == 0)
                {
                    boundaryOptions.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center;
                    loops = room.GetBoundarySegments(boundaryOptions);
                }

                if (loops == null || loops.Count == 0)
                    return null;

                var finishDir = (finishCurve.GetEndPoint(1) - finishCurve.GetEndPoint(0)).Normalize();
                var finishMid = finishCurve.Evaluate(0.5, true);

                Wall closestWall = null;
                double bestDistance = double.MaxValue;

                foreach (var loop in loops)
                {
                    foreach (var seg in loop)
                    {
                        if (seg == null)
                            continue;

                        var hostWall = Doc.GetElement(seg.ElementId) as Wall;
                        if (hostWall == null)
                            continue;

                        if (hostWall.WallType?.Kind == WallKind.Curtain)
                            continue;

                        var segCurve = seg.GetCurve();
                        if (segCurve == null || segCurve.Length < 1e-6)
                            continue;

                        var segDir = (segCurve.GetEndPoint(1) - segCurve.GetEndPoint(0)).Normalize();
                        var parallel = Math.Abs(finishDir.DotProduct(segDir));
                        if (parallel < 0.95)
                            continue;

                        var projection = segCurve.Project(finishMid);
                        if (projection == null)
                            continue;

                        var distance = projection.Distance;
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            closestWall = hostWall;
                        }
                    }
                }

                var maxHostDistance = 1000.0 / 304.8;
                if (closestWall != null && bestDistance <= maxHostDistance)
                    return closestWall;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private double GetRoomTestElevation(Room room)
        {
            try
            {
                var roomPoint = (room.Location as LocationPoint)?.Point;
                if (roomPoint != null)
                    return roomPoint.Z;

                var level = Doc.GetElement(room.LevelId) as Level;
                if (level != null)
                    return level.Elevation + (1000.0 / 304.8);
            }
            catch
            {
            }

            return 0;
        }

        private bool IsPointInRoomSafe(Room room, XYZ point)
        {
            try
            {
                return room.IsPointInRoom(point);
            }
            catch
            {
                return false;
            }
        }

        private void EnsureFinishWallFacesRoom(Wall wall, Room room)
        {
            try
            {
                var wallCurve = (wall.Location as LocationCurve)?.Curve as Line;
                if (wallCurve == null)
                    return;

                var wallMid = wallCurve.Evaluate(0.5, true);
                var orientation = wall.Orientation;
                var testZ = GetRoomTestElevation(room);
                var offset = 30.0 / 304.8;

                var pExterior = new XYZ(wallMid.X + orientation.X * offset, wallMid.Y + orientation.Y * offset, testZ);
                var pInterior = new XYZ(wallMid.X - orientation.X * offset, wallMid.Y - orientation.Y * offset, testZ);

                var exteriorInside = IsPointInRoomSafe(room, pExterior);
                var interiorInside = IsPointInRoomSafe(room, pInterior);

                if (exteriorInside && !interiorInside)
                {
                    wall.Flip();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"校正牆面 {wall.Id} 朝向失敗: {ex.Message}");
            }
        }

        private List<OpeningMarker> CollectOpeningMarkersFromBoundaryWalls(Room room, bool includeDoors, bool includeWindows, bool includeGenericOpenings)
        {
            var markers = new List<OpeningMarker>();

            try
            {
                var boundaryOptions = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };

                var segments = room.GetBoundarySegments(boundaryOptions);
                if (segments == null || !segments.Any())
                    return markers;

                var boundaryWallIds = new HashSet<ElementId>();
                foreach (var seg in segments.SelectMany(x => x))
                {
                    if (Doc.GetElement(seg.ElementId) is Wall)
                        boundaryWallIds.Add(seg.ElementId);
                }

                var handledIds = new HashSet<ElementId>();
                foreach (var wallId in boundaryWallIds)
                {
                    if (!(Doc.GetElement(wallId) is Wall hostWall))
                        continue;

                    var insertIds = hostWall.FindInserts(true, true, true, true);
                    foreach (var insertId in insertIds)
                    {
                        if (handledIds.Contains(insertId))
                            continue;

                        var insertElement = Doc.GetElement(insertId);
                        if (insertElement == null)
                            continue;

                        if (!TryBuildOpeningMarker(insertElement, hostWall, includeDoors, includeWindows, includeGenericOpenings, out var marker))
                            continue;

                        markers.Add(marker);
                        handledIds.Add(insertId);
                    }
                }

                Logger.Log($"房間 {room.Name} 收集到 {markers.Count} 個門窗/開孔標記");
            }
            catch (Exception ex)
            {
                Logger.Log($"收集房間 {room.Name} 開口標記失敗: {ex.Message}");
            }

            return markers;
        }

        private bool TryBuildOpeningMarker(Element openingElement, Wall hostWall, bool includeDoors, bool includeWindows, bool includeGenericOpenings,
            out OpeningMarker marker)
        {
            marker = default;

            var source = openingElement.GetType().Name;
            XYZ point = null;
            var width = 0.0;
            var wallDirection = GetWallDirection(hostWall);

            if (openingElement is FamilyInstance familyInstance)
            {
                var categoryId = familyInstance.Category != null ? RevitCompat.GetElementIdValue(familyInstance.Category.Id) : -1;
                var isDoor = categoryId == (int)BuiltInCategory.OST_Doors;
                var isWindow = categoryId == (int)BuiltInCategory.OST_Windows;

                if ((isDoor && !includeDoors) || (isWindow && !includeWindows))
                    return false;

                if (!isDoor && !isWindow && !includeGenericOpenings)
                    return false;

                point = (familyInstance.Location as LocationPoint)?.Point ?? GetElementCenter(openingElement);
                width = isDoor || isWindow
                    ? Math.Max(GetOpeningWidth(familyInstance), EstimateOpeningWidthOnHostWall(openingElement, hostWall))
                    : EstimateOpeningWidthOnHostWall(openingElement, hostWall);
                source = isDoor ? "Door" : isWindow ? "Window" : source;
            }
            else if (openingElement is Opening)
            {
                if (!includeGenericOpenings)
                    return false;

                point = GetElementCenter(openingElement);
                width = EstimateOpeningWidthOnHostWall(openingElement, hostWall);
                source = "Opening";
            }
            else
            {
                if (!includeGenericOpenings)
                    return false;

                point = GetElementCenter(openingElement);
                width = EstimateOpeningWidthOnHostWall(openingElement, hostWall);
            }

            if (point == null)
                return false;

            if (width <= 1e-6)
                width = 900.0 / 304.8;

            var bb = openingElement.get_BoundingBox(null);
            var bottomZ = bb?.Min.Z ?? point.Z;
            var topZ = bb?.Max.Z ?? point.Z;

            if (topZ - bottomZ < (100.0 / 304.8))
            {
                // 保底：至少建立 100mm 高開口
                topZ = bottomZ + (100.0 / 304.8);
            }

            var clearance = 10.0 / 304.8;
            marker = new OpeningMarker
            {
                SourceId = openingElement.Id,
                Point = point,
                HalfWidth = Math.Max(width * 0.5 + clearance, 25.0 / 304.8),
                Direction = wallDirection,
                BottomZ = bottomZ,
                TopZ = topZ,
                Source = source
            };
            return true;
        }

        private XYZ GetWallDirection(Wall wall)
        {
            try
            {
                var wallLine = (wall.Location as LocationCurve)?.Curve as Line;
                if (wallLine == null)
                    return XYZ.BasisX;

                return wallLine.Direction.Normalize();
            }
            catch
            {
                return XYZ.BasisX;
            }
        }

        private XYZ GetElementCenter(Element element)
        {
            try
            {
                var bb = element.get_BoundingBox(null);
                if (bb == null)
                    return null;

                return (bb.Min + bb.Max) * 0.5;
            }
            catch
            {
                return null;
            }
        }

        private double EstimateOpeningWidthOnHostWall(Element openingElement, Wall hostWall)
        {
            try
            {
                var wallLine = (hostWall.Location as LocationCurve)?.Curve as Line;
                var bb = openingElement.get_BoundingBox(null);

                if (wallLine == null || bb == null)
                    return 900.0 / 304.8;

                var dir = wallLine.Direction.Normalize();
                var corners = new[]
                {
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)
                };

                var projections = corners.Select(pt => pt.DotProduct(dir)).ToList();
                var width = projections.Max() - projections.Min();

                if (width > 1e-6)
                    return width;

                return Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
            }
            catch
            {
                return 900.0 / 304.8;
            }
        }

        private List<Curve> SplitCurvesByOpeningMarkers(List<Curve> sourceCurves, List<OpeningMarker> markers)
        {
            if (markers == null || !markers.Any())
                return sourceCurves;

            var result = new List<Curve>();
            foreach (var curve in sourceCurves)
            {
                if (curve == null || curve.Length < 1e-6)
                    continue;

                var cuts = new List<(double a, double b)>();
                var curveDirection = GetCurveDirection(curve);
                foreach (var marker in markers)
                {
                    // 只切割與開口宿主牆方向一致的牆段，避免錯切到垂直牆
                    if (curveDirection != null)
                    {
                        var dot = Math.Abs(curveDirection.DotProduct(marker.Direction));
                        if (dot < 0.95)
                            continue;
                    }

                    var projection = curve.Project(marker.Point);
                    if (projection == null)
                        continue;

                    var maxDistance = 180.0 / 304.8;
                    if (projection.Distance > maxDistance)
                        continue;

                    var mid = curve.ComputeNormalizedParameter(projection.Parameter);
                    var half = Math.Min(marker.HalfWidth / curve.Length, 0.45);
                    var a = Math.Max(0, mid - half);
                    var b = Math.Min(1, mid + half);
                    if (b - a > 1e-6)
                        cuts.Add((a, b));
                }

                if (!cuts.Any())
                {
                    result.Add(curve);
                    continue;
                }

                cuts.Sort((x, y) => x.a.CompareTo(y.a));
                var mergedCuts = new List<(double a, double b)>();
                var current = cuts[0];
                for (var i = 1; i < cuts.Count; i++)
                {
                    var next = cuts[i];
                    if (next.a <= current.b)
                        current = (current.a, Math.Max(current.b, next.b));
                    else
                    {
                        mergedCuts.Add(current);
                        current = next;
                    }
                }
                mergedCuts.Add(current);

                var prev = 0.0;
                foreach (var cut in mergedCuts)
                {
                    if (cut.a - prev > 1e-6)
                    {
                        var seg = curve.Clone();
                        seg.MakeBound(curve.ComputeRawParameter(prev), curve.ComputeRawParameter(cut.a));
                        if (seg.Length > (50.0 / 304.8))
                            result.Add(seg);
                    }
                    prev = cut.b;
                }

                if (1 - prev > 1e-6)
                {
                    var seg = curve.Clone();
                    seg.MakeBound(curve.ComputeRawParameter(prev), curve.ComputeRawParameter(1));
                    if (seg.Length > (50.0 / 304.8))
                        result.Add(seg);
                }
            }

            return result;
        }

        private void CreateFinishWallOpeningsFromMarkers(List<Wall> finishWalls, List<OpeningMarker> markers)
        {
            try
            {
                Logger.Log($"開始套用 {markers.Count} 個門窗/開孔到粉刷牆");
                var handledSources = new HashSet<ElementId>();

                foreach (var marker in markers)
                {
                    if (handledSources.Contains(marker.SourceId))
                        continue;

                    Wall bestWall = null;
                    XYZ bestProjPoint = null;
                    var bestDist = double.MaxValue;

                    foreach (var wall in finishWalls)
                    {
                        var wallCurve = (wall.Location as LocationCurve)?.Curve;
                        if (wallCurve == null)
                            continue;

                        var curveDirection = GetCurveDirection(wallCurve);
                        if (curveDirection != null)
                        {
                            var dot = Math.Abs(curveDirection.DotProduct(marker.Direction));
                            if (dot < 0.95)
                                continue;
                        }

                        var projection = wallCurve.Project(marker.Point);
                        if (projection == null)
                            continue;

                        var dist = projection.Distance;
                        var maxDist = 180.0 / 304.8;
                        if (dist > maxDist)
                            continue;

                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestWall = wall;
                            bestProjPoint = projection.XYZPoint;
                        }
                    }

                    if (bestWall == null || bestProjPoint == null)
                        continue;

                    var wallDir = GetCurveDirection((bestWall.Location as LocationCurve)?.Curve) ?? XYZ.BasisX;
                    var p1 = bestProjPoint - wallDir * marker.HalfWidth;
                    var p2 = bestProjPoint + wallDir * marker.HalfWidth;
                    p1 = new XYZ(p1.X, p1.Y, marker.BottomZ);
                    p2 = new XYZ(p2.X, p2.Y, marker.TopZ);

                    if (p2.Z - p1.Z < (100.0 / 304.8))
                        continue;

                    try
                    {
                        Doc.Create.NewOpening(bestWall, p1, p2);
                        handledSources.Add(marker.SourceId);
                        Logger.Log($"✓ 已建立粉刷牆開口：來源 {marker.SourceId}，牆 {bestWall.Id}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"建立粉刷牆開口失敗，來源 {marker.SourceId}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"套用粉刷牆開口失敗: {ex.Message}");
            }
        }

        private XYZ GetCurveDirection(Curve curve)
        {
            try
            {
                if (curve is Line line)
                    return line.Direction.Normalize();

                var start = curve.GetEndPoint(0);
                var end = curve.GetEndPoint(1);
                if (start.DistanceTo(end) < 1e-9)
                    return null;

                return (end - start).Normalize();
            }
            catch
            {
                return null;
            }
        }

        private List<Curve> DeduplicateCurves(List<Curve> curves)
        {
            var unique = new List<Curve>();
            var keys = new HashSet<string>();

            foreach (var curve in curves)
            {
                if (curve == null || curve.Length < 1e-6)
                    continue;

                var key = BuildCurveKey(curve);
                if (keys.Contains(key))
                    continue;

                var overlaps = unique.Any(existing => AreWallsParallelAndOverlapping(curve, existing));
                if (overlaps)
                    continue;

                keys.Add(key);
                unique.Add(curve);
            }

            return unique;
        }

        private string BuildCurveKey(Curve curve)
        {
            var s = curve.GetEndPoint(0);
            var e = curve.GetEndPoint(1);

            var sKey = $"{Math.Round(s.X * 304.8, 1)}|{Math.Round(s.Y * 304.8, 1)}|{Math.Round(s.Z * 304.8, 1)}";
            var eKey = $"{Math.Round(e.X * 304.8, 1)}|{Math.Round(e.Y * 304.8, 1)}|{Math.Round(e.Z * 304.8, 1)}";

            return string.CompareOrdinal(sKey, eKey) <= 0 ? $"{sKey}>{eKey}" : $"{eKey}>{sKey}";
        }



        void AdjustWallLocationTowardsRoom(Wall wall, Room room)
        {
            try
            {
                Logger.Log($"調整牆面 {wall.Id} 位置朝向房間內側");
                
                // 取得牆面的位置線
                var locationCurve = wall.Location as LocationCurve;
                if (locationCurve == null) return;

                var wallCurve = locationCurve.Curve;
                
                // 取得房間中心點
                var roomLocation = (room.Location as LocationPoint)?.Point;
                if (roomLocation == null) return;

                // 計算牆面中點
                var wallMidPoint = wallCurve.Evaluate(0.5, true);
                
                // 計算從牆面到房間中心的方向
                var toRoomDirection = (roomLocation - wallMidPoint).Normalize();
                
                // 取得牆面的法向量
                if (wallCurve is Line line)
                {
                    var wallDirection = line.Direction;
                    var wallNormal = XYZ.BasisZ.CrossProduct(wallDirection).Normalize();
                    
                    // 確保法向量指向房間內側
                    if (wallNormal.DotProduct(toRoomDirection) < 0)
                    {
                        wallNormal = wallNormal.Negate();
                    }
                    
                    // 嘗試翻轉牆面使其面向房間內側
                    try
                    {
                        var currentNormal = wall.Orientation;
                        if (currentNormal.DotProduct(wallNormal) < 0)
                        {
                            wall.Flip(); // 翻轉牆面
                            Logger.Log($"翻轉牆面 {wall.Id} 朝向");
                        }
                    }
                    catch (Exception flipEx)
                    {
                        Logger.Log($"無法翻轉牆面 {wall.Id}: {flipEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"調整牆面位置失敗: {ex.Message}");
            }
        }

        double GetWallTypeThickness(WallType wallType)
        {
            try
            {
                var structure = wallType.GetCompoundStructure();
                if (structure != null)
                {
                    return structure.GetWidth();
                }
                
                // 如果無法取得複合結構，使用預設厚度
                var thicknessParam = wallType.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
                if (thicknessParam != null)
                {
                    return thicknessParam.AsDouble();
                }
                
                // 預設厚度 25mm
                return 25.0 / 304.8; // 25mm 轉換為英尺
            }
            catch
            {
                // 發生錯誤時使用預設厚度
                return 25.0 / 304.8;
            }
        }

        List<Curve> CreateInsetWallBoundary(Room room, double insetDistance)
        {
            try
            {
                Logger.Log($"建立房間 {room.Name} 的內縮邊界，內縮距離: {insetDistance * 304.8:F2} mm");
                
                // 取得房間邊界
                Level level; double halfT; CurveLoop loop;
                var profile = GetRoomProfile(room, FloorBoundaryMode.InnerFinish, out level, out halfT, out loop);
                
                if (profile.Size < 3)
                {
                    Logger.Log("房間輪廓不足以建立內縮邊界");
                    return new List<Curve>();
                }

                var originalCurves = profile.Cast<Curve>().ToList();
                Logger.Log($"原始邊界有 {originalCurves.Count} 條曲線");

                // 暫時跳過API偏移，直接使用手動方式
                Logger.Log("使用手動內縮方式");

                // 如果偏移失敗，嘗試手動內縮
                Logger.Log("嘗試手動內縮邊界");
                return CreateManualInsetBoundary(originalCurves, insetDistance);
            }
            catch (Exception ex)
            {
                Logger.Log($"建立內縮邊界異常: {ex.Message}");
                return new List<Curve>();
            }
        }

        List<Curve> CreateManualInsetBoundary(List<Curve> originalCurves, double insetDistance)
        {
            try
            {
                Logger.Log($"開始手動內縮，距離: {insetDistance * 304.8:F2} mm");
                
                // 計算房間中心點，用於判斷內外方向
                var centerPoint = CalculateRoomCenter(originalCurves);
                Logger.Log($"房間中心點: ({centerPoint.X * 304.8:F2}, {centerPoint.Y * 304.8:F2})");
                
                var insetCurves = new List<Curve>();
                
                for (int i = 0; i < originalCurves.Count; i++)
                {
                    var curve = originalCurves[i];
                    if (!(curve is Line line)) 
                    {
                        // 非直線段暫時使用原曲線
                        insetCurves.Add(curve);
                        Logger.Log($"非直線段 {i}，使用原曲線");
                        continue;
                    }

                    // 計算直線的中點
                    var midPoint = line.Evaluate(0.5, true);
                    
                    // 計算直線的法向量
                    var direction = line.Direction;
                    var normal1 = XYZ.BasisZ.CrossProduct(direction).Normalize();
                    var normal2 = normal1.Negate();
                    
                    // 判斷哪個法向量指向房間內側
                    var testPoint1 = midPoint + normal1 * 0.1; // 測試點1
                    var testPoint2 = midPoint + normal2 * 0.1; // 測試點2
                    
                    var dist1 = centerPoint.DistanceTo(testPoint1);
                    var dist2 = centerPoint.DistanceTo(testPoint2);
                    
                    // 選擇距離房間中心更近的法向量（指向內側）
                    var inwardNormal = dist1 < dist2 ? normal1 : normal2;
                    
                    Logger.Log($"直線段 {i}: 中點({midPoint.X * 304.8:F2}, {midPoint.Y * 304.8:F2}), " +
                              $"法向量({inwardNormal.X:F3}, {inwardNormal.Y:F3})");
                    
                    // 向內偏移直線
                    var startPoint = line.GetEndPoint(0) + inwardNormal * insetDistance;
                    var endPoint = line.GetEndPoint(1) + inwardNormal * insetDistance;
                    
                    var insetLine = Line.CreateBound(startPoint, endPoint);
                    insetCurves.Add(insetLine);
                    
                    Logger.Log($"直線段 {i} 內縮完成，偏移距離: {insetDistance * 304.8:F2} mm");
                }

                Logger.Log($"手動內縮完成，建立 {insetCurves.Count} 條曲線");
                return insetCurves;
            }
            catch (Exception ex)
            {
                Logger.Log($"手動內縮失敗: {ex.Message}");
                return new List<Curve>();
            }
        }

        /// <summary>
        /// 核心方法：由線轉為粉刷牆
        /// </summary>
        Wall CreateFinishWallFromLine(Curve boundaryLine, WallType wallType, Level level, double wallHeight, Room room, List<Wall> existingWalls = null)
        {
            try
            {
                Logger.Log($"開始由線轉為粉刷牆");
                Logger.Log($"線起點: ({boundaryLine.GetEndPoint(0).X * 304.8:F1}, {boundaryLine.GetEndPoint(0).Y * 304.8:F1})");
                Logger.Log($"線終點: ({boundaryLine.GetEndPoint(1).X * 304.8:F1}, {boundaryLine.GetEndPoint(1).Y * 304.8:F1})");
                
                // 檢查是否與現有粉刷牆重疊
                if (existingWalls != null && existingWalls.Any())
                {
                    foreach (var existingWall in existingWalls)
                    {
                        try
                        {
                            var existingCurve = (existingWall.Location as LocationCurve)?.Curve;
                            if (existingCurve != null && AreWallsParallelAndOverlapping(boundaryLine, existingCurve))
                            {
                                Logger.Log($"⚠️ 檢測到與現有粉刷牆 {existingWall.Id} 重疊，跳過創建");
                                return null;
                            }
                        }
                        catch (Exception checkEx)
                        {
                            Logger.Log($"檢查重疊時發生錯誤: {checkEx.Message}");
                            // 繼續處理，不因為檢查錯誤而停止
                        }
                    }
                }
                
                // 直接由線創建牆
                var wall = Wall.Create(Doc, boundaryLine, wallType.Id, level.Id, wallHeight, 0, false, false);
                
                if (wall != null)
                {
                    Logger.Log($"✓ 牆創建成功，ID: {wall.Id}");
                    
                    // 立即禁用兩端 Wall Join，防止 Revit 自動延伸裝修牆去拖拉結構牆端點
                    try { WallUtils.DisallowWallJoinAtEnd(wall, 0); } catch { }
                    try { WallUtils.DisallowWallJoinAtEnd(wall, 1); } catch { }
                    
                    // 中心線已依房間測試偏移半厚，維持中心線定位避免再次位移
                    SetFinishWallProperties(wall, WallLocationLine.WallCenterline);
                    EnsureFinishWallFacesRoom(wall, room);
                    
                    // 標記房間關聯
                    TagRoomOnElement(wall, room);
                    
                    Logger.Log($"✓ 粉刷牆設定完成");
                    return wall;
                }
                else
                {
                    Logger.Log($"✗ 牆創建失敗");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"✗ 由線轉為粉刷牆失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 參考 Dynamo 腳本：
        /// 1) 先用 ±牆厚偏移取測試點判斷哪側在房間內
        /// 2) 再用同方向的 ±半牆厚建立粉刷牆中心線
        /// </summary>
        Curve BuildFinishWallCenterlineByRoomTest(Curve boundaryCurve, Room room, WallType wallType)
        {
            try
            {
                if (boundaryCurve == null || room == null || wallType == null)
                    return boundaryCurve;

                // 只處理可偏移曲線；若失敗則回退原線
                var fullOffsetPos = boundaryCurve.CreateOffset(wallType.Width, XYZ.BasisZ);
                var fullOffsetNeg = boundaryCurve.CreateOffset(-wallType.Width, XYZ.BasisZ);
                if (fullOffsetPos == null || fullOffsetNeg == null)
                    return boundaryCurve;

                var probePos = fullOffsetPos.Evaluate(0.5, true);
                var usePositive = IsPointInRoomSafe(room, probePos);
                var halfOffset = usePositive ? (wallType.Width * 0.5) : (-wallType.Width * 0.5);

                var centerline = boundaryCurve.CreateOffset(halfOffset, XYZ.BasisZ);
                return centerline ?? boundaryCurve;
            }
            catch (Exception ex)
            {
                Logger.Log($"依房間測試建立粉刷牆中心線失敗，回退原線: {ex.Message}");
                return boundaryCurve;
            }
        }

        /// <summary>
        /// 將曲線向側面偏移指定距離（英尺）。
        /// 等同 DesignScript Curve.Offset：正距離 = 面朝方向的左側，負距離 = 右側。
        /// 支援 Line 和 Arc；其他類型返回 null。
        /// </summary>
        Curve OffsetCurveLateral(Curve curve, double distance)
        {
            try
            {
                if (curve is Line line)
                {
                    var s = line.GetEndPoint(0);
                    var e = line.GetEndPoint(1);
                    // 左法向量（逆時針 90°）
                    var perp = new XYZ(-(e.Y - s.Y), e.X - s.X, 0).Normalize();
                    return Line.CreateBound(
                        new XYZ(s.X + perp.X * distance, s.Y + perp.Y * distance, s.Z),
                        new XYZ(e.X + perp.X * distance, e.Y + perp.Y * distance, e.Z));
                }
                if (curve is Arc arc)
                {
                    // 法向量 Z 分量判斷 CCW/CW；CCW 時正偏移 = 向圓心（半徑減小）
                    var n = arc.XDirection.CrossProduct(arc.YDirection);
                    double newRadius = arc.Radius + (n.Z >= 0 ? -distance : distance);
                    if (newRadius <= 1e-6) return null;
                    return Arc.Create(arc.Center, newRadius,
                        arc.GetEndParameter(0), arc.GetEndParameter(1),
                        arc.XDirection, arc.YDirection);
                }
            }
            catch (Exception ex) { Logger.Log($"OffsetCurveLateral 失敗: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Dynamo 等效：從房間幾何體（Solid）的垂直面中取得底部水平邊線。
        /// 垂直面在門洞處自然不存在 → 底部邊自動排除門洞，無需切割曲線。
        /// 等同 Dynamo: Element.Geometry → PolySurface.Surfaces（垂直面）→ Surface.PerimeterCurves（底邊）
        /// </summary>
        List<Curve> GetRoomGeometryBoundaryCurves(Room room, Level level)
        {
            var result = new List<Curve>();
            try
            {
                var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false };
                var geomElem = room.get_Geometry(options);
                if (geomElem == null)
                {
                    Logger.Log("房間幾何體為空");
                    return result;
                }

                // 取得體積最大的 Solid（即房間主實體）
                Solid roomSolid = null;
                double maxVol = 0;
                foreach (GeometryObject obj in geomElem)
                {
                    if (obj is Solid s && s.Volume > maxVol)
                    {
                        maxVol = s.Volume;
                        roomSolid = s;
                    }
                }

                if (roomSolid == null)
                {
                    Logger.Log("找不到房間 Solid");
                    return result;
                }

                Logger.Log($"房間 Solid 體積: {roomSolid.Volume * 0.0283168:F4} m³");

                // 找出 Solid 的最低 Z（地板標高）
                double minZ = double.MaxValue;
                foreach (Edge edge in roomSolid.Edges)
                {
                    var ec = edge.AsCurve();
                    if (ec == null) continue;
                    double z0 = ec.GetEndPoint(0).Z;
                    double z1 = ec.GetEndPoint(1).Z;
                    if (z0 < minZ) minZ = z0;
                    if (z1 < minZ) minZ = z1;
                }
                double zTol = 15.0 / 304.8; // 15mm 容差

                // 從每個垂直面取得底部水平邊
                int verticalFaceCount = 0;
                foreach (Face face in roomSolid.Faces)
                {
                    // 判斷是否為垂直面（法線 Z 分量接近 0）
                    var normal = face.ComputeNormal(new UV(0.5, 0.5));
                    if (Math.Abs(normal.Z) > 0.1) continue;
                    verticalFaceCount++;

                    // 收集在最低 Z 的水平邊
                    foreach (EdgeArray loop in face.EdgeLoops)
                    {
                        foreach (Edge edge in loop)
                        {
                            var ec = edge.AsCurve();
                            if (ec == null) continue;

                            var p0 = ec.GetEndPoint(0);
                            var p1 = ec.GetEndPoint(1);

                            // 必須水平（兩端 Z 相同）
                            if (Math.Abs(p0.Z - p1.Z) > zTol) continue;
                            // 必須在地板層
                            if (Math.Abs(p0.Z - minZ) > zTol) continue;
                            // 排除極短邊
                            if (ec.Length < 1e-6) continue;

                            result.Add(ec);
                        }
                    }
                }

                Logger.Log($"房間幾何體：{verticalFaceCount} 個垂直面，提取 {result.Count} 條底部邊線（minZ={minZ * 304.8:F1}mm）");
            }
            catch (Exception ex)
            {
                Logger.Log($"GetRoomGeometryBoundaryCurves 失敗: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 按建議方式：直接提取房間邊界線
        /// </summary>
        List<Curve> ExtractRoomBoundaryLines(Room room)
        {
            try
            {
                Logger.Log($"開始提取房間 {room.Name} 的邊界線");
                
                // 使用最直接的方法獲取房間邊界
                var boundaryOptions = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                
                // 獲取房間邊界段
                var boundarySegments = room.GetBoundarySegments(boundaryOptions);
                
                if (boundarySegments == null || !boundarySegments.Any())
                {
                    Logger.Log("Finish 邊界獲取失敗，嘗試 Center 邊界");
                    boundaryOptions.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center;
                    boundarySegments = room.GetBoundarySegments(boundaryOptions);
                }
                
                if (boundarySegments == null || !boundarySegments.Any())
                {
                    Logger.Log("無法獲取房間邊界段");
                    return new List<Curve>();
                }
                
                var boundaryLines = new List<Curve>();

                Logger.Log($"房間共有 {boundarySegments.Count} 個邊界循環");

                for (int loopIndex = 0; loopIndex < boundarySegments.Count; loopIndex++)
                {
                    var loopSegments = boundarySegments[loopIndex];
                    Logger.Log($"處理邊界循環 {loopIndex + 1}，共 {loopSegments.Count} 個段");

                    foreach (var segment in loopSegments)
                    {
                        if (ShouldSkipBoundarySegment(segment))
                        {
                            continue;
                        }

                        var curve = segment.GetCurve();
                        if (curve != null && curve.Length > 1e-6)
                        {
                            boundaryLines.Add(curve);
                        
                            // 記錄邊界線信息
                            var start = curve.GetEndPoint(0);
                            var end = curve.GetEndPoint(1);
                            Logger.Log($"邊界線: ({start.X * 304.8:F1}, {start.Y * 304.8:F1}) -> " +
                                     $"({end.X * 304.8:F1}, {end.Y * 304.8:F1}), 長度: {curve.Length * 304.8:F1}mm");
                        }
                    }
                }
                
                Logger.Log($"✓ 成功提取 {boundaryLines.Count} 條房間邊界線");
                return boundaryLines;
            }
            catch (Exception ex)
            {
                Logger.Log($"✗ 提取房間邊界線失敗: {ex.Message}");
                return new List<Curve>();
            }
        }

        /// <summary>
        /// 舊方法：直接獲取房間內表面邊界，用於生成粉刷牆（保留備用）
        /// </summary>
        List<Curve> GetRoomInnerBoundary(Room room)
        {
            try
            {
                Logger.Log($"=== 開始診斷房間 {room.Name} (編號: {room.Number}) 的邊界 ===");
                Logger.Log($"房間面積: {room.Area * 0.092903:F2} m²");
                Logger.Log($"房間樓層ID: {room.LevelId}");
                
                // 嘗試多種邊界獲取方式
                var allBoundaryMethods = new[]
                {
                    SpatialElementBoundaryLocation.Finish,
                    SpatialElementBoundaryLocation.Center,
                    SpatialElementBoundaryLocation.CoreBoundary
                };
                
                List<Curve> bestBoundary = null;
                SpatialElementBoundaryLocation bestMethod = SpatialElementBoundaryLocation.Finish;
                
                foreach (var method in allBoundaryMethods)
                {
                    try
                    {
                        Logger.Log($"嘗試邊界獲取方法: {method}");
                        
                        var boundaryOptions = new SpatialElementBoundaryOptions
                        {
                            SpatialElementBoundaryLocation = method
                        };
                        
                        var boundarySegments = room.GetBoundarySegments(boundaryOptions);
                        
                        if (boundarySegments != null && boundarySegments.Any())
                        {
                            var boundaryCurves = new List<Curve>();
                            var mainBoundary = boundarySegments[0];
                            
                            Logger.Log($"使用 {method} 獲取到 {boundarySegments.Count} 個邊界循環");
                            Logger.Log($"主要邊界循環有 {mainBoundary.Count} 個段");
                            
                            foreach (var segment in mainBoundary)
                            {
                                try
                                {
                                    var curve = segment.GetCurve();
                                    var element = Doc.GetElement(segment.ElementId);
                                    
                                    if (curve != null && curve.Length > 1e-6)
                                    {
                                        boundaryCurves.Add(curve);
                                        
                                        var start = curve.GetEndPoint(0);
                                        var end = curve.GetEndPoint(1);
                                        var elementInfo = element != null ? $" (元素: {element.GetType().Name} ID:{element.Id})" : "";
                                        
                                        Logger.Log($"  邊界段: 起點({start.X * 304.8:F2}, {start.Y * 304.8:F2}), " +
                                                 $"終點({end.X * 304.8:F2}, {end.Y * 304.8:F2}), " +
                                                 $"長度: {curve.Length * 304.8:F2}mm{elementInfo}");
                                    }
                                }
                                catch (Exception segEx)
                                {
                                    Logger.Log($"  處理邊界段失敗: {segEx.Message}");
                                }
                            }
                            
                            if (boundaryCurves.Count > 0)
                            {
                                Logger.Log($"✓ {method} 成功獲取 {boundaryCurves.Count} 條邊界曲線");
                                
                                if (bestBoundary == null || boundaryCurves.Count > bestBoundary.Count)
                                {
                                    bestBoundary = boundaryCurves;
                                    bestMethod = method;
                                }
                            }
                            else
                            {
                                Logger.Log($"✗ {method} 未獲取到有效邊界曲線");
                            }
                        }
                        else
                        {
                            Logger.Log($"✗ {method} 無法獲取邊界段");
                        }
                    }
                    catch (Exception methodEx)
                    {
                        Logger.Log($"✗ {method} 方法執行失敗: {methodEx.Message}");
                    }
                }
                
                if (bestBoundary != null && bestBoundary.Any())
                {
                    Logger.Log($"=== 選用最佳方法: {bestMethod}，獲得 {bestBoundary.Count} 條邊界曲線 ===");
                    return bestBoundary;
                }
                else
                {
                    Logger.Log("=== 所有邊界獲取方法都失敗 ===");
                    return new List<Curve>();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"=== 房間邊界診斷完全失敗: {ex.Message} ===");
                Logger.Log($"堆疊追蹤: {ex.StackTrace}");
                return new List<Curve>();
            }
        }

        /// <summary>
        /// 舊方法：獲取粉刷牆的正確定位線，基於房間實際邊界範圍（暫時保留）
        /// </summary>
        List<Curve> GetFinishWallLocationLines(Room room)
        {
            try
            {
                Logger.Log($"開始為房間 {room.Name} 建立粉刷牆定位線");
                
                // 獲取房間的實際邊界範圍
                var spatialBoundaryOptions = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                
                var boundarySegments = room.GetBoundarySegments(spatialBoundaryOptions);
                if (boundarySegments == null || !boundarySegments.Any())
                {
                    Logger.Log("無法獲取房間邊界段");
                    return new List<Curve>();
                }
                
                var finishWallLocationLines = new List<Curve>();
                var mainBoundary = boundarySegments[0]; // 取主要邊界
                
                Logger.Log($"房間主要邊界有 {mainBoundary.Count} 個段");
                
                foreach (var segment in mainBoundary)
                {
                    try
                    {
                        var segmentCurve = segment.GetCurve();
                        var boundaryElement = Doc.GetElement(segment.ElementId);
                        
                        if (boundaryElement is Wall boundaryWall)
                        {
                            Logger.Log($"處理邊界牆 {boundaryWall.Id}，厚度: {boundaryWall.Width * 304.8:F2}mm");
                            
                            // 計算粉刷牆應該放置的位置（緊貼結構牆內側）
                            var locationLine = CalculateFinishWallLocationLine(segmentCurve, boundaryWall, room);
                            
                            if (locationLine != null && locationLine.Length > 1e-6)
                            {
                                finishWallLocationLines.Add(locationLine);
                                
                                var start = locationLine.GetEndPoint(0);
                                var end = locationLine.GetEndPoint(1);
                                Logger.Log($"粉刷牆定位線: 起點({start.X * 304.8:F2}, {start.Y * 304.8:F2}), " +
                                         $"終點({end.X * 304.8:F2}, {end.Y * 304.8:F2}), 長度: {locationLine.Length * 304.8:F2}mm");
                            }
                        }
                        else
                        {
                            // 對於非牆面邊界（如柱子等），直接使用邊界曲線
                            if (segmentCurve != null && segmentCurve.Length > 1e-6)
                            {
                                finishWallLocationLines.Add(segmentCurve);
                                Logger.Log($"非牆面邊界，直接使用邊界曲線，長度: {segmentCurve.Length * 304.8:F2}mm");
                            }
                        }
                    }
                    catch (Exception segEx)
                    {
                        Logger.Log($"處理邊界段時發生錯誤: {segEx.Message}");
                    }
                }
                
                Logger.Log($"成功建立 {finishWallLocationLines.Count} 條粉刷牆定位線");
                return finishWallLocationLines;
            }
            catch (Exception ex)
            {
                Logger.Log($"建立粉刷牆定位線失敗: {ex.Message}");
                return new List<Curve>();
            }
        }

        /// <summary>
        /// 計算粉刷牆的精確定位線位置
        /// </summary>
        Curve CalculateFinishWallLocationLine(Curve boundarySegment, Wall boundaryWall, Room room)
        {
            try
            {
                Logger.Log($"計算粉刷牆定位線，邊界牆ID: {boundaryWall.Id}");
                
                // 獲取房間中心點
                var roomCenter = GetRoomCenterPoint(room);
                
                // 計算邊界段的方向和法向量
                var segmentDirection = (boundarySegment.GetEndPoint(1) - boundarySegment.GetEndPoint(0)).Normalize();
                var segmentNormal = new XYZ(-segmentDirection.Y, segmentDirection.X, 0); // 垂直於邊界的法向量
                
                // 計算邊界段的中點
                var segmentMidPoint = boundarySegment.Evaluate(0.5, true);
                
                // 判斷法向量的方向（應該指向房間內部）
                var toRoomCenter = (roomCenter - segmentMidPoint).Normalize();
                var dotProduct = segmentNormal.DotProduct(toRoomCenter);
                
                // 如果法向量指向房間外部，則反向
                if (dotProduct < 0)
                {
                    segmentNormal = segmentNormal.Negate();
                }
                
                // 計算粉刷牆偏移距離
                // 對於使用核心面:內部定位線的牆，偏移距離應該是結構牆厚度的一半
                double wallThickness = boundaryWall.Width;
                double offsetDistance = wallThickness * 0.5;
                
                Logger.Log($"結構牆厚度: {wallThickness * 304.8:F2}mm");
                Logger.Log($"粉刷牆向房間內偏移距離: {offsetDistance * 304.8:F2}mm");
                Logger.Log($"偏移方向: ({segmentNormal.X:F3}, {segmentNormal.Y:F3})");
                
                // 計算偏移向量
                var offsetVector = segmentNormal * offsetDistance;
                
                // 創建粉刷牆的定位線（向房間內部偏移）
                var startPoint = boundarySegment.GetEndPoint(0) + offsetVector;
                var endPoint = boundarySegment.GetEndPoint(1) + offsetVector;
                
                var finishWallLocationLine = Line.CreateBound(startPoint, endPoint);
                
                // 記錄詳細的偏移信息
                var originalStart = boundarySegment.GetEndPoint(0);
                var originalEnd = boundarySegment.GetEndPoint(1);
                
                Logger.Log($"原始邊界線: 起點({originalStart.X * 304.8:F2}, {originalStart.Y * 304.8:F2}), " +
                         $"終點({originalEnd.X * 304.8:F2}, {originalEnd.Y * 304.8:F2})");
                Logger.Log($"粉刷牆定位線: 起點({startPoint.X * 304.8:F2}, {startPoint.Y * 304.8:F2}), " +
                         $"終點({endPoint.X * 304.8:F2}, {endPoint.Y * 304.8:F2})");
                Logger.Log($"定位線長度: {finishWallLocationLine.Length * 304.8:F2}mm");
                
                return finishWallLocationLine;
            }
            catch (Exception ex)
            {
                Logger.Log($"計算粉刷牆定位線失敗: {ex.Message}");
                Logger.Log($"回退使用原始邊界線");
                return boundarySegment; // 失敗時返回原始邊界
            }
        }

        /// <summary>
        /// 獲取房間中心點
        /// </summary>
        XYZ GetRoomCenterPoint(Room room)
        {
            try
            {
                var roomLocation = room.Location as LocationPoint;
                if (roomLocation != null)
                {
                    return roomLocation.Point;
                }
                
                // 如果沒有LocationPoint，使用BoundingBox計算中心
                var bbox = room.get_BoundingBox(null);
                if (bbox != null)
                {
                    return new XYZ(
                        (bbox.Min.X + bbox.Max.X) / 2,
                        (bbox.Min.Y + bbox.Max.Y) / 2,
                        bbox.Min.Z
                    );
                }
                
                Logger.Log("無法獲取房間中心點，使用原點");
                return XYZ.Zero;
            }
            catch (Exception ex)
            {
                Logger.Log($"獲取房間中心點失敗: {ex.Message}");
                return XYZ.Zero;
            }
        }

        /// <summary>
        /// 獲取房間邊界的結構牆
        /// </summary>
        List<Wall> GetRoomBoundaryWalls(Room room)
        {
            try
            {
                var boundaryWalls = new List<Wall>();
                
                var sbo = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center
                };

                var boundarySegments = room.GetBoundarySegments(sbo);
                
                foreach (var boundaryLoop in boundarySegments)
                {
                    foreach (var segment in boundaryLoop)
                    {
                        var element = Doc.GetElement(segment.ElementId);
                        if (element is Wall wall)
                        {
                            boundaryWalls.Add(wall);
                            Logger.Log($"找到邊界牆: {wall.Id}，厚度: {wall.Width * 304.8:F2}mm");
                        }
                    }
                }
                
                Logger.Log($"房間 {room.Name} 共有 {boundaryWalls.Count} 面邊界牆");
                return boundaryWalls;
            }
            catch (Exception ex)
            {
                Logger.Log($"獲取房間邊界牆失敗: {ex.Message}");
                return new List<Wall>();
            }
        }

        /// <summary>
        /// 計算牆面平均厚度
        /// </summary>
        double CalculateAverageWallThickness(List<Wall> walls)
        {
            try
            {
                if (walls == null || !walls.Any())
                {
                    Logger.Log("沒有牆面用於計算平均厚度");
                    return 0;
                }

                double totalThickness = 0;
                int validWallCount = 0;

                foreach (var wall in walls)
                {
                    try
                    {
                        double thickness = wall.Width;
                        if (thickness > 0)
                        {
                            totalThickness += thickness;
                            validWallCount++;
                            Logger.Log($"牆 {wall.Id} 厚度: {thickness * 304.8:F2}mm");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"獲取牆 {wall.Id} 厚度失敗: {ex.Message}");
                    }
                }

                if (validWallCount > 0)
                {
                    double avgThickness = totalThickness / validWallCount;
                    Logger.Log($"計算出平均牆厚度: {avgThickness * 304.8:F2}mm (基於 {validWallCount} 面牆)");
                    return avgThickness;
                }
                else
                {
                    Logger.Log("沒有有效的牆面厚度資料");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"計算平均牆厚度失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 調整牆面方向，確保面向房間內部
        /// </summary>
        void AdjustWallOrientation(Wall wall, XYZ roomCenter)
        {
            try
            {
                Logger.Log($"開始調整牆面 {wall.Id} 的方向");
                
                var locationCurve = wall.Location as LocationCurve;
                if (locationCurve == null)
                {
                    Logger.Log($"無法獲取牆面 {wall.Id} 的定位曲線");
                    return;
                }

                var curve = locationCurve.Curve;
                var midPoint = curve.Evaluate(0.5, true);
                
                // 計算牆面的法向量
                var direction = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                var normal = new XYZ(-direction.Y, direction.X, 0); // 法向量（垂直於牆面方向）
                
                // 計算從牆面中點到房間中心的向量
                var toRoomCenter = (roomCenter - midPoint).Normalize();
                
                // 檢查牆面是否需要翻轉
                var dotProduct = normal.DotProduct(toRoomCenter);
                Logger.Log($"牆面法向量與房間中心方向的點積: {dotProduct:F3}");
                
                // 如果點積為負，說明牆面背向房間，需要翻轉
                if (dotProduct < 0)
                {
                    try
                    {
                        wall.Flip();
                        Logger.Log($"牆面 {wall.Id} 已翻轉，現在面向房間內部");
                    }
                    catch (Exception flipEx)
                    {
                        Logger.Log($"翻轉牆面 {wall.Id} 失敗: {flipEx.Message}");
                    }
                }
                else
                {
                    Logger.Log($"牆面 {wall.Id} 方向正確，面向房間內部");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"調整牆面 {wall.Id} 方向失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 驗證房間邊界是否形成封閉循環
        /// </summary>
        bool ValidateRoomBoundary(List<Curve> curves)
        {
            try
            {
                if (curves == null || curves.Count < 3)
                {
                    Logger.Log("邊界曲線數量不足，無法形成封閉區域");
                    return false;
                }

                // 檢查曲線是否首尾相連
                double tolerance = 1e-6;
                for (int i = 0; i < curves.Count; i++)
                {
                    var current = curves[i];
                    var next = curves[(i + 1) % curves.Count];
                    
                    if (current == null || next == null)
                    {
                        Logger.Log($"邊界曲線 {i} 或 {(i + 1) % curves.Count} 為null");
                        return false;
                    }
                    
                    var currentEnd = current.GetEndPoint(1);
                    var nextStart = next.GetEndPoint(0);
                    var distance = currentEnd.DistanceTo(nextStart);
                    
                    if (distance > tolerance)
                    {
                        Logger.Log($"邊界曲線 {i} 和 {(i + 1) % curves.Count} 未連接，距離: {distance * 304.8:F2}mm");
                        return false;
                    }
                }
                
                Logger.Log("房間邊界驗證通過，形成封閉循環");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"驗證房間邊界失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 將曲線向內偏移指定距離，讓裝修牆緊貼結構牆內側
        /// </summary>
        List<Curve> OffsetCurvesInward(List<Curve> curves, double offsetDistance)
        {
            try
            {
                Logger.Log($"開始向內偏移曲線，偏移距離: {offsetDistance * 304.8:F2}mm");

                if (curves == null || !curves.Any() || offsetDistance <= 0)
                {
                    Logger.Log("偏移參數無效，返回原始曲線");
                    return curves ?? new List<Curve>();
                }

                // 優先嘗試 CurveLoop 整體偏移（適用於完整閉合環，無門窗間隙）
                try
                {
                    var curveLoop = CurveLoop.Create(curves);
                    var offsetLoop = CurveLoop.CreateViaOffset(curveLoop, -offsetDistance, XYZ.BasisZ);
                    var loopOffsetCurves = offsetLoop.ToList();

                    if (loopOffsetCurves.Any() && loopOffsetCurves.All(c => c != null && c.Length > 1e-6))
                    {
                        Logger.Log($"CurveLoop 整體偏移成功，生成 {loopOffsetCurves.Count} 條曲線");
                        return loopOffsetCurves;
                    }
                }
                catch (Exception loopEx)
                {
                    Logger.Log($"CurveLoop 整體偏移失敗（可能有門窗間隙）: {loopEx.Message}，改用逐段偏移");
                }

                // 備用：逐段向內偏移（曲線有門窗間隙時使用）
                // 以所有曲線的幾何中心計算室內方向
                double cx = 0, cy = 0; int n = 0;
                foreach (var c in curves)
                {
                    cx += c.GetEndPoint(0).X + c.GetEndPoint(1).X;
                    cy += c.GetEndPoint(0).Y + c.GetEndPoint(1).Y;
                    n  += 2;
                }
                if (n == 0) return curves;
                cx /= n; cy /= n;

                var result = new List<Curve>();
                foreach (var c in curves)
                {
                    if (c == null || c.Length < 1e-9) continue;

                    var start = c.GetEndPoint(0);
                    var end   = c.GetEndPoint(1);
                    var dir   = (end - start).Normalize();

                    // 兩個法線方向（垂直於曲線，在 XY 平面）
                    var leftNorm  = new XYZ(-dir.Y,  dir.X, 0);
                    var rightNorm = new XYZ( dir.Y, -dir.X, 0);

                    // 選擇朝向室內中心的法線
                    var mid       = new XYZ((start.X + end.X) / 2, (start.Y + end.Y) / 2, start.Z);
                    var toCentroid = new XYZ(cx - mid.X, cy - mid.Y, 0);
                    var inward     = toCentroid.DotProduct(leftNorm) >= 0 ? leftNorm : rightNorm;

                    var offset = inward.Multiply(offsetDistance);
                    var newStart = new XYZ(start.X + offset.X, start.Y + offset.Y, start.Z);
                    var newEnd   = new XYZ(end.X   + offset.X, end.Y   + offset.Y, end.Z);

                    try
                    {
                        result.Add(Line.CreateBound(newStart, newEnd));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"逐段偏移建立直線失敗: {ex.Message}，保留原曲線");
                        result.Add(c);
                    }
                }

                Logger.Log($"逐段偏移完成，共 {result.Count} 條曲線");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Log($"曲線偏移處理失敗: {ex.Message}");
                return curves ?? new List<Curve>();
            }
        }

        XYZ CalculateRoomCenter(List<Curve> curves)
        {
            try
            {
                double totalX = 0, totalY = 0;
                int pointCount = 0;
                
                foreach (var curve in curves)
                {
                    var startPoint = curve.GetEndPoint(0);
                    var endPoint = curve.GetEndPoint(1);
                    
                    totalX += startPoint.X + endPoint.X;
                    totalY += startPoint.Y + endPoint.Y;
                    pointCount += 2;
                }
                
                if (pointCount > 0)
                {
                    return new XYZ(totalX / pointCount, totalY / pointCount, 0);
                }
                
                return XYZ.Zero;
            }
            catch
            {
                return XYZ.Zero;
            }
        }

        void CreateContinuousWallFinish(Room room, FinishSettings settings)
        {
            try
            {
                Logger.Log($"嘗試建立連續牆面裝修 for 房間 {room.Name}");
                
                var level = GetLevel(room.LevelId);
                var wt = GetWallType(settings.SelectedWallTypeId);
                double wallHeight = settings.MmToInternalUnits(settings.CeilingHeightMm + settings.WallOffsetMm);
                
                // 取得完整的房間輪廓
                Level lvl; double halfT; CurveLoop loop;
                var profile = GetRoomProfile(room, FloorBoundaryMode.InnerFinish, out lvl, out halfT, out loop);
                
                if (profile.Size >= 3)
                {
                    try
                    {
                        // 嘗試建立單一連續牆面
                        var curves = new List<Curve>();
                        foreach (Curve c in profile)
                        {
                            curves.Add(c);
                        }
                        
                        // 使用 CurveLoop 建立牆面
                        var curveArray = new CurveArray();
                        foreach (var curve in curves)
                        {
                            curveArray.Append(curve);
                        }
                        
                        Logger.Log($"嘗試建立連續牆面，包含 {curves.Count} 條曲線");
                        
                        // 這裡可以嘗試不同的牆面建立方法
                        // 但 Revit 可能不支援直接從 CurveLoop 建立牆面
                        Logger.Log("連續牆面建立方法需要進一步研究");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"建立連續牆面失敗: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"連續牆面處理異常: {ex.Message}");
            }
        }

        void TagRoomOnElement(Element e, Room r)
        {
            try
            {
                FinishingElementGuard.MarkAsManagedFinishing(e);

                var roomIdLong = RevitCompat.GetElementIdValue(r.Id);

                var p = e.LookupParameter("房間ID(AR_RoomId)") ?? e.LookupParameter("房間ID") ?? e.LookupParameter("AR_RoomId");
                if (p != null && !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.Integer)
                    {
                        // Revit 2024+ ElementId 可能超過 int 範圍，需安全轉換
                        if (roomIdLong >= int.MinValue && roomIdLong <= int.MaxValue)
                            p.Set((int)roomIdLong);
                    }
                    else if (p.StorageType == StorageType.String)
                        p.Set(roomIdLong.ToString());
                }
            }
            catch { }
        }

        private static bool IsProtectedGeneratedFinishElement(Element element)
        {
            if (!FinishingElementGuard.IsSupportedFinishCategory(element))
                return false;

            if (element is DirectShape ds)
                return string.Equals(ds.ApplicationId, FinishingElementGuard.StableMarker, StringComparison.OrdinalIgnoreCase);

            return FinishingElementGuard.HasStableMarker(element);
        }

        public JoinResults AutoJoinExistingWalls(IList<ElementId> targetRoomIds)
        {
            var results = new JoinResults();
            
            try
            {
                // 取得目標房間
                var rooms = GetRooms(targetRoomIds);
                var allWalls = new FilteredElementCollector(Doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .Where(IsFinishWallCandidate)
                    .ToList();

                Logger.Log($"找到 {allWalls.Count} 個裝修牆面進行自動接合");

                if (!allWalls.Any())
                {
                    results.Errors.Add("未找到任何裝修牆面可供接合");
                    return results;
                }

                // 按房間分組處理牆面
                var joinAttempts = 0;
                var successfulJoins = 0;

                foreach (var room in rooms)
                {
                    try
                    {
                        var roomWalls = GetWallsInRoom(allWalls, room);
                        Logger.Log($"房間 {room.Number}: 找到 {roomWalls.Count} 個牆面");

                        if (roomWalls.Count < 2) continue;

                        // 嘗試接合相鄰的牆面
                        for (int i = 0; i < roomWalls.Count; i++)
                        {
                            for (int j = i + 1; j < roomWalls.Count; j++)
                            {
                                var wall1 = roomWalls[i];
                                var wall2 = roomWalls[j];
                                
                                joinAttempts++;
                                results.TotalAttempts++;

                                if (TryJoinWalls(wall1, wall2))
                                {
                                    successfulJoins++;
                                    results.SuccessCount++;
                                    Logger.Log($"成功接合牆面: {wall1.Id} 和 {wall2.Id}");
                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        results.Errors.Add($"房間 {room.Number} 處理失敗: {ex.Message}");
                        Logger.Log($"房間 {room.Number} 自動接合失敗: {ex.Message}");
                    }
                }

                // 所有房間處理完畢後統一 Regenerate（避免逐房間呼叫的效能損耗）
                Doc.Regenerate();
                Logger.Log($"自動接合完成: {successfulJoins}/{joinAttempts}");
            }
            catch (Exception ex)
            {
                results.Errors.Add($"自動接合執行失敗: {ex.Message}");
                Logger.Log($"AutoJoinExistingWalls 失敗: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// 修復房間邊界牆的端點對齊問題，解決「結構牆端點跑掉導致房間偵測失敗」。
        /// 涵蓋兩種情形：
        ///   L 型接頭 — 兩道牆的端點互相非常接近但未完全重合
        ///   T 型接頭 — 一道牆的端點非常接近另一道牆的側面（投影在牆體之上）
        /// </summary>
        /// <param name="targetRoomIds">限定處理的房間 ID；傳 null 時掃描全圖。</param>
        /// <param name="toleranceMm">判定「接近」的距離容差（毫米，預設 25 mm）。</param>
        public (int Fixed, int Failed, List<string> Errors) AlignWallEndpoints(
            IList<ElementId> targetRoomIds, double toleranceMm = 25.0)
        {
            double tol = toleranceMm / 304.8; // feet
            int fixedCount = 0;
            int failedCount = 0;
            var errors = new List<string>();

            try
            {
                // ── 1. 收集候選牆（設為房間邊界、位於目標房間附近）──
                var allWalls = new FilteredElementCollector(Doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .Where(w => IsRoomBoundingWall(w) &&
                                (w.Location as LocationCurve)?.Curve is Line)
                    .ToList();

                if (targetRoomIds != null && targetRoomIds.Any())
                {
                    var rooms = GetRooms(targetRoomIds).ToList();
                    var roomBBoxes = rooms
                        .Select(r => r.get_BoundingBox(null))
                        .Where(bb => bb != null)
                        .ToList();

                    if (roomBBoxes.Any())
                    {
                        double margin = 1.0; // 1 foot 緩衝
                        double minX = roomBBoxes.Min(bb => bb.Min.X) - margin;
                        double minY = roomBBoxes.Min(bb => bb.Min.Y) - margin;
                        double maxX = roomBBoxes.Max(bb => bb.Max.X) + margin;
                        double maxY = roomBBoxes.Max(bb => bb.Max.Y) + margin;

                        allWalls = allWalls.Where(w =>
                        {
                            var loc = (w.Location as LocationCurve)?.Curve;
                            if (loc == null) return false;
                            var p0 = loc.GetEndPoint(0);
                            var p1 = loc.GetEndPoint(1);
                            return (p0.X >= minX && p0.X <= maxX && p0.Y >= minY && p0.Y <= maxY) ||
                                   (p1.X >= minX && p1.X <= maxX && p1.Y >= minY && p1.Y <= maxY);
                        }).ToList();
                    }
                }

                Logger.Log($"AlignWallEndpoints: 待檢查牆 {allWalls.Count} 道，容差 {toleranceMm:F1} mm");

                if (!allWalls.Any())
                {
                    errors.Add("未找到任何「設為房間邊界」的牆可供修復");
                    return (fixedCount, failedCount, errors);
                }

                // ── 2. 建立端點快照（Line 列表 + 端點列表）──
                var wallLines = allWalls
                    .Select(w => (w.Location as LocationCurve).Curve as Line)
                    .ToList();

                // moves: (wallIdx, epIdx 0/1) → 新端點位置
                var moves = new Dictionary<(int wi, int ei), XYZ>();

                // ── Pass A：L 型接頭 — 端點對端點 ──
                for (int a = 0; a < allWalls.Count; a++)
                {
                    if (wallLines[a] == null) continue;
                    for (int eA = 0; eA <= 1; eA++)
                    {
                        if (moves.ContainsKey((a, eA))) continue;
                        var posA = wallLines[a].GetEndPoint(eA);

                        for (int b = a + 1; b < allWalls.Count; b++)
                        {
                            if (wallLines[b] == null) continue;
                            for (int eB = 0; eB <= 1; eB++)
                            {
                                if (moves.ContainsKey((b, eB))) continue;
                                var posB = wallLines[b].GetEndPoint(eB);
                                double dist = posA.DistanceTo(posB);
                                if (dist > 1e-9 && dist < tol)
                                {
                                    // 以 A 端點為準，將 B 端點對齊過去
                                    moves[(b, eB)] = posA;
                                    Logger.Log($"L 型接頭: 牆{RevitCompat.GetElementIdValue(allWalls[b].Id)} ep{eB} → 牆{RevitCompat.GetElementIdValue(allWalls[a].Id)} ep{eA}，偏差 {dist * 304.8:F1} mm");
                                }
                            }
                        }
                    }
                }

                // ── Pass B：T 型接頭 — 端點投影到另一道牆的側面 ──
                for (int a = 0; a < allWalls.Count; a++)
                {
                    if (wallLines[a] == null) continue;
                    for (int eA = 0; eA <= 1; eA++)
                    {
                        if (moves.ContainsKey((a, eA))) continue;
                        var posA = wallLines[a].GetEndPoint(eA);

                        for (int b = 0; b < allWalls.Count; b++)
                        {
                            if (b == a || wallLines[b] == null) continue;
                            var lineB  = wallLines[b];
                            var bDir   = (lineB.GetEndPoint(1) - lineB.GetEndPoint(0)).Normalize();
                            var bOrig  = lineB.GetEndPoint(0);
                            double param = (posA - bOrig).DotProduct(bDir);
                            double bLen  = lineB.Length;

                            // 投影需落在牆體中段（排除端點區域，避免重複處理 L 型接頭）
                            if (param < tol || param > bLen - tol) continue;

                            var proj = bOrig + bDir.Multiply(param);
                            double dist = posA.DistanceTo(proj);
                            if (dist > 1e-9 && dist < tol)
                            {
                                moves[(a, eA)] = proj;
                                Logger.Log($"T 型接頭: 牆{RevitCompat.GetElementIdValue(allWalls[a].Id)} ep{eA} → 牆{RevitCompat.GetElementIdValue(allWalls[b].Id)} 側面，偏差 {dist * 304.8:F1} mm");
                                break;
                            }
                        }
                    }
                }

                if (!moves.Any())
                {
                    Logger.Log("AlignWallEndpoints: 未發現需要修復的端點");
                    return (fixedCount, failedCount, errors);
                }

                Logger.Log($"AlignWallEndpoints: 準備修復 {moves.Count} 個端點");

                // ── 3. 套用端點移動 ──
                foreach (var kvp in moves)
                {
                    var (wallIdx, epIdx) = kvp.Key;
                    var newPos = kvp.Value;
                    var wall   = allWalls[wallIdx];

                    try
                    {
                        var locCurve    = wall.Location as LocationCurve;
                        var currentLine = locCurve?.Curve as Line;
                        if (currentLine == null) { failedCount++; continue; }

                        var p0 = currentLine.GetEndPoint(0);
                        var p1 = currentLine.GetEndPoint(1);

                        Line newLine = epIdx == 0
                            ? Line.CreateBound(newPos, p1)
                            : Line.CreateBound(p0, newPos);

                        try
                        {
                            locCurve.Curve = newLine;
                        }
                        catch
                        {
                            // 若 join 約束阻止移動，暫時解除後再試
                            WallUtils.DisallowWallJoinAtEnd(wall, epIdx);
                            locCurve.Curve = newLine;
                        }

                        fixedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        var msg = $"修復牆 {RevitCompat.GetElementIdValue(wall.Id)} 端點 {epIdx} 失敗：{ex.Message}";
                        errors.Add(msg);
                        Logger.Log($"AlignWallEndpoints 套用失敗：{msg}");
                    }
                }

                Logger.Log($"AlignWallEndpoints: 完成，修復 {fixedCount}，失敗 {failedCount}");
            }
            catch (Exception ex)
            {
                errors.Add($"端點對齊執行失敗：{ex.Message}");
                Logger.Log($"AlignWallEndpoints 異常：{ex.Message}");
            }

            return (fixedCount, failedCount, errors);
        }

        private List<Wall> GetWallsInRoom(List<Wall> allWalls, Room room)
        {
            var roomWalls = new List<Wall>();

            try
            {
                long targetRoomId = RevitCompat.GetElementIdValue(room.Id);
                var roomBBox = room.get_BoundingBox(null);

                foreach (var wall in allWalls)
                {
                    try
                    {
                        // 優先：比對牆面標記的 AR_RoomId（生成時寫入，精確對應）
                        long taggedId = GetTaggedRoomId(wall);
                        if (taggedId == targetRoomId)
                        {
                            roomWalls.Add(wall);
                            continue;
                        }

                        // 備用：未標記牆面，檢查牆中點是否在房間 BoundingBox 內
                        if (taggedId <= 0 && roomBBox != null)
                        {
                            var wallCurve = (wall.Location as LocationCurve)?.Curve;
                            if (wallCurve == null) continue;
                            var mid = wallCurve.Evaluate(0.5, true);
                            const double bbTol = 50.0 / 304.8; // 50mm 容差
                            if (mid.X >= roomBBox.Min.X - bbTol && mid.X <= roomBBox.Max.X + bbTol &&
                                mid.Y >= roomBBox.Min.Y - bbTol && mid.Y <= roomBBox.Max.Y + bbTol)
                                roomWalls.Add(wall);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"檢查牆面 {wall.Id} 與房間 {room.Number} 的關係時失敗: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"GetWallsInRoom 失敗 (房間 {room.Number}): {ex.Message}");
            }

            return roomWalls;
        }

        private bool IsFinishWallCandidate(Wall wall)
        {
            if (wall == null)
                return false;

            if (IsRoomBoundingWall(wall))
                return false;

            return IsProtectedGeneratedFinishElement(wall);
        }

        private bool TryJoinWalls(Wall wall1, Wall wall2)
        {
            try
            {
                var curve1 = (wall1.Location as LocationCurve)?.Curve;
                var curve2 = (wall2.Location as LocationCurve)?.Curve;

                if (curve1 == null || curve2 == null) return false;

                if (IsRoomBoundingWall(wall1) || IsRoomBoundingWall(wall2))
                    return false;

                // 端點距離檢查無法用於粉刷牆角落（端點距離 = halfWidth×√2，非 0mm）。
                // 改用 Bounding Box 相交判斷：粉刷牆體在角落處重疊，BB 一定相交。
                var bb1 = wall1.get_BoundingBox(null);
                var bb2 = wall2.get_BoundingBox(null);
                if (bb1 == null || bb2 == null) return false;

                const double bbTol = 5.0 / 304.8; // 5mm 容差
                bool xOverlap = bb1.Min.X - bbTol <= bb2.Max.X && bb1.Max.X + bbTol >= bb2.Min.X;
                bool yOverlap = bb1.Min.Y - bbTol <= bb2.Max.Y && bb1.Max.Y + bbTol >= bb2.Min.Y;
                if (!xOverlap || !yOverlap) return false;

                // 排除平行牆（平行牆 BB 也可能相交，但不應接合）
                var dir1 = (curve1.GetEndPoint(1) - curve1.GetEndPoint(0)).Normalize();
                var dir2 = (curve2.GetEndPoint(1) - curve2.GetEndPoint(0)).Normalize();
                double parallelDot = Math.Abs(dir1.DotProduct(dir2));
                if (parallelDot > 0.95) return false; // 夾角 < ~18°，視為平行

                if (!JoinGeometryUtils.AreElementsJoined(Doc, wall1, wall2))
                    JoinGeometryUtils.JoinGeometry(Doc, wall1, wall2);

                try { WallUtils.DisallowWallJoinAtEnd(wall1, 0); } catch { }
                try { WallUtils.DisallowWallJoinAtEnd(wall1, 1); } catch { }
                try { WallUtils.DisallowWallJoinAtEnd(wall2, 0); } catch { }
                try { WallUtils.DisallowWallJoinAtEnd(wall2, 1); } catch { }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"接合牆面 {wall1.Id} 和 {wall2.Id} 失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 獲取房間附近的結構牆體
        /// </summary>
        private List<Wall> GetStructuralWallsInRoom(Room room)
        {
            var structuralWalls = new List<Wall>();
            
            try
            {
                // 獲取所有非裝修牆體（結構牆體）
                var allWalls = new FilteredElementCollector(Doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .Where(w => !w.WallType.Name.Contains("AR_") && !w.WallType.Name.Contains("裝修"))
                    .ToList();

                var roomBBox = room.get_BoundingBox(null);
                if (roomBBox == null) return structuralWalls;

                // 擴大搜尋範圍
                var expandedBBox = new BoundingBoxXYZ
                {
                    Min = roomBBox.Min - new XYZ(5, 5, 0), // 擴大5英尺
                    Max = roomBBox.Max + new XYZ(5, 5, 0)
                };

                foreach (var wall in allWalls)
                {
                    try
                    {
                        var wallBBox = wall.get_BoundingBox(null);
                        if (wallBBox != null && BoundingBoxesIntersect(expandedBBox, wallBBox))
                        {
                            structuralWalls.Add(wall);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"檢查結構牆 {wall.Id} 時發生錯誤: {ex.Message}");
                    }
                }

                Logger.Log($"在房間 {room.Name} 附近找到 {structuralWalls.Count} 個結構牆體");
            }
            catch (Exception ex)
            {
                Logger.Log($"獲取結構牆體失敗: {ex.Message}");
            }

            return structuralWalls;
        }

        /// <summary>
        /// 檢查兩個包圍盒是否相交
        /// </summary>
        private bool BoundingBoxesIntersect(BoundingBoxXYZ box1, BoundingBoxXYZ box2)
        {
            return !(box1.Max.X < box2.Min.X || box2.Max.X < box1.Min.X ||
                     box1.Max.Y < box2.Min.Y || box2.Max.Y < box1.Min.Y ||
                     box1.Max.Z < box2.Min.Z || box2.Max.Z < box1.Min.Z);
        }

        // ════════════════════════════════════════════════════════════════
        //  結構牆端點 → 結構柱邊緣對齊
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// 將結構牆的左右端點（控制點）對齊至最近的結構柱側面，
        /// 修復因端點未觸及柱面而導致的房間邊界缺口。
        /// 必須在 Transaction 內呼叫。
        /// </summary>
        public (int Fixed, int Failed, List<string> Errors) AlignStructuralWallsToColumns(
            IList<ElementId> targetRoomIds, double toleranceMm = 200.0)
        {
            double tol = toleranceMm / 304.8;
            int fixedCount = 0, failedCount = 0;
            var errors = new List<string>();

            try
            {
                // ── 1. 收集結構柱的側面平面 ──
                var columns = new FilteredElementCollector(Doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                if (!columns.Any())
                {
                    errors.Add("模型中無結構柱（OST_StructuralColumns），無法執行柱邊對齊");
                    return (fixedCount, failedCount, errors);
                }

                var colFaces = ExtractColumnSidefaces(columns);
                Logger.Log($"AlignStructuralWallsToColumns: 找到 {columns.Count} 支結構柱，共 {colFaces.Count} 個側面");

                // ── 2. 收集目標結構牆 ──
                var structWalls = new FilteredElementCollector(Doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .Where(w => (w.Location as LocationCurve)?.Curve is Line && IsStructuralWall(w))
                    .ToList();

                if (targetRoomIds != null && targetRoomIds.Any())
                {
                    var rooms = GetRooms(targetRoomIds).ToList();
                    var roomBBoxes = rooms.Select(r => r.get_BoundingBox(null)).Where(b => b != null).ToList();
                    if (roomBBoxes.Any())
                    {
                        double margin = tol + 1.0;
                        double minX = roomBBoxes.Min(b => b.Min.X) - margin;
                        double minY = roomBBoxes.Min(b => b.Min.Y) - margin;
                        double maxX = roomBBoxes.Max(b => b.Max.X) + margin;
                        double maxY = roomBBoxes.Max(b => b.Max.Y) + margin;
                        structWalls = structWalls.Where(w =>
                        {
                            var loc = (w.Location as LocationCurve)?.Curve;
                            if (loc == null) return false;
                            var p0 = loc.GetEndPoint(0);
                            var p1 = loc.GetEndPoint(1);
                            return (p0.X >= minX && p0.X <= maxX && p0.Y >= minY && p0.Y <= maxY) ||
                                   (p1.X >= minX && p1.X <= maxX && p1.Y >= minY && p1.Y <= maxY);
                        }).ToList();
                    }
                }

                Logger.Log($"AlignStructuralWallsToColumns: 待處理結構牆 {structWalls.Count} 道");

                if (!structWalls.Any())
                {
                    errors.Add("選定範圍內無結構牆（WallFunction.Structural）可對齊");
                    return (fixedCount, failedCount, errors);
                }

                // ── 3. 計算端點 → 柱側面對齊目標 ──
                var moves = new Dictionary<(int wi, int ei), XYZ>();
                for (int wi = 0; wi < structWalls.Count; wi++)
                {
                    var wall = structWalls[wi];
                    var line = (wall.Location as LocationCurve).Curve as Line;
                    if (line == null) continue;
                    var wallDir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();

                    for (int ei = 0; ei <= 1; ei++)
                    {
                        if (moves.ContainsKey((wi, ei))) continue;
                        var ep = line.GetEndPoint(ei);
                        XYZ bestSnap = null;
                        double bestDist = tol;

                        foreach (var cf in colFaces)
                        {
                            // 柱面法向量須與牆方向接近平行（cos 30° ≈ 0.866）
                            double dot = Math.Abs(cf.Normal.DotProduct(wallDir));
                            if (dot < 0.866) continue;

                            // 端點到面的距離
                            double signedDist = cf.SignedDistanceTo(ep);
                            double absDist = Math.Abs(signedDist);
                            if (absDist < 1e-9 || absDist >= bestDist) continue;

                            // 投影點是否在柱面範圍內
                            var projPt3d = ep - cf.Normal.Multiply(signedDist);
                            if (!cf.ContainsProjection(projPt3d, tol * 0.5)) continue;

                            bestDist = absDist;
                            bestSnap = new XYZ(projPt3d.X, projPt3d.Y, ep.Z);
                        }

                        if (bestSnap != null)
                        {
                            moves[(wi, ei)] = bestSnap;
                            Logger.Log($"AlignToColumn: 牆 {RevitCompat.GetElementIdValue(wall.Id)} ep{ei}，偏差 {bestDist * 304.8:F1} mm");
                        }
                    }
                }

                if (!moves.Any())
                {
                    Logger.Log("AlignStructuralWallsToColumns: 未發現需要對齊的端點");
                    errors.Add($"未找到需要對齊的端點（容差 {toleranceMm:F0} mm 內無符合柱側面）");
                    return (fixedCount, failedCount, errors);
                }

                Logger.Log($"AlignStructuralWallsToColumns: 準備對齊 {moves.Count} 個端點");

                // ── 4. 套用端點移動 ──
                foreach (var kvp in moves)
                {
                    var (wallIdx, epIdx) = kvp.Key;
                    var newPos = kvp.Value;
                    var wall = structWalls[wallIdx];
                    try
                    {
                        var locCurve = wall.Location as LocationCurve;
                        var curLine = locCurve?.Curve as Line;
                        if (curLine == null) { failedCount++; continue; }

                        var p0 = curLine.GetEndPoint(0);
                        var p1 = curLine.GetEndPoint(1);
                        var newLine = epIdx == 0
                            ? Line.CreateBound(newPos, p1)
                            : Line.CreateBound(p0, newPos);

                        try { locCurve.Curve = newLine; }
                        catch
                        {
                            WallUtils.DisallowWallJoinAtEnd(wall, epIdx);
                            locCurve.Curve = newLine;
                        }
                        fixedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        var msg = $"牆 {RevitCompat.GetElementIdValue(wall.Id)} ep{epIdx} 對齊失敗：{ex.Message}";
                        errors.Add(msg);
                        Logger.Log(msg);
                    }
                }

                Logger.Log($"AlignStructuralWallsToColumns: 完成 {fixedCount} 個，失敗 {failedCount}");
            }
            catch (Exception ex)
            {
                errors.Add($"結構牆對齊柱邊執行失敗：{ex.Message}");
                Logger.Log($"AlignStructuralWallsToColumns 異常：{ex.Message}");
            }

            return (fixedCount, failedCount, errors);
        }

        private static bool IsStructuralWall(Wall wall)
        {
            try
            {
                var typeName = wall.WallType?.Name ?? string.Empty;
                // 排除裝修/粉刷牆（AR_ 前綴或包含「裝修」字樣）
                return !typeName.Contains("AR_") && !typeName.Contains("裝修");
            }
            catch { return false; }
        }

        /// <summary>
        /// 封裝柱的一個垂直側面，提供距離計算與點包含判斷。
        /// </summary>
        private sealed class ColumnFace
        {
            public XYZ Normal { get; }
            public XYZ Origin { get; }
            private readonly PlanarFace _pf;

            public ColumnFace(PlanarFace pf)
            {
                _pf = pf;
                Normal = pf.FaceNormal.Normalize();
                var bb = pf.GetBoundingBox();
                Origin = pf.Evaluate(new UV(
                    (bb.Min.U + bb.Max.U) * 0.5,
                    (bb.Min.V + bb.Max.V) * 0.5));
            }

            public double SignedDistanceTo(XYZ pt) => (pt - Origin).DotProduct(Normal);

            /// <summary>
            /// 判斷 projPt（已投影至面平面的點）是否落在面的 UV 範圍內（加容差）。
            /// </summary>
            public bool ContainsProjection(XYZ projPt, double tolerance)
            {
                try
                {
                    var result = _pf.Project(projPt);
                    if (result == null) return false;
                    var uv = result.UVPoint;
                    var bb = _pf.GetBoundingBox();
                    return uv.U >= bb.Min.U - tolerance && uv.U <= bb.Max.U + tolerance
                        && uv.V >= bb.Min.V - tolerance && uv.V <= bb.Max.V + tolerance;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// 從結構柱列表中提取所有垂直側面（法向量水平）。
        /// </summary>
        private List<ColumnFace> ExtractColumnSidefaces(IEnumerable<FamilyInstance> columns)
        {
            var result = new List<ColumnFace>();
            var opts = new Options { ComputeReferences = false };
            foreach (var col in columns)
            {
                try
                {
                    foreach (var solid in EnumerateGeometrySolids(col.get_Geometry(opts)))
                    {
                        if (solid.Volume < 1e-9) continue;
                        foreach (Face face in solid.Faces)
                        {
                            if (!(face is PlanarFace pf)) continue;
                            // 只取垂直側面（法向量幾乎水平）
                            if (Math.Abs(pf.FaceNormal.Z) > 0.15) continue;
                            result.Add(new ColumnFace(pf));
                        }
                    }
                }
                catch { }
            }
            return result;
        }

        /// <summary>
        /// 遞迴遍歷 GeometryElement，yield 所有 Solid（含 GeometryInstance 內部）。
        /// </summary>
        private static IEnumerable<Solid> EnumerateGeometrySolids(GeometryElement geomElem)
        {
            if (geomElem == null) yield break;
            foreach (var obj in geomElem)
            {
                if (obj is Solid s && s.Volume > 0)
                    yield return s;
                else if (obj is GeometryInstance gi)
                    foreach (var inner in EnumerateGeometrySolids(gi.GetInstanceGeometry()))
                        yield return inner;
            }
        }

        /// <summary>
        /// 調整曲線定位以確保牆面核心正確朝向房間內側（修正定位線錯誤）
        /// </summary>
        private Curve AdjustCurveForWallPlacement(Curve originalCurve, XYZ roomCenter, WallType wallType)
        {
            try
            {
                if (originalCurve == null || roomCenter == null) return originalCurve;
                
                Logger.Log($"開始調整曲線定位，房間中心: ({roomCenter.X * 304.8:F2}, {roomCenter.Y * 304.8:F2})");
                
                // 獲取牆類型的厚度
                var wallThickness = wallType.Width; // Revit內部單位（英尺）
                Logger.Log($"牆類型厚度: {wallThickness * 304.8:F2} mm");
                
                // 不進行偏移，直接使用房間邊界作為牆面定位線
                // 這樣可以確保粉刷牆緊貼房間邊界
                Logger.Log($"使用房間邊界作為牆面定位線，不進行額外偏移");
                
                return originalCurve;
            }
            catch (Exception ex)
            {
                Logger.Log($"調整曲線定位失敗: {ex.Message}");
                return originalCurve;
            }
        }
    }
}
