using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Models;

namespace YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Services
{
    public class FamilyParameterRenameService
    {
        private readonly Document _document;
        private static readonly IReadOnlyList<KeyValuePair<string, string>> NameTranslations =
            new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("\u53c2\u7167\u3057\u3066\u3044\u308b\u4ed5\u69d8\u66f8\u7b49\u306e\u30d0\u30fc\u30b8\u30e7\u30f3", "\u53c3\u7167\u898f\u683c\u66f8\u7b49\u7248\u672c"),
                new KeyValuePair<string, string>("\u30c7\u30fc\u30bf\u4f5c\u6210\u30bd\u30d5\u30c8", "\u8cc7\u6599\u5efa\u7acb\u8edf\u9ad4"),
                new KeyValuePair<string, string>("\u6d88\u8017\u54c1\u30fb\u5099\u54c1\u60c5\u5831", "\u6d88\u8017\u54c1\u8207\u5099\u54c1\u8cc7\u8a0a"),
                new KeyValuePair<string, string>("\u30d5\u30a1\u30a4\u30eb\u5f62\u5f0f", "\u6a94\u6848\u683c\u5f0f"),
                new KeyValuePair<string, string>("\u4ed5\u69d8\u30d0\u30fc\u30b8\u30e7\u30f3", "\u898f\u683c\u7248\u672c"),
                new KeyValuePair<string, string>("\u30ab\u30a6\u30f3\u30bf\u30fc\u8272", "\u6ab3\u9762\u984f\u8272"),
                new KeyValuePair<string, string>("\u30c7\u30fc\u30bf\u4f5c\u6210\u5e74\u6708", "\u8cc7\u6599\u5efa\u7acb\u5e74\u6708"),
                new KeyValuePair<string, string>("\u30d1\u30cd\u30eb\u8272", "\u9762\u677f\u984f\u8272"),
                new KeyValuePair<string, string>("\u4ed8\u5c5e\u5358\u4f4d", "\u9644\u5c6c\u55ae\u4f4d"),
                new KeyValuePair<string, string>("\u4f01\u696d\u30b3\u30fc\u30c9", "\u4f01\u696d\u4ee3\u78bc"),
                new KeyValuePair<string, string>("\u4e0a\u6c34\u8ca0\u8377\u5358\u4f4d", "\u7d66\u6c34\u8ca0\u8377\u55ae\u4f4d"),
                new KeyValuePair<string, string>("\u4e0a\u6c34\u63a5\u7d9a\u53e3", "\u7d66\u6c34\u63a5\u7e8c\u53e3"),
                new KeyValuePair<string, string>("\u4e0a\u6c34", "\u7d66\u6c34"),
                new KeyValuePair<string, string>("\u4e2d\u6c34\u8ca0\u8377\u5358\u4f4d", "\u4e2d\u6c34\u8ca0\u8377\u55ae\u4f4d"),
                new KeyValuePair<string, string>("\u4e2d\u6c34\u63a5\u7d9a\u53e3", "\u4e2d\u6c34\u63a5\u7e8c\u53e3"),
                new KeyValuePair<string, string>("\u6c5a\u6c34\u8ca0\u8377\u5358\u4f4d", "\u6c61\u6c34\u8ca0\u8377\u55ae\u4f4d"),
                new KeyValuePair<string, string>("\u6d17\u6d44\u6c34\u91cf", "\u6c96\u6d17\u6c34\u91cf"),
                new KeyValuePair<string, string>("\u6cd5\u5b9a\u8010\u7528\u5e74\u6570", "\u6cd5\u5b9a\u8010\u7528\u5e74\u6578"),
                new KeyValuePair<string, string>("\u6d41\u91cf", "\u6d41\u91cf"),
                new KeyValuePair<string, string>("\u8ca0\u8377\u5206\u985e", "\u8ca0\u8377\u5206\u985e"),
                new KeyValuePair<string, string>("\u6d88\u8cbb\u96fb\u529b", "\u6d88\u8017\u96fb\u529b"),
                new KeyValuePair<string, string>("\u5468\u6ce2\u6570", "\u983b\u7387"),
                new KeyValuePair<string, string>("\u30b3\u30e1\u30f3\u30c8", "\u8a3b\u89e3"),
                new KeyValuePair<string, string>("\u30d0\u30fc\u30b8\u30e7\u30f3", "\u7248\u672c"),
                new KeyValuePair<string, string>("\u30bd\u30d5\u30c8", "\u8edf\u9ad4"),
                new KeyValuePair<string, string>("\u30c7\u30fc\u30bf", "\u8cc7\u6599"),
                new KeyValuePair<string, string>("\u30d5\u30a1\u30a4\u30eb", "\u6a94\u6848"),
                new KeyValuePair<string, string>("\u30ab\u30a6\u30f3\u30bf\u30fc", "\u6ab3\u9762"),
                new KeyValuePair<string, string>("\u30d1\u30cd\u30eb", "\u9762\u677f"),
                new KeyValuePair<string, string>("\u30b3\u30fc\u30c9", "\u4ee3\u78bc"),
                new KeyValuePair<string, string>("\u30b0\u30eb\u30fc\u30d7", "\u7fa4\u7d44"),
                new KeyValuePair<string, string>("\u4f5c\u6210", "\u5efa\u7acb"),
                new KeyValuePair<string, string>("\u63a5\u7d9a\u53e3", "\u63a5\u7e8c\u53e3"),
                new KeyValuePair<string, string>("\u4ed5\u69d8", "\u898f\u683c"),
                new KeyValuePair<string, string>("\u4ed8\u5c5e", "\u9644\u5c6c"),
                new KeyValuePair<string, string>("\u5358\u4f4d", "\u55ae\u4f4d"),
                new KeyValuePair<string, string>("\u5f62\u5f0f", "\u683c\u5f0f"),
                new KeyValuePair<string, string>("\u6750\u8cea", "\u6750\u8cea"),
                new KeyValuePair<string, string>("\u7cfb\u7d71", "\u7cfb\u7d71"),
                new KeyValuePair<string, string>("\u4f7f\u7528\u6c34", "\u4f7f\u7528\u6c34"),
                new KeyValuePair<string, string>("\u547c\u79f0", "\u7a31\u547c"),
                new KeyValuePair<string, string>("\u5206\u985e", "\u5206\u985e"),
                new KeyValuePair<string, string>("\u96fb\u529b", "\u96fb\u529b"),
                new KeyValuePair<string, string>("\u6c34\u91cf", "\u6c34\u91cf"),
                new KeyValuePair<string, string>("\u8a18\u53f7", "\u8a18\u865f"),
                new KeyValuePair<string, string>("\u5c5e", "\u5c6c"),
                new KeyValuePair<string, string>("\u5e74\u6570", "\u5e74\u6578"),
                new KeyValuePair<string, string>("\u6570", "\u6578")
            };

