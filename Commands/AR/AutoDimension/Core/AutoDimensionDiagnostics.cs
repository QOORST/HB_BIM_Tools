using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
namespace YDBIM.AutoDimension.Core
{
    public static class AutoDimensionDiagnostics
    {
        public static string DiagnoseGrids(Document doc)
        {
            if (doc == null || doc.IsModifiable) throw new InvalidOperationException("文件無效或已有進行中的交易。");
            var view = doc.ActiveView;
            if (!(view is ViewPlan) || view.IsTemplate) throw new InvalidOperationException("請切換至非樣板平面視圖。");
            var source = VisibleGridCollector.Collect(doc, view);
            var saved = AutoDimensionSettingsStore.Load(doc, DimensionMode.BeamGrid);
            var messages = new List<string>();
            var options = new DimensionOptions {
                ModeType = DimensionMode.BeamGrid, Direction = saved?.Direction ?? PlacementDirection.NorthWest,
                DimensionTypeName = saved?.DimensionTypeName, GridOffsetsInPaperSpace = saved?.GridOffsetsInPaperSpace ?? true,
                GridPrimaryOffsetInternal = UnitUtils.ConvertToInternalUnits(saved?.GridPrimaryOffsetMm ?? 20, UnitTypeId.Millimeters),
                GridOverallOffsetInternal = UnitUtils.ConvertToInternalUnits(saved?.GridOverallOffsetMm ?? 10, UnitTypeId.Millimeters),
                SelectedHorizontalGridIds = source.Horizontal.Select(g => g.Id).ToList(),
                SelectedVerticalGridIds = source.Vertical.Select(g => g.Id).ToList(), Diagnostics = messages
            };
            int Count() => new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)).GetElementCount();
            int before = Count(), created = 0;
            string error = null;
            TransactionStatus rollback;
            using (var tx = new Transaction(doc, "HB_BIM 軸線診斷（自動回復）")) {
                tx.Start();
                try { created = new AutoDimensionService().CreateDimensions(doc, view, options); }
                catch (Exception ex) { error = ex.ToString(); }
                finally { rollback = tx.RollBack(); }
            }
            int after = Count();
            if (rollback != TransactionStatus.RolledBack || before != after) throw new InvalidOperationException("診斷回復驗證失敗。");
            return JsonConvert.SerializeObject(new {
                ViewId = ElementIdCompat.ToInt32(view.Id), ViewName = view.Name, ViewType = view.ViewType.ToString(), view.Scale,
                Scope = "All visible straight grids; saved offsets/direction or default NW 20/10 paper mm",
                Horizontal = source.Horizontal.Count, Vertical = source.Vertical.Count,
                Direction = options.Direction.ToString(), PrimaryOffsetInternal = options.GridPrimaryOffsetInternal,
                OverallOffsetInternal = options.GridOverallOffsetInternal, options.GridOffsetsInPaperSpace,
                CreatedDuringProbe = created, DimensionsBefore = before, DimensionsAfter = after,
                RolledBack = true, Error = error, Messages = messages
            }, Formatting.Indented);
        }
    }
}
