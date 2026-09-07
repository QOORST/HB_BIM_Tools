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
            new AutoTagCategoryRule("牆", BuiltInCategory.OST_Walls, BuiltInCategory.OST_WallTags, true, false),
            new AutoTagCategoryRule("樓板", BuiltInCategory.OST_Floors, BuiltInCategory.OST_FloorTags, true, false),
            new AutoTagCategoryRule("管", BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeTags, false, true),
            new AutoTagCategoryRule("風管", BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctTags, false, true),
            new AutoTagCategoryRule("電纜架", BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayTags, false, true),
            new AutoTagCategoryRule("電管", BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitTags, false, true),
            new AutoTagCategoryRule("機械設備", BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_MechanicalEquipmentTags, false, true),
            new AutoTagCategoryRule("衛生設備", BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_PlumbingFixtureTags, false, true),
            new AutoTagCategoryRule("電氣設備", BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalEquipmentTags, false, true),
            new AutoTagCategoryRule("電氣裝置", BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_ElectricalFixtureTags, false, true)
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
                return AutoTagResult.Failed("請至少選擇一個構件分類與標籤族型。");

            Document doc = uiDoc.Document;
            View view = doc.ActiveView;

            if (!CanCreateTagInView(view))
                return AutoTagResult.Failed("目前視圖不可建立標籤，請切換到平面、立面、剖面或詳圖視圖。");

            IList<Element> candidates = GetCandidateElements(uiDoc, view, options, selectedRules);
            if (candidates.Count == 0)
                return AutoTagResult.Failed("沒有可處理的元素。請確認目前視圖或目前選取中含有所選分類的可標註構件。");

            HashSet<ElementId> taggedElementIds = options.SkipExistingTags
                ? CollectTaggedElementIds(doc, view)
                : new HashSet<ElementId>();
            List<TagViewRect> occupiedTagRects = options.AvoidTagOverlap
                ? CollectTagRects(doc, view)
                : new List<TagViewRect>();

            int matched = 0;
            int created = 0;
            int skipped = 0;
            int failed = 0;

            using (var tx = new Transaction(doc, GetTransactionName(mode)))
            {
                tx.Start();

                foreach (Element element in candidates)
                {
                    if (!MatchesMode(element, view, mode))
                        continue;

                    matched++;

                    if (taggedElementIds.Contains(element.Id))
                    {
                        skipped++;
                        continue;
                    }

                    AutoTagRuleSelection rule = selectedRules.First(item => item.Rule.ElementCategory == (BuiltInCategory)GetElementIdValue(element.Category.Id));
                    if (TryCreateTag(doc, view, element, rule.TagTypeId, options, occupiedTagRects, out TagViewRect placedRect))
                    {
                        taggedElementIds.Add(element.Id);
                        if (placedRect != null)
                            occupiedTagRects.Add(placedRect);

                        created++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                if (created == 0)
                {
                    tx.RollBack();
                    return AutoTagResult.Failed(
                        matched == 0
                            ? $"沒有符合「{GetModeName(mode)}」方向的元素。"
                            : $"找到 {matched} 個符合方向的元素，但未能建立標籤。請確認所選分類已有可用標籤族，且此視圖允許該構件標籤。");
                }

                tx.Commit();
            }

            return AutoTagResult.Succeeded(candidates.Count, matched, created, skipped, failed);
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

        private static IList<Element> GetCandidateElements(
            UIDocument uiDoc,
            View view,
            AutoTagOptions options,
            IReadOnlyList<AutoTagRuleSelection> selectedRules)
        {
            Document doc = uiDoc.Document;
            var selectedCategoryIds = new HashSet<long>(selectedRules.Select(item => (long)item.Rule.ElementCategory));

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
                .ToList();
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

        private static HashSet<ElementId> CollectTaggedElementIds(Document doc, View view)
        {
            var result = new HashSet<ElementId>();
            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (IndependentTag tag in tags)
            {
                foreach (ElementId id in GetTaggedElementIds(tag))
                    result.Add(id);
            }

            return result;
        }

        private static IEnumerable<ElementId> GetTaggedElementIds(IndependentTag tag)
        {
            MethodInfo method = typeof(IndependentTag).GetMethod("GetTaggedLocalElementIds", Type.EmptyTypes);
            if (method != null)
            {
                object value = method.Invoke(tag, null);
                if (value is IEnumerable<ElementId> ids)
                    return ids.Where(id => id != ElementId.InvalidElementId);
            }

            PropertyInfo property = typeof(IndependentTag).GetProperty("TaggedLocalElementId");
            if (property != null)
            {
                object value = property.GetValue(tag);
                if (value is ElementId id && id != ElementId.InvalidElementId)
                    return new[] { id };
            }

            return Enumerable.Empty<ElementId>();
        }

        private static bool MatchesMode(Element element, View view, AutoTagMode mode)
        {
            if (TryGetPrimaryDirection(element, out XYZ direction))
            {
                double z = Math.Abs(direction.Normalize().Z);
                return mode == AutoTagMode.Vertical
                    ? z >= VerticalDirectionThreshold
                    : z <= HorizontalDirectionThreshold;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (box == null)
                return false;

            XYZ size = box.Max - box.Min;
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
            Element element,
            ElementId tagTypeId,
            AutoTagOptions options,
            IList<TagViewRect> occupiedTagRects,
            out TagViewRect placedRect)
        {
            placedRect = null;
            if (!TryGetTagPoint(element, view, options, out XYZ tagPoint))
                return false;

            try
            {
                FamilySymbol tagSymbol = doc.GetElement(tagTypeId) as FamilySymbol;
                if (tagSymbol != null && !tagSymbol.IsActive)
                    tagSymbol.Activate();

                IndependentTag tag = IndependentTag.Create(
                    doc,
                    view.Id,
                    new Reference(element),
                    options.AddLeader,
                    TagMode.TM_ADDBY_CATEGORY,
                    TagOrientation.Horizontal,
                    tagPoint);

                if (tag != null)
                {
                    if (tag.GetTypeId() != tagTypeId)
                        tag.ChangeTypeId(tagTypeId);

                    tag.TagHeadPosition = tagPoint;
                    AlignTagBoxCenter(doc, view, tag, tagPoint);

                    if (options.AvoidTagOverlap)
                    {
                        placedRect = MoveTagToAvailablePosition(doc, view, tag, tagPoint, options.OffsetMillimeters, occupiedTagRects);
                    }
                    else
                    {
                        placedRect = GetTagRect(doc, view, tag);
                    }
                }

                return tag != null;
            }
            catch
            {
                return false;
            }
        }

        private static List<TagViewRect> CollectTagRects(Document doc, View view)
        {
            var result = new List<TagViewRect>();
            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (IndependentTag tag in tags)
            {
                TagViewRect rect = GetTagRect(doc, view, tag);
                if (rect != null)
                    result.Add(rect);
            }

            return result;
        }

        private static TagViewRect MoveTagToAvailablePosition(
            Document doc,
            View view,
            IndependentTag tag,
            XYZ preferredPoint,
            double offsetMillimeters,
            IList<TagViewRect> occupiedRects)
        {
            TagViewRect current = GetTagRect(doc, view, tag);
            if (current == null || !IntersectsAny(current, occupiedRects))
                return current;

            double step = Math.Max(offsetMillimeters / 304.8, 150.0 / 304.8);
            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();
            XYZ[] directions =
            {
                up,
                right,
                up * -1.0,
                right * -1.0,
                up + right,
                up + right * -1.0,
                up * -1.0 + right,
                (up + right) * -1.0
            };

            XYZ bestPoint = preferredPoint;
            TagViewRect bestRect = current;
            int bestOverlapCount = CountIntersections(current, occupiedRects);

            for (int ring = 1; ring <= 4; ring++)
            {
                foreach (XYZ direction in directions)
                {
                    XYZ candidatePoint = preferredPoint + direction.Normalize() * step * ring;
                    AlignTagBoxCenter(doc, view, tag, candidatePoint);

                    TagViewRect candidateRect = GetTagRect(doc, view, tag);
                    if (candidateRect == null)
                        continue;

                    int overlapCount = CountIntersections(candidateRect, occupiedRects);
                    if (overlapCount == 0)
                        return candidateRect;

                    if (overlapCount < bestOverlapCount)
                    {
                        bestOverlapCount = overlapCount;
                        bestPoint = candidatePoint;
                        bestRect = candidateRect;
                    }
                }
            }

            AlignTagBoxCenter(doc, view, tag, bestPoint);
            return bestRect;
        }

        private static TagViewRect GetTagRect(Document doc, View view, IndependentTag tag)
        {
            try
            {
                doc.Regenerate();
                BoundingBoxXYZ box = tag.get_BoundingBox(view);
                return box == null ? null : TagViewRect.FromBoundingBox(box, view);
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
            if (doc == null || view == null || tag == null || targetPoint == null)
                return;

            try
            {
                doc.Regenerate();
                BoundingBoxXYZ box = tag.get_BoundingBox(view);
                if (box == null)
                    return;

                XYZ currentCenter = (box.Min + box.Max) * 0.5;
                XYZ correction = targetPoint - currentCenter;
                if (correction.GetLength() <= MinimumLength)
                    return;

                tag.TagHeadPosition = tag.TagHeadPosition + correction;
            }
            catch
            {
            }
        }

        private static bool TryGetTagPoint(Element element, View view, AutoTagOptions options, out XYZ point)
        {
            point = null;

            if (!TryGetElementCenter(element, view, out XYZ center))
                return false;

            double offset = options.OffsetMillimeters / 304.8;
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

            return true;
        }

        private static bool TryGetElementCenter(Element element, View view, out XYZ point)
        {
            point = null;

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

            if (element.Location is LocationPoint locationPoint)
            {
                point = locationPoint.Point;
                return true;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (box == null)
                return false;

            point = (box.Min + box.Max) * 0.5;
            return true;
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
                GeometryElement nestedGeometry = instance.GetInstanceGeometry();
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
            return mode == AutoTagMode.Vertical ? "自動標籤 - 垂直元素" : "自動標籤 - 水平元素";
        }

        private static string GetModeName(AutoTagMode mode)
        {
            return mode == AutoTagMode.Vertical ? "垂直" : "水平";
        }

        private sealed class TagViewRect
        {
            private const double PaddingFeet = 20.0 / 304.8;

            private TagViewRect(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX - PaddingFeet;
                MinY = minY - PaddingFeet;
                MaxX = maxX + PaddingFeet;
                MaxY = maxY + PaddingFeet;
            }

            private double MinX { get; }

            private double MinY { get; }

            private double MaxX { get; }

            private double MaxY { get; }

            public static TagViewRect FromBoundingBox(BoundingBoxXYZ box, View view)
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

                return new TagViewRect(minX, minY, maxX, maxY);
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
