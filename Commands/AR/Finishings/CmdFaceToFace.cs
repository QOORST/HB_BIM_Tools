using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings
{
    /// <summary>
    /// AR 裝修工具 - 面生面
    /// 與面選模板邏輯相同，但參數寫入材料資訊供數量產出使用
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdFaceToFace : IExternalCommand
    {
        private WallType _currentWallType;
        private FloorType _currentFloorType;
        private Material _currentMaterial;
        private double _currentThickness;
        private bool _useGenericModel; // 是否使用一般模型
        private double _slopedFaceFloorMaxAngleDeg = 45.0; // 斜面與水平面夾角小於等於此值時套用樓板類型
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

                // 1) 小視窗：選取牆類型 + 樓板類型 或 一般模型
                var dlg = new PickFacePalette(doc);
                dlg.Title = "AR 裝修 - 面生面";
                new System.Windows.Interop.WindowInteropHelper(dlg) { Owner = uiapp.MainWindowHandle };
                var ok = dlg.ShowDialog();
                if (ok != true) return Result.Cancelled;

                _useGenericModel = dlg.UseGenericModel;
                _slopedFaceFloorMaxAngleDeg = dlg.SlopedFaceFloorMaxAngleDeg;

                if (_useGenericModel)
                {
                    // 使用一般模型模式
                    _currentMaterial = dlg.SelectedMaterial;
                    _currentThickness = dlg.ThicknessMm;
                    _currentWallType = null;
                    _currentFloorType = null;

                    Debug.WriteLine($"🎨 使用一般模型模式 - 材質: {_currentMaterial?.Name}, 厚度: {_currentThickness} mm");
                }
                else
                {
                    // 使用牆/樓板類型模式
                    _currentWallType = dlg.SelectedWallType;
                    _currentFloorType = dlg.SelectedFloorType;

                    // 從選取的類型中取得材料和厚度（用於顯示）
                    _currentMaterial = null;
                    _currentThickness = 0;

                    if (_currentWallType != null)
                    {
                        var wallCompound = _currentWallType.GetCompoundStructure();
                        if (wallCompound != null && wallCompound.GetLayers().Count > 0)
                        {
                            var layer = wallCompound.GetLayers()[0];
                            _currentThickness = layer.Width * 304.8; // feet to mm
                            _currentMaterial = doc.GetElement(layer.MaterialId) as Material;
                        }
                    }
                    else if (_currentFloorType != null)
                    {
                        var floorCompound = _currentFloorType.GetCompoundStructure();
                        if (floorCompound != null && floorCompound.GetLayers().Count > 0)
                        {
                            var layer = floorCompound.GetLayers()[0];
                            _currentThickness = layer.Width * 304.8; // feet to mm
                            _currentMaterial = doc.GetElement(layer.MaterialId) as Material;
                        }
                    }

                    Debug.WriteLine($"🎨 使用類型模式 - 牆類型: {_currentWallType?.Name}, 樓板類型: {_currentFloorType?.Name}");
                }

                // 2) 多面選取（點擊完成按鈕結束）
                var filter = new FaceOnHostFilter(allowFloor: true);

                // 步驟 1: 選取多個面
                IList<Reference> selectedRefs;
                try
                {
                    var promptMsg = $"點選要生成裝修面的『面』（支援牆、柱、梁、板、樓梯；點擊完成按鈕結束選取）\n" +
                                  $"🎨 材料: {_currentMaterial?.Name ?? "預設"} | 📏 厚度: {_currentThickness}mm";
                    selectedRefs = uidoc.Selection.PickObjects(ObjectType.Face, filter, promptMsg);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (selectedRefs == null || selectedRefs.Count == 0)
                {
                    TaskDialog.Show("面生面", "未選取任何面。");
                    return Result.Cancelled;
                }

                // 步驟 2: 驗證和過濾選取的面（支援平面與曲面）
                var validFaces = new List<(Element host, Face face, Reference reference)>();
                var skippedCount = 0;
                var invalidCount = 0;

                foreach (var r in selectedRefs)
                {
                    var host = doc.GetElement(r.ElementId);
                    var selectedFace = host?.GetGeometryObjectFromReference(r) as Face;

                    if (selectedFace == null)
                    {
                        Debug.WriteLine($"⚠️ 跳過無效的面 - 宿主: {host?.Name ?? "未知"} (ID: {r.ElementId})");
                        invalidCount++;
                        continue;
                    }

                    if (!(selectedFace is PlanarFace))
                        Debug.WriteLine($"ℹ️ 選取到曲面，將使用 DirectShape 路徑處理");

                    // 檢查是否已選取過這個面
                    var faceKey = GetFaceKey(host, selectedFace);
                    if (_selectedFaces.Contains(faceKey))
                    {
                        Debug.WriteLine($"⚠️ 該面已經選取過，跳過 - 宿主: {host.Name} (ID: {host.Id})");
                        skippedCount++;
                        continue;
                    }

                    validFaces.Add((host, selectedFace, r));
                    _selectedFaces.Add(faceKey);
                }

                // 步驟 3: 批次生成所有裝修面
                int created = 0;
                int failed = 0;
                double totalAreaM2 = 0;
                var errorMessages = new List<string>();

                using (var tg = new TransactionGroup(doc, "AR裝修-面生面"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "AR裝修-面生面"))
                    {
                        t.Start();

                        foreach (var (host, face, reference) in validFaces)
                        {
                            try
                            {
                                ElementId id = CreateFinishingFace(doc, host, face, _currentThickness, _currentMaterial);

                                if (id != ElementId.InvalidElementId)
                                {
                                    created++;
                                    _createdElementIds.Add(id);

                                    // 計算面積
                                    var areaM2 = face.Area * 0.09290304; // ft² → m²
                                    totalAreaM2 += areaM2;

                                    Debug.WriteLine($"✅ 成功生成裝修面 ID: {id.GetIdValue()}，面積: {areaM2:F2} m²");
                                }
                                else
                                {
                                    failed++;
                                    errorMessages.Add($"宿主 {host.Name} (ID: {host.Id}) - 生成失敗");
                                }
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                var errorMsg = $"宿主 {host.Name} (ID: {host.Id}) - {ex.Message}";
                                errorMessages.Add(errorMsg);
                                Debug.WriteLine($"❌ 生成裝修面失敗: {errorMsg}");
                            }
                        }

                        t.Commit();
                    }

                    // 步驟 3.5: 接合所有相鄰的裝修牆
                    if (_createdElementIds.Count > 1)
                    {
                        using (var t = new Transaction(doc, "接合裝修牆"))
                        {
                            t.Start();

                            try
                            {
                                VisualFeedbackHelper.JoinAdjacentFinishingWalls(doc, _createdElementIds);
                                Debug.WriteLine($"✅ 完成裝修牆接合處理");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"⚠️ 裝修牆接合處理失敗: {ex.Message}");
                            }

                            t.Commit();
                        }
                    }

                    tg.Assimilate();
                }

                // 步驟 4: 顯示完成訊息和統計
                var summaryMsg = $"📊 生成結果統計\n\n" +
                               $"✅ 成功生成: {created} 個裝修面\n" +
                               $"📐 總面積: {totalAreaM2:F2} m²\n" +
                               $"🎨 材料: {_currentMaterial?.Name ?? "預設"}\n" +
                               $"📏 厚度: {_currentThickness} mm\n\n";

                if (skippedCount > 0)
                {
                    summaryMsg += $"⚠️ 跳過重複選取: {skippedCount} 個面\n";
                }

                if (invalidCount > 0)
                {
                    summaryMsg += $"⚠️ 跳過無效幾何: {invalidCount} 個面\n";
                }

                if (failed > 0)
                {
                    summaryMsg += $"\n❌ 生成失敗: {failed} 個面\n";
                    if (errorMessages.Count > 0 && errorMessages.Count <= 5)
                    {
                        summaryMsg += "\n失敗詳情:\n";
                        foreach (var msg in errorMessages)
                        {
                            summaryMsg += $"  • {msg}\n";
                        }
                    }
                    else if (errorMessages.Count > 5)
                    {
                        summaryMsg += $"\n（顯示前5個錯誤）\n";
                        for (int i = 0; i < 5; i++)
                        {
                            summaryMsg += $"  • {errorMessages[i]}\n";
                        }
                    }
                }

                TaskDialog.Show("AR裝修-面生面完成", summaryMsg);

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
        /// 生成裝修面（Wall 或 Floor，或 DirectShape 曲面）
        /// 垂直平面使用 Wall，水平平面使用 Floor，曲面使用 DirectShape
        /// </summary>
        private ElementId CreateFinishingFace(Document doc, Element host, Face faceAny, double thicknessMm, Material material)
        {
            try
            {
                Debug.WriteLine($"🎯 開始生成裝修面 - 宿主: {host.Name} (ID: {host.Id})");

                // 曲面：統一使用 FormworkEngine（DirectShape）路徑，避免 Boolean 縫隙問題
                if (!(faceAny is PlanarFace))
                {
                    Debug.WriteLine("🌀 曲面：使用 FormworkEngine.BuildFromAnyFace 路徑");
                    var curvedId = FormworkEngine.BuildFromAnyFace(doc, host, faceAny, thicknessMm, material);
                    if (curvedId != ElementId.InvalidElementId)
                    {
                        // ForworkEngine 已寫入面積參數，此處補寫裝修共用參數
                        var curvedElem = doc.GetElement(curvedId);
                        if (curvedElem != null)
                            SetFinishingParameters(curvedElem, host, thicknessMm, material, faceAny.Area, faceAny);
                    }
                    return curvedId;
                }

                var face = (PlanarFace)faceAny;

                // 取得面的法向量和邊界
                var normal = face.FaceNormal;
                var curveLoops = face.GetEdgesAsCurveLoops();

                if (curveLoops == null || curveLoops.Count == 0)
                {
                    Debug.WriteLine("❌ 無法取得面的邊界");
                    return ElementId.InvalidElementId;
                }

                Debug.WriteLine($"✅ 取得 {curveLoops.Count} 個邊界曲線環");
                var outerLoop = GetOuterLoop(curveLoops);
                if (outerLoop == null)
                {
                    Debug.WriteLine("❌ 無法判定外邊界環");
                    return ElementId.InvalidElementId;
                }

                // 轉換厚度（mm → feet）
                double thicknessFt = thicknessMm / 304.8;

                // 🎯 關鍵判斷：根據法向量判斷是垂直面還是水平面
                // 垂直面：法向量的 Z 分量接近 0
                // 水平面：法向量的 Z 分量接近 ±1
                bool isVertical = Math.Abs(normal.Z) < 0.1; // Z 分量小於 0.1 視為垂直
                bool isHorizontal = Math.Abs(normal.Z) > 0.9; // Z 分量大於 0.9 視為水平

                Debug.WriteLine($"📐 面的方向 - 法向量: ({normal.X:F3}, {normal.Y:F3}, {normal.Z:F3})");
                Debug.WriteLine($"📐 判斷結果 - 垂直面: {isVertical}, 水平面: {isHorizontal}");

                ElementId createdId = ElementId.InvalidElementId;

                if (_useGenericModel)
                {
                    // 使用一般模型創建裝修面
                    Debug.WriteLine("🎨 使用一般模型創建裝修面");
                    createdId = CreateGenericModelFromFace(doc, host, face, outerLoop, thicknessMm, material);
                }
                else
                {
                    // 使用牆/樓板類型創建裝修面
                    if (isVertical)
                    {
                        // 垂直面 → 使用 Wall
                        if (_currentWallType == null)
                        {
                            Debug.WriteLine("❌ 未選取牆類型，無法生成垂直面");
                            TaskDialog.Show("錯誤", "未選取牆類型，無法生成垂直面。\n請在設定畫面選擇牆類型。");
                            return ElementId.InvalidElementId;
                        }
                        createdId = CreateWallFromFace(doc, host, face, curveLoops, _currentWallType);
                    }
                    else if (isHorizontal)
                    {
                        // 水平面 → 使用 Floor
                        if (_currentFloorType == null)
                        {
                            Debug.WriteLine("❌ 未選取樓板類型，無法生成水平面");
                            TaskDialog.Show("錯誤", "未選取樓板類型，無法生成水平面。\n請在設定畫面選擇樓板類型。");
                            return ElementId.InvalidElementId;
                        }
                        createdId = CreateFloorFromFace(doc, host, face, curveLoops, _currentFloorType);
                    }
                    else
                    {
                        // 斜面（常見於樓梯底面、斜板）：用角度規則決定套用牆或樓板類型，
                        // 幾何仍用 DirectShape 保留原始斜面，避免 Wall/Floor API 將面拉直或建立失敗。
                        Debug.WriteLine("📐 斜面：依角度規則選擇牆/樓板類型，並以 DirectShape 保留斜面幾何");
                        if (!ResolveSlopedFaceType(doc, normal, out var slopedThicknessMm, out var slopedMaterial, out var slopedTypeName, out var slopeAngleDeg))
                        {
                            TaskDialog.Show("錯誤",
                                "無法依斜面角度取得可用的牆/樓板類型。\n" +
                                "請確認已選擇牆類型或樓板類型，或改用一般模型模式。");
                            return ElementId.InvalidElementId;
                        }

                        Debug.WriteLine($"📐 斜面角度 {slopeAngleDeg:F1}°，套用類型：{slopedTypeName}");
                        createdId = CreateGenericModelFromFace(doc, host, face, outerLoop, slopedThicknessMm, slopedMaterial);
                    }
                }

                return createdId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CreateFinishingFace 失敗: {ex.Message}");
                throw;
            }
        }

        private bool ResolveSlopedFaceType(
            Document doc,
            XYZ normal,
            out double thicknessMm,
            out Material material,
            out string typeName,
            out double slopeAngleDeg)
        {
            thicknessMm = 0;
            material = null;
            typeName = null;

            var normalized = normal.GetLength() > 1e-9 ? normal.Normalize() : XYZ.BasisZ;
            var cosToVertical = Math.Min(1.0, Math.Abs(normalized.Z));
            slopeAngleDeg = Math.Acos(cosToVertical) * 180.0 / Math.PI;

            bool preferFloor = slopeAngleDeg <= _slopedFaceFloorMaxAngleDeg;
            if (preferFloor && TryGetFloorTypeAppearance(doc, _currentFloorType, out thicknessMm, out material, out typeName))
                return true;

            if (!preferFloor && TryGetWallTypeAppearance(doc, _currentWallType, out thicknessMm, out material, out typeName))
                return true;

            // 若使用者只指定其中一種類型，仍允許斜面套用該類型，避免流程卡住。
            if (TryGetFloorTypeAppearance(doc, _currentFloorType, out thicknessMm, out material, out typeName))
                return true;

            if (TryGetWallTypeAppearance(doc, _currentWallType, out thicknessMm, out material, out typeName))
                return true;

            return false;
        }

        private bool TryGetWallTypeAppearance(Document doc, WallType wallType, out double thicknessMm, out Material material, out string typeName)
        {
            thicknessMm = 0;
            material = null;
            typeName = null;

            if (wallType == null) return false;

            thicknessMm = GetThicknessFromWallType(wallType);
            material = GetMaterialFromWallType(doc, wallType);
            typeName = $"牆類型：{wallType.Name}";
            return thicknessMm > 0;
        }

        private bool TryGetFloorTypeAppearance(Document doc, FloorType floorType, out double thicknessMm, out Material material, out string typeName)
        {
            thicknessMm = 0;
            material = null;
            typeName = null;

            if (floorType == null) return false;

            thicknessMm = GetThicknessFromFloorType(floorType);
            material = GetMaterialFromFloorType(doc, floorType);
            typeName = $"樓板類型：{floorType.Name}";
            return thicknessMm > 0;
        }

        /// <summary>
        /// 從垂直面創建牆，並將源牆的開口孔洞切割到裝修牆上
        /// </summary>
        private ElementId CreateWallFromFace(Document doc, Element host, PlanarFace face, IList<CurveLoop> faceLoops, WallType wallType)
        {
            try
            {
                Debug.WriteLine("🧱 開始創建牆（垂直裝修面）");

                var outerLoop = GetOuterLoop(faceLoops);
                if (outerLoop == null)
                {
                    Debug.WriteLine("❌ 無法取得牆面外邊界");
                    return ElementId.InvalidElementId;
                }

                var level = GetNearestLevel(doc, face, preferBottom: true);
                if (level == null) { Debug.WriteLine("❌ 無法取得樓層"); return ElementId.InvalidElementId; }

                // 計算高度範圍
                double minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (var curve in outerLoop)
                {
                    minZ = Math.Min(minZ, Math.Min(curve.GetEndPoint(0).Z, curve.GetEndPoint(1).Z));
                    maxZ = Math.Max(maxZ, Math.Max(curve.GetEndPoint(0).Z, curve.GetEndPoint(1).Z));
                }
                double wallHeight = maxZ - minZ;

                // 底部曲線 + 偏移（讓裝修牆內側切齊結構面，不重疊）
                // 重要：若牆面被門洞切斷，單一底邊常只覆蓋局部範圍，會導致水平長度錯誤。
                // 因此優先用宿主牆 LocationCurve + 面輪廓投影取得完整水平範圍。
                Curve baseCurve = null;
                bool baseFromHostLocation = false;
                if (host is Wall hostWallForBase)
                {
                    var hostLocation = (hostWallForBase.Location as LocationCurve)?.Curve;
                    if (hostLocation != null)
                    {
                        double minParam = double.MaxValue;
                        double maxParam = double.MinValue;

                        foreach (var c in outerLoop)
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                var pt = c.GetEndPoint(i);
                                var proj = hostLocation.Project(pt);
                                if (proj == null) continue;

                                minParam = Math.Min(minParam, proj.Parameter);
                                maxParam = Math.Max(maxParam, proj.Parameter);
                            }
                        }

                        if (minParam != double.MaxValue && maxParam != double.MinValue && maxParam > minParam)
                        {
                            var start = hostLocation.Evaluate(minParam, false);
                            var end = hostLocation.Evaluate(maxParam, false);
                            // 將基線固定在面最低高程，避免沿原牆微小斜率造成高度偏差。
                            start = new XYZ(start.X, start.Y, minZ);
                            end = new XYZ(end.X, end.Y, minZ);

                            if (start.DistanceTo(end) > 1e-6)
                            {
                                baseCurve = Line.CreateBound(start, end);
                                baseFromHostLocation = true;
                                Debug.WriteLine($"✅ 使用宿主牆投影基線，長度: {baseCurve.Length * 304.8:F2} mm");
                            }
                        }
                    }
                }

                if (baseCurve == null)
                {
                    baseCurve = GetBottomCurveFromLoop(outerLoop, level);
                }

                if (baseCurve == null) { Debug.WriteLine("❌ 無法取得底部曲線"); return ElementId.InvalidElementId; }

                double wallThicknessFt = wallType.Width;
                var horizontalNormal = new XYZ(face.FaceNormal.X, face.FaceNormal.Y, 0);
                if (horizontalNormal.GetLength() > 1e-6) horizontalNormal = horizontalNormal.Normalize();

                // 若基線取自宿主牆中心線，需額外加上宿主牆半厚，才不會重疊原牆。
                double offsetDistance = wallThicknessFt / 2.0;
                if (baseFromHostLocation && host is Wall hostWallForOffset)
                {
                    offsetDistance += hostWallForOffset.Width / 2.0;
                }

                var offsetVec = horizontalNormal * offsetDistance;
                var offsetBaseCurve = Line.CreateBound(
                    baseCurve.GetEndPoint(0) + offsetVec,
                    baseCurve.GetEndPoint(1) + offsetVec);

                // 創建牆
                var wall = Wall.Create(doc, offsetBaseCurve, wallType.Id, level.Id, wallHeight, 0, false, false);
                if (wall == null) { Debug.WriteLine("❌ 牆創建失敗"); return ElementId.InvalidElementId; }

                // 設定基礎偏移
                var baseOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (baseOffsetParam != null && !baseOffsetParam.IsReadOnly)
                    baseOffsetParam.Set(minZ - level.Elevation);

                Debug.WriteLine($"✅ 牆創建成功 - ID: {wall.Id}");

                // 設定共用參數
                var material = GetMaterialFromWallType(doc, wallType);
                var thicknessMm = GetThicknessFromWallType(wallType);
                SetFinishingParameters(wall, host, thicknessMm, material, face.Area, face);

                // 🔑 核心：將源牆的開口依幾何位置切割到裝修牆上
                if (host is Wall hostWall)
                {
                    // 混合策略：
                    // 1. 優先用面內環處理窗洞/可見孔洞輪廓
                    // 2. 再用門窗參數逐一補洞；已切過的重複窗洞若失敗就略過
                    var loopCuts = CutOpeningsFromFaceInnerLoops(doc, wall, faceLoops);
                    var sourceCuts = CutOpeningsFromSourceWall(doc, hostWall, wall, processDoors: true, processWindows: true);
                    Debug.WriteLine($"✅ 開口處理完成 - 來源牆切割: {sourceCuts}, 面邊界切割: {loopCuts}");
                }

                // 實務優化：裝修牆不應反向干涉原始結構。
                // 生成完成後立即鎖住端點接合，並把任何偶發接合調整為「結構切裝修」或直接解除。
                VisualFeedbackHelper.ProtectStructureFromFinishingWall(doc, wall);

                return wall.Id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CreateWallFromFace 失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 依據源牆的開口幾何位置，在目標裝修牆上切割相同的孔洞
        /// 使用 NewOpening + 世界座標投影，確保開口位置準確
        /// </summary>
        private int CutOpeningsFromSourceWall(Document doc, Wall sourceWall, Wall targetWall, bool processDoors = true, bool processWindows = true)
        {
            try
            {
                var inserts = sourceWall.FindInserts(true, true, true, true);
                if (inserts.Count == 0) { Debug.WriteLine("  ℹ️ 源牆上沒有開口"); return 0; }

                Debug.WriteLine($"  ✂️ 開始切割 {inserts.Count} 個開口到裝修牆");

                // 目標牆的位置曲線和方向向量
                var targetLocation = (targetWall.Location as LocationCurve)?.Curve;
                if (targetLocation == null) { Debug.WriteLine("  ❌ 無法取得目標牆位置曲線"); return 0; }

                double end0 = targetLocation.GetEndParameter(0);
                double end1 = targetLocation.GetEndParameter(1);
                double pMin = Math.Min(end0, end1);
                double pMax = Math.Max(end0, end1);

                // 預留邊界容差，避免開口剛好卡在牆端點導致 API 拒絕
                double edgeTol = 1.0 / 304.8; // 1mm
                pMin += edgeTol;
                pMax -= edgeTol;

                if (pMax <= pMin)
                {
                    Debug.WriteLine("  ❌ 目標牆有效參數範圍無效");
                    return 0;
                }

                // 目標牆基準 Z（用於門/窗底高）
                double targetBaseZ = 0;
                var level = doc.GetElement(targetWall.LevelId) as Level;
                if (level != null)
                {
                    var baseOffset = targetWall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
                    targetBaseZ = level.Elevation + baseOffset;
                }

                // 來源牆基準 Z（用於將門洞高度以相對值轉移到目標牆）
                double sourceBaseZ = 0;
                var sourceLevel = doc.GetElement(sourceWall.LevelId) as Level;
                if (sourceLevel != null)
                {
                    var sourceBaseOffset = sourceWall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
                    sourceBaseZ = sourceLevel.Elevation + sourceBaseOffset;
                }

                int cutCount = 0;
                foreach (var insertId in inserts)
                {
                    try
                    {
                        var insert = doc.GetElement(insertId) as FamilyInstance;
                        if (insert == null) continue;

                        var catId = insert.Category?.Id.GetIdValue() ?? 0;
                        bool isWindow = catId == (long)BuiltInCategory.OST_Windows;
                        bool isDoor = catId == (long)BuiltInCategory.OST_Doors;

                        if (!isWindow && !isDoor)
                        {
                            continue;
                        }

                        if ((isWindow && !processWindows) || (isDoor && !processDoors))
                        {
                            continue;
                        }

                        // 取得開口的 LocationPoint（門窗中心點）
                        var insertCenter = (insert.Location as LocationPoint)?.Point;
                        if (insertCenter == null) continue;

                        // 🔑 將門窗中心點投影到目標裝修牆的位置曲線上
                        var proj = targetLocation.Project(insertCenter);
                        if (proj == null) continue;

                        // 優先使用族參數
                        double width = Math.Max(GetOpeningWidth(insert), 1e-6);
                        double height = Math.Max(GetOpeningHeight(insert), 1e-6);
                        double sill = Math.Max(GetOpeningSill(insert), 0);

                        // 若族參數異常，回退到 bbox
                        var bbox = insert.get_BoundingBox(null);
                        if ((width < 10.0 / 304.8 || height < 10.0 / 304.8) && bbox != null)
                        {
                            width = Math.Max(width, bbox.Max.DistanceTo(new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z)));
                            height = Math.Max(height, bbox.Max.Z - bbox.Min.Z);
                            sill = Math.Max(0, bbox.Min.Z - targetBaseZ);
                        }

                        // 中心參數 + 寬度範圍
                        double centerParam = proj.Parameter;
                        double halfWidth = width / 2.0;
                        double leftParam = centerParam - halfWidth;
                        double rightParam = centerParam + halfWidth;

                        // 夾制到牆有效範圍
                        leftParam = Math.Max(leftParam, pMin);
                        rightParam = Math.Min(rightParam, pMax);

                        if (rightParam - leftParam < 5.0 / 304.8)
                        {
                            Debug.WriteLine($"    ⚠️ 開口 {insert.Name} 寬度落在牆邊界外，略過");
                            continue;
                        }

                        var ptLeft = targetLocation.Evaluate(leftParam, false);
                        var ptRight = targetLocation.Evaluate(rightParam, false);

                        double bottomZ = targetBaseZ + sill;
                        double topZ = bottomZ + height;

                        // 開孔高度優先使用來源門窗 bbox 的相對高程，
                        // 這比單純依賴 Height/Sill 參數更接近實際可切除範圍。
                        if (bbox != null)
                        {
                            double relBottom = bbox.Min.Z - sourceBaseZ;
                            double relTop = bbox.Max.Z - sourceBaseZ;
                            if (relTop - relBottom > 10.0 / 304.8)
                            {
                                bottomZ = targetBaseZ + relBottom;
                                topZ = targetBaseZ + relTop;
                            }
                        }

                        // 若 bbox 高度仍無效，才回退到參數高度
                        if (topZ - bottomZ < 10.0 / 304.8)
                        {
                            bottomZ = targetBaseZ + sill;
                            topZ = bottomZ + height;
                        }

                        var openingPt1 = new XYZ(ptLeft.X, ptLeft.Y, bottomZ);
                        var openingPt2 = new XYZ(ptRight.X, ptRight.Y, topZ);

                        Debug.WriteLine($"    📐 開口 {insert.Name}: 寬={(rightParam-leftParam)*304.8:F0}mm, 高={(topZ-bottomZ)*304.8:F0}mm, 底Z={bottomZ*304.8:F0}mm");

                        doc.Create.NewOpening(targetWall, openingPt1, openingPt2);
                        cutCount++;
                        Debug.WriteLine($"    ✅ 開口切割成功");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"    ⚠️ 開口 {insertId} 切割失敗: {ex.Message}");
                    }
                }

                Debug.WriteLine($"  ✅ 共切割 {cutCount} 個開口");
                return cutCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CutOpeningsFromSourceWall 失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 由選取面的內邊界環直接回切牆面開口（門窗/編輯輪廓/牆開孔）
        /// </summary>
        private int CutOpeningsFromFaceInnerLoops(Document doc, Wall targetWall, IList<CurveLoop> faceLoops)
        {
            try
            {
                if (faceLoops == null || faceLoops.Count <= 1) return 0;

                var outerLoop = GetOuterLoop(faceLoops);
                var innerLoops = faceLoops.Where(l => !ReferenceEquals(l, outerLoop)).ToList();
                if (innerLoops.Count == 0) return 0;

                var targetLocation = (targetWall.Location as LocationCurve)?.Curve;
                if (targetLocation == null) return 0;

                double p0 = targetLocation.GetEndParameter(0);
                double p1 = targetLocation.GetEndParameter(1);
                double pMinBound = Math.Min(p0, p1);
                double pMaxBound = Math.Max(p0, p1);

                int cutCount = 0;
                foreach (var loop in innerLoops)
                {
                    try
                    {
                        var projectedPts = new List<XYZ>();
                        foreach (var sourceCurve in loop)
                        {
                            var sampled = sourceCurve.Tessellate();
                            if (sampled == null || sampled.Count == 0)
                            {
                                sampled = new List<XYZ> { sourceCurve.GetEndPoint(0), sourceCurve.GetEndPoint(1) };
                            }

                            foreach (var p in sampled)
                            {
                                var mp = ProjectPointToTargetWall(p, targetLocation, pMinBound, pMaxBound);
                                if (mp != null) projectedPts.Add(mp);
                            }
                        }

                        if (projectedPts.Count < 2)
                        {
                            Debug.WriteLine("    ⚠️ 內環可用投影點不足，略過");
                            continue;
                        }

                        var minParam = double.MaxValue;
                        var maxParam = double.MinValue;
                        var minZ = double.MaxValue;
                        var maxZ = double.MinValue;

                        foreach (var p in projectedPts)
                        {
                            var proj = targetLocation.Project(p);
                            if (proj == null) continue;

                            minParam = Math.Min(minParam, proj.Parameter);
                            maxParam = Math.Max(maxParam, proj.Parameter);
                            minZ = Math.Min(minZ, p.Z);
                            maxZ = Math.Max(maxZ, p.Z);
                        }

                        if (minParam == double.MaxValue || maxParam == double.MinValue || maxParam - minParam < 1e-6 || maxZ - minZ < 1e-6)
                        {
                            Debug.WriteLine("    ⚠️ 內環投影範圍無效，略過");
                            continue;
                        }

                        // 優先使用 Wall+兩點矩形開口（Revit 最穩定）
                        var pOnMin = targetLocation.Evaluate(minParam, false);
                        var pOnMax = targetLocation.Evaluate(maxParam, false);
                        var openingPt1 = new XYZ(pOnMin.X, pOnMin.Y, minZ);
                        var openingPt2 = new XYZ(pOnMax.X, pOnMax.Y, maxZ);

                        try
                        {
                            doc.Create.NewOpening(targetWall, openingPt1, openingPt2);
                        }
                        catch
                        {
                            // 回退：嘗試原輪廓模式
                            var curveArray = new CurveArray();
                            int segmentCount = 0;
                            foreach (var sourceCurve in loop)
                            {
                                var mappedCurves = ProjectCurveToTargetWall(sourceCurve, targetLocation, pMinBound, pMaxBound);
                                foreach (var mapped in mappedCurves)
                                {
                                    curveArray.Append(mapped);
                                    segmentCount++;
                                }
                            }

                            if (segmentCount < 3)
                            {
                                Debug.WriteLine("    ⚠️ 輪廓回退模式邊數不足，略過");
                                continue;
                            }

                            doc.Create.NewOpening(targetWall, curveArray, true);
                        }

                        cutCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"    ⚠️ 面邊界開口切割失敗: {ex.Message}");
                    }
                }

                return cutCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CutOpeningsFromFaceInnerLoops 失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 將來源面的曲線投影到目標牆中心線，保留 Z 高度來重建開口輪廓
        /// </summary>
        private List<Curve> ProjectCurveToTargetWall(Curve sourceCurve, Curve targetLocation, double paramMin, double paramMax)
        {
            var mapped = new List<Curve>();
            try
            {
                if (sourceCurve == null || targetLocation == null) return mapped;

                var s0 = ProjectPointToTargetWall(sourceCurve.GetEndPoint(0), targetLocation, paramMin, paramMax);
                var s1 = ProjectPointToTargetWall(sourceCurve.GetEndPoint(1), targetLocation, paramMin, paramMax);

                if (s0 == null || s1 == null || s0.DistanceTo(s1) < 1e-6) return mapped;

                if (sourceCurve is Line)
                {
                    mapped.Add(Line.CreateBound(s0, s1));
                    return mapped;
                }

                if (sourceCurve is Arc)
                {
                    var mid = ProjectPointToTargetWall(sourceCurve.Evaluate(0.5, true), targetLocation, paramMin, paramMax);
                    if (mid != null)
                    {
                        try
                        {
                            mapped.Add(Arc.Create(s0, s1, mid));
                            return mapped;
                        }
                        catch
                        {
                            // 弧線重建失敗時，退回折線近似
                        }
                    }
                }

                var pts = sourceCurve.Tessellate();
                if (pts != null && pts.Count >= 2)
                {
                    XYZ prev = null;
                    foreach (var p in pts)
                    {
                        var mp = ProjectPointToTargetWall(p, targetLocation, paramMin, paramMax);
                        if (mp == null) continue;

                        if (prev != null && prev.DistanceTo(mp) > 1e-6)
                        {
                            mapped.Add(Line.CreateBound(prev, mp));
                        }
                        prev = mp;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"    ⚠️ ProjectCurveToTargetWall 失敗: {ex.Message}");
            }

            return mapped;
        }

        /// <summary>
        /// 將點投影到牆中心線，並夾制在牆長度範圍內
        /// </summary>
        private XYZ ProjectPointToTargetWall(XYZ point, Curve targetLocation, double paramMin, double paramMax)
        {
            try
            {
                if (point == null || targetLocation == null) return null;
                var proj = targetLocation.Project(point);
                if (proj == null) return null;

                var p = Math.Max(paramMin, Math.Min(paramMax, proj.Parameter));
                var onLine = targetLocation.Evaluate(p, false);
                return new XYZ(onLine.X, onLine.Y, point.Z);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 從水平面創建樓板
        /// </summary>
        private ElementId CreateFloorFromFace(Document doc, Element host, PlanarFace face, IList<CurveLoop> curveLoops, FloorType floorType)
        {
            try
            {
                Debug.WriteLine("🏢 開始創建樓板（水平裝修面）");
                Debug.WriteLine($"  使用樓板類型: {floorType.Name}");

                var outerLoop = GetOuterLoop(curveLoops);
                if (outerLoop == null)
                {
                    Debug.WriteLine("❌ 無法取得樓板外邊界");
                    return ElementId.InvalidElementId;
                }

                // 🎯 關鍵判斷：根據法向量判斷是頂面還是底面
                var normal = face.FaceNormal;
                bool isTopFace = normal.Z > 0; // 法向量向上 → 頂面
                bool isBottomFace = normal.Z < 0; // 法向量向下 → 底面

                Debug.WriteLine($"📐 水平面方向 - 法向量 Z: {normal.Z:F3}");
                Debug.WriteLine($"📐 判斷結果 - 頂面: {isTopFace}, 底面: {isBottomFace}");

                // 取得樓層
                var level = GetNearestLevel(doc, face, preferBottom: false);
                if (level == null)
                {
                    Debug.WriteLine("❌ 無法取得樓層");
                    return ElementId.InvalidElementId;
                }

                // 從 CurveLoop 計算平均 Z 值（面的位置）
                double avgZ = 0;
                int pointCount = 0;
                foreach (var curve in outerLoop)
                {
                    avgZ += curve.GetEndPoint(0).Z;
                    pointCount++;
                }
                avgZ /= pointCount;

                // 邊界原則：裝修板範圍應忠實跟隨使用者選取的面，不能為了避干涉而縮掉有效範圍。
                // 與原始結構的關係，改由「垂直淨空 + 禁止接合」處理，而不是改小平面邊界。
                var safeCurveLoops = curveLoops.ToList();

                // 創建樓板（保留內邊界環，支援孔洞）
                var floor = Floor.Create(doc, safeCurveLoops, floorType.Id, level.Id);
                if (floor == null)
                {
                    Debug.WriteLine("❌ 樓板創建失敗");
                    return ElementId.InvalidElementId;
                }

                Debug.WriteLine($"✅ 樓板創建成功 - ID: {floor.Id}");

                // 🎯 關鍵：根據頂面/底面調整樓板位置
                // 從樓板類型中取得材料和厚度
                var material = GetMaterialFromFloorType(doc, floorType);
                var thicknessMm = GetThicknessFromFloorType(floorType);

                // ⚠️ 重要：Revit 的樓板是向下生長的！
                // FLOOR_HEIGHTABOVELEVEL_PARAM 是樓板頂部相對於樓層的高度
                double thicknessFt = thicknessMm / 304.8;
                double clearanceFt = 1.0 / 304.8; // 1 mm 安全淨空，避免與原始結構共面後被視為互相干涉
                double heightOffset = avgZ - level.Elevation;

                if (isTopFace)
                {
                    // 頂面：裝修板位於結構面上方，底部與結構面保持極小淨空
                    heightOffset += thicknessFt + clearanceFt;
                    Debug.WriteLine($"  ℹ️ 頂面：裝修樓板位於結構面上方，保留 {clearanceFt * 304.8:F2} mm 淨空");
                }
                else if (isBottomFace)
                {
                    // 底面：裝修板懸掛於結構底面下方，頂部與結構面保持極小淨空
                    heightOffset -= clearanceFt;
                    Debug.WriteLine($"  ℹ️ 底面：裝修樓板位於結構底面下方，保留 {clearanceFt * 304.8:F2} mm 淨空");
                }

                var heightParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                if (heightParam != null && !heightParam.IsReadOnly)
                {
                    heightParam.Set(heightOffset);
                    Debug.WriteLine($"✅ 設定樓板高度偏移: {heightOffset * 304.8:F2} mm");
                }

                // 🎯 牆/樓板模式：使用 Revit 本身的材質系統，不需要視圖覆蓋
                Debug.WriteLine($"  ℹ️ 樓板類型「{floorType.Name}」將使用 Revit 本身的材質顯示");

                // 設定共用參數
                SetFinishingParameters(floor, host, thicknessMm, material, face.Area, face);

                // 保護原始結構：裝修板不得反向干涉宿主結構
                VisualFeedbackHelper.ProtectStructureFromFinishingElement(doc, floor);

                return floor.Id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CreateFloorFromFace 失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 使用一般模型（DirectShape）創建裝修面
        /// </summary>
        private ElementId CreateGenericModelFromFace(Document doc, Element host, PlanarFace face, CurveLoop curveLoop, double thicknessMm, Material material, bool skipStructuralCut = false)
        {
            try
            {
                Debug.WriteLine("🎨 開始創建一般模型（裝修面）");

                // 轉換厚度（mm → feet）
                double thicknessFt = thicknessMm / 304.8;

                // 取得面的法向量
                var normal = face.FaceNormal;
                Debug.WriteLine($"📐 面的法向量: ({normal.X:F3}, {normal.Y:F3}, {normal.Z:F3})");

                // 創建擠出實體
                var solid = CreateExtrudedSolid(curveLoop, normal, thicknessFt);
                if (solid == null || solid.Volume < 1e-6)
                {
                    Debug.WriteLine("❌ 無法創建擠出實體");
                    return ElementId.InvalidElementId;
                }

                Debug.WriteLine($"✅ 創建擠出實體成功，體積: {solid.Volume * Math.Pow(304.8, 3):F2} mm³");

                // 🎯 使用布林運算切割掉與所有結構元素重疊的部分。
                // 柱面例外：柱常貼牆/梁/板，鄰近結構扣除會把有效柱面吃掉。
                if (!skipStructuralCut)
                    solid = CutSolidWithStructuralElements(doc, solid, host, face);
                else
                    Debug.WriteLine("🏛️ 柱面專用路徑：略過鄰近結構扣除，保留選取柱面");

                if (solid == null || solid.Volume < 1e-6)
                {
                    Debug.WriteLine($"❌ 布林切割後實體無效或體積為 0");
                    return ElementId.InvalidElementId;
                }

                double originalAreaM2 = face.Area * 0.09290304; // ft² → m²，僅作為厚度異常時的備援
                Debug.WriteLine($"📐 原始面面積: {originalAreaM2:F4} m²");

                // 🎯 分解實體為多個獨立的實體（如果被切割成多個片段）
                var separatedSolids = SeparateSolids(solid);
                Debug.WriteLine($"📊 分解後得到 {separatedSolids.Count} 個獨立實體");

                // 為每個獨立實體創建 DirectShape
                var createdIds = new List<ElementId>();
                int index = 1;

                foreach (var separatedSolid in separatedSolids)
                {
                    if (separatedSolid == null || separatedSolid.Volume < 1e-6)
                    {
                        Debug.WriteLine($"  ⚠️ 跳過體積為 0 的實體");
                        continue;
                    }

                    // 一般模型是有厚度的實體；實際可計量面積應依扣除後體積 / 厚度回推。
                    // 這樣門窗洞、鄰近結構扣除與分片後的數量才會與實體一致。
                    double volumeMm3 = separatedSolid.Volume * Math.Pow(304.8, 3);
                    double fragmentAreaM2 = CalculateAreaFromVolumeAndThickness(separatedSolid, thicknessFt, originalAreaM2);

                    Debug.WriteLine($"  📐 實體 {index}: 體積={volumeMm3:F2} mm³, 厚度={thicknessMm:F1} mm, 面積={fragmentAreaM2:F4} m²");

                    // 使用 DirectShape 創建一般模型
                    var directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    directShape.ApplicationId = "HB_BIM_Finishings";
                    directShape.ApplicationDataId = "FaceToFace_GenericModel";
                    directShape.SetShape(new GeometryObject[] { separatedSolid });

                    string shapeName = separatedSolids.Count > 1
                        ? $"裝修面_{thicknessMm:F0}mm_{material?.Name ?? "未指定"}_{index}"
                        : $"裝修面_{thicknessMm:F0}mm_{material?.Name ?? "未指定"}";

                    directShape.Name = shapeName;

                    Debug.WriteLine($"  ✅ 創建 DirectShape 成功 - ID: {directShape.Id}, 名稱: {shapeName}");

                    // 設定材料和視圖覆蓋
                    ApplyMaterialOverride(doc, directShape, material);

                    // 設定共用參數（使用按比例分配的面積）
                    SetFinishingParameters(directShape, host, thicknessMm, material, fragmentAreaM2, face);
                    VisualFeedbackHelper.ProtectStructureFromFinishingElement(doc, directShape);

                    createdIds.Add(directShape.Id);
                    index++;
                }

                // 返回第一個創建的元素 ID（如果有多個，後續可以透過 _createdElementIds 取得）
                if (createdIds.Count > 0)
                {
                    // 將所有創建的 ID 加入到 _createdElementIds 以便高亮顯示
                    _createdElementIds.AddRange(createdIds);

                    Debug.WriteLine($"✅ 共創建 {createdIds.Count} 個裝修面元素");
                    return createdIds[0];
                }
                else
                {
                    Debug.WriteLine($"❌ 未能創建任何裝修面元素");
                    return ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CreateGenericModelFromFace 失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 創建擠出實體
        /// </summary>
        private Solid CreateExtrudedSolid(CurveLoop profile, XYZ direction, double distance)
        {
            try
            {
                var curveLoops = new List<CurveLoop> { profile };
                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(curveLoops, direction, distance);
                return solid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ CreateExtrudedSolid 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 使用布林運算切割掉與所有結構元素和已創建裝修面重疊的部分（完全參考面選模板的實現）
        /// </summary>
        private Solid CutSolidWithStructuralElements(Document doc, Solid solid, Element host, PlanarFace face)
        {
            try
            {
                Debug.WriteLine($"🔧 開始從裝修面中扣除相鄰元件（參考面選模板邏輯）");

                // 🎯 關鍵：參考面選模板的搜尋邏輯
                // 使用 厚度 + 3000mm 作為搜尋半徑（確保能找到所有相鄰結構）
                double thicknessMm = solid.Volume * Math.Pow(304.8, 3) / (face.Area * Math.Pow(304.8, 2));
                double searchRadiusMm = thicknessMm + 3000.0;
                double searchRadiusFt = searchRadiusMm / 304.8;

                Debug.WriteLine($"  🔍 搜尋半徑: {searchRadiusMm:F0}mm ({searchRadiusFt:F2}ft)");

                // 🎯 使用宿主元素的邊界框（參考 GeometryExtractor）
                var hostBBox = host.get_BoundingBox(null);
                if (hostBBox == null)
                {
                    Debug.WriteLine($"  ⚠️ 無法取得宿主元素的邊界框");
                    return solid;
                }

                // 擴大邊界框（參考 GeometryExtractor.GetNearbyStructuralElementsFromFace）
                var expandedBounds = new BoundingBoxXYZ
                {
                    Min = hostBBox.Min - new XYZ(searchRadiusFt, searchRadiusFt, searchRadiusFt),
                    Max = hostBBox.Max + new XYZ(searchRadiusFt, searchRadiusFt, searchRadiusFt)
                };

                var outline = new Outline(expandedBounds.Min, expandedBounds.Max);
                var bbFilter = new BoundingBoxIntersectsFilter(outline);

                // 🎯 參考面選模板：收集結構元素類別 + 門窗開孔類別（用於開孔）
                var structuralCategories = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Columns,
                    BuiltInCategory.OST_GenericModel,   // 包含已創建的裝修面
                    BuiltInCategory.OST_Doors,          // 🚪 門（用於開孔）
                    BuiltInCategory.OST_Windows,        // 🪟 窗（用於開孔）
                    BuiltInCategory.OST_ShaftOpening    // 🔲 軸開孔（用於開孔）
                };

                var nearbyElements = new List<Element>();

                // 🎯 參考 GeometryExtractor：分類別收集元素
                foreach (var category in structuralCategories)
                {
                    try
                    {
                        var collector = new FilteredElementCollector(doc)
                            .OfCategory(category)
                            .WhereElementIsNotElementType()
                            .WherePasses(bbFilter);

                        foreach (var element in collector)
                        {
                            if (element.Id != host.Id) // 排除宿主元素本身
                            {
                                nearbyElements.Add(element);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 搜索類別 {category} 時出錯: {ex.Message}");
                    }
                }

                Debug.WriteLine($"  📊 找到 {nearbyElements.Count} 個附近元素");

                // 分類處理：先切割結構元素，再切割已創建的裝修面
                var structuralElements = new List<Element>();
                var finishingElements = new List<Element>();

                foreach (var element in nearbyElements)
                {
                    if (element.Category?.Id.GetIdValue() == (long)BuiltInCategory.OST_GenericModel)
                    {
                        // 只處理已經創建的裝修面（在 _createdElementIds 中）
                        if (_createdElementIds.Contains(element.Id))
                        {
                            finishingElements.Add(element);
                        }
                    }
                    else
                    {
                        structuralElements.Add(element);
                    }
                }

                Debug.WriteLine($"  📊 結構元素: {structuralElements.Count} 個, 已創建裝修面: {finishingElements.Count} 個");

                // 🎯 關鍵：參考面選模板的扣除邏輯
                Solid finalFormwork = solid;
                int subtractedCount = 0;

                // 🎯 關鍵修正：只對宿主牆/樓板進行開口切割（而不是所有附近的元素）
                if (host is Wall hostWall)
                {
                    Debug.WriteLine($"  🚪 宿主是牆，開始切割牆上的門窗開口");
                    Debug.WriteLine($"  📊 切割前體積: {finalFormwork.Volume * Math.Pow(304.8, 3):F2} mm³ ({finalFormwork.Volume:F6} ft³)");

                    finalFormwork = CutWallOpeningsFromSolid(doc, hostWall, finalFormwork, ref subtractedCount);

                    if (finalFormwork == null)
                    {
                        Debug.WriteLine($"❌ 切割門窗開口後實體為 null");
                        return null;
                    }

                    Debug.WriteLine($"  📊 切割後體積: {finalFormwork.Volume * Math.Pow(304.8, 3):F2} mm³ ({finalFormwork.Volume:F6} ft³)");

                    if (finalFormwork.Volume <= 1e-6)
                    {
                        Debug.WriteLine($"❌ 切割門窗開口後實體體積過小: {finalFormwork.Volume:F9} ft³");
                        return null;
                    }
                }
                else if (host is Floor hostFloor)
                {
                    Debug.WriteLine($"  🏢 宿主是樓板，開始切割樓板上的開口");
                    finalFormwork = CutFloorOpeningsFromSolid(doc, hostFloor, finalFormwork, ref subtractedCount);
                    if (finalFormwork == null || finalFormwork.Volume <= 1e-6)
                    {
                        Debug.WriteLine($"❌ 切割樓板開口後實體無效");
                        return null;
                    }
                }

                // 第一階段：從裝修面中扣除結構元件（參考 CmdPickFace.cs 第 1006-1027 行）
                foreach (var nearbyElement in structuralElements)
                {
                    var categoryName = nearbyElement.Category?.Name ?? "未知類別";
                    var elementName = nearbyElement.Name ?? $"ID:{nearbyElement.Id.GetIdValue()}";

                    Debug.WriteLine($"  🔍 處理元素: {categoryName} - {elementName} (ID: {nearbyElement.Id.GetIdValue()})");

                    // 🎯 跳過宿主牆的門窗切割（已經在上面處理過了）
                    // 不再對每個附近的牆進行門窗切割

                    var nearbySolids = GetElementSolids(nearbyElement);

                    if (nearbySolids.Count == 0)
                    {
                        Debug.WriteLine($"    ⚠️ 無法取得實體幾何");
                        continue;
                    }

                    Debug.WriteLine($"    ✅ 取得 {nearbySolids.Count} 個實體");

                    foreach (var nearbySolid in nearbySolids)
                    {
                        if (nearbySolid == null || nearbySolid.Volume <= 1e-6)
                        {
                            Debug.WriteLine($"    ⚠️ 實體無效或體積過小: {nearbySolid?.Volume:F9}");
                            continue;
                        }

                        try
                        {
                            // 🎯 直接執行布林差集（不檢查相交比例，與面選模板一致）
                            var tempSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                finalFormwork, nearbySolid, BooleanOperationsType.Difference);

                            if (tempSolid != null && tempSolid.Volume > 1e-6)
                            {
                                finalFormwork = tempSolid;
                                subtractedCount++;
                                Debug.WriteLine($"    ✅ 成功扣除，剩餘體積: {finalFormwork.Volume * Math.Pow(304.8, 3):F2} mm³");
                            }
                            else
                            {
                                Debug.WriteLine($"    ⚠️ 布林運算結果無效");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"    ❌ 布林運算失敗: {ex.Message}");
                        }
                    }
                }

                // 第二階段：從裝修面中扣除已創建的裝修面（避免重疊）
                foreach (var nearbyElement in finishingElements)
                {
                    var nearbySolids = GetElementSolids(nearbyElement);
                    if (nearbySolids.Count == 0) continue;

                    foreach (var nearbySolid in nearbySolids)
                    {
                        if (nearbySolid == null || nearbySolid.Volume <= 1e-6) continue;

                        try
                        {
                            var tempSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                finalFormwork, nearbySolid, BooleanOperationsType.Difference);

                            if (tempSolid != null && tempSolid.Volume > 1e-6)
                            {
                                finalFormwork = tempSolid;
                                subtractedCount++;
                                Debug.WriteLine($"  ✅ 從裝修面扣除已創建裝修面 {nearbyElement.Id}, 剩餘體積: {finalFormwork.Volume * Math.Pow(304.8, 3):F2} mm³");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"  ⚠️ 從裝修面扣除裝修面 {nearbyElement.Id} 失敗: {ex.Message}");
                        }
                    }
                }

                // 檢查最終體積
                if (finalFormwork == null || finalFormwork.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 扣除後裝修面體積過小: {finalFormwork?.Volume:F9}");
                    return null;
                }

                Debug.WriteLine($"✅ 裝修面扣除完成，共扣除 {subtractedCount} 個元件，最終體積: {finalFormwork.Volume * Math.Pow(304.8, 3):F2} mm³");
                return finalFormwork;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CutSolidWithStructuralElements 失敗: {ex.Message}");
                return solid;
            }
        }

        /// <summary>
        /// 從實體中切割牆上的門窗開口（參考面生模板邏輯）
        /// </summary>
        private Solid CutWallOpeningsFromSolid(Document doc, Wall wall, Solid solid, ref int subtractedCount)
        {
            try
            {
                Debug.WriteLine($"  🚪 開始處理牆 {wall.Id} 上的門窗開口");
                System.Diagnostics.Trace.WriteLine($"  🚪 開始處理牆 {wall.Id} 上的門窗開口");
                Debug.WriteLine($"  📊 輸入實體體積: {solid.Volume * Math.Pow(304.8, 3):F2} mm³ ({solid.Volume:F6} ft³)");

                // 找到牆上的所有門窗
                var openings = wall.FindInserts(true, true, true, true);
                if (openings.Count == 0)
                {
                    Debug.WriteLine($"    ℹ️ 牆上沒有門窗開口");
                    System.Diagnostics.Trace.WriteLine($"    ℹ️ 牆上沒有門窗開口");
                    return solid;
                }

                Debug.WriteLine($"    📊 找到 {openings.Count} 個門窗開口");
                System.Diagnostics.Trace.WriteLine($"    📊 找到 {openings.Count} 個門窗開口");

                // 🎯 診斷：統計門窗數量並顯示對話框
                int windowCount = 0;
                int doorCount = 0;
                var diagDetails = new System.Text.StringBuilder();
                diagDetails.AppendLine($"牆 ID: {wall.Id.GetIdValue()}");
                diagDetails.AppendLine($"總開口數: {openings.Count}");
                diagDetails.AppendLine("");

                foreach (var openingId in openings)
                {
                    var opening = doc.GetElement(openingId) as FamilyInstance;
                    if (opening != null)
                    {
                        var catId = opening.Category?.Id.GetIdValue() ?? 0;
                        var catName = opening.Category?.Name ?? "未知";
                        var familyName = opening.Symbol?.FamilyName ?? "未知";
                        var typeName = opening.Symbol?.Name ?? "未知";

                        if (catId == (long)BuiltInCategory.OST_Windows)
                        {
                            windowCount++;
                            diagDetails.AppendLine($"窗 {windowCount}: {familyName} - {typeName}");
                        }
                        else if (catId == (long)BuiltInCategory.OST_Doors)
                        {
                            doorCount++;
                            diagDetails.AppendLine($"門 {doorCount}: {familyName} - {typeName}");
                        }
                        else
                        {
                            diagDetails.AppendLine($"其他 ({catName}): {familyName} - {typeName}");
                        }
                    }
                }

                diagDetails.AppendLine("");
                diagDetails.AppendLine($"窗: {windowCount} 個");
                diagDetails.AppendLine($"門: {doorCount} 個");

                TaskDialog.Show("🔍 門窗開口診斷", diagDetails.ToString());

                Solid resultSolid = solid;

                // 🎯 診斷：收集幾何提取資訊
                var geomDiag = new System.Text.StringBuilder();
                geomDiag.AppendLine("🔍 幾何提取診斷");
                geomDiag.AppendLine("");

                foreach (var openingId in openings)
                {
                    var opening = doc.GetElement(openingId) as FamilyInstance;
                    if (opening == null) continue;

                    var openingName = opening.Name ?? $"ID:{openingId.GetIdValue()}";
                    Debug.WriteLine($"    🔍 處理開口: {openingName} (ID: {openingId.GetIdValue()})");

                    try
                    {
                        // 🎯 診斷：輸出開口的類別和類型
                        var categoryName = opening.Category?.Name ?? "未知類別";
                        var familyName = opening.Symbol?.FamilyName ?? "未知族群";
                        var typeName = opening.Symbol?.Name ?? "未知類型";
                        Debug.WriteLine($"      📋 開口資訊: 類別={categoryName}, 族群={familyName}, 類型={typeName}");

                        geomDiag.AppendLine($"📋 {categoryName}: {familyName} - {typeName}");

                        // 🎯 直接使用邊界框創建開口實體（不提取窗戶的實體幾何）
                        var openingBBox = opening.get_BoundingBox(null);
                        if (openingBBox != null)
                        {
                            Debug.WriteLine($"      📊 邊界框: Min=({openingBBox.Min.X:F2}, {openingBBox.Min.Y:F2}, {openingBBox.Min.Z:F2}), Max=({openingBBox.Max.X:F2}, {openingBBox.Max.Y:F2}, {openingBBox.Max.Z:F2})");

                            // 擴大邊界框以確保完全切割
                            var expandedMin = openingBBox.Min - new XYZ(0.1, 0.1, 0.1);
                            var expandedMax = openingBBox.Max + new XYZ(0.1, 0.1, 0.1);

                            // 創建邊界框實體
                            var boxSolid = CreateBoxSolid(expandedMin, expandedMax);
                            if (boxSolid != null && boxSolid.Volume > 1e-6)
                            {
                                Debug.WriteLine($"      📊 開口邊界框體積: {boxSolid.Volume * Math.Pow(304.8, 3):F2} mm³");
                                geomDiag.AppendLine($"   開口邊界框體積 = {boxSolid.Volume * Math.Pow(304.8, 3):F2} mm³");

                                try
                                {
                                    var tempSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                        resultSolid, boxSolid, BooleanOperationsType.Difference);

                                    if (tempSolid != null && tempSolid.Volume > 1e-6)
                                    {
                                        resultSolid = tempSolid;
                                        subtractedCount++;
                                        Debug.WriteLine($"      ✅ 成功切割開口，剩餘體積: {resultSolid.Volume * Math.Pow(304.8, 3):F2} mm³");
                                        geomDiag.AppendLine($"   ✅ 布林運算成功！");
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"      ⚠️ 布林運算結果無效");
                                        geomDiag.AppendLine($"   ⚠️ 布林運算結果無效");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"      ⚠️ 布林運算失敗: {ex.Message}");
                                    geomDiag.AppendLine($"   ❌ 布林運算失敗: {ex.Message}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"      ❌ 無法創建邊界框實體");
                                geomDiag.AppendLine($"   ❌ 無法創建邊界框實體");
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"      ❌ 無法取得邊界框");
                            geomDiag.AppendLine($"   ❌ 無法取得邊界框");
                        }

                        geomDiag.AppendLine("");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"    ❌ 處理開口 {openingId} 失敗: {ex.Message}");
                        geomDiag.AppendLine($"   ❌ 處理失敗: {ex.Message}");
                        geomDiag.AppendLine("");
                    }
                }

                // 🎯 顯示幾何提取診斷對話框
                geomDiag.AppendLine($"總計成功切割: {subtractedCount} 個開口");
                TaskDialog.Show("🔍 幾何提取診斷", geomDiag.ToString());

                Debug.WriteLine($"  ✅ 完成門窗開口切割，共切割 {openings.Count} 個開口");
                return resultSolid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ❌ CutWallOpeningsFromSolid 失敗: {ex.Message}");
                return solid;
            }
        }

        /// <summary>
        /// 從實體中切割樓板上的開口（包括外部開口和輪廓開口）
        /// </summary>
        private Solid CutFloorOpeningsFromSolid(Document doc, Floor floor, Solid solid, ref int subtractedCount)
        {
            try
            {
                Debug.WriteLine($"  🏢 開始處理樓板 {floor.Id} 上的開口");
                System.Diagnostics.Trace.WriteLine($"  🏢 開始處理樓板 {floor.Id} 上的開口");

                var resultSolid = solid;
                int totalOpenings = 0;

                // 🎯 診斷：收集幾何提取資訊
                var geomDiag = new System.Text.StringBuilder();
                geomDiag.AppendLine("🔍 樓板開口診斷");
                geomDiag.AppendLine($"樓板 ID: {floor.Id.GetIdValue()}");
                geomDiag.AppendLine("");

                // 方法 1：處理外部開口（軸開孔、樓梯開口等）
                var externalOpenings = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ShaftOpening)
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .ToList();

                geomDiag.AppendLine($"📊 找到 {externalOpenings.Count} 個軸開孔");
                Debug.WriteLine($"    📊 找到 {externalOpenings.Count} 個軸開孔");

                foreach (var opening in externalOpenings)
                {
                    try
                    {
                        var openingName = opening.Name ?? $"ID:{opening.Id.GetIdValue()}";
                        geomDiag.AppendLine($"📋 軸開孔: {openingName}");

                        // 取得開口的實體幾何
                        var openingSolids = GetElementSolids(opening);
                        geomDiag.AppendLine($"   提取到 {openingSolids.Count} 個實體");

                        if (openingSolids.Count > 0)
                        {
                            int solidIdx = 0;
                            foreach (var openingSolid in openingSolids)
                            {
                                solidIdx++;
                                if (openingSolid == null || openingSolid.Volume <= 1e-6)
                                {
                                    geomDiag.AppendLine($"   實體 {solidIdx}: 體積太小");
                                    continue;
                                }

                                geomDiag.AppendLine($"   實體 {solidIdx}: 體積 = {openingSolid.Volume * Math.Pow(304.8, 3):F2} mm³");

                                try
                                {
                                    var tempSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                        resultSolid, openingSolid, BooleanOperationsType.Difference);

                                    if (tempSolid != null && tempSolid.Volume > 1e-6)
                                    {
                                        resultSolid = tempSolid;
                                        subtractedCount++;
                                        totalOpenings++;
                                        geomDiag.AppendLine($"   ✅ 布林運算成功！");
                                        Debug.WriteLine($"      ✅ 成功切割開口");
                                    }
                                    else
                                    {
                                        geomDiag.AppendLine($"   ⚠️ 布林運算結果無效");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    geomDiag.AppendLine($"   ❌ 布林運算失敗: {ex.Message}");
                                    Debug.WriteLine($"      ⚠️ 布林運算失敗: {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            geomDiag.AppendLine($"   ⚠️ 無法提取幾何");
                        }

                        geomDiag.AppendLine("");
                    }
                    catch (Exception ex)
                    {
                        geomDiag.AppendLine($"   ❌ 處理失敗: {ex.Message}");
                        geomDiag.AppendLine("");
                        Debug.WriteLine($"    ❌ 處理開口 {opening.Id} 失敗: {ex.Message}");
                    }
                }

                // 方法 2：處理樓板輪廓開口（從 Sketch 中獲取）
                try
                {
                    // 🎯 使用 SketchId 獲取 Sketch（適用於所有 Revit 版本）
                    Sketch sketch = null;
                    if (floor.SketchId != null && floor.SketchId != ElementId.InvalidElementId)
                    {
                        sketch = doc.GetElement(floor.SketchId) as Sketch;
                    }
                    if (sketch != null)
                    {
                        var profile = sketch.Profile;
                        if (profile != null && profile.Size > 1)
                        {
                            // profile.Size > 1 表示有內部洞口（第一個是外輪廓，其餘是洞口）
                            int holeCount = profile.Size - 1;
                            geomDiag.AppendLine($"📊 找到 {holeCount} 個輪廓開口");
                            Debug.WriteLine($"    📊 找到 {holeCount} 個輪廓開口");

                            // 從第二個 CurveArray 開始（第一個是外輪廓）
                            for (int i = 1; i < profile.Size; i++)
                            {
                                var holeProfile = profile.get_Item(i);
                                if (holeProfile == null || holeProfile.Size == 0) continue;

                                try
                                {
                                    geomDiag.AppendLine($"📋 輪廓開口 {i}: {holeProfile.Size} 條曲線");

                                    // 將 CurveArray 轉換為 CurveLoop
                                    var curves = new List<Curve>();
                                    foreach (Curve curve in holeProfile)
                                    {
                                        curves.Add(curve);
                                    }

                                    if (curves.Count < 3)
                                    {
                                        geomDiag.AppendLine($"   ⚠️ 曲線數量不足（{curves.Count}）");
                                        continue;
                                    }

                                    var curveLoop = CurveLoop.Create(curves);

                                    // 取得樓板的高度和厚度
                                    var floorBBox = floor.get_BoundingBox(null);
                                    if (floorBBox == null)
                                    {
                                        geomDiag.AppendLine($"   ⚠️ 無法取得樓板邊界框");
                                        continue;
                                    }

                                    double minZ = floorBBox.Min.Z;
                                    double maxZ = floorBBox.Max.Z;
                                    double height = maxZ - minZ + 0.1; // 稍微加高以確保完全切割

                                    // 創建拉伸實體（從樓板底部到頂部）
                                    var holeSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                                        new List<CurveLoop> { curveLoop },
                                        XYZ.BasisZ,
                                        height);

                                    if (holeSolid != null && holeSolid.Volume > 1e-6)
                                    {
                                        // 將實體移動到正確的高度
                                        var transform = Transform.CreateTranslation(new XYZ(0, 0, minZ - 0.05));
                                        holeSolid = SolidUtils.CreateTransformed(holeSolid, transform);

                                        geomDiag.AppendLine($"   開口實體體積 = {holeSolid.Volume * Math.Pow(304.8, 3):F2} mm³");

                                        try
                                        {
                                            var tempSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                                resultSolid, holeSolid, BooleanOperationsType.Difference);

                                            if (tempSolid != null && tempSolid.Volume > 1e-6)
                                            {
                                                resultSolid = tempSolid;
                                                subtractedCount++;
                                                totalOpenings++;
                                                geomDiag.AppendLine($"   ✅ 布林運算成功！");
                                                Debug.WriteLine($"      ✅ 成功切割輪廓開口 {i}");
                                            }
                                            else
                                            {
                                                geomDiag.AppendLine($"   ⚠️ 布林運算結果無效");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            geomDiag.AppendLine($"   ❌ 布林運算失敗: {ex.Message}");
                                            Debug.WriteLine($"      ⚠️ 布林運算失敗: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        geomDiag.AppendLine($"   ⚠️ 無法創建開口實體");
                                    }

                                    geomDiag.AppendLine("");
                                }
                                catch (Exception ex)
                                {
                                    geomDiag.AppendLine($"   ❌ 處理失敗: {ex.Message}");
                                    geomDiag.AppendLine("");
                                    Debug.WriteLine($"    ❌ 處理輪廓開口 {i} 失敗: {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            geomDiag.AppendLine($"📊 沒有輪廓開口");
                            Debug.WriteLine($"    ℹ️ 樓板沒有輪廓開口");
                        }
                    }
                    else
                    {
                        geomDiag.AppendLine($"⚠️ 無法取得樓板 Sketch");
                        Debug.WriteLine($"    ⚠️ 無法取得樓板 Sketch");
                    }
                }
                catch (Exception ex)
                {
                    geomDiag.AppendLine($"❌ 處理輪廓開口失敗: {ex.Message}");
                    Debug.WriteLine($"    ❌ 處理輪廓開口失敗: {ex.Message}");
                }

                // 🎯 顯示診斷對話框
                geomDiag.AppendLine("");
                geomDiag.AppendLine($"總計成功切割: {totalOpenings} 個開口");
                TaskDialog.Show("🔍 樓板開口診斷", geomDiag.ToString());

                Debug.WriteLine($"  ✅ 完成樓板開口切割，共切割 {totalOpenings} 個開口");
                return resultSolid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ❌ CutFloorOpeningsFromSolid 失敗: {ex.Message}");
                return solid;
            }
        }

        /// <summary>
        /// 創建邊界框實體
        /// </summary>
        private Solid CreateBoxSolid(XYZ min, XYZ max)
        {
            try
            {
                // 創建邊界框的 8 個頂點
                var p0 = min;
                var p1 = new XYZ(max.X, min.Y, min.Z);
                var p2 = new XYZ(max.X, max.Y, min.Z);
                var p3 = new XYZ(min.X, max.Y, min.Z);
                var p4 = new XYZ(min.X, min.Y, max.Z);
                var p5 = new XYZ(max.X, min.Y, max.Z);
                var p6 = max;
                var p7 = new XYZ(min.X, max.Y, max.Z);

                // 創建底面曲線環
                var bottomLoop = new CurveLoop();
                bottomLoop.Append(Line.CreateBound(p0, p1));
                bottomLoop.Append(Line.CreateBound(p1, p2));
                bottomLoop.Append(Line.CreateBound(p2, p3));
                bottomLoop.Append(Line.CreateBound(p3, p0));

                // 創建擠出實體
                var height = max.Z - min.Z;
                var extrusionVector = new XYZ(0, 0, height);

                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { bottomLoop }, extrusionVector, height);

                return solid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CreateBoxSolid 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 分解實體為多個獨立的實體（處理布林切割後可能產生的多個片段）
        /// </summary>
        private List<Solid> SeparateSolids(Solid solid)
        {
            var solids = new List<Solid>();

            try
            {
                // 方法：使用 SolidUtils.SplitVolumes 分解實體
                // 這個方法可以將一個包含多個不連續區域的實體分解為多個獨立的實體

                // 首先檢查實體是否有效
                if (solid == null || solid.Volume < 1e-6)
                {
                    Debug.WriteLine($"  ⚠️ 實體無效或體積為 0");
                    return solids;
                }

                // 嘗試使用 SolidUtils.SplitVolumes 分解
                try
                {
                    var splitSolids = SolidUtils.SplitVolumes(solid);
                    if (splitSolids != null && splitSolids.Count > 0)
                    {
                        foreach (Solid splitSolid in splitSolids)
                        {
                            if (splitSolid != null && splitSolid.Volume > 1e-6)
                            {
                                solids.Add(splitSolid);
                            }
                        }

                        if (solids.Count > 1)
                        {
                            Debug.WriteLine($"  ✅ 使用 SplitVolumes 分解為 {solids.Count} 個獨立實體");
                            return solids;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  ⚠️ SplitVolumes 失敗: {ex.Message}");
                }

                // 如果 SplitVolumes 失敗或只返回一個實體，使用原始實體
                if (solids.Count == 0)
                {
                    solids.Add(solid);
                    Debug.WriteLine($"  ℹ️ 實體未被分解，保持為單一實體");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ SeparateSolids 失敗: {ex.Message}");
                if (solid != null)
                {
                    solids.Add(solid);
                }
            }

            return solids;
        }

        /// <summary>
        /// 為 DirectShape 設定材料視圖覆蓋（參考面選模板工具的實現）
        /// </summary>
        private void ApplyMaterialOverride(Document doc, DirectShape directShape, Material material)
        {
            if (material == null || material.Id == ElementId.InvalidElementId)
                return;

            try
            {
                Debug.WriteLine($"🎨 開始設定材質: {material?.Name ?? "無材質"} 給元素 {directShape.Id}");

                // 🎯 參考 CmdPickFace.cs 的 SetElementMaterialAndColor 方法

                // 1. 設定元素的材質參數
                var materialParam = directShape.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (materialParam != null && !materialParam.IsReadOnly)
                {
                    materialParam.Set(material.Id);
                    Debug.WriteLine($"✅ 成功設定材質參數: {material.Name}");
                }

                // 2. 使用 VisualEffectsManager 設定材質和顏色（不透明，完整顯示材質顏色）
                VisualEffectsManager.SetFormworkMaterialAndColor(doc, directShape.Id, material, transparency: 0);

                Debug.WriteLine($"✅ 材質和顏色設定完成: {material.Name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 設定材質和顏色失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 從斜面創建 DirectShape（備用方案）
        /// </summary>
        private ElementId CreateDirectShapeFromFace(Document doc, Element host, PlanarFace face, double thicknessFt, Material material)
        {
            try
            {
                Debug.WriteLine("📦 使用 DirectShape 創建斜面裝修面");

                var curveLoops = face.GetEdgesAsCurveLoops();
                var normal = face.FaceNormal;

                // 創建擠出實體
                var extrusionDir = normal;
                var formworkSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, extrusionDir, thicknessFt);

                if (formworkSolid?.Volume <= 1e-6)
                {
                    Debug.WriteLine($"❌ 擠出實體體積過小");
                    return ElementId.InvalidElementId;
                }

                // 創建 DirectShape
                var directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                directShape.ApplicationId = "HB_BIM_Finishings";
                directShape.ApplicationDataId = "FaceToFace_Sloped";
                directShape.SetShape(new GeometryObject[] { formworkSolid });
                directShape.Name = $"裝修面_斜面_{host.Id}_{DateTime.Now:HHmmss}";

                // 設定材料
                if (material?.Id != null && material.Id != ElementId.InvalidElementId)
                {
                    var materialParam = directShape.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (materialParam != null && !materialParam.IsReadOnly)
                    {
                        materialParam.Set(material.Id);
                    }
                    SetMaterialColorInAllViews(doc, directShape.Id, material);
                }

                // 設定共用參數。
                // DirectShape 一般模型面積以「體積 / 厚度」回推，確保扣除後數量與實體一致。
                var areaM2 = CalculateAreaFromVolumeAndThickness(formworkSolid, thicknessFt, face.Area * 0.09290304);
                SetFinishingParameters(directShape, host, thicknessFt * 304.8, material, areaM2, face);
                VisualFeedbackHelper.ProtectStructureFromFinishingElement(doc, directShape);

                Debug.WriteLine($"✅ DirectShape 創建成功 - ID: {directShape.Id}");
                return directShape.Id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CreateDirectShapeFromFace 失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 設定裝修面的共用參數
        /// </summary>
        /// <param name="faceAreaFt2OrM2">面積，對於 Wall/Floor 是 ft²，對於 DirectShape 是 m²（已計算好的實際面積）</param>
        private void SetFinishingParameters(Element element, Element host, double thicknessMm, Material material, double faceAreaFt2OrM2, Face clickedFace = null)
        {
            try
            {
                // 設定宿主ID
                var hostIdParam = element.LookupParameter(SharedParams.P_HostId);
                if (hostIdParam != null && !hostIdParam.IsReadOnly)
                {
                    hostIdParam.Set(host.Id.GetIdValue().ToString());
                }

                // 設定厚度
                var thicknessParam = element.LookupParameter(SharedParams.P_Thickness);
                if (thicknessParam != null && !thicknessParam.IsReadOnly)
                {
                    thicknessParam.Set(thicknessMm);
                }

                // 設定面積
                double areaM2 = 0;
                if (element is Wall wall)
                {
                    // 牆的面積從參數中取得
                    var wallAreaParam = wall.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (wallAreaParam != null)
                    {
                        areaM2 = wallAreaParam.AsDouble() * 0.09290304; // ft² → m²
                    }
                }
                else if (element is Floor floor)
                {
                    // 樓板的面積從參數中取得
                    var floorAreaParam = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (floorAreaParam != null)
                    {
                        areaM2 = floorAreaParam.AsDouble() * 0.09290304; // ft² → m²
                    }
                }
                else if (element is DirectShape)
                {
                    // DirectShape 使用已經計算好的實際面積（m²）
                    areaM2 = faceAreaFt2OrM2; // 已經是 m²
                }
                else
                {
                    // 其他元素使用原始面的面積
                    areaM2 = faceAreaFt2OrM2 * 0.09290304; // ft² → m²
                }

                var areaParam = element.LookupParameter(SharedParams.P_Area);
                if (areaParam != null && !areaParam.IsReadOnly)
                {
                    // 🎯 參考面選模板邏輯：轉換為平方英尺（Revit 內部單位）
                    // SharedParams.P_Area 定義為 SpecTypeId.Area，期望平方英尺而非平方米
                    double areaInSquareFeet = AreaCalculator.ConvertToSquareFeet(areaM2);
                    areaParam.Set(areaInSquareFeet);
                    Debug.WriteLine($"    ✅ 設定面積: {areaM2:F4} m² ({areaInSquareFeet:F4} sq ft)");
                }

                // 一般模型（DirectShape）沒有 Revit 內建 HOST_AREA_COMPUTED，
                // 明細表若抓「裝修面積」而不是「面積」會漏量，因此兩個欄位都同步寫入。
                var finishingAreaParam = element.LookupParameter(SharedParams.P_FinishingArea)
                    ?? element.LookupParameter("裝修面積");
                if (finishingAreaParam != null && !finishingAreaParam.IsReadOnly)
                {
                    double areaInSquareFeet = AreaCalculator.ConvertToSquareFeet(areaM2);
                    finishingAreaParam.Set(areaInSquareFeet);
                    Debug.WriteLine($"    ✅ 設定裝修面積: {areaM2:F4} m² ({areaInSquareFeet:F4} sq ft)");
                }

                if (clickedFace != null)
                {
                    var quantity = CalculateFaceQuantity(clickedFace);

                    var lengthParam = element.LookupParameter(SharedParams.P_Length)
                        ?? element.LookupParameter("長度");
                    if (lengthParam != null && !lengthParam.IsReadOnly && quantity.LengthFt > 1e-9)
                    {
                        lengthParam.Set(quantity.LengthFt);
                        Debug.WriteLine($"    ✅ 設定長度: {quantity.LengthFt * 0.3048:F3} m");
                    }

                    var heightParam = element.LookupParameter(SharedParams.P_Height)
                        ?? element.LookupParameter("高度");
                    if (heightParam != null && !heightParam.IsReadOnly && quantity.HeightFt > 1e-9)
                    {
                        heightParam.Set(quantity.HeightFt);
                        Debug.WriteLine($"    ✅ 設定高度: {quantity.HeightFt * 0.3048:F3} m");
                    }
                }

                // 設定材料名稱
                var materialNameParam = element.LookupParameter(SharedParams.P_MaterialName);
                if (materialNameParam != null && !materialNameParam.IsReadOnly && material != null)
                {
                    materialNameParam.Set(material.Name ?? "");
                    Debug.WriteLine($"    ✅ 設定材料名稱: {material.Name}");
                }

                // 與房間裝修自動產出一致：裝修面不得作為房間邊界，
                // 避免後續房間面積、邊界、驗算歸戶被新生成的粉刷面干擾。
                var roomBoundingParam = element.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                if (roomBoundingParam != null && !roomBoundingParam.IsReadOnly)
                {
                    roomBoundingParam.Set(0);
                    Debug.WriteLine("    ✅ 已取消面生面粉刷面的房間邊界");
                }

                // 穩定識別標記：供刪除 / 篩選 / 後續維護使用
                var commentsParam = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentsParam != null && !commentsParam.IsReadOnly)
                {
                    var existing = commentsParam.AsString() ?? string.Empty;
                    if (existing.IndexOf("HB_BIM_Finishings", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        var marker = string.IsNullOrWhiteSpace(existing)
                            ? "HB_BIM_Finishings"
                            : existing + " | HB_BIM_Finishings";
                        commentsParam.Set(marker);
                    }
                }

                // 嘗試寫入房間關聯參數，使面生面元素能被 ValueWriter 的數量表功能識別
                TagElementWithRoomIfPossible(element.Document, element, clickedFace);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"    ⚠️ 設定參數失敗: {ex.Message}");
            }
        }

        private (double LengthFt, double HeightFt) CalculateFaceQuantity(Face face)
        {
            try
            {
                if (face == null) return (0, 0);

                var loops = face.GetEdgesAsCurveLoops();
                if (loops == null || loops.Count == 0) return (0, 0);

                var outer = GetOuterLoop(loops);
                if (outer == null) return (0, 0);

                double minZ = double.MaxValue;
                double maxZ = double.MinValue;
                var points = new List<XYZ>();

                foreach (var curve in outer)
                {
                    var tessellated = curve.Tessellate();
                    if (tessellated == null || tessellated.Count == 0)
                        tessellated = new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) };

                    foreach (var p in tessellated)
                    {
                        points.Add(p);
                        minZ = Math.Min(minZ, p.Z);
                        maxZ = Math.Max(maxZ, p.Z);
                    }
                }

                double heightFt = maxZ > minZ ? maxZ - minZ : 0;
                double lengthFt = 0;

                if (face is PlanarFace pf)
                {
                    var normal = pf.FaceNormal;
                    bool isVertical = Math.Abs(normal.Z) < 0.1;

                    if (isVertical && points.Count > 1)
                    {
                        // 垂直裝修面：長度取水平投影最大距離，高度取 Z 差。
                        for (int i = 0; i < points.Count; i++)
                        {
                            for (int j = i + 1; j < points.Count; j++)
                            {
                                var a = points[i];
                                var b = points[j];
                                var horizontal = new XYZ(b.X - a.X, b.Y - a.Y, 0);
                                lengthFt = Math.Max(lengthFt, horizontal.GetLength());
                            }
                        }
                    }
                    else
                    {
                        // 水平/斜面：無單一可靠「長度」定義，取外環最長邊作為排程參考值。
                        lengthFt = outer.Max(c => c.ApproximateLength);
                    }
                }
                else
                {
                    lengthFt = outer.Max(c => c.ApproximateLength);
                }

                return (lengthFt, heightFt);
            }
            catch
            {
                return (0, 0);
            }
        }

        private static double CalculateAreaFromVolumeAndThickness(Solid solid, double thicknessFt, double fallbackAreaM2)
        {
            try
            {
                if (solid == null || solid.Volume <= 1e-9 || thicknessFt <= 1e-9)
                    return Math.Max(0, fallbackAreaM2);

                // Revit 內部單位：Volume = ft³，Thickness = ft，因此面積 = ft²。
                var areaFt2 = solid.Volume / thicknessFt;
                return areaFt2 * 0.09290304;
            }
            catch
            {
                return Math.Max(0, fallbackAreaM2);
            }
        }

        /// <summary>
        /// 以點擊面的法向量（或備用：元素幾何中心）查詢包含該元素的房間，並將房間 ID / 名稱 / 編號
        /// 寫入對應的 AR_RoomId / AR_RoomNames / AR_RoomNumbers 共用參數。
        /// 優先使用 clickedFace.FaceNormal 確定點擊面朝向哪個房間，比元素中心點更可靠。
        /// 若共用參數尚未綁定（使用者尚未執行過房間裝修工具），則靜默略過；
        /// ValueWriter.BuildRoomElementIndex 的空間回查備援可補全這些元素。
        /// </summary>
        private static void TagElementWithRoomIfPossible(Document doc, Element element, Face clickedFace = null)
        {
            try
            {
                // 取得測試點
                // 優先：用點擊面法向量取得可靠測試點（法線方向即為所選房間方向，100mm 偏移確保在房間內）
                XYZ testPoint = null;
                if (clickedFace != null)
                {
                    try
                    {
                        var bbox2d = clickedFace.GetBoundingBox();
                        var centerUV = new UV(
                            (bbox2d.Min.U + bbox2d.Max.U) * 0.5,
                            (bbox2d.Min.V + bbox2d.Max.V) * 0.5);
                        var facePt = clickedFace.Evaluate(centerUV);
                        // Face 基底類別使用 ComputeNormal(UV)；PlanarFace 才有 FaceNormal 屬性
                        var faceNormal = (clickedFace is PlanarFace pf)
                            ? pf.FaceNormal
                            : clickedFace.ComputeNormal(centerUV);
                        // 沿面法線偏移 100mm，進入法線所指的房間
                        testPoint = facePt + faceNormal * (100.0 / 304.8);
                        Debug.WriteLine($"    🎯 使用面法線取得測試點: ({testPoint.X:F3}, {testPoint.Y:F3}, {testPoint.Z:F3})，法線: ({faceNormal.X:F3}, {faceNormal.Y:F3}, {faceNormal.Z:F3})");
                    }
                    catch
                    {
                        testPoint = null; // 若面法線取得失敗，回退至元素中心
                    }
                }

                // 備用：使用元素幾何中心
                if (testPoint == null)
                {
                    if (element.Location is LocationPoint lp)
                        testPoint = lp.Point;
                    else if (element.Location is LocationCurve lc)
                        testPoint = lc.Curve.Evaluate(0.5, true);
                    else
                    {
                        var bbox = element.get_BoundingBox(null);
                        if (bbox != null)
                            testPoint = (bbox.Min + bbox.Max) * 0.5;
                    }
                }
                if (testPoint == null) return;

                // 查詢所有有效房間
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                var matchedRooms = new List<Room>();
                foreach (var room in rooms)
                {
                    if (room.IsPointInRoom(testPoint))
                        matchedRooms.Add(room);
                }

                if (matchedRooms.Count > 1)
                {
                    Debug.WriteLine($"    ⚠️ 面生面房間歸戶命中多間房間，已略過 AR_RoomId 寫入: {string.Join(",", matchedRooms.Select(r => r.Number))}");
                    return;
                }

                var foundRoom = matchedRooms.FirstOrDefault();
                if (foundRoom == null) return;

                // 寫入 AR_RoomId
                var pId = element.LookupParameter("房間ID(AR_RoomId)")
                       ?? element.LookupParameter("房間ID")
                       ?? element.LookupParameter("AR_RoomId");
                if (pId != null && !pId.IsReadOnly)
                {
                    var rid = foundRoom.Id.GetIdValue();
                    if (pId.StorageType == StorageType.Integer && rid >= int.MinValue && rid <= int.MaxValue)
                        pId.Set((int)rid);
                    else if (pId.StorageType == StorageType.String)
                        pId.Set(rid.ToString());
                }

                // 寫入 AR_RoomNames
                var pName = element.LookupParameter("房間名稱(AR_RoomNames)")
                         ?? element.LookupParameter("房間名稱")
                         ?? element.LookupParameter("AR_RoomNames");
                if (pName != null && !pName.IsReadOnly && pName.StorageType == StorageType.String)
                    pName.Set(BuildRoomDisplayName(foundRoom));

                // 寫入 AR_RoomNumbers
                var pNum = element.LookupParameter("房間編號(AR_RoomNumbers)")
                        ?? element.LookupParameter("房間編號")
                        ?? element.LookupParameter("AR_RoomNumbers");
                if (pNum != null && !pNum.IsReadOnly && pNum.StorageType == StorageType.String)
                    pNum.Set(foundRoom.Number ?? "");

                Debug.WriteLine($"    ✅ 標記房間: {foundRoom.Number} {foundRoom.Name} (ID: {foundRoom.Id})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"    ⚠️ TagElementWithRoomIfPossible 失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得或創建牆類型
        /// </summary>
        private WallType GetOrCreateWallType(Document doc, double thicknessMm, Material material)
        {
            try
            {
                string typeName = $"裝修牆_{thicknessMm}mm_{material?.Name ?? "預設"}";

                // 先嘗試找現有的類型
                var existingType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(wt => wt.Name == typeName);

                if (existingType != null)
                {
                    Debug.WriteLine($"✅ 找到現有牆類型: {typeName}");
                    return existingType;
                }

                // 找一個基本牆類型作為範本
                var baseWallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(wt => wt.Kind == WallKind.Basic);

                if (baseWallType == null)
                {
                    Debug.WriteLine("❌ 找不到基本牆類型");
                    return null;
                }

                // 複製並修改
                var newWallType = baseWallType.Duplicate(typeName) as WallType;
                if (newWallType == null)
                {
                    Debug.WriteLine("❌ 複製牆類型失敗");
                    return baseWallType; // 使用基本類型
                }

                // 🎯 關鍵：設定牆厚度和材料
                var compoundStructure = newWallType.GetCompoundStructure();
                if (compoundStructure != null && material != null)
                {
                    try
                    {
                        // 清除所有外層和內層
                        compoundStructure.SetNumberOfShellLayers(ShellLayerType.Exterior, 0);
                        compoundStructure.SetNumberOfShellLayers(ShellLayerType.Interior, 0);

                        // 取得所有層
                        var layers = compoundStructure.GetLayers();
                        Debug.WriteLine($"  📊 牆類型原有層數: {layers.Count}");

                        // 刪除多餘的層，只保留一層
                        while (layers.Count > 1)
                        {
                            compoundStructure.DeleteLayer(layers.Count - 1);
                            layers = compoundStructure.GetLayers();
                        }

                        if (layers.Count > 0)
                        {
                            // 設定唯一的結構層
                            compoundStructure.SetLayerWidth(0, thicknessMm / 304.8); // mm → ft
                            compoundStructure.SetMaterialId(0, material.Id);
                            compoundStructure.SetLayerFunction(0, MaterialFunctionAssignment.Structure);

                            Debug.WriteLine($"  ✅ 設定牆層: 厚度={thicknessMm}mm, 材料={material.Name}");
                        }

                        newWallType.SetCompoundStructure(compoundStructure);
                        Debug.WriteLine($"  ✅ 複合結構設定完成");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 設定複合結構失敗: {ex.Message}");
                    }
                }

                Debug.WriteLine($"✅ 創建新牆類型: {typeName}");
                return newWallType;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ GetOrCreateWallType 失敗: {ex.Message}，使用預設類型");
                // 返回第一個找到的基本牆類型
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(wt => wt.Kind == WallKind.Basic);
            }
        }

        private static string BuildRoomDisplayName(Room room)
        {
            var name = room?.Name ?? string.Empty;
            var number = room?.Number ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            if (string.IsNullOrWhiteSpace(number))
                return name.Trim();

            var trimmedName = name.Trim();
            var trimmedNumber = number.Trim();

            if (trimmedName.EndsWith(trimmedNumber, StringComparison.OrdinalIgnoreCase))
            {
                var pureName = trimmedName.Substring(0, trimmedName.Length - trimmedNumber.Length).TrimEnd();
                pureName = pureName.TrimEnd('-', '_', '(', ')', '（', '）', ' ');
                if (!string.IsNullOrWhiteSpace(pureName))
                    return pureName;
            }

            return trimmedName;
        }

        /// <summary>
        /// 取得或創建樓板類型
        /// </summary>
        private FloorType GetOrCreateFloorType(Document doc, double thicknessMm, Material material)
        {
            try
            {
                string typeName = $"裝修樓板_{thicknessMm}mm_{material?.Name ?? "預設"}";

                // 先嘗試找現有的類型
                var existingType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(ft => ft.Name == typeName);

                if (existingType != null)
                {
                    Debug.WriteLine($"✅ 找到現有樓板類型: {typeName}");
                    return existingType;
                }

                // 找一個基本樓板類型作為範本
                var baseFloorType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault();

                if (baseFloorType == null)
                {
                    Debug.WriteLine("❌ 找不到樓板類型");
                    return null;
                }

                // 複製並修改
                var newFloorType = baseFloorType.Duplicate(typeName) as FloorType;
                if (newFloorType == null)
                {
                    Debug.WriteLine("❌ 複製樓板類型失敗");
                    return baseFloorType; // 使用基本類型
                }

                // 🎯 關鍵：設定樓板厚度和材料
                var compoundStructure = newFloorType.GetCompoundStructure();
                if (compoundStructure != null && material != null)
                {
                    try
                    {
                        // 清除所有外層和內層
                        compoundStructure.SetNumberOfShellLayers(ShellLayerType.Exterior, 0);
                        compoundStructure.SetNumberOfShellLayers(ShellLayerType.Interior, 0);

                        // 取得所有層
                        var layers = compoundStructure.GetLayers();
                        Debug.WriteLine($"  📊 樓板類型原有層數: {layers.Count}");

                        // 刪除多餘的層，只保留一層
                        while (layers.Count > 1)
                        {
                            compoundStructure.DeleteLayer(layers.Count - 1);
                            layers = compoundStructure.GetLayers();
                        }

                        if (layers.Count > 0)
                        {
                            // 設定唯一的結構層
                            compoundStructure.SetLayerWidth(0, thicknessMm / 304.8); // mm → ft
                            compoundStructure.SetMaterialId(0, material.Id);
                            compoundStructure.SetLayerFunction(0, MaterialFunctionAssignment.Structure);

                            Debug.WriteLine($"  ✅ 設定樓板層: 厚度={thicknessMm}mm, 材料={material.Name}");
                        }

                        newFloorType.SetCompoundStructure(compoundStructure);
                        Debug.WriteLine($"  ✅ 複合結構設定完成");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 設定複合結構失敗: {ex.Message}");
                    }
                }

                Debug.WriteLine($"✅ 創建新樓板類型: {typeName}");
                return newFloorType;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ GetOrCreateFloorType 失敗: {ex.Message}，使用預設類型");
                // 返回第一個找到的樓板類型
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault();
            }
        }

        /// <summary>
        /// 取得最近的樓層
        /// </summary>
        private Level GetNearestLevel(Document doc, PlanarFace face, bool preferBottom)
        {
            try
            {
                // 牆面用最低點判斷樓層，樓板/水平面用平均點判斷。
                var curveLoops = face.GetEdgesAsCurveLoops();
                if (curveLoops == null || curveLoops.Count == 0)
                {
                    Debug.WriteLine("❌ 無法取得面的邊界");
                    return null;
                }

                double avgZ = 0;
                int pointCount = 0;
                double minZ = double.MaxValue;
                foreach (var loop in curveLoops)
                {
                    foreach (var curve in loop)
                    {
                        var z = curve.GetEndPoint(0).Z;
                        avgZ += z;
                        pointCount++;
                        if (z < minZ) minZ = z;
                    }
                }
                double faceZ = preferBottom ? minZ : (avgZ / pointCount);

                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => Math.Abs(l.Elevation - faceZ))
                    .ToList();

                if (levels.Count > 0)
                {
                    Debug.WriteLine($"✅ 找到最近的樓層: {levels[0].Name} (判斷Z={faceZ * 304.8:F2}mm, 模式={(preferBottom ? "底點" : "平均")})");
                    return levels[0];
                }

                Debug.WriteLine("❌ 找不到樓層");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ GetNearestLevel 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 從 CurveLoop 中取得底部曲線
        /// </summary>
        private Curve GetBottomCurveFromLoop(CurveLoop curveLoop, Level level)
        {
            try
            {
                // 🎯 改進：優先找水平曲線（Z 值最小的），如果沒有水平曲線，則找最長的曲線
                Curve bottomCurve = null;
                double minZ = double.MaxValue;
                double maxLength = 0;

                // 第一遍：找 Z 值最小的水平曲線
                foreach (var curve in curveLoop)
                {
                    var start = curve.GetEndPoint(0);
                    var end = curve.GetEndPoint(1);
                    var avgZ = (start.Z + end.Z) / 2;
                    var deltaZ = Math.Abs(end.Z - start.Z);

                    // 如果是水平曲線（Z 差異小於 1mm）
                    if (deltaZ < 1.0 / 304.8)
                    {
                        if (avgZ < minZ)
                        {
                            minZ = avgZ;
                            bottomCurve = curve;
                        }
                    }
                }

                // 如果找到水平曲線，使用它
                if (bottomCurve != null)
                {
                    Debug.WriteLine($"✅ 找到底部水平曲線，Z: {minZ * 304.8:F2} mm, 長度: {bottomCurve.Length * 304.8:F2} mm");
                    return bottomCurve;
                }

                // 第二遍：如果沒有水平曲線，找最長的曲線（可能是垂直面）
                Debug.WriteLine("  ⚠️ 沒有找到水平曲線，使用最長曲線");
                foreach (var curve in curveLoop)
                {
                    if (curve.Length > maxLength)
                    {
                        maxLength = curve.Length;
                        bottomCurve = curve;
                    }
                }

                if (bottomCurve != null)
                {
                    Debug.WriteLine($"✅ 找到最長曲線，長度: {bottomCurve.Length * 304.8:F2} mm");
                }

                return bottomCurve;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ GetBottomCurveFromLoop 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 從多個邊界環中找出外邊界（通常為周長最長者）
        /// </summary>
        private CurveLoop GetOuterLoop(IList<CurveLoop> loops)
        {
            try
            {
                if (loops == null || loops.Count == 0) return null;
                if (loops.Count == 1) return loops[0];

                CurveLoop outer = null;
                double maxPerimeter = double.MinValue;

                foreach (var loop in loops)
                {
                    double perimeter = 0;
                    foreach (var c in loop) perimeter += c.Length;

                    if (perimeter > maxPerimeter)
                    {
                        maxPerimeter = perimeter;
                        outer = loop;
                    }
                }

                return outer;
            }
            catch
            {
                return loops != null && loops.Count > 0 ? loops[0] : null;
            }
        }

        /// <summary>
        /// 扣除相鄰元件（牆、樓板等）- 保留用於 DirectShape 備用方案
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

                    // 取得相鄰元件的所有實體
                    var nearbySolids = GetElementSolids(nearbyElement);
                    if (nearbySolids.Count == 0) continue;

                    foreach (var nearbySolid in nearbySolids)
                    {
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
        /// 在裝修牆上創建開口（對應原牆的門窗位置）
        /// </summary>
        /// <summary>
        /// 根據源牆的開口幾何直接為目標牆創建相同輪廓的開口
        /// 🔑 核心：不複製門窗族實例，而是直接基於開口幾何切割
        /// </summary>
        private void CreateOpeningsFromHostGeometry(Document doc, Wall sourceWall, Wall targetWall)
        {
            try
            {
                Debug.WriteLine($"🔓 開始根據源牆開口幾何創建目標牆開口");

                // 步驟 1：取得源牆的所有開口
                var openings = sourceWall.FindInserts(true, true, true, true);
                if (openings.Count == 0)
                {
                    Debug.WriteLine("  ℹ️ 源牆上沒有開口");
                    return;
                }

                Debug.WriteLine($"  📊 找到 {openings.Count} 個開口");

                // 步驟 2：取得源牆和目標牆的位置曲線
                var sourceLocation = (sourceWall.Location as LocationCurve)?.Curve;
                var targetLocation = (targetWall.Location as LocationCurve)?.Curve;

                if (sourceLocation == null || targetLocation == null)
                {
                    Debug.WriteLine("  ❌ 無法取得牆的位置曲線");
                    return;
                }

                int createdCount = 0;

                // 步驟 3：逐個處理開口
                foreach (ElementId openingId in openings)
                {
                    try
                    {
                        var openingElement = doc.GetElement(openingId);
                        if (openingElement == null) continue;

                        Debug.WriteLine($"  🔍 處理開口: {openingElement.Name} (ID: {openingId})");

                        // 取得開口的幾何體
                        var openingGeom = openingElement.get_Geometry(new Options());
                        if (openingGeom == null)
                        {
                            Debug.WriteLine($"    ⚠️ 無法取得開口幾何");
                            continue;
                        }

                        // 從開口幾何體提取邊界框（開口的範圍）
                        var openingBBox = openingElement.get_BoundingBox(null);
                        if (openingBBox == null)
                        {
                            Debug.WriteLine($"    ⚠️ 無法取得開口邊界框");
                            continue;
                        }

                        Debug.WriteLine($"    📐 開口邊界框: Min({openingBBox.Min.X:F2}, {openingBBox.Min.Y:F2}, {openingBBox.Min.Z:F2})");
                        Debug.WriteLine($"                 Max({openingBBox.Max.X:F2}, {openingBBox.Max.Y:F2}, {openingBBox.Max.Z:F2})");

                        // 計算開口在源牆上的中心點
                        var openingCenter = new XYZ(
                            (openingBBox.Min.X + openingBBox.Max.X) / 2,
                            (openingBBox.Min.Y + openingBBox.Max.Y) / 2,
                            (openingBBox.Min.Z + openingBBox.Max.Z) / 2
                        );

                        Debug.WriteLine($"    📍 開口中心: ({openingCenter.X:F2}, {openingCenter.Y:F2}, {openingCenter.Z:F2})");

                        // 🔑 改進：將開口中心點直接投影到目標牆位置曲線上
                        // 這樣可以確保開口位置與源牆開口對應
                        var projectionOnTarget = targetLocation.Project(openingCenter);
                        if (projectionOnTarget == null)
                        {
                            Debug.WriteLine($"    ❌ 無法投影到目標牆");
                            continue;
                        }

                        var targetOpeningCenter = projectionOnTarget.XYZPoint;
                        Debug.WriteLine($"    📍 目標開口中心: ({targetOpeningCenter.X:F2}, {targetOpeningCenter.Y:F2}, {targetOpeningCenter.Z:F2})");

                        // 計算開口的尺寸（基於邊界框）
                        double openingWidth = openingBBox.Max.X - openingBBox.Min.X;
                        double openingHeight = openingBBox.Max.Z - openingBBox.Min.Z;
                        double sillHeight = openingBBox.Min.Z - (sourceWall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0);

                        Debug.WriteLine($"    📏 開口尺寸: 寬={openingWidth * 304.8:F0}mm, 高={openingHeight * 304.8:F0}mm, 窗台={sillHeight * 304.8:F0}mm");

                        // 在目標牆上創建矩形開口
                        var opening = CreateRectangularOpening(doc, targetWall, targetOpeningCenter, openingWidth, openingHeight, sillHeight);
                        if (opening != null)
                        {
                            createdCount++;
                            Debug.WriteLine($"    ✅ 開口創建成功! ID: {opening.Id}");
                        }
                        else
                        {
                            Debug.WriteLine($"    ⚠️ 開口創建返回 null");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ❌ 處理開口 {openingId} 失敗: {ex.Message}");
                    }
                }

                Debug.WriteLine($"✅ 共創建 {createdCount} 個開口");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CreateOpeningsFromHostGeometry 失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 直接複製源牆上的門窗族實例到目標牆上
        /// 使用族實例複製而非矩形開口，保留所有原始屬性
        /// </summary>
        private void CopyWallFamilyInstances(Document doc, Wall sourceWall, Wall targetWall)
        {
            try
            {
                Debug.WriteLine($"🚪 開始複製牆上的門窗族實例");

                // 找到原牆上的所有門窗
                var openings = sourceWall.FindInserts(true, true, true, true);
                if (openings.Count == 0)
                {
                    Debug.WriteLine("  ℹ️ 原牆上沒有門窗開口");
                    return;
                }

                Debug.WriteLine($"  📊 找到 {openings.Count} 個門窗");

                // 取得原牆和新牆的位置曲線
                var sourceLocation = (sourceWall.Location as LocationCurve)?.Curve;
                var targetLocation = (targetWall.Location as LocationCurve)?.Curve;

                if (sourceLocation == null || targetLocation == null)
                {
                    Debug.WriteLine("  ❌ 無法取得牆的位置曲線");
                    return;
                }

                int copiedCount = 0;
                foreach (ElementId openingId in openings)
                {
                    var sourceInstance = doc.GetElement(openingId) as FamilyInstance;
                    if (sourceInstance == null) continue;

                    try
                    {
                        Debug.WriteLine($"  🔍 處理門窗: {sourceInstance.Name} (ID: {sourceInstance.Id})");

                        // 取得門窗在源牆上的位置
                        var sourceLocation_Point = (sourceInstance.Location as LocationPoint)?.Point;
                        if (sourceLocation_Point == null)
                        {
                            Debug.WriteLine($"    ❌ 無法取得門窗位置");
                            continue;
                        }

                        Debug.WriteLine($"    📍 源牆上門窗位置: ({sourceLocation_Point.X:F2}, {sourceLocation_Point.Y:F2}, {sourceLocation_Point.Z:F2})");

                        // 🔑 改進：直接在目標牆位置曲線上投影源牆門窗的絕對位置
                        // 而非使用相對參數投影（相對參數在牆被偏移時會不准確）
                        var projectionOnTarget = targetLocation.Project(sourceLocation_Point);
                        if (projectionOnTarget == null)
                        {
                            Debug.WriteLine($"    ❌ 無法投影到目標牆");
                            continue;
                        }

                        var pointOnTarget = projectionOnTarget.XYZPoint;
                        double parameter = projectionOnTarget.Parameter;
                        
                        Debug.WriteLine($"    📐 目標位置（直接投影）: ({pointOnTarget.X:F2}, {pointOnTarget.Y:F2}, {pointOnTarget.Z:F2}), 參數: {parameter:F4}");

                        // 🔑 關鍵：使用特殊族實例複製方法，帶有更好的錯誤處理
                        ElementId newInstanceId = ElementId.InvalidElementId;
                        bool instanceCreated = false;

                        // 方法 1：嘗試直接在目標牆上創建新的族實例（首選）
                        try
                        {
                            var newInstance = doc.Create.NewFamilyInstance(
                                pointOnTarget,
                                sourceInstance.Symbol,
                                targetWall,
                                sourceInstance.StructuralType);

                            if (newInstance != null)
                            {
                                // 複製重要參數
                                CopyInstanceParameters(sourceInstance, newInstance);
                                newInstanceId = newInstance.Id;
                                instanceCreated = true;

                                copiedCount++;
                                Debug.WriteLine($"    ✅ 族實例複製成功! 新ID: {newInstanceId}");
                            }
                            else
                            {
                                Debug.WriteLine($"    ⚠️ NewFamilyInstance 返回 null");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"    ⚠️ 族實例複製失敗: {ex.Message}");
                        }

                        // 方法 2：如果族實例複製失敗，回退到矩形開口
                        if (!instanceCreated)
                        {
                            try
                            {
                                Debug.WriteLine($"    🔄 回退到矩形開口創建...");

                                // 提取門窗尺寸
                                double width = GetOpeningWidth(sourceInstance);
                                double height = GetOpeningHeight(sourceInstance);
                                double sill = GetOpeningSill(sourceInstance);

                                Debug.WriteLine($"    📏 提取尺寸: 寬={width * 304.8:F0}mm, 高={height * 304.8:F0}mm, 窗台高={sill * 304.8:F0}mm");

                                var wallOpening = CreateRectangularOpening(doc, targetWall, pointOnTarget, width, height, sill);
                                if (wallOpening != null)
                                {
                                    copiedCount++;
                                    Debug.WriteLine($"    ✅ 矩形開口創建成功! 開口ID: {wallOpening.Id}");
                                }
                                else
                                {
                                    Debug.WriteLine($"    ❌ 矩形開口創建返回 null");
                                }
                            }
                            catch (Exception exOpening)
                            {
                                Debug.WriteLine($"    ❌ 矩形開口創建失敗: {exOpening.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ❌ 處理門窗 {openingId} 失敗: {ex.Message}");
                    }
                }

                Debug.WriteLine($"✅ 共複製或創建 {copiedCount} 個門窗族實例或開口");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CopyWallFamilyInstances 失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 複製族實例的重要參數
        /// </summary>
        private void CopyInstanceParameters(FamilyInstance source, FamilyInstance target)
        {
            try
            {
                // 複製名稱
                target.Name = source.Name;

                // 複製共用參數
                if (source.Parameters != null)
                {
                    foreach (Parameter param in source.Parameters)
                    {
                        if (param.IsReadOnly) continue;
                        if (!param.HasValue) continue;

                        try
                        {
                            var targetParam = target.LookupParameter(param.Definition.Name);
                            if (targetParam != null && !targetParam.IsReadOnly)
                            {
                                // 根據參數類型複製值
                                switch (param.StorageType)
                                {
                                    case StorageType.Double:
                                        targetParam.Set(param.AsDouble());
                                        break;
                                    case StorageType.Integer:
                                        targetParam.Set(param.AsInteger());
                                        break;
                                    case StorageType.String:
                                        targetParam.Set(param.AsString());
                                        break;
                                    case StorageType.ElementId:
                                        targetParam.Set(param.AsElementId());
                                        break;
                                }
                            }
                        }
                        catch
                        {
                            // 忽略無法複製的參數
                        }
                    }
                }

                Debug.WriteLine($"  📋 參數複製完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠️ 複製參數失敗: {ex.Message}");
            }
        }

        private void CopyWallOpenings(Document doc, Wall sourceWall, Wall targetWall, double thicknessMm)
        {
            // 此方法已被 CopyWallFamilyInstances 取代，保留以維持向後相容
            CopyWallFamilyInstances(doc, sourceWall, targetWall);
        }

        /// <summary>
        /// 取得門窗的寬度
        /// </summary>
        private double GetOpeningWidth(FamilyInstance opening)
        {
            try
            {
                // 嘗試取得寬度參數
                var widthParam = opening.Symbol?.LookupParameter("寬度") ??
                                opening.Symbol?.LookupParameter("Width") ??
                                opening.LookupParameter("寬度") ??
                                opening.LookupParameter("Width");

                if (widthParam != null && widthParam.HasValue)
                {
                    return widthParam.AsDouble();
                }

                // 嘗試取得粗略寬度
                var roughWidthParam = opening.Symbol?.LookupParameter("Rough Width") ??
                                     opening.LookupParameter("Rough Width");

                if (roughWidthParam != null && roughWidthParam.HasValue)
                {
                    return roughWidthParam.AsDouble();
                }

                // 預設值
                return 900.0 / 304.8; // 900mm
            }
            catch
            {
                return 900.0 / 304.8;
            }
        }

        /// <summary>
        /// 取得門窗的高度
        /// </summary>
        private double GetOpeningHeight(FamilyInstance opening)
        {
            try
            {
                // 嘗試取得高度參數
                var heightParam = opening.Symbol?.LookupParameter("高度") ??
                                 opening.Symbol?.LookupParameter("Height") ??
                                 opening.LookupParameter("高度") ??
                                 opening.LookupParameter("Height");

                if (heightParam != null && heightParam.HasValue)
                {
                    return heightParam.AsDouble();
                }

                // 嘗試取得粗略高度
                var roughHeightParam = opening.Symbol?.LookupParameter("Rough Height") ??
                                      opening.LookupParameter("Rough Height");

                if (roughHeightParam != null && roughHeightParam.HasValue)
                {
                    return roughHeightParam.AsDouble();
                }

                // 根據類別設定預設高度
                if (opening.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_Doors)
                {
                    return 2100.0 / 304.8; // 門預設 2100mm
                }
                else if (opening.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_Windows)
                {
                    return 1200.0 / 304.8; // 窗預設 1200mm
                }

                return 2100.0 / 304.8;
            }
            catch
            {
                return 2100.0 / 304.8;
            }
        }

        /// <summary>
        /// 取得窗台高度
        /// </summary>
        private double GetOpeningSill(FamilyInstance opening)
        {
            try
            {
                // 只有窗才有窗台高
                if (opening.Category.Id.GetIdValue() != (long)BuiltInCategory.OST_Windows)
                {
                    return 0; // 門的窗台高為 0
                }

                // 嘗試取得窗台高參數
                var sillParam = opening.Symbol?.LookupParameter("窗台高") ??
                               opening.Symbol?.LookupParameter("Sill Height") ??
                               opening.LookupParameter("窗台高") ??
                               opening.LookupParameter("Sill Height");

                if (sillParam != null && sillParam.HasValue)
                {
                    return sillParam.AsDouble();
                }

                // 預設窗台高 900mm
                return 900.0 / 304.8;
            }
            catch
            {
                return 900.0 / 304.8;
            }
        }

        /// <summary>
        /// 在牆上創建矩形開口
        /// 🔑 改進：使用牆的邊界框和法向量確保開口輪廓正確
        /// </summary>
        private Opening CreateRectangularOpening(Document doc, Wall wall, XYZ centerPoint, double width, double height, double sillHeight)
        {
            try
            {
                Debug.WriteLine($"  🔍 開始創建矩形開口: 位置 ({centerPoint.X:F2}, {centerPoint.Y:F2}, {centerPoint.Z:F2}), 寬={width * 304.8:F0}mm, 高={height * 304.8:F0}mm, 窗台高={sillHeight * 304.8:F0}mm");

                // 取得牆的位置曲線
                var wallLocation = (wall.Location as LocationCurve)?.Curve;
                if (wallLocation == null) return null;

                // 將中心點投影到牆的位置曲線上
                var projection = wallLocation.Project(centerPoint);
                if (projection == null) return null;

                var projPoint = projection.XYZPoint;
                double parameter = projection.Parameter;

                // 取得牆的方向向量和法向量
                var wallStart = wallLocation.GetEndPoint(0);
                var wallEnd = wallLocation.GetEndPoint(1);
                var wallDirection = (wallEnd - wallStart).Normalize();
                Debug.WriteLine($"  📐 牆方向向量: ({wallDirection.X:F3}, {wallDirection.Y:F3}, {wallDirection.Z:F3})");

                // 🔑 改進：計算開口的四個角點時，要考慮牆的厚度和方向
                // 開口應該在牆的位置曲線上（已考慮中心線偏移），沿牆方向的兩側
                var halfWidth = width / 2.0;
                var leftPoint = projPoint - wallDirection * halfWidth;
                var rightPoint = projPoint + wallDirection * halfWidth;

                // 取得牆的基礎偏移和樓層高度
                var baseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
                var levelId = wall.LevelId;
                var level = doc.GetElement(levelId) as Level;
                var levelElevation = level?.Elevation ?? 0;

                // 計算開口的底部和頂部高度（相對於絕對坐標）
                var bottomZ = levelElevation + baseOffset + sillHeight;
                var topZ = bottomZ + height;

                Debug.WriteLine($"  📏 開口高度: 底部 Z = {bottomZ * 304.8:F2}mm, 頂部 Z = {topZ * 304.8:F2}mm");
                Debug.WriteLine($"  📍 開口四角: 左({leftPoint.X:F2}, {leftPoint.Y:F2}), 右({rightPoint.X:F2}, {rightPoint.Y:F2})");

                // 創建開口的四個角點（在 3D 空間中）
                var p1 = new XYZ(leftPoint.X, leftPoint.Y, bottomZ);
                var p2 = new XYZ(rightPoint.X, rightPoint.Y, bottomZ);
                var p3 = new XYZ(rightPoint.X, rightPoint.Y, topZ);
                var p4 = new XYZ(leftPoint.X, leftPoint.Y, topZ);

                // 🔑 改進：使用 CurveLoop 而非 CurveArray，避免順序問題
                var curveArray = new CurveArray();
                // 按照逆時針順序（從頂部俯視）創建邊界
                curveArray.Append(Line.CreateBound(p1, p2));  // 底部左-右
                curveArray.Append(Line.CreateBound(p2, p3));  // 右側下-上
                curveArray.Append(Line.CreateBound(p3, p4));  // 頂部右-左
                curveArray.Append(Line.CreateBound(p4, p1));  // 左側上-下

                Debug.WriteLine($"  ✅ 開口曲線邊界已定義，共 4 條邊");

                // 創建開口
                var opening = doc.Create.NewOpening(wall, curveArray, true);

                if (opening != null)
                {
                    Debug.WriteLine($"    ✅ 創建矩形開口成功: ID = {opening.Id}");
                }
                else
                {
                    Debug.WriteLine($"    ⚠️ NewOpening 返回 null");
                }

                return opening;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"    ❌ CreateRectangularOpening 失敗: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 取得實心填充圖案ID
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
                Debug.WriteLine($"⚠️ GetSolidFillPatternId 失敗: {ex.Message}");
            }

            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// 從牆類型中取得材料
        /// </summary>
        private Material GetMaterialFromWallType(Document doc, WallType wallType)
        {
            try
            {
                var compound = wallType.GetCompoundStructure();
                if (compound != null && compound.GetLayers().Count > 0)
                {
                    var layer = compound.GetLayers()[0];
                    return doc.GetElement(layer.MaterialId) as Material;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ GetMaterialFromWallType 失敗: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 從牆類型中取得厚度（mm）
        /// </summary>
        private double GetThicknessFromWallType(WallType wallType)
        {
            try
            {
                var compound = wallType.GetCompoundStructure();
                if (compound != null && compound.GetLayers().Count > 0)
                {
                    double totalThickness = 0;
                    foreach (var layer in compound.GetLayers())
                    {
                        totalThickness += layer.Width;
                    }
                    return totalThickness * 304.8; // feet to mm
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ GetThicknessFromWallType 失敗: {ex.Message}");
            }
            return 20.0; // 預設 20mm
        }

        /// <summary>
        /// 從樓板類型中取得材料
        /// </summary>
        private Material GetMaterialFromFloorType(Document doc, FloorType floorType)
        {
            try
            {
                var compound = floorType.GetCompoundStructure();
                if (compound != null && compound.GetLayers().Count > 0)
                {
                    var layer = compound.GetLayers()[0];
                    return doc.GetElement(layer.MaterialId) as Material;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ GetMaterialFromFloorType 失敗: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 從樓板類型中取得厚度（mm）
        /// </summary>
        private double GetThicknessFromFloorType(FloorType floorType)
        {
            try
            {
                var compound = floorType.GetCompoundStructure();
                if (compound != null && compound.GetLayers().Count > 0)
                {
                    double totalThickness = 0;
                    foreach (var layer in compound.GetLayers())
                    {
                        totalThickness += layer.Width;
                    }
                    return totalThickness * 304.8; // feet to mm
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ GetThicknessFromFloorType 失敗: {ex.Message}");
            }
            return 20.0; // 預設 20mm
        }

        /// <summary>
        /// 在所有視圖中設定材料顏色（使用材料的「著色」顏色）
        /// </summary>
        private void SetMaterialColorInAllViews(Document doc, ElementId elementId, Material material)
        {
            try
            {
                // 取得材料的「著色」顏色（圖形標籤中的顏色）
                var materialColor = material.Color;
                if (!materialColor.IsValid)
                {
                    Debug.WriteLine($"  ⚠️ 材料顏色無效: {material.Name}");
                    return;
                }

                // 取得實心填充圖案
                var solidPatternId = VisualFeedbackHelper.GetSolidFillPatternId(doc);

                // 取得所有可列印且非範本的視圖
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.CanBePrinted && !v.IsTemplate)
                    .ToList();

                foreach (var view in allViews)
                {
                    try
                    {
                        var overrides = new OverrideGraphicSettings();

                        // 設定填充圖案
                        if (solidPatternId != ElementId.InvalidElementId)
                        {
                            overrides.SetSurfaceForegroundPatternId(solidPatternId);
                            overrides.SetSurfaceBackgroundPatternId(solidPatternId);
                            overrides.SetCutForegroundPatternId(solidPatternId);
                            overrides.SetCutBackgroundPatternId(solidPatternId);
                            overrides.SetSurfaceForegroundPatternVisible(true);
                            overrides.SetSurfaceBackgroundPatternVisible(true);
                        }

                        // 設定所有顏色屬性（使用材料的「著色」顏色）
                        overrides.SetSurfaceForegroundPatternColor(materialColor);
                        overrides.SetSurfaceBackgroundPatternColor(materialColor);
                        overrides.SetProjectionLineColor(materialColor);
                        overrides.SetCutLineColor(materialColor);
                        overrides.SetCutForegroundPatternColor(materialColor);
                        overrides.SetCutBackgroundPatternColor(materialColor);

                        // 不設定透明度，完整顯示材料顏色
                        overrides.SetSurfaceTransparency(0);

                        view.SetElementOverrides(elementId, overrides);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ⚠️ 為視圖 '{view.Name}' 設定材料顏色失敗: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ❌ SetMaterialColorInAllViews 失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得元素的所有實體幾何（參考模板工具的實現）
        /// </summary>
        private List<Solid> GetElementSolids(Element element)
        {
            var result = new List<Solid>();
            try
            {
                var options = new Options
                {
                    ComputeReferences = true,
                    DetailLevel = ViewDetailLevel.Fine,
                    IncludeNonVisibleObjects = true  // 🎯 改為 true 以獲取門窗的隱藏幾何
                };

                var geomElem = element.get_Geometry(options);
                if (geomElem == null) return result;

                foreach (var geomObj in geomElem)
                {
                    if (geomObj is Solid solid && solid.Volume > 1e-6)
                    {
                        result.Add(solid);
                    }
                    else if (geomObj is GeometryInstance geomInst)
                    {
                        // 🎯 對於 FamilyInstance（門窗），同時嘗試兩種方法獲取幾何
                        var instGeom = geomInst.GetInstanceGeometry();
                        if (instGeom != null)
                        {
                            foreach (var instObj in instGeom)
                            {
                                if (instObj is Solid instSolid && instSolid.Volume > 1e-6)
                                {
                                    result.Add(instSolid);
                                }
                            }
                        }

                        // 🎯 同時嘗試 GetSymbolGeometry（某些窗戶可能只在這裡有幾何）
                        var symbolGeom = geomInst.GetSymbolGeometry();
                        if (symbolGeom != null)
                        {
                            var transform = geomInst.Transform;
                            foreach (var symbolObj in symbolGeom)
                            {
                                if (symbolObj is Solid symbolSolid && symbolSolid.Volume > 1e-6)
                                {
                                    // 🎯 需要應用變換矩陣
                                    var transformedSolid = SolidUtils.CreateTransformed(symbolSolid, transform);
                                    if (transformedSolid != null && transformedSolid.Volume > 1e-6)
                                    {
                                        result.Add(transformedSolid);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠️ GetElementSolids 失敗: {ex.Message}");
            }

            return result;
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
        private string GetFaceKey(Element host, Face face)
        {
            XYZ origin, normal;
            if (face is PlanarFace pf)
            {
                origin = pf.Origin;
                normal = pf.FaceNormal;
            }
            else
            {
                // 曲面：使用面的 UV 中心點與中心法向量
                var bbox = face.GetBoundingBox();
                var uvMid = new UV((bbox.Min.U + bbox.Max.U) / 2.0, (bbox.Min.V + bbox.Max.V) / 2.0);
                origin = face.Evaluate(uvMid);
                normal = face.ComputeNormal(uvMid);
            }
            return $"{host.Id}_{origin.X:F3}_{origin.Y:F3}_{origin.Z:F3}_{normal.X:F3}_{normal.Y:F3}_{normal.Z:F3}";
        }

        // 只允許可作為裝修面宿主的建築/結構元素
        private class FaceOnHostFilter : ISelectionFilter
        {
            private readonly bool _allowFloor;
            public FaceOnHostFilter(bool allowFloor) { _allowFloor = allowFloor; }

            public bool AllowElement(Element e)
            {
                if (e?.Category?.Id == null) return false;
#if REVIT2024 || REVIT2025 || REVIT2026
                long v = e.Category.Id.GetIdValue();
#else
                long v = e.Category.Id.IntegerValue;
#endif
                if (v == (long)BuiltInCategory.OST_Walls) return true;
                if (v == (long)BuiltInCategory.OST_StructuralColumns) return true;
                if (v == (long)BuiltInCategory.OST_StructuralFraming) return true;
                if (_allowFloor && v == (long)BuiltInCategory.OST_Floors) return true;
                if (ElementCategorizer.IsStairs(e)) return true;
                return false;
            }
            public bool AllowReference(Reference r, XYZ p) => true;
        }

        // 選取牆類型 + 樓板類型 + 一般模型的小視窗（中文 UI）
        private class PickFacePalette : System.Windows.Window
        {
            private readonly Document _doc;
            private readonly System.Windows.Controls.RadioButton _rbUseType;
            private readonly System.Windows.Controls.RadioButton _rbUseGenericModel;
            private readonly System.Windows.Controls.ComboBox _cmbWall;
            private readonly System.Windows.Controls.ComboBox _cmbFloor;
            private readonly System.Windows.Controls.ComboBox _cmbMaterial;
            private readonly System.Windows.Controls.TextBox _txtThickness;
            private readonly System.Windows.Controls.TextBox _txtSlopedFaceFloorMaxAngle;
            private readonly System.Windows.Controls.StackPanel _panelType;
            private readonly System.Windows.Controls.StackPanel _panelGenericModel;

            public WallType SelectedWallType { get; private set; }
            public FloorType SelectedFloorType { get; private set; }
            public Material SelectedMaterial { get; private set; }
            public double ThicknessMm { get; private set; } = 20.0;
            public bool UseGenericModel { get; private set; } = false;
            public double SlopedFaceFloorMaxAngleDeg { get; private set; } = 45.0;

            public PickFacePalette(Document doc)
            {
                _doc = doc;
                Title = "AR 裝修 - 面生面";
                Width = 520; Height = 360;
                WindowStyle = System.Windows.WindowStyle.ToolWindow;
                ResizeMode = System.Windows.ResizeMode.NoResize;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                var root = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(10) };
                Content = root;
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                // row0：選擇模式（單選按鈕）
                var row0 = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 12) };
                row0.Children.Add(new System.Windows.Controls.Label { Content = "創建模式：", Width = 80, VerticalAlignment = System.Windows.VerticalAlignment.Center, FontWeight = System.Windows.FontWeights.Bold });
                _rbUseType = new System.Windows.Controls.RadioButton { Content = "使用牆/樓板類型", IsChecked = true, Margin = new System.Windows.Thickness(0, 0, 20, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                _rbUseGenericModel = new System.Windows.Controls.RadioButton { Content = "使用一般模型", VerticalAlignment = System.Windows.VerticalAlignment.Center };
                row0.Children.Add(_rbUseType);
                row0.Children.Add(_rbUseGenericModel);
                root.Children.Add(row0);
                System.Windows.Controls.Grid.SetRow(row0, 0);

                // row1：類型選擇面板（牆類型 + 樓板類型）
                _panelType = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 0, 0, 12) };

                var row1a = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row1a.Children.Add(new System.Windows.Controls.Label { Content = "牆類型：", Width = 80, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                _cmbWall = new System.Windows.Controls.ComboBox { Width = 330, IsEditable = false };
                _cmbWall.Items.Add(new TypeItem("＜不指定＞", ElementId.InvalidElementId));
                var wallTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .Where(wt => wt.Kind == WallKind.Basic)
                    .OrderBy(wt => wt.Name);
                foreach (var wt in wallTypes) _cmbWall.Items.Add(new TypeItem(wt.Name, wt.Id));
                _cmbWall.SelectedIndex = 0;
                row1a.Children.Add(_cmbWall);
                _panelType.Children.Add(row1a);

                var row1b = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row1b.Children.Add(new System.Windows.Controls.Label { Content = "樓板類型：", Width = 80, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                _cmbFloor = new System.Windows.Controls.ComboBox { Width = 330, IsEditable = false };
                _cmbFloor.Items.Add(new TypeItem("＜不指定＞", ElementId.InvalidElementId));
                var floorTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .OrderBy(ft => ft.Name);
                foreach (var ft in floorTypes) _cmbFloor.Items.Add(new TypeItem(ft.Name, ft.Id));
                _cmbFloor.SelectedIndex = 0;
                row1b.Children.Add(_cmbFloor);
                _panelType.Children.Add(row1b);

                var row1c = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row1c.Children.Add(new System.Windows.Controls.Label { Content = "斜面規則：", Width = 80, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                row1c.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "與水平夾角 ≤",
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new System.Windows.Thickness(0, 0, 6, 0)
                });
                _txtSlopedFaceFloorMaxAngle = new System.Windows.Controls.TextBox { Width = 55, Text = "45" };
                row1c.Children.Add(_txtSlopedFaceFloorMaxAngle);
                row1c.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "° 用樓板，較陡用牆",
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new System.Windows.Thickness(6, 0, 0, 0)
                });
                _panelType.Children.Add(row1c);

                var row1d = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var lblInfo1 = new System.Windows.Controls.TextBlock
                {
                    Text = "提示：垂直面建立牆、水平面建立樓板；斜面保留幾何並依規則套用類型材料與厚度",
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextWrapping = System.Windows.TextWrapping.Wrap
                };
                row1d.Children.Add(lblInfo1);
                _panelType.Children.Add(row1d);

                root.Children.Add(_panelType);
                System.Windows.Controls.Grid.SetRow(_panelType, 1);

                // row2：一般模型面板（材質 + 厚度）
                _panelGenericModel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 0, 0, 12), Visibility = System.Windows.Visibility.Collapsed };

                var row2a = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row2a.Children.Add(new System.Windows.Controls.Label { Content = "材質：", Width = 80, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                _cmbMaterial = new System.Windows.Controls.ComboBox { Width = 330, IsEditable = false };
                var materials = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .OrderBy(m => m.Name);
                foreach (var mat in materials) _cmbMaterial.Items.Add(new MatItem(mat.Name, mat.Id));
                if (_cmbMaterial.Items.Count > 0) _cmbMaterial.SelectedIndex = 0;
                row2a.Children.Add(_cmbMaterial);
                _panelGenericModel.Children.Add(row2a);

                var row2b = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row2b.Children.Add(new System.Windows.Controls.Label { Content = "厚度 (mm)：", Width = 80, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                _txtThickness = new System.Windows.Controls.TextBox { Width = 100, Text = "20" };
                row2b.Children.Add(_txtThickness);
                _panelGenericModel.Children.Add(row2b);

                var row2c = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var lblInfo2 = new System.Windows.Controls.TextBlock
                {
                    Text = "提示：使用一般模型創建裝修面，適用於任意形狀",
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextWrapping = System.Windows.TextWrapping.Wrap
                };
                row2c.Children.Add(lblInfo2);
                _panelGenericModel.Children.Add(row2c);

                root.Children.Add(_panelGenericModel);
                System.Windows.Controls.Grid.SetRow(_panelGenericModel, 2);

                // 單選按鈕事件：切換面板顯示
                _rbUseType.Checked += (s, e) =>
                {
                    _panelType.Visibility = System.Windows.Visibility.Visible;
                    _panelGenericModel.Visibility = System.Windows.Visibility.Collapsed;
                };
                _rbUseGenericModel.Checked += (s, e) =>
                {
                    _panelType.Visibility = System.Windows.Visibility.Collapsed;
                    _panelGenericModel.Visibility = System.Windows.Visibility.Visible;
                };

                // row3：按鈕
                var row3 = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var ok = new System.Windows.Controls.Button { Content = "開始點選", Width = 100, Margin = new System.Windows.Thickness(0, 0, 8, 0), IsDefault = true };
                var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 80, IsCancel = true };

                ok.Click += (s, e) =>
                {
                    if (_rbUseType.IsChecked == true)
                    {
                        // 使用牆/樓板類型模式
                        var wallItem = _cmbWall.SelectedItem as TypeItem;
                        var floorItem = _cmbFloor.SelectedItem as TypeItem;

                        if ((wallItem == null || wallItem.Id == ElementId.InvalidElementId) &&
                            (floorItem == null || floorItem.Id == ElementId.InvalidElementId))
                        {
                            System.Windows.MessageBox.Show("請至少選擇一個牆類型或樓板類型。", "面生面");
                            return;
                        }

                        SelectedWallType = (wallItem != null && wallItem.Id != ElementId.InvalidElementId)
                            ? _doc.GetElement(wallItem.Id) as WallType
                            : null;

                        SelectedFloorType = (floorItem != null && floorItem.Id != ElementId.InvalidElementId)
                            ? _doc.GetElement(floorItem.Id) as FloorType
                            : null;

                        if (!double.TryParse(_txtSlopedFaceFloorMaxAngle.Text, out double slopedFaceFloorMaxAngle) ||
                            slopedFaceFloorMaxAngle < 0 ||
                            slopedFaceFloorMaxAngle > 90)
                        {
                            System.Windows.MessageBox.Show("請輸入有效的斜面規則角度（0 到 90 度）。", "面生面");
                            return;
                        }

                        SlopedFaceFloorMaxAngleDeg = slopedFaceFloorMaxAngle;
                        UseGenericModel = false;
                    }
                    else if (_rbUseGenericModel.IsChecked == true)
                    {
                        // 使用一般模型模式
                        var matItem = _cmbMaterial.SelectedItem as MatItem;
                        if (matItem == null || matItem.Id == ElementId.InvalidElementId)
                        {
                            System.Windows.MessageBox.Show("請選擇材質。", "面生面");
                            return;
                        }

                        if (!double.TryParse(_txtThickness.Text, out double thickness) || thickness <= 0)
                        {
                            System.Windows.MessageBox.Show("請輸入有效的厚度（大於 0）。", "面生面");
                            return;
                        }

                        SelectedMaterial = _doc.GetElement(matItem.Id) as Material;
                        ThicknessMm = thickness;
                        UseGenericModel = true;
                    }

                    DialogResult = true; Close();
                };
                cancel.Click += (s, e) => { DialogResult = false; Close(); };

                row3.Children.Add(ok); row3.Children.Add(cancel);
                root.Children.Add(row3);
                System.Windows.Controls.Grid.SetRow(row3, 3);
            }

            private class TypeItem
            {
                public string Name; public ElementId Id;
                public TypeItem(string n, ElementId id) { Name = n; Id = id; }
                public override string ToString() => Name;
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
        public static ElementId GetSolidFillPatternId(Document doc)
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

        /// <summary>
        /// 接合所有相鄰的裝修牆，避免牆與牆相接處出現縫隙
        /// </summary>
        public static void JoinAdjacentFinishingWalls(Document doc, List<ElementId> createdElementIds)
        {
            try
            {
                // 篩選出所有牆元素
                var walls = createdElementIds
                    .Select(id => doc.GetElement(id))
                    .OfType<Wall>()
                    .ToList();

                if (walls.Count < 2)
                {
                    Debug.WriteLine("  ℹ️ 裝修牆數量少於2個，無需接合");
                    return;
                }

                Debug.WriteLine($"  📐 開始處理 {walls.Count} 個裝修牆的接合關係");
                int joinCount = 0;
                const double tolerance = 0.1; // 容差：0.1 英尺 ≈ 30mm
                var finishWallIds = walls.Select(w => w.Id).ToList();

                // 先全面鎖住裝修牆端點，避免 Revit 自動把既有結構一起接合進來
                foreach (var wall in walls)
                {
                    ProtectStructureFromFinishingWall(doc, wall, finishWallIds);
                }

                // 僅對「裝修牆彼此相鄰」的情況，暫時開放端點並手動接合
                for (int i = 0; i < walls.Count; i++)
                {
                    for (int j = i + 1; j < walls.Count; j++)
                    {
                        var wall1 = walls[i];
                        var wall2 = walls[j];

                        try
                        {
                            if (AreWallsAdjacent(wall1, wall2, tolerance))
                            {
                                Debug.WriteLine($"  🔗 牆面 {wall1.Id} 和 {wall2.Id} 相鄰，嘗試安全接合");

                                try
                                {
                                    WallUtils.AllowWallJoinAtEnd(wall1, 0);
                                    WallUtils.AllowWallJoinAtEnd(wall1, 1);
                                    WallUtils.AllowWallJoinAtEnd(wall2, 0);
                                    WallUtils.AllowWallJoinAtEnd(wall2, 1);

                                    if (!JoinGeometryUtils.AreElementsJoined(doc, wall1, wall2))
                                    {
                                        JoinGeometryUtils.JoinGeometry(doc, wall1, wall2);
                                        joinCount++;
                                        Debug.WriteLine($"  ✅ 成功接合牆面 {wall1.Id} 和 {wall2.Id}");
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"  ℹ️ 牆面 {wall1.Id} 和 {wall2.Id} 已經接合");
                                    }
                                }
                                catch (Exception joinEx)
                                {
                                    Debug.WriteLine($"  ⚠️ 幾何接合失敗: {joinEx.Message}");
                                }
                                finally
                                {
                                    // 接合完再次鎖定，避免它們去干涉鄰近原始結構
                                    ProtectStructureFromFinishingWall(doc, wall1, finishWallIds);
                                    ProtectStructureFromFinishingWall(doc, wall2, finishWallIds);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"  ⚠️ 檢查牆面 {wall1.Id} 和 {wall2.Id} 接合關係失敗: {ex.Message}");
                        }
                    }
                }

                Debug.WriteLine($"  ✅ 完成裝修牆接合處理，共接合 {joinCount} 對牆面");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ❌ JoinAdjacentFinishingWalls 失敗: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 保護原始結構不被裝修牆反向干涉。
        /// 預設禁止裝修牆端點自動接合；若已意外與既有結構接合，則切換成「結構切裝修」或直接解除牆對牆接合。
        /// </summary>
        public static void ProtectStructureFromFinishingWall(Document doc, Wall finishWall, ICollection<ElementId> allowedFinishWalls = null)
        {
            if (doc == null || finishWall == null) return;

            try
            {
                WallUtils.DisallowWallJoinAtEnd(finishWall, 0);
                WallUtils.DisallowWallJoinAtEnd(finishWall, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠️ 鎖定裝修牆 {finishWall.Id} 端點失敗: {ex.Message}");
            }

            try
            {
                var bbox = finishWall.get_BoundingBox(null);
                if (bbox == null) return;

                double pad = 50.0 / 304.8; // 50 mm buffer
                var outline = new Outline(
                    new XYZ(bbox.Min.X - pad, bbox.Min.Y - pad, bbox.Min.Z - pad),
                    new XYZ(bbox.Max.X + pad, bbox.Max.Y + pad, bbox.Max.Z + pad));

                var nearby = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .ToElements();

                foreach (var other in nearby)
                {
                    if (other == null || other.Id == finishWall.Id || other.Category == null)
                        continue;

                    if (allowedFinishWalls != null && allowedFinishWalls.Any(id => id.GetIdValue() == other.Id.GetIdValue()))
                        continue;

                    if (!IsStructuralJoinCandidate(other))
                        continue;

                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, finishWall, other))
                            continue;

                        // 若不慎接合，優先確保是「原始結構切裝修」，而不是反過來
                        bool structureCutsFinishing = false;
                        try
                        {
                            structureCutsFinishing = JoinGeometryUtils.IsCuttingElementInJoin(doc, other, finishWall);
                        }
                        catch
                        {
                        }

                        if (!structureCutsFinishing)
                        {
                            JoinGeometryUtils.SwitchJoinOrder(doc, finishWall, other);
                            Debug.WriteLine($"  🔄 已調整切割順序：保留原始結構，改為結構切裝修 ({other.Id} -> {finishWall.Id})");
                        }

                        // 若對象是既有牆，直接解除接合，避免牆端延伸/吞併原結構線條
                        if (other is Wall)
                        {
                            JoinGeometryUtils.UnjoinGeometry(doc, finishWall, other);
                            Debug.WriteLine($"  🔓 已解除裝修牆 {finishWall.Id} 與既有牆 {other.Id} 的幾何接合");
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Debug.WriteLine($"  ⚠️ 處理裝修牆 {finishWall.Id} 與元素 {other.Id} 的保護邏輯失敗: {innerEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠️ ProtectStructureFromFinishingWall 失敗: {ex.Message}");
            }
        }

        public static void ProtectStructureFromFinishingElement(Document doc, Element finishingElement)
        {
            if (doc == null || finishingElement == null) return;

            if (finishingElement is Wall wall)
            {
                ProtectStructureFromFinishingWall(doc, wall);
                return;
            }

            try
            {
                var bbox = finishingElement.get_BoundingBox(null);
                if (bbox == null) return;

                double pad = 50.0 / 304.8;
                var outline = new Outline(
                    new XYZ(bbox.Min.X - pad, bbox.Min.Y - pad, bbox.Min.Z - pad),
                    new XYZ(bbox.Max.X + pad, bbox.Max.Y + pad, bbox.Max.Z + pad));

                var nearby = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .ToElements();

                foreach (var other in nearby)
                {
                    if (other == null || other.Id == finishingElement.Id || other.Category == null)
                        continue;

                    if (!IsStructuralJoinCandidate(other))
                        continue;

                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, finishingElement, other))
                        {
                            JoinGeometryUtils.UnjoinGeometry(doc, finishingElement, other);
                            Debug.WriteLine($"  🔓 已解除裝修元素 {finishingElement.Id} 與結構元素 {other.Id} 的幾何接合");
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Debug.WriteLine($"  ⚠️ 保護裝修元素 {finishingElement.Id} 與結構元素 {other.Id} 失敗: {innerEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠️ ProtectStructureFromFinishingElement 失敗: {ex.Message}");
            }
        }

        private static bool IsStructuralJoinCandidate(Element element)
        {
            var categoryId = element.Category?.Id?.GetIdValue() ?? -1;
            return categoryId == (long)BuiltInCategory.OST_Walls
                || categoryId == (long)BuiltInCategory.OST_Floors
                || categoryId == (long)BuiltInCategory.OST_Ceilings
                || categoryId == (long)BuiltInCategory.OST_Roofs
                || categoryId == (long)BuiltInCategory.OST_StructuralFraming
                || categoryId == (long)BuiltInCategory.OST_StructuralColumns;
        }

        /// <summary>
        /// 判斷兩面牆是否相鄰（端點距離小於容差）
        /// </summary>
        private static bool AreWallsAdjacent(Wall wall1, Wall wall2, double tolerance)
        {
            try
            {
                var curve1 = (wall1.Location as LocationCurve)?.Curve;
                var curve2 = (wall2.Location as LocationCurve)?.Curve;

                if (curve1 == null || curve2 == null) return false;

                // 獲取端點
                var p1Start = curve1.GetEndPoint(0);
                var p1End = curve1.GetEndPoint(1);
                var p2Start = curve2.GetEndPoint(0);
                var p2End = curve2.GetEndPoint(1);

                // 檢查端點距離
                var distances = new[]
                {
                    p1Start.DistanceTo(p2Start),
                    p1Start.DistanceTo(p2End),
                    p1End.DistanceTo(p2Start),
                    p1End.DistanceTo(p2End)
                };

                var minDistance = distances.Min();

                // 如果最近距離小於容差，認為相鄰
                if (minDistance < tolerance)
                {
                    // 額外檢查：確保不是同一條線（平行且重疊）
                    if (AreWallsParallelAndOverlapping(curve1, curve2))
                    {
                        Debug.WriteLine($"  ⚠️ 牆面 {wall1.Id} 和 {wall2.Id} 重疊，不應接合");
                        return false;
                    }

                    Debug.WriteLine($"  📏 牆面 {wall1.Id} 和 {wall2.Id} 端點距離 {minDistance * 304.8:F2}mm");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠️ AreWallsAdjacent 失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 判斷兩條曲線是否平行且重疊
        /// </summary>
        private static bool AreWallsParallelAndOverlapping(Curve curve1, Curve curve2)
        {
            try
            {
                if (!(curve1 is Line line1) || !(curve2 is Line line2))
                    return false;

                // 檢查是否平行
                var dir1 = line1.Direction;
                var dir2 = line2.Direction;
                var dot = Math.Abs(dir1.DotProduct(dir2));

                if (dot < 0.99) // 不平行
                    return false;

                // 檢查是否在同一直線上
                var p1 = line1.GetEndPoint(0);
                var p2 = line2.GetEndPoint(0);
                var vec = (p2 - p1).Normalize();
                var cross = Math.Abs(vec.DotProduct(dir1));

                if (cross < 0.99) // 不在同一直線上
                    return false;

                // 檢查是否有重疊
                var proj1Start = line1.Project(line2.GetEndPoint(0));
                var proj1End = line1.Project(line2.GetEndPoint(1));

                if (proj1Start != null && proj1Start.Distance < 0.01)
                    return true;
                if (proj1End != null && proj1End.Distance < 0.01)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