        public FamilyParameterRenameService(Document document)
        {
            _document = document;
        }

        public bool IsFamilyDocument => _document.IsFamilyDocument;

        public IReadOnlyList<FamilyRenameOption> GetProjectFamilies()
        {
            if (_document.IsFamilyDocument)
            {
                return Array.Empty<FamilyRenameOption>();
            }

            return new FilteredElementCollector(_document)
                .OfClass(typeof(Autodesk.Revit.DB.Family))
                .Cast<Autodesk.Revit.DB.Family>()
                .Where(x => x.IsEditable)
                .Select(x => new FamilyRenameOption(x))
                .OrderBy(x => x.CategoryName)
                .ThenBy(x => x.Name)
                .ToList();
        }

        public IReadOnlyList<ParameterRenameRow> BuildRowsForCurrentFamily()
        {
            if (!_document.IsFamilyDocument)
            {
                throw new InvalidOperationException("Active document is not a family document.");
            }

            return BuildRows(_document.FamilyManager);
        }

        public IReadOnlyList<ParameterRenameRow> BuildRowsForProjectFamily(Autodesk.Revit.DB.Family family)
        {
            if (family == null)
            {
                return Array.Empty<ParameterRenameRow>();
            }

            var familyDoc = _document.EditFamily(family);
            try
            {
                return BuildRows(familyDoc.FamilyManager);
            }
            finally
            {
                familyDoc.Close(false);
            }
        }

