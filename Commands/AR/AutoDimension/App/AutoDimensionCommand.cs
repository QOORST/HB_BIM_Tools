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
