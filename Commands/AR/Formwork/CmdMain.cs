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
                
                // 避免重複開啟視窗
                if (_win != null && _win.IsVisible)
                {
                    _win.Activate();
                    return Result.Succeeded;
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
                var filter = new HostFilter(_vm.IncludeWall, _vm.IncludeColumn, _vm.IncludeBeam, _vm.IncludeSlab, _vm.IncludeStairs);
                var refs = _uidoc.Selection.PickObjects(
                    ObjectType.Element, filter, "�Цb�ҫ������ �� / �W / �� / �O");

                _vm.SetPicked(refs.Select(r => r.ElementId).ToList());
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex) { TaskDialog.Show("�������", ex.Message); }
        }

        public string GetName() => "YD_BIM_Tools.Pick";
    }

    // ---------- ���� ----------
    class RunHandler : IExternalEventHandler
    {
        private readonly UIDocument _uidoc;
        private readonly UiVm _vm;
        public RunHandler(UIDocument uidoc, UiVm vm) { _uidoc = uidoc; _vm = vm; }

        public void Execute(UIApplication app)
        {
            var doc = _uidoc.Document;
            try
            {
                _vm.RaiseRunStarted(0);
                SharedParams.Ensure(doc);

                var hosts = _vm.GetHostElements();
                _vm.RaiseRunStarted(hosts.Count);

                var sw = Stopwatch.StartNew();

                // 使用結構分析的正確邏輯作為主要方法
                FormworkEngine.Debug.Enable(true);
                FormworkEngine.BeginRun();

                using (var tg = new TransactionGroup(doc, "模板計算"))
                {
                    tg.Start();
                    using (var t = new Transaction(doc, "生成/更新模板"))
                    {
                        t.Start();

                        // 執行完整的結構分析（與結構分析傳統模式相同的邏輯）
                        var analysisResult = StructuralFormworkAnalyzer.AnalyzeProject(doc);
                        
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
                        foreach (var elementAnalysis in relevantAnalyses)
                        {
                            var element = elementAnalysis.Key;
                            var analysis = elementAnalysis.Value;

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
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ 生成元素 {element.Id} 模板失敗: {ex.Message}");
                                System.Diagnostics.Debug.WriteLine($"❌ 堆疊: {ex.StackTrace}");
                            }

                            i++;
                            _vm.RaiseProgress(i, relevantAnalyses.Count, sw.Elapsed);
                            
                            // 強制處理 UI 事件，讓進度視窗能更新
                            System.Windows.Forms.Application.DoEvents();
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"\n========== 總計生成 {totalFormworkCount} 個模板 ==========");

                        if (_vm.Isolate && _vm.DrawFormwork && all.Count > 0)
                            doc.ActiveView.IsolateElementsTemporary(all);

                        t.Commit();
                    }
                    tg.Assimilate();
                }

                // �� �����@��
                FormworkEngine.EndRun();                       // �� �[�o��

                sw.Stop();
                _vm.RaiseRunFinished();
                // 移除完成對話框 - 有效面積已正確產出在參數中
                System.Diagnostics.Debug.WriteLine("模板生成完成");
                System.Diagnostics.Debug.WriteLine(FormworkEngine.GetSummary());
            }
            catch (Exception ex)
            {
                _vm.RaiseRunFinished();
                TaskDialog.Show("模板生成 - 錯誤", ex.ToString());
            }
        }


        public string GetName() => "YD_BIM_Tools.Run";

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
                            true, vm.ThicknessMm, vm.BottomOffsetMm, vm.DrawFormwork);
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
                return ImprovedFormworkEngine.CreateFormworkFromElement(doc, element, 18.0);
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
                    if (face is PlanarFace planarFace && ShouldGenerateFormwork(planarFace, element))
                    {
                        var formworkId = FormworkEngine.BuildFromFaceAccurate(doc, element, planarFace, 18.0, null);
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
                else if (hostElement.Category?.Id.Value == (int)BuiltInCategory.OST_Stairs)
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
            return element.Category?.Id.Value == (int)BuiltInCategory.OST_StructuralColumns;
        }

        private bool IsStructuralFraming(Element element)
        {
            return element.Category?.Id.Value == (int)BuiltInCategory.OST_StructuralFraming;
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
                            
                            // 設定透明度以便查看結構
                            overrides.SetSurfaceTransparency(15); // 15% 透明度
                            
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

            var categoryId = element.Category.Id.Value;

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
            else if (categoryId == (long)BuiltInCategory.OST_Stairs)
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

        private bool ShouldGenerateFormwork(PlanarFace face, Element element)
        {
            try
            {
                // 簡化的判斷邏輯
                var normal = face.FaceNormal;
                var area = face.Area * 0.092903; // 轉換為平方米

                // 面積太小的面不生成模板
                if (area < 0.01) return false;

                // 可以添加更多判斷邏輯
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // ---------- ViewModel + UI ----------
    public class UiVm
    {
        internal readonly Document _doc;
        private readonly UIDocument _uidoc;

        // ���O
        public bool IncludeWall = true;
        public bool IncludeColumn = true;
        public bool IncludeBeam = true;
        public bool IncludeSlab = true;
        public bool IncludeStairs = true;

        // �ﶵ
        public bool DrawFormwork = true;
        public bool Isolate = true;
        public bool WriteExplanation = true;
        public bool ActiveViewOnly = false;
        public bool IncludeBottom = true;

        // �Ѽ�
        public double ThicknessMm = 20.0;
        public double BottomOffsetMm = 30.0;
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
        public event Action RunFinished;

        private IList<ElementId> _pickedHostIds = new List<ElementId>();

        public UiVm(Document doc, UIDocument uidoc) { _doc = doc; _uidoc = uidoc; }

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
                ids.AddRange(FE().OfCategory(BuiltInCategory.OST_Stairs).ToElementIds());

            return ids.Select(id => _doc.GetElement(id)).ToList();
        }

        internal void RaiseRunStarted(int total) => RunStarted?.Invoke(total);
        internal void RaiseProgress(int c, int t, TimeSpan e) => ProgressChanged?.Invoke(c, t, e);
        internal void RaiseRunFinished() => RunFinished?.Invoke();

        // --- 主視窗 ---
        public class UiMain : WpfWindow
        {
            private readonly UiVm _vm;
            private readonly ExternalEvent _pickEvt;
            private readonly ExternalEvent _runEvt;

            private WpfLabel _lblCount;
            private ProgressWindow _progressWindow;

            public UiMain(UiVm vm, ExternalEvent pickEvt, ExternalEvent runEvt)
            {
                _vm = vm; _pickEvt = pickEvt; _runEvt = runEvt;

                Title = "模板（Formwork）";
                Width = 480; Height = 780;
                WindowStyle = System.Windows.WindowStyle.ToolWindow;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei UI");
                FontSize = 12;
                var g = new WpfGrid 
                { 
                    Margin = new WpfThickness(15),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250))
                };
                Content = g;
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
                // 前4行自動高度（移除進度條區域）
                for (int i = 0; i < 4; ++i)
                    g.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                // 內容區域使用剩餘空間
                g.RowDefinitions.Add(new WpfRowDef { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                // 最後一行（按鈕行）固定高度
                g.RowDefinitions.Add(new WpfRowDef { Height = new System.Windows.GridLength(60) });

                // 選取模型
                var gbPick = new WpfGroupBox 
                { 
                    Header = "選取模型", 
                    Margin = new WpfThickness(0, 0, 0, 8),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    BorderThickness = new WpfThickness(1)
                };
                var pickRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new WpfThickness(8) };
                var btnPick = new WpfButton 
                { 
                    Content = "選取模型", 
                    Width = 110, 
                    Height = 32,
                    ToolTip = "在模型中選取要處理的結構元素",
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 248, 255)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 130, 180))
                };
                btnPick.Click += (s, e) => { try { _pickEvt.Raise(); } catch { } };
                _lblCount = new WpfLabel { Content = "已選取 0 個模型", Margin = new WpfThickness(12, 0, 0, 0), FontWeight = WpfFontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 130, 180)) };
                pickRow.Children.Add(btnPick);
                pickRow.Children.Add(_lblCount);
                gbPick.Content = pickRow;
                g.Children.Add(gbPick);
                WpfGrid.SetRow(gbPick, 0);

                _vm.SelectionChanged += n => Dispatcher.Invoke(() => _lblCount.Content = $"已選取 {n} 個模型");

                // 設定進度事件處理
                _vm.RunStarted += total => Dispatcher.Invoke(() =>
                {
                    if (_progressWindow != null)
                    {
                        _progressWindow.UpdateProgress(0, total, TimeSpan.Zero);
                    }
                });

                _vm.ProgressChanged += (curr, total, elapsed) => Dispatcher.Invoke(() =>
                {
                    if (_progressWindow != null)
                    {
                        _progressWindow.UpdateProgress(curr, total, elapsed);
                    }
                });

                _vm.RunFinished += () => Dispatcher.Invoke(() =>
                {
                    if (_progressWindow != null)
                    {
                        _progressWindow.Complete();
                    }
                    
                    // 進度完成後重新顯示主介面
                    System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ => Dispatcher.Invoke(() =>
                    {
                        this.Show();
                        this.WindowState = System.Windows.WindowState.Normal;
                        this.Activate();
                    }));
                });

                // 包含類別
                var gbCat = new WpfGroupBox 
                { 
                    Header = "包含類別", 
                    Margin = new WpfThickness(0, 0, 0, 8),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    BorderThickness = new WpfThickness(1)
                };
                var cat = new WpfStackPanel { Orientation = WpfOrientation.Vertical, Margin = new WpfThickness(8) };
                AddCheck(cat, "牆", v => _vm.IncludeWall = v, _vm.IncludeWall);
                AddCheck(cat, "結構柱", v => _vm.IncludeColumn = v, _vm.IncludeColumn);
                AddCheck(cat, "結構梁", v => _vm.IncludeBeam = v, _vm.IncludeBeam);
                AddCheck(cat, "樓板 / 地板", v => _vm.IncludeSlab = v, _vm.IncludeSlab);
                AddCheck(cat, "樓梯", v => _vm.IncludeStairs = v, _vm.IncludeStairs);
                gbCat.Content = cat;
                g.Children.Add(gbCat);
                WpfGrid.SetRow(gbCat, 1);

                // 處理選項
                var gbOpt = new WpfGroupBox 
                { 
                    Header = "處理選項", 
                    Margin = new WpfThickness(0, 0, 0, 8),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    BorderThickness = new WpfThickness(1)
                };
                var opt = new WpfStackPanel { Orientation = WpfOrientation.Vertical, Margin = new WpfThickness(8) };
                AddCheck(opt, "繪製模板", v => _vm.DrawFormwork = v, _vm.DrawFormwork);
                AddCheck(opt, "隔離模板（僅現時檢視）", v => _vm.Isolate = v, _vm.Isolate);
                AddCheck(opt, "寫入解說參數", v => _vm.WriteExplanation = v, _vm.WriteExplanation);
                AddCheck(opt, "僅目前視圖", v => _vm.ActiveViewOnly = v, _vm.ActiveViewOnly);
                AddCheck(opt, "包含底模", v => _vm.IncludeBottom = v, _vm.IncludeBottom);
                gbOpt.Content = opt;
                g.Children.Add(gbOpt);
                WpfGrid.SetRow(gbOpt, 2);

                // 參數設定
                var gbParam = new WpfGroupBox 
                { 
                    Header = "參數設定", 
                    Margin = new WpfThickness(0, 0, 0, 8),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    BorderThickness = new WpfThickness(1)
                };
                var pGrid = new WpfGrid { Margin = new WpfThickness(8) };
                pGrid.ColumnDefinitions.Add(new WpfColumnDef { Width = new System.Windows.GridLength(135) });
                pGrid.ColumnDefinitions.Add(new WpfColumnDef());
                var lb1 = new WpfLabel { Content = "模板厚度 (mm)：", VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var tbThk = new WpfTextBox { Text = _vm.ThicknessMm.ToString(CultureInfo.InvariantCulture) };
                var lb2 = new WpfLabel { Content = "底模下偏 (mm)：", VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var tbOff = new WpfTextBox { Text = _vm.BottomOffsetMm.ToString(CultureInfo.InvariantCulture) };
                pGrid.Children.Add(lb1); WpfGrid.SetRow(lb1, 0); WpfGrid.SetColumn(lb1, 0);
                pGrid.Children.Add(tbThk); WpfGrid.SetRow(tbThk, 0); WpfGrid.SetColumn(tbThk, 1);
                pGrid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                pGrid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                pGrid.Children.Add(lb2); WpfGrid.SetRow(lb2, 1); WpfGrid.SetColumn(lb2, 0);
                pGrid.Children.Add(tbOff); WpfGrid.SetRow(tbOff, 1); WpfGrid.SetColumn(tbOff, 1);
                gbParam.Content = pGrid;
                g.Children.Add(gbParam);
                WpfGrid.SetRow(gbParam, 3);

                // 模板材質設定（分類）
                var gbMat = new WpfGroupBox 
                { 
                    Header = "模板材質設定", 
                    Margin = new WpfThickness(0, 0, 0, 8),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    BorderThickness = new WpfThickness(1)
                };
                var matGrid = new WpfGrid { Margin = new WpfThickness(8) };
                matGrid.ColumnDefinitions.Add(new WpfColumnDef { Width = new System.Windows.GridLength(80) });
                matGrid.ColumnDefinitions.Add(new WpfColumnDef());
                
                var mats = new FilteredElementCollector(_vm._doc)
                    .OfClass(typeof(Material)).Cast<Material>().OrderBy(m => m.Name).ToList();
                var matItems = new List<ComboItem> { new ComboItem("＜不指定＞", ElementId.InvalidElementId) };
                foreach (var m in mats) matItems.Add(new ComboItem(m.Name, m.Id));

                // 牆模板材質
                var lblWall = new WpfLabel { Content = "牆：", VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var cmbWall = new WpfComboBox();
                foreach (var item in matItems) cmbWall.Items.Add(item);
                cmbWall.SelectedIndex = 0;
                cmbWall.SelectionChanged += (s, e) =>
                {
                    var item = cmbWall.SelectedItem as ComboItem;
                    _vm.WallMaterialId = item?.Id ?? ElementId.InvalidElementId;
                };

                // 柱模板材質
                var lblColumn = new WpfLabel { Content = "柱：", VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var cmbColumn = new WpfComboBox();
                foreach (var item in matItems) cmbColumn.Items.Add(item);
                cmbColumn.SelectedIndex = 0;
                cmbColumn.SelectionChanged += (s, e) =>
                {
                    var item = cmbColumn.SelectedItem as ComboItem;
                    _vm.ColumnMaterialId = item?.Id ?? ElementId.InvalidElementId;
                };

                // 梁模板材質
                var lblBeam = new WpfLabel { Content = "梁：", VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var cmbBeam = new WpfComboBox();
                foreach (var item in matItems) cmbBeam.Items.Add(item);
                cmbBeam.SelectedIndex = 0;
                cmbBeam.SelectionChanged += (s, e) =>
                {
                    var item = cmbBeam.SelectedItem as ComboItem;
                    _vm.BeamMaterialId = item?.Id ?? ElementId.InvalidElementId;
                };

                // 板模板材質
                var lblSlab = new WpfLabel { Content = "板：", VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var cmbSlab = new WpfComboBox();
                foreach (var item in matItems) cmbSlab.Items.Add(item);
                cmbSlab.SelectedIndex = 0;
                cmbSlab.SelectionChanged += (s, e) =>
                {
                    var item = cmbSlab.SelectedItem as ComboItem;
                    _vm.SlabMaterialId = item?.Id ?? ElementId.InvalidElementId;
                };

                // 佈局
                for (int i = 0; i < 4; i++)
                {
                    matGrid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                }

                matGrid.Children.Add(lblWall); WpfGrid.SetRow(lblWall, 0); WpfGrid.SetColumn(lblWall, 0);
                matGrid.Children.Add(cmbWall); WpfGrid.SetRow(cmbWall, 0); WpfGrid.SetColumn(cmbWall, 1);
                matGrid.Children.Add(lblColumn); WpfGrid.SetRow(lblColumn, 1); WpfGrid.SetColumn(lblColumn, 0);
                matGrid.Children.Add(cmbColumn); WpfGrid.SetRow(cmbColumn, 1); WpfGrid.SetColumn(cmbColumn, 1);
                matGrid.Children.Add(lblBeam); WpfGrid.SetRow(lblBeam, 2); WpfGrid.SetColumn(lblBeam, 0);
                matGrid.Children.Add(cmbBeam); WpfGrid.SetRow(cmbBeam, 2); WpfGrid.SetColumn(cmbBeam, 1);
                matGrid.Children.Add(lblSlab); WpfGrid.SetRow(lblSlab, 3); WpfGrid.SetColumn(lblSlab, 0);
                matGrid.Children.Add(cmbSlab); WpfGrid.SetRow(cmbSlab, 3); WpfGrid.SetColumn(cmbSlab, 1);

                gbMat.Content = matGrid;
                g.Children.Add(gbMat);
                WpfGrid.SetRow(gbMat, 4);

                // 進度條將在獨立視窗顯示，這裡不需要了

                // 操作列 - 固定在右下角
                var btnRow = new WpfStackPanel 
                { 
                    Orientation = WpfOrientation.Horizontal, 
                    HorizontalAlignment = WpfHorizontal.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                    Margin = new WpfThickness(0, 10, 10, 10) 
                };
                var btnRun = new WpfButton 
                { 
                    Content = "開始", 
                    Width = 120, 
                    Height = 36, 
                    Margin = new WpfThickness(0, 0, 12, 0), 
                    IsDefault = true,
                    FontSize = 14,
                    FontWeight = WpfFontWeights.Bold,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 139, 34)),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 100, 0))
                };
                var btnClose = new WpfButton 
                { 
                    Content = "關閉", 
                    Width = 120, 
                    Height = 36, 
                    IsCancel = true,
                    FontSize = 14,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160))
                };
                btnRun.Click += (s, e) =>
                {
                    double tmm = ParseOr(_vm.ThicknessMm, tbThk.Text, 20);
                    double off = ParseOr(_vm.BottomOffsetMm, tbOff.Text, 30);
                    _vm.ThicknessMm = Math.Max(0.1, tmm);
                    _vm.BottomOffsetMm = Math.Max(0.0, off);
                    
                    // 隱藏主介面，只顯示進度視窗
                    this.Hide();
                    ShowProgressWindow();
                    
                    try { _runEvt.Raise(); } catch { }
                };
                btnClose.Click += (s, e) => Close();
                btnRow.Children.Add(btnRun); btnRow.Children.Add(btnClose);
                g.Children.Add(btnRow);
                WpfGrid.SetRow(btnRow, 5); // 調整到最後一行
            }

            private void ShowProgressWindow()
            {
                if (_progressWindow != null)
                {
                    _progressWindow.Close();
                    _progressWindow = null;
                }

                _progressWindow = new ProgressWindow();
                _progressWindow.Owner = this;
                _progressWindow.Show();
            }

            private static double ParseOr(double def, string s, double fallback)
            {
                double v;
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : fallback;
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
            if (_c && e.Category != null && e.Category.Id.Value == (int)BuiltInCategory.OST_StructuralColumns) return true;
            if (_b && e.Category != null && e.Category.Id.Value == (int)BuiltInCategory.OST_StructuralFraming) return true;
            if (_s && e is Floor) return true;
            if (_st && e.Category != null && e.Category.Id.Value == (int)BuiltInCategory.OST_Stairs) return true;
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
        private WpfLabel _lblStatus;
        private WpfButton _btnCancel;
        private DateTime _startTime;

        public ProgressWindow()
        {
            InitializeWindow();
            _startTime = DateTime.Now;
        }

        private void InitializeWindow()
        {
            Title = "模板生成進度";
            Width = 400;
            Height = 200;
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
            _lblStatus = new WpfLabel 
            { 
                Content = "正在準備...", 
                FontWeight = WpfFontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 130, 180))
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
            _btnCancel.Click += (s, e) => Close();
            grid.Children.Add(_btnCancel);
            WpfGrid.SetRow(_btnCancel, 6);

            Content = grid;
        }

        public void UpdateProgress(int current, int total, TimeSpan elapsed)
        {
            // 使用 BeginInvoke 代替 Invoke，避免阻塞
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (total > 0)
                {
                    _progressBar.IsIndeterminate = false;
                    _progressBar.Value = (double)current / total * 100;
                    _lblProgress.Content = $"{current} / {total}";
                    _lblStatus.Content = $"正在處理第 {current} 個元素...";
                    
                    // 計算預計剩餘時間
                    if (current > 0 && elapsed.TotalSeconds > 0)
                    {
                        double avgTimePerItem = elapsed.TotalSeconds / current;
                        int remaining = total - current;
                        double estimatedRemainingSeconds = avgTimePerItem * remaining;
                        var estimatedRemaining = TimeSpan.FromSeconds(estimatedRemainingSeconds);
                        
                        var remainingString = estimatedRemaining.TotalHours >= 1 
                            ? estimatedRemaining.ToString(@"hh\:mm\:ss") 
                            : estimatedRemaining.ToString(@"mm\:ss");
                        
                        _lblStatus.Content = $"正在處理第 {current} 個元素... (預計剩餘: {remainingString})";
                    }
                }
                else
                {
                    _progressBar.IsIndeterminate = true;
                    _lblProgress.Content = "準備中...";
                    _lblStatus.Content = "正在初始化...";
                }

                var timeString = elapsed.TotalHours >= 1 
                    ? elapsed.ToString(@"hh\:mm\:ss") 
                    : elapsed.ToString(@"mm\:ss");
                _lblTime.Content = $"用時: {timeString}";
                
                // 強制更新 UI
                UpdateLayout();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void Complete()
        {
            Dispatcher.Invoke(() =>
            {
                _progressBar.Value = 100;
                _lblStatus.Content = "完成！";
                _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 139, 34));
                _btnCancel.Content = "關閉";
                
                // 3秒後自動關閉
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(3);
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    Close();
                };
                timer.Start();
            });
        }
    }
}
