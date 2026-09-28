using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Helpers;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    // 單次生成共用的範圍。連線讀取不完整時仍保留元件，核對表明列原因。
    public sealed class SystemSnapshot
    {
        public sealed class Row
        {
            public Element Element;
            public long Id;
            public string UniqueId;
            public string Scope;
            public bool Included;
            public double? LengthMm;
            internal PipeMeasurement Measurement;
            public string Code => (Element is Pipe ? "P" : "E") + Id;
            public readonly List<string> Issues = new List<string>();
            public readonly List<string> Ports = new List<string>();
            public readonly SortedSet<long> External = new SortedSet<long>();
            public int OpenPorts;
        }
        public List<Row> Rows { get; } = new List<Row>();
        public ConnectivityGraph Graph { get; private set; }
        public string SystemUniqueId { get; private set; }
        public IEnumerable<Element> Elements => Rows.Where(r => r.Included).Select(r => r.Element);

        public static SystemSnapshot Capture(PipingSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            var result = new SystemSnapshot { SystemUniqueId = system.UniqueId };
            var network = system.PipingNetwork;
            if (network == null || network.Size == 0) throw new InvalidOperationException("系統沒有管路元件。");
            foreach (Element element in network.Cast<Element>().GroupBy(e => e.Id.GetIdValue())
                .Select(g => g.First()).OrderBy(e => e.Id.GetIdValue()))
            {
                long category = element.Category?.Id.GetIdValue() ?? 0;
                bool included = element is Pipe || (element is FamilyInstance &&
                    (category == (long)BuiltInCategory.OST_PipeFitting ||
                     category == (long)BuiltInCategory.OST_PipeAccessory ||
                     category == (long)BuiltInCategory.OST_Sprinklers ||
                     category == (long)BuiltInCategory.OST_PlumbingFixtures));
                var row = new Row { Element = element, Id = element.Id.GetIdValue(),
                    UniqueId = element.UniqueId, Included = included,
                    Scope = included ? "納入" : "排除：尚未支援此元件類別" };
                if (element is Pipe pipe)
                {
                    if (pipe.Location is LocationCurve location)
                    {
                        row.LengthMm = UnitUtils.ConvertFromInternalUnits(location.Curve.Length, UnitTypeId.Millimeters);
                        XYZ a = location.Curve.GetEndPoint(0), b = location.Curve.GetEndPoint(1);
                        double Mm(double value) => UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters);
                        row.Measurement = new PipeMeasurement { X0 = Mm(a.X), Y0 = Mm(a.Y), Z0 = Mm(a.Z),
                            X1 = Mm(b.X), Y1 = Mm(b.Y), Z1 = Mm(b.Z), LengthMm = row.LengthMm.Value,
                            DiameterMm = Mm(pipe.Diameter), IsStraight = location.Curve is Line };
                    }
                    else row.Issues.Add("模型長度無法讀取");
                }
                result.Rows.Add(row);
            }
            if (!result.Elements.Any()) throw new InvalidOperationException("系統沒有支援的管路元件。");
            result.Graph = new ConnectivityGraph(result.Rows.Select(r => r.Id));
            var scope = new HashSet<long>(result.Rows.Select(r => r.Id));
            foreach (var row in result.Rows) result.ReadConnections(row, scope);
            return result;
        }

        private void ReadConnections(Row row, HashSet<long> scope)
        {
            try
            {
                var manager = (row.Element as MEPCurve)?.ConnectorManager ??
                    (row.Element as FamilyInstance)?.MEPModel?.ConnectorManager;
                if (manager == null) { row.Issues.Add("沒有可讀取的接頭資料"); return; }
                int count = 0;
                foreach (Connector port in manager.Connectors)
                {
                    if (port.Domain != Domain.DomainPiping || port.ConnectorType == ConnectorType.Logical) continue;
                    count++;
                    try
                    {
                        string size = port.Shape == ConnectorProfileType.Round
                            ? ExportFormatting.Number(UnitUtils.ConvertFromInternalUnits(port.Radius * 2, UnitTypeId.Millimeters), "0.###")
                            : "非圓形";
                        row.Ports.Add(port.Id + ":" + size);
                        bool connected = false;
                        foreach (Connector other in port.AllRefs)
                        {
                            if (other.Owner.Id == row.Element.Id || other.Domain != Domain.DomainPiping ||
                                other.ConnectorType == ConnectorType.Logical || !port.IsConnectedTo(other)) continue;
                            connected = true;
                            long otherId = other.Owner.Id.GetIdValue();
                            if (scope.Contains(otherId)) Graph.Connect(row.Id, otherId);
                            else row.External.Add(otherId);
                        }
                        if (!connected)
                        {
                            if (port.IsConnected) row.Issues.Add("接頭 " + port.Id + " 已連接但無可辨識的實體對端");
                            else row.OpenPorts++;
                        }
                    }
                    catch (Exception ex) { row.Issues.Add("接頭讀取不完整：" + ex.Message); }
                }
                if (count == 0) row.Issues.Add("沒有實體管路接頭");
            }
            catch (Exception ex) { row.Issues.Add("連通讀取不完整：" + ex.Message); }
        }

        public void ExportAudit(Document doc, string systemName, string path)
        {
            var groups = Graph.Components();
            AtomicOutput.Write(path, writer =>
            {
                Write(writer, "文件", "文件路徑", "系統", "系統UniqueId", "ElementId", "UniqueId", "類別", "類型", "算量範圍",
                    "連通群組", "相連ElementId", "範圍外ElementId", "接頭ID:直徑mm", "未接端數", "模型長度mm", "材料", "加工狀態", "檢查事項", "圖面編號");
                foreach (var row in Rows)
                    Write(writer, doc.Title, doc.PathName, systemName, SystemUniqueId, row.Id.ToString(), row.UniqueId,
                        row.Element.Category?.Name, doc.GetElement(row.Element.GetTypeId())?.Name, row.Scope,
                        groups[row.Id].ToString(), string.Join(";", Graph.Neighbors(row.Id)), string.Join(";", row.External),
                        string.Join(";", row.Ports.OrderBy(p => p, StringComparer.Ordinal)), row.OpenPorts.ToString(),
                        row.LengthMm.HasValue ? ExportFormatting.Number(row.LengthMm.Value, "0.###") : "",
                        ISOData.ReadMaterial(row.Element), "待確認接法／規格／扣長；模型長度非切管長度", string.Join(";", row.Issues), row.Code);
            });
        }
        public void ExportPipes(Document doc, string systemName, string path)
        {
            AtomicOutput.Write(path, writer =>
            {
                Write(writer, "文件", "系統", "系統UniqueId", "圖面編號", "ElementId", "UniqueId", "類型", "材料",
                    "管徑mm", "模型長度mm", "端0_Xmm", "端0_Ymm", "端0_Zmm", "端1_Xmm", "端1_Ymm", "端1_Zmm",
                    "直管坡度絕對值%", "座標基準", "加工切長mm", "說明");
                foreach (var row in Rows.Where(r => r.Included && r.Element is Pipe))
                {
                    var m = row.Measurement;
                    if (m == null) throw new InvalidOperationException("管線 " + row.Id + " 沒有完整尺寸資料。");
                    string N(double value) => ExportFormatting.Number(value, "0.###");
                    Write(writer, doc.Title, systemName, SystemUniqueId, row.Code, row.Id.ToString(), row.UniqueId,
                        doc.GetElement(row.Element.GetTypeId())?.Name, ISOData.ReadMaterial(row.Element), N(m.DiameterMm), N(m.LengthMm),
                        N(m.X0), N(m.Y0), N(m.Z0), N(m.X1), N(m.Y1), N(m.Z1), m.SlopeText,
                        "Revit 內部原點／內部座標軸，非共享座標或專案高程", "", "端0／端1非流向；接法與扣長尚未確認");
                }
            });
        }
        private static void Write(TextWriter writer, params string[] cells)
        {
            writer.WriteLine(ExportFormatting.CsvRow(cells));
        }
        public string Summary => $"範圍 {Rows.Count} 件，算量 {Elements.Count()} 件，排除 {Rows.Count(r => !r.Included)} 件；" +
            $"連通群組 {Graph.Components().Values.Distinct().Count()} 組，未接端 {Rows.Sum(r => r.OpenPorts)}，讀取異常 {Rows.Count(r => r.Issues.Count > 0)} 件（未接端不一定是錯誤）";
    }
}
