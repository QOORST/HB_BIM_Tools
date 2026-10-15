namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    // Snapshot only: no Revit objects cross into the editor.
    internal sealed class TypeJoinChoice
    {
        public string CategoryKey { get; }
        public string FamilyName { get; }
        public string TypeName { get; }
        public string Label => FamilyName + " / " + TypeName;

        public TypeJoinChoice(string categoryKey, string familyName, string typeName)
        {
            CategoryKey = categoryKey;
            FamilyName = familyName;
            TypeName = typeName;
        }

        public override string ToString() => Label;
    }
}
