using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP.MepCheck
{
    internal sealed class MepCheckService
    {
        private const double FeetToMm = 304.8;

        public MepCheckResult Run(Document doc, View view, MepCheckOptions options)
        {
            var result = new MepCheckResult();

            if (options.CheckPipeDrainDirection)
            {
                CheckPipeDrainDirection(doc, view, options, result);
            }

            if (options.CheckEquipmentLevelDistribution)
            {
                CheckEquipmentLevelDistribution(doc, view, options, result);
            }

            if (options.CheckConnectorCompleteness)
            {
                CheckConnectorCompleteness(doc, view, options, result);
            }

            if (options.CheckSystemData)
            {
                CheckSystemData(doc, view, options, result);
            }

            return result;
        }

        private static void CheckConnectorCompleteness(Document doc, View view, MepCheckOptions options, MepCheckResult result)
        {
            foreach (Element element in CollectConnectorElements(doc, view, options.Scope))
            {
                List<Connector> connectors = GetPhysicalConnectors(element).ToList();
                if (connectors.Count == 0)
                {
                    continue;
                }

                result.ConnectorElementCount++;
                int openCount = connectors.Count(connector => !IsConnectorConnected(connector));
                if (openCount == 0)
                {
                    continue;
                }

                result.Issues.Add(CreateIssue(
                    "Connector.Completeness",
                    MepCheckSeverity.Warning,
                    element,
                    GetElementLevelName(doc, element),
                    GetElementSystemName(element),
                    $"{connectors.Count} 個實體端點，{openCount} 個未連接",
                    "所有實體端點均已連接",
                    "發現未連接端點，請確認是否為設計開口、末端設備或模型中斷。"));
            }
        }

        private static void CheckSystemData(Document doc, View view, MepCheckOptions options, MepCheckResult result)
        {
            List<Element> elements = CollectConnectorElements(doc, view, options.Scope)
                .Where(HasSystemBearingConnector)
                .ToList();
            foreach (Element element in elements)
            {
                List<Connector> connectors = GetPhysicalConnectors(element).ToList();
                if (connectors.Count == 0 || !connectors.Any(IsConnectorConnected))
                {
                    continue;
                }

                result.SystemDataElementCount++;
                string systemName = GetElementSystemName(element);
                if (string.IsNullOrWhiteSpace(systemName))
                {
                    result.Issues.Add(CreateIssue(
                        "SystemData.MissingSystem",
                        MepCheckSeverity.Warning,
                        element,
                        GetElementLevelName(doc, element),
                        string.Empty,
                        "已連接，但系統名稱空白",
                        "已指派 MEP 系統",
                        "構件已有實體連接，但未取得系統名稱；請檢查 Connector 分類與系統指派。"));
                }
            }

            if (options.CheckDuplicateEquipmentMarks)
            {
                CheckDuplicateEquipmentMarks(doc, view, options.Scope, result);
            }
        }

        private static void CheckDuplicateEquipmentMarks(Document doc, View view, MepCheckScope scope, MepCheckResult result)
        {
            List<FamilyInstance> equipment = CollectEquipment(doc, view, scope).ToList();
            var duplicateGroups = equipment
                .Select(item => new
                {
                    Element = item,
                    Mark = item.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim() ?? string.Empty
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Mark))
                .GroupBy(item => item.Mark, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var group in duplicateGroups)
            {
                string duplicateIds = string.Join(", ", group.Select(item => item.Element.Id.GetIdValue()));
                foreach (var item in group)
                {
                    result.Issues.Add(CreateIssue(
                        "SystemData.DuplicateMark",
                        MepCheckSeverity.Warning,
                        item.Element,
                        GetElementLevelName(doc, item.Element),
                        GetElementSystemName(item.Element),
                        item.Mark,
                        "設備編號必須唯一",
                        $"設備編號「{item.Mark}」重複，相關元素 ID：{duplicateIds}。"));
                }
            }
        }

        private static void CheckPipeDrainDirection(Document doc, View view, MepCheckOptions options, MepCheckResult result)
        {
            double minimumHorizontalFeet = UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);
            double flatToleranceFeet = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            XYZ outletPoint = GetOutletPoint(doc, options.OutletElementId);
            XYZ downstreamVector = GetDownstreamVector(options.DownstreamDirection);

            IEnumerable<Pipe> pipes = CollectElements<Pipe>(doc, view, options.Scope)
                .Where(p => p.Location is LocationCurve);

            foreach (Pipe pipe in pipes)
            {
                string systemName = GetSystemName(doc, pipe);
                if (options.DrainageSystemOnly && !LooksLikeDrainageSystem(systemName, pipe.Name))
                {
                    continue;
                }

                if (!TryGetLine(pipe, out Line line))
                {
                    result.PipeCount++;
                    result.Issues.Add(CreateIssue(
                        "PipeDrain.Direction",
                        MepCheckSeverity.Info,
                        pipe,
                        GetLevelName(doc, pipe.LevelId),
                        systemName,
                        string.Empty,
                        "直線管段",
                        "此管線不是直線管段，已略過洩水方向判斷。"));
                    continue;
                }

                XYZ start = line.GetEndPoint(0);
                XYZ end = line.GetEndPoint(1);
                double horizontal = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
                if (horizontal <= minimumHorizontalFeet)
                {
                    result.IgnoredPipeCount++;
                    continue;
                }

                double deltaZ = end.Z - start.Z;
                if (options.IgnoreFlatPipes && Math.Abs(deltaZ) <= flatToleranceFeet)
                {
                    result.IgnoredPipeCount++;
                    continue;
                }

                result.PipeCount++;

                double slopePercent = Math.Abs(deltaZ / horizontal) * 100.0;
                XYZ lowerEndpoint = deltaZ >= 0 ? start : end;
                string direction = deltaZ >= 0
                    ? $"端點 2 往端點 1 洩水，坡度 {slopePercent:0.###}%"
                    : $"端點 1 往端點 2 洩水，坡度 {slopePercent:0.###}%";

                if (slopePercent < options.MinimumDrainSlopePercent)
                {
                    result.Issues.Add(CreateIssue(
                        "PipeDrain.Slope",
                        MepCheckSeverity.Warning,
                        pipe,
                        GetLevelName(doc, pipe.LevelId),
                        systemName,
                        direction,
                        $">= {options.MinimumDrainSlopePercent:0.###}%",
                        "管線坡度低於設定值，請確認排水坡度。"));
                    continue;
                }

                FlowRuleResult flowRule = EvaluateFlowRule(doc, pipe, options, start, end, lowerEndpoint, outletPoint, downstreamVector);
                if (!flowRule.IsValid)
                {
                    result.Issues.Add(CreateIssue(
                        "PipeDrain.FlowRule",
                        MepCheckSeverity.Warning,
                        pipe,
                        GetLevelName(doc, pipe.LevelId),
                        systemName,
                        direction,
                        flowRule.ExpectedValue,
                        flowRule.Message));
                    continue;
                }

                result.Issues.Add(CreateIssue(
                    "PipeDrain.Direction",
                    MepCheckSeverity.Pass,
                    pipe,
                    GetLevelName(doc, pipe.LevelId),
                    systemName,
                    direction,
                    flowRule.ExpectedValue,
                    flowRule.Message));
            }
        }

        private static void CheckEquipmentLevelDistribution(Document doc, View view, MepCheckOptions options, MepCheckResult result)
        {
            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (!levels.Any())
            {
                return;
            }

            IEnumerable<FamilyInstance> equipment = CollectEquipment(doc, view, options.Scope);
            double toleranceFeet = UnitUtils.ConvertToInternalUnits(options.EquipmentLevelToleranceMm, UnitTypeId.Millimeters);

            foreach (FamilyInstance item in equipment)
            {
                result.EquipmentCount++;

                XYZ point = GetElementPoint(item);
                if (point == null)
                {
                    result.Issues.Add(CreateIssue(
                        "Equipment.Level",
                        MepCheckSeverity.Info,
                        item,
                        GetLevelName(doc, item.LevelId),
                        string.Empty,
                        string.Empty,
                        "可取得元素定位點",
                        "此設備無法取得定位點，請手動確認樓層。"));
                    continue;
                }

                Level assigned = doc.GetElement(item.LevelId) as Level;
                Level nearest = levels.OrderBy(l => Math.Abs(l.Elevation - point.Z)).FirstOrDefault();
                if (nearest == null)
                {
                    continue;
                }

                string assignedName = assigned?.Name ?? "(未指定樓層)";
                double diff = assigned == null ? double.MaxValue : Math.Abs(point.Z - assigned.Elevation);
                string current = $"{assignedName} / Z {ToProjectMm(point.Z):0.#} mm";
                string expected = $"{nearest.Name} / 差距 {ToProjectMm(Math.Abs(point.Z - nearest.Elevation)):0.#} mm";

                if (assigned == null || assigned.Id != nearest.Id || diff > toleranceFeet)
                {
                    result.Issues.Add(CreateIssue(
                        "Equipment.Level",
                        MepCheckSeverity.Warning,
                        item,
                        assignedName,
                        string.Empty,
                        current,
                        expected,
                        "設備所在樓層與模型高程可能不一致。"));
                }
                else
                {
                    result.Issues.Add(CreateIssue(
                        "Equipment.Level",
                        MepCheckSeverity.Pass,
                        item,
                        assignedName,
                        string.Empty,
                        current,
                        expected,
                        "設備樓層分布符合容許誤差。"));
                }
            }
        }

        private static FlowRuleResult EvaluateFlowRule(
            Document doc,
            Pipe pipe,
            MepCheckOptions options,
            XYZ start,
            XYZ end,
            XYZ lowerEndpoint,
            XYZ outletPoint,
            XYZ downstreamVector)
        {
            switch (options.DrainFlowRule)
            {
                case MepDrainFlowRule.OutletElement:
                    return outletPoint == null
                        ? FlowRuleResult.Fail("指定排出口", "無法取得指定排出口的位置。")
                        : CheckTowardPoint(start, end, lowerEndpoint, outletPoint, "低點靠近指定排出口", "管線洩水方向符合指定排出口。", "低點未朝向指定排出口，請確認管線坡向。");

                case MepDrainFlowRule.DownstreamDirection:
                    return CheckTowardVector(start, end, lowerEndpoint, downstreamVector);

                case MepDrainFlowRule.SystemFlow:
                    XYZ systemPoint = TryGetSystemReferencePoint(doc, pipe);
                    if (systemPoint != null)
                    {
                        return CheckTowardPoint(start, end, lowerEndpoint, systemPoint, "低點靠近系統基準設備", "管線洩水方向符合系統流向基準。", "低點未朝向系統基準設備，請確認系統流向。");
                    }
                    return FlowRuleResult.Pass("系統流向未提供基準，改用高程判斷", "管線洩水方向符合坡度；此系統未取得基準設備。");

                default:
                    return FlowRuleResult.Pass("依高程自動判斷", "管線洩水方向符合坡度。");
            }
        }

        private static FlowRuleResult CheckTowardPoint(XYZ start, XYZ end, XYZ lowerEndpoint, XYZ targetPoint, string expected, string passMessage, string failMessage)
        {
            XYZ expectedLower = start.DistanceTo(targetPoint) <= end.DistanceTo(targetPoint) ? start : end;
            return SamePoint(expectedLower, lowerEndpoint)
                ? FlowRuleResult.Pass(expected, passMessage)
                : FlowRuleResult.Fail(expected, failMessage);
        }

        private static FlowRuleResult CheckTowardVector(XYZ start, XYZ end, XYZ lowerEndpoint, XYZ downstreamVector)
        {
            double startStation = start.DotProduct(downstreamVector);
            double endStation = end.DotProduct(downstreamVector);
            XYZ expectedLower = endStation >= startStation ? end : start;
            string expected = $"低點朝向 {GetDirectionText(downstreamVector)}";
            return SamePoint(expectedLower, lowerEndpoint)
                ? FlowRuleResult.Pass(expected, "管線洩水方向符合指定下游方向。")
                : FlowRuleResult.Fail(expected, "低點未朝向指定下游方向，請確認管線坡向。");
        }

        private static XYZ GetOutletPoint(Document doc, ElementId elementId)
        {
            if (elementId == null || elementId == ElementId.InvalidElementId)
            {
                return null;
            }

            Element element = doc.GetElement(elementId);
            return element == null ? null : GetElementPoint(element);
        }

        private static XYZ TryGetSystemReferencePoint(Document doc, Pipe pipe)
        {
            object system = pipe.MEPSystem;
            if (system == null)
            {
                return null;
            }

            object baseEquipment = system.GetType().GetProperty("BaseEquipment")?.GetValue(system, null);
            if (baseEquipment is Element equipment)
            {
                return GetElementPoint(equipment);
            }

            ElementId baseEquipmentId = system.GetType().GetProperty("BaseEquipmentId")?.GetValue(system, null) as ElementId;
            if (baseEquipmentId != null && baseEquipmentId != ElementId.InvalidElementId)
            {
                Element element = doc.GetElement(baseEquipmentId);
                return element == null ? null : GetElementPoint(element);
            }

            return null;
        }

        private static XYZ GetDownstreamVector(MepDownstreamDirection direction)
        {
            switch (direction)
            {
                case MepDownstreamDirection.NegativeX: return -XYZ.BasisX;
                case MepDownstreamDirection.PositiveY: return XYZ.BasisY;
                case MepDownstreamDirection.NegativeY: return -XYZ.BasisY;
                default: return XYZ.BasisX;
            }
        }

        private static string GetDirectionText(XYZ vector)
        {
            if (vector.IsAlmostEqualTo(XYZ.BasisX)) return "+X";
            if (vector.IsAlmostEqualTo(-XYZ.BasisX)) return "-X";
            if (vector.IsAlmostEqualTo(XYZ.BasisY)) return "+Y";
            return "-Y";
        }

        private static bool SamePoint(XYZ a, XYZ b)
        {
            return a.DistanceTo(b) <= UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
        }

        private static IEnumerable<T> CollectElements<T>(Document doc, View view, MepCheckScope scope) where T : Element
        {
            FilteredElementCollector collector = scope == MepCheckScope.CurrentView
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);

            return collector
                .OfClass(typeof(T))
                .WhereElementIsNotElementType()
                .Cast<T>();
        }

        private static IEnumerable<Element> CollectConnectorElements(Document doc, View view, MepCheckScope scope)
        {
            FilteredElementCollector curveCollector = scope == MepCheckScope.CurrentView
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);
            FilteredElementCollector familyCollector = scope == MepCheckScope.CurrentView
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);

            var seen = new HashSet<long>();
            foreach (MEPCurve curve in curveCollector
                .OfClass(typeof(MEPCurve))
                .WhereElementIsNotElementType()
                .OfType<MEPCurve>())
            {
                if (seen.Add(curve.Id.GetIdValue()))
                {
                    yield return curve;
                }
            }

            foreach (FamilyInstance family in familyCollector
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>())
            {
                if (family.MEPModel?.ConnectorManager?.Connectors == null)
                {
                    continue;
                }

                if (seen.Add(family.Id.GetIdValue()))
                {
                    yield return family;
                }
            }
        }

        private static IEnumerable<Connector> GetPhysicalConnectors(Element element)
        {
            ConnectorSet connectorSet = null;
            if (element is MEPCurve curve)
            {
                connectorSet = curve.ConnectorManager?.Connectors;
            }
            else if (element is FamilyInstance family)
            {
                connectorSet = family.MEPModel?.ConnectorManager?.Connectors;
            }

            if (connectorSet == null)
            {
                yield break;
            }

            foreach (Connector connector in connectorSet)
            {
                if (connector != null &&
                    (connector.ConnectorType == ConnectorType.End ||
                     connector.ConnectorType == ConnectorType.Curve))
                {
                    yield return connector;
                }
            }
        }

        private static bool IsConnectorConnected(Connector connector)
        {
            try
            {
                return connector.IsConnected;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasSystemBearingConnector(Element element)
        {
            if (element is Pipe || element is Duct)
            {
                return true;
            }

            return GetPhysicalConnectors(element).Any(connector =>
                connector.Domain == Domain.DomainPiping ||
                connector.Domain == Domain.DomainHvac);
        }

        private static string GetElementSystemName(Element element)
        {
            if (element is MEPCurve curve && !string.IsNullOrWhiteSpace(curve.MEPSystem?.Name))
            {
                return curve.MEPSystem.Name;
            }

            return GetPhysicalConnectors(element)
                .Select(connector =>
                {
                    try
                    {
                        return connector.MEPSystem?.Name ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                })
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty;
        }

        private static string GetElementLevelName(Document doc, Element element)
        {
            ElementId levelId = element.LevelId;
            if (levelId != null && levelId != ElementId.InvalidElementId)
            {
                return GetLevelName(doc, levelId);
            }

            Parameter levelParameter = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            ElementId parameterLevelId = levelParameter?.AsElementId() ?? ElementId.InvalidElementId;
            return GetLevelName(doc, parameterLevelId);
        }

        private static IEnumerable<FamilyInstance> CollectEquipment(Document doc, View view, MepCheckScope scope)
        {
            BuiltInCategory[] categories =
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_PlumbingFixtures
            };

            foreach (BuiltInCategory category in categories)
            {
                FilteredElementCollector collector = scope == MepCheckScope.CurrentView
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);

                foreach (FamilyInstance item in collector
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    yield return item;
                }
            }
        }

        private static bool TryGetLine(Pipe pipe, out Line line)
        {
            line = null;
            if (pipe.Location is LocationCurve lc && lc.Curve is Line l)
            {
                line = l;
                return true;
            }
            return false;
        }

        private static XYZ GetElementPoint(Element element)
        {
            if (element.Location is LocationPoint lp)
            {
                return lp.Point;
            }

            if (element.Location is LocationCurve lc)
            {
                return lc.Curve.Evaluate(0.5, true);
            }

            BoundingBoxXYZ bbox = element.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) * 0.5;
            }

            return null;
        }

        private static string GetSystemName(Document doc, Pipe pipe)
        {
            string name = pipe.MEPSystem?.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            Parameter parameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId id = parameter?.AsElementId() ?? ElementId.InvalidElementId;
            Element type = id == ElementId.InvalidElementId ? null : doc.GetElement(id);
            return type?.Name ?? string.Empty;
        }

        private static bool LooksLikeDrainageSystem(string systemName, string pipeName)
        {
            string text = $"{systemName} {pipeName}".ToUpperInvariant();
            string[] keywords =
            {
                "排水", "污水", "雨水", "廢水", "透氣", "洩水",
                "DRAIN", "WASTE", "SAN", "VENT", "RW", "WW", "SW", "WP", "VP"
            };
            return keywords.Any(k => text.Contains(k.ToUpperInvariant()));
        }

        private static string GetLevelName(Document doc, ElementId levelId)
        {
            if (levelId == null || levelId == ElementId.InvalidElementId)
            {
                return "(未指定樓層)";
            }
            return doc.GetElement(levelId)?.Name ?? "(未指定樓層)";
        }

        private static MepCheckIssue CreateIssue(
            string checkKey,
            MepCheckSeverity severity,
            Element element,
            string levelName,
            string systemName,
            string currentValue,
            string expectedValue,
            string message)
        {
            return new MepCheckIssue
            {
                CheckKey = checkKey,
                Severity = severity,
                ElementId = element?.Id ?? ElementId.InvalidElementId,
                Category = element?.Category?.Name ?? string.Empty,
                ElementName = element?.Name ?? string.Empty,
                LevelName = levelName,
                SystemName = systemName,
                CurrentValue = currentValue,
                ExpectedValue = expectedValue,
                Message = message
            };
        }

        private static double ToProjectMm(double feet)
        {
            return feet * FeetToMm;
        }

        private sealed class FlowRuleResult
        {
            public bool IsValid { get; private set; }
            public string ExpectedValue { get; private set; }
            public string Message { get; private set; }

            public static FlowRuleResult Pass(string expectedValue, string message)
            {
                return new FlowRuleResult { IsValid = true, ExpectedValue = expectedValue, Message = message };
            }

            public static FlowRuleResult Fail(string expectedValue, string message)
            {
                return new FlowRuleResult { IsValid = false, ExpectedValue = expectedValue, Message = message };
            }
        }
    }
}
