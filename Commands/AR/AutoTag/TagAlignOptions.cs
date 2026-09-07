namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal enum TagAlignScope
    {
        Selection,
        ActiveView
    }

    internal enum TagAlignMode
    {
        Horizontal,
        Vertical,
        DistributeHorizontal,
        DistributeVertical
    }

    internal enum TagAlignBase
    {
        First,
        Average
    }

    internal sealed class TagAlignOptions
    {
        public TagAlignScope Scope { get; set; } = TagAlignScope.Selection;

        public TagAlignMode Mode { get; set; } = TagAlignMode.Horizontal;

        public TagAlignBase Base { get; set; } = TagAlignBase.Average;

        public double SpacingMillimeters { get; set; } = 300.0;

        public bool AvoidOverlap { get; set; } = true;
    }
}
