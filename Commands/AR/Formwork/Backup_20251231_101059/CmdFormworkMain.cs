using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 模板生成主命令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdFormworkMain : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 模板生成功能
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Formwork.Generate"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援模板生成功能。\n\n" +
                        "此功能僅適用於試用版、標準版和專業版授權。\n\n" +
                        "點擊「授權管理」按鈕以查看或升級授權。");
                    return Result.Cancelled;
                }

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc?.Document;

                if (doc == null)
                {
                    message = "無法取得有效的 Revit 文件";
                    return Result.Failed;
                }

                // 讓使用者選擇結構元素
                var selection = uidoc.Selection.PickObjects(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new StructuralElementFilter(),
                    "請選擇要生成模板的結構元素（柱、梁、樓板、牆等）");

                if (selection == null || selection.Count == 0)
                {
                    return Result.Cancelled;
                }

                // 詢問模板厚度
                double thickness = 18.0; // 預設 18mm
                var thicknessDialog = new TaskDialog("模板厚度設定");
                thicknessDialog.MainInstruction = "請設定模板厚度";
                thicknessDialog.MainContent = $"預設厚度：{thickness} mm\n\n" +
                    "常用厚度：\n" +
                    "• 12mm - 輕型模板\n" +
                    "• 18mm - 標準模板\n" +
                    "• 21mm - 重型模板";
                thicknessDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "12mm - 輕型模板");
                thicknessDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "18mm - 標準模板（推薦）");
                thicknessDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "21mm - 重型模板");
                thicknessDialog.CommonButtons = TaskDialogCommonButtons.Cancel;

                var thicknessResult = thicknessDialog.Show();
                switch (thicknessResult)
                {
                    case TaskDialogResult.CommandLink1:
                        thickness = 12.0;
                        break;
                    case TaskDialogResult.CommandLink2:
                        thickness = 18.0;
                        break;
                    case TaskDialogResult.CommandLink3:
                        thickness = 21.0;
                        break;
                    default:
                        return Result.Cancelled;
                }

                // 執行模板生成
                int successCount = 0;
                int failCount = 0;
                var createdFormworkIds = new List<ElementId>();

                using (Transaction trans = new Transaction(doc, "生成模板"))
                {
                    trans.Start();

                    foreach (var reference in selection)
                    {
                        try
                        {
                            Element element = doc.GetElement(reference);
                            if (element == null) continue;

                            // 生成模板
                            var formworkIds = CreateFormworkForElement(doc, element, thickness);
                            if (formworkIds != null && formworkIds.Any())
                            {
                                createdFormworkIds.AddRange(formworkIds);
                                successCount++;
                            }
                            else
                            {
                                failCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            System.Diagnostics.Debug.WriteLine($"生成模板失敗: {ex.Message}");
                        }
                    }

                    trans.Commit();
                }

                // 顯示結果
                TaskDialog.Show("模板生成完成",
                    $"模板生成作業完成！\n\n" +
                    $"成功: {successCount} 個元素\n" +
                    $"失敗: {failCount} 個元素\n" +
                    $"生成模板數量: {createdFormworkIds.Count}\n" +
                    $"模板厚度: {thickness} mm");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"執行失敗: {ex.Message}";
                TaskDialog.Show("錯誤", $"模板生成時發生錯誤:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 為單個元素創建模板
        /// </summary>
        private List<ElementId> CreateFormworkForElement(Document doc, Element element, double thicknessMm)
        {
            // TODO: 實作模板生成邏輯
            // 這裡先返回空列表，後續可以實作具體邏輯
            return new List<ElementId>();
        }
    }

    /// <summary>
    /// 結構元素篩選器
    /// </summary>
    public class StructuralElementFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null || elem.Category == null) return false;

#if REVIT2024 || REVIT2025 || REVIT2026
            var catId = elem.Category.Id.Value;
#else
            var catId = elem.Category.Id.IntegerValue;
#endif
            return catId == (int)BuiltInCategory.OST_StructuralColumns ||
                   catId == (int)BuiltInCategory.OST_StructuralFraming ||
                   catId == (int)BuiltInCategory.OST_Floors ||
                   catId == (int)BuiltInCategory.OST_Walls;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}

