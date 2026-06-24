using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    /// <summary>
    /// 模板數量計算器 - 根據圖片規範實現準確的模板面積計算
    /// </summary>
    public static class FormworkQuantityCalculator
    {
        /// <summary>
        /// 計算元素的準確模板面積
        /// </summary>
        public static double CalculateFormworkArea(Element element, List<ElementConnection> connections)
        {
            var category = element.Category?.Id?.GetIdValue();

            if (category.HasValue && ElementCategorizer.IsStairCategory(category.Value))
                return CalculateGenericFormworkArea(element, connections);
            
            switch (category)
            {
                case (int)BuiltInCategory.OST_StructuralFraming: // 梁
                    return CalculateBeamFormworkArea(element, connections);
                    
                case (int)BuiltInCategory.OST_Floors: // 板
                    return CalculateSlabFormworkArea(element, connections);
                    
                case (int)BuiltInCategory.OST_StructuralColumns: // 柱
                    return CalculateColumnFormworkArea(element, connections);
                    
                case (int)BuiltInCategory.OST_Walls: // 牆
                    return CalculateWallFormworkArea(element, connections);
                    
                default:
                    return CalculateGenericFormworkArea(element, connections);
            }
        }

        /// <summary>
        /// 梁模板面積計算 - 基本公式: 梁長 × (梁的上下模板面積)
        /// 計算方式包含頂部和底部模板面積、側邊模板面積，需扣除與樑、柱接觸的部分
        /// </summary>
        private static double CalculateBeamFormworkArea(Element beam, List<ElementConnection> connections)
        {
            try
            {
                var solids = FormworkEngine.GetElementSolids(beam).ToList();
                if (!solids.Any()) return 0;

                // 獲取梁的幾何參數
                var beamParams = GetBeamGeometry(beam, solids);
                if (beamParams == null) return 0;

                double length = beamParams.Length;
                double width = beamParams.Width;
                double height = beamParams.Height;

                // 基本模板面積計算
                double bottomArea = length * width; // 底部模板
                double topArea = length * width;    // 頂部模板 (通常不需要，但某些情況需要)
                double sideArea = 2 * (length * height); // 兩側模板

                // 計算總模板面積
                double totalArea = bottomArea + sideArea; // 通常不包含頂部

                // 扣除與其他元素接觸的部分
                double deductionArea = 0;
                foreach (var connection in connections)
                {
                    deductionArea += CalculateBeamConnectionDeduction(connection, beamParams);
                }

                double finalArea = Math.Max(0, totalArea - deductionArea);
                
                FormworkEngine.Debug.Log("梁模板計算 ID:{0} - 長:{1:F2}m 寬:{2:F2}m 高:{3:F2}m 面積:{4:F2}m² 扣除:{5:F2}m²", 
                    beam.Id.GetIdValue(), length, width, height, totalArea, deductionArea);

                return finalArea;
            }
            catch (Exception ex)
            {
                FormworkEngine.Debug.Log("梁模板計算錯誤: {0}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 板模板面積計算 - 基本公式: 板的長度 × 板的寬度
        /// 注意事項: 需扣除與樑、柱接觸的部分，若板有開口(如樓梯口)需扣除開口面積
        /// </summary>
        private static double CalculateSlabFormworkArea(Element slab, List<ElementConnection> connections)
        {
            try
            {
                var solids = FormworkEngine.GetElementSolids(slab).ToList();
                if (!solids.Any()) return 0;

                // 計算板的底面面積
                double baseArea = 0;
                foreach (var solid in solids)
                {
                    if (solid?.Volume > 1e-6)
                    {
                        // 獲取板的底面 - 通常是水平面且面積最大的面
                        var faces = solid.Faces.Cast<Face>().ToList();
                        var horizontalFaces = faces.Where(f => IsHorizontalFace(f)).OrderByDescending(f => f.Area).ToList();
                        
                        if (horizontalFaces.Any())
                        {
                            // 取最大的水平面作為底面
                            baseArea += UnitUtils.ConvertFromInternalUnits(horizontalFaces.First().Area, UnitTypeId.SquareMeters);
                        }
                    }
                }

                // 扣除與其他元素接觸的部分
                double deductionArea = 0;
                foreach (var connection in connections)
                {
                    deductionArea += CalculateSlabConnectionDeduction(connection);
                }

                // 扣除開口面積（如樓梯口、電梯井等）
                double openingArea = CalculateSlabOpenings(slab);

                double finalArea = Math.Max(0, baseArea - deductionArea - openingArea);
                
                FormworkEngine.Debug.Log("板模板計算 ID:{0} - 基本面積:{1:F2}m² 扣除連接:{2:F2}m² 扣除開口:{3:F2}m²", 
                    slab.Id.GetIdValue(), baseArea, deductionArea, openingArea);

                return finalArea;
            }
            catch (Exception ex)
            {
                FormworkEngine.Debug.Log("板模板計算錯誤: {0}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 柱模板面積計算 - 基本公式: 柱高 × 柱周長
        /// 需扣除項目: 若有樓板在柱頂四周，模板面積需減去樓板厚度；
        /// 若有梁在柱頂四周，需從柱周長中扣除梁的斷面積；若柱還有RC牆，需扣除RC牆的側面面積
        /// </summary>
        private static double CalculateColumnFormworkArea(Element column, List<ElementConnection> connections)
        {
            try
            {
                var solids = FormworkEngine.GetElementSolids(column).ToList();
                if (!solids.Any()) return 0;

                // 獲取柱的幾何參數
                var columnParams = GetColumnGeometry(column, solids);
                if (columnParams == null) return 0;

                double height = columnParams.Height;
                double perimeter = columnParams.Perimeter;

                // 基本模板面積 = 柱高 × 柱周長
                double baseArea = height * perimeter;

                // 扣除與其他元素接觸的部分
                double deductionArea = 0;
                foreach (var connection in connections)
                {
                    deductionArea += CalculateColumnConnectionDeduction(connection, columnParams);
                }

                double finalArea = Math.Max(0, baseArea - deductionArea);
                
                FormworkEngine.Debug.Log("柱模板計算 ID:{0} - 高:{1:F2}m 周長:{2:F2}m 面積:{3:F2}m² 扣除:{4:F2}m²", 
                    column.Id.GetIdValue(), height, perimeter, baseArea, deductionArea);

                return finalArea;
            }
            catch (Exception ex)
            {
                FormworkEngine.Debug.Log("柱模板計算錯誤: {0}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 牆模板面積計算 - 基本公式: 牆長 × 牆高
        /// 注意事項: 需要扣除開口(如窗戶、門)的面積；
        /// 對於有梁、板的牆，計算方式需與柱類似，從牆的總面積中扣除與梁、板接觸的面積
        /// </summary>
        private static double CalculateWallFormworkArea(Element wall, List<ElementConnection> connections)
        {
            try
            {
                var solids = FormworkEngine.GetElementSolids(wall).ToList();
                if (!solids.Any()) return 0;

                // 獲取牆的幾何參數
                var wallParams = GetWallGeometry(wall, solids);
                if (wallParams == null) return 0;

                double length = wallParams.Length;
                double height = wallParams.Height;

                // 基本模板面積 = 牆長 × 牆高 × 2 (兩面)
                double baseArea = length * height * 2;

                // 扣除開口面積（窗戶、門等）
                double openingArea = CalculateWallOpenings(wall);

                // 扣除與其他元素接觸的部分
                double deductionArea = 0;
                foreach (var connection in connections)
                {
                    deductionArea += CalculateWallConnectionDeduction(connection);
                }

                double finalArea = Math.Max(0, baseArea - openingArea - deductionArea);
                
                FormworkEngine.Debug.Log("牆模板計算 ID:{0} - 長:{1:F2}m 高:{2:F2}m 面積:{3:F2}m² 扣除開口:{4:F2}m² 扣除連接:{5:F2}m²", 
                    wall.Id.GetIdValue(), length, height, baseArea, openingArea, deductionArea);

                return finalArea;
            }
            catch (Exception ex)
            {
                FormworkEngine.Debug.Log("牆模板計算錯誤: {0}", ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 通用模板面積計算 - 用於其他類型的結構元素
        /// </summary>
        private static double CalculateGenericFormworkArea(Element element, List<ElementConnection> connections)
        {
            try
            {
                var solids = FormworkEngine.GetElementSolids(element).ToList();
                if (!solids.Any()) return 0;

                double totalArea = 0;
                foreach (var solid in solids)
                {
                    if (solid?.Volume > 1e-6)
                    {
                        var faces = solid.Faces.Cast<Face>();
                        foreach (var face in faces)
                        {
                            // 計算需要模板的面
                            if (RequiresFormwork(face))
                            {
                                totalArea += UnitUtils.ConvertFromInternalUnits(face.Area, UnitTypeId.SquareMeters);
                            }
                        }
                    }
                }

                // 扣除連接部分
                double deductionArea = 0;
                foreach (var connection in connections)
                {
                    deductionArea += connection.ContactArea * 0.8; // 80%的連接面積需要扣除
                }

                return Math.Max(0, totalArea - deductionArea);
            }
            catch (Exception ex)
            {
                FormworkEngine.Debug.Log("通用模板計算錯誤: {0}", ex.Message);
                return 0;
            }
        }

        #region 幾何參數計算輔助方法

        private static BeamGeometry GetBeamGeometry(Element beam, List<Solid> solids)
        {
            try
            {
                // ── 優先 1：從 LocationCurve 取梁長，從斷面參數取寬高（支援斜梁）
                double length = 0;
                double width = 0;
                double height = 0;

                var locCurve = beam.Location as LocationCurve;
                if (locCurve?.Curve != null)
                {
                    length = UnitUtils.ConvertFromInternalUnits(locCurve.Curve.Length, UnitTypeId.Meters);
                }

                // 從型別參數取斷面尺寸（Revit 標準參數）
                var bType = beam.Document.GetElement(beam.GetTypeId());
                if (bType != null)
                {
                    var wb = bType.LookupParameter("b") ?? bType.LookupParameter("Width") ?? bType.LookupParameter("梁寬");
                    var hb = bType.LookupParameter("h") ?? bType.LookupParameter("Height") ?? bType.LookupParameter("梁深") ?? bType.LookupParameter("梁高");
                    if (wb != null && wb.HasValue)
                        width = UnitUtils.ConvertFromInternalUnits(wb.AsDouble(), UnitTypeId.Meters);
                    if (hb != null && hb.HasValue)
                        height = UnitUtils.ConvertFromInternalUnits(hb.AsDouble(), UnitTypeId.Meters);
                }

                // 若參數取不到，退回實體 BoundingBox（沿梁方向投影）
                if (width < 1e-4 || height < 1e-4)
                {
                    var bb = GetElementBoundingBox(solids);
                    if (bb == null) return null;

                    double dx = bb.Max.X - bb.Min.X;
                    double dy = bb.Max.Y - bb.Min.Y;
                    double dz = bb.Max.Z - bb.Min.Z;

                    // 梁長方向為三邊最長者；寬/高取其餘兩邊
                    if (dx >= dy && dx >= dz)
                    {
                        if (length < 1e-4) length = UnitUtils.ConvertFromInternalUnits(dx, UnitTypeId.Meters);
                        width   = UnitUtils.ConvertFromInternalUnits(Math.Max(dy, dz), UnitTypeId.Meters);
                        height  = UnitUtils.ConvertFromInternalUnits(Math.Min(dy, dz), UnitTypeId.Meters);
                    }
                    else if (dy >= dx && dy >= dz)
                    {
                        if (length < 1e-4) length = UnitUtils.ConvertFromInternalUnits(dy, UnitTypeId.Meters);
                        width   = UnitUtils.ConvertFromInternalUnits(Math.Max(dx, dz), UnitTypeId.Meters);
                        height  = UnitUtils.ConvertFromInternalUnits(Math.Min(dx, dz), UnitTypeId.Meters);
                    }
                    else
                    {
                        if (length < 1e-4) length = UnitUtils.ConvertFromInternalUnits(dz, UnitTypeId.Meters);
                        width   = UnitUtils.ConvertFromInternalUnits(Math.Max(dx, dy), UnitTypeId.Meters);
                        height  = UnitUtils.ConvertFromInternalUnits(Math.Min(dx, dy), UnitTypeId.Meters);
                    }
                }

                if (length < 1e-4) return null;
                return new BeamGeometry { Length = length, Width = width, Height = height };
            }
            catch
            {
                return null;
            }
        }

        private static ColumnGeometry GetColumnGeometry(Element column, List<Solid> solids)
        {
            try
            {
                var boundingBox = GetElementBoundingBox(solids);
                if (boundingBox == null) return null;

                // 柱通常是垂直的，高度是Z方向
                double height = UnitUtils.ConvertFromInternalUnits(
                    boundingBox.Max.Z - boundingBox.Min.Z, UnitTypeId.Meters);

                double width = UnitUtils.ConvertFromInternalUnits(
                    boundingBox.Max.X - boundingBox.Min.X, UnitTypeId.Meters);
                
                double depth = UnitUtils.ConvertFromInternalUnits(
                    boundingBox.Max.Y - boundingBox.Min.Y, UnitTypeId.Meters);

                // 計算周長 (假設矩形截面)
                double perimeter = 2 * (width + depth);

                return new ColumnGeometry { Height = height, Width = width, Depth = depth, Perimeter = perimeter };
            }
            catch
            {
                return null;
            }
        }

        private static WallGeometry GetWallGeometry(Element wall, List<Solid> solids)
        {
            try
            {
                var boundingBox = GetElementBoundingBox(solids);
                if (boundingBox == null) return null;

                double length = UnitUtils.ConvertFromInternalUnits(
                    Math.Max(boundingBox.Max.X - boundingBox.Min.X,
                            boundingBox.Max.Y - boundingBox.Min.Y), 
                    UnitTypeId.Meters);

                double height = UnitUtils.ConvertFromInternalUnits(
                    boundingBox.Max.Z - boundingBox.Min.Z, UnitTypeId.Meters);

                double thickness = UnitUtils.ConvertFromInternalUnits(
                    Math.Min(boundingBox.Max.X - boundingBox.Min.X,
                            boundingBox.Max.Y - boundingBox.Min.Y), 
                    UnitTypeId.Meters);

                return new WallGeometry { Length = length, Height = height, Thickness = thickness };
            }
            catch
            {
                return null;
            }
        }

        private static BoundingBoxXYZ GetElementBoundingBox(List<Solid> solids)
        {
            if (!solids.Any()) return null;

            BoundingBoxXYZ result = null;
            foreach (var solid in solids)
            {
                if (solid?.Volume > 1e-6)
                {
                    var bb = solid.GetBoundingBox();
                    if (result == null)
                    {
                        result = bb;
                    }
                    else
                    {
                        // 合併包圍盒
                        result.Min = new XYZ(
                            Math.Min(result.Min.X, bb.Min.X),
                            Math.Min(result.Min.Y, bb.Min.Y),
                            Math.Min(result.Min.Z, bb.Min.Z));
                        result.Max = new XYZ(
                            Math.Max(result.Max.X, bb.Max.X),
                            Math.Max(result.Max.Y, bb.Max.Y),
                            Math.Max(result.Max.Z, bb.Max.Z));
                    }
                }
            }
            return result;
        }

        #endregion

        #region 連接扣除計算方法

        private static double CalculateBeamConnectionDeduction(ElementConnection connection, BeamGeometry beamParams)
        {
            // 使用實際幾何接觸面積（ContactArea 由 FindConnectedElements 計算），不用假設值
            switch (connection.ConnectionType)
            {
                case ConnectionType.ColumnBeam:
                    // 梁端嵌入柱：扣除梁側模 + 底模在柱寬度內的面積
                    // ContactArea = 兩側模面積 + 底模面積（在柱斷面範圍內）
                    return UnitUtils.ConvertFromInternalUnits(connection.ContactArea, UnitTypeId.SquareMeters);

                case ConnectionType.BeamSlab:
                    // 梁頂被板覆蓋：扣除頂面面積（= 梁寬 × 在板範圍內的梁長）
                    // ContactArea 即為接觸投影面積，直接使用
                    return UnitUtils.ConvertFromInternalUnits(connection.ContactArea, UnitTypeId.SquareMeters);

                default:
                    return UnitUtils.ConvertFromInternalUnits(connection.ContactArea, UnitTypeId.SquareMeters);
            }
        }

        private static double CalculateSlabConnectionDeduction(ElementConnection connection)
        {
            // 板底模扣除：凡被梁/柱佔用的投影面積均扣除（施工實務：梁體所佔位置不鋪板底模）
            return UnitUtils.ConvertFromInternalUnits(connection.ContactArea, UnitTypeId.SquareMeters);
        }

        private static double CalculateColumnConnectionDeduction(ElementConnection connection, ColumnGeometry columnParams)
        {
            // 使用實際接觸面積，而非固定假設值
            switch (connection.ConnectionType)
            {
                case ConnectionType.ColumnBeam:
                    // 柱側面被梁端遮蔽的面積：ContactArea = 梁斷面在柱側面的投影
                    return UnitUtils.ConvertFromInternalUnits(connection.ContactArea, UnitTypeId.SquareMeters);

                case ConnectionType.ColumnSlab:
                    // 柱側面被樓板包覆的面積：ContactArea = 柱周長 × 板厚
                    return UnitUtils.ConvertFromInternalUnits(connection.ContactArea, UnitTypeId.SquareMeters);

                default:
                    return UnitUtils.ConvertFromInternalUnits(connection.ContactArea, UnitTypeId.SquareMeters);
            }
        }

        private static double CalculateWallConnectionDeduction(ElementConnection connection)
        {
            // 牆側模扣除：實際接觸面積（柱/梁/板與牆的接觸部分不需要牆面模板）
            return UnitUtils.ConvertFromInternalUnits(connection.ContactArea, UnitTypeId.SquareMeters);
        }

        #endregion

        #region 開口計算方法

        private static double CalculateSlabOpenings(Element slab)
        {
            // 計算板開口面積（樓梯口、電梯井、設備孔等），單位：m²
            double openingArea = 0;
            try
            {
                var doc = slab.Document;

                // 方法 1：使用 Floor.FindInserts 取得嵌入的 Opening/FamilyInstance
                if (slab is Floor floor)
                {
                    var inserts = floor.FindInserts(true, true, true, true);
                    foreach (var insertId in inserts)
                    {
                        var insert = doc.GetElement(insertId);
                        if (insert == null) continue;

                        // 取開口元素的垂直投影面積（即板底模需扣除的面積）
                        // 優先從幾何取水平截面面積
                        var insertSolids = FormworkEngine.GetElementSolids(insert);
                        foreach (var solid in insertSolids)
                        {
                            if (solid?.Volume <= 1e-6) continue;
                            foreach (Face f in solid.Faces)
                            {
                                var n = f.ComputeNormal(new UV(0.5, 0.5));
                                // 取向下的水平面（板底投影面）
                                if (n.Z < -0.8)
                                {
                                    openingArea += UnitUtils.ConvertFromInternalUnits(f.Area, UnitTypeId.SquareMeters);
                                    break; // 每個 solid 只取一個底面
                                }
                            }
                        }
                    }
                }

                // 方法 2：尋找與板幾何重疊的 Opening 元素（Shaft Opening 也可能切穿樓板）
                // Opening 類別無 BoundarySegments，改用幾何實體取水平截面面積
                var slabBB = slab.get_BoundingBox(null);
                if (slabBB != null)
                {
                    var bbFilter = new BoundingBoxIntersectsFilter(
                        new Outline(slabBB.Min, slabBB.Max));
                    var openings = new FilteredElementCollector(doc)
                        .OfClass(typeof(Opening))
                        .WherePasses(bbFilter)
                        .Cast<Opening>()
                        .ToList();

                    foreach (var opening in openings)
                    {
                        // 從 Opening 幾何取水平截面面積（向下面 = 板底開口投影）
                        var openingSolids = FormworkEngine.GetElementSolids(opening);
                        foreach (var solid in openingSolids)
                        {
                            if (solid?.Volume <= 1e-6) continue;
                            foreach (Face f in solid.Faces)
                            {
                                try
                                {
                                    XYZ fn = f.ComputeNormal(new UV(0.5, 0.5));
                                    if (fn.Z < -0.8)
                                    {
                                        openingArea += UnitUtils.ConvertFromInternalUnits(
                                            f.Area, UnitTypeId.SquareMeters);
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FormworkEngine.Debug.Log("計算板開口錯誤: {0}", ex.Message);
            }
            return openingArea;
        }

        private static double CalculateWallOpenings(Element wall)
        {
            double openingArea = 0;
            if (!(wall is Wall wallElement)) return 0;

            try
            {
                var doc = wall.Document;
                var wallInserts = wallElement.FindInserts(true, true, true, true);

                foreach (var insertId in wallInserts)
                {
                    var insert = doc.GetElement(insertId);
                    if (insert == null) continue;

                    // 優先從開口元素的 Width/Height 參數取正確面積
                    // （適用於門、窗、洞口 FamilyInstance）
                    double w = 0, h = 0;
                    var pW = insert.LookupParameter("Width") ?? insert.LookupParameter("寬度");
                    var pH = insert.LookupParameter("Height") ?? insert.LookupParameter("高度");

                    if (pW != null && pW.HasValue && pH != null && pH.HasValue)
                    {
                        w = UnitUtils.ConvertFromInternalUnits(pW.AsDouble(), UnitTypeId.Meters);
                        h = UnitUtils.ConvertFromInternalUnits(pH.AsDouble(), UnitTypeId.Meters);
                        if (w > 0 && h > 0)
                        {
                            openingArea += w * h * 2; // 兩面各扣除一次
                            continue;
                        }
                    }

                    // 退路：取開口幾何在牆面方向上的最大面面積
                    // 牆法向量方向投影面為實際開口面
                    XYZ wallNormal = GetWallNormal(wallElement);
                    var insertSolids = FormworkEngine.GetElementSolids(insert);
                    foreach (var solid in insertSolids)
                    {
                        if (solid?.Volume <= 1e-6) continue;
                        double maxFaceArea = 0;
                        foreach (Face f in solid.Faces)
                        {
                            try
                            {
                                XYZ fn = f.ComputeNormal(new UV(0.5, 0.5));
                                // 取與牆面法向量平行的面（即洞口正面）
                                if (Math.Abs(fn.DotProduct(wallNormal)) > 0.8)
                                    maxFaceArea = Math.Max(maxFaceArea, f.Area);
                            }
                            catch { }
                        }
                        openingArea += UnitUtils.ConvertFromInternalUnits(maxFaceArea, UnitTypeId.SquareMeters) * 2;
                    }
                }
            }
            catch (Exception ex)
            {
                FormworkEngine.Debug.Log("計算牆開口錯誤: {0}", ex.Message);
            }

            return openingArea;
        }

        private static XYZ GetWallNormal(Wall wall)
        {
            try
            {
                var locCurve = wall.Location as LocationCurve;
                if (locCurve?.Curve is Line line)
                {
                    var dir = line.Direction.Normalize();
                    return new XYZ(-dir.Y, dir.X, 0).Normalize(); // 垂直於牆長方向
                }
            }
            catch { }
            return XYZ.BasisX;
        }

        #endregion

        #region 輔助方法

        private static bool IsHorizontalFace(Face face)
        {
            try
            {
                var normal = face.ComputeNormal(new UV(0.5, 0.5));
                // 檢查法向量是否接近垂直（Z方向）
                return Math.Abs(normal.Z) > 0.8;
            }
            catch
            {
                return false;
            }
        }

        private static bool RequiresFormwork(Face face)
        {
            try
            {
                var normal = face.ComputeNormal(new UV(0.5, 0.5));
                // 向下的面通常需要模板支撐
                return normal.Z < -0.1 || Math.Abs(normal.Z) < 0.8; // 底面或側面
            }
            catch
            {
                return true; // 默認需要模板
            }
        }

        #endregion

        #region 幾何數據結構

        private class BeamGeometry
        {
            public double Length { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }

        private class ColumnGeometry
        {
            public double Height { get; set; }
            public double Width { get; set; }
            public double Depth { get; set; }
            public double Perimeter { get; set; }
        }

        private class WallGeometry
        {
            public double Length { get; set; }
            public double Height { get; set; }
            public double Thickness { get; set; }
        }

        #endregion
    }
}
