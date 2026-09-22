using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;
using RevitFamily = Autodesk.Revit.DB.Family;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveFamilyCatalog
    {
        internal static readonly string[] Presets = { "套管-圓形_無", "開孔-矩形_無" };

        internal static bool IsSleeveSymbol(FamilySymbol symbol)
        {
            if (symbol?.Category == null) return false;
            long category = symbol.Category.Id.GetIdValue();
            if (category != (long)BuiltInCategory.OST_PipeAccessory && category != (long)BuiltInCategory.OST_GenericModel) return false;
            string text = (symbol.FamilyName ?? "") + " " + (symbol.Name ?? "");
            return new[] { "sleeve", "套管", "開孔", "开孔" }.Any(t => text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) &&
                !new[] { "消防", "子母", "母管", "管束", "閥", "阀", "valve", "sprinkler", "窗", "window", "door", "門", "风口", "風口", "grille", "louver" }
                    .Any(t => text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // UI entry points and creation use the same missing-only policy; updates require explicit user action.
        internal static List<string> LoadMissing(Document doc)
        {
            var errors = new List<string>();
            var loaded = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(RevitFamily)).Cast<RevitFamily>().Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            string folder = Path.Combine(Path.GetDirectoryName(typeof(SleeveFamilyCatalog).Assembly.Location), "Resources", "Families");
            foreach (string name in Presets)
            {
                if (loaded.Contains(name)) continue;
                string path = Path.Combine(folder, name + ".rfa");
                if (!File.Exists(path)) { errors.Add(name + "：找不到預設族群檔"); continue; }
                Transaction tx = null;
                SubTransaction sub = null;
                try
                {
                    if (doc.IsModifiable) { sub = new SubTransaction(doc); sub.Start(); }
                    else { tx = new Transaction(doc, "載入預設族群 " + name); tx.Start(); }
                    if (!doc.LoadFamily(path, new PreserveExisting(), out RevitFamily family) || family == null || family.Name != name)
                        throw new InvalidOperationException("載入失敗，請確認族群名稱與 Revit 版本");
                    var status = sub != null ? sub.Commit() : tx.Commit();
                    if (status != TransactionStatus.Committed) throw new InvalidOperationException("載入未完成提交");
                    loaded.Add(name);
                }
                catch (Exception ex)
                {
                    if (sub?.GetStatus() == TransactionStatus.Started) sub.RollBack();
                    if (tx?.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    errors.Add(name + "：" + ex.Message);
                }
                finally { sub?.Dispose(); tx?.Dispose(); }
            }
            return errors;
        }

        private sealed class PreserveExisting : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = false; return false; }
            public bool OnSharedFamilyFound(RevitFamily sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Project; overwriteParameterValues = false; return true; }
        }
    }
}
