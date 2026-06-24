using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace YDBIM.AutoDimension.Core
{

internal sealed class RoomAnnotationResult
{
    public RoomAnnotationResult(int processed, int changed)
    {
        Processed = processed;
        Changed = changed;
    }

    public int Processed { get; }

    public int Changed { get; }
}

internal sealed class RoomAnnotationService
{
    public RoomAnnotationResult UpdateRoomContent(Document doc, View view)
    {
        IReadOnlyList<Room> rooms = CollectRooms(doc, view);
        int processed = 0;
        int updated = 0;
        foreach (Room room in rooms)
        {
            if (!TryGetRoomDimensions(room, view, out double widthInternal, out double depthInternal))
            {
                continue;
            }

            processed++;
            double widthMm = UnitUtils.ConvertFromInternalUnits(widthInternal, UnitTypeId.Millimeters);
            double depthMm = UnitUtils.ConvertFromInternalUnits(depthInternal, UnitTypeId.Millimeters);
            int widthRounded = (int)Math.Round(widthMm, MidpointRounding.AwayFromZero);
            int depthRounded = (int)Math.Round(depthMm, MidpointRounding.AwayFromZero);

            bool changed = false;
            changed |= TrySetParameter(room, "BG_DIM_Width1", widthRounded.ToString());
            changed |= TrySetParameter(room, "BG_DIM_Depth1", depthRounded.ToString());
            changed |= TrySetParameter(room, "Comments", $"{widthRounded} X {depthRounded}");

            if (changed)
            {
                updated++;
            }
        }

        return new RoomAnnotationResult(processed, updated);
    }

    private static IReadOnlyList<Room> CollectRooms(Document doc, View view)
    {
        var byId = new Dictionary<int, Room>();

        foreach (Room room in new FilteredElementCollector(doc, view.Id)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .OfType<Room>())
        {
            AddRoom(byId, room);
        }

        if (byId.Count > 0)
        {
            return byId.Values.ToList();
        }

        ElementId? levelId = view.GenLevel?.Id;
        foreach (Room room in new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .OfType<Room>())
        {
            if (levelId is not null && room.LevelId != levelId)
            {
                continue;
            }

            AddRoom(byId, room);
        }

        return byId.Values.ToList();
    }

    private static void AddRoom(IDictionary<int, Room> rooms, Room room)
    {
        if (room.Area <= 1e-6)
        {
            return;
        }

        rooms[ElementIdCompat.ToInt32(room.Id)] = room;
    }

    private static bool TryGetRoomDimensions(Room room, View view, out double widthInternal, out double depthInternal)
    {
        widthInternal = 0.0;
        depthInternal = 0.0;

        var points = new List<XYZ>();
        var directions = new List<(XYZ Direction, double Length)>();
        var boundaryOptions = new SpatialElementBoundaryOptions();
        IList<IList<BoundarySegment>>? boundaries = null;
        try
        {
            boundaries = room.GetBoundarySegments(boundaryOptions);
        }
        catch
        {
            boundaries = null;
        }

        if (boundaries is not null)
        {
            foreach (IList<BoundarySegment> loop in boundaries)
            {
                foreach (BoundarySegment segment in loop)
                {
                    Curve curve = segment.GetCurve();
                    if (!curve.IsBound)
                    {
                        continue;
                    }

                    points.Add(curve.GetEndPoint(0));
                    points.Add(curve.GetEndPoint(1));

                    if (curve is Line line && line.IsBound)
                    {
                        XYZ direction = (line.GetEndPoint(1) - line.GetEndPoint(0));
                        double length = direction.GetLength();
                        if (length > 1e-6)
                        {
                            directions.Add((DirectionUtils.Canonicalize(direction.Normalize()), length));
                        }
                    }
                }
            }
        }

        if (points.Count >= 2)
        {
            XYZ widthDirection = ResolveDominantBoundaryDirection(directions, view.RightDirection.Normalize());
            XYZ depthDirection = ResolvePerpendicularDirection(widthDirection, directions, view.UpDirection.Normalize());
            widthInternal = ProjectionRange(points, widthDirection);
            depthInternal = ProjectionRange(points, depthDirection);
            return widthInternal > 1e-6 && depthInternal > 1e-6;
        }

        BoundingBoxXYZ? bbox = room.get_BoundingBox(view) ?? room.get_BoundingBox(null);
        if (bbox is null)
        {
            return false;
        }

        widthInternal = Math.Abs(bbox.Max.X - bbox.Min.X);
        depthInternal = Math.Abs(bbox.Max.Y - bbox.Min.Y);
        return widthInternal > 1e-6 && depthInternal > 1e-6;
    }

    private static XYZ ResolveDominantBoundaryDirection(IReadOnlyList<(XYZ Direction, double Length)> directions, XYZ fallback)
    {
        if (directions.Count == 0)
        {
            return fallback;
        }

        return directions
            .Select(d => new
            {
                d.Direction,
                Weight = directions
                    .Where(x => DirectionUtils.IsParallel(x.Direction, d.Direction))
                    .Sum(x => x.Length)
            })
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => Math.Abs(x.Direction.DotProduct(fallback)))
            .First()
            .Direction;
    }

    private static XYZ ResolvePerpendicularDirection(XYZ widthDirection, IReadOnlyList<(XYZ Direction, double Length)> directions, XYZ fallback)
    {
        XYZ? perpendicular = directions
            .Where(d => Math.Abs(d.Direction.DotProduct(widthDirection)) < 0.2)
            .OrderByDescending(d => d.Length)
            .Select(d => (XYZ?)d.Direction)
            .FirstOrDefault();

        if (perpendicular is not null)
        {
            return perpendicular;
        }

        XYZ normal = XYZ.BasisZ;
        XYZ depth = normal.CrossProduct(widthDirection);
        if (depth.GetLength() < 1e-6)
        {
            return fallback;
        }

        return depth.Normalize();
    }

    private static double ProjectionRange(IReadOnlyList<XYZ> points, XYZ direction)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (XYZ point in points)
        {
            double projection = point.DotProduct(direction);
            min = Math.Min(min, projection);
            max = Math.Max(max, projection);
        }

        return Math.Abs(max - min);
    }

    private static bool TrySetParameter(Element element, string parameterName, string value)
    {
        Parameter? parameter = parameterName == "Comments"
            ? element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
            : element.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly)
        {
            return false;
        }

        StorageType storageType = parameter.StorageType;
        if (storageType == StorageType.String)
        {
            if (parameter.AsString() == value)
            {
                return false;
            }

            return parameter.Set(value);
        }

        if (storageType == StorageType.Integer && int.TryParse(value, out int i))
        {
            if (parameter.AsInteger() == i)
            {
                return false;
            }

            return parameter.Set(i);
        }

        if (storageType == StorageType.Double && double.TryParse(value, out double d))
        {
            double internalValue = UnitUtils.ConvertToInternalUnits(d, UnitTypeId.Millimeters);
            if (Math.Abs(parameter.AsDouble() - internalValue) < 1e-6)
            {
                return false;
            }

            return parameter.Set(internalValue);
        }

        return false;
    }
}
}

