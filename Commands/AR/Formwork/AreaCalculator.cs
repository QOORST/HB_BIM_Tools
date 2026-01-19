using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 統一的面積計算工具 - 解決重複程式碼問題
    /// </summary>
    public static class AreaCalculator
    {
        /// <summary>
        /// 🚀 性能優化: 面積計算快取
        /// </summary>
        private static readonly Dictionary<string, double> _areaCache = new Dictionary<string, double>();
        /// <summary>
        /// 計算模板面積 (包含接觸面扣除)
        /// </summary>
        /// <param name="formworkSolid">模板實體</param>
        /// <param name="structuralElements">結構元素清單</param>
        /// <param name="tolerance">容差值</param>
        /// <returns>有效面積 (平方米)</returns>
        public static double CalculateFormworkAreaWithDeduction(Solid formworkSolid, 
            IEnumerable<Element> structuralElements, double tolerance = 0.001)
        {
            if (formworkSolid == null || !IsValidSolid(formworkSolid))
            {
                System.Diagnostics.Debug.WriteLine("❌ 模板實體無效");
                return 0.0;
            }
            
            try
            {
                double totalArea = 0.0;
                double deductionArea = 0.0;
                
                // 🚀 性能優化: 快速計算總表面積 (使用 LINQ 平行處理)
                totalArea = formworkSolid.Faces.Cast<Face>()
                    .Where(face => face is PlanarFace)
                    .AsParallel()
                    .Sum(face => face.Area);
                
                // 計算接觸面扣除面積
                deductionArea = CalculateContactDeductionArea(formworkSolid, structuralElements, tolerance);
                
                // 有效面積 = 總面積 - 扣除面積
                double effectiveArea = Math.Max(0.0, totalArea - deductionArea);
                
                // 轉換為平方米
                double effectiveAreaM2 = ConvertToSquareMeters(effectiveArea);
                
                System.Diagnostics.Debug.WriteLine($"📊 面積計算: 總面積={ConvertToSquareMeters(totalArea):F2}m², 扣除面積={ConvertToSquareMeters(deductionArea):F2}m², 有效面積={effectiveAreaM2:F2}m²");
                
                return effectiveAreaM2;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算模板面積失敗: {ex.Message}");
                return 0.0;
            }
        }
        
        /// <summary>
        /// 計算簡單模板面積 (不含扣除)
        /// </summary>
        /// <param name="formworkSolid">模板實體</param>
        /// <returns>面積 (平方米)</returns>
        public static double CalculateSimpleFormworkArea(Solid formworkSolid)
        {
            if (formworkSolid == null || !IsValidSolid(formworkSolid))
            {
                System.Diagnostics.Debug.WriteLine("❌ 模板實體無效");
                return 0.0;
            }
            
            try
            {
                double totalArea = 0.0;
                
                foreach (Face face in formworkSolid.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        totalArea += planarFace.Area;
                    }
                }
                
                double areaM2 = ConvertToSquareMeters(totalArea);
                System.Diagnostics.Debug.WriteLine($"📊 簡單面積計算: {areaM2:F2}m²");
                
                return areaM2;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算簡單模板面積失敗: {ex.Message}");
                return 0.0;
            }
        }
        
        /// <summary>
        /// 計算面選模板面積
        /// </summary>
        /// <param name="selectedFace">選取的面</param>
        /// <param name="thickness">模板厚度</param>
        /// <returns>面積 (平方米)</returns>
        public static double CalculateFacePickedFormworkArea(Face selectedFace, double thickness = 0.018)
        {
            if (selectedFace == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ 選取的面無效");
                return 0.0;
            }
            
            try
            {
                double faceArea = 0.0;
                
                if (selectedFace is PlanarFace planarFace)
                {
                    faceArea = planarFace.Area;
                }
                else
                {
                    // 對於非平面，使用近似計算
                    faceArea = selectedFace.Area;
                }
                
                double areaM2 = ConvertToSquareMeters(faceArea);
                System.Diagnostics.Debug.WriteLine($"📊 面選模板面積: {areaM2:F2}m² (厚度: {thickness * 1000}mm)");
                
                return areaM2;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算面選模板面積失敗: {ex.Message}");
                return 0.0;
            }
        }
        
        /// <summary>
        /// 計算接觸面扣除面積（改進版 - 增強 Debug 輸出）
        /// </summary>
        /// <param name="formworkSolid">模板實體</param>
        /// <param name="structuralElements">結構元素</param>
        /// <param name="tolerance">容差值</param>
        /// <returns>扣除面積 (平方英尺)</returns>
        public static double CalculateContactDeductionArea(Solid formworkSolid, 
            IEnumerable<Element> structuralElements, double tolerance = 0.001)
        {
            if (formworkSolid == null || structuralElements == null) return 0.0;
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"  ├─ 開始計算 {structuralElements.Count()} 個鄰近元素的接觸面積");
                
                double totalDeduction = 0.0;
                int processedCount = 0;
                int validCount = 0;
                
                // 🚀 使用並行計算處理多個結構元素
                foreach (var element in structuralElements)
                {
                    processedCount++;
                    var elementSolids = GeometryExtractor.GetElementSolids(element);
                    
                    if (elementSolids.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"  │  ├─ [{processedCount}] ID {element.Id.Value}: 無法取得實體");
                        continue;
                    }
                    
                    foreach (var elementSolid in elementSolids)
                    {
                        if (!IsValidSolid(elementSolid)) continue;
                        
                        double contactArea = CalculateContactArea(formworkSolid, elementSolid, tolerance);
                        
                        if (contactArea > 0)
                        {
                            validCount++;
                            totalDeduction += contactArea;
                            
                            var categoryName = ElementCategorizer.GetCategoryName(element);
                            double contactAreaM2 = ConvertToSquareMeters(contactArea);
                            System.Diagnostics.Debug.WriteLine($"  │  ├─ [{processedCount}] {categoryName} (ID: {element.Id.Value}): 接觸面積 = {contactArea:F6} sq ft = {contactAreaM2:F6} m²");
                        }
                    }
                }
                
                double totalDeductionM2 = ConvertToSquareMeters(totalDeduction);
                System.Diagnostics.Debug.WriteLine($"  ├─ 接觸面扣除統計:");
                System.Diagnostics.Debug.WriteLine($"  │  ├─ 處理元素: {processedCount} 個");
                System.Diagnostics.Debug.WriteLine($"  │  ├─ 有效接觸: {validCount} 個");
                System.Diagnostics.Debug.WriteLine($"  │  └─ 總扣除: {totalDeduction:F6} sq ft = {totalDeductionM2:F6} m²");
                
                return totalDeduction;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"  └─ ❌ 計算接觸面扣除失敗: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"     堆疊追蹤: {ex.StackTrace}");
                return 0.0;
            }
        }
        
        /// <summary>
        /// 計算兩個實體的接觸面積
        /// </summary>
        /// <param name="solid1">實體1</param>
        /// <param name="solid2">實體2</param>
        /// <param name="tolerance">容差值</param>
        /// <returns>接觸面積</returns>
        public static double CalculateContactArea(Solid solid1, Solid solid2, double tolerance = 0.001)
        {
            if (solid1 == null || solid2 == null || !IsValidSolid(solid1) || !IsValidSolid(solid2))
                return 0.0;
            
            try
            {
                // 使用布林運算計算交集
                var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    solid1, solid2, BooleanOperationsType.Intersect);
                
                if (intersection != null && IsValidSolid(intersection))
                {
                    // 如果有交集，計算交集的表面積作為接觸面積
                    double contactArea = 0.0;
                    int faceCount = 0;
                    
                    foreach (Face face in intersection.Faces)
                    {
                        faceCount++;
                        contactArea += face.Area;
                    }
                    
                    if (contactArea > 0)
                    {
                        double contactAreaM2 = ConvertToSquareMeters(contactArea);
                        System.Diagnostics.Debug.WriteLine($"      ├─ 交集實體: {faceCount} 個面, 接觸面積 = {contactArea:F6} sq ft = {contactAreaM2:F6} m²");
                    }
                    
                    return contactArea;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"      └─ 無交集或交集無效");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"      └─ ⚠️ 接觸面積計算異常: {ex.Message}");
            }
            
            return 0.0;
        }
        
        /// <summary>
        /// 計算元素集合的總面積
        /// </summary>
        /// <param name="elements">元素集合</param>
        /// <returns>總面積 (平方米)</returns>
        public static double CalculateTotalElementsArea(IEnumerable<Element> elements)
        {
            if (elements == null) return 0.0;
            
            double totalArea = 0.0;
            
            try
            {
                foreach (var element in elements)
                {
                    var solids = GeometryExtractor.GetElementSolids(element);
                    foreach (var solid in solids)
                    {
                        if (IsValidSolid(solid))
                        {
                            foreach (Face face in solid.Faces)
                            {
                                totalArea += face.Area;
                            }
                        }
                    }
                }
                
                double totalAreaM2 = ConvertToSquareMeters(totalArea);
                System.Diagnostics.Debug.WriteLine($"📊 元素總面積: {totalAreaM2:F2}m²");
                
                return totalAreaM2;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算元素總面積失敗: {ex.Message}");
                return 0.0;
            }
        }
        
        /// <summary>
        /// 驗證實體是否有效
        /// </summary>
        /// <param name="solid">實體</param>
        /// <returns>是否有效</returns>
        public static bool IsValidSolid(Solid solid)
        {
            return solid != null && 
                   solid.Volume > 1e-10 && 
                   solid.SurfaceArea > 1e-10 && 
                   solid.Faces.Size > 0;
        }
        
        /// <summary>
        /// 轉換面積單位為平方米
        /// </summary>
        /// <param name="areaInFeet">英尺平方面積</param>
        /// <returns>平方米面積</returns>
        public static double ConvertToSquareMeters(double areaInFeet)
        {
            const double feetToMetersSquared = 0.092903; // 1 平方英尺 = 0.092903 平方米
            return areaInFeet * feetToMetersSquared;
        }
        
        /// <summary>
        /// 轉換面積單位為平方英尺
        /// </summary>
        /// <param name="areaInMeters">平方米面積</param>
        /// <returns>英尺平方面積</returns>
        public static double ConvertToSquareFeet(double areaInMeters)
        {
            const double metersToFeetSquared = 10.7639; // 1 平方米 = 10.7639 平方英尺
            return areaInMeters * metersToFeetSquared;
        }
        
        /// <summary>
        /// 計算面積的統計摘要
        /// </summary>
        /// <param name="areas">面積清單</param>
        /// <returns>統計摘要</returns>
        public static AreaStatistics CalculateAreaStatistics(IEnumerable<double> areas)
        {
            if (areas == null || !areas.Any())
            {
                return new AreaStatistics();
            }
            
            var areaList = areas.ToList();
            
            return new AreaStatistics
            {
                Count = areaList.Count,
                TotalArea = areaList.Sum(),
                AverageArea = areaList.Average(),
                MinArea = areaList.Min(),
                MaxArea = areaList.Max(),
                MedianArea = CalculateMedian(areaList)
            };
        }
        
        /// <summary>
        /// 計算中位數
        /// </summary>
        /// <param name="values">數值清單</param>
        /// <returns>中位數</returns>
        private static double CalculateMedian(List<double> values)
        {
            var sortedValues = values.OrderBy(x => x).ToList();
            int count = sortedValues.Count;
            
            if (count % 2 == 0)
            {
                return (sortedValues[count / 2 - 1] + sortedValues[count / 2]) / 2.0;
            }
            else
            {
                return sortedValues[count / 2];
            }
        }
        
        /// <summary>
        /// 🚀 性能優化: 批量計算多個模板實體的面積
        /// </summary>
        /// <param name="formworkSolids">模板實體清單</param>
        /// <param name="structuralElements">結構元素清單</param>
        /// <param name="includeDeduction">是否包含接觸面扣除</param>
        /// <returns>面積清單 (平方米)</returns>
        public static List<double> CalculateBatchFormworkAreas(IEnumerable<Solid> formworkSolids,
            IEnumerable<Element> structuralElements = null, bool includeDeduction = true)
        {
            if (formworkSolids == null) return new List<double>();
            
            var solids = formworkSolids.Where(IsValidSolid).ToList();
            if (solids.Count == 0) return new List<double>();
            
            try
            {
                // 🚀 並行處理多個模板實體
                return solids.AsParallel().Select(solid =>
                {
                    if (includeDeduction && structuralElements != null)
                    {
                        return CalculateFormworkAreaWithDeduction(solid, structuralElements);
                    }
                    else
                    {
                        return CalculateSimpleFormworkArea(solid);
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 批量面積計算失敗，回退到順序處理: {ex.Message}");
                
                // 回退到順序處理
                var results = new List<double>();
                foreach (var solid in solids)
                {
                    if (includeDeduction && structuralElements != null)
                    {
                        results.Add(CalculateFormworkAreaWithDeduction(solid, structuralElements));
                    }
                    else
                    {
                        results.Add(CalculateSimpleFormworkArea(solid));
                    }
                }
                return results;
            }
        }
        
        /// <summary>
        /// 🚀 性能優化: 清理面積計算快取
        /// </summary>
        public static void ClearAreaCache()
        {
            _areaCache.Clear();
            System.Diagnostics.Debug.WriteLine("✅ 面積計算快取已清理");
        }
    }
    
    /// <summary>
    /// 面積統計資料結構
    /// </summary>
    public class AreaStatistics
    {
        public int Count { get; set; }
        public double TotalArea { get; set; }
        public double AverageArea { get; set; }
        public double MinArea { get; set; }
        public double MaxArea { get; set; }
        public double MedianArea { get; set; }
        
        public override string ToString()
        {
            return $"統計: 數量={Count}, 總面積={TotalArea:F2}m², 平均={AverageArea:F2}m², 最小={MinArea:F2}m², 最大={MaxArea:F2}m², 中位數={MedianArea:F2}m²";
        }
    }
}