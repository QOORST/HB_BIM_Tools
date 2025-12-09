using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    /// <summary>
    /// PCF (Piping Component File) 匯出器
    /// PCF 是管線加工行業的標準格式，用於 CNC 切割和彎管機
    /// </summary>
    public class PCFExporter
    {
        /// <summary>
        /// 匯出為 PCF 格式檔案
        /// </summary>
        public void ExportToPCF(ISOData isoData, string filePath)
        {
            if (isoData == null)
                throw new ArgumentNullException(nameof(isoData));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("檔案路徑不能為空", nameof(filePath));

            StringBuilder pcfContent = new StringBuilder();

            // 寫入檔頭
            WriteHeader(pcfContent, isoData);

            // 寫入管線資料
            WritePipelineData(pcfContent, isoData);

            // 寫入材料清單
            WriteBOM(pcfContent, isoData);

            // 寫入檔尾
            WriteFooter(pcfContent, isoData);

            // 儲存檔案
            File.WriteAllText(filePath, pcfContent.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// 寫入 PCF 檔頭
        /// </summary>
        private void WriteHeader(StringBuilder sb, ISOData isoData)
        {
            sb.AppendLine("ISOGEN-FILES");
            sb.AppendLine($"UNITS-BORE MM");
            sb.AppendLine($"UNITS-CO-ORDS MM");
            sb.AppendLine($"UNITS-WEIGHT KGS");
            sb.AppendLine();
            
            sb.AppendLine("PIPELINE-REFERENCE");
            sb.AppendLine($"    PROJECT-NAME {isoData.ProjectName ?? "Unknown"}");
            sb.AppendLine($"    DRAWING-NUMBER {isoData.ISONumber}");
            sb.AppendLine($"    ISOMETRIC-DRAWING {isoData.SystemName}");
            sb.AppendLine($"    REVISION-NUMBER 1");
            sb.AppendLine($"    REVISION-DATE {isoData.CreatedDate:dd-MMM-yyyy}");
            sb.AppendLine($"    PIPING-SPEC {isoData.SystemType}");
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入管線資料
        /// </summary>
        private void WritePipelineData(StringBuilder sb, ISOData isoData)
        {
            sb.AppendLine("PIPELINE-DATA");
            sb.AppendLine();

            // 寫入主管線
            sb.AppendLine("    MAIN-PIPELINE");
            WritePipeSegments(sb, isoData.MainPipeSegments);

            // 寫入分支管線
            foreach (var branch in isoData.BranchSegments.OrderBy(kvp => kvp.Key))
            {
                sb.AppendLine();
                sb.AppendLine($"    BRANCH-PIPELINE {branch.Key}");
                WritePipeSegments(sb, branch.Value);
            }

            sb.AppendLine();
            sb.AppendLine("END-PIPELINE-DATA");
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入管線段資料
        /// </summary>
        private void WritePipeSegments(StringBuilder sb, List<PipeSegment> segments)
        {
            foreach (var segment in segments)
            {
                switch (segment.Type)
                {
                    case "Pipe":
                        WritePipeComponent(sb, segment);
                        break;
                    case "Elbow":
                        WriteElbowComponent(sb, segment);
                        break;
                    case "Tee":
                        WriteTeeComponent(sb, segment);
                        break;
                    case "Reducer":
                        WriteReducerComponent(sb, segment);
                        break;
                    case "Flange":
                        WriteFlangeComponent(sb, segment);
                        break;
                    case "Valve":
                        WriteValveComponent(sb, segment);
                        break;
                    default:
                        WriteGenericComponent(sb, segment);
                        break;
                }
            }
        }

        /// <summary>
        /// 寫入管線元件
        /// </summary>
        private void WritePipeComponent(StringBuilder sb, PipeSegment segment)
        {
            sb.AppendLine($"        PIPE");
            sb.AppendLine($"            ITEM-CODE {segment.SequenceNumber}");
            sb.AppendLine($"            PIPING-SPEC {segment.Material ?? "CARBON-STEEL"}");
            sb.AppendLine($"            NOMINAL-DIAMETER {segment.Diameter:F1}");
            sb.AppendLine($"            END-POINT {FormatCoordinate(segment.StartPoint)}");
            sb.AppendLine($"            END-POINT {FormatCoordinate(segment.EndPoint)}");
            sb.AppendLine($"            CENTRE-POINT {FormatCoordinate(segment.CenterPoint)}");
            
            if (segment.Length > 0)
            {
                sb.AppendLine($"            CUT-LENGTH {segment.Length:F1}");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入彎頭元件
        /// </summary>
        private void WriteElbowComponent(StringBuilder sb, PipeSegment segment)
        {
            sb.AppendLine($"        ELBOW");
            sb.AppendLine($"            ITEM-CODE {segment.SequenceNumber}");
            sb.AppendLine($"            PIPING-SPEC {segment.Material ?? "CARBON-STEEL"}");
            sb.AppendLine($"            NOMINAL-DIAMETER {segment.Diameter:F1}");
            sb.AppendLine($"            CENTRE-POINT {FormatCoordinate(segment.CenterPoint)}");
            sb.AppendLine($"            ANGLE 45");  // 預設 45 度，可依實際情況調整
            sb.AppendLine($"            RADIUS {segment.Diameter * 1.5:F1}");  // 1.5D 彎頭
            
            if (!string.IsNullOrEmpty(segment.TypeName))
            {
                sb.AppendLine($"            COMPONENT-NAME {segment.TypeName}");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入三通元件
        /// </summary>
        private void WriteTeeComponent(StringBuilder sb, PipeSegment segment)
        {
            sb.AppendLine($"        TEE");
            sb.AppendLine($"            ITEM-CODE {segment.SequenceNumber}");
            sb.AppendLine($"            PIPING-SPEC {segment.Material ?? "CARBON-STEEL"}");
            sb.AppendLine($"            NOMINAL-DIAMETER {segment.Diameter:F1}");
            sb.AppendLine($"            CENTRE-POINT {FormatCoordinate(segment.CenterPoint)}");
            
            if (!string.IsNullOrEmpty(segment.TypeName))
            {
                sb.AppendLine($"            COMPONENT-NAME {segment.TypeName}");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入異徑管元件
        /// </summary>
        private void WriteReducerComponent(StringBuilder sb, PipeSegment segment)
        {
            sb.AppendLine($"        REDUCER");
            sb.AppendLine($"            ITEM-CODE {segment.SequenceNumber}");
            sb.AppendLine($"            PIPING-SPEC {segment.Material ?? "CARBON-STEEL"}");
            sb.AppendLine($"            NOMINAL-DIAMETER {segment.Diameter:F1}");
            sb.AppendLine($"            CENTRE-POINT {FormatCoordinate(segment.CenterPoint)}");
            
            if (!string.IsNullOrEmpty(segment.TypeName))
            {
                sb.AppendLine($"            COMPONENT-NAME {segment.TypeName}");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入法蘭元件
        /// </summary>
        private void WriteFlangeComponent(StringBuilder sb, PipeSegment segment)
        {
            sb.AppendLine($"        FLANGE");
            sb.AppendLine($"            ITEM-CODE {segment.SequenceNumber}");
            sb.AppendLine($"            PIPING-SPEC {segment.Material ?? "CARBON-STEEL"}");
            sb.AppendLine($"            NOMINAL-DIAMETER {segment.Diameter:F1}");
            sb.AppendLine($"            CENTRE-POINT {FormatCoordinate(segment.CenterPoint)}");
            
            if (!string.IsNullOrEmpty(segment.TypeName))
            {
                sb.AppendLine($"            COMPONENT-NAME {segment.TypeName}");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入閥門元件
        /// </summary>
        private void WriteValveComponent(StringBuilder sb, PipeSegment segment)
        {
            sb.AppendLine($"        VALVE");
            sb.AppendLine($"            ITEM-CODE {segment.SequenceNumber}");
            sb.AppendLine($"            PIPING-SPEC {segment.Material ?? "CARBON-STEEL"}");
            sb.AppendLine($"            NOMINAL-DIAMETER {segment.Diameter:F1}");
            sb.AppendLine($"            CENTRE-POINT {FormatCoordinate(segment.CenterPoint)}");
            
            if (!string.IsNullOrEmpty(segment.TypeName))
            {
                sb.AppendLine($"            COMPONENT-NAME {segment.TypeName}");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入一般元件
        /// </summary>
        private void WriteGenericComponent(StringBuilder sb, PipeSegment segment)
        {
            sb.AppendLine($"        COMPONENT");
            sb.AppendLine($"            ITEM-CODE {segment.SequenceNumber}");
            sb.AppendLine($"            COMPONENT-TYPE {segment.Type}");
            sb.AppendLine($"            PIPING-SPEC {segment.Material ?? "CARBON-STEEL"}");
            sb.AppendLine($"            NOMINAL-DIAMETER {segment.Diameter:F1}");
            sb.AppendLine($"            CENTRE-POINT {FormatCoordinate(segment.CenterPoint)}");
            
            if (!string.IsNullOrEmpty(segment.TypeName))
            {
                sb.AppendLine($"            COMPONENT-NAME {segment.TypeName}");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入材料清單
        /// </summary>
        private void WriteBOM(StringBuilder sb, ISOData isoData)
        {
            sb.AppendLine("BILL-OF-MATERIALS");
            sb.AppendLine();

            int itemNumber = 1;
            foreach (var bomItem in isoData.BillOfMaterials.OrderBy(b => b.Type).ThenBy(b => b.Diameter))
            {
                sb.AppendLine($"    BOM-ITEM {itemNumber}");
                sb.AppendLine($"        ITEM-CODE {bomItem.ItemNumber}");
                sb.AppendLine($"        COMPONENT-TYPE {bomItem.Type}");
                sb.AppendLine($"        NOMINAL-DIAMETER {bomItem.Diameter:F1}");
                sb.AppendLine($"        DESCRIPTION {bomItem.Description ?? bomItem.Type}");
                sb.AppendLine($"        MATERIAL {bomItem.Material ?? "CARBON-STEEL"}");
                sb.AppendLine($"        QUANTITY {bomItem.Quantity}");
                
                if (bomItem.Type == "Pipe" && bomItem.TotalLength > 0)
                {
                    sb.AppendLine($"        TOTAL-LENGTH {bomItem.TotalLength:F1}");
                }
                
                if (bomItem.TotalWeight > 0)
                {
                    sb.AppendLine($"        TOTAL-WEIGHT {bomItem.TotalWeight:F2}");
                }
                
                sb.AppendLine();
                itemNumber++;
            }

            sb.AppendLine("END-BILL-OF-MATERIALS");
            sb.AppendLine();
        }

        /// <summary>
        /// 寫入檔尾
        /// </summary>
        private void WriteFooter(StringBuilder sb, ISOData isoData)
        {
            sb.AppendLine("PIPELINE-SUMMARY");
            sb.AppendLine($"    TOTAL-LENGTH {isoData.TotalLength:F1}");
            
            if (isoData.TotalWeight > 0)
            {
                sb.AppendLine($"    TOTAL-WEIGHT {isoData.TotalWeight:F2}");
            }
            
            sb.AppendLine($"    COMPONENT-COUNT {isoData.MainPipeSegments.Count + isoData.BranchSegments.Values.Sum(b => b.Count)}");
            sb.AppendLine();
            
            sb.AppendLine("END-ISOGEN-FILES");
        }

        /// <summary>
        /// 格式化座標為 PCF 格式（X Y Z）
        /// </summary>
        private string FormatCoordinate(Autodesk.Revit.DB.XYZ point)
        {
            if (point == null)
                return "0.0 0.0 0.0";

            // 轉換為 mm
            double x = Autodesk.Revit.DB.UnitUtils.ConvertFromInternalUnits(
                point.X, Autodesk.Revit.DB.UnitTypeId.Millimeters);
            double y = Autodesk.Revit.DB.UnitUtils.ConvertFromInternalUnits(
                point.Y, Autodesk.Revit.DB.UnitTypeId.Millimeters);
            double z = Autodesk.Revit.DB.UnitUtils.ConvertFromInternalUnits(
                point.Z, Autodesk.Revit.DB.UnitTypeId.Millimeters);

            return $"{x:F1} {y:F1} {z:F1}";
        }

        /// <summary>
        /// 匯出材料清單為 CSV 格式
        /// </summary>
        public void ExportBOMToCSV(ISOData isoData, string filePath)
        {
            if (isoData == null)
                throw new ArgumentNullException(nameof(isoData));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("檔案路徑不能為空", nameof(filePath));

            StringBuilder csv = new StringBuilder();

            // CSV 標題行
            csv.AppendLine("項次,元件類型,管徑(mm),描述,材料,數量,單位,總長度(m),重量(kg)");

            // 資料行
            foreach (var bomItem in isoData.BillOfMaterials)
            {
                string lengthStr = bomItem.Type == "Pipe" && bomItem.TotalLength > 0 
                    ? (bomItem.TotalLength / 1000).ToString("F2") 
                    : "-";
                    
                string weightStr = bomItem.TotalWeight > 0 
                    ? bomItem.TotalWeight.ToString("F2") 
                    : "-";
                
                string description = !string.IsNullOrEmpty(bomItem.Description) 
                    ? bomItem.Description 
                    : bomItem.Type;
                
                string material = !string.IsNullOrEmpty(bomItem.Material) 
                    ? bomItem.Material 
                    : "聚氯乙烯 - 硬質";

                csv.AppendLine($"{bomItem.ItemNumber}," +
                             $"{bomItem.Type}," +
                             $"{bomItem.Diameter:F0}," +
                             $"{description}," +
                             $"{material}," +
                             $"{bomItem.Quantity}," +
                             $"{bomItem.Unit}," +
                             $"{lengthStr}," +
                             $"{weightStr}");
            }

            // 寫入檔案(使用 UTF-8 with BOM 以便 Excel 正確顯示中文)
            File.WriteAllText(filePath, csv.ToString(), new UTF8Encoding(true));
        }
    }
}
