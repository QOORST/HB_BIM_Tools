using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

#nullable enable

namespace YDBIM.AutoDimension.Core
{

internal enum DimensionMode
{
    ColumnSetout = 0,
    BeamGrid = 1,
    BeamWidth = 2
}

internal enum PlacementMode
{
    Auto = 0,
    Manual = 1
}

internal enum PlacementDirection
{
    East = 0,
    West = 1,
    North = 2,
    South = 3,
    NorthEast = 4,
    NorthWest = 5,
    SouthEast = 6,
    SouthWest = 7
}

internal enum LeftRightSide
{
    None = 0,
    Left = 1,
    Right = 2
}

internal enum FrontBackSide
{
    None = 0,
    Front = 1,
    Back = 2
}

internal sealed class DimensionOptions
{
    public DimensionMode ModeType { get; set; } = DimensionMode.ColumnSetout;

    public PlacementMode Mode { get; set; } = PlacementMode.Auto;

    public PlacementDirection Direction { get; set; } = PlacementDirection.East;

    public LeftRightSide ColumnLeftRightSide { get; set; } = LeftRightSide.None;

    public FrontBackSide ColumnFrontBackSide { get; set; } = FrontBackSide.Front;

    public double OffsetInternal { get; set; } = 3.0;

    public string? DimensionTypeName { get; set; }

    public IReadOnlyList<ElementId> SelectedHorizontalGridIds { get; set; } = Array.Empty<ElementId>();

    public IReadOnlyList<ElementId> SelectedVerticalGridIds { get; set; } = Array.Empty<ElementId>();
}
}

