using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdSleeveGlRebase : IExternalCommand
    {
        private const double Tolerance = 1e-6;
        private sealed class Filter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is FamilyInstance f && f.Symbol?.FamilyName == "套管-圓形_無";
            public bool AllowReference(Reference r, XYZ p) => false;
        }
        private sealed class PortState
        {
            internal Connector Port;
            internal XYZ Origin;
            internal XYZ Direction;
            internal string Peers;
        }
        private static string Peers(Connector c) => string.Join("|", c.AllRefs.Cast<Connector>()
            .Where(p => p.Owner.Id != c.Owner.Id && p.ConnectorType == ConnectorType.End)
            .Select(p => p.Owner.UniqueId + ":" + p.Id).OrderBy(x => x));
        private static Parameter LevelParameter(FamilyInstance f) => new[] {
            BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM }
            .Select(f.get_Parameter).FirstOrDefault(p => p != null && p.StorageType == StorageType.ElementId && !p.IsReadOnly);
        private static Parameter ElevationParameter(FamilyInstance f) => new[] { "立面高程", "Elevation" }
            .Select(f.LookupParameter).FirstOrDefault(p => p != null && p.StorageType == StorageType.Double && !p.IsReadOnly);
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            var doc = ui.Document;
            try
            {
                if (!LicenseManager.Instance.HasFeatureAccess("MEP.PipeSleeve")) throw new InvalidOperationException("授權未包含套管功能。");
                Level gl = SleeveGlLevel.Require(doc);
                var filter = new Filter();
                var selection = ui.Selection.GetElementIds().Select(doc.GetElement).ToList();
                if (selection.Count == 0) selection = ui.Selection.PickObjects(ObjectType.Element, filter, "選取套管-圓形_無，可無來源管線；Esc 取消").Select(doc.GetElement).ToList();
                if (selection.Any(e => !filter.AllowElement(e))) throw new InvalidOperationException("本次只支援套管-圓形_無，請排除其他元素。");
                var sleeves = selection.Cast<FamilyInstance>().ToList();
                if (sleeves.Count == 0) return Result.Cancelled;
                foreach (var f in sleeves)
                {
                    if (f.Pinned || f.GroupId != ElementId.InvalidElementId || !(f.Location is LocationPoint) ||
                        LevelParameter(f) == null || ElevationParameter(f) == null)
                        throw new InvalidOperationException($"套管 {f.Id} 已釘住、位於群組或缺少可寫入樓層／立面高程，未進行修改。");
                    var ports = CmdRaftCadSleeve.Ports(f);
                    if (ports.Count != 2 || ports.Any(p => p.Shape != ConnectorProfileType.Round))
                        throw new InvalidOperationException($"套管 {f.Id} 無法確認兩端圓形接點，未進行修改。");
                }
                var originals = sleeves.Select(f => new {
                    Sleeve = f, Point = ((LocationPoint)f.Location).Point, Rotation = ((LocationPoint)f.Location).Rotation,
                    Center = CmdRaftCadSleeve.Ports(f).Select(p => p.Origin).Aggregate((a,b) => a+b) * .5,
                    Type = f.GetTypeId(), Bounds = f.get_BoundingBox(null),
                    Dependents = string.Join("|", f.GetDependentElements(null).Select(id => id.ToString()).OrderBy(x => x))
                }).ToList();
                // Include directly connected neighbours: changing a level can propagate a move.
                var portStates = sleeves.SelectMany(CmdRaftCadSleeve.Ports)
                    .SelectMany(c => new[] { c }.Concat(c.AllRefs.Cast<Connector>().Where(p => p.ConnectorType == ConnectorType.End)))
                    .Select(c => new PortState { Port=c, Origin=c.Origin, Direction=c.CoordinateSystem.BasisZ, Peers=Peers(c) }).ToList();
                var preview = new TaskDialog("套管 GL 歸位") {
                    MainInstruction = $"將 {sleeves.Count} 支套管歸位至 {gl.Name}？",
                    MainContent = "保留原實例與位置；同步約束樓層及立面高程，不修改 TOP／BOP。任一驗證失敗即整批回復。",
                    ExpandedContent = string.Join("\n", originals.Select(x => $"{x.Sleeve.Id}：{SleeveGlLevel.Actual(doc,x.Sleeve)?.Name ?? "未確認"} → {gl.Name}，立面高程 {(x.Center.Z-gl.ProjectElevation)*304.8:0.##} mm")),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel, DefaultButton=TaskDialogResult.Cancel
                };
                if (preview.Show() != TaskDialogResult.Ok) return Result.Cancelled;
                using (var tx = new Transaction(doc, "套管 GL 歸位（保留實例）"))
                {
                    tx.Start();
                    foreach (var x in originals)
                    {
                        if (!LevelParameter(x.Sleeve).Set(gl.Id)) throw new InvalidOperationException("樓層寫入失敗。");
                        doc.Regenerate();
                        XYZ delta = x.Point - ((LocationPoint)x.Sleeve.Location).Point;
                        if (delta.GetLength() > Tolerance) ElementTransformUtils.MoveElement(doc, x.Sleeve.Id, delta);
                        if (!ElevationParameter(x.Sleeve).Set(x.Center.Z - gl.ProjectElevation)) throw new InvalidOperationException("立面高程寫入失敗。");
                        foreach (string name in new[] { "所屬樓層", "套管樓層", "樓層名稱", "Level Name", "Reference Level Name", "參考樓層名稱", "所屬樓層名稱" })
                        {
                            var p = x.Sleeve.LookupParameter(name);
                            if (p == null) continue;
                            if (p.StorageType == StorageType.String && p.AsString() != gl.Name && (p.IsReadOnly || !p.Set(gl.Name)))
                                throw new InvalidOperationException($"{name} 無法同步，已取消。");
                        }
                    }
                    doc.Regenerate();
                    foreach (var x in originals)
                    {
                        var location = (LocationPoint)x.Sleeve.Location;
                        var bounds = x.Sleeve.get_BoundingBox(null);
                        if (SleeveGlLevel.Actual(doc,x.Sleeve)?.Id != gl.Id || x.Sleeve.GetTypeId() != x.Type ||
                            location.Point.DistanceTo(x.Point) > Tolerance || Math.Abs(location.Rotation-x.Rotation) > Tolerance ||
                            Math.Abs(ElevationParameter(x.Sleeve).AsDouble() - (x.Center.Z-gl.ProjectElevation)) > Tolerance ||
                            (x.Bounds != null && (bounds == null || bounds.Min.DistanceTo(x.Bounds.Min)>Tolerance || bounds.Max.DistanceTo(x.Bounds.Max)>Tolerance)) ||
                            x.Dependents != string.Join("|", x.Sleeve.GetDependentElements(null).Select(id=>id.ToString()).OrderBy(v=>v)))
                            throw new InvalidOperationException($"套管 {x.Sleeve.Id} 的位置、尺寸、樓層或相依元素驗證失敗，已整批回復。");
                    }
                    foreach (var p in portStates)
                        if (p.Port.Origin.DistanceTo(p.Origin)>Tolerance || p.Port.CoordinateSystem.BasisZ.DistanceTo(p.Direction)>Tolerance || Peers(p.Port)!=p.Peers)
                            throw new InvalidOperationException("接點位置、方向或連接關係改變，已整批回復。");
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 未提交歸位結果。");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { TaskDialog.Show("套管 GL 歸位", ex.Message); return Result.Failed; }
        }
    }
}
