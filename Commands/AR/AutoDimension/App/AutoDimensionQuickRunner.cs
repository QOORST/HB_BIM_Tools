using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YDBIM.AutoDimension.Core;
using YDBIM.AutoDimension.UI;
using Forms = System.Windows.Forms;

#nullable enable

namespace YDBIM.AutoDimension.App
{
internal static class AutoDimensionQuickRunner
{
    internal static DimensionMode LastMode { get; private set; } = DimensionMode.ColumnSetout;

    internal static Result Run(ExternalCommandData data, ref string message, DimensionMode? requestedMode = null, bool linkedSource = false)
    {
        var ui = data.Application.ActiveUIDocument;
        if (ui == null) return Result.Cancelled;
        var doc = ui.Document;
        var view = doc.ActiveView;
        if (view is not ViewPlan || view.IsTemplate)
        {
            TaskDialog.Show("自動標註", "請切換至平面視圖後選取柱、梁或軸線。");
            return Result.Cancelled;
        }
        try
        {
            RevitLinkInstance? sourceLink = null;
            if (linkedSource)
            {
                if (!LinkedDimensionSourceChooser.TryChoose(data.Application, requestedMode, out sourceLink, out var mode))
                    return Result.Cancelled;
                if (sourceLink == null) throw new InvalidOperationException("尚未選取有效的連結來源。");
                requestedMode = mode;
            }
            if (requestedMode.HasValue) LastMode = requestedMode.Value;
            bool wholeView = DimensionQuickPolicy.UsesWholeView(requestedMode);
            var sourceDoc = sourceLink == null ? doc : sourceLink.GetLinkDocument()
                ?? throw new InvalidOperationException("所選連結模型已卸載，請重新選取來源。");
            var visible = sourceLink == null
                ? new HashSet<ElementId>(new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElementIds())
                : LinkedDimensionSourceChooser.CollectVisible(doc, view, sourceLink);
            var filter = new TargetFilter(visible, requestedMode);
            var selected = sourceLink != null && !wholeView
                ? ui.Selection.PickObjects(ObjectType.LinkedElement,
                    new LinkedTargetFilter(sourceLink, filter), "點選所選連結模型中的可見梁；完成後按完成，Esc 取消。")
                    .Select(r => sourceDoc.GetElement(r.LinkedElementId)).Where(filter.AllowElement).GroupBy(e => e.Id).Select(g => g.First()).ToList()
                : (wholeView ? visible : ui.Selection.GetElementIds()).Select(sourceDoc.GetElement).Where(filter.AllowElement).ToList();
            if (sourceLink == null && !wholeView && selected.Count == 0)
                selected = ui.Selection.PickElementsByRectangle(filter, "框選要標註的柱、梁或軸線；Esc 取消。").ToList();
            if (selected.Count == 0)
            {
                if (wholeView) TaskDialog.Show("自動標註", "目前視圖沒有可處理的" + Name(requestedMode!.Value) + "。");
                return Result.Cancelled;
            }

            var types = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear).Select(t => t.Name).Distinct().OrderBy(n => n).ToList();
            if (types.Count == 0) throw new InvalidOperationException("專案沒有可用的線性尺寸類型。");
            var grids = sourceLink == null ? VisibleGridCollector.Collect(doc, view)
                : LinkedDimensionSourceChooser.CollectGrids(sourceLink, view, visible);
            var saved = linkedSource ? LinkedDimensionSourceChooser.LoadFormats(doc) : AutoDimensionSettingsStore.LoadAll(doc);
            var requests = new List<DimensionOptions>();
            var warnings = new List<string>();
            foreach (var group in selected.GroupBy(e => ModeOf(e)!.Value).OrderBy(g => g.Key))
            {
                var mode = group.Key;
                var ids = group.Select(e => e.Id).ToList();
                var horizontal = grids.Horizontal.Where(g => ids.Contains(g.Id)).ToList();
                var vertical = grids.Vertical.Where(g => ids.Contains(g.Id)).ToList();
                if (mode == DimensionMode.BeamGrid && horizontal.Count < 2 && vertical.Count < 2)
                { warnings.Add("軸線：選取範圍內缺少同方向的兩條可見直線軸線，已略過。"); continue; }

                DimensionOptions options;
                if (saved.TryGetValue(mode, out var remembered) && remembered.CanReuse(mode, types))
                    options = remembered.ToOptions();
                else
                {
                    // Ask only for the selected category; no transaction is open while collecting input.
                    var settings = new Dictionary<DimensionMode, AutoDimensionSavedSettings>();
                    if (remembered != null) settings[mode] = linkedSource
                        ? AutoDimensionSavedSettings.FromOptions(remembered.ToOptions()) : remembered;
                    else
                    {
                        var shared = saved.Values.FirstOrDefault(s => s.CanReuse(s.ModeType, types));
                        if (shared != null) settings[mode] = new AutoDimensionSavedSettings {
                            ModeType = mode, DimensionTypeName = shared.DimensionTypeName, OffsetMm = shared.OffsetMm
                        };
                    }
                    using var form = new DimensionOptionsForm(types, horizontal, vertical, mode, true,
                        "HB_BIM 自動標註｜" + Name(mode), settings, viewScale: view.Scale);
                    form.ConfigureQuickScope(ids.Count, wholeView);
                    DimensionOptions? validated = null;
                    form.FormClosing += (_, e) => {
                        if (form.DialogResult != Forms.DialogResult.OK) return;
                        if (!form.TryGetOptions(out var candidate, out string error))
                        { e.Cancel = true; Forms.MessageBox.Show(form, error, "自動標註", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning); }
                        else validated = candidate;
                    };
                    if (form.ShowDialog(new Window(data.Application.MainWindowHandle)) != Forms.DialogResult.OK || validated == null)
                        return Result.Cancelled;
                    options = validated;
                }
                options.SourceLinkInstanceId = sourceLink?.Id;
                options.SelectedBeamIds = Array.Empty<ElementId>();
                options.SelectedColumnIds = Array.Empty<ElementId>();
                if (mode != DimensionMode.BeamGrid)
                {
                    options.SelectedHorizontalGridIds = Array.Empty<ElementId>();
                    options.SelectedVerticalGridIds = Array.Empty<ElementId>();
                }
                if (mode == DimensionMode.BeamWidth) { options.SelectedBeamsOnly = true; options.SelectedBeamIds = ids; }
                if (mode == DimensionMode.ColumnSetout) options.SelectedColumnIds = ids;
                if (mode == DimensionMode.BeamGrid)
                {
                    // Reuse distances and style, not the previous view's grid IDs.
                    if (wholeView || (remembered != null && remembered.CanReuse(mode, types)))
                    {
                        options.SelectedHorizontalGridIds = horizontal.Select(g => g.Id).ToList();
                        options.SelectedVerticalGridIds = vertical.Select(g => g.Id).ToList();
                    }
                }
                if (mode == DimensionMode.ColumnSetout && options.Mode == PlacementMode.Manual)
                {
                    XYZ delta = ui.Selection.PickPoint("請點選柱標註放置側。") - view.Origin;
                    if (options.ColumnLeftRightSide != LeftRightSide.None)
                        options.ColumnLeftRightSide = delta.DotProduct(view.RightDirection) >= 0 ? LeftRightSide.Right : LeftRightSide.Left;
                    if (options.ColumnFrontBackSide != FrontBackSide.None)
                        options.ColumnFrontBackSide = delta.DotProduct(view.UpDirection) >= 0 ? FrontBackSide.Front : FrontBackSide.Back;
                }
                requests.Add(options);
            }

            int total = 0;
            using (var tx = new Transaction(doc, wholeView ? "自動標註－補齊目前視圖" : "自動標註－目前選取"))
            {
                if (requests.Count > 0)
                {
                    tx.Start();
                    var service = new AutoDimensionService();
                    foreach (var options in requests)
                    {
                        int created = service.CreateDimensions(doc, view, options);
                        warnings.AddRange(options.Warnings);
                        if (DimensionQuickPolicy.HasUnresolved(created, options.RequestedDimensionCount, options.ExistingDimensionCount))
                            warnings.Add(Name(options.ModeType) + "：部分構件未建立新尺寸，請檢查有效參考或既有標註。");
                        total += created;
                    }
                    if (total == 0) tx.RollBack();
                    else if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("標註未成功提交。");
                }
            }
            foreach (var options in requests)
                try
                {
                    if (linkedSource) LinkedDimensionSourceChooser.RememberFormat(doc, options);
                    else AutoDimensionSettingsStore.Save(doc, options);
                }
                catch (Exception ex) { warnings.Add("設定未能儲存：" + ex.Message); break; }
            if (warnings.Count > 0) TaskDialog.Show("自動標註", $"建立 {total} 條尺寸。\n" + string.Join("\n", warnings));
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        catch (Exception ex) { message = ex.Message; TaskDialog.Show("自動標註", ex.Message); return Result.Failed; }
    }

