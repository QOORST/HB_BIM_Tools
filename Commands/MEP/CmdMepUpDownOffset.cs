using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    /// <summary>
    /// Creates a manual up/down offset for one straight Pipe or Duct by replacing it with multiple
    /// copied MEPCurve segments and elbow fittings.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMepUpDownOffset : IExternalCommand
    {
        private const double DirectionTolerance = 1e-6;
        private static MepUpDownOffsetOptions quickOptions;
        private static Document quickDocument;

        private static bool HasQuickOptions(Document doc)
        {
            // Revit may expose different managed wrappers for the same native document.
            return quickOptions != null && doc != null && doc.IsValidObject &&
                quickDocument != null && quickDocument.IsValidObject && quickDocument.Equals(doc);
        }

        internal static bool ConfigureQuickOptions(Document doc, double? startCenterMm = null, bool? directionUp = null)
        {
            if (doc == null || doc.IsFamilyDocument) throw new InvalidOperationException("請開啟專案文件。");
            var choices = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l=>l.ProjectElevation).Select(l=>new MepTargetLevel {UniqueId=l.UniqueId,Name=l.Name,
                    ElevationMm=UnitUtils.ConvertFromInternalUnits(l.ProjectElevation,UnitTypeId.Millimeters)}).ToList();
            var current = HasQuickOptions(doc) ? quickOptions : null;
            using (var form = new MepQuickDrawingForm(current ?? MepUpDownOffsetOptions.QuickDefault(), choices, startCenterMm, directionUp))
            {
                if (form.ShowDialog() != WinForms.DialogResult.OK) return false;
                quickOptions = form.Options;
                quickDocument = doc;
                return true;
            }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ExecuteCore(commandData, ref message, elements, null);
        }

        internal static Result ExecuteCore(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements,
            bool? forcedOffsetUp)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                Reference pickedRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new MepCurveSelectionFilter(),
                    "請選取要上下翻彎的直線 Pipe 或 Duct");

                MEPCurve target = doc.GetElement(pickedRef) as MEPCurve;
                if (target == null)
                {
                    TaskDialog.Show("MEP 上下翻彎", "請選取 Pipe 或 Duct。");
                    return Result.Cancelled;
                }

                Line line = GetLine(target);
                if (line == null)
                {
                    TaskDialog.Show("MEP 上下翻彎", "目前僅支援直線 Pipe 或 Duct。");
                    return Result.Cancelled;
                }

                MepUpDownOffsetOptions options = MepUpDownOffsetOptions.Default();
                if (forcedOffsetUp.HasValue)
                {
                    options.OffsetUp = forcedOffsetUp.Value;
                }

                using (var form = new MepUpDownOffsetOptionsForm(options, forcedOffsetUp))
                {
                    if (form.ShowDialog() != WinForms.DialogResult.OK)
                    {
                        return Result.Cancelled;
                    }

                    options = form.Options;
                }

                if (forcedOffsetUp.HasValue)
                {
                    options.OffsetUp = forcedOffsetUp.Value;
                }

                XYZ pickedPoint = pickedRef.GlobalPoint ?? line.Evaluate(0.5, true);
                if (!TryBuildOffsetPath(line, pickedPoint, options, out List<XYZ> path, out string pathFailReason))
                {
                    TaskDialog.Show("MEP 上下翻彎", pathFailReason);
                    return Result.Cancelled;
                }

                if (!ConfirmOffsetPreview(target, line, path, options))
                {
                    return Result.Cancelled;
                }

                List<ElementId> segmentIds;
                int elbowCount;
                using (Transaction tx = new Transaction(doc, "MEP 上下翻彎"))
                {
                    tx.Start();

                    if (!TryReplaceWithSegments(doc, target, path, out segmentIds, out elbowCount, out string replaceFailReason))
                    {
                        tx.RollBack();
                        TaskDialog.Show("MEP 上下翻彎", replaceFailReason);
                        return Result.Cancelled;
                    }

                    tx.Commit();
                }

                TaskDialog.Show(
                    "MEP 上下翻彎",
                    "完成上下翻彎。\n\n" +
                    $"元素類型：{GetElementKindName(target)}\n" +
                    $"新管段數：{segmentIds.Count}\n" +
                    $"彎頭數：{elbowCount}\n" +
                    $"方向：{(options.OffsetUp ? "上翻" : "下翻")}\n" +
                    $"角度：{(options.UseNinetyDegree ? "90°" : $"{options.AngleDegrees:0}°")}\n" +
                    $"偏移高度：約 {options.OffsetHeightMm:0.#} mm");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MEP 上下翻彎", $"執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }

        internal static Result ExecuteEndOffset(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements,
            bool offsetUp,
            bool quick = false)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                Reference pickedRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new MepCurveSelectionFilter(true),
                    offsetUp ? "選取管、電管、風管或電纜線架的未連接端附近，建立上行" : "選取管、電管、風管或電纜線架的未連接端附近，建立下行");

                MEPCurve target = doc.GetElement(pickedRef) as MEPCurve;
                Line line = GetLine(target);
                if (target == null || line == null)
                {
                    TaskDialog.Show("MEP 末端上下行", "請選取直線管、電管、風管或電纜線架。");
                    return Result.Cancelled;
                }

                if (Math.Sqrt(line.Direction.X * line.Direction.X + line.Direction.Y * line.Direction.Y) <= DirectionTolerance)
                {
                    TaskDialog.Show("MEP 末端上下行", "此管段為立管，無法判定水平延伸方向；請選取水平或帶坡度的直線段。");
                    return Result.Cancelled;
                }

                MepUpDownOffsetOptions options = MepUpDownOffsetOptions.Default();
                options.OffsetUp = offsetUp;
                if (quick)
                {
                    XYZ selectedPoint = pickedRef.GlobalPoint ?? line.Evaluate(0.5, true);
                    int selectedEnd = selectedPoint.DistanceTo(line.GetEndPoint(0)) <= selectedPoint.DistanceTo(line.GetEndPoint(1)) ? 0 : 1;
                    double selectedZ = UnitUtils.ConvertFromInternalUnits(line.GetEndPoint(selectedEnd).Z, UnitTypeId.Millimeters);
                    if (!HasQuickOptions(doc) && !ConfigureQuickOptions(doc, selectedZ, offsetUp)) return Result.Cancelled;
                    options = new MepUpDownOffsetOptions {
                        OffsetUp=offsetUp, UseNinetyDegree=quickOptions.UseNinetyDegree,
                        AngleDegrees=quickOptions.AngleDegrees, OffsetHeightMm=quickOptions.OffsetHeightMm,
                        MiddleLengthMm=quickOptions.MiddleLengthMm, UseTargetLevel=quickOptions.UseTargetLevel,
                        TargetLevelUniqueId=quickOptions.TargetLevelUniqueId, TargetLevelOffsetMm=quickOptions.TargetLevelOffsetMm };
                }
                else
                {
                    using (var form = new MepUpDownOffsetOptionsForm(options, offsetUp, true))
                    {
                        if (form.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;
                        options = form.Options;
                        options.OffsetUp = offsetUp;
                    }
                }

                XYZ pickedPoint = pickedRef.GlobalPoint ?? line.Evaluate(0.5, true);
                int endIndex = pickedPoint.DistanceTo(line.GetEndPoint(0)) <= pickedPoint.DistanceTo(line.GetEndPoint(1))
                    ? 0
                    : 1;

                if (options.UseTargetLevel)
                {
                    var level = string.IsNullOrEmpty(options.TargetLevelUniqueId) ? null : doc.GetElement(options.TargetLevelUniqueId) as Level;
                    if (level == null) throw new InvalidOperationException("目標樓層已不存在，請重新開啟上下行設定選擇樓層。");
                    double startMm = UnitUtils.ConvertFromInternalUnits(line.GetEndPoint(endIndex).Z, UnitTypeId.Millimeters);
                    double levelMm = UnitUtils.ConvertFromInternalUnits(level.ProjectElevation, UnitTypeId.Millimeters);
                    double delta = MepEndOffsetMath.TargetDeltaMm(startMm, levelMm, options.TargetLevelOffsetMm);
                    if (Math.Abs(delta) < 10) throw new InvalidOperationException("目標管中心與目前端點高差不足 10 mm，不需建立上下行管段。");
                    options.OffsetUp = delta > 0;
                    options.OffsetHeightMm = Math.Abs(delta);
                    var confirm = new TaskDialog("目標樓層定位") {
                        MainInstruction=$"{(options.OffsetUp ? "上行" : "下行")} {Math.Abs(delta):0.###} mm → {level.Name}",
                        MainContent=$"管中心偏移：{options.TargetLevelOffsetMm:0.###} mm\n"+
                            $"內部管中心標高：{startMm:0.###} → {startMm+delta:0.###} mm\n\n依目標自動決定方向；本次定位不建立持續樓層約束。",
                        CommonButtons=TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                        DefaultButton=TaskDialogResult.Cancel };
                    if ((!quick || options.OffsetUp != offsetUp) && confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;
                }

                if (!TryBuildEndOffsetPath(line, endIndex, options, out List<XYZ> path, out string pathFailReason))
                {
                    TaskDialog.Show("MEP 末端上下行", pathFailReason);
                    return Result.Cancelled;
                }

                Connector endConnector = GetConnectorAt(target, line.GetEndPoint(endIndex));
                if (endConnector == null || endConnector.IsConnected)
                {
                    TaskDialog.Show("MEP 末端上下行", "選取端點已連接或無有效連接器，請選擇未連接端點再執行。");
                    return Result.Cancelled;
                }

                if (!quick && !ConfirmEndOffsetPreview(target, path, options, endIndex))
                {
                    return Result.Cancelled;
                }

                List<ElementId> segmentIds;
                int elbowCount;
                using (Transaction tx = new Transaction(doc, options.OffsetUp ? "MEP 末端上行" : "MEP 末端下行"))
                {
                    tx.Start();
                    if (!TryCreateEndOffsetSegments(doc, target, path, endIndex, out segmentIds, out elbowCount, out string failReason))
                    {
                        tx.RollBack();
                        TaskDialog.Show("MEP 末端上下行", failReason);
                        return Result.Cancelled;
                    }

                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("末端上下行交易未完成；請檢查接頭或模型錯誤。");
                }

                if (!quick) TaskDialog.Show(
                    "MEP 末端上下行",
                    "完成末端上下行。\n\n" +
                    $"建立管段：{segmentIds.Count}\n" +
                    $"建立彎頭：{elbowCount}\n" +
                    $"方向：{(offsetUp ? "上行" : "下行")}\n" +
                    $"角度：{(options.UseNinetyDegree ? "90°" : $"{options.AngleDegrees:0}°")}\n" +
                    $"偏移高度：{options.OffsetHeightMm:0.#} mm");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MEP 末端上下行", $"執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static bool TryBuildOffsetPath(
            Line sourceLine,
            XYZ pickedPoint,
            MepUpDownOffsetOptions options,
            out List<XYZ> path,
            out string failReason)
        {
            path = null;
            failReason = null;

            XYZ start = sourceLine.GetEndPoint(0);
            XYZ end = sourceLine.GetEndPoint(1);
            XYZ direction = (end - start).Normalize();
            double length = sourceLine.Length;
            double minSegmentLength = UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);
            double minEndStraight = UnitUtils.ConvertToInternalUnits(50.0, UnitTypeId.Millimeters);
            double offsetHeight = UnitUtils.ConvertToInternalUnits(options.OffsetHeightMm, UnitTypeId.Millimeters);
            double middleLength = UnitUtils.ConvertToInternalUnits(options.MiddleLengthMm, UnitTypeId.Millimeters);
            double sign = options.OffsetUp ? 1.0 : -1.0;

            if (offsetHeight < minSegmentLength)
            {
                failReason = "偏移高度過小，無法建立上下翻彎。";
                return false;
            }

            double pickedStation = (pickedPoint - start).DotProduct(direction);
            pickedStation = Math.Max(0.0, Math.Min(length, pickedStation));

            double run;
            if (options.UseNinetyDegree)
            {
                run = 0.0;
            }
            else
            {
                double angleRadians = options.AngleDegrees * Math.PI / 180.0;
                if (angleRadians <= 0.0 || Math.Abs(Math.Tan(angleRadians)) < DirectionTolerance)
                {
                    failReason = "角度設定無效，請使用 45、30、20 或 15 度。";
                    return false;
                }

                run = Math.Abs(offsetHeight) / Math.Tan(angleRadians);
            }

            double requiredSpan = options.UseNinetyDegree
                ? middleLength
                : middleLength + run * 2.0;
            double requiredLength = requiredSpan + minEndStraight * 2.0;
            if (length < requiredLength || requiredSpan <= minSegmentLength)
            {
                double needMm = UnitUtils.ConvertFromInternalUnits(requiredLength, UnitTypeId.Millimeters);
                double actualMm = UnitUtils.ConvertFromInternalUnits(length, UnitTypeId.Millimeters);
                failReason =
                    "管線長度不足，無法放入翻彎。\n\n" +
                    $"管線長度：約 {actualMm:0.#} mm\n" +
                    $"至少需要：約 {needMm:0.#} mm\n" +
                    "請縮小偏移高度或中段長度。";
                return false;
            }

            double minCenterStation = minEndStraight + requiredSpan / 2.0;
            double maxCenterStation = length - minEndStraight - requiredSpan / 2.0;
            if (minCenterStation > maxCenterStation)
            {
                failReason = "可用直管長度不足，無法建立翻彎。";
                return false;
            }

            // If the user picks too close to either pipe end, keep the intended operation
            // and move the offset center to the nearest valid station instead of failing.
            pickedStation = Math.Max(minCenterStation, Math.Min(maxCenterStation, pickedStation));

            double p1Station = pickedStation - middleLength / 2.0 - run;
            double p2Station = pickedStation + middleLength / 2.0 + run;
            if (options.UseNinetyDegree)
            {
                p1Station = pickedStation - middleLength / 2.0;
                p2Station = pickedStation + middleLength / 2.0;
            }

            if (p1Station < minEndStraight || p2Station > length - minEndStraight || p2Station <= p1Station)
            {
                double needMm = UnitUtils.ConvertFromInternalUnits((p2Station - p1Station) + minEndStraight * 2.0, UnitTypeId.Millimeters);
                double actualMm = UnitUtils.ConvertFromInternalUnits(length, UnitTypeId.Millimeters);
                failReason =
                    "所選位置附近的管線長度不足，無法放入翻彎。\n\n" +
                    $"管線長度：約 {actualMm:0.#} mm\n" +
                    $"至少需要：約 {needMm:0.#} mm\n" +
                    "請改選靠近管線中間的位置，或縮小偏移高度/中段長度。";
                return false;
            }

            XYZ p1 = start + direction.Multiply(p1Station);
            XYZ p2 = start + direction.Multiply(p2Station);
            XYZ vertical = XYZ.BasisZ.Multiply(offsetHeight * sign);

            if (options.UseNinetyDegree)
            {
                path = CleanPath(new List<XYZ>
                {
                    start,
                    p1,
                    p1 + vertical,
                    p2 + vertical,
                    p2,
                    end
                }, minSegmentLength);
            }
            else
            {
                XYZ q1Base = start + direction.Multiply(pickedStation - middleLength / 2.0);
                XYZ q2Base = start + direction.Multiply(pickedStation + middleLength / 2.0);
                path = CleanPath(new List<XYZ>
                {
                    start,
                    p1,
                    q1Base + vertical,
                    q2Base + vertical,
                    p2,
                    end
                }, minSegmentLength);
            }

            if (path.Count < 3)
            {
                failReason = "計算後有效管段不足，無法建立翻彎。";
                return false;
            }

            return true;
        }

        private static List<XYZ> CleanPath(List<XYZ> points, double minSegmentLength)
        {
            var result = new List<XYZ>();
            foreach (XYZ point in points)
            {
                if (result.Count == 0 || result.Last().DistanceTo(point) >= minSegmentLength)
                {
                    result.Add(point);
                }
            }

            return result;
        }

        private static bool ConfirmOffsetPreview(
            MEPCurve target,
            Line sourceLine,
            List<XYZ> path,
            MepUpDownOffsetOptions options)
        {
            int segmentCount = Math.Max(0, path.Count - 1);
            int expectedElbows = Math.Max(0, segmentCount - 1);
            double originalLengthMm = UnitUtils.ConvertFromInternalUnits(sourceLine.Length, UnitTypeId.Millimeters);
            double newLengthMm = UnitUtils.ConvertFromInternalUnits(GetPathLength(path), UnitTypeId.Millimeters);
            double angleRunMm = 0.0;

            if (!options.UseNinetyDegree)
            {
                double angleRadians = options.AngleDegrees * Math.PI / 180.0;
                angleRunMm = options.OffsetHeightMm / Math.Tan(angleRadians);
            }

            string mode = options.UseNinetyDegree ? "90° 垂直翻彎" : $"{options.AngleDegrees:0}° 斜管翻彎";
            string direction = options.OffsetUp ? "上翻" : "下翻";

            var dialog = new TaskDialog("MEP 上下翻彎預覽")
            {
                MainInstruction = "確認是否套用上下翻彎？",
                MainContent =
                    $"元素類型：{GetElementKindName(target)}\n" +
                    $"翻彎方向：{direction}\n" +
                    $"翻彎方式：{mode}\n" +
                    $"偏移高度：{options.OffsetHeightMm:0.#} mm\n" +
                    $"避讓中段：{options.MiddleLengthMm:0.#} mm\n" +
                    (options.UseNinetyDegree ? string.Empty : $"單側水平退距：約 {angleRunMm:0.#} mm\n") +
                    $"原長度：約 {originalLengthMm:0.#} mm\n" +
                    $"新路徑總長：約 {newLengthMm:0.#} mm\n" +
                    $"預計分段：{segmentCount} 段\n" +
                    $"必要彎頭：{expectedElbows} 個\n\n" +
                    "按「確定」後會以多段新管線取代原元素，若彎頭建立不完整會自動回復。",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };

            return dialog.Show() == TaskDialogResult.Ok;
        }

        private static double GetPathLength(List<XYZ> path)
        {
            double length = 0.0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                length += path[i].DistanceTo(path[i + 1]);
            }

            return length;
        }

        private static bool TryBuildEndOffsetPath(
            Line sourceLine,
            int endIndex,
            MepUpDownOffsetOptions options,
            out List<XYZ> path,
            out string failReason)
        {
            path = null;
            failReason = null;

            XYZ start = sourceLine.GetEndPoint(0);
            XYZ end = sourceLine.GetEndPoint(1);
            XYZ basePoint = sourceLine.GetEndPoint(endIndex);
            XYZ outward = endIndex == 0
                ? (start - end).Normalize()
                : (end - start).Normalize();
            // Offset angles reference the world XY plane, not the source pipe's slope.
            if (!MepEndOffsetMath.TryGetPlanDirection(outward.X, outward.Y, out double planX, out double planY))
            {
                failReason = "此管段無法判定水平延伸方向。";
                return false;
            }
            outward = new XYZ(planX, planY, 0);

            double minSegmentLength = UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);
            double offsetHeight = UnitUtils.ConvertToInternalUnits(options.OffsetHeightMm, UnitTypeId.Millimeters);
            double middleLength = UnitUtils.ConvertToInternalUnits(options.MiddleLengthMm, UnitTypeId.Millimeters);
            double sign = options.OffsetUp ? 1.0 : -1.0;
            XYZ vertical = XYZ.BasisZ.Multiply(offsetHeight * sign);

            if (offsetHeight < minSegmentLength)
            {
                failReason = "偏移高度過小，無法建立末端上下行。";
                return false;
            }

            var points = new List<XYZ> { basePoint };
            if (options.UseNinetyDegree)
            {
                points.Add(basePoint + vertical);
            }
            else
            {
                double angleRadians = options.AngleDegrees * Math.PI / 180.0;
                if (angleRadians <= 0.0 || Math.Abs(Math.Tan(angleRadians)) < DirectionTolerance)
                {
                    failReason = "角度設定不正確，請使用有效的預設或自訂角度。";
                    return false;
                }

                double run = Math.Abs(offsetHeight) / Math.Tan(angleRadians);
                if (run < minSegmentLength)
                {
                    failReason = "斜管水平距離過短，無法建立末端上下行。";
                    return false;
                }

                points.Add(basePoint + outward.Multiply(run) + vertical);
            }

            if (middleLength >= minSegmentLength)
            {
                points.Add(points.Last() + outward.Multiply(middleLength));
            }

            path = CleanPath(points, minSegmentLength);
            if (path.Count < 2)
            {
                failReason = "計算後有效管段不足，無法建立末端上下行。";
                return false;
            }

            return true;
        }

        private static bool ConfirmEndOffsetPreview(
            MEPCurve target,
            List<XYZ> path,
            MepUpDownOffsetOptions options,
            int endIndex)
        {
            double newLengthMm = UnitUtils.ConvertFromInternalUnits(GetPathLength(path), UnitTypeId.Millimeters);
            string mode = options.UseNinetyDegree ? "90° 垂直上下行" : $"{options.AngleDegrees:0}° 斜管上下行";
            var dialog = new TaskDialog("MEP 末端上下行預覽")
            {
                MainInstruction = "確認是否建立末端上下行？",
                MainContent =
                    $"元素類型：{GetElementKindName(target)}\n" +
                    $"端點：{(endIndex == 0 ? "起點端" : "終點端")}\n" +
                    $"方向：{(options.OffsetUp ? "上行" : "下行")}\n" +
                    $"方式：{mode}\n" +
                    $"偏移高度：{options.OffsetHeightMm:0.#} mm\n" +
                    $"末端水平段：{options.MiddleLengthMm:0.#} mm\n" +
                    $"新增路徑總長：約 {newLengthMm:0.#} mm\n\n" +
                    "按「確定」後會從未連接端點建立新增管段並嘗試建立彎頭；若彎頭建立不完整會自動回復。",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };

            return dialog.Show() == TaskDialogResult.Ok;
        }

        private static bool TryCreateEndOffsetSegments(
            Document doc,
            MEPCurve source,
            List<XYZ> path,
            int endIndex,
            out List<ElementId> segmentIds,
            out int elbowCount,
            out string failReason)
        {
            segmentIds = new List<ElementId>();
            elbowCount = 0;
            failReason = null;

            var segments = new List<MEPCurve>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                XYZ start = endIndex == 0 ? path[i + 1] : path[i];
                XYZ end = endIndex == 0 ? path[i] : path[i + 1];
                if (start.DistanceTo(end) <= doc.Application.ShortCurveTolerance)
                {
                    failReason = $"第 {i + 1} 段短於 Revit 最小曲線長度。";
                    return false;
                }
                MEPCurve segment;
                if (source is Conduit || source is CableTray)
                {
                    ElementId levelId = source.ReferenceLevel?.Id ?? source.LevelId;
                    if (!(doc.GetElement(levelId) is Level))
                        throw new InvalidOperationException("原電管或電纜線架缺少有效樓層基準。");
                    if (source is Conduit)
                    {
                        segment = Conduit.Create(doc, source.GetTypeId(), start, end, levelId);
                        CopySize(source, segment, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                    }
                    else
                    {
                        segment = CableTray.Create(doc, source.GetTypeId(), start, end, levelId);
                        CopySize(source, segment, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                        CopySize(source, segment, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
                    }
                }
                else
                {
                    ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElement(doc, source.Id, new XYZ(1000.0, 0.0, 0.0));
                    segment = copiedIds.Select(id => doc.GetElement(id)).OfType<MEPCurve>().FirstOrDefault();
                }
                if (segment == null)
                {
                    failReason = $"建立第 {i + 1} 段管線失敗。";
                    return false;
                }

                LocationCurve locationCurve = segment.Location as LocationCurve;
                if (locationCurve == null)
                {
                    failReason = $"第 {i + 1} 段管線沒有 LocationCurve。";
                    return false;
                }

                locationCurve.Curve = endIndex == 0
                    ? Line.CreateBound(path[i + 1], path[i])
                    : Line.CreateBound(path[i], path[i + 1]);
                segments.Add(segment);
                segmentIds.Add(segment.Id);
            }

            doc.Regenerate();

            List<MEPCurve> ordered = endIndex == 0
                ? segments.AsEnumerable().Reverse().Concat(new[] { source }).ToList()
                : new[] { source }.Concat(segments).ToList();

            elbowCount = CreateElbows(doc, ordered);
            int requiredElbowCount = Math.Max(0, ordered.Count - 1);
            if (elbowCount < requiredElbowCount)
            {
                failReason =
                    "彎頭建立不完整，已取消本次末端上下行並回復模型。\n\n" +
                    $"必要彎頭：{requiredElbowCount}\n" +
                    $"成功彎頭：{elbowCount}\n\n" +
                    "可能原因：管件族/風管管件不支援此角度、空間不足、路由偏好未設定，或端點已被其他元素占用。";
                return false;
            }

            return true;
        }

        private static void CopySize(MEPCurve source, MEPCurve target, BuiltInParameter parameterId)
        {
            Parameter from = source.get_Parameter(parameterId);
            Parameter to = target.get_Parameter(parameterId);
            if (from == null || to == null || to.IsReadOnly || !to.Set(from.AsDouble()))
                throw new InvalidOperationException("無法沿用原管線尺寸，已取消本次建立。");
        }

        private static bool TryReplaceWithSegments(
            Document doc,
            MEPCurve source,
            List<XYZ> path,
            out List<ElementId> segmentIds,
            out int elbowCount,
            out string failReason)
        {
            segmentIds = new List<ElementId>();
            elbowCount = 0;
            failReason = null;

            Line originalLine = GetLine(source);
            if (originalLine == null)
            {
                failReason = "來源元素不是直線。";
                return false;
            }

            Connector sourceStart = GetConnectorAt(source, originalLine.GetEndPoint(0));
            Connector sourceEnd = GetConnectorAt(source, originalLine.GetEndPoint(1));
            Connector externalStart = GetExternalConnector(sourceStart, source.Id);
            Connector externalEnd = GetExternalConnector(sourceEnd, source.Id);

            TryDisconnect(sourceStart, externalStart);
            TryDisconnect(sourceEnd, externalEnd);

            var segments = new List<MEPCurve>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElement(doc, source.Id, new XYZ(1000.0, 0.0, 0.0));
                MEPCurve segment = copiedIds
                    .Select(id => doc.GetElement(id))
                    .OfType<MEPCurve>()
                    .FirstOrDefault();
                if (segment == null)
                {
                    failReason = $"建立第 {i + 1} 段管線失敗。";
                    return false;
                }

                LocationCurve locationCurve = segment.Location as LocationCurve;
                if (locationCurve == null)
                {
                    failReason = $"第 {i + 1} 段管線沒有 LocationCurve。";
                    return false;
                }

                locationCurve.Curve = Line.CreateBound(path[i], path[i + 1]);
                segments.Add(segment);
                segmentIds.Add(segment.Id);
            }

            doc.Delete(source.Id);
            doc.Regenerate();

            elbowCount = CreateElbows(doc, segments);
            int requiredElbowCount = Math.Max(0, segments.Count - 1);
            if (elbowCount < requiredElbowCount)
            {
                failReason =
                    "彎頭建立不完整，已取消本次翻彎並回復模型。\n\n" +
                    $"必要彎頭：{requiredElbowCount}\n" +
                    $"成功彎頭：{elbowCount}\n\n" +
                    "可能原因：管件族/風管管件不支援此角度、空間不足、路由偏好未設定，或段長太短。";
                return false;
            }

            Connector newStart = GetConnectorAt(segments.First(), path.First());
            Connector newEnd = GetConnectorAt(segments.Last(), path.Last());
            TryConnect(newStart, externalStart);
            TryConnect(newEnd, externalEnd);
            doc.Regenerate();

            return true;
        }

        private static int CreateElbows(Document doc, List<MEPCurve> segments)
        {
            int count = 0;
            for (int i = 0; i < segments.Count - 1; i++)
            {
                XYZ joint = (GetEndPoint(segments[i], 1) + GetEndPoint(segments[i + 1], 0)) * 0.5;
                Connector first = GetConnectorAt(segments[i], joint);
                Connector second = GetConnectorAt(segments[i + 1], joint);
                if (first == null || second == null)
                {
                    continue;
                }

                try
                {
                    FamilyInstance elbow = doc.Create.NewElbowFitting(first, second);
                    if (elbow != null && elbow.IsValidObject)
                    {
                        count++;
                    }
                }
                catch
                {
                    // Caller rejects an incomplete fitting count and rolls back the transaction.
                }
            }

            return count;
        }

        private static XYZ GetEndPoint(MEPCurve curve, int index)
        {
            Line line = GetLine(curve);
            return line?.GetEndPoint(index);
        }

        private static Line GetLine(Element element)
        {
            return (element?.Location as LocationCurve)?.Curve as Line;
        }

        private static Connector GetConnectorAt(MEPCurve curve, XYZ point)
        {
            if (curve?.ConnectorManager?.Connectors == null || point == null)
            {
                return null;
            }

            double tolerance = UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Millimeters);
            return curve.ConnectorManager.Connectors
                .Cast<Connector>()
                .Where(connector => connector.ConnectorType == ConnectorType.End)
                .OrderBy(connector => connector.Origin.DistanceTo(point))
                .FirstOrDefault(connector => connector.Origin.DistanceTo(point) <= tolerance);
        }

        private static Connector GetExternalConnector(Connector connector, ElementId ownerId)
        {
            if (connector == null || !connector.IsConnected)
            {
                return null;
            }

            foreach (Connector connected in connector.AllRefs)
            {
                if (connected.Owner != null && connected.Owner.Id != ownerId)
                {
                    return connected;
                }
            }

            return null;
        }

        private static void TryDisconnect(Connector first, Connector second)
        {
            try
            {
                if (first != null && second != null)
                {
                    first.DisconnectFrom(second);
                }
            }
            catch
            {
            }
        }

        private static void TryConnect(Connector first, Connector second)
        {
            try
            {
                if (first != null && second != null && !first.IsConnectedTo(second))
                {
                    first.ConnectTo(second);
                }
            }
            catch
            {
            }
        }

        private static string GetElementKindName(Element element)
        {
            if (element is Pipe) return "Pipe";
            if (element is Duct) return "Duct";
            if (element is Conduit) return "電管";
            if (element is CableTray) return "電纜線架";
            return element?.GetType().Name ?? "MEP";
        }

        private class MepCurveSelectionFilter : ISelectionFilter
        {
            private readonly bool includeElectrical;
            public MepCurveSelectionFilter(bool includeElectrical = false) { this.includeElectrical = includeElectrical; }
            public bool AllowElement(Element elem)
            {
                return elem is Pipe || elem is Duct || (includeElectrical && (elem is Conduit || elem is CableTray));
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdMepManualUpOffset : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CmdMepUpDownOffset.ExecuteCore(commandData, ref message, elements, true);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdMepManualDownOffset : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CmdMepUpDownOffset.ExecuteCore(commandData, ref message, elements, false);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdMepEndUpOffset : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CmdMepUpDownOffset.ExecuteEndOffset(commandData, ref message, elements, true);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdMepEndDownOffset : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CmdMepUpDownOffset.ExecuteEndOffset(commandData, ref message, elements, false);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdMepQuickUp : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => CmdMepUpDownOffset.ExecuteEndOffset(data, ref message, elements, true, true);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdMepQuickDown : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => CmdMepUpDownOffset.ExecuteEndOffset(data, ref message, elements, false, true);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CmdMepQuickSettings : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try { return CmdMepUpDownOffset.ConfigureQuickOptions(data.Application.ActiveUIDocument?.Document) ? Result.Succeeded : Result.Cancelled; }
            catch (Exception ex) { message=ex.Message;TaskDialog.Show("快速上下行設定",message);return Result.Failed; }
        }
    }

    internal static class MepEndOffsetMath
    {
        internal static double TargetDeltaMm(double startMm, double levelMm, double offsetMm)
        {
            double delta=levelMm+offsetMm-startMm;
            if (double.IsNaN(delta) || double.IsInfinity(delta)) throw new ArgumentException("標高必須為有效數值。");
            return delta;
        }
        internal static bool TryGetPlanDirection(double x, double y, out double planX, out double planY)
        {
            planX = planY = 0;
            double length = Math.Sqrt(x*x + y*y);
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 1e-6) return false;
            planX = x / length;
            planY = y / length;
            return true;
        }
    }

    internal sealed class MepTargetLevel
    {
        public string UniqueId {get;set;}
        public string Name {get;set;}
        public double ElevationMm {get;set;}
        public override string ToString() => Name;
    }

    internal class MepUpDownOffsetOptions
    {
        public static MepUpDownOffsetOptions QuickDefault()
        {
            var options = Default();
            options.UseNinetyDegree = true;
            options.MiddleLengthMm = 0;
            return options;
        }
        public bool OffsetUp { get; set; }
        public bool UseNinetyDegree { get; set; }
        public double AngleDegrees { get; set; }
        public double OffsetHeightMm { get; set; }
        public double MiddleLengthMm { get; set; }
        public bool UseTargetLevel {get;set;}
        public string TargetLevelUniqueId {get;set;}
        public double TargetLevelOffsetMm {get;set;}

        public static MepUpDownOffsetOptions Default()
        {
            return new MepUpDownOffsetOptions
            {
                OffsetUp = true,
                UseNinetyDegree = false,
                AngleDegrees = 45.0,
                OffsetHeightMm = 300.0,
                MiddleLengthMm = 500.0
            };
        }
    }

    internal class MepUpDownOffsetOptionsForm : WinForms.Form
    {
        private readonly WinForms.RadioButton _rbUp;
        private readonly WinForms.RadioButton _rbDown;
        private readonly WinForms.RadioButton _rbNinety;
        private readonly WinForms.RadioButton _rbAngle;
        private readonly WinForms.ComboBox _cmbAngle;
        private readonly WinForms.NumericUpDown _numCustomAngle;
        private readonly WinForms.NumericUpDown _numOffsetHeight;
        private readonly WinForms.NumericUpDown _numMiddleLength;
        private readonly WinForms.ComboBox _targetMode;
        private readonly WinForms.ComboBox _targetLevel;
        private readonly WinForms.NumericUpDown _targetOffset;

        public MepUpDownOffsetOptions Options { get; private set; }

        public MepUpDownOffsetOptionsForm(MepUpDownOffsetOptions options, bool? forcedOffsetUp = null, bool endMode = false,
            List<MepTargetLevel> targetLevels = null)
        {
            Options = options;
            Text = forcedOffsetUp.HasValue
                ? (forcedOffsetUp.Value ? "MEP 手動上翻" : "MEP 手動下翻")
                : "MEP 上下翻彎";
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            Width = 360;
            Height = 300;

            var lblDirection = new WinForms.Label { Left = 16, Top = 18, Width = 100, Text = "翻彎方向" };
            _rbUp = new WinForms.RadioButton { Left = 120, Top = 16, Width = 70, Text = "上翻" };
            _rbDown = new WinForms.RadioButton { Left = 200, Top = 16, Width = 70, Text = "下翻" };

            var lblMode = new WinForms.Label { Left = 16, Top = 54, Width = 100, Text = "翻彎角度" };
            _rbNinety = new WinForms.RadioButton { Left = 120, Top = 52, Width = 70, Text = "90°" };
            _rbAngle = new WinForms.RadioButton { Left = 200, Top = 52, Width = 70, Text = "斜管" };
            _cmbAngle = new WinForms.ComboBox
            {
                Left = 120,
                Top = 84,
                Width = 120,
                DropDownStyle = WinForms.ComboBoxStyle.DropDownList
            };
            _cmbAngle.Items.AddRange(new object[] { "45", "30", "20", "15", "自訂" });
            _numCustomAngle = new WinForms.NumericUpDown {
                Left=248, Top=84, Width=80, Minimum=1, Maximum=89,
                DecimalPlaces=1, Increment=0.5m, Visible=false };
            _cmbAngle.SelectedIndexChanged += (_, __) => {
                _numCustomAngle.Visible = (_cmbAngle.SelectedItem as string) == "自訂";
            };
            _rbAngle.CheckedChanged += (_, __) => {
                _cmbAngle.Enabled = _rbAngle.Checked;
                _numCustomAngle.Enabled = _rbAngle.Checked;
            };

            var lblOffset = new WinForms.Label { Left = 16, Top = 124, Width = 120, Text = "偏移高度 (mm)" };
            _numOffsetHeight = new WinForms.NumericUpDown
            {
                Left = 150,
                Top = 120,
                Width = 120,
                Minimum = 10,
                Maximum = 5000,
                DecimalPlaces = 0,
                Increment = 50
            };

            var lblMiddle = new WinForms.Label { Left = 16, Top = 160, Width = 120, Text = endMode ? "末端水平段 (mm)" : "避讓中段 (mm)" };
            _numMiddleLength = new WinForms.NumericUpDown
            {
                Left = 150,
                Top = 156,
                Width = 120,
                Minimum = 0,
                Maximum = 10000,
                DecimalPlaces = 0,
                Increment = 50
            };

            var btnOk = new WinForms.Button { Left = 170, Top = 210, Width = 75, Text = "確定", DialogResult = WinForms.DialogResult.OK };
            var btnCancel = new WinForms.Button { Left = 255, Top = 210, Width = 75, Text = "取消", DialogResult = WinForms.DialogResult.Cancel };
            btnOk.Click += (_, __) => {
                if (_targetMode?.SelectedIndex == 1 && !(_targetLevel.SelectedItem is MepTargetLevel))
                {
                    DialogResult=WinForms.DialogResult.None;
                    WinForms.MessageBox.Show(this,"請選擇有效的目標樓層。","上下行設定");
                    return;
                }
                SaveOptions();
            };

            // Radio buttons are exclusive within a parent: direction and angle need separate groups.
            var directionPanel = new WinForms.Panel { Left=0, Top=0, Width=340, Height=46 };
            directionPanel.Controls.AddRange(new WinForms.Control[] {lblDirection, _rbUp, _rbDown});
            var anglePanel = new WinForms.Panel { Left=0, Top=48, Width=340, Height=34 };
            lblMode.Top=6;_rbNinety.Top=4;_rbAngle.Top=4;
            anglePanel.Controls.AddRange(new WinForms.Control[] {lblMode, _rbNinety, _rbAngle});
            Controls.AddRange(new WinForms.Control[]
            {
                directionPanel, anglePanel, _cmbAngle, _numCustomAngle,
                lblOffset, _numOffsetHeight,
                lblMiddle, _numMiddleLength,
                btnOk, btnCancel
            });

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            _rbUp.Checked = options.OffsetUp;
            _rbDown.Checked = !options.OffsetUp;
            if (forcedOffsetUp.HasValue)
            {
                _rbUp.Enabled = false;
                _rbDown.Enabled = false;
            }

            _rbNinety.Checked = options.UseNinetyDegree;
            _rbAngle.Checked = !options.UseNinetyDegree;
            _cmbAngle.SelectedItem = options.AngleDegrees.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
            _numCustomAngle.Value = Math.Max(1m, Math.Min(89m, (decimal)options.AngleDegrees));
            if (_cmbAngle.SelectedIndex < 0) _cmbAngle.SelectedItem = "自訂";
            _cmbAngle.Enabled = _rbAngle.Checked;
            _numCustomAngle.Enabled = _rbAngle.Checked;
            _numOffsetHeight.Value = (decimal)options.OffsetHeightMm;
            _numMiddleLength.Value = (decimal)options.MiddleLengthMm;
            if (targetLevels != null)
            {
                Height=420;btnOk.Top=340;btnCancel.Top=340;
                lblDirection.Text="相對高度方向";
                _targetMode=new WinForms.ComboBox {Left=150,Top=196,Width=178,DropDownStyle=WinForms.ComboBoxStyle.DropDownList};
                _targetMode.Items.AddRange(new object[]{"相對高度","目標樓層＋偏移"});
                _targetLevel=new WinForms.ComboBox {Left=150,Top=232,Width=178,DropDownStyle=WinForms.ComboBoxStyle.DropDownList};
                foreach(var level in targetLevels) _targetLevel.Items.Add(level);
                _targetLevel.SelectedItem=targetLevels.FirstOrDefault(l=>l.UniqueId==options.TargetLevelUniqueId);
                _targetOffset=new WinForms.NumericUpDown {Left=150,Top=268,Width=178,Minimum=-1000000,Maximum=1000000,DecimalPlaces=1,Increment=50,
                    Value=(decimal)options.TargetLevelOffsetMm};
                Controls.AddRange(new WinForms.Control[]{
                    new WinForms.Label{Left=16,Top=200,Width=130,Text="定位方式"},_targetMode,
                    new WinForms.Label{Left=16,Top=236,Width=130,Text="目標樓層"},_targetLevel,
                    new WinForms.Label{Left=16,Top=272,Width=130,Text="管中心偏移 (mm)"},_targetOffset});
                _targetMode.SelectedIndexChanged+=(_,__)=>{
                    bool target=_targetMode.SelectedIndex==1;
                    _targetLevel.Enabled=target;_targetOffset.Enabled=target;_numOffsetHeight.Enabled=!target;
                    directionPanel.Visible=!target;
                };
                _targetMode.SelectedIndex=options.UseTargetLevel ? 1 : 0;
            }
        }

        private void SaveOptions()
        {
            double angle = (_cmbAngle.SelectedItem as string) == "自訂"
                ? (double)_numCustomAngle.Value
                : double.Parse(_cmbAngle.SelectedItem.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            Options = new MepUpDownOffsetOptions
            {
                OffsetUp = _rbUp.Checked,
                UseNinetyDegree = _rbNinety.Checked,
                AngleDegrees = angle,
                OffsetHeightMm = (double)_numOffsetHeight.Value,
                MiddleLengthMm = (double)_numMiddleLength.Value,
                UseTargetLevel = _targetMode?.SelectedIndex == 1,
                TargetLevelUniqueId = (_targetLevel?.SelectedItem as MepTargetLevel)?.UniqueId,
                TargetLevelOffsetMm = _targetOffset == null ? 0 : (double)_targetOffset.Value
            };
        }
    }
}
