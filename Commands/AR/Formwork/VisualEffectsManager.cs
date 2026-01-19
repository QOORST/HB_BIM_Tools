using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 統一的視覺效果管理工具 - 解決重複程式碼問題
    /// </summary>
    public static class VisualEffectsManager
    {
        /// <summary>
        /// 設定元素外觀
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="elementId">元素ID</param>
        /// <param name="surfaceColor">表面顏色</param>
        /// <param name="material">材質</param>
        /// <param name="transparency">透明度 (0-100)</param>
        /// <param name="lineWeight">線重</param>
        /// <param name="fillPattern">填充圖案</param>
        public static void SetElementAppearance(Document doc, ElementId elementId, 
            Color surfaceColor = default(Color), Material material = null, int transparency = 0, 
            int lineWeight = 1, FillPatternElement fillPattern = null)
        {
            if (doc.ActiveView == null) return;
            
            var overrides = new OverrideGraphicSettings();
            
            try
            {
                // 材質設定 (注意：SetSurfaceMaterialId 在某些版本可能不存在，先註解)
                // if (material != null)
                // {
                //     overrides.SetSurfaceMaterialId(material.Id);
                // }
                
                // 顏色設定
                if (surfaceColor.Red != 0 || surfaceColor.Green != 0 || surfaceColor.Blue != 0)
                {
                    overrides.SetProjectionLineColor(surfaceColor);
                    overrides.SetSurfaceBackgroundPatternColor(surfaceColor);
                    overrides.SetSurfaceForegroundPatternColor(surfaceColor);
                }
                
                // 填充圖案設定
                if (fillPattern != null)
                {
                    overrides.SetSurfaceBackgroundPatternId(fillPattern.Id);
                    overrides.SetSurfaceForegroundPatternId(fillPattern.Id);
                    overrides.SetSurfaceBackgroundPatternVisible(true);
                    overrides.SetSurfaceForegroundPatternVisible(true);
                }
                
                // 透明度設定
                if (transparency > 0 && transparency <= 100)
                {
                    overrides.SetSurfaceTransparency(transparency);
                }
                
                // 線重設定
                if (lineWeight > 0)
                {
                    overrides.SetProjectionLineWeight(lineWeight);
                }
                
                doc.ActiveView.SetElementOverrides(elementId, overrides);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定元素 {elementId} 視覺效果失敗: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 設定結構分析外觀 (包含實體填充)
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="elementId">元素ID</param>
        /// <param name="color">顏色</param>
        /// <param name="category">構件類別</param>
        public static void SetStructuralAnalysisAppearance(Document doc, ElementId elementId, Color color, string category)
        {
            if (doc.ActiveView == null) return;
            
            try
            {
                var overrides = new OverrideGraphicSettings();
                
                // 基本顏色設定
                overrides.SetProjectionLineColor(color);
                overrides.SetSurfaceBackgroundPatternColor(color);
                overrides.SetSurfaceForegroundPatternColor(color);
                
                // 取得實體填充圖案
                var solidFillPatternId = GetSolidFillPatternId(doc);
                if (solidFillPatternId != null && solidFillPatternId != ElementId.InvalidElementId)
                {
                    overrides.SetSurfaceBackgroundPatternId(solidFillPatternId);
                    overrides.SetSurfaceForegroundPatternId(solidFillPatternId);
                    overrides.SetSurfaceBackgroundPatternVisible(true);
                    overrides.SetSurfaceForegroundPatternVisible(true);
                }
                
                // 線重和透明度
                overrides.SetProjectionLineWeight(2);
                overrides.SetSurfaceTransparency(30); // 30% 透明度讓顏色更柔和
                
                doc.ActiveView.SetElementOverrides(elementId, overrides);
                
                System.Diagnostics.Debug.WriteLine($"✅ 設定 {category} 元素 {elementId.Value} 外觀: R={color.Red}, G={color.Green}, B={color.Blue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 設定結構分析外觀失敗: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 設定預覽高亮效果
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="elementId">元素ID</param>
        /// <param name="isHighlighted">是否高亮</param>
        public static void SetPreviewHighlight(Document doc, ElementId elementId, bool isHighlighted = true)
        {
            if (doc.ActiveView == null) return;
            
            try
            {
                if (isHighlighted)
                {
                    var overrides = new OverrideGraphicSettings();
                    var highlightColor = new Color(255, 255, 0); // 黃色高亮
                    
                    overrides.SetProjectionLineColor(highlightColor);
                    overrides.SetProjectionLineWeight(3);
                    overrides.SetSurfaceTransparency(50);
                    
                    doc.ActiveView.SetElementOverrides(elementId, overrides);
                }
                else
                {
                    ClearElementOverrides(doc, elementId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定預覽高亮失敗: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 設定模板材質和顏色
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="elementId">元素ID</param>
        /// <param name="material">材質</param>
        /// <param name="transparency">透明度</param>
        public static void SetFormworkMaterialAndColor(Document doc, ElementId elementId, Material material, int transparency = 60)
        {
            if (material == null) return;
            
            try
            {
                // 取得所有可列印且非範本的視圖
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.CanBePrinted && !v.IsTemplate)
                    .ToList();
                
                var materialColor = GetMaterialColor(material);
                if (!materialColor.IsValid)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 材質顏色無效: {material.Name}");
                    return;
                }
                
                // 取得實心填充圖案ID
                var solidPatternId = GetSolidFillPatternId(doc);
                
                foreach (var view in allViews)
                {
                    try
                    {
                        var overrides = new OverrideGraphicSettings();
                        
                        // 設定填充圖案（重要：確保顏色能顯示）
                        if (solidPatternId != ElementId.InvalidElementId)
                        {
                            overrides.SetSurfaceForegroundPatternId(solidPatternId);
                            overrides.SetSurfaceBackgroundPatternId(solidPatternId);
                            overrides.SetCutForegroundPatternId(solidPatternId);
                            overrides.SetCutBackgroundPatternId(solidPatternId);
                        }
                        
                        // 設定所有顏色屬性（確保在不同視圖模式下都能顯示）
                        overrides.SetSurfaceForegroundPatternColor(materialColor);
                        overrides.SetSurfaceBackgroundPatternColor(materialColor);
                        overrides.SetProjectionLineColor(materialColor);
                        overrides.SetCutLineColor(materialColor);
                        overrides.SetCutForegroundPatternColor(materialColor);
                        overrides.SetCutBackgroundPatternColor(materialColor);
                        
                        // 3D視圖特殊設定
                        if (view is View3D)
                        {
                            overrides.SetSurfaceTransparency(transparency);
                            overrides.SetProjectionLineWeight(3);
                            overrides.SetCutLineWeight(3);
                        }
                        else
                        {
                            // 其他視圖使用較細的線條
                            overrides.SetProjectionLineWeight(1);
                        }
                        
                        view.SetElementOverrides(elementId, overrides);
                        System.Diagnostics.Debug.WriteLine($"✅ 為視圖 '{view.Name}' 設定材質顏色: R={materialColor.Red}, G={materialColor.Green}, B={materialColor.Blue}");
                    }
                    catch (Exception viewEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ 視圖 '{view.Name}' 設定失敗: {viewEx.Message}");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ 完成為 {allViews.Count} 個視圖設定材質顏色");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 設定模板材質和顏色失敗: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清除元素覆蓋設定
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="elementId">元素ID</param>
        public static void ClearElementOverrides(Document doc, ElementId elementId)
        {
            if (doc.ActiveView != null)
            {
                try
                {
                    doc.ActiveView.SetElementOverrides(elementId, new OverrideGraphicSettings());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"清除元素 {elementId} 覆蓋設定失敗: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 🚀 性能優化: 批量清除元素覆蓋設定
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="elementIds">元素ID清單</param>
        public static void ClearMultipleElementOverrides(Document doc, System.Collections.Generic.IEnumerable<ElementId> elementIds)
        {
            if (doc.ActiveView == null) return;
            
            var clearOverrides = new OverrideGraphicSettings();
            var elementIdList = elementIds.ToList();
            
            // 🚀 性能優化: 分批處理大量元素，避免UI凍結
            const int batchSize = 100;
            for (int i = 0; i < elementIdList.Count; i += batchSize)
            {
                var batch = elementIdList.Skip(i).Take(batchSize);
                
                foreach (var elementId in batch)
                {
                    try
                    {
                        doc.ActiveView.SetElementOverrides(elementId, clearOverrides);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"清除元素 {elementId} 覆蓋設定失敗: {ex.Message}");
                    }
                }
                
                // 讓 UI 有機會更新
                if (i > 0 && i % 500 == 0)
                {
                    System.Threading.Thread.Sleep(1);
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ 批量清除 {elementIdList.Count} 個元素的視覺效果完成");
        }
        
        /// <summary>
        /// 🚀 性能優化: 批量設定結構分析外觀
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="elementIds">元素ID清單</param>
        /// <param name="colors">對應的顏色清單</param>
        /// <param name="categories">對應的類別清單</param>
        public static void SetBatchStructuralAnalysisAppearance(Document doc, 
            System.Collections.Generic.List<ElementId> elementIds, 
            System.Collections.Generic.List<Color> colors, 
            System.Collections.Generic.List<string> categories)
        {
            if (doc.ActiveView == null || elementIds == null || colors == null || categories == null) return;
            if (elementIds.Count != colors.Count || elementIds.Count != categories.Count) return;
            
            var solidFillPatternId = GetSolidFillPatternId(doc);
            
            // 🚀 分批處理避免UI凍結
            const int batchSize = 50;
            for (int i = 0; i < elementIds.Count; i += batchSize)
            {
                int endIndex = Math.Min(i + batchSize, elementIds.Count);
                
                for (int j = i; j < endIndex; j++)
                {
                    try
                    {
                        var elementId = elementIds[j];
                        var color = colors[j];
                        var category = categories[j];
                        
                        var overrides = new OverrideGraphicSettings();
                        
                        // 基本顏色設定
                        overrides.SetProjectionLineColor(color);
                        overrides.SetSurfaceBackgroundPatternColor(color);
                        overrides.SetSurfaceForegroundPatternColor(color);
                        
                        // 實體填充設定
                        if (solidFillPatternId != null && solidFillPatternId != ElementId.InvalidElementId)
                        {
                            overrides.SetSurfaceBackgroundPatternId(solidFillPatternId);
                            overrides.SetSurfaceForegroundPatternId(solidFillPatternId);
                            overrides.SetSurfaceBackgroundPatternVisible(true);
                            overrides.SetSurfaceForegroundPatternVisible(true);
                        }
                        
                        overrides.SetProjectionLineWeight(2);
                        overrides.SetSurfaceTransparency(30);
                        
                        doc.ActiveView.SetElementOverrides(elementId, overrides);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 批量設定元素外觀失敗: {ex.Message}");
                    }
                }
                
                // UI 更新間隔
                if (i > 0 && i % 200 == 0)
                {
                    System.Threading.Thread.Sleep(1);
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ 批量設定 {elementIds.Count} 個元素的結構分析外觀完成");
        }
        
        /// <summary>
        /// 取得實體填充圖案ID
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <returns>實體填充圖案ID</returns>
        private static ElementId GetSolidFillPatternId(Document doc)
        {
            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement));
                
                foreach (FillPatternElement fillPatternElement in collector)
                {
                    var fillPattern = fillPatternElement.GetFillPattern();
                    if (fillPattern.IsSolidFill)
                    {
                        return fillPatternElement.Id;
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("⚠️ 找不到實體填充圖案");
                return ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 取得實體填充圖案失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }
        
        /// <summary>
        /// 取得材質顏色
        /// </summary>
        /// <param name="material">材質</param>
        /// <returns>材質顏色</returns>
        private static Color GetMaterialColor(Material material)
        {
            try
            {
                // 嘗試取得表面顏色
                if (material.SurfaceForegroundPatternColor.IsValid)
                {
                    return material.SurfaceForegroundPatternColor;
                }
                
                if (material.SurfaceBackgroundPatternColor.IsValid)
                {
                    return material.SurfaceBackgroundPatternColor;
                }
                
                // 如果沒有設定顏色，返回預設顏色
                return new Color(200, 200, 200); // 淺灰色
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"取得材質顏色失敗: {ex.Message}");
                return new Color(200, 200, 200); // 預設淺灰色
            }
        }
    }
}