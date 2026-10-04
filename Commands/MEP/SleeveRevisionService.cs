using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveRevisionService
    {
        internal static List<SleeveRevisionSnapshot> List(Document doc)
        {
            var list = SleeveDocumentStorage.Find(doc, SleeveDocumentStorage.RevisionSchema)
                .Select(s => SleeveDocumentStorage.Read<SleeveRevisionSnapshot>(s, SleeveDocumentStorage.RevisionSchema)).ToList();
            foreach (var s in list) SleeveRevisionPolicy.Validate(s);
            if (list.GroupBy(s => s.Id).Any(g => g.Count() > 1)) throw new InvalidOperationException("套管基準紀錄重複，請檢查模型。");
            return list.OrderByDescending(s => s.CapturedUtc).ToList();
        }
        internal static SleeveRevisionSnapshot Capture(Document doc, SleeveRevisionSnapshot baseline = null)
        {
            var result = new SleeveRevisionSnapshot { Name = "目前模型" };
            foreach (var r in SleeveQuantityData.Read(doc))
            {
                var fi = (FamilyInstance)doc.GetElement(r.Id);
                var transform = fi.GetTransform();
                var p = (fi.Location as LocationPoint)?.Point ?? transform.Origin;
                result.Records.Add(new SleeveRevisionRecord { Id = fi.UniqueId, Mark = SleeveStableNumbering.Mark(fi),
                    LevelKey = r.LevelKey, Level = r.Level, Zone = r.Zone, System = r.System, Type = r.Type,
                    Specification = r.Specification, Length = r.Length, Material = r.Material, Host = r.Host,
                    Status = r.Status, Control = r.Control, Included = r.Included,
                    PositionMm = new[] {p.X * 304.8, p.Y * 304.8, p.Z * 304.8},
                    Orientation = new[] {transform.BasisX.X, transform.BasisX.Y, transform.BasisX.Z,
                        transform.BasisY.X, transform.BasisY.Y, transform.BasisY.Z, transform.BasisZ.X, transform.BasisZ.Y, transform.BasisZ.Z} });
            }
            // A surviving element that lost enrollment is not a deletion. Retain its identity explicitly.
            var ids = new HashSet<string>(result.Records.Select(r => r.Id));
            foreach (var old in baseline?.Records ?? Enumerable.Empty<SleeveRevisionRecord>())
                if (!ids.Contains(old.Id) && doc.GetElement(old.Id) is FamilyInstance fi)
                    result.Records.Add(new SleeveRevisionRecord { Id = fi.UniqueId, Mark = SleeveStableNumbering.Mark(fi),
                        Recognized = false, Included = false, Control = "未辨識", Status = "仍存在模型，已不在套管統計範圍" });
            SleeveRevisionPolicy.Validate(result);
            return result;
        }
        internal static SleeveRevisionSnapshot Save(Document doc, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 80) throw new InvalidOperationException("請輸入 1～80 字的基準名稱。");
            if (List(doc).Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("基準名稱已存在；請使用新名稱，既有基準不會覆寫。");
            using (var tx = new Transaction(doc, "套管補編號並儲存版本基準"))
            {
                tx.Start();
                SleeveQuantityData.EnsureParameters(doc); doc.Regenerate();
                var rows = SleeveQuantityData.Read(doc);
                SleeveStableNumbering.Assign(doc, rows.Where(r => r.Included).Select(r => doc.GetElement(r.Id)).OfType<FamilyInstance>());
                SleeveQuantityData.Synchronize(doc); doc.Regenerate();
                var snapshot = Capture(doc); snapshot.Name = name;
                SleeveDocumentStorage.Write(doc, SleeveDocumentStorage.RevisionSchema, snapshot);
                if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 未提交套管基準。");
                return snapshot;
            }
        }
    }
}
