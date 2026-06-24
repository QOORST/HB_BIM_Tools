using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;
using YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish;
using YD_RevitTools.LicenseManager.Helpers;
using YD_RevitTools.LicenseManager.UI.Finishings;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings
{
    /// <summary>
    /// AR 裝修工具 - 刪除裝修元素
    /// 支援依全部 / 目前選取集 / 樓層 / 房間刪除 AR 裝修元素
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdDeleteFinishings : IExternalCommand
    {
        private static readonly BuiltInCategory[] TargetCategories =
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_GenericModel
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            try
            {
                var licenseManager = LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Finishings.Delete") && !licenseManager.HasFeatureAccess("Finishings.Generate"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援刪除裝修功能。\n\n" +
                        "請升級到 Trial 或以上版本以使用此功能。");
                    return Result.Cancelled;
                }

                var items = CollectFinishingItems(doc);
                if (items.Count == 0)
                {
                    var fallbackDialog = new TaskDialog("刪除裝修")
                    {
                        MainInstruction = "未找到已標記的 AR 裝修元素",
                        MainContent =
                            "這通常表示目前模型中的裝修是較早版本生成，尚未寫入穩定識別標記。\n\n" +
                            "您仍可用手動框選方式刪除這些裝修層。"
                    };
                    fallbackDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "手動框選要刪除的裝修元素（相容舊版）");
                    fallbackDialog.CommonButtons = TaskDialogCommonButtons.Cancel;

                    if (fallbackDialog.Show() != TaskDialogResult.CommandLink1)
                        return Result.Cancelled;

                    var fallbackTargets = PickPotentialFinishingElements(uidoc);
                    if (fallbackTargets == null || fallbackTargets.Count == 0)
                    {
                        TaskDialog.Show("刪除裝修", "未選擇任何裝修元素。");
                        return Result.Cancelled;
                    }

                    var fallbackConfirm = TaskDialog.Show("確認刪除",
                        $"即將刪除 {fallbackTargets.Count} 個手動選取的裝修元素。\n\n" +
                        "請確認您選取的都是裝修層，而非原始結構。是否繼續？",
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                    if (fallbackConfirm != TaskDialogResult.Yes)
                        return Result.Cancelled;

                    using (var t = new Transaction(doc, "Delete Finishings"))
                    {
                        t.Start();
                        doc.Delete(fallbackTargets);
                        t.Commit();
                    }

                    TaskDialog.Show("刪除完成", $"已成功刪除 {fallbackTargets.Count} 個手動選取的裝修元素。");
                    return Result.Succeeded;
                }

                var currentSelection = uidoc.Selection.GetElementIds();
                var selectedFinishingIds = items
                    .Where(x => currentSelection.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToList();

                IList<ElementId> targets;
                string deleteMode;

                if (selectedFinishingIds.Count == 0)
                {
                    var startDialog = new TaskDialog("刪除裝修")
                    {
                        MainInstruction = "選擇刪除方式",
                        MainContent =
                            $"目前尚未預先選取裝修元素。\n" +
                            $"專案中共找到 {items.Count} 個 AR 裝修元素。"
                    };
                    startDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "現在手動框選要刪除的裝修元素");
                    startDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "依條件篩選刪除（全部／樓層／房間）");
                    startDialog.CommonButtons = TaskDialogCommonButtons.Cancel;

                    var startResult = startDialog.Show();
                    if (startResult == TaskDialogResult.CommandLink1)
                    {
                        targets = PickFinishingElements(uidoc);
                        if (targets == null || targets.Count == 0)
                        {
                            TaskDialog.Show("刪除裝修", "未選擇任何裝修元素。");
                            return Result.Cancelled;
                        }
                        deleteMode = "手動選取";
                    }
                    else if (startResult == TaskDialogResult.CommandLink2)
                    {
                        var dialog = new DeleteFinishingsDialog(items, currentSelection);
                        new System.Windows.Interop.WindowInteropHelper(dialog) { Owner = uiapp.MainWindowHandle };
                        if (dialog.ShowDialog() != true || dialog.SelectedIds == null || !dialog.SelectedIds.Any())
                            return Result.Cancelled;

                        targets = dialog.SelectedIds.Distinct().ToList();
                        deleteMode = ResolveDeleteMode(items, currentSelection, targets);
                    }
                    else
                    {
                        return Result.Cancelled;
                    }
                }
                else
                {
                    var dialog = new DeleteFinishingsDialog(items, currentSelection);
                    new System.Windows.Interop.WindowInteropHelper(dialog) { Owner = uiapp.MainWindowHandle };
                    if (dialog.ShowDialog() != true || dialog.SelectedIds == null || !dialog.SelectedIds.Any())
                        return Result.Cancelled;

                    targets = dialog.SelectedIds.Distinct().ToList();
                    deleteMode = ResolveDeleteMode(items, currentSelection, targets);
                }

                var confirmResult = TaskDialog.Show("確認刪除",
                    $"即將刪除 {targets.Count} 個裝修元素（{deleteMode}）。\n\n" +
                    "此操作無法復原，是否繼續？",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirmResult != TaskDialogResult.Yes)
                    return Result.Cancelled;

                using (var t = new Transaction(doc, "Delete Finishings"))
                {
                    t.Start();
                    doc.Delete(targets);
                    t.Commit();
                }

                TaskDialog.Show("刪除完成", $"已成功刪除 {targets.Count} 個裝修元素。");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"刪除裝修失敗: {ex.Message}";
                Debug.WriteLine($"❌ 刪除裝修失敗: {ex}");
                TaskDialog.Show("錯誤", message);
                return Result.Failed;
            }
        }

        private static string ResolveDeleteMode(IList<FinishingItemInfo> allItems, ICollection<ElementId> currentSelection, IList<ElementId> targets)
        {
            if (targets == null || targets.Count == 0)
                return "未指定";

            if (targets.Count == allItems.Count)
                return "全部裝修元素";

            var selectedCount = allItems.Count(x => currentSelection.Contains(x.Id));
            if (selectedCount > 0 && targets.Count == selectedCount)
                return "目前選取集";

            var targetSet = targets.Select(x => x.GetIdValue()).ToHashSet();
            var levelCount = allItems.Where(x => targetSet.Contains(x.Id.GetIdValue())).Select(x => x.LevelName ?? string.Empty).Distinct().Count();
            var roomCount = allItems.Where(x => targetSet.Contains(x.Id.GetIdValue())).Select(x => x.RoomDisplay ?? string.Empty).Distinct().Count();

            if (roomCount > 0 && roomCount <= levelCount)
                return "房間篩選";

            return "樓層／條件篩選";
        }

        private static List<ElementId> PickFinishingElements(UIDocument uidoc)
        {
            try
            {
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new FinishingDeleteSelectionFilter(uidoc.Document),
                    "選擇要刪除的裝修元素（可多選，完成後按 Enter）");

                return refs.Select(r => r.ElementId).Distinct().ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return new List<ElementId>();
            }
        }

        private static List<ElementId> PickPotentialFinishingElements(UIDocument uidoc)
        {
            try
            {
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PotentialFinishingSelectionFilter(uidoc.Document),
                    "框選要刪除的裝修元素（舊版相容模式；請勿選到原始結構）");

                return refs.Select(r => r.ElementId).Distinct().ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return new List<ElementId>();
            }
        }

        private static List<FinishingItemInfo> CollectFinishingItems(Document doc)
        {
            var items = new List<FinishingItemInfo>();

            foreach (var bic in TargetCategories)
            {
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(IsFinishingElement)
                    .ToList();

                foreach (var element in elements)
                {
                    items.Add(new FinishingItemInfo
                    {
                        Id = element.Id,
                        LevelName = GetLevelName(doc, element),
                        RoomDisplay = GetRoomDisplay(doc, element)
                    });
                }
            }

            return items
                .GroupBy(x => x.Id.GetIdValue())
                .Select(g => g.First())
                .ToList();
        }

        internal static bool IsFinishingElement(Element element)
        {
            if (element == null || element.Category == null)
                return false;

            var categoryId = element.Category.Id.GetIdValue();
            if (categoryId != (long)BuiltInCategory.OST_Walls &&
                categoryId != (long)BuiltInCategory.OST_Floors &&
                categoryId != (long)BuiltInCategory.OST_Ceilings &&
                categoryId != (long)BuiltInCategory.OST_GenericModel)
            {
                return false;
            }

            // DirectShape 以 ApplicationId 為最高優先識別依據
            if (element is DirectShape ds)
            {
                if (ds.ApplicationId == "YD_BIM_Finishings")
                    return true;

                if (ds.ApplicationId == "YD_BIM_Formwork")
                    return false;
            }

            // 面生面/房間裝修都會寫入識別資料；優先用資料判斷，避免依賴名稱
            if (FinishingElementGuard.IsManagedFinishingElement(element))
                return true;

            return LooksLikeFinishingByName(element, element.Document);
        }

        private static bool HasFinishingRoomData(Element element)
        {
            var roomId = GetRoomId(element);
            if (roomId.HasValue && roomId.Value > 0)
                return true;

            var roomName = GetStringParam(element, "房間名稱(AR_RoomNames)", "房間名稱", "AR_RoomNames");
            if (!string.IsNullOrWhiteSpace(roomName))
                return true;

            var roomNumber = GetStringParam(element, "房間編號(AR_RoomNumbers)", "房間編號", "AR_RoomNumbers");
            if (!string.IsNullOrWhiteSpace(roomNumber))
                return true;

            return false;
        }

        private static bool HasStableFinishingMarker(Element element)
        {
            var comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
            return !string.IsNullOrWhiteSpace(comments)
                && comments.IndexOf("YD_BIM_Finishings", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasFaceToFaceFinishingData(Element element)
        {
            // 面生面已寫入這些共用參數；既有模型可直接被辨識，不必重生。
            var hostIdText = element.LookupParameter(SharedParams.P_HostId)?.AsString();
            var materialName = element.LookupParameter(SharedParams.P_MaterialName)?.AsString();
            var thicknessParam = element.LookupParameter(SharedParams.P_Thickness);
            var areaParam = element.LookupParameter(SharedParams.P_Area);

            long hostIdValue;
            var hasHostId = !string.IsNullOrWhiteSpace(hostIdText) && long.TryParse(hostIdText, out hostIdValue) && hostIdValue > 0;
            var hasMaterial = !string.IsNullOrWhiteSpace(materialName);
            var hasThickness = thicknessParam != null && thicknessParam.StorageType == StorageType.Double && Math.Abs(thicknessParam.AsDouble()) > 1e-9;
            var hasArea = areaParam != null && areaParam.StorageType == StorageType.Double && Math.Abs(areaParam.AsDouble()) > 1e-9;

            return hasHostId && (hasMaterial || hasThickness || hasArea);
        }

        private static bool LooksLikeFinishingByName(Element element, Document doc)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(element.Name))
                candidates.Add(element.Name);

            var typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                var type = doc.GetElement(typeId) as ElementType;
                if (type != null && !string.IsNullOrWhiteSpace(type.Name))
                    candidates.Add(type.Name);
            }

            foreach (var name in candidates)
            {
                if (name.IndexOf("裝修", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("finish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("AR_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetLevelName(Document doc, Element element)
        {
            try
            {
                if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
                {
                    var level = doc.GetElement(element.LevelId) as Level;
                    if (level != null && !string.IsNullOrWhiteSpace(level.Name))
                        return level.Name;
                }

                var levelParam = element.LookupParameter("Reference Level") ?? element.LookupParameter("樓層");
                if (levelParam != null)
                {
                    var value = levelParam.AsValueString() ?? levelParam.AsString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }

                var bb = element.get_BoundingBox(null);
                if (bb != null)
                {
                    var centerZ = (bb.Min.Z + bb.Max.Z) / 2.0;
                    var levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .ToList();

                    var best = levels.LastOrDefault(l => l.Elevation <= centerZ + 1e-6) ?? levels.FirstOrDefault();
                    if (best != null)
                        return best.Name;
                }
            }
            catch
            {
            }

            return "（未知樓層）";
        }

        private static string GetRoomDisplay(Document doc, Element element)
        {
            var roomNumber = GetStringParam(element, "房間編號(AR_RoomNumbers)", "房間編號", "AR_RoomNumbers");
            var roomName = GetStringParam(element, "房間名稱(AR_RoomNames)", "房間名稱", "AR_RoomNames");

            var roomId = GetRoomId(element);
            if (roomId.HasValue && (string.IsNullOrWhiteSpace(roomNumber) || string.IsNullOrWhiteSpace(roomName)))
            {
                var room = doc.GetElement(new ElementId(roomId.Value)) as Room;
                if (room != null)
                {
                    if (string.IsNullOrWhiteSpace(roomNumber)) roomNumber = room.Number;
                    if (string.IsNullOrWhiteSpace(roomName)) roomName = room.Name;
                }
            }

            if (!string.IsNullOrWhiteSpace(roomNumber) && !string.IsNullOrWhiteSpace(roomName))
                return $"{roomNumber} - {roomName}";
            if (!string.IsNullOrWhiteSpace(roomNumber))
                return roomNumber;
            if (!string.IsNullOrWhiteSpace(roomName))
                return roomName;

            return "（無房間關聯）";
        }

        private static string GetStringParam(Element element, params string[] names)
        {
            foreach (var name in names)
            {
                var param = element.LookupParameter(name);
                if (param == null) continue;

                var value = param.AsString() ?? param.AsValueString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static int? GetRoomId(Element element)
        {
            var param = element.LookupParameter("房間ID(AR_RoomId)")
                     ?? element.LookupParameter("房間ID")
                     ?? element.LookupParameter("AR_RoomId");
            if (param == null)
                return null;

            if (param.StorageType == StorageType.Integer)
            {
                var value = param.AsInteger();
                return value > 0 ? value : (int?)null;
            }

            if (param.StorageType == StorageType.String)
            {
                int parsed;
                if (int.TryParse(param.AsString(), out parsed) && parsed > 0)
                    return parsed;
            }

            return null;
        }
    }

    internal class FinishingDeleteSelectionFilter : ISelectionFilter
    {
        private readonly Document _doc;

        public FinishingDeleteSelectionFilter(Document doc)
        {
            _doc = doc;
        }

        public bool AllowElement(Element elem)
        {
            return elem != null && elem.Document == _doc && CmdDeleteFinishings.IsFinishingElement(elem);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    internal class PotentialFinishingSelectionFilter : ISelectionFilter
    {
        private readonly Document _doc;

        public PotentialFinishingSelectionFilter(Document doc)
        {
            _doc = doc;
        }

        public bool AllowElement(Element elem)
        {
            if (elem == null || elem.Document != _doc || elem.Category == null)
                return false;

            var categoryId = elem.Category.Id.GetIdValue();
            return categoryId == (long)BuiltInCategory.OST_Walls
                || categoryId == (long)BuiltInCategory.OST_Floors
                || categoryId == (long)BuiltInCategory.OST_Ceilings
                || categoryId == (long)BuiltInCategory.OST_GenericModel;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
