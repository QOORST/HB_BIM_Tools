using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings
{
    /// <summary>
    /// AR 裝修工具 - 面生面
    /// 與面選模板邏輯相同，但參數寫入材料資訊供數量產出使用
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdFaceToFace : IExternalCommand
    {
        private Material _currentMaterial;
        private double _currentThickness;
        private HashSet<string> _selectedFaces = new HashSet<string>();
        private List<ElementId> _createdElementIds = new List<ElementId>(); // 記錄已創建的元素ID，用於持續高亮

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 檢查授權 - 裝修面生面功能
                var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
                if (!licenseManager.HasFeatureAccess("Finishings.FaceToFace"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援裝修面生面功能。\n\n" +
                        "請升級至試用版、標準版或專業版以使用此功能。\n\n" +
                        "點擊「授權管理」按鈕以查看或更新授權。");
                    return Result.Cancelled;
                }

                var uiapp = commandData.Application;
                var uidoc = uiapp.ActiveUIDocument;
                var doc = uidoc.Document;

                if (doc == null)
                {
                    TaskDialog.Show("錯誤", "無法取得有效的 Revit 文件");
                    return Result.Failed;
                }

                SharedParams.Ensure(doc); // 確保共用參數存在

                // 1) 小視窗：材料 + 厚度
                var dlg = new PickFacePalette(doc);
                dlg.Title = "AR 裝修 - 面生面"; // 修改標題以區分
                new System.Windows.Interop.WindowInteropHelper(dlg) { Owner = uiapp.MainWindowHandle };
                var ok = dlg.ShowDialog();
                if (ok != true) return Result.Cancelled;

                _currentMaterial = dlg.SelectedMaterial;
                _currentThickness = dlg.ThicknessMm;

                // 2) 連續點選平面（ESC 結束）
                var filter = new FaceOnHostFilter(allowFloor: true);
                int created = 0;
                double totalAreaM2 = 0; // 總面積統計

                using (var tg = new TransactionGroup(doc, "AR裝修-面生面"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "AR裝修-面生面"))
                    {
                        t.Start();

                        // 取得當前視圖，用於持續高亮顯示
                        var activeView = doc.ActiveView;

                        while (true)
                        {
                            Reference r;
                            try
                            {
                                var promptMsg = $"點選要生成裝修面的『面』（ESC 結束）\n" +
                                              $"✅ 已建立: {created} 個 | 📊 總面積: {totalAreaM2:F2} m²\n" +
                                              $"🎨 材料: {_currentMaterial?.Name ?? "預設"} | 📏 厚度: {_currentThickness}mm\n" +
                                              $"💡 提示：已選取的面會持續顯示綠色高亮";
                                r = uidoc.Selection.PickObject(ObjectType.Face, filter, promptMsg);
                            }
                            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                            {
                                break;
                            }

                            var host = doc.GetElement(r.ElementId);
                            var pf = host?.GetGeometryObjectFromReference(r) as PlanarFace;
                            if (pf == null)
                            {
                                TaskDialog.Show("面生面", "僅支援『平面(PlanarFace)』的面。");
                                continue;
                            }

                            // 檢查是否已選取過這個面
                            var faceKey = GetFaceKey(host, pf);
                            if (_selectedFaces.Contains(faceKey))
                            {
                                // 🎯 改進：只顯示紅色閃爍，不彈出對話框，保持連續點選
                                VisualFeedbackHelper.FlashElementWithPersistentHighlight(
                                    doc, uidoc, activeView, host.Id,
                                    new Color(255, 0, 0), // 紅色閃爍
                                    _createdElementIds,   // 已創建的元素保持綠色高亮
                                    new Color(0, 255, 0), // 綠色高亮
                                    flashDurationMs: 500);

                                Debug.WriteLine($"⚠️ 該面已經選取過，跳過 - 宿主: {host.Name} (ID: {host.Id})");
                                continue;
                            }

                            // 生成裝修面
                            try
                            {
                                ElementId id = CreateFinishingFace(doc, host, pf, _currentThickness, _currentMaterial);

                                if (id != ElementId.InvalidElementId)
                                {
                                    created++;
                                    _selectedFaces.Add(faceKey);
                                    _createdElementIds.Add(id); // 記錄已創建的元素ID

                                    // 計算面積
                                    var areaM2 = pf.Area * 0.09290304; // ft² → m²
                                    totalAreaM2 += areaM2;

                                    // 🎯 改進：綠色閃爍後持續顯示綠色高亮
                                    VisualFeedbackHelper.FlashElementWithPersistentHighlight(
                                        doc, uidoc, activeView, id,
                                        new Color(0, 255, 0), // 綠色閃爍
                                        _createdElementIds,   // 所有已創建的元素保持綠色高亮
                                        new Color(0, 255, 0), // 綠色高亮
                                        flashDurationMs: 300);

                                    Debug.WriteLine($"✅ 成功生成裝修面 ID: {id.Value}，面積: {areaM2:F2} m²");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"❌ 生成裝修面失敗: {ex.Message}");
                                TaskDialog.Show("錯誤", $"生成裝修面失敗: {ex.Message}");
                            }
                        }

                        t.Commit();
                    }

                    tg.Assimilate();
                }

                // 顯示完成訊息
                if (created > 0)
                {
                    TaskDialog.Show("AR裝修-面生面完成",
                        $"成功生成 {created} 個裝修面\n" +
                        $"總面積: {totalAreaM2:F2} m²\n" +
                        $"材料: {_currentMaterial?.Name ?? "預設"}\n" +
                        $"厚度: {_currentThickness} mm");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Debug.WriteLine($"❌ AR裝修-面生面執行失敗: {ex}");
                TaskDialog.Show("錯誤", $"執行失敗: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 生成裝修面（DirectShape）
        /// 與面選模板邏輯相同，但參數寫入材料資訊
        /// </summary>
        private ElementId CreateFinishingFace(Document doc, Element host, PlanarFace face, double thicknessMm, Material material)
        {
            try
            {
                Debug.WriteLine($"🎯 開始生成裝修面 - 宿主: {host.Name} (ID: {host.Id})");

                // 取得面的法向量和邊界
                var normal = face.FaceNormal;
                var curveLoops = face.GetEdgesAsCurveLoops();

                if (curveLoops == null || curveLoops.Count == 0)
                {
                    Debug.WriteLine("❌ 無法取得面的邊界");
                    return ElementId.InvalidElementId;
                }

                Debug.WriteLine($"✅ 取得 {curveLoops.Count} 個邊界曲線環");

                // 轉換厚度（mm → feet）
                double thicknessFt = thicknessMm / 304.8;

                // 創建擠出實體（向外擠出）
                var extrusionDir = normal;
                var formworkSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, extrusionDir, thicknessFt);

                if (formworkSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 擠出實體體積過小: {formworkSolid?.Volume ?? 0}");
                    return ElementId.InvalidElementId;
                }

                Debug.WriteLine($"✅ 擠出實體創建成功，體積: {formworkSolid.Volume}");

                // 🎯 關鍵改進 1：扣除相鄰元件（牆、樓板等）
                Debug.WriteLine("🔧 開始扣除相鄰元件");
                var exposedSolid = SubtractNearbyElements(doc, host, formworkSolid);

                if (exposedSolid == null || exposedSolid.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 扣除相鄰元件後體積過小或為空");
                    return ElementId.InvalidElementId;
                }

                Debug.WriteLine($"✅ 扣除相鄰元件完成，剩餘體積: {exposedSolid.Volume}");

                // 🎯 關鍵改進 2：將實體拆分成多個獨立的片段
                Debug.WriteLine("🔧 開始拆分實體為獨立片段");

                var splitSolids = SplitSolidIntoFragments(exposedSolid);
                Debug.WriteLine($"✅ 拆分完成，共 {splitSolids.Count} 個片段");

                // 為每個片段創建獨立的 DirectShape
                var createdIds = new List<ElementId>();
                int fragmentIndex = 1;

                foreach (var solidFragment in splitSolids)
                {
                    if (solidFragment?.Volume <= 1e-6) continue;

                    var directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    directShape.ApplicationId = "YD_BIM_Finishings";
                    directShape.ApplicationDataId = "FaceToFace_Fragment";
                    directShape.SetShape(new GeometryObject[] { solidFragment });
                    directShape.Name = $"裝修面_{host.Id}_{DateTime.Now:HHmmss}_片段{fragmentIndex}";

                    // 🎯 關鍵：設定材料參數（供數量產出使用）
                    // 只設定材料參數，不設定視圖覆蓋，讓 Revit 自動使用材料的原生外觀
                    if (material?.Id != null && material.Id != ElementId.InvalidElementId)
                    {
                        try
                        {
                            // 設定材料參數 - Revit 會自動根據材料設定顯示顏色、透明度等
                            var materialParam = directShape.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                            if (materialParam != null && !materialParam.IsReadOnly)
                            {
                                materialParam.Set(material.Id);
                                Debug.WriteLine($"  ✅ 片段 {fragmentIndex} 設定材料: {material.Name}，Revit 將自動使用材料的原生外觀");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"  ⚠️ 片段 {fragmentIndex} 設定材料失敗: {ex.Message}");
                        }
                    }

                    // 設定共用參數 - 宿主元素ID
                    var hostIdParam = directShape.LookupParameter(SharedParams.P_HostId);
                    if (hostIdParam != null && !hostIdParam.IsReadOnly)
                    {
                        hostIdParam.Set(host.Id.Value.ToString());
                    }

                    // 設定共用參數 - 厚度（mm）
                    var thicknessParam = directShape.LookupParameter(SharedParams.P_Thickness);
                    if (thicknessParam != null && !thicknessParam.IsReadOnly)
                    {
                        thicknessParam.Set(thicknessMm);
                    }

                    // 🎯 修正：計算每個片段的實際面積（而不是使用原始面的面積）
                    // 方法：體積 ÷ 厚度 = 面積
                    double fragmentAreaFt2 = solidFragment.Volume / thicknessFt; // ft²
                    double fragmentAreaM2 = fragmentAreaFt2 * 0.09290304; // ft² → m²

                    var areaParam = directShape.LookupParameter(SharedParams.P_Area);
                    if (areaParam != null && !areaParam.IsReadOnly)
                    {
                        areaParam.Set(fragmentAreaM2);
                        Debug.WriteLine($"  ✅ 片段 {fragmentIndex} 設定面積: {fragmentAreaM2:F4} m² (體積: {solidFragment.Volume:F6} ft³)");
                    }

                    // 🎯 新增：設定共用參數 - 材料名稱
                    var materialNameParam = directShape.LookupParameter(SharedParams.P_MaterialName);
                    if (materialNameParam != null && !materialNameParam.IsReadOnly && material != null)
                    {
                        materialNameParam.Set(material.Name ?? "");
                        Debug.WriteLine($"  ✅ 片段 {fragmentIndex} 設定材料名稱: {material.Name}");
                    }

                    createdIds.Add(directShape.Id);
                    fragmentIndex++;
                }

                Debug.WriteLine($"✅ 成功創建 {createdIds.Count} 個裝修面片段");

                if (createdIds.Count > 1)
                {
                    TaskDialog.Show("提示",
                        $"已生成 {createdIds.Count} 個裝修面片段\n" +
                        "您可以手動選擇並刪除不需要的片段");
                }

                return createdIds.Count > 0 ? createdIds[0] : ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CreateFinishingFace 失敗: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 扣除相鄰元件（牆、樓板等）
        /// </summary>
        private Solid SubtractNearbyElements(Document doc, Element host, Solid formworkSolid)
        {
            try
            {
                var exposedSolid = formworkSolid;

                // 取得宿主元素的包圍盒，擴大搜尋範圍
                var hostBB = host.get_BoundingBox(null);
                if (hostBB == null)
                {
                    Debug.WriteLine("  ⚠️ 無法取得宿主元素的包圍盒");
                    return formworkSolid;
                }

                // 擴大包圍盒（向外擴展 1 英尺）
                var expandedMin = hostBB.Min - new XYZ(1, 1, 1);
                var expandedMax = hostBB.Max + new XYZ(1, 1, 1);
                var outline = new Outline(expandedMin, expandedMax);

                // 建立過濾器：牆、樓板、柱、梁
                var filter = new ElementMulticategoryFilter(new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming
                });

                // 搜尋相鄰元件
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .WhereElementIsNotElementType();

                var nearbyElements = collector.ToList();
                Debug.WriteLine($"  🔍 找到 {nearbyElements.Count} 個相鄰元件");

                int subtractedCount = 0;

                foreach (var nearbyElement in nearbyElements)
                {
                    // 跳過宿主元素本身
                    if (nearbyElement.Id == host.Id) continue;

                    // 取得相鄰元件的實體
                    var nearbySolid = GetElementSolid(nearbyElement);
                    if (nearbySolid == null || nearbySolid.Volume <= 1e-6) continue;

                    try
                    {
                        // 布林扣除：從模板中扣除相鄰元件
                        var resultSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                            exposedSolid, nearbySolid, BooleanOperationsType.Difference);

                        if (resultSolid != null && resultSolid.Volume > 1e-6)
                        {
                            exposedSolid = resultSolid;
                            subtractedCount++;
                            Debug.WriteLine($"  ✅ 扣除元件 {nearbyElement.Id}，剩餘體積: {exposedSolid.Volume:F3}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 扣除元件 {nearbyElement.Id} 失敗: {ex.Message}");
                    }
                }

                Debug.WriteLine($"  ✅ 共扣除 {subtractedCount} 個相鄰元件");
                return exposedSolid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ❌ SubtractNearbyElements 失敗: {ex.Message}");
                return formworkSolid; // 失敗時返回原始實體
            }
        }

        /// <summary>
        /// 取得元素的實體幾何
        /// </summary>
        private Solid GetElementSolid(Element element)
        {
            try
            {
                var options = new Options
                {
                    ComputeReferences = false,
                    DetailLevel = ViewDetailLevel.Fine,
                    IncludeNonVisibleObjects = false
                };

                var geomElem = element.get_Geometry(options);
                if (geomElem == null) return null;

                foreach (var geomObj in geomElem)
                {
                    if (geomObj is Solid solid && solid.Volume > 1e-6)
                    {
                        return solid;
                    }
                    else if (geomObj is GeometryInstance geomInst)
                    {
                        var instGeom = geomInst.GetInstanceGeometry();
                        if (instGeom != null)
                        {
                            foreach (var instObj in instGeom)
                            {
                                if (instObj is Solid instSolid && instSolid.Volume > 1e-6)
                                {
                                    return instSolid;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠️ GetElementSolid 失敗: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 將實體拆分成多個獨立的片段
        /// </summary>
        private List<Solid> SplitSolidIntoFragments(Solid solid)
        {
            var fragments = new List<Solid>();

            if (solid?.Volume <= 1e-6) return fragments;

            try
            {
                var splitResult = SolidUtils.SplitVolumes(solid);

                if (splitResult != null && splitResult.Count > 0)
                {
                    Debug.WriteLine($"  ✅ SplitVolumes 成功，拆分為 {splitResult.Count} 個片段");
                    foreach (Solid fragment in splitResult)
                    {
                        if (fragment?.Volume > 1e-6)
                        {
                            fragments.Add(fragment);
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("  ⚠️ SplitVolumes 返回空，使用原始實體");
                    fragments.Add(solid);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠️ SplitVolumes 失敗: {ex.Message}，使用原始實體");
                fragments.Add(solid);
            }

            return fragments;
        }

        /// <summary>
        /// 生成面的唯一鍵值（用於檢查重複選取）
        /// </summary>
        private string GetFaceKey(Element host, PlanarFace face)
        {
            var origin = face.Origin;
            var normal = face.FaceNormal;
            return $"{host.Id}_{origin.X:F3}_{origin.Y:F3}_{origin.Z:F3}_{normal.X:F3}_{normal.Y:F3}_{normal.Z:F3}";
        }

        // 只允許牆/柱/梁/板
        private class FaceOnHostFilter : ISelectionFilter
        {
            private readonly bool _allowFloor;
            public FaceOnHostFilter(bool allowFloor) { _allowFloor = allowFloor; }

            public bool AllowElement(Element e)
            {
                if (e?.Category?.Id == null) return false;
#if REVIT2024 || REVIT2025 || REVIT2026
                long v = e.Category.Id.Value;
#else
                long v = e.Category.Id.IntegerValue;
#endif
                if (v == (long)BuiltInCategory.OST_Walls) return true;
                if (v == (long)BuiltInCategory.OST_StructuralColumns) return true;
                if (v == (long)BuiltInCategory.OST_StructuralFraming) return true;
                if (_allowFloor && v == (long)BuiltInCategory.OST_Floors) return true;
                return false;
            }
            public bool AllowReference(Reference r, XYZ p) => true;
        }

        // 材料 + 厚度的小視窗（中文 UI）
        private class PickFacePalette : System.Windows.Window
        {
            private readonly Document _doc;
            private readonly System.Windows.Controls.ComboBox _cmb;
            private readonly System.Windows.Controls.TextBox _tbThk;

            public Material SelectedMaterial { get; private set; }
            public double ThicknessMm { get; private set; } = 20.0;

            public PickFacePalette(Document doc)
            {
                _doc = doc;
                Title = "AR 裝修 - 面生面";
                Width = 380; Height = 160;
                WindowStyle = System.Windows.WindowStyle.ToolWindow;
                ResizeMode = System.Windows.ResizeMode.NoResize;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                var root = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(10) };
                Content = root;
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                // row1：材料
                var row1 = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row1.Children.Add(new System.Windows.Controls.Label { Content = "材料：", Width = 60, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                _cmb = new System.Windows.Controls.ComboBox { Width = 280, IsEditable = false };
                _cmb.Items.Add(new MatItem("＜不指定＞", ElementId.InvalidElementId));
                var mats = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().OrderBy(m => m.Name);
                foreach (var m in mats) _cmb.Items.Add(new MatItem(m.Name, m.Id));
                _cmb.SelectedIndex = 0;
                row1.Children.Add(_cmb);
                root.Children.Add(row1);
                System.Windows.Controls.Grid.SetRow(row1, 0);

                // row2：厚度
                var row2 = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row2.Children.Add(new System.Windows.Controls.Label { Content = "厚度 (mm)：", Width = 60, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                _tbThk = new System.Windows.Controls.TextBox { Width = 80, Text = "20" };
                row2.Children.Add(_tbThk);
                root.Children.Add(row2);
                System.Windows.Controls.Grid.SetRow(row2, 1);

                // row3：按鈕
                var row3 = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var ok = new System.Windows.Controls.Button { Content = "開始點選", Width = 100, Margin = new System.Windows.Thickness(0, 0, 8, 0), IsDefault = true };
                var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 80, IsCancel = true };

                ok.Click += (s, e) =>
                {
                    if (!double.TryParse(_tbThk.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mm) || mm <= 0)
                    {
                        System.Windows.MessageBox.Show("請輸入正確的厚度（mm）。", "面生面");
                        return;
                    }
                    ThicknessMm = mm;

                    var item = _cmb.SelectedItem as MatItem;
                    SelectedMaterial = (item != null && item.Id != ElementId.InvalidElementId)
                        ? _doc.GetElement(item.Id) as Material
                        : null;

                    DialogResult = true; Close();
                };
                cancel.Click += (s, e) => { DialogResult = false; Close(); };

                row3.Children.Add(ok); row3.Children.Add(cancel);
                root.Children.Add(row3);
                System.Windows.Controls.Grid.SetRow(row3, 2);
            }

            private class MatItem
            {
                public string Name; public ElementId Id;
                public MatItem(string n, ElementId id) { Name = n; Id = id; }
                public override string ToString() => Name;
            }
        }
    }

    /// <summary>
    /// 輕量級視覺反饋輔助類
    /// </summary>
    public static class VisualFeedbackHelper
    {
        /// <summary>
        /// 閃爍元素以提供即時視覺反饋
        /// </summary>
        public static void FlashElement(Document doc, UIDocument uidoc, ElementId elementId, Color color, int lineWeight = 3, int durationMs = 300)
        {
            try
            {
                var view = doc.ActiveView;
                if (view == null) return;

                var overrides = new OverrideGraphicSettings();
                overrides.SetProjectionLineColor(color);
                overrides.SetProjectionLineWeight(lineWeight);

                // 設定高亮
                view.SetElementOverrides(elementId, overrides);
                uidoc.RefreshActiveView();

                // 短暫延遲
                System.Threading.Thread.Sleep(durationMs);

                // 恢復
                view.SetElementOverrides(elementId, new OverrideGraphicSettings());
                uidoc.RefreshActiveView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ FlashElement 失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 閃爍元素並保持其他元素的持續高亮
        /// </summary>
        /// <param name="doc">文檔</param>
        /// <param name="uidoc">UI文檔</param>
        /// <param name="view">視圖</param>
        /// <param name="flashElementId">要閃爍的元素ID</param>
        /// <param name="flashColor">閃爍顏色</param>
        /// <param name="persistentElementIds">需要持續高亮的元素ID列表</param>
        /// <param name="persistentColor">持續高亮顏色</param>
        /// <param name="flashDurationMs">閃爍持續時間（毫秒）</param>
        public static void FlashElementWithPersistentHighlight(
            Document doc,
            UIDocument uidoc,
            View view,
            ElementId flashElementId,
            Color flashColor,
            List<ElementId> persistentElementIds,
            Color persistentColor,
            int flashDurationMs = 300)
        {
            try
            {
                if (view == null) return;

                // 1. 設定閃爍元素的高亮（強烈）
                var flashOverrides = new OverrideGraphicSettings();
                flashOverrides.SetProjectionLineColor(flashColor);
                flashOverrides.SetProjectionLineWeight(5); // 較粗的線條
                flashOverrides.SetSurfaceTransparency(30); // 半透明

                // 設定填充顏色（如果是 3D 視圖）
                if (view is View3D)
                {
                    var solidPatternId = GetSolidFillPatternId(doc);
                    if (solidPatternId != null && solidPatternId != ElementId.InvalidElementId)
                    {
                        flashOverrides.SetSurfaceForegroundPatternId(solidPatternId);
                        flashOverrides.SetSurfaceForegroundPatternColor(flashColor);
                        flashOverrides.SetSurfaceForegroundPatternVisible(true);
                    }
                }

                view.SetElementOverrides(flashElementId, flashOverrides);

                // 2. 設定所有已創建元素的持續高亮（柔和）
                var persistentOverrides = new OverrideGraphicSettings();
                persistentOverrides.SetProjectionLineColor(persistentColor);
                persistentOverrides.SetProjectionLineWeight(2); // 較細的線條
                persistentOverrides.SetSurfaceTransparency(60); // 更透明

                // 設定填充顏色（如果是 3D 視圖）
                if (view is View3D)
                {
                    var solidPatternId = GetSolidFillPatternId(doc);
                    if (solidPatternId != null && solidPatternId != ElementId.InvalidElementId)
                    {
                        persistentOverrides.SetSurfaceForegroundPatternId(solidPatternId);
                        persistentOverrides.SetSurfaceForegroundPatternColor(persistentColor);
                        persistentOverrides.SetSurfaceForegroundPatternVisible(true);
                    }
                }

                foreach (var id in persistentElementIds)
                {
                    if (id != flashElementId) // 不覆蓋閃爍元素
                    {
                        view.SetElementOverrides(id, persistentOverrides);
                    }
                }

                uidoc.RefreshActiveView();

                // 3. 短暫延遲（閃爍效果）
                System.Threading.Thread.Sleep(flashDurationMs);

                // 4. 恢復：將閃爍元素改為持續高亮
                view.SetElementOverrides(flashElementId, persistentOverrides);
                uidoc.RefreshActiveView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ FlashElementWithPersistentHighlight 失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得實心填充圖案ID
        /// </summary>
        private static ElementId GetSolidFillPatternId(Document doc)
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
                Debug.WriteLine($"⚠️ GetSolidFillPatternId 失敗: {ex.Message}");
            }

            return ElementId.InvalidElementId;
        }
    }
}

