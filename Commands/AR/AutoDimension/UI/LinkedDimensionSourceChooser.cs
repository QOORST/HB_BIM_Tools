using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YDBIM.AutoDimension.Core;
using Forms = System.Windows.Forms;

#nullable enable

namespace YDBIM.AutoDimension.UI
{
internal static class LinkedDimensionSourceChooser
{
    private sealed class Session
    {
        internal string? LinkUniqueId;
        internal DimensionMode Mode = DimensionMode.ColumnSetout;
        internal readonly Dictionary<DimensionMode, AutoDimensionSavedSettings> Formats = new Dictionary<DimensionMode, AutoDimensionSavedSettings>();
    }

    private static readonly ConditionalWeakTable<Document, Session> Sessions = new ConditionalWeakTable<Document, Session>();

    internal static IReadOnlyDictionary<DimensionMode, AutoDimensionSavedSettings> LoadFormats(Document doc)
    {
        var result = AutoDimensionSettingsStore.LoadAll(doc).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in Sessions.GetValue(doc, _ => new Session()).Formats) result[pair.Key] = pair.Value;
        return result;
    }

    internal static void RememberFormat(Document doc, DimensionOptions options)
    {
        var format = AutoDimensionSavedSettings.FromOptions(options);
        format.SelectedHorizontalGridIds.Clear();
        format.SelectedVerticalGridIds.Clear();
        Sessions.GetValue(doc, _ => new Session()).Formats[options.ModeType] = format;
    }

    internal static bool TryChoose(UIApplication app, DimensionMode? requestedMode,
        out RevitLinkInstance? link, out DimensionMode mode)
    {
        link = null;
        mode = requestedMode ?? DimensionMode.ColumnSetout;
#if REVIT2024 || REVIT2025 || REVIT2026
        var doc = app.ActiveUIDocument.Document;
        var view = doc.ActiveView;
        var session = Sessions.GetValue(doc, _ => new Session());
        var links = new FilteredElementCollector(doc, view.Id).OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>().Where(item => item.GetLinkDocument() != null && !item.IsHidden(view))
            .OrderBy(item => item.Name).ThenBy(item => item.UniqueId).ToList();
        int remembered = session.LinkUniqueId == null ? -1 : links.FindIndex(item => item.UniqueId == session.LinkUniqueId);
        bool invalid = session.LinkUniqueId != null && remembered < 0;
        if (invalid)
            TaskDialog.Show("連結模型標註", "上次選取的連結已刪除、卸載或在目前視圖不可見。請明確重新選取來源。");
        if (links.Count == 0)
        {
            TaskDialog.Show("連結模型標註", "目前視圖沒有已載入且可見的直接連結模型。");
            return false;
        }

        var modes = new[] { DimensionMode.ColumnSetout, DimensionMode.BeamGrid, DimensionMode.BeamWidth };
        using var form = new LinkedDimensionSourceForm(
            links.Select(item => item.Name + " [" + item.Id + "]").ToList(), remembered,
            Array.IndexOf(modes, requestedMode ?? session.Mode), requestedMode.HasValue);
        if (form.ShowDialog(new Owner(app.MainWindowHandle)) != Forms.DialogResult.OK) return false;
        if (form.SourceIndex < 0 || form.SourceIndex >= links.Count || form.CategoryIndex < 0 || form.CategoryIndex >= modes.Length)
            return false;
        link = links[form.SourceIndex];
        mode = modes[form.CategoryIndex];
        if (!link.IsValidObject || link.GetLinkDocument() == null || link.IsHidden(view))
        {
            TaskDialog.Show("連結模型標註", "所選連結已失效，請重新執行並選取來源。");
            link = null;
            return false;
        }
        session.LinkUniqueId = link.UniqueId;
        session.Mode = mode;
        return true;
#else
        TaskDialog.Show("連結模型標註", "連結模型標註需要 Revit 2024 以上版本；Revit 2022／2023 不支援此視圖可見性篩選。本機模型標註仍可使用。");
        return false;
#endif
    }

    internal static HashSet<ElementId> CollectVisible(Document host, View view, RevitLinkInstance link)
    {
#if REVIT2024 || REVIT2025 || REVIT2026
        if (!link.IsValidObject || link.GetLinkDocument() == null || link.IsHidden(view))
            throw new InvalidOperationException("所選連結已失效、卸載或隱藏，請重新選取來源。");
        using var collector = new FilteredElementCollector(host, view.Id, link.Id);
        using var categories = new ElementMulticategoryFilter(new[] {
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Grids
        });
        return new HashSet<ElementId>(collector.WherePasses(categories).WhereElementIsNotElementType().ToElementIds());
#else
        throw new NotSupportedException("連結模型標註需要 Revit 2024 以上版本。");
#endif
    }

    internal static (IReadOnlyList<GridSelectionItem> Horizontal, IReadOnlyList<GridSelectionItem> Vertical)
        CollectGrids(RevitLinkInstance link, View hostView, HashSet<ElementId> visible)
    {
        var horizontal = new List<GridSelectionItem>();
        var vertical = new List<GridSelectionItem>();
        var source = link.GetLinkDocument() ?? throw new InvalidOperationException("連結模型已卸載。");
        var transform = link.GetTotalTransform();
        XYZ right = hostView.RightDirection.Normalize(), up = hostView.UpDirection.Normalize();
        foreach (var id in visible)
        {
            if (source.GetElement(id) is not Grid grid || grid.Curve is not Line line || !line.IsBound) continue;
            XYZ start = transform.OfPoint(line.GetEndPoint(0)), end = transform.OfPoint(line.GetEndPoint(1));
            XYZ direction = (end - start).Normalize(), midpoint = (start + end) * 0.5;
            bool isHorizontal = Math.Abs(direction.DotProduct(right)) >= Math.Abs(direction.DotProduct(up));
            double axis = midpoint.DotProduct(isHorizontal ? up : right);
            double mm = UnitUtils.ConvertFromInternalUnits(axis, UnitTypeId.Millimeters);
            string label = grid.Name + (isHorizontal ? " (Y=" : " (X=") + mm.ToString("N0") + ")";
            (isHorizontal ? horizontal : vertical).Add(new GridSelectionItem(id, label, axis));
        }
        return (horizontal.OrderBy(item => item.AxisValue).ToList(), vertical.OrderBy(item => item.AxisValue).ToList());
    }

    private sealed class Owner : Forms.IWin32Window
    {
        internal Owner(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }
}
}
