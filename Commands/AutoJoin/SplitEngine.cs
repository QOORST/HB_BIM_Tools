using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class SplitResult
    {
        public int TargetElements { get; set; }
        public int OriginalDeleted { get; set; }
        public int NewElementsCreated { get; set; }
        public int Skipped { get; set; }
        public int FailedOperations { get; set; }
        public List<string> FailureSamples { get; } = new List<string>();
    }

    /// <summary>
    /// 樓板分割過濾器：允許選取樓板 + 結構構架（梁）
    /// </summary>
    internal sealed class FloorAndFramingFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            var id = elem.Category?.Id;
            if (id == null) return false;
            var bic = (BuiltInCategory)(int)id.GetIdValue();
            return bic == BuiltInCategory.OST_Floors
                || bic == BuiltInCategory.OST_StructuralFraming;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    /// <summary>
    /// 牆分割 - 目標牆過濾器（僅選牆）
    /// </summary>
    internal sealed class WallTargetFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            var id = elem.Category?.Id;
            if (id == null) return false;
            var bic = (BuiltInCategory)(int)id.GetIdValue();
            return bic == BuiltInCategory.OST_Walls;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    /// <summary>
    /// 牆分割 - 切割構件過濾器（結構柱、結構構架、牆）
    /// </summary>
    internal sealed class WallCutterFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            var id = elem.Category?.Id;
            if (id == null) return false;
            var bic = (BuiltInCategory)(int)id.GetIdValue();
            return bic == BuiltInCategory.OST_StructuralColumns
                || bic == BuiltInCategory.OST_StructuralFraming
                || bic == BuiltInCategory.OST_Walls;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    internal static class SplitEngine
    {
        // 最短段落長度：約 30 mm（Revit 內部單位為英尺）
        private const double MinSegmentLength = 0.1;

        /// <summary>
        /// 依結構構架（梁）分割樓板。
        /// 邏輯參考 SplitFloorByBeam：先接合再取頂面封閉環，逐環建立新樓板。
        /// </summary>
        public static SplitResult RunSplitFloor(Document doc, IList<Floor> floors, IList<Element> cutters)
        {
            var result = new SplitResult { TargetElements = floors.Count };

            // 第一步：將切割構件接合到樓板（SwitchJoinOrder 讓構件切穿樓板）
            using (var tran = new Transaction(doc, "分割樓板 - 接合"))
            {
                tran.Start();
                foreach (var floor in floors)
                {
                    foreach (var cutter in cutters)
                    {
                        try
                        {
                            if (!JoinGeometryUtils.AreElementsJoined(doc, floor, cutter))
                            {
                                JoinGeometryUtils.JoinGeometry(doc, floor, cutter);
                                JoinGeometryUtils.SwitchJoinOrder(doc, floor, cutter);
                            }
                        }
                        catch { /* 忽略接合失敗，繼續下一對 */ }
                    }
                }
                tran.Commit();
            }

            // 第二步：取頂面封閉環，依環數重建樓板
            using (var tran = new Transaction(doc, "分割樓板 - 重建"))
            {
                tran.Start();
                foreach (var floor in floors)
                {
                    try
                    {
                        var faceRefs = HostObjectUtils.GetTopFaces(floor);
                        if (!faceRefs.Any()) { result.Skipped++; continue; }

                        var topFace = floor.GetGeometryObjectFromReference(faceRefs[0]) as Face;
                        var loops = topFace?.GetEdgesAsCurveLoops();

                        // 只有多於一個封閉環才代表已被切割
                        if (loops == null || loops.Count <= 1) { result.Skipped++; continue; }

                        var level = doc.GetElement(floor.LevelId) as Level;
                        var floorType = doc.GetElement(floor.GetTypeId()) as FloorType;

                        doc.Delete(floor.Id);
                        result.OriginalDeleted++;

                        foreach (var loop in loops)
                        {
                            try
                            {
                                var newFloor = CreateFloorFromLoop(doc, loop, floorType, level);
                                if (newFloor != null) result.NewElementsCreated++;
                                else result.FailedOperations++;
                            }
                            catch (Exception ex)
                            {
                                result.FailedOperations++;
                                if (result.FailureSamples.Count < 5)
                                    result.FailureSamples.Add($"建立樓板失敗: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailedOperations++;
                        if (result.FailureSamples.Count < 5)
                            result.FailureSamples.Add($"Floor({floor.Id}): {ex.Message}");
                    }
                }
                tran.Commit();
            }

            return result;
        }

        /// <summary>
        /// 依接合後外側面封閉環重建牆段（邏輯同樓板分割）。
        /// 第一步：SwitchJoinOrder 使切割構件切穿牆體。
        /// 第二步：取外側面 CurveLoops，多個封閉環即代表已被切割；依各環範圍建立新牆段。
        /// </summary>
        public static SplitResult RunSplitWall(Document doc, IList<Wall> walls, IList<Element> cutters)
        {
            var result = new SplitResult { TargetElements = walls.Count };

            // 第一步：將切割構件接合到牆（SwitchJoinOrder 讓構件切穿牆體）
            using (var tran = new Transaction(doc, "分割牆 - 接合"))
            {
                tran.Start();
                foreach (var wall in walls)
                {
                    foreach (var cutter in cutters)
                    {
                        try
                        {
                            if (!JoinGeometryUtils.AreElementsJoined(doc, wall, cutter))
                            {
                                JoinGeometryUtils.JoinGeometry(doc, wall, cutter);
                                JoinGeometryUtils.SwitchJoinOrder(doc, wall, cutter);
                            }
                        }
                        catch { /* 忽略接合失敗，繼續下一對 */ }
                    }
                }
                tran.Commit();
            }

            // 第二步：取外側面封閉環，依環數重建牆段
            using (var tran = new Transaction(doc, "分割牆 - 重建"))
            {
                tran.Start();
                foreach (var wall in walls)
                {
                    try
                    {
                        if (!TrySplitWall(doc, wall, cutters, result))
                            result.Skipped++;
                    }
                    catch (Exception ex)
                    {
                        result.FailedOperations++;
                        if (result.FailureSamples.Count < 5)
                            result.FailureSamples.Add($"Wall({wall.Id}): {ex.Message}");
                    }
                }
                tran.Commit();
            }

            return result;
        }

        // ─── 私有輔助方法 ────────────────────────────────────────────────────────

        /// <summary>
        /// 依接合後外側面的 CurveLoops 分割牆（同 CreateFloorFromLoop 邏輯）。
        /// 接合使切割構件切穿牆體後，外側面會出現多個獨立封閉環，每環對應一段新牆。
        /// </summary>
        private static bool TrySplitWall(Document doc, Wall wall, IList<Element> cutters, SplitResult result)
        {
            if (TrySplitWallByBeamHeights(doc, wall, cutters, result))
                return true;

            return TrySplitWallByProfile(doc, wall, result);
        }

        /// <summary>
        /// 依梁的實際高度區間分割牆。梁需與牆的長度投影及牆厚範圍重疊，才會作為分割來源。
        /// </summary>
        private static bool TrySplitWallByBeamHeights(Document doc, Wall wall, IList<Element> cutters, SplitResult result)
        {
            var locationCurve = wall.Location as LocationCurve;
            if (!(locationCurve?.Curve is Line wallLine)) return false;

            var level = doc.GetElement(wall.LevelId) as Level;
            if (level == null) return false;

            var wallStart = wallLine.GetEndPoint(0);
            var wallEnd = wallLine.GetEndPoint(1);
            var wallDir = (wallEnd - wallStart).Normalize();
            var wallLength = wallStart.DistanceTo(wallEnd);
            if (wallLength < MinSegmentLength) return false;

            var wallNormal = wallDir.CrossProduct(XYZ.BasisZ).Normalize();
            var wallType = doc.GetElement(wall.GetTypeId()) as WallType;
            var wallWidth = wallType?.Width ?? 0.0;
            var horizontalTolerance = Math.Max(wallWidth * 0.5 + 0.2, 0.35);
            var baseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0.0;
            var unconnectedHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 10.0;
            var wallBottom = level.Elevation + baseOffset;
            var wallTop = wallBottom + unconnectedHeight;
            if (wallTop - wallBottom < MinSegmentLength) return false;

            var blockedIntervals = new List<Tuple<double, double>>();
            foreach (var cutter in cutters)
            {
                if (!IsStructuralFraming(cutter)) continue;

                var box = cutter.get_BoundingBox(null);
                if (box == null) continue;
                if (!DoesBoxOverlapWallFootprint(box, wallStart, wallDir, wallNormal, wallLength, horizontalTolerance))
                    continue;

                var cutBottom = Math.Max(box.Min.Z, wallBottom);
                var cutTop = Math.Min(box.Max.Z, wallTop);
                if (cutTop - cutBottom < MinSegmentLength) continue;

                blockedIntervals.Add(Tuple.Create(cutBottom, cutTop));
            }

            if (blockedIntervals.Count == 0) return false;

            var merged = MergeIntervals(blockedIntervals);
            var segments = new List<Tuple<double, double>>();
            var cursor = wallBottom;
            foreach (var interval in merged)
            {
                if (interval.Item1 - cursor >= MinSegmentLength)
                    segments.Add(Tuple.Create(cursor, interval.Item1));

                if (interval.Item2 > cursor)
                    cursor = interval.Item2;
            }

            if (wallTop - cursor >= MinSegmentLength)
                segments.Add(Tuple.Create(cursor, wallTop));

            if (segments.Count <= 1) return false;

            var wallTypeId = wall.GetTypeId();
            var levelId = wall.LevelId;
            var flipped = wall.Flipped;
            var structuralParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM);
            var isStructural = structuralParam != null && structuralParam.AsInteger() != 0;

            doc.Delete(wall.Id);
            result.OriginalDeleted++;

            foreach (var segment in segments)
            {
                try
                {
                    var segmentBaseOffset = segment.Item1 - level.Elevation;
                    var segmentHeight = segment.Item2 - segment.Item1;
                    if (segmentHeight < MinSegmentLength) continue;

                    Wall.Create(doc, wallLine, wallTypeId, levelId, segmentHeight, segmentBaseOffset, flipped, isStructural);
                    result.NewElementsCreated++;
                }
                catch (Exception ex)
                {
                    result.FailedOperations++;
                    if (result.FailureSamples.Count < 5)
                        result.FailureSamples.Add($"依梁位建立牆段失敗: {ex.Message}");
                }
            }

            return true;
        }

        private static bool TrySplitWallByProfile(Document doc, Wall wall, SplitResult result)
        {
            // 僅處理直線形基本牆
            var locationCurve = wall.Location as LocationCurve;
            if (!(locationCurve?.Curve is Line wallLine)) return false;

            var wallStart = wallLine.GetEndPoint(0);
            var wallDir   = wallLine.Direction;

            // 取外側面參考（若無則改取內側面）
            IList<Reference> sideRefs = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
            if (!sideRefs.Any())
                sideRefs = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
            if (!sideRefs.Any()) return false;

            var face  = wall.GetGeometryObjectFromReference(sideRefs[0]) as Face;
            var loops = face?.GetEdgesAsCurveLoops();

            // 只有多於一個封閉環才代表已被切割（同樓板分割邏輯）
            if (loops == null || loops.Count <= 1) return false;

            // 讀取原始牆體參數
            var wallTypeId        = wall.GetTypeId();
            var levelId           = wall.LevelId;
            var baseOffset        = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0.0;
            var unconnectedHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 10.0;
            var flipped           = wall.Flipped;
            var structuralParam   = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM);
            var isStructural      = structuralParam != null && structuralParam.AsInteger() != 0;

            doc.Delete(wall.Id);
            result.OriginalDeleted++;

            foreach (var loop in loops)
            {
                try
                {
                    // 將各環所有端點投影到牆方向，求段落的起終範圍
                    double loopMin = double.MaxValue;
                    double loopMax = double.MinValue;
                    foreach (var curve in loop)
                    {
                        for (int e = 0; e < 2; e++)
                        {
                            var p = wallDir.DotProduct(curve.GetEndPoint(e) - wallStart);
                            if (p < loopMin) loopMin = p;
                            if (p > loopMax) loopMax = p;
                        }
                    }

                    if (loopMax - loopMin < MinSegmentLength) continue;

                    var segLine = Line.CreateBound(
                        wallStart + wallDir.Multiply(loopMin),
                        wallStart + wallDir.Multiply(loopMax));

                    Wall.Create(doc, segLine, wallTypeId, levelId, unconnectedHeight, baseOffset, flipped, isStructural);
                    result.NewElementsCreated++;
                }
                catch (Exception ex)
                {
                    result.FailedOperations++;
                    if (result.FailureSamples.Count < 5)
                        result.FailureSamples.Add($"建立牆段失敗: {ex.Message}");
                }
            }

            return true;
        }

        private static bool IsStructuralFraming(Element element)
        {
            var id = element?.Category?.Id;
            if (id == null) return false;
            return (BuiltInCategory)(int)id.GetIdValue() == BuiltInCategory.OST_StructuralFraming;
        }

        private static bool DoesBoxOverlapWallFootprint(
            BoundingBoxXYZ box,
            XYZ wallStart,
            XYZ wallDir,
            XYZ wallNormal,
            double wallLength,
            double tolerance)
        {
            double minAlong = double.MaxValue;
            double maxAlong = double.MinValue;
            double minNormal = double.MaxValue;
            double maxNormal = double.MinValue;

            foreach (var corner in GetBoxCorners(box))
            {
                var relative = corner - wallStart;
                var along = wallDir.DotProduct(relative);
                var normal = wallNormal.DotProduct(relative);

                if (along < minAlong) minAlong = along;
                if (along > maxAlong) maxAlong = along;
                if (normal < minNormal) minNormal = normal;
                if (normal > maxNormal) maxNormal = normal;
            }

            var overlapsLength = maxAlong >= -tolerance && minAlong <= wallLength + tolerance;
            var overlapsThickness = maxNormal >= -tolerance && minNormal <= tolerance;
            return overlapsLength && overlapsThickness;
        }

        private static IEnumerable<XYZ> GetBoxCorners(BoundingBoxXYZ box)
        {
            yield return new XYZ(box.Min.X, box.Min.Y, box.Min.Z);
            yield return new XYZ(box.Min.X, box.Min.Y, box.Max.Z);
            yield return new XYZ(box.Min.X, box.Max.Y, box.Min.Z);
            yield return new XYZ(box.Min.X, box.Max.Y, box.Max.Z);
            yield return new XYZ(box.Max.X, box.Min.Y, box.Min.Z);
            yield return new XYZ(box.Max.X, box.Min.Y, box.Max.Z);
            yield return new XYZ(box.Max.X, box.Max.Y, box.Min.Z);
            yield return new XYZ(box.Max.X, box.Max.Y, box.Max.Z);
        }

        private static List<Tuple<double, double>> MergeIntervals(List<Tuple<double, double>> intervals)
        {
            var sorted = intervals
                .OrderBy(i => i.Item1)
                .ThenBy(i => i.Item2)
                .ToList();

            var merged = new List<Tuple<double, double>>();
            foreach (var interval in sorted)
            {
                if (merged.Count == 0 || interval.Item1 > merged[merged.Count - 1].Item2 + 0.02)
                {
                    merged.Add(interval);
                    continue;
                }

                var last = merged[merged.Count - 1];
                if (interval.Item2 > last.Item2)
                    merged[merged.Count - 1] = Tuple.Create(last.Item1, interval.Item2);
            }

            return merged;
        }

        private static Floor CreateFloorFromLoop(Document doc, CurveLoop loop, FloorType floorType, Level level)
        {
#if REVIT2022
            // Revit 2022 使用舊式 API
            var curveArray = new CurveArray();
            foreach (var curve in loop)
                curveArray.Append(curve);
#pragma warning disable CS0618
            return doc.Create.NewFloor(curveArray, floorType, level, false);
#pragma warning restore CS0618
#else
            // Revit 2023+ 使用 Floor.Create
            return Floor.Create(doc, new List<CurveLoop> { loop }, floorType.Id, level.Id);
#endif
        }


    }
}