        public void ValidateRows(IReadOnlyList<ParameterRenameRow> rows)
        {
            var existingNames = new HashSet<string>(rows.Select(x => x.OldName), StringComparer.OrdinalIgnoreCase);
            var pendingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (!row.IsSelected)
                {
                    row.Status = "Ignored";
                    row.Message = "";
                    continue;
                }

                var newName = (row.NewName ?? "").Trim();
                var messages = new List<string>();

                if (string.IsNullOrWhiteSpace(newName))
                {
                    messages.Add("New name is required.");
                }
                else if (string.Equals(row.OldName, newName, StringComparison.Ordinal))
                {
                    row.Status = "Unchanged";
                    row.Message = "";
                    continue;
                }
                else
                {
                    if (existingNames.Contains(newName))
                    {
                        messages.Add("Parameter name already exists.");
                    }

                    if (!pendingNames.Add(newName))
                    {
                        messages.Add("Duplicate new name.");
                    }
                }

                row.Status = messages.Count == 0 ? "Ready" : "Invalid";
                row.Message = string.Join(" ", messages);
            }
        }

        public int RenameCurrentFamilyParameters(IReadOnlyList<ParameterRenameRow> rows)
        {
            if (!_document.IsFamilyDocument)
            {
                throw new InvalidOperationException("Active document is not a family document.");
            }

            return RenameInFamilyDocument(_document, rows);
        }

        public int RenameProjectFamilyParameters(Autodesk.Revit.DB.Family family, IReadOnlyList<ParameterRenameRow> rows)
        {
            if (family == null)
            {
                return 0;
            }

            var familyDoc = _document.EditFamily(family);
            try
            {
                var renamed = RenameInFamilyDocument(familyDoc, rows);
                if (renamed > 0)
                {
                    familyDoc.LoadFamily(_document, new OverwriteFamilyLoadOptions());
                }

                return renamed;
            }
            finally
            {
                familyDoc.Close(false);
            }
        }

        private static IReadOnlyList<ParameterRenameRow> BuildRows(FamilyManager familyManager)
        {
            return familyManager.Parameters
                .Cast<FamilyParameter>()
                .OrderBy(x => x.Definition.Name)
                .Select(x => new ParameterRenameRow
                {
                    Parameter = x,
                    OldName = x.Definition.Name,
                    NewName = SuggestName(x.Definition.Name)
                })
                .ToList();
        }

        private static int RenameInFamilyDocument(Document familyDoc, IReadOnlyList<ParameterRenameRow> rows)
        {
            var readyRows = rows.Where(x => x.Status == "Ready").ToList();
            if (readyRows.Count == 0)
            {
                return 0;
            }

            var familyManager = familyDoc.FamilyManager;
            var renamed = 0;
            using (var transaction = new Transaction(familyDoc, "Rename family parameters"))
            {
                transaction.Start();

                foreach (var row in readyRows)
                {
                    try
                    {
                        var parameter = FindParameter(familyManager, row.OldName);
                        if (parameter == null)
                        {
                            row.Status = "Failed";
                            row.Message = "Parameter not found.";
                            continue;
                        }

                        familyManager.RenameParameter(parameter, row.NewName.Trim());
                        row.Status = "Renamed";
                        row.Message = "";
                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        row.Status = "Failed";
                        row.Message = ex.Message;
                    }
                }

                transaction.Commit();
            }

            return renamed;
        }

        private static FamilyParameter FindParameter(FamilyManager familyManager, string name)
        {
            return familyManager.Parameters
                .Cast<FamilyParameter>()
                .FirstOrDefault(x => string.Equals(x.Definition.Name, name, StringComparison.Ordinal));
        }

        public static string SuggestName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }

            var result = name.Trim();
            foreach (var translation in NameTranslations)
            {
                result = result.Replace(translation.Key, translation.Value);
            }

            result = Regex.Replace(result, "\\(([^\\)]*)\\)", match =>
            {
                var value = match.Groups[1].Value.Trim();
                if (Regex.IsMatch(value, "^[A-Za-z]+/[A-Za-z]+$"))
                {
                    return "(" + value + ")";
                }

                return "(" + value.ToLowerInvariant() + ")";
            });
            result = Regex.Replace(result, "\\s+", " ");
            return result;
        }

        private class OverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Autodesk.Revit.DB.Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
