using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager;
using YD_RevitTools.LicenseManager.Helpers;

// --- WPF alias ---
using WpfWindow = System.Windows.Window;
using WpfThickness = System.Windows.Thickness;
using WpfHorizontal = System.Windows.HorizontalAlignment;
using WpfPanel = System.Windows.Controls.Panel;
using WpfGrid = System.Windows.Controls.Grid;
using WpfRowDef = System.Windows.Controls.RowDefinition;
using WpfColumnDef = System.Windows.Controls.ColumnDefinition;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfLabel = System.Windows.Controls.Label;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfGroupBox = System.Windows.Controls.GroupBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfFontWeights = System.Windows.FontWeights;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    [Transaction(TransactionMode.Manual)]
    public class CmdMain : IExternalCommand
    {
        private static UiVm.UiMain _win;
        private static bool _sessionCleanupRegistered;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 授權檢查
                if (!LicenseHelper.CheckLicense("FormworkGeneration", "模板生成", LicenseType.Standard))
                {
                    return Result.Cancelled;
                }

                FormworkEngine.Debug.Enable(true);

                var doc = commandData.Application.ActiveUIDocument.Document;
                if (!_sessionCleanupRegistered)
                {
                    commandData.Application.Application.DocumentClosed += OnDocumentClosed;
                    _sessionCleanupRegistered = true;
                }
                
                // 避免重複開啟視窗
                if (_win != null)
                {
                    if (_win.IsRunPending)
                        return Result.Cancelled;
                    if (_win.SourceDocument.Equals(doc))
                    {
                        _win.Show();
                        _win.Activate();
                        return Result.Succeeded;
                    }
                    _win.Close();
                }

                var uiapp = commandData.Application;
                var uidoc = uiapp.ActiveUIDocument;
                var vm = new UiVm(uidoc.Document, uidoc);
                var pickEvt = ExternalEvent.Create(new PickHandler(uidoc, vm));
                var runEvt = ExternalEvent.Create(new RunHandler(uidoc, vm));

                _win = new UiVm.UiMain(vm, pickEvt, runEvt);

                // �j�b Revit �D�����W�A�קK�Q���I��
                var helper = new System.Windows.Interop.WindowInteropHelper(_win);
                helper.Owner = uiapp.MainWindowHandle;

                _win.Closed += (s, e) => _win = null;
                _win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                _win.Show();   // �D�ҺA

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void OnDocumentClosed(object sender, Autodesk.Revit.DB.Events.DocumentClosedEventArgs e)
        {
            UiVm.ClearClosedSessions();
            if (_win != null && !_win.SourceDocument.IsValidObject && !_win.IsRunPending)
                _win.Close();
        }
    }

    // ---------- ��� ----------
    class PickHandler : IExternalEventHandler
    {
        private readonly UIDocument _uidoc;
        private readonly UiVm _vm;
        public PickHandler(UIDocument uidoc, UiVm vm) { _uidoc = uidoc; _vm = vm; }

        public void Execute(UIApplication app)
        {
            try
            {
                if (!_vm.IsActiveDocument(app))
                {
                    TaskDialog.Show("模板生成", "請切回開啟此視窗的專案，或在目前專案重新開啟模板生成。");
                    return;
                }
                var filter = new HostFilter(_vm.IncludeWall, _vm.IncludeColumn, _vm.IncludeBeam, _vm.IncludeSlab, _vm.IncludeStairs);
                var refs = _uidoc.Selection.PickObjects(
                    ObjectType.Element, filter, "�Цb�ҫ������ �� / �W / �� / �O");

                _vm.SetPicked(refs.Select(r => r.ElementId).ToList());
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex) { TaskDialog.Show("�������", ex.Message); }
        }

        public string GetName() => "HB_BIM_Tools.Pick";
    }

    // ---------- ���� ----------
    class RunHandler : IExternalEventHandler
    {
        private readonly UIDocument _uidoc;
        private readonly UiVm _vm;
        private long? _currentHostId;
        public RunHandler(UIDocument uidoc, UiVm vm) { _uidoc = uidoc; _vm = vm; }

        public void Execute(UIApplication app)
        {
            if (!_vm.IsActiveDocument(app))
            {
                _vm.CancelQueuedRun();
                _vm.RaiseRunFinished(UiVm.FormworkRunOutcome.Failed,
                    "專案已切換或關閉，未執行模板生成。請在目前專案重新開啟模板生成。");
                return;
            }
            if (!_vm.TryStartRun()) return;

            var doc = _uidoc.Document;
            var totalTimer = Stopwatch.StartNew();
            var outcome = UiVm.FormworkRunOutcome.Failed;
            string outcomeDetail = "模板生成未完成。";
            string errorDialog = null;
            bool budgetRunStarted = false;
            bool engineRunStarted = false;
            bool rollbackUncertain = false;

            try
            {
                _currentHostId = null;
                FormworkEngine.Debug.Enable(true);
                CurvedMeshBudget.StartRun(stage => RunCheckpoint(stage, totalTimer.Elapsed));
                budgetRunStarted = true;

                _vm.RaiseRunStarted(0);
                var stageTimer = Stopwatch.StartNew();
                CurvedMeshBudget.Checkpoint("準備：解析宿主清單");
                var hosts = ExpandHostsForProcessing(doc, _vm.GetHostElements());
                LogPerformanceStage("解析宿主", stageTimer, $"宿主 {hosts.Count}");
                _vm.RaiseRunStarted(hosts.Count);
                CurvedMeshBudget.Checkpoint($"準備完成：{hosts.Count} 個宿主");

                // 使用結構分析的正確邏輯作為主要方法
                FormworkEngine.BeginRun();
                engineRunStarted = true;

                using (var tg = new TransactionGroup(doc, "模板計算"))
                {
                    if (tg.Start() != TransactionStatus.Started)
                        throw new InvalidOperationException("無法啟動模板計算交易群組。");

                    try
                    {
                        stageTimer.Restart();
                        CurvedMeshBudget.Checkpoint("準備：設定共用參數");
                        SharedParams.Ensure(doc);
                        CurvedMeshBudget.Checkpoint("準備：共用參數已確認");
                        LogPerformanceStage("共用參數", stageTimer);

                        using (var t = new Transaction(doc, "生成/更新模板"))
                        {
                            if (t.Start() != TransactionStatus.Started)
                                throw new InvalidOperationException("無法啟動模板生成交易。");
                            try
                            {
                                // 執行完整的結構分析（與結構分析傳統模式相同的邏輯）
                                var analysisOptions = new StructuralFormworkAnalyzer.AnalysisOptions
                                {
                                    IncludeStructuralBottom = _vm.IncludeStructuralBottom,
                                    IncludeFoundationBottom = _vm.IncludeFoundationBottom,
                                    ActiveViewOnly = _vm.ActiveViewOnly,
                                    ViewId = _vm.ActiveViewOnly ? doc.ActiveView.Id : ElementId.InvalidElementId,
                                    TargetElementIds = hosts.Select(h => h.Id).ToList(),
                                    CurrentElementIdChanged = id => _currentHostId = id?.GetIdValue()
                                };

                                stageTimer.Restart();
                                CurvedMeshBudget.Checkpoint("分析：收集並分析結構元素");
                                var analysisResult = StructuralFormworkAnalyzer.AnalyzeProject(doc, analysisOptions);
                                CurvedMeshBudget.ThrowIfExceeded();
                                LogPerformanceStage(
                                    "結構分析",
                                    stageTimer,
                                    $"要求 {analysisResult.Metrics.RequestedTargetCount} / 候選 {analysisResult.Metrics.CandidateElementCount} / 實際 {analysisResult.Metrics.AnalyzedElementCount}");

                                // 過濾只處理用戶選取的元素
                                var selectedIds = new HashSet<ElementId>(hosts.Select(h => h.Id));
                                var relevantAnalyses = analysisResult.ElementAnalyses
                                    .Where(kvp => selectedIds.Contains(kvp.Key.Id))
                                    .ToList();

                                _vm.RaiseRunStarted(relevantAnalyses.Count);

                                var all = new List<ElementId>();
                                int i = 0;

                                // 使用結構分析的生成邏輯
                                int totalFormworkCount = 0;
                                stageTimer.Restart();
                                foreach (var elementAnalysis in relevantAnalyses)
                                {
                                    var element = elementAnalysis.Key;
                                    var analysis = elementAnalysis.Value;
                                    _currentHostId = element.Id.GetIdValue();
                                    CurvedMeshBudget.Checkpoint($"生成：宿主 {i + 1}/{relevantAnalyses.Count}（ID {element.Id.GetIdValue()}）");

                                    try
                                    {
                                        System.Diagnostics.Debug.WriteLine($"\n========== 處理元素: {element.Id} ({element.Name}) ==========");

                                        var formworkIds = GenerateFormworkWithStructuralAnalysis(doc, element, analysis, _vm);

                                        System.Diagnostics.Debug.WriteLine($"✅ 生成了 {formworkIds.Count} 個模板");

                                        if (_vm.DrawFormwork && formworkIds.Count > 0)
                                        {
                                            all.AddRange(formworkIds);
                                            totalFormworkCount += formworkIds.Count;

                                            // 設定模板參數和材質
                                            System.Diagnostics.Debug.WriteLine($"📝 開始設定參數和材質...");
                                            SetFormworkParametersAndMaterials(doc, formworkIds, element, analysis, _vm);
                                            System.Diagnostics.Debug.WriteLine($"✅ 參數和材質設定完成");
                                        }
                                        else if (!_vm.DrawFormwork)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"⚠️ DrawFormwork 為 false，跳過模板生成");
                                        }
                                        else
                                        {
                                            System.Diagnostics.Debug.WriteLine($"⚠️ 未生成任何模板");
                                        }
                                    }
                                    catch (System.OperationCanceledException)
                                    {
                                        throw;
                                    }
                                    catch (CurvedMeshLimitException)
                                    {
                                        throw;
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"❌ 生成元素 {element.Id} 模板失敗: {ex.Message}");
                                        System.Diagnostics.Debug.WriteLine($"❌ 堆疊: {ex.StackTrace}");
                                    }

                                    CurvedMeshBudget.ThrowIfExceeded();
                                    i++;
                                    _vm.RaiseProgress(i, relevantAnalyses.Count, totalTimer.Elapsed);
                                    CurvedMeshBudget.Checkpoint($"生成：已完成 {i}/{relevantAnalyses.Count} 個宿主");
                                }

                                LogPerformanceStage("模板生成與參數", stageTimer, $"宿主 {relevantAnalyses.Count}、模板 {totalFormworkCount}");
                                System.Diagnostics.Debug.WriteLine($"\n========== 總計生成 {totalFormworkCount} 個模板 ==========");

                                if (_vm.Isolate && _vm.DrawFormwork && all.Count > 0)
                                    doc.ActiveView.IsolateElementsTemporary(all);

                                _currentHostId = null;
                                CurvedMeshBudget.ThrowIfExceeded();
                                CurvedMeshBudget.Checkpoint("提交：確認模板生成交易");
                                if (t.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("模板生成交易未成功提交。");
                            }
                            catch
                            {
                                if (!RollBackTransactionIfNeeded(t))
                                    rollbackUncertain = true;
                                throw;
                            }
                        }

                        CurvedMeshBudget.ThrowIfExceeded();
                        CurvedMeshBudget.Checkpoint("提交：確認整輪模板計算");
                        if (tg.Assimilate() != TransactionStatus.Committed)
                            throw new InvalidOperationException("模板計算交易群組未成功提交。");
                    }
                    catch
                    {
                        if (!RollBackGroupIfNeeded(tg))
                            rollbackUncertain = true;
                        throw;
                    }
                }

                outcome = UiVm.FormworkRunOutcome.Completed;
                outcomeDetail = "模板生成完成。";
                System.Diagnostics.Debug.WriteLine("模板生成完成");
                System.Diagnostics.Debug.WriteLine(FormworkEngine.GetSummary());
            }
            catch (System.OperationCanceledException ex)
            {
                outcome = UiVm.FormworkRunOutcome.Cancelled;
                outcomeDetail = string.IsNullOrWhiteSpace(ex.Message)
                    ? "已取消。" + RollbackMessage(rollbackUncertain)
                    : ex.Message + "。" + RollbackMessage(rollbackUncertain);
                FormworkEngine.Debug.Log("模板生成取消 - {0}", outcomeDetail);
            }
            catch (CurvedMeshLimitException ex)
            {
                outcomeDetail = "已停止以保護記憶體。" + RollbackMessage(rollbackUncertain);
                errorDialog = outcomeDetail + FormatHostContext() + "\n\n" + ex.Message;
                FormworkEngine.Debug.Log("模板生成資源限制 - {0}", ex.Message);
            }
            catch (Exception ex)
            {
                outcomeDetail = "模板生成失敗。" + RollbackMessage(rollbackUncertain);
                errorDialog = outcomeDetail + FormatHostContext() + "\n\n" + ex;
            }
            finally
            {
                if (engineRunStarted)
                {
                    try { FormworkEngine.EndRun(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"結束模板執行失敗: {ex.Message}"); }
                }
                else if (budgetRunStarted)
                {
                    GeometryExtractor.ClearGeometryCache();
                }

                if (budgetRunStarted)
                    CurvedMeshBudget.EndRun();

                totalTimer.Stop();
                FormworkEngine.Debug.Log("執行總計 - {0} ms（{1}）", totalTimer.ElapsedMilliseconds, outcome);
                _vm.RaiseRunFinished(outcome, outcomeDetail);
            }

            if (!string.IsNullOrWhiteSpace(errorDialog))
                TaskDialog.Show("模板生成 - 錯誤", errorDialog);
        }


        public string GetName() => "HB_BIM_Tools.Run";

        private static void LogPerformanceStage(string stage, Stopwatch timer, string detail = null)
        {
            timer.Stop();
            FormworkEngine.Debug.Log(
                "執行階段 - {0}: {1} ms{2}",
                stage,
                timer.ElapsedMilliseconds,
                string.IsNullOrWhiteSpace(detail) ? string.Empty : $"（{detail}）");
        }

        private void RunCheckpoint(string stage, TimeSpan elapsed)
        {
            _vm.ThrowIfCancellationRequested(stage);
            var displayStage = _currentHostId.HasValue
                ? $"宿主 {_currentHostId.Value}｜{stage}"
                : stage;
            _vm.RaiseRunStage(displayStage, elapsed);

            // Revit API 必須留在主執行緒；只在安全檢查點泵送 UI，不能硬中斷正在執行的原生 API。
            System.Windows.Forms.Application.DoEvents();
            _vm.ThrowIfCancellationRequested(stage);
        }

        private string FormatHostContext()
            => _currentHostId.HasValue ? $"\n目前宿主 ID：{_currentHostId.Value}" : string.Empty;

        private static string RollbackMessage(bool rollbackUncertain)
            => rollbackUncertain
                ? "未能確認所有模型變更均已回復，請立即檢查模型。"
                : "本輪模型變更已回復。";

        private static bool RollBackTransactionIfNeeded(Transaction transaction)
        {
            try
            {
                var status = transaction.GetStatus();
                if (status == TransactionStatus.Started)
                    return transaction.RollBack() == TransactionStatus.RolledBack;
                return status == TransactionStatus.RolledBack ||
                       status == TransactionStatus.Uninitialized ||
                       status == TransactionStatus.Committed;
            }
            catch
            {
                return false;
            }
        }

        private static bool RollBackGroupIfNeeded(TransactionGroup group)
        {
            try
            {
                var status = group.GetStatus();
                if (status == TransactionStatus.Started)
                    return group.RollBack() == TransactionStatus.RolledBack;
                return status == TransactionStatus.RolledBack || status == TransactionStatus.Uninitialized;
            }
            catch
            {
                return false;
            }
        }

        private static IList<Element> ExpandHostsForProcessing(Document doc, IList<Element> hosts)
        {
            var expanded = new List<Element>();

            foreach (var host in hosts ?? new List<Element>())
            {
                if (host == null) continue;

                if (ElementCategorizer.IsStairs(host))
                {
                    var parts = ElementCategorizer.GetStairPartElements(doc, host);
                    if (parts.Count > 0)
                    {
                        expanded.AddRange(parts);
                        continue;
                    }
                }

                expanded.Add(host);
            }

            return expanded
                .GroupBy(e => e.Id.GetIdValue())
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// 使用結構分析邏輯生成模板
        /// </summary>
        private List<ElementId> GenerateFormworkWithStructuralAnalysis(Document doc, Element element, ElementFormworkAnalysis analysis, UiVm vm)
        {
            var formworkIds = new List<ElementId>();

            try
            {
                // 使用與結構分析傳統模式相同的三層回退邏輯
                
                // 第一優先：改進的模板引擎（基於 Dynamo 邏輯）
                formworkIds = GenerateFormworkWithImprovedEngine(doc, element);

                // 如果改進引擎失敗，嘗試 Wall/Floor 引擎
                if (formworkIds.Count == 0)
                {
                    var wallFloorIds = GenerateFormworkWithWallFloor(doc, element);
                    formworkIds = wallFloorIds;

                    // 如果都失敗，最後回退到原始方法（結構分析的成功方法）
                    if (wallFloorIds.Count == 0)
                    {
                        var fallbackIds = FormworkEngine.BuildFormworkSolids(
                            doc, element, analysis.FormworkInfo, null, null,
                            ShouldIncludeBottom(element, vm), vm.ThicknessMm, vm.BottomOffsetMm, vm.DrawFormwork);
                        formworkIds = fallbackIds.ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成模板失敗: {ex.Message}");
            }

            return formworkIds;
        }

        /// <summary>
        /// 改進引擎生成模板
        /// </summary>
        private List<ElementId> GenerateFormworkWithImprovedEngine(Document doc, Element element)
        {
            try
            {
                if (!ShouldIncludeBottom(element, _vm))
                {
                    return new List<ElementId>();
                }

                return ImprovedFormworkEngine.CreateFormworkFromElement(doc, element, _vm.ThicknessMm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"改進引擎失敗: {ex.Message}");
                return new List<ElementId>();
            }
        }

        /// <summary>
        /// Wall/Floor 引擎生成模板
        /// </summary>
        private List<ElementId> GenerateFormworkWithWallFloor(Document doc, Element element)
        {
            var formworkIds = new List<ElementId>();
            try
            {
                var faces = GetElementFaces(element);
                foreach (var face in faces)
                {
                    if (face is PlanarFace planarFace && ShouldGenerateFormwork(planarFace, element, _vm))
                    {
                        var formworkId = FormworkEngine.BuildFromFaceAccurate(doc, element, planarFace, _vm.ThicknessMm, null);
                        if (formworkId != ElementId.InvalidElementId)
                        {
                            formworkIds.Add(formworkId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Wall/Floor 引擎失敗: {ex.Message}");
            }
            return formworkIds;
        }

        /// <summary>
        /// 設定模板參數和材質
        /// </summary>
        private void SetFormworkParametersAndMaterials(Document doc, List<ElementId> formworkIds, Element hostElement, ElementFormworkAnalysis analysis, UiVm vm)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"\n🔧 開始設定 {formworkIds.Count} 個模板的參數...");
                
                int successCount = 0;
                foreach (var formworkId in formworkIds)
                {
                    var formworkElement = doc.GetElement(formworkId);
                    if (formworkElement == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 無法取得模板元素: {formworkId}");
                        continue;
                    }

                    System.Diagnostics.Debug.WriteLine($"\n--- 處理模板 ID: {formworkId} ---");

                    // 1. 設定對應的材質（根據宿主類型）
                    System.Diagnostics.Debug.WriteLine($"1️⃣ 設定材質...");
                    var material = GetMaterialByElementType(hostElement, vm);
                    if (material != null)
                    {
                        SetElementMaterial(formworkElement, material);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ 未找到對應材質");
                    }

                    // 2. 計算並設定模板面積參數
                    System.Diagnostics.Debug.WriteLine($"2️⃣ 計算面積...");
                    var formworkArea = CalculateFormworkElementArea(formworkElement);
                    if (formworkArea > 0)
                    {
                        SetFormworkAreaParameter(formworkElement, formworkArea);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ 面積計算結果為 0");
                    }

                    // 3. 設定厚度參數
                    System.Diagnostics.Debug.WriteLine($"3️⃣ 設定厚度...");
                    SetThicknessParameter(formworkElement, vm.ThicknessMm);

                    // 4. 設定其他共用參數
                    System.Diagnostics.Debug.WriteLine($"4️⃣ 設定其他參數...");
                    SetAdditionalParameters(formworkElement, hostElement, analysis);

                    successCount++;
                }
                
                System.Diagnostics.Debug.WriteLine($"\n✅ 成功設定 {successCount}/{formworkIds.Count} 個模板的參數");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 設定參數整體失敗: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 堆疊: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 設定厚度參數
        /// </summary>
        private void SetThicknessParameter(Element formworkElement, double thicknessMm)
        {
            try
            {
                // 轉換為 Revit 內部單位 (英尺)
                double thicknessFt = thicknessMm / 304.8; // mm → ft
                
                var thicknessParam = formworkElement.LookupParameter("厚度");
                if (thicknessParam != null && !thicknessParam.IsReadOnly)
                {
                    thicknessParam.Set(thicknessFt);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定厚度: {thicknessMm:F1} mm ({thicknessFt:F6} ft)");
                }
                else
                {
                    // 嘗試其他可能的厚度參數名稱
                    var thicknessParam2 = formworkElement.get_Parameter(BuiltInParameter.GENERIC_THICKNESS);
                    if (thicknessParam2 != null && !thicknessParam2.IsReadOnly)
                    {
                        thicknessParam2.Set(thicknessFt);
                        System.Diagnostics.Debug.WriteLine($"✅ 設定內建厚度參數: {thicknessMm:F1} mm");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ 找不到可用的厚度參數");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 設定厚度失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 根據宿主元素類型獲取對應材質
        /// </summary>
        private Material GetMaterialByElementType(Element hostElement, UiVm vm)
        {
            try
            {
                ElementId materialId = ElementId.InvalidElementId;
                string elementTypeName = "未知";

                // 根據元素類型選擇材質
                if (hostElement is Wall)
                {
                    materialId = vm.WallMaterialId;
                    elementTypeName = "牆";
                }
                else if (IsStructuralColumn(hostElement))
                {
                    materialId = vm.ColumnMaterialId;
                    elementTypeName = "柱";
                }
                else if (IsStructuralFraming(hostElement))
                {
                    materialId = vm.BeamMaterialId;
                    elementTypeName = "梁";
                }
                else if (hostElement is Floor)
                {
                    materialId = vm.SlabMaterialId;
                    elementTypeName = "板";
                }
                else if (ElementCategorizer.IsStairs(hostElement))
                {
                    materialId = vm.MaterialId; // 樓梯使用預設材質
                    elementTypeName = "樓梯";
                }
                else
                {
                    materialId = vm.MaterialId; // 備用材質
                    elementTypeName = "其他";
                }

                System.Diagnostics.Debug.WriteLine($"📌 宿主類型: {elementTypeName}, 材質ID: {materialId}");

                if (materialId != null && materialId != ElementId.InvalidElementId)
                {
                    var material = _uidoc.Document.GetElement(materialId) as Material;
                    if (material != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ 找到材質: {material.Name}");
                        return material;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ 材質ID {materialId} 無效");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 未設定 {elementTypeName} 的材質");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 獲取材質失敗: {ex.Message}");
            }
            return null;
        }

        // 輔助方法
        private bool IsStructuralColumn(Element element)
        {
            return element.Category?.Id.GetIdValue() == (int)BuiltInCategory.OST_StructuralColumns;
        }

        private bool IsStructuralFraming(Element element)
        {
            return element.Category?.Id.GetIdValue() == (int)BuiltInCategory.OST_StructuralFraming;
        }

        private void SetElementMaterial(Element element, Material material)
        {
            try
            {
                if (material == null || element == null) 
                {
                    System.Diagnostics.Debug.WriteLine("❌ 材質或元素為空，跳過設定");
                    return;
                }

                var doc = element.Document;
                System.Diagnostics.Debug.WriteLine($"🎨 設定材質: {material.Name} (ID: {material.Id}) → 元素 {element.Id}");

                bool materialSet = false;

                // 方法 1: 設定元素的材質參數 (對 DirectShape 也有效)
                try
                {
                    var materialParam = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (materialParam != null && !materialParam.IsReadOnly)
                    {
                        materialParam.Set(material.Id);
                        materialSet = true;
                        System.Diagnostics.Debug.WriteLine($"✅ 成功設定 MATERIAL_ID_PARAM");
                    }
                }
                catch (Exception paramEx)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 設定材質參數失敗: {paramEx.Message}");
                }

                // 方法 2: DirectShape 特殊處理 - 使用 SetShape 時設定 GraphicsStyle
                if (element is DirectShape directShape)
                {
                    try
                    {
                        // DirectShape 需要通過視圖覆蓋來顯示材質顏色
                        var activeView = doc.ActiveView;
                        if (activeView != null && material.Color.IsValid)
                        {
                            var overrides = new OverrideGraphicSettings();
                            
                            // 設定填充圖樣為實心並使用材質顏色
                            var solidPattern = GetSolidFillPatternId(doc);
                            if (solidPattern != ElementId.InvalidElementId)
                            {
                                overrides.SetSurfaceForegroundPatternId(solidPattern);
                                overrides.SetCutForegroundPatternId(solidPattern);
                            }
                            
                            // 設定材質顏色
                            var color = material.Color;
                            overrides.SetSurfaceForegroundPatternColor(color);
                            overrides.SetSurfaceBackgroundPatternColor(color);
                            overrides.SetProjectionLineColor(color);
                            overrides.SetCutLineColor(color);
                            overrides.SetCutForegroundPatternColor(color);

                            // 不設定透明度，完整顯示材質顏色
                            overrides.SetSurfaceTransparency(0); // 0% 透明度（不透明）
                            
                            // 應用視圖覆蓋
                            activeView.SetElementOverrides(element.Id, overrides);
                            materialSet = true;
                            
                            System.Diagnostics.Debug.WriteLine($"✅ DirectShape 視圖覆蓋設定成功: RGB({color.Red},{color.Green},{color.Blue})");
                        }
                    }
                    catch (Exception overrideEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ 設定視圖覆蓋失敗: {overrideEx.Message}");
                    }
                }

                if (!materialSet)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 材質設定未成功應用");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 材質設定完成");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 設定材質整體失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 獲取實心填充圖樣ID
        /// </summary>
        private ElementId GetSolidFillPatternId(Document doc)
        {
            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement));

                foreach (FillPatternElement fpe in collector)
                {
                    if (fpe.GetFillPattern().IsSolidFill)
                    {
                        return fpe.Id;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"獲取實心填充圖樣失敗: {ex.Message}");
            }
            return ElementId.InvalidElementId;
        }

        private double CalculateFormworkElementArea(Element formworkElement)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"\n📐 計算模板面積: ID {formworkElement.Id}");
                
                if (!(formworkElement is DirectShape directShape))
                {
                    System.Diagnostics.Debug.WriteLine("❌ 非 DirectShape 元素");
                    return 0.0;
                }

                var geometry = formworkElement.get_Geometry(new Options 
                { 
                    DetailLevel = ViewDetailLevel.Fine,
                    ComputeReferences = false
                });

                if (geometry == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 無法取得幾何");
                    return 0.0;
                }

                double totalVolumeM3 = 0.0;
                int solidCount = 0;

                foreach (var geomObj in geometry)
                {
                    Solid solidToProcess = null;

                    if (geomObj is Solid solid && solid.Volume > 1e-6)
                    {
                        solidToProcess = solid;
                    }
                    else if (geomObj is GeometryInstance instance)
                    {
                        var instGeometry = instance.GetInstanceGeometry();
                        foreach (var instObj in instGeometry)
                        {
                            if (instObj is Solid instSolid && instSolid.Volume > 1e-6)
                            {
                                solidToProcess = instSolid;
                                break;
                            }
                        }
                    }

                    if (solidToProcess != null)
                    {
                        solidCount++;
                        // 體積轉換: ft³ → m³
                        double volumeM3 = solidToProcess.Volume * 0.0283168;
                        totalVolumeM3 += volumeM3;
                        
                        System.Diagnostics.Debug.WriteLine($"   Solid #{solidCount}: 體積 = {solidToProcess.Volume:F6} ft³ = {volumeM3:F6} m³");
                    }
                }

                if (totalVolumeM3 == 0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 未找到有效實體或體積為 0");
                    return 0.0;
                }

                // 使用 UI 設定的厚度
                double thicknessMm = _vm.ThicknessMm;
                double thicknessM = thicknessMm / 1000.0; // mm → m
                
                if (thicknessM <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 厚度無效: {thicknessMm} mm");
                    return 0.0;
                }

                // 面積 = 總體積 / 厚度
                double calculatedAreaM2 = totalVolumeM3 / thicknessM;
                
                System.Diagnostics.Debug.WriteLine($"\n📊 計算結果:");
                System.Diagnostics.Debug.WriteLine($"   總體積: {totalVolumeM3:F6} m³");
                System.Diagnostics.Debug.WriteLine($"   厚度: {thicknessMm:F1} mm = {thicknessM:F6} m");
                System.Diagnostics.Debug.WriteLine($"   計算面積: {calculatedAreaM2:F3} m²");
                
                return calculatedAreaM2;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算面積失敗: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 堆疊: {ex.StackTrace}");
                return 0.0;
            }
        }

        private void SetFormworkAreaParameter(Element formworkElement, double areaM2)
        {
            try
            {
                // 使用 AreaCalculator 轉換為 Revit 內部單位 (平方英尺)
                double areaFt2 = AreaCalculator.ConvertToSquareFeet(areaM2);

                // 設定模板總面積
                var totalParam = formworkElement.LookupParameter(SharedParams.P_Total);
                if (totalParam != null && !totalParam.IsReadOnly)
                {
                    totalParam.Set(areaFt2);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定模板合計面積: {areaM2:F3} m² ({areaFt2:F3} ft²)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 找不到參數: {SharedParams.P_Total}");
                }

                // 設定有效面積（與總面積相同）
                var effectiveAreaParam = formworkElement.LookupParameter(SharedParams.P_EffectiveArea);
                if (effectiveAreaParam != null && !effectiveAreaParam.IsReadOnly)
                {
                    effectiveAreaParam.Set(areaFt2);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定有效面積: {areaM2:F3} m² ({areaFt2:F3} ft²)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 找不到參數: {SharedParams.P_EffectiveArea}");
                }

                // 在名稱中記錄面積以便驗證
                if (areaM2 > 0)
                {
                    try
                    {
                        var currentName = formworkElement.Name ?? "模板";
                        var newName = $"{currentName}_面積{areaM2:F3}m²";
                        if (newName.Length <= 250) // Revit 名稱長度限制
                        {
                            formworkElement.Name = newName;
                            System.Diagnostics.Debug.WriteLine($"✅ 更新元素名稱: {newName}");
                        }
                    }
                    catch (Exception nameEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 更新名稱失敗: {nameEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 設定面積參數失敗: {ex.Message}");
            }
        }

        private void SetAdditionalParameters(Element formworkElement, Element hostElement, ElementFormworkAnalysis analysis)
        {
            try
            {
                // 設定宿主ID參數
                var hostIdParam = formworkElement.LookupParameter(SharedParams.P_HostId);
                if (hostIdParam != null && !hostIdParam.IsReadOnly)
                {
                    hostIdParam.Set(hostElement.Id.ToString());
                    System.Diagnostics.Debug.WriteLine($"設定宿主ID: {hostElement.Id}");
                }

                // 設定模板類型參數
                var categoryParam = formworkElement.LookupParameter(SharedParams.P_Category);
                if (categoryParam != null && !categoryParam.IsReadOnly)
                {
                    var categoryName = GetElementCategoryName(hostElement);
                    categoryParam.Set(categoryName);
                    System.Diagnostics.Debug.WriteLine($"設定模板類型: {categoryName}");
                }

                // 移除不需要的參數設定

                // 設定分析時間
                var timeParam = formworkElement.LookupParameter(SharedParams.P_AnalysisTime);
                if (timeParam != null && !timeParam.IsReadOnly)
                {
                    timeParam.Set(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定額外參數失敗: {ex.Message}");
            }
        }

        private string GetElementCategoryName(Element element)
        {
            if (element == null || element.Category == null)
                return "其他";

            var categoryId = element.Category.Id.GetIdValue();

            if (categoryId == (long)BuiltInCategory.OST_StructuralColumns)
                return "柱模板";
            else if (categoryId == (long)BuiltInCategory.OST_StructuralFraming)
                return "梁模板";
            else if (categoryId == (long)BuiltInCategory.OST_Floors)
                return "板模板";
            else if (categoryId == (long)BuiltInCategory.OST_Walls)
                return "牆模板";
            else if (categoryId == (long)BuiltInCategory.OST_StructuralFoundation)
                return "基礎模板";
            else if (ElementCategorizer.IsStairCategory(categoryId))
                return "樓梯模板";
            else
                return "其他";
        }

        private List<Face> GetElementFaces(Element element)
        {
            var faces = new List<Face>();
            try
            {
                var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
                if (geometry != null)
                {
                    foreach (var geomObj in geometry)
                    {
                        if (geomObj is Solid solid)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                faces.Add(face);
                            }
                        }
                        else if (geomObj is GeometryInstance instance)
                        {
                            var instGeom = instance.GetInstanceGeometry();
                            foreach (var instObj in instGeom)
                            {
                                if (instObj is Solid instSolid)
                                {
                                    foreach (Face face in instSolid.Faces)
                                    {
                                        faces.Add(face);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"獲取面失敗: {ex.Message}");
            }
            return faces;
        }

        private bool ShouldGenerateFormwork(PlanarFace face, Element element, UiVm vm)
        {
            try
            {
                // 簡化的判斷邏輯
                var normal = face.FaceNormal;
                var area = face.Area * 0.092903; // 轉換為平方米

                // 面積太小的面不生成模板
                if (area < 0.01) return false;

                if (ElementCategorizer.IsStructuralBeam(element) && normal.Z > 0.7)
                    return false;

                if (normal.Z < -0.7 && !ShouldIncludeBottom(element, vm))
                    return false;

                // 可以添加更多判斷邏輯
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldIncludeBottom(Element element, UiVm vm)
        {
            if (element?.Category?.Id == null)
                return vm.IncludeStructuralBottom;

            return element.Category.Id.GetIdValue() == (int)BuiltInCategory.OST_StructuralFoundation
                ? vm.IncludeFoundationBottom
                : vm.IncludeStructuralBottom;
        }
    }

    // ---------- ViewModel + UI ----------
    public class UiVm
    {
        public enum FormworkRunOutcome
        {
            Completed,
            Cancelled,
            Failed
        }

        internal readonly Document _doc;
        private readonly UIDocument _uidoc;
        private int _runState; // 0 = idle, 1 = queued, 2 = executing
        private int _cancellationRequested;

        // Keep open-document settings alive until DocumentClosed, even between tool windows.
        private static readonly Dictionary<Document, SessionSettings> Sessions
            = new Dictionary<Document, SessionSettings>();
        private readonly SessionSettings _settings;

        private sealed class SessionSettings
        {
            internal bool IncludeWall = true, IncludeColumn = true, IncludeBeam = true,
                IncludeSlab = true, IncludeStairs = true;
            internal bool DrawFormwork = true, Isolate = true, WriteExplanation = true,
                ActiveViewOnly = false, IncludeStructuralBottom = true, IncludeFoundationBottom = true;
            internal double ThicknessMm = 20.0, BottomOffsetMm = 30.0;
            internal long WallMaterialId = -1, ColumnMaterialId = -1, BeamMaterialId = -1, SlabMaterialId = -1;
        }

        // ���O
        public bool IncludeWall { get => _settings.IncludeWall; set => _settings.IncludeWall = value; }
        public bool IncludeColumn { get => _settings.IncludeColumn; set => _settings.IncludeColumn = value; }
        public bool IncludeBeam { get => _settings.IncludeBeam; set => _settings.IncludeBeam = value; }
        public bool IncludeSlab { get => _settings.IncludeSlab; set => _settings.IncludeSlab = value; }
        public bool IncludeStairs { get => _settings.IncludeStairs; set => _settings.IncludeStairs = value; }

        // �ﶵ
        public bool DrawFormwork { get => _settings.DrawFormwork; set => _settings.DrawFormwork = value; }
        public bool Isolate { get => _settings.Isolate; set => _settings.Isolate = value; }
        public bool WriteExplanation { get => _settings.WriteExplanation; set => _settings.WriteExplanation = value; }
        public bool ActiveViewOnly { get => _settings.ActiveViewOnly; set => _settings.ActiveViewOnly = value; }
        public bool IncludeStructuralBottom { get => _settings.IncludeStructuralBottom; set => _settings.IncludeStructuralBottom = value; }
        public bool IncludeFoundationBottom { get => _settings.IncludeFoundationBottom; set => _settings.IncludeFoundationBottom = value; }

        // �Ѽ�
        public double ThicknessMm { get => _settings.ThicknessMm; set => _settings.ThicknessMm = value; }
        public double BottomOffsetMm { get => _settings.BottomOffsetMm; set => _settings.BottomOffsetMm = value; }
        public ElementId MaterialId = ElementId.InvalidElementId;

        // 分類材質設定
        public ElementId WallMaterialId = ElementId.InvalidElementId;
        public ElementId ColumnMaterialId = ElementId.InvalidElementId;
        public ElementId BeamMaterialId = ElementId.InvalidElementId;
        public ElementId SlabMaterialId = ElementId.InvalidElementId;

        // �ƥ�
        public event Action<int> SelectionChanged;
        public event Action<int> RunStarted;
        public event Action<int, int, TimeSpan> ProgressChanged;
        public event Action<string, TimeSpan> RunStageChanged;
        public event Action<FormworkRunOutcome, string> RunFinished;

        private IList<ElementId> _pickedHostIds = new List<ElementId>();

        public UiVm(Document doc, UIDocument uidoc)
        {
            _doc = doc;
            _uidoc = uidoc;
            ClearClosedSessions();
            if (!Sessions.TryGetValue(doc, out var settings))
            {
                settings = new SessionSettings();
                Sessions.Add(doc, settings);
            }
            _settings = settings;
        }

        internal static void ClearClosedSessions()
        {
            foreach (var doc in Sessions.Keys.Where(doc => !doc.IsValidObject).ToList())
                Sessions.Remove(doc);
        }

        internal bool IsActiveDocument(UIApplication app)
            => _doc.IsValidObject && _doc.Equals(app.ActiveUIDocument?.Document);

        internal bool IsRunPending => System.Threading.Volatile.Read(ref _runState) != 0;

        public void SetPicked(IList<ElementId> ids)
        {
            _pickedHostIds = ids ?? new List<ElementId>();
            SelectionChanged?.Invoke(_pickedHostIds.Count);
        }

        public IList<Element> GetHostElements()
        {
            if (_pickedHostIds.Any())
                return _pickedHostIds.Select(id => _doc.GetElement(id)).ToList();

            var ids = new List<ElementId>();
            // 依是否僅現視圖，選擇不同的 collector
            Func<FilteredElementCollector> FE = () =>
                ActiveViewOnly ? new FilteredElementCollector(_doc, _doc.ActiveView.Id) : new FilteredElementCollector(_doc);

            if (IncludeWall)
                ids.AddRange(FE().OfClass(typeof(Wall)).ToElementIds());
            if (IncludeColumn)
                ids.AddRange(FE().OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_StructuralColumns).ToElementIds());
            if (IncludeBeam)
                ids.AddRange(FE().OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming).ToElementIds());
            if (IncludeSlab)
                ids.AddRange(FE().OfClass(typeof(Floor)).ToElementIds());
            if (IncludeStairs)
            {
                foreach (var category in ElementCategorizer.GetStructuralCategories().Where(c => ElementCategorizer.IsStairCategory((long)c)))
                    ids.AddRange(FE().OfCategory(category).ToElementIds());
            }

            return ids
                .GroupBy(id => id.GetIdValue())
                .Select(g => _doc.GetElement(g.First()))
                .Where(e => e != null)
                .ToList();
        }

        internal void RaiseRunStarted(int total) => RunStarted?.Invoke(total);
        internal void RaiseProgress(int c, int t, TimeSpan e) => ProgressChanged?.Invoke(c, t, e);
        internal void RaiseRunStage(string stage, TimeSpan elapsed) => RunStageChanged?.Invoke(stage, elapsed);

        internal bool TryQueueRun()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _runState, 1, 0) != 0)
                return false;
            System.Threading.Volatile.Write(ref _cancellationRequested, 0);
            return true;
        }

        internal bool TryStartRun()
            => System.Threading.Interlocked.CompareExchange(ref _runState, 2, 1) == 1;

        internal void CancelQueuedRun()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _runState, 0, 1) == 1)
                System.Threading.Volatile.Write(ref _cancellationRequested, 0);
        }

        internal void RequestCancellation()
        {
            if (System.Threading.Volatile.Read(ref _runState) != 0)
                System.Threading.Volatile.Write(ref _cancellationRequested, 1);
        }

        internal void ThrowIfCancellationRequested(string stage)
        {
            if (System.Threading.Volatile.Read(ref _cancellationRequested) != 0)
                throw new System.OperationCanceledException($"已在安全檢查點取消（{stage}）");
        }

        internal void RaiseRunFinished(FormworkRunOutcome outcome, string detail)
        {
            System.Threading.Volatile.Write(ref _cancellationRequested, 0);
            System.Threading.Volatile.Write(ref _runState, 0);
            RunFinished?.Invoke(outcome, detail);
        }

        // --- 主視窗 ---
        public class UiMain : WpfWindow
        {
            private readonly UiVm _vm;
            private readonly ExternalEvent _pickEvt;
            private readonly ExternalEvent _runEvt;

            private WpfLabel _lblCount;
            private ProgressWindow _progressWindow;

            internal Document SourceDocument => _vm._doc;
            internal bool IsRunPending => _vm.IsRunPending;

            public UiMain(UiVm vm, ExternalEvent pickEvt, ExternalEvent runEvt)
            {
                _vm = vm; _pickEvt = pickEvt; _runEvt = runEvt;
                Title = "模板生成";
                Width = 540; Height = 690;
                MinWidth = 460; MinHeight = 440;
                MaxHeight = System.Windows.SystemParameters.WorkArea.Height;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei UI");
                FontSize = 13;
                UseLayoutRounding = true;
                Background = Brush("#FFFFFF");
                Foreground = Brush("#20262E");

                var inputStyle = new System.Windows.Style(typeof(WpfTextBox));
                inputStyle.Setters.Add(new System.Windows.Setter(WpfTextBox.PaddingProperty, new WpfThickness(9, 6, 9, 6)));
                inputStyle.Setters.Add(new System.Windows.Setter(WpfTextBox.MinHeightProperty, 34.0));
                inputStyle.Setters.Add(new System.Windows.Setter(WpfTextBox.BorderBrushProperty, Brush("#C7CED6")));
                Resources.Add(typeof(WpfTextBox), inputStyle);
                var comboStyle = new System.Windows.Style(typeof(WpfComboBox));
                comboStyle.Setters.Add(new System.Windows.Setter(WpfComboBox.MinHeightProperty, 34.0));
                comboStyle.Setters.Add(new System.Windows.Setter(WpfComboBox.PaddingProperty, new WpfThickness(8, 5, 8, 5)));
                comboStyle.Setters.Add(new System.Windows.Setter(WpfComboBox.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
                Resources.Add(typeof(WpfComboBox), comboStyle);

                var root = new WpfGrid { Background = Brush("#FFFFFF") };
                root.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new WpfRowDef { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                root.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                Content = root;

                var header = new WpfStackPanel { Margin = new WpfThickness(24, 16, 24, 12) };
                header.Children.Add(new WpfTextBlock { Text = "模板生成", FontSize = 22, FontWeight = WpfFontWeights.SemiBold });
                header.Children.Add(new WpfTextBlock
                {
                    Text = _vm._doc.Title, ToolTip = _vm._doc.Title,
                    Foreground = Brush("#65717E"), Margin = new WpfThickness(0, 5, 0, 0),
                    TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
                });
                root.Children.Add(header);

                var body = new WpfStackPanel { Margin = new WpfThickness(24, 0, 24, 16) };
                var scroll = new System.Windows.Controls.ScrollViewer
                {
                    Content = body, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
                };
                root.Children.Add(scroll); WpfGrid.SetRow(scroll, 1);

                var pickRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 2) };
                pickRow.ColumnDefinitions.Add(new WpfColumnDef { Width = System.Windows.GridLength.Auto });
                pickRow.ColumnDefinitions.Add(new WpfColumnDef());
                var btnPick = ActionButton("選取模型", false);
                btnPick.ToolTip = "在目前專案選取要處理的元素";
                btnPick.Click += (s, e) => { try { _pickEvt.Raise(); } catch { } };
                _lblCount = new WpfLabel
                {
                    Content = "已選取 0 個模型", Foreground = Brush("#65717E"),
                    Margin = new WpfThickness(12, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                pickRow.Children.Add(btnPick);
                pickRow.Children.Add(_lblCount); WpfGrid.SetColumn(_lblCount, 1);
                body.Children.Add(pickRow);

                _vm.SelectionChanged += n => Dispatcher.Invoke(() => _lblCount.Content = $"已選取 {n} 個模型");
                _vm.RunStarted += total => Dispatcher.Invoke(() => _progressWindow?.UpdateProgress(0, total, TimeSpan.Zero));
                _vm.ProgressChanged += (curr, total, elapsed) => Dispatcher.Invoke(() => _progressWindow?.UpdateProgress(curr, total, elapsed));
                _vm.RunStageChanged += (stage, elapsed) => Dispatcher.Invoke(() => _progressWindow?.UpdateStage(stage, elapsed));
                _vm.RunFinished += (outcome, detail) => Dispatcher.Invoke(() =>
                {
                    _progressWindow?.Finish(outcome, detail);
                    Show();
                    WindowState = System.Windows.WindowState.Normal;
                    Activate();
                });

                var categories = new System.Windows.Controls.WrapPanel { ItemWidth = 90, ItemHeight = 28 };
                AddCheck(categories, "牆", v => _vm.IncludeWall = v, _vm.IncludeWall);
                AddCheck(categories, "結構柱", v => _vm.IncludeColumn = v, _vm.IncludeColumn);
                AddCheck(categories, "結構梁", v => _vm.IncludeBeam = v, _vm.IncludeBeam);
                AddCheck(categories, "樓板", v => _vm.IncludeSlab = v, _vm.IncludeSlab);
                AddCheck(categories, "樓梯", v => _vm.IncludeStairs = v, _vm.IncludeStairs);
                AddSection(body, "包含類別", categories);

                var options = new System.Windows.Controls.WrapPanel { ItemWidth = 224, ItemHeight = 30 };
                AddCheck(options, "繪製模板", v => _vm.DrawFormwork = v, _vm.DrawFormwork);
                AddCheck(options, "隔離模板", v => _vm.Isolate = v, _vm.Isolate);
                AddCheck(options, "寫入解說參數", v => _vm.WriteExplanation = v, _vm.WriteExplanation);
                AddCheck(options, "僅目前視圖", v => _vm.ActiveViewOnly = v, _vm.ActiveViewOnly);
                AddCheck(options, "結構產出底模", v => _vm.IncludeStructuralBottom = v, _vm.IncludeStructuralBottom);
                AddCheck(options, "基礎產出底模", v => _vm.IncludeFoundationBottom = v, _vm.IncludeFoundationBottom);
                AddSection(body, "處理選項", options);

                var parameters = TwoColumnGrid(1);
                var tbThk = new WpfTextBox { Text = _vm.ThicknessMm.ToString(CultureInfo.InvariantCulture) };
                var tbOff = new WpfTextBox { Text = _vm.BottomOffsetMm.ToString(CultureInfo.InvariantCulture) };
                tbThk.TextChanged += (s, e) => RememberNumber(tbThk.Text, 0.1, v => _vm.ThicknessMm = v);
                tbOff.TextChanged += (s, e) => RememberNumber(tbOff.Text, 0.0, v => _vm.BottomOffsetMm = v);
                AddField(parameters, "模板厚度 (mm)", tbThk, 0, 0);
                AddField(parameters, "底模下偏 (mm)", tbOff, 0, 1);
                AddSection(body, "尺寸", parameters);

                var materials = TwoColumnGrid(2);
                var mats = new FilteredElementCollector(_vm._doc)
                    .OfClass(typeof(Material)).Cast<Material>().OrderBy(m => m.Name).ToList();
                var matItems = new List<ComboItem> { new ComboItem("不指定", ElementId.InvalidElementId) };
                foreach (var m in mats) matItems.Add(new ComboItem(m.Name, m.Id));
                AddField(materials, "牆", MaterialCombo(matItems, _vm._settings.WallMaterialId, id =>
                {
                    _vm.WallMaterialId = id; _vm._settings.WallMaterialId = id.GetIdValue();
                }), 0, 0);
                AddField(materials, "柱", MaterialCombo(matItems, _vm._settings.ColumnMaterialId, id =>
                {
                    _vm.ColumnMaterialId = id; _vm._settings.ColumnMaterialId = id.GetIdValue();
                }), 0, 1);
                AddField(materials, "梁", MaterialCombo(matItems, _vm._settings.BeamMaterialId, id =>
                {
                    _vm.BeamMaterialId = id; _vm._settings.BeamMaterialId = id.GetIdValue();
                }), 1, 0);
                AddField(materials, "板", MaterialCombo(matItems, _vm._settings.SlabMaterialId, id =>
                {
                    _vm.SlabMaterialId = id; _vm._settings.SlabMaterialId = id.GetIdValue();
                }), 1, 1);
                AddSection(body, "模板材質", materials);

                var footer = new System.Windows.Controls.Border
                {
                    BorderBrush = Brush("#E3E7EB"), BorderThickness = new WpfThickness(0, 1, 0, 0),
                    Background = Brush("#F7F8FA"), Padding = new WpfThickness(24, 14, 24, 14)
                };
                var buttons = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontal.Right };
                var btnClose = ActionButton("關閉", false);
                btnClose.IsCancel = true;
                btnClose.Margin = new WpfThickness(0, 0, 10, 0);
                btnClose.Click += (s, e) => Close();
                var btnRun = ActionButton("開始生成", true);
                btnRun.IsDefault = true;
                btnRun.Click += (s, e) =>
                {
                    if (!_vm.TryQueueRun())
                    {
                        System.Windows.MessageBox.Show(this, "模板生成已在排程或執行中。", "模板生成",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        return;
                    }
                    _vm.ThicknessMm = Math.Max(0.1, ParseOr(_vm.ThicknessMm, tbThk.Text));
                    _vm.BottomOffsetMm = Math.Max(0.0, ParseOr(_vm.BottomOffsetMm, tbOff.Text));
                    tbThk.Text = _vm.ThicknessMm.ToString(CultureInfo.InvariantCulture);
                    tbOff.Text = _vm.BottomOffsetMm.ToString(CultureInfo.InvariantCulture);
                    Hide();
                    ShowProgressWindow();
                    try
                    {
                        if (_runEvt.Raise() != ExternalEventRequest.Accepted)
                        {
                            _vm.CancelQueuedRun();
                            _progressWindow?.Finish(FormworkRunOutcome.Failed, "Revit 未接受執行要求，請稍後再試。");
                            Show(); Activate();
                        }
                    }
                    catch (Exception ex)
                    {
                        _vm.CancelQueuedRun();
                        _progressWindow?.Finish(FormworkRunOutcome.Failed, "無法排程模板生成：" + ex.Message);
                        Show(); Activate();
                    }
                };
                buttons.Children.Add(btnClose); buttons.Children.Add(btnRun);
                footer.Child = buttons;
                root.Children.Add(footer); WpfGrid.SetRow(footer, 2);
            }

            private static System.Windows.Media.Brush Brush(string color)
                => (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(color);

            private static WpfButton ActionButton(string text, bool primary)
                => new WpfButton
                {
                    Content = text, MinWidth = 100, Height = 36, Padding = new WpfThickness(16, 0, 16, 0),
                    FontWeight = primary ? WpfFontWeights.SemiBold : WpfFontWeights.Normal,
                    Background = Brush(primary ? "#087F8C" : "#FFFFFF"),
                    Foreground = Brush(primary ? "#FFFFFF" : "#303B47"),
                    BorderBrush = Brush(primary ? "#087F8C" : "#C7CED6"),
                    BorderThickness = new WpfThickness(1)
                };

            private static void AddSection(WpfPanel panel, string title, System.Windows.UIElement content)
            {
                panel.Children.Add(new WpfTextBlock
                {
                    Text = title, FontSize = 13, FontWeight = WpfFontWeights.SemiBold,
                    Margin = new WpfThickness(0, 14, 0, 7), Foreground = Brush("#303B47")
                });
                panel.Children.Add(content);
            }

            private static WpfGrid TwoColumnGrid(int rows)
            {
                var grid = new WpfGrid();
                grid.ColumnDefinitions.Add(new WpfColumnDef());
                grid.ColumnDefinitions.Add(new WpfColumnDef());
                for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                return grid;
            }

            private static void AddField(WpfGrid grid, string label, System.Windows.FrameworkElement input, int row, int column)
            {
                var field = new WpfStackPanel { Margin = new WpfThickness(column == 0 ? 0 : 8, row > 0 ? 10 : 0, column == 0 ? 8 : 0, 0) };
                field.Children.Add(new WpfTextBlock { Text = label, Foreground = Brush("#65717E"), Margin = new WpfThickness(0, 0, 0, 5) });
                field.Children.Add(input);
                grid.Children.Add(field); WpfGrid.SetRow(field, row); WpfGrid.SetColumn(field, column);
            }

            private static WpfComboBox MaterialCombo(IList<ComboItem> items, long savedId, Action<ElementId> set)
            {
                var combo = new WpfComboBox { ItemsSource = items, MinWidth = 0 };
                // Material IDs are resolved against the current document's fresh material list.
                combo.SelectedItem = items.FirstOrDefault(item => item.Id.GetIdValue() == savedId) ?? items[0];
                set(((ComboItem)combo.SelectedItem).Id);
                combo.ToolTip = combo.SelectedItem.ToString();
                combo.SelectionChanged += (s, e) =>
                {
                    var item = combo.SelectedItem as ComboItem;
                    set(item?.Id ?? ElementId.InvalidElementId);
                    combo.ToolTip = item?.Name;
                };
                var template = new System.Windows.DataTemplate();
                var text = new System.Windows.FrameworkElementFactory(typeof(WpfTextBlock));
                text.SetBinding(WpfTextBlock.TextProperty, new System.Windows.Data.Binding());
                text.SetValue(WpfTextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
                template.VisualTree = text;
                combo.ItemTemplate = template;
                return combo;
            }
            private void ShowProgressWindow()
            {
                if (_progressWindow != null)
                {
                    _progressWindow.ForceClose();
                    _progressWindow = null;
                }

                _progressWindow = new ProgressWindow();
                _progressWindow.Owner = this;
                _progressWindow.CancelRequested += _vm.RequestCancellation;
                var shownWindow = _progressWindow;
                _progressWindow.Closed += (s, e) =>
                {
                    if (ReferenceEquals(_progressWindow, shownWindow))
                        _progressWindow = null;
                };
                _progressWindow.Show();
            }

            private static double ParseOr(double def, string s)
            {
                double v;
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)
                    && !double.IsNaN(v) && !double.IsInfinity(v) ? v : def;
            }

            private static void RememberNumber(string text, double minimum, Action<double> set)
            {
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
                    && !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum)
                    set(value);
            }

            private static void AddCheck(WpfPanel p, string text, Action<bool> set, bool init)
            {
                var cb = new WpfCheckBox { Content = text, IsChecked = init, Margin = new WpfThickness(0, 2, 0, 2) };
                cb.Checked += (s, e) => set(true);
                cb.Unchecked += (s, e) => set(false);
                p.Children.Add(cb);
            }

            private class ComboItem
            {
                public string Name; public ElementId Id;
                public ComboItem(string n, ElementId i) { Name = n; Id = i; }
                public override string ToString() => Name;
            }
        }
    }

    // ---------- 宿主過濾器 ----------
    class HostFilter : ISelectionFilter
    {
        private readonly bool _w, _c, _b, _s;
        private readonly bool _st;
        
        public HostFilter(bool walls, bool cols, bool beams, bool slabs, bool stairs = true)
        { _w = walls; _c = cols; _b = beams; _s = slabs; _st = stairs; }

        public bool AllowElement(Element e)
        {
            if (_w && e is Wall) return true;
            if (_c && e.Category != null && e.Category.Id.GetIdValue() == (int)BuiltInCategory.OST_StructuralColumns) return true;
            if (_b && e.Category != null && e.Category.Id.GetIdValue() == (int)BuiltInCategory.OST_StructuralFraming) return true;
            if (_s && e is Floor) return true;
            if (_st && ElementCategorizer.IsStairs(e)) return true;
            return false;
        }

        public bool AllowReference(Reference r, XYZ p) => true;
    }

    // ---------- 獨立進度視窗 ----------
    public class ProgressWindow : WpfWindow
    {
        private WpfProgressBar _progressBar;
        private WpfLabel _lblProgress;
        private WpfLabel _lblTime;
        private WpfTextBlock _lblStatus;
        private WpfButton _btnCancel;
        private DateTime _startTime;
        private bool _cancelRequested;
        private bool _finished;

        public event Action CancelRequested;

        public ProgressWindow()
        {
            InitializeWindow();
            _startTime = DateTime.Now;
            Closing += OnClosing;
        }

        private void InitializeWindow()
        {
            Title = "模板生成進度";
            Width = 480;
            Height = 320;
            MaxHeight = System.Windows.SystemParameters.WorkArea.Height - 40;
            WindowStyle = System.Windows.WindowStyle.ToolWindow;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            ResizeMode = System.Windows.ResizeMode.NoResize;
            FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei UI");
            FontSize = 12;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));

            var grid = new WpfGrid { Margin = new WpfThickness(20) };
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new WpfRowDef { Height = new System.Windows.GridLength(20) });
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new WpfRowDef { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });

            // 狀態標籤
            _lblStatus = new WpfTextBlock
            { 
                Text = "正在準備...",
                FontWeight = WpfFontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 130, 180)),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                MaxHeight = 72,
                Margin = new WpfThickness(5, 4, 5, 8),
                ToolTip = "Revit 原生 API 的單次運算無法強制中斷；取消會在下一個安全檢查點生效。"
            };
            grid.Children.Add(_lblStatus);
            WpfGrid.SetRow(_lblStatus, 0);

            // 進度條
            _progressBar = new WpfProgressBar 
            { 
                Height = 24, 
                Minimum = 0, 
                Maximum = 100,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 230, 230)),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 139, 34))
            };
            grid.Children.Add(_progressBar);
            WpfGrid.SetRow(_progressBar, 2);

            // 進度文字
            _lblProgress = new WpfLabel 
            { 
                Content = "0 / 0", 
                HorizontalAlignment = WpfHorizontal.Center,
                Margin = new WpfThickness(0, 5, 0, 0)
            };
            grid.Children.Add(_lblProgress);
            WpfGrid.SetRow(_lblProgress, 3);

            // 時間標籤
            _lblTime = new WpfLabel 
            { 
                Content = "用時: 00:00", 
                HorizontalAlignment = WpfHorizontal.Center,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100))
            };
            grid.Children.Add(_lblTime);
            WpfGrid.SetRow(_lblTime, 4);

            // 取消按鈕
            _btnCancel = new WpfButton 
            { 
                Content = "取消", 
                Width = 80, 
                Height = 30,
                HorizontalAlignment = WpfHorizontal.Center,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160))
            };
            _btnCancel.Click += (s, e) =>
            {
                if (_finished) Close();
                else RequestCancellation();
            };
            grid.Children.Add(_btnCancel);
            WpfGrid.SetRow(_btnCancel, 6);

            Content = grid;
        }

        public void UpdateProgress(int current, int total, TimeSpan elapsed)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateProgress(current, total, elapsed)));
                return;
            }

            if (total > 0)
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.Value = (double)current / total * 100;
                _lblProgress.Content = $"{current} / {total}";
            }
            else
            {
                _progressBar.IsIndeterminate = true;
                _lblProgress.Content = "準備中...";
            }

            UpdateElapsed(elapsed);
            if (!_cancelRequested && !_finished)
                _lblStatus.Text = total > 0 ? $"正在處理第 {current} 個元素..." : "正在初始化...";
        }

        public void UpdateStage(string stage, TimeSpan elapsed)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateStage(stage, elapsed)));
                return;
            }

            UpdateElapsed(elapsed);
            if (!_cancelRequested && !_finished)
                _lblStatus.Text = string.IsNullOrWhiteSpace(stage) ? "正在處理..." : stage;
        }

        internal void Finish(UiVm.FormworkRunOutcome outcome, string detail)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Finish(outcome, detail)));
                return;
            }

            _finished = true;
            _progressBar.IsIndeterminate = false;
            _lblStatus.Text = detail;
            _btnCancel.IsEnabled = true;
            _btnCancel.Content = "關閉";

            if (outcome == UiVm.FormworkRunOutcome.Completed)
            {
                _progressBar.Value = 100;
                _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 139, 34));
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    Close();
                };
                timer.Start();
            }
            else if (outcome == UiVm.FormworkRunOutcome.Cancelled)
            {
                _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 134, 11));
            }
            else
            {
                _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(178, 34, 34));
            }
        }

        internal void ForceClose()
        {
            _finished = true;
            Close();
        }

        private void RequestCancellation()
        {
            if (_cancelRequested || _finished) return;
            _cancelRequested = true;
            _btnCancel.IsEnabled = false;
            _btnCancel.Content = "取消中...";
            _lblStatus.Text = "已要求取消；正在等待安全檢查點...";
            _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 134, 11));
            CancelRequested?.Invoke();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_finished) return;
            e.Cancel = true;
            RequestCancellation();
        }

        private void UpdateElapsed(TimeSpan elapsed)
        {
            var timeString = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"hh\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
            _lblTime.Content = $"用時: {timeString}";
        }
    }
}
