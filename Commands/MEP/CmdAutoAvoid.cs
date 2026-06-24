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
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    /// <summary>
    /// 管線避讓工具命令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoAvoid : IExternalCommand
    {
        private static double _lastBendAngle = 45.0;
        private static double _lastOffsetMm = 500.0;
        private static DirectionMode _lastDirection = DirectionMode.Up;
        private static bool _lastRepeatMode = true;
        private UIDocument _uidoc;
        private Document _doc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;

            Logger.Info("=== 管線避讓工具啟動 ===");

            try
            {
                List<Element> preselectedElements = GetPreselectedTargets();
                if (preselectedElements.Count > 0)
                {
                    _uidoc.Selection.SetElementIds(new List<ElementId>());
                    Logger.Info($"使用預選的 {preselectedElements.Count} 個元素");
                }

                if (!TryGetOptions(preselectedElements.Count, out AvoidOptions opt, out bool repeatMode))
                {
                    Logger.Info("使用者取消操作");
                    return Result.Cancelled;
                }
                Logger.Info($"設定參數: 彎角={opt.BendAngle}度, 偏移={opt.ExtraOffsetMm}mm, 方向={opt.Direction}, 連續模式={repeatMode}");

                // 統計變數
                int successCount = 0;
                int failCount = 0;

                bool usePreselectedOnce = preselectedElements.Count > 0;
                do
                {
                    try
                    {
                        List<Element> targetElements = usePreselectedOnce
                            ? preselectedElements
                            : PickTargetElements();
                        usePreselectedOnce = false;

                        if (targetElements.Count == 0)
                        {
                            Logger.Info("未選擇任何元素");
                            break;
                        }

                        Reference point1Ref = _uidoc.Selection.PickObject(
                            ObjectType.PointOnElement,
                            new PipeSelectionFilter(),
                            "步驟 2/3：點選避讓區段起點（在管線上）"
                        );

                        Reference point2Ref = _uidoc.Selection.PickObject(
                            ObjectType.PointOnElement,
                            new PipeSelectionFilter(),
                            "步驟 3/3：點選避讓區段終點（在管線上）"
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
                while (repeatMode);

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

        private List<Element> GetPreselectedTargets()
        {
            ICollection<ElementId> preSelectedIds = _uidoc.Selection.GetElementIds();
            if (preSelectedIds == null || preSelectedIds.Count == 0)
            {
                return new List<Element>();
            }

            return preSelectedIds
                .Select(id => _doc.GetElement(id))
                .Where(IsSupportedTarget)
                .ToList();
        }

        private List<Element> PickTargetElements()
        {
            IList<Reference> refs = _uidoc.Selection.PickObjects(
                ObjectType.Element,
                new PipeSelectionFilter(),
                "步驟 1/3：選擇要避讓的管線（可框選/多選），按完成");

            return refs?.Select(r => _doc.GetElement(r)).Where(IsSupportedTarget).ToList() ?? new List<Element>();
        }

        private static bool IsSupportedTarget(Element element)
        {
            return element is Pipe || element is Duct || element is Conduit;
        }

        private bool TryGetOptions(int preselectedCount, out AvoidOptions options, out bool repeatMode)
        {
            options = new AvoidOptions
            {
                BendAngle = 45.0,
                ExtraOffsetMm = 500,
                Direction = DirectionMode.Up
            };
            repeatMode = false;

            using (var form = new WinForms.Form())
            using (var headerPanel = new WinForms.Panel())
            using (var titleLabel = new WinForms.Label())
            using (var selectionLabel = new WinForms.Label())
            using (var settingsGroup = new WinForms.GroupBox())
            using (var angleLabel = new WinForms.Label())
            using (var angleBox = new WinForms.ComboBox())
            using (var offsetLabel = new WinForms.Label())
            using (var offsetBox = new WinForms.NumericUpDown())
            using (var preset300Button = new WinForms.Button())
            using (var preset500Button = new WinForms.Button())
            using (var preset800Button = new WinForms.Button())
            using (var directionLabel = new WinForms.Label())
            using (var upRadio = new WinForms.RadioButton())
            using (var downRadio = new WinForms.RadioButton())
            using (var autoRadio = new WinForms.RadioButton())
            using (var repeatCheck = new WinForms.CheckBox())
            using (var flowPanel = new WinForms.Panel())
            using (var hintLabel = new WinForms.Label())
            using (var actionPanel = new WinForms.Panel())
            using (var okButton = new WinForms.Button())
            using (var cancelButton = new WinForms.Button())
            {
                form.Text = "管線避讓";
                form.ClientSize = new System.Drawing.Size(444, 430);
                form.StartPosition = WinForms.FormStartPosition.CenterScreen;
                form.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = true;
                form.BackColor = System.Drawing.Color.FromArgb(245, 247, 250);

                headerPanel.Left = 0;
                headerPanel.Top = 0;
                headerPanel.Width = 444;
                headerPanel.Height = 76;
                headerPanel.BackColor = System.Drawing.Color.FromArgb(0, 120, 215);

                titleLabel.Text = "設定避讓方式";
                titleLabel.Left = 22;
                titleLabel.Top = 13;
                titleLabel.Width = 380;
                titleLabel.Height = 24;
                titleLabel.ForeColor = System.Drawing.Color.White;
                titleLabel.Font = new System.Drawing.Font(titleLabel.Font.FontFamily, 13, System.Drawing.FontStyle.Bold);

                selectionLabel.Text = preselectedCount > 0
                    ? $"已使用目前選取：{preselectedCount} 支管線"
                    : "未預選管線：按開始後先選管線";
                selectionLabel.Left = 22;
                selectionLabel.Top = 42;
                selectionLabel.Width = 380;
                selectionLabel.Height = 20;
                selectionLabel.ForeColor = System.Drawing.Color.FromArgb(224, 240, 255);

                settingsGroup.Text = "避讓參數";
                settingsGroup.Left = 18;
                settingsGroup.Top = 86;
                settingsGroup.Width = 408;
                settingsGroup.Height = 156;
                settingsGroup.BackColor = System.Drawing.Color.White;
                settingsGroup.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);

                angleLabel.Text = "彎頭角度";
                angleLabel.Left = 18;
                angleLabel.Top = 30;
                angleLabel.Width = 80;
                angleLabel.ForeColor = System.Drawing.Color.FromArgb(74, 85, 104);

                angleBox.Left = 110;
                angleBox.Top = 26;
                angleBox.Width = 260;
                angleBox.Height = 28;
                angleBox.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
                angleBox.Items.AddRange(new object[] { "22.5", "45", "90" });
                angleBox.SelectedItem = _lastBendAngle.ToString("0.##");
                if (angleBox.SelectedIndex < 0)
                    angleBox.SelectedItem = "45";

                offsetLabel.Text = "避讓高度";
                offsetLabel.Left = 18;
                offsetLabel.Top = 68;
                offsetLabel.Width = 90;
                offsetLabel.ForeColor = System.Drawing.Color.FromArgb(74, 85, 104);

                offsetBox.Left = 110;
                offsetBox.Top = 64;
                offsetBox.Width = 100;
                offsetBox.Height = 28;
                offsetBox.Minimum = 0;
                offsetBox.Maximum = 5000;
                offsetBox.Value = (decimal)Math.Min(
                    (double)offsetBox.Maximum,
                    Math.Max((double)offsetBox.Minimum, _lastOffsetMm));
                offsetBox.Increment = 50;

                preset300Button.Text = "300";
                preset300Button.Left = 224;
                preset300Button.Top = 63;
                preset300Button.Width = 46;
                preset300Button.Height = 28;
                preset300Button.FlatStyle = WinForms.FlatStyle.Flat;
                preset300Button.BackColor = System.Drawing.Color.FromArgb(235, 242, 255);
                preset300Button.ForeColor = System.Drawing.Color.FromArgb(26, 86, 219);
                preset300Button.Click += (s, e) => offsetBox.Value = 300;

                preset500Button.Text = "500";
                preset500Button.Left = 276;
                preset500Button.Top = 63;
                preset500Button.Width = 46;
                preset500Button.Height = 28;
                preset500Button.FlatStyle = WinForms.FlatStyle.Flat;
                preset500Button.BackColor = System.Drawing.Color.FromArgb(235, 242, 255);
                preset500Button.ForeColor = System.Drawing.Color.FromArgb(26, 86, 219);
                preset500Button.Click += (s, e) => offsetBox.Value = 500;

                preset800Button.Text = "800";
                preset800Button.Left = 328;
                preset800Button.Top = 63;
                preset800Button.Width = 46;
                preset800Button.Height = 28;
                preset800Button.FlatStyle = WinForms.FlatStyle.Flat;
                preset800Button.BackColor = System.Drawing.Color.FromArgb(235, 242, 255);
                preset800Button.ForeColor = System.Drawing.Color.FromArgb(26, 86, 219);
                preset800Button.Click += (s, e) => offsetBox.Value = 800;

                directionLabel.Text = "翻彎方向";
                directionLabel.Left = 18;
                directionLabel.Top = 110;
                directionLabel.Width = 80;
                directionLabel.ForeColor = System.Drawing.Color.FromArgb(74, 85, 104);

                upRadio.Text = "向上";
                upRadio.Left = 110;
                upRadio.Top = 108;
                upRadio.Width = 70;
                upRadio.Checked = _lastDirection == DirectionMode.Up;

                downRadio.Text = "向下";
                downRadio.Left = 190;
                downRadio.Top = 108;
                downRadio.Width = 70;
                downRadio.Checked = _lastDirection == DirectionMode.Down;

                autoRadio.Text = "自動";
                autoRadio.Left = 270;
                autoRadio.Top = 108;
                autoRadio.Width = 70;
                autoRadio.Checked = _lastDirection == DirectionMode.Auto;

                repeatCheck.Text = "連續模式：完成後繼續下一組避讓";
                repeatCheck.Left = 128;
                repeatCheck.Top = 252;
                repeatCheck.Checked = _lastRepeatMode;
                repeatCheck.Width = 285;
                repeatCheck.Height = 24;
                repeatCheck.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);

                flowPanel.Left = 18;
                flowPanel.Top = 288;
                flowPanel.Width = 408;
                flowPanel.Height = 38;
                flowPanel.BackColor = System.Drawing.Color.FromArgb(232, 244, 253);

                hintLabel.Text = "開始後流程：選管線 → 點起點 → 點終點";
                hintLabel.Left = 14;
                hintLabel.Top = 10;
                hintLabel.Width = 380;
                hintLabel.ForeColor = System.Drawing.Color.FromArgb(43, 108, 176);

                actionPanel.Left = 0;
                actionPanel.Top = 374;
                actionPanel.Width = 444;
                actionPanel.Height = 44;
                actionPanel.BackColor = System.Drawing.Color.White;

                okButton.Text = "開始避讓";
                okButton.Left = 236;
                okButton.Top = 7;
                okButton.Width = 96;
                okButton.Height = 30;
                okButton.FlatStyle = WinForms.FlatStyle.Flat;
                okButton.BackColor = System.Drawing.Color.FromArgb(0, 120, 215);
                okButton.ForeColor = System.Drawing.Color.White;
                okButton.UseVisualStyleBackColor = false;
                okButton.DialogResult = WinForms.DialogResult.OK;

                cancelButton.Text = "取消";
                cancelButton.Left = 342;
                cancelButton.Top = 7;
                cancelButton.Width = 70;
                cancelButton.Height = 30;
                cancelButton.FlatStyle = WinForms.FlatStyle.Flat;
                cancelButton.BackColor = System.Drawing.Color.White;
                cancelButton.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);
                cancelButton.DialogResult = WinForms.DialogResult.Cancel;

                headerPanel.Controls.Add(titleLabel);
                headerPanel.Controls.Add(selectionLabel);
                settingsGroup.Controls.Add(angleLabel);
                settingsGroup.Controls.Add(angleBox);
                settingsGroup.Controls.Add(offsetLabel);
                settingsGroup.Controls.Add(offsetBox);
                settingsGroup.Controls.Add(preset300Button);
                settingsGroup.Controls.Add(preset500Button);
                settingsGroup.Controls.Add(preset800Button);
                settingsGroup.Controls.Add(directionLabel);
                settingsGroup.Controls.Add(upRadio);
                settingsGroup.Controls.Add(downRadio);
                settingsGroup.Controls.Add(autoRadio);
                flowPanel.Controls.Add(hintLabel);
                actionPanel.Controls.Add(okButton);
                actionPanel.Controls.Add(cancelButton);
                form.Controls.Add(headerPanel);
                form.Controls.Add(settingsGroup);
                form.Controls.Add(repeatCheck);
                form.Controls.Add(flowPanel);
                form.Controls.Add(actionPanel);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                if (form.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return false;
                }

                options.BendAngle = double.Parse(angleBox.SelectedItem?.ToString() ?? "45");
                options.ExtraOffsetMm = (double)offsetBox.Value;
                options.Direction = downRadio.Checked
                    ? DirectionMode.Down
                    : autoRadio.Checked
                        ? DirectionMode.Auto
                        : DirectionMode.Up;
                repeatMode = repeatCheck.Checked;
                _lastBendAngle = options.BendAngle;
                _lastOffsetMm = options.ExtraOffsetMm;
                _lastDirection = options.Direction;
                _lastRepeatMode = repeatMode;

                var (isValid, errors) = options.Validate();
                if (!isValid)
                {
                    TaskDialog.Show("參數錯誤", string.Join("\n", errors));
                    return false;
                }

                return true;
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
