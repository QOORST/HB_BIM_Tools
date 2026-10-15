using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using YDBIM.AutoDimension.Core;
using YDBIM.AutoDimension.UI;

internal static class QuickFlowTests
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; }
        T Field<T>(object form, string name) => (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var types = new[] { "Linear" };
        Check(LinkedDimensionPolicy.SamePair("link1:beam1:face1", "link1:beam2:face2", "link1:beam2:face2", "link1:beam1:face1"), "linked pair reverse ordering");
        Check(!LinkedDimensionPolicy.SamePair("link1:face1", "link1:face2", "link2:face1", "link2:face2"), "different link instance is not covered");
        Check(!LinkedDimensionPolicy.SamePair("link1:beam1", "link1:beam2", "link1:beam3", "link1:beam4"), "different source beams are not covered");
        Check(!LinkedDimensionPolicy.SamePair("", "b", "", "b"), "empty stable reference is not coverage");
        Check(!LinkedDimensionPolicy.SamePair("a", "a", "a", "a"), "same reference cannot measure distance");
        Check(LinkedDimensionPolicy.SegmentsMatch(new[] { 1d, 2d }, new double?[] { 1, 2 }, .01), "native linked values match");
        Check(LinkedDimensionPolicy.SegmentsMatch(new[] { 1d }, new double?[] { 1.005 }, .01), "native linked tolerance");
        Check(!LinkedDimensionPolicy.SegmentsMatch(new[] { 1d }, new double?[] { 1.1 }, .01), "wrong linked scale rejected");
        Check(!LinkedDimensionPolicy.SegmentsMatch(new[] { 1d, 2d }, new double?[] { 3 }, .01), "collapsed linked chain rejected");
        Check(!LinkedDimensionPolicy.SegmentsMatch(new[] { 1d }, new double?[] { null }, .01), "missing native measurement rejected");
        Check(!LinkedDimensionPolicy.SegmentsMatch(new[] { 1d }, new double?[] { double.NaN }, .01), "invalid native measurement rejected");
        Check(!LinkedDimensionPolicy.SegmentsMatch(new[] { 1d }, new double?[] { double.PositiveInfinity }, .01), "infinite native measurement rejected");
        Check(!LinkedDimensionPolicy.SegmentsMatch(new[] { 0d }, new double?[] { 0 }, .01), "zero native measurement rejected");
        var linkedOptions = new DimensionOptions { SourceLinkInstanceId = new ElementId(123), SelectedBeamIds = new[] { new ElementId(42) } };
        var restoredLocal = AutoDimensionSavedSettings.FromOptions(linkedOptions).ToOptions();
        Check(restoredLocal.SourceLinkInstanceId == null && restoredLocal.SelectedBeamIds.Count == 0, "linked source and beam ids never leak into saved local formatting");
        var boundaries = new[] { 0d, 70d, 1000d, 1070d, 2000d, 2070d };
        var allSegments = boundaries.Zip(boundaries.Skip(1), (a, b) => (a, b)).ToArray();
        Check(BeamChainProjectionPolicy.MissingRuns(boundaries, Array.Empty<(double, double)>(), 1)
            .SequenceEqual(new[] { (0, 5) }), "no existing segments creates one chain");
        Check(BeamChainProjectionPolicy.MissingRuns(boundaries, allSegments, 1).Count == 0, "complete chain creates nothing");
        Check(BeamChainProjectionPolicy.MissingRuns(boundaries, new[] { (0d, 70d), (1070d, 2000d) }, 1)
            .SequenceEqual(new[] { (1, 3), (4, 5) }), "only contiguous missing runs created");
        Check(BeamChainProjectionPolicy.MissingRuns(boundaries, new[] { (0d, 2070d) }, 1)
            .SequenceEqual(new[] { (0, 5) }), "overall dimension cannot hide subdivisions");
        Check(BeamChainProjectionPolicy.MissingRuns(boundaries, new[] { (70d, 0d), (1000d, 70d), (1070d, 1000d) }, 1)
            .SequenceEqual(new[] { (3, 5) }), "reversed existing segments cover same intervals");
        Check(BeamChainProjectionPolicy.MissingRuns(new[] { 0d, 70d }, new[] { (0.5, 70.5) }, 1).Count == 0,
            "endpoint tolerance supported");
        Check(BeamChainProjectionPolicy.MissingRuns(new[] { 0d, 70d }, new[] { (2d, 72d) }, 1).Count == 1,
            "same length at different position not covered");
        Check(BeamChainProjectionPolicy.MissingRuns(new[] { 0d, 70d }, new[] { (0d, 80d) }, 1).Count == 1,
            "both endpoints required");
        Check(BeamChainProjectionPolicy.MissingRuns(new[] { 70d, 1000d }, new[] { (0d, 70d), (1000d, 1070d) }, 1).Count == 1,
            "separate dimensions do not invent a connecting segment");
        Check(BeamChainProjectionPolicy.MissingRuns(Array.Empty<double>(), allSegments, 1).Count == 0, "empty candidate");
        Check(BeamChainProjectionPolicy.MissingRuns(new[] { 0d, 70d }, new[] { (double.NaN, 70d) }, 1).Count == 1,
            "invalid existing measurement cannot cover candidate");
        var supplemental = BeamChainProjectionPolicy.MissingRuns(boundaries, new[] { (70d, 1000d) }, 1);
        var after = new List<(double, double)> { (70d, 1000d) };
        foreach (var run in supplemental)
            for (int i = run.Start; i < run.End; i++) after.Add((boundaries[i], boundaries[i + 1]));
        Check(BeamChainProjectionPolicy.MissingRuns(boundaries, after, 1).Count == 0, "rerun after filling gaps creates nothing");
        var merged = BeamChainProjectionPolicy.MergeRanges(new[] { (0d, 3070d), (1000d, 4070d) }, 1);
        Check(merged.Count == 1 && merged[0].Count == 2, "partial projected chains merge");
        Check(BeamChainProjectionPolicy.MergeRanges(new[] { (0d, 100d), (200d, 300d) }, 1).Count == 2, "disjoint chains stay separate");
        Check(BeamChainProjectionPolicy.MergeRanges(new[] { (0d, 100d), (0d, 100d) }, 1).Count == 1, "overlapping buildup merges");
        Check(BeamChainProjectionPolicy.MergeRanges(new[] { (200d, 300d), (0d, 100d), (90d, 210d) }, 1).Count == 1, "transitive merge independent of order");
        Check(BeamChainProjectionPolicy.MergeRanges(Array.Empty<(double, double)>(), 1).Count == 0, "empty range list");
        Check(BeamChainProjectionPolicy.SamePositions(new[] { 0d, 70d, 1000d, 1070d }, new[] { 1070d, 1000d, 70d, 0d }, 1), "reversed boundaries same");
        Check(!BeamChainProjectionPolicy.SamePositions(new[] { 0d, 70d, 1000d, 1070d }, new[] { 0d, 1070d }, 1), "matching endpoints cannot hide missing segments");
        Check(!BeamChainProjectionPolicy.SamePositions(new[] { 0d, 70d }, new[] { 0d, 80d }, 1), "different buildup edge retained");
        Check(DimensionQuickPolicy.UsesWholeView(DimensionMode.ColumnSetout), "columns use whole view");
        Check(DimensionQuickPolicy.UsesWholeView(DimensionMode.BeamGrid), "grids use whole view");
        Check(!DimensionQuickPolicy.UsesWholeView(DimensionMode.BeamWidth), "beams remain selection based");
        Check(!DimensionQuickPolicy.UsesWholeView(null), "advanced selection never expands scope");
        Check(DimensionQuickPolicy.SameMeasurement("A|B", "A|B", 1), "same references independent of offset");
        Check(DimensionQuickPolicy.SameMeasurement("A|B", "A|B", -1), "reversed line is same measurement");
        Check(!DimensionQuickPolicy.SameMeasurement("A|B", "A|C", 1), "different references retained");
        Check(!DimensionQuickPolicy.SameMeasurement("A|B", "A|B", 0), "other measurement direction retained");
        Check(!DimensionQuickPolicy.SameMeasurement("", "", 1), "invalid references not considered covered");
        Check(!DimensionQuickPolicy.SameMeasurement("A", "A", double.NaN), "invalid direction rejected");
        Check(!DimensionQuickPolicy.HasUnresolved(0, 4, 4), "already complete remains quiet");
        Check(DimensionQuickPolicy.HasUnresolved(0, 4, 3), "missing reference still warns");
        Check(!DimensionQuickPolicy.HasUnresolved(2, 4, 2), "partially filled now complete");
        Check(DimensionQuickPolicy.HasUnresolved(1, 4, 2), "partial failure still warns");
        var beam = new AutoDimensionSavedSettings { ModeType = DimensionMode.BeamWidth, DimensionTypeName = "Linear", OffsetMm = 600, BeamSpacingOffsetMm = 800 };
        Check(beam.CanReuse(DimensionMode.BeamWidth, types), "beam configuration reusable");
        Check(!beam.CanReuse(DimensionMode.ColumnSetout, types), "different mode rejected");
        Check(!beam.CanReuse(DimensionMode.BeamWidth, new[] { "Other" }), "deleted type rejected");
        Check(!beam.CanReuse(DimensionMode.BeamWidth, Array.Empty<string>()), "empty types rejected");
        beam.IncludeBeamSpacing = false;
        Check(!beam.CanReuse(DimensionMode.BeamWidth, types), "no beam dimensions rejected");
        beam.IncludeBeamWidth = true;
        Check(beam.CanReuse(DimensionMode.BeamWidth, types), "width-only configuration");
        beam.OffsetMm = double.NaN;
        Check(!beam.CanReuse(DimensionMode.BeamWidth, types), "invalid offset rejected");
        beam.OffsetMm = -600;
        Check(beam.CanReuse(DimensionMode.BeamWidth, types), "signed offset supported");
        beam.OffsetMm = 0;
        Check(beam.CanReuse(DimensionMode.BeamWidth, types), "zero offset supported");
        beam.OffsetMm = 600;
        var options = beam.ToOptions();
        Check(Math.Abs(options.OffsetInternal * 304.8 - 600) < 1e-8, "offset conversion");
        Check(Math.Abs(options.BeamSpacingOffsetInternal!.Value * 304.8 - 800) < 1e-8, "spacing conversion");
        Check(options.SelectedBeamIds.Count == 0 && options.SelectedColumnIds == null, "selection not persisted");
        var column = new AutoDimensionSavedSettings { ModeType = DimensionMode.ColumnSetout };
        Check(column.CanReuse(DimensionMode.ColumnSetout, types), "default column configuration");
        column.ColumnFrontBackSide = FrontBackSide.None;
        Check(!column.CanReuse(DimensionMode.ColumnSetout, types), "no column side rejected");
        var grid = new AutoDimensionSavedSettings { ModeType = DimensionMode.BeamGrid, GridOffsetsInPaperSpace = true, GridPrimaryOffsetMm = 8, GridOverallOffsetMm = 20,
            SelectedHorizontalGridIds = new List<int> { 99 } };
        Check(grid.CanReuse(DimensionMode.BeamGrid, types), "grid configuration reusable");
        Check(grid.ToOptions().SelectedHorizontalGridIds.Count == 0, "old grid IDs never restored for quick run");
        Check(grid.ToOptions().GridOffsetsInPaperSpace, "paper-space preserved");
        grid.Direction = (PlacementDirection)99;
        Check(!grid.CanReuse(DimensionMode.BeamGrid, types), "invalid direction rejected");
        grid.Direction = PlacementDirection.East;
        foreach (var mode in new[] { DimensionMode.ColumnSetout, DimensionMode.BeamWidth, DimensionMode.BeamGrid })
        {
            using var form = new DimensionOptionsForm(types,
                new[] { new GridSelectionItem(new ElementId(1), "A", 0), new GridSelectionItem(new ElementId(2), "B", 1) },
                Array.Empty<GridSelectionItem>(), mode, true,
                savedSettings: new Dictionary<DimensionMode, AutoDimensionSavedSettings> { [DimensionMode.BeamGrid] = grid });
            form.ConfigureQuickScope(2);
            Check(Field<TabControl>(form, "_tabControl").TabPages.Count == 2, "quick flow hides grid bubbles");
            Check(Field<Button>(form, "_applyButton").Text == "套用並繼續", "quick action label");
            Check(form.TryGetOptions(out var result, out _), "quick form valid");
            Check(result.ModeType == mode && !result.GridBubblesOnly, "mode locked");
            if (mode == DimensionMode.BeamWidth)
                Check(!Field<ComboBox>(form, "_beamScope").Enabled && result.SelectedBeamsOnly, "cannot expand quick beam scope");
            if (mode == DimensionMode.BeamGrid)
                Check(result.SelectedHorizontalGridIds.Count == 2, "current grid selection replaces stale IDs");
        }
        using (var form = new DimensionOptionsForm(types,
            new[] { new GridSelectionItem(new ElementId(1), "A", 0), new GridSelectionItem(new ElementId(2), "B", 1) },
            Array.Empty<GridSelectionItem>(), DimensionMode.BeamGrid, true))
        {
            form.ConfigureQuickScope(2, true);
            Check(Field<Label>(form, "_statusLabel").Text.Contains("目前視圖"), "full-view scope clearly labeled");
            Check(!Field<CheckedListBox>(form, "_horizontalGridList").Enabled, "full-view grids cannot be narrowed");
            Check(form.TryGetOptions(out var wholeViewOptions, out _) && wholeViewOptions.SelectedHorizontalGridIds.Count == 2, "all visible grids included");
        }
        Console.WriteLine($"PASS {count} quick-flow settings/UI checks; native Revit execution not tested.");
    }
}
