// CmdJoinToPicked.cs - 接合所有相交元素到選取的目標元素
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    /// <summary>
    /// 接合所有相交元素到選取的目標元素（優先級：柱 > 梁 > 樓板 > 牆）
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdJoinToPicked : IExternalCommand
    {

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet elems)
        {
            // 檢查授權 - 接合到選取功能
            var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("JoinToPicked"))
            {
                TaskDialog.Show("授權限制",
                    "您的授權版本不支援接合到選取功能。\n\n" +
                    "請升級至試用版、標準版或專業版以使用此功能。\n\n" +
                    "點擊「授權管理」按鈕以查看或更新授權。");
                return Result.Cancelled;
            }

            var uidoc = data.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                // 讓使用者選取目標元素
                var r = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    "請點選目標構件（柱/梁/牆/樓板等結構元素）");
                var target = doc.GetElement(r);

                if (target == null || target.Category == null)
                    return Result.Cancelled;

                // 收集候選元素
                var candidates = CollectCandidateElements(doc);

                // 找出與目標相交的元素
                var intersecting = FindIntersectingElements(doc, target, candidates);

                // 執行接合操作
                var stats = PerformJoinOperations(doc, target, intersecting);

                // 顯示結果
                ShowResults(stats);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                msg = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// 收集候選元素（柱、梁、牆、樓板）
        /// </summary>
        private List<Element> CollectCandidateElements(Document doc)
        {
            var candidateCats = new[]
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors
            };

            var candidates = new List<Element>();
            foreach (var bic in candidateCats)
            {
                candidates.AddRange(
                    new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToElements()
                        .Where(JoinGeometryHelper.IsJoinable)
                );
            }

            return candidates;
        }

        /// <summary>
        /// 找出與目標元素相交的元素
        /// </summary>
        private List<Element> FindIntersectingElements(Document doc, Element target, List<Element> candidates)
        {
            // 取得目標的膨脹外框
            var outline = JoinGeometryHelper.GetOutline(target, 0.10);
            if (outline == null) return new List<Element>();

            // 第一階段：BoundingBox 過濾
            var near = new FilteredElementCollector(doc, candidates.Select(x => x.Id).ToList())
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e.Id != target.Id)
                .ToList();

            // 第二階段：精確碰撞檢測
            var intersecting = new List<Element>();
            var iFilter = new ElementIntersectsElementFilter(target);

            foreach (var e in near)
            {
                var hit = new FilteredElementCollector(doc, new List<ElementId> { e.Id })
                    .WherePasses(iFilter)
                    .ToElementIds();

                if (hit.Count > 0)
                    intersecting.Add(e);
            }

            return intersecting;
        }

        /// <summary>
        /// 執行接合操作統計
        /// </summary>
        private class JoinStats
        {
            public int CheckedPairs { get; set; }
            public int Joined { get; set; }
            public int Already { get; set; }
            public int Switched { get; set; }
            public int Failed { get; set; }
        }

        /// <summary>
        /// 執行接合操作
        /// </summary>
        private JoinStats PerformJoinOperations(Document doc, Element target, List<Element> intersecting)
        {
            var stats = new JoinStats();

            using (var t = new Transaction(doc, "接合到選取元素"))
            {
                t.Start();

                foreach (var e in intersecting)
                {
                    stats.CheckedPairs++;

                    try
                    {
                        ProcessElementPair(doc, e, target, stats);
                    }
                    catch
                    {
                        stats.Failed++;
                    }
                }

                t.Commit();
            }

            return stats;
        }

        /// <summary>
        /// 處理單一元素配對
        /// </summary>
        private void ProcessElementPair(Document doc, Element e, Element target, JoinStats stats)
        {
            // 步驟 1: 確保元素已接合
            if (!JoinGeometryUtils.AreElementsJoined(doc, e, target))
            {
                // 嘗試接合
                if (!JoinGeometryHelper.TryJoin(doc, e, target))
                {
                    // 失敗則解除所有接合後重試
                    JoinGeometryHelper.UnjoinAllWith(doc, e);
                    JoinGeometryHelper.UnjoinAllWith(doc, target);

                    if (!JoinGeometryHelper.TryJoin(doc, e, target))
                    {
                        stats.Failed++;
                        return;
                    }
                }
                stats.Joined++;
            }
            else
            {
                stats.Already++;
            }

            // 步驟 2: 檢查並調整切割順序
            bool wantECutTarget = JoinGeometryHelper.ShouldACutB(e, target);

            if (JoinGeometryHelper.NeedSwitch(doc, e, target, wantECutTarget))
            {
                if (JoinGeometryHelper.TrySwitchWithRetry(doc, e, target))
                    stats.Switched++;
                else
                    stats.Failed++;
            }
        }

        /// <summary>
        /// 顯示執行結果
        /// </summary>
        private void ShowResults(JoinStats stats)
        {
            TaskDialog.Show("接合到選取元素",
                $"檢查配對：{stats.CheckedPairs}\n" +
                $"新接合：{stats.Joined}\n" +
                $"切換順序：{stats.Switched}\n" +
                $"原已接合：{stats.Already}\n" +
                $"失敗/略過：{stats.Failed}");
        }

    }
}
