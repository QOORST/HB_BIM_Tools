using System;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal sealed class SleeveIssueResult
    {
        internal ViewSchedule Schedule;
        internal SleeveRevisionSnapshot Baseline;
    }
    internal static class SleeveIssueService
    {
        internal static SleeveIssueResult Execute(Document doc, SleeveIssueRequest request)
        {
            if (doc == null || doc.IsFamilyDocument || doc.IsModifiable) throw new InvalidOperationException("請於專案文件執行發圖作業。");
            if (request?.Options == null) throw new InvalidOperationException("缺少發圖範圍設定。");
            string name = SleeveIssueRequest.ValidateName(request.BaselineName);
            if (SleeveRevisionService.List(doc).Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("基準名稱已存在，請另取新名稱；本次未寫入模型。");
            if (request.Controls.Any(p => !SleeveIssueRequest.ValidControl(p.Value)))
                throw new InvalidOperationException("統計設定只能選擇納入、排除或作廢。");
            var options = request.Options;
            using (var group = new TransactionGroup(doc, "套管發圖：統計設定、補號、上圖與保存基準"))
            {
                if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("無法開始發圖作業。");
                try
                {
                    using (var tx = new Transaction(doc, "套管發圖統計設定"))
                    {
                        tx.Start();
                        SleeveQuantityData.EnsureParameters(doc); doc.Regenerate();
                        var enrolled = SleeveQuantityData.Synchronize(doc, options.ManualIds).ToDictionary(r => r.Id.GetIdValue());
                        foreach (var pair in request.Controls)
                        {
                            if (!enrolled.TryGetValue(pair.Key, out var row)) throw new InvalidOperationException("套管已不存在或不在管理範圍，請關閉後重新開啟發圖視窗。");
                            var p = doc.GetElement(row.Id).get_Parameter(SleeveQuantityData.Id(1));
                            if ((p?.AsString() ?? "") == pair.Value) continue;
                            if (p == null || p.IsReadOnly || !p.Set(pair.Value)) throw new InvalidOperationException("無法更新套管 " + pair.Key + " 的統計設定。");
                        }
                        var rows = SleeveQuantityData.Synchronize(doc);
                        if (rows.Any(r => !string.IsNullOrWhiteSpace(r.Control) && !SleeveIssueRequest.ValidControl(r.Control)))
                            throw new InvalidOperationException("整案仍有無效的統計設定。請切換全部樓層、清空區域篩選後修正，再保存整案基準。");
                        if (!rows.Any(options.Matches)) throw new InvalidOperationException("所選表格範圍沒有納入套管，請檢查樓層、區域與統計設定。");
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 未提交統計設定。");
                    }
                    // Manual enrollment was committed above; do not re-enroll a manual sleeve the user just excluded.
                    var scheduleOptions = new SleeveQuantityOptions { LevelKey = options.LevelKey, LevelName = options.LevelName,
                        Zone = options.Zone, Detailed = options.Detailed, SheetId = options.SheetId, LeftMm = options.LeftMm, TopMm = options.TopMm };
                    var schedule = SleeveQuantitySchedule.CreateOrUpdate(doc, scheduleOptions);
                    var baseline = SleeveRevisionService.Save(doc, name);
                    if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 未完成發圖作業。");
                    return new SleeveIssueResult { Schedule = schedule, Baseline = baseline };
                }
                catch
                {
                    if (group.GetStatus() == TransactionStatus.Started && group.RollBack() != TransactionStatus.RolledBack)
                        throw new InvalidOperationException("發圖作業回復失敗，請停止編輯並檢查模型。");
                    throw;
                }
            }
        }
    }
}
