using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class AutoTagService
    {
        private const double VerticalDirectionThreshold = 0.70;
        private const double HorizontalDirectionThreshold = 0.30;
        private const double MinimumLength = 1e-6;

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
                    if (!options.AllDirections && (mode != AutoTagMode.Unified || IsDirectionalCategory(element)) &&
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
                        bool ok = TryCreateTag(doc, view, candidate, rule.TagTypeId, options, occupiedTagRects,
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

                if (created == 0)
                    tx.RollBack();
                else if (tx.Commit() != TransactionStatus.Committed)
                    return AutoTagResult.Failed("標籤交易未提交，請處理 Revit 的失敗訊息後重試。");
            }

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
            out TagViewRect placedRect, out ElementId tagId, out string error)
        {
            placedRect = null;
            tagId = ElementId.InvalidElementId;
            error = "未能建立標籤。";
            try
            {
                if (!TryGetTagPoint(candidate, view, options, out XYZ tagPoint))
                {
                    error = "無法取得放置點，或放置點位於目前視圖裁切範圍外。";
                    return false;
                }
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

                    tag.TagHeadPosition = tagPoint;
                    AlignTagBoxCenter(doc, view, tag, tagPoint);

                    if (options.AvoidTagOverlap && options.Placement != AutoTagPlacement.Center)
                    {
                        placedRect = MoveTagToAvailablePosition(doc, view, tag, tagPoint, options, occupiedTagRects, candidate.Link != null);
                    }
                    else
                    {
                        placedRect = GetTagRect(doc, view, tag, options.UsePaperMillimeters);
                    }
                }

                return tag != null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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

        private static void AlignTagBoxCenter(Document doc, View view, IndependentTag tag, XYZ targetPoint)
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
                if (correction.GetLength() <= tolerance) return;
                if (attempt == 5)
                    throw new InvalidOperationException("標籤本體置中未收斂，請檢查標籤族的圖形或原點；此筆已回復。");
                tag.TagHeadPosition += correction;
            }
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

            if (!TryGetElementCenter(candidate.Element, candidate.Link == null ? view : null, out XYZ center))
                return false;

            center = candidate.ToHost.OfPoint(center);
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
