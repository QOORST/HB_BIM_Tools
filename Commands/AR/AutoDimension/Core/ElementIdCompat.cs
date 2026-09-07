using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;

namespace YDBIM.AutoDimension.Core
{
internal static class ElementIdCompat
{
    public static int ToInt32(ElementId id)
    {
        return checked((int)id.GetIdValue());
    }

    public static ElementId FromInt32(int id)
    {
        return RevitApiCompatibility.CreateElementId(id);
    }
}
}
