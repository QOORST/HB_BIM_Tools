using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    internal static class DetourRouteBuilder
    {
        internal static DetourPlan Build(Line line, XYZ first, XYZ last, AvoidOptions options,
            DirectionMode direction, double minimumSegment)
        {
            if (direction != DirectionMode.Up && direction != DirectionMode.Down)
                throw new InvalidOperationException("請選擇上翻、下翻或自動比較上下方向。");
            XYZ start = line.GetEndPoint(0), end = line.GetEndPoint(1), axis = line.Direction;
            double a = (first - start).DotProduct(axis), b = (last - start).DotProduct(axis);
            if (a > b) { double temporary = a; a = b; b = temporary; }
            double offset = options.ExtraOffsetMm / 304.8;
            double run = AvoidRoutePolicy.Validate(line.Length, a, b, axis.Z, offset, options.BendAngle, minimumSegment);
            XYZ lift = XYZ.BasisZ * (direction == DirectionMode.Up ? offset : -offset);
            // Same six-point geometry as the original command: the middle section is parallel
            // to the source, retaining its slope; the two external endpoints do not move.
            return new DetourPlan { UsedDirection = direction, OffsetFt = offset, IsValid = true,
                Path = new List<XYZ> { start, start + axis * a, start + axis * (a + run) + lift,
                    start + axis * (b - run) + lift, start + axis * b, end } };
        }
    }
}
