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
        /// 以房間定位點與包圍盒採樣點檢查「同一點是否同時屬於多間房」。
        /// 若命中多間房，後續自動寫參數、刪除或生成皆應視為不安全。
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
                        Description = $"偵測到房間重疊或空間歸屬不唯一：{string.Join("、", matched.Select(FormatRoomLabel))}。相關房間已禁止自動回寫參數、刪除或生成裝修面。"
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
            var z = (min.Z + max.Z) * 0.5;
            var x1 = min.X + (max.X - min.X) * 0.25;
            var x2 = min.X + (max.X - min.X) * 0.50;
            var x3 = min.X + (max.X - min.X) * 0.75;
            var y1 = min.Y + (max.Y - min.Y) * 0.25;
            var y2 = min.Y + (max.Y - min.Y) * 0.50;
            var y3 = min.Y + (max.Y - min.Y) * 0.75;

            yield return new XYZ(x2, y2, z);
            yield return new XYZ(x1, y1, z);
            yield return new XYZ(x1, y3, z);
            yield return new XYZ(x3, y1, z);
            yield return new XYZ(x3, y3, z);
        }

        private static bool IsPointInRoomSafe(Room room, XYZ point)
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
    }
}
