using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed partial class TagAlignService
    {
        private const double AlignTolerance = 0.05 / 304.8;
        private TagAlignResult AlignToPickedReference(UIDocument uiDoc, TagAlignOptions options)
        {
            var doc=uiDoc.Document; var view=doc.ActiveView;
            bool horizontal=options.Mode==TagAlignMode.Horizontal;
            ElementId basisId; IList<Reference> picked;
            try {
                basisId=uiDoc.Selection.PickObject(ObjectType.Element,new ReferenceFilter(view,horizontal,false,ElementId.InvalidElementId),"選取基準標籤或平行直線；Esc 取消").ElementId;
                picked=uiDoc.Selection.PickObjects(ObjectType.Element,new ReferenceFilter(view,horizontal,true,basisId),"批次選取標籤，按完成套用；Esc 取消且不修改模型");
            } catch(Autodesk.Revit.Exceptions.OperationCanceledException) { return TagAlignResult.Cancelled(); }
            if(picked.Count==0) return TagAlignResult.Failed("未選取待對齊標籤。");
            var axis=(horizontal?view.UpDirection:view.RightDirection).Normalize();
            var cross=(horizontal?view.RightDirection:view.UpDirection).Normalize();
            var notes=new List<string>(); var moved=new List<ElementId>();
            int overlap=0; double maxError=0;
            using(var group=new TransactionGroup(doc,"依基準對齊標籤")) {
                group.Start();
                try {
                    var selected=picked.Select(r=>r.ElementId).Distinct().Where(id=>id!=basisId).ToList();
                    var ids=new FilteredElementCollector(doc,view.Id).OfClass(typeof(IndependentTag)).ToElementIds().Union(selected).ToList();
                    if(doc.GetElement(basisId) is IndependentTag && !ids.Contains(basisId)) ids.Add(basisId);
                    var before=MeasureCommittedHeads(doc,view,ids);
                    XYZ basis=ReferenceCenter(doc,basisId,before);
                    foreach(var id in selected) {
                        var tag=doc.GetElement(id) as IndependentTag;
                        if(tag==null||tag.Pinned||tag.GroupId!=ElementId.InvalidElementId||tag.IsOrphaned) { notes.Add($"{id}：鎖定、群組或失去關聯，未移動"); continue; }
                        using(var tx=new Transaction(doc,"移動標籤")) {
                            tx.Start();
                            tag.TagHeadPosition+=axis*((basis-before[id].Center).DotProduct(axis));
                            foreach(var end in before[id].Ends) tag.SetLeaderEnd(end.Key,end.Value);
                            if(tx.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException($"{id}：交易未完成");
                        }
                        moved.Add(id);
                    }
                    if(moved.Count==0) throw new InvalidOperationException("沒有可移動的標籤。\n"+string.Join("\n",notes));
                    var after=MeasureCommittedHeads(doc,view,ids);
                    XYZ finalBasis=ReferenceCenter(doc,basisId,after);
                    if(finalBasis.DistanceTo(basis)>AlignTolerance) throw new InvalidOperationException($"基準 {basisId} 中心改變");
                    var moving=new HashSet<ElementId>(moved);
                    foreach(var id in ids) {
                        if(!moving.Contains(id)&&(before[id].Head.DistanceTo(after[id].Head)>AlignTolerance||before[id].Center.DistanceTo(after[id].Center)>AlignTolerance)) throw new InvalidOperationException($"未指定移動的標籤 {id} 位置改變");
                        VerifyLeaderState(doc,id,before[id]);
                    }
                    foreach(var id in moved) {
                        var a=after[id]; double error=Math.Abs((a.Center-finalBasis).DotProduct(axis));
                        maxError=Math.Max(maxError,error*304.8);
                        if(error>AlignTolerance||Math.Abs((a.Center-before[id].Center).DotProduct(cross))>AlignTolerance)
                            throw new InvalidOperationException($"標籤 {id}：誤差 {error*304.8:F3} mm；基準中心 {finalBasis.DotProduct(axis)*304.8:F3} mm；本體中心 {a.Center.DotProduct(axis)*304.8:F3} mm；頭位置 {a.Head.DotProduct(axis)*304.8:F3} mm");
                        var rect=TagRect.FromBoundingBox(a.Box,view);
                        if(after.Any(other=>other.Key!=id&&rect.Intersects(TagRect.FromBoundingBox(other.Value.Box,view)))) { overlap++; notes.Add($"{id}：重疊待複核，保留基準位置"); }
                    }
                    if(group.Assimilate()!=TransactionStatus.Committed) throw new InvalidOperationException("對齊交易群組未完成");
                } catch(Exception ex) {
                    if(group.GetStatus()==TransactionStatus.Started) {
                        if(group.RollBack()!=TransactionStatus.RolledBack) throw new InvalidOperationException("無法回復對齊，請檢查模型。",ex);
                        return TagAlignResult.Failed("本次對齊已回復。\n"+ex.Message);
                    }
                    return TagAlignResult.Failed("交易未正常完成，請檢查模型。\n"+ex.Message);
                }
            }
            return TagAlignResult.Succeeded(moved.Count,$"包圍框中心驗證通過 {moved.Count} 個；最大誤差 {maxError:F3} mm。\n重疊待複核 {overlap} 個。\n"+string.Join("\n",notes), needsReview: notes.Count > 0 || overlap > 0);
        }
        internal sealed class HeadSnapshot {
            internal XYZ Head; internal BoundingBoxXYZ Box; internal bool HasLeader; internal LeaderEndCondition Condition;
            internal List<KeyValuePair<Reference,XYZ>> Ends=new List<KeyValuePair<Reference,XYZ>>();
            internal XYZ Center=>Box.Transform.OfPoint((Box.Min+Box.Max)*0.5);
        }
        internal static Dictionary<ElementId,HeadSnapshot> MeasureCommittedHeads(Document doc,View view,IList<ElementId> ids)
        {
            var result=new Dictionary<ElementId,HeadSnapshot>();
            foreach(var id in ids) {
                var tag=doc.GetElement(id) as IndependentTag;
                if(tag==null) throw new InvalidOperationException($"標籤 {id} 已失效");
                var state=new HeadSnapshot { Head=tag.TagHeadPosition,HasLeader=tag.HasLeader };
                if(tag.HasLeader) {
                    state.Condition=tag.LeaderEndCondition;
                    if(state.Condition==LeaderEndCondition.Free)
                        foreach(var reference in tag.GetTaggedReferences()) state.Ends.Add(new KeyValuePair<Reference,XYZ>(reference,tag.GetLeaderEnd(reference)));
                }
                result.Add(id,state);
            }
            if (!result.Values.Any(state => state.HasLeader)) {
                foreach (var id in ids) {
                    var tag = (IndependentTag)doc.GetElement(id);
                    var box = tag.get_BoundingBox(view);
                    if (box == null) throw new InvalidOperationException($"標籤 {id} 無法量測");
                    result[id].Box = new BoundingBoxXYZ { Min = box.Min, Max = box.Max, Transform = box.Transform };
                }
                return result;
            }
            // Commit temporary leader suppression before reading bounds; rollback restores the original model.
            using(var preparation=new TransactionGroup(doc,"暫時量測標籤本體")) {
                preparation.Start();
                try {
                    using(var tx=new Transaction(doc,"暫時關閉引線")) {
                        tx.Start();
                        foreach(var id in ids) { var tag=(IndependentTag)doc.GetElement(id); if(tag.HasLeader) tag.HasLeader=false; }
                        if(tx.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException("量測準備未提交");
                    }
                    foreach(var id in ids) {
                        var tag=(IndependentTag)doc.GetElement(id); var box=tag.get_BoundingBox(view);
                        if(box==null) throw new InvalidOperationException($"標籤 {id} 無法量測");
                        if(tag.TagHeadPosition.DistanceTo(result[id].Head)>AlignTolerance) throw new InvalidOperationException($"標籤 {id} 關閉引線後頭位置改變，無法可靠量測");
                        result[id].Box=new BoundingBoxXYZ { Min=box.Min,Max=box.Max,Transform=box.Transform };
                    }
                } finally {
                    if(preparation.GetStatus()==TransactionStatus.Started&&preparation.RollBack()!=TransactionStatus.RolledBack) throw new InvalidOperationException("量測操作未完整回復");
                }
            }
            foreach(var id in ids) {
                var tag=(IndependentTag)doc.GetElement(id);
                if(tag.TagHeadPosition.DistanceTo(result[id].Head)>AlignTolerance) throw new InvalidOperationException($"標籤 {id} 量測後位置未還原");
                VerifyLeaderState(doc,id,result[id]);
            }
            return result;
        }
        private static void VerifyLeaderState(Document doc,ElementId id,HeadSnapshot state) {
            var tag=(IndependentTag)doc.GetElement(id);
            if(tag.HasLeader!=state.HasLeader||(state.HasLeader&&tag.LeaderEndCondition!=state.Condition)) throw new InvalidOperationException($"標籤 {id} 引線狀態改變");
            foreach(var end in state.Ends) if(tag.GetLeaderEnd(end.Key).DistanceTo(end.Value)>AlignTolerance) throw new InvalidOperationException($"標籤 {id} 引線端點改變");
        }
        private static XYZ ReferenceCenter(Document doc,ElementId id,Dictionary<ElementId,HeadSnapshot> heads) {
            return heads.TryGetValue(id,out var state)?state.Center:((CurveElement)doc.GetElement(id)).GeometryCurve.Evaluate(0.5,true);
        }
        private sealed class ReferenceFilter:ISelectionFilter {
            private readonly View view; private readonly bool horizontal,tagsOnly; private readonly ElementId excluded;
            public ReferenceFilter(View view,bool horizontal,bool tagsOnly,ElementId excluded) { this.view=view;this.horizontal=horizontal;this.tagsOnly=tagsOnly;this.excluded=excluded; }
            public bool AllowElement(Element e) {
                if(e.Id==excluded) return false;
                if(e is IndependentTag tag) return tag.OwnerViewId==view.Id&&!tag.IsOrphaned;
                if(tagsOnly||!(e is CurveElement curve)||!(curve.GeometryCurve is Line line)) return false;
                if(e.ViewSpecific&&e.OwnerViewId!=view.Id) return false;
                return Math.Abs(line.Direction.DotProduct(horizontal?view.RightDirection:view.UpDirection))>0.999999;
            }
            public bool AllowReference(Reference r,XYZ p) { return false; }
        }
    }
}
