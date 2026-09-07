using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdArchitecturalOpeningFromSleeves : IExternalCommand
    {
        private const double MmToFeet = 1.0 / 304.8;
        private const double MoveToleranceFeet = 5.0 * MmToFeet;
        private static readonly Guid OpeningSyncSchemaGuid = new Guid("9D52C888-5E04-4C60-8D01-21B233977DD0");

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var licenseManager = LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("MEP.PipeSleeve"))
                {
                    TaskDialog.Show("授權限制", "您的授權不支援套管/開孔同步功能。");
                    return Result.Cancelled;
                }

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null)
                {
                    return Result.Cancelled;
                }

                OpeningSyncResult result;
                using (Transaction tx = new Transaction(doc, "依 MEP 套管建立建築開孔"))
                {
                    try
                    {
                        tx.Start();
                        result = ArchitecturalOpeningSyncService.SyncFromLinkedSleeves(doc);
                        if (tx.GetStatus() == TransactionStatus.Started)
                        {
                            tx.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started)
                        {
                            tx.RollBack();
                        }

                        message = ex.Message;
                        TaskDialog.Show("建築開孔同步", "執行失敗，已復原本次變更：\n" + ex.Message);
                        return Result.Cancelled;
                    }
                }

                TaskDialog.Show("建築開孔同步", result.ToTaskDialogText());
                return result.CreatedCount > 0 || result.UpdatedCount > 0 || result.SkippedCount > 0
                    ? Result.Succeeded
                    : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("建築開孔同步", "執行失敗，未寫入變更：\n" + ex.Message);
                return Result.Cancelled;
            }
        }

        private sealed class OpeningSyncResult
        {
            public int LinkCount { get; set; }
            public int SourceSleeveCount { get; set; }
            public int CreatedCount { get; set; }
            public int UpdatedCount { get; set; }
            public int SkippedCount { get; set; }
            public int FailedCount { get; set; }
            public List<string> Messages { get; } = new List<string>();

            public string ToTaskDialogText()
            {
                string details = Messages.Count == 0 ? string.Empty : "\n\n訊息:\n" + string.Join("\n", Messages.Take(8));
                return "掃描連結模型: " + LinkCount + "\n" +
                       "找到 MEP 套管需求: " + SourceSleeveCount + "\n" +
                       "建立開孔/預留洞: " + CreatedCount + "\n" +
                       "更新既有開孔/預留洞: " + UpdatedCount + "\n" +
                       "略過: " + SkippedCount + "\n" +
                       "失敗: " + FailedCount + details;
            }
        }

        private static class ArchitecturalOpeningSyncService
        {
            public static OpeningSyncResult SyncFromLinkedSleeves(Document doc)
            {
                var result = new OpeningSyncResult();
                if (EnsureDefaultFamiliesLoaded(doc, result))
                {
                    doc.Regenerate();
                }

                List<FamilySymbol> roundSymbols = FindArchitecturalOpeningSymbols(doc, rectangular: false);
                List<FamilySymbol> rectangularSymbols = FindArchitecturalOpeningSymbols(doc, rectangular: true);
                if (roundSymbols.Count == 0 && rectangularSymbols.Count == 0)
                {
                    result.Messages.Add("找不到建築開孔/預留洞切割用族型。請先載入或放入 Resources\\Families：套管-開口圓形_無.rfa、套管-開口矩形_無.rfa 或 開孔-開口矩形_無.rfa；名稱可含版本尾碼，但需含 開口/開孔 與 圓形/矩形。");
                    AppendOpeningSymbolDiagnostics(doc, result);
                    return result;
                }
                Dictionary<string, FamilyInstance> existingByKey = CollectExistingOpenings(doc);
                foreach (RevitLinkInstance link in new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .OfType<RevitLinkInstance>())
                {
                    Document linkDoc = link.GetLinkDocument();
                    if (linkDoc == null)
                    {
                        continue;
                    }

                    result.LinkCount++;
                    Transform transform = link.GetTotalTransform() ?? Transform.Identity;
                    foreach (FamilyInstance sleeve in CollectLinkedAutoSleeves(linkDoc))
                    {
                        result.SourceSleeveCount++;
                        try
                        {
                            OpeningSource source = BuildSource(doc, link, linkDoc, transform, sleeve);
                            if (source == null || !IsPointInActiveViewScope(doc, source.Point))
                            {
                                result.SkippedCount++;
                                continue;
                            }

                            FamilySymbol symbol = ResolveOpeningSymbol(source, source.IsRectangular ? rectangularSymbols : roundSymbols);
                            if (symbol == null && source.IsRectangular)
                            {
                                result.FailedCount++;
                                result.Messages.Add("缺少矩形開孔/預留洞切割族型，略過 " + source.SourceMark + "。");
                                continue;
                            }
                            if (symbol == null)
                            {
                                result.FailedCount++;
                                result.Messages.Add("缺少圓形開孔/預留洞切割族型，略過 " + source.SourceMark + "。");
                                continue;
                            }
                            string key = BuildSourceKey(source.LinkInstanceId, source.SourceUniqueId);
                            if (existingByKey.TryGetValue(key, out FamilyInstance existing))
                            {
                                UpdateOpening(doc, existing, source, symbol);
                                result.UpdatedCount++;
                            }
                            else
                            {
                                FamilyInstance created = CreateOpening(doc, symbol, source);
                                if (created == null)
                                {
                                    result.FailedCount++;
                                    result.Messages.Add("無法建立開孔: " + source.SourceMark);
                                    continue;
                                }

                                existingByKey[key] = created;
                                result.CreatedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            if (result.Messages.Count < 8)
                            {
                                result.Messages.Add("套管 " + SafeReadMark(sleeve) + " 同步失敗: " + ex.Message);
                            }
                        }
                    }
                }

                if (result.LinkCount == 0)
                {
                    result.Messages.Add("目前模型沒有可讀取的 Revit 連結模型。");
                }
                else if (result.SourceSleeveCount == 0)
                {
                    result.Messages.Add("連結模型中未找到含來源管線 Id 的自動套管。");
                }

                return result;
            }

            private static FamilyInstance CreateOpening(Document doc, FamilySymbol symbol, OpeningSource source)
            {
                Activate(doc, symbol);
                FamilyInstance instance = null;
                Level level = FindNearestLevelBelow(doc, source.Point.Z);
                try
                {
                    instance = level != null
                        ? doc.Create.NewFamilyInstance(source.Point, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural)
                        : doc.Create.NewFamilyInstance(source.Point, symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                }
                catch
                {
                    instance = doc.Create.NewFamilyInstance(source.Point, symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                }

                if (instance != null)
                {
                    WriteOpeningData(doc, instance, source, symbol);
                }

                return instance;
            }

            private static void UpdateOpening(Document doc, FamilyInstance opening, OpeningSource source, FamilySymbol symbol)
            {
                if (opening.Symbol != null && opening.Symbol.Id != symbol.Id)
                {
                    Activate(doc, symbol);
                    opening.Symbol = symbol;
                }

                LocationPoint location = opening.Location as LocationPoint;
                if (location != null)
                {
                    XYZ delta = source.Point - location.Point;
                    if (delta.GetLength() > MoveToleranceFeet)
                    {
                        ElementTransformUtils.MoveElement(doc, opening.Id, delta);
                    }
                }

                WriteOpeningData(doc, opening, source, symbol);
            }

            private static void WriteOpeningData(Document doc, FamilyInstance opening, OpeningSource source, FamilySymbol symbol)
            {
                AlignToDirection(doc, opening, source.Point, source.Direction);
                SetDouble(opening, source.LengthFeet, "長度");
                if (source.IsRectangular)
                {
                    SetDouble(opening, source.WidthFeet, "Width", "寬度");
                    SetDouble(opening, source.HeightFeet, "Height", "高度");
                }
                else
                {
                    SetDouble(opening, source.DiameterFeet, "標稱直徑");
                }

                Schema schema = GetOrCreateSchema();
                Entity entity = new Entity(schema);
                entity.Set(schema.GetField("SourceLinkInstanceId"), source.LinkInstanceId);
                entity.Set(schema.GetField("SourceLinkDocumentTitle"), source.SourceLinkName ?? string.Empty);
                entity.Set(schema.GetField("SourceSleeveUniqueId"), source.SourceUniqueId ?? string.Empty);
                entity.Set(schema.GetField("SourceSleeveMark"), source.SourceMark ?? string.Empty);
                opening.SetEntity(entity);
            }

            private static OpeningSource BuildSource(Document hostDoc, RevitLinkInstance link, Document linkDoc, Transform transform, FamilyInstance sleeve)
            {
                LocationPoint lp = sleeve.Location as LocationPoint;
                if (lp == null)
                {
                    return null;
                }

                XYZ point = transform.OfPoint(lp.Point);
                XYZ direction = transform.OfVector(GetSourceDirection(sleeve));
                if (direction == null || direction.GetLength() < 1e-9)
                {
                    direction = XYZ.BasisX;
                }
                direction = direction.Normalize();

                string crossing = ReadString(sleeve, "穿越構件", "Host Type");
                string dn = ReadString(sleeve, "管道標稱直徑", "DN", "Nominal Diameter");
                string mark = ReadString(sleeve, BuiltInParameter.ALL_MODEL_MARK, "套管編號", "開孔編號", "編號", "Sleeve Number");
                if (string.IsNullOrWhiteSpace(mark))
                {
                    mark = "OPEN-" + ElementIdToInt(sleeve.Id);
                }

                double length = ReadDouble(sleeve, 200 * MmToFeet, "套管長度", "長度", "Length", "Depth");
                double diameter = ReadDouble(sleeve, 100 * MmToFeet, "套管直徑", "直徑", "Diameter", "Diatot", "邊界寬度", "大小");
                double width = ReadDouble(sleeve, diameter, "開孔寬度", "寬度", "Width", "Sleeve Width");
                double height = ReadDouble(sleeve, diameter, "開孔高度", "高度", "Height", "Sleeve Height");

                return new OpeningSource
                {
                    LinkInstanceId = ElementIdToInt(link.Id),
                    SourceLinkName = string.IsNullOrWhiteSpace(linkDoc.Title) ? link.Name : linkDoc.Title,
                    SourceUniqueId = sleeve.UniqueId,
                    SourceMark = mark,
                    Point = point,
                    Direction = direction,
                    LengthFeet = length,
                    DiameterFeet = diameter,
                    WidthFeet = width,
                    HeightFeet = height,
                    IsRectangular = IsRectangularSleeve(sleeve),
                    Crossing = string.IsNullOrWhiteSpace(crossing) ? "開孔" : crossing,
                    DnText = dn,
                    SystemName = ReadString(sleeve, "系統名稱", "System Name", "Source System Name", "備註", "Comments"),
                    LevelName = ReadString(sleeve, "套管樓層", "樓層名稱", "Level Name", "樓層")
                };
            }

            private static IEnumerable<FamilyInstance> CollectLinkedAutoSleeves(Document linkDoc)
            {
                return new FilteredElementCollector(linkDoc)
                    .OfClass(typeof(FamilyInstance))
                    .OfType<FamilyInstance>()
                    .Where(IsAutoGeneratedSleeve);
            }

            private static bool IsAutoGeneratedSleeve(FamilyInstance instance)
            {
                if (instance == null || instance.Category == null)
                {
                    return false;
                }

                string sourcePipeId = ReadString(instance, "來源管線Id", "Pipe Id", "Source Pipe Id");
                if (string.IsNullOrWhiteSpace(sourcePipeId))
                {
                    return false;
                }

                string name = ((instance.Symbol?.FamilyName ?? string.Empty) + " " + (instance.Symbol?.Name ?? string.Empty)).ToLowerInvariant();
                return name.Contains("套管") || name.Contains("開孔") || name.Contains("sleeve") || name.Contains("opening");
            }

            private static Dictionary<string, FamilyInstance> CollectExistingOpenings(Document doc)
            {
                Schema schema = GetOrCreateSchema();
                var result = new Dictionary<string, FamilyInstance>(StringComparer.OrdinalIgnoreCase);
                foreach (FamilyInstance instance in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .OfType<FamilyInstance>())
                {
                    Entity entity = instance.GetEntity(schema);
                    if (!entity.IsValid())
                    {
                        continue;
                    }

                    int linkId = entity.Get<int>(schema.GetField("SourceLinkInstanceId"));
                    string uniqueId = entity.Get<string>(schema.GetField("SourceSleeveUniqueId"));
                    if (!string.IsNullOrWhiteSpace(uniqueId))
                    {
                        result[BuildSourceKey(linkId, uniqueId)] = instance;
                    }
                }

                return result;
            }

            private static string BuildSourceKey(int linkInstanceId, string sourceUniqueId)
            {
                return linkInstanceId + "|" + (sourceUniqueId ?? string.Empty);
            }

            private static Schema GetOrCreateSchema()
            {
                Schema schema = Schema.Lookup(OpeningSyncSchemaGuid);
                if (schema != null)
                {
                    return schema;
                }

                SchemaBuilder builder = new SchemaBuilder(OpeningSyncSchemaGuid);
                builder.SetSchemaName("HB_BIM_ArchitecturalOpeningSync");
                builder.SetReadAccessLevel(AccessLevel.Public);
                builder.SetWriteAccessLevel(AccessLevel.Public);
                builder.AddSimpleField("SourceLinkInstanceId", typeof(int));
                builder.AddSimpleField("SourceLinkDocumentTitle", typeof(string));
                builder.AddSimpleField("SourceSleeveUniqueId", typeof(string));
                builder.AddSimpleField("SourceSleeveMark", typeof(string));
                return builder.Finish();
            }

            private static bool EnsureDefaultFamiliesLoaded(Document doc, OpeningSyncResult result)
            {
                bool loadedAny = false;
                string assemblyDir = Path.GetDirectoryName(typeof(CmdArchitecturalOpeningFromSleeves).Assembly.Location) ?? string.Empty;
                string familiesDir = Path.Combine(assemblyDir, "Resources", "Families");
                foreach (string fileName in new[] { "套管-開口圓形_無.rfa", "套管-開口矩形_無.rfa", "開孔-開口矩形_無.rfa", "開孔-矩形_無.rfa" })
                {
                    string familyName = Path.GetFileNameWithoutExtension(fileName);
                    if (IsFamilyLoaded(doc, familyName))
                    {
                        continue;
                    }

                    string path = Path.Combine(familiesDir, fileName);
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    try
                    {
                        Autodesk.Revit.DB.Family loaded;
                        bool loadedOk = doc.LoadFamily(path, new OpeningFamilyLoadOptions(), out loaded);
                        if (loadedOk || loaded != null)
                        {
                            loadedAny = true;
                        }
                        if (!loadedOk && loaded == null)
                        {
                            result.Messages.Add("族群載入未完成: " + fileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Messages.Add("族群載入失敗 " + fileName + ": " + ex.Message);
                    }
                }
            
                return loadedAny;
            }

            private static bool IsFamilyLoaded(Document doc, string familyName)
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Family))
                    .OfType<Autodesk.Revit.DB.Family>()
                    .Any(family => string.Equals(family.Name, familyName, StringComparison.OrdinalIgnoreCase));
            }
            private static List<FamilySymbol> FindArchitecturalOpeningSymbols(Document doc, bool rectangular)
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfType<FamilySymbol>()
                    .Where(symbol => IsArchitecturalOpeningSymbol(symbol, rectangular))
                    .OrderBy(symbol => symbol.FamilyName)
                    .ThenBy(symbol => symbol.Name)
                    .ToList();
            }

            private static bool IsArchitecturalOpeningSymbol(FamilySymbol symbol, bool rectangular)
            {
                if (symbol == null)
                {
                    return false;
                }

                string text = GetSymbolSearchText(symbol);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                string[] includeTokens = { "套管-開口", "開孔-開口", "建築開孔", "預留洞", "洞口", "開孔", "切割", "opening" };
                string[] rectangularTokens = { "矩形", "方形", "rect", "rectangle" };
                string[] roundTokens = { "圓形", "圆形", "round", "circle" };
                string[] excludeTokens = { "套管-圓形_無", "管附件-套管", "pipe sleeve" };

                return ContainsAny(text, includeTokens) &&
                       !ContainsAny(text, excludeTokens) &&
                       (rectangular ? ContainsAny(text, rectangularTokens) : ContainsAny(text, roundTokens));
            }

            private static string GetSymbolSearchText(FamilySymbol symbol)
            {
                return ((symbol.FamilyName ?? string.Empty) + " " + (symbol.Name ?? string.Empty) + " " + (symbol.Family?.Name ?? string.Empty)).ToLowerInvariant();
            }

            private static void AppendOpeningSymbolDiagnostics(Document doc, OpeningSyncResult result)
            {
                if (result.Messages.Count >= 8)
                {
                    return;
                }

                var candidates = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfType<FamilySymbol>()
                    .Where(symbol => ContainsAny(GetSymbolSearchText(symbol), new[] { "開口", "開孔", "預留洞", "opening" }))
                    .Select(symbol => (symbol.FamilyName ?? string.Empty) + ": " + (symbol.Name ?? string.Empty) + " [" + (symbol.Category?.Name ?? "無類別") + "]")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList();

                if (candidates.Count == 0)
                {
                    result.Messages.Add("診斷: 目前模型沒有找到名稱含 開口/開孔/預留洞/opening 的族型。");
                }
                else
                {
                    result.Messages.Add("診斷: 找到相近族型但未能判定形狀，請確認名稱含 圓形 或 矩形: " + string.Join("; ", candidates));
                }
            }

            private static FamilySymbol ResolveOpeningSymbol(OpeningSource source, IList<FamilySymbol> symbols)
            {
                if (source == null || symbols == null || symbols.Count == 0)
                {
                    return null;
                }

                double targetMm = source.IsRectangular
                    ? Math.Max(source.WidthFeet, source.HeightFeet) / MmToFeet
                    : source.DiameterFeet / MmToFeet;
                int dn = ExtractFirstPositiveInt(source.DnText);

                return symbols
                    .OrderBy(symbol => GetOpeningSymbolMatchScore(symbol, dn, targetMm))
                    .ThenBy(symbol => symbol.FamilyName)
                    .ThenBy(symbol => symbol.Name)
                    .FirstOrDefault();
            }

            private static double GetOpeningSymbolMatchScore(FamilySymbol symbol, int dn, double targetMm)
            {
                string name = ((symbol.FamilyName ?? string.Empty) + " " + (symbol.Name ?? string.Empty)).ToLowerInvariant();
                if (dn > 0)
                {
                    string dnText = dn.ToString();
                    if (name.Contains("dn" + dnText) || name.Contains(dnText + "a") || name.Contains(dnText + "mm") || name.Contains("-" + dnText))
                    {
                        return 0;
                    }
                }

                double symbolFeet = ReadDouble(symbol, 0, "管道標稱直徑", "標稱直徑", "開孔直徑", "套管直徑", "直徑", "Diameter", "Diatot", "邊界寬度", "開孔寬度", "寬度", "Width");
                if (symbolFeet > 0)
                {
                    double valueMm = symbolFeet / MmToFeet;
                    return Math.Abs(valueMm - targetMm) + 10;
                }

                int nameNumber = ExtractFirstPositiveInt(name);
                if (nameNumber > 0 && targetMm > 0)
                {
                    return Math.Abs(nameNumber - targetMm) + 20;
                }

                return 100000;
            }

            private static int ExtractFirstPositiveInt(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return 0;
                string digits = string.Empty;
                foreach (char c in text)
                {
                    if (char.IsDigit(c))
                    {
                        digits += c;
                    }
                    else if (digits.Length > 0)
                    {
                        break;
                    }
                }

                int value;
                return int.TryParse(digits, out value) && value > 0 ? value : 0;
            }

            private static int ElementIdToInt(ElementId id)
            {
                if (id == null || id == ElementId.InvalidElementId)
                {
                    return -1;
                }

                var valueProperty = typeof(ElementId).GetProperty("Value");
                if (valueProperty != null)
                {
                    object value = valueProperty.GetValue(id, null);
                    if (value is long longValue)
                    {
                        if (longValue > int.MaxValue) return int.MaxValue;
                        if (longValue < int.MinValue) return int.MinValue;
                        return (int)longValue;
                    }

                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }

                var integerValueProperty = typeof(ElementId).GetProperty("IntegerValue");
                if (integerValueProperty != null)
                {
                    object value = integerValueProperty.GetValue(id, null);
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }

                return -1;
            }
            private static bool ContainsAny(string text, IEnumerable<string> tokens)
            {
                return tokens.Any(token => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            private static void Activate(Document doc, FamilySymbol symbol)
            {
                if (symbol != null && !symbol.IsActive)
                {
                    symbol.Activate();
                }
            }

            private static XYZ GetSourceDirection(FamilyInstance instance)
            {
                try
                {
                    if (instance.FacingOrientation != null && instance.FacingOrientation.GetLength() > 1e-9)
                    {
                        return instance.FacingOrientation;
                    }
                }
                catch { }

                try
                {
                    if (instance.HandOrientation != null && instance.HandOrientation.GetLength() > 1e-9)
                    {
                        return instance.HandOrientation;
                    }
                }
                catch { }

                return XYZ.BasisX;
            }

            private static bool IsRectangularSleeve(FamilyInstance sleeve)
            {
                string text = ((sleeve.Symbol?.FamilyName ?? string.Empty) + " " + (sleeve.Symbol?.Name ?? string.Empty)).ToLowerInvariant();
                if (text.Contains("開孔") || text.Contains("opening") || text.Contains("矩形"))
                {
                    return true;
                }

                return HasParameter(sleeve, "開孔寬度", "寬度", "Width") || HasParameter(sleeve, "開孔高度", "高度", "Height");
            }

            private static void AlignToDirection(Document doc, FamilyInstance instance, XYZ point, XYZ targetDirection)
            {
                try
                {
                    XYZ current = GetSourceDirection(instance).Normalize();
                    XYZ target = targetDirection.Normalize();
                    double dot = Math.Max(-1.0, Math.Min(1.0, current.DotProduct(target)));
                    double angle = Math.Acos(dot);
                    if (angle < 0.001)
                    {
                        return;
                    }

                    XYZ axis = current.CrossProduct(target);
                    if (axis.GetLength() < 1e-9)
                    {
                        axis = Math.Abs(current.DotProduct(XYZ.BasisZ)) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
                    }
                    axis = axis.Normalize();
                    ElementTransformUtils.RotateElement(doc, instance.Id, Line.CreateUnbound(point, axis), angle);
                }
                catch
                {
                    // Some families constrain rotation; keep position/size data synced even if orientation is locked.
                }
            }

            private static Level FindNearestLevelBelow(Document doc, double z)
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .OfType<Level>()
                    .OrderByDescending(level => level.Elevation)
                    .FirstOrDefault(level => level.Elevation <= z + 0.001)
                    ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).OfType<Level>().OrderBy(level => Math.Abs(level.Elevation - z)).FirstOrDefault();
            }

            private static bool IsPointInActiveViewScope(Document doc, XYZ point)
            {
                View view = doc.ActiveView;
                if (view == null || point == null)
                {
                    return true;
                }

                try
                {
                    View3D view3d = view as View3D;
                    if (view3d != null && view3d.IsSectionBoxActive)
                    {
                        BoundingBoxXYZ box = view3d.GetSectionBox();
                        Transform inverse = box.Transform.Inverse;
                        XYZ local = inverse.OfPoint(point);
                        return local.X >= box.Min.X - 0.001 && local.X <= box.Max.X + 0.001 &&
                               local.Y >= box.Min.Y - 0.001 && local.Y <= box.Max.Y + 0.001 &&
                               local.Z >= box.Min.Z - 0.001 && local.Z <= box.Max.Z + 0.001;
                    }

                    if (view.CropBoxActive)
                    {
                        BoundingBoxXYZ box = view.CropBox;
                        Transform inverse = box.Transform.Inverse;
                        XYZ local = inverse.OfPoint(point);
                        return local.X >= box.Min.X - 0.001 && local.X <= box.Max.X + 0.001 &&
                               local.Y >= box.Min.Y - 0.001 && local.Y <= box.Max.Y + 0.001 &&
                               local.Z >= box.Min.Z - 0.001 && local.Z <= box.Max.Z + 0.001;
                    }
                }
                catch { }

                return true;
            }

            private static bool HasParameter(Element element, params string[] names)
            {
                return names.Any(name => element.LookupParameter(name) != null);
            }

            private static string SafeReadMark(FamilyInstance instance)
            {
                return ReadString(instance, BuiltInParameter.ALL_MODEL_MARK, "套管編號", "編號", "Sleeve Number");
            }

            private static string ReadString(Element element, BuiltInParameter builtIn, params string[] names)
            {
                try
                {
                    Parameter builtInParameter = element.get_Parameter(builtIn);
                    if (builtInParameter != null && builtInParameter.HasValue)
                    {
                        string value = builtInParameter.AsString() ?? builtInParameter.AsValueString();
                        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                    }
                }
                catch { }

                return ReadString(element, names);
            }

            private static string ReadString(Element element, params string[] names)
            {
                if (element == null) return string.Empty;
                foreach (string name in names)
                {
                    Parameter parameter = element.LookupParameter(name);
                    if (parameter == null || !parameter.HasValue) continue;
                    string value = parameter.AsString() ?? parameter.AsValueString();
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }

                return string.Empty;
            }

            private static double ReadDouble(Element element, double fallback, params string[] names)
            {
                if (element == null) return fallback;
                foreach (string name in names)
                {
                    Parameter parameter = element.LookupParameter(name);
                    if (parameter == null || !parameter.HasValue) continue;
                    if (parameter.StorageType == StorageType.Double)
                    {
                        double value = parameter.AsDouble();
                        if (value > 0) return value;
                    }
                }

                return fallback;
            }

            private static void SetString(FamilyInstance instance, string value, BuiltInParameter builtIn, params string[] names)
            {
                try
                {
                    Parameter parameter = instance.get_Parameter(builtIn);
                    if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                    {
                        parameter.Set(value ?? string.Empty);
                        return;
                    }
                }
                catch { }

                SetString(instance, value, names);
            }

            private static void SetString(FamilyInstance instance, string value, params string[] names)
            {
                foreach (string name in names)
                {
                    Parameter parameter = instance.LookupParameter(name);
                    if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                    {
                        parameter.Set(value ?? string.Empty);
                        return;
                    }
                }
            }

            private static void SetDouble(FamilyInstance instance, double value, params string[] names)
            {
                if (value <= 0) return;
                foreach (string name in names)
                {
                    Parameter parameter = instance.LookupParameter(name);
                    if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Double)
                    {
                        parameter.Set(value);
                        return;
                    }
                }
            }
        }

        private sealed class OpeningSource
        {
            public int LinkInstanceId { get; set; }
            public string SourceLinkName { get; set; }
            public string SourceUniqueId { get; set; }
            public string SourceMark { get; set; }
            public XYZ Point { get; set; }
            public XYZ Direction { get; set; }
            public double LengthFeet { get; set; }
            public double DiameterFeet { get; set; }
            public double WidthFeet { get; set; }
            public double HeightFeet { get; set; }
            public bool IsRectangular { get; set; }
            public string Crossing { get; set; }
            public string DnText { get; set; }
            public string SystemName { get; set; }
            public string LevelName { get; set; }
        }

        private sealed class OpeningFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(Autodesk.Revit.DB.Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Project;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}



