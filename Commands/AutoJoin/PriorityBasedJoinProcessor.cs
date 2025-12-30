using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AutoJoin
{
    /// <summary>
    /// 基於優先順序的接合處理器
    /// 優先順序：柱 > 梁 > 版 > 牆
    /// </summary>
    public class PriorityBasedJoinProcessor
    {
        private readonly Document _doc;
        private readonly ElementTypeMode _mode;
        private readonly bool _fixWrongOrder;

        public PriorityBasedJoinProcessor(Document doc, ElementTypeMode mode, bool fixWrongOrder)
        {
            _doc = doc;
            _mode = mode;
            _fixWrongOrder = fixWrongOrder;
        }

        /// <summary>
        /// 處理指定元件的接合
        /// </summary>
        public JoinResult ProcessElement(Element element)
        {
            var result = new JoinResult { ProcessedElement = element };

            try
            {
                // 取得元件的幾何輪廓
                var elementSolid = GetElementSolid(element);
                if (elementSolid == null)
                {
                    result.AddMessage($"無法取得元件幾何: {element.Id}");
                    return result;
                }

                // 根據模式找出要接合的目標元件
                var targetElements = FindTargetElements(element, elementSolid);
                result.AddMessage($"找到 {targetElements.Count} 個碰觸的元件");

                // 處理每個目標元件的接合
                foreach (var target in targetElements)
                {
                    ProcessJoin(element, target, result);
                }
            }
            catch (Exception ex)
            {
                result.AddMessage($"處理失敗: {ex.Message}");
                result.Success = false;
            }

            return result;
        }

        /// <summary>
        /// 處理兩個元件之間的接合
        /// </summary>
        private void ProcessJoin(Element source, Element target, JoinResult result)
        {
            try
            {
                // 檢查是否已經接合
                if (JoinGeometryUtils.AreElementsJoined(_doc, source, target))
                {
                    // 已接合，檢查切割順序是否正確
                    if (_fixWrongOrder && !IsCorrectCuttingOrder(source, target))
                    {
                        // 順序錯誤，重新接合
                        JoinGeometryUtils.UnjoinGeometry(_doc, source, target);
                        JoinGeometryUtils.JoinGeometry(_doc, source, target);
                        
                        // 設定正確的切割順序
                        SetCorrectCuttingOrder(source, target);
                        
                        result.JoinedCount++;
                        result.AddMessage($"修正接合順序: {source.Id} <-> {target.Id}");
                    }
                    else
                    {
                        result.AddMessage($"已接合（跳過）: {source.Id} <-> {target.Id}");
                    }
                }
                else
                {
                    // 未接合，建立新接合
                    JoinGeometryUtils.JoinGeometry(_doc, source, target);
                    
                    // 設定正確的切割順序
                    SetCorrectCuttingOrder(source, target);
                    
                    result.JoinedCount++;
                    result.AddMessage($"建立接合: {source.Id} <-> {target.Id}");
                }
            }
            catch (Exception ex)
            {
                result.AddMessage($"接合失敗 {source.Id} <-> {target.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定正確的切割順序
        /// </summary>
        private void SetCorrectCuttingOrder(Element source, Element target)
        {
            try
            {
                var sourcePriority = GetElementPriority(source);
                var targetPriority = GetElementPriority(target);

                // 優先順序高的切割優先順序低的
                bool sourceShouldCutTarget = sourcePriority > targetPriority;
                bool sourceCurrentlyCutsTarget = JoinGeometryUtils.IsCuttingElementInJoin(_doc, source, target);

                // 如果當前切割方向與期望不符，則切換
                if (sourceShouldCutTarget != sourceCurrentlyCutsTarget)
                {
                    JoinGeometryUtils.SwitchJoinOrder(_doc, source, target);
                }
            }
            catch (Exception ex)
            {
                // 切換失敗時記錄錯誤但不中斷流程
                System.Diagnostics.Debug.WriteLine($"切換接合順序失敗: {source.Id} <-> {target.Id}, {ex.Message}");
            }
        }

        /// <summary>
        /// 檢查切割順序是否正確
        /// </summary>
        private bool IsCorrectCuttingOrder(Element source, Element target)
        {
            var sourcePriority = GetElementPriority(source);
            var targetPriority = GetElementPriority(target);

            bool sourceCutsTarget = JoinGeometryUtils.IsCuttingElementInJoin(_doc, source, target);

            // 優先順序高的應該切割優先順序低的
            return (sourcePriority > targetPriority && sourceCutsTarget) ||
                   (sourcePriority < targetPriority && !sourceCutsTarget);
        }

        /// <summary>
        /// 取得元件的優先順序
        /// 柱(4) > 梁(3) > 版(2) > 牆(1)
        /// </summary>
        private int GetElementPriority(Element element)
        {
#if REVIT2022 || REVIT2023
            if (element.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns)
                return 4;
            if (element.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                return 3;
            if (element.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Floors)
                return 2;
            if (element.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Walls)
                return 1;
#else
            if (element.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns)
                return 4;
            if (element.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming)
                return 3;
            if (element.Category?.Id.Value == (long)BuiltInCategory.OST_Floors)
                return 2;
            if (element.Category?.Id.Value == (long)BuiltInCategory.OST_Walls)
                return 1;
#endif
            return 0;
        }

        /// <summary>
        /// 找出要接合的目標元件
        /// </summary>
        private List<Element> FindTargetElements(Element source, Solid sourceSolid)
        {
            var targets = new List<Element>();
            var sourcePriority = GetElementPriority(source);

            // 根據模式決定要搜尋的類別
            var categoriesToSearch = GetCategoriesToSearch();

            foreach (var category in categoriesToSearch)
            {
                var collector = new FilteredElementCollector(_doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType();

                foreach (Element element in collector)
                {
                    // 跳過自己
                    if (element.Id == source.Id) continue;

                    // 檢查優先順序
                    var targetPriority = GetElementPriority(element);
                    if (!ShouldJoin(sourcePriority, targetPriority)) continue;

                    // 檢查是否碰觸
                    if (IsIntersecting(sourceSolid, element))
                    {
                        targets.Add(element);
                    }
                }
            }

            return targets;
        }

        /// <summary>
        /// 根據模式決定要搜尋的類別
        /// </summary>
        private List<BuiltInCategory> GetCategoriesToSearch()
        {
            var categories = new List<BuiltInCategory>();

            switch (_mode)
            {
                case ElementTypeMode.All:
                    // 智慧模式：搜尋所有結構元件
                    categories.Add(BuiltInCategory.OST_StructuralColumns);
                    categories.Add(BuiltInCategory.OST_StructuralFraming);
                    categories.Add(BuiltInCategory.OST_Floors);
                    categories.Add(BuiltInCategory.OST_Walls);
                    break;

                case ElementTypeMode.Column:
                    // 柱切割：梁、版、牆
                    categories.Add(BuiltInCategory.OST_StructuralFraming);
                    categories.Add(BuiltInCategory.OST_Floors);
                    categories.Add(BuiltInCategory.OST_Walls);
                    break;

                case ElementTypeMode.Beam:
                    // 梁切割：版、牆
                    categories.Add(BuiltInCategory.OST_Floors);
                    categories.Add(BuiltInCategory.OST_Walls);
                    break;

                case ElementTypeMode.Floor:
                    // 版切割：牆
                    categories.Add(BuiltInCategory.OST_Walls);
                    break;

                case ElementTypeMode.Wall:
                    // 牆被切割：柱、梁、版
                    categories.Add(BuiltInCategory.OST_StructuralColumns);
                    categories.Add(BuiltInCategory.OST_StructuralFraming);
                    categories.Add(BuiltInCategory.OST_Floors);
                    break;
            }

            return categories;
        }

        /// <summary>
        /// 判斷是否應該接合
        /// </summary>
        private bool ShouldJoin(int sourcePriority, int targetPriority)
        {
            switch (_mode)
            {
                case ElementTypeMode.All:
                    // 智慧模式：高優先順序切割低優先順序
                    return sourcePriority > targetPriority;

                case ElementTypeMode.Column:
                case ElementTypeMode.Beam:
                case ElementTypeMode.Floor:
                    // 高優先順序切割低優先順序
                    return sourcePriority > targetPriority;

                case ElementTypeMode.Wall:
                    // 牆被所有高優先順序切割
                    return targetPriority > sourcePriority;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 檢查兩個元件是否相交（帶接觸面積檢查）
        /// </summary>
        private bool IsIntersecting(Solid solid1, Element element2)
        {
            try
            {
                var solid2 = GetElementSolid(element2);
                if (solid2 == null) return false;

                var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    solid1, solid2, BooleanOperationsType.Intersect);

                if (intersection == null) return false;

                // 計算接觸面積（使用交集體積作為近似）
                double intersectionVolumeCubicFeet = intersection.Volume;

                // 如果交集體積為 0，表示沒有接觸，不跳出（繼續處理）
                if (intersectionVolumeCubicFeet <= 0.0001)
                    return false;

                // 轉換為立方公尺（1 ft³ = 0.0283168 m³）
                double intersectionVolumeCubicMeters = intersectionVolumeCubicFeet * 0.0283168;

                // 計算接觸面積的近似值（假設接觸深度為 0.1m）
                double estimatedContactAreaSqMeters = intersectionVolumeCubicMeters / 0.1;

                // 接觸面積範圍：> 0 且 < 0.5 m²
                // 如果接觸面積太大（>= 0.5 m²），可能是完全重疊，跳過
                if (estimatedContactAreaSqMeters >= 0.5)
                {
                    System.Diagnostics.Debug.WriteLine($"接觸面積過大，跳過: {estimatedContactAreaSqMeters:F4} m²");
                    return false;
                }

                // 接觸面積在合理範圍內
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"相交檢查失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 取得元件的實體幾何
        /// </summary>
        private Solid GetElementSolid(Element element)
        {
            var options = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            var geometry = element.get_Geometry(options);
            if (geometry == null) return null;

            foreach (GeometryObject geomObj in geometry)
            {
                if (geomObj is Solid solid && solid.Volume > 0.0001)
                    return solid;

                if (geomObj is GeometryInstance instance)
                {
                    var instGeometry = instance.GetInstanceGeometry();
                    foreach (GeometryObject instObj in instGeometry)
                    {
                        if (instObj is Solid instSolid && instSolid.Volume > 0.0001)
                            return instSolid;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 接合處理結果
    /// </summary>
    public class JoinResult
    {
        public Element ProcessedElement { get; set; }
        public int JoinedCount { get; set; }
        public bool Success { get; set; } = true;
        public List<string> Messages { get; private set; } = new List<string>();

        public void AddMessage(string message)
        {
            Messages.Add(message);
        }
    }
}

