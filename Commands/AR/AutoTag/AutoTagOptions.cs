using Autodesk.Revit.DB;
using System;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal enum AutoTagTemplate
    {
        Structure,
        Mep,
        Architecture
    }

    internal enum AutoTagScope
    {
        ActiveView,
        Selection,
        LinkedView
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
            bool mepDefault,
            bool architectureDefault = false)
        {
            Name = name;
            ElementCategory = elementCategory;
            TagCategory = tagCategory;
            StructureDefault = structureDefault;
            MepDefault = mepDefault;
            ArchitectureDefault = architectureDefault;
        }

        public string Name { get; }

        public BuiltInCategory ElementCategory { get; }

        public BuiltInCategory TagCategory { get; }

        public bool StructureDefault { get; }

        public bool MepDefault { get; }

        public bool ArchitectureDefault { get; }
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

    internal enum AutoTagTextDirection
    {
        Horizontal,
        Vertical,
        FollowElement
    }

    internal sealed class AutoTagOptions
    {
        public AutoTagTemplate Template { get; set; } = AutoTagTemplate.Mep;

        public AutoTagScope Scope { get; set; } = AutoTagScope.ActiveView;

        public string LinkInstanceUniqueId { get; set; } = string.Empty;

        public AutoTagPlacement Placement { get; set; } = AutoTagPlacement.Above;

        public bool AddLeader { get; set; } = true;

        public AutoTagTextDirection TextDirection { get; set; } = AutoTagTextDirection.Horizontal;
        public bool PipeBank { get; set; }
        public double BankGapPaperMm { get; set; } = 2.0;
        public double BankLeaderPaperMm { get; set; } = 5.0;

        public bool SkipExistingTags { get; set; } = true;

        public bool AvoidTagOverlap { get; set; } = true;

        // Defaults preserve previously saved model-distance and direction behavior.
        public bool VerticalOnly { get; set; }
        public bool AllDirections { get; set; }
        public bool UsePaperMillimeters { get; set; }

        public double GetModelOffsetMillimeters(int viewScale)
        {
            return OffsetMillimeters * (UsePaperMillimeters ? Math.Max(1, viewScale) : 1);
        }

        public double MaxMovePaperMillimeters { get; set; } = 10.0;
        public bool AllowCrossSide { get; set; }

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
