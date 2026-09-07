using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Helpers;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    /// <summary>
    /// Batch connects piping connectors from multiple equipment families into selected main pipes.
    /// This is the API counterpart of the Dynamo "Connect IntoMULTIPLE" workflow.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdMepMultiConnectInto : IExternalCommand
    {
        private const double MinimumPipeLengthMm = 30.0;
        private const double MainEndClearanceMm = 50.0;
        private const double ConnectorToleranceMm = 5.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            if (doc == null)
            {
                message = "找不到目前的 Revit 文件。";
                return Result.Failed;
            }

            try
            {
                MultiConnectOptions options = MultiConnectOptions.Default();
                using (var form = new MultiConnectOptionsForm(options))
                {
                    if (form.ShowDialog() != WinForms.DialogResult.OK)
                    {
                        return Result.Cancelled;
                    }

                    options = form.Options;
                }

                IList<Reference> equipmentRefs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new EquipmentSelectionFilter(),
                    "請選取要批次接管的設備，可框選或複選，完成後按「完成」。");

                List<FamilyInstance> equipment = equipmentRefs
                    .Select(r => doc.GetElement(r))
                    .OfType<FamilyInstance>()
                    .Distinct(new ElementIdEqualityComparer<FamilyInstance>())
                    .ToList();

                if (equipment.Count == 0)
                {
                    TaskDialog.Show("多點接入主管", "沒有選到可用的設備。");
                    return Result.Cancelled;
                }

                IList<Reference> mainRefs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "請選取可接入的主管 Pipe，可框選或複選，完成後按「完成」。");

                List<Pipe> mainPipes = mainRefs
                    .Select(r => doc.GetElement(r))
                    .OfType<Pipe>()
                    .Where(p => GetPipeLine(p) != null)
                    .Distinct(new ElementIdEqualityComparer<Pipe>())
                    .ToList();

                if (mainPipes.Count == 0)
                {
                    TaskDialog.Show("多點接入主管", "沒有選到直線主管 Pipe。");
                    return Result.Cancelled;
                }

                List<MultiConnectTask> tasks = BuildTasks(doc, equipment, mainPipes, options, out List<string> warnings);
                if (tasks.Count == 0)
                {
                    string detail = warnings.Count > 0 ? "\n\n" + string.Join("\n", warnings.Take(10)) : string.Empty;
                    TaskDialog.Show("多點接入主管", "找不到可接入的設備 Connector。" + detail);
                    return Result.Cancelled;
                }

                if (!ConfirmPreview(equipment.Count, mainPipes.Count, tasks, warnings, options))
                {
                    return Result.Cancelled;
                }

                List<Pipe> dynamicMainSegments = new List<Pipe>(mainPipes);
                int teeCount = 0;
                int branchCount = 0;
                using (Transaction tx = new Transaction(doc, "MEP 多點接入主管"))
                {
                    tx.Start();

                    List<string> failures = new List<string>();
                    foreach (MultiConnectTask task in tasks.OrderBy(t => t.MainPipe.Id.GetIdValue()).ThenBy(t => t.MainStation))
                    {
                        if (!TryCreateBranchAndTee(doc, task, dynamicMainSegments, options, out Pipe branch, out string failReason))
                        {
                            failures.Add($"{task.SourceLabel}：{failReason}");
                            break;
                        }

                        branchCount++;
                        teeCount++;
                    }

                    if (failures.Count > 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show(
                            "多點接入主管",
                            "接入失敗，已取消本次批次作業並回復模型。\n\n" +
                            string.Join("\n", failures.Take(8)));
                        return Result.Cancelled;
                    }

                    tx.Commit();
                }

                TaskDialog.Show(
                    "多點接入主管",
                    "批次接入完成。\n\n" +
                    $"設備數：{equipment.Count}\n" +
                    $"主管數：{mainPipes.Count}\n" +
                    $"建立支管：{branchCount}\n" +
                    $"建立三通：{teeCount}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("多點接入主管", "執行失敗：\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static List<MultiConnectTask> BuildTasks(
            Document doc,
            List<FamilyInstance> equipment,
            List<Pipe> mainPipes,
            MultiConnectOptions options,
            out List<string> warnings)
        {
            warnings = new List<string>();
            List<MultiConnectTask> tasks = new List<MultiConnectTask>();
            double minLength = UnitUtils.ConvertToInternalUnits(MinimumPipeLengthMm, UnitTypeId.Millimeters);
            double endClearance = UnitUtils.ConvertToInternalUnits(MainEndClearanceMm, UnitTypeId.Millimeters);

            foreach (FamilyInstance family in equipment)
            {
                List<Connector> connectors = GetConnectors(family)
                    .Where(c => c.ConnectorType == ConnectorType.End && IsPipingConnector(c))
                    .Where(c => options.IncludeConnectedConnectors || !c.IsConnected)
                    .ToList();

                if (connectors.Count == 0)
                {
                    warnings.Add($"{GetElementLabel(family)}：沒有可用的 Pipe Connector。");
                    continue;
                }

                foreach (Connector connector in connectors)
                {
                    PipeRole role = GetConnectorRole(connector);
                    if (!options.IncludeUnknownSystem && role == PipeRole.Unknown)
                    {
                        continue;
                    }

                    Pipe main = FindBestMainPipe(doc, mainPipes, connector, role, options, out XYZ teePoint, out double station);
                    if (main == null)
                    {
                        warnings.Add($"{GetElementLabel(family)}：找不到符合系統的主管。");
                        continue;
                    }

                    Line mainLine = GetPipeLine(main);
                    if (mainLine == null)
                    {
                        continue;
                    }

                    if (station < endClearance || station > mainLine.Length - endClearance)
                    {
                        warnings.Add($"{GetElementLabel(family)}：接入點太靠近主管端點，已略過。");
                        continue;
                    }

                    if (connector.Origin.DistanceTo(teePoint) < minLength)
                    {
                        warnings.Add($"{GetElementLabel(family)}：Connector 到主管距離太短，已略過。");
                        continue;
                    }

                    if (role == PipeRole.Sanitary && options.SanitarySlopeRatio > 0)
                    {
                        double horizontal = new XYZ(teePoint.X - connector.Origin.X, teePoint.Y - connector.Origin.Y, 0).GetLength();
                        double requiredDrop = horizontal / options.SanitarySlopeRatio;
                        double actualDrop = connector.Origin.Z - teePoint.Z;
                        if (actualDrop + UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters) < requiredDrop)
                        {
                            warnings.Add($"{GetElementLabel(family)}：排水接入點坡度不足 1:{options.SanitarySlopeRatio:0.#}，已略過。");
                            continue;
                        }
                    }

                    tasks.Add(new MultiConnectTask
                    {
                        Equipment = family,
                        Connector = connector,
                        Role = role,
                        MainPipe = main,
                        TeePoint = teePoint,
                        MainStation = station,
                        SourceLabel = $"{GetElementLabel(family)} / {GetRoleDisplayName(role)}"
                    });
                }
            }

            return tasks;
        }

        private static bool ConfirmPreview(
            int equipmentCount,
            int mainPipeCount,
            List<MultiConnectTask> tasks,
            List<string> warnings,
            MultiConnectOptions options)
        {
            string warningText = warnings.Count == 0
                ? string.Empty
                : "\n\n略過/警告：\n" + string.Join("\n", warnings.Take(8)) + (warnings.Count > 8 ? "\n..." : string.Empty);

            var groups = tasks
                .GroupBy(t => t.Role)
                .Select(g => $"{GetRoleDisplayName(g.Key)}：{g.Count()}");

            var dialog = new TaskDialog("多點接入主管預覽")
            {
                MainInstruction = "確認是否批次接入主管？",
                MainContent =
                    $"設備數：{equipmentCount}\n" +
                    $"主管數：{mainPipeCount}\n" +
                    $"預計接入：{tasks.Count}\n" +
                    $"系統分布：{string.Join("，", groups)}\n" +
                    $"CHWS 延伸：{options.SupplyLengthMm:0.#} mm\n" +
                    $"CHWR 延伸：{options.ReturnLengthMm:0.#} mm\n" +
                    $"CONDENSATE 延伸：{options.SanitaryLengthMm:0.#} mm\n" +
                    $"排水坡度：1:{options.SanitarySlopeRatio:0.#}\n\n" +
                    "按「確定」後會建立支管、打斷主管並建立 Tee；若任一接入失敗會整批回復。" +
                    warningText,
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };

            return dialog.Show() == TaskDialogResult.Ok;
        }

        private static bool TryCreateBranchAndTee(
            Document doc,
            MultiConnectTask task,
            List<Pipe> dynamicMainSegments,
            MultiConnectOptions options,
            out Pipe branch,
            out string failReason)
        {
            branch = null;
            failReason = string.Empty;

            Pipe targetMain = FindMainSegmentAtPoint(dynamicMainSegments, task.TeePoint) ?? task.MainPipe;
            if (targetMain == null || !targetMain.IsValidObject)
            {
                failReason = "找不到可打斷的主管段。";
                return false;
            }

            ElementId systemTypeId = GetSystemTypeId(targetMain);
            ElementId pipeTypeId = targetMain.GetTypeId();
            ElementId levelId = GetReferenceLevelId(targetMain);
            if (systemTypeId == ElementId.InvalidElementId || pipeTypeId == ElementId.InvalidElementId || levelId == ElementId.InvalidElementId)
            {
                failReason = "主管缺少系統類型、管型或樓層資訊。";
                return false;
            }

            XYZ branchStart = task.Connector.Origin;
            XYZ branchEnd = task.TeePoint;
            if (branchStart.DistanceTo(branchEnd) < UnitUtils.ConvertToInternalUnits(MinimumPipeLengthMm, UnitTypeId.Millimeters))
            {
                failReason = "支管長度太短。";
                return false;
            }

            try
            {
                branch = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, branchStart, branchEnd);
                doc.Regenerate();
            }
            catch (Exception ex)
            {
                failReason = "建立支管失敗：" + ex.Message;
                return false;
            }

            if (branch == null || !branch.IsValidObject)
            {
                failReason = "建立支管失敗。";
                return false;
            }

            TrySetPipeDiameter(branch, targetMain);

            Connector branchStartConnector = GetNearestOpenEndConnector(branch, branchStart) ?? GetNearestEndConnector(branch, branchStart);
            TryConnect(task.Connector, branchStartConnector);
            doc.Regenerate();

            XYZ teePoint = task.TeePoint;
            Line targetLine = GetPipeLine(targetMain);
            if (targetLine == null)
            {
                failReason = "主管不是直線管段。";
                return false;
            }

            try
            {
                ElementId newSegmentId = PlumbingUtils.BreakCurve(doc, targetMain.Id, teePoint);
                doc.Regenerate();

                Pipe newMain = doc.GetElement(newSegmentId) as Pipe;
                if (newMain == null)
                {
                    failReason = "打斷主管失敗。";
                    return false;
                }

                dynamicMainSegments.Add(newMain);

                Connector mainA = GetNearestOpenEndConnector(targetMain, teePoint) ?? GetNearestEndConnector(targetMain, teePoint);
                Connector mainB = GetNearestOpenEndConnector(newMain, teePoint) ?? GetNearestEndConnector(newMain, teePoint);
                Connector branchEndConnector = GetNearestOpenEndConnector(branch, branchEnd) ?? GetNearestEndConnector(branch, branchEnd);

                if (mainA == null || mainB == null || branchEndConnector == null)
                {
                    failReason = "找不到建立 Tee 所需的 Connector。";
                    return false;
                }

                FamilyInstance tee = doc.Create.NewTeeFitting(mainA, mainB, branchEndConnector);
                doc.Regenerate();
                if (tee == null || !tee.IsValidObject)
                {
                    failReason = "建立 Tee 失敗。";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failReason = "建立 Tee 失敗：" + ex.Message;
                return false;
            }
        }

        private static Pipe FindBestMainPipe(
            Document doc,
            IEnumerable<Pipe> mainPipes,
            Connector connector,
            PipeRole role,
            MultiConnectOptions options,
            out XYZ teePoint,
            out double station)
        {
            teePoint = null;
            station = 0.0;
            Pipe best = null;
            double bestScore = double.MaxValue;

            XYZ origin = connector.Origin;
            XYZ direction = GetConnectorDirection(connector);

            foreach (Pipe pipe in mainPipes.Where(p => p != null && p.IsValidObject))
            {
                Line line = GetPipeLine(pipe);
                if (line == null) continue;

                PipeRole mainRole = GetPipeRole(doc, pipe);
                bool roleMatches = role == PipeRole.Unknown || mainRole == PipeRole.Unknown || mainRole == role;
                if (options.RequireMatchingSystem && !roleMatches)
                {
                    continue;
                }

                XYZ projected = ProjectPointToLine(line, origin);
                if (projected == null) continue;

                double s = GetStationOnLine(line, projected);
                if (s < -1e-6 || s > line.Length + 1e-6) continue;

                double distance = origin.DistanceTo(projected);
                double alignmentPenalty = 0.0;
                if (direction != null)
                {
                    XYZ toMain = (projected - origin);
                    if (toMain.GetLength() > 1e-9)
                    {
                        double dot = Math.Abs(direction.Normalize().DotProduct(toMain.Normalize()));
                        alignmentPenalty = (1.0 - dot) * UnitUtils.ConvertToInternalUnits(500.0, UnitTypeId.Millimeters);
                    }
                }

                double systemPenalty = roleMatches ? 0.0 : UnitUtils.ConvertToInternalUnits(2000.0, UnitTypeId.Millimeters);
                double targetLength = UnitUtils.ConvertToInternalUnits(GetPreferredLengthMm(options, role), UnitTypeId.Millimeters);
                double preferredLengthPenalty = targetLength > 1e-9 ? Math.Abs(distance - targetLength) * 0.25 : 0.0;
                double score = distance + alignmentPenalty + systemPenalty + preferredLengthPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = pipe;
                    teePoint = projected;
                    station = s;
                }
            }

            return best;
        }

        private static double GetPreferredLengthMm(MultiConnectOptions options, PipeRole role)
        {
            switch (role)
            {
                case PipeRole.SupplyHydronic:
                    return options.SupplyLengthMm;
                case PipeRole.ReturnHydronic:
                    return options.ReturnLengthMm;
                case PipeRole.Sanitary:
                    return options.SanitaryLengthMm;
                default:
                    return 0.0;
            }
        }

        private static Pipe FindMainSegmentAtPoint(IEnumerable<Pipe> segments, XYZ point)
        {
            Pipe best = null;
            double bestDistance = double.MaxValue;
            foreach (Pipe pipe in segments.Where(p => p != null && p.IsValidObject))
            {
                Line line = GetPipeLine(pipe);
                if (line == null) continue;

                XYZ projected = ProjectPointToLine(line, point);
                if (projected == null) continue;

                double station = GetStationOnLine(line, projected);
                if (station < -1e-6 || station > line.Length + 1e-6) continue;

                double distance = projected.DistanceTo(point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pipe;
                }
            }

            return best;
        }

        private static PipeRole GetConnectorRole(Connector connector)
        {
            string text = string.Join(" ", new[]
            {
                SafeString(() => connector.MEPSystem?.Name),
                SafeString(() => connector.Owner?.Name),
                SafeString(() => connector.Owner?.Category?.Name)
            });

            return GuessRole(text);
        }

        private static PipeRole GetPipeRole(Document doc, Pipe pipe)
        {
            string text = string.Join(" ", new[]
            {
                SafeString(() => pipe.MEPSystem?.Name),
                SafeString(() => doc.GetElement(GetSystemTypeId(pipe))?.Name),
                SafeString(() => doc.GetElement(pipe.GetTypeId())?.Name),
                SafeString(() => pipe.Name)
            });

            return GuessRole(text);
        }

        private static PipeRole GuessRole(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return PipeRole.Unknown;
            }

            string value = text.ToLowerInvariant();
            if (value.Contains("supplyhydronic") || value.Contains("supply") || value.Contains("chws") || value.Contains("chs") || value.Contains("冰水供"))
            {
                return PipeRole.SupplyHydronic;
            }

            if (value.Contains("returnhydronic") || value.Contains("return") || value.Contains("chwr") || value.Contains("chr") || value.Contains("冰水回"))
            {
                return PipeRole.ReturnHydronic;
            }

            if (value.Contains("sanitary") || value.Contains("condensate") || value.Contains("cond") || value.Contains("drain") || value.Contains("排水") || value.Contains("冷凝"))
            {
                return PipeRole.Sanitary;
            }

            return PipeRole.Unknown;
        }

        private static string SafeString(Func<string> getter)
        {
            try
            {
                return getter() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetRoleDisplayName(PipeRole role)
        {
            switch (role)
            {
                case PipeRole.SupplyHydronic:
                    return "CHWS/Supply";
                case PipeRole.ReturnHydronic:
                    return "CHWR/Return";
                case PipeRole.Sanitary:
                    return "COND/Sanitary";
                default:
                    return "未分類";
            }
        }

        private static void TrySetPipeDiameter(Pipe target, Pipe source)
        {
            try
            {
                Parameter sourceDiameter = source.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                Parameter targetDiameter = target.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (sourceDiameter != null && targetDiameter != null && !targetDiameter.IsReadOnly)
                {
                    targetDiameter.Set(sourceDiameter.AsDouble());
                }
            }
            catch
            {
            }
        }

        private static bool TryConnect(Connector first, Connector second)
        {
            try
            {
                if (first != null && second != null && !first.IsConnectedTo(second))
                {
                    first.ConnectTo(second);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<Connector> GetConnectors(FamilyInstance familyInstance)
        {
            IEnumerable<Connector> ownConnectors = familyInstance?.MEPModel?.ConnectorManager?.Connectors != null
                ? familyInstance.MEPModel.ConnectorManager.Connectors.Cast<Connector>()
                : Enumerable.Empty<Connector>();

            return ownConnectors.Concat(GetNestedFamilyConnectors(familyInstance));
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
                    FamilyInstance subFamily = subElement as FamilyInstance;
                    if (subFamily?.MEPModel?.ConnectorManager?.Connectors != null)
                    {
                        connectors.AddRange(subFamily.MEPModel.ConnectorManager.Connectors.Cast<Connector>());
                        connectors.AddRange(GetNestedFamilyConnectors(subFamily));
                    }
                }
            }
            catch
            {
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
                return false;
            }
        }

        private static XYZ GetConnectorDirection(Connector connector)
        {
            try
            {
                XYZ direction = connector.CoordinateSystem?.BasisZ;
                return direction != null && direction.GetLength() > 1e-9 ? direction.Normalize() : null;
            }
            catch
            {
                return null;
            }
        }

        private static Line GetPipeLine(Pipe pipe)
        {
            return (pipe?.Location as LocationCurve)?.Curve as Line;
        }

        private static XYZ ProjectPointToLine(Line line, XYZ point)
        {
            IntersectionResult result = line.Project(point);
            return result?.XYZPoint;
        }

        private static double GetStationOnLine(Line line, XYZ point)
        {
            XYZ start = line.GetEndPoint(0);
            XYZ direction = (line.GetEndPoint(1) - start).Normalize();
            return (point - start).DotProduct(direction);
        }

        private static Connector GetNearestOpenEndConnector(Pipe pipe, XYZ point)
        {
            return GetPipeConnectors(pipe)
                .Where(c => c.ConnectorType == ConnectorType.End && !c.IsConnected)
                .OrderBy(c => c.Origin.DistanceTo(point))
                .FirstOrDefault(c => c.Origin.DistanceTo(point) <= UnitUtils.ConvertToInternalUnits(ConnectorToleranceMm, UnitTypeId.Millimeters));
        }

        private static Connector GetNearestEndConnector(Pipe pipe, XYZ point)
        {
            return GetPipeConnectors(pipe)
                .Where(c => c.ConnectorType == ConnectorType.End)
                .OrderBy(c => c.Origin.DistanceTo(point))
                .FirstOrDefault(c => c.Origin.DistanceTo(point) <= UnitUtils.ConvertToInternalUnits(ConnectorToleranceMm, UnitTypeId.Millimeters));
        }

        private static IEnumerable<Connector> GetPipeConnectors(Pipe pipe)
        {
            return pipe?.ConnectorManager?.Connectors?.Cast<Connector>() ?? Enumerable.Empty<Connector>();
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

        private static string GetElementLabel(Element element)
        {
            return $"{element?.Name ?? "Element"} ID {element?.Id.GetIdValue()}";
        }

        private sealed class EquipmentSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                FamilyInstance family = elem as FamilyInstance;
                if (family == null)
                {
                    return false;
                }

                if (family.Category != null
                    && family.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_MechanicalEquipment)
                {
                    return true;
                }

                return GetConnectors(family).Any(c => c.ConnectorType == ConnectorType.End && IsPipingConnector(c));
            }

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        private sealed class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Pipe && GetPipeLine(elem as Pipe) != null;
            }

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        private sealed class ElementIdEqualityComparer<T> : IEqualityComparer<T> where T : Element
        {
            public bool Equals(T x, T y)
            {
                return x?.Id.GetIdValue() == y?.Id.GetIdValue();
            }

            public int GetHashCode(T obj)
            {
                return obj?.Id.GetIdValue().GetHashCode() ?? 0;
            }
        }
    }

    internal enum PipeRole
    {
        Unknown,
        SupplyHydronic,
        ReturnHydronic,
        Sanitary
    }

    internal sealed class MultiConnectTask
    {
        public FamilyInstance Equipment { get; set; }
        public Connector Connector { get; set; }
        public PipeRole Role { get; set; }
        public Pipe MainPipe { get; set; }
        public XYZ TeePoint { get; set; }
        public double MainStation { get; set; }
        public string SourceLabel { get; set; }
    }

    internal sealed class MultiConnectOptions
    {
        public double SupplyLengthMm { get; set; }
        public double ReturnLengthMm { get; set; }
        public double SanitaryLengthMm { get; set; }
        public double SanitarySlopeRatio { get; set; }
        public bool RequireMatchingSystem { get; set; }
        public bool IncludeUnknownSystem { get; set; }
        public bool IncludeConnectedConnectors { get; set; }

        public static MultiConnectOptions Default()
        {
            return new MultiConnectOptions
            {
                SupplyLengthMm = 300.0,
                ReturnLengthMm = 250.0,
                SanitaryLengthMm = 200.0,
                SanitarySlopeRatio = 90.0,
                RequireMatchingSystem = true,
                IncludeUnknownSystem = false,
                IncludeConnectedConnectors = false
            };
        }
    }

    internal sealed class MultiConnectOptionsForm : WinForms.Form
    {
        private readonly WinForms.NumericUpDown _numSupply;
        private readonly WinForms.NumericUpDown _numReturn;
        private readonly WinForms.NumericUpDown _numSanitary;
        private readonly WinForms.NumericUpDown _numSlope;
        private readonly WinForms.CheckBox _chkRequireMatch;
        private readonly WinForms.CheckBox _chkUnknown;
        private readonly WinForms.CheckBox _chkConnected;

        public MultiConnectOptions Options { get; private set; }

        public MultiConnectOptionsForm(MultiConnectOptions options)
        {
            Options = options;
            Text = "多點接入主管設定";
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            Width = 390;
            Height = 320;

            var lblSupply = new WinForms.Label { Left = 16, Top = 20, Width = 150, Text = "CHWS 延伸 (mm)" };
            _numSupply = CreateNumber(180, 16, options.SupplyLengthMm, 0, 5000, 50);

            var lblReturn = new WinForms.Label { Left = 16, Top = 56, Width = 150, Text = "CHWR 延伸 (mm)" };
            _numReturn = CreateNumber(180, 52, options.ReturnLengthMm, 0, 5000, 50);

            var lblSanitary = new WinForms.Label { Left = 16, Top = 92, Width = 150, Text = "CONDENSATE 延伸 (mm)" };
            _numSanitary = CreateNumber(180, 88, options.SanitaryLengthMm, 0, 5000, 50);

            var lblSlope = new WinForms.Label { Left = 16, Top = 128, Width = 150, Text = "排水坡度 1:" };
            _numSlope = CreateNumber(180, 124, options.SanitarySlopeRatio, 1, 500, 5);

            _chkRequireMatch = new WinForms.CheckBox
            {
                Left = 16,
                Top = 164,
                Width = 320,
                Text = "只接入相同系統主管",
                Checked = options.RequireMatchingSystem
            };

            _chkUnknown = new WinForms.CheckBox
            {
                Left = 16,
                Top = 194,
                Width = 320,
                Text = "包含未分類 Connector",
                Checked = options.IncludeUnknownSystem
            };

            _chkConnected = new WinForms.CheckBox
            {
                Left = 16,
                Top = 224,
                Width = 320,
                Text = "包含已連接 Connector",
                Checked = options.IncludeConnectedConnectors
            };

            var btnOk = new WinForms.Button { Left = 205, Top = 254, Width = 75, Text = "確定", DialogResult = WinForms.DialogResult.OK };
            var btnCancel = new WinForms.Button { Left = 290, Top = 254, Width = 75, Text = "取消", DialogResult = WinForms.DialogResult.Cancel };
            btnOk.Click += (_, __) => SaveOptions();

            Controls.AddRange(new WinForms.Control[]
            {
                lblSupply, _numSupply,
                lblReturn, _numReturn,
                lblSanitary, _numSanitary,
                lblSlope, _numSlope,
                _chkRequireMatch, _chkUnknown, _chkConnected,
                btnOk, btnCancel
            });

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private static WinForms.NumericUpDown CreateNumber(int left, int top, double value, decimal min, decimal max, decimal increment)
        {
            return new WinForms.NumericUpDown
            {
                Left = left,
                Top = top,
                Width = 120,
                Minimum = min,
                Maximum = max,
                Increment = increment,
                DecimalPlaces = 0,
                Value = Math.Max(min, Math.Min(max, (decimal)value))
            };
        }

        private void SaveOptions()
        {
            Options = new MultiConnectOptions
            {
                SupplyLengthMm = (double)_numSupply.Value,
                ReturnLengthMm = (double)_numReturn.Value,
                SanitaryLengthMm = (double)_numSanitary.Value,
                SanitarySlopeRatio = (double)_numSlope.Value,
                RequireMatchingSystem = _chkRequireMatch.Checked,
                IncludeUnknownSystem = _chkUnknown.Checked,
                IncludeConnectedConnectors = _chkConnected.Checked
            };
        }
    }
}
