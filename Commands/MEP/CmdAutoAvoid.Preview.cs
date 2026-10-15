using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.UI;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class CmdAutoAvoid
    {
        private sealed class PreviewOwner : WinForms.IWin32Window
        {
            public IntPtr Handle { get; }
            internal PreviewOwner(IntPtr handle) { Handle = handle; }
        }
        private IntPtr _windowHandle;

        private int PreviewAvoidanceBatch(List<Element> targets, XYZ first, XYZ last, AvoidOptions options,
            ref bool transactionPending, out bool failed)
        {
            failed = false;
            var sourceIds = targets.Select(t => t.Id).Distinct().ToList();
            var routes = new List<DetourPreviewItem>();
            var issues = new List<string>();
            var created = new List<ElementId>();
            var originalSelection = _uidoc.Selection.GetElementIds().ToList();
            using (var group = new TransactionGroup(_doc, "避讓預覽及確認"))
            {
                group.Start();
                try
                {
                    foreach (var id in sourceIds)
                    {
                        bool accepted = false;
                        var attempts = new List<string>();
                        var directions = options.Direction == DirectionMode.Auto
                            ? new[] { DirectionMode.Up, DirectionMode.Down } : new[] { options.Direction };
                        foreach (var direction in directions)
                        {
                            using (var trans = new Transaction(_doc, "暫存避讓候選"))
                            {
                                trans.Start();
                                var failureOptions = trans.GetFailureHandlingOptions();
                                failureOptions.SetFailuresPreprocessor(new RollbackOnFailure());
                                failureOptions.SetClearAfterRollback(true);
                                failureOptions.SetForcedModalHandling(true);
                                trans.SetFailureHandlingOptions(failureOptions);
                                try
                                {
                                    var target = _doc.GetElement(id) as MEPCurve;
                                    var plan = BuildPreviewPlan(target, first, last, options, direction);
                                    var connections = new HashSet<ElementId>();
                                    foreach (Connector connector in target.ConnectorManager.Connectors)
                                        foreach (Connector other in connector.AllRefs)
                                            if (other.Owner.Id != id && connector.IsConnectedTo(other)) connections.Add(other.Owner.Id);
                                    if (!RevitUtils.ReplaceWithDetour(_doc, target, plan, options, out var candidateIds, out var reason, retainOriginal: true))
                                        throw new InvalidOperationException(reason ?? "管段／彎頭或原有連接無法完整保留。");
                                    var candidateIssues = DetourCollisionReview.Check(_doc, candidateIds, connections, options.ClearanceMm);
                                    if (candidateIssues.Count > 0)
                                    {
                                        attempts.AddRange(candidateIssues.Select(s => $"{direction}：{s}"));
                                        if (direction == directions.Last()) routes.Add(new DetourPreviewItem { Label = $"管段 {id}（未通過）", Path = plan.Path });
                                        trans.RollBack(); continue;
                                    }
                                    TransactionStatus status = trans.Commit();
                                    if (status == TransactionStatus.Pending) { transactionPending = true; throw new InvalidOperationException("Revit 正在處理交易失敗，停止後續操作。"); }
                                    if (status != TransactionStatus.Committed) throw new InvalidOperationException("Revit 拒絕候選交易。");
                                    created.AddRange(candidateIds);
                                    routes.Add(new DetourPreviewItem { Label = $"管段 {id}｜{(direction == DirectionMode.Up ? "上翻" : "下翻")}", Path = plan.Path });
                                    accepted = true; break;
                                }
                                catch (Exception ex)
                                {
                                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                                    if (transactionPending) throw;
                                    attempts.Add(direction + "：" + ex.Message);
                                }
                            }
                        }
                        if (!accepted) issues.AddRange(attempts.Select(s => $"管段 {id}：{s}"));
                    }
                    failed = issues.Count > 0;
                    _uidoc.Selection.SetElementIds(created);
                    _uidoc.RefreshActiveView();
                    using (var preview = new DetourPreviewForm(routes, issues, options.ClearanceMm))
                    {
                        if (preview.ShowDialog(new PreviewOwner(_windowHandle)) != WinForms.DialogResult.OK || failed || created.Count == 0)
                        { group.RollBack(); return 0; }
                    }
                    // Read-only review is modal: the checked model cannot change between review and acceptance.
                    if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("整批避讓未成功提交。");
                    return sourceIds.Count;
                }
                finally
                {
                    if (group.GetStatus() == TransactionStatus.Started && !transactionPending) group.RollBack();
                    var valid = originalSelection.Where(id => _doc.GetElement(id) != null).ToList();
                    _uidoc.Selection.SetElementIds(valid);
                    _uidoc.RefreshActiveView();
                }
            }
        }

        private DetourPlan BuildPreviewPlan(MEPCurve target, XYZ first, XYZ last, AvoidOptions options, DirectionMode direction)
        {
            if (!((target?.Location as LocationCurve)?.Curve is Line line)) throw new InvalidOperationException("僅支援直線管線。");
            if ((target is Autodesk.Revit.DB.Plumbing.Pipe || target is Autodesk.Revit.DB.Mechanical.Duct) &&
                (InsulationLiningBase.GetInsulationIds(_doc, target.Id).Count > 0 ||
                 (target is Autodesk.Revit.DB.Mechanical.Duct && InsulationLiningBase.GetLiningIds(_doc, target.Id).Count > 0)))
                throw new InvalidOperationException("帶保溫／內襯管線尚未支援翻彎重建，已保留原管線。");
            return DetourRouteBuilder.Build(line, first, last, options, direction,
                Math.Max(_doc.Application.ShortCurveTolerance * 2, 1.0 / 304.8));
        }
    }
}
