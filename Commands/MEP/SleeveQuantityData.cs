using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveQuantityData
    {
        internal static readonly string[] Names = { "納入", "統計設定", "區域", "樓層識別", "樓層", "系統", "族型", "套管規格", "長度mm", "檢核狀態", "群組", "編號", "標高mm", "管線DN", "材質", "穿越構件" };
        internal static Guid Id(int index) => new Guid("f2d17e13-e5ad-451b-a37f-" + (index + 1).ToString("x12", CultureInfo.InvariantCulture));
        internal static string Name(int index) => "HB_套管表_" + Names[index];
        internal static bool Supported(FamilyInstance fi) => fi?.Category != null &&
            (fi.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_GenericModel ||
             fi.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_PipeAccessory);

        internal static string Text(Element e, params string[] names)
        {
            foreach (var name in names)
            {
                var p = e?.LookupParameter(name);
                if (p?.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(p.AsString())) return p.AsString();
            }
            return "";
        }

        private static double Size(FamilyInstance fi, params string[] names)
        {
            foreach (var e in new Element[] { fi, fi.Symbol })
                foreach (var name in names)
                {
                    var p = e?.LookupParameter(name);
                    if (p?.StorageType == StorageType.Double && p.Definition.GetDataType() == SpecTypeId.Length && p.AsDouble() > 0)
                        return p.AsDouble();
                }
            return 0;
        }

        private static string MaterialName(FamilyInstance fi)
        {
            foreach (var e in new Element[] { fi, fi.Symbol })
                foreach (var name in new[] { "套管材質", "材質", "材料", "Material" })
                {
                    var p = e?.LookupParameter(name);
                    if (p?.StorageType == StorageType.ElementId && fi.Document.GetElement(p.AsElementId()) is Material material) return material.Name;
                    if (p?.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(p.AsString())) return p.AsString();
                }
            return "未指定";
        }

        internal static bool Automatic(FamilyInstance fi)
        {
            // CAD records also include non-sleeve components. Names alone never enroll a manual instance.
            string name = (fi.Symbol?.FamilyName ?? "") + " " + (fi.Symbol?.Name ?? "");
            bool sleeve = name.IndexOf("套管", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("sleeve", StringComparison.OrdinalIgnoreCase) >= 0;
            return sleeve && (RaftCadRecord.IsManaged(fi) || SleeveSourceRecord.HasRecord(fi) || long.TryParse(Text(fi, "來源管線Id", "Pipe Id", "Source Pipe Id"), out long source) && source > 0);
        }

        internal static List<SleeveQuantityRow> Read(Document doc, ISet<long> manual = null, ISet<long> affected = null)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(fi => affected == null || affected.Contains(fi.Id.GetIdValue()))
                .Where(Supported).Where(fi => Automatic(fi) || !string.IsNullOrEmpty(fi.get_Parameter(Id(1))?.AsString()) || manual?.Contains(fi.Id.GetIdValue()) == true)
                .Select(fi => ReadRow(doc, fi, manual?.Contains(fi.Id.GetIdValue()) == true)).ToList();
        }

        private static SleeveQuantityRow ReadRow(Document doc, FamilyInstance fi, bool manual)
        {
            string control = manual ? "納入" : fi.get_Parameter(Id(1))?.AsString() ?? "";
            var original = PipeSleeveManagerForm.PipeSleeveManagerRow.From(doc, fi);
            bool hasSource = SleeveSourceRecord.HasRecord(fi) || long.TryParse(Text(fi, "來源管線Id", "Pipe Id", "Source Pipe Id"), out long source) && source > 0;
            string status = !hasSource && !RaftCadRecord.IsManaged(fi) ? "手動套管待確認" : original.Status;
            if (!string.IsNullOrWhiteSpace(control) && control != "納入" && control != "排除" && control != "作廢") status = "統計設定無效";
            double length = Size(fi, "套管長度", "長度", "Length", "Sleeve Length", "深度", "Depth");
            string spec = SleeveQuantityPolicy.Specification(Size(fi, "開孔寬度", "寬度", "Width", "Sleeve Width"),
                Size(fi, "開孔高度", "高度", "Height", "Sleeve Height"), Size(fi, "管外直徑", "Outer Diameter", "Outside Diameter"),
                Size(fi, "有效內徑", "套管內徑", "內徑", "Clear Inside Diameter", "Inside Diameter", "Inner Diameter"),
                Size(fi, "套管直徑", "直徑", "Diameter", "Sleeve Diameter", "Diatot"));
            if (length <= 0 || spec == "待確認") status += "／尺寸待確認";
            var level = SleeveGlLevel.Actual(doc, fi);
            var point = (fi.Location as LocationPoint)?.Point;
            return new SleeveQuantityRow { Id = fi.Id, Control = control, Included = SleeveQuantityPolicy.Include(control, Automatic(fi)),
                LevelKey = level?.UniqueId ?? "", Level = level?.Name ?? "未指定樓層", Zone = fi.get_Parameter(Id(2))?.AsString() ?? "",
                System = original.SystemName, Type = original.TypeName, Specification = spec, Length = SleeveQuantityPolicy.Millimetres(length),
                Status = status, Mark = SleeveStableNumbering.Mark(fi), PipeDn = original.NominalDiameter, Material = MaterialName(fi), Host = original.HostType,
                Elevation = point != null && level != null ? ((point.Z - level.ProjectElevation) * 304.8).ToString("0.###", CultureInfo.InvariantCulture) : "待確認" };
        }

        internal static void EnsureParameters(Document doc)
        {
            var app = doc.Application;
            string previous = app.SharedParametersFilename;
            string temp = Path.Combine(Path.GetTempPath(), "HB-Sleeve-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(temp, "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n" +
                    "*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
                app.SharedParametersFilename = temp;
                var group = app.OpenSharedParameterFile().Groups.Create("HB_SleeveQuantity");
                for (int i = 0; i < Names.Length; i++)
                {
                    var existing = SharedParameterElement.Lookup(doc, Id(i));
                    var definition = existing?.GetDefinition();
                    if (definition != null && definition.GetDataType() != SpecTypeId.String.Text)
                        throw new InvalidOperationException(Name(i) + " 的參數型別不相容。");
                    Definition def = definition ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(Name(i), SpecTypeId.String.Text)
                    { GUID = Id(i), UserModifiable = i == 1 || i == 2 });
                    var bound = doc.ParameterBindings.get_Item(def);
                    if (bound != null && !(bound is InstanceBinding)) throw new InvalidOperationException(Name(i) + " 必須是實例參數。");
                    var categories = app.Create.NewCategorySet();
                    if (bound is InstanceBinding old) foreach (Category c in old.Categories) categories.Insert(c);
                    var generic = doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
                    var accessory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipeAccessory);
                    if (bound is InstanceBinding valid && valid.Categories.Contains(generic) && valid.Categories.Contains(accessory)) continue;
                    categories.Insert(generic); categories.Insert(accessory);
                    var binding = app.Create.NewInstanceBinding(categories);
                    bool ok = bound == null ? doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data) : doc.ParameterBindings.ReInsert(def, binding, GroupTypeId.Data);
                    if (!ok) throw new InvalidOperationException("無法綁定套管明細表參數：" + Name(i));
                }
            }
            finally { app.SharedParametersFilename = previous; if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static List<SleeveQuantityRow> Synchronize(Document doc, ISet<long> manual = null, ISet<long> affected = null)
        {
            if (SharedParameterElement.Lookup(doc, Id(0)) == null) return new List<SleeveQuantityRow>();
            var rows = Read(doc, manual, affected);
            var rowIds = new HashSet<long>(rows.Select(r => r.Id.GetIdValue()));
            // A former sleeve may have changed family or lost its source metadata.
            foreach (var fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(fi => affected == null || affected.Contains(fi.Id.GetIdValue())).Where(Supported))
                if (!rowIds.Contains(fi.Id.GetIdValue()) && fi.get_Parameter(Id(0))?.AsString() == "1") Set(fi, 0, "0");
            foreach (var row in rows)
            {
                var fi = doc.GetElement(row.Id);
                if (manual?.Contains(row.Id.GetIdValue()) == true) Set(fi, 1, "納入");
                var values = new Dictionary<int, string> { {0, row.Included ? "1" : "0"}, {3, row.LevelKey}, {4, row.Level}, {5, row.System},
                    {6, row.Type}, {7, row.Specification}, {8, row.Length}, {9, row.Status}, {10, row.GroupKey}, {11, row.Mark}, {12, row.Elevation}, {13, row.PipeDn}, {14, row.Material}, {15, row.Host} };
                foreach (var value in values) Set(fi, value.Key, value.Value);
            }
            return rows;
        }

        private static void Set(Element fi, int index, string value)
        {
            var p = fi.get_Parameter(Id(index));
            if (p == null) throw new InvalidOperationException("套管缺少明細表參數：" + Name(index));
            if ((p.AsString() ?? "") == (value ?? "")) return;
            if (p.IsReadOnly || !p.Set(value ?? "")) throw new InvalidOperationException("無法更新套管 " + fi.Id + " 的「" + Name(index) + "」。");
        }
    }
}
