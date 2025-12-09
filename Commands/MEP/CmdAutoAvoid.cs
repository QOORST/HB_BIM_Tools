using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.UI;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    /// <summary>
    /// 管線避讓工具命令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoAvoid : IExternalCommand
    {
        private UIDocument _uidoc;
        private Document _doc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;

            Logger.Info("=== 管線避讓工具啟動 ===");

            try
            {
                // 步驟 1：顯示設定視窗
                var win = new MainWindow();
                bool? result = win.ShowDialog();

                if (result != true)
                {
                    Logger.Info("使用者取消操作");
                    return Result.Cancelled;
                }

                AvoidOptions opt = win.Options;
                Logger.Info($"設定參數: 彎角={opt.BendAngle}度, 偏移={opt.ExtraOffsetMm}mm, 方向={opt.Direction}");

                // 統計變數
                int successCount = 0;
                int failCount = 0;

                // 循環處理
                while (true)
                {
                    try
                    {
                        List<Element> targetElements = new List<Element>();

                        // 步驟 2A：檢查是否有預選元素
                        ICollection<ElementId> preSelectedIds = _uidoc.Selection.GetElementIds();
                        if (preSelectedIds != null && preSelectedIds.Count > 0)
                        {
                            // 使用預選元素
                            targetElements = preSelectedIds
                                .Select(id => _doc.GetElement(id))
                                .Where(e => e != null && (e is Pipe || e is Duct || e is Conduit))
                                .ToList();

                            if (targetElements.Count > 0)
                            {
                                Logger.Info($"使用預選的 {targetElements.Count} 個元素");

                                // 清除選擇以避免干擾後續操作
                                _uidoc.Selection.SetElementIds(new List<ElementId>());
                            }
                        }

                        // 步驟 2B：如果沒有預選，則提示選擇
                        if (targetElements.Count == 0)
                        {
                            IList<Reference> pipeRefs = _uidoc.Selection.PickObjects(
                                ObjectType.Element,
                                new PipeSelectionFilter(),
                                "請選擇要避讓的管線（可多選），按 Finish 或右鍵完成選擇"
                            );

                            if (pipeRefs == null || pipeRefs.Count == 0)
                            {
                                Logger.Info("未選擇任何元素");
                                break;
                            }

                            targetElements = pipeRefs.Select(r => _doc.GetElement(r)).Where(e => e != null).ToList();
                            Logger.Info($"選擇了 {targetElements.Count} 個元素");
                        }

                        // 步驟 3：選擇兩個管線上的點
                        Reference point1Ref = _uidoc.Selection.PickObject(
                            ObjectType.PointOnElement,
                            new PipeSelectionFilter(),
                            "請選擇避讓起點（在管線上點擊）"
                        );

                        Reference point2Ref = _uidoc.Selection.PickObject(
                            ObjectType.PointOnElement,
                            new PipeSelectionFilter(),
                            "請選擇避讓終點（在管線上點擊）"
                        );

                        XYZ point1 = point1Ref.GlobalPoint;
                        XYZ point2 = point2Ref.GlobalPoint;

                        Logger.Info($"起點: ({point1.X * GeometryUtils.FT_TO_MM:F0}, {point1.Y * GeometryUtils.FT_TO_MM:F0}, {point1.Z * GeometryUtils.FT_TO_MM:F0})");
                        Logger.Info($"終點: ({point2.X * GeometryUtils.FT_TO_MM:F0}, {point2.Y * GeometryUtils.FT_TO_MM:F0}, {point2.Z * GeometryUtils.FT_TO_MM:F0})");

                        // 步驟 4：執行避讓
                        foreach (var targetElement in targetElements)
                        {
                            ElementId targetId = targetElement.Id;

                            using (var trans = new Transaction(_doc, $"避讓管線-{targetId}"))
                            {
                                trans.Start();
                                try
                                {
                                    Result bendResult = ExecuteBendByPoints(targetElement, point1, point2, opt);

                                    if (bendResult == Result.Succeeded)
                                    {
                                        trans.Commit();
                                        successCount++;
                                        Logger.Info($"元素 {targetId} 避讓成功");
                                    }
                                    else
                                    {
                                        trans.RollBack();
                                        failCount++;
                                        Logger.Warning($"元素 {targetId} 避讓失敗");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (trans.HasStarted() && !trans.HasEnded())
                                    {
                                        trans.RollBack();
                                    }
                                    failCount++;
                                    Logger.Error($"元素 {targetId} 避讓過程發生錯誤", ex);
                                }
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // 使用者按 ESC，退出循環
                        Logger.Info("使用者取消選擇");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("處理過程發生錯誤", ex);
                        break;
                    }
                }

                // 顯示最終結果
                if (successCount > 0 || failCount > 0)
                {
                    string summary = $"避讓完成：\n\n成功: {successCount} 個\n失敗: {failCount} 個";
                    if (failCount > 0)
                    {
                        summary += $"\n\n詳細資訊請查看日誌：\n{Logger.GetLogFilePath()}";
                    }
                    TaskDialog.Show("管線避讓", summary);
                    Logger.Info($"避讓結果 - 成功: {successCount}, 失敗: {failCount}");
                }

                return successCount > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                Logger.Info("使用者取消操作");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("操作失敗", ex);
                TaskDialog.Show("錯誤", $"操作失敗: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 根據兩點執行避讓（從 TestCommand 移植）
        /// </summary>
        private Result ExecuteBendByPoints(Element target, XYZ point1, XYZ point2, AvoidOptions opt)
        {
            ElementId targetId = target.Id;

            try
            {
                // 獲取元素位置曲線
                var lc = target.Location as LocationCurve;
                if (lc == null)
                {
                    TaskDialog.Show("錯誤", "選擇的元素沒有位置曲線");
                    return Result.Failed;
                }

                Line pipeLine = lc.Curve as Line;
                if (pipeLine == null)
                {
                    TaskDialog.Show("錯誤", "僅支援直線管線");
                    return Result.Failed;
                }

                XYZ startpoint = pipeLine.GetEndPoint(0);
                XYZ endpoint = pipeLine.GetEndPoint(1);
                XYZ dir = (endpoint - startpoint).Normalize();

                // 投影點到元素上
                XYZ point1project = pipeLine.Project(point1).XYZPoint;
                XYZ point2project = pipeLine.Project(point2).XYZPoint;

                // 確保 point1 在 point2 前
                double t1 = (point1project - startpoint).DotProduct(dir);
                double t2 = (point2project - startpoint).DotProduct(dir);
                if (t1 > t2)
                {
                    var temp = point1project;
                    point1project = point2project;
                    point2project = temp;
                }

                Logger.Info($"投影起點: ({point1project.X * GeometryUtils.FT_TO_MM:F0}, {point1project.Y * GeometryUtils.FT_TO_MM:F0}, {point1project.Z * GeometryUtils.FT_TO_MM:F0})");
                Logger.Info($"投影終點: ({point2project.X * GeometryUtils.FT_TO_MM:F0}, {point2project.Y * GeometryUtils.FT_TO_MM:F0}, {point2project.Z * GeometryUtils.FT_TO_MM:F0})");

                // 計算避讓偏移量
                double offsetvalue = opt.ExtraOffsetMm * GeometryUtils.MM_TO_FT;

                // 決定方向（向上或向下避讓）
                bool flipUp = opt.Direction == DirectionMode.Up ||
                              (opt.Direction == DirectionMode.Auto) ||
                              opt.Direction == DirectionMode.VerticalFlip;

                if (opt.Direction == DirectionMode.Down)
                    flipUp = false;

                Logger.Info($"避讓偏移量: {offsetvalue * GeometryUtils.FT_TO_MM:F0}mm, 方向: {(flipUp ? "向上" : "向下")}");

                // 決定彎角計算水平偏移（0度時水平偏移為無限大，需要特殊處理）
                double angleRad = opt.BendAngle * Math.PI / 180.0;
                double horizontalOffset = Math.Abs(offsetvalue / Math.Tan(angleRad));

                // 防止無限大或 NaN 值
                if (double.IsInfinity(horizontalOffset) || double.IsNaN(horizontalOffset))
                {
                    horizontalOffset = 0;
                }

                Logger.Info($"彎角={opt.BendAngle}度, 水平偏移={horizontalOffset * GeometryUtils.FT_TO_MM:F0}mm");

                // 構建 6 點路徑（考慮彎角）
                XYZ p1 = startpoint;

                // p2 在點1附近
                XYZ p2 = point1project;

                // p3 = p2 向上/向下移動 + 向前延伸（根據彎角）
                XYZ verticalOffset = new XYZ(0, 0, flipUp ? offsetvalue : -offsetvalue);
                XYZ horizontalExtension = dir * horizontalOffset; // 管線方向延伸（根據彎角）
                XYZ p3 = point1project + verticalOffset + horizontalExtension;

                // p4 = p5 向上/向下移動 + 向後延伸（根據彎角）
                XYZ p4 = point2project + verticalOffset - horizontalExtension; // 點2處向後延伸
                // p5 在點2附近
                XYZ p5 = point2project;

                XYZ p6 = endpoint;

                var path = new List<XYZ> { p1, p2, p3, p4, p5, p6 };

                Logger.Info($"生成路徑: {path.Count} 個點");
                for (int i = 0; i < path.Count; i++)
                {
                    Logger.Debug($"  點{i}: ({path[i].X * GeometryUtils.FT_TO_MM:F0}, {path[i].Y * GeometryUtils.FT_TO_MM:F0}, {path[i].Z * GeometryUtils.FT_TO_MM:F0})");
                }

                // 構建 DetourPlan
                var plan = new DetourPlan
                {
                    Path = path,
                    UsedDirection = opt.Direction,
                    OffsetFt = offsetvalue,
                    IsValid = true
                };

                // 執行替換（注意：已在 Transaction 中，不需要再開啟）
                bool success = RevitUtils.ReplaceWithDetour(_doc, target, plan, opt);
                if (success)
                {
                    Logger.Info($"避讓執行成功 - 元素 Id={targetId}");
                    return Result.Succeeded;
                }
                else
                {
                    Logger.Warning($"避讓執行失敗 - 元素 Id={targetId} - ReplaceWithDetour 返回 false");
                    return Result.Failed;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ExecuteBendByPoints 失敗 - 元素 Id={targetId}", ex);
                TaskDialog.Show("路徑生成錯誤",
                    $"元素 ID: {targetId}\n" +
                    $"錯誤訊息：{ex.Message}\n\n" +
                    $"詳細資訊：\n{Logger.GetLogFilePath()}");
                throw; // 重新拋出異常以便上層處理
            }
        }

        /// <summary>
        /// 元素選擇過濾器
        /// </summary>
        private class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Pipe || elem is Duct || elem is Conduit;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}

