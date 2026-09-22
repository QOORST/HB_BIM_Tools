using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class JoinFailureDetail
{
    public ElementId FirstElementId { get; set; }

    public ElementId SecondElementId { get; set; }

    public string Description { get; set; }
    public string Summary { get; set; }

    public override string ToString()
    {
        return Summary ?? Description;
    }
}

internal sealed class JoinEngineResult
{
    public int ElementsInScope { get; set; }
    public int GroupMembersSkipped { get; set; }
    public int GroupsAttempted { get; set; }
    public int GroupsCommitted { get; set; }
    public int GroupsRolledBack { get; set; }
    public int PairChecks { get; set; }
    public int PairCandidates { get; set; }
    public int Joined { get; set; }
    public int Unjoined { get; set; }
    public int Reordered { get; set; }
    public int FailedOperations { get; set; }
    public List<string> FailureSamples { get; } = new();
    public List<JoinFailureDetail> FailureDetails { get; } = new();
}

internal static class JoinEngine
{
    private const double Epsilon = 1e-6;

    public static JoinEngineResult RunAutoJoin(Document doc, IList<ClassifiedElement> elements, AutoJoinSettings settings)
    {
        var result = new JoinEngineResult
        {
            ElementsInScope = elements.Count
        };

        elements = ExcludeGroupMembers(elements, result);
        if (elements.Count < 2) return result;
        var priority = BuildPriorityIndex(settings.PriorityKeys);
        var boxes = new Dictionary<Element, BoundingBoxXYZ>();
        var spatial = new JoinSpatialCursor(elements.Count, index => GetCachedBoundingBox(elements[index].Element, boxes));

        using var transaction = new Transaction(doc, "Auto Join");
        transaction.Start();

        for (var i = 0; i < elements.Count; i++)
        {
            var a = elements[i];
            var bboxA = GetCachedBoundingBox(a.Element, boxes);
            if (bboxA == null)
            {
                continue;
            }

            for (var j = spatial.Next(i, i + 1); j < elements.Count; j = spatial.Next(i, j + 1))
            {
                var b = elements[j];

                result.PairChecks++;

                if (!settings.AllowSameCategoryJoin && a.CategorySpec.Key == b.CategorySpec.Key)
                {
                    continue;
                }

                if (!settings.AllowStructuralNonStructuralJoin && a.CategorySpec.IsStructural != b.CategorySpec.IsStructural)
                {
                    continue;
                }

                bboxA = GetCachedBoundingBox(a.Element, boxes);
                var bboxB = GetCachedBoundingBox(b.Element, boxes);
                if (bboxA == null || bboxB == null || !BoundingBoxesMatch(bboxA, bboxB, settings.CheckInDetail))
                {
                    continue;
                }

                result.PairCandidates++;

                try
                {
                    if (!JoinGeometryUtils.AreElementsJoined(doc, a.Element, b.Element))
                    {
                        JoinGeometryUtils.JoinGeometry(doc, a.Element, b.Element);
                        result.Joined++;
                    }

                    EnsureJoinOrder(doc, a, b, priority, result);
                }
                catch (Exception ex)
                {
                    result.FailedOperations++;
                    AddFailureSample(result, a, b, ex);
                }
                finally
                {
                    // A join can affect connected geometry beyond this pair, even after an exception.
                    boxes.Clear();
                    spatial.Invalidate();
                }
            }
        }

        CommitOrThrow(transaction);
        return result;
    }

    public static JoinEngineResult RunUnjoin(Document doc, IList<ClassifiedElement> elements, AutoJoinSettings settings)
    {
        var result = new JoinEngineResult
        {
            ElementsInScope = elements.Count
        };

        elements = ExcludeGroupMembers(elements, result);
        if (elements.Count < 2) return result;
        using var transaction = new Transaction(doc, "Auto Unjoin");
        transaction.Start();
        var boxes = new Dictionary<Element, BoundingBoxXYZ>();
        var spatial = new JoinSpatialCursor(elements.Count, index => GetCachedBoundingBox(elements[index].Element, boxes));

        for (var i = 0; i < elements.Count; i++)
        {
            var a = elements[i];
            var bboxA = GetCachedBoundingBox(a.Element, boxes);
            if (bboxA == null)
            {
                continue;
            }

            for (var j = spatial.Next(i, i + 1); j < elements.Count; j = spatial.Next(i, j + 1))
            {
                var b = elements[j];

                result.PairChecks++;

                if (!settings.AllowSameCategoryJoin && a.CategorySpec.Key == b.CategorySpec.Key)
                {
                    continue;
                }

                if (!settings.AllowStructuralNonStructuralJoin && a.CategorySpec.IsStructural != b.CategorySpec.IsStructural)
                {
                    continue;
                }

                bboxA = GetCachedBoundingBox(a.Element, boxes);
                var bboxB = GetCachedBoundingBox(b.Element, boxes);
                if (bboxA == null || bboxB == null || !BoundingBoxesMatch(bboxA, bboxB, settings.CheckInDetail))
                {
                    continue;
                }

                result.PairCandidates++;

                try
                {
                    if (JoinGeometryUtils.AreElementsJoined(doc, a.Element, b.Element))
                    {
                        JoinGeometryUtils.UnjoinGeometry(doc, a.Element, b.Element);
                        result.Unjoined++;
                    }
                }
                catch (Exception ex)
                {
                    result.FailedOperations++;
                    AddFailureSample(result, a, b, ex);
                }
                finally
                {
                    boxes.Clear();
                    spatial.Invalidate();
                }
            }
        }

        CommitOrThrow(transaction);
        return result;
    }

