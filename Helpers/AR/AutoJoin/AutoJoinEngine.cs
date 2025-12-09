using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin
{
    /// <summary>
    /// 自動接合執行報告
    /// </summary>
    public class AutoJoinReport
    {
        public int PairsChecked { get; set; }
        public int Joined { get; set; }
        public int Already { get; set; }
        public int Switched { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
    }

    /// <summary>
    /// 自動結構接合引擎
    /// </summary>
    public class AutoJoinEngine
    {
        private StreamWriter _log;
        private List<Rule> _cachedRules;

        public AutoJoinReport Run(Document doc, UIDocument uidoc, AutoJoinSettings s)
        {
            var rpt = new AutoJoinReport();

            try
            {
                // 初始化日誌
                InitializeLog(s);

                // 建立規則並收集元素池（只建立一次）
                _cachedRules = BuildRules(s);
                var pools = CollectPools(doc, uidoc, s);

                // 執行接合操作
                using (var t = new Transaction(doc, s.DryRun ? "[預覽] 自動結構接合" : "自動結構接合"))
                {
                    if (!s.DryRun) t.Start();

                    ProcessJoinRules(doc, s, pools, rpt);

                    if (!s.DryRun) t.Commit();
                }

                // 記錄摘要
                LogSummary(rpt);
            }
            finally
            {
                _log?.Dispose();
            }

            return rpt;
        }

        /// <summary>
        /// 初始化 CSV 日誌
        /// </summary>
        private void InitializeLog(AutoJoinSettings s)
        {
            if (!s.EnableCsvLog) return;

            try
            {
                var dir = Path.GetDirectoryName(s.CsvPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                _log = new StreamWriter(s.CsvPath, false, Encoding.UTF8);
                _log.WriteLine("Timestamp,A_Id,A_Category,B_Id,B_Category,Action,Result,Message");
            }
            catch (Exception ex)
            {
                // 日誌初始化失敗不應中斷主流程
                System.Diagnostics.Debug.WriteLine($"Log initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 處理所有接合規則
        /// </summary>
        private void ProcessJoinRules(Document doc, AutoJoinSettings s,
            Dictionary<BuiltInCategory, List<Element>> pools, AutoJoinReport rpt)
        {
            foreach (var rule in _cachedRules)
            {
                if (!pools.TryGetValue(rule.A, out var listA) || listA.Count == 0) continue;
                if (!pools.TryGetValue(rule.B, out var listB) || listB.Count == 0) continue;

                ProcessRulePairs(doc, s, rule, listA, listB, rpt);
            }
        }

        /// <summary>
        /// 處理單一規則的所有元素配對
        /// </summary>
        private void ProcessRulePairs(Document doc, AutoJoinSettings s, Rule rule,
            List<Element> listA, List<Element> listB, AutoJoinReport rpt)
        {
            var bIds = listB.Select(e => e.Id).ToList();

            foreach (var a in listA)
            {
                var outline = JoinGeometryHelper.GetOutline(a, s.InflateFeet);
                if (outline == null) continue;

                // 第一階段：BoundingBox 過濾
                var nearB = new FilteredElementCollector(doc, bIds)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();

                // 第二階段：精確碰撞檢測
                foreach (var b in nearB)
                {
                    if (a.Id == b.Id) continue;

                    // 精確檢測是否相交
                    if (!JoinGeometryHelper.AreElementsIntersecting(doc, a, b))
                    {
                        rpt.Skipped++;
                        continue;
                    }

                    rpt.PairsChecked++;
                    ProcessElementPair(doc, s, a, b, rule.ACutsB, rpt);
                }
            }
        }

        /// <summary>
        /// 處理單一元素配對
        /// </summary>
        private void ProcessElementPair(Document doc, AutoJoinSettings s,
            Element a, Element b, bool needACutB, AutoJoinReport rpt)
        {
            // 模式 1: 只切換順序
            if (s.SwitchOnly)
            {
                ProcessSwitchOnly(doc, s, a, b, rpt);
                return;
            }

            // 模式 2: 已接合的元素
            if (JoinGeometryUtils.AreElementsJoined(doc, a, b))
            {
                ProcessAlreadyJoined(doc, s, a, b, needACutB, rpt);
                return;
            }

            // 模式 3: 新接合
            ProcessNewJoin(doc, s, a, b, needACutB, rpt);
        }

        /// <summary>
        /// 處理「只切換順序」模式
        /// </summary>
        private void ProcessSwitchOnly(Document doc, AutoJoinSettings s, Element a, Element b, AutoJoinReport rpt)
        {
            if (!JoinGeometryUtils.AreElementsJoined(doc, a, b)) return;

            if (!s.DryRun)
            {
                if (JoinGeometryHelper.TrySwitchWithRetry(doc, a, b))
                {
                    rpt.Switched++;
                    LogAction(a, b, "SwitchOnly", "Success", "");
                }
                else
                {
                    rpt.Failed++;
                    LogAction(a, b, "SwitchOnly", "Failed", "Switch failed");
                }
            }
            else
            {
                rpt.Switched++;
            }
        }

        /// <summary>
        /// 處理已接合的元素
        /// </summary>
        private void ProcessAlreadyJoined(Document doc, AutoJoinSettings s,
            Element a, Element b, bool needACutB, AutoJoinReport rpt)
        {
            rpt.Already++;

            // 檢查是否需要切換順序
            if (needACutB && JoinGeometryHelper.NeedSwitch(doc, a, b, true))
            {
                if (!s.DryRun)
                {
                    if (JoinGeometryHelper.TrySwitchWithRetry(doc, a, b))
                    {
                        rpt.Switched++;
                        LogAction(a, b, "Switch", "Success", "Corrected cutting order");
                    }
                    else
                    {
                        rpt.Failed++;
                        LogAction(a, b, "Switch", "Failed", "Could not correct cutting order");
                    }
                }
                else
                {
                    rpt.Switched++;
                }
            }
            else
            {
                LogAction(a, b, "AlreadyJoined", "Skipped", "Cutting order already correct");
            }
        }

        /// <summary>
        /// 處理新接合
        /// </summary>
        private void ProcessNewJoin(Document doc, AutoJoinSettings s,
            Element a, Element b, bool needACutB, AutoJoinReport rpt)
        {
            if (!s.DryRun)
            {
                if (JoinGeometryHelper.TryJoinThenSwitch(doc, a, b, needACutB, out string errorMsg))
                {
                    rpt.Joined++;
                    LogAction(a, b, "Join", "Success", needACutB ? "Joined with switch" : "Joined");
                }
                else
                {
                    // 嘗試強制接合
                    if (JoinGeometryHelper.ForceJoin(doc, a, b, needACutB, out errorMsg))
                    {
                        rpt.Joined++;
                        LogAction(a, b, "ForceJoin", "Success", "Joined after unjoin all");
                    }
                    else
                    {
                        rpt.Failed++;
                        LogAction(a, b, "Join", "Failed", errorMsg ?? "Unknown error");
                    }
                }
            }
            else
            {
                // 預覽模式
                rpt.Joined++;
                if (needACutB) rpt.Switched++;
            }
        }

        #region 規則與元素收集

        /// <summary>
        /// 接合規則結構
        /// </summary>
        private struct Rule
        {
            public BuiltInCategory A;
            public BuiltInCategory B;
            public bool ACutsB;
        }

        /// <summary>
        /// 建立接合規則列表
        /// </summary>
        private List<Rule> BuildRules(AutoJoinSettings s)
        {
            var list = new List<Rule>();

            // 內建規則
            if (s.Rule_Wall_Floor_FloorCuts)
                list.Add(new Rule { A = BuiltInCategory.OST_Floors, B = BuiltInCategory.OST_Walls, ACutsB = true });

            if (s.Rule_Wall_Beam_BeamCuts)
                list.Add(new Rule { A = BuiltInCategory.OST_StructuralFraming, B = BuiltInCategory.OST_Walls, ACutsB = true });

            if (s.Rule_Floor_Column_ColumnCuts)
                list.Add(new Rule { A = BuiltInCategory.OST_StructuralColumns, B = BuiltInCategory.OST_Floors, ACutsB = true });

            // 自訂規則
            foreach (var (a, b) in s.CustomPairs.Distinct())
                list.Add(new Rule { A = a, B = b, ACutsB = true });

            return list;
        }

        /// <summary>
        /// 收集需要處理的元素池（使用快取的規則）
        /// </summary>
        private Dictionary<BuiltInCategory, List<Element>> CollectPools(Document doc, UIDocument uidoc, AutoJoinSettings s)
        {
            var result = new Dictionary<BuiltInCategory, List<Element>>();

            // 取得使用者選取的元素（如果需要）
            HashSet<ElementId> selected = null;
            if (s.OnlyUserSelection && uidoc != null)
                selected = new HashSet<ElementId>(uidoc.Selection.GetElementIds());

            // 只收集規則中會用到的類別
            var needCats = new HashSet<BuiltInCategory>();
            foreach (var r in _cachedRules)
            {
                needCats.Add(r.A);
                needCats.Add(r.B);
            }

            // 收集各類別的元素
            foreach (var bic in needCats)
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType();

                // 如果只處理目前視圖
                if (s.OnlyActiveView && doc.ActiveView != null)
                    col = col.WherePasses(new VisibleInViewFilter(doc, doc.ActiveView.Id));

                // 過濾並收集元素
                var list = col.ToElements()
                    .Where(JoinGeometryHelper.IsJoinable)
                    .Where(e => selected == null || selected.Contains(e.Id))
                    .ToList();

                if (list.Count > 0)
                    result[bic] = list;
            }

            return result;
        }

        #endregion

        #region 日誌記錄

        /// <summary>
        /// 記錄操作動作
        /// </summary>
        private void LogAction(Element a, Element b, string action, string result, string message)
        {
            if (_log == null) return;

            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var aId = a?.Id.Value ?? -1;
                var bId = b?.Id.Value ?? -1;
                var aCat = a?.Category?.Name ?? "Unknown";
                var bCat = b?.Category?.Name ?? "Unknown";

                _log.WriteLine($"{timestamp},{aId},{aCat},{bId},{bCat},{action},{result},{message}");
                _log.Flush();
            }
            catch
            {
                // 日誌記錄失敗不應影響主流程
            }
        }

        /// <summary>
        /// 記錄執行摘要
        /// </summary>
        private void LogSummary(AutoJoinReport rpt)
        {
            if (_log == null) return;

            try
            {
                _log.WriteLine();
                _log.WriteLine("=== 執行摘要 ===");
                _log.WriteLine($"檢查配對,{rpt.PairsChecked}");
                _log.WriteLine($"新接合,{rpt.Joined}");
                _log.WriteLine($"原已接合,{rpt.Already}");
                _log.WriteLine($"切換順序,{rpt.Switched}");
                _log.WriteLine($"略過（未相交）,{rpt.Skipped}");
                _log.WriteLine($"失敗,{rpt.Failed}");
                _log.Flush();
            }
            catch
            {
                // 忽略日誌錯誤
            }
        }

        #endregion
    }
}
