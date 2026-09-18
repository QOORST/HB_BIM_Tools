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
                        var curves = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                        var curve = curves.FirstOrDefault() ?? grid.Curve;
                        XYZ delta = curve.GetEndPoint(1) - curve.GetEndPoint(0);
                        // End0/End1 follow the model datum, not arbitrary curve enumeration order.
                        if (delta.DotProduct(grid.Curve.GetEndPoint(1) - grid.Curve.GetEndPoint(0)) < 0) delta = delta.Negate();
                        double x = delta.DotProduct(view.RightDirection), y = delta.DotProduct(view.UpDirection);
                        bool end1 = Math.Abs(x) >= Math.Abs(y) ? (x > 0) == options.HorizontalBubbleRight : (y > 0) == options.VerticalBubbleTop;
                        Set(grid, view, DatumEnds.End0, options.GridBubblesBothEnds || !end1);
                        Set(grid, view, DatumEnds.End1, options.GridBubblesBothEnds || end1);
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
        private static void Set(Grid grid, View view, DatumEnds end, bool visible)
        {
            if (visible) grid.ShowBubbleInView(end, view); else grid.HideBubbleInView(end, view);
            if (grid.IsBubbleVisibleInView(end, view) != visible) throw new InvalidOperationException("標頭顯示驗證失敗");
        }
    }
}
