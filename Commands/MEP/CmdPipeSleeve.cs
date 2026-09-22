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
                LoadMissingPresetFamilies(doc);
                using (var settings = new PipeSleeveSettingsForm(pipes.Count))
                {
                    settings.ConfigureLevels(SleeveLevelPolicy.Choices(doc), SleeveLevelPolicy.Read(doc));
                    settings.ValidateLevelRule = key => SleeveLevelPolicy.Summary(doc, pipes, key);
                    var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                        .Where(SleeveFamilyCatalog.IsSleeveSymbol)
                        .OrderBy(s=>s.FamilyName).ThenBy(s=>s.Name).ToList();
                    settings.ConfigureSizes(PipeSleeveNominalRules.ConfigurableSizes.Select(x=>new PipeSleeveSettingsForm.SizeRow { DN=x }).ToList(), symbols.Select(s=>new PipeSleeveSettingsForm.SymbolChoice {
                        Id=s.Id.GetIdValue().ToString(), Name=s.FamilyName+": "+s.Name, Family=s.FamilyName, Type=s.Name }).ToList());
                    var owner = new System.Windows.Forms.NativeWindow();
                    owner.AssignHandle(commandData.Application.MainWindowHandle);
                    try
                    {
                        if (settings.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                            return Result.Succeeded; // Preserve committed preset loads when closing settings.
                    }
                    finally { owner.ReleaseHandle(); }
                    options = new PipeSleeveOptions
                    {
                        CreationLevelUniqueId = settings.CreationLevelUniqueId,
                        PreserveNominalTypeDimensions = true,
                        UseDiameterSymbolMap = settings.UseDiameterMap,
                        SleeveSymbolByDiameterMm = settings.SizeRows.ToDictionary(r=>r.DN,r=>symbols.FirstOrDefault(s=>s.Id.GetIdValue().ToString()==r.SymbolId)?.Id ?? ElementId.InvalidElementId),
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

                {
                    if (!SleeveLevelPolicy.Confirm(doc, pipes, options.CreationLevelUniqueId)) return Result.Succeeded;
                    PipeSleeveResult result = PipeSleeveService.ExecuteCreate(
                        doc,
                        pipes,
                        options);
                    TaskDialog.Show("管線套管", result.ToTaskDialogText());
                    return Result.Succeeded;
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
                sleeveWindow.ShowDialog();
                if (sleeveWindow.HasUnrecoveredFailure)
                    return Result.Failed;
                return Result.Succeeded;
#endif
            }
            catch (SleeveOperationRolledBackException ex)
            {
                // Revit rolls back ALL command transactions for Failed/Cancelled, including preset loads.
                // The operation already rolled back; Succeeded here means handled, not sleeves created.
                TaskDialog.Show("套管建立／更新未完成", ex.Message);
                return Result.Succeeded;
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

        private static void LoadMissingPresetFamilies(Document doc)
        {
            var errors = SleeveFamilyCatalog.LoadMissing(doc);
            if (errors.Count > 0) TaskDialog.Show("預設族群載入", string.Join("\n", errors) + "\n\n仍可選用模型內既有的族群。");
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













