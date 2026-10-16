using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdAutoVoidCut : IExternalCommand
    {
        private sealed class PickFilter : ISelectionFilter
        {
            private readonly bool cutters;
            internal PickFilter(bool cutters) { this.cutters = cutters; }
            public bool AllowElement(Element element) => cutters
                ? element is FamilyInstance && InstanceVoidCutUtils.IsVoidInstanceCuttingElement(element)
                : IsTarget(element);
            public bool AllowReference(Reference reference, XYZ point) => false;
        }

        private static bool IsTarget(Element element)
        {
            if (ElementClassifier.TryClassify(element) != null) return true;
#if REVIT2024 || REVIT2025 || REVIT2026
            // Subdivisions are selected explicitly; never substitute their host.
            if (element is Toposolid) return true;
#endif
            return false;
        }

        private sealed class PairFailures : IFailuresPreprocessor
        {
            internal string Reason;
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                var messages = accessor.GetFailureMessages();
                if (messages.Count == 0) return FailureProcessingResult.Continue;
                Reason = string.Join("；", messages.Select(x => x.GetDescriptionText()));
                return FailureProcessingResult.ProceedWithRollBack;
            }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (!LicenseManager.Instance.HasFeatureAccess("AutoJoin"))
            {
                TaskDialog.Show("授權錯誤", "您的授權不包含接合工具功能。");
                return Result.Cancelled;
            }
            var ui = data.Application.ActiveUIDocument;
            if (ui == null) return Result.Cancelled;
            var doc = ui.Document;
            if (doc.IsFamilyDocument || doc.IsReadOnly)
            {
                TaskDialog.Show("自動切割", "請在可編輯的專案文件執行。");
                return Result.Cancelled;
            }
            try
            {
                bool currentView;
                HashSet<string> targetKeys;
                using (var scope = new VoidCutScopeForm())
                {
                    if (scope.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;
                    currentView = scope.CurrentView;
                    targetKeys = scope.TargetKeys;
                }
                if (currentView && !FilteredElementCollector.IsViewValidForElementIteration(doc, doc.ActiveView.Id))
                {
                    TaskDialog.Show("自動切割", "目前視圖無法收集模型構件，請切換模型視圖或改用手動選取。");
                    return Result.Cancelled;
                }
                // In view mode the picked instances define exact type IDs, not name substrings.
                var cutters = ui.Selection.PickObjects(ObjectType.Element, new PickFilter(true),
                    currentView ? "選取切割來源的代表元件；將處理目前視圖內相同族與類型的所有可切割元件。" :
                    "步驟 1/2：選取含可切割空心的水溝或族元件，完成後按完成。")
                    .Select(x => x.ElementId).Distinct().Select(doc.GetElement).ToList();
                if (cutters.Count == 0) return Result.Cancelled;
                List<Element> floors;
                if (currentView)
                {
                    var types = new HashSet<ElementId>(cutters.Select(x => x.GetTypeId()));
                    var visible = new FilteredElementCollector(doc, doc.ActiveView.Id).WhereElementIsNotElementType()
                        .Where(x => !x.IsHidden(doc.ActiveView) && (!doc.ActiveView.IsTemporaryHideIsolateActive() ||
                            doc.ActiveView.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, x.Id))).ToList();
                    cutters = visible.Where(x => x is FamilyInstance && types.Contains(x.GetTypeId()) &&
                        InstanceVoidCutUtils.IsVoidInstanceCuttingElement(x)).ToList();
                    floors = visible.Where(x => targetKeys.Contains(TargetKey(x))).ToList();
                }
                else
                    floors = ui.Selection.PickObjects(ObjectType.Element, new PickFilter(false),
                        "步驟 2/2：選取被切割構件（接合工具類別、地形實體或子分區），完成後按完成。")
                        .Select(x => x.ElementId).Distinct().Select(doc.GetElement).ToList();
                if (floors.Count == 0 || cutters.Count == 0)
                {
                    TaskDialog.Show("自動切割", "目前範圍內沒有符合的切割來源或被切割構件。模型未變更。");
                    return Result.Cancelled;
                }
                if (cutters.Count > VoidCutPolicy.MaximumSelection || floors.Count > VoidCutPolicy.MaximumSelection)
                {
                    TaskDialog.Show("自動切割", "每次各選取最多 500 個元件，請縮小範圍。模型未變更。");
                    return Result.Cancelled;
                }
                var issues = new List<JoinFailureDetail>();
                foreach (var target in floors.Where(x => !InstanceVoidCutUtils.CanBeCutWithVoid(x)).ToList())
                {
                    AddIssue(issues, target, null, "原生 API 不允許此構件使用族空心切割；未改切母地形或其他構件。");
                    floors.Remove(target);
                }
                var floorBounds = floors.ToDictionary(x => x.Id, Bounds);
                var candidates = new List<Tuple<Element, Element>>();
                int existing = 0;
                foreach (var cutter in cutters)
                {
                    var bounds = Bounds(cutter);
                    bool matched = false;
                    foreach (var floor in floors)
                    {
                        if (floor.Id == cutter.Id) continue;
                        if (InstanceVoidCutUtils.InstanceVoidCutExists(floor, cutter))
                        { existing++; matched = true; continue; }
                        if (!VoidCutPolicy.Overlaps(bounds, floorBounds[floor.Id])) continue;
                        matched = true;
                        candidates.Add(Tuple.Create(cutter, floor));
                    }
                    if (!matched) AddIssue(issues, cutter, null,
                        "未找到可配對構件。請檢查選取範圍及高程；空心若超出元件外框，可能無法由此外框初篩找到。");
                }
                if (candidates.Count > VoidCutPolicy.MaximumCandidates)
                {
                    TaskDialog.Show("自動切割", "候選配對超過 2000 組，請分批選取。模型未變更。");
                    return Result.Cancelled;
                }
                int created = 0;
                if (candidates.Count > 0)
                {
                    using (var group = new TransactionGroup(doc, "自動空心切割"))
                    {
                        group.Start();
                        foreach (var pair in candidates)
                        {
                            var cutter = pair.Item1;
                            var floor = pair.Item2;
                            if (!InstanceVoidCutUtils.CanBeCutWithVoid(floor))
                            { AddIssue(issues, cutter, floor, "原生 API 不允許此構件使用族空心切割；未改切母地形。"); continue; }
                            if (cutter.GroupId != ElementId.InvalidElementId || floor.GroupId != ElementId.InvalidElementId ||
                                cutter.Pinned || floor.Pinned)
                            { AddIssue(issues, cutter, floor, "群組或固定元件略過，未解除群組或取消固定。"); continue; }
                            using (var tx = new Transaction(doc, "空心切割構件"))
                            {
                                var failures = new PairFailures();
                                try
                                {
                                    tx.Start();
                                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                                        .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
                                    double before = Volume(floor);
                                    InstanceVoidCutUtils.AddInstanceVoidCut(doc, floor, cutter);
                                    doc.Regenerate();
                                    if (!InstanceVoidCutUtils.InstanceVoidCutExists(floor, cutter) ||
                                        !VoidCutPolicy.HasReduction(before, Volume(floor)))
                                        throw new InvalidOperationException("未驗證到實際切除體積，已回復；可能空心未相交或該區域已被切除。");
                                    if (tx.Commit() != TransactionStatus.Committed)
                                        throw new InvalidOperationException(failures.Reason ?? "切割未提交。");
                                    created++;
                                }
                                catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
                                catch (Exception ex)
                                {
                                    if (tx.GetStatus() == TransactionStatus.Started && tx.RollBack() != TransactionStatus.RolledBack)
                                        throw new InvalidOperationException("切割回復失敗，停止本批操作。", ex);
                                    AddIssue(issues, cutter, floor, ex.Message);
                                }
                            }
                        }
                        if (created == 0) group.RollBack();
                        else if (group.Assimilate() != TransactionStatus.Committed)
                            throw new InvalidOperationException("本批切割未提交。");
                    }
                }
                ui.RefreshActiveView();
                if (issues.Count > 0)
                    ModelessReviewController.ShowOrUpdate("自動切割｜需確認",
                        $"已建立 {created} 組切割；既有 {existing} 組；需確認 {issues.Count} 項。", issues, doc);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = "自動切割未完成：" + ex.Message; return Result.Failed; }
        }

        private static double Volume(Element element)
        {
            var parameter = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double)
                return parameter.AsDouble();
            using (var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }))
            {
                if (geometry == null) throw new InvalidOperationException("無法取得構件實體，未保留此切割。");
                return SolidVolume(geometry);
            }
        }

        private static string TargetKey(Element element)
        {
#if REVIT2024 || REVIT2025 || REVIT2026
            if (element is Toposolid topography)
                return topography.HostTopoId == ElementId.InvalidElementId ? "Toposolid" : "ToposolidSubdivision";
#endif
            return ElementClassifier.TryClassify(element)?.Key ?? string.Empty;
        }

        private static double SolidVolume(GeometryElement geometry)
        {
            double total = 0;
            foreach (var item in geometry)
            {
                if (item is Solid solid && solid.Volume > 0) total += solid.Volume;
                else if (item is GeometryInstance instance)
                    using (var nested = instance.GetInstanceGeometry())
                        if (nested != null) total += SolidVolume(nested);
            }
            return total;
        }

        private static double[] Bounds(Element element)
        {
            var box = element.get_BoundingBox(null);
            if (box == null) return null;
            var corners = new List<XYZ>();
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                corners.Add(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return new[] { corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z),
                corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z) };
        }

        private static void AddIssue(List<JoinFailureDetail> issues, Element cutter, Element floor, string reason)
        {
            issues.Add(new JoinFailureDetail { FirstElementId = cutter.Id,
                SecondElementId = floor?.Id ?? ElementId.InvalidElementId, Description = reason,
                Summary = $"切割元件 {cutter.Id} → 構件 {floor?.Id.ToString() ?? "未配對"}：{reason}" });
        }
    }
}