    private static string Name(DimensionMode mode) => mode == DimensionMode.BeamWidth ? "梁" : mode == DimensionMode.ColumnSetout ? "柱" : "軸線";
    private static DimensionMode? ModeOf(Element? element)
    {
        if (element is Grid) return DimensionMode.BeamGrid;
        if (element is not FamilyInstance || element.Category == null) return null;
        int category = ElementIdCompat.ToInt32(element.Category.Id);
        if (category == (int)BuiltInCategory.OST_StructuralFraming) return DimensionMode.BeamWidth;
        if (category == (int)BuiltInCategory.OST_StructuralColumns || category == (int)BuiltInCategory.OST_Columns) return DimensionMode.ColumnSetout;
        return null;
    }
    private sealed class TargetFilter : ISelectionFilter
    {
        private readonly HashSet<ElementId> _visible;
        private readonly DimensionMode? _mode;
        internal TargetFilter(HashSet<ElementId> visible, DimensionMode? mode) { _visible = visible; _mode = mode; }
        public bool AllowElement(Element e) => e != null && _visible.Contains(e.Id) && ModeOf(e).HasValue && (!_mode.HasValue || ModeOf(e) == _mode);
        public bool AllowReference(Reference reference, XYZ point) => false;
    }
    private sealed class LinkedTargetFilter : ISelectionFilter
    {
        private readonly RevitLinkInstance _link;
        private readonly TargetFilter _targets;
        internal LinkedTargetFilter(RevitLinkInstance link, TargetFilter targets) { _link = link; _targets = targets; }
        public bool AllowElement(Element e) => e is RevitLinkInstance && e.Id.Equals(_link.Id);
        public bool AllowReference(Reference reference, XYZ point) => reference.ElementId.Equals(_link.Id)
            && _targets.AllowElement(_link.GetLinkDocument()?.GetElement(reference.LinkedElementId)!);
    }
    private sealed class Window : Forms.IWin32Window
    {
        internal Window(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }
}
}
