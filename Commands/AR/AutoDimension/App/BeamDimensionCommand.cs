using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YDBIM.AutoDimension.Core;

namespace YDBIM.AutoDimension.App
{

[Transaction(TransactionMode.Manual)]
public sealed class BeamDimensionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        return DimensionCommandRunner.Run(commandData, ref message, DimensionMode.BeamWidth, "梁標註");
    }
}
}


