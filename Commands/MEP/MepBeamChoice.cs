using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class CmdMepPositionDimension
    {
        private static string FaceKey(Document doc, Reference reference)
        {
            if (doc.GetElement(reference.ElementId) is RevitLinkInstance link)
            {
                var source = link.GetLinkDocument() ?? throw new InvalidOperationException("基準連結未載入。");
                return link.UniqueId + "|" + reference.CreateReferenceInLink().ConvertToStableRepresentation(source);
            }
            return reference.ConvertToStableRepresentation(doc);
        }

        private sealed class CandidateFaceFilter : ISelectionFilter
        {
            private readonly Document _doc;
            private readonly HashSet<ElementId> _elements;
            private readonly HashSet<string> _keys;
            internal CandidateFaceFilter(Document doc, IEnumerable<BeamMatch> matches)
            {
                _doc = doc;
                _elements = new HashSet<ElementId>(matches.Select(m => m.Side.Reference.ElementId));
                _keys = new HashSet<string>(matches.Select(m => FaceKey(doc,m.Side.Reference)),StringComparer.Ordinal);
            }
            public bool AllowElement(Element element) => _elements.Contains(element.Id);
            public bool AllowReference(Reference reference, XYZ point)
            {
                try { return _elements.Contains(reference.ElementId) && _keys.Contains(FaceKey(_doc,reference)); }
                catch { return false; }
            }
        }

        private static BeamMatch ChooseBeamMatch(UIDocument ui, Document doc, List<BeamMatch> matches,
            XYZ normal, string label, ref string preferredReference)
        {
            if (matches.Count == 0) return null;
            if (matches.Count == 1 || matches[1].Distance-matches[0].Distance > 25/304.8) return matches[0];
            var ambiguous = matches.Where(m => m.Distance-matches[0].Distance <= 25/304.8).ToList();
            string cached = preferredReference;
            var previous = ambiguous.FirstOrDefault(m => FaceKey(doc,m.Side.Reference) == cached);
            if (previous != null) return previous;
            if (ui == null) return null;
            var dialog = new TaskDialog("定位基準待指定") {
                MainInstruction = label + "：有多個接近的梁側面",
                MainContent = "未自動猜測基準。手選僅接受下列已通過方向、範圍與高程檢查的候選面。\n" +
                    "本批後續項目若仍包含同一歧義候選面，沿用你的選擇。",
                ExpandedContent = string.Join("\n",ambiguous.Select(m =>
                    $"梁 {m.Side.Id}｜距離 {m.Distance*304.8:0.##} mm｜高程差 {m.HeightGap*304.8:0.##} mm｜" +
                    $"面高程 {m.Side.Vertices.Min(p=>p.DotProduct(normal))*304.8:0.##} ～ {m.Side.Vertices.Max(p=>p.DotProduct(normal))*304.8:0.##} mm")),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,"手選基準面");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,"略過此項");
            var answer = dialog.Show();
            if (answer == TaskDialogResult.Cancel) throw new System.OperationCanceledException("使用者取消定位基準選取。");
            if (answer != TaskDialogResult.CommandLink1) return null;
            var picked = ui.Selection.PickObject(ObjectType.PointOnElement,new CandidateFaceFilter(doc,ambiguous),
                "選取定位基準梁側面，可按 Tab 切換；Esc 取消本次操作");
            string key = FaceKey(doc,picked);
            var chosen = ambiguous.FirstOrDefault(m => FaceKey(doc,m.Side.Reference) == key);
            if (chosen == null) throw new InvalidOperationException("所選面不是有效候選，未建立尺寸。");
            preferredReference = key;
            return chosen;
        }
    }
}
