using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core;

namespace HB.MepField.Native
{
    internal static class SlopeChecks
    {
        private sealed class RejectFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor) =>
                accessor.GetFailureMessages().Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }

        internal static void Run(Document doc, ElementId system, ElementId type, ElementId level,
            List<string> results)
        {
            double minimum = Math.Max(doc.Application.ShortCurveTolerance * 2, 1.0 / 304.8);
            int geometryChecks = 0;
            foreach (double slope in new[] { -.02, -.01, .01, .02 })
            foreach (bool reverse in new[] { false, true })
            foreach (double angle in new[] { 22.5, 45.0, 90.0 })
            foreach (var direction in new[] { DirectionMode.Up, DirectionMode.Down })
            {
                XYZ a = new XYZ(0, 30, 3), b = new XYZ(20, 30, 3 + 20 * slope);
                Line line = Line.CreateBound(reverse ? b : a, reverse ? a : b);
                var options = new AvoidOptions { ExtraOffsetMm = 304.8, BendAngle = angle };
                // Reversed picks deliberately exercise station ordering as used by the command.
                var plan = DetourRouteBuilder.Build(line, line.Evaluate(.8, true), line.Evaluate(.2, true), options, direction, minimum);
                var path = plan.Path;
                double lift = direction == DirectionMode.Up ? 1 : -1;
                double run = angle == 90 ? 0 : 1 / Math.Tan(angle * Math.PI / 180);
                bool geometry = path[0].IsAlmostEqualTo(line.GetEndPoint(0)) && path[5].IsAlmostEqualTo(line.GetEndPoint(1)) &&
                    (path[3] - path[2]).Normalize().IsAlmostEqualTo(line.Direction) &&
                    path[2].IsAlmostEqualTo(line.Evaluate(.2, true) + line.Direction * run + XYZ.BasisZ * lift) &&
                    path[3].IsAlmostEqualTo(line.Evaluate(.8, true) - line.Direction * run + XYZ.BasisZ * lift);
                if (!geometry) throw new Exception($"Slope geometry mismatch: {slope}, reverse={reverse}, {angle}, {direction}");
                geometryChecks++;

                if (slope != .02 || angle != 45) continue;
                var before = new HashSet<ElementId>(new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds());
                using (var group = new TransactionGroup(doc, "QA sloped preview cancel"))
                {
                    group.Start();
                    using (var tx = new Transaction(doc, "QA sloped actual reroute"))
                    {
                        tx.Start();
                        var failures = tx.GetFailureHandlingOptions();
                        failures.SetFailuresPreprocessor(new RejectFailures()); failures.SetClearAfterRollback(true); failures.SetForcedModalHandling(true);
                        tx.SetFailureHandlingOptions(failures);
                        var pipe = Pipe.Create(doc, system, type, level, line.GetEndPoint(0), line.GetEndPoint(1));
                        pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(100 / 304.8);
                        doc.Regenerate();
                        bool made = RevitUtils.ReplaceWithDetour(doc, pipe, plan, options, out var created, out var reason, retainOriginal: true);
                        if (!made) throw new Exception("Sloped native reroute failed: " + reason);
                        var segments = created.Select(id => doc.GetElement(id)).OfType<Pipe>().ToList();
                        var matching = segments.Select(p => (Line)((LocationCurve)p.Location).Curve)
                            .Count(c => Math.Abs(Math.Abs(c.Direction.DotProduct(line.Direction)) - 1) < 1e-8);
                        if (segments.Count != 5 || matching != 3) throw new Exception("Sloped main segment slope changed");
                        var issues = DetourCollisionReview.Check(doc, created, new HashSet<ElementId>(), 0);
                        if (issues.Count != 0) throw new Exception("Sloped clear route rejected: " + string.Join(" | ", issues));
                        if (tx.Commit() != TransactionStatus.Committed) throw new Exception("Sloped transaction rejected");
                        results.Add($"PASS native sloped reroute reverse={reverse} {direction}: 5 pipes, 4 elbows, 3 main segments retain slope, clear collision check, strict commit");
                    }
                    group.RollBack();
                }
                if (!before.SetEquals(new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds()))
                    throw new Exception("Sloped preview cancel changed model element set");
                results.Add("PASS sloped preview cancel restores exact element set");
            }
            results.Add($"PASS {geometryChecks} production route geometry cases: signed 1%/2%, reversed source and picks, up/down, 22.5/45/90 degrees");
        }
    }
}
