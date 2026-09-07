using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public enum PipeCenterAlignFittingMode
    {
        AskEveryTime,
        AlwaysCreate,
        AlignOnly
    }

    public enum PipeCenterAlignConnectionMode
    {
        Auto,
        Tee,
        Takeoff,
        Vertical45
    }

    public enum PipeCenterAlignGeometryMode
    {
        WholePipeElevation,
        EndpointOnly
    }

    /// <summary>
    /// Aligns one selected branch pipe endpoint to the centerline of a selected horizontal main pipe,
    /// with an optional step to split the main pipe and create a tee fitting.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdPipeCenterAlign : IExternalCommand
    {
        private const double DirectionTolerance = 1e-6;
        private const double ParallelTolerance = 1e-4;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                if (!TryGetTargetPipes(uiDoc, doc, out Pipe mainPipe, out Pipe branchPipe))
                {
                    return Result.Cancelled;
                }

                Line mainLine = GetPipeLine(mainPipe);
                Line branchLine = GetPipeLine(branchPipe);

                if (mainLine == null || branchLine == null)
                {
                    TaskDialog.Show("支管中心對齊", "目前僅支援直線 Pipe。請確認幹管與支管都是直線管段。");
                    return Result.Cancelled;
                }

                if (!IsPlanMainCandidate(mainLine))
                {
                    TaskDialog.Show("支管中心對齊", "幹管必須是平面上有長度的直管，不支援垂直立管作為幹管。請先選取水平或帶坡度的幹管。");
                    return Result.Cancelled;
                }

                if (AreParallelInPlan(mainLine, branchLine))
                {
                    TaskDialog.Show("支管中心對齊", "支管與幹管接近平行，無法判定中心線交點。");
                    return Result.Cancelled;
                }

                int movingEndIndex = GetNearestEndIndexToLineInPlan(branchLine, mainLine);
                XYZ oldMovingPoint = branchLine.GetEndPoint(movingEndIndex);
                XYZ fixedPoint = branchLine.GetEndPoint(1 - movingEndIndex);
                PipeCenterAlignGeometryMode geometryMode = PipeCenterAlignSettings.GetGeometryMode();

                if (geometryMode == PipeCenterAlignGeometryMode.EndpointOnly && IsEndpointConnected(branchPipe, oldMovingPoint))
                {
                    TaskDialog.Show(
                        "支管中心對齊",
                        "選取的支管端點已連接其他管件或管段。\n\n請先斷開該端點，再執行中心對齊，避免破壞既有接頭。");
                    return Result.Cancelled;
                }

                if (!TryGetAlignedEndpoint(mainLine, fixedPoint, oldMovingPoint, out XYZ targetPoint, out XYZ slopePoint, out string failReason))
                {
                    TaskDialog.Show("支管中心對齊", failReason);
                    return Result.Cancelled;
                }

                XYZ newFixedPoint = fixedPoint;
                XYZ newMovingPoint = targetPoint;
                double verticalShift = 0.0;
                if (geometryMode == PipeCenterAlignGeometryMode.WholePipeElevation)
                {
                    verticalShift = targetPoint.Z - slopePoint.Z;
                    XYZ elevationMove = XYZ.BasisZ.Multiply(verticalShift);
                    newFixedPoint = fixedPoint + elevationMove;
                    newMovingPoint = slopePoint + elevationMove;
                }

                double moveDistance = Math.Max(oldMovingPoint.DistanceTo(newMovingPoint), fixedPoint.DistanceTo(newFixedPoint));
                double oneMillimeter = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
                if (!PipeCenterAlignSettings.TryGetCreateFittingChoice(out bool shouldCreateTee, out PipeCenterAlignConnectionMode connectionMode))
                {
                    return Result.Cancelled;
                }

                if (connectionMode == PipeCenterAlignConnectionMode.Vertical45)
                {
                    newFixedPoint = fixedPoint;
                    newMovingPoint = targetPoint;
                    moveDistance = oldMovingPoint.DistanceTo(targetPoint);
                }

                double minimumLength = UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);
                if (connectionMode != PipeCenterAlignConnectionMode.Vertical45 &&
                    moveDistance >= oneMillimeter &&
                    newMovingPoint.DistanceTo(newFixedPoint) < minimumLength)
                {
                    TaskDialog.Show("支管中心對齊", "調整後支管長度過短，已取消操作。");
                    return Result.Cancelled;
                }

                if (!shouldCreateTee && moveDistance < oneMillimeter)
                {
                    TaskDialog.Show("支管中心對齊", "支管端點已在幹管中心線上，不需要調整。");
                    return Result.Succeeded;
                }

                if (shouldCreateTee &&
                    RequiresMainPipeSplit(connectionMode, mainPipe, branchPipe) &&
                    !CanSplitMainPipe(mainLine, targetPoint, out string splitFailReason))
                {
                    TaskDialog.Show("支管中心對齊", splitFailReason);
                    return Result.Cancelled;
                }

                ElementId fittingId = ElementId.InvalidElementId;
                ElementId splitPipeId = ElementId.InvalidElementId;
                ElementId vertical45PipeId = ElementId.InvalidElementId;
                bool vertical45Created = false;

                using (Transaction tx = new Transaction(doc, GetTransactionName(shouldCreateTee, connectionMode)))
                {
                    tx.Start();

                    if (connectionMode == PipeCenterAlignConnectionMode.Vertical45)
                    {
                        if (!TryCreateVertical45Alignment(
                            doc,
                            mainPipe,
                            branchPipe,
                            movingEndIndex,
                            newFixedPoint,
                            targetPoint,
                            minimumLength,
                            out vertical45PipeId,
                            out fittingId,
                            out string verticalFailReason))
                        {
                            tx.RollBack();
                            TaskDialog.Show("支管中心對齊", verticalFailReason);
                            return Result.Cancelled;
                        }

                        vertical45Created = true;
                    }
                    else if (moveDistance >= oneMillimeter)
                    {
                        if (geometryMode == PipeCenterAlignGeometryMode.WholePipeElevation &&
                            Math.Abs(verticalShift) >= oneMillimeter)
                        {
                            ICollection<ElementId> branchGroupIds = CollectConnectedBranchGroupIds(branchPipe, mainPipe.Id);
                            if (branchGroupIds.Count > 0)
                            {
                                List<ElementId> verticalPipeIds = branchGroupIds
                                    .Where(id => IsVerticalPipe(doc.GetElement(id) as Pipe))
                                    .ToList();
                                List<ElementId> movableGroupIds = branchGroupIds
                                    .Where(id => !verticalPipeIds.Any(verticalId => verticalId == id))
                                    .ToList();

                                if (movableGroupIds.Count > 0)
                                {
                                    ElementTransformUtils.MoveElements(doc, movableGroupIds, XYZ.BasisZ.Multiply(verticalShift));
                                    doc.Regenerate();
                                }
                            }
                        }

                        LocationCurve locationCurve = branchPipe.Location as LocationCurve;
                        Line newLine = movingEndIndex == 0
                            ? Line.CreateBound(newMovingPoint, newFixedPoint)
                            : Line.CreateBound(newFixedPoint, newMovingPoint);

                        locationCurve.Curve = newLine;
                        doc.Regenerate();
                    }

                    if (shouldCreateTee &&
                        connectionMode != PipeCenterAlignConnectionMode.Vertical45 &&
                        !TryCreateFittingAtIntersection(doc, mainPipe, branchPipe, targetPoint, connectionMode, out splitPipeId, out fittingId, out string teeFailReason))
                    {
                        tx.Commit();
                        TaskDialog.Show(
                            "支管中心對齊",
                            teeFailReason + "\n\n已完成支管端點對齊，但接頭建立失敗；未保留半完成的切管或接頭。");
                        return Result.Succeeded;
                    }

                    tx.Commit();
                }

                double movedMm = UnitUtils.ConvertFromInternalUnits(moveDistance, UnitTypeId.Millimeters);
                string teeSummary = vertical45Created
                    ? fittingId == ElementId.InvalidElementId
                        ? $"\n45°斜管 ID：{vertical45PipeId.GetIdValue()}\n未建立接頭：請檢查幹管 Routing Preferences 是否支援 Takeoff/Wye"
                        : $"\n45°斜管 ID：{vertical45PipeId.GetIdValue()}\n接頭 ID：{fittingId.GetIdValue()}"
                    : shouldCreateTee
                    ? splitPipeId == ElementId.InvalidElementId
                        ? $"\n接頭 ID：{fittingId.GetIdValue()}"
                        : $"\n新幹管段 ID：{splitPipeId.GetIdValue()}\n三通 ID：{fittingId.GetIdValue()}"
                    : "\n未建立三通：使用者選擇只對齊端點";

                TaskDialog.Show(
                    "支管中心對齊",
                    $"完成支管端點中心對齊。\n\n" +
                    $"幹管：Pipe / ID {mainPipe.Id.GetIdValue()}\n" +
                    $"支管：Pipe / ID {branchPipe.Id.GetIdValue()}\n" +
                    $"端點位移：約 {movedMm:F1} mm" +
                    teeSummary);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("支管中心對齊", $"執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static bool TryGetTargetPipes(UIDocument uiDoc, Document doc, out Pipe mainPipe, out Pipe branchPipe)
        {
            mainPipe = null;
            branchPipe = null;

            List<Pipe> preselected = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Pipe>()
                .Take(2)
                .ToList();

            if (preselected.Count == 2)
            {
                if (TryArrangePipes(preselected[0], preselected[1], out mainPipe, out branchPipe))
                {
                    uiDoc.Selection.SetElementIds(new List<ElementId>());
                    return true;
                }
            }

            if (preselected.Count == 1)
            {
                mainPipe = preselected[0];
                Reference branchRefFromSingle = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new PipeSelectionFilter(mainPipe.Id),
                    "請選取要對齊的支管");

                branchPipe = doc.GetElement(branchRefFromSingle) as Pipe;
                uiDoc.Selection.SetElementIds(new List<ElementId>());
                return branchPipe != null && mainPipe.Id != branchPipe.Id;
            }

            Reference mainRef = uiDoc.Selection.PickObject(
                ObjectType.Element,
                new PipeSelectionFilter(),
                "請選取幹管（平面上有長度的直管，可帶坡度）");

            Reference branchRef = uiDoc.Selection.PickObject(
                ObjectType.Element,
                new PipeSelectionFilter(),
                "請選取要對齊的支管");

            mainPipe = doc.GetElement(mainRef) as Pipe;
            branchPipe = doc.GetElement(branchRef) as Pipe;

            if (mainPipe == null || branchPipe == null || mainPipe.Id == branchPipe.Id)
            {
                TaskDialog.Show("支管中心對齊", "請選取兩支不同的 Pipe。");
                return false;
            }

            return true;
        }

        private static bool TryArrangePipes(Pipe first, Pipe second, out Pipe mainPipe, out Pipe branchPipe)
        {
            mainPipe = null;
            branchPipe = null;

            if (first == null || second == null || first.Id == second.Id)
            {
                return false;
            }

            Line firstLine = GetPipeLine(first);
            Line secondLine = GetPipeLine(second);
            bool firstMainCandidate = firstLine != null && IsPlanMainCandidate(firstLine);
            bool secondMainCandidate = secondLine != null && IsPlanMainCandidate(secondLine);

            if (firstMainCandidate && !secondMainCandidate)
            {
                mainPipe = first;
                branchPipe = second;
                return true;
            }

            if (!firstMainCandidate && secondMainCandidate)
            {
                mainPipe = second;
                branchPipe = first;
                return true;
            }

            mainPipe = first;
            branchPipe = second;
            return true;
        }

        internal static Line GetPipeLine(Pipe pipe)
        {
            LocationCurve locationCurve = pipe?.Location as LocationCurve;
            return locationCurve?.Curve as Line;
        }

        private static XYZ GetDirection(Line line)
        {
            return (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
        }

        internal static bool IsPlanMainCandidate(Line line)
        {
            XYZ delta = line.GetEndPoint(1) - line.GetEndPoint(0);
            double xyLength = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            return xyLength >= UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);
        }

        internal static bool AreParallelInPlan(Line first, Line second)
        {
            XYZ firstDirection = GetPlanDirection(first);
            XYZ secondDirection = GetPlanDirection(second);
            if (firstDirection == null || secondDirection == null)
            {
                return true;
            }

            return Math.Abs(Cross2D(firstDirection, secondDirection)) < ParallelTolerance;
        }

        internal static int GetNearestEndIndexToLineInPlan(Line branchLine, Line mainLine)
        {
            double distance0 = DistancePointToSegmentInPlan(branchLine.GetEndPoint(0), mainLine);
            double distance1 = DistancePointToSegmentInPlan(branchLine.GetEndPoint(1), mainLine);
            return distance0 <= distance1 ? 0 : 1;
        }

        internal static bool TryGetAlignedEndpoint(Line mainLine, XYZ fixedPoint, XYZ movingPoint, out XYZ targetPoint, out XYZ slopePoint, out string failReason)
        {
            targetPoint = null;
            slopePoint = null;
            failReason = "無法計算支管中心線與幹管中心線的平面交點。";

            XYZ branchVector = movingPoint - fixedPoint;
            XYZ branchPlanDirection = GetPlanDirection(fixedPoint, movingPoint);
            XYZ mainPlanDirection = GetPlanDirection(mainLine);

            if (branchVector.GetLength() < DirectionTolerance || branchPlanDirection == null || mainPlanDirection == null)
            {
                failReason = "支管長度過短或接近垂直，無法判定平面方向。";
                return false;
            }

            XYZ mainStart = mainLine.GetEndPoint(0);
            XYZ mainEnd = mainLine.GetEndPoint(1);
            XYZ mainDelta = ToPlanVector(mainEnd - mainStart);
            XYZ branchDelta = ToPlanVector(movingPoint - fixedPoint);
            XYZ startDelta = ToPlanVector(fixedPoint - mainStart);
            double denominator = Cross2D(mainDelta, branchDelta);

            if (Math.Abs(denominator) < DirectionTolerance)
            {
                failReason = "支管與幹管在平面上接近平行，無法建立交點。";
                return false;
            }

            double mainRatio = Cross2D(startDelta, branchDelta) / denominator;
            double branchRatio = Cross2D(startDelta, mainDelta) / denominator;

            if (mainRatio < -DirectionTolerance || mainRatio > 1.0 + DirectionTolerance)
            {
                failReason = "支管中心線與幹管中心線的平面交點不在幹管線段範圍內。";
                return false;
            }

            if (branchRatio < -DirectionTolerance)
            {
                failReason = "平面交點位於支管固定端反方向，無法在不改變平面角度的情況下對齊。請確認選到靠近幹管的支管端。";
                return false;
            }

            mainRatio = Math.Max(0.0, Math.Min(1.0, mainRatio));
            double targetZ = mainStart.Z + (mainEnd.Z - mainStart.Z) * mainRatio;
            XYZ planHit = mainStart + (mainEnd - mainStart).Multiply(mainRatio);
            targetPoint = new XYZ(planHit.X, planHit.Y, targetZ);
            XYZ slopeHit = fixedPoint + branchVector.Multiply(branchRatio);
            slopePoint = new XYZ(planHit.X, planHit.Y, slopeHit.Z);
            return true;
        }

        private static XYZ GetPlanDirection(Line line)
        {
            return GetPlanDirection(line.GetEndPoint(0), line.GetEndPoint(1));
        }

        private static XYZ GetPlanDirection(XYZ start, XYZ end)
        {
            XYZ delta = ToPlanVector(end - start);
            if (delta.GetLength() < DirectionTolerance)
            {
                return null;
            }

            return delta.Normalize();
        }

        private static XYZ ToPlanVector(XYZ vector)
        {
            return new XYZ(vector.X, vector.Y, 0.0);
        }

        private static double Cross2D(XYZ first, XYZ second)
        {
            return first.X * second.Y - first.Y * second.X;
        }

        private static double DistancePointToSegmentInPlan(XYZ point, Line line)
        {
            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);
            XYZ segment = ToPlanVector(end - start);
            double segmentLengthSquared = segment.DotProduct(segment);
            if (segmentLengthSquared < DirectionTolerance)
            {
                return ToPlanVector(point - start).GetLength();
            }

            double ratio = ToPlanVector(point - start).DotProduct(segment) / segmentLengthSquared;
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            XYZ closest = start + (end - start).Multiply(ratio);
            return ToPlanVector(point - closest).GetLength();
        }

        internal static bool CanSplitMainPipe(Line mainLine, XYZ splitPoint, out string failReason)
        {
            failReason = null;

            double minimumSegmentLength = UnitUtils.ConvertToInternalUnits(50.0, UnitTypeId.Millimeters);
            double distanceToStart = splitPoint.DistanceTo(mainLine.GetEndPoint(0));
            double distanceToEnd = splitPoint.DistanceTo(mainLine.GetEndPoint(1));

            if (distanceToStart < minimumSegmentLength || distanceToEnd < minimumSegmentLength)
            {
                failReason = "三通位置太接近幹管端點，無法安全切開幹管。\n\n請改用只對齊端點，或先延長/調整幹管後再建立三通。";
                return false;
            }

            return true;
        }

        private static string GetTransactionName(bool shouldCreateFitting, PipeCenterAlignConnectionMode connectionMode)
        {
            if (connectionMode == PipeCenterAlignConnectionMode.Vertical45)
            {
                return "支管 45°垂直翻彎對齊";
            }

            return shouldCreateFitting ? "支管中心對齊並建立三通" : "支管中心對齊";
        }

        internal static bool RequiresMainPipeSplit(PipeCenterAlignConnectionMode connectionMode, Pipe mainPipe, Pipe branchPipe)
        {
            if (connectionMode == PipeCenterAlignConnectionMode.Tee)
            {
                return true;
            }

            if (connectionMode == PipeCenterAlignConnectionMode.Takeoff)
            {
                return false;
            }

            if (connectionMode == PipeCenterAlignConnectionMode.Vertical45)
            {
                return false;
            }

            return IsTeeAngleCandidate(mainPipe, branchPipe);
        }

        private static bool TryCreateVertical45Alignment(
            Document doc,
            Pipe mainPipe,
            Pipe branchPipe,
            int movingEndIndex,
            XYZ fixedPoint,
            XYZ targetPoint,
            double minimumLength,
            out ElementId diagonalPipeId,
            out ElementId fittingId,
            out string failReason)
        {
            diagonalPipeId = ElementId.InvalidElementId;
            fittingId = ElementId.InvalidElementId;
            failReason = null;

            XYZ planDirection = GetPlanDirection(fixedPoint, targetPoint);
            if (planDirection == null)
            {
                failReason = "固定端與幹管交點在平面上距離過短，無法建立 45°垂直翻彎。";
                return false;
            }

            double deltaZ = targetPoint.Z - fixedPoint.Z;
            double verticalTolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            if (Math.Abs(deltaZ) <= verticalTolerance)
            {
                failReason = "支管固定端與幹管交點高程幾乎相同，不需要建立 45°垂直翻彎。";
                return false;
            }

            double angleRadians = 45.0 * Math.PI / 180.0;
            double diagonalRun = Math.Abs(deltaZ) / Math.Tan(angleRadians);
            double totalRun = ToPlanVector(targetPoint - fixedPoint).GetLength();
            double requiredRun = diagonalRun + minimumLength;
            if (totalRun < requiredRun)
            {
                double totalRunMm = UnitUtils.ConvertFromInternalUnits(totalRun, UnitTypeId.Millimeters);
                double requiredRunMm = UnitUtils.ConvertFromInternalUnits(requiredRun, UnitTypeId.Millimeters);
                failReason =
                    "支管到幹管交點的平面距離不足，無法放入 45°垂直翻彎。\n\n" +
                    $"目前可用距離：約 {totalRunMm:F1} mm\n" +
                    $"至少需要：約 {requiredRunMm:F1} mm";
                return false;
            }

            XYZ bendPlanPoint = targetPoint - planDirection.Multiply(diagonalRun);
            XYZ bendPoint = new XYZ(bendPlanPoint.X, bendPlanPoint.Y, fixedPoint.Z);
            if (bendPoint.DistanceTo(fixedPoint) < minimumLength ||
                bendPoint.DistanceTo(targetPoint) < minimumLength)
            {
                failReason = "45°翻彎後的直管或斜管長度過短，已取消操作。";
                return false;
            }

            ElementId systemTypeId = GetSystemTypeId(branchPipe);
            ElementId pipeTypeId = branchPipe.GetTypeId();
            ElementId levelId = GetReferenceLevelId(branchPipe);
            if (systemTypeId == ElementId.InvalidElementId ||
                pipeTypeId == ElementId.InvalidElementId ||
                levelId == ElementId.InvalidElementId)
            {
                failReason = "無法取得支管的系統、管型或參考樓層。";
                return false;
            }

            Pipe diagonalPipe;
            try
            {
                LocationCurve locationCurve = branchPipe.Location as LocationCurve;
                Line firstLine = movingEndIndex == 0
                    ? Line.CreateBound(bendPoint, fixedPoint)
                    : Line.CreateBound(fixedPoint, bendPoint);
                locationCurve.Curve = firstLine;

                diagonalPipe = Pipe.Create(
                    doc,
                    systemTypeId,
                    pipeTypeId,
                    levelId,
                    bendPoint,
                    targetPoint);

                double diameter = GetPipeDiameter(branchPipe);
                ApplyPipeDiameter(diagonalPipe, diameter);
                doc.Regenerate();
            }
            catch (Exception ex)
            {
                failReason = $"建立 45°垂直翻彎管段失敗：{ex.Message}";
                return false;
            }

            diagonalPipeId = diagonalPipe.Id;

            Connector branchAtBend = GetOpenEndConnectorNear(branchPipe, bendPoint);
            Connector diagonalAtBend = GetOpenEndConnectorNear(diagonalPipe, bendPoint);
            if (!TryCreateElbow(doc, branchAtBend, diagonalAtBend, out string elbowFailReason))
            {
                failReason =
                    "無法在支管與 45°斜管之間建立彎頭。\n\n" +
                    elbowFailReason;
                return false;
            }

            Connector diagonalAtMain = GetOpenEndConnectorNear(diagonalPipe, targetPoint);
            if (!TryCreateTakeoffFitting(doc, mainPipe, diagonalPipe, diagonalAtMain, out fittingId, out string takeoffFailReason))
            {
                fittingId = ElementId.InvalidElementId;
                failReason =
                    "已建立 45°垂直翻彎，但無法建立幹管 Takeoff/Wye 接頭。\n\n" +
                    takeoffFailReason;
                return true;
            }

            return true;
        }

        internal static bool TryCreateFittingAtIntersection(
            Document doc,
            Pipe mainPipe,
            Pipe branchPipe,
            XYZ intersectionPoint,
            PipeCenterAlignConnectionMode connectionMode,
            out ElementId splitPipeId,
            out ElementId fittingId,
            out string failReason)
        {
            splitPipeId = ElementId.InvalidElementId;
            fittingId = ElementId.InvalidElementId;
            failReason = null;

            bool teeCandidate = IsTeeAngleCandidate(mainPipe, branchPipe);
            if (connectionMode == PipeCenterAlignConnectionMode.Takeoff ||
                (connectionMode == PipeCenterAlignConnectionMode.Auto && !teeCandidate))
            {
                Connector branchConnector = GetOpenEndConnectorNear(branchPipe, intersectionPoint);
                if (TryCreateTakeoffAtIntersection(doc, mainPipe, branchPipe, branchConnector, out fittingId, out failReason))
                {
                    return true;
                }

                if (connectionMode == PipeCenterAlignConnectionMode.Takeoff)
                {
                    return false;
                }

                failReason += "\n\n自動模式已改嘗試三通。";
            }

            using (SubTransaction teeTx = new SubTransaction(doc))
            {
                try
                {
                    teeTx.Start();

                    splitPipeId = PlumbingUtils.BreakCurve(doc, mainPipe.Id, intersectionPoint);
                    if (splitPipeId == ElementId.InvalidElementId)
                    {
                        failReason = "幹管切割失敗。請確認交點位於幹管中心線內且不是端點。";
                        teeTx.RollBack();
                        return false;
                    }

                    doc.Regenerate();

                    Pipe splitPipe = doc.GetElement(splitPipeId) as Pipe;
                    Connector mainConnectorA = GetOpenEndConnectorNear(mainPipe, intersectionPoint) ?? GetEndConnectorNear(mainPipe, intersectionPoint);
                    Connector mainConnectorB = GetOpenEndConnectorNear(splitPipe, intersectionPoint) ?? GetEndConnectorNear(splitPipe, intersectionPoint);
                    Connector branchConnector = GetOpenEndConnectorNear(branchPipe, intersectionPoint) ?? GetEndConnectorNear(branchPipe, intersectionPoint);
                    TeeConnectorCandidate bestTeeCandidate = BuildTeeConnectorCandidates(mainPipe, splitPipe, branchPipe, intersectionPoint)
                        .FirstOrDefault();
                    if (bestTeeCandidate != null)
                    {
                        mainConnectorA = bestTeeCandidate.MainConnectorA;
                        mainConnectorB = bestTeeCandidate.MainConnectorB;
                        branchConnector = bestTeeCandidate.BranchConnector;
                    }

                    if (mainConnectorA == null || mainConnectorB == null || branchConnector == null)
                    {
                        failReason = "找不到可用的 Tee Connector。\n\n請確認支管端點已對齊幹管中心，且接入端不是被其他管件完全佔用。";
                        teeTx.RollBack();
                        return false;
                    }

                    if (HasExternalConnection(branchConnector, branchPipe.Id))
                    {
                        failReason = "支管接入端已連接其他管件，無法再直接建立 Tee。\n\n請選取靠近幹管且端點可接入的水平支管段，或先使用只對齊後由 Revit 配件鎖點連動調整。";
                        teeTx.RollBack();
                        return false;
                    }

                    if (TryCreateByConnectorConnectTo(
                        doc,
                        mainConnectorA,
                        mainConnectorB,
                        branchConnector,
                        out FamilyInstance connectedFitting,
                        out string connectFailReason))
                    {
                        doc.Regenerate();
                        if (IsTeeConnectedToExpectedPipes(connectedFitting, mainPipe.Id, splitPipeId, branchPipe.Id))
                        {
                            fittingId = connectedFitting.Id;
                            teeTx.Commit();
                            return true;
                        }

                        doc.Delete(connectedFitting.Id);
                        doc.Regenerate();
                        connectFailReason = "Connector.ConnectTo 產生了管件，但未正確連接到兩段幹管與支管。";
                    }

                    if (!TryCreateValidatedTee(
                        doc,
                        mainConnectorA,
                        mainConnectorB,
                        branchConnector,
                        mainPipe.Id,
                        splitPipeId,
                        branchPipe.Id,
                        out FamilyInstance tee,
                        out failReason))
                    {
                        if (!string.IsNullOrWhiteSpace(connectFailReason))
                        {
                            failReason = connectFailReason + "\n\n" + failReason;
                        }

                        teeTx.RollBack();
                        return false;
                    }

                    fittingId = tee.Id;
                    doc.Regenerate();
                    teeTx.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    if (teeTx.HasStarted())
                    {
                        teeTx.RollBack();
                    }

                    string teeFailure =
                        "Revit 依三段管最近 Connector 建立可變角度三通失敗。\n\n" +
                        $"Tee 訊息：{ex.Message}";
                    failReason = teeFailure;
                    return false;
                }
            }
        }

        private static bool TryCreateTakeoffAtIntersection(
            Document doc,
            Pipe mainPipe,
            Pipe branchPipe,
            Connector branchConnector,
            out ElementId fittingId,
            out string failReason)
        {
            fittingId = ElementId.InvalidElementId;
            failReason = null;

            using (SubTransaction takeoffTx = new SubTransaction(doc))
            {
                try
                {
                    takeoffTx.Start();
                    if (!TryCreateTakeoffFitting(doc, mainPipe, branchPipe, branchConnector, out fittingId, out failReason))
                    {
                        takeoffTx.RollBack();
                        return false;
                    }

                    takeoffTx.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    if (takeoffTx.HasStarted())
                    {
                        takeoffTx.RollBack();
                    }

                    failReason = $"建立斜接 Takeoff/Wye 失敗：{ex.Message}";
                    return false;
                }
            }
        }

        private static bool IsTeeAngleCandidate(Pipe mainPipe, Pipe branchPipe)
        {
            Line mainLine = GetPipeLine(mainPipe);
            Line branchLine = GetPipeLine(branchPipe);
            XYZ mainDirection = GetPlanDirection(mainLine);
            XYZ branchDirection = GetPlanDirection(branchLine);
            if (mainDirection == null || branchDirection == null)
            {
                return false;
            }

            double dot = Math.Abs(mainDirection.DotProduct(branchDirection));
            double angleDegrees = Math.Acos(Math.Min(1.0, Math.Max(-1.0, dot))) * 180.0 / Math.PI;
            double angleFromPerpendicular = Math.Abs(90.0 - angleDegrees);
            return angleFromPerpendicular <= 5.0;
        }

        private static bool TryCreateTakeoffFitting(
            Document doc,
            Pipe mainPipe,
            Pipe branchPipe,
            Connector branchConnector,
            out ElementId fittingId,
            out string failReason)
        {
            fittingId = ElementId.InvalidElementId;
            failReason = null;

            if (branchConnector == null)
            {
                failReason = "找不到支管開放端點 Connector，無法建立斜接 Takeoff/Wye。";
                return false;
            }

            try
            {
                FamilyInstance takeoff = doc.Create.NewTakeoffFitting(branchConnector, mainPipe);
                doc.Regenerate();

                if (takeoff == null)
                {
                    failReason = "Revit 未能建立斜接 Takeoff/Wye。請確認管路類型 Routing Preference 已設定 Tap/Takeoff/Wye 類接頭。";
                    return false;
                }

                if (!IsFittingConnectedToExpectedPipes(takeoff, mainPipe.Id, branchPipe.Id))
                {
                    doc.Delete(takeoff.Id);
                    failReason = "Revit 已建立斜接接頭，但未正確連接到幹管與支管。";
                    return false;
                }

                fittingId = takeoff.Id;
                return true;
            }
            catch (Exception ex)
            {
                failReason =
                    "無法建立斜接 Takeoff/Wye。\n\n" +
                    "45 度支管通常需要管路類型 Routing Preference 設定 Tap、Takeoff 或 Wye 類接頭，而不是一般 90 度 Tee。\n\n" +
                    $"Revit 訊息：{ex.Message}";
                return false;
            }
        }

        private static bool TryCreateElbow(Document doc, Connector first, Connector second, out string failReason)
        {
            failReason = null;
            if (first == null || second == null)
            {
                failReason = "找不到建立彎頭所需的開放端點 Connector。";
                return false;
            }

            List<ConnectorPairCandidate> connectorCandidates = BuildConnectorPairCandidates(first.Owner, second.Owner);
            foreach (ConnectorPairCandidate connectorCandidate in connectorCandidates.Take(12))
            {
                if (!connectorCandidate.IsValidTurnAngle)
                {
                    continue;
                }

                try
                {
                    FamilyInstance candidateElbow = doc.Create.NewElbowFitting(connectorCandidate.First, connectorCandidate.Second);
                    doc.Regenerate();
                    if (candidateElbow != null && candidateElbow.IsValidObject)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            try
            {
                FamilyInstance elbow = doc.Create.NewElbowFitting(first, second);
                doc.Regenerate();
                if (elbow == null || !elbow.IsValidObject)
                {
                    failReason = "Revit 未能建立彎頭。請確認管路類型 Routing Preference 已設定相容彎頭。";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failReason = $"Revit 彎頭訊息：{ex.Message}";
                return false;
            }
        }

        private static ElementId GetSystemTypeId(Pipe pipe)
        {
            Parameter parameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId systemTypeId = parameter?.AsElementId() ?? ElementId.InvalidElementId;
            return systemTypeId != ElementId.InvalidElementId
                ? systemTypeId
                : pipe?.MEPSystem?.GetTypeId() ?? ElementId.InvalidElementId;
        }

        private static ElementId GetReferenceLevelId(Pipe pipe)
        {
            Parameter parameter = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            return parameter?.AsElementId() ?? ElementId.InvalidElementId;
        }

        private static double GetPipeDiameter(Pipe pipe)
        {
            return pipe
                .get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?
                .AsDouble() ?? 0.0;
        }

        private static void ApplyPipeDiameter(Pipe pipe, double diameter)
        {
            Parameter parameter = pipe
                .get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (parameter != null && !parameter.IsReadOnly && diameter > 0.0)
            {
                parameter.Set(diameter);
            }
        }

        private static bool IsFittingConnectedToExpectedPipes(FamilyInstance fitting, ElementId firstPipeId, ElementId secondPipeId)
        {
            ConnectorSet connectors = fitting?.MEPModel?.ConnectorManager?.Connectors;
            if (connectors == null)
            {
                return false;
            }

            HashSet<ElementId> connectedPipeIds = new HashSet<ElementId>();
            foreach (Connector connector in connectors)
            {
                foreach (Connector connected in connector.AllRefs)
                {
                    Element owner = connected.Owner;
                    if (owner is Pipe)
                    {
                        connectedPipeIds.Add(owner.Id);
                    }
                }
            }

            return connectedPipeIds.Contains(firstPipeId) && connectedPipeIds.Contains(secondPipeId);
        }

        private static bool TryGetThreeConnectorsClosest(
            Pipe firstPipe,
            Pipe secondPipe,
            Pipe thirdPipe,
            out Connector firstConnector,
            out Connector secondConnector,
            out Connector thirdConnector,
            out string failReason)
        {
            firstConnector = null;
            secondConnector = null;
            thirdConnector = null;
            failReason = null;

            List<Connector> firstConnectors = GetEndConnectors(firstPipe);
            List<Connector> secondConnectors = GetEndConnectors(secondPipe);
            List<Connector> thirdConnectors = GetEndConnectors(thirdPipe);

            if (!firstConnectors.Any() || !secondConnectors.Any() || !thirdConnectors.Any())
            {
                failReason = "找不到建立三通所需的三段管端點 Connector。";
                return false;
            }

            double bestScore = double.MaxValue;
            foreach (Connector c1 in firstConnectors)
            {
                foreach (Connector c2 in secondConnectors)
                {
                    foreach (Connector c3 in thirdConnectors)
                    {
                        if (c1.Domain != c2.Domain || c1.Domain != c3.Domain)
                        {
                            continue;
                        }

                        double score =
                            c1.Origin.DistanceTo(c2.Origin) +
                            c2.Origin.DistanceTo(c3.Origin) +
                            c3.Origin.DistanceTo(c1.Origin);

                        if (score < bestScore)
                        {
                            bestScore = score;
                            firstConnector = c1;
                            secondConnector = c2;
                            thirdConnector = c3;
                        }
                    }
                }
            }

            if (firstConnector == null || secondConnector == null || thirdConnector == null)
            {
                failReason = "三段管的 Connector 系統領域不一致，無法建立三通。";
                return false;
            }

            return true;
        }

        private static List<Connector> GetEndConnectors(Pipe pipe)
        {
            List<Connector> connectors = new List<Connector>();
            if (pipe?.ConnectorManager?.Connectors == null)
            {
                return connectors;
            }

            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector.ConnectorType == ConnectorType.End)
                {
                    connectors.Add(connector);
                }
            }

            return connectors;
        }

        private static bool TryCreateByConnectorConnectTo(
            Document doc,
            Connector mainConnectorA,
            Connector mainConnectorB,
            Connector branchConnector,
            out FamilyInstance fitting,
            out string failReason)
        {
            fitting = null;
            failReason = null;
            List<string> attemptMessages = new List<string>();

            Connector[][] attempts =
            {
                new[] { branchConnector, mainConnectorA },
                new[] { mainConnectorA, branchConnector },
                new[] { branchConnector, mainConnectorB },
                new[] { mainConnectorB, branchConnector }
            };

            foreach (Connector[] attempt in attempts)
            {
                HashSet<ElementId> beforeIds = GetMepFittingIds(doc);
                using (SubTransaction subTx = new SubTransaction(doc))
                {
                    try
                    {
                        subTx.Start();
                        attempt[0].ConnectTo(attempt[1]);
                        doc.Regenerate();

                        FamilyInstance newFitting = GetNewestMepFitting(doc, beforeIds);
                        if (newFitting != null)
                        {
                            subTx.Commit();
                            fitting = newFitting;
                            return true;
                        }

                        subTx.RollBack();
                        attemptMessages.Add("ConnectTo 未產生新的管件。");
                    }
                    catch (Exception ex)
                    {
                        if (subTx.HasStarted())
                        {
                            subTx.RollBack();
                        }

                        attemptMessages.Add(ex.Message);
                    }
                }
            }

            failReason =
                "已嘗試用 Connector.ConnectTo 模擬手動接入，但 Revit 未產生可用管件。\n" +
                $"ConnectTo 訊息：{string.Join(" / ", attemptMessages.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())}";
            return false;
        }

        private static HashSet<ElementId> GetMepFittingIds(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(IsPipeFittingOrAccessory)
                .Select(x => x.Id)
                .ToHashSet();
        }

        private static FamilyInstance GetNewestMepFitting(Document doc, HashSet<ElementId> beforeIds)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(IsPipeFittingOrAccessory)
                .Where(x => !beforeIds.Contains(x.Id))
                .OrderByDescending(x => x.Id.GetIdValue())
                .FirstOrDefault();
        }

        private static bool IsPipeFittingOrAccessory(FamilyInstance familyInstance)
        {
            long categoryId = familyInstance?.Category?.Id.GetIdValue() ?? 0;
            return categoryId == (long)BuiltInCategory.OST_PipeFitting ||
                   categoryId == (long)BuiltInCategory.OST_PipeAccessory;
        }

        private static List<ConnectorPairCandidate> BuildConnectorPairCandidates(Element firstElement, Element secondElement)
        {
            List<Connector> firstConnectors = GetElementConnectors(firstElement)
                .Where(connector => connector.ConnectorType == ConnectorType.End)
                .ToList();
            List<Connector> secondConnectors = GetElementConnectors(secondElement)
                .Where(connector => connector.ConnectorType == ConnectorType.End)
                .ToList();

            List<ConnectorPairCandidate> candidates = new List<ConnectorPairCandidate>();
            foreach (Connector firstConnector in firstConnectors)
            {
                foreach (Connector secondConnector in secondConnectors)
                {
                    if (!AreConnectorsCompatible(firstConnector, secondConnector))
                    {
                        continue;
                    }

                    candidates.Add(new ConnectorPairCandidate(firstConnector, secondConnector));
                }
            }

            return candidates
                .OrderBy(candidate => candidate.SortKey)
                .ToList();
        }

        private static List<TeeConnectorCandidate> BuildTeeConnectorCandidates(Pipe mainPipe, Pipe splitPipe, Pipe branchPipe, XYZ intersectionPoint)
        {
            List<Connector> mainConnectors = GetEndConnectors(mainPipe);
            List<Connector> splitConnectors = GetEndConnectors(splitPipe);
            List<Connector> branchConnectors = GetEndConnectors(branchPipe);
            List<TeeConnectorCandidate> candidates = new List<TeeConnectorCandidate>();

            foreach (Connector mainConnector in mainConnectors)
            {
                foreach (Connector splitConnector in splitConnectors)
                {
                    if (!AreConnectorsCompatible(mainConnector, splitConnector))
                    {
                        continue;
                    }

                    foreach (Connector branchConnector in branchConnectors)
                    {
                        if (!AreConnectorsCompatible(mainConnector, branchConnector) ||
                            !AreConnectorsCompatible(splitConnector, branchConnector) ||
                            HasExternalConnection(branchConnector, branchPipe.Id))
                        {
                            continue;
                        }

                        candidates.Add(new TeeConnectorCandidate(
                            mainConnector,
                            splitConnector,
                            branchConnector,
                            intersectionPoint));
                    }
                }
            }

            return candidates
                .OrderBy(candidate => candidate.SortKey)
                .ToList();
        }

        private static bool AreConnectorsCompatible(Connector firstConnector, Connector secondConnector)
        {
            if (firstConnector == null || secondConnector == null)
            {
                return false;
            }

            if (firstConnector.Domain != secondConnector.Domain)
            {
                return false;
            }

            try
            {
                return firstConnector.Shape == secondConnector.Shape;
            }
            catch
            {
                return true;
            }
        }

        private static int GetConnectorConnectedCount(Connector connector)
        {
            if (connector == null)
            {
                return 1;
            }

            try
            {
                return connector.IsConnected ? 1 : 0;
            }
            catch
            {
                return 1;
            }
        }

        private static double GetConnectorRawAngle(Connector firstConnector, Connector secondConnector)
        {
            XYZ firstDirection = GetConnectorDirection(firstConnector);
            XYZ secondDirection = GetConnectorDirection(secondConnector);
            if (firstDirection == null || secondDirection == null)
            {
                return double.NaN;
            }

            double radians = firstDirection.AngleTo(secondDirection);
            return radians * 180.0 / Math.PI;
        }

        private static double GetConnectorTurnAngle(Connector firstConnector, Connector secondConnector)
        {
            double rawAngle = GetConnectorRawAngle(firstConnector, secondConnector);
            if (double.IsNaN(rawAngle))
            {
                return double.NaN;
            }

            return Math.Min(rawAngle, Math.Abs(180.0 - rawAngle));
        }

        private static bool IsValidElbowTurnAngle(double turnAngle)
        {
            return !double.IsNaN(turnAngle) &&
                   turnAngle > 1.0 &&
                   turnAngle <= 95.0;
        }

        private sealed class ConnectorPairCandidate
        {
            public ConnectorPairCandidate(Connector firstConnector, Connector secondConnector)
            {
                First = firstConnector;
                Second = secondConnector;
                RawAngle = GetConnectorRawAngle(firstConnector, secondConnector);
                TurnAngle = GetConnectorTurnAngle(firstConnector, secondConnector);
                Distance = firstConnector?.Origin.DistanceTo(secondConnector?.Origin) ?? double.MaxValue;
                ConnectedCount = GetConnectorConnectedCount(firstConnector) + GetConnectorConnectedCount(secondConnector);
                IsValidTurnAngle = IsValidElbowTurnAngle(TurnAngle);
            }

            public Connector First { get; }
            public Connector Second { get; }
            public double RawAngle { get; }
            public double TurnAngle { get; }
            public double Distance { get; }
            public int ConnectedCount { get; }
            public bool IsValidTurnAngle { get; }
            public double SortKey => (IsValidTurnAngle ? 0.0 : 1000000.0) + ConnectedCount * 1000.0 + Distance;
        }

        private sealed class TeeConnectorCandidate
        {
            public TeeConnectorCandidate(Connector mainConnectorA, Connector mainConnectorB, Connector branchConnector, XYZ intersectionPoint)
            {
                MainConnectorA = mainConnectorA;
                MainConnectorB = mainConnectorB;
                BranchConnector = branchConnector;

                double mainRawAngle = GetConnectorRawAngle(mainConnectorA, mainConnectorB);
                double branchTurnA = GetConnectorTurnAngle(mainConnectorA, branchConnector);
                double branchTurnB = GetConnectorTurnAngle(mainConnectorB, branchConnector);
                double branchTurn = new[] { branchTurnA, branchTurnB }
                    .Where(value => !double.IsNaN(value))
                    .DefaultIfEmpty(double.NaN)
                    .Min();

                bool branchAngleValid = IsValidElbowTurnAngle(branchTurn);
                double mainAnglePenalty = double.IsNaN(mainRawAngle)
                    ? 180.0
                    : Math.Abs(180.0 - mainRawAngle);
                double distancePenalty =
                    (mainConnectorA?.Origin.DistanceTo(intersectionPoint) ?? double.MaxValue / 4.0) +
                    (mainConnectorB?.Origin.DistanceTo(intersectionPoint) ?? double.MaxValue / 4.0) +
                    (branchConnector?.Origin.DistanceTo(intersectionPoint) ?? double.MaxValue / 4.0);
                int connectedCount =
                    GetConnectorConnectedCount(mainConnectorA) +
                    GetConnectorConnectedCount(mainConnectorB) +
                    GetConnectorConnectedCount(branchConnector);

                SortKey =
                    (branchAngleValid ? 0.0 : 1000000.0) +
                    mainAnglePenalty * 1000.0 +
                    connectedCount * 100.0 +
                    distancePenalty;
            }

            public Connector MainConnectorA { get; }
            public Connector MainConnectorB { get; }
            public Connector BranchConnector { get; }
            public double SortKey { get; }
        }

        private static List<Connector[]> BuildTeeConnectorOrders(Connector mainConnectorA, Connector mainConnectorB, Connector branchConnector)
        {
            List<Connector[]> attempts = new List<Connector[]>
            {
                new[] { mainConnectorA, mainConnectorB, branchConnector },
                new[] { mainConnectorB, mainConnectorA, branchConnector },
                new[] { mainConnectorA, branchConnector, mainConnectorB },
                new[] { mainConnectorB, branchConnector, mainConnectorA },
                new[] { branchConnector, mainConnectorA, mainConnectorB },
                new[] { branchConnector, mainConnectorB, mainConnectorA }
            };

            return attempts
                .OrderBy(GetTeeConnectorOrderScore)
                .ToList();
        }

        private static double GetTeeConnectorOrderScore(Connector[] attempt)
        {
            if (attempt == null || attempt.Length < 3)
            {
                return double.MaxValue;
            }

            double firstSecondRawAngle = GetConnectorRawAngle(attempt[0], attempt[1]);
            double mainPenalty = double.IsNaN(firstSecondRawAngle)
                ? 180.0
                : Math.Abs(180.0 - firstSecondRawAngle);
            double branchTurnA = GetConnectorTurnAngle(attempt[0], attempt[2]);
            double branchTurnB = GetConnectorTurnAngle(attempt[1], attempt[2]);
            double branchTurn = new[] { branchTurnA, branchTurnB }
                .Where(value => !double.IsNaN(value))
                .DefaultIfEmpty(double.NaN)
                .Min();
            bool branchAngleValid = IsValidElbowTurnAngle(branchTurn);
            double distancePenalty =
                (attempt[0]?.Origin.DistanceTo(attempt[1]?.Origin) ?? 1000.0) +
                (attempt[2]?.Origin.DistanceTo(attempt[0]?.Origin) ?? 1000.0) +
                (attempt[2]?.Origin.DistanceTo(attempt[1]?.Origin) ?? 1000.0);

            return (branchAngleValid ? 0.0 : 1000000.0) +
                   mainPenalty * 1000.0 +
                   distancePenalty;
        }

        private static bool TryCreateValidatedTee(
            Document doc,
            Connector mainConnectorA,
            Connector mainConnectorB,
            Connector branchConnector,
            ElementId mainPipeId,
            ElementId splitPipeId,
            ElementId branchPipeId,
            out FamilyInstance tee,
            out string failReason)
        {
            tee = null;
            failReason = null;
            List<string> attemptMessages = new List<string>();

            List<Connector[]> attempts = BuildTeeConnectorOrders(mainConnectorA, mainConnectorB, branchConnector);

            foreach (Connector[] attempt in attempts)
            {
                using (SubTransaction subTx = new SubTransaction(doc))
                {
                    try
                    {
                        subTx.Start();
                        FamilyInstance candidate = doc.Create.NewTeeFitting(attempt[0], attempt[1], attempt[2]);
                        doc.Regenerate();

                        if (candidate != null)
                        {
                            if (IsTeeConnectedToExpectedPipes(candidate, mainPipeId, splitPipeId, branchPipeId))
                            {
                                subTx.Commit();
                                tee = candidate;
                                return true;
                            }

                            subTx.RollBack();
                            attemptMessages.Add("Revit 已建立三通，但未正確連接到兩段幹管與支管。");
                            continue;
                        }

                        subTx.RollBack();
                        attemptMessages.Add("Revit 未建立三通。");
                    }
                    catch (Exception ex)
                    {
                        if (subTx.HasStarted())
                        {
                            subTx.RollBack();
                        }

                        attemptMessages.Add(ex.Message);
                    }
                }
            }

            failReason =
                "無法正確配置三通。\n\n" +
                "已嘗試三個 Connector 的所有排列，但 Revit 仍無法建立有效連接。請確認：\n" +
                "1. 管路類型 Routing Preference 已設定 Tee fitting。\n" +
                "2. 三通族支援目前的管徑組合。\n" +
                "3. 可變角三通族已載入且允許目前支管角度。\n\n" +
                $"Revit 訊息：{string.Join(" / ", attemptMessages.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())}";
            return false;
        }

        private static bool IsTeeConnectedToExpectedPipes(FamilyInstance tee, ElementId mainPipeId, ElementId splitPipeId, ElementId branchPipeId)
        {
            ConnectorSet connectors = tee?.MEPModel?.ConnectorManager?.Connectors;
            if (connectors == null)
            {
                return false;
            }

            HashSet<ElementId> connectedPipeIds = new HashSet<ElementId>();
            foreach (Connector connector in connectors)
            {
                foreach (Connector connected in connector.AllRefs)
                {
                    Element owner = connected.Owner;
                    if (owner is Pipe)
                    {
                        connectedPipeIds.Add(owner.Id);
                    }
                }
            }

            return connectedPipeIds.Contains(mainPipeId) &&
                   connectedPipeIds.Contains(splitPipeId) &&
                   connectedPipeIds.Contains(branchPipeId);
        }

        private static XYZ GetConnectorDirection(Connector connector)
        {
            try
            {
                XYZ direction = connector?.CoordinateSystem?.BasisZ;
                if (direction == null || direction.GetLength() < DirectionTolerance)
                {
                    return null;
                }

                return direction.Normalize();
            }
            catch
            {
                return null;
            }
        }

        private static Connector GetOpenEndConnectorNear(Pipe pipe, XYZ point)
        {
            if (pipe?.ConnectorManager?.Connectors == null)
            {
                return null;
            }

            double connectorTolerance = UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Millimeters);
            Connector nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector.ConnectorType != ConnectorType.End)
                {
                    continue;
                }

                double distance = connector.Origin.DistanceTo(point);
                if (distance > connectorTolerance || HasExternalConnection(connector, pipe.Id))
                {
                    continue;
                }

                if (distance < nearestDistance)
                {
                    nearest = connector;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private static Connector GetEndConnectorNear(Pipe pipe, XYZ point)
        {
            if (pipe?.ConnectorManager?.Connectors == null || point == null)
            {
                return null;
            }

            Connector nearest = null;
            double nearestDistance = double.MaxValue;
            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector.ConnectorType != ConnectorType.End)
                {
                    continue;
                }

                double distance = connector.Origin.DistanceTo(point);
                if (distance < nearestDistance)
                {
                    nearest = connector;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private static bool IsEndpointConnected(Pipe pipe, XYZ endpoint)
        {
            if (pipe?.ConnectorManager?.Connectors == null)
            {
                return false;
            }

            double connectorTolerance = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters);
            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector.Origin.DistanceTo(endpoint) <= connectorTolerance && connector.IsConnected)
                {
                    foreach (Connector connected in connector.AllRefs)
                    {
                        if (connected.Owner != null && connected.Owner.Id != pipe.Id)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        internal static ICollection<ElementId> CollectConnectedBranchGroupIds(Pipe branchPipe, ElementId mainPipeId)
        {
            var result = new HashSet<long>();
            var queue = new Queue<Element>();
            Document doc = branchPipe?.Document;
            if (branchPipe == null || doc == null)
            {
                return new List<ElementId>();
            }

            queue.Enqueue(branchPipe);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 200)
            {
                Element current = queue.Dequeue();
                if (current == null || current.Id == mainPipeId)
                {
                    continue;
                }

                if (!IsMovableBranchElement(current))
                {
                    continue;
                }

                long idValue = current.Id.GetIdValue();
                if (!result.Add(idValue))
                {
                    continue;
                }

                foreach (Connector connector in GetElementConnectors(current))
                {
                    foreach (Connector connected in connector.AllRefs)
                    {
                        Element owner = connected?.Owner;
                        if (owner == null || owner.Id == current.Id || owner.Id == mainPipeId)
                        {
                            continue;
                        }

                        if (IsMovableBranchElement(owner) && !result.Contains(owner.Id.GetIdValue()))
                        {
                            queue.Enqueue(owner);
                        }
                    }
                }
            }

            return result
                .Select(RevitApiCompatibility.CreateElementId)
                .ToList();
        }

        internal static bool IsVerticalPipe(Pipe pipe)
        {
            LocationCurve locationCurve = pipe?.Location as LocationCurve;
            Line line = locationCurve?.Curve as Line;
            if (line == null)
            {
                return false;
            }

            XYZ direction = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
            return Math.Abs(direction.Z) >= 0.98;
        }

        private static bool IsMovableBranchElement(Element element)
        {
            if (element is Pipe)
            {
                return true;
            }

            FamilyInstance family = element as FamilyInstance;
            if (family?.Category == null)
            {
                return false;
            }

            long categoryId = family.Category.Id.GetIdValue();
            return categoryId == (long)BuiltInCategory.OST_PipeFitting ||
                   categoryId == (long)BuiltInCategory.OST_PipeAccessory;
        }

        private static IEnumerable<Connector> GetElementConnectors(Element element)
        {
            if (element is Pipe pipe)
            {
                return GetEndConnectors(pipe);
            }

            FamilyInstance family = element as FamilyInstance;
            return family?.MEPModel?.ConnectorManager?.Connectors != null
                ? family.MEPModel.ConnectorManager.Connectors.Cast<Connector>()
                : Enumerable.Empty<Connector>();
        }

        private static bool HasExternalConnection(Connector connector, ElementId ownerId)
        {
            if (connector == null || !connector.IsConnected)
            {
                return false;
            }

            foreach (Connector connected in connector.AllRefs)
            {
                if (connected.Owner != null && connected.Owner.Id != ownerId)
                {
                    return true;
                }
            }

            return false;
        }

        private class PipeSelectionFilter : ISelectionFilter
        {
            private readonly ElementId _excludedId;

            public PipeSelectionFilter()
            {
            }

            public PipeSelectionFilter(ElementId excludedId)
            {
                _excludedId = excludedId;
            }

            public bool AllowElement(Element elem)
            {
                return elem is Pipe && (_excludedId == null || elem.Id != _excludedId);
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }

        private class SpecificPipeSelectionFilter : ISelectionFilter
        {
            private readonly Document _doc;
            private readonly ElementId _targetId;

            public SpecificPipeSelectionFilter(Document doc, ElementId targetId)
            {
                _doc = doc;
                _targetId = targetId;
            }

            public bool AllowElement(Element elem)
            {
                return elem != null && elem.Id == _targetId;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                Element element = _doc.GetElement(reference);
                return element != null && element.Id == _targetId;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdPipeBatchCenterAlign : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                Reference mainReference = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new BatchPipeSelectionFilter(),
                    "選取幹管");
                Pipe mainPipe = doc.GetElement(mainReference) as Pipe;
                if (mainPipe == null)
                {
                    return Result.Cancelled;
                }

                IList<Reference> branchReferences = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new BatchPipeSelectionFilter(mainPipe.Id),
                    "選取要批次對齊的支管");
                List<Pipe> branchPipes = branchReferences
                    .Select(reference => doc.GetElement(reference) as Pipe)
                    .Where(pipe => pipe != null && pipe.Id != mainPipe.Id)
                    .GroupBy(pipe => pipe.Id.GetIdValue())
                    .Select(group => group.First())
                    .ToList();

                if (!branchPipes.Any())
                {
                    TaskDialog.Show("批次支管對齊", "未選取支管。");
                    return Result.Cancelled;
                }

                PipeCenterAlignGeometryMode geometryMode = PipeCenterAlignSettings.GetGeometryMode();
                if (!PipeCenterAlignSettings.TryGetCreateFittingChoice(out bool shouldCreateFitting, out PipeCenterAlignConnectionMode connectionMode))
                {
                    return Result.Cancelled;
                }

                if (connectionMode == PipeCenterAlignConnectionMode.Vertical45)
                {
                    TaskDialog.Show("批次支管對齊", "批次模式目前不支援 45° 垂直接入，請改用 Auto / Tee / Align only。");
                    return Result.Cancelled;
                }

                int successCount = 0;
                int fittingFailCount = 0;
                int failCount = 0;
                List<string> reportLines = new List<string>();
                List<string> errorReportLines = new List<string>();

                using (Transaction tx = new Transaction(doc, shouldCreateFitting ? "批次支管對齊與接入" : "批次支管對齊"))
                {
                    tx.Start();

                    foreach (Pipe branchPipe in branchPipes)
                    {
                        using (SubTransaction subTx = new SubTransaction(doc))
                        {
                            subTx.Start();
                            BatchAlignResult result = TryAlignOneBranch(
                                doc,
                                mainPipe,
                                branchPipe,
                                geometryMode,
                                shouldCreateFitting,
                                connectionMode);

                            if (result.Success)
                            {
                                subTx.Commit();
                                successCount++;
                                if (result.FittingRequested && !result.FittingCreated)
                                {
                                    fittingFailCount++;
                                }
                            }
                            else
                            {
                                subTx.RollBack();
                                failCount++;
                            }

                            string reportLine = result.GetReportLine();
                            reportLines.Add(reportLine);
                            if (!result.Success || (result.FittingRequested && !result.FittingCreated))
                            {
                                errorReportLines.Add(reportLine);
                            }
                        }
                    }

                    tx.Commit();
                }

                bool hasError = errorReportLines.Count > 0;
                string title = hasError ? "批次支管對齊 - 有錯誤" : "批次支管對齊";
                List<string> displayLines = hasError ? errorReportLines : reportLines;
                string detail = displayLines.Any()
                    ? string.Join("\n", displayLines.Take(18)) +
                      (displayLines.Count > 18 ? $"\n...其餘 {displayLines.Count - 18} 筆略" : string.Empty)
                    : "所有選取支管皆已處理。";

                TaskDialog.Show(
                    title,
                    $"完成批次支管對齊。\n\n" +
                    $"成功：{successCount}\n" +
                    $"接頭失敗但已對齊：{fittingFailCount}\n" +
                    $"失敗：{failCount}\n\n" +
                    (hasError ? "錯誤/警告項目：\n" : "處理項目：\n") +
                    detail);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("批次支管對齊", $"執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static BatchAlignResult TryAlignOneBranch(
            Document doc,
            Pipe mainPipe,
            Pipe branchPipe,
            PipeCenterAlignGeometryMode geometryMode,
            bool shouldCreateFitting,
            PipeCenterAlignConnectionMode connectionMode)
        {
            BatchAlignResult result = new BatchAlignResult
            {
                BranchId = branchPipe?.Id ?? ElementId.InvalidElementId,
                FittingRequested = shouldCreateFitting
            };

            Line mainLine = CmdPipeCenterAlign.GetPipeLine(mainPipe);
            Line branchLine = CmdPipeCenterAlign.GetPipeLine(branchPipe);
            if (mainLine == null || branchLine == null)
            {
                return result.Fail("不是有效直線管");
            }

            if (!CmdPipeCenterAlign.IsPlanMainCandidate(mainLine))
            {
                return result.Fail("幹管不是水平/斜率很小的平面管");
            }

            if (CmdPipeCenterAlign.IsVerticalPipe(branchPipe))
            {
                return result.Fail("略過立管");
            }

            if (CmdPipeCenterAlign.AreParallelInPlan(mainLine, branchLine))
            {
                return result.Fail("支管與幹管平面平行");
            }

            int movingEndIndex = CmdPipeCenterAlign.GetNearestEndIndexToLineInPlan(branchLine, mainLine);
            XYZ oldMovingPoint = branchLine.GetEndPoint(movingEndIndex);
            XYZ fixedPoint = branchLine.GetEndPoint(1 - movingEndIndex);

            if (geometryMode == PipeCenterAlignGeometryMode.EndpointOnly &&
                IsEndpointConnectedForBatch(branchPipe, oldMovingPoint))
            {
                return result.Fail("支管接入端已連接，EndpointOnly 模式略過");
            }

            if (!CmdPipeCenterAlign.TryGetAlignedEndpoint(mainLine, fixedPoint, oldMovingPoint, out XYZ targetPoint, out XYZ slopePoint, out string failReason))
            {
                return result.Fail(failReason);
            }

            XYZ newFixedPoint = fixedPoint;
            XYZ newMovingPoint = targetPoint;
            double verticalShift = 0.0;
            if (geometryMode == PipeCenterAlignGeometryMode.WholePipeElevation)
            {
                verticalShift = targetPoint.Z - slopePoint.Z;
                XYZ elevationMove = XYZ.BasisZ.Multiply(verticalShift);
                newFixedPoint = fixedPoint + elevationMove;
                newMovingPoint = slopePoint + elevationMove;
            }

            double oneMillimeter = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            double minimumLength = UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);
            double moveDistance = Math.Max(oldMovingPoint.DistanceTo(newMovingPoint), fixedPoint.DistanceTo(newFixedPoint));
            if (moveDistance >= oneMillimeter && newMovingPoint.DistanceTo(newFixedPoint) < minimumLength)
            {
                return result.Fail("支管調整後長度不足");
            }

            if (shouldCreateFitting &&
                CmdPipeCenterAlign.RequiresMainPipeSplit(connectionMode, mainPipe, branchPipe) &&
                !CmdPipeCenterAlign.CanSplitMainPipe(mainLine, targetPoint, out string splitFailReason))
            {
                return result.Fail(splitFailReason);
            }

            if (moveDistance >= oneMillimeter)
            {
                if (geometryMode == PipeCenterAlignGeometryMode.WholePipeElevation &&
                    Math.Abs(verticalShift) >= oneMillimeter)
                {
                    ICollection<ElementId> branchGroupIds = CmdPipeCenterAlign.CollectConnectedBranchGroupIds(branchPipe, mainPipe.Id);
                    List<ElementId> movableGroupIds = branchGroupIds
                        .Where(id => !CmdPipeCenterAlign.IsVerticalPipe(doc.GetElement(id) as Pipe))
                        .ToList();

                    if (movableGroupIds.Count > 0)
                    {
                        ElementTransformUtils.MoveElements(doc, movableGroupIds, XYZ.BasisZ.Multiply(verticalShift));
                        doc.Regenerate();
                    }
                }

                LocationCurve locationCurve = branchPipe.Location as LocationCurve;
                if (locationCurve == null)
                {
                    return result.Fail("支管無 LocationCurve");
                }

                locationCurve.Curve = movingEndIndex == 0
                    ? Line.CreateBound(newMovingPoint, newFixedPoint)
                    : Line.CreateBound(newFixedPoint, newMovingPoint);
                doc.Regenerate();
            }

            result.Success = true;
            result.MovedMm = UnitUtils.ConvertFromInternalUnits(moveDistance, UnitTypeId.Millimeters);

            if (shouldCreateFitting)
            {
                if (CmdPipeCenterAlign.TryCreateFittingAtIntersection(doc, mainPipe, branchPipe, targetPoint, connectionMode, out ElementId splitPipeId, out ElementId fittingId, out string teeFailReason))
                {
                    result.FittingCreated = true;
                    result.FittingId = fittingId;
                    result.SplitPipeId = splitPipeId;
                }
                else
                {
                    result.FittingCreated = false;
                    result.Message = teeFailReason;
                }
            }

            return result;
        }

        private static bool IsEndpointConnectedForBatch(Pipe pipe, XYZ endpoint)
        {
            if (pipe?.ConnectorManager?.Connectors == null)
            {
                return false;
            }

            double connectorTolerance = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters);
            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector.Origin.DistanceTo(endpoint) <= connectorTolerance && connector.IsConnected)
                {
                    foreach (Connector connected in connector.AllRefs)
                    {
                        if (connected.Owner != null && connected.Owner.Id != pipe.Id)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private sealed class BatchAlignResult
        {
            public ElementId BranchId { get; set; } = ElementId.InvalidElementId;
            public bool Success { get; set; }
            public bool FittingRequested { get; set; }
            public bool FittingCreated { get; set; }
            public ElementId FittingId { get; set; } = ElementId.InvalidElementId;
            public ElementId SplitPipeId { get; set; } = ElementId.InvalidElementId;
            public double MovedMm { get; set; }
            public string Message { get; set; }

            public BatchAlignResult Fail(string message)
            {
                Success = false;
                Message = message;
                return this;
            }

            public string GetReportLine()
            {
                string idText = BranchId == ElementId.InvalidElementId ? "?" : BranchId.GetIdValue().ToString();
                if (!Success)
                {
                    return $"ID {idText}：失敗 - {Message}";
                }

                if (FittingRequested && !FittingCreated)
                {
                    return $"ID {idText}：已對齊 {MovedMm:F1} mm，接頭失敗 - {Message}";
                }

                return FittingCreated
                    ? $"ID {idText}：已對齊 {MovedMm:F1} mm，接頭 ID {FittingId.GetIdValue()}"
                    : $"ID {idText}：已對齊 {MovedMm:F1} mm";
            }
        }

        private sealed class BatchPipeSelectionFilter : ISelectionFilter
        {
            private readonly ElementId _excludedId;

            public BatchPipeSelectionFilter()
            {
                _excludedId = ElementId.InvalidElementId;
            }

            public BatchPipeSelectionFilter(ElementId excludedId)
            {
                _excludedId = excludedId ?? ElementId.InvalidElementId;
            }

            public bool AllowElement(Element elem)
            {
                return elem is Pipe && elem.Id != _excludedId;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdPipeCenterAlignSettings : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return PipeCenterAlignSettings.ShowSettingsDialog(true) ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("支管中心對齊設定", $"設定失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    public class PipeCenterAlignSettingsData
    {
        public PipeCenterAlignFittingMode FittingMode { get; set; } = PipeCenterAlignFittingMode.AskEveryTime;
        public PipeCenterAlignGeometryMode GeometryMode { get; set; } = PipeCenterAlignGeometryMode.WholePipeElevation;
    }

    public static class PipeCenterAlignSettings
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PipeCenterAlignSettingsData));

        private static string SettingsPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "HB_BIM_Tools", "PipeCenterAlignSettings.xml");
            }
        }

        public static bool TryGetCreateFittingChoice(out bool shouldCreateFitting, out PipeCenterAlignConnectionMode connectionMode)
        {
            connectionMode = PipeCenterAlignConnectionMode.Auto;
            PipeCenterAlignSettingsData settings = Load();
            switch (settings.FittingMode)
            {
                case PipeCenterAlignFittingMode.AlwaysCreate:
                    shouldCreateFitting = true;
                    connectionMode = PipeCenterAlignConnectionMode.Auto;
                    return true;

                case PipeCenterAlignFittingMode.AlignOnly:
                    shouldCreateFitting = false;
                    return true;

                default:
                    return AskCreateFitting(out shouldCreateFitting, out connectionMode);
            }
        }

        public static PipeCenterAlignGeometryMode GetGeometryMode()
        {
            return Load().GeometryMode;
        }

        public static bool ShowSettingsDialog(bool showSavedMessage)
        {
            PipeCenterAlignSettingsData settings = Load();
            TaskDialog dialog = new TaskDialog("支管中心對齊設定");
            dialog.MainInstruction = "選擇接頭生成預設行為";
            dialog.MainContent =
                $"目前設定：{GetModeLabel(settings.FittingMode)}\n\n" +
                "此設定會記住在本機使用者資料夾，之後執行支管中心對齊時會自動套用。";
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "每次詢問", "每次對齊前都詢問是否建立三通 / Takeoff / Wye。");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "自動建立接頭", "對齊後直接嘗試建立三通或斜接 Takeoff / Wye。");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "只對齊端點", "只修剪/延伸支管端點，不建立接頭。");
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult result = dialog.Show();
            PipeCenterAlignFittingMode mode;
            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    mode = PipeCenterAlignFittingMode.AskEveryTime;
                    break;
                case TaskDialogResult.CommandLink2:
                    mode = PipeCenterAlignFittingMode.AlwaysCreate;
                    break;
                case TaskDialogResult.CommandLink3:
                    mode = PipeCenterAlignFittingMode.AlignOnly;
                    break;
                default:
                    return false;
            }

            settings.FittingMode = mode;
            Save(settings);

            if (showSavedMessage)
            {
                TaskDialog.Show("支管中心對齊設定", $"已儲存設定：{GetModeLabel(mode)}");
            }

            return true;
        }

        private static bool AskCreateFitting(out bool shouldCreateFitting, out PipeCenterAlignConnectionMode connectionMode)
        {
            shouldCreateFitting = false;
            connectionMode = PipeCenterAlignConnectionMode.Auto;

            while (true)
            {
                TaskDialog dialog = new TaskDialog("支管中心對齊");
                dialog.MainInstruction = "選擇本次對齊後的配管方式";
                dialog.MainContent =
                    "Auto 會依支管與幹管角度判斷：接近 90 度走三通，斜接走 Takeoff/Wye。\n" +
                    "45°垂直翻彎會依高差計算水平退距：run = Abs(dz) / tan(45°)。\n\n" +
                    "可到「支管設定」改成自動建立或只對齊，避免每次詢問。";
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Auto 自動判斷接頭", "90° 建立三通；斜接建立 Takeoff/Wye。");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "90° 三通", "切開幹管並建立 Tee fitting。");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "45° 垂直翻彎對齊", "依高差建立水平段＋45°斜管，並嘗試接入幹管。");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "本次只對齊端點", "只延伸/修剪支管，不建立接頭。");
                dialog.CommonButtons = TaskDialogCommonButtons.Cancel;

                TaskDialogResult result = dialog.Show();
                switch (result)
                {
                    case TaskDialogResult.CommandLink1:
                        shouldCreateFitting = true;
                        connectionMode = PipeCenterAlignConnectionMode.Auto;
                        return true;
                    case TaskDialogResult.CommandLink2:
                        shouldCreateFitting = true;
                        connectionMode = PipeCenterAlignConnectionMode.Tee;
                        return true;
                    case TaskDialogResult.CommandLink3:
                        shouldCreateFitting = true;
                        connectionMode = PipeCenterAlignConnectionMode.Vertical45;
                        return true;
                    case TaskDialogResult.CommandLink4:
                        shouldCreateFitting = false;
                        return true;
                    default:
                        return false;
                }
            }
        }

        private static PipeCenterAlignSettingsData Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new PipeCenterAlignSettingsData();
                }

                using (StreamReader reader = new StreamReader(SettingsPath))
                {
                    return (PipeCenterAlignSettingsData)Serializer.Deserialize(reader) ?? new PipeCenterAlignSettingsData();
                }
            }
            catch
            {
                return new PipeCenterAlignSettingsData();
            }
        }

        private static void Save(PipeCenterAlignSettingsData settings)
        {
            string directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (StreamWriter writer = new StreamWriter(SettingsPath))
            {
                Serializer.Serialize(writer, settings);
            }
        }

        private static string GetModeLabel(PipeCenterAlignFittingMode mode)
        {
            switch (mode)
            {
                case PipeCenterAlignFittingMode.AlwaysCreate:
                    return "自動建立接頭";
                case PipeCenterAlignFittingMode.AlignOnly:
                    return "只對齊端點";
                default:
                    return "每次詢問";
            }
        }
    }
}
