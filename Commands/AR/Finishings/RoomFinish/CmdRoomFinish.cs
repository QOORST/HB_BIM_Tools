using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    /// <summary>
    /// AR 裝修工具 - 房間裝修
    /// 基於房間邊界自動生成牆、樓板、天花板裝修
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdRoomFinish : IExternalCommand
    {
#if !REVIT2026
        private static UI.MainWindow _openWindow;
#endif
#if REVIT2026
        private class TypeOption
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }
#endif

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 房間裝修功能
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Finishings.RoomFinish"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援房間裝修功能。\n\n" +
                        "請升級至試用版、標準版或專業版以使用此功能。\n\n" +
                        "點擊「授權管理」按鈕以查看或更新授權。");
                    return Result.Cancelled;
                }

                Logger.ClearLog();
                Logger.Log("=== YD 房間裝修工具執行開始 ===");

                var uiApp = commandData.Application;
                var uiDoc = uiApp.ActiveUIDocument;
                var doc = uiDoc.Document;

                if (uiDoc == null || doc == null)
                {
                    message = "無法取得有效的 Revit 文件";
                    return Result.Failed;
                }

#if REVIT2026
                return RoomFinish2026ModelessController.Show(uiApp, ref message);
#else
                if (_openWindow != null)
                {
                    if (_openWindow.IsVisible)
                    {
                        _openWindow.Activate();
                        return Result.Succeeded;
                    }
                    _openWindow = null;
                }

                // 建立 ExternalEvent handlers（modeless 架構）
                var syncHandler = new SyncRoomsHandler();
                var applyHandler = new ApplyAndUpdateHandler();
                var pickHandler = new PickRoomsHandler();
                var focusHandler = new FocusRoomsHandler();
                var updateValuesHandler = new UpdateValuesOnlyHandler();
                var autoJoinHandler = new AutoJoinWallsHandler();
                var alignColumnsHandler = new AlignWallsToColumnsHandler();
                var toggleWallJoinsHandler = new ToggleFinishWallJoinsHandler();
                var clearArParamsHandler = new ClearArFinishParamsHandler();
                var createCheckViewsHandler = new CreateCheckViewsHandler();

                var syncEvent = ExternalEvent.Create(syncHandler);
                var applyEvent = ExternalEvent.Create(applyHandler);
                var pickEvent = ExternalEvent.Create(pickHandler);
                var focusEvent = ExternalEvent.Create(focusHandler);
                var updateValuesEvent = ExternalEvent.Create(updateValuesHandler);
                var autoJoinEvent = ExternalEvent.Create(autoJoinHandler);
                var alignColumnsEvent = ExternalEvent.Create(alignColumnsHandler);
                var toggleWallJoinsEvent = ExternalEvent.Create(toggleWallJoinsHandler);
                var clearArParamsEvent = ExternalEvent.Create(clearArParamsHandler);
                var createCheckViewsEvent = ExternalEvent.Create(createCheckViewsHandler);

                var win = new UI.MainWindow(
                    uiDoc,
                    syncHandler, syncEvent,
                    applyHandler, applyEvent,
                    pickHandler, pickEvent,
                    focusHandler, focusEvent,
                    updateValuesHandler, updateValuesEvent,
                    autoJoinHandler, autoJoinEvent,
                    alignColumnsHandler, alignColumnsEvent,
                    toggleWallJoinsHandler, toggleWallJoinsEvent,
                    clearArParamsHandler, clearArParamsEvent,
                    createCheckViewsHandler, createCheckViewsEvent);

                _openWindow = win;
                win.Closed += (_, __) =>
                {
                    _openWindow = null;
                    syncEvent.Dispose();
                    applyEvent.Dispose();
                    pickEvent.Dispose();
                    focusEvent.Dispose();
                    updateValuesEvent.Dispose();
                    autoJoinEvent.Dispose();
                    alignColumnsEvent.Dispose();
                    toggleWallJoinsEvent.Dispose();
                    clearArParamsEvent.Dispose();
                    createCheckViewsEvent.Dispose();
                };
                win.Show(); // modeless — 不阻斷 Revit 操作
                return Result.Succeeded;
