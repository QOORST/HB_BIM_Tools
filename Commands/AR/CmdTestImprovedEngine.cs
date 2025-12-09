using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager;
using YD_RevitTools.LicenseManager.Helpers.AR;

namespace YD_RevitTools.LicenseManager.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    public class CmdTestImprovedEngine : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 授權檢查
                var licenseManager = LicenseManager.Instance; if (!licenseManager.HasFeatureAccess("TestImprovedEngine"))
                {
                    return Result.Cancelled;
                }

                var doc = commandData.Application.ActiveUIDocument.Document;
                var uidoc = commandData.Application.ActiveUIDocument;

                // 讓用戶選擇一個結構元素
                var selection = uidoc.Selection.PickObject(ObjectType.Element,
                    new StructuralElementFilter(), "選擇一個結構元素來測試改進的模板引擎");

                var selectedElement = doc.GetElement(selection.ElementId);
                if (selectedElement == null)
                {
                    message = "未選擇有效的元素";
                    return Result.Failed;
                }

                using (var tx = new Transaction(doc, "測試改進模板引擎"))
                {
                    tx.Start();

                    // 使用改進的引擎生成模板
                    var formworkIds = ImprovedFormworkEngine.CreateFormworkFromElement(doc, selectedElement, 18.0);

                    tx.Commit();

                    // 顯示結果
                    TaskDialog.Show("測試結果",
                        $"選擇元素: {selectedElement.Category?.Name} (ID: {selectedElement.Id})\n" +
                        $"生成模板數量: {formworkIds.Count()}\n\n" +
                        $"請檢查 Visual Studio 輸出視窗中的詳細 Debug 資訊");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 結構元素篩選器
    /// </summary>
    public class StructuralElementFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem.Category?.Id.Value == (int)BuiltInCategory.OST_StructuralColumns ||
                   elem.Category?.Id.Value == (int)BuiltInCategory.OST_StructuralFraming ||
                   elem.Category?.Id.Value == (int)BuiltInCategory.OST_Floors ||
                   elem.Category?.Id.Value == (int)BuiltInCategory.OST_Walls;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}