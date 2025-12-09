using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    public class DetourPlan
    {
        public IList<XYZ> Path { get; set; }
        public DirectionMode UsedDirection { get; set; }
        public double OffsetFt { get; set; }
        public bool IsValid { get; set; } = true;
    }

    public static class RoutingPlanner
    {
        /// <summary>
        /// 規劃避讓路徑
        /// </summary>
        public static DetourPlan PlanDetour(Element target, ClashHit hit, AvoidOptions opt)
        {
            var lc = target.Location as LocationCurve;
            if (lc == null) return null;
            var line = lc.Curve as Line;
            if (line == null) return null;

            XYZ p0 = line.GetEndPoint(0);
            XYZ p3 = line.GetEndPoint(1);
            XYZ dir = (p3 - p0).Normalize();

            // 如果用戶明確選擇垂直翻彎，優先使用該方法
            if (opt.Direction == DirectionMode.VerticalFlip)
            {
                Logger.Debug("使用垂直翻彎模式");
                var flipPlan = PlanVerticalFlip(target, hit, opt);
                if (flipPlan != null && flipPlan.IsValid)
                {
                    return flipPlan;
                }
                Logger.Debug("垂直翻彎失敗，嘗試其他方向");
            }

            XYZ c = hit.ClashPoint ?? (hit.ObstacleBB.Min + hit.ObstacleBB.Max) * 0.5;

            double clearFt = opt.ClearanceMm * GeometryUtils.MM_TO_FT;
            double extraFt = opt.ExtraOffsetMm * GeometryUtils.MM_TO_FT;

            var modes = BuildDirectionOrder(opt, dir, c, (p0 + p3) * 0.5);
            
            foreach (var m in modes)
            {
                // 如果在 Auto 模式下遇到 VerticalFlip，嘗試使用
                if (m == DirectionMode.VerticalFlip)
                {
                    var flipPlan = PlanVerticalFlip(target, hit, opt);
                    if (flipPlan != null && flipPlan.IsValid)
                    {
                        return flipPlan;
                    }
                    continue;
                }

                XYZ offDir = DirectionVector(m, dir);
                if (offDir.IsZeroLength()) continue;

                double halfThick = 0.0;
                if (hit.ObstacleBB != null)
                {
                    XYZ ext = (hit.ObstacleBB.Max - hit.ObstacleBB.Min) * 0.5;
                    halfThick = Math.Max(ext.X, Math.Max(ext.Y, ext.Z));
                }

                double offset = halfThick + clearFt + extraFt;

                double t = (c - p0).DotProduct(dir);
                XYZ midOnLine = p0 + t * dir;

                XYZ p1 = midOnLine + offDir * offset;
                
                double tEnd = (p3 - p0).DotProduct(dir);
                if (tEnd < t + 2.0) tEnd = t + 2.0;
                XYZ endOnLine = p0 + tEnd * dir;
                XYZ p2 = endOnLine + offDir * offset;

                var path = new List<XYZ> { p0, p1, p2, p3 };

                Logger.Debug($"生成路徑: 方向={m}, 偏移={offset * GeometryUtils.FT_TO_MM:F2}mm");

                return new DetourPlan 
                { 
                    Path = path, 
                    UsedDirection = m, 
                    OffsetFt = offset,
                    IsValid = true
                };
            }

            Logger.Warning("無法生成有效的避讓路徑");
            return null;
        }

        /// <summary>
        /// 根據配置和障礙物位置智慧選擇方向優先順序
        /// </summary>
        private static List<DirectionMode> BuildDirectionOrder(AvoidOptions opt, XYZ routeDir, XYZ obstacleCenter, XYZ routeCenter)
        {
            if (opt.Direction != DirectionMode.Auto) 
                return new List<DirectionMode> { opt.Direction };

            var list = new List<DirectionMode>();

            // 計算障礙物相對於路徑的位置
            XYZ toObstacle = (obstacleCenter - routeCenter);
            
            if (GeometryUtils.IsNearlyHorizontal(routeDir))
            {
                // 水平路徑：優先垂直翻彎，其次往上或往下
                list.Add(DirectionMode.VerticalFlip);  // 優先嘗試垂直翻彎
                
                if (toObstacle.Z > 0)
                {
                    list.Add(DirectionMode.Down);
                    list.Add(DirectionMode.Up);
                }
                else
                {
                    list.Add(DirectionMode.Up);
                    list.Add(DirectionMode.Down);
                }
                
                // 次要選擇：左右方向
                XYZ perp = GeometryUtils.PerpHorizontal(routeDir, true);
                if (toObstacle.DotProduct(perp) > 0)
                {
                    list.Add(DirectionMode.Right);
                    list.Add(DirectionMode.Left);
                }
                else
                {
                    list.Add(DirectionMode.Left);
                    list.Add(DirectionMode.Right);
                }
            }
            else
            {
                // 垂直或傾斜路徑：優先左右
                XYZ perp = GeometryUtils.PerpHorizontal(routeDir, true);
                if (toObstacle.DotProduct(perp) > 0)
                {
                    list.Add(DirectionMode.Right);
                    list.Add(DirectionMode.Left);
                }
                else
                {
                    list.Add(DirectionMode.Left);
                    list.Add(DirectionMode.Right);
                }
                
                list.Add(DirectionMode.Up);
                list.Add(DirectionMode.Down);
            }

            Logger.Debug($"智慧方向順序: {string.Join(", ", list)}");
            return list;
        }

        private static XYZ DirectionVector(DirectionMode mode, XYZ routeDir)
        {
            switch (mode)
            {
                case DirectionMode.Up: 
                    return XYZ.BasisZ;
                case DirectionMode.Down: 
                    return -XYZ.BasisZ;
                case DirectionMode.Left: 
                    return GeometryUtils.PerpHorizontal(routeDir, left:true);
                case DirectionMode.Right: 
                    return GeometryUtils.PerpHorizontal(routeDir, left:false);
                case DirectionMode.VerticalFlip:
                    // 垂直翻彎：根據障礙物位置決定向上或向下
                    return XYZ.BasisZ;  // 預設向上，實際會在 PlanDetour 中調整
                default: 
                    return XYZ.Zero;
            }
        }

        /// <summary>
        /// 計算垂直翻彎路徑（完全參考最新的簡化邏輯）
        /// </summary>
        private static DetourPlan PlanVerticalFlip(Element target, ClashHit hit, AvoidOptions opt)
        {
            var lc = target.Location as LocationCurve;
            if (lc == null) return null;
            var line = lc.Curve as Line;
            if (line == null) return null;

            XYZ startpoint = line.GetEndPoint(0);  // 管線起點
            XYZ endpoint = line.GetEndPoint(1);    // 管線終點
            XYZ dir = (endpoint - startpoint).Normalize();

            // 只對水平管線使用垂直翻彎
            if (!GeometryUtils.IsNearlyHorizontal(dir))
            {
                Logger.Debug("非水平管線，不適用垂直翻彎");
                return null;
            }

            XYZ c = hit.ClashPoint ?? (hit.ObstacleBB.Min + hit.ObstacleBB.Max) * 0.5;
            double clearFt = opt.ClearanceMm * GeometryUtils.MM_TO_FT;
            double extraFt = opt.ExtraOffsetMm * GeometryUtils.MM_TO_FT;

            // 計算障礙物的高度範圍
            double obsTop = hit.ObstacleBB.Max.Z;
            double obsBottom = hit.ObstacleBB.Min.Z;
            double pipeZ = startpoint.Z;

            // 決定翻彎方向：如果管線在障礙物下方，往上翻
            bool flipUp = pipeZ < (obsTop + obsBottom) / 2;
            double offsetvalue = flipUp ? 
                (obsTop - pipeZ + clearFt + extraFt) : 
                (pipeZ - obsBottom + clearFt + extraFt);

            Logger.Debug($"翻彎高度: {offsetvalue * GeometryUtils.FT_TO_MM:F0}mm, 方向: {(flipUp ? "向上" : "向下")}");

            // 投影障礙物中心到管線上
            XYZ point1project = line.Project(c).XYZPoint;

            // 根據角度計算水平偏移距離
            double angleRad = opt.BendAngle * Math.PI / 180.0;
            double horizontalOffset = offsetvalue / Math.Tan(angleRad);
            
            Logger.Debug($"角度={opt.BendAngle}°, 水平偏移={horizontalOffset * GeometryUtils.FT_TO_MM:F0}mm");

            // 計算翻彎前後的點（障礙物兩側，對稱分布）
            XYZ pointBefore = point1project - dir * horizontalOffset;  // 翻彎前
            XYZ pointAfter = point1project + dir * horizontalOffset;   // 翻彎後
            
            // 確保點在管線範圍內
            double tBefore = (pointBefore - startpoint).DotProduct(dir);
            double tAfter = (pointAfter - startpoint).DotProduct(dir);
            double lineLength = (endpoint - startpoint).GetLength();
            
            if (tBefore < 0.5 || tAfter > lineLength - 0.5)
            {
                Logger.Debug($"管線太短，無法容納翻彎: 需要 {horizontalOffset * 2 * GeometryUtils.FT_TO_MM:F0}mm");
                return null;
            }

            // 計算翻彎後的高度點
            double zOffset = flipUp ? offsetvalue : -offsetvalue;
            XYZ pointBeforeUp = pointBefore + new XYZ(0, 0, zOffset);
            XYZ pointAfterUp = pointAfter + new XYZ(0, 0, zOffset);
            
            // 建立路徑：startpoint → pointBefore → pointBeforeUp → pointAfterUp → pointAfter → endpoint
            List<XYZ> path = new List<XYZ>
            {
                startpoint,      // A: 原起點
                pointBefore,     // B: 翻彎起點（水平）
                pointBeforeUp,   // C: 翻彎後高點（斜向上）
                pointAfterUp,    // D: 高處水平段終點
                pointAfter,      // E: 翻彎終點（斜向下）
                endpoint         // F: 原終點
            };

            Logger.Debug($"垂直翻彎路徑點數: {path.Count}");
            for (int i = 0; i < path.Count; i++)
            {
                Logger.Debug($"  點{i}: ({path[i].X * GeometryUtils.FT_TO_MM:F0}, {path[i].Y * GeometryUtils.FT_TO_MM:F0}, {path[i].Z * GeometryUtils.FT_TO_MM:F0})");
            }

            return new DetourPlan 
            { 
                Path = path, 
                UsedDirection = DirectionMode.VerticalFlip, 
                OffsetFt = offsetvalue,
                IsValid = true
            };
        }
    }
}