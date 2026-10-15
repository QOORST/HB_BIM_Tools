using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using YD_RevitTools.LicenseManager.Commands.MEP;

internal static class Program
{
    private static int checks;
    private static MepTextBox Box(double x, double y = 2, double width = 6, double height = 2.5)
        => new MepTextBox { Along = x, Across = y, Width = width, Height = height };
    private static void Check(bool ok, string label)
    { if (!ok) throw new Exception(label); checks++; }
    private static bool Clear(MepTextBox[] boxes, double[] positions)
        => !Enumerable.Range(0, boxes.Length).Any(i => Enumerable.Range(i + 1, boxes.Length - i - 1)
            .Any(j => MepDimensionTextPolicy.Overlaps(boxes[i], positions[i], boxes[j], positions[j], .3)));
    private static void Main(string[] args)
    {
        var pair = new[] { Box(5), Box(8) };
        var plan = MepDimensionTextPolicy.Plan(pair, 3);
        Check(Clear(pair, plan.Across) && plan.UnresolvedGroups == 0, "simple pair resolves");
        Check(plan.Across.All(p => p >= 0), "prefer original side");
        Check(plan.Across.Max(p => Math.Abs(p - 2)) <= 3, "movement stays bounded while keeping line clearance");
        Check(plan.Across.Select((p, i) => MepDimensionTextPolicy.ClearsDimensionLine(pair[i], p, .5)).All(x => x),
            "resolved pair clears the dimension line");
        var distantTall = new[] { Box(5), Box(8), Box(30, 8, 6, 12) };
        var tallPlan = MepDimensionTextPolicy.Plan(distantTall, 3);
        Check(plan.Across.SequenceEqual(tallPlan.Across.Take(2)), "distant multiline note does not inflate displacement");
        Check(tallPlan.Across[2] == 8, "clear multiline note is fixed");
        var separated = new[] { Box(5), Box(8), Box(30), Box(33) };
        var separatedPlan = MepDimensionTextPolicy.Plan(separated, 3);
        Check(Clear(separated, separatedPlan.Across), "independent overlap groups resolve");
        var blocked = new[] { Box(5, 2, 6, 12), Box(8, 2, 6, 12), Box(30), Box(33) };
        var blockedPlan = MepDimensionTextPolicy.Plan(blocked, 3);
        Check(blockedPlan.UnresolvedGroups == 1 && blockedPlan.Across[0] == 2 && blockedPlan.Across[1] == 2,
            "unresolvable group restored atomically");
        Check(!MepDimensionTextPolicy.Overlaps(blocked[2], blockedPlan.Across[2], blocked[3], blockedPlan.Across[3], .3),
            "other group still succeeds");
        var zero = MepDimensionTextPolicy.Plan(pair, 0);
        Check(zero.UnresolvedGroups == 1 && zero.Across.SequenceEqual(new[] { 2.0, 2.0 }), "zero cap retains originals");
        var negative = new[] { Box(5, -2), Box(8, -2) };
        Check(MepDimensionTextPolicy.Plan(negative, 3).Across.All(p => p <= 0), "negative side is retained");
        var far = new[] { Box(5, 20), Box(8, 20) };
        Check(MepDimensionTextPolicy.Plan(far, 3).Across.All(p => Math.Abs(p - 20) <= 3), "cap is relative to original not dimension line");
        var crossing = new[] { Box(5, 0, 6, 4), Box(8, 0, 6, 4) };
        Check(Clear(crossing, MepDimensionTextPolicy.Plan(crossing, 3).Across), "cross-line fallback when same side cannot fit");
        var repeatBoxes = pair.Select((b, i) => Box(b.Along, plan.Across[i], b.Width, b.Height)).ToArray();
        Check(MepDimensionTextPolicy.Plan(repeatBoxes, 3).Across.SequenceEqual(plan.Across), "no repeat drift");
        var obstacle = new[] { Box(5), Box(8), Box(7, 5.2) };
        var obstaclePlan = MepDimensionTextPolicy.Plan(obstacle, 4);
        Check(obstaclePlan.Across[2] == 5.2 && Clear(obstacle, obstaclePlan.Across), "clear text acts as fixed obstacle");
        var nearLine = new[] { Box(5, .5) };
        var nearPlan = MepDimensionTextPolicy.Plan(nearLine, 3);
        Check(nearPlan.Across[0] == 2 && nearPlan.UnresolvedGroups == 0, "isolated text too close to line moves outwards");
        Check(nearLine[0].Along == 5 && nearLine[0].Across == .5, "planner does not modify source position or segment station");
        var limited = MepDimensionTextPolicy.Plan(nearLine, .5);
        Check(limited.Across[0] == .5 && limited.UnresolvedGroups == 1, "insufficient cap preserves isolated original and reports review");
        var negativeNear = new[] { Box(5, -.5) };
        Check(MepDimensionTextPolicy.Plan(negativeNear, 3).Across[0] == -2, "negative-side clearance moves outward");
        var exactGap = new[] { Box(5, 1.75) };
        Check(MepDimensionTextPolicy.Plan(exactGap, 3).Across[0] == 1.75, "exact clearance threshold is accepted");
        Check(MepDimensionTextPolicy.Plan(nearLine, 3, lineClearance: 1).Across[0] == 2.5,
            "custom clearance uses text boundary rather than text center");
        var shortCap = MepDimensionTextPolicy.Plan(pair, 1.5);
        Check(shortCap.UnresolvedGroups == 1 && shortCap.Across.All(p => p == 2), "no line-hugging solution to avoid reporting unresolved");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 6 })
        {
            bool rejected = false;
            try { MepDimensionTextPolicy.Plan(pair, 3, lineClearance: invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "reject invalid line clearance");
        }
        Check(MepDimensionTextPolicy.Plan(Array.Empty<MepTextBox>(), 3).Across.Length == 0, "empty input");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 21 })
        {
            bool rejected = false;
            try { MepDimensionTextPolicy.Plan(pair, invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "reject invalid cap");
        }
        var random = new Random(481);
        for (int sample = 0; sample < 100; sample++)
        {
            var boxes = Enumerable.Range(0, 8).Select(i => Box(random.NextDouble() * 35,
                random.NextDouble() * 10 - 5, 3 + random.NextDouble() * 5, 2 + random.NextDouble() * 4)).ToArray();
            var result = MepDimensionTextPolicy.Plan(boxes, 2.3);
            Check(result.Across.Select((p, i) => Math.Abs(p - boxes[i].Across) <= 2.3 + 1e-7).All(x => x), "random cap invariant");
            Check(result.Across.SequenceEqual(MepDimensionTextPolicy.Plan(boxes, 2.3).Across), "deterministic result");
            bool noNewCollision = true;
            for (int i = 0; i < boxes.Length; i++) for (int j = i + 1; j < boxes.Length; j++)
                if (!MepDimensionTextPolicy.Overlaps(boxes[i], boxes[i].Across, boxes[j], boxes[j].Across, .3) &&
                    MepDimensionTextPolicy.Overlaps(boxes[i], result.Across[i], boxes[j], result.Across[j], .3)) noNewCollision = false;
            Check(noNewCollision, "no newly introduced collisions");
            Check(result.Across.Select((p, i) => Math.Abs(p - boxes[i].Across) < 1e-7 ||
                MepDimensionTextPolicy.ClearsDimensionLine(boxes[i], p, .5)).All(x => x), "all moved labels clear line");
            Check(result.UnresolvedGroups > 0 || (Clear(boxes, result.Across) && result.Across.Select((p, i) =>
                MepDimensionTextPolicy.ClearsDimensionLine(boxes[i], p, .5)).All(x => x)), "success means collisions and line clearance both verified");
        }
        if (args.Length > 0)
        {
            var scenarios = new[] {
                new { Name = "一般重疊", Boxes = pair, Labels = new[] { "200", "250" } },
                new { Name = "遠處有多行附註", Boxes = distantTall, Labels = new[] { "200", "250", "600\n附註\n三行" } },
                new { Name = "過密群組保留原位", Boxes = blocked, Labels = new[] { "200\n附註\n三行", "250\n附註\n三行", "300", "350" } }
            };
            var data = scenarios.Select(s => new { name = s.Name,
                boxes = s.Boxes.Select((b, i) => new { x = b.Along, y = b.Across, w = b.Width, h = b.Height, label = s.Labels[i] }),
                layouts = Enumerable.Range(1, 10).Select(n => { double cap = n / 2.0; var r = MepDimensionTextPolicy.Plan(s.Boxes, cap);
                    return new { cap, positions = r.Across, unresolved = r.UnresolvedGroups }; }) });
            File.WriteAllText(args[0], JsonSerializer.Serialize(data));
        }
        Console.WriteLine($"PASS: {checks} dimension text policy checks; native Revit layout not tested.");
    }
}
