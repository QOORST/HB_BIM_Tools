using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

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
            var category = element.Category?.Id?.Value;
            
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
                    beam.Id.Value, length, width, height, totalArea, deductionArea);

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
                    slab.Id.Value, baseArea, deductionArea, openingArea);

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
                    column.Id.Value, height, perimeter, baseArea, deductionArea);

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
                    wall.Id.Value, length, height, baseArea, openingArea, deductionArea);

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
                var boundingBox = GetElementBoundingBox(solids);
                if (boundingBox == null) return null;

                double length = UnitUtils.ConvertFromInternalUnits(
                    Math.Max(boundingBox.Max.X - boundingBox.Min.X,
                            Math.Max(boundingBox.Max.Y - boundingBox.Min.Y,
                                    boundingBox.Max.Z - boundingBox.Min.Z)), 
                    UnitTypeId.Meters);

                double width = UnitUtils.ConvertFromInternalUnits(
                    Math.Min(boundingBox.Max.X - boundingBox.Min.X,
                            Math.Max(boundingBox.Max.Y - boundingBox.Min.Y,
                                    boundingBox.Max.Z - boundingBox.Min.Z)), 
                    UnitTypeId.Meters);

                double height = UnitUtils.ConvertFromInternalUnits(
                    Math.Min(boundingBox.Max.X - boundingBox.Min.X,
                            Math.Min(boundingBox.Max.Y - boundingBox.Min.Y,
                                    boundingBox.Max.Z - boundingBox.Min.Z)), 
                    UnitTypeId.Meters);

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
            switch (connection.ConnectionType)
            {
                case ConnectionType.ColumnBeam:
                    // 梁與柱連接：扣除柱寬度範圍內的模板面積
                    return beamParams.Height * 0.3; // 假設柱寬約30cm
                    
                case ConnectionType.BeamSlab:
                    // 梁與板連接：通常梁頂部被板覆蓋
                    return beamParams.Length * beamParams.Width * 0.8; // 80%的頂部面積
                    
                default:
                    return connection.ContactArea * 0.5;
            }
        }

        private static double CalculateSlabConnectionDeduction(ElementConnection connection)
        {
            switch (connection.ConnectionType)
            {
                case ConnectionType.BeamSlab:
                    // 板與梁連接：梁佔用的面積
                    return connection.ContactArea;
                    
                case ConnectionType.ColumnSlab:
                    // 板與柱連接：柱佔用的面積
                    return connection.ContactArea;
                    
                default:
                    return connection.ContactArea * 0.8;
            }
        }

        private static double CalculateColumnConnectionDeduction(ElementConnection connection, ColumnGeometry columnParams)
        {
            switch (connection.ConnectionType)
            {
                case ConnectionType.ColumnBeam:
                    // 柱與梁連接：扣除梁高度範圍內的周長面積
                    return columnParams.Perimeter * 0.6; // 假設梁高約60cm
                    
                case ConnectionType.ColumnSlab:
                    // 柱與板連接：扣除板厚度範圍內的周長面積
                    return columnParams.Perimeter * 0.15; // 假設板厚約15cm
                    
                default:
                    return connection.ContactArea;
            }
        }

        private static double CalculateWallConnectionDeduction(ElementConnection connection)
        {
            return connection.ContactArea * 0.9; // 90%的連接面積需要扣除
        }

        #endregion

        #region 開口計算方法

        private static double CalculateSlabOpenings(Element slab)
        {
            // TODO: 實現板開口計算（如樓梯口、電梯井等）
            // 這需要分析板的幾何形狀或從參數中獲取開口信息
            return 0;
        }

        private static double CalculateWallOpenings(Element wall)
        {
            double openingArea = 0;
            
            try
            {
                // 對於牆類型，需要強制轉換為 Wall 才能使用 FindInserts
                if (wall is Wall wallElement)
                {
                    var wallOpenings = wallElement.FindInserts(true, true, true, true);
                    
                    foreach (ElementId openingId in wallOpenings)
                    {
                        var opening = wall.Document.GetElement(openingId);
                        if (opening != null)
                        {
                            // 計算開口面積
                            var openingSolids = FormworkEngine.GetElementSolids(opening).ToList();
                            foreach (var solid in openingSolids)
                            {
                                if (solid?.Volume > 1e-6)
                                {
                                    // 估算開口面積（使用包圍盒的最大面）
                                    var bb = solid.GetBoundingBox();
                                    double width = UnitUtils.ConvertFromInternalUnits(bb.Max.X - bb.Min.X, UnitTypeId.Meters);
                                    double height = UnitUtils.ConvertFromInternalUnits(bb.Max.Z - bb.Min.Z, UnitTypeId.Meters);
                                    openingArea += width * height;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FormworkEngine.Debug.Log("計算牆開口錯誤: {0}", ex.Message);
            }
            
            return openingArea;
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