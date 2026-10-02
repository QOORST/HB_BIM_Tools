using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class TagFrameCatalog
    {
        private readonly Dictionary<ElementId, double[]> centers = new Dictionary<ElementId, double[]>();
        internal static TagFrameCatalog Read(Document project, IEnumerable<ElementId> types, List<string> issues)
        {
            var result = new TagFrameCatalog();
            foreach (var group in types.Distinct().Select(project.GetElement).OfType<FamilySymbol>().GroupBy(s => s.Family.Id))
            {
                Document family = null;
                try
                {
                    var first = group.First();
                    if (!first.Family.IsEditable) continue;
                    family = project.EditFamily(first.Family);
                    // Nested annotations can introduce independent origins and conditional frames.
                    if (new FilteredElementCollector(family).OfClass(typeof(FamilyInstance)).Any()) continue;
                    foreach (var symbol in group)
                    {
                        var type = family.FamilyManager.Types.Cast<FamilyType>().FirstOrDefault(t => t.Name == symbol.Name);
                        if (type == null) continue;
                        using (var tx = new Transaction(family, "讀取標籤線框族型"))
                        {
                            tx.Start();
                            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new AbortFailures()).SetClearAfterRollback(true));
                            family.FamilyManager.CurrentType = type;
                            if (tx.Commit() != TransactionStatus.Committed) continue;
                        }
                        var curves = new List<double[][]>();
                        foreach (var element in new FilteredElementCollector(family).OfClass(typeof(CurveElement)).Cast<CurveElement>())
                        {
                            if (!(element is DetailCurve)) continue;
                            var curve = element.GeometryCurve;
                            if (curve == null || !curve.IsBound) continue;
                            if (!(curve is Line) && !(curve is Arc)) { curves.Clear(); break; }
                            curves.Add(curve.Tessellate().Select(p => new[] { p.X * 304.8, p.Y * 304.8, p.Z * 304.8 }).ToArray());
                        }
                        if (TagFrameMath.TryCenter(curves, out var center)) result.centers[symbol.Id] = center;
                    }
                }
                catch (Exception ex) { issues.Add("標籤線框無法讀取，採包圍框並列為待複核：" + ex.Message); }
                finally
                {
                    if (family != null && !family.Close(false)) throw new InvalidOperationException("無法關閉線框量測族群文件；未載回或儲存模型。");
                }
            }
            return result;
        }
        internal bool TryCenter(IndependentTag tag, View view, out XYZ center)
        {
            center = null;
            if (!centers.TryGetValue(tag.GetTypeId(), out var local)) return false;
            // Orientation is a placement mode, not the rendered angle (families may rotate with their host).
            double angle = tag.RotationAngle;
            if (double.IsNaN(angle) || double.IsInfinity(angle)) return false;
            var offset = TagFrameMath.Offset(local, angle, Math.Max(1, view.Scale));
            center = tag.TagHeadPosition + view.RightDirection * (offset[0] / 304.8) + view.UpDirection * (offset[1] / 304.8);
            return true;
        }
        private sealed class AbortFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor f) => f.GetFailureMessages().Count > 0
                ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
