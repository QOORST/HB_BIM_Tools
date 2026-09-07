using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    internal sealed class RoomOverlapCheckIssue
    {
        public HashSet<long> RoomIds { get; } = new HashSet<long>();
        public string Description { get; set; }
    }

    internal static class RoomOverlapGuard
    {
        /// <summary>
        /// 檢查目標房間是否存在空間歸屬不唯一的風險。
        /// 取樣包含房間定位點、平面九宮格，以及低/中/高三個高度；
        /// 可涵蓋樓梯間、挑空、跨層房間等只在部分高度重疊的情境。
        /// </summary>
        public static List<RoomOverlapCheckIssue> Detect(IList<Room> rooms)
        {
            var issues = new List<RoomOverlapCheckIssue>();
            if (rooms == null || rooms.Count < 2)
                return issues;

            var roomSamples = rooms
                .Where(r => r != null && r.Area > 0)
                .Select(r => new
                {
                    Room = r,
                    Id = RevitCompat.GetElementIdValue(r.Id),
                    Points = GetRoomProbePoints(r).ToList()
                })
                .Where(x => x.Points.Count > 0)
                .ToList();

            var reportedKeys = new HashSet<string>();

            foreach (var sample in roomSamples)
            {
                foreach (var point in sample.Points)
                {
                    var matched = roomSamples
                        .Where(x => IsPointInRoomSafe(x.Room, point))
                        .Select(x => x.Room)
                        .ToList();

                    if (matched.Count <= 1)
                        continue;

                    var ids = matched.Select(r => RevitCompat.GetElementIdValue(r.Id)).OrderBy(x => x).ToList();
                    var key = string.Join("|", ids);
                    if (!reportedKeys.Add(key))
                        continue;

                    var issue = new RoomOverlapCheckIssue
                    {
                        Description = $"偵測到房間重疊或空間歸屬不唯一：{string.Join("、", matched.Select(FormatRoomLabel))}。相關房間已禁止自動回寫或重產裝修面，避免誤改報表參數。"
                    };

                    foreach (var id in ids)
                        issue.RoomIds.Add(id);

                    issues.Add(issue);
                }
            }

            return issues;
        }

        public static string FormatRoomLabel(Room room)
        {
            if (room == null)
                return "未知房間";

            var number = string.IsNullOrWhiteSpace(room.Number) ? "(無編號)" : room.Number;
            var name = string.IsNullOrWhiteSpace(room.Name) ? "(無名稱)" : room.Name;
            return $"{number} {name}";
        }

        public static bool IsPointInRoomSafe(Room room, XYZ point)
        {
            try
            {
                return room != null && point != null && room.IsPointInRoom(point);
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<XYZ> GetRoomProbePoints(Room room)
        {
            if (room == null)
                yield break;

            if (room.Location is LocationPoint lp)
                yield return lp.Point;

            var bbox = room.get_BoundingBox(null);
            if (bbox == null)
                yield break;

            var min = bbox.Min;
            var max = bbox.Max;
            var xs = new[]
            {
                min.X + (max.X - min.X) * 0.25,
                min.X + (max.X - min.X) * 0.50,
                min.X + (max.X - min.X) * 0.75
            };
            var ys = new[]
            {
                min.Y + (max.Y - min.Y) * 0.25,
                min.Y + (max.Y - min.Y) * 0.50,
                min.Y + (max.Y - min.Y) * 0.75
            };
            var zs = new[]
            {
                min.Z + (max.Z - min.Z) * 0.10,
                min.Z + (max.Z - min.Z) * 0.50,
                min.Z + (max.Z - min.Z) * 0.90
            };

            foreach (var z in zs)
            {
                foreach (var x in xs)
                {
                    foreach (var y in ys)
                    {
                        yield return new XYZ(x, y, z);
                    }
                }
            }
        }
    }
}
