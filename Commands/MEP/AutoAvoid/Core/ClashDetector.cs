using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;


namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    public class ClashHit
    {
        public Element Obstacle { get; set; }
        public BoundingBoxXYZ ObstacleBB { get; set; }
        public double Distance { get; set; }
        public XYZ ClashPoint { get; set; }
    }

    public static class ClashDetector
    {
        /// <summary>
        /// 獲取元素的近似半徑（用於碰撞距離檢查）
        /// </summary>
        private static double GetApproxRadiusFt(Element e)
        {
            try
            {
                if (e is Pipe p)
                {
                    var dia = p.Diameter;
                    if (dia > 0) return dia * 0.5;
                }
                else if (e is Duct d)
                {
                    var dia = d.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                    if (dia != null && dia.HasValue) return dia.AsDouble() * 0.5;
                    var w = d.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                    var h = d.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                    if (w != null && h != null && w.HasValue && h.HasValue)
                    {
                        return 0.5 * Math.Min(w.AsDouble(), h.AsDouble());
                    }
                }
                else if (e is Conduit c)
                {
                    var dia = c.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                    if (dia != null && dia.HasValue) return dia.AsDouble() * 0.5;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"無法獲取元素 {e.Id} 的半徑: {ex.Message}");
            }
            return 0.0;
        }

        /// <summary>
        /// 計算兩條線段之間的最短距離
        /// </summary>
        private static double SegmentSegmentMinDistance(XYZ p1, XYZ q1, XYZ p2, XYZ q2)
        {
            return GeometryUtils.SegmentSegmentMinDistance(p1, q1, p2, q2);
        }
        /// <summary>
        /// 收集障礙物元素
        /// </summary>
        public static List<Element> CollectObstacles(Document doc, View view, AvoidOptions opt, List<Element> excluding)
        {
            Logger.Info($"開始收集障礙物，排除 {excluding.Count} 個目標元素");
            var idsExclude = new HashSet<long>(excluding.Select(x => x.Id.Value));

            var result = new List<Element>();
            
            try
            {
                if (opt.IncludeWalls)
                {
                    var walls = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(Wall))
                        .WhereElementIsNotElementType()
                        .ToElements();
                    result.AddRange(walls);
                    Logger.Debug($"收集到 {walls.Count} 個牆");
                }
                
                if (opt.IncludeFloors)
                {
                    var floors = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(Floor))
                        .WhereElementIsNotElementType()
                        .ToElements();
                    result.AddRange(floors);
                    Logger.Debug($"收集到 {floors.Count} 個樓板");
                }
                
                if (opt.IncludeFraming)
                {
                    var framing = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    result.AddRange(framing);
                    Logger.Debug($"收集到 {framing.Count} 個結構梁");
                }
                
                if (opt.IncludeMEP)
                {
                    var mepCats = new BuiltInCategory[] {
                        BuiltInCategory.OST_PipeCurves, 
                        BuiltInCategory.OST_DuctCurves, 
                        BuiltInCategory.OST_Conduit
                    };
                    foreach (var cat in mepCats)
                    {
                        var meps = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(cat)
                            .WhereElementIsNotElementType()
                            .ToElements();
                        result.AddRange(meps);
                    }
                }

                if (opt.IncludeFittings)
                {
                    var fittingCats = new BuiltInCategory[] {
                        BuiltInCategory.OST_PipeFitting,
                        BuiltInCategory.OST_DuctFitting,
                        BuiltInCategory.OST_ConduitFitting
                    };
                    foreach (var cat in fittingCats)
                    {
                        var fittings = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(cat)
                            .WhereElementIsNotElementType()
                            .ToElements();
                        result.AddRange(fittings);
                    }
                }

                // 移除目標元素本身
                result = result.Where(e => !idsExclude.Contains(e.Id.Value)).ToList();
                Logger.Info($"總共收集到 {result.Count} 個障礙物");
            }
            catch (Exception ex)
            {
                Logger.Error($"收集障礙物時發生錯誤", ex);
            }

            return result;
        }

        /// <summary>
        /// 檢測目標元素沿路徑的第一個衝突
        /// </summary>
        public static ClashHit FirstClashAlong(Document doc, Element target, List<Element> obstacles, AvoidOptions opt)
        {
            var lc = target.Location as LocationCurve;
            if (lc == null) return null;
            var line = lc.Curve as Line;
            if (line == null) return null;

            double clearFt = opt.ClearanceMm * GeometryUtils.MM_TO_FT;
            ClashHit closestHit = null;
            double minDistance = double.MaxValue;

            foreach (var obs in obstacles)
            {
                try
                {
                    var bb = obs.get_BoundingBox(null);
                    if (bb != null)
                    {
                        var bbx = GeometryUtils.Expand(bb, clearFt);
                        if (GeometryUtils.LineIntersectsAABB(line, bbx))
                        {
                            double dist = GeometryUtils.PointToLineDistance(
                                (bb.Min + bb.Max) * 0.5, line);
                            
                            if (dist < minDistance)
                            {
                                minDistance = dist;
                                closestHit = new ClashHit 
                                { 
                                    Obstacle = obs, 
                                    ObstacleBB = bbx,
                                    Distance = dist,
                                    ClashPoint = (bb.Min + bb.Max) * 0.5
                                };
                            }
                        }
                    }

                    // MEP 曲線精確檢測
                    if (obs.Location is LocationCurve loc2 && loc2.Curve is Line line2)
                    {
                        double r1 = GetApproxRadiusFt(target);
                        double r2 = GetApproxRadiusFt(obs);
                        double limit = r1 + r2 + clearFt;

                        double d = SegmentSegmentMinDistance(
                            line.GetEndPoint(0), line.GetEndPoint(1),
                            line2.GetEndPoint(0), line2.GetEndPoint(1));
                        
                        if (d <= limit && d < minDistance)
                        {
                            minDistance = d;
                            var bb2 = obs.get_BoundingBox(null);
                            closestHit = new ClashHit 
                            { 
                                Obstacle = obs, 
                                ObstacleBB = bb2,
                                Distance = d,
                                ClashPoint = (line2.GetEndPoint(0) + line2.GetEndPoint(1)) * 0.5
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"檢測元素 {obs.Id} 時發生錯誤: {ex.Message}");
                }
            }

            if (closestHit != null)
            {
                Logger.Debug($"檢測到衝突: 障礙物 {closestHit.Obstacle.Id}, 距離 {minDistance * GeometryUtils.FT_TO_MM:F2}mm");
            }

            return closestHit;
        }

        /// <summary>
        /// 驗證路徑是否與障礙物衝突
        /// </summary>
        public static bool ValidatePath(Document doc, List<XYZ> path, List<Element> obstacles, AvoidOptions opt)
        {
            if (path == null || path.Count < 2) return false;

            double clearFt = opt.ClearanceMm * GeometryUtils.MM_TO_FT;

            for (int i = 0; i < path.Count - 1; i++)
            {
                var segment = Line.CreateBound(path[i], path[i + 1]);
                
                foreach (var obs in obstacles)
                {
                    try
                    {
                        var bb = obs.get_BoundingBox(null);
                        if (bb != null)
                        {
                            var bbx = GeometryUtils.Expand(bb, clearFt);
                            if (GeometryUtils.LineIntersectsAABB(segment, bbx))
                            {
                                Logger.Warning($"路徑段 {i} 與障礙物 {obs.Id} 發生衝突");
                                return false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"驗證路徑時發生錯誤: {ex.Message}");
                    }
                }
            }

            return true;
        }
    }
}