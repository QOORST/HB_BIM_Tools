using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    /// <summary>建立避讓管線；任何不完整連接皆失敗，由呼叫端回復整個交易。</summary>
    public static class RevitUtils
    {
        private const double ConnectorTolerance = 1.0 / 304.8; // 1 mm

        // Do not retain live connector wrappers across regeneration/deletion.
        private sealed class ConnectorReference
        {
            public ElementId OwnerId { get; }
            public int ConnectorId { get; }

            public ConnectorReference(Connector connector)
            {
                OwnerId = connector.Owner.Id;
                ConnectorId = connector.Id;
            }

            public Connector Resolve(Document doc)
            {
                Connector connector = GetConnectorManager(doc.GetElement(OwnerId))?.Lookup(ConnectorId);
                if (connector == null)
                    throw new InvalidOperationException($"連接器已不存在：{OwnerId}/{ConnectorId}");
                return connector;
            }
        }

        /// <summary>
        /// 必須在交易內呼叫；false 代表呼叫端必須 RollBack，不可提交部分結果。
        /// </summary>
        public static bool ReplaceWithDetour(Document doc, Element target, DetourPlan plan, AvoidOptions opt)
        {
            if (doc == null || !doc.IsModifiable || target == null || plan == null || !plan.IsValid ||
                plan.Path == null || plan.Path.Count < 2 ||
                !(target is Pipe || target is Duct || target is Conduit))
            {
                Logger.Warning("無效的替換參數或未啟動交易");
                return false;
            }

            ElementId originalId = target.Id;
            try
            {
                var source = (MEPCurve)target;
                var line = (source.Location as LocationCurve)?.Curve as Line;
                if (line == null)
                    throw new InvalidOperationException("僅支援直線管線");

                XYZ start = line.GetEndPoint(0);
                XYZ end = line.GetEndPoint(1);
                if (plan.Path.First().DistanceTo(start) > ConnectorTolerance ||
                    plan.Path.Last().DistanceTo(end) > ConnectorTolerance)
                    throw new InvalidOperationException("避讓路徑必須保留原始兩端位置");

                Connector sourceStart = RequireConnectorAtPoint(source, start);
                Connector sourceEnd = RequireConnectorAtPoint(source, end);
                // A branch/tap cannot be silently discarded by replacing only the two endpoints.
                foreach (Connector connector in source.ConnectorManager.Connectors)
                {
                    if (connector.ConnectorType != ConnectorType.Logical && connector.IsConnected &&
                        connector.Id != sourceStart.Id && connector.Id != sourceEnd.Id)
                        throw new InvalidOperationException("來源有中途分支連接，無法安全替換");
                }

                var externalStart = CaptureExternalConnections(sourceStart);
                var externalEnd = CaptureExternalConnections(sourceEnd);
                Disconnect(doc, sourceStart, externalStart);
                Disconnect(doc, sourceEnd, externalEnd);

                var segments = new List<MEPCurve>();
                for (int i = 0; i < plan.Path.Count - 1; i++)
                    segments.Add(CreateSegment(doc, source, plan.Path[i], plan.Path[i + 1]));

                doc.Regenerate();
                List<FamilyInstance> elbows = CreateElbowsForSegments(doc, segments);
                RestoreExternalConnections(doc, segments.First(), start, externalStart);
                RestoreExternalConnections(doc, segments.Last(), end, externalEnd);
                doc.Regenerate();
                ValidateConnections(doc, segments, elbows, start, end, externalStart, externalEnd);

                // Delay deletion until the complete replacement exists. Still verify afterwards:
                // deletion may cascade to dependent elements, including original neighbours.
                ICollection<ElementId> deletedIds = doc.Delete(originalId);
                if (deletedIds.Count != 1 || !deletedIds.Contains(originalId))
                    throw new InvalidOperationException("刪除原管線會連帶刪除其他元素，取消避讓以保留相依資料");
                doc.Regenerate();
                ValidateConnections(doc, segments, elbows, start, end, externalStart, externalEnd);

                Logger.Info($"元素 {originalId} 替換及連接驗證完成，等待交易提交（{segments.Count} 段）");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"替換元素 {originalId} 失敗，必須回復交易", ex);
                return false;
            }
        }

        private static MEPCurve CreateSegment(Document doc, MEPCurve source, XYZ start, XYZ end)
        {
            if (source is Conduit conduit)
            {
                if (conduit.LevelId == ElementId.InvalidElementId)
                    throw new InvalidOperationException("電管沒有參考樓層");
                Conduit segment = Conduit.Create(doc, conduit.GetTypeId(), start, end, conduit.LevelId);
                if (segment == null)
                    throw new InvalidOperationException("建立電管段失敗");
                Parameter diameter = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                Parameter newDiameter = segment.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (diameter == null || newDiameter == null ||
                    (Math.Abs(newDiameter.AsDouble() - diameter.AsDouble()) > 1e-9 &&
                     (newDiameter.IsReadOnly || !newDiameter.Set(diameter.AsDouble()))))
                    throw new InvalidOperationException("無法保留電管直徑");
                return segment;
            }

            // Preserve the existing pipe/duct copy-based parameter behaviour.
            var copiedIds = ElementTransformUtils.CopyElement(doc, source.Id, new XYZ(1000, 0, 0));
            if (copiedIds.Count != 1)
                throw new InvalidOperationException("複製管線未產生唯一管線段");
            var copy = doc.GetElement(copiedIds.Single()) as MEPCurve;
            var location = copy?.Location as LocationCurve;
            if (location == null)
                throw new InvalidOperationException("複製的管線段沒有 LocationCurve");
            location.Curve = Line.CreateBound(start, end);
            return copy;
        }

        /// <summary>依路徑順序連接相鄰段。缺少任何彎頭/連接即拋出，禁止部分成功。</summary>
        public static List<FamilyInstance> CreateElbowsForSegments(Document doc, List<MEPCurve> segments)
        {
            if (segments == null || segments.Count == 0)
                throw new InvalidOperationException("沒有可連接的管線段");

            // Snapshot expected adjacent pairs before any elbow trims the segment endpoints.
            var pairs = new List<Tuple<ConnectorReference, ConnectorReference>>();
            for (int i = 0; i < segments.Count - 1; i++)
            {
                XYZ joint = ((LocationCurve)segments[i].Location).Curve.GetEndPoint(1);
                pairs.Add(Tuple.Create(
                    new ConnectorReference(RequireConnectorAtPoint(segments[i], joint)),
                    new ConnectorReference(RequireConnectorAtPoint(segments[i + 1], joint))));
            }

            var elbows = new List<FamilyInstance>();
            for (int i = 0; i < pairs.Count; i++)
            {
                Connector first = pairs[i].Item1.Resolve(doc);
                Connector second = pairs[i].Item2.Resolve(doc);
                if (first.IsConnected || second.IsConnected)
                    throw new InvalidOperationException($"第 {i + 1} 個轉折的連接器已被占用");
                FamilyInstance elbow = doc.Create.NewElbowFitting(first, second);
                if (elbow == null || !elbow.IsValidObject)
                    throw new InvalidOperationException($"第 {i + 1} 個彎頭建立失敗");
                elbows.Add(elbow);
                doc.Regenerate();
                ValidateElbow(elbow, segments[i], segments[i + 1]);
            }
            return elbows;
        }

        private static List<ConnectorReference> CaptureExternalConnections(Connector source)
        {
            var references = new List<ConnectorReference>();
            foreach (Connector other in source.AllRefs)
            {
                if (other.Owner.Id != source.Owner.Id && other.ConnectorType != ConnectorType.Logical &&
                    source.IsConnectedTo(other))
                    references.Add(new ConnectorReference(other));
            }
            if (source.IsConnected && references.Count == 0)
                throw new InvalidOperationException("無法辨識原有實體連接");
            return references;
        }

        private static void Disconnect(Document doc, Connector source, List<ConnectorReference> references)
        {
            foreach (var reference in references)
            {
                Connector external = reference.Resolve(doc);
                source.DisconnectFrom(external);
                if (source.IsConnectedTo(external))
                    throw new InvalidOperationException("無法斷開原有連接");
            }
        }

        private static void RestoreExternalConnections(Document doc, MEPCurve segment, XYZ point,
            List<ConnectorReference> references)
        {
            foreach (var reference in references)
            {
                Connector endpoint = RequireConnectorAtPoint(segment, point);
                Connector external = reference.Resolve(doc);
                if (!endpoint.IsConnectedTo(external))
                    endpoint.ConnectTo(external);
            }
        }

        private static void ValidateConnections(Document doc, List<MEPCurve> segments,
            List<FamilyInstance> elbows, XYZ start, XYZ end,
            List<ConnectorReference> externalStart, List<ConnectorReference> externalEnd)
        {
            if (segments.Any(segment => !segment.IsValidObject) || elbows.Count != segments.Count - 1)
                throw new InvalidOperationException("避讓管線或彎頭數量不完整");
            for (int i = 0; i < elbows.Count; i++)
                ValidateElbow(elbows[i], segments[i], segments[i + 1]);
            ValidateEndpoint(doc, RequireConnectorAtPoint(segments.First(), start), externalStart);
            ValidateEndpoint(doc, RequireConnectorAtPoint(segments.Last(), end), externalEnd);
        }

        private static void ValidateElbow(FamilyInstance elbow, MEPCurve first, MEPCurve second)
        {
            if (elbow == null || !elbow.IsValidObject || elbow.MEPModel?.ConnectorManager == null)
                throw new InvalidOperationException("彎頭不存在或沒有連接器");
            var ends = elbow.MEPModel.ConnectorManager.Connectors.Cast<Connector>()
                .Where(c => c.ConnectorType == ConnectorType.End).ToList();
            if (ends.Count != 2 || ends.Any(c => !c.IsConnected || CaptureExternalConnections(c).Count != 1) ||
                !((IsConnectedToCurve(ends[0], first) && IsConnectedToCurve(ends[1], second)) ||
                  (IsConnectedToCurve(ends[1], first) && IsConnectedToCurve(ends[0], second))))
                throw new InvalidOperationException($"彎頭 {elbow.Id} 未連接預期的兩個相鄰管線段");
        }

        private static bool IsConnectedToCurve(Connector connector, MEPCurve curve)
        {
            return curve.ConnectorManager.Connectors.Cast<Connector>()
                .Any(other => other.ConnectorType == ConnectorType.End && connector.IsConnectedTo(other));
        }

        private static void ValidateEndpoint(Document doc, Connector endpoint,
            List<ConnectorReference> expected)
        {
            foreach (var reference in expected)
                if (!endpoint.IsConnectedTo(reference.Resolve(doc)))
                    throw new InvalidOperationException("原有端點連接未恢復");
            if (CaptureExternalConnections(endpoint).Count != expected.Count)
                throw new InvalidOperationException("端點出現非預期連接");
        }

        private static ConnectorManager GetConnectorManager(Element element)
        {
            if (element is MEPCurve curve) return curve.ConnectorManager;
            if (element is FamilyInstance instance) return instance.MEPModel?.ConnectorManager;
            return null;
        }

        private static Connector RequireConnectorAtPoint(Element element, XYZ point)
        {
            Connector connector = GetConnectorManager(element)?.Connectors.Cast<Connector>()
                .Where(c => c.ConnectorType == ConnectorType.End)
                .OrderBy(c => c.Origin.DistanceTo(point))
                .FirstOrDefault(c => c.Origin.DistanceTo(point) <= ConnectorTolerance);
            if (connector == null)
                throw new InvalidOperationException($"元素 {element?.Id} 缺少預期的端點連接器");
            return connector;
        }
    }
}
