using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class AutoTagService
    {
        private const double VerticalDirectionThreshold = 0.70;
        private const double HorizontalDirectionThreshold = 0.30;
        private const double MinimumLength = 1e-6;

        private sealed class TagTimings
        {
            public readonly Stopwatch Geometry = new Stopwatch();
            public readonly Stopwatch Creation = new Stopwatch();
            public readonly Stopwatch Alignment = new Stopwatch();
            public readonly Stopwatch Avoidance = new Stopwatch();

            public void Stop()
            {
                Geometry.Stop(); Creation.Stop(); Alignment.Stop(); Avoidance.Stop();
            }
        }

        public static readonly AutoTagCategoryRule[] CategoryRules =
        {
            new AutoTagCategoryRule("結構柱", BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralColumnTags, true, false),
            new AutoTagCategoryRule("結構樑", BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralFramingTags, true, false),
            new AutoTagCategoryRule("牆", BuiltInCategory.OST_Walls, BuiltInCategory.OST_WallTags, true, false, true),
            new AutoTagCategoryRule("樓板", BuiltInCategory.OST_Floors, BuiltInCategory.OST_FloorTags, true, false, true),
            new AutoTagCategoryRule("管線", BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeTags, false, true),
            new AutoTagCategoryRule("風管", BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctTags, false, true),
            new AutoTagCategoryRule("電纜橋架", BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayTags, false, true),
            new AutoTagCategoryRule("電管", BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitTags, false, true),
            new AutoTagCategoryRule("機械設備", BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_MechanicalEquipmentTags, false, true),
            new AutoTagCategoryRule("衛生設備", BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_PlumbingFixtureTags, false, true),
            new AutoTagCategoryRule("電氣設備", BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalEquipmentTags, false, true),
            new AutoTagCategoryRule("電氣裝置", BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_ElectricalFixtureTags, false, true),
            new AutoTagCategoryRule("門", BuiltInCategory.OST_Doors, BuiltInCategory.OST_DoorTags, false, false, true),
            new AutoTagCategoryRule("窗", BuiltInCategory.OST_Windows, BuiltInCategory.OST_WindowTags, false, false, true),
            new AutoTagCategoryRule("天花板", BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_CeilingTags, false, false, true),
            new AutoTagCategoryRule("屋頂", BuiltInCategory.OST_Roofs, BuiltInCategory.OST_RoofTags, false, false, true),
            new AutoTagCategoryRule("一般模型", BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_GenericModelTags, false, false, true),
            new AutoTagCategoryRule("結構基礎", BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_StructuralFoundationTags, true, false),
            new AutoTagCategoryRule("管件", BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeFittingTags, false, true),
            new AutoTagCategoryRule("管附件", BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_PipeAccessoryTags, false, true),
            new AutoTagCategoryRule("風管配件", BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctFittingTags, false, true),
            new AutoTagCategoryRule("風管附件", BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_DuctAccessoryTags, false, true),
            new AutoTagCategoryRule("風口", BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_DuctTerminalTags, false, true),
            new AutoTagCategoryRule("照明設備", BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingFixtureTags, false, true),
            new AutoTagCategoryRule("灑水頭", BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_SprinklerTags, false, true)
        };

        public AutoTagResult TagElements(
            UIDocument uiDoc,
            AutoTagMode mode,
            AutoTagOptions options,
            IReadOnlyList<AutoTagRuleSelection> selectedRules)
        {
            if (uiDoc == null)
                throw new ArgumentNullException(nameof(uiDoc));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (selectedRules == null || selectedRules.Count == 0)
                return AutoTagResult.Failed("請至少選擇一個元素分類與標籤族型。");

            var totalTimer = Stopwatch.StartNew();
            var timings = new TagTimings();
            Document doc = uiDoc.Document;
            View view = doc.ActiveView;

            if (!CanCreateTagInView(view))
                return AutoTagResult.Failed("目前視圖不可建立標籤，請切換到平面、立面、剖面或詳圖視圖。");

            IList<TagCandidate> candidates = GetCandidateElements(uiDoc, view, options, selectedRules);
            if (candidates.Count == 0)
                return AutoTagResult.Failed("沒有可處理的元素。請確認目前視圖或目前選取中含有所選分類的可標註元素。");

            var existingTags = options.SkipExistingTags
                ? CollectExistingTags(doc, view)
                : new Dictionary<string, List<ElementId>>();
            var taggedElementIds = new HashSet<string>(existingTags.Keys);
            var skippedTagIds = new HashSet<ElementId>();
            var occupiedTagRects = new List<TagViewRect>();

            var issues = new List<string>();
            var reviewIds = new List<ElementId>();
            var bankTags = new List<(TagCandidate Candidate, ElementId Id)>();
            if (options.PipeBank)
            {
                if (options.Scope != AutoTagScope.Selection || options.Placement == AutoTagPlacement.Center || !options.AddLeader)
                    return AutoTagResult.Failed("管排需選取一組平行直管，指定排列側別並啟用引線。");
                if (candidates.Count < 2 || candidates.Count > 100 || candidates.Any(c => !(c.Element is MEPCurve) ||
                    !(c.Element.Location is LocationCurve lc) || !(lc.Curve is Line)))
                    return AutoTagResult.Failed("管排接受 2 至 100 條直線管、風管、電管或橋架；請分組選取。");
                XYZ axis = candidates[0].ToHost.OfVector(((LocationCurve)candidates[0].Element.Location).Curve.GetEndPoint(1) -
                    ((LocationCurve)candidates[0].Element.Location).Curve.GetEndPoint(0));
                axis -= view.ViewDirection * axis.DotProduct(view.ViewDirection);
                if (axis.GetLength() < MinimumLength) return AutoTagResult.Failed("管排在此視圖沒有可辨識方向。");
                axis = axis.Normalize();
                foreach (var candidate in candidates)
                {
                    var curve = ((LocationCurve)candidate.Element.Location).Curve;
                    XYZ direction = candidate.ToHost.OfVector(curve.GetEndPoint(1) - curve.GetEndPoint(0));
                    direction -= view.ViewDirection * direction.DotProduct(view.ViewDirection);
                    if (direction.GetLength() < MinimumLength || Math.Abs(direction.Normalize().DotProduct(axis)) < 0.99985)
                        return AutoTagResult.Failed("所選管線投影方向不平行，請拆成不同管排處理。");
                }
            }
            int matched = 0;
            int created = 0;
            int skipped = 0;
            int failed = 0;

            using (var tx = new Transaction(doc, GetTransactionName(mode)))
            {
                tx.Start();
                if (options.AvoidTagOverlap)
                    occupiedTagRects = CollectTagRects(doc, view, options.UsePaperMillimeters);

                foreach (TagCandidate candidate in candidates)
                {
                    Element element = candidate.Element;
                    if (!options.PipeBank && !options.AllDirections && (mode != AutoTagMode.Unified || IsDirectionalCategory(element)) &&
                        !MatchesMode(candidate, view, options.VerticalOnly ? AutoTagMode.Vertical : AutoTagMode.Horizontal))
                        continue;

                    matched++;

                    if (taggedElementIds.Contains(candidate.Key))
                    {
                        skipped++;
                        if (existingTags.TryGetValue(candidate.Key, out var evidence))
                        {
                            foreach (var id in evidence) skippedTagIds.Add(id);
                            issues.Add($"{candidate.Description}：略過，既有標籤 ID {string.Join(", ", evidence.Select(GetElementIdValue))}。");
                        }
                        continue;
                    }

                    AutoTagRuleSelection rule = selectedRules.First(item => item.Rule.ElementCategory == (BuiltInCategory)GetElementIdValue(element.Category.Id));
                    using (var itemTransaction = new SubTransaction(doc))
                    {
                        itemTransaction.Start();
                        bool ok = TryCreateTag(doc, view, candidate, rule.TagTypeId, options, occupiedTagRects, timings,
                            out TagViewRect placedRect, out ElementId tagId, out string error);
                        if (!ok)
                        {
                            itemTransaction.RollBack();
                            failed++;
                            issues.Add($"{rule.Rule.Name} / {candidate.Description}：{error}");
                            continue;
                        }
                        if (itemTransaction.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("單筆標籤交易未完成，已停止本次作業。");
                        taggedElementIds.Add(candidate.Key);
                        if (options.PipeBank) bankTags.Add((candidate, tagId));
                        if (options.AvoidTagOverlap && (placedRect == null || IntersectsAny(placedRect, occupiedTagRects)))
                        {
                            reviewIds.Add(tagId);
                            issues.Add($"{rule.Rule.Name} / {candidate.Description} / 標籤 {GetElementIdValue(tagId)}：" +
                                (placedRect == null ? "無法確認重疊，需複核。" : "避讓後仍重疊，需複核。"));
                        }
                        if (placedRect != null) occupiedTagRects.Add(placedRect);
                        created++;
                    }
                }

                if (options.PipeBank && created > 0)
                {
                    try
                    {
                        if (failed > 0) throw new InvalidOperationException("部分管排標籤建立失敗，不保留不完整排列。");
                        reviewIds.Clear();
                        ArrangePipeBank(doc, view, options, bankTags, reviewIds, issues);
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return AutoTagResult.Failed("管排配置已回復：" + ex.Message);
                    }
                }
                if (created == 0)
                    tx.RollBack();
                else if (tx.Commit() != TransactionStatus.Committed)
                    return AutoTagResult.Failed("標籤交易未提交，請處理 Revit 的失敗訊息後重試。");
            }

            totalTimer.Stop();
            issues.Insert(0, $"耗時：總計 {totalTimer.Elapsed.TotalSeconds:F2} 秒；逐筆定位 {timings.Geometry.Elapsed.TotalSeconds:F2}、建立 {timings.Creation.Elapsed.TotalSeconds:F2}、置中 {timings.Alignment.Elapsed.TotalSeconds:F2}、避讓 {timings.Avoidance.Elapsed.TotalSeconds:F2} 秒（總計另含收集、管排整理與交易）。");
            issues.Insert(0, $"視圖：{view.Name} [{GetElementIdValue(view.Id)}]；位置：{options.Placement}；既有標籤依據由本次執行重新讀取。");
            return AutoTagResult.Completed(candidates.Count, matched, created, skipped, failed, issues, reviewIds, skippedTagIds.ToList());
        }

        public static IReadOnlyList<AutoTagTypeOption> GetTagTypeOptions(Document doc, BuiltInCategory tagCategory)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(tagCategory)
                .Cast<FamilySymbol>()
                .OrderBy(symbol => symbol.FamilyName)
                .ThenBy(symbol => symbol.Name)
                .Select(symbol => new AutoTagTypeOption(symbol.Id, symbol.FamilyName + " : " + symbol.Name))
                .ToList();
        }

        private static bool CanCreateTagInView(View view)
        {
            if (view == null || view.IsTemplate)
                return false;

            return view.ViewType != ViewType.DrawingSheet
                && view.ViewType != ViewType.Schedule
                && view.ViewType != ViewType.ProjectBrowser
                && view.ViewType != ViewType.SystemBrowser
                && view.ViewType != ViewType.Internal;
        }

        private static IList<TagCandidate> GetCandidateElements(
            UIDocument uiDoc,
            View view,
            AutoTagOptions options,
            IReadOnlyList<AutoTagRuleSelection> selectedRules)
        {
            Document doc = uiDoc.Document;
            var selectedCategoryIds = new HashSet<long>(selectedRules.Select(item => (long)item.Rule.ElementCategory));

            if (options.Scope == AutoTagScope.LinkedView)
            {
#if REVIT2024 || REVIT2025 || REVIT2026
                var link = string.IsNullOrWhiteSpace(options.LinkInstanceUniqueId) ? null :
                    doc.GetElement(options.LinkInstanceUniqueId) as RevitLinkInstance;
                if (link == null || link.GetLinkDocument() == null)
                    throw new InvalidOperationException("指定連結已卸載或不存在，請重新整理後選擇連結。");
                if (link.IsHidden(view) || view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_RvtLinks)))
                    return new List<TagCandidate>();
                var filter = new ElementMulticategoryFilter(selectedRules.Select(r => r.Rule.ElementCategory).ToList());
                return new FilteredElementCollector(doc, view.Id, link.Id)
                    .WhereElementIsNotElementType().WherePasses(filter).ToElements()
                    .Where(e => IsCandidateElement(e, selectedCategoryIds))
                    .Select(e => new TagCandidate(e, link)).ToList();
#else
                throw new InvalidOperationException("指定連結的視圖篩選需要 Revit 2024 或更新版本。");
#endif
            }

            IEnumerable<Element> elements;
            if (options.Scope == AutoTagScope.Selection)
            {
                elements = uiDoc.Selection.GetElementIds().Select(doc.GetElement);
            }
            else
            {
                var filters = selectedRules
                    .Select(rule => new ElementCategoryFilter(rule.Rule.ElementCategory))
                    .Cast<ElementFilter>()
                    .ToList();

                elements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(new LogicalOrFilter(filters))
                    .ToElements();
            }

            return elements
                .Where(element => IsCandidateElement(element, selectedCategoryIds))
                .Select(element => new TagCandidate(element, null)).ToList();
        }

        private static bool IsCandidateElement(Element element, HashSet<long> selectedCategoryIds)
        {
            if (element == null || element.ViewSpecific || element.Category == null)
                return false;

            return element.Category.CategoryType == CategoryType.Model
                && selectedCategoryIds.Contains(GetElementIdValue(element.Category.Id));
        }

        private static long GetElementIdValue(ElementId id)
        {
#if REVIT2024 || REVIT2025 || REVIT2026
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        private sealed class TagCandidate
        {
            public TagCandidate(Element element, RevitLinkInstance link)
            {
                Element = element;
                Link = link;
                ToHost = link?.GetTotalTransform() ?? Transform.Identity;
            }
            public Element Element { get; }
            public RevitLinkInstance Link { get; }
            public Transform ToHost { get; }
            public string Key => MakeKey(Link?.Id, Element.Id);
            public string Description => Link == null ? $"元素 {GetElementIdValue(Element.Id)}" :
                $"連結 {Link.Name} [{GetElementIdValue(Link.Id)}] / 元素 {GetElementIdValue(Element.Id)}";
            public Reference Reference => Link == null ? new Reference(Element) : new Reference(Element).CreateLinkReference(Link);
        }

        private static string MakeKey(ElementId linkId, ElementId elementId)
        {
            return $"{(linkId == null ? -1 : GetElementIdValue(linkId))}:{GetElementIdValue(elementId)}";
        }

        private static HashSet<string> CollectTaggedElementIds(Document doc, View view)
        {
            return new HashSet<string>(CollectExistingTags(doc, view).Keys);
        }

        private static Dictionary<string, List<ElementId>> CollectExistingTags(Document doc, View view)
        {
            var result = new Dictionary<string, List<ElementId>>();
            var ids = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag)).ToElementIds();
            foreach (var tagId in ids)
            {
                // Resolve against the current document, including after Undo/Redo.
                var tag = doc.GetElement(tagId) as IndependentTag;
                if (tag == null || !tag.IsValidObject || tag.IsOrphaned) continue;
                foreach (LinkElementId id in tag.GetTaggedElementIds())
                {
                    string key = null;
                    if (id.LinkInstanceId != ElementId.InvalidElementId && id.LinkedElementId != ElementId.InvalidElementId)
                        key = MakeKey(id.LinkInstanceId, id.LinkedElementId);
                    else if (id.HostElementId != ElementId.InvalidElementId)
                        key = MakeKey(null, id.HostElementId);
                    if (key == null) continue;
                    if (!result.TryGetValue(key, out var tags))
                        result[key] = tags = new List<ElementId>();
                    if (!tags.Contains(tag.Id)) tags.Add(tag.Id);
                }
            }
            return result;
        }

        private static bool IsDirectionalCategory(Element element)
        {
            return IsCategory(element, BuiltInCategory.OST_StructuralFraming) ||
                IsCategory(element, BuiltInCategory.OST_PipeCurves) || IsCategory(element, BuiltInCategory.OST_DuctCurves) ||
                IsCategory(element, BuiltInCategory.OST_Conduit) || IsCategory(element, BuiltInCategory.OST_CableTray);
        }

        private static bool MatchesMode(TagCandidate candidate, View view, AutoTagMode mode)
        {
            Element element = candidate.Element;
            if (TryGetPrimaryDirection(element, out XYZ direction))
            {
                double z = Math.Abs(candidate.ToHost.OfVector(direction).Normalize().Z);
                return mode == AutoTagMode.Vertical
                    ? z >= VerticalDirectionThreshold
                    : z <= HorizontalDirectionThreshold;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(candidate.Link == null ? view : null) ?? element.get_BoundingBox(null);
            if (box == null)
                return false;

            var corners = BoxCorners(box).Select(p => candidate.ToHost.OfPoint(p)).ToList();
            XYZ size = new XYZ(corners.Max(p => p.X) - corners.Min(p => p.X),
                corners.Max(p => p.Y) - corners.Min(p => p.Y), corners.Max(p => p.Z) - corners.Min(p => p.Z));
            double xy = Math.Max(Math.Abs(size.X), Math.Abs(size.Y));
            double zSize = Math.Abs(size.Z);

            return mode == AutoTagMode.Vertical
                ? zSize > xy && zSize > MinimumLength
                : xy >= zSize && xy > MinimumLength;
        }

        private static bool TryGetPrimaryDirection(Element element, out XYZ direction)
        {
            direction = null;

            if (element.Location is LocationCurve locationCurve)
            {
                Curve curve = locationCurve.Curve;
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                XYZ candidate = end - start;
                if (candidate.GetLength() > MinimumLength)
                {
                    direction = candidate.Normalize();
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreateTag(
            Document doc,
            View view,
            TagCandidate candidate,
            ElementId tagTypeId,
            AutoTagOptions options,
            IList<TagViewRect> occupiedTagRects,
            TagTimings timings,
            out TagViewRect placedRect, out ElementId tagId, out string error)
        {
            placedRect = null;
            tagId = ElementId.InvalidElementId;
            error = "未能建立標籤。";
            try
            {
                timings.Geometry.Start();
                if (!TryGetTagPoint(candidate, view, options, out XYZ tagPoint))
                {
                    error = "無法取得放置點，或放置點位於目前視圖裁切範圍外。";
                    return false;
                }
                timings.Geometry.Stop();
                timings.Creation.Start();
                FamilySymbol tagSymbol = doc.GetElement(tagTypeId) as FamilySymbol;
                if (tagSymbol != null && !tagSymbol.IsActive)
                    tagSymbol.Activate();

                IndependentTag tag = IndependentTag.Create(
                    doc,
                    view.Id,
                    candidate.Reference,
                    options.AddLeader,
                    TagMode.TM_ADDBY_CATEGORY,
                    TagOrientation.Horizontal,
                    tagPoint);

                if (tag != null)
                {
                    tagId = tag.Id;
                    if (tag.GetTypeId() != tagTypeId)
                        tag.ChangeTypeId(tagTypeId);

                    ApplyTextDirection(tag, candidate, view, options.PipeBank ? AutoTagTextDirection.Horizontal : options.TextDirection);
                    doc.Regenerate();
                    tag.TagHeadPosition = tagPoint;
                    timings.Creation.Stop();
                    timings.Alignment.Start();
                    BoundingBoxXYZ alignedBox = AlignTagBoxCenter(doc, view, tag, tagPoint);
                    timings.Alignment.Stop();

                    if (!options.PipeBank && options.AvoidTagOverlap && options.Placement != AutoTagPlacement.Center)
                    {
                        timings.Avoidance.Start();
                        placedRect = MoveTagToAvailablePosition(doc, view, tag, tagPoint, options, occupiedTagRects, candidate.Link != null);
                        timings.Avoidance.Stop();
                    }
                    else
                    {
                        placedRect = TagViewRect.FromBoundingBox(alignedBox, view,
                            options.UsePaperMillimeters ? 0.4 * Math.Max(1, view.Scale) : 20.0);
                    }
                }

                return tag != null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                timings.Stop();
            }
        }

        private static void ArrangePipeBank(Document doc, View view, AutoTagOptions options,
            List<(TagCandidate Candidate, ElementId Id)> items, List<ElementId> review, List<string> issues)
        {
            if (double.IsNaN(options.BankGapPaperMm) || double.IsInfinity(options.BankGapPaperMm) ||
                options.BankGapPaperMm < 0.5 || options.BankGapPaperMm > 50 ||
                double.IsNaN(options.BankLeaderPaperMm) || double.IsInfinity(options.BankLeaderPaperMm) ||
                options.BankLeaderPaperMm < 2 || options.BankLeaderPaperMm > 100)
                throw new InvalidOperationException("管排紙面尺寸無效。");
            bool column = options.Placement == AutoTagPlacement.Left || options.Placement == AutoTagPlacement.Right;
            double sign = options.Placement == AutoTagPlacement.Left || options.Placement == AutoTagPlacement.Below ? -1 : 1;
            XYZ outward = (column ? view.RightDirection : view.UpDirection) * sign;
            XYZ along = column ? view.UpDirection : view.RightDirection;
            double scale = Math.Max(1, view.Scale) / 304.8;
            double gap = options.BankGapPaperMm * scale;
            double lead = options.BankLeaderPaperMm * scale;
            var firstCurve = ((LocationCurve)items[0].Candidate.Element.Location).Curve;
            XYZ axis = items[0].Candidate.ToHost.OfVector(firstCurve.GetEndPoint(1) - firstCurve.GetEndPoint(0));
            axis -= view.ViewDirection * axis.DotProduct(view.ViewDirection);
            axis = axis.Normalize();
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            foreach (var item in items)
            {
                var curve = ((LocationCurve)item.Candidate.Element.Location).Curve;
                double a = item.Candidate.ToHost.OfPoint(curve.GetEndPoint(0)).DotProduct(axis);
                double b = item.Candidate.ToHost.OfPoint(curve.GetEndPoint(1)).DotProduct(axis);
                lo = Math.Max(lo, Math.Min(a, b)); hi = Math.Min(hi, Math.Max(a, b));
            }
            if (hi - lo <= MinimumLength) throw new InvalidOperationException("所選管段沒有共同直管區段，請重新分組。");
            double commonStation = (lo + hi) / 2;
            var rows = items.Select(item =>
            {
                var curve = ((LocationCurve)item.Candidate.Element.Location).Curve;
                XYZ start = item.Candidate.ToHost.OfPoint(curve.GetEndPoint(0));
                XYZ end = item.Candidate.ToHost.OfPoint(curve.GetEndPoint(1));
                XYZ anchor = start + (end - start) * ((commonStation - start.DotProduct(axis)) / (end - start).DotProduct(axis));
                var tag = (IndependentTag)doc.GetElement(item.Id);
                var box = GetTagHeadBox(doc, view, tag);
                if (box == null) throw new InvalidOperationException("無法量測管排標籤。");
                var corners = BoxCorners(box).ToList();
                return new { Item = item, Tag = tag, Anchor = anchor,
                    Size = corners.Max(p => p.DotProduct(along)) - corners.Min(p => p.DotProduct(along)),
                    Depth = corners.Max(p => p.DotProduct(outward)) - corners.Min(p => p.DotProduct(outward)) };
            }).OrderBy(row => row.Anchor.DotProduct(along)).ThenBy(row => row.Item.Candidate.Key).ToList();
            double edge = rows.Max(r => r.Anchor.DotProduct(outward)) + lead;
            double total = rows.Sum(r => r.Size) + gap * (rows.Count - 1);
            double cursor = (rows.First().Anchor.DotProduct(along) + rows.Last().Anchor.DotProduct(along) - total) / 2;
            var segments = new List<(XYZ A, XYZ B, ElementId Id)>();
            foreach (var row in rows)
            {
                double station = cursor + row.Size / 2;
                XYZ target = row.Anchor + along * (station - row.Anchor.DotProduct(along)) +
                    outward * (edge + lead + row.Depth / 2 - row.Anchor.DotProduct(outward));
                AlignTagBoxCenter(doc, view, row.Tag, target);
                row.Tag.HasLeader = true;
                row.Tag.LeaderEndCondition = LeaderEndCondition.Free;
                var reference = row.Item.Candidate.Reference;
                row.Tag.SetLeaderEnd(reference, row.Anchor);
                XYZ elbow = target + outward * (edge - target.DotProduct(outward));
                row.Tag.SetLeaderElbow(reference, elbow);
                segments.Add((row.Anchor, elbow, row.Tag.Id));
                segments.Add((elbow, row.Tag.TagHeadPosition, row.Tag.Id));
                if (!IsInsideCrop(view, target))
                {
                    review.Add(row.Tag.Id);
                    issues.Add($"管排標籤 {GetElementIdValue(row.Tag.Id)} 超出裁切範圍，需複核。");
                }
                cursor += row.Size + gap;
            }
            // Fixed order avoids arbitrary cross-side shuffling. Remaining
            // intersections are explicit review items, never silently accepted.
            for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i]; var b = segments[j];
                    if (a.Id == b.Id) continue;
                    double Cross(XYZ p, XYZ q, XYZ r) =>
                        (q - p).DotProduct(view.RightDirection) * (r - p).DotProduct(view.UpDirection) -
                        (q - p).DotProduct(view.UpDirection) * (r - p).DotProduct(view.RightDirection);
                    if (Cross(a.A, a.B, b.A) * Cross(a.A, a.B, b.B) <= 0 &&
                        Cross(b.A, b.B, a.A) * Cross(b.A, b.B, a.B) <= 0 &&
                        Math.Max(Math.Min(a.A.DotProduct(along), a.B.DotProduct(along)), Math.Min(b.A.DotProduct(along), b.B.DotProduct(along))) <=
                        Math.Min(Math.Max(a.A.DotProduct(along), a.B.DotProduct(along)), Math.Max(b.A.DotProduct(along), b.B.DotProduct(along))) &&
                        Math.Max(Math.Min(a.A.DotProduct(outward), a.B.DotProduct(outward)), Math.Min(b.A.DotProduct(outward), b.B.DotProduct(outward))) <=
                        Math.Min(Math.Max(a.A.DotProduct(outward), a.B.DotProduct(outward)), Math.Max(b.A.DotProduct(outward), b.B.DotProduct(outward))))
                    {
                        if (!review.Contains(a.Id)) review.Add(a.Id);
                        if (!review.Contains(b.Id)) review.Add(b.Id);
                        issues.Add($"管排引線 {GetElementIdValue(a.Id)} / {GetElementIdValue(b.Id)} 可能交叉，需複核。");
                    }
                }
            var bankIds = new HashSet<ElementId>(items.Select(i => i.Id));
            var occupied = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>().Where(t => !bankIds.Contains(t.Id)).Select(t => GetTagRect(doc, view, t, true)).ToList();
            foreach (var row in rows)
            {
                var rect = GetTagRect(doc, view, row.Tag, true);
                if (rect == null || occupied.Any(r => r == null) || IntersectsAny(rect, occupied))
                {
                    if (!review.Contains(row.Tag.Id)) review.Add(row.Tag.Id);
                    issues.Add($"管排標籤 {GetElementIdValue(row.Tag.Id)} 有重疊或無法量測，需複核。");
                }
                if (rect != null) occupied.Add(rect);
            }
        }

        private static void ApplyTextDirection(IndependentTag tag, TagCandidate candidate, View view,
            AutoTagTextDirection mode)
        {
            tag.TagOrientation = mode == AutoTagTextDirection.Vertical
                ? TagOrientation.Vertical : TagOrientation.Horizontal;
            if (mode != AutoTagTextDirection.FollowElement || !(candidate.Element is MEPCurve) ||
                !(candidate.Element.Location is LocationCurve location)) return;

            // Use the local tangent at the anchor, including link rotation, and
            // measure the angle in view coordinates rather than world XY.
            XYZ tangent = candidate.ToHost.OfVector(location.Curve.ComputeDerivatives(0.5, true).BasisX);
            double x = tangent.DotProduct(view.RightDirection);
            double y = tangent.DotProduct(view.UpDirection);
            if (Math.Sqrt(x * x + y * y) <= MinimumLength) return;
            double angle = Math.Atan2(y, x);
            if (angle > Math.PI / 2) angle -= Math.PI;
            if (angle <= -Math.PI / 2) angle += Math.PI;
            tag.TagOrientation = TagOrientation.AnyModelDirection;
            tag.RotationAngle = angle;
        }

        private static List<TagViewRect> CollectTagRects(Document doc, View view, bool paperUnits)
        {
            var result = new List<TagViewRect>();
            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (IndependentTag tag in tags)
            {
                TagViewRect rect = GetTagRect(doc, view, tag, paperUnits);
                if (rect == null)
                    throw new InvalidOperationException($"無法量測既有標籤 {GetElementIdValue(tag.Id)}，為避免誤判已停止避讓；可關閉避讓後重試。");
                result.Add(rect);
            }

            return result;
        }

        private static TagViewRect MoveTagToAvailablePosition(
            Document doc, View view, IndependentTag tag, XYZ preferredPoint,
            AutoTagOptions options, IList<TagViewRect> occupiedRects, bool constrainToCrop)
        {
            TagViewRect original = GetTagRect(doc, view, tag, options.UsePaperMillimeters);
            if (original == null || !IntersectsAny(original, occupiedRects)) return original;
            XYZ originalHead = tag.TagHeadPosition;
            foreach (var offset in AutoTagAvoidanceSearch.GetOffsets(options.Placement,
                options.MaxMovePaperMillimeters, options.AllowCrossSide, options.AddLeader))
            {
                double scale = Math.Max(1, view.Scale) / 304.8;
                XYZ point = preferredPoint + view.RightDirection * (offset.X * scale) + view.UpDirection * (offset.Y * scale);
                if (constrainToCrop && !IsInsideCrop(view, point)) continue;
                AlignTagBoxCenter(doc, view, tag, point);
                TagViewRect rect = GetTagRect(doc, view, tag, options.UsePaperMillimeters);
                if (rect != null && !IntersectsAny(rect, occupiedRects)) return rect;
            }
            // No acceptable position: restore the exact original head, not a best-effort overlap.
            tag.TagHeadPosition = originalHead;
            return original;
        }

        private static BoundingBoxXYZ GetTagHeadBox(Document doc, View view, IndependentTag tag)
        {
            if (!tag.HasLeader)
            {
                doc.Regenerate();
                return tag.get_BoundingBox(view);
            }
            using (var probe = new SubTransaction(doc))
            {
                probe.Start();
                if (tag.HasLeader) tag.HasLeader = false;
                doc.Regenerate();
                var measured = tag.get_BoundingBox(view);
                var copy = measured == null ? null : new BoundingBoxXYZ
                {
                    Min = measured.Min, Max = measured.Max, Transform = measured.Transform
                };
                probe.RollBack();
                return copy;
            }
        }

        private static TagViewRect GetTagRect(Document doc, View view, IndependentTag tag, bool paperUnits = false)
        {
            try
            {
                BoundingBoxXYZ box = GetTagHeadBox(doc, view, tag);
                return box == null ? null : TagViewRect.FromBoundingBox(box, view, paperUnits ? 0.4 * Math.Max(1, view.Scale) : 20.0);
            }
            catch
            {
                return null;
            }
        }

        private static bool IntersectsAny(TagViewRect rect, IList<TagViewRect> occupiedRects)
        {
            return CountIntersections(rect, occupiedRects) > 0;
        }

        private static int CountIntersections(TagViewRect rect, IList<TagViewRect> occupiedRects)
        {
            if (rect == null || occupiedRects == null || occupiedRects.Count == 0)
                return 0;

            int count = 0;
            foreach (TagViewRect occupied in occupiedRects)
            {
                if (rect.Intersects(occupied))
                    count++;
            }

            return count;
        }

        private static BoundingBoxXYZ AlignTagBoxCenter(Document doc, View view, IndependentTag tag, XYZ targetPoint)
        {
            double tolerance = 0.05 * Math.Max(1, view.Scale) / 304.8;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var box = GetTagHeadBox(doc, view, tag);
                if (box == null) throw new InvalidOperationException("無法量測標籤本體。");
                XYZ currentCenter = box.Transform.OfPoint((box.Min + box.Max) * 0.5);
                XYZ delta = targetPoint - currentCenter;
                XYZ correction = view.RightDirection * delta.DotProduct(view.RightDirection) +
                    view.UpDirection * delta.DotProduct(view.UpDirection);
                if (correction.GetLength() <= tolerance) return box;
                if (attempt == 5)
                    throw new InvalidOperationException("標籤本體置中未收斂，請檢查標籤族的圖形或原點；此筆已回復。");
                tag.TagHeadPosition += correction;
            }
            throw new InvalidOperationException("標籤本體置中未完成。");
        }

        private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
        {
            foreach (double x in new[] { box.Min.X, box.Max.X })
                foreach (double y in new[] { box.Min.Y, box.Max.Y })
                    foreach (double z in new[] { box.Min.Z, box.Max.Z })
                        yield return box.Transform.OfPoint(new XYZ(x, y, z));
        }

        // The view collector may include elements outside the crop. Only use anchors inside it.
        private static bool IsInsideCrop(View view, XYZ point)
        {
            if (!view.CropBoxActive) return true;
            var local = view.CropBox.Transform.Inverse.OfPoint(point);
            if (local.X < view.CropBox.Min.X || local.X > view.CropBox.Max.X ||
                local.Y < view.CropBox.Min.Y || local.Y > view.CropBox.Max.Y) return false;
            using (var manager = view.GetCropRegionShapeManager())
            {
                var loops = manager.GetCropShape();
                if (loops.Count == 0) return false;
                XYZ right = view.RightDirection;
                XYZ up = view.UpDirection;
                double px = point.DotProduct(right), py = point.DotProduct(up);
                foreach (var loop in loops)
                {
                    var vertices = loop.SelectMany(c => c.Tessellate()).ToList();
                    bool inside = false;
                    for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
                    {
                        double ax = vertices[i].DotProduct(right), ay = vertices[i].DotProduct(up);
                        double bx = vertices[j].DotProduct(right), by = vertices[j].DotProduct(up);
                        if ((ay > py) != (by > py) && px < (bx - ax) * (py - ay) / (by - ay) + ax)
                            inside = !inside;
                    }
                    if (inside) return true;
                }
                return false;
            }
        }

        private static bool TryGetTagPoint(TagCandidate candidate, View view, AutoTagOptions options, out XYZ point)
        {
            point = null;

            XYZ center;
            if (view is ViewPlan plan &&
                (IsCategory(candidate.Element, BuiltInCategory.OST_StructuralFraming) ||
                 IsCategory(candidate.Element, BuiltInCategory.OST_StructuralColumns)))
            {
                if (!TryGetPlanStructuralCenter(candidate, plan, out center))
                    throw new InvalidOperationException("無法取得平面梁投影或柱切割截面中心；未改用體積重心，請核對視圖範圍與元素幾何。");
            }
            else
            {
                if (!TryGetElementCenter(candidate.Element, candidate.Link == null ? view : null, out center))
                    return false;
                center = candidate.ToHost.OfPoint(center);
            }
            if (candidate.Link != null && !IsInsideCrop(view, center)) return false;

            double offset = options.GetModelOffsetMillimeters(view.Scale) / 304.8;
            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();

            switch (options.Placement)
            {
                case AutoTagPlacement.Above:
                    point = center + up * offset;
                    break;
                case AutoTagPlacement.Below:
                    point = center - up * offset;
                    break;
                case AutoTagPlacement.Left:
                    point = center - right * offset;
                    break;
                case AutoTagPlacement.Right:
                    point = center + right * offset;
                    break;
                default:
                    point = center;
                    break;
            }

            return candidate.Link == null || IsInsideCrop(view, point);
        }

        private static bool TryGetPlanStructuralCenter(TagCandidate candidate, ViewPlan view, out XYZ center)
        {
            center = null;
            bool column = IsCategory(candidate.Element, BuiltInCategory.OST_StructuralColumns);
            double? cutHeight = null;
            if (column)
            {
                using (var range = view.GetViewRange())
                {
                    Level level = view.Document.GetElement(range.GetLevelId(PlanViewPlane.CutPlane)) as Level;
                    if (level == null) return false;
                    cutHeight = level.ProjectElevation + range.GetOffset(PlanViewPlane.CutPlane);
                }
            }

            XYZ axis = candidate.Element is FamilyInstance family ? family.GetTransform().BasisX : XYZ.BasisX;
            if (!column && candidate.Element.Location is LocationCurve location)
                axis = location.Curve.ComputeDerivatives(0.5, true).BasisX;
            axis = candidate.ToHost.OfVector(axis);
            XYZ normal = view.ViewDirection.Normalize();
            axis -= normal * axis.DotProduct(normal);
            if (axis.GetLength() < MinimumLength) axis = view.RightDirection;
            axis = axis.Normalize();
            XYZ across = normal.CrossProduct(axis).Normalize();

            var points = new List<XYZ>();
            var options = new Options { IncludeNonVisibleObjects = false, ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine };
            // Use physical solids, excluding family insertion points and symbolic graphics.
            var geometry = candidate.Element.get_Geometry(options);
            if (geometry == null) return false;
            foreach (GeometryObject item in geometry)
                CollectPlanStructuralPoints(item, candidate.ToHost, cutHeight, points);
            if (points.Count == 0) return false;

            XYZ origin = points[0];
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (XYZ point in points)
            {
                XYZ delta = point - origin;
                double x = delta.DotProduct(axis), y = delta.DotProduct(across);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            center = origin + axis * ((minX + maxX) * 0.5) + across * ((minY + maxY) * 0.5);
            return true;
        }

        private static void CollectPlanStructuralPoints(GeometryObject item, Transform toHost,
            double? cutHeight, List<XYZ> points)
        {
            if (item is GeometryInstance instance)
            {
                var geometry = instance.GetSymbolGeometry();
                if (geometry == null) return;
                Transform nested = toHost.Multiply(instance.Transform);
                foreach (GeometryObject child in geometry)
                    CollectPlanStructuralPoints(child, nested, cutHeight, points);
                return;
            }
            if (!(item is Solid solid) || solid.Faces.Size == 0 || solid.Volume <= MinimumLength) return;
            if (!cutHeight.HasValue && solid.Faces.Cast<Face>().All(face => face is PlanarFace) &&
                solid.Edges.Cast<Edge>().All(edge => edge.AsCurve() is Line))
            {
                // Linear extrema of a polyhedron occur at vertices; no tessellation is needed.
                foreach (Edge edge in solid.Edges)
                {
                    Curve curve = edge.AsCurve();
                    points.Add(toHost.OfPoint(curve.GetEndPoint(0)));
                    points.Add(toHost.OfPoint(curve.GetEndPoint(1)));
                }
                return;
            }
            foreach (Face face in solid.Faces)
            {
                Mesh mesh = face.Triangulate();
                if (!cutHeight.HasValue)
                {
                    foreach (XYZ vertex in mesh.Vertices) points.Add(toHost.OfPoint(vertex));
                    continue;
                }
                // Intersect the physical skin with the host plan's horizontal cut plane.
                for (int i = 0; i < mesh.NumTriangles; i++)
                {
                    MeshTriangle triangle = mesh.get_Triangle(i);
                    for (int edge = 0; edge < 3; edge++)
                    {
                        XYZ a = toHost.OfPoint(triangle.get_Vertex(edge));
                        XYZ b = toHost.OfPoint(triangle.get_Vertex((edge + 1) % 3));
                        double da = a.Z - cutHeight.Value, db = b.Z - cutHeight.Value;
                        if (Math.Abs(da) <= MinimumLength) points.Add(a);
                        if ((da < 0 && db > 0) || (da > 0 && db < 0))
                            points.Add(a + (b - a) * (da / (da - db)));
                    }
                }
            }
        }

        private static bool TryGetElementCenter(Element element, View view, out XYZ point)
        {
            point = null;

            // Structural location curves/origins may differ from the physical member due to justification/offsets.
            if ((IsCategory(element, BuiltInCategory.OST_StructuralFraming) ||
                 IsCategory(element, BuiltInCategory.OST_StructuralColumns)) &&
                TryGetStructuralSolidCenter(element, view, out point))
                return true;

            if (IsCategory(element, BuiltInCategory.OST_Floors) &&
                TryGetLargestHorizontalFaceCentroid(element, out point))
            {
                return true;
            }

            if (element.Location is LocationCurve locationCurve)
            {
                point = locationCurve.Curve.Evaluate(0.5, true);
                return true;
            }

            // Family insertion points may be on an edge or at the base. Prefer the visible bounds.
            BoundingBoxXYZ box = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (box != null)
            {
                point = box.Transform.OfPoint((box.Min + box.Max) * 0.5);
                return true;
            }
            if (element.Location is LocationPoint locationPoint)
            {
                point = locationPoint.Point;
                return true;
            }
            return false;
        }

        private static bool TryGetStructuralSolidCenter(Element element, View view, out XYZ center)
        {
            center = null;
            var options = new Options { IncludeNonVisibleObjects = false, ComputeReferences = false };
            if (view != null) options.View = view;
            var geometry = element.get_Geometry(options);
            if (geometry == null) return false;
            XYZ weighted = XYZ.Zero;
            double totalVolume = 0;
            foreach (GeometryObject item in geometry)
                AccumulateSolidCenter(item, Transform.Identity, ref weighted, ref totalVolume);
            if (totalVolume <= MinimumLength) return false;
            center = weighted / totalVolume;
            return true;
        }

        private static void AccumulateSolidCenter(GeometryObject item, Transform transform,
            ref XYZ weighted, ref double totalVolume)
        {
            if (item is Solid solid && solid.Faces.Size > 0 && solid.Volume > MinimumLength)
            {
                double volume = solid.Volume * Math.Abs(transform.Determinant);
                weighted += transform.OfPoint(solid.ComputeCentroid()) * volume;
                totalVolume += volume;
            }
            else if (item is GeometryInstance instance)
            {
                var symbolGeometry = instance.GetSymbolGeometry();
                if (symbolGeometry == null) return;
                var nested = transform.Multiply(instance.Transform);
                foreach (GeometryObject child in symbolGeometry)
                    AccumulateSolidCenter(child, nested, ref weighted, ref totalVolume);
            }
        }

        private static bool TryGetLargestHorizontalFaceCentroid(Element element, out XYZ centroid)
        {
            centroid = null;

            Options options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geometry = element.get_Geometry(options);
            if (geometry == null)
                return false;

            double bestArea = 0.0;
            XYZ bestCentroid = null;
            foreach (GeometryObject geometryObject in geometry)
            {
                AccumulateLargestHorizontalFace(geometryObject, Transform.Identity, ref bestArea, ref bestCentroid);
            }

            if (bestCentroid == null || bestArea <= MinimumLength)
                return false;

            centroid = bestCentroid;
            return true;
        }

        private static void AccumulateLargestHorizontalFace(
            GeometryObject geometryObject,
            Transform transform,
            ref double bestArea,
            ref XYZ bestCentroid)
        {
            if (geometryObject is Solid solid && solid.Faces.Size > 0 && solid.Volume > MinimumLength)
            {
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace planarFace))
                        continue;

                    XYZ normal = transform.OfVector(planarFace.FaceNormal).Normalize();
                    if (Math.Abs(normal.Z) < 0.95)
                        continue;

                    if (TryComputeFaceMeshCentroid(face, transform, out XYZ faceCentroid, out double faceArea) &&
                        faceArea > bestArea)
                    {
                        bestArea = faceArea;
                        bestCentroid = faceCentroid;
                    }
                }
            }
            else if (geometryObject is GeometryInstance instance)
            {
                Transform nestedTransform = transform.Multiply(instance.Transform);
                GeometryElement nestedGeometry = instance.GetSymbolGeometry();
                if (nestedGeometry == null)
                    return;

                foreach (GeometryObject nestedObject in nestedGeometry)
                {
                    AccumulateLargestHorizontalFace(nestedObject, nestedTransform, ref bestArea, ref bestCentroid);
                }
            }
        }

        private static bool TryComputeFaceMeshCentroid(
            Face face,
            Transform transform,
            out XYZ centroid,
            out double area)
        {
            centroid = null;
            area = 0.0;

            Mesh mesh = face.Triangulate();
            if (mesh == null || mesh.NumTriangles == 0)
                return false;

            XYZ weighted = XYZ.Zero;
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle triangle = mesh.get_Triangle(i);
                XYZ p0 = transform.OfPoint(triangle.get_Vertex(0));
                XYZ p1 = transform.OfPoint(triangle.get_Vertex(1));
                XYZ p2 = transform.OfPoint(triangle.get_Vertex(2));

                double triangleArea = (p1 - p0).CrossProduct(p2 - p0).GetLength() * 0.5;
                if (triangleArea <= MinimumLength)
                    continue;

                XYZ triangleCentroid = (p0 + p1 + p2) / 3.0;
                weighted += triangleCentroid * triangleArea;
                area += triangleArea;
            }

            if (area <= MinimumLength)
                return false;

            centroid = weighted / area;
            return true;
        }

        private static bool IsCategory(Element element, BuiltInCategory category)
        {
            return element?.Category != null &&
                   GetElementIdValue(element.Category.Id) == (long)category;
        }

        private static string GetTransactionName(AutoTagMode mode)
        {
            return mode == AutoTagMode.Unified ? "自動標籤" : mode == AutoTagMode.Vertical ? "自動標籤 - 垂直元素" : "自動標籤 - 水平元素";
        }

        private static string GetModeName(AutoTagMode mode)
        {
            return mode == AutoTagMode.Vertical ? "垂直" : "水平";
        }

        private sealed class TagViewRect
        {


            private TagViewRect(double minX, double minY, double maxX, double maxY, double paddingMm)
            {
                double PaddingFeet = paddingMm / 304.8;
                MinX = minX - PaddingFeet;
                MinY = minY - PaddingFeet;
                MaxX = maxX + PaddingFeet;
                MaxY = maxY + PaddingFeet;
            }

            private double MinX { get; }

            private double MinY { get; }

            private double MaxX { get; }

            private double MaxY { get; }

            public static TagViewRect FromBoundingBox(BoundingBoxXYZ box, View view, double paddingMm)
            {
                XYZ right = view.RightDirection.Normalize();
                XYZ up = view.UpDirection.Normalize();
                XYZ[] corners =
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                    new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                };

                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;

                foreach (XYZ corner in corners)
                {
                    double x = corner.DotProduct(right);
                    double y = corner.DotProduct(up);
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }

                return new TagViewRect(minX, minY, maxX, maxY, paddingMm);
            }

            public bool Intersects(TagViewRect other)
            {
                if (other == null)
                    return false;

                return MinX <= other.MaxX &&
                       MaxX >= other.MinX &&
                       MinY <= other.MaxY &&
                       MaxY >= other.MinY;
            }
        }
    }
}
