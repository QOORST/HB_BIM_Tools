using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YDBIM.AutoDimension.App
{
[Transaction(TransactionMode.Manual)]
public sealed class AutoDimensionLinkedCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        => AutoDimensionQuickRunner.Run(data, ref message, linkedSource: true);
}
}
