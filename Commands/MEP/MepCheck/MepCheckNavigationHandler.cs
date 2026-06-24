using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP.MepCheck
{
    internal enum MepCheckNavigationAction
    {
        Select,
        SelectAndZoom,
        FixDuplicateMarks,
        Recheck
    }

    internal sealed class MepCheckNavigationHandler : IExternalEventHandler
    {
        private readonly object _syncRoot = new object();
        private readonly Document _document;
        private readonly ElementId _viewId;
        private readonly MepCheckOptions _options;
        private List<ElementId> _elementIds = new List<ElementId>();
        private MepCheckNavigationAction _action = MepCheckNavigationAction.SelectAndZoom;
        private MepCheckResultsForm _form;

        public MepCheckNavigationHandler(Document document, ElementId viewId, MepCheckOptions options)
        {
            _document = document;
            _viewId = viewId;
            _options = options;
        }

        public void Attach(MepCheckResultsForm form)
        {
            _form = form;
        }

        public void Request(IEnumerable<ElementId> elementIds, MepCheckNavigationAction action)
        {
            lock (_syncRoot)
            {
                _elementIds = elementIds?
                    .Where(id => id != null && id != ElementId.InvalidElementId)
                    .Distinct(new ElementIdValueComparer())
                    .ToList() ?? new List<ElementId>();
                _action = action;
            }
        }

        public void RequestRecheck()
        {
            Request(Array.Empty<ElementId>(), MepCheckNavigationAction.Recheck);
        }

        public void Execute(UIApplication app)
        {
            List<ElementId> ids;
            MepCheckNavigationAction action;
            lock (_syncRoot)
            {
                ids = _elementIds.ToList();
                action = _action;
            }

            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null || !ReferenceEquals(uiDoc.Document, _document))
            {
                _form?.SetRequestCompleted("目前作用中的文件已變更，請回到原文件後再執行。", true);
                return;
            }

            try
            {
                switch (action)
                {
                    case MepCheckNavigationAction.Select:
                    case MepCheckNavigationAction.SelectAndZoom:
                        Navigate(uiDoc, ids, action);
                        break;
                    case MepCheckNavigationAction.FixDuplicateMarks:
                        FixDuplicateMarks(ids);
                        Recheck("設備編號修復完成，已重新檢查。");
                        break;
                    case MepCheckNavigationAction.Recheck:
                        Recheck("已重新檢查目前模型。");
                        break;
                }
            }
            catch (Exception ex)
            {
                _form?.SetRequestCompleted($"MEP 動作執行失敗：{ex.Message}", true);
            }
        }

        public string GetName()
        {
            return "YD BIM Tools - MEP 檢查動作";
        }

        private static void Navigate(
            UIDocument uiDoc,
            IReadOnlyCollection<ElementId> ids,
            MepCheckNavigationAction action)
        {
            if (ids.Count == 0)
            {
                return;
            }

            List<ElementId> elementIds = ids.ToList();
            uiDoc.Selection.SetElementIds(elementIds);
            if (action == MepCheckNavigationAction.SelectAndZoom)
            {
                uiDoc.ShowElements(elementIds);
            }
        }

        private void FixDuplicateMarks(IReadOnlyCollection<ElementId> selectedIds)
        {
            if (selectedIds.Count == 0)
            {
                throw new InvalidOperationException("未選取可修復的重複設備編號。");
            }

            var allEquipment = CollectEquipment(_document).ToList();
            var allMarks = new HashSet<string>(
                allEquipment
                    .Select(GetMark)
                    .Where(mark => !string.IsNullOrWhiteSpace(mark)),
                StringComparer.OrdinalIgnoreCase);
            var selectedValues = new HashSet<long>(
                selectedIds.Select(id => id.GetIdValue()));

            int fixedCount = 0;
            using (var transaction = new Transaction(_document, "修復重複設備編號"))
            {
                transaction.Start();
                foreach (var group in allEquipment
                    .Select(element => new { Element = element, Mark = GetMark(element) })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Mark))
                    .GroupBy(item => item.Mark, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1))
                {
                    List<FamilyInstance> ordered = group
                        .Select(item => item.Element)
                        .OrderBy(element => element.Id.GetIdValue())
                        .ToList();
                    long preservedId = ordered[0].Id.GetIdValue();
                    int suffix = 2;

                    foreach (FamilyInstance element in ordered)
                    {
                        if (element.Id.GetIdValue() == preservedId ||
                            !selectedValues.Contains(element.Id.GetIdValue()))
                        {
                            continue;
                        }

                        Parameter parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                        if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
                        {
                            continue;
                        }

                        string newMark;
                        do
                        {
                            newMark = $"{group.Key}-{suffix:00}";
                            suffix++;
                        }
                        while (allMarks.Contains(newMark));

                        if (parameter.Set(newMark))
                        {
                            allMarks.Add(newMark);
                            fixedCount++;
                        }
                    }
                }

                if (fixedCount == 0)
                {
                    transaction.RollBack();
                    throw new InvalidOperationException("選取項目沒有可寫入的重複設備編號，或選到的是保留項目。");
                }

                transaction.Commit();
            }
        }

        private void Recheck(string status)
        {
            View view = _document.GetElement(_viewId) as View ?? _document.ActiveView;
            MepCheckResult result = new MepCheckService().Run(_document, view, _options);
            _form?.UpdateResult(result, status);
        }

        private static IEnumerable<FamilyInstance> CollectEquipment(Document doc)
        {
            foreach (BuiltInCategory category in new[]
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_PlumbingFixtures
            })
            {
                foreach (FamilyInstance element in new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    yield return element;
                }
            }
        }

        private static string GetMark(Element element)
        {
            return element
                .get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?
                .AsString()?
                .Trim() ?? string.Empty;
        }

        private sealed class ElementIdValueComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.GetIdValue() == y.GetIdValue();
            }

            public int GetHashCode(ElementId obj)
            {
                return obj?.GetIdValue().GetHashCode() ?? 0;
            }
        }
    }
}
