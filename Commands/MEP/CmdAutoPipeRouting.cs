using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoPipeRouting.UI;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoPipeRouting : IExternalCommand
    {
        private const double MinCreateLengthMm = 10.0;
        private const double TeePointToleranceMm = 3.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                MainWindow win = new MainWindow();
                if (win.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                AutoPipeRoutingOptions options = win.Options;
                if (!TryPickInputs(uiDoc, doc, out Pipe mainPipe, out List<Element> sourceElements))
                {
                    return Result.Cancelled;
                }

                Line mainLine = GetPipeLine(mainPipe);
                if (mainLine == null)
                {
                    TaskDialog.Show("自動配管", "無法取得水平幹管中心線。");
                    return Result.Cancelled;
                }

                XYZ mainDir = (mainLine.GetEndPoint(1) - mainLine.GetEndPoint(0)).Normalize();
                List<RouteTask> tasks = BuildTasks(mainLine, mainDir, sourceElements);
                if (!tasks.Any())
                {
                    TaskDialog.Show("自動配管", "沒有可處理的設備或支管 Connector。");
                    return Result.Cancelled;
                }

                int successCount = 0;
                List<string> fails = new List<string>();
                List<Pipe> mainSegments = new List<Pipe> { mainPipe };

                using (Transaction tx = new Transaction(doc, "MEP 自動配管"))
                {
                    tx.Start();

                    foreach (RouteTask task in tasks.OrderBy(t => t.Station))
                    {
                        Pipe targetMain = FindMainSegmentAtPoint(mainSegments, task.ConnectOnMain) ?? mainPipe;
                        if (TryRouteOne(doc, targetMain, mainSegments, task, options, out string fail))
                        {
                            successCount++;
                        }
                        else
                        {
                            fails.Add($"{task.SourceLabel} ID {task.SourceElement.Id.GetIdValue()}: {fail}");
                        }
                    }

                    tx.Commit();
                }

                string summary =
                    $"完成 {successCount}/{tasks.Count} 組自動配管。" +
                    (fails.Count == 0 ? "\n\n全部完成。" : "\n\n失敗項目：\n" + string.Join("\n", fails.Take(12)));
                TaskDialog.Show("自動配管", summary);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("自動配管", $"執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static bool TryPickInputs(UIDocument uiDoc, Document doc, out Pipe mainPipe, out List<Element> branchElements)
        {
            mainPipe = null;
            branchElements = new List<Element>();

            IList<Reference> refs = uiDoc.Selection.PickObjects(
                ObjectType.Element,
                new RouteSourceSelectionFilter(),
                "請先選取設備或支管，可複選，完成後再選水平幹管");

            branchElements = refs
                .Select(r => doc.GetElement(r))
                .Where(e => e != null)
                .Distinct(new ElementIdComparer())
                .ToList();

            if (!branchElements.Any())
            {
                return false;
            }

            Reference mainRef = uiDoc.Selection.PickObject(
                ObjectType.Element,
                new MainPipeSelectionFilter(branchElements.Select(e => e.Id)),
                "請選取要接入的水平幹管");

            mainPipe = doc.GetElement(mainRef) as Pipe;
            return mainPipe != null;
        }

        private static List<RouteTask> BuildTasks(Line mainLine, XYZ mainDir, List<Element> sourceElements)
        {
            List<RouteTask> tasks = new List<RouteTask>();
            XYZ origin = mainLine.GetEndPoint(0);

            foreach (Element source in sourceElements)
            {
                Connector connector = GetNearestRouteConnector(source, mainLine);
                if (connector == null)
                {
                    continue;
                }

                XYZ start = connector.Origin;
                XYZ onMain = ProjectPointToLine(mainLine, start);
                double station = (onMain - origin).DotProduct(mainDir);

                tasks.Add(new RouteTask
                {
                    SourceElement = source,
                    SourceConnector = connector,
                    ConnectOnMain = onMain,
                    Station = station,
                    SourceLabel = source is Pipe ? "支管" : "設備"
                });
            }

            return tasks;
        }

        private static bool TryRouteOne(
            Document doc,
            Pipe mainPipe,
            List<Pipe> mainSegments,
            RouteTask task,
            AutoPipeRoutingOptions options,
            out string failReason)
        {
            failReason = string.Empty;

            ElementId systemTypeId = GetSystemTypeId(mainPipe);
            ElementId pipeTypeId = mainPipe.GetTypeId();
            ElementId levelId = GetReferenceLevelId(mainPipe);
            double routeDiameter = GetRouteDiameter(task.SourceConnector, task.SourceElement, mainPipe);
            if (systemTypeId == ElementId.InvalidElementId || pipeTypeId == ElementId.InvalidElementId || levelId == ElementId.InvalidElementId)
            {
                failReason = "無法取得幹管的系統類型、管類型或樓層。";
                return false;
            }

            XYZ start = task.SourceConnector.Origin;
            XYZ onMain = task.ConnectOnMain;
            IList<XYZ> routePoints = BuildRoutePoints(mainPipe, task, options);
            double minLen = UnitUtils.ConvertToInternalUnits(MinCreateLengthMm, UnitTypeId.Millimeters);

            List<Pipe> createdPipes = new List<Pipe>();
            Pipe branchNearMain;
            Connector branchConnectorAtMain;

            try
            {
                if (!TryCreateRoutePipes(doc, systemTypeId, pipeTypeId, levelId, routeDiameter, routePoints, minLen, createdPipes, out string createFail))
                {
                    failReason = createFail;
                    SafeDelete(doc, createdPipes);
                    return false;
                }

                doc.Regenerate();

                if (!TryConnectPipeChain(doc, createdPipes, routePoints, task.SourceConnector, out string connectFail))
                {
                    failReason = connectFail;
                    SafeDelete(doc, createdPipes);
                    return false;
                }

                branchNearMain = createdPipes.Last();
                branchConnectorAtMain = GetNearestOpenEndConnector(branchNearMain, onMain) ?? GetNearestEndConnector(branchNearMain, onMain);

                if (branchConnectorAtMain == null)
                {
                    failReason = "找不到靠近幹管的 Connector。";
                    SafeDelete(doc, createdPipes);
                    return false;
                }

                if (TryCreateTakeoff(doc, branchConnectorAtMain, mainPipe))
                {
                    return true;
                }

                if (TryCreateTeeByBreakingMain(doc, mainPipe, mainSegments, branchNearMain, branchConnectorAtMain, onMain, out failReason))
                {
                    return true;
                }

                SafeDelete(doc, createdPipes);
                return false;
            }
            catch (Exception ex)
            {
                SafeDelete(doc, createdPipes);
                failReason = ex.Message;
                return false;
            }
        }

        private static IList<XYZ> BuildRoutePoints(Pipe mainPipe, RouteTask task, AutoPipeRoutingOptions options)
        {
            List<XYZ> points = new List<XYZ>();
            XYZ start = task.SourceConnector.Origin;
            XYZ onMain = task.ConnectOnMain;

            points.Add(start);

            if (!(task.SourceElement is Pipe) && TryGetConnectorDirection(task.SourceConnector, onMain, out XYZ connectorDirection))
            {
                XYZ leadPoint = CalculateEquipmentLeadPoint(start, onMain, connectorDirection, mainPipe, options);
                if (leadPoint.DistanceTo(start) > UnitUtils.ConvertToInternalUnits(MinCreateLengthMm, UnitTypeId.Millimeters))
                {
                    points.Add(leadPoint);
                }
            }

            XYZ routeBase = points.Last();
            XYZ offsetPoint = CalculateOffsetPoint(routeBase, onMain, options);
            if (offsetPoint.DistanceTo(routeBase) > UnitUtils.ConvertToInternalUnits(MinCreateLengthMm, UnitTypeId.Millimeters)
                && offsetPoint.DistanceTo(onMain) > UnitUtils.ConvertToInternalUnits(MinCreateLengthMm, UnitTypeId.Millimeters))
            {
                points.Add(offsetPoint);
            }

            points.Add(onMain);
            return RemoveShortDuplicatePoints(points);
        }

        private static XYZ CalculateEquipmentLeadPoint(
            XYZ start,
            XYZ onMain,
            XYZ connectorDirection,
            Pipe mainPipe,
            AutoPipeRoutingOptions options)
        {
            XYZ direction = connectorDirection.Normalize();
            double minLen = UnitUtils.ConvertToInternalUnits(MinCreateLengthMm, UnitTypeId.Millimeters);

            if (Math.Abs(direction.Z) > 0.7 && options.SlopeDenominator > 0.0)
            {
                double targetZ = GetSlopedZ(start, onMain, options);
                double dz = targetZ - start.Z;
                if (Math.Abs(dz) > minLen && Math.Sign(dz) == Math.Sign(direction.Z))
                {
                    return new XYZ(start.X, start.Y, targetZ);
                }
            }

            return start + direction.Multiply(GetEquipmentLeadLength(mainPipe));
        }

        private static IList<XYZ> RemoveShortDuplicatePoints(IList<XYZ> points)
        {
            double minLen = UnitUtils.ConvertToInternalUnits(MinCreateLengthMm, UnitTypeId.Millimeters);
            List<XYZ> cleaned = new List<XYZ>();
            foreach (XYZ point in points)
            {
                if (!cleaned.Any() || cleaned.Last().DistanceTo(point) >= minLen)
                {
                    cleaned.Add(point);
                }
            }

            if (cleaned.Count >= 2 && cleaned.Last().DistanceTo(points.Last()) >= minLen)
            {
                cleaned.Add(points.Last());
            }

            return cleaned;
        }

        private static bool TryCreateRoutePipes(
            Document doc,
            ElementId systemTypeId,
            ElementId pipeTypeId,
            ElementId levelId,
            double routeDiameter,
            IList<XYZ> routePoints,
            double minLen,
            List<Pipe> createdPipes,
            out string failReason)
        {
            failReason = string.Empty;

            if (routePoints == null || routePoints.Count < 2)
            {
                failReason = "配管路徑點不足，無法建立管段。";
                return false;
            }

            for (int i = 0; i < routePoints.Count - 1; i++)
            {
                XYZ a = routePoints[i];
                XYZ b = routePoints[i + 1];
                if (a.DistanceTo(b) < minLen)
                {
                    continue;
                }

                Pipe pipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, a, b);
                ApplyPipeDiameter(pipe, routeDiameter);
                createdPipes.Add(pipe);
            }

            if (!createdPipes.Any())
            {
                failReason = "配管路徑太短，沒有建立任何管段。";
                return false;
            }

            return true;
        }

        private static bool TryConnectPipeChain(
            Document doc,
            IList<Pipe> pipes,
            IList<XYZ> routePoints,
            Connector sourceConnector,
            out string failReason)
        {
            failReason = string.Empty;
            if (pipes == null || pipes.Count == 0)
            {
                failReason = "沒有可連接的管段。";
                return false;
            }

            Pipe firstPipe = pipes.First();
            Connector firstStart = GetNearestOpenEndConnector(firstPipe, routePoints.First()) ?? GetNearestEndConnector(firstPipe, routePoints.First());
            if (!TryConnect(doc, sourceConnector, firstStart))
            {
                failReason = "無法連接設備/支管端 Connector。";
                return false;
            }

            for (int i = 0; i < pipes.Count - 1; i++)
            {
                XYZ joint = GetSharedRoutePoint(pipes[i], pipes[i + 1], routePoints);
                Connector a = GetNearestOpenEndConnector(pipes[i], joint) ?? GetNearestEndConnector(pipes[i], joint);
                Connector b = GetNearestOpenEndConnector(pipes[i + 1], joint) ?? GetNearestEndConnector(pipes[i + 1], joint);
                if (!TryConnect(doc, a, b))
                {
                    failReason = "無法建立中間轉折管件。";
                    return false;
                }
            }

            return true;
        }

        private static XYZ GetSharedRoutePoint(Pipe first, Pipe second, IList<XYZ> routePoints)
        {
            Connector bestFirst = null;
            Connector bestSecond = null;
            double bestDistance = double.MaxValue;

            foreach (Connector a in GetPipeConnectors(first).Where(c => c.ConnectorType == ConnectorType.End))
            {
                foreach (Connector b in GetPipeConnectors(second).Where(c => c.ConnectorType == ConnectorType.End))
                {
                    double distance = a.Origin.DistanceTo(b.Origin);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestFirst = a;
                        bestSecond = b;
                    }
                }
            }

            if (bestFirst != null && bestSecond != null)
            {
                return new XYZ(
                    (bestFirst.Origin.X + bestSecond.Origin.X) / 2.0,
                    (bestFirst.Origin.Y + bestSecond.Origin.Y) / 2.0,
                    (bestFirst.Origin.Z + bestSecond.Origin.Z) / 2.0);
            }

            return routePoints.Count > 1 ? routePoints[routePoints.Count - 2] : routePoints.Last();
        }

        private static bool TryGetConnectorDirection(Connector connector, XYZ targetPoint, out XYZ direction)
        {
            direction = null;
            try
            {
                XYZ basis = connector.CoordinateSystem?.BasisZ;
                if (basis == null || basis.GetLength() < 1e-9)
                {
                    return false;
                }

                direction = basis.Normalize();
                double testLength = UnitUtils.ConvertToInternalUnits(100.0, UnitTypeId.Millimeters);
                if ((connector.Origin + direction.Multiply(testLength)).DistanceTo(targetPoint)
                    > (connector.Origin - direction.Multiply(testLength)).DistanceTo(targetPoint))
                {
                    direction = direction.Negate();
                }

                return true;
            }
            catch
            {
                direction = null;
                return false;
            }
        }

        private static double GetEquipmentLeadLength(Pipe mainPipe)
        {
            double defaultLength = UnitUtils.ConvertToInternalUnits(150.0, UnitTypeId.Millimeters);
            try
            {
                Parameter diameter = mainPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                double value = diameter?.AsDouble() ?? 0.0;
                if (value > 1e-9)
                {
                    return Math.Max(defaultLength, value * 2.0);
                }
            }
            catch
            {
                // Use default lead length.
            }

            return defaultLength;
        }

        private static XYZ CalculateOffsetPoint(XYZ start, XYZ onMain, AutoPipeRoutingOptions options)
        {
            XYZ horizontal = new XYZ(onMain.X - start.X, onMain.Y - start.Y, 0.0);
            if (horizontal.GetLength() < 1e-9)
            {
                return onMain;
            }

            double distanceFt = UnitUtils.ConvertToInternalUnits(Math.Max(0.0, options.DistancePipeMm), UnitTypeId.Millimeters);
            if (distanceFt <= 1e-9)
            {
                return onMain;
            }

            XYZ unit = horizontal.Normalize();
            XYZ offset = onMain - unit.Multiply(distanceFt);

            return new XYZ(offset.X, offset.Y, GetSlopedZ(offset, onMain, options));
        }

        private static double GetSlopedZ(XYZ point, XYZ onMain, AutoPipeRoutingOptions options)
        {
            if (options.SlopeDenominator <= 0.0)
            {
                return point.Z;
            }

            double horizontalDistance = new XYZ(onMain.X - point.X, onMain.Y - point.Y, 0.0).GetLength();
            return onMain.Z + horizontalDistance / options.SlopeDenominator;
        }

        private static double GetRouteDiameter(Connector sourceConnector, Element sourceElement, Pipe mainPipe)
        {
            double connectorDiameter = GetConnectorDiameter(sourceConnector);
            if (connectorDiameter > 1e-9)
            {
                return connectorDiameter;
            }

            if (sourceElement is Pipe sourcePipe)
            {
                double sourceDiameter = GetPipeDiameter(sourcePipe);
                if (sourceDiameter > 1e-9)
                {
                    return sourceDiameter;
                }
            }

            return GetPipeDiameter(mainPipe);
        }

        private static double GetConnectorDiameter(Connector connector)
        {
            if (connector == null)
            {
                return 0.0;
            }

            try
            {
                if (connector.Radius > 1e-9)
                {
                    return connector.Radius * 2.0;
                }
            }
            catch
            {
                // Some connector types do not expose radius.
            }

            return 0.0;
        }

        private static double GetPipeDiameter(Pipe pipe)
        {
            try
            {
                Parameter diameter = pipe?.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                return diameter?.AsDouble() ?? 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static void ApplyPipeDiameter(Pipe pipe, double diameter)
        {
            if (pipe == null || diameter <= 1e-9)
            {
                return;
            }

            try
            {
                Parameter p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(diameter);
                }
            }
            catch
            {
                // Keep the type default diameter when Revit or the pipe type does not allow instance diameter changes.
            }
        }

        private static bool TryCreateTakeoff(Document doc, Connector branchConnector, Pipe mainPipe)
        {
            try
            {
                FamilyInstance takeoff = doc.Create.NewTakeoffFitting(branchConnector, mainPipe);
                doc.Regenerate();
                return takeoff != null && takeoff.IsValidObject;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCreateTeeByBreakingMain(
            Document doc,
            Pipe mainPipe,
            List<Pipe> mainSegments,
            Pipe branchPipe,
            Connector branchConnector,
            XYZ teePoint,
            out string failReason)
        {
            failReason = string.Empty;

            try
            {
                Pipe targetMain = FindMainSegmentAtPoint(mainSegments, teePoint) ?? mainPipe;
                Line line = GetPipeLine(targetMain);
                if (line == null)
                {
                    failReason = "幹管沒有有效中心線。";
                    return false;
                }

                double tol = UnitUtils.ConvertToInternalUnits(TeePointToleranceMm, UnitTypeId.Millimeters);
                if (teePoint.DistanceTo(line.GetEndPoint(0)) < tol || teePoint.DistanceTo(line.GetEndPoint(1)) < tol)
                {
                    failReason = "接管點太靠近幹管端點。";
                    return false;
                }

                ElementId newSegmentId = PlumbingUtils.BreakCurve(doc, targetMain.Id, teePoint);
                doc.Regenerate();
                Pipe newMain = doc.GetElement(newSegmentId) as Pipe;
                if (newMain == null)
                {
                    failReason = "切斷幹管失敗。";
                    return false;
                }
                mainSegments.Add(newMain);

                Connector cMain1 = GetNearestOpenEndConnector(targetMain, teePoint) ?? GetNearestEndConnector(targetMain, teePoint);
                Connector cMain2 = GetNearestOpenEndConnector(newMain, teePoint) ?? GetNearestEndConnector(newMain, teePoint);
                Connector cBranch = branchConnector ?? GetNearestOpenEndConnector(branchPipe, teePoint) ?? GetNearestEndConnector(branchPipe, teePoint);
                if (cMain1 == null || cMain2 == null || cBranch == null)
                {
                    failReason = "找不到建立三通所需的 Connector。";
                    return false;
                }

                FamilyInstance tee = doc.Create.NewTeeFitting(cMain1, cMain2, cBranch);
                doc.Regenerate();
                if (tee == null || !tee.IsValidObject)
                {
                    failReason = "Revit 無法建立三通。";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failReason = "建立三通失敗：" + ex.Message;
                return false;
            }
        }

        private static bool TryConnect(Document doc, Connector a, Connector b)
        {
            if (a == null || b == null) return false;

            try
            {
                a.ConnectTo(b);
                doc.Regenerate();
                return true;
            }
            catch
            {
                try
                {
                    FamilyInstance elbow = doc.Create.NewElbowFitting(a, b);
                    doc.Regenerate();
                    return elbow != null && elbow.IsValidObject;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void SafeDelete(Document doc, IEnumerable<Element> elements)
        {
            foreach (Element element in elements.Where(e => e != null && e.IsValidObject).ToList())
            {
                try
                {
                    doc.Delete(element.Id);
                }
                catch
                {
                    // ignore cleanup failures
                }
            }
            doc.Regenerate();
        }

        private static Pipe FindMainSegmentAtPoint(IEnumerable<Pipe> segments, XYZ point)
        {
            Pipe best = null;
            double bestDistance = double.MaxValue;
            foreach (Pipe pipe in segments.Where(p => p != null && p.IsValidObject))
            {
                Line line = GetPipeLine(pipe);
                if (line == null) continue;

                IntersectionResult projected = line.Project(point);
                if (projected == null) continue;

                XYZ p = projected.XYZPoint;
                double d = p.DistanceTo(point);
                double station = (p - line.GetEndPoint(0)).DotProduct((line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize());
                if (station < -1e-6 || station > line.Length + 1e-6) continue;

                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = pipe;
                }
            }
            return best;
        }

        private static Line GetPipeLine(Pipe pipe)
        {
            return (pipe?.Location as LocationCurve)?.Curve as Line;
        }

        private static XYZ ProjectPointToLine(Line line, XYZ point)
        {
            IntersectionResult result = line.Project(point);
            return result?.XYZPoint ?? point;
        }

        private static Connector GetNearestRouteConnector(Element element, Line mainLine)
        {
            IEnumerable<Connector> connectors = GetConnectors(element)
                .Where(c => c.ConnectorType == ConnectorType.End && IsPipingConnector(c))
                .ToList();

            Connector best = GetNearestConnector(connectors.Where(c => !c.IsConnected), mainLine);
            return best ?? GetNearestConnector(connectors, mainLine);
        }

        private static Connector GetNearestConnector(IEnumerable<Connector> connectors, Line mainLine)
        {
            Connector best = null;
            double bestDistance = double.MaxValue;
            foreach (Connector connector in connectors)
            {
                XYZ projected = ProjectPointToLine(mainLine, connector.Origin);
                double d = connector.Origin.DistanceTo(projected);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = connector;
                }
            }
            return best;
        }

        private static Connector GetNearestOpenEndConnector(Pipe pipe, XYZ target)
        {
            return GetPipeConnectors(pipe)
                .Where(c => c.ConnectorType == ConnectorType.End && !c.IsConnected)
                .OrderBy(c => c.Origin.DistanceTo(target))
                .FirstOrDefault();
        }

        private static Connector GetNearestEndConnector(Pipe pipe, XYZ target)
        {
            return GetPipeConnectors(pipe)
                .Where(c => c.ConnectorType == ConnectorType.End)
                .OrderBy(c => c.Origin.DistanceTo(target))
                .FirstOrDefault();
        }

        private static IEnumerable<Connector> GetPipeConnectors(Pipe pipe)
        {
            return pipe?.ConnectorManager?.Connectors?.Cast<Connector>() ?? Enumerable.Empty<Connector>();
        }

        private static IEnumerable<Connector> GetConnectors(Element element)
        {
            if (element is Pipe pipe)
            {
                return GetPipeConnectors(pipe);
            }

            if (element is FamilyInstance familyInstance)
            {
                IEnumerable<Connector> ownConnectors = familyInstance.MEPModel?.ConnectorManager?.Connectors != null
                    ? familyInstance.MEPModel.ConnectorManager.Connectors.Cast<Connector>()
                    : Enumerable.Empty<Connector>();

                return ownConnectors.Concat(GetNestedFamilyConnectors(familyInstance));
            }

            return Enumerable.Empty<Connector>();
        }

        private static IEnumerable<Connector> GetNestedFamilyConnectors(FamilyInstance familyInstance)
        {
            if (familyInstance?.Document == null)
            {
                return Enumerable.Empty<Connector>();
            }

            List<Connector> connectors = new List<Connector>();
            try
            {
                foreach (ElementId subId in familyInstance.GetSubComponentIds())
                {
                    Element subElement = familyInstance.Document.GetElement(subId);
                    if (subElement is FamilyInstance subFamily
                        && subFamily.MEPModel?.ConnectorManager?.Connectors != null)
                    {
                        connectors.AddRange(subFamily.MEPModel.ConnectorManager.Connectors.Cast<Connector>());
                        connectors.AddRange(GetNestedFamilyConnectors(subFamily));
                    }
                }
            }
            catch
            {
                // Some families do not expose subcomponents.
            }

            return connectors;
        }

        private static bool IsPipingConnector(Connector connector)
        {
            try
            {
                return connector.Domain == Domain.DomainPiping;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsLikelyMepEquipment(FamilyInstance familyInstance)
        {
            if (familyInstance?.Category == null)
            {
                return false;
            }

            long categoryId = familyInstance.Category.Id.GetIdValue();
            BuiltInCategory[] allowedCategories =
            {
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_SpecialityEquipment
            };

            return allowedCategories.Any(category => categoryId == (long)category);
        }

        private static ElementId GetReferenceLevelId(Pipe pipe)
        {
            Parameter p = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            return p?.AsElementId() ?? ElementId.InvalidElementId;
        }

        private static ElementId GetSystemTypeId(Pipe pipe)
        {
            Parameter p = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId id = p?.AsElementId() ?? ElementId.InvalidElementId;
            if (id != ElementId.InvalidElementId)
            {
                return id;
            }
            return pipe?.MEPSystem?.GetTypeId() ?? ElementId.InvalidElementId;
        }

        private sealed class MainPipeSelectionFilter : ISelectionFilter
        {
            private readonly HashSet<long> _excludedIds;

            public MainPipeSelectionFilter(IEnumerable<ElementId> excludedIds = null)
            {
                _excludedIds = new HashSet<long>((excludedIds ?? Enumerable.Empty<ElementId>()).Select(id => id.GetIdValue()));
            }

            public bool AllowElement(Element elem)
            {
                if (elem == null || _excludedIds.Contains(elem.Id.GetIdValue()))
                {
                    return false;
                }

                return elem is Pipe;
            }

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        private sealed class RouteSourceSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem == null) return false;
                if (elem is Pipe) return true;
                if (elem is FamilyInstance fi)
                {
                    if (IsLikelyMepEquipment(fi))
                    {
                        return true;
                    }

                    return GetConnectors(fi)
                        .Any(c => c.ConnectorType == ConnectorType.End && IsPipingConnector(c));
                }
                return false;
            }

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        private sealed class ElementIdComparer : IEqualityComparer<Element>
        {
            public bool Equals(Element x, Element y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.Id.GetIdValue() == y.Id.GetIdValue();
            }

            public int GetHashCode(Element obj)
            {
                return obj?.Id.GetIdValue().GetHashCode() ?? 0;
            }
        }

        private sealed class RouteTask
        {
            public Element SourceElement { get; set; }
            public Connector SourceConnector { get; set; }
            public XYZ ConnectOnMain { get; set; }
            public double Station { get; set; }
            public string SourceLabel { get; set; }
        }
    }
}
