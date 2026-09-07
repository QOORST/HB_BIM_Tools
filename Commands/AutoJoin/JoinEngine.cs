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

    public override string ToString()
    {
        return Description;
    }
}

internal sealed class JoinEngineResult
{
    public int ElementsInScope { get; set; }
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

        var priority = BuildPriorityIndex(settings.PriorityKeys);

        using var transaction = new Transaction(doc, "Auto Join");
        transaction.Start();

        for (var i = 0; i < elements.Count; i++)
        {
            var a = elements[i];
            var bboxA = GetModelBoundingBox(a.Element);
            if (bboxA == null)
            {
                continue;
            }

            for (var j = i + 1; j < elements.Count; j++)
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

                var bboxB = GetModelBoundingBox(b.Element);
                if (bboxB == null || !BoundingBoxesMatch(bboxA, bboxB, settings.CheckInDetail))
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
            }
        }

        transaction.Commit();
        return result;
    }

    public static JoinEngineResult RunUnjoin(Document doc, IList<ClassifiedElement> elements, AutoJoinSettings settings)
    {
        var result = new JoinEngineResult
        {
            ElementsInScope = elements.Count
        };

        using var transaction = new Transaction(doc, "Auto Unjoin");
        transaction.Start();

        for (var i = 0; i < elements.Count; i++)
        {
            var a = elements[i];
            var bboxA = GetModelBoundingBox(a.Element);
            if (bboxA == null)
            {
                continue;
            }

            for (var j = i + 1; j < elements.Count; j++)
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

                var bboxB = GetModelBoundingBox(b.Element);
                if (bboxB == null || !BoundingBoxesMatch(bboxA, bboxB, settings.CheckInDetail))
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
            }
        }

        transaction.Commit();
        return result;
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

    private static BoundingBoxXYZ GetModelBoundingBox(Element element)
    {
        return element?.get_BoundingBox(null);
    }

    private static void AddFailureSample(JoinEngineResult result, ClassifiedElement a, ClassifiedElement b, Exception ex)
    {
        if (result.FailureDetails.Count >= 20)
        {
            return;
        }

        var message = ex?.Message ?? "Unknown error";
        var description = $"{a.CategorySpec.Key}({a.Element.Id}) <-> {b.CategorySpec.Key}({b.Element.Id}): {message}";

        result.FailureDetails.Add(new JoinFailureDetail
        {
            FirstElementId = a.Element.Id,
            SecondElementId = b.Element.Id,
            Description = description
        });

        if (result.FailureSamples.Count < 5)
        {
            result.FailureSamples.Add(description);
        }
    }

    private static bool BoundingBoxesMatch(BoundingBoxXYZ a, BoundingBoxXYZ b, bool includeTouching)
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
