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

                if (IsEndpointConnected(branchPipe, oldMovingPoint))
                {
                    TaskDialog.Show(
                        "支管中心對齊",
                        "選取的支管端點已連接其他管件或管段。\n\n請先斷開該端點，再執行中心對齊，避免破壞既有接頭。");
                    return Result.Cancelled;
                }

                if (!TryGetAlignedEndpoint(mainLine, fixedPoint, oldMovingPoint, out XYZ targetPoint, out string failReason))
                {
                    TaskDialog.Show("支管中心對齊", failReason);
                    return Result.Cancelled;
                }

                double moveDistance = oldMovingPoint.DistanceTo(targetPoint);
                double oneMillimeter = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
                if (!PipeCenterAlignSettings.TryGetCreateFittingChoice(out bool shouldCreateTee))
                {
                    return Result.Cancelled;
                }

                double minimumLength = UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);
                if (moveDistance >= oneMillimeter && targetPoint.DistanceTo(fixedPoint) < minimumLength)
                {
                    TaskDialog.Show("支管中心對齊", "調整後支管長度過短，已取消操作。");
                    return Result.Cancelled;
                }

                if (!shouldCreateTee && moveDistance < oneMillimeter)
                {
                    TaskDialog.Show("支管中心對齊", "支管端點已在幹管中心線上，不需要調整。");
                    return Result.Succeeded;
                }

                if (false && shouldCreateTee && !CanSplitMainPipe(mainLine, targetPoint, out string splitFailReason))
                {
                    TaskDialog.Show("支管中心對齊", splitFailReason);
                    return Result.Cancelled;
                }

                ElementId fittingId = ElementId.InvalidElementId;
                ElementId splitPipeId = ElementId.InvalidElementId;

                using (Transaction tx = new Transaction(doc, shouldCreateTee ? "支管中心對齊並建立三通" : "支管中心對齊"))
                {
                    tx.Start();

                    if (moveDistance >= oneMillimeter)
                    {
                        LocationCurve locationCurve = branchPipe.Location as LocationCurve;
                        Line newLine = movingEndIndex == 0
                            ? Line.CreateBound(targetPoint, fixedPoint)
                            : Line.CreateBound(fixedPoint, targetPoint);

                        locationCurve.Curve = newLine;
                        doc.Regenerate();
                    }

                    if (shouldCreateTee &&
                        !TryCreateFittingAtIntersection(doc, mainPipe, branchPipe, targetPoint, out splitPipeId, out fittingId, out string teeFailReason))
                    {
                        tx.Commit();
                        TaskDialog.Show(
                            "支管中心對齊",
                            teeFailReason + "\n\n已取消本次操作，模型未保留半完成的切管或接頭。");
                        return Result.Succeeded;
                    }

                    tx.Commit();
                }

                double movedMm = UnitUtils.ConvertFromInternalUnits(moveDistance, UnitTypeId.Millimeters);
                string teeSummary = shouldCreateTee
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

        private static Line GetPipeLine(Pipe pipe)
        {
            LocationCurve locationCurve = pipe?.Location as LocationCurve;
            return locationCurve?.Curve as Line;
        }

        private static XYZ GetDirection(Line line)
        {
            return (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
        }

        private static bool IsPlanMainCandidate(Line line)
        {
            XYZ delta = line.GetEndPoint(1) - line.GetEndPoint(0);
            double xyLength = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            return xyLength >= UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);
        }

        private static bool AreParallelInPlan(Line first, Line second)
        {
            XYZ firstDirection = GetPlanDirection(first);
            XYZ secondDirection = GetPlanDirection(second);
            if (firstDirection == null || secondDirection == null)
            {
                return true;
            }

            return Math.Abs(Cross2D(firstDirection, secondDirection)) < ParallelTolerance;
        }

        private static int GetNearestEndIndexToLineInPlan(Line branchLine, Line mainLine)
        {
            double distance0 = DistancePointToSegmentInPlan(branchLine.GetEndPoint(0), mainLine);
            double distance1 = DistancePointToSegmentInPlan(branchLine.GetEndPoint(1), mainLine);
            return distance0 <= distance1 ? 0 : 1;
        }

        private static bool TryGetAlignedEndpoint(Line mainLine, XYZ fixedPoint, XYZ movingPoint, out XYZ targetPoint, out string failReason)
        {
            targetPoint = null;
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

        private static bool CanSplitMainPipe(Line mainLine, XYZ splitPoint, out string failReason)
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

        private static bool TryCreateFittingAtIntersection(
            Document doc,
            Pipe mainPipe,
            Pipe branchPipe,
            XYZ intersectionPoint,
            out ElementId splitPipeId,
            out ElementId fittingId,
            out string failReason)
        {
            splitPipeId = ElementId.InvalidElementId;
            fittingId = ElementId.InvalidElementId;
            failReason = null;

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
                    if (!TryGetThreeConnectorsClosest(
                        mainPipe,
                        splitPipe,
                        branchPipe,
                        out Connector mainConnectorA,
                        out Connector mainConnectorB,
                        out Connector branchConnector,
                        out failReason))
                    {
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
                        fittingId = connectedFitting.Id;
                        doc.Regenerate();
                        teeTx.Commit();
                        return true;
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

            Connector[][] attempts =
            {
                new[] { mainConnectorA, mainConnectorB, branchConnector },
                new[] { mainConnectorB, mainConnectorA, branchConnector },
                new[] { mainConnectorA, branchConnector, mainConnectorB },
                new[] { mainConnectorB, branchConnector, mainConnectorA },
                new[] { branchConnector, mainConnectorA, mainConnectorB },
                new[] { branchConnector, mainConnectorB, mainConnectorA }
            };

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
                            subTx.Commit();
                            tee = candidate;
                            return true;
                        }

                        subTx.RollBack();
                        attemptMessages.Add("Revit 已建立接頭，但未正確連接到兩段幹管與支管。");
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
    }

    public static class PipeCenterAlignSettings
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PipeCenterAlignSettingsData));

        private static string SettingsPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "YD_BIM_Tools", "PipeCenterAlignSettings.xml");
            }
        }

        public static bool TryGetCreateFittingChoice(out bool shouldCreateFitting)
        {
            PipeCenterAlignSettingsData settings = Load();
            switch (settings.FittingMode)
            {
                case PipeCenterAlignFittingMode.AlwaysCreate:
                    shouldCreateFitting = true;
                    return true;

                case PipeCenterAlignFittingMode.AlignOnly:
                    shouldCreateFitting = false;
                    return true;

                default:
                    return AskCreateFitting(out shouldCreateFitting);
            }
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

        private static bool AskCreateFitting(out bool shouldCreateFitting)
        {
            shouldCreateFitting = false;

            while (true)
            {
                TaskDialog dialog = new TaskDialog("支管中心對齊");
                dialog.MainInstruction = "是否在對齊後自動建立接頭？";
                dialog.MainContent =
                    "90 度支管會嘗試建立三通；45 度等斜接支管會嘗試建立 Takeoff/Wye。\n\n" +
                    "可到「支管設定」改成自動建立或只對齊，避免每次詢問。";
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "本次建立接頭");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "本次只對齊端點");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "設定預設行為...");
                dialog.CommonButtons = TaskDialogCommonButtons.Cancel;

                TaskDialogResult result = dialog.Show();
                switch (result)
                {
                    case TaskDialogResult.CommandLink1:
                        shouldCreateFitting = true;
                        return true;
                    case TaskDialogResult.CommandLink2:
                        shouldCreateFitting = false;
                        return true;
                    case TaskDialogResult.CommandLink3:
                        if (!ShowSettingsDialog(false))
                        {
                            return false;
                        }

                        PipeCenterAlignFittingMode mode = Load().FittingMode;
                        if (mode == PipeCenterAlignFittingMode.AlwaysCreate)
                        {
                            shouldCreateFitting = true;
                            return true;
                        }

                        if (mode == PipeCenterAlignFittingMode.AlignOnly)
                        {
                            shouldCreateFitting = false;
                            return true;
                        }

                        continue;
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
