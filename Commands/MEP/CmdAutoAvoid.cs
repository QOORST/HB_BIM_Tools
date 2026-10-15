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
    public partial class CmdAutoAvoid : IExternalCommand
    {
        private static double _lastBendAngle = 45.0;
        private static double _lastOffsetMm = 500.0;
        private static double _lastClearanceMm = 50.0;
        private static DirectionMode _lastDirection = DirectionMode.Up;
        private static bool _lastRepeatMode = true;
        private UIDocument _uidoc;
        private Document _doc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            _windowHandle = commandData.Application.MainWindowHandle;
            _uidoc = commandData.Application.ActiveUIDocument;
            if (_uidoc == null)
            {
                TaskDialog.Show("管線避讓", "請先開啟模型，再執行管線避讓。");
                return Result.Cancelled;
            }
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

                bool transactionPending = false;
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

                        int kept = PreviewAvoidanceBatch(targetElements, point1, point2, opt, ref transactionPending, out bool failed);
                        successCount += kept;
                        if (failed) failCount += targetElements.Count;
                        if (kept == 0 && !failed) break;
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
                        TaskDialog.Show("避讓未完成", ex.Message);
                        break;
                    }
                }
                while (repeatMode && !transactionPending);

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

        private sealed class RollbackOnFailure : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                // Revit repairs must not silently change the graph validated before Commit.
                // Fail closed even on warnings, which may discard constraints/connections.
                return accessor.GetFailureMessages().Count == 0
                    ? FailureProcessingResult.Continue
                    : FailureProcessingResult.ProceedWithRollBack;
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
            using (var clearanceLabel = new WinForms.Label())
            using (var clearanceBox = new WinForms.NumericUpDown())
            using (var repeatCheck = new WinForms.CheckBox())
            using (var flowPanel = new WinForms.Panel())
            using (var hintLabel = new WinForms.Label())
            using (var actionPanel = new WinForms.Panel())
            using (var okButton = new WinForms.Button())
            using (var cancelButton = new WinForms.Button())
            {
                form.Text = "HB_BIM 管線避讓";
                form.ClientSize = new System.Drawing.Size(444, 470);
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
                headerPanel.BackColor = System.Drawing.Color.FromArgb(0, 105, 180);

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
                settingsGroup.Height = 196;
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
                angleBox.SelectedItem = _lastBendAngle.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                if (angleBox.SelectedIndex < 0)
                    angleBox.SelectedItem = "45";

                offsetLabel.Text = "避讓高度 mm";
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

                autoRadio.Text = "比較上下";
                autoRadio.Left = 270;
                autoRadio.Top = 108;
                autoRadio.Width = 100;
                autoRadio.Checked = _lastDirection == DirectionMode.Auto;
                clearanceLabel.Text = "要求淨距 mm";
                clearanceLabel.SetBounds(18, 148, 90, 22);
                clearanceBox.SetBounds(110, 144, 100, 28);
                clearanceBox.Minimum = 0; clearanceBox.Maximum = 1000;
                clearanceBox.Value = (decimal)_lastClearanceMm;
                clearanceBox.Increment = 10;
                settingsGroup.Controls.Add(clearanceLabel);
                settingsGroup.Controls.Add(clearanceBox);

                repeatCheck.Text = "連續模式：完成後繼續下一組避讓";
                repeatCheck.Left = 128;
                repeatCheck.Top = 292;
                repeatCheck.Checked = _lastRepeatMode;
                repeatCheck.Width = 285;
                repeatCheck.Height = 24;
                repeatCheck.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);

                flowPanel.Left = 18;
                flowPanel.Top = 328;
                flowPanel.Width = 408;
                flowPanel.Height = 38;
                flowPanel.BackColor = System.Drawing.Color.FromArgb(232, 244, 253);

                hintLabel.Text = "選管線 → 點起終點 → 預覽／複查 → 確認";
                hintLabel.Left = 14;
                hintLabel.Top = 10;
                hintLabel.Width = 380;
                hintLabel.ForeColor = System.Drawing.Color.FromArgb(43, 108, 176);

                actionPanel.Left = 0;
                actionPanel.Top = 414;
                actionPanel.Width = 444;
                actionPanel.Height = 44;
                actionPanel.BackColor = System.Drawing.Color.White;

                okButton.Text = "開始預覽";
                okButton.Left = 236;
                okButton.Top = 7;
                okButton.Width = 96;
                okButton.Height = 30;
                okButton.FlatStyle = WinForms.FlatStyle.Flat;
                okButton.BackColor = System.Drawing.Color.FromArgb(0, 105, 180);
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

                options.BendAngle = double.Parse(angleBox.SelectedItem?.ToString() ?? "45", System.Globalization.CultureInfo.InvariantCulture);
                options.ExtraOffsetMm = (double)offsetBox.Value;
                options.ClearanceMm = (double)clearanceBox.Value;
                _lastClearanceMm = options.ClearanceMm;
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
