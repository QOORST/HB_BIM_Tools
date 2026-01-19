using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 統一的幾何實體提取工具 - 解決重複程式碼問題
    /// </summary>
    public static class GeometryExtractor
    {
        /// <summary>
        /// 預設幾何容差
        /// </summary>
        private const double DEFAULT_TOLERANCE = 1e-6;
        
        /// <summary>
        /// 精細幾何容差 (用於重要計算)
        /// </summary>
        public const double FINE_TOLERANCE = 1e-9;
        
        /// <summary>
        /// 一般幾何容差 (用於一般處理)
        /// </summary>
        public const double NORMAL_TOLERANCE = 1e-6;
        
        /// <summary>
        /// 🚀 性能優化: 幾何快取機制 - 避免重複計算相同元素的幾何
        /// </summary>
        private static readonly Dictionary<ElementId, List<Solid>> _geometryCache = new Dictionary<ElementId, List<Solid>>();
        private static readonly Dictionary<ElementId, double> _volumeCache = new Dictionary<ElementId, double>();
        
        /// <summary>
        /// 取得元素的所有實體幾何
        /// </summary>
        /// <param name="element">要提取的元素</param>
        /// <param name="tolerance">體積容差</param>
        /// <param name="computeReferences">是否計算參考</param>
        /// <returns>實體幾何清單</returns>
        public static List<Solid> GetElementSolids(Element element, double tolerance = DEFAULT_TOLERANCE, bool computeReferences = true)
        {
            if (element == null) return new List<Solid>();
            
            // 🚀 性能優化: 檢查快取，避免重複計算
            if (_geometryCache.ContainsKey(element.Id))
            {
                return new List<Solid>(_geometryCache[element.Id]); // 返回副本避免修改快取
            }
            
            var solids = new List<Solid>();
            
            try
            {
                var options = new Options 
                { 
                    DetailLevel = ViewDetailLevel.Fine,
                    ComputeReferences = computeReferences
                };
                
                var geometry = element.get_Geometry(options);
                if (geometry != null)
                {
                    ExtractSolidsFromGeometry(geometry, solids, tolerance);
                }
                
                // 🚀 性能優化: 將結果加入快取 (最多快取 1000 個元素避免記憶體溢出)
                if (_geometryCache.Count < 1000)
                {
                    _geometryCache[element.Id] = new List<Solid>(solids);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"提取元素 {element.Id} 幾何失敗: {ex.Message}");
            }
            
            return solids;
        }
        
        /// <summary>
        /// 取得元素的最大實體幾何
        /// </summary>
        /// <param name="element">要提取的元素</param>
        /// <param name="tolerance">體積容差</param>
        /// <returns>最大的實體幾何，如果沒有則返回null</returns>
        public static Solid GetLargestSolid(Element element, double tolerance = DEFAULT_TOLERANCE)
        {
            var solids = GetElementSolids(element, tolerance);
            return GetLargestSolid(solids);
        }
        
        /// <summary>
        /// 從實體清單中取得最大的實體
        /// </summary>
        /// <param name="solids">實體清單</param>
        /// <returns>最大的實體幾何，如果沒有則返回null</returns>
        public static Solid GetLargestSolid(List<Solid> solids)
        {
            if (solids == null || solids.Count == 0) return null;
            
            Solid largestSolid = null;
            double maxVolume = 0;
            
            foreach (var solid in solids)
            {
                if (solid != null && solid.Volume > maxVolume)
                {
                    maxVolume = solid.Volume;
                    largestSolid = solid;
                }
            }
            
            return largestSolid;
        }
        
        /// <summary>
        /// 計算元素的總體積
        /// </summary>
        /// <param name="element">要計算的元素</param>
        /// <param name="tolerance">體積容差</param>
        /// <returns>總體積 (立方英尺)</returns>
        public static double GetTotalVolume(Element element, double tolerance = DEFAULT_TOLERANCE)
        {
            var solids = GetElementSolids(element, tolerance);
            double totalVolume = 0;
            
            foreach (var solid in solids)
            {
                totalVolume += solid.Volume;
            }
            
            return totalVolume;
        }
        
        /// <summary>
        /// 計算元素的總表面積
        /// </summary>
        /// <param name="element">要計算的元素</param>
        /// <param name="tolerance">體積容差</param>
        /// <returns>總表面積 (平方英尺)</returns>
        public static double GetTotalSurfaceArea(Element element, double tolerance = DEFAULT_TOLERANCE)
        {
            var solids = GetElementSolids(element, tolerance);
            double totalArea = 0;
            
            foreach (var solid in solids)
            {
                foreach (Face face in solid.Faces)
                {
                    totalArea += face.Area;
                }
            }
            
            return totalArea;
        }
        
        /// <summary>
        /// 從幾何元素中遞歸提取實體
        /// </summary>
        /// <param name="geometry">幾何元素</param>
        /// <param name="solids">實體清單</param>
        /// <param name="tolerance">體積容差</param>
        private static void ExtractSolidsFromGeometry(GeometryElement geometry, List<Solid> solids, double tolerance)
        {
            foreach (var geomObj in geometry)
            {
                if (geomObj is Solid solid && IsValidSolid(solid, tolerance))
                {
                    solids.Add(solid);
                }
                else if (geomObj is GeometryInstance instance)
                {
                    var instGeom = instance.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        ExtractSolidsFromGeometry(instGeom, solids, tolerance);
                    }
                }
            }
        }
        
        /// <summary>
        /// 檢查實體是否有效
        /// </summary>
        /// <param name="solid">要檢查的實體</param>
        /// <param name="tolerance">體積容差</param>
        /// <returns>是否為有效實體</returns>
        private static bool IsValidSolid(Solid solid, double tolerance)
        {
            return solid != null && solid.Volume > tolerance && solid.SurfaceArea > tolerance;
        }
        
        /// <summary>
        /// 檢查兩個實體是否相交
        /// </summary>
        /// <param name="solid1">第一個實體</param>
        /// <param name="solid2">第二個實體</param>
        /// <returns>是否相交</returns>
        public static bool DoSolidsIntersect(Solid solid1, Solid solid2)
        {
            if (solid1 == null || solid2 == null) return false;
            
            try
            {
                var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    solid1, solid2, BooleanOperationsType.Intersect);
                
                return intersection != null && intersection.Volume > FINE_TOLERANCE;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"檢查實體相交失敗: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 計算兩個實體的相交體積
        /// </summary>
        /// <param name="solid1">第一個實體</param>
        /// <param name="solid2">第二個實體</param>
        /// <returns>相交體積 (立方英尺)</returns>
        public static double GetIntersectionVolume(Solid solid1, Solid solid2)
        {
            if (solid1 == null || solid2 == null) return 0;
            
            try
            {
                var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    solid1, solid2, BooleanOperationsType.Intersect);
                
                return intersection?.Volume ?? 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"計算相交體積失敗: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// 🚀 性能優化: 清理幾何快取 (釋放記憶體)
        /// </summary>
        public static void ClearGeometryCache()
        {
            _geometryCache.Clear();
            _volumeCache.Clear();
            System.Diagnostics.Debug.WriteLine("✅ 幾何快取已清理");
        }
        
        /// <summary>
        /// 🚀 性能優化: 取得快取使用情況
        /// </summary>
        public static string GetCacheStatistics()
        {
            return $"幾何快取: {_geometryCache.Count} 個元素, 體積快取: {_volumeCache.Count} 個元素";
        }
        
        /// <summary>
        /// 獲取指定區域附近的結構元素 (用於接觸面扣除)
        /// </summary>
        /// <param name="doc">文件</param>
        /// <param name="hostElement">宿主元素 (會被排除)</param>
        /// <param name="searchBounds">搜尋範圍邊界框</param>
        /// <param name="searchRadiusFt">額外搜尋半徑 (英尺)</param>
        /// <returns>鄰近的結構元素清單</returns>
        public static List<Element> GetNearbyStructuralElements(Document doc, Element hostElement, BoundingBoxXYZ searchBounds, double searchRadiusFt = 0)
        {
            var nearbyElements = new List<Element>();
            try
            {
                // 擴大搜索範圍
                var expandedBounds = new BoundingBoxXYZ
                {
                    Min = searchBounds.Min - new XYZ(searchRadiusFt, searchRadiusFt, searchRadiusFt),
                    Max = searchBounds.Max + new XYZ(searchRadiusFt, searchRadiusFt, searchRadiusFt)
                };

                System.Diagnostics.Debug.WriteLine($"🔍 GetNearbyStructuralElements 開始搜索...");
                
                var filter = new BoundingBoxIntersectsFilter(new Outline(expandedBounds.Min, expandedBounds.Max));
                var categories = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Stairs
                };

                foreach (var category in categories)
                {
                    try
                    {
                        var collector = new FilteredElementCollector(doc)
                            .OfCategory(category)
                            .WhereElementIsNotElementType()
                            .WherePasses(filter);

                        int count = 0;
                        foreach (var element in collector)
                        {
                            if (element.Id != hostElement.Id) // 排除自身
                            {
                                nearbyElements.Add(element);
                                count++;
                            }
                        }
                        
                        if (count > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"  {category}: {count} 個元素");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"搜索類別 {category} 時出錯: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✅ 總共找到 {nearbyElements.Count} 個附近結構元素");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 搜索附近元素失敗: {ex.Message}");
            }
            return nearbyElements;
        }
        
        /// <summary>
        /// 從面的範圍獲取附近的結構元素
        /// </summary>
        /// <param name="doc">文件</param>
        /// <param name="hostElement">宿主元素</param>
        /// <param name="face">參考面</param>
        /// <param name="searchRadiusMm">搜索半徑(毫米)</param>
        /// <returns>鄰近的結構元素清單</returns>
        public static List<Element> GetNearbyStructuralElementsFromFace(Document doc, Element hostElement, PlanarFace face, double searchRadiusMm)
        {
            try
            {
                var searchRadius = searchRadiusMm / 304.8; // 轉換為英尺
                
                System.Diagnostics.Debug.WriteLine($"🔍 GetNearbyStructuralElementsFromFace 開始:");
                System.Diagnostics.Debug.WriteLine($"  宿主元素: {hostElement.Id} ({hostElement.Category?.Name})");
                System.Diagnostics.Debug.WriteLine($"  搜索半徑: {searchRadiusMm:F0} mm = {searchRadius:F2} ft");
                
                // 計算面的3D邊界框
                var faceVertices = new List<XYZ>();
                var curveLoops = face.GetEdgesAsCurveLoops();
                foreach (var curveLoop in curveLoops)
                {
                    foreach (Curve curve in curveLoop)
                    {
                        faceVertices.Add(curve.GetEndPoint(0));
                        faceVertices.Add(curve.GetEndPoint(1));
                    }
                }
                
                if (faceVertices.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 無法獲取面的頂點");
                    return new List<Element>();
                }
                
                var minX = faceVertices.Min(v => v.X) - searchRadius;
                var maxX = faceVertices.Max(v => v.X) + searchRadius;
                var minY = faceVertices.Min(v => v.Y) - searchRadius;
                var maxY = faceVertices.Max(v => v.Y) + searchRadius;
                var minZ = faceVertices.Min(v => v.Z) - searchRadius;
                var maxZ = faceVertices.Max(v => v.Z) + searchRadius;
                
                System.Diagnostics.Debug.WriteLine($"  面範圍: X({faceVertices.Min(v => v.X):F2} ~ {faceVertices.Max(v => v.X):F2})");
                System.Diagnostics.Debug.WriteLine($"  面範圍: Y({faceVertices.Min(v => v.Y):F2} ~ {faceVertices.Max(v => v.Y):F2})");
                System.Diagnostics.Debug.WriteLine($"  面範圍: Z({faceVertices.Min(v => v.Z):F2} ~ {faceVertices.Max(v => v.Z):F2})");
                System.Diagnostics.Debug.WriteLine($"  搜索範圍: X({minX:F2} ~ {maxX:F2})");
                System.Diagnostics.Debug.WriteLine($"  搜索範圍: Y({minY:F2} ~ {maxY:F2})");
                System.Diagnostics.Debug.WriteLine($"  搜索範圍: Z({minZ:F2} ~ {maxZ:F2})");
                
                var expandedBounds = new BoundingBoxXYZ
                {
                    Min = new XYZ(minX, minY, minZ),
                    Max = new XYZ(maxX, maxY, maxZ)
                };

                var result = GetNearbyStructuralElements(doc, hostElement, expandedBounds, 0);
                System.Diagnostics.Debug.WriteLine($"✅ 找到 {result.Count} 個鄰近元素");
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 從面搜索附近元素失敗: {ex.Message}");
                return new List<Element>();
            }
        }
        
        /// <summary>
        /// 智能接觸面扣除 - 只扣除真正接觸的鄰近元素
        /// </summary>
        /// <param name="formworkSolid">模板實體</param>
        /// <param name="nearbyElements">鄰近結構元素</param>
        /// <param name="intersectionThreshold">交集閾值比例 (預設 5%)</param>
        /// <param name="hostElement">宿主元素 (用於特殊判斷)</param>
        /// <returns>扣除後的實體，若完全被覆蓋則返回 null</returns>
        public static Solid ApplySmartContactDeduction(Solid formworkSolid, List<Element> nearbyElements, double intersectionThreshold = 0.05, Element hostElement = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 ApplySmartContactDeduction 開始:");
                System.Diagnostics.Debug.WriteLine($"  模板原始體積: {formworkSolid.Volume:F6}");
                System.Diagnostics.Debug.WriteLine($"  鄰近元素數量: {nearbyElements.Count}");
                System.Diagnostics.Debug.WriteLine($"  交集閾值: {intersectionThreshold:F2} ({intersectionThreshold * 100}%)");
                
                // 判斷宿主是否為柱子
                bool isColumnHost = hostElement?.Category?.Id?.Value == (long)BuiltInCategory.OST_StructuralColumns;
                if (isColumnHost)
                {
                    System.Diagnostics.Debug.WriteLine($"  🏛️ 宿主為柱子，使用特殊扣除邏輯");
                }
                
                if (nearbyElements.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("  ⚠️ 沒有鄰近元素,跳過扣除");
                    return formworkSolid;
                }

                Solid result = formworkSolid;
                double originalVolume = formworkSolid.Volume;
                int deductionCount = 0;

                foreach (var element in nearbyElements)
                {
                    try
                    {
                        var elementSolids = GetElementSolids(element);
                        var elementCategory = element.Category?.Id?.Value ?? 0;
                        bool isSlabOrBeam = elementCategory == (long)BuiltInCategory.OST_Floors || 
                                           elementCategory == (long)BuiltInCategory.OST_StructuralFraming;
                        
                        System.Diagnostics.Debug.WriteLine($"  檢查元素 {element.Id} ({element.Category?.Name}): {elementSolids.Count} 個實體");
                        
                        foreach (var elementSolid in elementSolids)
                        {
                            if (elementSolid?.Volume <= FINE_TOLERANCE) continue;

                            // 檢查交集
                            var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                                result, elementSolid, BooleanOperationsType.Intersect);

                            if (intersection?.Volume > FINE_TOLERANCE)
                            {
                                double intersectionRatio = intersection.Volume / result.Volume;
                                System.Diagnostics.Debug.WriteLine($"    ✓ 發現交集 - 交集體積: {intersection.Volume:F6}, 比例: {intersectionRatio:F3} ({intersectionRatio * 100:F1}%)");

                                // 🔧 特殊邏輯: 如果是柱子模板且鄰近元素是樓板或梁，直接扣除不考慮閾值
                                bool shouldDeductDirectly = isColumnHost && isSlabOrBeam;
                                
                                if (shouldDeductDirectly)
                                {
                                    System.Diagnostics.Debug.WriteLine($"    🏛️ 柱子穿過樓板/梁，直接扣除接觸部分");
                                    
                                    var difference = BooleanOperationsUtils.ExecuteBooleanOperation(
                                        result, elementSolid, BooleanOperationsType.Difference);

                                    if (difference?.Volume > FINE_TOLERANCE)
                                    {
                                        double remainingRatio = difference.Volume / result.Volume;
                                        result = difference;
                                        deductionCount++;
                                        System.Diagnostics.Debug.WriteLine($"    ✅ 執行扣除 #{deductionCount} - 剩餘體積: {result.Volume:F6}, 剩餘比例: {remainingRatio:F3} ({remainingRatio * 100:F1}%)");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"    ⚠️ 扣除後無剩餘實體，該面可能完全被接觸面覆蓋");
                                        return null;
                                    }
                                }
                                // 一般邏輯: 根據閾值判斷
                                else if (intersectionRatio > intersectionThreshold)
                                {
                                    var difference = BooleanOperationsUtils.ExecuteBooleanOperation(
                                        result, elementSolid, BooleanOperationsType.Difference);

                                    if (difference?.Volume > FINE_TOLERANCE)
                                    {
                                        double remainingRatio = difference.Volume / result.Volume;
                                        result = difference;
                                        deductionCount++;
                                        System.Diagnostics.Debug.WriteLine($"    ✅ 執行扣除 #{deductionCount} - 剩餘體積: {result.Volume:F6}, 剩餘比例: {remainingRatio:F3} ({remainingRatio * 100:F1}%)");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"    ⚠️ 扣除後無剩餘實體，該面可能完全被接觸面覆蓋");
                                        return null;
                                    }
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"    ⏭️ 交集比例 {intersectionRatio:F3} 低於閾值 {intersectionThreshold:F3}，不扣除");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"    ❌ 處理元素 {element.Id} 時出錯: {ex.Message}");
                    }
                }

                // 如果最終體積太小，表示大部分都是接觸面，不應生成模板
                double finalRatio = result.Volume / originalVolume;
                if (finalRatio < 0.1)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 最終體積比例 {finalRatio:F3} ({finalRatio * 100:F1}%) 過小，大部分為接觸面，不生成模板");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"✅ 接觸面扣除完成 - 執行了 {deductionCount} 次扣除，最終體積保留比例: {finalRatio:F3} ({finalRatio * 100:F1}%)");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 接觸面扣除失敗: {ex.Message}");
                return formworkSolid; // 返回原始形狀
            }
        }
    }
}