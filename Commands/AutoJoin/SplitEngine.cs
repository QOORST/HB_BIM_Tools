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
    /// 樓板分割過濾器：允許選取樓板 + 結構構架（梁）
    /// </summary>
    internal sealed class FloorAndFramingFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            var id = elem.Category?.Id;
            if (id == null) return false;
            var bic = (BuiltInCategory)(int)id.GetIdValue();
            return bic == BuiltInCategory.OST_Floors
                || bic == BuiltInCategory.OST_StructuralFraming;
        }

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

        // Only simple, disjoint rectangular regions are supported. A face with
        // multiple loops can contain holes: never treat each loop as a new host.
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
                        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                            .SetFailuresPreprocessor(new RollbackOnFailure())
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
                        foreach (var replacement in replacements)
                            CopyParameters(original, replacement);

                        // Inspect the actual deletion cascade. Hosted inserts,
                        // annotations, constraints and analytical elements must
                        // never silently disappear with the original host.
                        var allowed = GetOwnedSketchElements(doc, original);
                        var deleted = doc.Delete(originalId);
                        if (!deleted.Contains(originalId) || deleted.Any(id => !allowed.Contains(id)))
                            throw new UnsupportedSplitException("刪除會影響相依構件，已保留原構件。");
                        doc.Regenerate();
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
            if (references.Count < 2)
                throw new UnsupportedSplitException("頂面未形成多個獨立面；孔洞或巢狀環不視為分割。");
            var level = doc.GetElement(floor.LevelId) as Level;
            var type = doc.GetElement(floor.GetTypeId()) as FloorType;
            if (level == null || type == null) throw new UnsupportedSplitException("樓板樓層或類型無效。");
            var replacements = new List<Element>();
            foreach (var reference in references)
            {
                var face = floor.GetGeometryObjectFromReference(reference) as PlanarFace;
                if (face == null || Math.Abs(face.FaceNormal.DotProduct(XYZ.BasisZ) - 1) > Tolerance)
                    throw new UnsupportedSplitException("僅支援水平、無孔洞的矩形樓板區域。");
                var loop = GetRectangle(face, XYZ.BasisX, XYZ.BasisY);
                var elevation = loop.First().GetEndPoint(0).Z;
                var levelLoop = CurveLoop.CreateViaTransform(loop,
                    Transform.CreateTranslation(new XYZ(0, 0, level.Elevation - elevation)));
                // Source height offset is restored by CopyParameters below.
                replacements.Add(CreateFloorFromLoop(doc, levelLoop, type, level));
            }
            return replacements;
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

        private static void CopyParameters(Element original, Element replacement)
        {
            foreach (Parameter source in original.Parameters)
            {
                if (source.IsReadOnly || !source.HasValue || source.StorageType == StorageType.None) continue;
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
                bool saved;
                switch (source.StorageType)
                {
                    case StorageType.Double: saved = target.AsDouble() == source.AsDouble() || target.Set(source.AsDouble()); break;
                    case StorageType.Integer: saved = target.AsInteger() == source.AsInteger() || target.Set(source.AsInteger()); break;
                    case StorageType.String: saved = target.AsString() == source.AsString() || target.Set(source.AsString() ?? ""); break;
                    case StorageType.ElementId: saved = target.AsElementId() == source.AsElementId() || target.Set(source.AsElementId()); break;
                    default: saved = false; break;
                }
                if (!saved) throw new UnsupportedSplitException("參數寫入失敗：" + source.Definition.Name);
            }
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

        private static Floor CreateFloorFromLoop(Document doc, CurveLoop loop, FloorType type, Level level)
        {
#if REVIT2022
            var array = new CurveArray();
            foreach (var curve in loop) array.Append(curve);
#pragma warning disable CS0618
            return doc.Create.NewFloor(array, type, level, false);
#pragma warning restore CS0618
#else
            return Floor.Create(doc, new List<CurveLoop> { loop }, type.Id, level.Id);
#endif
        }

        private sealed class UnsupportedSplitException : Exception
        {
            public UnsupportedSplitException(string message) : base(message) { }
        }

        private sealed class RollbackOnFailure : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                // Do not accept warnings that may detach hosts, discard constraints
                // or otherwise repair the document by deleting more elements.
                return accessor.GetFailureMessages().Count == 0
                    ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
            }
        }
    }
}
