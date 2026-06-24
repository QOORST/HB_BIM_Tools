using Autodesk.Revit.DB;
using System.Reflection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal static class ElementClassifier
{
    public static CategorySpec TryClassify(Element element)
    {
        if (element?.Category == null)
        {
            return null;
        }

        var bic = (BuiltInCategory)GetElementIdValue(element.Category.Id);
        if (!CategoryCatalog.IsSupportedBuiltInCategory(bic))
        {
            return null;
        }

        if (bic == BuiltInCategory.OST_Floors)
        {
            return IsStructuralFloor(element)
                ? CategoryCatalog.ByKey("StructuralFloor")
                : CategoryCatalog.ByKey("Floor");
        }

        if (bic == BuiltInCategory.OST_Walls)
        {
            return IsStructuralWall(element)
                ? CategoryCatalog.ByKey("StructuralWall")
                : CategoryCatalog.ByKey("Wall");
        }

        return bic switch
        {
            BuiltInCategory.OST_Ceilings => CategoryCatalog.ByKey("Ceiling"),
            BuiltInCategory.OST_Columns => CategoryCatalog.ByKey("Column"),
            BuiltInCategory.OST_GenericModel => CategoryCatalog.ByKey("GenericModel"),
            BuiltInCategory.OST_Roofs => CategoryCatalog.ByKey("Roof"),
            BuiltInCategory.OST_StructuralColumns => CategoryCatalog.ByKey("StructuralColumn"),
            BuiltInCategory.OST_StructuralFoundation => CategoryCatalog.ByKey("StructuralFoundation"),
            BuiltInCategory.OST_StructuralFraming => CategoryCatalog.ByKey("StructuralFraming"),
            _ => null
        };
    }

    private static bool IsStructuralWall(Element element)
    {
        var parameter = element.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
        return parameter != null && parameter.AsInteger() == 1;
    }

    private static bool IsStructuralFloor(Element element)
    {
        var parameter = element.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
        return parameter != null && parameter.AsInteger() == 1;
    }

    private static int GetElementIdValue(ElementId id)
    {
        var valueProperty = typeof(ElementId).GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueProperty?.PropertyType == typeof(long))
        {
            var longValue = (long)valueProperty.GetValue(id);
            return unchecked((int)longValue);
        }

        var integerValueProperty = typeof(ElementId).GetProperty("IntegerValue", BindingFlags.Instance | BindingFlags.Public);
        if (integerValueProperty?.PropertyType == typeof(int))
        {
            return (int)integerValueProperty.GetValue(id);
        }

        return id.GetHashCode();
    }
    }
}
