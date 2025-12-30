using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Commands.AutoJoin;
#if !REVIT2025 && !REVIT2026
using YD_RevitTools.LicenseManager.UI.AutoJoin;
#endif

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    /// <summary>
    /// 簡化版自動接合命令
    /// 基於物件優先順序：柱 > 梁 > 版 > 牆
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoJoinSimple : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet elems)
        {
#if REVIT2025 || REVIT2026
            TaskDialog.Show("功能不可用",
                "簡化版自動接合功能在 Revit 2025/2026 中暫不可用。\n\n" +
                "請使用 Revit 2024 或更早版本。");
            return Result.Cancelled;
#else
            // 檢查授權
            var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("AutoJoin"))
            {
                TaskDialog.Show("授權限制",
                    "您的授權版本不支援自動接合功能。\n\n" +
                    "請升級至試用版、標準版或專業版以使用此功能。");
                return Result.Cancelled;
            }

            var uidoc = data.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                // 顯示簡化介面
                var window = new AutoJoinSimpleWindow();
                if (window.ShowDialog() != true)
                    return Result.Cancelled;

                // 取得使用者選擇
                var mode = window.SelectedElementType;
                var scope = window.SelectedScope;
                var fixWrongOrder = window.FixWrongOrder;
                var isDryRun = window.IsDryRun;

                // 收集要處理的元件
                var elementsToProcess = CollectElements(doc, uidoc, mode, scope);
                if (elementsToProcess.Count == 0)
                {
                    TaskDialog.Show("提示", "找不到符合條件的元件");
                    return Result.Cancelled;
                }

                // 建立處理器
                var processor = new PriorityBasedJoinProcessor(doc, mode, fixWrongOrder);

                // 處理元件
                var results = new List<JoinResult>();
                
                if (!isDryRun)
                {
                    using (var trans = new Transaction(doc, "自動結構接合"))
                    {
                        trans.Start();

                        foreach (var element in elementsToProcess)
                        {
                            var result = processor.ProcessElement(element);
                            results.Add(result);
                        }

                        trans.Commit();
                    }
                }
                else
                {
                    // 預覽模式（不修改模型）
                    foreach (var element in elementsToProcess)
                    {
                        var result = processor.ProcessElement(element);
                        results.Add(result);
                    }
                }

                // 顯示結果
                ShowResults(results, isDryRun);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                msg = ex.Message;
                TaskDialog.Show("錯誤", $"執行失敗：{ex.Message}");
                return Result.Failed;
            }
#endif
        }

        /// <summary>
        /// 收集要處理的元件
        /// </summary>
        private List<Element> CollectElements(Document doc, UIDocument uidoc, ElementTypeMode mode, ProcessingScope scope)
        {
            // 智慧模式：收集所有結構元件
            if (mode == ElementTypeMode.All)
            {
                var allElements = new List<Element>();

                // 根據範圍建立不同的 collector
                switch (scope)
                {
                    case ProcessingScope.CurrentView:
                        // 目前視圖：收集視圖中的所有結構元件
                        allElements.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .OfCategory(BuiltInCategory.OST_StructuralColumns)
                            .WhereElementIsNotElementType()
                            .ToElements());

                        allElements.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .ToElements());

                        allElements.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .OfCategory(BuiltInCategory.OST_Floors)
                            .WhereElementIsNotElementType()
                            .ToElements());

                        allElements.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .OfCategory(BuiltInCategory.OST_Walls)
                            .WhereElementIsNotElementType()
                            .ToElements());
                        break;

                    case ProcessingScope.Selection:
                        // 目前選取：只處理選取的結構元件
                        var selIds = uidoc.Selection.GetElementIds();
                        if (selIds.Count == 0) return new List<Element>();

                        var selectedElements = new FilteredElementCollector(doc, selIds.ToList())
                            .WhereElementIsNotElementType()
                            .ToElements();

                        // 過濾出結構元件
                        foreach (var elem in selectedElements)
                        {
#if REVIT2022 || REVIT2023
                            var category = elem.Category?.Id.IntegerValue;
#else
                            var category = elem.Category?.Id.Value;
#endif
                            if (category == (int)BuiltInCategory.OST_StructuralColumns ||
                                category == (int)BuiltInCategory.OST_StructuralFraming ||
                                category == (int)BuiltInCategory.OST_Floors ||
                                category == (int)BuiltInCategory.OST_Walls)
                            {
                                allElements.Add(elem);
                            }
                        }
                        break;

                    case ProcessingScope.AllElements:
                    default:
                        // 整個專案：收集所有結構元件
                        allElements.AddRange(new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_StructuralColumns)
                            .WhereElementIsNotElementType()
                            .ToElements());

                        allElements.AddRange(new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .ToElements());

                        allElements.AddRange(new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Floors)
                            .WhereElementIsNotElementType()
                            .ToElements());

                        allElements.AddRange(new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Walls)
                            .WhereElementIsNotElementType()
                            .ToElements());
                        break;
                }

                return allElements;
            }
            else
            {
                // 單一模式：只收集指定類別
                var category = GetCategoryForMode(mode);
                FilteredElementCollector collector;

                switch (scope)
                {
                    case ProcessingScope.CurrentView:
                        collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
                        break;

                    case ProcessingScope.Selection:
                        var selIds = uidoc.Selection.GetElementIds();
                        if (selIds.Count == 0) return new List<Element>();
                        collector = new FilteredElementCollector(doc, selIds.ToList());
                        break;

                    case ProcessingScope.AllElements:
                    default:
                        collector = new FilteredElementCollector(doc);
                        break;
                }

                return collector
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();
            }
        }

        /// <summary>
        /// 根據模式取得對應的類別
        /// </summary>
        private BuiltInCategory GetCategoryForMode(ElementTypeMode mode)
        {
            switch (mode)
            {
                case ElementTypeMode.All:
                    // 智慧模式：返回柱（後續會處理所有類型）
                    return BuiltInCategory.OST_StructuralColumns;
                case ElementTypeMode.Column:
                    return BuiltInCategory.OST_StructuralColumns;
                case ElementTypeMode.Beam:
                    return BuiltInCategory.OST_StructuralFraming;
                case ElementTypeMode.Floor:
                    return BuiltInCategory.OST_Floors;
                case ElementTypeMode.Wall:
                    return BuiltInCategory.OST_Walls;
                default:
                    return BuiltInCategory.OST_StructuralColumns;
            }
        }

        /// <summary>
        /// 顯示處理結果
        /// </summary>
        private void ShowResults(List<JoinResult> results, bool isDryRun)
        {
            var totalProcessed = results.Count;
            var totalJoined = results.Sum(r => r.JoinedCount);
            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);

            var message = isDryRun ? "【預覽模式 - 未修改模型】\n\n" : "";
            message += $"處理元件：{totalProcessed}\n";
            message += $"成功：{successCount}\n";
            message += $"失敗：{failCount}\n";
            message += $"總接合數：{totalJoined}";

            TaskDialog.Show("自動接合結果", message);
        }
    }
}

