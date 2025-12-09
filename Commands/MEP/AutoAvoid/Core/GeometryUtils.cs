using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    public static class GeometryUtils
    {
        public const double MM_TO_FT = 1.0 / 304.8;
        public const double FT_TO_MM = 304.8;
        private const double TOLERANCE = 1e-9;

        public static bool LineIntersectsAABB(Line line, BoundingBoxXYZ bb)
        {
            if (line == null || bb == null) return false;

            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            XYZ d = p1 - p0;

            double tmin = 0.0, tmax = 1.0;

            if (!AxisIntersect(p0.X, d.X, bb.Min.X, bb.Max.X, ref tmin, ref tmax)) return false;
            if (!AxisIntersect(p0.Y, d.Y, bb.Min.Y, bb.Max.Y, ref tmin, ref tmax)) return false;
            if (!AxisIntersect(p0.Z, d.Z, bb.Min.Z, bb.Max.Z, ref tmin, ref tmax)) return false;

            return tmax >= Math.Max(0.0, tmin);
        }

        private static bool AxisIntersect(double p, double d, double min, double max, ref double tmin, ref double tmax)
        {
            if (Math.Abs(d) < TOLERANCE)
            {
                if (p < min || p > max) return false;
                return true;
            }
            double ood = 1.0 / d;
            double t1 = (min - p) * ood;
            double t2 = (max - p) * ood;
            if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; }
            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            if (tmin > tmax) return false;
            return true;
        }

        public static BoundingBoxXYZ Expand(BoundingBoxXYZ bb, double offsetFt)
        {
            if (bb == null) return null;
            var n = new BoundingBoxXYZ();
            n.Min = new XYZ(bb.Min.X - offsetFt, bb.Min.Y - offsetFt, bb.Min.Z - offsetFt);
            n.Max = new XYZ(bb.Max.X + offsetFt, bb.Max.Y + offsetFt, bb.Max.Z + offsetFt);
            return n;
        }

        public static XYZ PerpHorizontal(XYZ dir, bool left) 
        {
            var v = new XYZ(dir.X, dir.Y, 0.0).Normalize();
            if (v.IsZeroLength()) v = XYZ.BasisX;
            var n = new XYZ(-v.Y, v.X, 0.0);
            return left ? n : n.Negate();
        }

        public static bool IsNearlyHorizontal(XYZ dir, double tol = 1e-3)
        {
            if (dir.IsZeroLength()) return true;
            XYZ u = dir.Normalize();
            return Math.Abs(u.Z) < 0.2;
        }

        public static bool IsZeroLength(this XYZ v) => v.GetLength() < TOLERANCE;

        /// <summary>
        /// 計算點到線段的最短距離
        /// </summary>
        public static double PointToLineDistance(XYZ point, Line line)
        {
            if (point == null || line == null) return double.MaxValue;

            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            XYZ dir = p1 - p0;
            double len = dir.GetLength();

            if (len < TOLERANCE) return point.DistanceTo(p0);

            double t = Math.Max(0, Math.Min(1, (point - p0).DotProduct(dir) / (len * len)));
            XYZ closest = p0 + t * dir;
            return point.DistanceTo(closest);
        }

        /// <summary>
        /// 計算兩條線段的最短距離
        /// </summary>
        public static double SegmentSegmentMinDistance(XYZ p1, XYZ q1, XYZ p2, XYZ q2)
        {
            XYZ d1 = q1 - p1;
            XYZ d2 = q2 - p2;
            XYZ r = p1 - p2;
            double a = d1.DotProduct(d1);
            double e = d2.DotProduct(d2);
            double f = d2.DotProduct(r);

            double s, t;
            if (a <= TOLERANCE && e <= TOLERANCE)
            {
                return r.GetLength();
            }
            if (a <= TOLERANCE)
            {
                s = 0.0;
                t = Math.Max(0.0, Math.Min(1.0, f / e));
            }
            else
            {
                double c = d1.DotProduct(r);
                if (e <= TOLERANCE)
                {
                    t = 0.0;
                    s = Math.Max(0.0, Math.Min(1.0, -c / a));
                }
                else
                {
                    double b = d1.DotProduct(d2);
                    double denom = a * e - b * b;
                    if (Math.Abs(denom) > TOLERANCE)
                        s = (b * f - c * e) / denom;
                    else
                        s = 0.0;
                    s = Math.Max(0.0, Math.Min(1.0, s));
                    t = (b * s + f) / e;
                    if (t < 0.0)
                    {
                        t = 0.0;
                        s = Math.Max(0.0, Math.Min(1.0, -c / a));
                    }
                    else if (t > 1.0)
                    {
                        t = 1.0;
                        s = Math.Max(0.0, Math.Min(1.0, (b - c) / a));
                    }
                }
            }

            XYZ c1 = p1 + s * d1;
            XYZ c2 = p2 + t * d2;
            return (c1 - c2).GetLength();
        }

        /// <summary>
        /// 計算向量在平面上的投影
        /// </summary>
        public static XYZ ProjectOnPlane(XYZ vector, XYZ planeNormal)
        {
            if (vector == null || planeNormal == null) return XYZ.Zero;
            XYZ n = planeNormal.Normalize();
            return vector - vector.DotProduct(n) * n;
        }

        /// <summary>
        /// 判斷點是否在 AABB 內
        /// </summary>
        public static bool IsPointInAABB(XYZ point, BoundingBoxXYZ bb)
        {
            if (point == null || bb == null) return false;
            return point.X >= bb.Min.X && point.X <= bb.Max.X &&
                   point.Y >= bb.Min.Y && point.Y <= bb.Max.Y &&
                   point.Z >= bb.Min.Z && point.Z <= bb.Max.Z;
        }
    }
}