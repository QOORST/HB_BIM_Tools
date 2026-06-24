using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    public class ProcessingResults
    {
        public int SuccessCount { get; set; } = 0;
        public List<(string roomNumber, string message)> Errors { get; } = new List<(string, string)>();
        
        public void AddError(string roomNumber, string message)
        {
            Errors.Add((roomNumber, message));
        }
    }

    internal sealed class RoomOverlapIssue
    {
        public HashSet<long> RoomIds { get; } = new HashSet<long>();
        public string Description { get; set; }
    }

    public class ValueWriter
    {
        private readonly UIDocument _uidoc;
        private Document Doc => _uidoc.Document;

        private static readonly BuiltInCategory[] FinishCategories =
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_GenericModel
        };

        const string GROUP_NAME = "AR_Finishings";
        const string P_RoomNames = "房間名稱(AR_RoomNames)";
        const string P_RoomNumbers = "房間編號(AR_RoomNumbers)";
        const string P_RoomId = "房間ID(AR_RoomId)";
        const string P_LegacyZhRoomNames = "房間名稱";
        const string P_LegacyZhRoomNumbers = "房間編號";
        const string P_LegacyZhRoomId = "房間ID";
        const string P_LegacyRoomNames = "AR_RoomNames";
        const string P_LegacyRoomNumbers = "AR_RoomNumbers";
        const string P_LegacyRoomId = "AR_RoomId";
        const string P_Summary = "AR_Summary";
        const string P_DynWallFinish = "AR_牆面塗層";
        const string P_DynFloorFinish = "AR_樓板塗層";
        const string P_DynCeilingFinish = "AR_天花板塗層";
        const string P_DynCeilingHeight = "AR_天花板高度";
        const string P_DynSkirtingFinish = "AR_踢腳板塗層";
        const string P_LegacyDynWallFinish = "牆面塗層";
        const string P_LegacyDynFloorFinish = "樓板塗層";
        const string P_LegacyDynCeilingFinish = "天花板塗層";
        const string P_LegacyDynCeilingHeight = "天花板高度";
        const string P_LegacyDynSkirtingFinish = "踢腳板塗層";
        // 快取已處理的房間，避免重複處理
        private readonly HashSet<long> _processedRoomIds = new HashSet<long>();

        public ValueWriter(UIDocument uidoc) { _uidoc = uidoc; }

        public void UpdateValues(FinishSettings settings)
        {
            if (!settings.SetValuesForGeometry && !settings.SetValuesForRooms)
                return; // 沒有要執行的操作

            var rooms = GetValidRooms(settings.TargetRoomIds).ToList();
            var results = new ProcessingResults();
            var roomIds = rooms.Select(r => RevitCompat.GetElementIdValue(r.Id)).ToHashSet();
            var overlapIssues = DetectRoomOverlapIssues(rooms);
            var unsafeRoomIds = new HashSet<long>(overlapIssues.SelectMany(x => x.RoomIds));

            foreach (var issue in overlapIssues.Take(20))
                results.AddError("房間重疊檢查", issue.Description);

            if (overlapIssues.Count > 20)
                results.AddError("房間重疊檢查", $"另有 {overlapIssues.Count - 20} 筆房間重疊風險未列出；已略過相關房間的參數回寫。");

            var roomElements = BuildRoomElementIndex(roomIds, results);

            foreach (var room in rooms)
            {
                var roomId = RevitCompat.GetElementIdValue(room.Id);
                if (_processedRoomIds.Contains(roomId))
                    continue; // 跳過已處理的房間

                if (unsafeRoomIds.Contains(roomId))
                {
                    results.AddError(room.Number, "偵測到此房間與其他房間有重疊風險，已略過房間與裝修元素參數回寫，避免誤改參數值。");
                    continue;
                }

                roomElements.TryGetValue(roomId, out var elems);
                elems ??= new List<Element>();

                try
                {
                    if (settings.SetValuesForGeometry) 
                    {
                        WriteRoomValuesToElements(room, elems);
                        results.SuccessCount++;
                    }
                    
                    if (settings.SetValuesForRooms) 
                    {
                        if (settings.GenerateGeometry)
                            WriteActualFinishParamsToRoom(room, settings, elems);
                        else
                            WriteDynamoReferenceParamsToRoom(room, settings);
                        WriteGeometrySummaryToRoom(room, elems);
                        results.SuccessCount++;
                    }

                    _processedRoomIds.Add(roomId);
                }
                catch (Exception ex)
                {
                    results.AddError(room.Number, ex.Message);
                }
            }

            ShowUpdateResults(results);
        }

        private IEnumerable<Room> GetValidRooms(IList<ElementId> targetRoomIds)
        {
            if (targetRoomIds != null && targetRoomIds.Count > 0)
            {
                return targetRoomIds
                    .Select(id => Doc.GetElement(id))
                    .OfType<Room>()
                    .Where(r => r.Area > 0);
            }

            return new FilteredElementCollector(Doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0);
        }

        private void ShowUpdateResults(ProcessingResults results)
        {
            if (results.Errors.Any())
            {
                var message = new StringBuilder();
                message.AppendLine($"參數更新完成: {results.SuccessCount} 成功");
                message.AppendLine($"錯誤數量: {results.Errors.Count}");
                message.AppendLine("\n錯誤詳情:");
                
                foreach (var error in results.Errors.Take(5))
                {
                    message.AppendLine($"- 房間 {error.roomNumber}: {error.message}");
                }
                
                if (results.Errors.Count > 5)
                {
                    message.AppendLine($"... 還有 {results.Errors.Count - 5} 個錯誤");
                }
                
                TaskDialog.Show("參數更新結果", message.ToString());
            }
        }

        private Dictionary<long, List<Element>> BuildRoomElementIndex(ISet<long> roomIds, ProcessingResults results)
        {
            var index = new Dictionary<long, List<Element>>();

            // 建立 roomId → Room 反向查找，供沒有 AR_RoomId 標記的元素（如面生面）進行空間回查備援
            var roomLookup = new Dictionary<long, Room>();
            foreach (var rid in roomIds)
            {
                if (Doc.GetElement(RevitCompat.CreateElementId(rid)) is Room r && r.Area > 0)
                    roomLookup[rid] = r;
            }
            var allRoomsForSpatial = roomLookup.Values.ToList();

            foreach (var category in FinishCategories)
            {
                var catElems = new FilteredElementCollector(Doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var elem in catElems)
                {
                    long roomId = GetElementRoomId(elem);

                    // 若元素沒有寫入 AR_RoomId（例如面生面產生的元素），
                    // 以幾何中心點進行空間回查，並同時補寫 AR_RoomId 供下次使用
                    if ((roomId <= 0 || !roomIds.Contains(roomId)) && allRoomsForSpatial.Count > 0 && FinishingElementGuard.IsSpatialLookupCandidate(elem))
                    {
                        var spatialRoomId = FindRoomIdSpatially(elem, allRoomsForSpatial, out var matchedRooms);
                        if (spatialRoomId > 0 && roomIds.Contains(spatialRoomId))
                        {
                            WriteRoomIdToElement(elem, spatialRoomId);
                            roomId = spatialRoomId;
                        }
                        else if (matchedRooms.Count > 1)
                        {
                            var labels = string.Join("、", matchedRooms.Take(4).Select(FormatRoomLabel));
                            if (matchedRooms.Count > 4)
                                labels += $"…等 {matchedRooms.Count} 間";
                            results.AddError("空間回查", $"元素 {elem.Id} 的中心點同時落在多間房間（{labels}），已略過 AR_RoomId 補寫與參數更新。");
                        }
                    }

                    if (roomId <= 0 || !roomIds.Contains(roomId))
                        continue;

                    if (!IsFinishElementForRoomIndex(elem))
                        continue;

                    if (!index.TryGetValue(roomId, out var list))
                    {
                        list = new List<Element>();
                        index[roomId] = list;
                    }

                    list.Add(elem);
                }
            }

            return index;
        }

        /// <summary>
        /// 以元素的幾何中心點對目標房間清單進行空間查詢。
        /// 用於補全沒有寫入 AR_RoomId 的面生面元素；找不到或命中多間房時回傳 0。
        /// </summary>
        private long FindRoomIdSpatially(Element element, IList<Room> rooms, out List<Room> matchedRooms)
        {
            matchedRooms = new List<Room>();
            try
            {
                XYZ testPoint = null;

                if (element.Location is LocationPoint lp)
                    testPoint = lp.Point;
                else if (element.Location is LocationCurve lc)
                    testPoint = lc.Curve.Evaluate(0.5, true); // 曲線中點
                else
                {
                    var bbox = element.get_BoundingBox(null);
                    if (bbox != null)
                        testPoint = (bbox.Min + bbox.Max) * 0.5;
                }

                if (testPoint == null) return 0;

                foreach (var room in rooms)
                {
                    if (IsPointInRoomSafe(room, testPoint))
                        matchedRooms.Add(room);
                }

                return matchedRooms.Count == 1
                    ? RevitCompat.GetElementIdValue(matchedRooms[0].Id)
                    : 0;
            }
            catch { }
            return 0;
        }

        private List<RoomOverlapIssue> DetectRoomOverlapIssues(IList<Room> rooms)
        {
            var issues = new List<RoomOverlapIssue>();
            if (rooms == null || rooms.Count < 2)
                return issues;

            var roomSamples = rooms
                .Select(r => new { Room = r, Id = RevitCompat.GetElementIdValue(r.Id), Points = GetRoomProbePoints(r).ToList() })
                .Where(x => x.Points.Count > 0)
                .ToList();

            var reportedKeys = new HashSet<string>();

            foreach (var sample in roomSamples)
            {
                foreach (var point in sample.Points)
                {
                    var matched = roomSamples
                        .Where(x => IsPointInRoomSafe(x.Room, point))
                        .Select(x => x.Room)
                        .ToList();

                    if (matched.Count <= 1)
                        continue;

                    var ids = matched.Select(r => RevitCompat.GetElementIdValue(r.Id)).OrderBy(x => x).ToList();
                    var key = string.Join("|", ids);
                    if (!reportedKeys.Add(key))
                        continue;

                    issues.Add(new RoomOverlapIssue
                    {
                        Description = $"偵測到房間重疊或空間歸屬不唯一：{string.Join("、", matched.Select(FormatRoomLabel))}。相關房間已禁止自動回寫參數。"
                    });
                    foreach (var id in ids)
                        issues.Last().RoomIds.Add(id);
                }
            }

            return issues;
        }

        private IEnumerable<XYZ> GetRoomProbePoints(Room room)
        {
            if (room == null)
                yield break;

            if (room.Location is LocationPoint lp)
                yield return lp.Point;

            var bbox = room.get_BoundingBox(null);
            if (bbox == null)
                yield break;

            var min = bbox.Min;
            var max = bbox.Max;
            var z = (min.Z + max.Z) * 0.5;
            var x1 = min.X + (max.X - min.X) * 0.25;
            var x2 = min.X + (max.X - min.X) * 0.50;
            var x3 = min.X + (max.X - min.X) * 0.75;
            var y1 = min.Y + (max.Y - min.Y) * 0.25;
            var y2 = min.Y + (max.Y - min.Y) * 0.50;
            var y3 = min.Y + (max.Y - min.Y) * 0.75;

            yield return new XYZ(x2, y2, z);
            yield return new XYZ(x1, y1, z);
            yield return new XYZ(x1, y3, z);
            yield return new XYZ(x3, y1, z);
            yield return new XYZ(x3, y3, z);
        }

        private static bool IsPointInRoomSafe(Room room, XYZ point)
        {
            try
            {
                return room != null && point != null && room.IsPointInRoom(point);
            }
            catch
            {
                return false;
            }
        }

        private static string FormatRoomLabel(Room room)
        {
            if (room == null)
                return "未知房間";

            var number = string.IsNullOrWhiteSpace(room.Number) ? "(無編號)" : room.Number;
            var name = string.IsNullOrWhiteSpace(room.Name) ? "(無名稱)" : room.Name;
            return $"{number} {name}";
        }

        /// <summary>
        /// 將房間 ID 補寫回元素的 AR_RoomId 參數（空間回查後補標記，避免下次重複空間查詢）。
        /// </summary>
        private static void WriteRoomIdToElement(Element element, long roomIdLong)
        {
            try
            {
                if (!FinishingElementGuard.IsManagedFinishingElement(element))
                    return;

                var p = element.LookupParameter("房間ID(AR_RoomId)")
                     ?? element.LookupParameter("房間ID")
                     ?? element.LookupParameter("AR_RoomId");
                if (p == null || p.IsReadOnly) return;

                if (p.StorageType == StorageType.Integer)
                {
                    if (roomIdLong >= int.MinValue && roomIdLong <= int.MaxValue)
                        p.Set((int)roomIdLong);
                }
                else if (p.StorageType == StorageType.String)
                {
                    p.Set(roomIdLong.ToString());
                }
            }
            catch { }
        }

        void WriteRoomValuesToElements(Room room, List<Element> elems)
        {
            if (elems == null || elems.Count == 0)
                return;

            foreach (var e in elems)
            {
                try
                {
                    var roomNumberParam = LookupAnyParameter(e, P_RoomNumbers, P_LegacyZhRoomNumbers, P_LegacyRoomNumbers);
                    var roomNameParam = LookupAnyParameter(e, P_RoomNames, P_LegacyZhRoomNames, P_LegacyRoomNames);
                    
                    if (roomNumberParam != null && !roomNumberParam.IsReadOnly)
                        SetStringIfChanged(roomNumberParam, room.Number ?? "");
                    
                    if (roomNameParam != null && !roomNameParam.IsReadOnly)
                        SetStringIfChanged(roomNameParam, BuildRoomDisplayName(room));
                }
                catch (Exception ex)
                {
                    // 記錄單個元素的錯誤，但不中斷整個處理流程
                    System.Diagnostics.Debug.WriteLine($"設定元素 {e.Id} 參數失敗: {ex.Message}");
                }
            }
        }

        void WriteGeometrySummaryToRoom(Room room, List<Element> elems)
        {
            var sb = new StringBuilder();

            // 房間基本面積（Revit 以 Finish 邊界計算，為淨使用面積）
            double roomAreaM2 = room.Area * 0.092903;
            sb.AppendLine($"房間 {room.Number} 裝修摘要:");
            sb.AppendLine($"房間面積: {roomAreaM2:F2} m²");

            if (!elems.Any())
            {
                sb.AppendLine("（無關聯的裝修元素）");
                var summaryParam2 = room.LookupParameter(P_Summary);
                if (summaryParam2 != null && !summaryParam2.IsReadOnly)
                    SetStringIfChanged(summaryParam2, sb.ToString());
                return;
            }

            // 依類別分組統計面積
            var groups = elems.GroupBy(e => GetElementDisplayName(e));
            double floorAreaM2 = 0;

            foreach (var g in groups)
            {
                double areaSum = 0;
                foreach (var e in g)
                {
                    try
                    {
                        // 只取面積參數，不混入周長（HOST_PERIMETER_COMPUTED 單位為 ft，不可與 ft² 加總）
                        var pArea = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        if (pArea != null && pArea.StorageType == StorageType.Double)
                            areaSum += pArea.AsDouble();
                    }
                    catch
                    {
                        // 忽略無法取得面積的元素
                    }
                }

                double areaSumM2 = areaSum * 0.092903;
                sb.AppendLine($"- {g.Key}: {g.Count()} 個, 面積 {areaSumM2:F2} m²");

                // 收集地板面積以交叉驗證
                if (g.Key.Contains("樓板") || g.Key.Contains("Floor"))
                    floorAreaM2 += areaSumM2;
            }

            // 交叉驗證：樓板面積應與房間面積接近（容許 ±1%）
            if (floorAreaM2 > 0)
            {
                double diff = Math.Abs(floorAreaM2 - roomAreaM2);
                double tolerance = roomAreaM2 * 0.01;
                if (diff > tolerance)
                    sb.AppendLine($"⚠ 面積差異: 房間 {roomAreaM2:F2} m² vs 樓板 {floorAreaM2:F2} m²（差 {diff:F3} m²）");
                else
                    sb.AppendLine($"✓ 面積驗證通過（差 {diff:F3} m²）");
            }

            var summaryParameter = room.LookupParameter(P_Summary);
            if (summaryParameter != null && !summaryParameter.IsReadOnly)
                SetStringIfChanged(summaryParameter, sb.ToString());
        }

        void WriteDynamoReferenceParamsToRoom(Room room, FinishSettings settings)
        {
            var roomOverride = settings.GetRoomOverride(room.Id);

            var wallTypeId = roomOverride != null && roomOverride.WallTypeId > 0
                ? RevitCompat.CreateElementId(roomOverride.WallTypeId)
                : settings.SelectedWallTypeId;

            var floorTypeId = roomOverride != null && roomOverride.FloorTypeId > 0
                ? RevitCompat.CreateElementId(roomOverride.FloorTypeId)
                : settings.SelectedFloorTypeId;

            var ceilingHeightMm = roomOverride != null && roomOverride.CeilingHeightMm > 0
                ? roomOverride.CeilingHeightMm
                : settings.CeilingHeightMm;

            var wallFinishName = GetTypeName(wallTypeId);
            var floorFinishName = GetTypeName(floorTypeId);
            var ceilingTypeId = roomOverride != null && roomOverride.CeilingTypeId > 0
                ? RevitCompat.CreateElementId(roomOverride.CeilingTypeId)
                : settings.SelectedCeilingTypeId;
            var ceilingFinishName = GetTypeName(ceilingTypeId);
            var ceilingHeightInternal = settings.MmToInternalUnits(ceilingHeightMm);

            var skirtingTypeId = roomOverride != null && roomOverride.SkirtingTypeId > 0
                ? RevitCompat.CreateElementId(roomOverride.SkirtingTypeId)
                : settings.SelectedSkirtingTypeId;
            var skirtingFinishName = GetTypeName(skirtingTypeId);

            var wallFinishParam = LookupRoomParameter(room, P_DynWallFinish);
            if (wallFinishParam != null && !wallFinishParam.IsReadOnly)
                SetStringIfChanged(wallFinishParam, wallFinishName ?? string.Empty);

            var floorFinishParam = LookupRoomParameter(room, P_DynFloorFinish);
            if (floorFinishParam != null && !floorFinishParam.IsReadOnly)
                SetStringIfChanged(floorFinishParam, floorFinishName ?? string.Empty);

            var ceilingFinishParam = LookupRoomParameter(room, P_DynCeilingFinish);
            if (ceilingFinishParam != null && !ceilingFinishParam.IsReadOnly)
                SetStringIfChanged(ceilingFinishParam, ceilingFinishName ?? string.Empty);

            var ceilingHeightParam = LookupRoomParameter(room, P_DynCeilingHeight);
            if (ceilingHeightParam != null && !ceilingHeightParam.IsReadOnly)
                SetDoubleIfChanged(ceilingHeightParam, ceilingHeightInternal);

            var skirtingFinishParam = LookupRoomParameter(room, P_DynSkirtingFinish);
            if (skirtingFinishParam != null && !skirtingFinishParam.IsReadOnly)
                SetStringIfChanged(skirtingFinishParam, skirtingFinishName ?? string.Empty);
        }

        void WriteActualFinishParamsToRoom(Room room, FinishSettings settings, List<Element> elems)
        {
            var actual = GetActualFinishInfo(elems);

            var roomOverride = settings.GetRoomOverride(room.Id);

            var wallTypeId = roomOverride != null && roomOverride.WallTypeId > 0
                ? RevitCompat.CreateElementId(roomOverride.WallTypeId)
                : settings.SelectedWallTypeId;
            var floorTypeId = roomOverride != null && roomOverride.FloorTypeId > 0
                ? RevitCompat.CreateElementId(roomOverride.FloorTypeId)
                : settings.SelectedFloorTypeId;
            var ceilingTypeId = roomOverride != null && roomOverride.CeilingTypeId > 0
                ? RevitCompat.CreateElementId(roomOverride.CeilingTypeId)
                : settings.SelectedCeilingTypeId;
            var skirtingTypeId = roomOverride != null && roomOverride.SkirtingTypeId > 0
                ? RevitCompat.CreateElementId(roomOverride.SkirtingTypeId)
                : settings.SelectedSkirtingTypeId;

            // 生成成功用實際值；生成失敗則保留本次設定值，避免被清空。
            var wallName = !string.IsNullOrWhiteSpace(actual.WallFinishName) ? actual.WallFinishName : GetTypeName(wallTypeId);
            var floorName = !string.IsNullOrWhiteSpace(actual.FloorFinishName) ? actual.FloorFinishName : GetTypeName(floorTypeId);
            var ceilingName = !string.IsNullOrWhiteSpace(actual.CeilingFinishName) ? actual.CeilingFinishName : GetTypeName(ceilingTypeId);
            var skirtingName = !string.IsNullOrWhiteSpace(actual.SkirtingFinishName) ? actual.SkirtingFinishName : GetTypeName(skirtingTypeId);

            SetRoomString(room, P_DynWallFinish, wallName);
            SetRoomString(room, P_DynFloorFinish, floorName);
            SetRoomString(room, P_DynCeilingFinish, ceilingName);
            SetRoomString(room, P_DynSkirtingFinish, skirtingName);

            var ceilingHeightParam = LookupRoomParameter(room, P_DynCeilingHeight);
            if (ceilingHeightParam != null && !ceilingHeightParam.IsReadOnly)
            {
                var value = !string.IsNullOrWhiteSpace(ceilingName)
                    ? settings.MmToInternalUnits(GetRoomCeilingHeightMm(room, settings))
                    : 0.0;
                SetDoubleIfChanged(ceilingHeightParam, value);
            }
        }

        private FinishInfo GetActualFinishInfo(List<Element> elems)
        {
            var info = new FinishInfo();
            if (elems == null || elems.Count == 0)
                return info;

            var wallFinishNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var floorFinishNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ceilingFinishNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skirtingFinishNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var elem in elems)
            {
                if (elem?.Category == null)
                    continue;

                var categoryId = RevitCompat.GetElementIdValue(elem.Category.Id);
                var typeName = GetTypeName(elem.GetTypeId());

                if (categoryId == (int)BuiltInCategory.OST_Walls)
                {
                    if (IsSkirtingWall(elem))
                    {
                        if (!string.IsNullOrWhiteSpace(typeName))
                            skirtingFinishNames.Add(typeName);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(typeName))
                            wallFinishNames.Add(typeName);
                    }
                }
                else if (categoryId == (int)BuiltInCategory.OST_Floors)
                {
                    if (!string.IsNullOrWhiteSpace(typeName))
                        floorFinishNames.Add(typeName);
                }
                else if (categoryId == (int)BuiltInCategory.OST_Ceilings)
                {
                    if (!string.IsNullOrWhiteSpace(typeName))
                        ceilingFinishNames.Add(typeName);
                }
            }

            info.WallFinishName = string.Join("、", wallFinishNames.OrderBy(x => x));
            info.FloorFinishName = string.Join("、", floorFinishNames.OrderBy(x => x));
            info.CeilingFinishName = string.Join("、", ceilingFinishNames.OrderBy(x => x));
            info.SkirtingFinishName = string.Join("、", skirtingFinishNames.OrderBy(x => x));

            return info;
        }

        private double GetRoomCeilingHeightMm(Room room, FinishSettings settings)
        {
            var roomOverride = settings.GetRoomOverride(room.Id);
            return roomOverride != null && roomOverride.CeilingHeightMm > 0
                ? roomOverride.CeilingHeightMm
                : settings.CeilingHeightMm;
        }

        private void SetRoomString(Room room, string parameterName, string value)
        {
            var parameter = LookupRoomParameter(room, parameterName);
            if (parameter != null && !parameter.IsReadOnly)
                SetStringIfChanged(parameter, value ?? string.Empty);
        }

        private static Parameter LookupRoomParameter(Room room, string preferredName, params string[] fallbackNames)
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

        private static bool IsFinishElementForRoomIndex(Element element)
        {
            if (element == null)
                return false;

            return FinishingElementGuard.IsManagedFinishingElement(element);
        }

        private static bool IsSpatialRoomLookupCandidate(Element element)
        {
            if (element == null)
                return false;

            return FinishingElementGuard.IsSpatialLookupCandidate(element);
        }

        private static bool IsNonRoomBoundingWall(Element element)
        {
            try
            {
                var roomBounding = element.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                return roomBounding != null && roomBounding.AsInteger() == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSkirtingWall(Element element)
        {
            try
            {
                var height = element.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble()
                          ?? element.get_Parameter(BuiltInParameter.WALL_ATTR_HEIGHT_PARAM)?.AsDouble()
                          ?? 0.0;
                return height > 0 && height <= (500.0 / 304.8);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFinishNameMarker(Element element)
        {
            try
            {
                var name = element.Name ?? string.Empty;
                var typeName = element.Document?.GetElement(element.GetTypeId())?.Name ?? string.Empty;
                var combined = $"{name} {typeName}";
                return FinishingElementGuard.HasFinishNameMarker(element);
            }
            catch
            {
                return false;
            }
        }

        private class FinishInfo
        {
            public string WallFinishName { get; set; } = string.Empty;
            public string FloorFinishName { get; set; } = string.Empty;
            public string CeilingFinishName { get; set; } = string.Empty;
            public string SkirtingFinishName { get; set; } = string.Empty;
        }

        private static Parameter LookupAnyParameter(Element element, params string[] names)
        {
            foreach (var name in names)
            {
                var parameter = element.LookupParameter(name);
                if (parameter != null)
                    return parameter;
            }

            return null;
        }

        private static int GetElementRoomId(Element element)
        {
            var parameter = LookupAnyParameter(element, P_RoomId, P_LegacyZhRoomId, P_LegacyRoomId);
            if (parameter == null)
                return 0;

            return parameter.StorageType switch
            {
                StorageType.Integer => parameter.AsInteger(),
                StorageType.String => int.TryParse(parameter.AsString(), out var parsed) ? parsed : 0,
                _ => 0
            };
        }

        private static void SetStringIfChanged(Parameter parameter, string value)
        {
            var current = parameter.AsString() ?? string.Empty;
            var target = value ?? string.Empty;
            if (!string.Equals(current, target, StringComparison.Ordinal))
                parameter.Set(target);
        }

        private static void SetDoubleIfChanged(Parameter parameter, double value)
        {
            if (Math.Abs(parameter.AsDouble() - value) > 1e-9)
                parameter.Set(value);
        }

        private static string BuildRoomDisplayName(Room room)
        {
            var name = room?.Name ?? string.Empty;
            var number = room?.Number ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            if (string.IsNullOrWhiteSpace(number))
                return name.Trim();

            var trimmedName = name.Trim();
            var trimmedNumber = number.Trim();

            if (trimmedName.EndsWith(trimmedNumber, StringComparison.OrdinalIgnoreCase))
            {
                var pureName = trimmedName.Substring(0, trimmedName.Length - trimmedNumber.Length).TrimEnd();
                pureName = pureName.TrimEnd('-', '_', '(', ')', '（', '）', ' ');
                if (!string.IsNullOrWhiteSpace(pureName))
                    return pureName;
            }

            return trimmedName;
        }

        private string GetTypeName(ElementId typeId)
        {
            if (typeId == null || typeId == ElementId.InvalidElementId)
                return string.Empty;

            // 回寫房間參數時保留完整類型名稱，避免同代號（如 W1 / W1-A）造成回讀歧義。
            return Doc.GetElement(typeId)?.Name ?? string.Empty;
        }

        private string GetElementDisplayName(Element element)
        {
            try
            {
                var categoryName = element.Category?.Name ?? "未知類別";
                var typeName = Doc.GetElement(element.GetTypeId())?.Name ?? "未知類型";
                return $"{categoryName} | {typeName}";
            }
            catch
            {
                return "未知元素";
            }
        }

        // --- Shared Parameters with ForgeTypeId (Revit 2024+) ---
        public void EnsureSharedParameters()
        {
            var app = Doc.Application;
            string spFile = app.SharedParametersFilename;
            if (string.IsNullOrWhiteSpace(spFile) || !File.Exists(spFile))
            {
                spFile = Path.Combine(Path.GetTempPath(), "AR_Finishings_SharedParameters.txt");
                if (!File.Exists(spFile)) File.WriteAllText(spFile, "# AR Finishings Shared Parameters");
                app.SharedParametersFilename = spFile;
            }

            var defFile = app.OpenSharedParameterFile();
            var group = defFile.Groups.get_Item(GROUP_NAME) ?? defFile.Groups.Create(GROUP_NAME);

            var defRoomId = GetOrCreateDefinition(P_RoomId, SpecTypeId.String.Text, group, Doc.ParameterBindings);
            var defRoomNames = GetOrCreateDefinition(P_RoomNames, SpecTypeId.String.Text, group, Doc.ParameterBindings);
            var defRoomNumbers = GetOrCreateDefinition(P_RoomNumbers, SpecTypeId.String.Text, group, Doc.ParameterBindings);
            var defSummary = GetOrCreateDefinition(P_Summary, SpecTypeId.String.Text, group, Doc.ParameterBindings);
            var defDynWallFinish = GetOrCreateDefinition(P_DynWallFinish, SpecTypeId.String.Text, group, Doc.ParameterBindings);
            var defDynFloorFinish = GetOrCreateDefinition(P_DynFloorFinish, SpecTypeId.String.Text, group, Doc.ParameterBindings);
            var defDynCeilingFinish = GetOrCreateDefinition(P_DynCeilingFinish, SpecTypeId.String.Text, group, Doc.ParameterBindings);
            var defDynCeilingHeight = GetOrCreateDefinition(P_DynCeilingHeight, SpecTypeId.Length, group, Doc.ParameterBindings);
            var defDynSkirtingFinish = GetOrCreateDefinition(P_DynSkirtingFinish, SpecTypeId.String.Text, group, Doc.ParameterBindings);

            Action bindAction = () =>
            {
                // finish elements
                var catsFinish = new CategorySet();
                catsFinish.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls));
                catsFinish.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Floors));
                catsFinish.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Ceilings));
                catsFinish.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel));

                var inst = app.Create.NewInstanceBinding(catsFinish);
                var map = Doc.ParameterBindings;
                BindOrRebind(map, defRoomId, inst, GroupTypeId.IdentityData);
                BindOrRebind(map, defRoomNames, inst, GroupTypeId.IdentityData);
                BindOrRebind(map, defRoomNumbers, inst, GroupTypeId.IdentityData);

                // rooms
                var catsRoom = new CategorySet();
                catsRoom.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms));
                var instRoom = app.Create.NewInstanceBinding(catsRoom);
                BindOrRebind(map, defSummary, instRoom, GroupTypeId.IdentityData);
                BindOrRebind(map, defDynWallFinish, instRoom, GroupTypeId.IdentityData);
                BindOrRebind(map, defDynFloorFinish, instRoom, GroupTypeId.IdentityData);
                BindOrRebind(map, defDynCeilingFinish, instRoom, GroupTypeId.IdentityData);
                BindOrRebind(map, defDynCeilingHeight, instRoom, GroupTypeId.IdentityData);
                BindOrRebind(map, defDynSkirtingFinish, instRoom, GroupTypeId.IdentityData);
            };

            if (Doc.IsModifiable)
            {
                bindAction();
            }
            else
            {
                using (var t = new Transaction(Doc, "Bind AR Parameters"))
                {
                    t.Start();
                    bindAction();
                    t.Commit();
                }
            }
        }

        private static void BindOrRebind(BindingMap map, Definition definition, Binding binding, ForgeTypeId groupTypeId)
        {
            if (map.Insert(definition, binding, groupTypeId))
                return;

            map.ReInsert(definition, binding, groupTypeId);
        }

        private static Definition GetOrCreateDefinition(string name, ForgeTypeId specTypeId, DefinitionGroup group, BindingMap bindings)
        {
            var existing = FindBoundDefinitionByName(bindings, name);
            if (existing != null)
                return existing;

            return group.Definitions.get_Item(name)
                ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(name, specTypeId));
        }

        private static Definition FindBoundDefinitionByName(BindingMap bindings, string name)
        {
            var it = bindings.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                var definition = it.Key;
                if (definition != null && string.Equals(definition.Name, name, StringComparison.Ordinal))
                    return definition;
            }

            return null;
        }
    }
}
