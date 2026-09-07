using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YDBIM.AutoDimension.Core;

#nullable enable

namespace YDBIM.AutoDimension.App
{

internal static class DimensionCommandRunner
{
    public static Result Run(ExternalCommandData commandData, ref string message, DimensionMode mode, string windowTitle)
    {
        return AutoDimensionModelessController.Show(commandData, ref message, mode, windowTitle, lockMode: false);
    }

    internal static string BuildNoChangeMessage(DimensionMode mode, Document doc, View view)
    {
        string detail = BuildDiagnosticMessage(mode, doc, view);
        string message = mode switch
        {
            DimensionMode.ColumnSetout => "沒有建立柱標註。\n請確認目前視圖可見結構柱與通過柱範圍的軸線，且柱族可取得平面參考面。",
            DimensionMode.BeamGrid => "沒有建立網格標註。\n請確認目前視圖至少有同方向 2 條可見直線軸線。",
            DimensionMode.BeamWidth => "沒有建立梁標註。\n請確認目前視圖可見直線結構梁，並可取得梁面、柱面或軸線參考。",
            _ => "沒有建立標註。請確認目前視圖與可見構件。"
        };

        return string.IsNullOrWhiteSpace(detail) ? message : message + "\n\n診斷資訊：\n" + detail;
    }

    private static string BuildDiagnosticMessage(DimensionMode mode, Document doc, View view)
    {
        int gridCount = CountVisibleElements<Grid>(doc, view);
        int linkCount = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .OfClass(typeof(RevitLinkInstance))
            .GetElementCount();

        if (mode == DimensionMode.BeamWidth)
        {
            int beamCount = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfType<FamilyInstance>()
                .Count();
            return $"本機結構構架：{beamCount}，可見軸線：{gridCount}，Revit 連結：{linkCount}。" +
                (beamCount == 0 && linkCount > 0 ? "\n若畫面梁來自連結模型，請先在連結模型執行，或將梁複製/綁定到目前模型後再標註。" : string.Empty);
        }

        if (mode == DimensionMode.ColumnSetout)
        {
            int structuralColumnCount = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .OfType<FamilyInstance>()
                .Count();
            int architecturalColumnCount = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfCategory(BuiltInCategory.OST_Columns)
                .OfType<FamilyInstance>()
                .Count();
            int columnCount = structuralColumnCount + architecturalColumnCount;
            return $"本機結構柱/柱：{columnCount}，可見軸線：{gridCount}，Revit 連結：{linkCount}。" +
                (columnCount == 0 && linkCount > 0 ? "\n若畫面柱來自連結模型，請先在連結模型執行，或將柱複製/綁定到目前模型後再標註。" : string.Empty);
        }

        return $"可見軸線：{gridCount}，Revit 連結：{linkCount}。";
    }

    private static int CountVisibleElements<T>(Document doc, View view) where T : Element
    {
        return new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .OfClass(typeof(T))
            .GetElementCount();
    }
}
}
