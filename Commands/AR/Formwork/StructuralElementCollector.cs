using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 統一的結構元素收集工具 - 解決重複程式碼問題
    /// </summary>
    public static class StructuralElementCollector
    {
        /// <summary>
        /// 收集所有結構元素
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="includeFoundation">是否包含基礎</param>
        /// <returns>結構元素清單</returns>
        public static List<Element> CollectAll(Document doc, bool includeFoundation = false)
        {
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Stairs
            };
            
            if (includeFoundation)
            {
                categories.Add(BuiltInCategory.OST_StructuralFoundation);
            }
                
            return CollectByCategories(doc, categories);
        }
        
        /// <summary>
        /// 收集混凝土澆置結構元素
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <returns>混凝土結構元素清單</returns>
        public static List<Element> CollectConcrete(Document doc)
        {
            return CollectAll(doc).Where(IsConcreteCastInPlace).ToList();
        }
        
        /// <summary>
        /// 收集指定類別的結構元素
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="categories">要收集的類別清單</param>
        /// <returns>結構元素清單</returns>
        public static List<Element> CollectByCategories(Document doc, List<BuiltInCategory> categories)
        {
            if (categories == null || categories.Count == 0)
                return new List<Element>();
            
            try
            {
                // 🚀 性能優化: 使用單一過濾器合併多個類別，避免多次遍歷文檔
                var categoryFilter = new ElementMulticategoryFilter(categories);
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(categoryFilter)
                    .WhereElementIsNotElementType();
                
                return collector.ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"批量收集類別失敗，回退到逐一收集: {ex.Message}");
                
                // 回退機制：如果批量失敗則逐一收集
                var elements = new List<Element>();
                foreach (var category in categories)
                {
                    try
                    {
                        var collector = new FilteredElementCollector(doc)
                            .OfCategory(category)
                            .WhereElementIsNotElementType();
                        
                        elements.AddRange(collector);
                    }
                    catch (Exception categoryEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"收集類別 {category} 失敗: {categoryEx.Message}");
                    }
                }
                return elements;
            }
        }
        
        /// <summary>
        /// 收集指定視圖中的結構元素
        /// </summary>
        /// <param name="doc">Revit文檔</param>
        /// <param name="viewId">視圖ID</param>
        /// <param name="includeFoundation">是否包含基礎</param>
        /// <returns>結構元素清單</returns>
        public static List<Element> CollectInView(Document doc, ElementId viewId, bool includeFoundation = false)
        {
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Stairs
            };
            
            if (includeFoundation)
            {
                categories.Add(BuiltInCategory.OST_StructuralFoundation);
            }
            
            var elements = new List<Element>();
            
            foreach (var category in categories)
            {
                try
                {
                    var collector = new FilteredElementCollector(doc, viewId)
                        .OfCategory(category)
                        .WhereElementIsNotElementType();
                    
                    elements.AddRange(collector);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"在視圖 {viewId} 中收集類別 {category} 失敗: {ex.Message}");
                }
            }
            
            return elements;
        }
        
        /// <summary>
        /// 判斷元素是否為混凝土澆置構件
        /// </summary>
        /// <param name="element">要判斷的元素</param>
        /// <returns>是否為混凝土澆置構件</returns>
        private static bool IsConcreteCastInPlace(Element element)
        {
            try
            {
                // 檢查材質參數
                var materialParam = element.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (materialParam != null)
                {
                    var materialName = materialParam.AsValueString()?.ToLower() ?? "";
                    if (materialName.Contains("concrete") || materialName.Contains("混凝土"))
                    {
                        return true;
                    }
                }
                
                // 檢查族群類型名稱
                if (element is FamilyInstance familyInstance)
                {
                    var typeName = familyInstance.Symbol?.Name?.ToLower() ?? "";
                    if (typeName.Contains("concrete") || typeName.Contains("混凝土") || 
                        typeName.Contains("rc") || typeName.Contains("鋼筋混凝土"))
                    {
                        return true;
                    }
                }
                
                // 檢查類別名稱
                var categoryName = element.Category?.Name?.ToLower() ?? "";
                if (categoryName.Contains("concrete") || categoryName.Contains("混凝土"))
                {
                    return true;
                }
                
                // 預設認為結構元素都需要模板
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"判斷元素 {element.Id} 材質失敗: {ex.Message}");
                return true; // 發生錯誤時預設為需要模板
            }
        }
    }
}