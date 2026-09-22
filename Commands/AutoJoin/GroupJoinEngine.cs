using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal static class GroupJoinEngine
    {
        public static void Run(Document doc, IList<ClassifiedElement> scope, AutoJoinSettings settings, JoinEngineResult result)
        {
            foreach (var bucket in scope.Where(x => !x.Element.GroupId.Equals(ElementId.InvalidElementId))
                .GroupBy(x => x.Element.GroupId))
            {
                var items = bucket.ToList();
                var group = doc.GetElement(bucket.Key) as Group;
                // Nested groups and incomplete pairs remain protected in this first version.
                if (items.Count < 2 || group == null || !group.GroupId.Equals(ElementId.InvalidElementId)) continue;
                result.GroupMembersSkipped -= items.Count;
                result.GroupsAttempted++;
                var typeId = group.GetTypeId();
                var members = new HashSet<ElementId>(group.GetMemberIds());
                int joined = 0, reordered = 0;
                ClassifiedElement first = items[0], second = items[1];
                using (var batch = new TransactionGroup(doc, "HB 同群組接合"))
                {
                    batch.Start();
                    try
                    {
                        var guard = new RollbackOnFailure();
                        using (var tx = new Transaction(doc, "HB 同群組接合嘗試"))
                        {
                            tx.Start();
                            tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                                .SetFailuresPreprocessor(guard).SetClearAfterRollback(true));
                            for (int i = 0; i < items.Count; i++)
                            for (int j = i + 1; j < items.Count; j++)
                            {
                                first = items[i]; second = items[j];
                                result.PairChecks++;
                                if (!settings.AllowSameCategoryJoin && first.CategorySpec.Key == second.CategorySpec.Key) continue;
                                if (!settings.AllowStructuralNonStructuralJoin && first.CategorySpec.IsStructural != second.CategorySpec.IsStructural) continue;
                                var a = first.Element; var b = second.Element;
                                if (!a.GroupId.Equals(bucket.Key) || !b.GroupId.Equals(bucket.Key))
                                    throw new InvalidOperationException("群組成員關係改變");
                                var ba = a.get_BoundingBox(null); var bb = b.get_BoundingBox(null);
                                if (ba == null || bb == null || !JoinEngine.BoundingBoxesMatch(ba, bb, settings.CheckInDetail)) continue;
                                result.PairCandidates++;
                                int pairJoined = 0, pairReordered = 0;
                                using (var pair = new SubTransaction(doc))
                                {
                                    pair.Start();
                                    try
                                    {
                                        if (!JoinGeometryUtils.AreElementsJoined(doc, a, b))
                                        {
                                            JoinGeometryUtils.JoinGeometry(doc, a, b);
                                            pairJoined = 1;
                                        }
                                        int pa = settings.PriorityKeys.IndexOf(first.CategorySpec.Key);
                                        int pb = settings.PriorityKeys.IndexOf(second.CategorySpec.Key);
                                        if (pa >= 0 && pb >= 0 && pa != pb)
                                        {
                                            var winner = pa < pb ? a : b; var loser = pa < pb ? b : a;
                                            if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, winner, loser))
                                            {
                                                JoinGeometryUtils.SwitchJoinOrder(doc, winner, loser);
                                                pairReordered = 1;
                                            }
                                        }
                                    }
                                    catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException ||
                                        ex is Autodesk.Revit.Exceptions.InvalidOperationException)
                                    {
                                        if (pair.RollBack() != TransactionStatus.RolledBack)
                                            throw new InvalidOperationException("配對回復失敗，停止此群組。", ex);
                                        result.FailedOperations++;
                                        result.FailureDetails.Add(new JoinFailureDetail {
                                            FirstElementId = a.Id, SecondElementId = b.Id,
                                            Summary = $"群組 {bucket.Key}｜{a.Id} ↔ {b.Id} 配對未套用",
                                            Description = $"僅此配對已回復，其他配對繼續處理。\r\n{ex.Message}"
                                        });
                                        continue;
                                    }
                                    // Regeneration/commit failures are not ordinary pair rejections.
                                    doc.Regenerate();
                                    if (pair.Commit() != TransactionStatus.Committed)
                                        throw new InvalidOperationException("配對交易未提交，回復此群組。 ");
                                }
                                joined += pairJoined;
                                reordered += pairReordered;
                            }
                            if (tx.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException(string.IsNullOrEmpty(guard.Message) ? "Revit 未提交群組接合" : guard.Message);
                        }
                        var after = doc.GetElement(bucket.Key) as Group;
                        if (after == null || !after.GetTypeId().Equals(typeId) || !members.SetEquals(after.GetMemberIds()) ||
                            members.Any(id => doc.GetElement(id) == null || !doc.GetElement(id).GroupId.Equals(bucket.Key)))
                            throw new InvalidOperationException("群組類型或成員變更，拒絕保留結果");
                        if (batch.Assimilate() != TransactionStatus.Committed)
                            throw new InvalidOperationException("群組接合結果未提交");
                        result.Joined += joined;
                        result.Reordered += reordered;
                        result.GroupsCommitted++;
                    }
                    catch (Exception ex)
                    {
                        if (batch.GetStatus() == TransactionStatus.Started && batch.RollBack() != TransactionStatus.RolledBack)
                            throw new InvalidOperationException("群組操作回復未完成，已停止批次作業。", ex);
                        result.GroupsRolledBack++;
                        result.FailedOperations++;
                        result.FailureDetails.Add(new JoinFailureDetail {
                            FirstElementId = first.Element.Id, SecondElementId = second.Element.Id,
                            Summary = $"群組 {bucket.Key}｜整組未套用，請查看原因",
                            Description = $"群組 {bucket.Key} 整組回復；顯示最後檢查配對，不一定是警告來源。\r\n{ex.Message}"
                        });
                    }
                }
            }
        }

        private sealed class RollbackOnFailure : IFailuresPreprocessor
        {
            public string Message { get; private set; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                var messages = accessor.GetFailureMessages();
                if (messages.Count == 0) return FailureProcessingResult.Continue;
                Message = string.Join("\r\n", messages.Select(x => x.GetDescriptionText()));
                // Never resolve group failures by deleting, ungrouping, or accepting changed instances.
                return FailureProcessingResult.ProceedWithRollBack;
            }
        }
    }
}
