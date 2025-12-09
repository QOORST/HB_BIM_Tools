using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;
using PipeSegmentModel = YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models.PipeSegment;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    /// <summary>
    /// 管線分析服務 - 分析管線系統並建立資料模型
    /// </summary>
    public class PipeAnalyzer
    {
        private Document _doc;

        public PipeAnalyzer(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// 分析管線系統並建立 ISO 資料
        /// </summary>
        public ISOData AnalyzePipingSystem(PipingSystem pipingSystem)
        {
            if (pipingSystem == null)
                throw new ArgumentNullException(nameof(pipingSystem));

            ISOData isoData = new ISOData
            {
                SystemName = pipingSystem.Name,
                SystemType = GetSystemType(pipingSystem),
                ProjectName = _doc.ProjectInformation.Name,
                ISONumber = GenerateISONumber(pipingSystem)
            };

            // 取得所有管線元件
            List<Element> elements = GetSystemElements(pipingSystem);

            // 建立管線段資料
            List<PipeSegmentModel> segments = CreatePipeSegments(elements);

            // 分析連接關係
            AnalyzeConnections(segments);

            // 識別主管線
            IdentifyMainPipe(segments, isoData);

            // 識別分支管線
            IdentifyBranches(segments, isoData);

            // 排序管線段
            SortSegments(isoData);

            // 計算總長度
            isoData.CalculateTotalLength();

            // 生成材料清單
            isoData.GenerateBOM();

            return isoData;
        }

        /// <summary>
        /// 取得系統元件
        /// </summary>
        private List<Element> GetSystemElements(PipingSystem pipingSystem)
        {
            List<Element> elements = new List<Element>();
            ElementSet systemElements = pipingSystem.PipingNetwork;

            foreach (Element elem in systemElements)
            {
                if (elem is Pipe || elem is FamilyInstance)
                {
                    elements.Add(elem);
                }
            }

            return elements;
        }

        /// <summary>
        /// 建立管線段資料模型
        /// </summary>
        private List<PipeSegmentModel> CreatePipeSegments(List<Element> elements)
        {
            List<PipeSegmentModel> segments = new List<PipeSegmentModel>();

            foreach (Element elem in elements)
            {
                PipeSegmentModel segment = CreateSegmentFromElement(elem);
                if (segment != null)
                {
                    segments.Add(segment);
                }
            }

            return segments;
        }

        /// <summary>
        /// 從元件建立管線段
        /// </summary>
        private PipeSegmentModel CreateSegmentFromElement(Element element)
        {
            PipeSegmentModel segment = new PipeSegmentModel
            {
                ElementId = element.Id
            };

            if (element is Pipe pipe)
            {
                segment.Type = "Pipe";
                
                // 取得管線位置
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve != null)
                {
                    Curve curve = locationCurve.Curve;
                    segment.StartPoint = curve.GetEndPoint(0);
                    segment.EndPoint = curve.GetEndPoint(1);
                    segment.CenterPoint = (segment.StartPoint + segment.EndPoint) / 2;
                    segment.Length = UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters);
                    
                    // 計算方向
                    XYZ direction = (segment.EndPoint - segment.StartPoint).Normalize();
                    segment.Direction = direction;
                }

                // 取得管徑
                segment.Diameter = UnitUtils.ConvertFromInternalUnits(pipe.Diameter, UnitTypeId.Millimeters);

                // 取得材料
                Parameter materialParam = pipe.LookupParameter("材料") ?? pipe.LookupParameter("Material");
                if (materialParam != null)
                {
                    segment.Material = materialParam.AsValueString();
                }
            }
            else if (element is FamilyInstance fitting)
            {
                // 判斷管配件類型
                segment.Type = GetFittingType(fitting);
                segment.FamilyName = fitting.Symbol.Family.Name;
                segment.TypeName = fitting.Symbol.Name;

                // 取得位置
                LocationPoint locationPoint = fitting.Location as LocationPoint;
                if (locationPoint != null)
                {
                    segment.CenterPoint = locationPoint.Point;
                    segment.StartPoint = segment.CenterPoint;
                    segment.EndPoint = segment.CenterPoint;
                }

                // 取得管徑
                segment.Diameter = PipeToISOCommand.GetPipeDiameter(fitting, _doc);

                // 取得材料
                Parameter materialParam = fitting.LookupParameter("材料") ?? fitting.LookupParameter("Material");
                if (materialParam != null)
                {
                    segment.Material = materialParam.AsValueString();
                }
            }
            else
            {
                return null;
            }

            return segment;
        }

        /// <summary>
        /// 判斷管配件類型
        /// </summary>
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
                familyName.Contains("閥") || typeName.Contains("凡而"))
                return "Valve";

            if (familyName.Contains("CAP") || typeName.Contains("CAP") || 
                familyName.Contains("管帽") || typeName.Contains("封頭"))
                return "Cap";

            if (familyName.Contains("COUPLING") || typeName.Contains("COUPLING") || 
                familyName.Contains("接頭") || typeName.Contains("套筒"))
                return "Coupling";

            return "Fitting";
        }

        /// <summary>
        /// 分析管線段之間的連接關係
        /// </summary>
        private void AnalyzeConnections(List<PipeSegmentModel> segments)
        {
            const double tolerance = 10; // 10mm 容差

            foreach (var segment in segments)
            {
                if (segment.Type == "Pipe")
                {
                    // 尋找連接到終點的元件
                    foreach (var other in segments)
                    {
                        if (other.ElementId == segment.ElementId)
                            continue;

                        double distToStart = PipeSegmentModel.CalculateDistance(segment.EndPoint, other.StartPoint);
                        double distToEnd = PipeSegmentModel.CalculateDistance(segment.EndPoint, other.EndPoint);
                        double distToCenter = PipeSegmentModel.CalculateDistance(segment.EndPoint, other.CenterPoint);

                        if (distToStart < tolerance || distToEnd < tolerance || distToCenter < tolerance)
                        {
                            segment.NextSegment = other;
                            other.PreviousSegment = segment;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 識別主管線（最長連續管線）
        /// </summary>
        private void IdentifyMainPipe(List<PipeSegmentModel> segments, ISOData isoData)
        {
            // 尋找起點（沒有 PreviousSegment 的管線）
            var startSegments = segments.Where(s => s.Type == "Pipe" && s.PreviousSegment == null).ToList();

            if (startSegments.Count == 0)
            {
                // 如果沒有明確起點，選擇最長的管線作為起點
                var longestPipe = segments.Where(s => s.Type == "Pipe")
                                         .OrderByDescending(s => s.Length)
                                         .FirstOrDefault();
                if (longestPipe != null)
                {
                    startSegments.Add(longestPipe);
                }
            }

            // 從每個可能的起點追蹤，找出最長路徑
            List<PipeSegmentModel> longestPath = new List<PipeSegmentModel>();

            foreach (var start in startSegments)
            {
                List<PipeSegmentModel> path = TracePath(start);
                if (path.Count > longestPath.Count)
                {
                    longestPath = path;
                }
            }

            // 標記主管線
            int sequence = 1;
            foreach (var segment in longestPath)
            {
                segment.IsMainPipe = true;
                segment.BranchNumber = 0;
                segment.SequenceNumber = sequence++;
                isoData.MainPipeSegments.Add(segment);
            }

            // 設定視圖方向
            if (isoData.MainPipeSegments.Count > 0)
            {
                var firstPipe = isoData.MainPipeSegments.First(s => s.Type == "Pipe");
                if (firstPipe != null)
                {
                    isoData.ViewStartPoint = firstPipe.StartPoint;
                    isoData.ViewDirection = firstPipe.Direction;
                }
            }
        }

        /// <summary>
        /// 追蹤管線路徑
        /// </summary>
        private List<PipeSegmentModel> TracePath(PipeSegmentModel start)
        {
            List<PipeSegmentModel> path = new List<PipeSegmentModel>();
            PipeSegmentModel current = start;
            HashSet<ElementId> visited = new HashSet<ElementId>();

            while (current != null && !visited.Contains(current.ElementId))
            {
                path.Add(current);
                visited.Add(current.ElementId);
                current = current.NextSegment;
            }

            return path;
        }

        /// <summary>
        /// 識別分支管線
        /// </summary>
        private void IdentifyBranches(List<PipeSegmentModel> segments, ISOData isoData)
        {
            int branchNumber = 1;

            foreach (var mainSegment in isoData.MainPipeSegments)
            {
                if (mainSegment.Type == "Tee" || mainSegment.Type == "Cross")
                {
                    // 在三通處尋找分支
                    var branches = segments.Where(s => 
                        !s.IsMainPipe && 
                        s.Type == "Pipe" &&
                        PipeSegmentModel.CalculateDistance(s.StartPoint, mainSegment.CenterPoint) < 10)
                        .ToList();

                    foreach (var branch in branches)
                    {
                        List<PipeSegmentModel> branchPath = TracePath(branch);
                        
                        int sequence = 1;
                        foreach (var segment in branchPath)
                        {
                            segment.BranchNumber = branchNumber;
                            segment.SequenceNumber = sequence++;
                        }

                        isoData.BranchSegments[branchNumber] = branchPath;
                        branchNumber++;
                    }
                }
            }
        }

        /// <summary>
        /// 排序管線段
        /// </summary>
        private void SortSegments(ISOData isoData)
        {
            // 主管線已在 IdentifyMainPipe 中排序
            // 這裡可以添加額外的排序邏輯
        }

        /// <summary>
        /// 取得系統類型
        /// </summary>
        private string GetSystemType(PipingSystem pipingSystem)
        {
            Parameter systemTypeParam = pipingSystem.LookupParameter("系統類型") ?? 
                                       pipingSystem.LookupParameter("System Type");
            
            if (systemTypeParam != null)
            {
                return systemTypeParam.AsValueString();
            }

            return "未分類";
        }

        /// <summary>
        /// 生成 ISO 編號
        /// </summary>
        private string GenerateISONumber(PipingSystem pipingSystem)
        {
            string systemName = pipingSystem.Name;
            string date = DateTime.Now.ToString("yyyyMMdd");
            string number = pipingSystem.Id.Value.ToString("D6");
            
            return $"ISO-{systemName}-{date}-{number}";
        }
    }
}
