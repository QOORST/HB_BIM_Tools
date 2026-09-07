using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Models
{
    public class FamilyRenameOption
    {
        public FamilyRenameOption(Autodesk.Revit.DB.Family family)
        {
            Family = family;
            Name = family.Name;
            CategoryName = family.FamilyCategory?.Name ?? "";
        }

        public Autodesk.Revit.DB.Family Family { get; }
        public string Name { get; }
        public string CategoryName { get; }
        public string DisplayName => string.IsNullOrWhiteSpace(CategoryName) ? Name : CategoryName + " / " + Name;
    }
}
