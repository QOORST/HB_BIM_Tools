using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models
{
    /// <summary>
    /// ISO 圖資料模型
    /// </summary>
    public class ISOData
    {
        /// <summary>
        /// ISO 圖編號
        /// </summary>
        public string ISONumber { get; set; }

        /// <summary>
        /// 管線系統名稱
        /// </summary>
        public string SystemName { get; set; }
        public ElementId SystemId { get; set; } = ElementId.InvalidElementId;
        public SystemSnapshot Snapshot { get; set; }
        public List<Element> GetScopedElements(Document doc)
        {
            if (Snapshot == null) Snapshot = SystemSnapshot.Capture(doc.GetElement(SystemId) as PipingSystem);
            return Snapshot.Elements.ToList();
        }

        /// <summary>
        /// 系統類型（供水、排水、消防等）
        /// </summary>
        public string SystemType { get; set; }

        /// <summary>
        /// 專案名稱
        /// </summary>
        public string ProjectName { get; set; }

        /// <summary>
        /// 建立日期
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// 主管線段列表（按順序排列）
        /// </summary>
        public List<PipeSegment> MainPipeSegments { get; set; }

        /// <summary>
        /// 分支管線段列表
        /// </summary>
        public Dictionary<int, List<PipeSegment>> BranchSegments { get; set; }

        /// <summary>
        /// 材料清單（BOM）
        /// </summary>
        public List<BOMItem> BillOfMaterials { get; set; }

        /// <summary>
        /// ISO 視圖的起點（用於繪圖定位）
        /// </summary>
        public XYZ ViewStartPoint { get; set; }

        /// <summary>
        /// ISO 視圖的方向（主管線方向）
        /// </summary>
        public XYZ ViewDirection { get; set; }

        /// <summary>
        /// 視圖比例（例如 1:50）
        /// </summary>
        public double ViewScale { get; set; }

        /// <summary>
        /// 總長度（mm）
        /// </summary>
        public double TotalLength { get; set; }

        /// <summary>
        /// 總重量（kg）- 可選
        /// </summary>
        public double TotalWeight { get; set; }

        /// <summary>
        /// 備註
        /// </summary>
        public string Notes { get; set; }

        public ISOData()
        {
            MainPipeSegments = new List<PipeSegment>();
            BranchSegments = new Dictionary<int, List<PipeSegment>>();
            BillOfMaterials = new List<BOMItem>();
            CreatedDate = DateTime.Now;
            ViewScale = 50; // 預設 1:50
        }

        /// <summary>
        /// 計算總長度
        /// </summary>
        public void CalculateTotalLength()
        {
            TotalLength = 0;

            foreach (var segment in MainPipeSegments)
            {
                if (segment.Type == "Pipe")
                {
                    TotalLength += segment.Length;
                }
            }

            foreach (var branch in BranchSegments.Values)
            {
                foreach (var segment in branch)
                {
                    if (segment.Type == "Pipe")
                    {
                        TotalLength += segment.Length;
                    }
                }
            }
        }

        /// <summary>
        /// 生成材料清單
        /// </summary>
        public void GenerateBOM()
        {
            BillOfMaterials.Clear();
            Dictionary<string, BOMItem> bomDict = new Dictionary<string, BOMItem>();

            // 處理主管線
            ProcessSegmentsForBOM(MainPipeSegments, bomDict);

            // 處理分支管線
            foreach (var branch in BranchSegments.Values)
            {
                ProcessSegmentsForBOM(branch, bomDict);
            }

            // 添加項次編號並加入清單
            int itemNumber = 1;
            var sortedItems = bomDict.Values.OrderBy(b => b.Type).ThenBy(b => b.Diameter).ToList();
            foreach (var bomItem in sortedItems)
            {
                bomItem.ItemNumber = itemNumber++;
                BillOfMaterials.Add(bomItem);
            }
        }

        /// <summary>
        /// 從 Revit 系統直接生成完整材料清單
        /// </summary>
        /// <param name="doc">Revit 文件</param>
        public void GenerateBOMFromSystem(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            BillOfMaterials.Clear();
            var elements = GetScopedElements(doc);
            if (elements.Count == 0)
                throw new InvalidOperationException("選定系統沒有管路元件。");
            var items = new Dictionary<string, BOMItem>();
            // 收集失敗時不退回不完整的路徑資料，也不交付部分 BOM。
            foreach (Element element in elements)
            {
                if (element is Pipe pipe) ProcessPipeForBOM(pipe, items);
                else if (element is FamilyInstance fitting) ProcessFittingForBOM(fitting, items, doc);
            }
            if (items.Count == 0) throw new InvalidOperationException("沒有可統計的管線或配件。");
            int number = 1;
            foreach (var item in items.Values.OrderBy(b => b.Type).ThenBy(b => b.Diameter).ThenBy(b => b.Description))
            {
                item.ItemNumber = number++;
                BillOfMaterials.Add(item);
            }
            if (BillOfMaterials.Sum(b => b.Quantity) != elements.Count)
                throw new InvalidOperationException("材料表件數與核對範圍不符，已停止匯出。");
            TotalLength = BillOfMaterials.Where(b => b.Type == "Pipe").Sum(b => b.TotalLength);
            double capturedLength = Snapshot.Rows.Where(r => r.Included && r.Element is Pipe)
                .Sum(r => r.LengthMm ?? throw new InvalidOperationException("核對範圍含無法讀取長度的管線。"));
            if (Math.Abs(TotalLength - capturedLength) > 0.001)
                throw new InvalidOperationException("材料表長度與核對範圍不符，已停止匯出。");
        }

        internal static string ReadMaterial(Element element)
        {
            foreach (var source in new[] { element, element.Document.GetElement(element.GetTypeId()) })
            {
                if (source == null) continue;
                foreach (var parameter in new[] { source.get_Parameter(BuiltInParameter.RBS_PIPE_MATERIAL_PARAM),
                    source.LookupParameter("材料"), source.LookupParameter("Material") })
                {
                    string value = ReadMaterialParameter(parameter, element.Document);
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
            }
            return "未指定";
        }

        private static string ReadMaterialParameter(Parameter parameter, Document doc)
        {
            if (parameter == null || !parameter.HasValue) return null;
            if (parameter.StorageType == StorageType.ElementId)
                return (doc.GetElement(parameter.AsElementId()) as Material)?.Name;
            if (parameter.StorageType == StorageType.String) return parameter.AsString();
            return null;
        }
        private void ProcessPipeForBOM(Pipe pipe, Dictionary<string, BOMItem> bomDict)
        {
            double diameter = UnitUtils.ConvertFromInternalUnits(pipe.Diameter, UnitTypeId.Millimeters);
            LocationCurve locationCurve = pipe.Location as LocationCurve;
            double length;

            if (locationCurve != null)
            {
                length = UnitUtils.ConvertFromInternalUnits(locationCurve.Curve.Length, UnitTypeId.Millimeters);
            }
            else throw new InvalidOperationException("管線 " + pipe.Id + " 缺少模型長度，已停止算量。");

            string typeName = pipe.PipeType.Name;
            string material = ReadMaterial(pipe);
            string key = $"Pipe|{pipe.GetTypeId()}|{diameter:R}|{material}";
            if (!bomDict.ContainsKey(key))
            {
                bomDict[key] = new BOMItem
                {
                    Type = "Pipe",
                    Diameter = diameter,
                    Description = typeName,
                    Material = material,
                    Unit = "段",
                    Quantity = 0,
                    TotalLength = 0
                };
            }

            BOMItem item = bomDict[key];
            item.Quantity++;
            item.TotalLength += length;
        }

        private void ProcessFittingForBOM(FamilyInstance fitting, Dictionary<string, BOMItem> bomDict, Document doc)
        {
            string fittingType = GetFittingType(fitting);
            string typeName = fitting.Symbol.Name;
            
            double diameter = PipeToISOCommand.GetPipeDiameter(fitting, doc);
            var connectors = fitting.MEPModel?.ConnectorManager?.Connectors;
            var sizes = connectors == null ? new List<double>() : connectors.Cast<Connector>()
                .Where(c => c.Domain == Domain.DomainPiping && c.Shape == ConnectorProfileType.Round)
                .Select(c => UnitUtils.ConvertFromInternalUnits(c.Radius * 2, UnitTypeId.Millimeters))
                .OrderByDescending(d => d).ToList();
            string sizeText = sizes.Count > 0
                ? string.Join("×", sizes.Select(d => ExportFormatting.Number(d, "0.###")))
                : diameter > 0 ? ExportFormatting.Number(diameter, "0.###") : "未指定";
            string material = ReadMaterial(fitting);
            string key = $"{fittingType}|{fitting.GetTypeId()}|{sizeText}|{material}";
            if (!bomDict.ContainsKey(key))
            {
                bomDict[key] = new BOMItem
                {
                    Type = fittingType,
                    SizeText = sizeText,
                    Diameter = diameter,
                    Description = typeName,
                    Material = material,
                    Unit = "個",
                    Quantity = 0,
                    TotalLength = 0
                };
            }

            bomDict[key].Quantity++;
        }

        private string GetFittingType(FamilyInstance fitting)
        {
            var category = fitting.Category?.Id;
            if (category == new ElementId(BuiltInCategory.OST_Sprinklers)) return "Sprinkler";
            if (category == new ElementId(BuiltInCategory.OST_PlumbingFixtures)) return "PlumbingFixture";
            string familyName = fitting.Symbol.Family.Name.ToUpper();
            string typeName = fitting.Symbol.Name.ToUpper();

            if (familyName.Contains("ELBOW") || typeName.Contains("ELBOW") || 
                familyName.Contains("彎頭") || typeName.Contains("彎頭"))
                return "Elbow";

            if (familyName.Contains("TEE") || typeName.Contains("TEE") || 
                familyName.Contains("三通") || typeName.Contains("三通"))
                return "Tee";

            if (familyName.Contains("REDUCER") || typeName.Contains("REDUCER") || 
                familyName.Contains("異徑") || typeName.Contains("大小頭"))
                return "Reducer";

            if (familyName.Contains("FLANGE") || typeName.Contains("FLANGE") || 
                familyName.Contains("法蘭") || typeName.Contains("凸緣"))
                return "Flange";

            if (familyName.Contains("VALVE") || typeName.Contains("VALVE") || 
                familyName.Contains("閥") || typeName.Contains("閥門"))
                return "Valve";

            if (familyName.Contains("CAP") || typeName.Contains("CAP") || 
                familyName.Contains("管帽") || typeName.Contains("封頭"))
                return "Cap";

            return category == new ElementId(BuiltInCategory.OST_PipeAccessory) ? "PipeAccessory" : "Fitting";
        }

        private void ProcessSegmentsForBOM(List<PipeSegment> segments, Dictionary<string, BOMItem> bomDict)
        {
            foreach (var segment in segments)
            {
                string key = $"{segment.Type}|{segment.Diameter:R}|{segment.FamilyName}|{segment.TypeName}|{segment.Material}";

                if (!bomDict.ContainsKey(key))
                {
                    bomDict[key] = new BOMItem
                    {
                        Type = segment.Type,
                        Diameter = segment.Diameter,
                        Description = segment.TypeName,
                        Material = ExportFormatting.MaterialOrUnknown(segment.Material),
                        Unit = segment.Type == "Pipe" ? "段" : "個",
                        Quantity = 0,
                        TotalLength = 0
                    };
                }

                BOMItem item = bomDict[key];
                item.Quantity++;

                if (segment.Type == "Pipe")
                {
                    item.TotalLength += segment.Length;
                }
            }
        }
    }

    /// <summary>
    /// 材料清單項目
    /// </summary>
    public class BOMItem
    {
        /// <summary>
        /// 項次
        /// </summary>
        public int ItemNumber { get; set; }

        /// <summary>
        /// 元件類型
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 管徑（mm）
        /// </summary>
        public double Diameter { get; set; }
        public string SizeText { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 材料
        /// </summary>
        public string Material { get; set; }

        /// <summary>
        /// 數量
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// 總長度（僅適用於管線，單位：mm）
        /// </summary>
        public double TotalLength { get; set; }

        /// <summary>
        /// 單位
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 單重（kg）- 可選
        /// </summary>
        public double UnitWeight { get; set; }

        /// <summary>
        /// 總重（kg）- 可選
        /// </summary>
        public double TotalWeight
        {
            get { return UnitWeight * Quantity; }
        }

        public override string ToString()
        {
            if (Type == "Pipe")
            {
                return $"{Type} Ø{Diameter:F0}mm - {TotalLength / 1000:F2}m ({Quantity} 段)";
            }
            else
            {
                return $"{Type} Ø{Diameter:F0}mm - {Description} x {Quantity}";
            }
        }
    }
}
