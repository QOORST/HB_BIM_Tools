using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO
{
    /// <summary>
    /// PipeToISO 主命令 - 管線轉 ISO 圖
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PipeToISOCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 開啟主視窗
                MainWindow mainWindow = new MainWindow(doc, uidoc);
                bool? dialogResult = mainWindow.ShowDialog();

                if (dialogResult == true)
                {
                    return Result.Succeeded;
                }

                // 已產生成果時不可回傳 Cancelled，避免 Revit 回復已提交的視圖。
                return mainWindow.HasGeneratedOutput ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", $"執行失敗：\n{ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 取得所有管線系統
        /// </summary>
        public static List<PipingSystem> GetAllPipingSystems(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            return collector
                .OfClass(typeof(PipingSystem))
                .Cast<PipingSystem>()
                .OrderBy(ps => ps.Name)
                .ToList();
        }

        /// <summary>
        /// 取得管線系統中的所有管線元件
        /// </summary>
        public static List<Element> GetPipeSystemElements(PipingSystem pipingSystem)
        {
            List<Element> elements = new List<Element>();
            
            if (pipingSystem == null)
                return elements;

            Document doc = pipingSystem.Document;
            ElementSet systemElements = pipingSystem.PipingNetwork;

            foreach (Element elem in systemElements)
            {
                if (elem is Pipe || elem is FamilyInstance)
                {
                    elements.Add(elem);
                }
            }

            return elements;
        }

        /// <summary>
        /// 檢查元件是否為管配件
        /// </summary>
        public static bool IsPipeFitting(Element element)
        {
            if (element is FamilyInstance familyInstance)
            {
                Category category = familyInstance.Category;
                if (category != null)
                {
                    return category.Id.GetIdValue() == (int)BuiltInCategory.OST_PipeFitting;
                }
            }
            return false;
        }

        /// <summary>
        /// 取得管線直徑（以 mm 為單位）
        /// </summary>
        public static double GetPipeDiameter(Element element, Document doc)
        {
            if (element is Pipe pipe)
            {
                double diameterFeet = pipe.Diameter;
                return UnitUtils.ConvertFromInternalUnits(diameterFeet, UnitTypeId.Millimeters);
            }
            else if (element is FamilyInstance fitting)
            {
                var connectors = fitting.MEPModel?.ConnectorManager?.Connectors;
                if (connectors != null)
                {
                    var diameters = connectors.Cast<Connector>()
                        .Where(c => c.Domain == Domain.DomainPiping && c.Shape == ConnectorProfileType.Round)
                        .Select(c => UnitUtils.ConvertFromInternalUnits(c.Radius * 2, UnitTypeId.Millimeters))
                        .Distinct().ToList();
                    // 多管徑配件不能以單一管徑冒充完整尺寸。
                    if (diameters.Count > 1) return 0;
                    if (diameters.Count == 1) return diameters[0];
                }
                // 嘗試從管配件取得尺寸參數
                Parameter sizeParam = fitting.LookupParameter("尺寸") ?? 
                                     fitting.LookupParameter("Size") ??
                                     fitting.LookupParameter("公稱直徑");
                
                if (sizeParam != null && sizeParam.HasValue)
                {
                    if (sizeParam.StorageType == StorageType.Double)
                    {
                        return UnitUtils.ConvertFromInternalUnits(sizeParam.AsDouble(), UnitTypeId.Millimeters);
                    }
                    else if (sizeParam.StorageType == StorageType.String)
                    {
                        string sizeStr = sizeParam.AsString();
                        // 嘗試解析尺寸字串（例如 "DN50", "2\"", "50mm"）
                        return Services.ExportFormatting.ParseDiameterMm(sizeStr);
                    }
                }
            }
            
            return 0;
        }

    }
}