    private static IList<ClassifiedElement> ExcludeGroupMembers(IList<ClassifiedElement> elements, JoinEngineResult result)
    {
        var eligible = new List<ClassifiedElement>();
        foreach (var item in elements)
        {
            // Batch geometry edits must not change model-group membership or consistency.
            if (!item.Element.GroupId.Equals(ElementId.InvalidElementId))
                result.GroupMembersSkipped++;
            else
                eligible.Add(item);
        }
        return eligible;
    }

    private static void CommitOrThrow(Transaction transaction)
    {
        var status = transaction.Commit();
        if (status != TransactionStatus.Committed)
            throw new InvalidOperationException($"接合交易未成功提交（{status}），本次操作數不可視為成功結果。請先處理 Revit 失敗訊息，再重新檢查模型。");
    }

    private static Dictionary<string, int> BuildPriorityIndex(IList<string> keys)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            index[keys[i]] = i;
        }

        return index;
    }

    private static void EnsureJoinOrder(
        Document doc,
        ClassifiedElement a,
        ClassifiedElement b,
        Dictionary<string, int> priority,
        JoinEngineResult result)
    {
        if (!priority.TryGetValue(a.CategorySpec.Key, out var indexA) ||
            !priority.TryGetValue(b.CategorySpec.Key, out var indexB) ||
            indexA == indexB)
        {
            return;
        }

        var winner = indexA < indexB ? a.Element : b.Element;
        var loser = ReferenceEquals(winner, a.Element) ? b.Element : a.Element;

        try
        {
            if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, winner, loser))
            {
                JoinGeometryUtils.SwitchJoinOrder(doc, winner, loser);
                result.Reordered++;
            }
        }
        catch (Exception ex)
        {
            result.FailedOperations++;
            AddFailureSample(result, a, b, ex);
        }
    }

    private static BoundingBoxXYZ GetCachedBoundingBox(Element element, Dictionary<Element, BoundingBoxXYZ> boxes)
    {
        if (element == null) return null;
        if (!boxes.TryGetValue(element, out var box))
        {
            box = element.get_BoundingBox(null);
            boxes[element] = box;
        }
        return box;
    }

    private static void AddFailureSample(JoinEngineResult result, ClassifiedElement a, ClassifiedElement b, Exception ex)
    {
        var message = ex?.Message ?? "Unknown error";
        var description = $"{a.CategorySpec.Key}({a.Element.Id}) <-> {b.CategorySpec.Key}({b.Element.Id}): {message}";

        result.FailureDetails.Add(new JoinFailureDetail
        {
            FirstElementId = a.Element.Id,
            SecondElementId = b.Element.Id,
            Summary = $"{CategoryLabel(a.CategorySpec.Key)} {a.Element.Id} ↔ {CategoryLabel(b.CategorySpec.Key)} {b.Element.Id}｜" +
                (message.IndexOf("elements cannot be joined", StringComparison.OrdinalIgnoreCase) >= 0 ? "Revit 無法接合，原因待確認" : "操作未完成，請查看詳細資料"),
            Description = description
        });

        if (result.FailureSamples.Count < 5)
        {
            result.FailureSamples.Add(description);
        }
    }

    private static string CategoryLabel(string key)
    {
        switch (key) {
            case "Wall": return "牆";
            case "Floor": return "樓板";
            case "StructuralColumn": return "結構柱";
            case "StructuralFraming": return "結構構架";
            case "StructuralFoundation": return "結構基礎";
            default: return key;
        }
    }

    internal static bool BoundingBoxesMatch(BoundingBoxXYZ a, BoundingBoxXYZ b, bool includeTouching)
    {
        var axMin = a.Min.X;
        var ayMin = a.Min.Y;
        var azMin = a.Min.Z;
        var axMax = a.Max.X;
        var ayMax = a.Max.Y;
        var azMax = a.Max.Z;

        var bxMin = b.Min.X;
        var byMin = b.Min.Y;
        var bzMin = b.Min.Z;
        var bxMax = b.Max.X;
        var byMax = b.Max.Y;
        var bzMax = b.Max.Z;

        if (includeTouching)
        {
            return axMax >= bxMin - Epsilon &&
                   bxMax >= axMin - Epsilon &&
                   ayMax >= byMin - Epsilon &&
                   byMax >= ayMin - Epsilon &&
                   azMax >= bzMin - Epsilon &&
                   bzMax >= azMin - Epsilon;
        }

        return axMax > bxMin + Epsilon &&
               bxMax > axMin + Epsilon &&
               ayMax > byMin + Epsilon &&
               byMax > ayMin + Epsilon &&
               azMax > bzMin + Epsilon &&
               bzMax > azMin + Epsilon;
    }
    }
}
