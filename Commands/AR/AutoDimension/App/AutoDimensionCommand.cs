using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YDBIM.AutoDimension.Core;

namespace YDBIM.AutoDimension.App
{

[Transaction(TransactionMode.Manual)]
public sealed class AutoDimensionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => AutoDimensionQuickRunner.Run(commandData, ref message, DimensionMode.ColumnSetout);
}

[Transaction(TransactionMode.Manual)]
public sealed class AutoDimensionGridCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        => AutoDimensionQuickRunner.Run(data, ref message, DimensionMode.BeamGrid);
}

[Transaction(TransactionMode.Manual)]
public sealed class AutoDimensionBeamCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        => AutoDimensionQuickRunner.Run(data, ref message, DimensionMode.BeamWidth);
}

[Transaction(TransactionMode.Manual)]
public sealed class AutoDimensionSelectionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        => AutoDimensionQuickRunner.Run(data, ref message);
}

[Transaction(TransactionMode.Manual)]
public sealed class AutoDimensionSettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        return AutoDimensionModelessController.Show(
            commandData,
            ref message,
            DimensionMode.BeamGrid,
            "HB_BIM 自動標註",
            lockMode: false);
    }
}
}
