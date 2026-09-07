using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdSelectRelatedTags : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "目前沒有開啟中的 Revit 文件。";
                return Result.Failed;
            }

            Document doc = uiDoc.Document;
            View view = doc.ActiveView;
            ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                TaskDialog.Show("關聯標籤", "請先選取構件或標籤。");
                return Result.Cancelled;
            }

            HashSet<ElementId> resultIds = new HashSet<ElementId>(selectedIds);
            HashSet<ElementId> targetElementIds = new HashSet<ElementId>();

            foreach (ElementId selectedId in selectedIds)
            {
                Element selected = doc.GetElement(selectedId);
                if (selected is IndependentTag selectedTag)
                {
                    foreach (ElementId taggedId in GetTaggedElementIds(selectedTag))
                    {
                        targetElementIds.Add(taggedId);
                        resultIds.Add(taggedId);
                    }
                }
                else if (selected != null)
                {
                    targetElementIds.Add(selectedId);
                }
            }

            int addedTags = 0;
            int addedElements = resultIds.Count - selectedIds.Count;
            foreach (IndependentTag tag in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(IndependentTag))
                         .Cast<IndependentTag>())
            {
                if (GetTaggedElementIds(tag).Any(targetElementIds.Contains) &&
                    resultIds.Add(tag.Id))
                {
                    addedTags++;
                }
            }

            if (resultIds.Count == selectedIds.Count)
            {
                TaskDialog.Show("關聯標籤", "目前視圖找不到與選取項目關聯的標籤或構件。");
                return Result.Succeeded;
            }

            uiDoc.Selection.SetElementIds(resultIds.ToList());
            TaskDialog.Show("關聯標籤", $"已加入關聯標籤 {addedTags} 個、關聯構件 {addedElements} 個。");
            return Result.Succeeded;
        }

        private static IEnumerable<ElementId> GetTaggedElementIds(IndependentTag tag)
        {
            MethodInfo method = typeof(IndependentTag).GetMethod("GetTaggedLocalElementIds", Type.EmptyTypes);
            if (method != null)
            {
                object value = method.Invoke(tag, null);
                if (value is IEnumerable<ElementId> ids)
                {
                    return ids.Where(id => id != ElementId.InvalidElementId);
                }
            }

            PropertyInfo property = typeof(IndependentTag).GetProperty("TaggedLocalElementId");
            if (property != null)
            {
                object value = property.GetValue(tag);
                if (value is ElementId id && id != ElementId.InvalidElementId)
                {
                    return new[] { id };
                }
            }

            return Enumerable.Empty<ElementId>();
        }
    }
}
