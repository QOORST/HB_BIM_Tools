using System;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models
{
    /// <summary>
    /// 管線段資料模型
    /// </summary>
    public class PipeSegment
    {
        /// <summary>
        /// Revit 元件 ID
        /// </summary>
        public ElementId ElementId { get; set; }

        /// <summary>
        /// 元件類型（Pipe, Elbow, Tee, Reducer 等）
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 起點座標
        /// </summary>
        public XYZ StartPoint { get; set; }

        /// <summary>
        /// 終點座標
        /// </summary>
        public XYZ EndPoint { get; set; }

        /// <summary>
        /// 中心點座標（用於管配件）
        /// </summary>
        public XYZ CenterPoint { get; set; }

        /// <summary>
        /// 管徑（mm）
        /// </summary>
        public double Diameter { get; set; }

        /// <summary>
        /// 長度（mm）
        /// </summary>
        public double Length { get; set; }

        /// <summary>
        /// 方向向量
        /// </summary>
        public XYZ Direction { get; set; }

        /// <summary>
        /// 族群名稱
        /// </summary>
        public string FamilyName { get; set; }

        /// <summary>
        /// 類型名稱
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// 材料
        /// </summary>
        public string Material { get; set; }

        /// <summary>
        /// 順序編號（用於 ISO 圖）
        /// </summary>
        public int SequenceNumber { get; set; }

        /// <summary>
        /// 連接的下一個元件
        /// </summary>
        public PipeSegment NextSegment { get; set; }

        /// <summary>
        /// 連接的上一個元件
        /// </summary>
        public PipeSegment PreviousSegment { get; set; }

        /// <summary>
        /// 是否為主管線（用於分支判斷）
        /// </summary>
        public bool IsMainPipe { get; set; }

        /// <summary>
        /// 分支編號（0 為主管線）
        /// </summary>
        public int BranchNumber { get; set; }

        /// <summary>
        /// 計算兩點之間的距離（mm）
        /// </summary>
        public static double CalculateDistance(XYZ point1, XYZ point2)
        {
            double distanceFeet = point1.DistanceTo(point2);
            return UnitUtils.ConvertFromInternalUnits(distanceFeet, UnitTypeId.Millimeters);
        }

        /// <summary>
        /// 判斷是否為水平管線（容許誤差 5 度）
        /// </summary>
        public bool IsHorizontal()
        {
            if (Direction == null)
                return false;

            double angle = Math.Abs(Direction.Z);
            return angle < Math.Sin(5 * Math.PI / 180); // 小於 5 度視為水平
        }

        /// <summary>
        /// 判斷是否為垂直管線（容許誤差 5 度）
        /// </summary>
        public bool IsVertical()
        {
            if (Direction == null)
                return false;

            double angle = Math.Abs(Direction.Z);
            return angle > Math.Cos(5 * Math.PI / 180); // 大於 85 度視為垂直
        }

        /// <summary>
        /// 取得 PCF 格式的元件類型代碼
        /// </summary>
        public string GetPCFTypeCode()
        {
            switch (Type?.ToUpper())
            {
                case "PIPE":
                    return "PIPE";
                case "ELBOW":
                    return "ELBOW";
                case "TEE":
                    return "TEE";
                case "REDUCER":
                    return "REDUCER";
                case "FLANGE":
                    return "FLANGE";
                case "VALVE":
                    return "VALVE";
                case "CAP":
                    return "CAP";
                case "COUPLING":
                    return "COUPLING";
                default:
                    return "PIPE-COMPONENT";
            }
        }

        public override string ToString()
        {
            return $"{Type} - Ø{Diameter:F0}mm - L:{Length:F0}mm - Seq:{SequenceNumber}";
        }
    }
}
