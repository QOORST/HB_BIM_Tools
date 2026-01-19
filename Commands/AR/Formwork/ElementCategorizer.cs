using System;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 統一的元素類別識別工具 - 解決重複程式碼問題
    /// </summary>
    public static class ElementCategorizer
    {
        /// <summary>
        /// 取得元素的中文類別名稱
        /// </summary>
        /// <param name="element">要識別的元素</param>
        /// <returns>中文類別名稱</returns>
        public static string GetCategoryName(Element element)
        {
            if (element?.Category?.Id == null) return "未知構件";
            
            var categoryId = element.Category.Id.Value;
            
            switch (categoryId)
            {
                case (long)BuiltInCategory.OST_StructuralColumns:
                    return "結構柱";
                case (long)BuiltInCategory.OST_StructuralFraming:
                    return "結構梁";
                case (long)BuiltInCategory.OST_Floors:
                    return "樓板";
                case (long)BuiltInCategory.OST_Walls:
                    return "結構牆";
                case (long)BuiltInCategory.OST_Stairs:
                    return "樓梯";
                case (long)BuiltInCategory.OST_StructuralFoundation:
                    return "基礎";
                case (long)BuiltInCategory.OST_Ramps:
                    return "坡道";
                default:
                    return GetCategoryByFallback(element);
            }
        }
        
        /// <summary>
        /// 取得元素的英文類別名稱
        /// </summary>
        /// <param name="element">要識別的元素</param>
        /// <returns>英文類別名稱</returns>
        public static string GetCategoryNameEnglish(Element element)
        {
            if (element?.Category?.Id == null) return "Unknown";
            
            var categoryId = element.Category.Id.Value;
            
            switch (categoryId)
            {
                case (long)BuiltInCategory.OST_StructuralColumns:
                    return "Column";
                case (long)BuiltInCategory.OST_StructuralFraming:
                    return "Beam";
                case (long)BuiltInCategory.OST_Floors:
                    return "Slab";
                case (long)BuiltInCategory.OST_Walls:
                    return "Wall";
                case (long)BuiltInCategory.OST_Stairs:
                    return "Stairs";
                case (long)BuiltInCategory.OST_StructuralFoundation:
                    return "Foundation";
                case (long)BuiltInCategory.OST_Ramps:
                    return "Ramp";
                default:
                    return GetCategoryByFallbackEnglish(element);
            }
        }
        
        /// <summary>
        /// 檢查元素是否為結構柱
        /// </summary>
        /// <param name="element">要檢查的元素</param>
        /// <returns>是否為結構柱</returns>
        public static bool IsStructuralColumn(Element element)
        {
            return element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
        }
        
        /// <summary>
        /// 檢查元素是否為結構梁
        /// </summary>
        /// <param name="element">要檢查的元素</param>
        /// <returns>是否為結構梁</returns>
        public static bool IsStructuralBeam(Element element)
        {
            return element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
        }
        
        /// <summary>
        /// 檢查元素是否為樓板
        /// </summary>
        /// <param name="element">要檢查的元素</param>
        /// <returns>是否為樓板</returns>
        public static bool IsFloor(Element element)
        {
            return element?.Category?.Id.Value == (long)BuiltInCategory.OST_Floors;
        }
        
        /// <summary>
        /// 檢查元素是否為牆
        /// </summary>
        /// <param name="element">要檢查的元素</param>
        /// <returns>是否為牆</returns>
        public static bool IsWall(Element element)
        {
            return element?.Category?.Id.Value == (long)BuiltInCategory.OST_Walls;
        }
        
        /// <summary>
        /// 檢查元素是否為樓梯
        /// </summary>
        /// <param name="element">要檢查的元素</param>
        /// <returns>是否為樓梯</returns>
        public static bool IsStairs(Element element)
        {
            return element?.Category?.Id.Value == (long)BuiltInCategory.OST_Stairs;
        }
        
        /// <summary>
        /// 檢查元素是否為基礎
        /// </summary>
        /// <param name="element">要檢查的元素</param>
        /// <returns>是否為基礎</returns>
        public static bool IsFoundation(Element element)
        {
            return element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;
        }
        
        /// <summary>
        /// 檢查元素是否為結構元素
        /// </summary>
        /// <param name="element">要檢查的元素</param>
        /// <returns>是否為結構元素</returns>
        public static bool IsStructuralElement(Element element)
        {
            if (element?.Category?.Id == null) return false;
            
            var categoryId = element.Category.Id.Value;
            
            return categoryId == (long)BuiltInCategory.OST_StructuralColumns ||
                   categoryId == (long)BuiltInCategory.OST_StructuralFraming ||
                   categoryId == (long)BuiltInCategory.OST_Floors ||
                   categoryId == (long)BuiltInCategory.OST_Walls ||
                   categoryId == (long)BuiltInCategory.OST_Stairs ||
                   categoryId == (long)BuiltInCategory.OST_StructuralFoundation;
        }
        
        /// <summary>
        /// 取得結構元素的BuiltInCategory
        /// </summary>
        /// <param name="element">要檢查的元素</param>
        /// <returns>BuiltInCategory，如果不是結構元素則返回null</returns>
        public static BuiltInCategory? GetStructuralCategory(Element element)
        {
            if (element?.Category?.Id == null) return null;
            
            var categoryId = element.Category.Id.Value;
            
            switch (categoryId)
            {
                case (long)BuiltInCategory.OST_StructuralColumns:
                    return BuiltInCategory.OST_StructuralColumns;
                case (long)BuiltInCategory.OST_StructuralFraming:
                    return BuiltInCategory.OST_StructuralFraming;
                case (long)BuiltInCategory.OST_Floors:
                    return BuiltInCategory.OST_Floors;
                case (long)BuiltInCategory.OST_Walls:
                    return BuiltInCategory.OST_Walls;
                case (long)BuiltInCategory.OST_Stairs:
                    return BuiltInCategory.OST_Stairs;
                case (long)BuiltInCategory.OST_StructuralFoundation:
                    return BuiltInCategory.OST_StructuralFoundation;
                default:
                    return null;
            }
        }
        
        /// <summary>
        /// 備用方法：根據名稱判斷類別 (中文)
        /// </summary>
        /// <param name="element">要判斷的元素</param>
        /// <returns>中文類別名稱</returns>
        private static string GetCategoryByFallback(Element element)
        {
            var name = element.Category?.Name ?? "";
            var elementName = element.Name ?? "";
            
            // 檢查類別名稱
            if (name.Contains("柱") || name.Contains("Column")) return "結構柱";
            if (name.Contains("梁") || name.Contains("Beam") || name.Contains("Framing")) return "結構梁";
            if (name.Contains("板") || name.Contains("樓板") || name.Contains("Floor") || name.Contains("Slab")) return "樓板";
            if (name.Contains("牆") || name.Contains("Wall")) return "結構牆";
            if (name.Contains("樓梯") || name.Contains("Stair")) return "樓梯";
            if (name.Contains("基礎") || name.Contains("Foundation")) return "基礎";
            if (name.Contains("坡道") || name.Contains("Ramp")) return "坡道";
            
            // 檢查元素名稱
            if (elementName.Contains("柱") || elementName.Contains("Column")) return "結構柱";
            if (elementName.Contains("梁") || elementName.Contains("Beam")) return "結構梁";
            if (elementName.Contains("板") || elementName.Contains("樓板") || elementName.Contains("Floor")) return "樓板";
            if (elementName.Contains("牆") || elementName.Contains("Wall")) return "結構牆";
            if (elementName.Contains("樓梯") || elementName.Contains("Stair")) return "樓梯";
            
            // 根據元素類型進一步判斷
            if (element is FamilyInstance)
            {
                // 族群實例通常是柱或梁
                return CheckFamilyInstanceType(element as FamilyInstance);
            }
            else if (element is Floor)
            {
                return "樓板";
            }
            else if (element is Wall)
            {
                return "結構牆";
            }
            
            return "未知構件";
        }
        
        /// <summary>
        /// 備用方法：根據名稱判斷類別 (英文)
        /// </summary>
        /// <param name="element">要判斷的元素</param>
        /// <returns>英文類別名稱</returns>
        private static string GetCategoryByFallbackEnglish(Element element)
        {
            var name = element.Category?.Name ?? "";
            var elementName = element.Name ?? "";
            
            if (name.Contains("Column") || elementName.Contains("Column")) return "Column";
            if (name.Contains("Beam") || name.Contains("Framing") || elementName.Contains("Beam")) return "Beam";
            if (name.Contains("Floor") || name.Contains("Slab") || elementName.Contains("Floor")) return "Slab";
            if (name.Contains("Wall") || elementName.Contains("Wall")) return "Wall";
            if (name.Contains("Stair") || elementName.Contains("Stair")) return "Stairs";
            if (name.Contains("Foundation") || elementName.Contains("Foundation")) return "Foundation";
            if (name.Contains("Ramp") || elementName.Contains("Ramp")) return "Ramp";
            
            if (element is Floor) return "Slab";
            if (element is Wall) return "Wall";
            
            return "Unknown";
        }
        
        /// <summary>
        /// 檢查族群實例的類型
        /// </summary>
        /// <param name="familyInstance">族群實例</param>
        /// <returns>推測的類別名稱</returns>
        private static string CheckFamilyInstanceType(FamilyInstance familyInstance)
        {
            try
            {
                // 檢查族群名稱 (簡化版本，不使用新語法)
                var familyName = "";
                if (familyInstance.Symbol?.Family?.Name != null)
                {
                    familyName = familyInstance.Symbol.Family.Name;
                }
                
                if (familyName.Contains("柱") || familyName.Contains("Column")) return "結構柱";
                if (familyName.Contains("梁") || familyName.Contains("Beam")) return "結構梁";
                if (familyName.Contains("基礎") || familyName.Contains("Foundation")) return "基礎";
                
                return "結構柱"; // 預設為柱
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"檢查族群實例類型失敗: {ex.Message}");
                return "結構柱";
            }
        }
    }
}