using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System.Text;

namespace YD_RevitTools.LicenseManager.Helpers.AR.Finishings
{
    public class GenerationResults
    {
        public Dictionary<string, int> SuccessCount { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> FailureCount { get; } = new Dictionary<string, int>();
        public List<(Room room, string message, Exception ex)> Errors { get; } = new List<(Room, string, Exception)>();
        
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

    public class JoinResults  
    {
        public int TotalAttempts { get; set; }
        public int SuccessCount { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    public class GeometryGenerator
    {
        private readonly UIDocument _uidoc;
        private Document Doc => _uidoc.Document;

        // 單位轉換常數
        private const double MM_TO_FEET = 304.8;

        /// <summary>
        /// 將毫米轉換為Revit內部單位（英尺），直接使用固定轉換
        /// </summary>
        private double ConvertFromMm(double mm)
        {
            // 直接轉換為英尺（Revit內部單位）
            return mm / MM_TO_FEET;
        }

        /// <summary>
        /// 將Revit內部單位（英尺）轉換為毫米
        /// </summary>
        private double ConvertToMm(double internalUnits)
        {
            // 直接從英尺轉換為毫米
            return internalUnits * MM_TO_FEET;
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

        public void GenerateForRooms(FinishSettings settings)
        {
            var rooms = GetRooms(settings.TargetRoomIds);
            var results = new GenerationResults();
            
            foreach (var room in rooms)
            {
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
            var roomNumber = room.Number;
            
            if (settings.SelectedFloorTypeId != ElementId.InvalidElementId) 
            {
                var success = TryCreateFloor(room, settings);
                results.RecordOperation("地板", roomNumber, success);
            }
            
            if (settings.SelectedCeilingTypeId != ElementId.InvalidElementId) 
            {
                var success = TryCreateCeiling(room, settings);
                results.RecordOperation("天花板", roomNumber, success);
            }
            
            if (settings.SelectedWallTypeId != ElementId.InvalidElementId) 
            {
                var success = TryCreateWallFinish(room, settings);
                results.RecordOperation("牆面", roomNumber, success);
            }
            
            if (settings.SelectedSkirtingTypeId != ElementId.InvalidElementId) 
            {
                var success = TryCreateSkirting(room, settings);
                results.RecordOperation("踢腳板", roomNumber, success);
            }
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
                Logger.Log($"取得 {boundarySegments.Count} 個邊界循環");

                if (boundarySegments.Count == 0)
                {
                    Logger.Log("警告：房間沒有邊界段");
                    return curveArray;
                }

                // 取得主要邊界（通常是第一個，最大的循環）
                var mainBoundary = boundarySegments[0];
                Logger.Log($"主要邊界有 {mainBoundary.Count} 個段");

                foreach (var seg in mainBoundary)
                {
                    try
                    {
                        var curve = seg.GetCurve();
                        if (curve != null && curve.Length > 1e-6) // 過濾掉極短的曲線
                        {
                            curveArray.Append(curve);
                            originalLoop.Append(curve);
                            
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

        bool TryCreateFloor(Room room, FinishSettings settings)
        {
            try
            {
                Logger.Log($"開始為房間 {room.Name} 建立樓板");
                
                Level level; double halfT; CurveLoop loop;
                var profile = GetRoomProfile(room, settings.BoundaryMode, out level, out halfT, out loop);
                var ft = GetFloorType(settings.SelectedFloorTypeId);
                if (ft == null) 
                {
                    Logger.Log($"找不到樓板類型 ID: {settings.SelectedFloorTypeId}");
                    return false;
                }

                Logger.Log($"使用樓板類型: {ft.Name}");

                // 確保輪廓是有效的
                if (profile.Size < 3) 
                {
                    Logger.Log($"房間輪廓點數不足: {profile.Size}");
                    return false;
                }
                
                var curves = profile.Cast<Curve>().ToList();
                if (!curves.Any()) return false;
                
                var loops = new List<CurveLoop> { CurveLoop.Create(curves) };
                Logger.Log($"建立樓板曲線環，包含 {curves.Count} 條曲線");
                
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
                Level level; double halfT; CurveLoop loop;
                var profile = GetRoomProfile(room, FloorBoundaryMode.InnerFinish, out level, out halfT, out loop);
                
                Logger.Log($"取得房間輪廓，包含 {profile.Size} 條曲線");
                
                // 驗證輪廓有效性
                if (profile.Size < 3)
                {
                    Logger.Log($"房間輪廓曲線數量不足: {profile.Size}");
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

                // 建立天花板輪廓
                var curves = profile.Cast<Curve>().ToList();
                
                // 記錄輪廓詳細資訊
                for (int i = 0; i < curves.Count; i++)
                {
                    var curve = curves[i];
                    Logger.Log($"天花板曲線 {i}: 起點({curve.GetEndPoint(0).X * 304.8:F2}, {curve.GetEndPoint(0).Y * 304.8:F2}), " +
                              $"終點({curve.GetEndPoint(1).X * 304.8:F2}, {curve.GetEndPoint(1).Y * 304.8:F2}), " +
                              $"長度: {curve.Length * 304.8:F2} mm");
                }

                // 確保曲線形成封閉輪廓
                var loops = new List<CurveLoop> { CurveLoop.Create(curves) };
                
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
                Logger.Log($"開始為房間 {room.Name} 建立牆面裝修");
                Logger.Log($"房間編號: {room.Number}，房間面積: {room.Area * 0.092903:F2} m²");
                Logger.Log($"專案長度單位: {GetProjectLengthUnit()}");
                
                // 檢查房間的有效性
                if (room.Area <= 0)
                {
                    Logger.Log($"警告：房間 {room.Name} 面積為 0，跳過處理");
                    return false;
                }
                
                var level = GetLevel(room.LevelId);
                if (level == null) 
                {
                    Logger.Log("無法取得樓層資訊");
                    return false;
                }
                Logger.Log($"房間所在樓層: {level.Name}");
                
                double wallHeight = settings.MmToInternalUnits(settings.CeilingHeightMm + settings.WallOffsetMm);
                Logger.Log($"牆面高度: {wallHeight} 英尺 (天花板高度 {settings.CeilingHeightMm}mm + 偏移 {settings.WallOffsetMm}mm)");
                
                var wt = GetWallType(settings.SelectedWallTypeId);
                if (wt == null) 
                {
                    Logger.Log($"找不到牆類型 ID: {settings.SelectedWallTypeId}");
                    return false;
                }

                Logger.Log($"使用牆類型: {wt.Name}，牆厚度: {wt.Width * 304.8:F2}mm");

                // 正確讀取房間邊界範圍，建立粉刷牆定位線
                Logger.Log("開始讀取房間實際邊界範圍");
                
                // 使用房間的實際邊界範圍來建立粉刷牆定位線
                var finishWallCurves = GetFinishWallLocationLines(room);
                
                if (finishWallCurves == null || !finishWallCurves.Any())
                {
                    Logger.Log($"無法獲取房間 {room.Name} 的粉刷牆定位線");
                    return false;
                }
                
                Logger.Log($"成功建立 {finishWallCurves.Count} 條粉刷牆定位線");
                var curves = finishWallCurves;
                
                if (curves == null || !curves.Any())
                {
                    Logger.Log($"警告：房間 {room.Name} 沒有有效的邊界曲線");
                    return false;
                }
                
                Logger.Log($"成功獲取 {curves.Count} 條房間邊界曲線");
                
                // 驗證邊界曲線的完整性
                bool hasValidCurves = false;
                foreach (var curve in curves)
                {
                    if (curve != null && curve.Length > 1e-6)
                    {
                        hasValidCurves = true;
                        break;
                    }
                }
                
                if (!hasValidCurves)
                {
                    Logger.Log($"錯誤：房間 {room.Name} 的粉刷牆定位線都無效");
                    return false;
                }
                
                // 驗證定位線完整性
                if (!ValidateRoomBoundary(curves))
                {
                    Logger.Log($"警告：房間 {room.Name} 的粉刷牆定位線可能不完整，但仍嘗試創建牆面");
                }
                
                Logger.Log("粉刷牆定位線已準備完成，開始創建牆面");

                Logger.Log($"取得 {curves.Count} 條房間邊界曲線");
                
                var createdWalls = new List<Wall>();

                // 合併連續的直線段以減少牆面數量
                var mergedCurves = MergeContinuousLines(curves);
                Logger.Log($"原始 {curves.Count} 條曲線合併為 {mergedCurves.Count} 條");

                // 創建牆面
                foreach (var c in mergedCurves)
                {
                    if (c == null || c.Length < 1e-6) continue;

                    Logger.Log($"建立牆段，長度: {c.Length * MM_TO_FEET:F2} mm");
                    Logger.Log($"牆段起點: ({c.GetEndPoint(0).X * MM_TO_FEET:F2}, {c.GetEndPoint(0).Y * MM_TO_FEET:F2})");
                    Logger.Log($"牆段終點: ({c.GetEndPoint(1).X * MM_TO_FEET:F2}, {c.GetEndPoint(1).Y * MM_TO_FEET:F2})");

                    // 計算房間中心點用於方向判斷
                    XYZ roomCenter = CalculateRoomCenter(curves);
                    Logger.Log($"房間中心點: ({roomCenter.X * MM_TO_FEET:F2}, {roomCenter.Y * MM_TO_FEET:F2})");

                    // 創建牆面，offset=0 表示定位線在牆中心
                    var wall = Wall.Create(Doc, c, wt.Id, level.Id, wallHeight, 0, false, false);
                    if (wall != null)
                    {
                        Logger.Log($"牆面創建完成，ID: {wall.Id}");

                        // 設定牆面的基本屬性
                        SetFinishWallProperties(wall);

                        // 檢查並調整牆面方向，確保面向房間內部
                        AdjustWallOrientation(wall, roomCenter);

                        Logger.Log($"牆面 {wall.Id} 屬性和方向設定完成");

                        TagRoomOnElement(wall, room);
                        createdWalls.Add(wall);
                        Logger.Log($"成功建立牆段，ID: {wall.Id}");
                    }
                    else
                    {
                        Logger.Log("牆段建立失敗，回傳 null");
                    }
                }

                // 自動接合相鄰的牆面
                if (createdWalls.Count > 1)
                {
                    Logger.Log("開始自動接合相鄰牆面");
                    JoinAdjacentWalls(createdWalls);
                }

                // 處理與結構牆的接合關係
                if (createdWalls.Any())
                {
                    Logger.Log("開始處理與結構牆的接合關係");
                    var allWalls = new FilteredElementCollector(Doc)
                        .OfCategory(BuiltInCategory.OST_Walls)
                        .WhereElementIsNotElementType()
                        .Cast<Wall>()
                        .Where(w => w.StructuralUsage != Autodesk.Revit.DB.Structure.StructuralWallUsage.NonBearing)
                        .ToList();
                    
                    foreach (var finishWall in createdWalls)
                    {
                        TryJoinWithStructuralWalls(finishWall, allWalls);
                    }
                }

                // 多次重新生成文檔以確保所有變更生效
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        Doc.Regenerate();
                        Logger.Log($"第 {i + 1} 次重新生成文檔完成");
                    }
                    catch (Exception regenEx)
                    {
                        Logger.Log($"第 {i + 1} 次重新生成文檔失敗: {regenEx.Message}");
                    }
                }

                Logger.Log($"完成房間 {room.Name} 牆面裝修建立，共 {createdWalls.Count} 面牆");
                return createdWalls.Any();
            }
            catch (Exception ex)
            { 
                Logger.Log($"建立牆面裝修異常: {ex.Message}");
                Logger.Log($"堆疊追蹤: {ex.StackTrace}");
                return false;
            }
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
                
                // 取得房間邊界曲線（不預先切割）
                var curves = GetBoundaryCurves(room, FloorBoundaryMode.InnerFinish);
                Logger.Log($"取得 {curves.Count} 條邊界曲線");

                var wt = GetWallType(settings.SelectedSkirtingTypeId);
                if (wt == null) 
                {
                    Logger.Log($"找不到踢腳板類型 ID: {settings.SelectedSkirtingTypeId}");
                    return false;
                }

                Logger.Log($"使用踢腳板類型: {wt.Name}");
                
                var createdWalls = new List<Wall>();
                
                // 合併連續的直線段以減少踢腳板數量
                var mergedCurves = MergeContinuousLines(curves);
                Logger.Log($"原始 {curves.Count} 條曲線合併為 {mergedCurves.Count} 條");
                
                // 創建踢腳板
                foreach (var c in mergedCurves)
                {
                    if (c == null || c.Length < 1e-6) continue;
                    
                    Logger.Log($"建立踢腳板段，長度: {c.Length * 304.8:F2} mm");
                    
                    var wall = Wall.Create(Doc, c, wt.Id, level.Id, height, 0, false, false);
                    if (wall != null)
                    {
                        // 設定踢腳板的基本屬性（包含房間邊界設定）
                        SetFinishWallProperties(wall);
                        
                        TagRoomOnElement(wall, room);
                        createdWalls.Add(wall);
                        Logger.Log($"成功建立踢腳板段，ID: {wall.Id}");
                    }
                }

                // 自動接合相鄰的踢腳板
                if (createdWalls.Count > 1)
                {
                    Logger.Log("開始自動接合相鄰踢腳板");
                    JoinAdjacentWalls(createdWalls);
                }

                // 如果設定要跳過開口，則對創建的踢腳板進行門窗切割
                if ((settings.SkipDoorsForSkirting || settings.SkipWindowsForSkirting) && createdWalls.Any())
                {
                    Logger.Log("開始進行踢腳板門窗開口切割");
                    CreateWallOpenings(room, createdWalls, settings.SkipDoorsForSkirting, settings.SkipWindowsForSkirting);
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
                if (opening.Category.Id.Value == (int)BuiltInCategory.OST_Doors)
                {
                    return 900.0 / 304.8; // 預設門寬 900mm
                }
                else if (opening.Category.Id.Value == (int)BuiltInCategory.OST_Windows)
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

            // 根據參數收集門和窗 - 使用更寬鬆的篩選條件
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

            var result = new List<Curve>();
            foreach (var c in curves)
            {
                var cuts = new List<(double a, double b)>(); // normalized

                foreach (var opening in openings)
                {
                    var lp = opening.Location as LocationPoint;
                    if (lp == null) continue;
                    
                    var point = lp.Point;
                    var proj = c.Project(point);
                    
                    // 放寬投影距離限制到300mm
                    if (proj == null || proj.Distance > (300.0 / 304.8)) continue;

                    // 獲取門窗寬度
                    double width = GetOpeningWidth(opening);
                    double clearance = 25.0 / 304.8; // 減少間距到25mm
                    double half = width * 0.5 + clearance;

                    // 計算切割範圍
                    double projParam = proj.Parameter;
                    double tMid = c.ComputeNormalizedParameter(projParam);
                    double tHalf = half / c.Length;

                    double a = Math.Max(0, tMid - tHalf);
                    double b = Math.Min(1, tMid + tHalf);
                    
                    // 確保切割範圍有效
                    if (b > a && (b - a) < 0.9) // 避免切掉整條線
                    {
                        cuts.Add((a, b));
                    }
                }

                if (cuts.Count == 0) { result.Add(c); continue; }

                cuts.Sort((x, y) => x.a.CompareTo(y.a));
                var merged = new List<(double a, double b)>();
                var cur = cuts[0];
                for (int i = 1; i < cuts.Count; i++)
                {
                    var n = cuts[i];
                    if (n.a <= cur.b) cur = (cur.a, Math.Max(cur.b, n.b));
                    else { merged.Add(cur); cur = n; }
                }
                merged.Add(cur);

                double prev = 0;
                foreach (var m in merged)
                {
                    if (m.a - prev > 1e-6)
                    {
                        var ra = c.ComputeRawParameter(prev);
                        var rb = c.ComputeRawParameter(m.a);
                        var seg = c.Clone();
                        seg.MakeBound(ra, rb);
                        if (seg.Length > 1e-6) result.Add(seg);
                    }
                    prev = m.b;
                }
                if (1 - prev > 1e-6)
                {
                    var ra = c.ComputeRawParameter(prev);
                    var rb = c.ComputeRawParameter(1);
                    var seg = c.Clone();
                    seg.MakeBound(ra, rb);
                    if (seg.Length > 1e-6) result.Add(seg);
                }
            }
            return result;
        }

        void CreateWallOpenings(Room room, List<Wall> walls, bool includeDoors = true, bool includeWindows = true)
        {
            try
            {
                Logger.Log($"開始為房間 {room.Name} 的牆面處理門窗開口");

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

                // 對每面牆進行開口處理
                foreach (var wall in walls)
                {
                    Logger.Log($"處理牆面 {wall.Id} 的開口");
                    var wallLocation = (wall.Location as LocationCurve)?.Curve;

                    if (wallLocation == null)
                    {
                        Logger.Log($"牆面 {wall.Id} 沒有位置曲線，跳過");
                        continue;
                    }

                    // 收集在這面牆上的門窗
                    var wallOpenings = new List<(FamilyInstance opening, double parameter, double width)>();

                    foreach (var opening in openings)
                    {
                        try
                        {
                            var openingLocation = (opening.Location as LocationPoint)?.Point;
                            if (openingLocation == null) continue;

                            // 檢查開口是否在這面牆上
                            var projection = wallLocation.Project(openingLocation);
                            if (projection != null)
                            {
                                var distance = projection.Distance * MM_TO_FEET;
                                var parameter = projection.Parameter;

                                // 檢查是否在牆上（距離小於牆厚的一半，且在牆段範圍內）
                                if (distance < wall.Width * 0.6 && parameter >= -0.01 && parameter <= 1.01)
                                {
                                    // 獲取門窗寬度
                                    double openingWidth = GetOpeningWidth(opening);
                                    wallOpenings.Add((opening, parameter, openingWidth));

                                    Logger.Log($"開口 {opening.Id} ({opening.Name}) 在牆面上，參數: {parameter:F3}, 寬度: {openingWidth * MM_TO_FEET:F2}mm");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"檢查開口 {opening.Id} 失敗: {ex.Message}");
                        }
                    }

                    // 如果這面牆上有門窗，嘗試編輯牆面輪廓以創建開口
                    if (wallOpenings.Any())
                    {
                        Logger.Log($"牆面 {wall.Id} 上有 {wallOpenings.Count} 個門窗，嘗試編輯輪廓");
                        TryEditWallProfileForOpenings(wall, wallOpenings);
                    }

                    // 嘗試與結構牆體接合
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
        /// 嘗試編輯牆面輪廓以創建門窗開口
        /// 注意：Revit 中門窗會自動在牆上創建開口，此方法主要用於診斷
        /// </summary>
        void TryEditWallProfileForOpenings(Wall wall, List<(FamilyInstance opening, double parameter, double width)> wallOpenings)
        {
            try
            {
                Logger.Log($"檢查牆面 {wall.Id} 上的 {wallOpenings.Count} 個門窗開口");

                foreach (var (opening, parameter, width) in wallOpenings)
                {
                    Logger.Log($"門窗 {opening.Id} ({opening.Name}) 在牆上，參數: {parameter:F3}, 寬度: {width * MM_TO_FEET:F2}mm");
                }

                // Revit 中的門窗會自動在牆面上創建開口
                // 我們只需確保牆面正確創建，門窗會自動處理開口
                Logger.Log("門窗開口由 Revit 自動處理");
            }
            catch (Exception ex)
            {
                Logger.Log($"檢查門窗開口失敗: {ex.Message}");
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
        /// 檢查兩面牆是否平行且重疊（專門用於結構牆與裝修牆的關係檢查）
        /// </summary>
        private bool AreWallsParallelAndOverlapping(Curve finishWallCurve, Curve structWallCurve)
        {
            try
            {
                // 獲取牆體方向向量
                var finishDir = (finishWallCurve.GetEndPoint(1) - finishWallCurve.GetEndPoint(0)).Normalize();
                var structDir = (structWallCurve.GetEndPoint(1) - structWallCurve.GetEndPoint(0)).Normalize();
                
                // 檢查是否平行（點積接近1或-1）
                var dotProduct = Math.Abs(finishDir.DotProduct(structDir));
                bool isParallel = dotProduct > 0.9; // 更嚴格的平行判斷
                
                if (!isParallel)
                {
                    return false;
                }
                
                // 檢查距離
                var distance1 = finishWallCurve.Distance(structWallCurve.GetEndPoint(0));
                var distance2 = finishWallCurve.Distance(structWallCurve.GetEndPoint(1));
                var minDistance = Math.Min(distance1, distance2);
                
                // 如果距離很近，檢查是否有重疊部分
                if (minDistance < 1.0) // 1英尺內認為可能重疊
                {
                    // 使用投影檢查重疊
                    var finishStart = finishWallCurve.GetEndPoint(0);
                    var finishEnd = finishWallCurve.GetEndPoint(1);
                    var structStart = structWallCurve.GetEndPoint(0);
                    var structEnd = structWallCurve.GetEndPoint(1);
                    
                    // 將結構牆端點投影到裝修牆線上
                    var projection1 = finishWallCurve.Project(structStart);
                    var projection2 = finishWallCurve.Project(structEnd);
                    
                    // 如果有投影點在裝修牆範圍內，認為重疊
                    return projection1.Distance < 0.5 || projection2.Distance < 0.5;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"檢查牆面平行重疊失敗: {ex.Message}");
                return false;
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
        private void SetFinishWallProperties(Wall wall)
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

                // 關鍵：設定牆定位線為核心面:內部
                try
                {
                    var locationLine = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
                    if (locationLine != null && !locationLine.IsReadOnly)
                    {
                        // 確保使用核心面:內部定位線
                        var coreInteriorValue = (int)WallLocationLine.CoreInterior;
                        locationLine.Set(coreInteriorValue);
                        Logger.Log($"✓ 已將粉刷牆 {wall.Id} 的定位線設為核心面:內部 (值: {coreInteriorValue})");
                        
                        // 驗證設定是否成功
                        var currentValue = locationLine.AsInteger();
                        if (currentValue == coreInteriorValue)
                        {
                            Logger.Log($"✓ 粉刷牆 {wall.Id} 定位線設定驗證成功");
                        }
                        else
                        {
                            Logger.Log($"⚠ 粉刷牆 {wall.Id} 定位線設定可能失敗，當前值: {currentValue}");
                        }
                    }
                    else
                    {
                        Logger.Log($"⚠ 無法設定粉刷牆 {wall.Id} 的定位線參數（參數為null或唯讀）");
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

        void JoinAdjacentWalls(List<Wall> walls)
        {
            try
            {
                Logger.Log($"開始智能接合 {walls.Count} 個牆面");
                int joinCount = 0;

                // 先允許所有牆面端點接合
                foreach (var wall in walls)
                {
                    try
                    {
                        WallUtils.AllowWallJoinAtEnd(wall, 0);
                        WallUtils.AllowWallJoinAtEnd(wall, 1);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"允許牆面 {wall.Id} 端點接合失敗: {ex.Message}");
                    }
                }

                // 使用更智能的接合方式
                for (int i = 0; i < walls.Count; i++)
                {
                    for (int j = i + 1; j < walls.Count; j++)
                    {
                        var wall1 = walls[i];
                        var wall2 = walls[j];

                        try
                        {
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
                                    }
                                    else
                                    {
                                        Logger.Log($"牆面 {wall1.Id} 和 {wall2.Id} 已經接合");
                                    }
                                }
                                catch (Exception joinEx)
                                {
                                    Logger.Log($"幾何接合失敗: {joinEx.Message}，嘗試使用端點接合");
                                    
                                    // 回退到端點接合
                                    try
                                    {
                                        WallUtils.AllowWallJoinAtEnd(wall1, 0);
                                        WallUtils.AllowWallJoinAtEnd(wall1, 1);
                                        WallUtils.AllowWallJoinAtEnd(wall2, 0);
                                        WallUtils.AllowWallJoinAtEnd(wall2, 1);
                                        Logger.Log($"設定牆面 {wall1.Id} 和 {wall2.Id} 端點接合");
                                    }
                                    catch (Exception endEx)
                                    {
                                        Logger.Log($"端點接合也失敗: {endEx.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"檢查牆面 {wall1.Id} 和 {wall2.Id} 關係失敗: {ex.Message}");
                        }
                    }
                }

                // 多次重新生成以確保接合生效
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        Doc.Regenerate();
                        Logger.Log($"執行第 {attempt + 1} 次文件重新生成");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"第 {attempt + 1} 次文件重新生成失敗: {ex.Message}");
                    }
                }

                Logger.Log($"完成智能牆面接合，成功接合了 {joinCount} 對牆面");
            }
            catch (Exception ex)
            {
                Logger.Log($"牆面接合異常: {ex.Message}");
            }
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
        bool ShouldJoinWalls(Wall wall1, Wall wall2, double tolerance = 0.1)
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
                Logger.Log("開始使用改良的門窗處理方式建立牆面");
                
                // 合併連續曲線
                var mergedCurves = MergeContinuousLines(curves);
                Logger.Log($"合併後有 {mergedCurves.Count} 條曲線");

                // 建立牆面
                foreach (var curve in mergedCurves)
                {
                    if (curve == null || curve.Length < 1e-6) continue;

                    try
                    {
                        // 建立牆面，確保牆面向房間內側
                        var wall = Wall.Create(Doc, curve, wallType.Id, level.Id, wallHeight, 0, false, false);
                        if (wall != null)
                        {
                            // 設定裝修牆面的基本屬性（包含房間邊界設定）
                            SetFinishWallProperties(wall);

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

                // 使用改良的門窗切割方式
                if (createdWalls.Any())
                {
                    Logger.Log("開始進行改良的門窗開口切割");
                    CreateWallOpenings(room, createdWalls, true, true);
                }

                Logger.Log($"完成牆面建立，建立了 {createdWalls.Count} 面牆");
            }
            catch (Exception ex)
            {
                Logger.Log($"建立牆面異常: {ex.Message}");
            }

            return createdWalls;
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
        /// 獲取粉刷牆的正確定位線，基於房間實際邊界範圍
        /// </summary>
        List<Curve> GetFinishWallLocationLines(Room room)
        {
            try
            {
                Logger.Log($"開始為房間 {room.Name} 建立粉刷牆定位線");

                // 獲取房間的完成面邊界（Finish）- 這已經是房間內側的邊界
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

                        // 直接使用 Finish 邊界曲線作為粉刷牆定位線
                        // Finish 邊界已經是房間內側的完成面位置，不需要再偏移
                        if (segmentCurve != null && segmentCurve.Length > 1e-6)
                        {
                            finishWallLocationLines.Add(segmentCurve);

                            var start = segmentCurve.GetEndPoint(0);
                            var end = segmentCurve.GetEndPoint(1);
                            var boundaryElement = Doc.GetElement(segment.ElementId);
                            var elementInfo = boundaryElement != null ? $"{boundaryElement.Category?.Name} ID:{boundaryElement.Id}" : "未知元素";

                            Logger.Log($"粉刷牆定位線 (來自{elementInfo}): 起點({start.X * 304.8:F2}, {start.Y * 304.8:F2}), " +
                                     $"終點({end.X * 304.8:F2}, {end.Y * 304.8:F2}), 長度: {segmentCurve.Length * 304.8:F2}mm");
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

                // 嘗試使用 CurveLoop 的整體偏移方法，這樣更準確
                try
                {
                    var curveLoop = CurveLoop.Create(curves);
                    var offsetLoop = CurveLoop.CreateViaOffset(curveLoop, -offsetDistance, XYZ.BasisZ);
                    var loopOffsetCurves = offsetLoop.ToList();
                    
                    Logger.Log($"使用 CurveLoop 整體偏移成功，生成 {loopOffsetCurves.Count} 條曲線");
                    
                    // 驗證偏移結果
                    if (loopOffsetCurves.Any() && loopOffsetCurves.All(c => c != null && c.Length > 1e-6))
                    {
                        return loopOffsetCurves;
                    }
                    else
                    {
                        Logger.Log("CurveLoop 偏移結果無效，使用逐條偏移方法");
                    }
                }
                catch (Exception loopEx)
                {
                    Logger.Log($"CurveLoop 整體偏移失敗: {loopEx.Message}，使用逐條偏移方法");
                }

                // 回退到逐條曲線偏移的方法
                var offsetCurves = new List<Curve>();
                var roomCenter = CalculateRoomCenter(curves);
                
                Logger.Log($"使用房間中心點: ({roomCenter.X * 304.8:F2}, {roomCenter.Y * 304.8:F2}) 來判斷偏移方向");
                
                foreach (var curve in curves)
                {
                    try
                    {
                        if (curve is Line line)
                        {
                            // 計算線段的法向量（垂直方向）
                            var direction = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                            var normal = new XYZ(-direction.Y, direction.X, 0);
                            
                            // 計算線段中點
                            var midPoint = line.Evaluate(0.5, true);
                            
                            // 判斷應該向哪個方向偏移（向房間中心方向）
                            var toCenter = (roomCenter - midPoint).Normalize();
                            var dotProduct = normal.DotProduct(toCenter);
                            
                            // 選擇正確的偏移方向（向房間內部）
                            if (dotProduct < 0)
                            {
                                normal = normal.Negate();
                            }
                            
                            // 創建偏移後的線段
                            var offsetVector = normal * offsetDistance;
                            var newStart = line.GetEndPoint(0) + offsetVector;
                            var newEnd = line.GetEndPoint(1) + offsetVector;
                            
                            var offsetLine = Line.CreateBound(newStart, newEnd);
                            offsetCurves.Add(offsetLine);
                            
                            Logger.Log($"線段偏移: 原始中點({midPoint.X * 304.8:F2}, {midPoint.Y * 304.8:F2}) -> " +
                                     $"新中點({offsetLine.Evaluate(0.5, true).X * 304.8:F2}, {offsetLine.Evaluate(0.5, true).Y * 304.8:F2})");
                        }
                        else
                        {
                            // 對於非直線（弧線等），嘗試使用 Revit 的 CreateOffset 方法
                            try
                            {
                                // 負值表示向內偏移
                                var offset = curve.CreateOffset(-offsetDistance, XYZ.BasisZ);
                                offsetCurves.Add(offset);
                                Logger.Log($"曲線偏移完成");
                            }
                            catch (Exception offsetEx)
                            {
                                Logger.Log($"曲線偏移失敗，保留原曲線: {offsetEx.Message}");
                                offsetCurves.Add(curve);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"處理單一曲線偏移失敗: {ex.Message}");
                        offsetCurves.Add(curve); // 保留原曲線
                    }
                }
                
                Logger.Log($"逐條偏移完成，處理了 {curves.Count} 條曲線，生成 {offsetCurves.Count} 條偏移曲線");
                return offsetCurves;
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
                var p = e.LookupParameter("AR_RoomId");
                if (p != null && !p.IsReadOnly) p.Set(r.Id.Value);
            }
            catch { }
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
                    .Where(w => w.WallType.Name.Contains("AR_") || w.WallType.Name.Contains("裝修"))
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

                        // 重新生成文檔以確保接合生效
                        Doc.Regenerate();
                    }
                    catch (Exception ex)
                    {
                        results.Errors.Add($"房間 {room.Number} 處理失敗: {ex.Message}");
                        Logger.Log($"房間 {room.Number} 自動接合失敗: {ex.Message}");
                    }
                }

                Logger.Log($"自動接合完成: {successfulJoins}/{joinAttempts}");
            }
            catch (Exception ex)
            {
                results.Errors.Add($"自動接合執行失敗: {ex.Message}");
                Logger.Log($"AutoJoinExistingWalls 失敗: {ex.Message}");
            }

            return results;
        }

        private List<Wall> GetWallsInRoom(List<Wall> allWalls, Room room)
        {
            var roomWalls = new List<Wall>();
            
            try
            {
                var boundaryOptions = new SpatialElementBoundaryOptions();
                var roomBoundary = room.GetBoundarySegments(boundaryOptions);
                if (!roomBoundary.Any()) return roomWalls;

                var roomLocation = room.Location as LocationPoint;
                var roomBBox = room.get_BoundingBox(null);
                var roomPoint = roomLocation?.Point ?? 
                    new XYZ((roomBBox.Min.X + roomBBox.Max.X) / 2, 
                           (roomBBox.Min.Y + roomBBox.Max.Y) / 2, 
                           (roomBBox.Min.Z + roomBBox.Max.Z) / 2);

                foreach (var wall in allWalls)
                {
                    try
                    {
                        // 檢查牆面是否在房間附近
                        var wallCurve = (wall.Location as LocationCurve)?.Curve;
                        if (wallCurve != null)
                        {
                            var distance = wallCurve.Distance(roomPoint);
                            if (distance < 10.0) // 10 英尺範圍內
                            {
                                roomWalls.Add(wall);
                            }
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

        private bool TryJoinWalls(Wall wall1, Wall wall2)
        {
            try
            {
                // 檢查牆面是否可以接合
                if (!WallUtils.IsWallJoinAllowedAtEnd(wall1, 0) && !WallUtils.IsWallJoinAllowedAtEnd(wall1, 1))
                    WallUtils.AllowWallJoinAtEnd(wall1, 0);
                
                if (!WallUtils.IsWallJoinAllowedAtEnd(wall2, 0) && !WallUtils.IsWallJoinAllowedAtEnd(wall2, 1))
                    WallUtils.AllowWallJoinAtEnd(wall2, 0);

                // 嘗試接合
                var curve1 = (wall1.Location as LocationCurve)?.Curve;
                var curve2 = (wall2.Location as LocationCurve)?.Curve;
                
                if (curve1 != null && curve2 != null)
                {
                    // 檢查端點是否接近
                    var tolerance = 1.0; // 1 英尺
                    
                    if (curve1.GetEndPoint(0).DistanceTo(curve2.GetEndPoint(0)) < tolerance ||
                        curve1.GetEndPoint(0).DistanceTo(curve2.GetEndPoint(1)) < tolerance ||
                        curve1.GetEndPoint(1).DistanceTo(curve2.GetEndPoint(0)) < tolerance ||
                        curve1.GetEndPoint(1).DistanceTo(curve2.GetEndPoint(1)) < tolerance)
                    {
                        JoinGeometryUtils.JoinGeometry(Doc, wall1, wall2);
                        return true;
                    }
                }
                
                return false;
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
