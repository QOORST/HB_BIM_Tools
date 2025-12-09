using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    public partial class PipeSleeveWindow : Window
    {
        private Document _doc;
        private List<Element> _pipes;
        private List<SleeveInfo> _sleeveInfos;

        public PipeSleeveWindow(Document doc, List<Element> pipes)
        {
            InitializeComponent();
            _doc = doc;
            _pipes = pipes;
            _sleeveInfos = new List<SleeveInfo>();

            LoadSleeveFamilies();
            UpdatePreview();
        }

        /// <summary>
        /// 載入套管族群
        /// </summary>
        private void LoadSleeveFamilies()
        {
            // 收集所有可用的套管族群
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            var families = collector.OfClass(typeof(Autodesk.Revit.DB.Family))
                .Cast<Autodesk.Revit.DB.Family>()
                .Where(f => f.FamilyCategory != null &&
                       (f.FamilyCategory.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory ||
                        f.FamilyCategory.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel))
                .ToList();

            // 填充下拉選單
            cmbWallSleeveFamily.ItemsSource = families;
            cmbWallSleeveFamily.DisplayMemberPath = "Name";
            cmbFloorSleeveFamily.ItemsSource = families;
            cmbFloorSleeveFamily.DisplayMemberPath = "Name";

            // 預設選擇第一個
            if (families.Count > 0)
            {
                cmbWallSleeveFamily.SelectedIndex = 0;
                cmbFloorSleeveFamily.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 更新預覽資訊
        /// </summary>
        private void UpdatePreview()
        {
            txtPreview.Text = $"已選擇管線: {_pipes.Count} 條\n\n" +
                             "點擊「分析」按鈕以檢測管線與牆體及結構的交集。";
        }

        /// <summary>
        /// 分析按鈕
        /// </summary>
        private void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("\n========================================");
                System.Diagnostics.Debug.WriteLine("開始分析管線...");
                System.Diagnostics.Debug.WriteLine("========================================");

                _sleeveInfos.Clear();
                int wallIntersections = 0;
                int floorIntersections = 0;
                int sleeveCounter = 1;

                System.Diagnostics.Debug.WriteLine($"管線總數: {_pipes.Count}");

                foreach (Element pipe in _pipes)
                {
                    // 取得管線的位置曲線
                    LocationCurve locCurve = pipe.Location as LocationCurve;
                    if (locCurve == null) continue;

                    Curve curve = locCurve.Curve;

                    // 取得管線直徑
                    double pipeDiameter = GetPipeDiameter(pipe);

                    // 取得管線方向
                    XYZ pipeDirection = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();

                    // 計算管線與 Z 軸的夾角
                    double dotProductZ = Math.Abs(pipeDirection.DotProduct(XYZ.BasisZ));
                    double angleWithZ = Math.Acos(Math.Min(1.0, dotProductZ)) * 180 / Math.PI;
                    bool isVertical = angleWithZ < 15;
                    bool isHorizontal = angleWithZ > 75;

                    // Debug: 輸出管線資訊
                    System.Diagnostics.Debug.WriteLine($"\n========== 分析管線 ID={pipe.Id} ==========");
                    System.Diagnostics.Debug.WriteLine($"起點: {FormatPoint(curve.GetEndPoint(0))}");
                    System.Diagnostics.Debug.WriteLine($"終點: {FormatPoint(curve.GetEndPoint(1))}");
                    System.Diagnostics.Debug.WriteLine($"方向: {FormatPoint(pipeDirection)}");
                    System.Diagnostics.Debug.WriteLine($"與Z軸夾角: {angleWithZ:F1}° (垂直={isVertical}, 水平={isHorizontal})");

                    // 檢測與牆的交集（當前文件）
                    System.Diagnostics.Debug.WriteLine($"檢測與牆的交集...");
                    var walls = FindIntersectingWalls(curve);
                    wallIntersections += walls.Count;
                    System.Diagnostics.Debug.WriteLine($"  找到 {walls.Count} 個牆交集");

                    // 檢測與樓板/樑的交集（當前文件）
                    System.Diagnostics.Debug.WriteLine($"檢測與樓板/樑的交集...");
                    var floors = FindIntersectingFloors(curve);
                    floorIntersections += floors.Count;
                    System.Diagnostics.Debug.WriteLine($"  找到 {floors.Count} 個樓板/樑交集");

                    // 儲存套管資訊（當前文件）
                    foreach (var wall in walls)
                    {
                        XYZ intersectionPoint = GetIntersectionPoint(curve, wall);

                        _sleeveInfos.Add(new SleeveInfo
                        {
                            Pipe = pipe,
                            HostElement = wall,
                            HostType = "Wall",
                            IntersectionPoint = intersectionPoint,
                            PipeDiameter = pipeDiameter,
                            PipeDirection = pipeDirection,
                            SleeveNumber = $"S-{sleeveCounter:D3}",
                            DistanceToTop = 0,
                            DistanceToBottom = 0,
                            IsFromLinkedModel = false
                        });
                        sleeveCounter++;
                    }

                    foreach (var floor in floors)
                    {
                        XYZ intersectionPoint = GetIntersectionPoint(curve, floor);

                        // 正確識別元素類型
                        string hostType = "Floor";
                        string categoryName = floor.Category?.Name ?? "Unknown";

                        if (floor.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                        {
                            hostType = "Beam";
                        }
                        else if (floor.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Floors)
                        {
                            hostType = "Floor";
                        }

                        System.Diagnostics.Debug.WriteLine($"\n  檢測到元素: ID={floor.Id}, 類別={categoryName}, 識別為={hostType}");

                        // 根據管線方向判斷是否應該放置套管
                        bool shouldCreateSleeve = ShouldCreateSleeveBasedOnDirection(pipeDirection, hostType, floor);

                        if (!shouldCreateSleeve)
                        {
                            System.Diagnostics.Debug.WriteLine($"  ✗ 跳過: 管線方向與{hostType}不匹配 (ID={floor.Id})");
                            continue;
                        }

                        System.Diagnostics.Debug.WriteLine($"  ✓ 將建立套管於{hostType} (ID={floor.Id})");

                        // 計算距離頂部和底部的距離
                        var distances = CalculateDistances(intersectionPoint, floor);

                        _sleeveInfos.Add(new SleeveInfo
                        {
                            Pipe = pipe,
                            HostElement = floor,
                            HostType = hostType,
                            IntersectionPoint = intersectionPoint,
                            PipeDiameter = pipeDiameter,
                            PipeDirection = pipeDirection,
                            SleeveNumber = $"S-{sleeveCounter:D3}",
                            DistanceToTop = distances.Item1,
                            DistanceToBottom = distances.Item2,
                            IsFromLinkedModel = false
                        });
                        sleeveCounter++;
                    }

                    // 如果啟用連結模型支援，檢測連結模型中的交集
                    if (chkIncludeLinks.IsChecked == true)
                    {
                        var linkedWalls = FindIntersectingWallsInLinks(curve);
                        wallIntersections += linkedWalls.Count;

                        foreach (var linkedWall in linkedWalls)
                        {
                            _sleeveInfos.Add(new SleeveInfo
                            {
                                Pipe = pipe,
                                HostElement = linkedWall.Element,
                                HostType = linkedWall.HostType,
                                IntersectionPoint = linkedWall.IntersectionPoint,
                                PipeDiameter = pipeDiameter,
                                PipeDirection = pipeDirection,
                                SleeveNumber = $"S-{sleeveCounter:D3}",
                                DistanceToTop = 0,
                                DistanceToBottom = 0,
                                IsFromLinkedModel = true,
                                LinkedDocument = linkedWall.LinkDocument,
                                LinkTransform = linkedWall.LinkTransform
                            });
                            sleeveCounter++;
                        }

                        var linkedFloors = FindIntersectingFloorsInLinks(curve);
                        floorIntersections += linkedFloors.Count;

                        foreach (var linkedFloor in linkedFloors)
                        {
                            // 根據管線方向判斷是否應該放置套管
                            bool shouldCreateSleeve = ShouldCreateSleeveBasedOnDirection(pipeDirection, linkedFloor.HostType, linkedFloor.Element);

                            if (!shouldCreateSleeve)
                            {
                                System.Diagnostics.Debug.WriteLine($"跳過連結模型: 管線方向與{linkedFloor.HostType}不匹配 (ID={linkedFloor.Element.Id})");
                                continue;
                            }

                            // 計算距離（需要在連結座標系中計算）
                            XYZ pointInLink = linkedFloor.LinkTransform.Inverse.OfPoint(linkedFloor.IntersectionPoint);
                            var distances = CalculateDistances(pointInLink, linkedFloor.Element);

                            _sleeveInfos.Add(new SleeveInfo
                            {
                                Pipe = pipe,
                                HostElement = linkedFloor.Element,
                                HostType = linkedFloor.HostType,
                                IntersectionPoint = linkedFloor.IntersectionPoint,
                                PipeDiameter = pipeDiameter,
                                PipeDirection = pipeDirection,
                                SleeveNumber = $"S-{sleeveCounter:D3}",
                                DistanceToTop = distances.Item1,
                                DistanceToBottom = distances.Item2,
                                IsFromLinkedModel = true,
                                LinkedDocument = linkedFloor.LinkDocument,
                                LinkTransform = linkedFloor.LinkTransform
                            });
                            sleeveCounter++;
                        }
                    }
                }

                // 更新預覽
                System.Diagnostics.Debug.WriteLine("\n========================================");
                System.Diagnostics.Debug.WriteLine("分析完成！");
                System.Diagnostics.Debug.WriteLine($"管線總數: {_pipes.Count}");
                System.Diagnostics.Debug.WriteLine($"穿牆交集: {wallIntersections}");
                System.Diagnostics.Debug.WriteLine($"穿樓板/樑交集: {floorIntersections}");
                System.Diagnostics.Debug.WriteLine($"需要套管總數: {_sleeveInfos.Count}");
                System.Diagnostics.Debug.WriteLine("========================================\n");

                txtPreview.Text = $"分析結果:\n\n" +
                                 $"管線總數: {_pipes.Count} 條\n" +
                                 $"穿牆交集: {wallIntersections} 處\n" +
                                 $"穿樓板/樑交集: {floorIntersections} 處\n" +
                                 $"需要套管總數: {_sleeveInfos.Count} 個\n\n" +
                                 "點擊「執行」按鈕以放置套管。";

                btnExecute.IsEnabled = _sleeveInfos.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n!!! 分析失敗 !!!");
                System.Diagnostics.Debug.WriteLine($"錯誤訊息: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆疊追蹤:\n{ex.StackTrace}");

                txtPreview.Text = $"分析失敗:\n\n{ex.Message}\n\n詳細資訊請查看 DebugView。";

                MessageBox.Show($"分析失敗:\n\n{ex.Message}\n\n詳細資訊:\n{ex.StackTrace}", "錯誤",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 取得管線直徑
        /// </summary>
        private double GetPipeDiameter(Element pipe)
        {
            try
            {
                // 嘗試從不同的參數取得直徑
                Parameter diamParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) ??
                                     pipe.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM) ??
                                     pipe.LookupParameter("直徑") ??
                                     pipe.LookupParameter("Diameter");

                if (diamParam != null && diamParam.HasValue)
                {
                    return diamParam.AsDouble();
                }

                // 如果是風管
                if (pipe is Duct)
                {
                    Parameter widthParam = pipe.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                    Parameter heightParam = pipe.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);

                    if (widthParam != null && heightParam != null)
                    {
                        // 使用等效直徑
                        double width = widthParam.AsDouble();
                        double height = heightParam.AsDouble();
                        return Math.Sqrt(width * height);
                    }
                }

                return 0.5; // 預設值 (約 150mm)
            }
            catch
            {
                return 0.5;
            }
        }

        /// <summary>
        /// 計算距離頂部和底部的距離
        /// </summary>
        private Tuple<double, double> CalculateDistances(XYZ point, Element element)
        {
            try
            {
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox != null)
                {
                    double distanceToTop = bbox.Max.Z - point.Z;
                    double distanceToBottom = point.Z - bbox.Min.Z;
                    return new Tuple<double, double>(distanceToTop, distanceToBottom);
                }
            }
            catch { }

            return new Tuple<double, double>(0, 0);
        }

        /// <summary>
        /// 執行按鈕
        /// </summary>
        private void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cmbWallSleeveFamily.SelectedItem == null || cmbFloorSleeveFamily.SelectedItem == null)
                {
                    MessageBox.Show("請選擇套管族群。", "警告",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                double clearance = 50; // 預設 50mm
                if (!string.IsNullOrEmpty(txtClearance.Text))
                {
                    double.TryParse(txtClearance.Text, out clearance);
                }

                using (Transaction trans = new Transaction(_doc, "放置管線套管"))
                {
                    trans.Start();

                    int successCount = 0;
                    foreach (var sleeveInfo in _sleeveInfos)
                    {
                        bool success = PlaceSleeve(sleeveInfo, clearance);
                        if (success) successCount++;
                    }

                    trans.Commit();

                    MessageBox.Show($"套管放置完成！\n\n" +
                                   $"成功放置: {successCount} / {_sleeveInfos.Count} 個",
                                   "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"執行失敗:\n{ex.Message}", "錯誤",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 取消按鈕
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 尋找與曲線相交的牆
        /// </summary>
        private List<Wall> FindIntersectingWalls(Curve curve)
        {
            List<Wall> result = new List<Wall>();

            // 檢查當前文件中的牆
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            var walls = collector.OfClass(typeof(Wall)).Cast<Wall>();

            foreach (Wall wall in walls)
            {
                if (IsIntersecting(curve, wall))
                {
                    result.Add(wall);
                }
            }

            return result;
        }

        /// <summary>
        /// 根據管線方向判斷是否應該在此結構元素上放置套管
        /// </summary>
        /// <param name="pipeDirection">管線方向向量</param>
        /// <param name="hostType">結構類型 (Floor/Beam)</param>
        /// <param name="element">結構元素</param>
        /// <returns>是否應該放置套管</returns>
        private bool ShouldCreateSleeveBasedOnDirection(XYZ pipeDirection, string hostType, Element element)
        {
            if (pipeDirection == null)
            {
                System.Diagnostics.Debug.WriteLine($"  ⚠ 警告: 無法取得管線方向，跳過此元素");
                return false; // 無法取得方向時，為安全起見不建立套管
            }

            // 計算管線與 Z 軸的夾角（使用點積）
            double dotProductZ = Math.Abs(pipeDirection.DotProduct(XYZ.BasisZ));
            double angleWithZ = Math.Acos(Math.Min(1.0, dotProductZ)) * 180 / Math.PI;

            // 判斷管線是否為垂直方向（考慮洩水坡度，容差放寬到 15 度）
            // 垂直管線：與 Z 軸夾角小於 15 度
            bool isVerticalPipe = angleWithZ < 15;

            // 判斷管線是否為水平方向（考慮洩水坡度，容差放寬）
            // 水平管線：與 Z 軸夾角大於 75 度（允許最多 15 度的洩水坡度）
            bool isHorizontalPipe = angleWithZ > 75;

            System.Diagnostics.Debug.WriteLine($"  → 管線方向: {FormatPoint(pipeDirection)}");
            System.Diagnostics.Debug.WriteLine($"  → 與Z軸夾角: {angleWithZ:F1}° (垂直={isVerticalPipe}, 水平={isHorizontalPipe})");
            System.Diagnostics.Debug.WriteLine($"  → 結構類型: {hostType} (ID={element.Id})");

            if (hostType == "Floor")
            {
                // 樓板：只有垂直管線才需要套管
                if (isVerticalPipe)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✓ 垂直管線穿樓板 - 需要套管");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ 非垂直管線不穿樓板 - 跳過 (夾角={angleWithZ:F1}°)");
                    return false;
                }
            }
            else if (hostType == "Beam")
            {
                // 樑：只有水平管線才需要套管
                if (isHorizontalPipe)
                {
                    // 進一步檢查：管線方向應該與樑的方向垂直或接近垂直
                    XYZ beamDirection = GetBeamDirection(element);
                    if (beamDirection != null)
                    {
                        // 計算管線與樑的夾角
                        double dotProduct = Math.Abs(pipeDirection.DotProduct(beamDirection));
                        double angleWithBeam = Math.Acos(Math.Min(1.0, dotProduct)) * 180 / Math.PI;

                        System.Diagnostics.Debug.WriteLine($"  → 樑方向: {FormatPoint(beamDirection)}");
                        System.Diagnostics.Debug.WriteLine($"  → 管線與樑夾角: {angleWithBeam:F1}°");

                        // 如果管線與樑接近垂直（夾角 60-120 度），則需要套管
                        if (angleWithBeam > 60 && angleWithBeam < 120)
                        {
                            System.Diagnostics.Debug.WriteLine($"  ✓ 水平管線穿樑 - 需要套管");
                            return true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  ✗ 管線與樑平行 - 跳過 (夾角={angleWithBeam:F1}°)");
                            return false;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"  ✓ 水平管線穿樑 - 需要套管（無法取得樑方向）");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ 非水平管線不穿樑 - 跳過 (夾角={angleWithZ:F1}°)");
                    return false;
                }
            }

            // 牆或其他類型，預設允許
            System.Diagnostics.Debug.WriteLine($"  ✓ 其他類型 - 允許");
            return true;
        }

        /// <summary>
        /// 取得樑的方向向量
        /// </summary>
        private XYZ GetBeamDirection(Element beam)
        {
            try
            {
                // 嘗試從 LocationCurve 取得方向
                LocationCurve locCurve = beam.Location as LocationCurve;
                if (locCurve != null)
                {
                    Curve curve = locCurve.Curve;
                    XYZ direction = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                    return direction;
                }

                // 如果是 FamilyInstance，嘗試從幾何取得
                if (beam is FamilyInstance fi)
                {
                    // 使用 FacingOrientation 或 HandOrientation
                    XYZ facing = fi.FacingOrientation;
                    if (facing != null && facing.GetLength() > 0.1)
                    {
                        return facing.Normalize();
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 格式化點座標（用於 Debug 輸出）
        /// </summary>
        private string FormatPoint(XYZ point)
        {
            if (point == null) return "null";
            return $"({point.X:F3}, {point.Y:F3}, {point.Z:F3})";
        }

        /// <summary>
        /// 尋找與曲線相交的樓板/樑
        /// 優先順序：樑 > 樓板（避免重複）
        /// 改進策略：如果管線穿過樑，則忽略該區域的樓板
        /// </summary>
        private List<Element> FindIntersectingFloors(Curve curve)
        {
            List<Element> result = new List<Element>();
            List<Tuple<Element, XYZ, Curve>> beamIntersections = new List<Tuple<Element, XYZ, Curve>>();

            // 第一步：檢查當前文件中的樑（優先）
            var beams = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (Element beam in beams)
            {
                if (IsIntersecting(curve, beam))
                {
                    result.Add(beam);

                    // 記錄樑的交集資訊（元素、交集點、交集線段）
                    XYZ intersectionPoint = GetIntersectionPoint(curve, beam);
                    Curve intersectionSegment = GetIntersectionSegment(curve, beam);

                    if (intersectionPoint != null)
                    {
                        beamIntersections.Add(new Tuple<Element, XYZ, Curve>(beam, intersectionPoint, intersectionSegment));
                        System.Diagnostics.Debug.WriteLine($"樑交集: {beam.Id}, 點: {intersectionPoint}");
                    }
                }
            }

            // 第二步：檢查當前文件中的樓板（次要）
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            var floors = collector.OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (Element floor in floors)
            {
                bool intersects = IsIntersecting(curve, floor);
                System.Diagnostics.Debug.WriteLine($"檢查樓板 {floor.Id}: 相交={intersects}");

                if (intersects)
                {
                    XYZ floorIntersectionPoint = GetIntersectionPoint(curve, floor);
                    System.Diagnostics.Debug.WriteLine($"  樓板交集點: {FormatPoint(floorIntersectionPoint)}");

                    // 檢查這個樓板交集點是否在任何樑的範圍內
                    bool isWithinBeam = false;

                    foreach (var beamInfo in beamIntersections)
                    {
                        XYZ beamPoint = beamInfo.Item2;
                        Curve beamSegment = beamInfo.Item3;

                        // 策略1：檢查點距離（容差 500mm，因為樑可能比樓板厚）
                        if (floorIntersectionPoint != null && beamPoint != null)
                        {
                            double distance = beamPoint.DistanceTo(floorIntersectionPoint);
                            if (distance < 1.64) // 500mm in feet
                            {
                                isWithinBeam = true;
                                System.Diagnostics.Debug.WriteLine($"  樓板 {floor.Id} 被排除（距離樑 {beamInfo.Item1.Id} 太近: {distance * 304.8:F0}mm）");
                                break;
                            }
                        }

                        // 策略2：檢查樓板交集點是否在樑的交集線段上
                        if (beamSegment != null && floorIntersectionPoint != null)
                        {
                            IntersectionResult ir = beamSegment.Project(floorIntersectionPoint);
                            if (ir != null && ir.Distance < 1.64) // 500mm in feet
                            {
                                isWithinBeam = true;
                                System.Diagnostics.Debug.WriteLine($"  樓板 {floor.Id} 被排除（在樑 {beamInfo.Item1.Id} 的交集線段上）");
                                break;
                            }
                        }
                    }

                    // 只有不在樑範圍內時才添加樓板
                    if (!isWithinBeam)
                    {
                        result.Add(floor);
                        System.Diagnostics.Debug.WriteLine($"  ✓ 樓板交集: {floor.Id}, 點: {FormatPoint(floorIntersectionPoint)}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 取得交集線段（用於更精確的判斷）
        /// </summary>
        private Curve GetIntersectionSegment(Curve curve, Element element)
        {
            try
            {
                Options opt = new Options();
                opt.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geomElem = element.get_Geometry(opt);
                if (geomElem == null) return null;

                foreach (GeometryObject geomObj in geomElem)
                {
                    Solid solid = geomObj as Solid;
                    if (solid != null && solid.Volume > 0)
                    {
                        SolidCurveIntersectionOptions scio = new SolidCurveIntersectionOptions();
                        SolidCurveIntersection intersection = solid.IntersectWithCurve(curve, scio);

                        if (intersection != null && intersection.SegmentCount > 0)
                        {
                            // 返回第一個交集線段
                            return intersection.GetCurveSegment(0);
                        }
                    }
                    else if (geomObj is GeometryInstance)
                    {
                        GeometryInstance geomInst = geomObj as GeometryInstance;
                        GeometryElement instGeom = geomInst.GetInstanceGeometry();

                        foreach (GeometryObject instObj in instGeom)
                        {
                            Solid instSolid = instObj as Solid;
                            if (instSolid != null && instSolid.Volume > 0)
                            {
                                SolidCurveIntersectionOptions scio = new SolidCurveIntersectionOptions();
                                SolidCurveIntersection intersection = instSolid.IntersectWithCurve(curve, scio);

                                if (intersection != null && intersection.SegmentCount > 0)
                                {
                                    return intersection.GetCurveSegment(0);
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 在連結模型中尋找與曲線相交的牆，返回交集資訊
        /// </summary>
        private List<LinkedElementInfo> FindIntersectingWallsInLinks(Curve curve)
        {
            List<LinkedElementInfo> result = new List<LinkedElementInfo>();

            try
            {
                // 取得所有 Revit 連結
                FilteredElementCollector linkCollector = new FilteredElementCollector(_doc);
                var revitLinks = linkCollector.OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(link => link.GetLinkDocument() != null);

                foreach (RevitLinkInstance linkInstance in revitLinks)
                {
                    Document linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc == null) continue;

                    // 取得連結的變換矩陣
                    Transform linkTransform = linkInstance.GetTotalTransform();

                    // 將曲線轉換到連結模型的座標系統
                    Curve transformedCurve = curve.CreateTransformed(linkTransform.Inverse);

                    // 在連結文件中尋找牆
                    FilteredElementCollector wallCollector = new FilteredElementCollector(linkDoc);
                    var walls = wallCollector.OfClass(typeof(Wall)).Cast<Wall>();

                    foreach (Wall wall in walls)
                    {
                        if (IsIntersecting(transformedCurve, wall))
                        {
                            // 計算交集點（在連結模型座標系中）
                            XYZ intersectionInLink = GetIntersectionPoint(transformedCurve, wall);

                            // 轉換回當前模型座標系
                            XYZ intersectionInCurrent = linkTransform.OfPoint(intersectionInLink);

                            result.Add(new LinkedElementInfo
                            {
                                Element = wall,
                                IntersectionPoint = intersectionInCurrent,
                                HostType = "Wall",
                                LinkDocument = linkDoc,
                                LinkTransform = linkTransform,
                                LinkName = linkDoc.Title
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"檢查連結模型中的牆時發生錯誤: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 在連結模型中尋找與曲線相交的樓板/樑，返回交集資訊
        /// </summary>
        private List<LinkedElementInfo> FindIntersectingFloorsInLinks(Curve curve)
        {
            List<LinkedElementInfo> result = new List<LinkedElementInfo>();

            try
            {
                // 取得所有 Revit 連結
                FilteredElementCollector linkCollector = new FilteredElementCollector(_doc);
                var revitLinks = linkCollector.OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(link => link.GetLinkDocument() != null);

                foreach (RevitLinkInstance linkInstance in revitLinks)
                {
                    Document linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc == null) continue;

                    // 取得連結的變換矩陣
                    Transform linkTransform = linkInstance.GetTotalTransform();

                    // 將曲線轉換到連結模型的座標系統
                    Curve transformedCurve = curve.CreateTransformed(linkTransform.Inverse);

                    // 在連結文件中尋找樓板
                    FilteredElementCollector floorCollector = new FilteredElementCollector(linkDoc);
                    var floors = floorCollector.OfCategory(BuiltInCategory.OST_Floors)
                        .WhereElementIsNotElementType()
                        .ToList();

                    foreach (Element floor in floors)
                    {
                        if (IsIntersecting(transformedCurve, floor))
                        {
                            // 計算交集點（在連結模型座標系中）
                            XYZ intersectionInLink = GetIntersectionPoint(transformedCurve, floor);

                            // 轉換回當前模型座標系
                            XYZ intersectionInCurrent = linkTransform.OfPoint(intersectionInLink);

                            result.Add(new LinkedElementInfo
                            {
                                Element = floor,
                                IntersectionPoint = intersectionInCurrent,
                                HostType = "Floor",
                                LinkDocument = linkDoc,
                                LinkTransform = linkTransform,
                                LinkName = linkDoc.Title
                            });
                        }
                    }

                    // 在連結文件中尋找樑
                    FilteredElementCollector beamCollector = new FilteredElementCollector(linkDoc);
                    var beams = beamCollector.OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WhereElementIsNotElementType()
                        .ToList();

                    foreach (Element beam in beams)
                    {
                        if (IsIntersecting(transformedCurve, beam))
                        {
                            // 計算交集點（在連結模型座標系中）
                            XYZ intersectionInLink = GetIntersectionPoint(transformedCurve, beam);

                            // 轉換回當前模型座標系
                            XYZ intersectionInCurrent = linkTransform.OfPoint(intersectionInLink);

                            result.Add(new LinkedElementInfo
                            {
                                Element = beam,
                                IntersectionPoint = intersectionInCurrent,
                                HostType = "Beam",
                                LinkDocument = linkDoc,
                                LinkTransform = linkTransform,
                                LinkName = linkDoc.Title
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"檢查連結模型中的樓板/樑時發生錯誤: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 檢查曲線是否與元素相交
        /// 使用 Curve-Solid 交集檢測，並驗證交集點在曲線範圍內
        /// </summary>
        private bool IsIntersecting(Curve curve, Element element)
        {
            try
            {
                Options opt = new Options();
                opt.DetailLevel = ViewDetailLevel.Fine;
                opt.IncludeNonVisibleObjects = false;

                GeometryElement geomElem = element.get_Geometry(opt);
                if (geomElem == null) return false;

                foreach (GeometryObject geomObj in geomElem)
                {
                    Solid solid = geomObj as Solid;
                    if (solid != null && solid.Volume > 0)
                    {
                        // 檢查曲線是否與 Solid 相交
                        SolidCurveIntersectionOptions scio = new SolidCurveIntersectionOptions();
                        SolidCurveIntersection intersection = solid.IntersectWithCurve(curve, scio);

                        if (intersection != null && intersection.SegmentCount > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"  → 元素 {element.Id} 有 {intersection.SegmentCount} 個交集線段");

                            // 驗證交集線段是否在管線實際範圍內
                            for (int i = 0; i < intersection.SegmentCount; i++)
                            {
                                Curve segment = intersection.GetCurveSegment(i);
                                XYZ segStart = segment.GetEndPoint(0);
                                XYZ segEnd = segment.GetEndPoint(1);

                                System.Diagnostics.Debug.WriteLine($"    線段 {i}: 起點={FormatPoint(segStart)}, 終點={FormatPoint(segEnd)}");

                                // 檢查交集線段的端點是否在管線範圍內
                                bool startOnCurve = IsPointOnCurve(segStart, curve, curve.GetEndPoint(0), curve.GetEndPoint(1));
                                bool endOnCurve = IsPointOnCurve(segEnd, curve, curve.GetEndPoint(0), curve.GetEndPoint(1));

                                System.Diagnostics.Debug.WriteLine($"    起點在曲線上={startOnCurve}, 終點在曲線上={endOnCurve}");

                                if (startOnCurve || endOnCurve)
                                {
                                    System.Diagnostics.Debug.WriteLine($"  ✓ 檢測到相交: 元素 {element.Id}");
                                    return true;
                                }
                            }

                            System.Diagnostics.Debug.WriteLine($"  ✗ 所有交集線段都在延長線上，元素 {element.Id} 不相交");
                        }
                    }
                    else if (geomObj is GeometryInstance)
                    {
                        GeometryInstance geomInst = geomObj as GeometryInstance;
                        GeometryElement instGeom = geomInst.GetInstanceGeometry();

                        foreach (GeometryObject instObj in instGeom)
                        {
                            Solid instSolid = instObj as Solid;
                            if (instSolid != null && instSolid.Volume > 0)
                            {
                                SolidCurveIntersectionOptions scio = new SolidCurveIntersectionOptions();
                                SolidCurveIntersection instIntersection = instSolid.IntersectWithCurve(curve, scio);

                                if (instIntersection != null && instIntersection.SegmentCount > 0)
                                {
                                    // 驗證交集線段是否在管線實際範圍內
                                    for (int i = 0; i < instIntersection.SegmentCount; i++)
                                    {
                                        Curve segment = instIntersection.GetCurveSegment(i);
                                        XYZ segStart = segment.GetEndPoint(0);
                                        XYZ segEnd = segment.GetEndPoint(1);

                                        if (IsPointOnCurve(segStart, curve, curve.GetEndPoint(0), curve.GetEndPoint(1)) ||
                                            IsPointOnCurve(segEnd, curve, curve.GetEndPoint(0), curve.GetEndPoint(1)))
                                        {
                                            System.Diagnostics.Debug.WriteLine($"  ✓ 檢測到相交: 元素 {element.Id} (GeometryInstance)");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"  ✗ 未檢測到相交: 元素 {element.Id}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"  ⚠ 交集檢測失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 檢查點是否在曲線的實際範圍內（不包括延長線）
        /// </summary>
        private bool IsPointOnCurve(XYZ point, Curve curve, XYZ curveStart, XYZ curveEnd)
        {
            try
            {
                // 將點投影到曲線上
                IntersectionResult result = curve.Project(point);
                if (result == null)
                {
                    System.Diagnostics.Debug.WriteLine($"      投影失敗: 點 {FormatPoint(point)}");
                    return false;
                }

                // 取得投影點的參數
                double parameter = result.Parameter;

                System.Diagnostics.Debug.WriteLine($"      投影參數: {parameter:F4} (點: {FormatPoint(point)})");

                // 檢查參數是否在 [0, 1] 範圍內（曲線的實際範圍）
                // 加上小容差以處理浮點數誤差
                const double tolerance = 0.001;
                bool isOnCurve = parameter >= -tolerance && parameter <= 1.0 + tolerance;

                System.Diagnostics.Debug.WriteLine($"      在曲線上: {isOnCurve} (參數範圍: [{-tolerance:F4}, {1.0 + tolerance:F4}])");

                return isOnCurve;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"      IsPointOnCurve 異常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用曲線與實體的交集檢測（舊方法，已棄用）
        /// </summary>
        private bool IsIntersectingByCurve_OLD(Curve curve, Element element)
        {
            try
            {
                Options opt = new Options();
                opt.DetailLevel = ViewDetailLevel.Fine;
                opt.IncludeNonVisibleObjects = false;

                GeometryElement geomElem = element.get_Geometry(opt);
                if (geomElem == null) return false;

                foreach (GeometryObject geomObj in geomElem)
                {
                    Solid solid = geomObj as Solid;
                    if (solid != null && solid.Volume > 0)
                    {
                        SolidCurveIntersectionOptions scio = new SolidCurveIntersectionOptions();
                        SolidCurveIntersection intersection = solid.IntersectWithCurve(curve, scio);

                        if (intersection != null && intersection.SegmentCount > 0)
                        {
                            return true;
                        }
                    }
                    else if (geomObj is GeometryInstance)
                    {
                        GeometryInstance geomInst = geomObj as GeometryInstance;
                        GeometryElement instGeom = geomInst.GetInstanceGeometry();

                        foreach (GeometryObject instObj in instGeom)
                        {
                            Solid instSolid = instObj as Solid;
                            if (instSolid != null && instSolid.Volume > 0)
                            {
                                SolidCurveIntersectionOptions scio = new SolidCurveIntersectionOptions();
                                SolidCurveIntersection intersection = instSolid.IntersectWithCurve(curve, scio);

                                if (intersection != null && intersection.SegmentCount > 0)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取得交集點（改進版：計算與構件中心的交點）
        /// </summary>
        private XYZ GetIntersectionPoint(Curve curve, Element element)
        {
            try
            {
                // 先取得幾何交集線段
                Curve intersectionSegment = GetIntersectionSegment(curve, element);

                if (intersectionSegment != null)
                {
                    // 返回交集線段的中點（構件中心位置）
                    XYZ midPoint = (intersectionSegment.GetEndPoint(0) + intersectionSegment.GetEndPoint(1)) / 2;

                    System.Diagnostics.Debug.WriteLine($"    交集線段: {FormatPoint(intersectionSegment.GetEndPoint(0))} → {FormatPoint(intersectionSegment.GetEndPoint(1))}");
                    System.Diagnostics.Debug.WriteLine($"    中心點: {FormatPoint(midPoint)}");

                    return midPoint;
                }

                // 如果無法取得交集線段，使用舊方法
                Options opt = new Options();
                opt.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geomElem = element.get_Geometry(opt);
                if (geomElem == null) return (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2;

                foreach (GeometryObject geomObj in geomElem)
                {
                    Solid solid = geomObj as Solid;
                    if (solid != null && solid.Volume > 0)
                    {
                        SolidCurveIntersectionOptions scio = new SolidCurveIntersectionOptions();
                        SolidCurveIntersection intersection = solid.IntersectWithCurve(curve, scio);

                        if (intersection != null && intersection.SegmentCount > 0)
                        {
                            // 返回交集線段的中點
                            Curve segment = intersection.GetCurveSegment(0);
                            return (segment.GetEndPoint(0) + segment.GetEndPoint(1)) / 2;
                        }
                    }
                    else if (geomObj is GeometryInstance)
                    {
                        GeometryInstance geomInst = geomObj as GeometryInstance;
                        GeometryElement instGeom = geomInst.GetInstanceGeometry();

                        foreach (GeometryObject instObj in instGeom)
                        {
                            Solid instSolid = instObj as Solid;
                            if (instSolid != null && instSolid.Volume > 0)
                            {
                                SolidCurveIntersectionOptions scio = new SolidCurveIntersectionOptions();
                                SolidCurveIntersection intersection = instSolid.IntersectWithCurve(curve, scio);

                                if (intersection != null && intersection.SegmentCount > 0)
                                {
                                    Curve segment = intersection.GetCurveSegment(0);
                                    return (segment.GetEndPoint(0) + segment.GetEndPoint(1)) / 2;
                                }
                            }
                        }
                    }
                }

                // 如果找不到精確交集點，返回曲線中點
                return (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2;
            }
            catch
            {
                return (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2;
            }
        }

        /// <summary>
        /// 放置套管
        /// </summary>
        private bool PlaceSleeve(SleeveInfo sleeveInfo, double clearance)
        {
            try
            {
                Autodesk.Revit.DB.Family sleeveFamily = sleeveInfo.HostType == "Wall"
                    ? cmbWallSleeveFamily.SelectedItem as Autodesk.Revit.DB.Family
                    : cmbFloorSleeveFamily.SelectedItem as Autodesk.Revit.DB.Family;

                if (sleeveFamily == null) return false;

                // 取得族群類型（根據管徑選擇合適的尺寸）
                FamilySymbol symbol = GetAppropriateSleeveSymbol(sleeveFamily, sleeveInfo.PipeDiameter, clearance);
                if (symbol == null) return false;

                // 啟用族群類型
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    _doc.Regenerate();
                }

                // 建立套管實例
                FamilyInstance sleeve = null;

                // 根據結構類型決定放置方式
                if (sleeveInfo.HostType == "Floor")
                {
                    // 穿樓板：使用基於面的放置方式
                    sleeve = CreateFloorSleeve(sleeveInfo, symbol);
                }
                else if (sleeveInfo.HostType == "Wall")
                {
                    // 穿牆：使用基於面的放置方式
                    sleeve = CreateWallSleeve(sleeveInfo, symbol);
                }
                else if (sleeveInfo.HostType == "Beam")
                {
                    // 穿樑：使用基於面的放置方式
                    sleeve = CreateBeamSleeve(sleeveInfo, symbol);
                }
                else
                {
                    // 預設方式
                    if (sleeveInfo.IsFromLinkedModel)
                    {
                        Level level = GetNearestLevel(sleeveInfo.IntersectionPoint);
                        if (level != null)
                        {
                            sleeve = _doc.Create.NewFamilyInstance(
                                sleeveInfo.IntersectionPoint,
                                symbol,
                                level,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        }
                    }
                    else
                    {
                        sleeve = _doc.Create.NewFamilyInstance(
                            sleeveInfo.IntersectionPoint,
                            symbol,
                            sleeveInfo.HostElement,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    }
                }

                if (sleeve == null) return false;

                // 儲存套管實例
                sleeveInfo.SleeveInstance = sleeve;

                // 如果啟用自動編號
                if (chkAutoNumber.IsChecked == true)
                {
                    SetSleeveNumber(sleeve, sleeveInfo.SleeveNumber);
                }

                // 如果啟用距離測量
                if (chkMeasureDistance.IsChecked == true)
                {
                    SetSleeveDistanceParameters(sleeve, sleeveInfo);
                }

                // 如果啟用自動調整尺寸
                if (chkAutoSize.IsChecked == true)
                {
                    SetSleeveDiameter(sleeve, sleeveInfo.PipeDiameter + clearance);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"放置套管失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根據管徑選擇合適的套管類型
        /// </summary>
        private FamilySymbol GetAppropriateSleeveSymbol(Autodesk.Revit.DB.Family family, double pipeDiameter, double clearance)
        {
            try
            {
                double requiredDiameter = pipeDiameter + clearance;
                FamilySymbol bestMatch = null;
                double minDifference = double.MaxValue;

                foreach (ElementId symbolId in family.GetFamilySymbolIds())
                {
                    FamilySymbol symbol = _doc.GetElement(symbolId) as FamilySymbol;
                    if (symbol == null) continue;

                    // 嘗試取得套管直徑參數
                    Parameter diamParam = symbol.LookupParameter("直徑") ??
                                         symbol.LookupParameter("Diameter") ??
                                         symbol.LookupParameter("套管直徑");

                    if (diamParam != null && diamParam.HasValue)
                    {
                        double sleeveDiam = diamParam.AsDouble();
                        double difference = Math.Abs(sleeveDiam - requiredDiameter);

                        if (difference < minDifference)
                        {
                            minDifference = difference;
                            bestMatch = symbol;
                        }
                    }
                }

                // 如果找不到合適的，返回第一個
                return bestMatch ?? _doc.GetElement(family.GetFamilySymbolIds().First()) as FamilySymbol;
            }
            catch
            {
                return _doc.GetElement(family.GetFamilySymbolIds().First()) as FamilySymbol;
            }
        }

        /// <summary>
        /// 建立穿樓板套管
        /// </summary>
        private FamilyInstance CreateFloorSleeve(SleeveInfo sleeveInfo, FamilySymbol symbol)
        {
            try
            {
                Floor floor = sleeveInfo.HostElement as Floor;
                if (floor == null && sleeveInfo.IsFromLinkedModel)
                {
                    // 連結模型中的樓板，使用 Level
                    Level level = GetNearestLevel(sleeveInfo.IntersectionPoint);
                    if (level != null)
                    {
                        FamilyInstance sleeve = _doc.Create.NewFamilyInstance(
                            sleeveInfo.IntersectionPoint,
                            symbol,
                            level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                        // 旋轉套管使其垂直（穿樓板應該是垂直的）
                        RotateSleeveVertical(sleeve, sleeveInfo.IntersectionPoint);
                        return sleeve;
                    }
                }
                else if (floor != null)
                {
                    // 當前文件中的樓板
                    // 使用基於面的放置
                    Reference faceRef = GetTopFaceReference(floor);
                    if (faceRef != null)
                    {
                        FamilyInstance sleeve = _doc.Create.NewFamilyInstance(
                            faceRef,
                            sleeveInfo.IntersectionPoint,
                            XYZ.Zero,
                            symbol);
                        return sleeve;
                    }
                    else
                    {
                        // 如果無法取得面參考，使用樓板作為主體
                        FamilyInstance sleeve = _doc.Create.NewFamilyInstance(
                            sleeveInfo.IntersectionPoint,
                            symbol,
                            floor,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                        // 旋轉套管使其垂直
                        RotateSleeveVertical(sleeve, sleeveInfo.IntersectionPoint);
                        return sleeve;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"建立穿樓板套管失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 建立穿牆套管
        /// </summary>
        private FamilyInstance CreateWallSleeve(SleeveInfo sleeveInfo, FamilySymbol symbol)
        {
            try
            {
                Wall wall = sleeveInfo.HostElement as Wall;
                if (wall == null && sleeveInfo.IsFromLinkedModel)
                {
                    // 連結模型中的牆，使用 Level
                    Level level = GetNearestLevel(sleeveInfo.IntersectionPoint);
                    if (level != null)
                    {
                        FamilyInstance sleeve = _doc.Create.NewFamilyInstance(
                            sleeveInfo.IntersectionPoint,
                            symbol,
                            level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                        // 旋轉套管使其垂直於牆面
                        RotateSleeveToMatchWall(sleeve, sleeveInfo);
                        return sleeve;
                    }
                }
                else if (wall != null)
                {
                    // 當前文件中的牆
                    FamilyInstance sleeve = _doc.Create.NewFamilyInstance(
                        sleeveInfo.IntersectionPoint,
                        symbol,
                        wall,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                    // 套管方向應該與管線方向一致（穿過牆）
                    if (sleeveInfo.PipeDirection != null)
                    {
                        RotateSleeve(sleeve, sleeveInfo.IntersectionPoint, sleeveInfo.PipeDirection);
                    }
                    return sleeve;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"建立穿牆套管失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 建立穿樑套管
        /// </summary>
        private FamilyInstance CreateBeamSleeve(SleeveInfo sleeveInfo, FamilySymbol symbol)
        {
            try
            {
                FamilyInstance beam = sleeveInfo.HostElement as FamilyInstance;
                if (beam == null && sleeveInfo.IsFromLinkedModel)
                {
                    // 連結模型中的樑，使用 Level
                    Level level = GetNearestLevel(sleeveInfo.IntersectionPoint);
                    if (level != null)
                    {
                        FamilyInstance sleeve = _doc.Create.NewFamilyInstance(
                            sleeveInfo.IntersectionPoint,
                            symbol,
                            level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                        // 旋轉套管使其垂直於樑（與管線方向一致）
                        if (sleeveInfo.PipeDirection != null)
                        {
                            RotateSleevePerpendicularToBeam(sleeve, sleeveInfo);
                        }
                        return sleeve;
                    }
                }
                else if (beam != null)
                {
                    // 當前文件中的樑
                    FamilyInstance sleeve = _doc.Create.NewFamilyInstance(
                        sleeveInfo.IntersectionPoint,
                        symbol,
                        beam,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                    // 旋轉套管使其垂直於樑（與管線方向一致）
                    if (sleeveInfo.PipeDirection != null)
                    {
                        RotateSleevePerpendicularToBeam(sleeve, sleeveInfo);
                    }
                    return sleeve;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"建立穿樑套管失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 設定套管編號
        /// </summary>
        private void SetSleeveNumber(FamilyInstance sleeve, string number)
        {
            try
            {
                Parameter markParam = sleeve.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (markParam != null && !markParam.IsReadOnly)
                {
                    markParam.Set(number);
                }

                // 也可以設定到自訂參數
                Parameter numberParam = sleeve.LookupParameter("套管編號") ??
                                       sleeve.LookupParameter("編號");
                if (numberParam != null && !numberParam.IsReadOnly)
                {
                    numberParam.Set(number);
                }
            }
            catch { }
        }

        /// <summary>
        /// 設定套管距離參數
        /// </summary>
        private void SetSleeveDistanceParameters(FamilyInstance sleeve, SleeveInfo info)
        {
            try
            {
                // 設定距離頂部的距離
                Parameter topParam = sleeve.LookupParameter("距頂距離") ??
                                    sleeve.LookupParameter("Distance to Top");
                if (topParam != null && !topParam.IsReadOnly)
                {
                    topParam.Set(info.DistanceToTop);
                }

                // 設定距離底部的距離
                Parameter bottomParam = sleeve.LookupParameter("距底距離") ??
                                       sleeve.LookupParameter("Distance to Bottom");
                if (bottomParam != null && !bottomParam.IsReadOnly)
                {
                    bottomParam.Set(info.DistanceToBottom);
                }
            }
            catch { }
        }

        /// <summary>
        /// 設定套管直徑
        /// </summary>
        private void SetSleeveDiameter(FamilyInstance sleeve, double diameter)
        {
            try
            {
                Parameter diamParam = sleeve.LookupParameter("直徑") ??
                                     sleeve.LookupParameter("Diameter") ??
                                     sleeve.LookupParameter("套管直徑");

                if (diamParam != null && !diamParam.IsReadOnly)
                {
                    diamParam.Set(diameter);
                }
            }
            catch { }
        }

        /// <summary>
        /// 取得最接近指定點的樓層
        /// </summary>
        private Level GetNearestLevel(XYZ point)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                var levels = collector.OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => Math.Abs(l.Elevation - point.Z))
                    .ToList();

                return levels.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 旋轉套管使其垂直（用於穿樓板）
        /// </summary>
        private void RotateSleeveVertical(FamilyInstance sleeve, XYZ location)
        {
            try
            {
                // 取得套管的當前方向
                LocationPoint locPoint = sleeve.Location as LocationPoint;
                if (locPoint == null) return;

                // 檢查套管是否已經是垂直的
                // 如果不是，旋轉 90 度使其垂直
                XYZ currentDirection = sleeve.FacingOrientation;
                XYZ verticalDirection = XYZ.BasisZ;

                // 如果當前方向不是垂直的，需要旋轉
                if (Math.Abs(currentDirection.DotProduct(verticalDirection)) < 0.99)
                {
                    // 計算旋轉軸（垂直於當前方向和目標方向）
                    XYZ rotationAxis = currentDirection.CrossProduct(verticalDirection);
                    if (rotationAxis.GetLength() > 0.001)
                    {
                        rotationAxis = rotationAxis.Normalize();
                        Line axis = Line.CreateBound(location, location + rotationAxis);
                        double angle = Math.PI / 2; // 90 度
                        ElementTransformUtils.RotateElement(_doc, sleeve.Id, axis, angle);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"旋轉套管失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 旋轉套管以匹配管線方向
        /// </summary>
        private void RotateSleeve(FamilyInstance sleeve, XYZ location, XYZ direction)
        {
            try
            {
                LocationPoint locPoint = sleeve.Location as LocationPoint;
                if (locPoint == null) return;

                // 取得套管的當前方向
                XYZ currentDirection = sleeve.FacingOrientation;
                XYZ targetDirection = direction.Normalize();

                // 計算需要旋轉的角度
                double dotProduct = currentDirection.DotProduct(targetDirection);

                // 如果方向已經一致，不需要旋轉
                if (Math.Abs(dotProduct - 1.0) < 0.001) return;
                if (Math.Abs(dotProduct + 1.0) < 0.001)
                {
                    // 方向相反，旋轉 180 度
                    XYZ rotationAxis = XYZ.BasisZ;
                    Line axis = Line.CreateBound(location, location + rotationAxis);
                    ElementTransformUtils.RotateElement(_doc, sleeve.Id, axis, Math.PI);
                    return;
                }

                // 計算旋轉軸和角度
                XYZ rotationAxis2 = currentDirection.CrossProduct(targetDirection);
                if (rotationAxis2.GetLength() > 0.001)
                {
                    rotationAxis2 = rotationAxis2.Normalize();
                    double angle = Math.Acos(dotProduct);
                    Line axis = Line.CreateBound(location, location + rotationAxis2);
                    ElementTransformUtils.RotateElement(_doc, sleeve.Id, axis, angle);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"旋轉套管失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 旋轉套管使其垂直於樑
        /// 套管方向應該與管線方向一致（因為管線垂直穿過樑）
        /// </summary>
        private void RotateSleevePerpendicularToBeam(FamilyInstance sleeve, SleeveInfo sleeveInfo)
        {
            try
            {
                LocationPoint locPoint = sleeve.Location as LocationPoint;
                if (locPoint == null) return;

                // 取得樑的方向
                XYZ beamDirection = GetBeamDirection(sleeveInfo.HostElement);
                if (beamDirection == null)
                {
                    // 如果無法取得樑方向，使用管線方向
                    System.Diagnostics.Debug.WriteLine($"  ⚠ 無法取得樑方向，使用管線方向");
                    RotateSleeve(sleeve, sleeveInfo.IntersectionPoint, sleeveInfo.PipeDirection);
                    return;
                }

                // 套管方向應該與管線方向一致（管線垂直穿過樑）
                XYZ sleeveDirection = sleeveInfo.PipeDirection.Normalize();

                System.Diagnostics.Debug.WriteLine($"  樑方向: {FormatPoint(beamDirection)}");
                System.Diagnostics.Debug.WriteLine($"  管線方向: {FormatPoint(sleeveInfo.PipeDirection)}");
                System.Diagnostics.Debug.WriteLine($"  套管方向（目標）: {FormatPoint(sleeveDirection)}");

                // 旋轉套管使其與管線方向一致
                RotateSleeve(sleeve, sleeveInfo.IntersectionPoint, sleeveDirection);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"旋轉穿樑套管失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 旋轉套管以匹配牆的方向
        /// </summary>
        private void RotateSleeveToMatchWall(FamilyInstance sleeve, SleeveInfo sleeveInfo)
        {
            try
            {
                if (sleeveInfo.PipeDirection != null)
                {
                    RotateSleeve(sleeve, sleeveInfo.IntersectionPoint, sleeveInfo.PipeDirection);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"旋轉套管失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得樓板的頂面參考
        /// </summary>
        private Reference GetTopFaceReference(Floor floor)
        {
            try
            {
                Options opt = new Options();
                opt.ComputeReferences = true;
                opt.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geomElem = floor.get_Geometry(opt);
                foreach (GeometryObject geomObj in geomElem)
                {
                    Solid solid = geomObj as Solid;
                    if (solid != null && solid.Faces.Size > 0)
                    {
                        // 找到最高的面（頂面）
                        Face topFace = null;
                        double maxZ = double.MinValue;

                        foreach (Face face in solid.Faces)
                        {
                            PlanarFace planarFace = face as PlanarFace;
                            if (planarFace != null)
                            {
                                XYZ normal = planarFace.FaceNormal;
                                // 檢查是否為水平面且法向量向上
                                if (Math.Abs(normal.Z - 1.0) < 0.1)
                                {
                                    BoundingBoxUV bbox = planarFace.GetBoundingBox();
                                    XYZ center = planarFace.Evaluate((bbox.Min + bbox.Max) / 2);
                                    if (center.Z > maxZ)
                                    {
                                        maxZ = center.Z;
                                        topFace = planarFace;
                                    }
                                }
                            }
                        }

                        if (topFace != null)
                        {
                            return topFace.Reference;
                        }
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"取得樓板頂面失敗: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// 套管資訊類別
    /// </summary>
    public class SleeveInfo
    {
        public Element Pipe { get; set; }
        public Element HostElement { get; set; }
        public string HostType { get; set; }
        public XYZ IntersectionPoint { get; set; }
        public double PipeDiameter { get; set; }
        public XYZ PipeDirection { get; set; }
        public double DistanceToTop { get; set; }
        public double DistanceToBottom { get; set; }
        public string SleeveNumber { get; set; }
        public FamilyInstance SleeveInstance { get; set; }
        public bool IsFromLinkedModel { get; set; }
        public Document LinkedDocument { get; set; }
        public Transform LinkTransform { get; set; }
    }

    /// <summary>
    /// 連結模型元素資訊類別
    /// </summary>
    public class LinkedElementInfo
    {
        public Element Element { get; set; }
        public XYZ IntersectionPoint { get; set; }
        public string HostType { get; set; }
        public Document LinkDocument { get; set; }
        public Transform LinkTransform { get; set; }
        public string LinkName { get; set; }
    }
}
