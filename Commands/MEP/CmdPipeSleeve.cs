using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Helpers;
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
                if (uidoc == null)
                {
                    TaskDialog.Show("自動套管", "請先開啟模型，再執行自動套管。");
                    return Result.Cancelled;
                }
                Document doc = uidoc.Document;

#if REVIT2025 || REVIT2026
                IList<Reference> selectedRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "請選擇要放置套管的管線、風管、電管或電纜線架");

                if (selectedRefs == null || selectedRefs.Count == 0)
                {
                    TaskDialog.Show("提示", "未選擇任何管線、風管、電管或電纜線架。");
                    return Result.Cancelled;
                }

                List<Element> pipes = selectedRefs
                    .Select(reference => doc.GetElement(reference))
                    .Where(element => element != null)
                    .ToList();

                PipeSleeveOptions options;
                using (var settings = new PipeSleeveSettingsForm(pipes.Count))
                {
                    var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                        .Where(s=>s.Category!=null && (s.Category.Id.GetIdValue()==(long)BuiltInCategory.OST_GenericModel || s.Category.Id.GetIdValue()==(long)BuiltInCategory.OST_PipeAccessory))
                        .Where(s=> (s.FamilyName+" "+s.Name).IndexOf("套管",StringComparison.OrdinalIgnoreCase)>=0 || (s.FamilyName+" "+s.Name).IndexOf("sleeve",StringComparison.OrdinalIgnoreCase)>=0 || (s.FamilyName+" "+s.Name).Contains("開孔"))
                        .OrderBy(s=>s.FamilyName).ThenBy(s=>s.Name).ToList();
                    settings.ConfigureSizes(PipeSleeveNominalRules.Mapping.Keys.OrderBy(x=>x).Select(x=>new PipeSleeveSettingsForm.SizeRow { DN=x }).ToList(), symbols.Select(s=>new PipeSleeveSettingsForm.SymbolChoice {
                        Id=s.Id.GetIdValue().ToString(), Name=s.FamilyName+": "+s.Name, Family=s.FamilyName, Type=s.Name }).ToList());
                    var owner = new System.Windows.Forms.NativeWindow();
                    owner.AssignHandle(commandData.Application.MainWindowHandle);
                    try
                    {
                        if (settings.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                            return Result.Cancelled;
                    }
                    finally { owner.ReleaseHandle(); }
                    options = new PipeSleeveOptions
                    {
                        PreserveNominalTypeDimensions = true,
                        UseDiameterSymbolMap = settings.UseDiameterMap,
                        SleeveSymbolByDiameterMm = settings.SizeRows.Where(r=>!string.IsNullOrEmpty(r.SymbolId)).ToDictionary(r=>r.DN,r=>symbols.First(s=>s.Id.GetIdValue().ToString()==r.SymbolId).Id),
                        DefaultWallSleeveSymbolId = symbols.FirstOrDefault(s=>s.Id.GetIdValue().ToString()==settings.WallSymbolId)?.Id ?? ElementId.InvalidElementId,
                        DefaultFloorSleeveSymbolId = symbols.FirstOrDefault(s=>s.Id.GetIdValue().ToString()==settings.FloorSymbolId)?.Id ?? ElementId.InvalidElementId,
                        ClearanceMm = settings.ClearanceMm,
                        IncludeCurrentModel = settings.IncludeCurrentModel,
                        IncludeLinks = settings.IncludeLinks,
                        ExcludeAdditionElements = settings.ExcludeAdditionElements,
                        AutoNumber = settings.AutoNumber,
                        SkipExisting = settings.SkipExisting,
                        LimitToActiveView = settings.LimitToActiveView,
                        ActiveViewId = doc.ActiveView.Id
                    };
                }

                using (Transaction tx = new Transaction(doc, "自動放置管線套管"))
                {
                    tx.Start();
                    PipeSleeveResult result = PipeSleeveService.CreateSleeves(
                        doc,
                        pipes,
                        options);
                    if (tx.Commit() != TransactionStatus.Committed)
                    {
                        TaskDialog.Show("自動套管", "Revit 未成功提交套管變更，請檢查失敗訊息。");
                        return Result.Failed;
                    }

                    TaskDialog.Show("管線套管", result.ToTaskDialogText());
                    return result.CreatedCount > 0 || result.SkippedExistingCount > 0
                        ? Result.Succeeded
                        : Result.Cancelled;
                }
#else
                // 選擇管線
                IList<Reference> selectedRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "請選擇要放置套管的管線、風管、電管或電纜線架");

                if (selectedRefs == null || selectedRefs.Count == 0)
                {
                    TaskDialog.Show("提示", "未選擇任何管線、風管、電管或電纜線架。");
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
                    return Result.Succeeded;
                }
                else
                {
                    return Result.Cancelled;
                }
#endif
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
            // 允許選擇管線（Pipe）、風管（Duct）、電管（Conduit）和電纜線架（CableTray）
            return elem is Pipe || elem is Duct || IsElementOfCategory(elem, BuiltInCategory.OST_Conduit) || IsElementOfCategory(elem, BuiltInCategory.OST_CableTray);
        }

        private static bool IsElementOfCategory(Element element, BuiltInCategory category)
        {
            return element?.Category != null && element.Category.Id.GetIdValue() == (long)category;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}













