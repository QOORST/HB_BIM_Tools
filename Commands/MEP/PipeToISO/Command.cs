using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;

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
                    TaskDialog.Show("成功", "ISO 圖與 PCF 檔案已成功生成！");
                    return Result.Succeeded;
                }

                return Result.Cancelled;
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
                    return category.Id.Value == (int)BuiltInCategory.OST_PipeFitting;
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
                        return ParseSizeString(sizeStr);
                    }
                }
            }
            
            return 0;
        }

        /// <summary>
        /// 解析尺寸字串為 mm 值
        /// </summary>
        private static double ParseSizeString(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr))
                return 0;

            // 移除常見前綴
            sizeStr = sizeStr.Replace("DN", "").Replace("dn", "")
                           .Replace("mm", "").Replace("MM", "")
                           .Replace("\"", "").Trim();

            if (double.TryParse(sizeStr, out double value))
            {
                // 假設小於 50 的數字是英吋，需轉換為 mm
                if (value < 50)
                {
                    return value * 25.4; // 英吋轉 mm
                }
                return value;
            }

            return 0;
        }
    }
}
