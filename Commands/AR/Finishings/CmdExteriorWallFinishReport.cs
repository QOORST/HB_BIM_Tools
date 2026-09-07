using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WinForms = System.Windows.Forms;
using YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings
{
    /// <summary>
    /// 外牆粉刷驗算 / 報表第一版。
    /// 不生成粉刷面，以牆外側面淨面積作為主量，供交付明細報表驗算使用。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdExteriorWallFinishReport : IExternalCommand
    {
        private const double Ft2ToM2 = 0.09290304;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Finishings.RoomFinish") &&
                    !licenseManager.HasFeatureAccess("Finishings.Generate"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援外牆粉刷驗算功能。\n\n" +
                        "請升級至試用版、標準版或專業版以使用此功能。");
                    return Result.Cancelled;
                }

                var uiDoc = commandData.Application.ActiveUIDocument;
                var doc = uiDoc?.Document;
                if (uiDoc == null || doc == null)
                {
                    message = "無法取得有效的 Revit 文件";
                    return Result.Failed;
                }

                ExteriorWallSelectionScope scope;
                try
                {
                    scope = PickExteriorWallSelectionScope(uiDoc);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (scope == null)
                    return Result.Cancelled;

                var rows = BuildReportRows(uiDoc, scope);
                if (rows.Count == 0)
                {
                    TaskDialog.Show("外牆粉刷驗算", "找不到可計算的外牆粉刷構件。請先選取外牆/柱/梁，或確認外牆類型/功能設定。");
                    return Result.Cancelled;
                }

                rows = rows
                    .Where(r => r != null && r.NetExteriorAreaM2 > 0)
                    .OrderBy(r => GetLevelSortKey(doc, r.LevelId))
                    .ThenBy(r => r.LevelName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.Direction, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.CategoryName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.ElementTypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (rows.Count == 0)
                {
                    TaskDialog.Show("外牆粉刷驗算", "已找到外牆候選牆，但無法取得有效外側面面積。");
                    return Result.Cancelled;
                }

                var saveDialog = new WinForms.SaveFileDialog
                {
                    Title = "匯出外牆粉刷驗算報表",
                    Filter = "Excel 活頁簿 (*.xlsx)|*.xlsx",
                    FileName = $"外牆粉刷驗算報表_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };

                if (saveDialog.ShowDialog() != WinForms.DialogResult.OK)
                    return Result.Cancelled;

                ExportWorkbook(saveDialog.FileName, rows);

                string viewName = null;
                using (var t = new Transaction(doc, "建立外牆粉刷驗算視圖"))
                {
                    t.Start();
                    viewName = CreateCheckView(doc, rows.Select(r => r.ElementId).Distinct().ToList());
                    t.Commit();
                }

                var total = rows.Sum(r => r.NetExteriorAreaM2);
                var opening = rows.Sum(r => r.OpeningAreaM2);
                var elementCount = rows.Select(r => RevitCompat.GetElementIdValue(r.ElementId)).Distinct().Count();
                TaskDialog.Show("外牆粉刷驗算",
                    $"外牆粉刷報表已匯出。\n\n" +
                    $"構件數：{elementCount}（明細面數：{rows.Count}）\n" +
                    $"外側面淨面積：{total:F2} ㎡\n" +
                    $"門窗/洞口參考扣除：{opening:F2} ㎡\n" +
                    $"驗算視圖：{viewName ?? "未建立"}\n\n" +
                    $"檔案：{saveDialog.FileName}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("外牆粉刷驗算錯誤", $"{ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        private static ExteriorWallSelectionScope PickExteriorWallSelectionScope(UIDocument uiDoc)
        {
            var doc = uiDoc.Document;

            TaskDialog.Show("外牆粉刷驗算",
                "請先框選要驗算的外牆立面範圍。\n\n" +
                "建議只框單一立面或單一檢核區段，避免不同外牆區域混在同一份驗算。");

            var pickedBox = uiDoc.Selection.PickBox(PickBoxStyle.Enclosing, "框選外牆粉刷驗算範圍");
            if (pickedBox == null)
                return null;

            TaskDialog.Show("外牆粉刷驗算",
                "請點選一個代表性的外牆粉刷面。\n\n" +
                "程式會以該面的外側法線作為本次立面計算方向。");

            var faceRef = uiDoc.Selection.PickObject(ObjectType.Face, "點選代表計算方向的外牆面");
            var host = doc.GetElement(faceRef.ElementId);
            var face = host?.GetGeometryObjectFromReference(faceRef) as Face;
            if (face == null)
            {
                TaskDialog.Show("外牆粉刷驗算", "無法取得點選面的幾何資訊。");
                return null;
            }

            var normal = GetFaceNormal(face);
            normal = new XYZ(normal.X, normal.Y, 0);
            if (normal.GetLength() < 1e-9)
            {
                TaskDialog.Show("外牆粉刷驗算", "點選面方向接近水平，無法作為外牆立面方向。請點選垂直外牆面。");
                return null;
            }

            normal = normal.Normalize();
            return new ExteriorWallSelectionScope
            {
                Box = BuildScopeBox(pickedBox),
                Direction = normal,
                DirectionName = DirectionNameFromNormal(normal)
            };
        }

        private static XYZ GetFaceNormal(Face face)
        {
            if (face is PlanarFace pf)
                return pf.FaceNormal;

            var bb = face.GetBoundingBox();
            var uv = new UV((bb.Min.U + bb.Max.U) * 0.5, (bb.Min.V + bb.Max.V) * 0.5);
            return face.ComputeNormal(uv);
        }

        private static BoundingBoxXYZ BuildScopeBox(PickedBox pickedBox)
        {
            var a = pickedBox.Min;
            var b = pickedBox.Max;
            var min = new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            var max = new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

            // 在立面/剖面視圖 PickBox 時，其中一個深度方向常接近 0。
            // 加一點厚度，讓框選能捕捉同一外殼深度附近的牆柱梁。
            const double minDepthFt = 3000.0 / 304.8;
            if (max.X - min.X < minDepthFt)
            {
                var c = (max.X + min.X) * 0.5;
                min = new XYZ(c - minDepthFt * 0.5, min.Y, min.Z);
                max = new XYZ(c + minDepthFt * 0.5, max.Y, max.Z);
            }
            if (max.Y - min.Y < minDepthFt)
            {
                var c = (max.Y + min.Y) * 0.5;
                min = new XYZ(min.X, c - minDepthFt * 0.5, min.Z);
                max = new XYZ(max.X, c + minDepthFt * 0.5, max.Z);
            }
            if (max.Z - min.Z < minDepthFt)
            {
                var c = (max.Z + min.Z) * 0.5;
                min = new XYZ(min.X, min.Y, c - minDepthFt * 0.5);
                max = new XYZ(max.X, max.Y, c + minDepthFt * 0.5);
            }

            return new BoundingBoxXYZ { Min = min, Max = max };
        }

        private static List<ExteriorWallReportRow> BuildReportRows(UIDocument uiDoc, ExteriorWallSelectionScope scope)
        {
            var doc = uiDoc.Document;
            var targets = CollectScopedTargets(doc, scope);
            var contexts = new List<ExteriorWallContext>
            {
                new ExteriorWallContext
                {
                    WallId = ElementId.InvalidElementId,
                    Direction = scope.Direction,
                    DirectionName = scope.DirectionName,
                    Center = (scope.Box.Min + scope.Box.Max) * 0.5,
                    ExpandedBox = scope.Box
                }
            };

            var rows = new List<ExteriorWallReportRow>();
            var faceKeys = new HashSet<string>();

            foreach (var element in targets)
            {
                if (element is Wall wall)
                {
                    rows.AddRange(BuildWallReportRows(doc, wall, scope, faceKeys));
                    continue;
                }

                rows.AddRange(BuildColumnBeamRows(doc, element, contexts, faceKeys));
            }

            return rows;
        }

        private static List<Element> CollectScopedTargets(Document doc, ExteriorWallSelectionScope scope)
        {
            var result = new List<Element>();
            var seen = new HashSet<long>();
            var outline = new Autodesk.Revit.DB.Outline(scope.Box.Min, scope.Box.Max);
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            var categories = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralFraming
            };

            foreach (var cat in categories)
            {
                foreach (var element in new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbFilter)
                    .ToElements())
                {
                    if (!IsSupportedSemiAutoElement(element))
                        continue;

                    if (seen.Add(RevitCompat.GetElementIdValue(element.Id)))
                        result.Add(element);
                }
            }

            return result;
        }

        private static bool IsSupportedSemiAutoElement(Element element)
        {
            if (element == null || element.Category == null)
                return false;

            if (FinishingElementGuard.IsManagedFinishingElement(element))
                return false;

            var cat = RevitCompat.GetElementIdValue(element.Category.Id);
            if (cat == (long)BuiltInCategory.OST_Walls)
                return element is Wall wall && wall.WallType != null && wall.WallType.Kind == WallKind.Basic;

            return cat == (long)BuiltInCategory.OST_StructuralColumns
                || cat == (long)BuiltInCategory.OST_Columns
                || cat == (long)BuiltInCategory.OST_StructuralFraming;
        }

        private static List<Element> CollectAutomaticTargets(Document doc, IList<Wall> exteriorWalls, IList<ExteriorWallContext> contexts)
        {
            var targets = exteriorWalls.Cast<Element>().ToList();
            var seen = new HashSet<long>(targets.Select(e => RevitCompat.GetElementIdValue(e.Id)));

            var categories = new[]
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralFraming
            };

            foreach (var cat in categories)
            {
                foreach (var element in new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements())
                {
                    if (!IsSupportedExteriorFinishElement(element))
                        continue;

                    var bb = element.get_BoundingBox(null);
                    if (bb == null)
                        continue;

                    if (!contexts.Any(c => BoxesOverlap(bb, c.ExpandedBox)))
                        continue;

                    if (seen.Add(RevitCompat.GetElementIdValue(element.Id)))
                        targets.Add(element);
                }
            }

            return targets;
        }

        private static bool IsSupportedExteriorFinishElement(Element element)
        {
            if (element == null || element.Category == null)
                return false;

            if (FinishingElementGuard.IsManagedFinishingElement(element))
                return false;

            var cat = RevitCompat.GetElementIdValue(element.Category.Id);
            if (cat == (long)BuiltInCategory.OST_Walls)
                return element is Wall wall && IsExteriorWallCandidate(wall);

            return cat == (long)BuiltInCategory.OST_StructuralColumns
                || cat == (long)BuiltInCategory.OST_Columns
                || cat == (long)BuiltInCategory.OST_StructuralFraming;
        }

        private static bool BoxesOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
                && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
                && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }

        private static BoundingBoxXYZ ExpandBox(BoundingBoxXYZ box, double xy, double z)
        {
            return new BoundingBoxXYZ
            {
                Min = new XYZ(box.Min.X - xy, box.Min.Y - xy, box.Min.Z - z),
                Max = new XYZ(box.Max.X + xy, box.Max.Y + xy, box.Max.Z + z)
            };
        }

        private static ExteriorWallContext BuildExteriorWallContext(Wall wall)
        {
            try
            {
                var bb = wall.get_BoundingBox(null);
                if (bb == null)
                    return null;

                var dir = GetExteriorNormal(wall);
                var center = (bb.Min + bb.Max) * 0.5;
                return new ExteriorWallContext
                {
                    WallId = wall.Id,
                    Direction = dir,
                    DirectionName = DirectionNameFromNormal(dir),
                    Center = center,
                    ExpandedBox = ExpandBox(bb, 3.0, 3.0)
                };
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<ExteriorWallReportRow> BuildColumnBeamRows(Document doc, Element element, IList<ExteriorWallContext> contexts, HashSet<string> faceKeys)
        {
            var rows = new List<ExteriorWallReportRow>();
            var level = GetElementLevel(doc, element);
            var type = doc.GetElement(element.GetTypeId());
            var categoryName = element.Category?.Name ?? "柱梁";
            var typeName = type?.Name ?? element.Name ?? string.Empty;
            SplitTypeLabel(typeName, out var code, out var materialFromName);
            var materialFallback = string.IsNullOrWhiteSpace(materialFromName)
                ? GetElementPrimaryMaterialName(doc, element)
                : materialFromName;

            foreach (var face in GetVerticalPlanarFaces(element))
            {
                var normal = new XYZ(face.FaceNormal.X, face.FaceNormal.Y, 0);
                if (normal.GetLength() < 1e-9)
                    continue;
                normal = normal.Normalize();

                var center = GetFaceCenter(face);
                var matched = contexts
                    .Where(c => normal.DotProduct(c.Direction) >= 0.70 && IsPointInsideXy(center, c.ExpandedBox))
                    .OrderBy(c => center.DistanceTo(c.Center))
                    .FirstOrDefault();
                if (matched == null)
                    continue;

                var key = BuildFaceKey(element.Id, center, normal);
                if (!faceKeys.Add(key))
                    continue;

                var materialName = GetFaceMaterialName(doc, face);
                if (string.IsNullOrWhiteSpace(materialName))
                    materialName = materialFallback;

                rows.Add(new ExteriorWallReportRow
                {
                    ElementId = element.Id,
                    LevelId = level?.Id ?? ElementId.InvalidElementId,
                    LevelName = level?.Name ?? "未分層",
                    Direction = matched.DirectionName,
                    CategoryName = categoryName,
                    ElementTypeId = element.GetTypeId(),
                    ElementTypeName = typeName,
                    FinishCode = string.IsNullOrWhiteSpace(code) ? typeName : code,
                    FinishMaterial = string.IsNullOrWhiteSpace(materialName) ? typeName : materialName,
                    NetExteriorAreaM2 = face.Area * Ft2ToM2,
                    OpeningAreaM2 = 0,
                    WallLengthM = 0,
                    WallHeightM = GetFaceHeightM(face),
                    Note = "柱/梁外立面垂直面；依相鄰外牆外側方向判斷"
                });
            }

            return rows;
        }

        private static bool IsPointInsideXy(XYZ p, BoundingBoxXYZ box)
        {
            return p.X >= box.Min.X && p.X <= box.Max.X
                && p.Y >= box.Min.Y && p.Y <= box.Max.Y
                && p.Z >= box.Min.Z && p.Z <= box.Max.Z;
        }

        private static string BuildFaceKey(ElementId elementId, XYZ center, XYZ normal)
        {
            return $"{RevitCompat.GetElementIdValue(elementId)}|" +
                   $"{Math.Round(center.X, 3)}|{Math.Round(center.Y, 3)}|{Math.Round(center.Z, 3)}|" +
                   $"{Math.Round(normal.X, 2)}|{Math.Round(normal.Y, 2)}";
        }

        private static IEnumerable<PlanarFace> GetVerticalPlanarFaces(Element element)
        {
            var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
            var geo = element.get_Geometry(opt);
            if (geo == null)
                yield break;

            foreach (var solid in EnumerateSolids(geo))
            {
                foreach (Face f in solid.Faces)
                {
                    if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) < 0.05 && pf.Area > 1e-6)
                        yield return pf;
                }
            }
        }

        private static IEnumerable<Solid> EnumerateSolids(GeometryElement geo)
        {
            foreach (GeometryObject obj in geo)
            {
                if (obj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-9)
                {
                    yield return solid;
                }
                else if (obj is GeometryInstance gi)
                {
                    var instGeo = gi.GetInstanceGeometry();
                    if (instGeo == null)
                        continue;

                    foreach (var s in EnumerateSolids(instGeo))
                        yield return s;
                }
            }
        }

        private static XYZ GetFaceCenter(PlanarFace face)
        {
            var pts = GetFacePoints(face).ToList();
            if (pts.Count == 0)
                return XYZ.Zero;

            return new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
        }

        private static double GetFaceHeightM(PlanarFace face)
        {
            var pts = GetFacePoints(face).ToList();
            if (pts.Count == 0)
                return 0;
            return (pts.Max(p => p.Z) - pts.Min(p => p.Z)) * 0.3048;
        }

        private static IEnumerable<XYZ> GetFacePoints(PlanarFace face)
        {
            foreach (var loop in face.GetEdgesAsCurveLoops())
            {
                foreach (var curve in loop)
                {
                    foreach (var p in curve.Tessellate())
                        yield return p;
                }
            }
        }

        private static Level GetElementLevel(Document doc, Element element)
        {
            if (element == null)
                return null;

            if (element.LevelId != ElementId.InvalidElementId && doc.GetElement(element.LevelId) is Level level)
                return level;

            var bb = element.get_BoundingBox(null);
            var z = bb?.Min.Z ?? 0;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - z))
                .FirstOrDefault();
        }

        private static string GetFaceMaterialName(Document doc, Face face)
        {
            try
            {
                var id = face.MaterialElementId;
                if (id != ElementId.InvalidElementId && doc.GetElement(id) is Material mat)
                    return mat.Name;
            }
            catch { }
            return string.Empty;
        }

        private static string GetElementPrimaryMaterialName(Document doc, Element element)
        {
            try
            {
                var materialIds = element.GetMaterialIds(false);
                foreach (var id in materialIds)
                {
                    if (doc.GetElement(id) is Material mat)
                        return mat.Name;
                }
            }
            catch { }
            return string.Empty;
        }

        private static bool IsExteriorWallCandidate(Wall wall)
        {
            if (wall == null || wall.WallType == null)
                return false;

            if (wall.WallType.Kind != WallKind.Basic)
                return false;

            if (FinishingElementGuard.IsManagedFinishingElement(wall))
                return false;

            var functionParam = wall.WallType.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
            if (functionParam != null && functionParam.StorageType == StorageType.Integer)
            {
                // 1 = Exterior in WallFunction enum value.
                if (functionParam.AsInteger() == 1)
                    return true;
            }

            var name = $"{wall.Name} {wall.WallType.Name}";
            return name.IndexOf("外牆", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("外墙", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Exterior", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<ExteriorWallReportRow> BuildWallReportRows(
            Document doc,
            Wall wall,
            ExteriorWallSelectionScope scope,
            HashSet<string> faceKeys)
        {
            var rows = new List<ExteriorWallReportRow>();
            var level = doc.GetElement(wall.LevelId) as Level;
            var type = wall.WallType;
            var materialName = GetExteriorMaterialName(doc, type);
            SplitTypeLabel(type?.Name ?? string.Empty, out var typeCode, out var typeMaterial);
            if (string.IsNullOrWhiteSpace(typeMaterial))
                typeMaterial = materialName;

            foreach (var face in GetVerticalPlanarFaces(wall))
            {
                var normal = new XYZ(face.FaceNormal.X, face.FaceNormal.Y, 0);
                if (normal.GetLength() < 1e-9)
                    continue;

                normal = normal.Normalize();
                if (normal.DotProduct(scope.Direction) < 0.70)
                    continue;

                var center = GetFaceCenter(face);
                if (!IsPointInsideXy(center, scope.Box))
                    continue;

                var key = BuildFaceKey(wall.Id, center, normal);
                if (!faceKeys.Add(key))
                    continue;

                var areaFt2 = GetExposedExteriorFaceAreaFt2(doc, wall, face);
                var areaM2 = areaFt2 * Ft2ToM2;
                if (areaM2 <= 0)
                    continue;

                rows.Add(new ExteriorWallReportRow
                {
                    WallId = wall.Id,
                    ElementId = wall.Id,
                    LevelId = wall.LevelId,
                    LevelName = level?.Name ?? "未指定",
                    Direction = scope.DirectionName,
                    CategoryName = wall.Category?.Name ?? "牆",
                    WallTypeId = type?.Id ?? ElementId.InvalidElementId,
                    WallTypeName = type?.Name ?? string.Empty,
                    ElementTypeId = type?.Id ?? ElementId.InvalidElementId,
                    ElementTypeName = type?.Name ?? string.Empty,
                    FinishCode = typeCode,
                    FinishMaterial = string.IsNullOrWhiteSpace(typeMaterial) ? materialName : typeMaterial,
                    NetExteriorAreaM2 = areaM2,
                    OpeningAreaM2 = 0,
                    WallLengthM = GetFaceLengthM(face),
                    WallHeightM = GetFaceHeightM(face),
                    Note = "半自動框選範圍；依指定立面方向計算牆外露面；開口已反映於幾何面積"
                });
            }

            return rows;
        }

        private static double GetFaceLengthM(PlanarFace face)
        {
            var pts = GetFacePoints(face).ToList();
            double max = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                for (int j = i + 1; j < pts.Count; j++)
                {
                    var a = pts[i];
                    var b = pts[j];
                    max = Math.Max(max, new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength());
                }
            }
            return max * 0.3048;
        }

        private static ExteriorWallReportRow BuildWallReportRow(Document doc, Wall wall)
        {
            var exteriorArea = GetExteriorFaceAreaM2(doc, wall);
            if (exteriorArea <= 0)
                return null;

            var level = doc.GetElement(wall.LevelId) as Level;
            var type = wall.WallType;
            var materialName = GetExteriorMaterialName(doc, type);
            SplitTypeLabel(type?.Name ?? string.Empty, out var typeCode, out var typeMaterial);
            if (string.IsNullOrWhiteSpace(typeMaterial))
                typeMaterial = materialName;

            var openingArea = EstimateHostedOpeningAreaM2(doc, wall);
            var orientation = GetExteriorDirection(wall);

            return new ExteriorWallReportRow
            {
                WallId = wall.Id,
                ElementId = wall.Id,
                LevelId = wall.LevelId,
                LevelName = level?.Name ?? "未分層",
                Direction = orientation,
                CategoryName = wall.Category?.Name ?? "牆",
                WallTypeId = type?.Id ?? ElementId.InvalidElementId,
                WallTypeName = type?.Name ?? string.Empty,
                ElementTypeId = type?.Id ?? ElementId.InvalidElementId,
                ElementTypeName = type?.Name ?? string.Empty,
                FinishCode = typeCode,
                FinishMaterial = string.IsNullOrWhiteSpace(typeMaterial) ? materialName : typeMaterial,
                NetExteriorAreaM2 = exteriorArea,
                OpeningAreaM2 = openingArea,
                WallLengthM = GetWallLengthM(wall),
                WallHeightM = GetWallHeightM(wall),
                Note = "外側面淨面積；開口扣除欄為門窗/洞口參考值"
            };
        }

        private static double GetExteriorFaceAreaM2(Document doc, Wall wall)
        {
            try
            {
                var refs = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
                if (refs == null || refs.Count == 0)
                    return 0;

                double areaFt2 = 0;
                foreach (var r in refs)
                {
                    if (!(wall.GetGeometryObjectFromReference(r) is Face face))
                        continue;

                    // 與「面生面→一般模型」同一數量邏輯：
                    // 將外側面形成薄片，扣除貼附/穿越的柱梁，再以體積 / 厚度回推外露粉刷面積。
                    // 若幾何布林失敗，才退回 Revit face.Area，避免整份報表中斷。
                    var exposedAreaFt2 = GetExposedExteriorFaceAreaFt2(doc, wall, face);
                    areaFt2 += exposedAreaFt2 > 0 ? exposedAreaFt2 : face.Area;
                }
                return areaFt2 * Ft2ToM2;
            }
            catch
            {
                return 0;
            }
        }

        private static double GetExposedExteriorFaceAreaFt2(Document doc, Wall wall, Face face)
        {
            try
            {
                if (!(face is PlanarFace pf))
                    return face.Area;

                var normal = pf.FaceNormal;
                if (normal == null || normal.GetLength() < 1e-9)
                    return face.Area;
                normal = normal.Normalize();

                var loops = face.GetEdgesAsCurveLoops()?.ToList();
                if (loops == null || loops.Count == 0)
                    return face.Area;

                const double finishThicknessFt = 20.0 / 304.8; // 20 mm 計算薄片，僅用於體積回推面積。
                var finishSolid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, normal, finishThicknessFt);
                if (finishSolid == null || finishSolid.Volume <= 1e-9)
                    return face.Area;

                var exposed = finishSolid;
                foreach (var blocker in CollectExteriorFaceBlockers(doc, wall, face))
                {
                    foreach (var blockerSolid in GetElementSolids(blocker))
                    {
                        if (blockerSolid == null || blockerSolid.Volume <= 1e-9)
                            continue;

                        try
                        {
                            var result = BooleanOperationsUtils.ExecuteBooleanOperation(
                                exposed, blockerSolid, BooleanOperationsType.Difference);
                            if (result != null && result.Volume > 1e-9)
                                exposed = result;
                        }
                        catch
                        {
                            // 個別元素扣除失敗時略過，避免單一異常幾何讓整份驗算失敗。
                        }
                    }
                }

                return exposed.Volume / finishThicknessFt;
            }
            catch
            {
                return face.Area;
            }
        }

        private static IEnumerable<Element> CollectExteriorFaceBlockers(Document doc, Wall wall, Face face)
        {
            var result = new List<Element>();
            try
            {
                var pts = GetFacePoints(face as PlanarFace).ToList();
                if (pts.Count == 0)
                    return result;

                var min = new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z));
                var max = new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z));
                const double expandFt = 300.0 / 304.8;
                var outline = new Autodesk.Revit.DB.Outline(
                    min - new XYZ(expandFt, expandFt, expandFt),
                    max + new XYZ(expandFt, expandFt, expandFt));
                var bbFilter = new BoundingBoxIntersectsFilter(outline);

                var categories = new[]
                {
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_Columns,
                    BuiltInCategory.OST_StructuralFraming
                };

                var seen = new HashSet<long>();
                foreach (var cat in categories)
                {
                    foreach (var element in new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .WherePasses(bbFilter)
                        .ToElements())
                    {
                        if (element.Id == wall.Id)
                            continue;

                        if (seen.Add(RevitCompat.GetElementIdValue(element.Id)))
                            result.Add(element);
                    }
                }
            }
            catch
            {
                // ignore and return what we have
            }

            return result;
        }

        private static List<Solid> GetElementSolids(Element element)
        {
            var solids = new List<Solid>();
            try
            {
                var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
                var geo = element?.get_Geometry(opt);
                if (geo == null)
                    return solids;

                solids.AddRange(EnumerateSolids(geo));
            }
            catch
            {
                // ignore invalid geometry
            }

            return solids;
        }

        private static double EstimateHostedOpeningAreaM2(Document doc, Wall wall)
        {
            try
            {
                double total = 0;
                var inserts = wall.FindInserts(true, true, true, true);
                foreach (var id in inserts)
                {
                    var e = doc.GetElement(id);
                    if (e == null)
                        continue;

                    var width = ReadLengthParam(e, BuiltInParameter.FAMILY_WIDTH_PARAM, "寬度", "Width");
                    var height = ReadLengthParam(e, BuiltInParameter.FAMILY_HEIGHT_PARAM, "高度", "Height");
                    if (width > 0 && height > 0)
                    {
                        total += width * height * Ft2ToM2;
                        continue;
                    }

                    var bb = e.get_BoundingBox(null);
                    if (bb != null)
                    {
                        var dx = Math.Abs(bb.Max.X - bb.Min.X);
                        var dz = Math.Abs(bb.Max.Z - bb.Min.Z);
                        if (dx > 0 && dz > 0)
                            total += dx * dz * Ft2ToM2;
                    }
                }
                return total;
            }
            catch
            {
                return 0;
            }
        }

        private static double ReadLengthParam(Element e, BuiltInParameter bip, params string[] fallbackNames)
        {
            var p = e.get_Parameter(bip);
            if (p != null && p.StorageType == StorageType.Double)
                return p.AsDouble();

            foreach (var name in fallbackNames)
            {
                p = e.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble();
            }
            return 0;
        }

        private static XYZ GetExteriorNormal(Wall wall)
        {
            try
            {
                var n = wall.Orientation;
                if (wall.Flipped)
                    n = -n;

                n = new XYZ(n.X, n.Y, 0);
                return n.GetLength() > 1e-9 ? n.Normalize() : XYZ.BasisY;
            }
            catch
            {
                return XYZ.BasisY;
            }
        }

        private static string GetExteriorDirection(Wall wall)
        {
            return DirectionNameFromNormal(GetExteriorNormal(wall));
        }

        private static string DirectionNameFromNormal(XYZ n)
        {
            if (Math.Abs(n.X) >= Math.Abs(n.Y))
                return n.X >= 0 ? "東向" : "西向";
            return n.Y >= 0 ? "北向" : "南向";
        }

        private static string GetExteriorMaterialName(Document doc, WallType wallType)
        {
            try
            {
                var cs = wallType?.GetCompoundStructure();
                var layers = cs?.GetLayers();
                if (layers == null || layers.Count == 0)
                    return string.Empty;

                // Exterior side is layer 0 for common compound walls; use first material as report default.
                var matId = layers[0].MaterialId;
                if (matId != ElementId.InvalidElementId && doc.GetElement(matId) is Material mat)
                    return mat.Name;
            }
            catch { }
            return string.Empty;
        }

        private static double GetWallLengthM(Wall wall)
        {
            if (wall.Location is LocationCurve lc)
                return lc.Curve.Length * 0.3048;
            return 0;
        }

        private static double GetWallHeightM(Wall wall)
        {
            var p = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)
                ?? wall.get_Parameter(BuiltInParameter.WALL_ATTR_HEIGHT_PARAM);
            return p != null && p.StorageType == StorageType.Double ? p.AsDouble() * 0.3048 : 0;
        }

        private static double GetLevelSortKey(Document doc, ElementId levelId)
        {
            return (doc.GetElement(levelId) as Level)?.Elevation ?? double.MaxValue;
        }

        private static void SplitTypeLabel(string text, out string code, out string material)
        {
            code = string.Empty;
            material = string.Empty;
            text = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            var separated = Regex.Match(text, @"^\s*([A-Za-z]+\d+[A-Za-z]?)\s*[-_：:]\s*(.+)$");
            if (separated.Success)
            {
                code = separated.Groups[1].Value.Trim();
                material = separated.Groups[2].Value.Trim();
                return;
            }

            var spaced = Regex.Match(text, @"^\s*([A-Za-z]+\d+[A-Za-z]?)\s+(.+)$");
            if (spaced.Success)
            {
                code = spaced.Groups[1].Value.Trim();
                material = spaced.Groups[2].Value.Trim();
                return;
            }

            code = text;
        }

        private static void ExportWorkbook(string filePath, IList<ExteriorWallReportRow> rows)
        {
            using (var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
                styles.Stylesheet = CreateStylesheet();
                styles.Stylesheet.Save();

                uint sheetId = 1;
                BuildDetailSheet(workbookPart, sheets, ref sheetId, rows);
                BuildSummarySheet(workbookPart, sheets, ref sheetId, rows);
                workbookPart.Workbook.Save();
            }
        }

        private static void BuildDetailSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, IList<ExteriorWallReportRow> rows)
        {
            var wsPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var worksheet = new Worksheet();
            worksheet.Append(new Columns(
                Col(1, 1, 12), Col(2, 2, 12), Col(3, 3, 14), Col(4, 4, 12),
                Col(5, 5, 28), Col(6, 6, 28), Col(7, 10, 14), Col(11, 11, 38)));
            worksheet.Append(sheetData);
            wsPart.Worksheet = worksheet;

            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(wsPart), SheetId = sheetId++, Name = "外牆粉刷明細" });

            uint rowIndex = 1;
            var title = new Row { RowIndex = rowIndex++ };
            title.Append(TextCell("外牆粉刷驗算明細表", 2));
            sheetData.Append(title);

            var info = new Row { RowIndex = rowIndex++ };
            info.Append(TextCell($"匯出時間：{DateTime.Now:yyyy/MM/dd HH:mm}    明細面數：{rows.Count}    外側面淨面積：{rows.Sum(r => r.NetExteriorAreaM2):F2} ㎡", 1));
            sheetData.Append(info);

            var header = new Row { RowIndex = rowIndex++ };
            foreach (var h in new[] { "樓層", "立面", "構件類別", "外牆代號", "外牆/粉刷材料", "構件類型", "長度(m)", "高度(m)", "外側面淨面積(㎡)", "開口參考扣除(㎡)", "備註" })
                header.Append(TextCell(h, 2));
            sheetData.Append(header);

            foreach (var r in rows)
            {
                var row = new Row { RowIndex = rowIndex++ };
                row.Append(TextCell(r.LevelName, 0));
                row.Append(TextCell(r.Direction, 0));
                row.Append(TextCell(r.CategoryName, 0));
                row.Append(TextCell(r.FinishCode, 0));
                row.Append(TextCell(r.FinishMaterial, 0));
                row.Append(TextCell(r.ElementTypeName, 0));
                row.Append(NumberCell(r.WallLengthM, 0));
                row.Append(NumberCell(r.WallHeightM, 0));
                row.Append(NumberCell(r.NetExteriorAreaM2, 0));
                row.Append(NumberCell(r.OpeningAreaM2, 0));
                row.Append(TextCell(r.Note, 0));
                sheetData.Append(row);
            }
        }

        private static void BuildSummarySheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, IList<ExteriorWallReportRow> rows)
        {
            var wsPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var worksheet = new Worksheet();
            worksheet.Append(new Columns(Col(1, 1, 14), Col(2, 2, 14), Col(3, 3, 14), Col(4, 4, 28), Col(5, 6, 16)));
            worksheet.Append(sheetData);
            wsPart.Worksheet = worksheet;

            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(wsPart), SheetId = sheetId++, Name = "外牆粉刷彙總" });

            uint rowIndex = 1;
            var title = new Row { RowIndex = rowIndex++ };
            title.Append(TextCell("外牆粉刷彙總表", 2));
            sheetData.Append(title);

            var header = new Row { RowIndex = rowIndex++ };
            foreach (var h in new[] { "樓層", "立面", "構件類別", "材料", "外側面淨面積(㎡)", "開口參考扣除(㎡)" })
                header.Append(TextCell(h, 2));
            sheetData.Append(header);

            var groups = rows
                .GroupBy(r => new { r.LevelName, r.Direction, r.CategoryName, r.FinishMaterial })
                .OrderBy(g => g.Key.LevelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.Direction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.FinishMaterial, StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups)
            {
                var row = new Row { RowIndex = rowIndex++ };
                row.Append(TextCell(g.Key.LevelName, 0));
                row.Append(TextCell(g.Key.Direction, 0));
                row.Append(TextCell(g.Key.CategoryName, 0));
                row.Append(TextCell(g.Key.FinishMaterial, 0));
                row.Append(NumberCell(g.Sum(x => x.NetExteriorAreaM2), 0));
                row.Append(NumberCell(g.Sum(x => x.OpeningAreaM2), 0));
                sheetData.Append(row);
            }
        }

        private static string CreateCheckView(Document doc, IList<ElementId> wallIds)
        {
            if (wallIds == null || wallIds.Count == 0)
                return null;

            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);
            if (vft == null)
                return null;

            var view = View3D.CreateIsometric(doc, vft.Id);
            view.Name = GetUniqueViewName(doc, $"AR_Check_外牆粉刷_{DateTime.Now:MMdd_HHmm}");

            var boxes = wallIds
                .Select(id => doc.GetElement(id)?.get_BoundingBox(null))
                .Where(bb => bb != null)
                .ToList();
            if (boxes.Count > 0)
            {
                var min = new XYZ(boxes.Min(b => b.Min.X), boxes.Min(b => b.Min.Y), boxes.Min(b => b.Min.Z));
                var max = new XYZ(boxes.Max(b => b.Max.X), boxes.Max(b => b.Max.Y), boxes.Max(b => b.Max.Z));
                var pad = 3.0;
                view.SetSectionBox(new BoundingBoxXYZ { Min = min - new XYZ(pad, pad, pad), Max = max + new XYZ(pad, pad, pad) });
            }

            try
            {
                view.IsolateElementsTemporary(wallIds);
            }
            catch { }

            return view.Name;
        }

        private static string GetUniqueViewName(Document doc, string baseName)
        {
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(baseName))
                return baseName;
            for (int i = 1; i < 1000; i++)
            {
                var name = $"{baseName}_{i}";
                if (!existing.Contains(name))
                    return name;
            }
            return $"{baseName}_{Guid.NewGuid():N}".Substring(0, Math.Min(60, baseName.Length + 33));
        }

        private static Stylesheet CreateStylesheet()
        {
            return new Stylesheet(
                new Fonts(new Font(), new Font(new Bold())),
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 }), new Fill(new PatternFill(new ForegroundColor { Rgb = "FFDCE6F1" }) { PatternType = PatternValues.Solid })),
                new Borders(new Border(), new Border(new LeftBorder { Style = BorderStyleValues.Thin }, new RightBorder { Style = BorderStyleValues.Thin }, new TopBorder { Style = BorderStyleValues.Thin }, new BottomBorder { Style = BorderStyleValues.Thin })),
                new CellFormats(
                    new CellFormat(),
                    new CellFormat { BorderId = 1, ApplyBorder = true },
                    new CellFormat { FontId = 1, FillId = 2, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true }));
        }

        private static Column Col(uint min, uint max, double width)
        {
            return new Column { Min = min, Max = max, Width = width, CustomWidth = true };
        }

        private static Cell TextCell(string value, uint style)
        {
            return new Cell
            {
                DataType = CellValues.InlineString,
                StyleIndex = style,
                InlineString = new InlineString(new Text(value ?? string.Empty))
            };
        }

        private static Cell NumberCell(double value, uint style)
        {
            return new Cell
            {
                DataType = CellValues.Number,
                StyleIndex = style,
                CellValue = new CellValue(Math.Round(value, 4).ToString(CultureInfo.InvariantCulture))
            };
        }

        private class ExteriorWallReportRow
        {
            public ElementId ElementId { get; set; }
            public ElementId WallId { get; set; }
            public ElementId LevelId { get; set; }
            public string LevelName { get; set; }
            public string Direction { get; set; }
            public string CategoryName { get; set; }
            public ElementId WallTypeId { get; set; }
            public string WallTypeName { get; set; }
            public ElementId ElementTypeId { get; set; }
            public string ElementTypeName { get; set; }
            public string FinishCode { get; set; }
            public string FinishMaterial { get; set; }
            public double NetExteriorAreaM2 { get; set; }
            public double OpeningAreaM2 { get; set; }
            public double WallLengthM { get; set; }
            public double WallHeightM { get; set; }
            public string Note { get; set; }
        }

        private class ExteriorWallContext
        {
            public ElementId WallId { get; set; }
            public XYZ Direction { get; set; }
            public string DirectionName { get; set; }
            public XYZ Center { get; set; }
            public BoundingBoxXYZ ExpandedBox { get; set; }
        }

        private class ExteriorWallSelectionScope
        {
            public BoundingBoxXYZ Box { get; set; }
            public XYZ Direction { get; set; }
            public string DirectionName { get; set; }
        }
    }
}
