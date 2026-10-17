using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class SplitResult
    {
        public int TargetElements { get; set; }
        public int OriginalDeleted { get; set; }
        public int NewElementsCreated { get; set; }
        public int Skipped { get; set; }
        public int FailedOperations { get; set; }
        public List<string> FailureSamples { get; } = new List<string>();
    }

    /// <summary>
    /// 樓板分割過濾器：允許選取樓板、結構構架（梁）、結構柱。
    /// </summary>
    internal sealed class FloorAndFramingFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            var id = elem.Category?.Id;
            if (id == null) return false;
            var bic = (BuiltInCategory)(int)id.GetIdValue();
            return bic == BuiltInCategory.OST_Floors || IsCutter(elem);
        }

        internal static bool IsCutter(Element elem) => elem?.Category != null &&
            (elem.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_StructuralFraming ||
             elem.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_StructuralColumns);

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    /// <summary>
    /// 牆分割 - 目標牆過濾器（僅選牆）
    /// </summary>
    internal sealed class WallTargetFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            var id = elem.Category?.Id;
            if (id == null) return false;
            var bic = (BuiltInCategory)(int)id.GetIdValue();
            return bic == BuiltInCategory.OST_Walls;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    /// <summary>
    /// 牆分割 - 切割構件過濾器（結構柱、結構構架、牆）
    /// </summary>
    internal sealed class WallCutterFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            var id = elem.Category?.Id;
            if (id == null) return false;
            var bic = (BuiltInCategory)(int)id.GetIdValue();
            return bic == BuiltInCategory.OST_StructuralColumns
                || bic == BuiltInCategory.OST_StructuralFraming
                || bic == BuiltInCategory.OST_Walls;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    internal static class SplitEngine
    {
        private const double Tolerance = 1e-6;
        private const double MinSegmentLength = 0.1;

        // Classify floor boundary loops by containment: inner loops remain holes.
        public static SplitResult RunSplitFloor(Document doc, IList<Floor> floors, IList<Element> cutters)
        {
            return Run(doc, floors.Cast<Element>().ToList(), cutters, RebuildFloor);
        }

        public static SplitResult RunSplitWall(Document doc, IList<Wall> walls, IList<Element> cutters)
        {
            // No bounding-box height shortcut: a partial beam overlap must not
            // remove a band across the full length or thickness of a wall.
            return Run(doc, walls.Cast<Element>().ToList(), cutters, RebuildWall);
        }

        private static SplitResult Run(Document doc, IList<Element> originals, IList<Element> cutters,
            Func<Document, Element, IList<Element>> rebuild)
        {
            var result = new SplitResult { TargetElements = originals.Count };
            foreach (var original in originals)
            {
                var originalId = original.Id;
                using (var transaction = new Transaction(doc, "安全分割構件"))
                {
                    try
                    {
                        RequireSafeHost(doc, original, cutters);
                        if (transaction.Start() != TransactionStatus.Started)
                            throw new InvalidOperationException("無法開始分割交易。");
                        var failures = new RollbackOnFailure(original);
                        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                            .SetFailuresPreprocessor(failures)
                            .SetClearAfterRollback(true)
                            .SetForcedModalHandling(true));
                        foreach (var cutter in cutters.Where(c => c != null && c.Id != originalId))
                        {
                            if (!JoinGeometryUtils.AreElementsJoined(doc, original, cutter))
                            {
                                // Selected batch cutters need not intersect every
                                // host. Preserve pre-existing joins regardless of
                                // the intersection filter's post-cut geometry.
                                if (!new ElementIntersectsElementFilter(original).PassesFilter(cutter)) continue;
                                JoinGeometryUtils.JoinGeometry(doc, original, cutter);
                            }
                            if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, cutter, original))
                                JoinGeometryUtils.SwitchJoinOrder(doc, original, cutter);
                        }
                        doc.Regenerate();
                        var expectedSolids = GetSolids(original);
                        var replacements = rebuild(doc, original);
                        if (replacements.Count < 2)
                            throw new UnsupportedSplitException("未形成至少兩個可安全重建的區域。");
                        var copied = new List<CopiedParameter>();
                        foreach (var replacement in replacements)
                        {
                            copied.AddRange(CopyParameters(original, replacement));
                            failures.AllowInheritedMark(replacement);
                        }

                        // Inspect the actual deletion cascade. Hosted inserts,
                        // annotations, constraints and analytical elements must
                        // never silently disappear with the original host.
                        var allowed = GetOwnedSketchElements(doc, original);
                        var deleted = doc.Delete(originalId);
                        if (!deleted.Contains(originalId) || deleted.Any(id => !allowed.Contains(id)))
                            throw new UnsupportedSplitException("刪除會影響相依構件，已保留原構件。");
                        doc.Regenerate();
                        foreach (var parameter in copied) parameter.Verify();
                        VerifyGeometry(expectedSolids, replacements);
                        var status = transaction.Commit();
                        if (status != TransactionStatus.Committed)
                            throw new InvalidOperationException("分割交易未提交：" + status);
                        // Publish counts only after the complete original's
                        // joins, replacements and deletion have committed.
                        result.OriginalDeleted++;
                        result.NewElementsCreated += replacements.Count;
                    }
                    catch (UnsupportedSplitException ex)
                    {
                        RollBack(transaction);
                        result.Skipped++;
                        AddSample(result, originalId, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        RollBack(transaction);
                        result.FailedOperations++;
                        AddSample(result, originalId, ex.Message);
                    }
                }
            }
            return result;
        }

        private static void RollBack(Transaction transaction)
        {
            if (transaction.GetStatus() == TransactionStatus.Started &&
                transaction.RollBack() != TransactionStatus.RolledBack)
                throw new InvalidOperationException("無法確認交易已回復，請停止操作並檢查文件。");
            if (transaction.GetStatus() == TransactionStatus.Pending)
                throw new InvalidOperationException("交易仍等待 Revit 處理，不能繼續分割。");
        }

        private static void AddSample(SplitResult result, ElementId id, string message)
        {
            if (result.FailureSamples.Count < 5)
                result.FailureSamples.Add($"Element({id}): {message}");
        }

        private static void RequireSafeHost(Document doc, Element original, IList<Element> cutters)
        {
            if (original.Pinned || original.GroupId != ElementId.InvalidElementId ||
                original.AssemblyInstanceId != ElementId.InvalidElementId || original.DesignOption != null ||
                original.GetEntitySchemaGuids().Count != 0 || original.GetMaterialIds(true).Count != 0)
                throw new UnsupportedSplitException("固定、群組、組合、設計選項、面塗料或自訂資料構件不支援重建。");
            var cutterIds = new HashSet<ElementId>(cutters.Where(c => c != null).Select(c => c.Id));
            if (JoinGeometryUtils.GetJoinedElements(doc, original).Any(id => !cutterIds.Contains(id)))
                throw new UnsupportedSplitException("構件含其他既有接合，無法安全保留。");
            var allowed = GetOwnedSketchElements(doc, original);
            if (original.GetDependentElements(null).Any(id => !allowed.Contains(id)))
                throw new UnsupportedSplitException("構件具有宿主、標註或其他相依元素，無法安全重建。");
        }

        private static HashSet<ElementId> GetOwnedSketchElements(Document doc, Element original)
        {
            var allowed = new HashSet<ElementId> { original.Id };
            foreach (var id in original.GetDependentElements(new ElementClassFilter(typeof(Sketch))))
            {
                allowed.Add(id);
                // Only model curves belonging to this sketch are intrinsic.
                // Do not whitelist dimensions or other downstream dependencies.
                var sketch = doc.GetElement(id) as Sketch;
                if (sketch == null) continue;
                foreach (var curveId in sketch.GetDependentElements(new ElementClassFilter(typeof(CurveElement))))
                    allowed.Add(curveId);
                allowed.Add(sketch.SketchPlane.Id);
            }
            return allowed;
        }

        private static IList<Element> RebuildFloor(Document doc, Element original)
        {
            var floor = (Floor)original;
            var references = HostObjectUtils.GetTopFaces(floor);
            var level = doc.GetElement(floor.LevelId) as Level;
            var type = doc.GetElement(floor.GetTypeId()) as FloorType;
            if (level == null || type == null) throw new UnsupportedSplitException("樓板樓層或類型無效。");
            var replacements = new List<Element>();
            foreach (var reference in references)
            {
                var face = floor.GetGeometryObjectFromReference(reference) as PlanarFace;
                if (face == null || Math.Abs(face.FaceNormal.DotProduct(XYZ.BasisZ) - 1) > Tolerance)
                    throw new UnsupportedSplitException("僅支援水平樓板，斜面與形狀編輯樓板保留原件。");
                var loops = face.GetEdgesAsCurveLoops();
                if (loops.Count == 0 || loops.Any(loop => loop.Count() < 3 ||
                    loop.Any(c => !(c is Line) || c.Length < doc.Application.ShortCurveTolerance)))
                    throw new UnsupportedSplitException("樓板分區需為有效直線封閉輪廓；曲線或過短邊保留原件。");
                // Revit may represent separate islands as multiple outer loops on ONE face.
                // Classify by containment, not face count or loop winding.
                foreach (var region in GroupFloorLoops(loops))
                {
                    var elevation = region[0].First().GetEndPoint(0).Z;
                    var shift = Transform.CreateTranslation(new XYZ(0, 0, level.Elevation - elevation));
                    var levelLoops = region.Select(loop => CurveLoop.CreateViaTransform(loop, shift)).ToList();
                    // Source height offset is restored by CopyParameters below.
                    replacements.Add(Floor.Create(doc, levelLoops, type.Id, level.Id));
                }
            }
            if (replacements.Count < 2)
                throw new UnsupportedSplitException("梁柱未將樓板分成至少兩個獨立區域；僅形成柱孔或局部缺口不視為分割。");
            return replacements;
        }

        private static IList<IList<CurveLoop>> GroupFloorLoops(IList<CurveLoop> loops)
        {
            var polygons = loops.Select(l => l.Select(c => c.GetEndPoint(0)).ToList()).ToList();
            var areas = polygons.Select(p => Math.Abs(p.Select((v, i) =>
                (v.X - p[0].X) * (p[(i + 1) % p.Count].Y - p[0].Y) -
                (p[(i + 1) % p.Count].X - p[0].X) * (v.Y - p[0].Y)).Sum()) / 2).ToArray();
            if (areas.Any(a => a <= Tolerance * Tolerance))
                throw new UnsupportedSplitException("樓板輪廓面積無效，保留原件。");
            var parents = Enumerable.Repeat(-1, loops.Count).ToArray();
            for (int i = 0; i < loops.Count; i++)
                for (int j = 0; j < loops.Count; j++)
                    if (i != j && areas[j] > areas[i] && ContainsPoint(polygons[j], polygons[i][0]) &&
                        (parents[i] < 0 || areas[j] < areas[parents[i]])) parents[i] = j;
            var depths = new int[loops.Count];
            for (int i = 0; i < loops.Count; i++)
                for (int p = parents[i]; p >= 0; p = parents[p]) depths[i]++;
            var regions = new List<IList<CurveLoop>>();
            for (int i = 0; i < loops.Count; i++)
                if (depths[i] % 2 == 0)
                {
                    var region = new List<CurveLoop> { loops[i] };
                    for (int j = 0; j < loops.Count; j++) if (parents[j] == i) region.Add(loops[j]);
                    regions.Add(region);
                }
            return regions;
        }

        private static bool ContainsPoint(IList<XYZ> polygon, XYZ point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[j]; var b = polygon[i];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double cross = dx * (point.Y - a.Y) - dy * (point.X - a.X);
                if (Math.Abs(cross) <= Tolerance * Math.Sqrt(dx * dx + dy * dy) &&
                    point.X >= Math.Min(a.X, b.X) - Tolerance && point.X <= Math.Max(a.X, b.X) + Tolerance &&
                    point.Y >= Math.Min(a.Y, b.Y) - Tolerance && point.Y <= Math.Max(a.Y, b.Y) + Tolerance)
                    throw new UnsupportedSplitException("樓板輪廓相接，無法明確辨識分區與孔洞，保留原件。");
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < a.X + (point.Y - a.Y) * dx / dy)
                    inside = !inside;
            }
            return inside;
        }

        private static IList<Element> RebuildWall(Document doc, Element original)
        {
            var wall = (Wall)original;
            var location = wall.Location as LocationCurve;
            var line = location?.Curve as Line;
            var type = doc.GetElement(wall.GetTypeId()) as WallType;
            var level = doc.GetElement(wall.LevelId) as Level;
            if (line == null || type == null || type.Kind != WallKind.Basic || level == null ||
                Math.Abs(line.Direction.Z) > Tolerance ||
                wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() != ElementId.InvalidElementId ||
                (wall.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger() ?? 0) != 0 ||
                (wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() ?? 0) != 0)
                throw new UnsupportedSplitException("僅支援直線、垂直、未附著且未連接頂部的基本牆。");
            var references = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
            if (references.Count < 2)
                throw new UnsupportedSplitException("外側面未形成獨立區域；局部梁重疊、孔洞或巢狀環不支援。");
            var start = line.GetEndPoint(0);
            var direction = line.Direction;
            var replacements = new List<Element>();
            foreach (var reference in references)
            {
                var face = wall.GetGeometryObjectFromReference(reference) as PlanarFace;
                if (face == null || Math.Abs(face.FaceNormal.Z) > Tolerance ||
                    Math.Abs(face.FaceNormal.DotProduct(direction)) > Tolerance)
                    throw new UnsupportedSplitException("牆面不是垂直平面。");
                var loop = GetRectangle(face, direction, XYZ.BasisZ);
                var points = loop.Select(c => c.GetEndPoint(0)).ToList();
                var min = points.Min(p => direction.DotProduct(p - start));
                var max = points.Max(p => direction.DotProduct(p - start));
                var bottom = points.Min(p => p.Z);
                var top = points.Max(p => p.Z);
                var segment = Line.CreateBound(start + direction.Multiply(min), start + direction.Multiply(max));
                var structural = (wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM)?.AsInteger() ?? 0) != 0;
                var replacement = Wall.Create(doc, segment, type.Id, level.Id, top - bottom,
                    bottom - level.Elevation, wall.Flipped, structural);
                WallUtils.DisallowWallJoinAtEnd(replacement, 0);
                WallUtils.DisallowWallJoinAtEnd(replacement, 1);
                replacements.Add(replacement);
            }
            return replacements;
        }

        private static CurveLoop GetRectangle(PlanarFace face, XYZ axisX, XYZ axisY)
        {
            var loops = face.GetEdgesAsCurveLoops();
            if (loops.Count != 1)
                throw new UnsupportedSplitException("孔洞或巢狀環不支援，已保留原構件。");
            var edges = loops[0].ToList();
            if (edges.Count != 4 || edges.Any(c => !(c is Line) || c.Length < MinSegmentLength))
                throw new UnsupportedSplitException("目前僅支援矩形區域，曲線或複雜輪廓已跳過。");
            var xEdges = 0;
            var yEdges = 0;
            foreach (Line edge in edges)
            {
                if (Math.Abs(Math.Abs(edge.Direction.DotProduct(axisX)) - 1) < Tolerance) xEdges++;
                else if (Math.Abs(Math.Abs(edge.Direction.DotProduct(axisY)) - 1) < Tolerance) yEdges++;
                else throw new UnsupportedSplitException("斜向或非矩形區域不支援。");
            }
            if (xEdges != 2 || yEdges != 2)
                throw new UnsupportedSplitException("輪廓不是矩形。");
            return loops[0];
        }

        private sealed class CopiedParameter
        {
            internal Parameter Target;
            internal object Value;
            internal bool Matches()
            {
                switch (Target.StorageType)
                {
                    case StorageType.Double: return Target.AsDouble() == (double)Value;
                    case StorageType.Integer: return Target.AsInteger() == (int)Value;
                    case StorageType.String: return (Target.AsString() ?? "") == (string)Value;
                    case StorageType.ElementId: return Target.AsElementId() == (ElementId)Value;
                    default: return false;
                }
            }
            internal void Verify()
            {
                if (!Matches()) throw new UnsupportedSplitException("重建後參數未保留：" + Target.Definition.Name);
            }
        }

        private static IList<CopiedParameter> CopyParameters(Element original, Element replacement)
        {
            var copied = new List<CopiedParameter>();
            foreach (Parameter source in original.Parameters)
            {
                if (source.IsReadOnly || source.StorageType == StorageType.None ||
                    (!source.HasValue && source.StorageType != StorageType.String)) continue;
                var isBuiltIn = source.Id.GetIdValue() < 0;
                var builtIn = isBuiltIn ? (BuiltInParameter)(int)source.Id.GetIdValue() : default(BuiltInParameter);
                // These values describe the new region, not the source's region.
                if (original is Wall && isBuiltIn && (builtIn == BuiltInParameter.WALL_BASE_OFFSET ||
                    builtIn == BuiltInParameter.WALL_USER_HEIGHT_PARAM)) continue;
                Parameter target;
                if (source.IsShared) target = replacement.get_Parameter(source.GUID);
                else if (isBuiltIn) target = replacement.get_Parameter(builtIn);
                else target = replacement.get_Parameter(source.Definition);
                if (target == null || target.IsReadOnly || target.StorageType != source.StorageType)
                    throw new UnsupportedSplitException("無法保留參數：" + source.Definition.Name);
                var copy = new CopiedParameter { Target = target };
                switch (source.StorageType)
                {
                    case StorageType.Double: copy.Value = source.AsDouble(); if (!copy.Matches()) target.Set((double)copy.Value); break;
                    case StorageType.Integer: copy.Value = source.AsInteger(); if (!copy.Matches()) target.Set((int)copy.Value); break;
                    case StorageType.String: copy.Value = source.AsString() ?? ""; if (!copy.Matches()) target.Set((string)copy.Value); break;
                    case StorageType.ElementId: copy.Value = source.AsElementId(); if (!copy.Matches()) target.Set((ElementId)copy.Value); break;
                    default: throw new UnsupportedSplitException("參數類型無法保留：" + source.Definition.Name);
                }
                copy.Verify();
                copied.Add(copy);
            }
            return copied;
        }

        private static IList<Solid> GetSolids(Element element)
        {
            var solids = new List<Solid>();
            var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
            if (geometry != null)
                foreach (var item in geometry)
                {
                    if (item is Solid solid && solid.Volume > Tolerance)
                        solids.Add(SolidUtils.CreateTransformed(solid, Transform.Identity));
                    else if (item is GeometryInstance)
                        throw new UnsupportedSplitException("巢狀幾何不支援安全驗證。");
                }
            if (solids.Count == 0) throw new UnsupportedSplitException("無法驗證構件實體。");
            return solids;
        }

        private static void VerifyGeometry(IList<Solid> expected, IList<Element> replacements)
        {
            var actual = replacements.SelectMany(GetSolids).ToList();
            var expectedVolume = expected.Sum(s => s.Volume);
            var tolerance = Math.Max(Tolerance, expectedVolume * 1e-6);
            var actualVolume = actual.Sum(s => s.Volume);
            var covered = 0.0;
            foreach (var solid in actual)
                foreach (var source in expected)
                    covered += BooleanOperationsUtils.ExecuteBooleanOperation(solid, source,
                        BooleanOperationsType.Intersect).Volume;
            // Equal volume alone does not establish equal shape or placement.
            if (Math.Abs(expectedVolume - actualVolume) > tolerance ||
                Math.Abs(covered - expectedVolume) > tolerance)
                throw new UnsupportedSplitException("新舊實體形狀不一致，分割已回復。");
            for (int i = 0; i < actual.Count; i++)
                for (int j = i + 1; j < actual.Count; j++)
                    if (BooleanOperationsUtils.ExecuteBooleanOperation(actual[i], actual[j],
                        BooleanOperationsType.Intersect).Volume > tolerance)
                        throw new UnsupportedSplitException("重建區域互相重疊，分割已回復。");
        }

        private sealed class UnsupportedSplitException : Exception
        {
            public UnsupportedSplitException(string message) : base(message) { }
        }

        internal sealed class RollbackOnFailure : IFailuresPreprocessor
        {
            private readonly HashSet<ElementId> inheritedMarkIds = new HashSet<ElementId>();
            private readonly string inheritedMark;
            internal RollbackOnFailure(Element original)
            {
                // Floor pieces intentionally retain the source Mark. No other warning is ignored.
                inheritedMark = original is Floor ? original.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() : null;
                if (!string.IsNullOrEmpty(inheritedMark)) inheritedMarkIds.Add(original.Id);
            }
            internal void AllowInheritedMark(Element replacement)
            {
                if (!string.IsNullOrEmpty(inheritedMark) &&
                    replacement.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() == inheritedMark)
                    inheritedMarkIds.Add(replacement.Id);
            }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var failure in accessor.GetFailureMessages())
                {
                    var ids = failure.GetFailingElementIds();
                    if (failure.GetSeverity() == FailureSeverity.Warning &&
                        failure.GetFailureDefinitionId() == BuiltInFailures.GeneralFailures.DuplicateValue &&
                        ids.Count > 0 && ids.All(inheritedMarkIds.Contains))
                        accessor.DeleteWarning(failure);
                    else return FailureProcessingResult.ProceedWithRollBack;
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}
