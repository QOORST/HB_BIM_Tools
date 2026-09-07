using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class TagAlignService
    {
        private const double MinimumLength = 1e-6;
        private const double RectPaddingFeet = 20.0 / 304.8;

        public TagAlignResult Align(UIDocument uiDoc, TagAlignOptions options)
        {
            if (uiDoc == null)
                throw new ArgumentNullException(nameof(uiDoc));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            Document doc = uiDoc.Document;
            View view = doc.ActiveView;
            if (view == null || view.IsTemplate)
                return TagAlignResult.Failed("目前視圖不可整理標籤。");

            List<IndependentTag> tags = GetTags(uiDoc, view, options.Scope);
            if (tags.Count < 2)
                return TagAlignResult.Failed("請至少選取 2 個標籤，或在目前視圖中保留 2 個以上標籤。");

            List<TagNode> nodes = tags
                .Select(tag => TagNode.Create(tag, view))
                .Where(node => node != null)
                .ToList();

            if (nodes.Count < 2)
                return TagAlignResult.Failed("無法讀取足夠的標籤位置。");

            using (var tx = new Transaction(doc, "標籤輔助對齊"))
            {
                tx.Start();
                ApplyAlignment(nodes, view, options);

                if (options.AvoidOverlap)
                    ResolveOverlaps(doc, view, nodes, options);

                tx.Commit();
            }

            return TagAlignResult.Succeeded(nodes.Count);
        }

        private static List<IndependentTag> GetTags(UIDocument uiDoc, View view, TagAlignScope scope)
        {
            Document doc = uiDoc.Document;
            if (scope == TagAlignScope.Selection)
            {
                return uiDoc.Selection.GetElementIds()
                    .Select(doc.GetElement)
                    .OfType<IndependentTag>()
                    .Where(tag => tag.OwnerViewId == view.Id)
                    .ToList();
            }

            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
        }

        private static void ApplyAlignment(List<TagNode> nodes, View view, TagAlignOptions options)
        {
            double spacing = options.SpacingMillimeters / 304.8;
            bool horizontal = options.Mode == TagAlignMode.Horizontal || options.Mode == TagAlignMode.DistributeHorizontal;

            if (options.Mode == TagAlignMode.DistributeHorizontal)
            {
                List<TagNode> ordered = nodes.OrderBy(node => node.X).ToList();
                double y = GetBaseY(ordered, options.Base);
                double startX = ordered.First().X;
                for (int i = 0; i < ordered.Count; i++)
                    MoveToViewPoint(ordered[i], view, startX + spacing * i, y);

                return;
            }

            if (options.Mode == TagAlignMode.DistributeVertical)
            {
                List<TagNode> ordered = nodes.OrderByDescending(node => node.Y).ToList();
                double x = GetBaseX(ordered, options.Base);
                double startY = ordered.First().Y;
                for (int i = 0; i < ordered.Count; i++)
                    MoveToViewPoint(ordered[i], view, x, startY - spacing * i);

                return;
            }

            double target = horizontal
                ? GetBaseY(nodes, options.Base)
                : GetBaseX(nodes, options.Base);

            foreach (TagNode node in nodes)
            {
                if (horizontal)
                    MoveToViewPoint(node, view, node.X, target);
                else
                    MoveToViewPoint(node, view, target, node.Y);
            }
        }

        private static void ResolveOverlaps(Document doc, View view, List<TagNode> nodes, TagAlignOptions options)
        {
            bool horizontal = options.Mode == TagAlignMode.Horizontal || options.Mode == TagAlignMode.DistributeHorizontal;
            double step = Math.Max(options.SpacingMillimeters / 304.8, 150.0 / 304.8);
            List<TagRect> occupied = new List<TagRect>();

            List<TagNode> ordered = horizontal
                ? nodes.OrderBy(node => node.X).ToList()
                : nodes.OrderByDescending(node => node.Y).ToList();

            foreach (TagNode node in ordered)
            {
                TagRect rect = GetRect(doc, view, node.Tag);
                if (rect == null)
                    continue;

                int guard = 0;
                while (IntersectsAny(rect, occupied) && guard < 12)
                {
                    if (horizontal)
                        MoveToViewPoint(node, view, node.X + step, node.Y);
                    else
                        MoveToViewPoint(node, view, node.X, node.Y - step);

                    rect = GetRect(doc, view, node.Tag);
                    if (rect == null)
                        break;

                    guard++;
                }

                if (rect != null)
                    occupied.Add(rect);
            }
        }

        private static void MoveToViewPoint(TagNode node, View view, double x, double y)
        {
            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();
            XYZ delta = right * (x - node.X) + up * (y - node.Y);

            if (delta.GetLength() <= MinimumLength)
                return;

            node.Tag.TagHeadPosition = node.Tag.TagHeadPosition + delta;
            node.Point = node.Tag.TagHeadPosition;
            node.X = x;
            node.Y = y;
        }

        private static double GetBaseX(List<TagNode> nodes, TagAlignBase alignBase)
        {
            return alignBase == TagAlignBase.First
                ? nodes.First().X
                : nodes.Average(node => node.X);
        }

        private static double GetBaseY(List<TagNode> nodes, TagAlignBase alignBase)
        {
            return alignBase == TagAlignBase.First
                ? nodes.First().Y
                : nodes.Average(node => node.Y);
        }

        private static TagRect GetRect(Document doc, View view, IndependentTag tag)
        {
            try
            {
                doc.Regenerate();
                BoundingBoxXYZ box = tag.get_BoundingBox(view);
                return box == null ? null : TagRect.FromBoundingBox(box, view);
            }
            catch
            {
                return null;
            }
        }

        private static bool IntersectsAny(TagRect rect, IList<TagRect> occupied)
        {
            return occupied.Any(rect.Intersects);
        }

        private sealed class TagNode
        {
            public IndependentTag Tag { get; private set; }

            public XYZ Point { get; set; }

            public double X { get; set; }

            public double Y { get; set; }

            public static TagNode Create(IndependentTag tag, View view)
            {
                if (tag == null)
                    return null;

                XYZ point = tag.TagHeadPosition;
                XYZ right = view.RightDirection.Normalize();
                XYZ up = view.UpDirection.Normalize();

                return new TagNode
                {
                    Tag = tag,
                    Point = point,
                    X = point.DotProduct(right),
                    Y = point.DotProduct(up)
                };
            }
        }

        private sealed class TagRect
        {
            private TagRect(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX - RectPaddingFeet;
                MinY = minY - RectPaddingFeet;
                MaxX = maxX + RectPaddingFeet;
                MaxY = maxY + RectPaddingFeet;
            }

            private double MinX { get; }

            private double MinY { get; }

            private double MaxX { get; }

            private double MaxY { get; }

            public static TagRect FromBoundingBox(BoundingBoxXYZ box, View view)
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

                return new TagRect(minX, minY, maxX, maxY);
            }

            public bool Intersects(TagRect other)
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
