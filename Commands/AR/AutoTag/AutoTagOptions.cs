using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal enum AutoTagTemplate
    {
        Structure,
        Mep
    }

    internal enum AutoTagScope
    {
        ActiveView,
        Selection
    }

    internal enum AutoTagPlacement
    {
        Center,
        Above,
        Below,
        Left,
        Right
    }

    internal sealed class AutoTagCategoryRule
    {
        public AutoTagCategoryRule(
            string name,
            BuiltInCategory elementCategory,
            BuiltInCategory tagCategory,
            bool structureDefault,
            bool mepDefault)
        {
            Name = name;
            ElementCategory = elementCategory;
            TagCategory = tagCategory;
            StructureDefault = structureDefault;
            MepDefault = mepDefault;
        }

        public string Name { get; }

        public BuiltInCategory ElementCategory { get; }

        public BuiltInCategory TagCategory { get; }

        public bool StructureDefault { get; }

        public bool MepDefault { get; }
    }

    internal sealed class AutoTagRuleSelection
    {
        public AutoTagRuleSelection(AutoTagCategoryRule rule, ElementId tagTypeId)
        {
            Rule = rule;
            TagTypeId = tagTypeId;
        }

        public AutoTagCategoryRule Rule { get; }

        public ElementId TagTypeId { get; }
    }

    internal sealed class AutoTagOptions
    {
        public AutoTagTemplate Template { get; set; } = AutoTagTemplate.Mep;

        public AutoTagScope Scope { get; set; } = AutoTagScope.ActiveView;

        public AutoTagPlacement Placement { get; set; } = AutoTagPlacement.Above;

        public bool AddLeader { get; set; } = true;

        public bool SkipExistingTags { get; set; } = true;

        public bool AvoidTagOverlap { get; set; } = true;

        public double OffsetMillimeters { get; set; } = 250.0;
    }

    internal sealed class AutoTagTypeOption
    {
        public AutoTagTypeOption(ElementId id, string label)
        {
            Id = id;
            Label = label;
        }

        public ElementId Id { get; }

        public string Label { get; }

        public override string ToString()
        {
            return Label;
        }
    }
}
