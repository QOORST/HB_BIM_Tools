using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Helpers;
using DrawingColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdDrainBranchConnect : IExternalCommand
    {
        private const double MinimumPipeLengthMm = 20.0;
        private const double MainEndClearanceMm = 50.0;
        private const double TargetBendAngleDegrees = 45.0;
        private const double GeometryTolerance = 1e-9;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            if (doc == null)
            {
                message = "目前沒有開啟中的 Revit 文件。";
                return Result.Failed;
            }

            try
            {
                using (var form = new DrainConnectOptionsForm())
                {
                    if (form.ShowDialog() != WinForms.DialogResult.OK)
                    {
                        return Result.Cancelled;
                    }

                    int successCount = 0;
                    int failureCount = 0;
                    var messages = new List<string>();
                    do
                    {
                        try
                        {
                            Reference branchReference = uiDoc.Selection.PickObject(
                                ObjectType.Element,
                                new PipeSelectionFilter(),
                                "請選取要接入的排水支管；按 ESC 結束連續操作。");
                            Pipe branchPipe = doc.GetElement(branchReference) as Pipe;

                            Reference mainReference = uiDoc.Selection.PickObject(
                                ObjectType.Element,
                                new ExcludingPipeSelectionFilter(branchPipe?.Id),
                                "請選取排水幹管。");
                            Pipe mainPipe = doc.GetElement(mainReference) as Pipe;

                            if (!TryValidateInputs(
                                    branchPipe,
                                    mainPipe,
                                    form.Options,
                                    out Connector branchConnector,
                                    out DrainRoutePlan routePlan,
                                    out string validationError))
                            {
                                failureCount++;
                                messages.Add(validationError);
                                TaskDialog.Show("排水連接", validationError);
                                if (!form.Options.RepeatMode)
                                    return Result.Cancelled;
                                continue;
                            }

                            string resultMessage;
                            using (var transaction = new Transaction(doc, "排水支管連接幹管"))
                            {
                                transaction.Start();
                                if (!TryCreateConnection(
                                        doc,
                                        branchPipe,
                                        mainPipe,
                                        branchConnector,
                                        routePlan,
                                        form.Options,
                                        out resultMessage))
                                {
                                    transaction.RollBack();
                                    failureCount++;
                                    messages.Add(resultMessage);
                                    TaskDialog.Show("排水連接", resultMessage);
                                    if (!form.Options.RepeatMode)
                                        return Result.Cancelled;
                                    continue;
                                }

                                transaction.Commit();
                            }

                            successCount++;
                            messages.Add(resultMessage);
                            if (!form.Options.RepeatMode)
                                TaskDialog.Show("排水連接", resultMessage);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break;
                        }
                    }
                    while (form.Options.RepeatMode);

                    if (form.Options.RepeatMode && (successCount > 0 || failureCount > 0))
                    {
                        string detail = messages.Count > 0
                            ? "\n\n最後結果：\n" + messages.Last()
                            : string.Empty;
                        TaskDialog.Show(
                            "排水連接",
                            $"連續操作完成。\n成功：{successCount}\n失敗：{failureCount}{detail}");
                    }

                    return successCount > 0 ? Result.Succeeded : Result.Cancelled;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("排水連接", $"執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static bool TryValidateInputs(
            Pipe branchPipe,
            Pipe mainPipe,
            DrainConnectOptions options,
            out Connector branchConnector,
            out DrainRoutePlan routePlan,
            out string error)
        {
            branchConnector = null;
            routePlan = null;
            error = string.Empty;

            if (branchPipe == null || mainPipe == null)
            {
                error = "支管或幹管選取無效。";
                return false;
            }

            Line mainLine = GetPipeLine(mainPipe);
            Line branchLine = GetPipeLine(branchPipe);
            if (mainLine == null || branchLine == null)
            {
                error = "目前只支援直線支管與直線幹管。";
                return false;
            }

            branchConnector = GetPipeConnectors(branchPipe)
                .Where(connector => connector.ConnectorType == ConnectorType.End && !connector.IsConnected)
                .OrderBy(connector => DistanceToBoundLine(mainLine, connector.Origin))
                .FirstOrDefault();
            if (branchConnector == null)
            {
                error = "支管沒有可用的未連接端點。請先斷開要接入幹管的一端。";
                return false;
            }

            XYZ extensionDirection = GetOutwardDirection(branchLine, branchConnector.Origin);
            if (extensionDirection == null)
            {
                error = "無法判斷支管開口端的延伸方向。";
                return false;
            }

            double straightLength = UnitUtils.ConvertToInternalUnits(
                options.StraightReserveMm,
                UnitTypeId.Millimeters);
            XYZ bendPoint = branchConnector.Origin + extensionDirection * straightLength;
            double finalStraightLength = options.RoutingMode == DrainRoutingMode.Double45Tee
                ? UnitUtils.ConvertToInternalUnits(options.FinalStraightMm, UnitTypeId.Millimeters)
                : 0.0;
            if (!TryFindConnectionPoint(
                    mainLine,
                    bendPoint,
                    extensionDirection,
                    finalStraightLength,
                    options.EntryDirection,
                    options.MinimumSlopePercent,
                    out XYZ connectionPoint,
                    out XYZ secondBendPoint,
                    out double diagonalSlopePercent))
            {
                string routeName = options.RoutingMode == DrainRoutingMode.Double45Tee
                    ? "直管＋45°彎頭＋斜管＋45°彎頭＋三通"
                    : "直管＋45°彎頭＋Y 型斜接";
                error =
                    $"找不到可建立「{routeName}」的安全接入點。\n" +
                    "請調整直管預留長度、接入方向，或確認幹管位置低於彎頭點且兩端保有足夠空間。";
                return false;
            }

            double minimumLength = UnitUtils.ConvertToInternalUnits(MinimumPipeLengthMm, UnitTypeId.Millimeters);
            double diagonalLength = bendPoint.DistanceTo(connectionPoint);
            if (straightLength < minimumLength || diagonalLength < minimumLength)
            {
                error = $"直管或斜管長度小於 {MinimumPipeLengthMm:0} mm，無法安全建立配件。";
                return false;
            }

            ElementId branchSystemType = GetSystemTypeId(branchPipe);
            ElementId mainSystemType = GetSystemTypeId(mainPipe);
            if (branchSystemType != ElementId.InvalidElementId &&
                mainSystemType != ElementId.InvalidElementId &&
                branchSystemType != mainSystemType)
            {
                error = "支管與幹管的管道系統類型不同。為避免跨系統誤接，工具已停止。";
                return false;
            }

            routePlan = new DrainRoutePlan
            {
                BranchPoint = branchConnector.Origin,
                BendPoint = bendPoint,
                SecondBendPoint = secondBendPoint,
                ConnectionPoint = connectionPoint,
                DiagonalSlopePercent = diagonalSlopePercent
            };
            return true;
        }

        private static bool TryCreateConnection(
            Document doc,
            Pipe branchPipe,
            Pipe mainPipe,
            Connector branchConnector,
            DrainRoutePlan routePlan,
            DrainConnectOptions options,
            out string resultMessage)
        {
            if (options.RoutingMode == DrainRoutingMode.Double45Tee)
            {
                return TryCreateDouble45TeeConnection(
                    doc,
                    branchPipe,
                    mainPipe,
                    branchConnector,
                    routePlan,
                    options,
                    out resultMessage);
            }

            resultMessage = string.Empty;
            ElementId systemTypeId = GetSystemTypeId(branchPipe);
            ElementId pipeTypeId = branchPipe.GetTypeId();
            ElementId levelId = GetReferenceLevelId(branchPipe);
            if (systemTypeId == ElementId.InvalidElementId ||
                pipeTypeId == ElementId.InvalidElementId ||
                levelId == ElementId.InvalidElementId)
            {
                resultMessage = "無法取得支管的系統、管型或參考樓層。";
                return false;
            }

            Pipe straightPipe;
            Pipe diagonalPipe;
            try
            {
                straightPipe = Pipe.Create(
                    doc,
                    systemTypeId,
                    pipeTypeId,
                    levelId,
                    routePlan.BranchPoint,
                    routePlan.BendPoint);
                diagonalPipe = Pipe.Create(
                    doc,
                    systemTypeId,
                    pipeTypeId,
                    levelId,
                    routePlan.BendPoint,
                    routePlan.ConnectionPoint);
                double diameter = GetPipeDiameter(branchPipe);
                ApplyPipeDiameter(straightPipe, diameter);
                ApplyPipeDiameter(diagonalPipe, diameter);
                doc.Regenerate();
            }
            catch (Exception ex)
            {
                resultMessage = $"建立直管或 45°斜管失敗：{ex.Message}";
                return false;
            }

            Connector straightAtBranch = GetNearestConnector(straightPipe, routePlan.BranchPoint, false);
            Connector straightAtBend = GetNearestConnector(straightPipe, routePlan.BendPoint, false);
            Connector diagonalAtBend = GetNearestConnector(diagonalPipe, routePlan.BendPoint, false);
            Connector diagonalAtMain = GetNearestConnector(diagonalPipe, routePlan.ConnectionPoint, false);
            if (!TryConnectCollinear(doc, branchConnector, straightAtBranch))
            {
                resultMessage = "無法連接既有支管與預留直管。";
                return false;
            }

            if (!TryCreateElbow(doc, straightAtBend, diagonalAtBend, out string elbowName))
            {
                resultMessage =
                    "無法在預留直管與斜管之間建立 45°彎頭。\n" +
                    "請確認支管管型的 Routing Preferences 已配置相容的 45°彎頭。";
                return false;
            }

            string fittingName = string.Empty;
            bool fittingCreated = false;
            if (options.ConnectionMode != DrainConnectionMode.Takeoff)
            {
                fittingCreated = TryCreateTee(
                    doc,
                    mainPipe,
                    diagonalPipe,
                    diagonalAtMain,
                    routePlan.ConnectionPoint,
                    out fittingName);
            }

            if (!fittingCreated && options.ConnectionMode != DrainConnectionMode.Wye)
            {
                fittingCreated = TryCreateTakeoff(doc, diagonalAtMain, mainPipe, out fittingName);
            }

            if (!fittingCreated)
            {
                resultMessage =
                    "無法建立幹管分支配件。\n" +
                    "請確認幹管類型的 Routing Preferences 已配置相容的 Wye／斜三通或 Takeoff 配件，且支管與幹管尺寸可配合。";
                return false;
            }

            resultMessage =
                $"排水支管已連接至幹管。\n" +
                $"接法：直管 {options.StraightReserveMm:0} mm＋45°彎頭＋斜接\n" +
                $"斜管坡度：{routePlan.DiagonalSlopePercent:0.###}%\n" +
                $"彎頭：{elbowName}\n" +
                $"分支配件：{fittingName}";
            return true;
        }

        private static bool TryCreateDouble45TeeConnection(
            Document doc,
            Pipe branchPipe,
            Pipe mainPipe,
            Connector branchConnector,
            DrainRoutePlan routePlan,
            DrainConnectOptions options,
            out string resultMessage)
        {
            resultMessage = string.Empty;
            ElementId systemTypeId = GetSystemTypeId(branchPipe);
            ElementId pipeTypeId = branchPipe.GetTypeId();
            ElementId levelId = GetReferenceLevelId(branchPipe);
            if (systemTypeId == ElementId.InvalidElementId ||
                pipeTypeId == ElementId.InvalidElementId ||
                levelId == ElementId.InvalidElementId)
            {
                resultMessage = "無法取得支管的系統、管型或參考樓層。";
                return false;
            }

            Pipe firstStraight;
            Pipe diagonalPipe;
            Pipe finalStraight;
            try
            {
                firstStraight = Pipe.Create(
                    doc,
                    systemTypeId,
                    pipeTypeId,
                    levelId,
                    routePlan.BranchPoint,
                    routePlan.BendPoint);
                diagonalPipe = Pipe.Create(
                    doc,
                    systemTypeId,
                    pipeTypeId,
                    levelId,
                    routePlan.BendPoint,
                    routePlan.SecondBendPoint);
                finalStraight = Pipe.Create(
                    doc,
                    systemTypeId,
                    pipeTypeId,
                    levelId,
                    routePlan.SecondBendPoint,
                    routePlan.ConnectionPoint);

                double diameter = GetPipeDiameter(branchPipe);
                ApplyPipeDiameter(firstStraight, diameter);
                ApplyPipeDiameter(diagonalPipe, diameter);
                ApplyPipeDiameter(finalStraight, diameter);
                doc.Regenerate();
            }
            catch (Exception ex)
            {
                resultMessage = $"建立雙 45°偏移管段失敗：{ex.Message}";
                return false;
            }

            Connector firstAtBranch = GetNearestConnector(firstStraight, routePlan.BranchPoint, false);
            Connector firstAtBend = GetNearestConnector(firstStraight, routePlan.BendPoint, false);
            Connector diagonalAtFirstBend = GetNearestConnector(diagonalPipe, routePlan.BendPoint, false);
            Connector diagonalAtSecondBend = GetNearestConnector(diagonalPipe, routePlan.SecondBendPoint, false);
            Connector finalAtBend = GetNearestConnector(finalStraight, routePlan.SecondBendPoint, false);
            Connector finalAtMain = GetNearestConnector(finalStraight, routePlan.ConnectionPoint, false);

            if (!TryConnectCollinear(doc, branchConnector, firstAtBranch))
            {
                resultMessage = "無法連接既有支管與預留直管。";
                return false;
            }

            if (!TryCreateElbow(doc, firstAtBend, diagonalAtFirstBend, out string firstElbowName))
            {
                resultMessage = "無法建立第一個 45°彎頭，請檢查支管管型的 Routing Preferences。";
                return false;
            }

            if (!TryCreateElbow(doc, diagonalAtSecondBend, finalAtBend, out string secondElbowName))
            {
                resultMessage = "無法建立第二個 45°彎頭，請檢查支管管型的 Routing Preferences。";
                return false;
            }

            if (!TryCreateTee(
                    doc,
                    mainPipe,
                    finalStraight,
                    finalAtMain,
                    routePlan.ConnectionPoint,
                    out string teeName))
            {
                resultMessage =
                    "無法切開幹管建立三通。\n" +
                    "請確認末端直管與幹管角度、管徑及 Routing Preferences 中的三通配件相容。";
                return false;
            }

            resultMessage =
                "排水支管已連接至幹管。\n" +
                $"接法：直管 {options.StraightReserveMm:0} mm＋雙 45°偏移＋末端直管 {options.FinalStraightMm:0} mm＋三通\n" +
                $"斜管坡度：{routePlan.DiagonalSlopePercent:0.###}%\n" +
                $"第一彎頭：{firstElbowName}\n" +
                $"第二彎頭：{secondElbowName}\n" +
                $"分支配件：{teeName}";
            return true;
        }

        private static bool TryConnectCollinear(Document doc, Connector existing, Connector created)
        {
            if (existing == null || created == null)
            {
                return false;
            }

            try
            {
                existing.ConnectTo(created);
                doc.Regenerate();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCreateElbow(
            Document doc,
            Connector first,
            Connector second,
            out string fittingName)
        {
            fittingName = string.Empty;
            try
            {
                FamilyInstance elbow = doc.Create.NewElbowFitting(first, second);
                doc.Regenerate();
                if (elbow == null || !elbow.IsValidObject)
                {
                    return false;
                }

                fittingName = elbow.Name;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFindConnectionPoint(
            Line mainLine,
            XYZ bendPoint,
            XYZ extensionDirection,
            double finalStraightLength,
            DrainEntryDirection entryDirection,
            double minimumSlopePercent,
            out XYZ connectionPoint,
            out XYZ secondBendPoint,
            out double slopePercent)
        {
            connectionPoint = null;
            secondBendPoint = null;
            slopePercent = 0.0;

            XYZ mainStart = mainLine.GetEndPoint(0);
            XYZ mainDirection = (mainLine.GetEndPoint(1) - mainStart).Normalize();
            XYZ shiftedMainStart = mainStart - extensionDirection * finalStraightLength;
            XYZ offset = shiftedMainStart - bendPoint;
            double cosineSquared = Math.Pow(
                Math.Cos(TargetBendAngleDegrees * Math.PI / 180.0),
                2.0);
            double mainDotExtension = mainDirection.DotProduct(extensionDirection);
            double offsetDotExtension = offset.DotProduct(extensionDirection);
            double a = mainDotExtension * mainDotExtension - cosineSquared;
            double b = 2.0 * (
                offsetDotExtension * mainDotExtension -
                cosineSquared * offset.DotProduct(mainDirection));
            double c =
                offsetDotExtension * offsetDotExtension -
                cosineSquared * offset.DotProduct(offset);

            var stations = new List<double>();
            if (Math.Abs(a) < GeometryTolerance)
            {
                if (Math.Abs(b) > GeometryTolerance)
                {
                    stations.Add(-c / b);
                }
            }
            else
            {
                double discriminant = b * b - 4.0 * a * c;
                if (discriminant >= -GeometryTolerance)
                {
                    double root = Math.Sqrt(Math.Max(0.0, discriminant));
                    stations.Add((-b - root) / (2.0 * a));
                    stations.Add((-b + root) / (2.0 * a));
                }
            }

            IntersectionResult projection = mainLine.Project(bendPoint);
            double projectedStation = projection == null
                ? 0.0
                : (projection.XYZPoint - mainStart).DotProduct(mainDirection);
            double endClearance = UnitUtils.ConvertToInternalUnits(
                MainEndClearanceMm,
                UnitTypeId.Millimeters);
            double minimumLength = UnitUtils.ConvertToInternalUnits(
                MinimumPipeLengthMm,
                UnitTypeId.Millimeters);

            var candidates = new List<DrainConnectionCandidate>();
            foreach (double station in stations.Distinct())
            {
                if (station <= endClearance || station >= mainLine.Length - endClearance)
                {
                    continue;
                }

                if (entryDirection == DrainEntryDirection.Positive &&
                    station <= projectedStation + GeometryTolerance)
                {
                    continue;
                }

                if (entryDirection == DrainEntryDirection.Negative &&
                    station >= projectedStation - GeometryTolerance)
                {
                    continue;
                }

                XYZ point = mainStart + mainDirection * station;
                XYZ candidateSecondBend = point - extensionDirection * finalStraightLength;
                XYZ diagonal = candidateSecondBend - bendPoint;
                if (diagonal.GetLength() < minimumLength ||
                    diagonal.DotProduct(extensionDirection) <= GeometryTolerance)
                {
                    continue;
                }

                double horizontalDistance = HorizontalDistance(bendPoint, candidateSecondBend);
                double elevationDrop = bendPoint.Z - candidateSecondBend.Z;
                if (horizontalDistance < minimumLength || elevationDrop <= GeometryTolerance)
                {
                    continue;
                }

                double candidateSlope = elevationDrop / horizontalDistance * 100.0;
                if (candidateSlope + 1e-6 < minimumSlopePercent)
                {
                    continue;
                }

                candidates.Add(new DrainConnectionCandidate
                {
                    Point = point,
                    SecondBendPoint = candidateSecondBend,
                    SlopePercent = candidateSlope,
                    DistanceFromProjection = Math.Abs(station - projectedStation)
                });
            }

            DrainConnectionCandidate selected = candidates
                .OrderBy(candidate => candidate.DistanceFromProjection)
                .FirstOrDefault();
            if (selected == null)
            {
                return false;
            }

            connectionPoint = selected.Point;
            secondBendPoint = selected.SecondBendPoint;
            slopePercent = selected.SlopePercent;
            return true;
        }

        private static XYZ GetOutwardDirection(Line branchLine, XYZ connectorOrigin)
        {
            XYZ start = branchLine.GetEndPoint(0);
            XYZ end = branchLine.GetEndPoint(1);
            if (connectorOrigin.DistanceTo(start) <= connectorOrigin.DistanceTo(end))
            {
                return (start - end).Normalize();
            }

            return (end - start).Normalize();
        }

        private static bool TryCreateTakeoff(
            Document doc,
            Connector branchConnector,
            Pipe mainPipe,
            out string fittingName)
        {
            fittingName = string.Empty;
            using (var subTransaction = new SubTransaction(doc))
            {
                subTransaction.Start();
                try
                {
                    FamilyInstance fitting = doc.Create.NewTakeoffFitting(branchConnector, mainPipe);
                    doc.Regenerate();
                    if (fitting == null || !fitting.IsValidObject)
                    {
                        subTransaction.RollBack();
                        return false;
                    }

                    fittingName = fitting.Name;
                    subTransaction.Commit();
                    return true;
                }
                catch
                {
                    subTransaction.RollBack();
                    return false;
                }
            }
        }

        private static bool TryCreateTee(
            Document doc,
            Pipe mainPipe,
            Pipe branchPipe,
            Connector branchConnector,
            XYZ connectionPoint,
            out string fittingName)
        {
            fittingName = string.Empty;
            using (var subTransaction = new SubTransaction(doc))
            {
                subTransaction.Start();
                try
                {
                    ElementId newSegmentId = PlumbingUtils.BreakCurve(doc, mainPipe.Id, connectionPoint);
                    doc.Regenerate();
                    Pipe newMainSegment = doc.GetElement(newSegmentId) as Pipe;
                    if (newMainSegment == null)
                    {
                        subTransaction.RollBack();
                        return false;
                    }

                    Connector mainA = GetNearestConnector(mainPipe, connectionPoint, true);
                    Connector mainB = GetNearestConnector(newMainSegment, connectionPoint, true);
                    Connector branch = branchConnector ?? GetNearestConnector(branchPipe, connectionPoint, true);
                    if (mainA == null || mainB == null || branch == null)
                    {
                        subTransaction.RollBack();
                        return false;
                    }

                    FamilyInstance fitting = doc.Create.NewTeeFitting(mainA, mainB, branch);
                    doc.Regenerate();
                    if (fitting == null || !fitting.IsValidObject)
                    {
                        subTransaction.RollBack();
                        return false;
                    }

                    fittingName = fitting.Name;
                    subTransaction.Commit();
                    return true;
                }
                catch
                {
                    subTransaction.RollBack();
                    return false;
                }
            }
        }

        private static Connector GetNearestConnector(Pipe pipe, XYZ point, bool preferOpen)
        {
            IEnumerable<Connector> connectors = GetPipeConnectors(pipe)
                .Where(connector => connector.ConnectorType == ConnectorType.End);
            if (preferOpen)
            {
                Connector open = connectors
                    .Where(connector => !connector.IsConnected)
                    .OrderBy(connector => connector.Origin.DistanceTo(point))
                    .FirstOrDefault();
                if (open != null)
                {
                    return open;
                }
            }

            return connectors
                .OrderBy(connector => connector.Origin.DistanceTo(point))
                .FirstOrDefault();
        }

        private static IEnumerable<Connector> GetPipeConnectors(Pipe pipe)
        {
            return pipe?.ConnectorManager?.Connectors?.Cast<Connector>()
                ?? Enumerable.Empty<Connector>();
        }

        private static Line GetPipeLine(Pipe pipe)
        {
            return (pipe?.Location as LocationCurve)?.Curve as Line;
        }

        private static double DistanceToBoundLine(Line line, XYZ point)
        {
            IntersectionResult result = line.Project(point);
            return result?.XYZPoint.DistanceTo(point) ?? double.MaxValue;
        }

        private static double HorizontalDistance(XYZ first, XYZ second)
        {
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static ElementId GetReferenceLevelId(Pipe pipe)
        {
            Parameter parameter = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            return parameter?.AsElementId() ?? ElementId.InvalidElementId;
        }

        private static ElementId GetSystemTypeId(Pipe pipe)
        {
            Parameter parameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId id = parameter?.AsElementId() ?? ElementId.InvalidElementId;
            return id != ElementId.InvalidElementId
                ? id
                : pipe?.MEPSystem?.GetTypeId() ?? ElementId.InvalidElementId;
        }

        private static double GetPipeDiameter(Pipe pipe)
        {
            return pipe?
                .get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?
                .AsDouble() ?? 0.0;
        }

        private static void ApplyPipeDiameter(Pipe pipe, double diameter)
        {
            Parameter parameter = pipe?
                .get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (parameter != null && !parameter.IsReadOnly && diameter > 1e-9)
            {
                parameter.Set(diameter);
            }
        }

        private sealed class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                return element is Pipe;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }

        private sealed class ExcludingPipeSelectionFilter : ISelectionFilter
        {
            private readonly long _excludedId;

            public ExcludingPipeSelectionFilter(ElementId excludedId)
            {
                _excludedId = excludedId?.GetIdValue() ?? long.MinValue;
            }

            public bool AllowElement(Element element)
            {
                return element is Pipe && element.Id.GetIdValue() != _excludedId;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }

    internal enum DrainConnectionMode
    {
        Auto,
        Wye,
        Takeoff
    }

    internal enum DrainRoutingMode
    {
        Double45Tee,
        Single45Wye
    }

    internal enum DrainEntryDirection
    {
        Auto,
        Positive,
        Negative
    }

    internal sealed class DrainConnectOptions
    {
        public double MinimumSlopePercent { get; set; } = 1.0;
        public double StraightReserveMm { get; set; } = 300.0;
        public double FinalStraightMm { get; set; } = 150.0;
        public DrainRoutingMode RoutingMode { get; set; } = DrainRoutingMode.Double45Tee;
        public DrainConnectionMode ConnectionMode { get; set; } = DrainConnectionMode.Auto;
        public DrainEntryDirection EntryDirection { get; set; } = DrainEntryDirection.Auto;
        public bool RepeatMode { get; set; }
    }

    internal sealed class DrainRoutePlan
    {
        public XYZ BranchPoint { get; set; }
        public XYZ BendPoint { get; set; }
        public XYZ SecondBendPoint { get; set; }
        public XYZ ConnectionPoint { get; set; }
        public double DiagonalSlopePercent { get; set; }
    }

    internal sealed class DrainConnectionCandidate
    {
        public XYZ Point { get; set; }
        public XYZ SecondBendPoint { get; set; }
        public double SlopePercent { get; set; }
        public double DistanceFromProjection { get; set; }
    }

    internal sealed class DrainConnectOptionsForm : WinForms.Form
    {
        private readonly WinForms.NumericUpDown _slope;
        private readonly WinForms.NumericUpDown _straightReserve;
        private readonly WinForms.NumericUpDown _finalStraight;
        private readonly WinForms.ComboBox _routingMode;
        private readonly WinForms.ComboBox _mode;
        private readonly WinForms.ComboBox _direction;
        private readonly WinForms.CheckBox _repeatMode;

        public DrainConnectOptionsForm()
        {
            Text = "排水連接設定";
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            Width = 560;
            Height = 500;
            MinimumSize = new Size(560, 500);
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            Font = new Font("Microsoft JhengHei UI", 10f);

            var root = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                Padding = new WinForms.Padding(18),
                ColumnCount = 2,
                RowCount = 10
            };
            root.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 55));
            root.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 45));
            Controls.Add(root);

            _repeatMode = new WinForms.CheckBox
            {
                Text = "連續處理多組支管（按 ESC 結束）",
                AutoSize = true,
                Checked = true,
                Margin = new WinForms.Padding(0, 10, 0, 4)
            };
            root.Controls.Add(_repeatMode, 0, 7);
            root.SetColumnSpan(_repeatMode, 2);

            root.Controls.Add(new WinForms.Label
            {
                Text = "排水支管連接幹管",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 15f, FontStyle.Bold),
                ForeColor = DrawingColor.FromArgb(0, 66, 96),
                Margin = new WinForms.Padding(0, 0, 0, 12)
            }, 0, 0);
            root.SetColumnSpan(root.GetControlFromPosition(0, 0), 2);

            root.Controls.Add(new WinForms.Label
            {
                Text = "最低允許坡度 (%)",
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left
            }, 0, 1);
            _slope = new WinForms.NumericUpDown
            {
                DecimalPlaces = 2,
                Minimum = 0.1M,
                Maximum = 20M,
                Increment = 0.1M,
                Value = 1M,
                Dock = WinForms.DockStyle.Fill
            };
            root.Controls.Add(_slope, 1, 1);

            root.Controls.Add(new WinForms.Label
            {
                Text = "配管方式",
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left
            }, 0, 2);
            _routingMode = new WinForms.ComboBox
            {
                DropDownStyle = WinForms.ComboBoxStyle.DropDownList,
                Dock = WinForms.DockStyle.Fill
            };
            _routingMode.Items.AddRange(new object[]
            {
                "雙 45°偏移＋三通（建議）",
                "單 45°＋Y 型斜接"
            });
            _routingMode.SelectedIndex = 0;
            root.Controls.Add(_routingMode, 1, 2);

            root.Controls.Add(new WinForms.Label
            {
                Text = "前端直管長度 (mm)",
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left
            }, 0, 3);
            _straightReserve = new WinForms.NumericUpDown
            {
                DecimalPlaces = 0,
                Minimum = 20M,
                Maximum = 5000M,
                Increment = 50M,
                Value = 300M,
                Dock = WinForms.DockStyle.Fill
            };
            root.Controls.Add(_straightReserve, 1, 3);

            root.Controls.Add(new WinForms.Label
            {
                Text = "末端直管長度 (mm)",
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left
            }, 0, 4);
            _finalStraight = new WinForms.NumericUpDown
            {
                DecimalPlaces = 0,
                Minimum = 20M,
                Maximum = 5000M,
                Increment = 50M,
                Value = 150M,
                Dock = WinForms.DockStyle.Fill
            };
            root.Controls.Add(_finalStraight, 1, 4);

            root.Controls.Add(new WinForms.Label
            {
                Text = "幹管接入方向",
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left
            }, 0, 5);
            _direction = new WinForms.ComboBox
            {
                DropDownStyle = WinForms.ComboBoxStyle.DropDownList,
                Dock = WinForms.DockStyle.Fill
            };
            _direction.Items.AddRange(new object[]
            {
                "自動（選擇最近的有效方向）",
                "沿幹管繪製方向",
                "逆幹管繪製方向"
            });
            _direction.SelectedIndex = 0;
            root.Controls.Add(_direction, 1, 5);

            root.Controls.Add(new WinForms.Label
            {
                Text = "幹管接頭方式",
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left
            }, 0, 6);
            _mode = new WinForms.ComboBox
            {
                DropDownStyle = WinForms.ComboBoxStyle.DropDownList,
                Dock = WinForms.DockStyle.Fill
            };
            _mode.Items.AddRange(new object[]
            {
                "自動（優先 Y 型斜接，失敗改 Takeoff）",
                "只使用 Y 型／斜三通",
                "只使用 Takeoff"
            });
            _mode.SelectedIndex = 0;
            root.Controls.Add(_mode, 1, 6);
            Action updateModeControls = () =>
            {
                bool double45 = _routingMode.SelectedIndex == (int)DrainRoutingMode.Double45Tee;
                _finalStraight.Enabled = double45;
                _mode.Enabled = !double45;
            };
            _routingMode.SelectedIndexChanged += (_, _) => updateModeControls();
            updateModeControls();

            root.Controls.Add(new WinForms.Label
            {
                Text = "雙 45°模式以兩個 45°彎頭形成平行偏移，末端直管切入幹管三通；單 45°模式則直接以 Y 型／Takeoff 斜接。",
                AutoSize = true,
                ForeColor = DrawingColor.FromArgb(80, 90, 100),
                Margin = new WinForms.Padding(0, 14, 0, 8)
            }, 0, 8);
            root.SetColumnSpan(root.GetControlFromPosition(0, 8), 2);

            var buttons = new WinForms.FlowLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                FlowDirection = WinForms.FlowDirection.RightToLeft,
                AutoSize = true
            };
            var cancel = new WinForms.Button
            {
                Text = "取消",
                Width = 96,
                Height = 34,
                DialogResult = WinForms.DialogResult.Cancel
            };
            var ok = new WinForms.Button
            {
                Text = "開始",
                Width = 110,
                Height = 34
            };
            ok.Click += (_, _) =>
            {
                Options = new DrainConnectOptions
                {
                    MinimumSlopePercent = (double)_slope.Value,
                    StraightReserveMm = (double)_straightReserve.Value,
                    FinalStraightMm = (double)_finalStraight.Value,
                    RoutingMode = (DrainRoutingMode)_routingMode.SelectedIndex,
                    ConnectionMode = (DrainConnectionMode)_mode.SelectedIndex,
                    EntryDirection = (DrainEntryDirection)_direction.SelectedIndex,
                    RepeatMode = _repeatMode.Checked
                };
                DialogResult = WinForms.DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            root.Controls.Add(buttons, 0, 9);
            root.SetColumnSpan(buttons, 2);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public DrainConnectOptions Options { get; private set; } = new DrainConnectOptions();
    }
}
