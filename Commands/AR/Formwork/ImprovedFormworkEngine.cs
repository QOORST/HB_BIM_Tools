using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 基於 Dynamo 腳本邏輯的改進版模板引擎
    /// 修正扣除錯誤，採用一般模型（DirectShape）作法
    /// </summary>
    internal static class ImprovedFormworkEngine
    {
        private const double MIN_FACE_AREA_M2 = 0.01; // 最小面積 1cm²
        private const double FORMWORK_THICKNESS_MM = 18.0; // 預設模板厚度 18mm
        private const double GEOMETRY_TOLERANCE = 1e-6;

        /// <summary>
        /// 主要入口：從元素生成模板（基於 Dynamo 邏輯 - 改進版接觸扣除）
        /// </summary>
        public static List<ElementId> CreateFormworkFromElement(Document doc, Element element, double thicknessMm = FORMWORK_THICKNESS_MM)
        {
            var formworkIds = new List<ElementId>();

            try
            {
                System.Diagnostics.Debug.WriteLine($"========== 改進引擎開始處理 ==========");
                System.Diagnostics.Debug.WriteLine($"目標元素 {element.Id}：{element.Category?.Name}");

                // Step 1: 取得目標元素的面（模仿 Dynamo 的 PolySurface.Surfaces）
                var elementSurfaces = GetElementSurfaces(element);
                System.Diagnostics.Debug.WriteLine($"目標元素有 {elementSurfaces.Count} 個平面");

                // Step 2: 按方向篩選面（模仿 Dynamo 的 Surface.FilterByOrientation）
                var filteredSurfaces = FilterSurfacesByOrientation(elementSurfaces);
                System.Diagnostics.Debug.WriteLine($"篩選後有 {filteredSurfaces.Count} 個需要模板的面");

                // Step 3: 為每個面生成模板（使用智能接觸扣除）
                int successCount = 0;
                for (int i = 0; i < filteredSurfaces.Count; i++)
                {
                    var surface = filteredSurfaces[i];
                    System.Diagnostics.Debug.WriteLine($"--- 處理第 {i + 1}/{filteredSurfaces.Count} 個面 ---");
                    
                    try
                    {
                        var formworkId = CreateFormworkForSurface(doc, element, surface, thicknessMm);
                        if (formworkId != ElementId.InvalidElementId)
                        {
                            formworkIds.Add(formworkId);
                            successCount++;
                            System.Diagnostics.Debug.WriteLine($"✅ 面 {i + 1} 模板生成成功，ID: {formworkId}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ 面 {i + 1} 模板生成失敗");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 面 {i + 1} 處理異常: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"========== 處理完成 ==========");
                System.Diagnostics.Debug.WriteLine($"總計生成 {successCount}/{filteredSurfaces.Count} 個模板");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CreateFormworkFromElement 完全失敗: {ex.Message}");
            }

            return formworkIds;
        }

        /// <summary>
        /// Step 1: 取得元素表面（PolySurface.Surfaces）
        /// </summary>
        private static List<PlanarFace> GetElementSurfaces(Element element)
        {
            var surfaces = new List<PlanarFace>();

            // 🚀 重構: 使用 GeometryExtractor 統一工具
            var solids = GeometryExtractor.GetElementSolids(element);
            foreach (var solid in solids)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        surfaces.Add(planarFace);
                    }
                }
            }

            return surfaces;
        }

        /// <summary>
        /// Step 2: 按方向篩選面（Surface.FilterByOrientation）
        /// 遵循模板實務原則: 只生成垂直面和底面模板
        /// </summary>
        private static List<PlanarFace> FilterSurfacesByOrientation(List<PlanarFace> surfaces)
        {
            var filteredSurfaces = new List<PlanarFace>();

            foreach (var surface in surfaces)
            {
                try
                {
                    var normal = surface.FaceNormal;
                    var area = surface.Area * 0.092903; // 轉換為平方米

                    // 面積過濾
                    if (area < MIN_FACE_AREA_M2) continue;

                    // 方向過濾（模板實務原則）
                    // ✅ 垂直面：Z分量接近0 (柱側面、梁側面、牆面)
                    // ✅ 底面：Z分量 < -0.7 (梁底、板底)
                    // ❌ 頂面：不生成 (頂面通常無需模板或被上層結構覆蓋)
                    bool isVertical = Math.Abs(normal.Z) < 0.3;
                    bool isHorizontalBottom = normal.Z < -0.7;

                    if (isVertical || isHorizontalBottom)
                    {
                        string faceType = isVertical ? "垂直面" : "底面";
                        System.Diagnostics.Debug.WriteLine($"✅ 面通過篩選 - 類型:{faceType}, Normal:({normal.X:F2},{normal.Y:F2},{normal.Z:F2}), Area:{area:F3}m²");
                        filteredSurfaces.Add(surface);
                    }
                    else
                    {
                        string reason = normal.Z > 0.7 ? "頂面(不需要模板)" : "非標準方向";
                        System.Diagnostics.Debug.WriteLine($"⏭️ 面被過濾 - Normal:({normal.X:F2},{normal.Y:F2},{normal.Z:F2}), Area:{area:F3}m², 原因:{reason}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 篩選面失敗: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"📊 篩選結果: {filteredSurfaces.Count}/{surfaces.Count} 個面符合模板實務原則");
            return filteredSurfaces;
        }

        /// <summary>
        /// Step 3: 為單個面生成模板（改用智能接觸扣除）
        /// </summary>
        private static ElementId CreateFormworkForSurface(Document doc, Element hostElement, 
            PlanarFace surface, double thicknessMm)
        {
            try
            {
                // 從面建立基本模板實體
                var formworkSolid = ExtrudeFormworkFromFace(surface, thicknessMm);
                if (formworkSolid?.Volume <= GEOMETRY_TOLERANCE)
                {
                    System.Diagnostics.Debug.WriteLine("無法建立基本模板實體");
                    return ElementId.InvalidElementId;
                }

                System.Diagnostics.Debug.WriteLine($"基本模板體積: {formworkSolid.Volume:F6}");

                // ✅ 新方法: 獲取鄰近元素並進行智能接觸扣除
                // 🔧 修正: 使用與面生面工具相同的搜尋邏輯,但擴大範圍確保能找到上方結構
                // 搜尋範圍: 模板厚度 + 3000mm 緩衝 (3公尺足以覆蓋大部分樓層高度)
                var searchRadiusMm = thicknessMm + 3000.0; 
                System.Diagnostics.Debug.WriteLine($"🔍 搜尋半徑: {searchRadiusMm:F0} mm ({searchRadiusMm/304.8:F2} ft)");
                System.Diagnostics.Debug.WriteLine($"🔍 面中心位置: ({surface.Origin.X:F2}, {surface.Origin.Y:F2}, {surface.Origin.Z:F2})");
                System.Diagnostics.Debug.WriteLine($"🔍 面法向量: ({surface.FaceNormal.X:F2}, {surface.FaceNormal.Y:F2}, {surface.FaceNormal.Z:F2})");
                
                var nearbyElements = GeometryExtractor.GetNearbyStructuralElementsFromFace(doc, hostElement, surface, searchRadiusMm);
                System.Diagnostics.Debug.WriteLine($"找到 {nearbyElements.Count} 個鄰近元素");
                
                // 列出找到的元素類型
                if (nearbyElements.Count > 0)
                {
                    var elementTypes = nearbyElements.GroupBy(e => e.Category?.Name ?? "未知")
                        .Select(g => $"{g.Key}({g.Count()})")
                        .ToArray();
                    System.Diagnostics.Debug.WriteLine($"  元素類型: {string.Join(", ", elementTypes)}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  ⚠️ 未找到任何鄰近元素，模板將不會被扣除");
                }

                // 🔧 改進: 根據宿主元素類型調整扣除策略
                // 柱子穿過樓板時,需要更積極的扣除策略 (降低閾值)
                bool isColumn = hostElement.Category?.Id?.Value == (long)BuiltInCategory.OST_StructuralColumns;
                double intersectionThreshold = isColumn ? 0.01 : 0.05; // 柱子使用 1% 閾值,其他使用 5%
                
                System.Diagnostics.Debug.WriteLine($"🎯 宿主元素類型: {hostElement.Category?.Name}, 使用閾值: {intersectionThreshold:F2} ({intersectionThreshold * 100}%)");

                // 使用智能接觸扣除邏輯（傳入宿主元素）
                var finalFormwork = GeometryExtractor.ApplySmartContactDeduction(formworkSolid, nearbyElements, intersectionThreshold, hostElement);
                if (finalFormwork?.Volume <= GEOMETRY_TOLERANCE)
                {
                    System.Diagnostics.Debug.WriteLine("扣除後模板體積過小或完全被覆蓋");
                    return ElementId.InvalidElementId;
                }

                System.Diagnostics.Debug.WriteLine($"最終模板體積: {finalFormwork.Volume:F6}");

                // 建立 DirectShape
                var directShape = CreateDirectShape(doc, finalFormwork, hostElement);
                if (directShape != null)
                {
                    // 計算並設定面積參數（模仿 Dynamo 的 Surface.Area + Convert By Units）
                    SetFormworkParameters(directShape, hostElement, finalFormwork);
                    return directShape.Id;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"為面生成模板失敗: {ex.Message}");
            }

            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// 從面擠出模板實體（修正版 - 避免與結構重疊）
        /// </summary>
        private static Solid ExtrudeFormworkFromFace(PlanarFace face, double thicknessMm)
        {
            try
            {
                var thickness = thicknessMm / 304.8; // 轉換為英尺
                var normal = face.FaceNormal;

                // 取得面的邊界曲線
                var curveLoops = face.GetEdgesAsCurveLoops();
                if (curveLoops.Count == 0) return null;

                // 修正：先將面向外偏移一個小距離，再向外擠出
                var offsetDistance = 0.01; // 1cm 偏移，避免與原結構重疊
                var offsetVector = normal.Multiply(offsetDistance);
                
                // 偏移曲線迴路
                var offsetLoops = new List<CurveLoop>();
                foreach (var loop in curveLoops)
                {
                    var offsetLoop = new CurveLoop();
                    foreach (Curve curve in loop)
                    {
                        var offsetCurve = curve.CreateTransformed(Transform.CreateTranslation(offsetVector));
                        offsetLoop.Append(offsetCurve);
                    }
                    offsetLoops.Add(offsetLoop);
                }

                // 從偏移位置向外擠出模板
                var extrusionVector = normal.Multiply(thickness);
                return GeometryCreationUtilities.CreateExtrusionGeometry(
                    offsetLoops, extrusionVector, thickness);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"擠出模板實體失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 建立 DirectShape
        /// </summary>
        private static DirectShape CreateDirectShape(Document doc, Solid solid, Element hostElement)
        {
            try
            {
                var directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                directShape.ApplicationId = "YD_BIM_Formwork";
                directShape.ApplicationDataId = "ImprovedEngine";
                directShape.SetShape(new GeometryObject[] { solid });
                directShape.Name = $"改進模板_{hostElement.Id}";

                return directShape;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"建立 DirectShape 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 設定模板參數（改進版 - 基於體積計算面積）
        /// </summary>
        private static void SetFormworkParameters(DirectShape formwork, Element hostElement, Solid formworkSolid)
        {
            try
            {
                // ✅ 1. 判斷並設定模板類別
                string category = GetFormworkCategory(hostElement);
                var categoryParam = formwork.LookupParameter(SharedParams.P_Category);
                if (categoryParam != null && !categoryParam.IsReadOnly)
                {
                    categoryParam.Set(category);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定 P_Category = {category}");
                }

                // ✅ 2. 設定 P_HostId 參數
                var hostIdParam = formwork.LookupParameter(SharedParams.P_HostId);
                if (hostIdParam != null && !hostIdParam.IsReadOnly)
                {
                    hostIdParam.Set(hostElement.Id.ToString());
                    System.Diagnostics.Debug.WriteLine($"✅ 設定 P_HostId = {hostElement.Id}");
                }
                
                // ✅ 3. 用體積/厚度計算面積
                // 模板體積 (立方英尺) → 立方米
                double volumeM3 = formworkSolid.Volume * 0.0283168; // ft³ → m³
                
                // 模板厚度 (使用 FORMWORK_THICKNESS_MM)
                double thicknessMm = FORMWORK_THICKNESS_MM;
                double thicknessM = thicknessMm / 1000.0; // mm → m
                
                // 面積 = 體積 / 厚度
                double calculatedAreaM2 = volumeM3 / thicknessM;
                
                System.Diagnostics.Debug.WriteLine($"📐 面積計算: 體積={volumeM3:F6}m³, 厚度={thicknessMm}mm, 面積={calculatedAreaM2:F3}m²");
                
                // ✅ 4. 設定有效面積參數
                var effectiveAreaParam = formwork.LookupParameter(SharedParams.P_EffectiveArea);
                if (effectiveAreaParam != null && !effectiveAreaParam.IsReadOnly)
                {
                    // 使用 AreaCalculator 轉換為 Revit 內部單位 (平方英尺)
                    double areaFt2 = AreaCalculator.ConvertToSquareFeet(calculatedAreaM2);
                    effectiveAreaParam.Set(areaFt2);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定 P_EffectiveArea = {calculatedAreaM2:F3}m² ({areaFt2:F3}ft²)");
                }

                // ✅ 5. 設定總面積參數 (與有效面積相同)
                var totalParam = formwork.LookupParameter(SharedParams.P_Total);
                if (totalParam != null && !totalParam.IsReadOnly)
                {
                    double areaFt2 = AreaCalculator.ConvertToSquareFeet(calculatedAreaM2);
                    totalParam.Set(areaFt2);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定 P_Total = {calculatedAreaM2:F3}m²");
                }

                // 設定註解（包含詳細資訊）
                var commentParam = formwork.LookupParameter("註解");
                if (commentParam != null && !commentParam.IsReadOnly)
                {
                    commentParam.Set($"改進模板 - {category} - 宿主:{hostElement.Id} - 面積:{calculatedAreaM2:F2}m²");
                }

                // 設定標記
                var markParam = formwork.LookupParameter("標記");
                if (markParam != null && !markParam.IsReadOnly)
                {
                    markParam.Set($"IMP_{hostElement.Id}");
                }

                System.Diagnostics.Debug.WriteLine($"✅ 參數設定完成 - 類別:{category}, 面積:{calculatedAreaM2:F2}m²");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定模板參數失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 根據宿主元素類型判斷模板類別
        /// </summary>
        private static string GetFormworkCategory(Element hostElement)
        {
            if (hostElement == null || hostElement.Category == null)
                return "其他";

            var categoryId = hostElement.Category.Id.Value;

            if (categoryId == (long)BuiltInCategory.OST_StructuralColumns)
                return "柱模板";
            else if (categoryId == (long)BuiltInCategory.OST_StructuralFraming)
                return "梁模板";
            else if (categoryId == (long)BuiltInCategory.OST_Floors)
                return "板模板";
            else if (categoryId == (long)BuiltInCategory.OST_Walls)
                return "牆模板";
            else if (categoryId == (long)BuiltInCategory.OST_StructuralFoundation)
                return "基礎模板";
            else if (categoryId == (long)BuiltInCategory.OST_Stairs)
                return "樓梯模板";
            else
                return "其他";
        }
    }
}