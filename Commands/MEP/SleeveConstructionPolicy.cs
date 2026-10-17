using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveConstructionItem
    {
        public string Id { get; set; }
        public string Mark { get; set; }
        public string Specification { get; set; }
        public string Length { get; set; }
        public string System { get; set; }
        public string Host { get; set; }
        public string Check { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double OffsetA { get; set; }
        public double OffsetB { get; set; }
        public double Elevation { get; set; }
        public double[] Orientation { get; set; }
        public string NoteId { get; set; }
        public string NoteText { get; set; }
    }
    internal sealed class SleeveFieldRecord
    {
        public string ElementId { get; set; }
        public string Installation { get; set; } = "未施工";
        public string Inspection { get; set; } = "未查驗";
        public double? MeasuredA { get; set; }
        public double? MeasuredB { get; set; }
        public double? MeasuredElevation { get; set; }
        public string Person { get; set; } = "";
        public string Date { get; set; } = "";
        public string PhotoReference { get; set; } = "";
        public string Remark { get; set; } = "";
    }
    internal sealed class SleeveConstructionPackage
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public string Revision { get; set; }
        public string Status { get; set; } = "草稿";
        public string SourceViewId { get; set; }
        public string SourceViewName { get; set; }
        public string PlanId { get; set; }
        public string GridAId { get; set; }
        public string GridBId { get; set; }
        public string GridAName { get; set; }
        public string GridBName { get; set; }
        public double[] GridFrame { get; set; }
        public string LevelKey { get; set; } = "";
        public string LevelName { get; set; }
        public string Zone { get; set; } = "";
        public double DatumElevation { get; set; }
        public string DatumName { get; set; }
        public string ApprovalReference { get; set; } = "";
        public string ApprovedBy { get; set; } = "";
        public DateTime? ApprovedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public List<SleeveConstructionItem> Items { get; set; } = new List<SleeveConstructionItem>();
        public List<string> SheetIds { get; set; } = new List<string>();
        public List<string> TableIds { get; set; } = new List<string>();
        public List<SleeveFieldRecord> FieldRecords { get; set; } = new List<SleeveFieldRecord>();
        public string HeaderId { get; set; }
        public string HeaderText { get; set; }
    }
    internal static class SleeveConstructionPolicy
    {
        internal static readonly string[] Installations = { "未施工", "已放樣", "已安裝", "已拆除" };
        internal static readonly string[] Inspections = { "未查驗", "待改善", "合格" };
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        internal static void Validate(SleeveConstructionPackage p)
        {
            if (p == null || p.Version != 1 || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Revision) ||
                p.Name.Length > 60 || p.Revision.Length > 30 || p.Items == null || p.Items.Count == 0 ||
                p.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.Id) || string.IsNullOrWhiteSpace(i.Mark) ||
                    !new[] {i.X,i.Y,i.Z,i.OffsetA,i.OffsetB,i.Elevation}.All(Finite)) ||
                p.Items.GroupBy(i => i.Id).Any(g => g.Count() > 1) ||
                p.Items.GroupBy(i => SleeveNumberPolicy.Key(i.Mark)).Any(g => g.Count() > 1))
                throw new InvalidOperationException("施工包資料不完整、套管空號或重號，請先完成套管檢查與補號。");
            if (p.GridFrame == null || p.GridFrame.Length != 12 || !p.GridFrame.All(Finite) || !Finite(p.DatumElevation))
                throw new InvalidOperationException("缺少定位軸線與高程基準。");
        }
        internal static string Changed(SleeveConstructionItem before, SleeveConstructionItem after)
        {
            if (after == null) return "刪除／移出範圍";
            if (before == null) return "新增";
            var changes = new List<string>();
            if (Math.Sqrt(Math.Pow(before.X-after.X,2)+Math.Pow(before.Y-after.Y,2)+Math.Pow(before.Z-after.Z,2))>.5) changes.Add("移位");
            if (before.Specification!=after.Specification || before.Length!=after.Length) changes.Add("尺寸變更");
            if (before.Mark!=after.Mark) changes.Add("編號變更");
            if (before.Orientation!=null && after.Orientation!=null && (before.Orientation.Length!=after.Orientation.Length ||
                before.Orientation.Zip(after.Orientation,(a,b)=>Math.Abs(a-b)).Any(d=>d>1e-8))) changes.Add("方向變更");
            if (Math.Abs(before.OffsetA-after.OffsetA)>.5 || Math.Abs(before.OffsetB-after.OffsetB)>.5 || Math.Abs(before.Elevation-after.Elevation)>.5) changes.Add("定位基準／尺寸變更");
            if (before.System!=after.System || before.Host!=after.Host || before.Check!=after.Check) changes.Add("系統／穿越／檢核變更");
            return string.Join("、",changes);
        }
        internal static void ValidateField(SleeveFieldRecord r)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.ElementId) || !Installations.Contains(r.Installation) || !Inspections.Contains(r.Inspection) ||
                new[] {r.MeasuredA,r.MeasuredB,r.MeasuredElevation}.Any(v=>v.HasValue&&!Finite(v.Value)))
                throw new InvalidOperationException("施工、查驗狀態或實測數值無效。");
            if (r.Inspection=="合格" && (r.Installation!="已安裝" || string.IsNullOrWhiteSpace(r.Person) ||
                !DateTime.TryParseExact(r.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _) ||
                !r.MeasuredA.HasValue || !r.MeasuredB.HasValue || !r.MeasuredElevation.HasValue))
                throw new InvalidOperationException("查驗合格需已安裝、查驗人、日期 yyyy-MM-dd 與三項實測值；是否符合允差由查驗人確認。");
        }
        internal static void Approve(SleeveConstructionPackage p, string person, string reference)
        {
            Validate(p);
            if (p.Status!="草稿" || string.IsNullOrWhiteSpace(person) || string.IsNullOrWhiteSpace(reference))
                throw new InvalidOperationException("僅可登錄草稿的人工核准結果，請填核准人與核准文件／簽核編號。");
            if (p.Items.Any(i=>i.Check!="正常"&&i.Check!="偏心可容納"))
                throw new InvalidOperationException("仍有待確認／待處理套管，請先處理並更新施工包。");
            p.Status="核准施工"; p.ApprovedBy=person.Trim(); p.ApprovalReference=reference.Trim(); p.ApprovedUtc=DateTime.UtcNow;
        }
        internal static string Cell(string text)
        {
            text=text??"";
            string probe=text.TrimStart();
            if(probe.Length>0&&"=+-@".IndexOf(probe[0])>=0) text="'"+text;
            return "\""+text.Replace("\"","\"\"")+"\"";
        }
        internal static string Number(double value) => value.ToString("0.###",CultureInfo.InvariantCulture);
        internal static string Csv(SleeveConstructionPackage p)
        {
            Validate(p);
            var b=new StringBuilder("施工包,版次,發圖狀態,構件UniqueId,套管編號,系統,規格,長度mm,穿越構件,軸線A,距A軸mm,軸線B,距B軸mm,高程基準,中心標高mm,檢核,施工狀態,查驗狀態,實測Amm,實測Bmm,實測標高mm,A偏差mm,B偏差mm,標高偏差mm,查驗人,日期,照片索引,備註\r\n");
            foreach(var i in p.Items)
            {
                var r=p.FieldRecords.SingleOrDefault(x=>x.ElementId==i.Id)??new SleeveFieldRecord{ElementId=i.Id};
                string N(double? v)=>v.HasValue?Number(v.Value):"";
                var cells=new[]{Cell(p.Name),Cell(p.Revision),Cell(p.Status),Cell(i.Id),Cell(i.Mark),Cell(i.System),Cell(i.Specification),Cell(i.Length),Cell(i.Host),
                    Cell(p.GridAName),Number(i.OffsetA),Cell(p.GridBName),Number(i.OffsetB),Cell(p.DatumName),Number(i.Elevation),Cell(i.Check),Cell(r.Installation),Cell(r.Inspection),
                    N(r.MeasuredA),N(r.MeasuredB),N(r.MeasuredElevation),N(r.MeasuredA-i.OffsetA),N(r.MeasuredB-i.OffsetB),N(r.MeasuredElevation-i.Elevation),
                    Cell(r.Person),Cell(r.Date),Cell(r.PhotoReference),Cell(r.Remark)};
                b.AppendLine(string.Join(",",cells));
            }
            return b.ToString();
        }
    }
}
