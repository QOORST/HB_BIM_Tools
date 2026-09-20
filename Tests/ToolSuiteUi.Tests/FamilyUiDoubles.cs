using System.Collections.Generic;
using YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Models;
namespace Autodesk.Revit.DB { public class FamilyParameter {} }
namespace YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Models {
    public class FamilyRenameOption { public object Family { get; set; } public string DisplayName { get; set; } }
}
namespace YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Services {
    // UI-only fixtures. These deliberately do not implement Revit model changes.
    public class FamilyParameterRenameService {
        public bool IsFamilyDocument => true;
        public IEnumerable<FamilyRenameOption> GetProjectFamilies() => new FamilyRenameOption[0];
        public IEnumerable<ParameterRenameRow> BuildRowsForCurrentFamily() => new[] {
            new ParameterRenameRow { OldName="舊參數名稱", NewName="新參數名稱", Message="新名稱與其他列重複。" }
        };
        public IEnumerable<ParameterRenameRow> BuildRowsForProjectFamily(object family) => BuildRowsForCurrentFamily();
        public static string SuggestName(string name) => name;
        public void ValidateRows(List<ParameterRenameRow> rows) {}
        public int RenameCurrentFamilyParameters(List<ParameterRenameRow> rows) => throw new System.NotSupportedException("UI fixture only");
        public int RenameProjectFamilyParameters(object family,List<ParameterRenameRow> rows) => throw new System.NotSupportedException("UI fixture only");
    }
}
