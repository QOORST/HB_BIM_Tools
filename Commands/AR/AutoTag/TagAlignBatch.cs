using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdTagAlignBatchHorizontal : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => TagBatchCommand.Execute(data, ref message, true);
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdTagAlignBatchVertical : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => TagBatchCommand.Execute(data, ref message, false);
    }
    internal static class TagBatchCommand
    {
        internal static Result Execute(ExternalCommandData data, ref string message, bool horizontal)
        {
            if (data.Application.ActiveUIDocument == null) return Result.Cancelled;
            try
            {
                var result = new TagAlignService().AlignStructureRows(data.Application.ActiveUIDocument, horizontal);
                if (!result.IsCancelled && (!result.Success || result.NeedsReview))
                    TaskDialog.Show(horizontal ? "結構標籤批次水平對齊" : "結構標籤批次垂直對齊", result.Message);
                return result.Success ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    internal sealed partial class TagAlignService
    {
        internal TagAlignResult AlignStructureRows(UIDocument ui, bool horizontal)
        {
            var doc = ui.Document; var view = doc.ActiveView;
            var filter = new StructureTagFilter(view.Id);
            IList<Element> basis, targets;
            try
            {
                basis = ui.Selection.PickElementsByRectangle(filter, "框選基準結構標籤；基準不移動。Esc 取消。");
                targets = ui.Selection.PickElementsByRectangle(filter, horizontal ? "框選待對齊標籤；依分類及上下順序配對，只移動上下位置。" : "框選待對齊標籤；依分類及左右順序配對，只移動左右位置。");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return TagAlignResult.Cancelled(); }
            var referenceIds = basis.Select(e => e.Id).Distinct().ToList();
            var targetIds = targets.Select(e => e.Id).Distinct().ToList();
            if (referenceIds.Intersect(targetIds).Any()) return TagAlignResult.Failed("兩側框選包含相同標籤，請重新框選不同欄。");
            if (referenceIds.Count != targetIds.Count || referenceIds.Count < 1 || referenceIds.Count > 500)
                return TagAlignResult.Failed($"基準 {referenceIds.Count} 個、待對齊 {targetIds.Count} 個；兩側需同數量，且各 1 至 500 個結構標籤。未移動。");
            if (targets.Any(e => e.Pinned || e.GroupId != ElementId.InvalidElementId))
                return TagAlignResult.Failed("待對齊標籤含鎖定或群組項目；本次未移動，請排除後重選。");
            var up = (horizontal ? view.UpDirection : view.RightDirection).Normalize();
            var right = (horizontal ? view.RightDirection : view.UpDirection).Normalize();
            int overlaps = 0;
            using (var group = new TransactionGroup(doc, horizontal ? "結構標籤批次水平對齊" : "結構標籤批次垂直對齊"))
            {
                group.Start();
                try
                {
                    var ids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).ToElementIds()
                        .Union(referenceIds).Union(targetIds).ToList();
                    var before = MeasureCommittedHeads(doc, view, ids);
                    var r = referenceIds.Select(id => before[id].Center.DotProduct(up) * 304.8).ToArray();
                    var t = targetIds.Select(id => before[id].Center.DotProduct(up) * 304.8).ToArray();
                    var pairs = new List<(int Reference, int Target)>();
                    var categories = basis.Concat(targets).Select(e => e.Category.Id).Distinct();
                    foreach (var category in categories)
                    {
                        var ri = Enumerable.Range(0, referenceIds.Count).Where(i => doc.GetElement(referenceIds[i]).Category.Id == category).ToArray();
                        var ti = Enumerable.Range(0, targetIds.Count).Where(i => doc.GetElement(targetIds[i]).Category.Id == category).ToArray();
                        if (!TagRowPairing.TryPair(ri.Select(i => r[i]).ToArray(), ti.Select(i => t[i]).ToArray(),
                            0.5 * Math.Max(1, view.Scale), 20 * Math.Max(1, view.Scale), out var categoryPairs, out var error))
                            throw new InvalidOperationException(basis.Concat(targets).First(e => e.Category.Id == category).Category.Name + "：" + error);
                        pairs.AddRange(categoryPairs.Select(p => (ri[p.Reference], ti[p.Target])));
                    }
                    // Keep the proven head-bounds alignment for this workflow. Different types/angles are ambiguous.
                    foreach (var pair in pairs)
                    {
                        var a = (IndependentTag)doc.GetElement(referenceIds[pair.Reference]);
                        var b = (IndependentTag)doc.GetElement(targetIds[pair.Target]);
                        if (a.GetTypeId() != b.GetTypeId() || Math.Abs(Math.Sin(a.RotationAngle - b.RotationAngle)) > 1e-6 ||
                            Math.Cos(a.RotationAngle - b.RotationAngle) < 0)
                            throw new InvalidOperationException("配對標籤的族型或旋轉不同，請分組選取；本次未移動。");
                    }
                    using (var tx = new Transaction(doc, "移動逐列配對標籤"))
                    {
                        tx.Start();
                        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new BatchAbortFailures()).SetClearAfterRollback(true));
                        foreach (var pair in pairs)
                        {
                            var id = targetIds[pair.Target];
                            var tag = (IndependentTag)doc.GetElement(id);
                            tag.TagHeadPosition += up * ((r[pair.Reference] - t[pair.Target]) / 304.8);
                            foreach (var end in before[id].Ends) tag.SetLeaderEnd(end.Key, end.Value);
                        }
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("移動交易未提交。");
                    }
                    var after = MeasureCommittedHeads(doc, view, ids);
                    var moving = new HashSet<ElementId>(targetIds);
                    foreach (var id in ids)
                    {
                        VerifyLeaderState(doc, id, before[id]);
                        if (!moving.Contains(id) && (before[id].Head.DistanceTo(after[id].Head) > AlignTolerance || before[id].Center.DistanceTo(after[id].Center) > AlignTolerance))
                            throw new InvalidOperationException($"基準或未選取標籤 {id} 發生位移。");
                    }
                    foreach (var pair in pairs)
                    {
                        var id = targetIds[pair.Target];
                        if (Math.Abs((after[id].Center - after[referenceIds[pair.Reference]].Center).DotProduct(up)) > AlignTolerance ||
                            Math.Abs((after[id].Center - before[id].Center).DotProduct(right)) > AlignTolerance)
                            throw new InvalidOperationException($"標籤 {id} 逐列對齊驗證失敗。");
                        var rect = TagRect.FromBoundingBox(after[id].Box, view);
                        if (after.Any(p => p.Key != id && rect.Intersects(TagRect.FromBoundingBox(p.Value.Box, view)))) overlaps++;
                    }
                    if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("對齊交易群組未完成。");
                }
                catch (Exception ex)
                {
                    if (group.GetStatus() == TransactionStatus.Started)
                    {
                        if (group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("回復失敗，請檢查模型。", ex);
                        return TagAlignResult.Failed("本次未保留變更。\n" + ex.Message);
                    }
                    throw;
                }
            }
            return TagAlignResult.Succeeded(targetIds.Count, $"已分組配對 {targetIds.Count} 個結構標籤；重疊待複核 {overlaps} 個。", overlaps > 0);
        }
        private sealed class StructureTagFilter : ISelectionFilter
        {
            private readonly ElementId viewId;
            private static readonly BuiltInCategory[] Categories = { BuiltInCategory.OST_StructuralFramingTags,
                BuiltInCategory.OST_StructuralColumnTags, BuiltInCategory.OST_WallTags, BuiltInCategory.OST_FloorTags,
                BuiltInCategory.OST_StructuralFoundationTags };
            internal StructureTagFilter(ElementId id) { viewId = id; }
            public bool AllowElement(Element e) => e is IndependentTag tag && tag.OwnerViewId == viewId && !tag.IsOrphaned &&
                Categories.Any(c => tag.Category?.Id == new ElementId(c));
            public bool AllowReference(Reference reference, XYZ point) => false;
        }
        private sealed class BatchAbortFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failures) => failures.GetFailureMessages().Count > 0
                ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
