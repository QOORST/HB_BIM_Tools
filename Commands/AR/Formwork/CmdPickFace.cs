using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows.Threading;
using YD_RevitTools.LicenseManager;

// WPF 別名（避免與 Autodesk.Revit.UI.* 名稱衝突）
using WpfWindow = System.Windows.Window;
using WpfGrid = System.Windows.Controls.Grid;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfLabel = System.Windows.Controls.Label;
using WpfButton = System.Windows.Controls.Button;
using WpfThickness = System.Windows.Thickness;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    [Transaction(TransactionMode.Manual)]
    public class CmdPickFace : IExternalCommand
    {
        private HashSet<string> _selectedFaces = new HashSet<string>(); // 已選取的面
        private List<ElementId> _previewElements = new List<ElementId>(); // 預覽元素
        private List<FormworkItem> _createdFormworkItems = new List<FormworkItem>(); // 已建立的模板項目
        private Material _currentMaterial;
        private double _currentThickness;

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet set)
        {
            var uiapp = data.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            try
            {
                // 授權檢查
                if (!LicenseHelper.CheckLicense("FaceFormwork", "面生面", LicenseType.Standard))
                {
                    return Result.Cancelled;
                }

                SharedParams.Ensure(doc); // 確保共用參數存在

                // 1) 小視窗：材料 + 厚度
                var dlg = new PickFacePalette(doc);
                new System.Windows.Interop.WindowInteropHelper(dlg) { Owner = uiapp.MainWindowHandle };
                var ok = dlg.ShowDialog();
                if (ok != true) return Result.Cancelled;

                _currentMaterial = dlg.SelectedMaterial;
                _currentThickness = dlg.ThicknessMm;

                // 2) 連續點選平面（ESC 結束）
                var filter = new FaceOnHostFilter(allowFloor: true);
                int created = 0;
                double totalAreaM2 = 0; // 總面積統計

                using (var tg = new TransactionGroup(doc, "面生面"))
                {
                    tg.Start();
                    FormworkEngine.BeginRun();

                    // 將 Transaction 移到迴圈外，提升效能並整合為單一操作
                    using (var t = new Transaction(doc, "面生面"))
                    {
                        t.Start();
                        while (true)
                        {
                            Reference r;
                            try
                            {
                                // 提供更詳細的提示訊息
                                var promptMsg = $"點選要生成模板的『面』（ESC 結束）\n" +
                                              $"已建立: {created} 個 | 總面積: {totalAreaM2:F2} m² | 材料: {_currentMaterial?.Name ?? "預設"} | 厚度: {_currentThickness}mm";
                                r = uidoc.Selection.PickObject(ObjectType.Face, filter, promptMsg);
                            }
                            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                            {
                                // 使用者按下 ESC，結束迴圈（不需要清理預覽，因為已改用即時生成）
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
                                // 提供視覺反饋，顯示該面已選取（紅色閃爍，持續 500ms 確保用戶看到）
                                VisualFeedbackHelper.FlashElement(doc, uidoc, host.Id, new Color(255, 0, 0), 4, 500);
                                Debug.WriteLine("⚠️ 該面已經選取過，跳過");
                                continue;
                            }

                            // 只生成用戶點選的面的模板（不使用傳統架構）
                            try
                            {
                                ElementId id = ElementId.InvalidElementId;

                                // 直接為選中的面生成模板
                                id = CreateSingleFaceFormworkDirectly(doc, host, pf, _currentThickness, _currentMaterial);
                                
                                if (id != ElementId.InvalidElementId)
                                {
                                    Debug.WriteLine($"成功為選中面生成模板 ID: {id.Value}");
                                }
                                else
                                {
                                    Debug.WriteLine("單面模板生成失敗");
                                }

                                if (id != ElementId.InvalidElementId) 
                                {
                                    created++;
                                    _selectedFaces.Add(GetFaceKey(host, pf)); // 記錄已選取的面
                                    
                                    // 計算並設定模板面積
                                    var element = doc.GetElement(id);
                                    double areaM2 = 0;
                                    if (element is DirectShape ds)
                                    {
                                        Debug.WriteLine($"🔍 開始處理DirectShape元素 ID: {id.Value}");
                                        
                                        // 先計算面積（包含詳細除錯）
                                        areaM2 = CalculateFormworkAreaWithDebug(doc, ds, pf);
                                        Debug.WriteLine($"📏 計算得到面積: {areaM2:F6} m²");
                                        
                                        // 設定材質和顏色
                                        SetElementMaterialAndColor(doc, ds, _currentMaterial);
                                        
                                        // 設定面積參數和類別 (傳入宿主元素)
                                        SetFormworkAreaParameter(ds, areaM2, host);
                                        totalAreaM2 += areaM2; // 累計總面積
                                        
                                        Debug.WriteLine($"✅ 元素處理完成，累計總面積: {totalAreaM2:F6} m²");
                                        
                                        // 記錄建立的模板項目供後續統計使用
                                        _createdFormworkItems.Add(new FormworkItem
                                        {
                                            ElementId = id,
                                            MaterialName = _currentMaterial?.Name ?? "預設",
                                            Thickness = _currentThickness,
                                            Area = areaM2,
                                            Notes = $"從 {host.Category?.Name ?? "未知"} 面生成"
                                        });
                                    }
                                    
                                    // 🎨 即時視覺反饋：綠色閃爍表示成功創建（傳入材料以保留顏色）
                                    VisualFeedbackHelper.FlashElement(doc, uidoc, id, new Color(0, 255, 0), 3, 300, _currentMaterial);

                                    Debug.WriteLine($"✅ 成功生成模板並立即顯示，面積: {areaM2:F2} m² | 總計: {created} 個 | 總面積: {totalAreaM2:F2} m²");
                                }
                                else
                                {
                                    // 提供失敗反饋，但不中斷流程
                                    Debug.WriteLine("該面無法生成模板（面不暴露或被其他結構完全遮擋）");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"生成模板時發生錯誤: {ex.Message}");
                                // 繼續執行，不中斷用戶操作
                            }
                        }
                        t.Commit();
                    }

                    FormworkEngine.EndRun();
                    tg.Assimilate();
                }

                // ESC 完成任務後，顯示數量計算頁面
                if (created > 0)
                {
                    // TODO: 整合 FormworkQuantityDialog
                    // 暫時使用簡化統計
                    ShowFallbackSummary(created, totalAreaM2);

                    /* 原始代碼 - 需要 FormworkQuantityDialog.cs
                    try
                    {
                        // 建立並顯示數量計算對話框
                        var quantityDialog = new FormworkQuantityDialog(doc, _createdFormworkItems);
                        new System.Windows.Interop.WindowInteropHelper(quantityDialog) { Owner = uiapp.MainWindowHandle };
                        quantityDialog.ShowDialog();
                    }
                    catch (Exception dialogEx)
                    {
                        Debug.WriteLine($"數量計算對話框顯示失敗: {dialogEx.Message}");
                        // 數量對話框失敗時，顯示備用的簡化統計 - 確保數據一致性
                        ShowFallbackSummary(created, totalAreaM2);
                    }
                    */
                }
                else
                {
                    TaskDialog.Show("面選模板", "未建立任何模板（可能選錯面或全部被裁切）。");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                msg = ex.Message;
                return Result.Failed;
            }
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
        private class PickFacePalette : WpfWindow
        {
            private readonly Document _doc;
            private readonly WpfComboBox _cmb;
            private readonly WpfTextBox _tbThk;

            public Material SelectedMaterial { get; private set; }
            public double ThicknessMm { get; private set; } = 20.0;

            public PickFacePalette(Document doc)
            {
                _doc = doc;
                Title = "面生面（像油漆）";
                Width = 380; Height = 160;
                WindowStyle = System.Windows.WindowStyle.ToolWindow;
                ResizeMode = System.Windows.ResizeMode.NoResize;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                var root = new WpfGrid { Margin = new WpfThickness(10) };
                Content = root;
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                // row1：材料
                var row1 = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new WpfThickness(0, 0, 0, 8) };
                row1.Children.Add(new WpfLabel { Content = "材料：", Width = 60, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                _cmb = new WpfComboBox { Width = 280, IsEditable = false };
                _cmb.Items.Add(new MatItem("＜不指定＞", ElementId.InvalidElementId));
                var mats = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().OrderBy(m => m.Name);
                foreach (var m in mats) _cmb.Items.Add(new MatItem(m.Name, m.Id));
                _cmb.SelectedIndex = 0;
                row1.Children.Add(_cmb);
                root.Children.Add(row1);
                System.Windows.Controls.Grid.SetRow(row1, 0);

                // row2：厚度
                var row2 = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new WpfThickness(0, 0, 0, 8) };
                row2.Children.Add(new WpfLabel { Content = "厚度 (mm)：", Width = 60, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                _tbThk = new WpfTextBox { Width = 80, Text = "20" };
                row2.Children.Add(_tbThk);
                root.Children.Add(row2);
                System.Windows.Controls.Grid.SetRow(row2, 1);

                // row3：按鈕
                var row3 = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var ok = new WpfButton { Content = "開始點選", Width = 100, Margin = new WpfThickness(0, 0, 8, 0), IsDefault = true };
                var cancel = new WpfButton { Content = "取消", Width = 80, IsCancel = true };

                ok.Click += (s, e) =>
                {
                    if (!double.TryParse(_tbThk.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var mm) || mm <= 0)
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

        /// <summary>
        /// 使用改進引擎為單個面生成模板
        /// </summary>
        private List<ElementId> CreateSingleFaceFormworkWithImprovedEngine(Document doc, Element host, PlanarFace face, double thicknessMm)
        {
            try
            {
                // 對於單面選擇，我們需要適配改進引擎的邏輯
                // 但只處理選中的面，而不是整個元素的所有面
                
                var formworkIds = new List<ElementId>();
                
                // 取得所有結構元素用於扣除計算
                var allStructuralElements = GetAllStructuralElementsForPickFace(doc);
                
                // 建立聯合實體
                var unionSolid = CreateUnionSolidForPickFace(allStructuralElements);
                if (unionSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine("無法建立聯合實體，回退到其他方法");
                    return formworkIds;
                }
                
                // 為選中的面生成模板
                var formworkId = CreateFormworkForSingleFace(doc, host, face, unionSolid, thicknessMm);
                if (formworkId != ElementId.InvalidElementId)
                {
                    formworkIds.Add(formworkId);
                }
                
                return formworkIds;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"改進引擎單面模板生成失敗: {ex.Message}");
                return new List<ElementId>();
            }
        }

        private List<Element> GetAllStructuralElementsForPickFace(Document doc)
        {
            // 🚀 重構: 使用統一的 StructuralElementCollector 工具類別
            return StructuralElementCollector.CollectAll(doc, includeFoundation: false);
        }

        private Solid CreateUnionSolidForPickFace(List<Element> elements)
        {
            var allSolids = new List<Solid>();
            foreach (var element in elements)
            {
                var solids = GetElementSolidsForPickFace(element);
                allSolids.AddRange(solids);
            }

            if (allSolids.Count == 0) return null;

            try
            {
                Solid unionResult = allSolids.First();
                for (int i = 1; i < allSolids.Count; i++)
                {
                    try
                    {
                        var nextUnion = BooleanOperationsUtils.ExecuteBooleanOperation(
                            unionResult, allSolids[i], BooleanOperationsType.Union);
                        if (nextUnion?.Volume > 1e-6)
                        {
                            unionResult = nextUnion;
                        }
                    }
                    catch { }
                }
                return unionResult;
            }
            catch
            {
                return null;
            }
        }

        private List<Solid> GetElementSolidsForPickFace(Element element)
        {
            // 🚀 重構: 使用統一的 GeometryExtractor 工具類別
            return GeometryExtractor.GetElementSolids(element);
        }

        private ElementId CreateFormworkForSingleFace(Document doc, Element hostElement, PlanarFace surface, Solid unionSolid, double thicknessMm)
        {
            try
            {
                // 檢查面積
                var area = surface.Area * 0.092903; // 轉換為平方米
                if (area < 0.01) return ElementId.InvalidElementId; // 最小面積 1cm²

                // 從面建立基本模板實體
                var thickness = thicknessMm / 304.8; // 轉換為英尺
                var normal = surface.FaceNormal;
                var curveLoops = surface.GetEdgesAsCurveLoops();
                if (curveLoops.Count == 0) return ElementId.InvalidElementId;

                var extrusionVector = normal.Multiply(thickness);
                var formworkSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, extrusionVector, thickness);

                if (formworkSolid?.Volume <= 1e-6) return ElementId.InvalidElementId;

                // 使用聯合實體進行精確扣除
                var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    formworkSolid, unionSolid, BooleanOperationsType.Intersect);

                Solid finalFormwork = formworkSolid;
                if (intersection?.Volume > 1e-6)
                {
                    double intersectionRatio = intersection.Volume / formworkSolid.Volume;
                    if (intersectionRatio > 0.05) // 5% 以上才扣除
                    {
                        var difference = BooleanOperationsUtils.ExecuteBooleanOperation(
                            formworkSolid, unionSolid, BooleanOperationsType.Difference);
                        if (difference?.Volume > 1e-6)
                        {
                            finalFormwork = difference;
                        }
                    }
                }

                // 建立 DirectShape
                var directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                directShape.ApplicationId = "YD_BIM_Formwork";
                directShape.ApplicationDataId = "ImprovedPickFace";
                directShape.SetShape(new GeometryObject[] { finalFormwork });
                directShape.Name = $"面選改進模板_{hostElement.Id}";

                return directShape.Id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"單面模板生成失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 產生面的唯一識別碼，避免重複選取同一個面
        /// </summary>
        private string GetFaceKey(Element hostElement, PlanarFace face)
        {
            try
            {
                // 使用元素ID + 面的幾何特徵作為唯一識別
                var origin = face.Origin;
                var normal = face.FaceNormal;
                var area = face.Area;
                
                return $"{hostElement.Id.Value}_{origin.X:F3}_{origin.Y:F3}_{origin.Z:F3}_{normal.X:F3}_{normal.Y:F3}_{normal.Z:F3}_{area:F6}";
            }
            catch
            {
                // 如果失敗，至少使用元素ID
                return $"{hostElement.Id.Value}_{DateTime.Now.Ticks}";
            }
        }

        /// <summary>
        /// 設定元素的材料和顏色 - 增強3D視圖顯示效果
        /// </summary>
        private void SetElementMaterialAndColor(Document doc, DirectShape element, Material material)
        {
            try
            {
                Debug.WriteLine($"🎨 開始設定材質: {material?.Name ?? "無材質"} 給元素 {element.Id}");

                // 🚀 重構: 使用統一的 VisualEffectsManager 工具類別
                if (material != null)
                {
                    // 1. 設定元素的材質參數
                    var materialParam = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (materialParam != null && !materialParam.IsReadOnly)
                    {
                        materialParam.Set(material.Id);
                        Debug.WriteLine($"✅ 成功設定材質參數: {material.Name}");
                    }
                    
                    // 2. 使用 VisualEffectsManager 設定材質和顏色（不透明，完整顯示材質顏色）
                    VisualEffectsManager.SetFormworkMaterialAndColor(doc, element.Id, material, transparency: 0);
                    
                    Debug.WriteLine($"✅ 材質和顏色設定完成: {material.Name}");
                }
                else
                {
                    Debug.WriteLine($"⚠️ 無材質可設定");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 設定材質和顏色失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 計算模板面積（平方公尺）- 帶詳細除錯資訊
        /// </summary>
        private double CalculateFormworkAreaWithDebug(Document doc, DirectShape formworkElement, PlanarFace originalFace)
        {
            try
            {
                Debug.WriteLine($"🔍 開始詳細面積計算分析...");
                
                // 🚀 重構: 優先使用原始面面積
                if (originalFace != null)
                {
                    double originalAreaM2 = AreaCalculator.CalculateFacePickedFormworkArea(originalFace);
                    Debug.WriteLine($"📐 原始面面積: {originalAreaM2:F6} m²");
                    
                    if (originalAreaM2 > 0)
                    {
                        Debug.WriteLine($"✅ 使用原始面面積作為模板面積");
                        return originalAreaM2;
                    }
                }

                // 🚀 重構: 嘗試從DirectShape幾何計算
                Debug.WriteLine($"� 分析DirectShape幾何...");
                var solids = GeometryExtractor.GetElementSolids(formworkElement);
                
                if (solids.Count > 0)
                {
                    var largestSolid = GeometryExtractor.GetLargestSolid(solids);
                    if (largestSolid != null)
                    {
                        double calculatedAreaM2 = AreaCalculator.CalculateSimpleFormworkArea(largestSolid);
                        Debug.WriteLine($"� 從幾何計算面積: {calculatedAreaM2:F6} m²");
                        
                        if (calculatedAreaM2 > 0)
                        {
                            Debug.WriteLine($"✅ 使用計算得到的幾何面積");
                            return calculatedAreaM2;
                        }
                    }
                }
                
                Debug.WriteLine($"⚠️ 所有方法都無法計算出有效面積");
                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 計算模板面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 計算模板面積（平方公尺）- 基於原始面的實際面積
        /// </summary>
        private double CalculateFormworkArea(DirectShape formworkElement)
        {
            try
            {
                // 方法1：嘗試從參數中讀取面積
                var areaParam = formworkElement.LookupParameter("P_EffectiveArea");
                if (areaParam != null && areaParam.HasValue)
                {
                    double paramArea = areaParam.AsDouble();
                    if (paramArea > 0)
                    {
                        // Revit 內部單位轉換為平方公尺
                        double paramAreaM2 = paramArea * 0.092903; // 1 sq ft = 0.092903 sq m
                        Debug.WriteLine($"✅ 從參數讀取面積: {paramArea:F6} sq ft = {paramAreaM2:F6} m²");
                        return paramAreaM2;
                    }
                }

                // 方法2：計算DirectShape的主要面積（通常是最大的面）
                var geometry = formworkElement.get_Geometry(new Options());
                double maxFaceArea = 0;
                double totalVolume = 0;

                foreach (GeometryObject geomObj in geometry)
                {
                    if (geomObj is Solid solid && solid.Volume > 0)
                    {
                        totalVolume += solid.Volume;
                        
                        // 找到最大的面（通常是模板的主要面）
                        foreach (Face face in solid.Faces)
                        {
                            if (face.Area > maxFaceArea)
                            {
                                maxFaceArea = face.Area;
                            }
                        }
                    }
                }

                // 使用最大面積作為模板面積
                double calculatedAreaM2 = maxFaceArea * 0.092903; // 1 sq ft = 0.092903 sq m

                Debug.WriteLine($"✅ 計算最大面積: {maxFaceArea:F6} sq ft = {calculatedAreaM2:F6} m² (體積: {totalVolume:F6} cu ft)");
                return calculatedAreaM2;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 計算模板面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 設定模板面積參數到共用參數欄位
        /// </summary>
        private void SetFormworkAreaParameter(DirectShape element, double areaM2, Element hostElement)
        {
            try
            {
                Debug.WriteLine($"🔧 開始設定面積參數: {areaM2:F6} m² 到元素 {element.Id.Value}");
                
                // 🚀 重構: 使用 AreaCalculator 的單位轉換方法
                double areaInSquareFeet = AreaCalculator.ConvertToSquareFeet(areaM2);
                Debug.WriteLine($"📐 面積轉換: {areaM2:F6} m² = {areaInSquareFeet:F6} sq ft");

                // 方法1：設定共用參數 "模板_有效面積"
                var effectiveAreaParam = element.LookupParameter(SharedParams.P_EffectiveArea);
                if (effectiveAreaParam != null && !effectiveAreaParam.IsReadOnly)
                {
                    effectiveAreaParam.Set(areaInSquareFeet);
                    Debug.WriteLine($"✅ 成功設定共用參數 '{SharedParams.P_EffectiveArea}': {areaM2:F2} m²");
                }
                else
                {
                    Debug.WriteLine($"⚠️ 無法找到或設定共用參數 '{SharedParams.P_EffectiveArea}'");
                }

                // 方法2：設定共用參數 "模板_合計" (也設定相同值)
                var totalParam = element.LookupParameter(SharedParams.P_Total);
                if (totalParam != null && !totalParam.IsReadOnly)
                {
                    totalParam.Set(areaInSquareFeet);
                    Debug.WriteLine($"✅ 成功設定共用參數 '{SharedParams.P_Total}': {areaM2:F2} m²");
                }
                else
                {
                    Debug.WriteLine($"⚠️ 無法找到或設定共用參數 '{SharedParams.P_Total}'");
                }

                // 方法3：設定類別參數 (從宿主元素判斷)
                var categoryParam = element.LookupParameter(SharedParams.P_Category);
                if (categoryParam != null && !categoryParam.IsReadOnly)
                {
                    // 🔧 修正: 使用統一的類別判斷邏輯 (基於宿主元素)
                    string category = GetFormworkCategoryFromHost(hostElement);
                    categoryParam.Set(category);
                    Debug.WriteLine($"✅ 成功設定共用參數 '{SharedParams.P_Category}': {category}");
                }

                // 方法4：設定宿主ID
                var hostIdParam = element.LookupParameter(SharedParams.P_HostId);
                if (hostIdParam != null && !hostIdParam.IsReadOnly)
                {
                    hostIdParam.Set(hostElement.Id.ToString());
                    Debug.WriteLine($"✅ 成功設定共用參數 '{SharedParams.P_HostId}': {hostElement.Id}");
                }

                // 方法5：設定厚度參數
                SetThicknessParameter(element, _currentThickness);

                // 方法6：設定材質參數
                SetMaterialParameter(element, _currentMaterial);

                // 方法7：設定分析時間
                var timeParam = element.LookupParameter(SharedParams.P_AnalysisTime);
                if (timeParam != null && !timeParam.IsReadOnly)
                {
                    var currentTime = System.DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                    timeParam.Set(currentTime);
                    Debug.WriteLine($"✅ 成功設定共用參數 '{SharedParams.P_AnalysisTime}': {currentTime}");
                }

                Debug.WriteLine($"🎯 所有參數設定完成");

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 設定面積參數失敗: {ex.Message}");
                Debug.WriteLine($"❌ 錯誤堆疊: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 設定厚度參數
        /// </summary>
        private void SetThicknessParameter(DirectShape element, double thicknessMm)
        {
            try
            {
                // 轉換為 Revit 內部單位 (英尺)
                double thicknessFt = thicknessMm / 304.8; // mm → ft
                
                // 嘗試設定 "厚度" 共用參數
                var thicknessParam = element.LookupParameter("厚度");
                if (thicknessParam != null && !thicknessParam.IsReadOnly)
                {
                    thicknessParam.Set(thicknessFt);
                    Debug.WriteLine($"✅ 成功設定厚度參數: {thicknessMm:F1} mm ({thicknessFt:F6} ft)");
                }
                else
                {
                    // 嘗試內建厚度參數
                    var builtInThickness = element.get_Parameter(BuiltInParameter.GENERIC_THICKNESS);
                    if (builtInThickness != null && !builtInThickness.IsReadOnly)
                    {
                        builtInThickness.Set(thicknessFt);
                        Debug.WriteLine($"✅ 成功設定內建厚度參數: {thicknessMm:F1} mm");
                    }
                    else
                    {
                        Debug.WriteLine($"⚠️ 找不到可用的厚度參數");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 設定厚度參數失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定材質參數
        /// </summary>
        private void SetMaterialParameter(DirectShape element, Material material)
        {
            try
            {
                if (material == null)
                {
                    Debug.WriteLine($"⚠️ 材質為空，跳過設定");
                    return;
                }

                // 設定 MATERIAL_ID_PARAM
                var materialParam = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (materialParam != null && !materialParam.IsReadOnly)
                {
                    materialParam.Set(material.Id);
                    Debug.WriteLine($"✅ 成功設定材質參數: {material.Name} (ID: {material.Id})");
                }
                else
                {
                    Debug.WriteLine($"⚠️ 無法設定材質參數");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 設定材質參數失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 創建即時預覽元素
        /// </summary>
        private ElementId CreatePreviewElement(Document doc, Element hostElement, PlanarFace face, double thickness)
        {
            try
            {
                // 簡化的預覽版本，只做基本的面擠出
                var normal = face.FaceNormal;
                var curveLoops = face.GetEdgesAsCurveLoops();
                if (curveLoops.Count == 0) return ElementId.InvalidElementId;

                var thicknessInFeet = thickness / 304.8;
                var extrusionVector = normal.Multiply(thicknessInFeet);
                var previewSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, extrusionVector, thicknessInFeet);

                if (previewSolid?.Volume <= 1e-6) return ElementId.InvalidElementId;

                // 建立預覽 DirectShape
                var directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                directShape.ApplicationId = "YD_BIM_Formwork_Preview";
                directShape.ApplicationDataId = "Preview";
                directShape.SetShape(new GeometryObject[] { previewSolid });
                directShape.Name = $"預覽模板_{hostElement.Id}";

                // 設定預覽樣式（半透明、虛線）
                var overrides = new OverrideGraphicSettings();
                overrides.SetSurfaceTransparency(50); // 50% 透明度
                overrides.SetProjectionLineWeight(1);
                overrides.SetProjectionLinePatternId(LinePatternElement.GetSolidPatternId());
                
                if (_currentMaterial?.Color.IsValid == true)
                {
                    overrides.SetProjectionLineColor(_currentMaterial.Color);
                    overrides.SetSurfaceForegroundPatternColor(_currentMaterial.Color);
                }

                var view = doc.ActiveView;
                if (view != null)
                {
                    view.SetElementOverrides(directShape.Id, overrides);
                }

                return directShape.Id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"創建預覽元素失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 清理所有預覽元素
        /// </summary>
        private void ClearPreviewElements(Document doc)
        {
            try
            {
                if (_previewElements.Count > 0)
                {
                    using (var t = new SubTransaction(doc))
                    {
                        t.Start();
                        foreach (var id in _previewElements)
                        {
                            try
                            {
                                doc.Delete(id);
                            }
                            catch
                            {
                                // 忽略刪除失敗
                            }
                        }
                        t.Commit();
                    }
                    _previewElements.Clear();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清理預覽元素失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 直接為選中的面生成模板（不使用傳統架構的全面掃描）
        /// </summary>
        private ElementId CreateSingleFaceFormworkDirectly(Document doc, Element hostElement, PlanarFace face, double thicknessMm, Material material)
        {
            try
            {
                Debug.WriteLine($"為面生成模板：面積 {face.Area * 0.092903:F2} m²");
                
                // 檢查面積
                var areaM2 = face.Area * 0.092903;
                if (areaM2 < 0.01)
                {
                    Debug.WriteLine("面積過小，跳過");
                    return ElementId.InvalidElementId;
                }

                // 🎯 關鍵改進：取得柱子及相鄰元件的實體交集後的面
                // 策略：
                // 1. 取得宿主元素（柱子）的實體
                // 2. 取得相鄰元件（牆、樓板等）的實體
                // 3. 將柱子與相鄰元件進行布林扣除，得到柱子暴露在外的部分
                // 4. 用選取面的輪廓裁切，只保留紅框區域
                // 5. 從裁切後的面生成模板

                var thickness = thicknessMm / 304.8; // 轉換為英尺
                var normal = face.FaceNormal;

                // 步驟 1: 取得宿主元素的實體
                Solid hostSolid = GetElementSolid(hostElement);
                if (hostSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 無法取得宿主元素實體（體積: {hostSolid?.Volume:F9}）");
                    TaskDialog.Show("錯誤", "無法取得宿主元素實體");
                    return ElementId.InvalidElementId;
                }
                Debug.WriteLine($"✅ 取得宿主元素實體，體積: {hostSolid.Volume:F6}");

                // 步驟 2: 取得相鄰元件並進行布林扣除
                var searchRadiusMm = thicknessMm + 3000.0;
                Debug.WriteLine($"🔍 搜尋半徑: {searchRadiusMm:F0}mm ({searchRadiusMm/304.8:F2}ft)");

                var nearbyElements = GeometryExtractor.GetNearbyStructuralElementsFromFace(doc, hostElement, face, searchRadiusMm);
                Debug.WriteLine($"✅ 找到 {nearbyElements.Count} 個相鄰元件");

                // 從柱子實體中扣除相鄰元件，得到暴露在外的部分
                Solid exposedHostSolid = hostSolid;
                foreach (var nearbyElem in nearbyElements)
                {
                    try
                    {
                        var nearbySolid = GetElementSolid(nearbyElem);
                        if (nearbySolid?.Volume > 1e-6)
                        {
                            var tempSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                exposedHostSolid, nearbySolid, BooleanOperationsType.Difference);

                            if (tempSolid?.Volume > 1e-6)
                            {
                                exposedHostSolid = tempSolid;
                                Debug.WriteLine($"  ✅ 扣除元件 {nearbyElem.Id.Value}，剩餘體積: {exposedHostSolid.Volume:F6}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 扣除元件 {nearbyElem.Id.Value} 失敗: {ex.Message}");
                    }
                }

                Debug.WriteLine($"✅ 布林扣除完成，暴露部分體積: {exposedHostSolid.Volume:F6}");

                // 步驟 3: 取得選取面的邊界曲線環
                var curveLoops = face.GetEdgesAsCurveLoops();
                if (curveLoops.Count == 0)
                {
                    Debug.WriteLine("❌ 無法取得面的邊界");
                    TaskDialog.Show("錯誤", "無法取得面的邊界");
                    return ElementId.InvalidElementId;
                }
                Debug.WriteLine($"✅ 取得 {curveLoops.Count} 個邊界曲線環");

                // 步驟 4: 用選取面的輪廓創建裁切實體（向兩側擠出，確保完全覆蓋）
                double extrusionDistance = 10.0; // 向兩側各擠出 10 英尺（約 3 米）
                Solid clippingSolid = CreateClippingSolid(curveLoops, normal, extrusionDistance);

                if (clippingSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 裁切實體創建失敗（體積: {clippingSolid?.Volume:F9}）");
                    TaskDialog.Show("錯誤", "裁切實體創建失敗");
                    return ElementId.InvalidElementId;
                }
                Debug.WriteLine($"✅ 裁切實體創建成功，體積: {clippingSolid.Volume:F6}");

                // 步驟 5: 與暴露的柱子實體交集，只保留選取面範圍內的部分
                Solid clippedHostSolid = null;
                try
                {
                    clippedHostSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                        exposedHostSolid, clippingSolid, BooleanOperationsType.Intersect);
                    Debug.WriteLine($"✅ 與暴露柱子交集成功，體積: {clippedHostSolid?.Volume:F6}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ 與暴露柱子交集失敗: {ex.Message}");
                    TaskDialog.Show("錯誤", $"與暴露柱子交集失敗: {ex.Message}");
                    return ElementId.InvalidElementId;
                }

                if (clippedHostSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 交集後實體體積過小: {clippedHostSolid?.Volume:F9}");
                    TaskDialog.Show("錯誤", $"交集後實體體積過小，可能該區域已被相鄰元件完全覆蓋");
                    return ElementId.InvalidElementId;
                }

                // 步驟 5: 從選取面生成模板
                // 🎯 關鍵修正：只從選取面本身生成模板，不要從裁切後實體的其他面生成
                // 策略：直接用選取面的輪廓向外擠出，確保只在選取面的範圍內

                var extrusionVector = normal.Multiply(thickness);
                Solid formworkSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, extrusionVector, thickness);

                if (formworkSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 模板創建失敗（體積: {formworkSolid?.Volume:F9}）");
                    TaskDialog.Show("錯誤", "模板創建失敗");
                    return ElementId.InvalidElementId;
                }
                Debug.WriteLine($"✅ 模板創建成功，體積: {formworkSolid.Volume:F6}");

                // 步驟 6: 🎯 關鍵修正：從模板中扣除相鄰元件
                // 雖然柱子已經扣除了相鄰元件，但模板向外擠出時可能會延伸到相鄰元件的位置
                // 需要再次將模板與相鄰元件進行扣除
                Debug.WriteLine("🔧 開始從模板中扣除相鄰元件");
                Solid finalFormwork = formworkSolid;

                foreach (var nearbyElem in nearbyElements)
                {
                    try
                    {
                        var nearbySolid = GetElementSolid(nearbyElem);
                        if (nearbySolid?.Volume > 1e-6)
                        {
                            var tempSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                finalFormwork, nearbySolid, BooleanOperationsType.Difference);

                            if (tempSolid?.Volume > 1e-6)
                            {
                                finalFormwork = tempSolid;
                                Debug.WriteLine($"  ✅ 從模板扣除元件 {nearbyElem.Id.Value}，剩餘體積: {finalFormwork.Volume:F6}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 從模板扣除元件 {nearbyElem.Id.Value} 失敗: {ex.Message}");
                    }
                }

                if (finalFormwork?.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 扣除後模板體積過小: {finalFormwork?.Volume:F9}");
                    TaskDialog.Show("錯誤", "扣除後模板體積過小，該區域可能完全被相鄰元件覆蓋");
                    return ElementId.InvalidElementId;
                }

                Debug.WriteLine($"✅ 模板扣除完成，最終體積: {finalFormwork.Volume:F6}");

                // 🎯 修正：不拆分，直接創建一個完整的模板，確保模板數量的正確性
                Debug.WriteLine("🔧 開始創建完整模板（不拆分）");

                try
                {
                    var directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    directShape.ApplicationId = "YD_BIM_Formwork";
                    directShape.ApplicationDataId = "SingleFace";
                    directShape.SetShape(new GeometryObject[] { finalFormwork });

                    // 名稱不包含片段編號
                    directShape.Name = $"面選模板_{hostElement.Id}_{DateTime.Now:HHmmss}";

                    // 設定材料
                    if (material?.Id != null && material.Id != ElementId.InvalidElementId)
                    {
                        try
                        {
                            // 1. 設定材料參數
                            var materialParam = directShape.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                            if (materialParam != null && !materialParam.IsReadOnly)
                            {
                                materialParam.Set(material.Id);
                                Debug.WriteLine($"  ✅ 設定材料參數成功");
                            }

                            // 2. 使用 VisualEffectsManager 設定材料顏色（在所有視圖中顯示）
                            VisualEffectsManager.SetFormworkMaterialAndColor(doc, directShape.Id, material, transparency: 0);
                            Debug.WriteLine($"  ✅ 設定材料顏色成功");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"  ⚠️ 設定材料失敗: {ex.Message}");
                        }
                    }

                    // 設定共用參數 - 宿主元素ID
                    try
                    {
                        var hostIdParam = directShape.LookupParameter(SharedParams.P_HostId);
                        if (hostIdParam != null && !hostIdParam.IsReadOnly)
                        {
                            hostIdParam.Set(hostElement.Id.Value.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 設定宿主ID參數失敗: {ex.Message}");
                    }

                    Debug.WriteLine($"✅ 成功創建完整模板，ID: {directShape.Id.Value}，體積: {finalFormwork.Volume:F6}");

                    // 返回創建的模板 ID
                    return directShape.Id;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ 創建模板失敗: {ex.Message}");
                    TaskDialog.Show("錯誤", $"創建模板失敗: {ex.Message}");
                    return ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"單面模板生成失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 將實體拆分成多個獨立的片段
        /// </summary>
        /// <param name="solid">要拆分的實體</param>
        /// <returns>拆分後的實體列表</returns>
        private List<Solid> SplitSolidIntoFragments(Solid solid)
        {
            var fragments = new List<Solid>();

            if (solid?.Volume <= 1e-6)
            {
                Debug.WriteLine("⚠️ 輸入實體體積過小，無法拆分");
                return fragments;
            }

            try
            {
                // 使用 SolidUtils.SplitVolumes 拆分實體
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
                    // 如果 SplitVolumes 失敗或返回空，使用原始實體
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
        /// 獲取元素的所有實體（已整合到 GeometryExtractor，保留用於向後兼容）
        /// </summary>
        /// <summary>
        /// 創建裁切實體，用於裁切宿主元素，只保留選取面範圍內的部分
        /// </summary>
        /// <param name="curveLoops">選取面的邊界曲線環</param>
        /// <param name="normal">選取面的法向量</param>
        /// <param name="extrusionDistance">向兩側擠出的距離（英尺）</param>
        /// <returns>裁切實體</returns>
        private Solid CreateClippingSolid(IList<CurveLoop> curveLoops, XYZ normal, double extrusionDistance)
        {
            try
            {
                Debug.WriteLine($"🎯 創建裁切實體，擠出距離: {extrusionDistance:F2} 英尺");

                // 向內和向外各擠出指定距離
                var inwardVector = normal.Negate().Multiply(extrusionDistance);
                var outwardVector = normal.Multiply(extrusionDistance);

                // 向內擠出
                var inwardSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, inwardVector, extrusionDistance);

                // 向外擠出
                var outwardSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, outwardVector, extrusionDistance);

                // 合併兩個實體
                if (inwardSolid?.Volume > 1e-6 && outwardSolid?.Volume > 1e-6)
                {
                    try
                    {
                        var combinedSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                            inwardSolid, outwardSolid, BooleanOperationsType.Union);
                        Debug.WriteLine($"✅ 裁切實體創建成功，體積: {combinedSolid?.Volume:F6}");
                        return combinedSolid;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ 合併失敗: {ex.Message}，使用向外擠出");
                        return outwardSolid;
                    }
                }
                else if (outwardSolid?.Volume > 1e-6)
                {
                    return outwardSolid;
                }
                else if (inwardSolid?.Volume > 1e-6)
                {
                    return inwardSolid;
                }

                Debug.WriteLine("❌ 裁切實體創建失敗");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 創建裁切實體異常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 從裁切後的柱子實體生成模板
        /// </summary>
        /// <param name="clippedSolid">裁切後的柱子實體</param>
        /// <param name="face">選取的面</param>
        /// <param name="thickness">模板厚度（英尺）</param>
        /// <returns>模板實體</returns>
        private Solid CreateFormworkFromClippedSolid(Solid clippedSolid, PlanarFace face, double thickness)
        {
            try
            {
                Debug.WriteLine("🎯 從裁切後的實體生成模板");

                var normal = face.FaceNormal;

                // 找到裁切後實體中與選取面對應的面
                PlanarFace matchingFace = null;
                double minDistance = double.MaxValue;

                foreach (Face f in clippedSolid.Faces)
                {
                    if (f is PlanarFace pf)
                    {
                        // 檢查法向量是否平行
                        double dotProduct = pf.FaceNormal.DotProduct(normal);
                        if (Math.Abs(dotProduct) > 0.99) // 平行或反平行
                        {
                            // 計算面中心距離
                            var faceCenterUV = new UV(
                                (pf.GetBoundingBox().Min.U + pf.GetBoundingBox().Max.U) / 2,
                                (pf.GetBoundingBox().Min.V + pf.GetBoundingBox().Max.V) / 2);
                            var faceCenter = pf.Evaluate(faceCenterUV);

                            var selectedFaceCenterUV = new UV(
                                (face.GetBoundingBox().Min.U + face.GetBoundingBox().Max.U) / 2,
                                (face.GetBoundingBox().Min.V + face.GetBoundingBox().Max.V) / 2);
                            var selectedFaceCenter = face.Evaluate(selectedFaceCenterUV);

                            double distance = faceCenter.DistanceTo(selectedFaceCenter);
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                matchingFace = pf;
                            }
                        }
                    }
                }

                if (matchingFace == null)
                {
                    Debug.WriteLine("❌ 找不到匹配的面");
                    return null;
                }

                Debug.WriteLine($"✅ 找到匹配的面，距離: {minDistance:F6}");

                // 從匹配的面向外擠出生成模板
                var curveLoops = matchingFace.GetEdgesAsCurveLoops();
                var extrusionVector = normal.Multiply(thickness);
                var formworkSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, extrusionVector, thickness);

                Debug.WriteLine($"✅ 模板實體生成成功，體積: {formworkSolid?.Volume:F6}");
                return formworkSolid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 生成模板異常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 創建一個精確的薄片模板實體，只包含選取面的模板
        /// 策略：向內和向外雙向擠出，確保與宿主元素有重疊
        /// </summary>
        /// <param name="curveLoops">選取面的邊界曲線環</param>
        /// <param name="normal">選取面的法向量</param>
        /// <param name="thickness">模板厚度（英尺）</param>
        /// <returns>薄片模板實體</returns>
        private Solid CreateThinFormworkSolid(IList<CurveLoop> curveLoops, XYZ normal, double thickness)
        {
            try
            {
                Debug.WriteLine("🎯 創建薄片模板實體（雙向擠出）");

                // 🔧 關鍵修正：雙向擠出，確保與宿主元素有重疊
                // 策略：向內擠出 0.5 英尺（約 150mm），向外擠出模板厚度
                // 這樣可以確保實體穿過選取面，與柱子內部有重疊

                double inwardDistance = 0.5; // 向內 0.5 英尺（約 150mm）

                // 步驟 1: 向內擠出（負方向）
                var inwardVector = normal.Negate().Multiply(inwardDistance);
                var inwardSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, inwardVector, inwardDistance);

                // 步驟 2: 向外擠出（正方向）
                var outwardVector = normal.Multiply(thickness);
                var outwardSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, outwardVector, thickness);

                // 步驟 3: 合併兩個實體
                Solid combinedSolid = null;
                if (inwardSolid?.Volume > 1e-6 && outwardSolid?.Volume > 1e-6)
                {
                    try
                    {
                        combinedSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                            inwardSolid, outwardSolid, BooleanOperationsType.Union);
                        Debug.WriteLine($"✅ 雙向擠出合併成功，體積: {combinedSolid?.Volume:F6}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ 合併失敗: {ex.Message}，使用向外擠出");
                        combinedSolid = outwardSolid;
                    }
                }
                else if (outwardSolid?.Volume > 1e-6)
                {
                    Debug.WriteLine("⚠️ 向內擠出失敗，只使用向外擠出");
                    combinedSolid = outwardSolid;
                }
                else
                {
                    Debug.WriteLine("❌ 擠出失敗");
                    return null;
                }

                if (combinedSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine("⚠️ 合併後實體體積過小");
                    return null;
                }

                Debug.WriteLine($"✅ 薄片實體已創建（雙向擠出），體積: {combinedSolid.Volume:F6}");
                return combinedSolid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 創建薄片實體失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 只保留與選取面相關的實體片段（過濾掉布林運算後產生的其他片段）
        /// 改進策略：檢查實體的每個面，找到與選取面最接近的片段
        /// </summary>
        /// <param name="solid">可能包含多個分離片段的實體</param>
        /// <param name="selectedFace">用戶選取的面</param>
        /// <returns>與選取面重疊的實體片段</returns>
        private Solid KeepOnlyFaceRelatedSolid(Solid solid, PlanarFace selectedFace)
        {
            try
            {
                Debug.WriteLine("🎯 開始過濾實體片段，只保留與選取面相關的部分");

                if (solid?.Volume <= 1e-6)
                {
                    Debug.WriteLine("⚠️ 輸入實體無效");
                    return null;
                }

                // 🎯 新策略：
                // 1. 將實體分解為獨立的片段（SolidUtils.SplitVolumes）
                // 2. 對每個片段，檢查其面是否與選取面重疊
                // 3. 只保留包含選取面的片段

                // 取得選取面的中心點和法向量
                var selectedFaceCenter = GetFaceCenter(selectedFace);
                var selectedNormal = selectedFace.FaceNormal;

                Debug.WriteLine($"📍 選取面中心: ({selectedFaceCenter.X:F3}, {selectedFaceCenter.Y:F3}, {selectedFaceCenter.Z:F3})");
                Debug.WriteLine($"📐 選取面法向量: ({selectedNormal.X:F3}, {selectedNormal.Y:F3}, {selectedNormal.Z:F3})");

                // 嘗試分割實體為獨立片段
                IList<Solid> fragments = null;
                try
                {
                    fragments = SolidUtils.SplitVolumes(solid);
                    Debug.WriteLine($"🔍 實體被分割為 {fragments.Count} 個片段");
                }
                catch
                {
                    // 如果分割失敗，將整個實體視為單一片段
                    fragments = new List<Solid> { solid };
                    Debug.WriteLine("🔍 實體無法分割，視為單一片段");
                }

                // 找到包含選取面的片段
                Solid bestFragment = null;
                double minDistance = double.MaxValue;

                foreach (var fragment in fragments)
                {
                    if (fragment?.Volume <= 1e-6) continue;

                    // 檢查這個片段的所有面
                    foreach (Face face in fragment.Faces)
                    {
                        if (face is PlanarFace planarFace)
                        {
                            // 檢查這個面是否與選取面重疊
                            var faceCenter = GetFaceCenter(planarFace);
                            var faceNormal = planarFace.FaceNormal;

                            // 計算面中心之間的距離
                            var distance = selectedFaceCenter.DistanceTo(faceCenter);

                            // 檢查法向量是否平行（同向或反向）
                            var dotProduct = Math.Abs(selectedNormal.DotProduct(faceNormal));
                            var isParallel = dotProduct > 0.99; // 允許小誤差

                            // 如果面是平行的且距離很近，這可能是我們要的片段
                            if (isParallel && distance < 0.1) // 距離小於 0.1 英尺（約 30mm）
                            {
                                Debug.WriteLine($"✅ 找到匹配的面，距離: {distance:F6}, 點積: {dotProduct:F6}");

                                if (distance < minDistance)
                                {
                                    minDistance = distance;
                                    bestFragment = fragment;
                                }
                            }
                        }
                    }
                }

                if (bestFragment != null)
                {
                    Debug.WriteLine($"✅ 過濾成功，原始體積: {solid.Volume:F6}, 過濾後體積: {bestFragment.Volume:F6}, 最小距離: {minDistance:F6}");
                    return bestFragment;
                }
                else
                {
                    Debug.WriteLine("⚠️ 未找到匹配的片段，返回原實體");
                    return solid;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 過濾實體片段失敗: {ex.Message}");
                return solid;
            }
        }

        /// <summary>
        /// 取得元素的實體幾何（合併所有實體）
        /// </summary>
        private Solid GetElementSolid(Element element)
        {
            try
            {
                Debug.WriteLine($"🔍 開始取得元素實體: {element.Name} (ID: {element.Id.Value})");

                var options = new Options
                {
                    ComputeReferences = false,
                    DetailLevel = ViewDetailLevel.Fine,
                    IncludeNonVisibleObjects = false
                };

                var geomElement = element.get_Geometry(options);
                if (geomElement == null)
                {
                    Debug.WriteLine("❌ 無法取得幾何元素");
                    return null;
                }

                List<Solid> solids = new List<Solid>();

                foreach (GeometryObject geomObj in geomElement)
                {
                    if (geomObj is Solid solid && solid.Volume > 1e-6)
                    {
                        solids.Add(solid);
                        Debug.WriteLine($"  ✅ 找到實體，體積: {solid.Volume:F6}");
                    }
                    else if (geomObj is GeometryInstance geomInst)
                    {
                        var instGeom = geomInst.GetInstanceGeometry();
                        foreach (GeometryObject instObj in instGeom)
                        {
                            if (instObj is Solid instSolid && instSolid.Volume > 1e-6)
                            {
                                solids.Add(instSolid);
                                Debug.WriteLine($"  ✅ 找到實例實體，體積: {instSolid.Volume:F6}");
                            }
                        }
                    }
                }

                if (solids.Count == 0)
                {
                    Debug.WriteLine("❌ 未找到任何有效實體");
                    return null;
                }

                Debug.WriteLine($"✅ 共找到 {solids.Count} 個實體");

                // 如果只有一個實體，直接返回
                if (solids.Count == 1)
                {
                    Debug.WriteLine($"✅ 返回單一實體，體積: {solids[0].Volume:F6}");
                    return solids[0];
                }

                // 如果有多個實體，合併它們
                Solid combinedSolid = solids[0];
                for (int i = 1; i < solids.Count; i++)
                {
                    try
                    {
                        combinedSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                            combinedSolid, solids[i], BooleanOperationsType.Union);
                        Debug.WriteLine($"  ✅ 合併實體 {i}，當前體積: {combinedSolid?.Volume:F6}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 合併實體 {i} 失敗: {ex.Message}");
                    }
                }

                Debug.WriteLine($"✅ 返回合併實體，總體積: {combinedSolid?.Volume:F6}");
                return combinedSolid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 取得元素實體失敗: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 取得平面的中心點
        /// </summary>
        private XYZ GetFaceCenter(PlanarFace face)
        {
            // 使用面的邊界框計算中心點
            var bbox = face.GetBoundingBox();
            if (bbox != null)
            {
                // GetBoundingBox 返回的是 UV 座標系統的邊界框
                // 需要將 UV 座標轉換為 XYZ 座標
                var uMin = bbox.Min.U;
                var uMax = bbox.Max.U;
                var vMin = bbox.Min.V;
                var vMax = bbox.Max.V;

                var uCenter = (uMin + uMax) / 2.0;
                var vCenter = (vMin + vMax) / 2.0;

                // 將 UV 座標轉換為 XYZ 座標
                var centerPoint = face.Evaluate(new UV(uCenter, vCenter));
                return centerPoint;
            }

            // 如果無法取得邊界框，使用面的原點
            return face.Origin;
        }

        /// <summary>
        /// 裁切模板實體，只保留選取面範圍內的部分（移除擠出產生的側面）
        /// 改進策略：創建一個精確的裁切盒，確保只保留選取面的模板
        /// </summary>
        /// <param name="formworkSolid">原始模板實體（包含側面）</param>
        /// <param name="selectedFace">用戶選取的面</param>
        /// <param name="thickness">模板厚度（英尺）</param>
        /// <returns>裁切後的模板實體（只包含選取面的模板）</returns>
        private Solid ClipFormworkToSelectedFaceOnly(Solid formworkSolid, PlanarFace selectedFace, double thickness)
        {
            try
            {
                Debug.WriteLine("🎯 開始裁切模板，只保留選取面範圍");

                // 獲取選取面的法向量和邊界
                var normal = selectedFace.FaceNormal;
                var curveLoops = selectedFace.GetEdgesAsCurveLoops();

                if (curveLoops.Count == 0)
                {
                    Debug.WriteLine("⚠️ 無法取得面的邊界，返回原始實體");
                    return formworkSolid;
                }

                // 🎯 改進策略：創建一個精確的裁切盒
                // 1. 從選取面向兩側各擠出一小段距離，創建一個薄盒
                // 2. 這個薄盒只覆蓋選取面的範圍，不會延伸到其他面

                // 向內偏移一點（避免邊界問題）
                var inwardOffset = -0.01 / 304.8; // 向內 0.01mm
                // 向外擠出（模板厚度 + 緩衝）
                var outwardDistance = thickness + 0.1 / 304.8; // 模板厚度 + 0.1mm 緩衝

                // 總擠出距離（向內 + 向外）
                var totalExtrusionDistance = Math.Abs(inwardOffset) + outwardDistance;

                // 創建裁切實體：從選取面向內偏移一點，然後向外擠出
                // 這樣可以確保裁切盒完全覆蓋模板，但不會延伸到其他面
                var startOffset = normal.Multiply(inwardOffset);
                var extrusionVector = normal.Multiply(totalExtrusionDistance);

                // 將曲線環偏移到起始位置
                var offsetLoops = new List<CurveLoop>();
                foreach (var loop in curveLoops)
                {
                    var offsetCurves = new List<Curve>();
                    foreach (Curve curve in loop)
                    {
                        var offsetCurve = curve.CreateTransformed(Transform.CreateTranslation(startOffset));
                        offsetCurves.Add(offsetCurve);
                    }
                    var offsetLoop = CurveLoop.Create(offsetCurves);
                    offsetLoops.Add(offsetLoop);
                }

                Solid clippingSolid = null;
                try
                {
                    clippingSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                        offsetLoops, extrusionVector, totalExtrusionDistance);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ 創建裁切實體失敗: {ex.Message}，嘗試使用原始曲線環");
                    // 如果偏移失敗，使用原始曲線環
                    try
                    {
                        clippingSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                            curveLoops, extrusionVector, totalExtrusionDistance);
                    }
                    catch (Exception ex2)
                    {
                        Debug.WriteLine($"⚠️ 使用原始曲線環也失敗: {ex2.Message}，返回原始實體");
                        return formworkSolid;
                    }
                }

                if (clippingSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine("⚠️ 裁切實體體積過小，返回原始實體");
                    return formworkSolid;
                }

                Debug.WriteLine($"📦 裁切實體已創建，體積: {clippingSolid.Volume:F6}");

                // 使用布林交集運算，只保留選取面範圍內的模板
                try
                {
                    var clippedSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                        formworkSolid, clippingSolid, BooleanOperationsType.Intersect);

                    if (clippedSolid?.Volume > 1e-6)
                    {
                        Debug.WriteLine($"✅ 裁切成功，原始體積: {formworkSolid.Volume:F6}, 裁切後體積: {clippedSolid.Volume:F6}");
                        return clippedSolid;
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ 裁切後體積過小，返回原始實體");
                        return formworkSolid;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ 布林運算失敗: {ex.Message}，返回原始實體");
                    return formworkSolid;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 裁切模板失敗: {ex.Message}，返回原始實體");
                return formworkSolid;
            }
        }

        private List<Solid> GetElementSolids(Element element)
        {
            var solids = new List<Solid>();
            try
            {
                var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
                if (geometry == null) return solids;

                foreach (var geomObj in geometry)
                {
                    if (geomObj is Solid solid && solid.Volume > 1e-6)
                    {
                        solids.Add(solid);
                    }
                    else if (geomObj is GeometryInstance instance)
                    {
                        var instGeom = instance.GetInstanceGeometry();
                        foreach (var instObj in instGeom)
                        {
                            if (instObj is Solid instSolid && instSolid.Volume > 1e-6)
                            {
                                solids.Add(instSolid);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"獲取元素 {element.Id} 的實體失敗: {ex.Message}");
            }
            return solids;
        }

        /// <summary>
        /// 顯示備用統計摘要（當主要對話框失敗時使用）
        /// </summary>
        private void ShowFallbackSummary(int created, double totalAreaM2)
        {
            try
            {
                Debug.WriteLine($"🔍 顯示備用統計摘要，created={created}, totalAreaM2={totalAreaM2:F6}");
                
                // 計算統計數據與主要對話框保持一致
                var totalCount = _createdFormworkItems.Count;
                var calculatedTotalArea = _createdFormworkItems.Sum(item => item.Area);
                
                // 使用實際數據，而不是可能不準確的變數
                var finalCount = Math.Max(created, totalCount);
                var finalArea = Math.Max(totalAreaM2, calculatedTotalArea);
                
                Debug.WriteLine($"📊 最終統計: 數量={finalCount}, 面積={finalArea:F6} m²");
                
                var summaryMsg = $"✅ 面選模板完成\n\n" +
                               $"📊 統計資訊:\n" +
                               $"• 模板數量: {finalCount} 個\n" +
                               $"• 總面積: {finalArea:F2} m²\n" +
                               $"• 材料: {_currentMaterial?.Name ?? "預設"}\n" +
                               $"• 厚度: {_currentThickness} mm\n\n" +
                               $"💡 提示:\n" +
                               $"每個片段都是獨立的元素，您可以手動選擇並刪除不需要的部分";

                // 添加詳細項目信息
                if (_createdFormworkItems.Any())
                {
                    summaryMsg += "\n\n📋 詳細項目:";
                    foreach (var item in _createdFormworkItems.Take(5)) // 最多顯示5個項目
                    {
                        summaryMsg += $"\n• ID:{item.ElementId.Value}, 面積:{item.Area:F2} m²";
                    }
                    if (_createdFormworkItems.Count > 5)
                    {
                        summaryMsg += $"\n... 還有 {_createdFormworkItems.Count - 5} 個項目";
                    }
                }

                Debug.WriteLine($"📋 備用對話框內容準備完成");
                TaskDialog.Show("面選模板", summaryMsg);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 顯示備用統計摘要失敗: {ex.Message}");
                // 最後的救援方案 - 最簡單的顯示
                TaskDialog.Show("面選模板", $"模板建立完成: {created} 個");
            }
        }

        /// <summary>
        /// 根據宿主元素類型判斷模板類別 (統一邏輯)
        /// </summary>
        private string GetFormworkCategoryFromHost(Element hostElement)
        {
            if (hostElement == null || hostElement.Category == null)
                return "其他";

#if REVIT2024 || REVIT2025 || REVIT2026
            var categoryId = hostElement.Category.Id.Value;
#else
            var categoryId = hostElement.Category.Id.IntegerValue;
#endif

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
    }

    /// <summary>
    /// 模板項目資料類別
    /// </summary>
    public class FormworkItem
    {
        public ElementId ElementId { get; set; }
        public string MaterialName { get; set; }
        public double Thickness { get; set; }
        public double Area { get; set; }
        public string Notes { get; set; }

        public FormworkItem()
        {
            ElementId = ElementId.InvalidElementId;
            MaterialName = "預設";
            Thickness = 0;
            Area = 0;
            Notes = "";
        }
    }

    /// <summary>
    /// 輕量級視覺反饋輔助類
    /// </summary>
    public static class VisualFeedbackHelper
    {
        /// <summary>
        /// 閃爍元素以提供即時視覺反饋（使用短暫延遲確保用戶可見）
        /// </summary>
        /// <param name="doc">文檔</param>
        /// <param name="uidoc">UI文檔</param>
        /// <param name="elementId">要閃爍的元素ID</param>
        /// <param name="color">閃爍顏色</param>
        /// <param name="lineWeight">線寬</param>
        /// <param name="durationMs">持續時間（毫秒），預設 300ms</param>
        /// <param name="materialToRestore">閃爍後要恢復的材料（如果為 null 則完全清除覆蓋）</param>
        public static void FlashElement(Document doc, UIDocument uidoc, ElementId elementId, Color color, int lineWeight = 3, int durationMs = 300, Material materialToRestore = null)
        {
            try
            {
                var view = doc.ActiveView;
                if (view == null) return;

                // 設定高亮顯示（加強視覺效果）
                var overrides = new OverrideGraphicSettings();
                overrides.SetProjectionLineColor(color);
                overrides.SetProjectionLineWeight(lineWeight);
                overrides.SetSurfaceTransparency(0); // 完全不透明以突出顯示
                view.SetElementOverrides(elementId, overrides);
                uidoc.RefreshActiveView();

                // 短暫延遲以確保用戶可見（使用 Thread.Sleep 在 Revit API 中是安全的）
                System.Threading.Thread.Sleep(durationMs);

                // 恢復正常顯示（如果有材料，則使用 VisualEffectsManager 恢復材料顏色）
                if (materialToRestore != null)
                {
                    // 使用 VisualEffectsManager 恢復材料顏色（只在當前視圖，不透明）
                    VisualEffectsManager.SetFormworkMaterialAndColorForView(doc, view, elementId, materialToRestore, transparency: 0);
                    Debug.WriteLine($"✅ 閃爍後恢復材料顏色: {materialToRestore.Name}");
                }
                else
                {
                    view.SetElementOverrides(elementId, new OverrideGraphicSettings());
                }
                uidoc.RefreshActiveView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"視覺反饋失敗: {ex.Message}");
            }
        }
    }
}
