// 僅供離線匯出測試的輸入資料替身；不模擬 Revit 收集、交易或視圖 API。
namespace Autodesk.Revit.DB
{
    public class XYZ { public double X, Y, Z; }
    public class UnitTypeId { public static UnitTypeId Millimeters = new(); }
    public class UnitUtils
    {
        public static double ConvertFromInternalUnits(double value, UnitTypeId unit) => value * 304.8;
    }
}

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models
{
    public class ISOData
    {
        public string ProjectName, ISONumber, SystemName, SystemType;
        public DateTime CreatedDate;
        public double TotalLength, TotalWeight;
        public List<PipeSegment> MainPipeSegments = new();
        public Dictionary<int, List<PipeSegment>> BranchSegments = new();
        public List<BOMItem> BillOfMaterials = new();
    }
    public class PipeSegment
    {
        public string Type, TypeName, Material;
        public int SequenceNumber;
        public double Diameter, Length;
        public Autodesk.Revit.DB.XYZ StartPoint, EndPoint, CenterPoint;
    }
    public class BOMItem
    {
        public int ItemNumber, Quantity;
        public string Type, Description, Material, Unit, SizeText;
        public double Diameter, TotalLength, TotalWeight;
    }
}
