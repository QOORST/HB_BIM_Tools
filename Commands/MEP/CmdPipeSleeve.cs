using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class CmdPipeSleeve : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 套管功能
                var licenseManager = LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("MEP.PipeSleeve"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權不支援管線套管功能。\n\n" +
                        "請升級至 Standard 或 Professional 版本以使用此功能。\n\n" +
                        "點擊「授權管理」按鈕以查看或更新您的授權。");
                    return Result.Cancelled;
                }

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;

                // 選擇管線
                IList<Reference> selectedRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "請選擇要放置套管的管線");

                if (selectedRefs == null || selectedRefs.Count == 0)
                {
                    TaskDialog.Show("提示", "未選擇任何管線。");
                    return Result.Cancelled;
                }

                // 收集選取的管線
                List<Element> pipes = new List<Element>();
                foreach (Reference refElem in selectedRefs)
                {
                    Element elem = doc.GetElement(refElem);
                    pipes.Add(elem);
                }

                // 開啟 UI 視窗進行設定
                var sleeveWindow = new PipeSleeveWindow(doc, pipes);
                bool? dialogResult = sleeveWindow.ShowDialog();

                if (dialogResult == true)
                {
                    TaskDialog.Show("完成",
                        $"管線套管放置完成！\n\n" +
                        $"已處理 {pipes.Count} 條管線。");
                    return Result.Succeeded;
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
                message = ex.Message;
                TaskDialog.Show("錯誤", $"執行失敗:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 管線選擇過濾器
    /// </summary>
    public class PipeSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            // 允許選擇管線（Pipe）和風管（Duct）
            return elem is Pipe || elem is Duct;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}