#endif
            }
            catch (Exception ex)
            {
                message = $"執行失敗: {ex.Message}";
                Logger.Log($"錯誤: {ex.Message}\n{ex.StackTrace}");
                TaskDialog.Show("錯誤", $"房間裝修工具執行時發生錯誤:\n{ex.Message}\n\n詳細資訊:\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

#if REVIT2026
        private bool TryGetOptions(UIDocument uiDoc, out FinishSettings settings)
        {
            settings = FinishSettings.LoadFromFile() ?? new FinishSettings();
            var doc = uiDoc.Document;

            var wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(x => x.Kind == WallKind.Basic)
                .OrderBy(x => x.Name)
                .Select(x => new TypeOption { Id = x.Id, Name = x.Name })
                .ToList();

            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(x => x.Name)
                .Select(x => new TypeOption { Id = x.Id, Name = x.Name })
                .ToList();

            var ceilingTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .OrderBy(x => x.Name)
                .Select(x => new TypeOption { Id = x.Id, Name = x.Name })
                .ToList();

            var skirtingTypes = wallTypes.ToList();

            var allRoomIds = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .Where(id => doc.GetElement(id) is Room)
                .ToList();

            var selectedRoomIds = uiDoc.Selection.GetElementIds()
                .Where(id => doc.GetElement(id) is Room)
                .ToList();

            if (!allRoomIds.Any())
            {
                TaskDialog.Show("房間裝修", "目前模型中找不到任何房間，請先建立房間後再執行。");
                return false;
            }

            if (!wallTypes.Any() || !floorTypes.Any() || !ceilingTypes.Any())
            {
                TaskDialog.Show("房間裝修", "找不到可用的牆、樓板或天花板類型，請先確認專案中已載入相關類型。");
                return false;
            }

            using (var form = new WinForms.Form())
            {
                form.Text = "YD BIM Tools - 房間裝修 (2026)";
                form.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                form.StartPosition = WinForms.FormStartPosition.CenterScreen;
                form.ClientSize = new System.Drawing.Size(500, 430);
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                int labelX = 20, inputX = 170, top = 18, lineHeight = 34;

                WinForms.Label AddLabel(string text, int y)
                {
                    var label = new WinForms.Label { Left = labelX, Top = y + 4, Width = 135, Text = text };
                    form.Controls.Add(label);
                    return label;
                }

                var infoLabel = new WinForms.Label
                {
                    Left = 20,
                    Top = top,
                    Width = 450,
                    Height = 30,
                    Text = "2026 已改為精簡介面。若要只處理部分房間，請先在模型中選取房間。"
                };
                form.Controls.Add(infoLabel);
                top += 38;

                AddLabel("牆類型", top);
                var cmbWalls = new WinForms.ComboBox { Left = inputX, Top = top, Width = 290, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
                cmbWalls.Items.AddRange(wallTypes.Cast<object>().ToArray());
                form.Controls.Add(cmbWalls);
                top += lineHeight;

                AddLabel("樓板類型", top);
                var cmbFloors = new WinForms.ComboBox { Left = inputX, Top = top, Width = 290, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
                cmbFloors.Items.AddRange(floorTypes.Cast<object>().ToArray());
                form.Controls.Add(cmbFloors);
                top += lineHeight;

                AddLabel("天花板類型", top);
                var cmbCeilings = new WinForms.ComboBox { Left = inputX, Top = top, Width = 290, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
                cmbCeilings.Items.AddRange(ceilingTypes.Cast<object>().ToArray());
                form.Controls.Add(cmbCeilings);
                top += lineHeight;

                AddLabel("踢腳板類型", top);
                var cmbSkirtings = new WinForms.ComboBox { Left = inputX, Top = top, Width = 290, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
                cmbSkirtings.Items.AddRange(skirtingTypes.Cast<object>().ToArray());
                form.Controls.Add(cmbSkirtings);
                top += lineHeight;

                AddLabel("邊界模式", top);
                var cmbBoundary = new WinForms.ComboBox { Left = inputX, Top = top, Width = 290, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
                cmbBoundary.Items.AddRange(new object[] { "內裝修面", "中心線", "外裝修面" });
                form.Controls.Add(cmbBoundary);
                top += lineHeight;

                AddLabel("牆高(mm)", top);
                var numWallHeight = new WinForms.NumericUpDown { Left = inputX, Top = top, Width = 120, Minimum = 1000, Maximum = 10000, DecimalPlaces = 0, Value = (decimal)Math.Max(1000, settings.WallHeightMm) };
                form.Controls.Add(numWallHeight);

                AddLabel("天花高(mm)", top + 36);
                var numCeilingHeight = new WinForms.NumericUpDown { Left = inputX, Top = top + 36, Width = 120, Minimum = 1000, Maximum = 10000, DecimalPlaces = 0, Value = (decimal)Math.Max(1000, settings.CeilingHeightMm) };
                form.Controls.Add(numCeilingHeight);

                AddLabel("踢腳高(mm)", top + 72);
                var numSkirtingHeight = new WinForms.NumericUpDown { Left = inputX, Top = top + 72, Width = 120, Minimum = 0, Maximum = 1000, DecimalPlaces = 0, Value = (decimal)Math.Max(0, settings.SkirtingHeightMm) };
                form.Controls.Add(numSkirtingHeight);
                top += 112;

                var chkGenerateGeometry = new WinForms.CheckBox { Left = 20, Top = top, Width = 140, Text = "產生裝修幾何", Checked = settings.GenerateGeometry };
                var chkUpdateValues = new WinForms.CheckBox { Left = 170, Top = top, Width = 140, Text = "更新參數值", Checked = settings.UpdateValues };
                var chkAutoJoinWalls = new WinForms.CheckBox { Left = 320, Top = top, Width = 140, Text = "自動接合牆面", Checked = settings.AutoJoinWalls };
                form.Controls.Add(chkGenerateGeometry);
                form.Controls.Add(chkUpdateValues);
                form.Controls.Add(chkAutoJoinWalls);
                top += 32;

                var chkUseSelection = new WinForms.CheckBox
                {
                    Left = 20,
                    Top = top,
                    Width = 440,
                    Checked = selectedRoomIds.Any(),
                    Enabled = selectedRoomIds.Any(),
                    Text = selectedRoomIds.Any()
                        ? $"僅處理目前選取房間 ({selectedRoomIds.Count} 間)"
                        : $"目前未選取房間，將處理全部 {allRoomIds.Count} 間房間"
                };
                form.Controls.Add(chkUseSelection);

                var btnOk = new WinForms.Button { Text = "確定並開始", Left = 255, Top = 380, Width = 105, DialogResult = WinForms.DialogResult.OK };
                var btnCancel = new WinForms.Button { Text = "取消", Left = 370, Top = 380, Width = 80, DialogResult = WinForms.DialogResult.Cancel };
                form.Controls.Add(btnOk);
                form.Controls.Add(btnCancel);
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                void SetComboSelection(WinForms.ComboBox combo, List<TypeOption> options, ElementId targetId)
                {
                    long idValue = targetId != null ? RevitCompat.GetElementIdValue(targetId) : -1;
                    var index = options.FindIndex(x => RevitCompat.GetElementIdValue(x.Id) == idValue);
                    combo.SelectedIndex = index >= 0 ? index : 0;
                }

                SetComboSelection(cmbWalls, wallTypes, settings.SelectedWallTypeId);
                SetComboSelection(cmbFloors, floorTypes, settings.SelectedFloorTypeId);
                SetComboSelection(cmbCeilings, ceilingTypes, settings.SelectedCeilingTypeId);
                SetComboSelection(cmbSkirtings, skirtingTypes, settings.SelectedSkirtingTypeId);
                cmbBoundary.SelectedIndex = settings.BoundaryMode == FloorBoundaryMode.Centerline ? 1 : settings.BoundaryMode == FloorBoundaryMode.OuterFinish ? 2 : 0;

                if (form.ShowDialog() != WinForms.DialogResult.OK)
                    return false;

                if (!chkGenerateGeometry.Checked && !chkUpdateValues.Checked && !chkAutoJoinWalls.Checked)
                {
                    TaskDialog.Show("房間裝修", "請至少選擇一項操作後再執行。");
                    return false;
                }

                settings.GenerateGeometry = chkGenerateGeometry.Checked;
                settings.UpdateValues = chkUpdateValues.Checked;
                settings.SetValuesForGeometry = chkUpdateValues.Checked;
                settings.SetValuesForRooms = chkUpdateValues.Checked;
                settings.AutoJoinWalls = chkAutoJoinWalls.Checked;
                settings.SkipDoorsForSkirting = true;
                settings.SkipWindowsForSkirting = true;
                settings.SkipOpeningsForWalls = true;
                settings.SelectedWallTypeId = ((TypeOption)cmbWalls.SelectedItem)?.Id ?? ElementId.InvalidElementId;
                settings.SelectedFloorTypeId = ((TypeOption)cmbFloors.SelectedItem)?.Id ?? ElementId.InvalidElementId;
                settings.SelectedCeilingTypeId = ((TypeOption)cmbCeilings.SelectedItem)?.Id ?? ElementId.InvalidElementId;
                settings.SelectedSkirtingTypeId = ((TypeOption)cmbSkirtings.SelectedItem)?.Id ?? ElementId.InvalidElementId;
                settings.WallHeightMm = (double)numWallHeight.Value;
                settings.CeilingHeightMm = (double)numCeilingHeight.Value;
                settings.SkirtingHeightMm = (double)numSkirtingHeight.Value;
                settings.WallOffsetMm = Math.Max(0, settings.WallHeightMm - settings.CeilingHeightMm);
                settings.BoundaryMode = cmbBoundary.SelectedIndex == 1
                    ? FloorBoundaryMode.Centerline
                    : cmbBoundary.SelectedIndex == 2
                        ? FloorBoundaryMode.OuterFinish
                        : FloorBoundaryMode.InnerFinish;
                settings.TargetRoomIds = chkUseSelection.Checked && selectedRoomIds.Any() ? selectedRoomIds : allRoomIds;
                settings.RoomOverrides = new List<RoomFinishOverride>();

                try
                {
                    settings.SaveToFile();
                }
                catch
                {
                    // ignore settings persistence failures
                }

                return true;
            }
        }

        internal static Result ExecuteRoomFinish(UIDocument uiDoc, FinishSettings settings, ref string message)
        {
            if (uiDoc == null || settings == null)
            {
                message = "無效的輸入參數";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var errors = new StringBuilder();
            var operationCount = 0;
            var successCount = 0;

            using (var t = new Transaction(doc, "AR 房間裝修"))
            {
                try
                {
                    if (t.Start() != TransactionStatus.Started)
                    {
                        message = "無法啟動交易";
                        return Result.Failed;
                    }

                    var writer = new ValueWriter(uiDoc);

                    try
                    {
                        writer.EnsureSharedParameters();
                    }
                    catch (Exception ex)
                    {
                        errors.AppendLine($"建立共享參數失敗: {ex.Message}");
                    }

                    if (settings.GenerateGeometry)
                    {
                        operationCount++;
                        try
                        {
                            var generator = new GeometryGenerator(uiDoc);
                            // 生成前先刪除同房間的老粉刷元素，防止重複生成疊加
                            if (settings.TargetRoomIds?.Count > 0)
                                generator.DeleteExistingFinishesForRooms(settings.TargetRoomIds);
                            generator.GenerateForRooms(settings);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"產生房間裝修失敗: {ex.Message}");
                        }
                    }

                    if (settings.UpdateValues)
                    {
                        operationCount++;
                        try
                        {
                            writer.UpdateValues(settings);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"更新房間參數失敗: {ex.Message}");
                        }
                    }

                    if (settings.AutoJoinWalls)
                    {
                        operationCount++;
                        try
                        {
                            var generator = new GeometryGenerator(uiDoc);
                            var joinResults = generator.AutoJoinExistingWalls(settings.TargetRoomIds);
                            if (joinResults.SuccessCount > 0 || joinResults.TotalAttempts >= 0)
                                successCount++;

                            if (joinResults.Errors.Any())
                            {
                                errors.AppendLine("自動接合牆面時發生的錯誤:");
                                foreach (var error in joinResults.Errors)
                                    errors.AppendLine($"  - {error}");
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"自動接合牆面失敗: {ex.Message}");
                        }
                    }

                    if (operationCount == 0)
                    {
                        t.RollBack();
                        message = "未選擇任何操作";
                        return Result.Cancelled;
                    }

                    var commitStatus = t.Commit();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        message = $"交易提交失敗: {commitStatus}";
                        return Result.Failed;
                    }

                    var summary = new StringBuilder();
                    summary.AppendLine($"處理房間數：{settings.TargetRoomIds?.Count ?? 0}");
                    summary.AppendLine($"操作完成：{successCount}/{operationCount} 成功");

                    if (errors.Length > 0)
                    {
                        summary.AppendLine();
                        summary.AppendLine("錯誤詳情：");
                        summary.Append(errors.ToString());
                    }

                    TaskDialog.Show(successCount == operationCount ? "房間裝修完成" : "房間裝修部分完成", summary.ToString());
                    message = summary.ToString();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    try { t.RollBack(); } catch { }
                    message = $"交易執行失敗: {ex.Message}";
                    Logger.Log($"房間裝修 2026 執行失敗: {ex.Message}\n{ex.StackTrace}");
                    return Result.Failed;
                }
            }
        }
#endif
    }
}
