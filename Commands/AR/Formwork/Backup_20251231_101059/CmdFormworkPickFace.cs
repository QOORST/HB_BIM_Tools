using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 面選模板命令 - 透過選擇面來生成模板
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdFormworkPickFace : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 面選模板功能
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Formwork.PickFace"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援面選模板功能。\n\n" +
                        "此功能僅適用於標準版和專業版授權。\n\n" +
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

                // 詢問模板厚度
                double thickness = 18.0; // 預設 18mm
                var thicknessDialog = new TaskDialog("模板厚度設定");
                thicknessDialog.MainInstruction = "請設定模板厚度";
                thicknessDialog.MainContent = $"預設厚度：{thickness} mm";
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

                // 讓使用者選擇面
                TaskDialog.Show("面選模板",
                    "請點選要生成模板的面。\n\n" +
                    "您可以選擇結構元素的任何面，\n" +
                    "系統將在該面上生成模板。\n\n" +
                    "按 ESC 取消選擇。");

                var createdFormworkIds = new List<ElementId>();
                int successCount = 0;

                using (Transaction trans = new Transaction(doc, "面選模板"))
                {
                    trans.Start();

                    while (true)
                    {
                        try
                        {
                            // 選擇面
                            var reference = uidoc.Selection.PickObject(
                                Autodesk.Revit.UI.Selection.ObjectType.Face,
                                "請選擇要生成模板的面（按 ESC 結束選擇）");

                            if (reference == null)
                                break;

                            // 取得面和元素
                            Element element = doc.GetElement(reference);
                            GeometryObject geoObj = element.GetGeometryObjectFromReference(reference);
                            
                            if (geoObj is Face face)
                            {
                                // 生成模板
                                var formworkId = CreateFormworkFromFace(doc, element, face, thickness);
                                if (formworkId != null && formworkId != ElementId.InvalidElementId)
                                {
                                    createdFormworkIds.Add(formworkId);
                                    successCount++;
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            // 使用者按 ESC，結束選擇
                            break;
                        }
                    }

                    if (createdFormworkIds.Count > 0)
                    {
                        trans.Commit();
                    }
                    else
                    {
                        trans.RollBack();
                        TaskDialog.Show("面選模板", "未生成任何模板。");
                        return Result.Cancelled;
                    }
                }

                // 顯示結果
                TaskDialog.Show("面選模板完成",
                    $"模板生成作業完成！\n\n" +
                    $"成功生成: {successCount} 個模板\n" +
                    $"模板厚度: {thickness} mm");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"執行失敗: {ex.Message}";
                TaskDialog.Show("錯誤", $"面選模板時發生錯誤:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 從面創建模板
        /// </summary>
        private ElementId CreateFormworkFromFace(Document doc, Element element, Face face, double thicknessMm)
        {
            try
            {
                // TODO: 實作從面生成模板的邏輯
                // 1. 取得面的邊界
                // 2. 創建模板幾何
                // 3. 設定模板參數
                
                // 這裡先返回 InvalidElementId，後續可以實作具體邏輯
                return ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"從面生成模板失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }
    }
}

