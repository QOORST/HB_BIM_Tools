using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YDBIM.AutoDimension.Core;

namespace YDBIM.AutoDimension.App
{
    [Transaction(TransactionMode.Manual)]
    public sealed class DimensionTextCleanupCommand : IExternalCommand
    {
        private sealed class Filter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Dimension;
            public bool AllowReference(Reference r, XYZ p) => false;
        }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null) return Result.Cancelled;
            var doc = ui.Document;
            try
            {
                var dims = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Dimension>().ToList();
                if (dims.Count == 0)
                    dims = ui.Selection.PickObjects(ObjectType.Element, new Filter(), "選取要整理的連續尺寸，完成後按完成。")
                        .Select(r => doc.GetElement(r)).OfType<Dimension>().ToList();
                using (var form = new YDBIM.AutoDimension.UI.DimensionTextCleanupForm())
                {
                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;
                    int moved = 0, skipped = 0, unresolved = 0;
                    var previewBoxes = new List<DimensionTextPreview.Box>();
                    using (var group = new TransactionGroup(doc, "尺寸文字整理"))
                    {
                        group.Start();
                        using (var tx = new Transaction(doc, "預覽尺寸文字排列"))
                        {
                            tx.Start();
                            foreach (var dim in dims)
                            {
                                if (dim.OwnerViewId != doc.ActiveView.Id || !(dim.Curve is Line line) || dim.NumberOfSegments < 2)
                                { skipped++; continue; }
                                using (var sub = new SubTransaction(doc))
                                {
                                    sub.Start();
                                    try
                                    {
                                        var along = line.Direction.Normalize();
                                        var across = doc.ActiveView.ViewDirection.CrossProduct(along).Normalize();
                                        double factor = 304.8 / doc.ActiveView.Scale;
                                        var type = doc.GetElement(dim.GetTypeId());
                                        double height = (type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0) * 304.8;
                                        if (height <= 0) throw new InvalidOperationException("無法取得文字高度");
                                        double widthScale = type.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE)?.AsDouble() ?? 1;
                                        if (widthScale <= 0) widthScale = 1;
                                        var segments = dim.Segments.Cast<DimensionSegment>().ToList();
                                        var boxes = new List<DimensionTextBox>();
                                        var originals = new List<XYZ>();
                                        for (int i = 0; i < segments.Count; i++)
                                        {
                                            var s = segments[i];
                                            if (!s.IsTextPositionAdjustable()) throw new InvalidOperationException("尺寸含不可調整段落");
                                            var pos = s.TextPosition;
                                            if (pos == null) throw new InvalidOperationException("尺寸無文字位置");
                                            originals.Add(pos);
                                            string text = (s.Prefix ?? "") + (string.IsNullOrEmpty(s.ValueOverride) ? s.ValueString : s.ValueOverride) + (s.Suffix ?? "");
                                            int length = Math.Max(text.Length, Math.Max((s.Above ?? "").Length, (s.Below ?? "").Length));
                                            // Conservative paper-space estimate; Revit exposes no segment text bounding box.
                                            boxes.Add(new DimensionTextBox { Index = i, Along = pos.DotProduct(along) * factor,
                                                Across = pos.DotProduct(across) * factor, Width = Math.Max(height, length * height * widthScale),
                                                Height = height * (1 + (string.IsNullOrEmpty(s.Above) ? 0 : 1) + (string.IsNullOrEmpty(s.Below) ? 0 : 1)) * 1.3,
                                                Movable = true });
                                        }
                                        int conflicts;
                                        var plan = DimensionTextLayout.Plan(boxes, form.Gap, form.Offset, out conflicts);
                                        int count = 0;
                                        var chainPreview = new List<DimensionTextPreview.Box>();
                                        foreach (var box in boxes)
                                        {
                                            double delta = (plan[box.Index] - box.Across) / factor;
                                            chainPreview.Add(new DimensionTextPreview.Box { Center = originals[box.Index] + across * delta,
                                                Along = along, Across = across, Width = box.Width / factor, Height = box.Height / factor });
                                            if (Math.Abs(delta) < 1e-9) continue;
                                            segments[box.Index].TextPosition = originals[box.Index] + across * delta;
                                            count++;
                                        }
                                        if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("尺寸更新未提交");
                                        moved += count; unresolved += conflicts;
                                        previewBoxes.AddRange(chainPreview);
                                    }
                                    catch { if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack(); skipped++; }
                                }
                            }
                            if (tx.Commit() != TransactionStatus.Committed) { group.RollBack(); return Result.Cancelled; }
                        }
                        // Compare all successfully processed selected chains in a common view coordinate system.
                        var bounds = previewBoxes.Select((b,i) => {
                            var corners=b.Corners;
                            var x=corners.Select(p=>p.DotProduct(doc.ActiveView.RightDirection)*304.8/doc.ActiveView.Scale).ToList();
                            var y=corners.Select(p=>p.DotProduct(doc.ActiveView.UpDirection)*304.8/doc.ActiveView.Scale).ToList();
                            return new DimensionTextBox { Index=i, Along=(x.Min()+x.Max())/2, Across=(y.Min()+y.Max())/2,
                                Width=x.Max()-x.Min(), Height=y.Max()-y.Min(), Movable=false };
                        }).ToList();
                        var conflictIds=DimensionTextLayout.FindConflicts(bounds,form.Gap);
                        foreach(var index in conflictIds) previewBoxes[index].Conflict=true;
                        unresolved=conflictIds.Count;
                        TaskDialogResult result;
                        using(var preview=new DimensionTextPreview(doc.ActiveView,previewBoxes))
                        {
                            string previewStatus;
                            try {
                                preview.Start(); ui.RefreshActiveView();
                                previewStatus=preview.RenderError != null ? "色框預覽失敗："+preview.RenderError :
                                    preview.Rendered ? "綠框：估算文字範圍；紅框：仍可能擁擠。" : "目前視圖未回報色框繪製，請以實際文字位置檢查。";
                            }
                            catch(Exception ex) { previewStatus="色框預覽無法啟動："+ex.Message; }
                            result = TaskDialog.Show("尺寸文字整理｜結果預覽",
                                $"已移動 {moved} 段文字；略過 {skipped} 條尺寸；仍可能擁擠 {unresolved} 段。\n\n{previewStatus}\n已檢查本次成功處理的尺寸鏈之間的文字；範圍為估算值，未檢查未選尺寸或模型線條。跨鏈衝突僅提示，不自動搬移。\n\n選擇「否」還原全部變更；保留後可用 Revit 復原。",
                                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.No);
                        }
                        if (result == TaskDialogResult.Yes) group.Assimilate(); else group.RollBack();
                        ui.RefreshActiveView();
                        return result == TaskDialogResult.Yes ? Result.Succeeded : Result.Cancelled;
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
