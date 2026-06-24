using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.MEP.MepCheck
{
    internal enum MepCheckScope
    {
        CurrentView,
        EntireModel
    }

    internal enum MepCheckSeverity
    {
        Pass,
        Info,
        Warning,
        Error
    }

    internal enum MepDrainFlowRule
    {
        Elevation,
        OutletElement,
        DownstreamDirection,
        SystemFlow
    }

    internal enum MepDownstreamDirection
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY
    }

    internal sealed class MepCheckOptions
    {
        public MepCheckScope Scope { get; set; } = MepCheckScope.CurrentView;
        public bool CheckPipeDrainDirection { get; set; } = true;
        public bool CheckEquipmentLevelDistribution { get; set; } = true;
        public bool CheckConnectorCompleteness { get; set; } = true;
        public bool CheckSystemData { get; set; } = true;
        public bool CheckDuplicateEquipmentMarks { get; set; } = true;
        public double MinimumDrainSlopePercent { get; set; } = 0.5;
        public double EquipmentLevelToleranceMm { get; set; } = 300.0;
        public bool DrainageSystemOnly { get; set; } = true;
        public bool IgnoreFlatPipes { get; set; } = true;
        public MepDrainFlowRule DrainFlowRule { get; set; } = MepDrainFlowRule.Elevation;
        public MepDownstreamDirection DownstreamDirection { get; set; } = MepDownstreamDirection.PositiveX;
        public ElementId OutletElementId { get; set; } = ElementId.InvalidElementId;
    }

    internal sealed class MepCheckIssue
    {
        public string CheckKey { get; set; } = string.Empty;
        public MepCheckSeverity Severity { get; set; }
        public ElementId ElementId { get; set; } = ElementId.InvalidElementId;
        public string Category { get; set; } = string.Empty;
        public string ElementName { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string SystemName { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string ExpectedValue { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    internal sealed class MepCheckResult
    {
        public int PipeCount { get; set; }
        public int IgnoredPipeCount { get; set; }
        public int EquipmentCount { get; set; }
        public int ConnectorElementCount { get; set; }
        public int SystemDataElementCount { get; set; }
        public System.Collections.Generic.List<MepCheckIssue> Issues { get; } = new System.Collections.Generic.List<MepCheckIssue>();
    }
}
