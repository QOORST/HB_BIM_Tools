using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 結構模板分析器 - 實現完整的BIM模板分析功能
    /// </summary>
    public static class StructuralFormworkAnalyzer
    {
        public class AnalysisOptions
        {
            public bool IncludeStructuralBottom { get; set; } = true;
            public bool IncludeFoundationBottom { get; set; } = true;
            public bool ActiveViewOnly { get; set; }
            public ElementId ViewId { get; set; } = ElementId.InvalidElementId;
            public IReadOnlyCollection<ElementId> TargetElementIds { get; set; }
            public Action<ElementId> CurrentElementIdChanged { get; set; }
        }

        // 鋼筋密度常數 (kg/m³)
        private const double REBAR_DENSITY_KG_M3 = 150.0; // 平均鋼筋密度
        
        // 模板類型枚舉
        public enum FormworkType
        {
            Primary,      // 主要模板 (紅色)
            Secondary,    // 次要模板 (黃色)
            Protection,   // 保護區域 (淺藍色)
            Waterproof,   // 防水處理 (深藍色)
            Joint         // 接縫處理 (綠色)
        }

        internal static StructuralAnalysisResult AnalyzeProject(Document doc, AnalysisOptions options = null)
        {
            options = options ?? new AnalysisOptions();
            var result = new StructuralAnalysisResult();
            var totalTimer = Stopwatch.StartNew();
            
            FormworkEngine.Debug.Log("開始結構模板專案分析");

            // 1. 收集所有結構元素
            var collectionTimer = Stopwatch.StartNew();
            CurvedMeshBudget.Checkpoint("分析：收集結構元素");
            var structuralElements = CollectStructuralElements(doc, options);
            CurvedMeshBudget.Checkpoint($"分析：已收集 {structuralElements.Count} 個結構候選");
            collectionTimer.Stop();

            var targetIds = AnalysisScope.ResolveCandidateIds(
                structuralElements.Select(e => e.Id.GetIdValue()),
                options.TargetElementIds?.Select(id => id.GetIdValue()));
            var targetElements = structuralElements
                .Where(e => targetIds.Contains(e.Id.GetIdValue()))
                .ToList();

            var modelBoundsById = new Dictionary<long, BoundingBoxXYZ>();
            for (int i = 0; i < structuralElements.Count; i++)
            {
                if ((i & 127) == 0)
                    CurvedMeshBudget.Checkpoint($"分析：準備包圍盒 {i}/{structuralElements.Count}");
                var candidate = structuralElements[i];
                modelBoundsById[candidate.Id.GetIdValue()] = GetModelSpaceBoundingBox(candidate);
            }

            result.Metrics.CandidateElementCount = structuralElements.Count;
            result.Metrics.RequestedTargetCount = options.TargetElementIds?.Count ?? structuralElements.Count;
            result.Metrics.AnalyzedElementCount = targetElements.Count;
            result.Metrics.CollectionMilliseconds = collectionTimer.ElapsedMilliseconds;

            FormworkEngine.Debug.Log(
                "分析範圍 - 要求 {0}、候選 {1}、實際分析 {2} 個元素",
                result.Metrics.RequestedTargetCount,
                result.Metrics.CandidateElementCount,
                result.Metrics.AnalyzedElementCount);

            // 2. 分析每個元素
            var analysisTimer = Stopwatch.StartNew();
            int targetIndex = 0;
            foreach (var element in targetElements)
            {
                targetIndex++;
                options.CurrentElementIdChanged?.Invoke(element.Id);
                CurvedMeshBudget.Checkpoint($"分析：元素 {targetIndex}/{targetElements.Count}（ID {element.Id.GetIdValue()}）");
                var analysis = AnalyzeElement(doc, element, structuralElements, modelBoundsById, options, result.Metrics);
                CurvedMeshBudget.ThrowIfExceeded();
                result.ElementAnalyses[element] = analysis;
                
                // 累加統計
                result.TotalElements++;
                result.TotalFormworkArea += analysis.FormworkArea;
                result.TotalConcreteVolume += analysis.ConcreteVolume;
                
                // 分類統計
                var categoryName = GetElementCategoryName(element);
                if (!result.CategorySummary.ContainsKey(categoryName))
                {
                    result.CategorySummary[categoryName] = new CategorySummary();
                }
                
                var summary = result.CategorySummary[categoryName];
                summary.Count++;
                summary.FormworkArea += analysis.FormworkArea;
                summary.ConcreteVolume += analysis.ConcreteVolume;
                CurvedMeshBudget.Checkpoint($"分析：已完成元素 {targetIndex}/{targetElements.Count}（ID {element.Id.GetIdValue()}）");
            }
            analysisTimer.Stop();
            result.Metrics.ElementAnalysisMilliseconds = analysisTimer.ElapsedMilliseconds;

            // 3. 計算鋼筋重量
            result.EstimatedRebarWeight = result.TotalConcreteVolume * REBAR_DENSITY_KG_M3 / 1000.0; // 轉換為噸

            // 4. 分析構件相互關係
            var interactionTimer = Stopwatch.StartNew();
            CurvedMeshBudget.Checkpoint("分析：彙整構件關係");
            AnalyzeElementInteractions(result);
            CurvedMeshBudget.Checkpoint("分析：構件關係彙整完成");
            interactionTimer.Stop();
            result.Metrics.InteractionMilliseconds = interactionTimer.ElapsedMilliseconds;

            totalTimer.Stop();
            result.Metrics.TotalMilliseconds = totalTimer.ElapsedMilliseconds;

            FormworkEngine.Debug.Log("分析完成 - 總面積: {0:F2}m², 總體積: {1:F2}m³", 
                result.TotalFormworkArea, result.TotalConcreteVolume);
            FormworkEngine.Debug.Log(
                "分析效能 - 收集 {0} ms、元素分析 {1} ms、關係彙整 {2} ms、總計 {3} ms；幾何擷取 {4} 次、布林嘗試 {5} 次",
                result.Metrics.CollectionMilliseconds,
                result.Metrics.ElementAnalysisMilliseconds,
                result.Metrics.InteractionMilliseconds,
                result.Metrics.TotalMilliseconds,
                result.Metrics.GeometryExtractionCount,
                result.Metrics.BooleanOperationCount);

            return result;
        }

        private static List<Element> CollectStructuralElements(Document doc, AnalysisOptions options)
        {
            var elements = new List<Element>();
            
            var categories = ElementCategorizer.GetStructuralCategories(includeFoundation: true);

            foreach (var category in categories)
            {
                var collector = options.ActiveViewOnly && options.ViewId != ElementId.InvalidElementId
                    ? new FilteredElementCollector(doc, options.ViewId)
                    : new FilteredElementCollector(doc);

                collector = collector
                    .OfCategory(category)
                    .WhereElementIsNotElementType();
                
                elements.AddRange(collector.ToElements());
            }

            return elements
                .GroupBy(e => e.Id.GetIdValue())
                .Select(g => g.First())
                .ToList();
        }

        private static ElementFormworkAnalysis AnalyzeElement(
            Document doc,
            Element element,
            List<Element> allElements,
            IReadOnlyDictionary<long, BoundingBoxXYZ> modelBoundsById,
            AnalysisOptions options,
            StructuralAnalysisMetrics metrics)
        {
            bool includeBottom = ShouldIncludeBottom(element, options);
            var analysis = new ElementFormworkAnalysis
            {
                Element = element,
                ElementType = GetElementType(element),
                ConcreteVolume = CalculateConcreteVolume(element, metrics),
                FormworkInfo = FormworkEngine.AnalyzeHost(doc, element, includeBottom)
            };

            // 分析與其他元素的關係
            analysis.ConnectedElements = FindConnectedElements(element, allElements, modelBoundsById, metrics);
            
            // 計算模板面積（根據連接情況調整）
            analysis.FormworkArea = CalculateFormworkArea(analysis);
            
            // 設定模板類型和顏色
            analysis.FormworkTypes = DetermineFormworkTypes(analysis);

            FormworkEngine.Debug.Log("分析元素 {0} - 體積: {1:F3}m³, 模板面積: {2:F3}m²", 
                element.Id.GetIdValue(), analysis.ConcreteVolume, analysis.FormworkArea);

            return analysis;
        }

        private static bool ShouldIncludeBottom(Element element, AnalysisOptions options)
        {
            if (element?.Category?.Id == null)
                return options.IncludeStructuralBottom;

            if (element.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_StructuralFoundation)
                return options.IncludeFoundationBottom;

            return options.IncludeStructuralBottom;
        }

        private static StructuralElementType GetElementType(Element element)
        {
            if (element is Wall) return StructuralElementType.Wall;
            if (element is Floor) return StructuralElementType.Slab;
            if (element.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_StructuralColumns)
                return StructuralElementType.Column;
            if (element.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_StructuralFraming)
                return StructuralElementType.Beam;
            if (element.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_StructuralFoundation)
                return StructuralElementType.Foundation;
            if (ElementCategorizer.IsStairs(element))
                return StructuralElementType.Stair;

            return StructuralElementType.Other;
        }

        private static double CalculateConcreteVolume(Element element, StructuralAnalysisMetrics metrics)
        {
            try
            {
                metrics.GeometryExtractionCount++;
                var solids = FormworkEngine.GetElementSolids(element).ToList();
                double totalVolume = 0;
                
                foreach (var solid in solids)
                {
                    if (solid != null && solid.Volume > 1e-6)
                    {
                        totalVolume += solid.Volume;
                    }
                }
                
                // 轉換為立方公尺
                return UnitUtils.ConvertFromInternalUnits(totalVolume, UnitTypeId.CubicMeters);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (CurvedMeshLimitException)
            {
                throw;
            }
            catch
            {
                return 0;
            }
        }

        private static List<ElementConnection> FindConnectedElements(
            Element element,
            List<Element> allElements,
            IReadOnlyDictionary<long, BoundingBoxXYZ> modelBoundsById,
            StructuralAnalysisMetrics metrics)
        {
            var connections = new List<ElementConnection>();
            metrics.GeometryExtractionCount++;
            var elementSolids = FormworkEngine.GetElementSolids(element).ToList();
            
            if (elementSolids.Count == 0) return connections;

            var elementBB = GetElementBoundingBox(elementSolids);
            modelBoundsById.TryGetValue(element.Id.GetIdValue(), out var elementModelBB);
            var searchTolerance = UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters); // 100mm

            int candidateIndex = 0;
            foreach (var other in allElements)
            {
                candidateIndex++;
                if ((candidateIndex & 31) == 0)
                    CurvedMeshBudget.Checkpoint($"分析：鄰接粗篩 {candidateIndex}/{allElements.Count}（宿主 ID {element.Id.GetIdValue()}）");
                if (other.Id == element.Id || !ElementCategorizer.CanDeductFormwork(other)) continue;

                // Element bounding boxes are transformed to model coordinates before the cheap gate.
                // Missing/invalid boxes conservatively pass so this optimization cannot hide a real connection.
                modelBoundsById.TryGetValue(other.Id.GetIdValue(), out var otherModelBB);
                if (elementModelBB != null && otherModelBB != null &&
                    !BoundingBoxesIntersect(elementModelBB, otherModelBB, searchTolerance))
                    continue;

                CurvedMeshBudget.Checkpoint($"分析：擷取鄰接幾何（宿主 ID {element.Id.GetIdValue()}／候選 ID {other.Id.GetIdValue()}）");
                metrics.GeometryExtractionCount++;
                var otherSolids = FormworkEngine.GetElementSolids(other).ToList();
                CurvedMeshBudget.ThrowIfExceeded();
                if (otherSolids.Count == 0) continue;

                var otherBB = GetElementBoundingBox(otherSolids);
                
                // 快速包圍盒檢查
                if (elementBB != null && otherBB != null &&
                    !BoundingBoxesIntersect(elementBB, otherBB, searchTolerance)) continue;

                // 詳細相交檢查
                var connection = AnalyzeConnection(elementSolids, otherSolids, element, other, metrics);
                if (connection != null)
                {
                    connections.Add(connection);
                }
            }

            return connections;
        }

        private static ElementConnection AnalyzeConnection(
            List<Solid> solids1,
            List<Solid> solids2,
            Element element1,
            Element element2,
            StructuralAnalysisMetrics metrics)
        {
            try
            {
                double totalIntersectionVolume = 0;
                double totalContactArea = 0;

                foreach (var solid1 in solids1)
                {
                    foreach (var solid2 in solids2)
                    {
                        if (solid1 == null || solid2 == null) continue;

                        try
                        {
                            CurvedMeshBudget.Checkpoint($"分析：布林相交（{element1.Id.GetIdValue()}／{element2.Id.GetIdValue()}）");
                            metrics.BooleanOperationCount++;
                            using (var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                                solid1, solid2, BooleanOperationsType.Intersect))
                            {
                                CurvedMeshBudget.Checkpoint($"分析：布林相交完成（{element1.Id.GetIdValue()}／{element2.Id.GetIdValue()}）");
                                if (intersection != null && intersection.Volume > 1e-9)
                                {
                                    totalIntersectionVolume += intersection.Volume;

                                    // 估算接觸面積
                                    totalContactArea += EstimateContactArea(intersection);
                                }
                            }
                        }
                        catch (System.OperationCanceledException)
                        {
                            throw;
                        }
                        catch (CurvedMeshLimitException)
                        {
                            throw;
                        }
                        catch
                        {
                            // 忽略布林運算錯誤
                        }
                    }
                }

                if (totalIntersectionVolume > 1e-9)
                {
                    return new ElementConnection
                    {
                        ConnectedElement = element2,
                        ConnectionType = DetermineConnectionType(element1, element2),
                        IntersectionVolume = UnitUtils.ConvertFromInternalUnits(totalIntersectionVolume, UnitTypeId.CubicMeters),
                        ContactArea = UnitUtils.ConvertFromInternalUnits(totalContactArea, UnitTypeId.SquareMeters)
                    };
                }
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (CurvedMeshLimitException)
            {
                throw;
            }
            catch
            {
                // 忽略錯誤
            }

            return null;
        }

        private static ConnectionType DetermineConnectionType(Element element1, Element element2)
        {
            var type1 = GetElementType(element1);
            var type2 = GetElementType(element2);

            if ((type1 == StructuralElementType.Column && type2 == StructuralElementType.Beam) ||
                (type1 == StructuralElementType.Beam && type2 == StructuralElementType.Column))
                return ConnectionType.ColumnBeam;

            if ((type1 == StructuralElementType.Column && type2 == StructuralElementType.Slab) ||
                (type1 == StructuralElementType.Slab && type2 == StructuralElementType.Column))
                return ConnectionType.ColumnSlab;

            if ((type1 == StructuralElementType.Beam && type2 == StructuralElementType.Slab) ||
                (type1 == StructuralElementType.Slab && type2 == StructuralElementType.Beam))
                return ConnectionType.BeamSlab;

            if ((type1 == StructuralElementType.Wall && type2 == StructuralElementType.Slab) ||
                (type1 == StructuralElementType.Slab && type2 == StructuralElementType.Wall))
                return ConnectionType.WallSlab;

            return ConnectionType.Other;
        }

        private static double EstimateContactArea(Solid intersection)
        {
            // 簡化的接觸面積估算
            var bb = intersection.GetBoundingBox();
            var dimensions = new[]
            {
                bb.Max.X - bb.Min.X,
                bb.Max.Y - bb.Min.Y,
                bb.Max.Z - bb.Min.Z
            };
            
            Array.Sort(dimensions);
            
            // 使用最大的兩個維度估算面積
            return dimensions[1] * dimensions[2];
        }

        private static double CalculateFormworkArea(ElementFormworkAnalysis analysis)
        {
            // 使用新的準確計算方法
            return FormworkQuantityCalculator.CalculateFormworkArea(analysis.Element, analysis.ConnectedElements);
        }

        private static double CalculateConnectionAdjustment(ElementConnection connection, StructuralElementType elementType)
        {
            // 根據連接類型和元素類型計算調整量
            double baseAdjustment = -connection.ContactArea * 0.8; // 連接處通常減少模板面積

            switch (connection.ConnectionType)
            {
                case ConnectionType.ColumnBeam:
                    if (elementType == StructuralElementType.Column)
                        return baseAdjustment * 1.2; // 柱在梁柱接合處減少更多
                    break;
                
                case ConnectionType.BeamSlab:
                    if (elementType == StructuralElementType.Beam)
                        return baseAdjustment * 0.6; // 梁與板接合處部分保留
                    break;
            }

            return baseAdjustment;
        }

        private static Dictionary<FormworkType, double> DetermineFormworkTypes(ElementFormworkAnalysis analysis)
        {
            var types = new Dictionary<FormworkType, double>();
            
            // 主要模板面積
            double primaryArea = analysis.FormworkArea * 0.8;
            types[FormworkType.Primary] = primaryArea;
            
            // 根據連接情況分配其他類型
            if (analysis.ConnectedElements.Count > 0)
            {
                types[FormworkType.Joint] = analysis.FormworkArea * 0.1;
                types[FormworkType.Protection] = analysis.FormworkArea * 0.1;
            }
            else
            {
                types[FormworkType.Secondary] = analysis.FormworkArea * 0.2;
            }

            return types;
        }

        private static void AnalyzeElementInteractions(StructuralAnalysisResult result)
        {
            // 分析整體構件相互關係
            FormworkEngine.Debug.Log("分析構件相互關係...");
            
            var interactionSummary = new Dictionary<string, int>();
            
            foreach (var analysis in result.ElementAnalyses.Values)
            {
                foreach (var connection in analysis.ConnectedElements)
                {
                    var key = $"{analysis.ElementType}-{GetElementType(connection.ConnectedElement)}";
                    if (!interactionSummary.ContainsKey(key))
                        interactionSummary[key] = 0;
                    interactionSummary[key]++;
                }
            }
            
            foreach (var interaction in interactionSummary)
            {
                FormworkEngine.Debug.Log("發現 {0} 個 {1} 連接", interaction.Value, interaction.Key);
            }
        }

        private static string GetElementCategoryName(Element element)
        {
            switch (GetElementType(element))
            {
                case StructuralElementType.Column: return "結構柱";
                case StructuralElementType.Beam: return "結構梁";
                case StructuralElementType.Slab: return "樓板";
                case StructuralElementType.Wall: return "牆";
                case StructuralElementType.Foundation: return "基礎";
                case StructuralElementType.Stair: return "樓梯";
                default: return "其他";
            }
        }

        private static BoundingBoxXYZ GetElementBoundingBox(List<Solid> solids)
        {
            BoundingBoxXYZ result = null;
            
            foreach (var solid in solids)
            {
                if (solid == null) continue;
                
                var bb = solid.GetBoundingBox();
                if (result == null)
                {
                    result = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max };
                }
                else
                {
                    result.Min = new XYZ(
                        Math.Min(result.Min.X, bb.Min.X),
                        Math.Min(result.Min.Y, bb.Min.Y),
                        Math.Min(result.Min.Z, bb.Min.Z));
                    result.Max = new XYZ(
                        Math.Max(result.Max.X, bb.Max.X),
                        Math.Max(result.Max.Y, bb.Max.Y),
                        Math.Max(result.Max.Z, bb.Max.Z));
                }
            }
            
            return result;
        }

        private static BoundingBoxXYZ GetModelSpaceBoundingBox(Element element)
        {
            try
            {
                var bounds = element?.get_BoundingBox(null);
                if (bounds == null) return null;

                var points = new List<XYZ>(8);
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    var local = new XYZ(
                        x == 0 ? bounds.Min.X : bounds.Max.X,
                        y == 0 ? bounds.Min.Y : bounds.Max.Y,
                        z == 0 ? bounds.Min.Z : bounds.Max.Z);
                    points.Add(bounds.Transform.OfPoint(local));
                }

                return new BoundingBoxXYZ
                {
                    Min = new XYZ(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
                    Max = new XYZ(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z))
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool BoundingBoxesIntersect(BoundingBoxXYZ bb1, BoundingBoxXYZ bb2, double tolerance)
        {
            return !(bb1.Min.X > bb2.Max.X + tolerance || bb1.Max.X < bb2.Min.X - tolerance ||
                    bb1.Min.Y > bb2.Max.Y + tolerance || bb1.Max.Y < bb2.Min.Y - tolerance ||
                    bb1.Min.Z > bb2.Max.Z + tolerance || bb1.Max.Z < bb2.Min.Z - tolerance);
        }
    }

    #region 數據結構定義

    public class StructuralAnalysisResult
    {
        public Dictionary<Element, ElementFormworkAnalysis> ElementAnalyses { get; set; } = new Dictionary<Element, ElementFormworkAnalysis>();
        public Dictionary<string, CategorySummary> CategorySummary { get; set; } = new Dictionary<string, CategorySummary>();

        public int TotalElements { get; set; }
        public double TotalFormworkArea { get; set; }
        public double TotalConcreteVolume { get; set; }
        public double EstimatedRebarWeight { get; set; }
        public StructuralAnalysisMetrics Metrics { get; } = new StructuralAnalysisMetrics();
    }

    public class StructuralAnalysisMetrics
    {
        public int RequestedTargetCount { get; set; }
        public int CandidateElementCount { get; set; }
        public int AnalyzedElementCount { get; set; }
        public long GeometryExtractionCount { get; set; }
        public long BooleanOperationCount { get; set; }
        public long CollectionMilliseconds { get; set; }
        public long ElementAnalysisMilliseconds { get; set; }
        public long InteractionMilliseconds { get; set; }
        public long TotalMilliseconds { get; set; }
    }

    public class ElementFormworkAnalysis
    {
        public Element Element { get; set; }
        public StructuralElementType ElementType { get; set; }
        public double ConcreteVolume { get; set; }
        public double FormworkArea { get; set; }
        public FormworkEngine.FormworkInfo FormworkInfo { get; set; }
        public List<ElementConnection> ConnectedElements { get; set; } = new List<ElementConnection>();
        public Dictionary<StructuralFormworkAnalyzer.FormworkType, double> FormworkTypes { get; set; } = new Dictionary<StructuralFormworkAnalyzer.FormworkType, double>();
    }

    public class ElementConnection
    {
        public Element ConnectedElement { get; set; }
        public ConnectionType ConnectionType { get; set; }
        public double IntersectionVolume { get; set; }
        public double ContactArea { get; set; }
    }

    public class CategorySummary
    {
        public int Count { get; set; }
        public double FormworkArea { get; set; }
        public double ConcreteVolume { get; set; }
    }

    public enum StructuralElementType
    {
        Column,
        Beam,
        Slab,
        Wall,
        Foundation,
        Stair,
        Other
    }

    public enum ConnectionType
    {
        ColumnBeam,
        ColumnSlab,
        BeamSlab,
        WallSlab,
        Other
    }

    #endregion
}
