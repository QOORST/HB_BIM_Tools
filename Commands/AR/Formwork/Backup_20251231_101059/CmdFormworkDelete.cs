using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 刪除模板命令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdFormworkDelete : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 刪除模板功能
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Formwork.Delete"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援刪除模板功能。\n\n" +
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

                // 詢問刪除方式
                var deleteDialog = new TaskDialog("刪除模板");
                deleteDialog.MainInstruction = "請選擇刪除方式";
                deleteDialog.MainContent = "您可以選擇以下方式刪除模板：";
                deleteDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, 
                    "選擇要刪除的模板", 
                    "手動選擇要刪除的模板元素");
                deleteDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, 
                    "刪除所有模板", 
                    "刪除專案中所有的模板元素（需確認）");
                deleteDialog.CommonButtons = TaskDialogCommonButtons.Cancel;

                var deleteResult = deleteDialog.Show();

                if (deleteResult == TaskDialogResult.CommandLink1)
                {
                    // 選擇要刪除的模板
                    return DeleteSelectedFormwork(uidoc, doc, ref message);
                }
                else if (deleteResult == TaskDialogResult.CommandLink2)
                {
                    // 刪除所有模板
                    return DeleteAllFormwork(doc, ref message);
                }
                else
                {
                    return Result.Cancelled;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"執行失敗: {ex.Message}";
                TaskDialog.Show("錯誤", $"刪除模板時發生錯誤:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 刪除選擇的模板
        /// </summary>
        private Result DeleteSelectedFormwork(UIDocument uidoc, Document doc, ref string message)
        {
            try
            {
                // 讓使用者選擇要刪除的模板
                var selection = uidoc.Selection.PickObjects(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new FormworkFilter(),
                    "請選擇要刪除的模板元素");

                if (selection == null || selection.Count == 0)
                {
                    return Result.Cancelled;
                }

                // 確認刪除
                var confirmResult = TaskDialog.Show("確認刪除",
                    $"確定要刪除 {selection.Count} 個模板元素嗎？\n\n此操作無法復原。",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirmResult != TaskDialogResult.Yes)
                {
                    return Result.Cancelled;
                }

                // 執行刪除
                using (Transaction trans = new Transaction(doc, "刪除模板"))
                {
                    trans.Start();

                    foreach (var reference in selection)
                    {
                        doc.Delete(reference.ElementId);
                    }

                    trans.Commit();
                }

                TaskDialog.Show("刪除完成", $"已成功刪除 {selection.Count} 個模板元素。");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"刪除失敗: {ex.Message}";
                return Result.Failed;
            }
        }

        /// <summary>
        /// 刪除所有模板
        /// </summary>
        private Result DeleteAllFormwork(Document doc, ref string message)
        {
            try
            {
                // TODO: 收集所有模板元素
                // 這裡需要定義如何識別模板元素（例如透過類別、參數等）
                var formworkElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => IsFormworkElement(e))
                    .ToList();

                if (formworkElements.Count == 0)
                {
                    TaskDialog.Show("刪除模板", "專案中沒有找到模板元素。");
                    return Result.Cancelled;
                }

                // 確認刪除
                var confirmResult = TaskDialog.Show("確認刪除",
                    $"找到 {formworkElements.Count} 個模板元素。\n\n" +
                    $"確定要刪除所有模板嗎？\n\n此操作無法復原。",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirmResult != TaskDialogResult.Yes)
                {
                    return Result.Cancelled;
                }

                // 執行刪除
                using (Transaction trans = new Transaction(doc, "刪除所有模板"))
                {
                    trans.Start();

                    foreach (var element in formworkElements)
                    {
                        doc.Delete(element.Id);
                    }

                    trans.Commit();
                }

                TaskDialog.Show("刪除完成", $"已成功刪除 {formworkElements.Count} 個模板元素。");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"刪除失敗: {ex.Message}";
                return Result.Failed;
            }
        }

        /// <summary>
        /// 判斷是否為模板元素
        /// </summary>
        private bool IsFormworkElement(Element element)
        {
            // TODO: 實作模板元素識別邏輯
            // 可以透過參數、類別、名稱等方式識別
            return false;
        }
    }

    /// <summary>
    /// 模板元素篩選器
    /// </summary>
    public class FormworkFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            // TODO: 實作模板元素篩選邏輯
            return elem != null && elem.Category != null;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}

