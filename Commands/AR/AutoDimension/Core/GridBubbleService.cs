using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YDBIM.AutoDimension.Core
{
    internal static class GridBubbleService
    {
        internal static string Apply(Document doc, View view, DimensionOptions options)
        {
            if (!(view is ViewPlan) || view.IsTemplate)
                throw new InvalidOperationException("軸線標頭目前僅支援平面視圖。");
            int changed = 0;
            var errors = new List<string>();
            foreach (var id in options.SelectedHorizontalGridIds.Concat(options.SelectedVerticalGridIds).Distinct())
            {
                using (var tx = new SubTransaction(doc))
                {
                    tx.Start();
                    try
                    {
                        var grid = doc.GetElement(id) as Grid;
                        if (grid == null || !(grid.Curve is Line)) throw new InvalidOperationException("非可處理直線軸線");
                        if (!grid.CanBeVisibleInView(view)) throw new InvalidOperationException("軸線無法顯示於此視圖");
                        XYZ axis = grid.Curve.GetEndPoint(1) - grid.Curve.GetEndPoint(0);
                        bool horizontal = Math.Abs(axis.DotProduct(view.RightDirection)) >= Math.Abs(axis.DotProduct(view.UpDirection));
                        bool positiveVisible = horizontal ? options.HorizontalBubbleRight : options.VerticalBubbleTop;
                        bool negativeVisible = horizontal ? options.HorizontalBubbleLeft : options.VerticalBubbleBottom;
                        // Equal settings do not require endpoint mapping (including hide both).
                        bool positiveEnd1 = true;
                        if (positiveVisible != negativeVisible)
                        {
                            XYZ delta = ReadDatumEndPosition(doc, grid, view, DatumEnds.End1)
                                - ReadDatumEndPosition(doc, grid, view, DatumEnds.End0);
                            double projected = delta.DotProduct(horizontal ? view.RightDirection : view.UpDirection);
                            if (Math.Abs(projected) < 1e-6)
                                throw new InvalidOperationException("無法辨識標頭端點方向，未修改此軸線。");
                            positiveEnd1 = projected > 0;
                        }
                        Set(grid, view, DatumEnds.End0, positiveEnd1 ? negativeVisible : positiveVisible);
                        Set(grid, view, DatumEnds.End1, positiveEnd1 ? positiveVisible : negativeVisible);
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("未提交");
                        changed++;
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        errors.Add(id + "：" + ex.Message);
                    }
                }
            }
            return $"標頭設定完成：{changed} 條；未完成：{errors.Count} 條。" +
                (errors.Count == 0 ? "" : "\n" + string.Join("\n", errors.Take(8)));
        }
        private static XYZ ReadDatumEndPosition(Document doc, Grid grid, View view, DatumEnds end)
        {
            // Leader.End is on the datum curve and is explicitly associated with DatumEnds.
            // Never infer this association from the order of Grid.Curve endpoints.
            using (var probe = new SubTransaction(doc))
            {
                probe.Start();
                try
                {
                    grid.ShowBubbleInView(end, view);
                    doc.Regenerate();
                    var leader = grid.GetLeader(end, view) ?? grid.AddLeader(end, view);
                    doc.Regenerate();
                    XYZ point = leader.End;
                    return new XYZ(point.X, point.Y, point.Z);
                }
                finally
                {
                    if (probe.GetStatus() == TransactionStatus.Started) probe.RollBack();
                }
            }
        }

        private static void Set(Grid grid, View view, DatumEnds end, bool visible)
        {
            if (visible) grid.ShowBubbleInView(end, view); else grid.HideBubbleInView(end, view);
            if (grid.IsBubbleVisibleInView(end, view) != visible) throw new InvalidOperationException("標頭顯示驗證失敗");
        }
    }
}
