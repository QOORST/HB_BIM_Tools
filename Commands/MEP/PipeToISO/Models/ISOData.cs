using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

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
            if (doc == null || string.IsNullOrEmpty(SystemName))
                return;

            BillOfMaterials.Clear();
            Dictionary<string, BOMItem> bomDict = new Dictionary<string, BOMItem>();

            try
            {
                // 查找管線系統
                FilteredElementCollector systemCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystem));

                PipingSystem targetSystem = null;
                foreach (PipingSystem system in systemCollector)
                {
                    if (system.Name == SystemName)
                    {
                        targetSystem = system;
                        break;
                    }
                }

                if (targetSystem == null)
                    return;

                // 獲取系統中的所有元件
                ElementSet systemElements = targetSystem.PipingNetwork;
                if (systemElements == null || systemElements.Size == 0)
                    return;

                // 處理每個元件
                foreach (Element element in systemElements)
                {
                    if (element is Pipe pipe)
                    {
                        ProcessPipeForBOM(pipe, bomDict);
                    }
                    else if (element is FamilyInstance fitting)
                    {
                        ProcessFittingForBOM(fitting, bomDict, doc);
                    }
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
            catch (Exception)
            {
                // 如果直接收集失敗,回退到使用段資料
                GenerateBOM();
            }
        }

        private void ProcessPipeForBOM(Pipe pipe, Dictionary<string, BOMItem> bomDict)
        {
            double diameter = UnitUtils.ConvertFromInternalUnits(pipe.Diameter, UnitTypeId.Millimeters);
            LocationCurve locationCurve = pipe.Location as LocationCurve;
            double length = 0;

            if (locationCurve != null)
            {
                length = UnitUtils.ConvertFromInternalUnits(locationCurve.Curve.Length, UnitTypeId.Millimeters);
            }

            string typeName = pipe.PipeType.Name;
            string material = "聚氯乙烯"; // 預設材料

            Parameter materialParam = pipe.LookupParameter("材料") ?? pipe.LookupParameter("Material");
            if (materialParam != null && materialParam.HasValue)
            {
                material = materialParam.AsValueString() ?? material;
            }

            string key = $"Pipe_{diameter:F0}_{typeName}";

            if (!bomDict.ContainsKey(key))
            {
                bomDict[key] = new BOMItem
                {
                    Type = "Pipe",
                    Diameter = diameter,
                    Description = typeName,
                    Material = material,
                    Unit = "m",
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
            
            // 獲取管徑
            double diameter = 0;
            
            // 嘗試從連接器獲取管徑
            ConnectorSet connectors = fitting.MEPModel?.ConnectorManager?.Connectors;
            if (connectors != null && connectors.Size > 0)
            {
                foreach (Connector conn in connectors)
                {
                    if (conn.Domain == Domain.DomainPiping)
                    {
                        diameter = UnitUtils.ConvertFromInternalUnits(conn.Radius * 2, UnitTypeId.Millimeters);
                        break;
                    }
                }
            }

            // 如果沒有連接器,嘗試從參數獲取
            if (diameter == 0)
            {
                Parameter diamParam = fitting.LookupParameter("尺寸") ?? 
                                     fitting.LookupParameter("Size") ??
                                     fitting.LookupParameter("管徑");
                if (diamParam != null && diamParam.HasValue)
                {
                    if (diamParam.StorageType == StorageType.Double)
                    {
                        diameter = UnitUtils.ConvertFromInternalUnits(diamParam.AsDouble(), UnitTypeId.Millimeters);
                    }
                    else if (diamParam.StorageType == StorageType.String)
                    {
                        string sizeStr = diamParam.AsString();
                        if (double.TryParse(sizeStr, out double parsedSize))
                        {
                            diameter = parsedSize;
                        }
                    }
                }
            }

            string material = "聚氯乙烯"; // 預設材料
            Parameter materialParam = fitting.LookupParameter("材料") ?? fitting.LookupParameter("Material");
            if (materialParam != null && materialParam.HasValue)
            {
                material = materialParam.AsValueString() ?? material;
            }

            string key = $"{fittingType}_{diameter:F0}_{typeName}";

            if (!bomDict.ContainsKey(key))
            {
                bomDict[key] = new BOMItem
                {
                    Type = fittingType,
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

            return "Fitting";
        }

        private void ProcessSegmentsForBOM(List<PipeSegment> segments, Dictionary<string, BOMItem> bomDict)
        {
            foreach (var segment in segments)
            {
                string key = $"{segment.Type}_{segment.Diameter:F0}_{segment.TypeName}";

                if (!bomDict.ContainsKey(key))
                {
                    bomDict[key] = new BOMItem
                    {
                        Type = segment.Type,
                        Diameter = segment.Diameter,
                        Description = segment.TypeName,
                        Material = segment.Material,
                        Unit = segment.Type == "Pipe" ? "m" : "個",
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
