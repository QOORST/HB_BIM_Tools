using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class PipeSleeveOptions
    {
        public double ClearanceMm { get; set; } = 50.0;
        public bool IncludeCurrentModel { get; set; } = true;
        public bool IncludeLinks { get; set; } = true;
        public bool ExcludeAdditionElements { get; set; } = true;
        public bool UseDiameterSymbolMap { get; set; } = true;
        public Dictionary<int, ElementId> SleeveSymbolByDiameterMm { get; set; } = new Dictionary<int, ElementId>();
        public ElementId DefaultWallSleeveSymbolId { get; set; } = ElementId.InvalidElementId;
        public ElementId DefaultFloorSleeveSymbolId { get; set; } = ElementId.InvalidElementId;
        public bool AutoNumber { get; set; } = true;
        public bool SkipExisting { get; set; } = true;
        public bool LimitToActiveView { get; set; } = true;
        public ElementId ActiveViewId { get; set; } = ElementId.InvalidElementId;
    }

    internal sealed class PipeSleeveResult
    {
        public int PipeCount { get; set; }
        public int CandidateCount { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedExistingCount { get; set; }
        public int FailedCount { get; set; }
        public int OrganizedCount { get; set; }
        public bool NumberingOrganized { get; set; }
        public List<string> Messages { get; } = new List<string>();

        public string ToTaskDialogText()
        {
            string details = Messages.Count == 0
                ? string.Empty
                : "\n\n前幾筆訊息:\n" + string.Join("\n", Messages.Take(8));
            string numbering = NumberingOrganized
                ? $"\n整理後套管總數: {OrganizedCount}\n編號狀態: 已依樓層、系統、穿越與 DN 自動整理"
                : string.Empty;

            return $"處理管線: {PipeCount}\n" +
                   $"找到穿越點: {CandidateCount}\n" +
                   $"建立套管: {CreatedCount}\n" +
                   $"更新套管: {UpdatedCount}\n" +
                   $"略過既有: {SkippedExistingCount}\n" +
                   $"失敗: {FailedCount}" + numbering + details;
        }
    }

    internal sealed class PipeSleeveCandidate
    {
        public Element Pipe { get; set; }
        public Element HostElement { get; set; }
        public string HostType { get; set; }
        public XYZ Point { get; set; }
        public XYZ Direction { get; set; }
        public XYZ SleeveDirection { get; set; }
        public double PipeDiameterFeet { get; set; }
        public int PipeNominalDiameterMm { get; set; }
        public bool IsRectangularDuct { get; set; }
        public double DuctWidthFeet { get; set; }
        public double DuctHeightFeet { get; set; }
        public double HostThicknessFeet { get; set; }
        public bool IsFromLink { get; set; }
        public Transform LinkTransform { get; set; }
        public string BeamRiskLevel { get; set; } = string.Empty;
        public string BeamRiskNote { get; set; } = string.Empty;
        public string CheckNote { get; set; } = string.Empty;
    }

    internal static class PipeSleeveService
    {
        private const double MmToFeet = 1.0 / 304.8;

        public static PipeSleeveResult CreateSleeves(Document doc, IList<Element> pipes, PipeSleeveOptions options)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (pipes == null) throw new ArgumentNullException(nameof(pipes));
            options = options ?? new PipeSleeveOptions();

            EnsureDefaultFamiliesLoaded(doc);
            doc.Regenerate();

            FamilySymbol wallSymbol = FindSleeveSymbol(doc, preferWall: true);

            FamilySymbol floorSymbol = FindSleeveSymbol(doc, preferWall: false);

            FamilySymbol openingSymbol = FindOpeningSymbol(doc);
            if (wallSymbol == null && floorSymbol == null && openingSymbol == null)
            {
                throw new InvalidOperationException("找不到可用的套管族型。請先載入 Generic Model 或 Pipe Accessory 類別，名稱包含 Sleeve、套管、預留孔、開孔或孔洞的族型。");
            }

            var result = new PipeSleeveResult { PipeCount = pipes.Count };
            var candidates = SuppressWallSleevesCoveredByBeams(Analyze(doc, pipes, options)).ToList();
            result.CandidateCount = candidates.Count;
            EvaluateBeamOpeningPrinciples(candidates, options.ClearanceMm * MmToFeet, result);

            if (!options.SkipExisting)
            {
                int removedStale = DeleteStaleSleevesForPipes(doc, pipes, candidates, options);
                if (removedStale > 0)
                {
                    result.Messages.Add($"已清理不再穿越或位於洞口的舊套管: {removedStale} 個。");
                }
            }

            if (candidates.Count == 0)
            {
                result.Messages.Add("未找到管線與牆、樓板或樑的穿越點。");
                return result;
            }

            if (wallSymbol != null && !wallSymbol.IsActive) wallSymbol.Activate();
            if (floorSymbol != null && !floorSymbol.IsActive) floorSymbol.Activate();
            if (openingSymbol != null && !openingSymbol.IsActive) openingSymbol.Activate();
            doc.Regenerate();

            int sleeveNumber = GetNextSleeveNumber(doc);
            var updatedSleeveIds = new HashSet<long>();
            foreach (PipeSleeveCandidate candidate in candidates)
            {
                try
                {
                    if (options.SkipExisting && HasExistingSleeveNear(doc, candidate))
                    {
                        result.SkippedExistingCount++;
                        continue;
                    }

                    FamilySymbol symbol = ResolveSleeveSymbol(doc, candidate, options, wallSymbol, floorSymbol, openingSymbol);

                    if (symbol == null)
                    {
                        result.FailedCount++;
                        result.Messages.Add($"缺少 {candidate.HostType} 可用套管族型，略過管線 {candidate.Pipe.Id}。");
                        continue;
                    }

                    EnsureSymbolActive(doc, symbol);
                    if (!options.SkipExisting)
                    {
                        FamilyInstance existingSleeve = FindExistingSleeveForUpdate(doc, candidate, updatedSleeveIds);
                        if (existingSleeve != null)
                        {
                            UpdateExistingSleeve(doc, existingSleeve, candidate, symbol, options.ClearanceMm * MmToFeet, options);
                            updatedSleeveIds.Add(existingSleeve.Id.GetIdValue());
                            result.UpdatedCount++;
                            continue;
                        }
                    }

                    FamilyInstance sleeve = PlaceSleeve(doc, candidate, symbol);
                    if (sleeve == null)
                    {
                        result.FailedCount++;
                        result.Messages.Add($"套管建立失敗: 管線 {candidate.Pipe.Id}, {candidate.HostType}");
                        continue;
                    }

                    doc.Regenerate();
                    MoveSleeveToPoint(doc, sleeve, candidate.Point);
                    SetSleeveParameters(sleeve, candidate, options.ClearanceMm * MmToFeet, options.AutoNumber ? $"PS-{sleeveNumber:D3}" : null);
                    AlignSleeveToDirection(doc, sleeve, candidate.Point, candidate.SleeveDirection ?? candidate.Direction);
                    SetSleeveLevelAndOffset(doc, sleeve, candidate.Point);
                    MoveSleeveToPoint(doc, sleeve, candidate.Point);
                    MoveSleeveGeometryCenterToPoint(doc, sleeve, candidate.Point);
                    ApplyBeamRiskViewOverride(doc, sleeve, candidate, options);
                    result.CreatedCount++;
                    sleeveNumber++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Messages.Add($"管線 {candidate.Pipe.Id}: {ex.Message}");
                }
            }

            if (options.AutoNumber && (result.CreatedCount > 0 || result.UpdatedCount > 0))
            {
                result.OrganizedCount = OrganizeSleeveNumbers(doc, options);
                result.NumberingOrganized = result.OrganizedCount > 0;
            }

            return result;
        }

        public static int OrganizeSleeveNumbers(Document doc, PipeSleeveOptions options)
        {
            if (doc == null || !doc.IsModifiable)
            {
                return 0;
            }

            options = options ?? new PipeSleeveOptions();

            List<FamilyInstance> sleeves = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfType<FamilyInstance>()
                .Where(IsAutoGeneratedSleeveInstance)
                .Where(fi => IsSleeveLocationInActiveViewScope(doc, options, fi))
                .OrderBy(fi => GetSleeveLevelName(doc, fi))
                .ThenBy(fi => GetSleeveSystemName(fi))
                .ThenBy(fi => ReadSleeveCrossing(fi))
                .ThenBy(fi => ReadString(fi, "管道標稱直徑", "Nominal Diameter"))
                .ThenBy(fi => fi.Id.GetIdValue())
                .ToList();

            int number = 1;
            foreach (FamilyInstance sleeve in sleeves)
            {
                SetString(sleeve, $"PS-{number:D3}", BuiltInParameter.ALL_MODEL_MARK, "套管編號", "編號", "Sleeve Number");

                string levelName = GetSleeveLevelName(doc, sleeve);
                if (!string.IsNullOrWhiteSpace(levelName))
                {
                    SetString(sleeve, levelName, "套管樓層", "樓層名稱", "Level Name", "Reference Level Name", "參考樓層名稱", "所屬樓層名稱");
                }

                number++;
            }

            return sleeves.Count;
        }

        private static int DeleteStaleSleevesForPipes(Document doc, IList<Element> pipes, IList<PipeSleeveCandidate> candidates, PipeSleeveOptions options)
        {
            if (doc == null || pipes == null || options == null || !HasActiveViewSpatialScope(doc, options))
            {
                return 0;
            }

            var sourceIds = new HashSet<string>(pipes
                .Where(p => p != null)
                .Select(p => p.Id.GetIdValue().ToString()), StringComparer.OrdinalIgnoreCase);

            if (sourceIds.Count == 0)
            {
                return 0;
            }

            List<ElementId> staleIds = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfType<FamilyInstance>()
                .Where(fi => IsSleeveInstance(fi))
                .Where(fi => sourceIds.Contains(ReadString(fi, "來源管線Id", "Pipe Id", "Source Pipe Id")))
                .Where(fi => IsSleeveLocationInActiveViewScope(doc, options, fi))
                .Where(fi => !MatchesAnyCandidate(fi, candidates))
                .Select(fi => fi.Id)
                .ToList();

            if (staleIds.Count == 0)
            {
                return 0;
            }

            doc.Delete(staleIds);
            return staleIds.Count;
        }

        private static bool HasActiveViewSpatialScope(Document doc, PipeSleeveOptions options)
        {
            if (doc == null || options == null || !options.LimitToActiveView) return false;
            if (options.ActiveViewId == null || options.ActiveViewId == ElementId.InvalidElementId) return false;
            View view = doc.GetElement(options.ActiveViewId) as View;
            View3D view3D = view as View3D;
            return (view3D != null && view3D.IsSectionBoxActive) || (view != null && view.CropBoxActive);
        }

        private static bool IsSleeveLocationInActiveViewScope(Document doc, PipeSleeveOptions options, FamilyInstance sleeve)
        {
            LocationPoint location = sleeve?.Location as LocationPoint;
            XYZ point = location?.Point;
            if (point == null)
            {
                BoundingBoxXYZ box = sleeve?.get_BoundingBox(null);
                if (box == null) return false;
                point = (box.Min + box.Max) * 0.5;
            }

            return IsPointInActiveViewScope(doc, options, point);
        }

        private static bool MatchesAnyCandidate(FamilyInstance sleeve, IEnumerable<PipeSleeveCandidate> candidates)
        {
            LocationPoint location = sleeve?.Location as LocationPoint;
            XYZ sleevePoint = location?.Point;
            if (sleevePoint == null) return false;

            string sourceId = ReadString(sleeve, "來源管線Id", "Pipe Id", "Source Pipe Id");
            string crossing = ReadSleeveCrossing(sleeve);

            return (candidates ?? Enumerable.Empty<PipeSleeveCandidate>()).Any(candidate =>
            {
                if (candidate?.Pipe == null || candidate.Point == null) return false;
                string candidateSourceId = candidate.Pipe.Id.GetIdValue().ToString();
                if (!string.Equals(sourceId, candidateSourceId, StringComparison.OrdinalIgnoreCase)) return false;

                string candidateCrossing = GetCrossingTypeText(candidate.HostType);
                if (!TextContains(crossing, candidateCrossing)) return false;

                return sleevePoint.DistanceTo(candidate.Point) <= GetExistingSleeveTolerance(candidate);
            });
        }
        private static IEnumerable<PipeSleeveCandidate> Analyze(Document doc, IList<Element> pipes, PipeSleeveOptions options)
        {
            Outline searchOutline = CreatePipeSearchOutline(pipes, 500.0 * MmToFeet);
            var hosts = options.IncludeCurrentModel ? CollectHostElements(doc, searchOutline).ToList() : new List<Element>();
            var links = options.IncludeLinks ? CollectLinkHosts(doc, searchOutline).ToList() : new List<Tuple<Element, Document, Transform, string>>();
            var candidatesByKey = new Dictionary<string, PipeSleeveCandidate>();

            foreach (Element pipe in pipes.Where(IsSupportedCurve))
            {
                Curve curve = ((LocationCurve)pipe.Location).Curve;
                XYZ direction = GetCurveDirection(curve);
                double ductWidth;
                double ductHeight;
                bool isRectangularDuct = TryGetRectangularDuctSizeFeet(pipe, out ductWidth, out ductHeight);
                double diameter = isRectangularDuct ? Math.Sqrt(ductWidth * ductHeight) : GetPipeDiameterFeet(pipe);
                int nominalDiameterMm = ToNominalDiameterMm(diameter);

                foreach (Element host in hosts)
                {
                    string hostType = GetHostType(host);
                    if (string.IsNullOrWhiteSpace(hostType)) continue;
                    if (ShouldExcludeHostByAdditionRule(options, host, hostType)) continue;
                    if (!ShouldCreate(direction, hostType)) continue;

                    XYZ point;
                    double hostThickness;
                    if (!TryGetIntersectionData(curve, host, hostType, out point, out hostThickness)) continue;

                    if (!IsPointInActiveViewScope(doc, options, point)) continue;

                    AddOrMergeCandidate(candidatesByKey, new PipeSleeveCandidate
                    {
                        Pipe = pipe,
                        HostElement = host,
                        HostType = hostType,
                        Point = point,
                        Direction = direction,
                        SleeveDirection = GetSleeveDirection(host, hostType, direction, null),
                        PipeDiameterFeet = diameter,
                        PipeNominalDiameterMm = nominalDiameterMm,
                        IsRectangularDuct = isRectangularDuct,
                        DuctWidthFeet = ductWidth,
                        DuctHeightFeet = ductHeight,
                        HostThicknessFeet = hostThickness,
                        IsFromLink = false
                    });
                }

                foreach (var linkHost in links)
                {
                    Element host = linkHost.Item1;
                    Transform linkTransform = linkHost.Item3;
                    string hostType = linkHost.Item4;
                    if (string.IsNullOrWhiteSpace(hostType)) continue;
                    if (ShouldExcludeHostByAdditionRule(options, host, hostType)) continue;
                    if (!ShouldCreate(direction, hostType)) continue;

                    Curve curveInLink = curve.CreateTransformed(linkTransform.Inverse);
                    XYZ pointInLink;
                    double hostThickness;
                    if (!TryGetIntersectionData(curveInLink, host, hostType, out pointInLink, out hostThickness)) continue;

                    XYZ point = linkTransform.OfPoint(pointInLink);
                    if (!IsPointInActiveViewScope(doc, options, point)) continue;
                    AddOrMergeCandidate(candidatesByKey, new PipeSleeveCandidate
                    {
                        Pipe = pipe,
                        HostElement = host,
                        HostType = hostType,
                        Point = point,
                        Direction = direction,
                        SleeveDirection = GetSleeveDirection(host, hostType, direction, linkTransform),
                        PipeDiameterFeet = diameter,
                        PipeNominalDiameterMm = nominalDiameterMm,
                        IsRectangularDuct = isRectangularDuct,
                        DuctWidthFeet = ductWidth,
                        DuctHeightFeet = ductHeight,
                        HostThicknessFeet = hostThickness,
                        IsFromLink = true,
                        LinkTransform = linkTransform
                    });
                }
            }

            return candidatesByKey.Values;
        }

        private static bool IsPointInActiveViewScope(Document doc, PipeSleeveOptions options, XYZ point)
        {
            if (doc == null || options == null || !options.LimitToActiveView) return true;
            ElementId viewId = options.ActiveViewId;
            if (viewId == null || viewId == ElementId.InvalidElementId) return true;

            View view = doc.GetElement(viewId) as View;
            if (view == null || view.IsTemplate) return true;

            if (view is View3D view3D && view3D.IsSectionBoxActive)
            {
                return IsPointInsideBoundingBox(point, view3D.GetSectionBox(), 1.0 * MmToFeet);
            }

            if (view.CropBoxActive)
            {
                return IsPointInsideBoundingBox(point, view.CropBox, 1.0 * MmToFeet);
            }

            return true;
        }

        private static bool IsPointInsideBoundingBox(XYZ point, BoundingBoxXYZ box, double tolerance)
        {
            if (point == null || box == null) return true;
            Transform transform = box.Transform ?? Transform.Identity;
            XYZ localPoint = transform.Inverse.OfPoint(point);
            return localPoint.X >= box.Min.X - tolerance && localPoint.X <= box.Max.X + tolerance
                && localPoint.Y >= box.Min.Y - tolerance && localPoint.Y <= box.Max.Y + tolerance
                && localPoint.Z >= box.Min.Z - tolerance && localPoint.Z <= box.Max.Z + tolerance;
        }
        private static IEnumerable<PipeSleeveCandidate> SuppressWallSleevesCoveredByBeams(IEnumerable<PipeSleeveCandidate> candidates)
        {
            List<PipeSleeveCandidate> items = candidates?.ToList() ?? new List<PipeSleeveCandidate>();
            List<PipeSleeveCandidate> beams = items.Where(c => c.HostType == "Beam").ToList();
            if (beams.Count == 0)
            {
                return items;
            }

            var suppressedWalls = new HashSet<PipeSleeveCandidate>();
            foreach (PipeSleeveCandidate wall in items.Where(c => c.HostType == "Wall"))
            {
                PipeSleeveCandidate beam;
                if (TryMergeWallSleeveIntoBeam(wall, beams, out beam))
                {
                    suppressedWalls.Add(wall);
                }
            }

            return items.Where(candidate => !suppressedWalls.Contains(candidate));
        }

        private static bool TryMergeWallSleeveIntoBeam(PipeSleeveCandidate wallCandidate, List<PipeSleeveCandidate> beamCandidates, out PipeSleeveCandidate mergedBeam)
        {
            mergedBeam = null;
            if (wallCandidate == null || wallCandidate.HostType != "Wall" || wallCandidate.Pipe == null || wallCandidate.Point == null || wallCandidate.Direction == null)
            {
                return false;
            }

            XYZ pipeDirection = wallCandidate.Direction.Normalize();
            double perpendicularTolerance = 150.0 * MmToFeet;
            double jointTolerance = 100.0 * MmToFeet;

            foreach (PipeSleeveCandidate beamCandidate in beamCandidates)
            {
                if (beamCandidate == null || beamCandidate.Pipe == null || beamCandidate.Point == null)
                {
                    continue;
                }

                if (beamCandidate.Pipe.Id.GetIdValue() != wallCandidate.Pipe.Id.GetIdValue())
                {
                    continue;
                }

                XYZ delta = wallCandidate.Point - beamCandidate.Point;
                double perpendicularDistance = delta.CrossProduct(pipeDirection).GetLength();
                if (perpendicularDistance > perpendicularTolerance)
                {
                    continue;
                }

                double beamHalf = Math.Max(beamCandidate.HostThicknessFeet, 0) * 0.5;
                double wallHalf = Math.Max(wallCandidate.HostThicknessFeet, 0) * 0.5;
                double wallStation = delta.DotProduct(pipeDirection);
                double mergeLimit = beamHalf + wallHalf + jointTolerance;
                mergeLimit = Math.Max(mergeLimit, 250.0 * MmToFeet);
                if (Math.Abs(wallStation) > mergeLimit)
                {
                    continue;
                }

                double beamMin = -beamHalf;
                double beamMax = beamHalf;
                double wallMin = wallStation - wallHalf;
                double wallMax = wallStation + wallHalf;
                double unionMin = Math.Min(beamMin, wallMin);
                double unionMax = Math.Max(beamMax, wallMax);
                double unionLength = unionMax - unionMin;
                double unionCenterStation = (unionMin + unionMax) * 0.5;

                if (unionLength > beamCandidate.HostThicknessFeet)
                {
                    beamCandidate.HostThicknessFeet = unionLength;
                    beamCandidate.Point = beamCandidate.Point + pipeDirection.Multiply(unionCenterStation);
                }

                beamCandidate.CheckNote = AppendNote(beamCandidate.CheckNote, "梁牆接合: 已合併牆厚，僅建立穿梁套管");
                mergedBeam = beamCandidate;
                return true;
            }

            return false;
        }

        private static string AppendNote(string existing, string note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return existing ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(existing))
            {
                return note;
            }

            if (existing.IndexOf(note, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return existing;
            }

            return existing + "；" + note;
        }
        private static void EvaluateBeamOpeningPrinciples(List<PipeSleeveCandidate> candidates, double clearanceFeet, PipeSleeveResult result)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            List<PipeSleeveCandidate> beamCandidates = candidates
                .Where(c => c.HostType == "Beam" && c.Point != null)
                .ToList();
            if (beamCandidates.Count == 0)
            {
                return;
            }

            foreach (PipeSleeveCandidate candidate in beamCandidates)
            {
                double openingDiameter = GetOpeningDiameterFeet(candidate, clearanceFeet);
                double beamDepth = EstimateBeamDepthFeet(candidate.HostElement, candidate.LinkTransform);
                var notes = new List<string>();
                string risk = "一般";

                if (openingDiameter >= 200.0 * MmToFeet)
                {
                    risk = MaxBeamRisk(risk, "需結構確認");
                    notes.Add($"孔徑 {ToMm(openingDiameter):F0}mm >= 200mm");
                }
                else if (openingDiameter >= 100.0 * MmToFeet)
                {
                    risk = MaxBeamRisk(risk, "提醒");
                    notes.Add($"孔徑 {ToMm(openingDiameter):F0}mm，建議確認補強需求");
                }

                if (beamDepth > 0 && openingDiameter > beamDepth / 3.0)
                {
                    risk = MaxBeamRisk(risk, "高風險");
                    notes.Add($"孔徑 {ToMm(openingDiameter):F0}mm > 梁深 1/3 ({ToMm(beamDepth / 3.0):F0}mm)");
                }

                if (beamDepth > 0 && TryGetDistanceToBeamEndFeet(candidate, out double endDistanceFeet))
                {
                    double noOpeningZone = beamDepth * 2.0;
                    if (endDistanceFeet < noOpeningZone)
                    {
                        risk = MaxBeamRisk(risk, "高風險");
                        notes.Add($"距梁端/柱邊 {ToMm(endDistanceFeet):F0}mm < 2H ({ToMm(noOpeningZone):F0}mm)");
                    }
                }

                candidate.BeamRiskLevel = risk;
                candidate.BeamRiskNote = notes.Count == 0
                    ? "穿梁檢核: 一般"
                    : "穿梁檢核: " + risk + " - " + string.Join("；", notes);
            }

            foreach (IGrouping<string, PipeSleeveCandidate> group in beamCandidates.GroupBy(GetBeamCandidateGroupKey))
            {
                List<PipeSleeveCandidate> groupItems = group.ToList();
                for (int i = 0; i < groupItems.Count; i++)
                {
                    for (int j = i + 1; j < groupItems.Count; j++)
                    {
                        PipeSleeveCandidate a = groupItems[i];
                        PipeSleeveCandidate b = groupItems[j];
                        double distance = a.Point.DistanceTo(b.Point);
                        double required = Math.Max(
                            Math.Max(GetOpeningDiameterFeet(a, clearanceFeet), GetOpeningDiameterFeet(b, clearanceFeet)) * 3.0,
                            300.0 * MmToFeet);

                        if (distance < required)
                        {
                            string note = $"孔距 {ToMm(distance):F0}mm < 建議 {ToMm(required):F0}mm";
                            AppendBeamRisk(a, "需結構確認", note);
                            AppendBeamRisk(b, "需結構確認", note);
                        }
                    }
                }
            }

            int warningCount = beamCandidates.Count(c => !string.IsNullOrWhiteSpace(c.BeamRiskLevel) && c.BeamRiskLevel != "一般");
            if (warningCount > 0)
            {
                result?.Messages.Add($"穿梁檢核提醒: {warningCount} 個套管需留意孔徑、梁端距離或孔距，已寫入備註/穿梁檢核參數。");
            }
        }

        private static double GetOpeningDiameterFeet(PipeSleeveCandidate candidate, double clearanceFeet)
        {
            if (candidate == null)
            {
                return 0;
            }

            if (candidate.IsRectangularDuct)
            {
                return Math.Max(candidate.DuctWidthFeet, candidate.DuctHeightFeet) + clearanceFeet;
            }

            return candidate.PipeDiameterFeet + clearanceFeet;
        }

        private static double EstimateBeamDepthFeet(Element beam, Transform linkTransform)
        {
            BoundingBoxXYZ box = beam?.get_BoundingBox(null);
            if (box == null)
            {
                return 0;
            }

            XYZ min = linkTransform == null ? box.Min : linkTransform.OfPoint(box.Min);
            XYZ max = linkTransform == null ? box.Max : linkTransform.OfPoint(box.Max);
            return Math.Abs(max.Z - min.Z);
        }

        private static bool TryGetDistanceToBeamEndFeet(PipeSleeveCandidate candidate, out double distanceFeet)
        {
            distanceFeet = 0;
            LocationCurve locationCurve = candidate?.HostElement?.Location as LocationCurve;
            Curve curve = locationCurve?.Curve;
            if (curve == null || candidate.Point == null)
            {
                return false;
            }

            try
            {
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                if (candidate.LinkTransform != null)
                {
                    start = candidate.LinkTransform.OfPoint(start);
                    end = candidate.LinkTransform.OfPoint(end);
                }

                XYZ axis = end - start;
                double length = axis.GetLength();
                if (length < 1e-9)
                {
                    return false;
                }

                XYZ direction = axis / length;
                double along = (candidate.Point - start).DotProduct(direction);
                distanceFeet = Math.Min(Math.Abs(along), Math.Abs(length - along));
                return true;
            }
            catch
            {
                return false;
            }
        }
        private static string GetBeamCandidateGroupKey(PipeSleeveCandidate candidate)
        {
            string source = candidate.IsFromLink ? "L" : "M";
            string documentTitle = candidate.HostElement?.Document?.Title ?? string.Empty;
            long id = candidate.HostElement?.Id.GetIdValue() ?? -1;
            return source + ":" + documentTitle + ":" + id;
        }

        private static void AppendBeamRisk(PipeSleeveCandidate candidate, string risk, string note)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(note))
            {
                return;
            }

            candidate.BeamRiskLevel = MaxBeamRisk(candidate.BeamRiskLevel, risk);
            if (string.IsNullOrWhiteSpace(candidate.BeamRiskNote) || candidate.BeamRiskNote == "穿梁檢核: 一般")
            {
                candidate.BeamRiskNote = "穿梁檢核: " + candidate.BeamRiskLevel + " - " + note;
                return;
            }

            if (candidate.BeamRiskNote.IndexOf(note, StringComparison.OrdinalIgnoreCase) < 0)
            {
                candidate.BeamRiskNote += "；" + note;
            }
        }

        private static string MaxBeamRisk(string current, string next)
        {
            return GetBeamRiskRank(next) > GetBeamRiskRank(current) ? next : (string.IsNullOrWhiteSpace(current) ? next : current);
        }

        private static int GetBeamRiskRank(string risk)
        {
            if (risk == "高風險") return 3;
            if (risk == "需結構確認") return 2;
            if (risk == "提醒") return 1;
            return 0;
        }

        private static double ToMm(double feet)
        {
            return feet / MmToFeet;
        }
        private static bool ShouldExcludeHostByAdditionRule(PipeSleeveOptions options, Element host, string hostType)
        {
            return options.ExcludeAdditionElements && hostType != "Beam" && IsAdditionOrStrengtheningElement(host);
        }

        private static void AddOrMergeCandidate(Dictionary<string, PipeSleeveCandidate> candidatesByKey, PipeSleeveCandidate candidate)
        {
            if (candidate.HostType == "Beam")
            {
                PipeSleeveCandidate beamCandidate = candidatesByKey.Values.FirstOrDefault(existing => CanMergeBeamCandidates(existing, candidate));
                if (beamCandidate != null)
                {
                    MergeBeamCandidate(beamCandidate, candidate);
                    return;
                }
            }

            string key = MakeKey(candidate.Pipe.Id, candidate.HostType, candidate.Point);
            PipeSleeveCandidate existingByKey;
            if (!candidatesByKey.TryGetValue(key, out existingByKey))
            {
                candidatesByKey[key] = candidate;
                return;
            }

            if (candidate.HostThicknessFeet > existingByKey.HostThicknessFeet)
            {
                existingByKey.HostThicknessFeet = candidate.HostThicknessFeet;
                existingByKey.Point = candidate.Point;
                existingByKey.HostElement = candidate.HostElement;
                existingByKey.IsFromLink = candidate.IsFromLink;
                existingByKey.LinkTransform = candidate.LinkTransform;
            }
        }

        private static bool CanMergeBeamCandidates(PipeSleeveCandidate existing, PipeSleeveCandidate candidate)
        {
            if (existing == null || candidate == null || existing.HostType != "Beam" || candidate.HostType != "Beam")
            {
                return false;
            }

            if (existing.Pipe.Id.GetIdValue() != candidate.Pipe.Id.GetIdValue() || existing.Direction == null || candidate.Direction == null)
            {
                return false;
            }

            XYZ direction = existing.Direction.Normalize();
            XYZ delta = candidate.Point - existing.Point;
            double perpendicularDistance = delta.CrossProduct(direction).GetLength();
            double alongDistance = Math.Abs(delta.DotProduct(direction));
            double tolerance = 150.0 * MmToFeet;
            double mergeLimit = (existing.HostThicknessFeet + candidate.HostThicknessFeet) * 0.5 + tolerance;

            return perpendicularDistance <= tolerance && alongDistance <= mergeLimit;
        }

        private static void MergeBeamCandidate(PipeSleeveCandidate existing, PipeSleeveCandidate candidate)
        {
            XYZ direction = existing.Direction.Normalize();
            double station = (candidate.Point - existing.Point).DotProduct(direction);
            double existingHalf = existing.HostThicknessFeet * 0.5;
            double candidateHalf = candidate.HostThicknessFeet * 0.5;
            double min = Math.Min(-existingHalf, station - candidateHalf);
            double max = Math.Max(existingHalf, station + candidateHalf);
            double center = (min + max) * 0.5;

            existing.Point = existing.Point + direction * center;
            existing.HostThicknessFeet = Math.Max(0, max - min);
        }
        private static bool IsAdditionOrStrengtheningElement(Element element)
        {
            if (element == null)
            {
                return false;
            }

            string text = GetElementSearchText(element);
            string[] tokens =
            {
                "增築", "增建", "增打", "補強", "补强", "加厚", "擴建", "扩建",
                "addition", "strengthen", "strengthening", "reinforce", "reinforcement"
            };

            return ContainsAny(text, tokens);
        }

        private static string GetElementSearchText(Element element)
        {
            var parts = new List<string>
            {
                element.Name ?? string.Empty,
                element.Category?.Name ?? string.Empty
            };

            ElementType type = element.Document?.GetElement(element.GetTypeId()) as ElementType;
            if (type != null)
            {
                parts.Add(type.Name ?? string.Empty);
                parts.Add(type.FamilyName ?? string.Empty);
            }

            AddParameterText(parts, element, BuiltInParameter.ALL_MODEL_MARK);
            AddParameterText(parts, element, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            AddParameterText(parts, element, BuiltInParameter.PHASE_CREATED);
            AddParameterText(parts, element, BuiltInParameter.PHASE_DEMOLISHED);
            if (type != null)
            {
                AddParameterText(parts, type, BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
            }

            string[] names = { "備註", "Comments", "註解", "階段", "Phase", "施工區", "區域", "Zone", "工項", "狀態", "Status", "IFC 預先定義的類型", "IFC 類型", "IFCExportAs", "IfcExportAs", "IfcName", "ObjectType", "類型名稱", "Type Name" };
            foreach (string name in names)
            {
                AddParameterText(parts, element, name);
                if (type != null)
                {
                    AddParameterText(parts, type, name);
                }
            }

            return string.Join(" ", parts).ToLowerInvariant();
        }

        private static void AddParameterText(List<string> parts, Element element, BuiltInParameter builtInParameter)
        {
            try
            {
                Parameter parameter = element?.get_Parameter(builtInParameter);
                AddParameterText(parts, parameter);
            }
            catch
            {
                // Some built-in parameters are not valid for every element/type.
            }
        }

        private static void AddParameterText(List<string> parts, Element element, string parameterName)
        {
            Parameter parameter = element?.LookupParameter(parameterName);
            AddParameterText(parts, parameter);
        }

        private static void AddParameterText(List<string> parts, Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue)
            {
                return;
            }

            string value = parameter.AsValueString();
            if (string.IsNullOrWhiteSpace(value) && parameter.StorageType == StorageType.String)
            {
                value = parameter.AsString();
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
        }
        private static Outline CreatePipeSearchOutline(IList<Element> pipes, double paddingFeet)
        {
            XYZ min = null;
            XYZ max = null;

            foreach (Element pipe in pipes ?? new List<Element>())
            {
                BoundingBoxXYZ box = pipe?.get_BoundingBox(null);
                if (box != null)
                {
                    ExpandBounds(box.Min, ref min, ref max);
                    ExpandBounds(box.Max, ref min, ref max);
                    continue;
                }

                LocationCurve locationCurve = pipe?.Location as LocationCurve;
                if (locationCurve?.Curve == null) continue;

                try
                {
                    foreach (XYZ point in locationCurve.Curve.Tessellate())
                    {
                        ExpandBounds(point, ref min, ref max);
                    }
                }
                catch
                {
                    ExpandBounds(locationCurve.Curve.GetEndPoint(0), ref min, ref max);
                    ExpandBounds(locationCurve.Curve.GetEndPoint(1), ref min, ref max);
                }
            }

            if (min == null || max == null)
            {
                return null;
            }

            XYZ padding = new XYZ(paddingFeet, paddingFeet, paddingFeet);
            return new Outline(min - padding, max + padding);
        }

        private static void ExpandBounds(XYZ point, ref XYZ min, ref XYZ max)
        {
            if (point == null) return;

            if (min == null || max == null)
            {
                min = point;
                max = point;
                return;
            }

            min = new XYZ(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new XYZ(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }

        private static BoundingBoxIntersectsFilter CreateOutlineFilter(Outline outline)
        {
            return outline == null ? null : new BoundingBoxIntersectsFilter(outline);
        }

        private static FilteredElementCollector ApplyOutlineFilter(FilteredElementCollector collector, Outline outline)
        {
            BoundingBoxIntersectsFilter filter = CreateOutlineFilter(outline);
            return filter == null ? collector : collector.WherePasses(filter);
        }

        private static Outline TransformOutline(Outline outline, Transform transform)
        {
            if (outline == null || transform == null) return outline;

            XYZ min = outline.MinimumPoint;
            XYZ max = outline.MaximumPoint;
            XYZ resultMin = null;
            XYZ resultMax = null;
            foreach (XYZ corner in GetOutlineCorners(min, max))
            {
                ExpandBounds(transform.OfPoint(corner), ref resultMin, ref resultMax);
            }

            return resultMin == null || resultMax == null ? null : new Outline(resultMin, resultMax);
        }

        private static IEnumerable<XYZ> GetOutlineCorners(XYZ min, XYZ max)
        {
            yield return new XYZ(min.X, min.Y, min.Z);
            yield return new XYZ(min.X, min.Y, max.Z);
            yield return new XYZ(min.X, max.Y, min.Z);
            yield return new XYZ(min.X, max.Y, max.Z);
            yield return new XYZ(max.X, min.Y, min.Z);
            yield return new XYZ(max.X, min.Y, max.Z);
            yield return new XYZ(max.X, max.Y, min.Z);
            yield return new XYZ(max.X, max.Y, max.Z);
        }

        private static IEnumerable<Element> CollectHostElements(Document doc, Outline searchOutline)
        {
            foreach (Wall wall in ApplyOutlineFilter(new FilteredElementCollector(doc), searchOutline).OfClass(typeof(Wall)).Cast<Wall>()) yield return wall;
            foreach (Floor floor in ApplyOutlineFilter(new FilteredElementCollector(doc), searchOutline).OfClass(typeof(Floor)).Cast<Floor>()) yield return floor;
            foreach (FamilyInstance beam in ApplyOutlineFilter(new FilteredElementCollector(doc), searchOutline)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()) yield return beam;
            foreach (Element host in CollectRecognizedHostLikeElements(doc, searchOutline)) yield return host;
        }

        private static IEnumerable<Tuple<Element, Document, Transform, string>> CollectLinkHosts(Document doc, Outline searchOutline)
        {
            foreach (RevitLinkInstance link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) continue;
                Transform transform = link.GetTotalTransform();
                Outline linkOutline = TransformOutline(searchOutline, transform.Inverse);

                foreach (Wall wall in ApplyOutlineFilter(new FilteredElementCollector(linkDoc), linkOutline).OfClass(typeof(Wall)).Cast<Wall>())
                    yield return Tuple.Create((Element)wall, linkDoc, transform, "Wall");
                foreach (Floor floor in ApplyOutlineFilter(new FilteredElementCollector(linkDoc), linkOutline).OfClass(typeof(Floor)).Cast<Floor>())
                    yield return Tuple.Create((Element)floor, linkDoc, transform, "Floor");
                foreach (FamilyInstance beam in ApplyOutlineFilter(new FilteredElementCollector(linkDoc), linkOutline)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                    yield return Tuple.Create((Element)beam, linkDoc, transform, "Beam");
                foreach (Element host in CollectRecognizedHostLikeElements(linkDoc, linkOutline))
                {
                    string hostType = GetHostType(host);
                    if (!string.IsNullOrWhiteSpace(hostType))
                    {
                        yield return Tuple.Create(host, linkDoc, transform, hostType);
                    }
                }
            }
        }

        private static IEnumerable<Element> CollectRecognizedHostLikeElements(Document doc, Outline searchOutline)
        {
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_GenericModel
            };

            ElementMulticategoryFilter categoryFilter = new ElementMulticategoryFilter(categories);
            foreach (Element element in new FilteredElementCollector(doc).WherePasses(categoryFilter).WhereElementIsNotElementType())
            {
                if (element is Wall || element is Floor)
                {
                    continue;
                }

                FamilyInstance familyInstance = element as FamilyInstance;
                if (familyInstance != null)
                {
                    if (IsSleeveInstance(familyInstance))
                    {
                        continue;
                    }

                    long categoryId = familyInstance.Category?.Id.GetIdValue() ?? 0;
                    if (categoryId == (long)BuiltInCategory.OST_StructuralFraming)
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(GetHostType(element)))
                {
                    yield return element;
                }
            }
        }

        private static bool TryGetIntersectionData(Curve curve, Element element, string hostType, out XYZ point, out double thicknessFeet)
        {
            point = null;
            thicknessFeet = 0;

            if (!MayCurveIntersectElementBox(curve, element))
            {
                return false;
            }

            Curve segment = GetIntersectionSegment(curve, element);
            if (segment != null)
            {
                if (!HasCompleteTraversal(curve, element, hostType))
                {
                    return false;
                }

                point = AdjustPointToHostCenter(element, hostType, (segment.GetEndPoint(0) + segment.GetEndPoint(1)) * 0.5);
                thicknessFeet = ResolveSleeveLengthFeet(element, hostType, segment.Length);
                return true;
            }

            // Native floors can contain shaft or slab openings. If the solid has no
            // curve intersection, falling back to the element bounding box would place
            // sleeves in the void area.
            if (hostType == "Floor" && element is Floor)
            {
                return false;
            }

            double sampledLength;
            if (!TryGetBoundingBoxIntersectionData(curve, element, out point, out sampledLength))
            {
                return false;
            }

            if (!HasCompleteTraversal(curve, element, hostType))
            {
                return false;
            }

            point = AdjustPointToHostCenter(element, hostType, point);
            thicknessFeet = ResolveSleeveLengthFeet(element, hostType, sampledLength);
            return true;
        }

        private static XYZ AdjustPointToHostCenter(Element host, string hostType, XYZ point)
        {
            if (host == null || point == null)
            {
                return point;
            }

            if (hostType == "Wall")
            {
                LocationCurve locationCurve = host.Location as LocationCurve;
                IntersectionResult projection = locationCurve?.Curve?.Project(point);
                if (projection != null)
                {
                    XYZ center = projection.XYZPoint;
                    return new XYZ(center.X, center.Y, point.Z);
                }
            }

            if (hostType == "Floor")
            {
                BoundingBoxXYZ box = host.get_BoundingBox(null);
                if (box != null)
                {
                    double centerZ = (box.Min.Z + box.Max.Z) * 0.5;
                    return new XYZ(point.X, point.Y, centerZ);
                }

                return point;
            }

            if (hostType == "Beam")
            {
                return point;
            }

            return point;
        }

        private static double ResolveSleeveLengthFeet(Element host, string hostType, double intersectionLengthFeet)
        {
            double hostThicknessFeet = ReadHostThicknessParameter(host, hostType);

            if (hostType == "Wall" || hostType == "Floor")
            {
                return hostThicknessFeet > 0 ? hostThicknessFeet : intersectionLengthFeet;
            }

            if (hostType == "Beam")
            {
                return intersectionLengthFeet > 0 ? intersectionLengthFeet : hostThicknessFeet;
            }

            return intersectionLengthFeet > 0 ? intersectionLengthFeet : hostThicknessFeet;
        }

        private static bool MayCurveIntersectElementBox(Curve curve, Element element)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || curve == null)
            {
                return false;
            }

            IList<XYZ> points;
            try
            {
                points = curve.Tessellate();
            }
            catch
            {
                points = new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) };
            }

            if (points == null || points.Count == 0)
            {
                return false;
            }

            XYZ min = points[0];
            XYZ max = points[0];
            foreach (XYZ point in points.Skip(1))
            {
                min = new XYZ(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
                max = new XYZ(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
            }

            double tolerance = 150.0 * MmToFeet;
            return max.X >= box.Min.X - tolerance && min.X <= box.Max.X + tolerance
                && max.Y >= box.Min.Y - tolerance && min.Y <= box.Max.Y + tolerance
                && max.Z >= box.Min.Z - tolerance && min.Z <= box.Max.Z + tolerance;
        }
        private static bool TryGetBoundingBoxIntersectionData(Curve curve, Element element, out XYZ point, out double thicknessFeet)
        {
            point = null;
            thicknessFeet = 0;
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
            {
                return false;
            }

            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            XYZ first = null;
            XYZ last = null;
            int steps = GetIntersectionSampleSteps(curve);
            double tolerance = 50.0 * MmToFeet;

            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                XYZ sample = EvaluateCurvePoint(curve, t, start, end);
                if (IsPointInsideBox(sample, box, tolerance))
                {
                    if (first == null)
                    {
                        first = sample;
                    }
                    last = sample;
                }
            }

            if (first == null || last == null)
            {
                return false;
            }

            point = (first + last) * 0.5;
            thicknessFeet = first.DistanceTo(last);
            return true;
        }

        private static bool HasCompleteTraversal(Curve curve, Element element, string hostType)
        {
            if (curve == null || element == null)
            {
                return false;
            }

            if (hostType == "Wall")
            {
                return HasCompleteWallTraversal(curve, element);
            }

            if (hostType == "Floor")
            {
                return HasCompleteFloorTraversal(curve, element);
            }

            return HasInsideRunWithOutsideBothSides(curve, element);
        }

        private static bool HasCompleteFloorTraversal(Curve curve, Element element)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return false;

            // Floor drains and toilet connections often terminate flush with the finished/top slab face.
            // Treat reaching both slab faces as a valid sleeve condition; do not require the pipe to protrude beyond them.
            double tolerance = 30.0 * MmToFeet;
            double minZ = double.MaxValue;
            double maxZ = double.MinValue;
            foreach (XYZ sample in SampleCurvePoints(curve))
            {
                minZ = Math.Min(minZ, sample.Z);
                maxZ = Math.Max(maxZ, sample.Z);
            }

            return minZ <= box.Min.Z + tolerance && maxZ >= box.Max.Z - tolerance;
        }

        private static bool HasCompleteWallTraversal(Curve curve, Element element)
        {
            LocationCurve locationCurve = element.Location as LocationCurve;
            double thickness = ReadHostThicknessParameter(element, "Wall");
            double tolerance = 5.0 * MmToFeet;

            if (locationCurve?.Curve != null && thickness > 0)
            {
                XYZ wallDirection = GetCurveDirection(locationCurve.Curve);
                XYZ normal = new XYZ(-wallDirection.Y, wallDirection.X, 0);
                if (!TryNormalize(normal, out normal))
                {
                    return HasCompleteBoxAxisTraversal(curve, element, preferVertical: false);
                }

                double halfThickness = thickness * 0.5;
                double minDistance = double.MaxValue;
                double maxDistance = double.MinValue;
                foreach (XYZ sample in SampleCurvePoints(curve))
                {
                    IntersectionResult projection = locationCurve.Curve.Project(sample);
                    if (projection == null) continue;

                    double signedDistance = (sample - projection.XYZPoint).DotProduct(normal);
                    minDistance = Math.Min(minDistance, signedDistance);
                    maxDistance = Math.Max(maxDistance, signedDistance);
                }

                return minDistance <= -halfThickness + tolerance && maxDistance >= halfThickness - tolerance;
            }

            return HasCompleteBoxAxisTraversal(curve, element, preferVertical: false);
        }

        private static bool HasInsideRunWithOutsideBothSides(Curve curve, Element element)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return false;

            int firstInside = -1;
            int lastInside = -1;
            List<XYZ> samples = SampleCurvePoints(curve).ToList();
            double tolerance = 5.0 * MmToFeet;
            for (int i = 0; i < samples.Count; i++)
            {
                if (IsPointInsideBox(samples[i], box, tolerance))
                {
                    if (firstInside < 0) firstInside = i;
                    lastInside = i;
                }
            }

            return firstInside > 0 && lastInside >= firstInside && lastInside < samples.Count - 1;
        }

        private static bool HasCompleteBoxAxisTraversal(Curve curve, Element element, bool preferVertical)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return false;

            List<XYZ> samples = SampleCurvePoints(curve).ToList();
            if (samples.Count == 0) return false;

            double tolerance = 5.0 * MmToFeet;
            double sizeX = box.Max.X - box.Min.X;
            double sizeY = box.Max.Y - box.Min.Y;
            double sizeZ = box.Max.Z - box.Min.Z;

            if (preferVertical || sizeZ <= sizeX && sizeZ <= sizeY)
            {
                return samples.Min(p => p.Z) <= box.Min.Z + tolerance && samples.Max(p => p.Z) >= box.Max.Z - tolerance;
            }

            if (sizeX <= sizeY)
            {
                return samples.Min(p => p.X) <= box.Min.X + tolerance && samples.Max(p => p.X) >= box.Max.X - tolerance;
            }

            return samples.Min(p => p.Y) <= box.Min.Y + tolerance && samples.Max(p => p.Y) >= box.Max.Y - tolerance;
        }

        private static IEnumerable<XYZ> SampleCurvePoints(Curve curve)
        {
            if (curve == null) yield break;

            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            int steps = GetIntersectionSampleSteps(curve);
            for (int i = 0; i <= steps; i++)
            {
                yield return EvaluateCurvePoint(curve, (double)i / steps, start, end);
            }
        }


        private static bool TryNormalize(XYZ vector, out XYZ normalized)
        {
            normalized = null;
            if (vector == null || vector.GetLength() < 1e-9)
            {
                return false;
            }

            normalized = vector.Normalize();
            return true;
        }
        private static int GetIntersectionSampleSteps(Curve curve)
        {
            const int minimumSteps = 160;
            const int maximumSteps = 3000;
            double targetSpacing = 25.0 * MmToFeet;

            try
            {
                double length = curve?.Length ?? 0;
                if (length > 0)
                {
                    return Math.Max(minimumSteps, Math.Min(maximumSteps, (int)Math.Ceiling(length / targetSpacing)));
                }
            }
            catch
            {
                // Keep fallback deterministic when Revit cannot report curve length.
            }

            return minimumSteps;
        }

        private static XYZ EvaluateCurvePoint(Curve curve, double normalizedParameter, XYZ start, XYZ end)
        {
            try
            {
                return curve.Evaluate(normalizedParameter, true);
            }
            catch
            {
                return start + (end - start) * normalizedParameter;
            }
        }

        private static bool IsPointInsideBox(XYZ point, BoundingBoxXYZ box, double tolerance)
        {
            return point.X >= box.Min.X - tolerance && point.X <= box.Max.X + tolerance
                && point.Y >= box.Min.Y - tolerance && point.Y <= box.Max.Y + tolerance
                && point.Z >= box.Min.Z - tolerance && point.Z <= box.Max.Z + tolerance;
        }

        private static Curve GetIntersectionSegment(Curve curve, Element element)
        {
            Options options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true };
            GeometryElement geometry = element.get_Geometry(options);
            if (geometry == null) return null;

            foreach (GeometryObject obj in geometry)
            {
                Curve found = GetIntersectionSegmentFromObject(curve, obj);
                if (found != null) return found;
            }

            return null;
        }

        private static Curve GetIntersectionSegmentFromObject(Curve curve, GeometryObject obj)
        {
            Solid solid = obj as Solid;
            if (solid != null && solid.Volume > 0)
            {
                try
                {
                    SolidCurveIntersection intersection = solid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                    if (intersection != null && intersection.SegmentCount > 0)
                    {
                        return intersection.GetCurveSegment(0);
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    return null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }

            GeometryInstance instance = obj as GeometryInstance;
            if (instance != null)
            {
                try
                {
                    foreach (GeometryObject nested in instance.GetInstanceGeometry())
                    {
                        Curve found = GetIntersectionSegmentFromObject(curve, nested);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    return null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }

            return null;
        }

        private const string BuiltInSleeveFamilyVersion = "2026.08.27.01";
        private const string FamilyVersionParameterName = "HB_族群版本";

        private static readonly string[] DefaultFamilyFileNames =
        {
            "套管-圓形_無.rfa",
            "開孔-矩形_無.rfa"
        };

        private static readonly Dictionary<string, string> BuiltInFamilyVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "套管-圓形_無", BuiltInSleeveFamilyVersion }
        };

        private static readonly string[] NetworkDefaultFamilyPaths =
        {
            @"\\192.168.0.200\w01_bim\02_進行中專案\0003_Revit族庫_MEP(2024統整工作區)-2021.2022\12_管附件(PA)\套管\套管-圓形_無.rfa",
            @"\\192.168.0.200\w01_bim\02_進行中專案\0003_Revit族庫_MEP(2024統整工作區)-2021.2022\12_管附件(PA)\套管\開孔-矩形_無.rfa"
        };

        private static void EnsureDefaultFamiliesLoaded(Document doc)
        {
            if (doc == null || !doc.IsModifiable)
            {
                return;
            }

            var attemptedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in GetDefaultFamilyCandidatePaths())
            {
                string familyName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(familyName) || attemptedNames.Contains(familyName))
                {
                    continue;
                }

                string builtInVersion = GetBuiltInFamilyVersion(familyName);
                if (IsFamilyAlreadyLoaded(doc, familyName) && IsLoadedFamilyVersionCurrent(doc, familyName, builtInVersion))
                {
                    attemptedNames.Add(familyName);
                    continue;
                }

                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    string fingerprint = SleeveFamilyLoadStamp.Fingerprint(path, builtInVersion);
                    if (SleeveFamilyLoadStamp.WasLoaded(doc, familyName, fingerprint))
                    {
                        attemptedNames.Add(familyName);
                        continue;
                    }

                    using (var loadTransaction = new SubTransaction(doc))
                    {
                        loadTransaction.Start();
                        Autodesk.Revit.DB.Family loadedFamily;
                        doc.LoadFamily(path, new OverwriteSleeveFamilyLoadOptions(), out loadedFamily);
                        doc.Regenerate();
                        if (!SleeveFamilyLoadStamp.Record(doc, familyName, fingerprint,
                            IsLoadedFamilyVersionCurrent(doc, familyName, builtInVersion)))
                        {
                            loadTransaction.RollBack();
                            continue;
                        }
                        if (loadTransaction.Commit() == TransactionStatus.Committed)
                            attemptedNames.Add(familyName);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"載入預設套管族群失敗: {path}, {ex.Message}");
                }
            }
        }

        private static IEnumerable<string> GetDefaultFamilyCandidatePaths()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(PipeSleeveService).Assembly.Location) ?? string.Empty;
            string installedFamiliesDir = Path.Combine(assemblyDir, "Resources", "Families");

            foreach (string fileName in DefaultFamilyFileNames)
            {
                yield return Path.Combine(installedFamiliesDir, fileName);
            }

            foreach (string path in NetworkDefaultFamilyPaths)
            {
                yield return path;
            }
        }

        private static bool IsFamilyAlreadyLoaded(Document doc, string familyName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(familyName))
            {
                return false;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Family))
                .OfType<Autodesk.Revit.DB.Family>()
                .Any(family => string.Equals(family.Name, familyName, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetBuiltInFamilyVersion(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName))
            {
                return null;
            }

            return BuiltInFamilyVersions.TryGetValue(familyName, out string version) ? version : null;
        }

        private static bool IsLoadedFamilyVersionCurrent(Document doc, string familyName, string builtInVersion)
        {
            if (doc == null || string.IsNullOrWhiteSpace(builtInVersion))
            {
                return true;
            }

            string loadedVersion = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfType<FamilySymbol>()
                .Where(symbol => string.Equals(symbol.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                .Select(ReadFamilyVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .OrderByDescending(version => version, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return CompareVersionText(loadedVersion, builtInVersion) >= 0;
        }

        private static string ReadFamilyVersion(FamilySymbol symbol)
        {
            Parameter parameter = symbol?.LookupParameter(FamilyVersionParameterName);
            return parameter?.AsString()?.Trim();
        }

        private static int CompareVersionText(string loadedVersion, string builtInVersion)
        {
            if (string.IsNullOrWhiteSpace(loadedVersion))
            {
                return -1;
            }

            string[] loadedParts = loadedVersion.Split('.');
            string[] builtInParts = builtInVersion.Split('.');
            int count = Math.Max(loadedParts.Length, builtInParts.Length);
            for (int i = 0; i < count; i++)
            {
                int loaded = i < loadedParts.Length && int.TryParse(loadedParts[i], out int loadedValue) ? loadedValue : 0;
                int builtIn = i < builtInParts.Length && int.TryParse(builtInParts[i], out int builtInValue) ? builtInValue : 0;
                int comparison = loaded.CompareTo(builtIn);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private sealed class OverwriteSleeveFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Autodesk.Revit.DB.Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
        private static FamilySymbol ResolveSleeveSymbol(Document doc, PipeSleeveCandidate candidate, PipeSleeveOptions options, FamilySymbol wallSymbol, FamilySymbol floorSymbol, FamilySymbol openingSymbol)
        {
            ElementId defaultId = candidate.HostType == "Wall" ? options.DefaultWallSleeveSymbolId : options.DefaultFloorSleeveSymbolId;
            FamilySymbol selectedDefault = GetFamilySymbol(doc, defaultId);

            if (candidate.IsRectangularDuct)
            {
                if (selectedDefault != null && IsOpeningSymbol(selectedDefault))
                {
                    return selectedDefault;
                }

                return openingSymbol ?? selectedDefault ?? wallSymbol ?? floorSymbol;
            }

            if (options.UseDiameterSymbolMap && options.SleeveSymbolByDiameterMm != null && options.SleeveSymbolByDiameterMm.Count > 0)
            {
                ElementId mappedId = FindMappedSleeveSymbolId(candidate.PipeNominalDiameterMm, options.SleeveSymbolByDiameterMm);
                FamilySymbol mapped = GetFamilySymbol(doc, mappedId);
                if (mapped != null && !IsOpeningSymbol(mapped))
                {
                    return mapped;
                }
            }

            if (selectedDefault != null && !IsOpeningSymbol(selectedDefault))
            {
                return selectedDefault;
            }

            return candidate.HostType == "Wall" ? wallSymbol ?? floorSymbol : floorSymbol ?? wallSymbol;
        }

        private static void EnsureSymbolActive(Document doc, FamilySymbol symbol)
        {
            if (symbol == null || symbol.IsActive)
            {
                return;
            }

            symbol.Activate();
            doc.Regenerate();
        }
        private static ElementId FindMappedSleeveSymbolId(int nominalDiameterMm, Dictionary<int, ElementId> map)
        {
            if (map.TryGetValue(nominalDiameterMm, out ElementId exact))
            {
                return exact;
            }

            int larger = map.Keys.Where(size => size >= nominalDiameterMm).OrderBy(size => size).FirstOrDefault();
            if (larger > 0)
            {
                return map[larger];
            }

            int nearest = map.Keys.OrderBy(size => Math.Abs(size - nominalDiameterMm)).FirstOrDefault();
            return nearest > 0 ? map[nearest] : ElementId.InvalidElementId;
        }

        private static FamilySymbol GetFamilySymbol(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId)
            {
                return null;
            }

            return doc.GetElement(id) as FamilySymbol;
        }

        private static int ToNominalDiameterMm(double diameterFeet)
        {
            int measured = (int)Math.Round(diameterFeet / MmToFeet);
            int[] common = { 15, 20, 25, 32, 40, 50, 65, 80, 100, 125, 150, 200, 250, 300 };
            int nextLarger = common.FirstOrDefault(size => size >= measured - 2);
            return nextLarger > 0 ? nextLarger : measured;
        }

        private static double EstimateHostThicknessFeet(Element host, string hostType, XYZ pipeDirection)
        {
            if (host == null)
            {
                return 0;
            }

            double parameterThickness = ReadHostThicknessParameter(host, hostType);
            if (parameterThickness > 0)
            {
                return parameterThickness;
            }

            BoundingBoxXYZ box = host.get_BoundingBox(null);
            if (box == null || pipeDirection == null)
            {
                return 0;
            }

            XYZ dir = pipeDirection.Normalize();
            XYZ size = box.Max - box.Min;
            double projected = Math.Abs(size.X * dir.X) + Math.Abs(size.Y * dir.Y) + Math.Abs(size.Z * dir.Z);
            return projected > 0 ? projected : 0;
        }

        private static XYZ GetSleeveDirection(Element host, string hostType, XYZ pipeDirection, Transform linkTransform)
        {
            XYZ direction = null;

            if (hostType == "Wall")
            {
                Wall wall = host as Wall;
                if (wall != null)
                {
                    direction = wall.Orientation;
                }

                if (direction == null || direction.GetLength() < 0.001)
                {
                    LocationCurve locationCurve = host?.Location as LocationCurve;
                    Curve curve = locationCurve?.Curve;
                    if (curve != null)
                    {
                        XYZ tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                        direction = tangent.CrossProduct(XYZ.BasisZ);
                    }
                }
            }
            else if (hostType == "Floor")
            {
                direction = XYZ.BasisZ;
            }
            else if (hostType == "Beam")
            {
                LocationCurve locationCurve = host?.Location as LocationCurve;
                Curve curve = locationCurve?.Curve;
                if (curve != null)
                {
                    XYZ tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                    XYZ normal = tangent.CrossProduct(XYZ.BasisZ);
                    if (normal.GetLength() < 0.001)
                    {
                        normal = XYZ.BasisZ.CrossProduct(tangent);
                    }
                    direction = normal.GetLength() > 0.001 ? normal : pipeDirection;
                }
                else
                {
                    direction = pipeDirection;
                }
            }

            if (direction == null || direction.GetLength() < 0.001)
            {
                direction = pipeDirection;
            }

            if (direction == null || direction.GetLength() < 0.001)
            {
                return null;
            }

            if (linkTransform != null)
            {
                direction = linkTransform.OfVector(direction);
            }

            direction = direction.Normalize();
            if (pipeDirection != null && pipeDirection.GetLength() > 0.001 && direction.DotProduct(pipeDirection.Normalize()) < 0)
            {
                direction = direction.Negate();
            }

            return direction;
        }
        private static double ReadHostThicknessParameter(Element host, string hostType)
        {
            Wall wall = host as Wall;
            if (wall != null && wall.Width > 0)
            {
                return wall.Width;
            }

            Parameter parameter = host.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM) ??
                                  host.LookupParameter("厚度") ??
                                  host.LookupParameter("Thickness") ??
                                  host.LookupParameter("Width") ??
                                  host.LookupParameter("寬度");
            if (parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double)
            {
                double value = parameter.AsDouble();
                if (value > 0)
                {
                    return value;
                }
            }

            return 0;
        }
        private static FamilySymbol FindSleeveSymbol(Document doc, bool preferWall)
        {
            string[] sleeveNames = { "sleeve", "套管" };
            string[] wallNames = { "wall", "牆", "墙" };
            string[] floorNames = { "floor", "slab", "beam", "樓板", "楼板", "樑", "梁" };

            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfType<FamilySymbol>()
                .Where(s => IsSleeveCategory(s.Category))
                .ToList();

            var sleeveSymbols = symbols
                .Where(s => ContainsAny(GetSymbolSearchText(s), sleeveNames) && !ContainsAny(GetSymbolSearchText(s), GetSleeveExcludeNames()))
                .OrderBy(s => GetSleeveSymbolSortGroup(s))
                .ThenBy(s => GetSleeveDiameterForSort(GetSymbolSearchText(s)))
                .ThenBy(s => s.FamilyName)
                .ThenBy(s => s.Name)
                .ToList();
            FamilySymbol named = sleeveSymbols.FirstOrDefault();
            if (named == null) return null;

            string[] preferred = preferWall ? wallNames : floorNames;
            return sleeveSymbols.FirstOrDefault(s => ContainsAny(GetSymbolSearchText(s), preferred) && IsGeneralSleeveSymbol(s)) ??
                   sleeveSymbols.FirstOrDefault(IsGeneralSleeveSymbol) ??
                   named;
        }

        private static FamilySymbol FindOpeningSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfType<FamilySymbol>()
                .Where(s => IsSleeveCategory(s.Category) && IsOpeningSymbol(s) && !ContainsAny(GetSymbolSearchText(s), GetSleeveExcludeNames()))
                .OrderByDescending(s => GetSymbolSearchText(s).IndexOf("開孔-矩形_無", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(s => s.FamilyName)
                .ThenBy(s => s.Name)
                .FirstOrDefault();
        }

        private static bool IsOpeningSymbol(FamilySymbol symbol)
        {
            return symbol != null && ContainsAny(GetSymbolSearchText(symbol), new[] { "開孔", "开孔", "opening" });
        }
        private static bool IsSleeveCategory(Category category)
        {
            if (category == null) return false;
            long id = category.Id.GetIdValue();
            return id == (long)BuiltInCategory.OST_GenericModel || id == (long)BuiltInCategory.OST_PipeAccessory;
        }

        private static IEnumerable<string> GetSleeveExcludeNames()
        {
            return new[] { "消防", "子母", "母管", "管束", "閥", "阀", "valve", "sprinkler", "窗", "窗帘", "窗簾", "window", "door", "門", "风口", "風口", "grille", "louver" };
        }

        private static bool IsGeneralSleeveSymbol(FamilySymbol symbol)
        {
            return symbol != null && !ContainsAny(GetSymbolSearchText(symbol), GetSpecialSleeveTokens());
        }

        private static int GetSleeveSymbolSortGroup(FamilySymbol symbol)
        {
            string text = GetSymbolSearchText(symbol);
            if (ContainsAny(text, GetSpecialSleeveTokens()))
            {
                return 1;
            }

            if (IsOpeningSymbol(symbol))
            {
                return 2;
            }

            return 0;
        }

        private static int GetSleeveDiameterForSort(string text)
        {
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(text ?? string.Empty, @"(?:dn|[-_ ])(\d{2,3})(?:a|mm|\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : int.MaxValue;
        }

        private static IEnumerable<string> GetSpecialSleeveTokens()
        {
            return new[] { "止水", "防水", "waterstop", "water stop" };
        }

        private static string GetSymbolSearchText(FamilySymbol symbol)
        {
            return ((symbol.FamilyName ?? string.Empty) + " " + (symbol.Name ?? string.Empty)).ToLowerInvariant();
        }

        private static bool ContainsAny(string text, IEnumerable<string> parts)
        {
            return parts.Any(p => text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static FamilyInstance PlaceSleeve(Document doc, PipeSleeveCandidate candidate, FamilySymbol symbol)
        {
            Level level = GetBaseLevelAtOrBelow(doc, candidate.Point) ?? GetNearestLevel(doc, candidate.Point);
            if (level != null)
            {
                try
                {
                    return doc.Create.NewFamilyInstance(candidate.Point, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                }
                catch
                {
                    // Some hosted families require a host; fall back to host-based placement below.
                }
            }

            try
            {
                if (!candidate.IsFromLink && candidate.HostType == "Wall" && candidate.HostElement is Wall wall)
                    return doc.Create.NewFamilyInstance(candidate.Point, symbol, wall, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                if (!candidate.IsFromLink && candidate.HostType == "Floor" && candidate.HostElement is Floor floor)
                    return doc.Create.NewFamilyInstance(candidate.Point, symbol, floor, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                if (!candidate.IsFromLink && candidate.HostType == "Beam" && candidate.HostElement is FamilyInstance beam)
                    return doc.Create.NewFamilyInstance(candidate.Point, symbol, beam, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            }
            catch
            {
                // Keep the failed candidate isolated from the rest of the batch.
            }

            return null;
        }

        private static void MoveSleeveToPoint(Document doc, FamilyInstance sleeve, XYZ targetPoint)
        {
            if (doc == null || sleeve == null || targetPoint == null)
            {
                return;
            }

            try
            {
                LocationPoint location = sleeve.Location as LocationPoint;
                if (location == null)
                {
                    return;
                }

                XYZ delta = targetPoint - location.Point;
                if (delta.GetLength() > 0.001)
                {
                    ElementTransformUtils.MoveElement(doc, sleeve.Id, delta);
                }
            }
            catch
            {
                // Some hosted families restrict movement; keep the created sleeve instead of failing the whole batch.
            }
        }
        private static void MoveSleeveGeometryCenterToPoint(Document doc, FamilyInstance sleeve, XYZ targetPoint)
        {
            if (doc == null || sleeve == null || targetPoint == null)
            {
                return;
            }

            try
            {
                doc.Regenerate();
                BoundingBoxXYZ box = sleeve.get_BoundingBox(null);
                if (box == null)
                {
                    return;
                }

                XYZ center = (box.Min + box.Max) * 0.5;
                XYZ delta = targetPoint - center;
                if (delta.GetLength() > 0.5 * MmToFeet)
                {
                    ElementTransformUtils.MoveElement(doc, sleeve.Id, delta);
                    doc.Regenerate();
                }
            }
            catch
            {
                // Some hosted families restrict movement; LocationPoint alignment above remains the fallback.
            }
        }
        private static int GetNextSleeveNumber(Document doc)
        {
            int maxNumber = 0;
            try
            {
                foreach (FamilyInstance sleeve in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .OfType<FamilyInstance>()
                    .Where(IsSleeveInstance))
                {
                    string mark = ReadString(sleeve, BuiltInParameter.ALL_MODEL_MARK, "套管編號", "編號", "Sleeve Number");
                    int number = ParseSleeveNumber(mark);
                    if (number > maxNumber)
                    {
                        maxNumber = number;
                    }
                }
            }
            catch
            {
                // If the model cannot be scanned, fall back to PS-001 instead of blocking sleeve placement.
            }

            return maxNumber + 1;
        }

        private static int ParseSleeveNumber(string mark)
        {
            if (string.IsNullOrWhiteSpace(mark)) return 0;

            string text = mark.Trim();
            if (text.StartsWith("PS-", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(3);
            }

            return int.TryParse(text, out int value) ? value : 0;
        }
        private static void SetSleeveParameters(FamilyInstance sleeve, PipeSleeveCandidate candidate, double clearanceFeet, string number)
        {
            if (!string.IsNullOrWhiteSpace(number)) SetString(sleeve, number, BuiltInParameter.ALL_MODEL_MARK, "套管編號", "編號", "Sleeve Number");

            double sleeveDiameter = candidate.PipeDiameterFeet + clearanceFeet;
            SetDouble(sleeve, sleeveDiameter, "套管直徑", "直徑", "Diameter", "Sleeve Diameter", "Diatot", "邊界寬度", "大小");
            SetDouble(sleeve, candidate.PipeDiameterFeet, "管徑", "Pipe Diameter");
            if (candidate.IsRectangularDuct)
            {
                SetDouble(sleeve, candidate.DuctWidthFeet + clearanceFeet, "開孔寬度", "寬度", "Width", "Sleeve Width");
                SetDouble(sleeve, candidate.DuctHeightFeet + clearanceFeet, "開孔高度", "高度", "Height", "Sleeve Height");
            }
            if (candidate.HostThicknessFeet > 0)
            {
                SetDouble(sleeve, candidate.HostThicknessFeet, "套管長度", "長度", "Length", "Sleeve Length", "深度", "Depth");
            }
            SetDouble(sleeve, candidate.HostThicknessFeet, "穿越厚度", "Host Thickness");
            string crossingType = GetCrossingTypeText(candidate.HostType);
            string systemName = GetSourceSystemName(candidate.Pipe);
            string systemTypeName = GetSourceSystemTypeName(candidate.Pipe);
            SetString(sleeve, systemTypeName, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "備註", "Comments");
            SetString(sleeve, systemName, "系統名稱", "System Name", "Source System Name");
            SetString(sleeve, crossingType, "穿越構件", "Host Type");
            SetString(sleeve,
                GetSleeveCheckInfo(candidate, crossingType),
                "套管檢核資訊", "Sleeve Check", "Check Info");
            if (!string.IsNullOrWhiteSpace(candidate.BeamRiskLevel))
            {
                SetString(sleeve, candidate.BeamRiskLevel, "穿梁風險等級", "Beam Opening Risk", "風險等級", "Risk Level");
                SetString(sleeve, candidate.BeamRiskNote, "穿梁檢核", "Beam Opening Check", "結構檢核", "Structural Check");
            }
            SetString(sleeve, candidate.IsFromLink ? "連結模型" : "當前模型", "穿越來源", "Host Source");
            SetString(sleeve, $"DN{candidate.PipeNominalDiameterMm}", "管道標稱直徑", "Nominal Diameter");
            SetString(sleeve, candidate.Pipe.Id.GetIdValue().ToString(), "來源管線Id", "Pipe Id", "Source Pipe Id");
        }

        private static string GetSleeveCheckInfo(PipeSleeveCandidate candidate, string crossingType)
        {
            string info = string.IsNullOrWhiteSpace(candidate?.BeamRiskNote) ? crossingType : candidate.BeamRiskNote;
            if (!string.IsNullOrWhiteSpace(candidate?.CheckNote))
            {
                info = AppendNote(info, candidate.CheckNote);
            }

            return info;
        }
        private static string GetSourceSystemTypeName(Element element)
        {
            if (element == null)
            {
                return string.Empty;
            }

            string parameterValue = ReadParameterValueText(element, "系統類型", "System Type", "系統分類", "System Classification");
            if (!string.IsNullOrWhiteSpace(parameterValue))
            {
                return parameterValue;
            }

            try
            {
                MEPSystem system = null;
                if (element is Pipe pipe)
                {
                    system = pipe.MEPSystem;
                }
                else if (element is Duct duct)
                {
                    system = duct.MEPSystem;
                }

                if (system != null)
                {
                    Element systemType = element.Document?.GetElement(system.GetTypeId());
                    if (!string.IsNullOrWhiteSpace(systemType?.Name))
                    {
                        return systemType.Name;
                    }

                    if (!string.IsNullOrWhiteSpace(system.Name))
                    {
                        return system.Name;
                    }
                }
            }
            catch
            {
                // Some placeholder or linked-derived elements may not expose a live MEPSystem.
            }

            return GetSourceSystemName(element);
        }
        private static string GetSourceSystemName(Element element)
        {
            if (element == null)
            {
                return string.Empty;
            }

            string name = ReadString(element, BuiltInParameter.RBS_SYSTEM_NAME_PARAM, "系統名稱", "System Name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            try
            {
                if (element is Pipe pipe && pipe.MEPSystem != null)
                {
                    return pipe.MEPSystem.Name ?? string.Empty;
                }

                if (element is Duct duct && duct.MEPSystem != null)
                {
                    return duct.MEPSystem.Name ?? string.Empty;
                }
            }
            catch
            {
                // Some placeholder or linked-derived elements may not expose a live MEPSystem.
            }

            return string.Empty;
        }
        private static string GetCrossingTypeText(string hostType)
        {
            if (hostType == "Wall") return "穿牆";
            if (hostType == "Floor") return "穿樓板";
            if (hostType == "Beam") return "穿梁";
            return "穿越構件";
        }
        private static void SetString(FamilyInstance instance, string value, BuiltInParameter builtIn, params string[] names)
        {
            Parameter parameter = instance.get_Parameter(builtIn);
            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
            {
                parameter.Set(value);
                return;
            }

            SetString(instance, value, names);
        }

        private static void SetString(FamilyInstance instance, string value, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = instance.LookupParameter(name);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value);
                    return;
                }
            }
        }

        private static void SetDouble(FamilyInstance instance, double value, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = instance.LookupParameter(name);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Double)
                {
                    parameter.Set(value);
                }
            }
        }

        private static FamilyInstance FindExistingSleeveForUpdate(Document doc, PipeSleeveCandidate candidate, HashSet<long> usedSleeveIds)
        {
            if (doc == null || candidate?.Pipe == null || candidate.Point == null)
            {
                return null;
            }

            string sourceId = candidate.Pipe.Id.GetIdValue().ToString();
            string crossingType = GetCrossingTypeText(candidate.HostType);
            double nearTolerance = GetExistingSleeveTolerance(candidate);

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfType<FamilyInstance>()
                .Where(fi => IsSleeveInstance(fi))
                .Where(fi => usedSleeveIds == null || !usedSleeveIds.Contains(fi.Id.GetIdValue()))
                .Select(fi => new ExistingSleeveMatch
                {
                    Instance = fi,
                    Location = (fi.Location as LocationPoint)?.Point,
                    Source = ReadString(fi, "來源管線Id", "Pipe Id", "Source Pipe Id"),
                    Crossing = ReadSleeveCrossing(fi)
                })
                .Where(x => x.Location != null)
                .Select(x =>
                {
                    x.Distance = x.Location.DistanceTo(candidate.Point);
                    x.SourceMatches = string.Equals(x.Source, sourceId, StringComparison.OrdinalIgnoreCase);
                    x.CrossingMatches = TextContains(x.Crossing, crossingType);
                    x.IsNear = x.Distance <= nearTolerance;
                    return x;
                })
                .Where(x => x.SourceMatches || (x.IsNear && x.CrossingMatches && string.IsNullOrWhiteSpace(x.Source) && x.Distance <= 25.0 * MmToFeet))
                .OrderBy(x => x.SourceMatches ? 0 : 1)
                .ThenBy(x => x.CrossingMatches ? 0 : 1)
                .ThenBy(x => x.Distance)
                .Select(x => x.Instance)
                .FirstOrDefault();
        }

        private static void UpdateExistingSleeve(Document doc, FamilyInstance sleeve, PipeSleeveCandidate candidate, FamilySymbol symbol, double clearanceFeet, PipeSleeveOptions options)
        {
            if (doc == null || sleeve == null || candidate == null)
            {
                return;
            }

            if (symbol != null && sleeve.Symbol != null && sleeve.Symbol.Id != symbol.Id)
            {
                sleeve.ChangeTypeId(symbol.Id);
            }

            MoveSleeveToPoint(doc, sleeve, candidate.Point);
            SetSleeveParameters(sleeve, candidate, clearanceFeet, null);
            AlignSleeveToDirection(doc, sleeve, candidate.Point, candidate.SleeveDirection ?? candidate.Direction);
            SetSleeveLevelAndOffset(doc, sleeve, candidate.Point);
            MoveSleeveToPoint(doc, sleeve, candidate.Point);
            MoveSleeveGeometryCenterToPoint(doc, sleeve, candidate.Point);
            ApplyBeamRiskViewOverride(doc, sleeve, candidate, options);
        }

        private static void ApplyBeamRiskViewOverride(Document doc, FamilyInstance sleeve, PipeSleeveCandidate candidate, PipeSleeveOptions options)
        {
            if (doc == null || sleeve == null || candidate == null || options == null)
            {
                return;
            }

            if (candidate.HostType != "Beam")
            {
                return;
            }

            View view = null;
            if (options.ActiveViewId != ElementId.InvalidElementId)
            {
                view = doc.GetElement(options.ActiveViewId) as View;
            }

            view = view ?? doc.ActiveView;
            if (view == null || view.IsTemplate)
            {
                return;
            }

            try
            {
                OverrideGraphicSettings settings = new OverrideGraphicSettings();
                int riskRank = GetBeamRiskRank(candidate.BeamRiskLevel);
                if (riskRank >= 2)
                {
                    ApplySleeveColor(settings, new Color(220, 30, 30), 0);
                }
                else if (riskRank == 1)
                {
                    ApplySleeveColor(settings, new Color(255, 150, 0), 0);
                }

                view.SetElementOverrides(sleeve.Id, settings);
            }
            catch
            {
                // View overrides can fail in schedules, templates, or special views; parameter-based risk data still remains.
            }
        }

        private static void ApplySleeveColor(OverrideGraphicSettings settings, Color color, int transparency)
        {
            settings.SetProjectionLineColor(color);
            settings.SetCutLineColor(color);
            settings.SetSurfaceForegroundPatternColor(color);
            settings.SetCutForegroundPatternColor(color);
            settings.SetSurfaceTransparency(transparency);
        }

        private static bool HasExistingSleeveNear(Document doc, PipeSleeveCandidate candidate)
        {
            if (doc == null || candidate?.Pipe == null || candidate.Point == null)
            {
                return false;
            }

            string sourceId = candidate.Pipe.Id.GetIdValue().ToString();
            string crossingType = GetCrossingTypeText(candidate.HostType);
            double tolerance = GetExistingSleeveTolerance(candidate);
            XYZ point = candidate.Point;
            XYZ min = new XYZ(point.X - tolerance, point.Y - tolerance, point.Z - tolerance);
            XYZ max = new XYZ(point.X + tolerance, point.Y + tolerance, point.Z + tolerance);
            Outline outline = new Outline(min, max);
            BoundingBoxIntersectsFilter nearbyFilter = new BoundingBoxIntersectsFilter(outline);

            return new FilteredElementCollector(doc)
                .WherePasses(nearbyFilter)
                .OfClass(typeof(FamilyInstance))
                .OfType<FamilyInstance>()
                .Where(fi => IsSleeveInstance(fi))
                .Any(fi =>
                {
                    LocationPoint lp = fi.Location as LocationPoint;
                    if (lp == null) return false;

                    double distance = lp.Point.DistanceTo(point);
                    if (distance > tolerance) return false;

                    string source = ReadString(fi, "來源管線Id", "Pipe Id", "Source Pipe Id");
                    if (string.Equals(source, sourceId, StringComparison.OrdinalIgnoreCase)) return true;

                    string crossing = ReadSleeveCrossing(fi);
                    if (!string.IsNullOrWhiteSpace(source) || !TextContains(crossing, crossingType)) return false;

                    double legacyTolerance = Math.Min(tolerance, 25.0 * MmToFeet);
                    return distance <= legacyTolerance;
                });
        }

        private sealed class ExistingSleeveMatch
        {
            public FamilyInstance Instance { get; set; }
            public XYZ Location { get; set; }
            public string Source { get; set; }
            public string Crossing { get; set; }
            public double Distance { get; set; }
            public bool SourceMatches { get; set; }
            public bool CrossingMatches { get; set; }
            public bool IsNear { get; set; }
        }

        private static double GetExistingSleeveTolerance(PipeSleeveCandidate candidate)
        {
            double tolerance = 150.0 * MmToFeet;
            if (candidate != null)
            {
                tolerance = Math.Max(tolerance, (candidate.PipeDiameterFeet + 100.0 * MmToFeet) * 0.75);
                if (candidate.HostThicknessFeet > 0)
                {
                    tolerance = Math.Max(tolerance, Math.Min(candidate.HostThicknessFeet * 0.25, 300.0 * MmToFeet));
                }
            }

            return tolerance;
        }

        private static bool IsSleeveInstance(FamilyInstance instance)
        {
            if (instance == null || !IsSleeveCategory(instance.Category))
            {
                return false;
            }

            string source = ReadString(instance, "來源管線Id", "Pipe Id", "Source Pipe Id");
            string crossing = ReadSleeveCrossing(instance);
            string text = GetFamilyInstanceSearchText(instance);
            return !string.IsNullOrWhiteSpace(source)
                || TextContains(crossing, "穿牆")
                || TextContains(crossing, "穿樓板")
                || TextContains(crossing, "穿梁")
                || ContainsAny(text, new[] { "套管", "開孔", "sleeve", "opening" });
        }

        private static bool IsAutoGeneratedSleeveInstance(FamilyInstance instance)
        {
            if (!IsSleeveInstance(instance)) return false;

            string source = ReadString(instance, "來源管線Id", "Pipe Id", "Source Pipe Id");
            return long.TryParse(source, out long sourceId) && sourceId > 0;
        }

        private static string GetFamilyInstanceSearchText(FamilyInstance instance)
        {
            string familyName = instance?.Symbol?.FamilyName ?? string.Empty;
            string typeName = instance?.Symbol?.Name ?? string.Empty;
            return (familyName + " " + typeName).ToLowerInvariant();
        }

        private static bool TextContains(string text, string value)
        {
            return !string.IsNullOrWhiteSpace(text)
                && !string.IsNullOrWhiteSpace(value)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadSleeveCrossing(FamilyInstance instance)
        {
            string crossing = ReadString(instance, "穿越構件", "Host Type");
            if (!string.IsNullOrWhiteSpace(crossing))
            {
                return crossing;
            }

            return ReadString(instance, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "備註", "Comments");
        }

        private static string GetSleeveSystemName(FamilyInstance sleeve)
        {
            return ReadString(sleeve, "系統名稱", "System Name", "Source System Name");
        }

        private static string GetSleeveLevelName(Document doc, FamilyInstance sleeve)
        {
            string value = ReadString(sleeve, "套管樓層", "樓層名稱", "Level Name", "Reference Level Name", "參考樓層名稱", "所屬樓層名稱", "樓層", "Level");
            if (!string.IsNullOrWhiteSpace(value)) return value;

            if (sleeve?.LevelId != null && sleeve.LevelId.GetIdValue() > 0)
            {
                Level level = doc.GetElement(sleeve.LevelId) as Level;
                if (level != null) return level.Name;
            }

            LocationPoint location = sleeve?.Location as LocationPoint;
            if (location != null)
            {
                Level level = GetBaseLevelAtOrBelow(doc, location.Point) ?? GetNearestLevel(doc, location.Point);
                if (level != null) return level.Name;
            }

            return string.Empty;
        }
        private static string ReadParameterValueText(Element element, params string[] names)
        {
            foreach (string name in names ?? new string[0])
            {
                Parameter parameter = element?.LookupParameter(name);
                if (parameter == null || !parameter.HasValue)
                {
                    continue;
                }

                if (parameter.StorageType == StorageType.String)
                {
                    string value = parameter.AsString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }

                string displayValue = parameter.AsValueString();
                if (!string.IsNullOrWhiteSpace(displayValue)) return displayValue;

                if (parameter.StorageType == StorageType.ElementId)
                {
                    Element referenced = element.Document?.GetElement(parameter.AsElementId());
                    if (!string.IsNullOrWhiteSpace(referenced?.Name)) return referenced.Name;
                }
            }

            return string.Empty;
        }
        private static string ReadString(Element element, BuiltInParameter builtIn, params string[] names)
        {
            Parameter parameter = element?.get_Parameter(builtIn);
            if (parameter != null && parameter.StorageType == StorageType.String)
            {
                string value = parameter.AsString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return ReadString(element, names);
        }

        private static string ReadString(Element element, params string[] names)
        {
            foreach (string name in names ?? new string[0])
            {
                Parameter parameter = element?.LookupParameter(name);
                if (parameter != null && parameter.StorageType == StorageType.String)
                {
                    string value = parameter.AsString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }

            return string.Empty;
        }
        private static string ReadString(FamilyInstance instance, BuiltInParameter builtIn, params string[] names)
        {
            Parameter parameter = instance?.get_Parameter(builtIn);
            if (parameter != null && parameter.StorageType == StorageType.String)
            {
                string value = parameter.AsString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return ReadString(instance, names);
        }
        private static string ReadString(FamilyInstance instance, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = instance.LookupParameter(name);
                if (parameter != null && parameter.StorageType == StorageType.String) return parameter.AsString();
            }
            return string.Empty;
        }

        private static void AlignSleeveToDirection(Document doc, FamilyInstance sleeve, XYZ point, XYZ targetDirection)
        {
            if (targetDirection == null) return;
            try
            {
                XYZ current = sleeve.FacingOrientation;
                XYZ target = targetDirection.Normalize();
                double dot = Math.Max(-1.0, Math.Min(1.0, current.DotProduct(target)));
                if (Math.Abs(dot - 1.0) < 0.001) return;

                XYZ axisDirection = current.CrossProduct(target);
                double angle = Math.Acos(dot);
                if (axisDirection.GetLength() < 0.001)
                {
                    axisDirection = XYZ.BasisZ;
                    angle = Math.PI;
                }

                Line axis = Line.CreateBound(point, point + axisDirection.Normalize());
                ElementTransformUtils.RotateElement(doc, sleeve.Id, axis, angle);
            }
            catch
            {
                // Some sleeve families are not direction-aware; placement is still useful.
            }
        }

        private static bool IsSupportedCurve(Element element)
        {
            return (element is Pipe || element is Duct || IsElementOfCategory(element, BuiltInCategory.OST_Conduit) || IsElementOfCategory(element, BuiltInCategory.OST_CableTray)) && element.Location is LocationCurve;
        }

        private static bool IsElementOfCategory(Element element, BuiltInCategory category)
        {
            return element?.Category != null && element.Category.Id.GetIdValue() == (long)category;
        }

        private static string GetHostType(Element element)
        {
            if (element == null) return null;
            if (element is Wall) return "Wall";
            if (element is Floor) return "Floor";

            long categoryId = element.Category?.Id.GetIdValue() ?? 0;
            if (categoryId == (long)BuiltInCategory.OST_StructuralFraming) return "Beam";
            if (categoryId == (long)BuiltInCategory.OST_StructuralColumns) return "Beam";

            string text = GetElementSearchText(element);
            if (ContainsAny(text, new[] { "ifcslab", "slab", "floor", "樓板", "楼板", "版" })) return "Floor";
            if (ContainsAny(text, new[] { "ifcbeam", "beam", "girder", "joist", "樑", "梁" })) return "Beam";
            if (ContainsAny(text, new[] { "ifcwall", "wall", "partition", "牆", "墙" })) return "Wall";

            return null;
        }

        private static bool ShouldCreate(XYZ pipeDirection, string hostType)
        {
            if (pipeDirection == null) return false;

            double vertical = Math.Abs(pipeDirection.Normalize().DotProduct(XYZ.BasisZ));
            if (hostType == "Floor") return vertical >= 0.85;
            if (hostType == "Wall" || hostType == "Beam") return vertical <= 0.25;
            return false;
        }

        private static XYZ GetCurveDirection(Curve curve)
        {
            return (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
        }

        private static double GetPipeDiameterFeet(Element pipe)
        {
            Parameter parameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) ??
                                  pipe.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM) ??
                                  pipe.LookupParameter("直徑") ??
                                  pipe.LookupParameter("Diameter");
            if (parameter != null && parameter.HasValue) return parameter.AsDouble();

            Parameter width = pipe.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            Parameter height = pipe.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
            if (width != null && height != null && width.HasValue && height.HasValue)
            {
                return Math.Sqrt(width.AsDouble() * height.AsDouble());
            }

            return 150.0 * MmToFeet;
        }

        private static bool TryGetRectangularDuctSizeFeet(Element element, out double widthFeet, out double heightFeet)
        {
            widthFeet = 0;
            heightFeet = 0;

            if (!(element is Duct || IsElementOfCategory(element, BuiltInCategory.OST_CableTray)))
            {
                return false;
            }
            Parameter width = element.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM) ??
                              element.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM) ??
                              element.LookupParameter("寬度") ??
                              element.LookupParameter("線架寬度") ??
                              element.LookupParameter("Width");
            Parameter height = element.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM) ??
                               element.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM) ??
                               element.LookupParameter("高度") ??
                               element.LookupParameter("線架高度") ??
                               element.LookupParameter("Height");
            if (width == null || height == null || !width.HasValue || !height.HasValue)
            {
                return false;
            }

            widthFeet = width.AsDouble();
            heightFeet = height.AsDouble();
            return widthFeet > 0 && heightFeet > 0;
        }
        private static void SetSleeveLevelAndOffset(Document doc, FamilyInstance sleeve, XYZ point)
        {
            if (doc == null || sleeve == null || point == null)
            {
                return;
            }

            Level level = GetBaseLevelAtOrBelow(doc, point) ?? GetNearestLevel(doc, point);
            if (level == null)
            {
                return;
            }

            double offset = point.Z - level.Elevation;
            bool levelSet = SetElementId(sleeve, level.Id,
                new[]
                {
                    BuiltInParameter.FAMILY_LEVEL_PARAM,
                    BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                    BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
                    BuiltInParameter.SCHEDULE_LEVEL_PARAM
                },
                "樓層", "參考樓層", "Schedule Level", "Level", "Reference Level");

            SetDouble(sleeve, offset,
                new[] { BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, BuiltInParameter.INSTANCE_ELEVATION_PARAM },
                "距離樓層的高程", "與樓層的偏移", "Offset", "Elevation from Level");

            // Tags read this family parameter, so keep it aligned with the level-relative offset.
            SetDouble(sleeve, offset, "立面高程", "Elevation");
            SetString(sleeve, level.Name, "套管樓層", "樓層名稱", "Level Name", "Reference Level Name", "參考樓層名稱", "所屬樓層名稱");
            SetString(sleeve, levelSet ? "當層基準" : "當層高程", "套管高程模式", "Elevation Mode");
        }

        private static bool SetElementId(FamilyInstance instance, ElementId value, BuiltInParameter[] builtIns, params string[] names)
        {
            if (instance == null || value == null)
            {
                return false;
            }

            foreach (BuiltInParameter builtIn in builtIns ?? new BuiltInParameter[0])
            {
                try
                {
                    Parameter parameter = instance.get_Parameter(builtIn);
                    if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.ElementId)
                    {
                        parameter.Set(value);
                        return true;
                    }
                }
                catch
                {
                    // Some built-in parameters are not available on every Revit version/family category.
                }
            }

            foreach (string name in names ?? new string[0])
            {
                Parameter parameter = instance.LookupParameter(name);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.ElementId)
                {
                    parameter.Set(value);
                    return true;
                }
            }

            return false;
        }

        private static void SetDouble(FamilyInstance instance, double value, BuiltInParameter builtIn, params string[] names)
        {
            SetDouble(instance, value, new[] { builtIn }, names);
        }

        private static void SetDouble(FamilyInstance instance, double value, BuiltInParameter[] builtIns, params string[] names)
        {
            foreach (BuiltInParameter builtIn in builtIns ?? new BuiltInParameter[0])
            {
                try
                {
                    Parameter parameter = instance.get_Parameter(builtIn);
                    if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Double)
                    {
                        parameter.Set(value);
                    }
                }
                catch
                {
                    // Some built-in parameters are not available on every Revit version/family category.
                }
            }

            SetDouble(instance, value, names);
        }

        private static Level GetBaseLevelAtOrBelow(Document doc, XYZ point)
        {
            const double tolerance = 1.0 * MmToFeet;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .OfType<Level>()
                .Where(level => level.Elevation <= point.Z + tolerance)
                .OrderByDescending(level => level.Elevation)
                .FirstOrDefault();
        }

        private static Level GetNearestLevel(Document doc, XYZ point)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .OfType<Level>()
                .OrderBy(level => Math.Abs(level.Elevation - point.Z))
                .FirstOrDefault();
        }

        private static string MakeKey(ElementId pipeId, string hostType, XYZ point)
        {
            double grid = 100.0 * MmToFeet;
            long x = (long)Math.Round(point.X / grid);
            long y = (long)Math.Round(point.Y / grid);
            long z = (long)Math.Round(point.Z / grid);
            return pipeId.GetIdValue() + ":" + hostType + ":" + x + ":" + y + ":" + z;
        }
    }
}





















































