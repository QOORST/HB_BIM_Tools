using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin
{
    /// <summary>
    /// 結構接合的共用輔助工具類
    /// </summary>
    public static class JoinGeometryHelper
    {
        #region 元素過濾與驗證

        /// <summary>
        /// 檢查元素是否可以進行接合操作
        /// </summary>
        public static bool IsJoinable(Element e)
        {
            if (e == null) return false;
            
            var doc = e.Document;
            if (doc == null || doc.IsFamilyDocument) return false;
            
            // 排除群組中的元素
            if (e.GroupId != ElementId.InvalidElementId) return false;
            
            // 排除釘選的元素
            if (e.Pinned) return false;
            
            // 排除連結檔案
            if (e is RevitLinkInstance) return false;
            
            // 必須有類別
            if (e.Category == null) return false;

            // 檢查設計選項：只處理主要選項中的元素
            var p = e.get_Parameter(BuiltInParameter.DESIGN_OPTION_ID);
            if (p != null && p.StorageType == StorageType.ElementId)
            {
                var optId = p.AsElementId();
                if (optId != ElementId.InvalidElementId)
                {
                    var opt = doc.GetElement(optId) as DesignOption;
                    if (opt != null && !opt.IsPrimary) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 取得元素的膨脹外框
        /// </summary>
        public static Outline GetOutline(Element e, double inflateFeet)
        {
            if (e == null) return null;
            
            var bb = e.get_BoundingBox(null);
            if (bb == null) return null;
            
            var min = new XYZ(bb.Min.X - inflateFeet, bb.Min.Y - inflateFeet, bb.Min.Z - inflateFeet);
            var max = new XYZ(bb.Max.X + inflateFeet, bb.Max.Y + inflateFeet, bb.Max.Z + inflateFeet);
            
            return new Outline(min, max);
        }

        /// <summary>
        /// 檢查兩個元素是否實際相交（精確檢測）
        /// </summary>
        public static bool AreElementsIntersecting(Document doc, Element a, Element b)
        {
            if (a == null || b == null || doc == null) return false;

            try
            {
                var filter = new ElementIntersectsElementFilter(a);
                var result = new FilteredElementCollector(doc, new List<ElementId> { b.Id })
                    .WherePasses(filter)
                    .ToElementIds();

                return result.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 計算兩個元素之間的最短距離（英尺）
        /// </summary>
        public static double GetMinimumDistance(Element a, Element b)
        {
            if (a == null || b == null) return double.MaxValue;

            try
            {
                // 使用 BoundingBox 估算距離（簡化版本）
                var bbA = a.get_BoundingBox(null);
                var bbB = b.get_BoundingBox(null);

                if (bbA == null || bbB == null) return double.MaxValue;

                return CalculateBoundingBoxDistance(bbA, bbB);
            }
            catch
            {
                return double.MaxValue;
            }
        }

        /// <summary>
        /// 檢查兩個元素是否接近但未相交（在容差範圍內）
        /// </summary>
        public static bool AreElementsNearby(Document doc, Element a, Element b, double toleranceFeet, out double distanceFeet)
        {
            distanceFeet = double.MaxValue;

            if (a == null || b == null || doc == null) return false;

            // 先檢查是否相交
            if (AreElementsIntersecting(doc, a, b))
            {
                distanceFeet = 0.0;
                return false; // 已經相交，不算「接近但未相交」
            }

            // 計算距離
            distanceFeet = GetMinimumDistance(a, b);

            // 檢查是否在容差範圍內
            return distanceFeet > 0 && distanceFeet <= toleranceFeet;
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 從幾何元素中取得最大的實體
        /// </summary>
        private static Solid GetLargestSolid(GeometryElement geomElem)
        {
            Solid largestSolid = null;
            double maxVolume = 0;

            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid && solid.Volume > maxVolume)
                {
                    maxVolume = solid.Volume;
                    largestSolid = solid;
                }
                else if (geomObj is GeometryInstance geomInst)
                {
                    var instGeom = geomInst.GetInstanceGeometry();
                    var instSolid = GetLargestSolid(instGeom);
                    if (instSolid != null && instSolid.Volume > maxVolume)
                    {
                        maxVolume = instSolid.Volume;
                        largestSolid = instSolid;
                    }
                }
            }

            return largestSolid;
        }

        /// <summary>
        /// 計算兩個 BoundingBox 之間的最短距離
        /// </summary>
        private static double CalculateBoundingBoxDistance(BoundingBoxXYZ bbA, BoundingBoxXYZ bbB)
        {
            // 計算每個軸向的距離
            double dx = Math.Max(0, Math.Max(bbA.Min.X - bbB.Max.X, bbB.Min.X - bbA.Max.X));
            double dy = Math.Max(0, Math.Max(bbA.Min.Y - bbB.Max.Y, bbB.Min.Y - bbA.Max.Y));
            double dz = Math.Max(0, Math.Max(bbA.Min.Z - bbB.Max.Z, bbB.Min.Z - bbA.Max.Z));

            // 返回三維空間中的距離
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        #endregion

        #region 接合操作

        /// <summary>
        /// 嘗試接合兩個元素
        /// </summary>
        public static bool TryJoin(Document doc, Element a, Element b)
        {
            if (doc == null || a == null || b == null) return false;
            
            try
            {
                // 如果已經接合，直接返回成功
                if (JoinGeometryUtils.AreElementsJoined(doc, a, b)) 
                    return true;
                
                JoinGeometryUtils.JoinGeometry(doc, a, b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 安全地切換接合順序
        /// </summary>
        public static bool SafeSwitch(Document doc, Element a, Element b)
        {
            if (doc == null || a == null || b == null) return false;
            
            try
            {
                JoinGeometryUtils.SwitchJoinOrder(doc, a, b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 嘗試切換接合順序，失敗時重試
        /// </summary>
        public static bool TrySwitchWithRetry(Document doc, Element a, Element b)
        {
            if (doc == null || a == null || b == null) return false;
            
            // 第一次嘗試
            if (SafeSwitch(doc, a, b)) return true;
            
            // 重試：先解除接合，重新接合，再切換
            try
            {
                JoinGeometryUtils.UnjoinGeometry(doc, a, b);
                JoinGeometryUtils.JoinGeometry(doc, a, b);
                JoinGeometryUtils.SwitchJoinOrder(doc, a, b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解除元素與所有其他元素的接合
        /// </summary>
        public static void UnjoinAllWith(Document doc, Element e)
        {
            if (doc == null || e == null) return;
            
            try
            {
                var joined = JoinGeometryUtils.GetJoinedElements(doc, e);
                foreach (var id in joined)
                {
                    try
                    {
                        var other = doc.GetElement(id);
                        if (other != null)
                            JoinGeometryUtils.UnjoinGeometry(doc, e, other);
                    }
                    catch { /* 忽略個別解除失敗 */ }
                }
            }
            catch { /* 忽略整體失敗 */ }
        }

        /// <summary>
        /// 檢查當前切割方向是否符合預期
        /// </summary>
        public static bool IsCuttingOrderCorrect(Document doc, Element a, Element b, bool wantACutB)
        {
            if (doc == null || a == null || b == null) return false;
            if (!JoinGeometryUtils.AreElementsJoined(doc, a, b)) return false;
            
            try
            {
                bool currentACutsB = JoinGeometryUtils.IsCuttingElementInJoin(doc, a, b);
                return currentACutsB == wantACutB;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 檢查是否需要切換接合順序
        /// </summary>
        public static bool NeedSwitch(Document doc, Element a, Element b, bool wantACutB)
        {
            if (!JoinGeometryUtils.AreElementsJoined(doc, a, b)) return false;
            return !IsCuttingOrderCorrect(doc, a, b, wantACutB);
        }

        #endregion

        #region 進階接合操作

        /// <summary>
        /// 嘗試接合並設定切割順序（帶重試機制）
        /// </summary>
        public static bool TryJoinThenSwitch(Document doc, Element a, Element b, bool needACutB, out string errorMessage)
        {
            errorMessage = null;
            
            if (doc == null || a == null || b == null)
            {
                errorMessage = "Invalid parameters";
                return false;
            }

            try
            {
                // 步驟 1: 嘗試接合
                if (!JoinGeometryUtils.AreElementsJoined(doc, a, b))
                {
                    try
                    {
                        JoinGeometryUtils.JoinGeometry(doc, a, b);
                    }
                    catch (Exception ex)
                    {
                        errorMessage = $"Join failed: {ex.Message}";
                        return false;
                    }
                }

                // 步驟 2: 檢查是否需要切換順序
                if (needACutB && NeedSwitch(doc, a, b, true))
                {
                    // 嘗試切換
                    if (!TrySwitchWithRetry(doc, a, b))
                    {
                        errorMessage = "Switch order failed";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 強制接合（解除所有現有接合後重試）
        /// </summary>
        public static bool ForceJoin(Document doc, Element a, Element b, bool needACutB, out string errorMessage)
        {
            errorMessage = null;
            
            try
            {
                // 先嘗試正常接合
                if (TryJoinThenSwitch(doc, a, b, needACutB, out errorMessage))
                    return true;

                // 失敗則解除所有接合後重試
                UnjoinAllWith(doc, a);
                UnjoinAllWith(doc, b);

                return TryJoinThenSwitch(doc, a, b, needACutB, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        #endregion

        #region 優先級系統

        /// <summary>
        /// 取得類別的切割優先級（數字越小優先級越高）
        /// </summary>
        public static int GetCategoryPriority(BuiltInCategory category)
        {
            switch (category)
            {
                case BuiltInCategory.OST_StructuralColumns:
                    return 1;
                case BuiltInCategory.OST_StructuralFraming:
                    return 2;
                case BuiltInCategory.OST_Floors:
                    return 3;
                case BuiltInCategory.OST_Walls:
                    return 4;
                case BuiltInCategory.OST_StructuralFoundation:
                    return 5;
                default:
                    return 99;
            }
        }

        /// <summary>
        /// 判斷元素 A 是否應該切割元素 B（基於優先級）
        /// </summary>
        public static bool ShouldACutB(Element a, Element b)
        {
            if (a == null || b == null) return false;
            if (a.Category == null || b.Category == null) return false;

            var categoryA = (BuiltInCategory)a.Category.Id.Value;
            var categoryB = (BuiltInCategory)b.Category.Id.Value;
            
            int priorityA = GetCategoryPriority(categoryA);
            int priorityB = GetCategoryPriority(categoryB);

            // 同類別不調整
            if (priorityA == priorityB) return false;
            
            // 優先級高者切割優先級低者
            return priorityA < priorityB;
        }

        #endregion
    }
}

