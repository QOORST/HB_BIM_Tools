using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    public static class FormworkEngine
    {
        #region 常數與設定
        private const double PROBE_DEPTH_MM = 50.0;
        private const double PROBE_EPS_MM = 1.0;
        private const double SHELL_EPS_MM = 0.1;
        private const double AABB_GROW_MM = 10.0;
        private const double ERROR_TOLERANCE = 1e-6;

        private static double _volThresholdMM3 = 200.0;

        private static readonly ICollection<BuiltInCategory> _catsStruct = ElementCategorizer.GetStructuralCategories(includeFoundation: true);

        private static readonly SolidOptions NoMatOpt = new SolidOptions(
            ElementId.InvalidElementId, 
            ElementId.InvalidElementId);
        #endregion

        #region 偵錯工具
        internal static class Debug
        {
            private static bool _enabled = false;
            private static readonly List<string> _logs = new List<string>();
            private static readonly Dictionary<string, Stopwatch> _timers = new Dictionary<string, Stopwatch>();

            public static void Enable(bool enable = true)
            {
                _enabled = enable;
                _logs.Clear();
                _timers.Clear();
            }

            public static void Log(string message, params object[] args)
            {
                if (!_enabled) return;
                var formatted = string.Format(message, args);
                _logs.Add(formatted);
                System.Diagnostics.Debug.WriteLine($"[Formwork] {formatted}");
            }

            public static string[] GetLogs() => _logs.ToArray();

            public static void StartTimer(string name)
            {
                if (!_enabled) return;
                if (_timers.ContainsKey(name))
                {
                    _timers[name].Restart();
                }
                else
                {
                    var sw = new Stopwatch();
                    sw.Start();
                    _timers[name] = sw;
                }
                Log("計時開始: {0}", name);
            }

            public static void StopTimer(string name)
            {
                if (!_enabled) return;
                if (_timers.ContainsKey(name))
                {
                    _timers[name].Stop();
                    Log("計時結束: {0}，耗時 {1} ms", name, _timers[name].ElapsedMilliseconds);
                }
            }
        }
        #endregion

        #region 單位轉換
        private static void ValidateParameters(double thickness, double offset)
        {
            if (thickness <= 0) throw new ArgumentException("模板厚度必須大於 0");
            if (offset < 0) throw new ArgumentException("底部偏移必須大於等於 0");
        }
        private static double Mm(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double ToM2(double a) => UnitUtils.ConvertFromInternalUnits(a, UnitTypeId.SquareMeters);
        private static bool IsNullOrTiny(Solid s) => s == null || s.Faces.IsEmpty || s.Volume < ERROR_TOLERANCE;
        #endregion

        // =================================================================================================
        // 以下為重新組織後的程式碼結構
        // =================================================================================================


        #region 幾何運算輔助方法
        private static bool IntersectBBox(Solid a, Solid b)
        {
            try 
            {
                var ba = a.GetBoundingBox();
                var bb = b.GetBoundingBox();
                return !(ba.Min.X > bb.Max.X || ba.Max.X < bb.Min.X ||
                        ba.Min.Y > bb.Max.Y || ba.Max.Y < bb.Min.Y ||
                        ba.Min.Z > bb.Max.Z || ba.Max.Z < bb.Min.Z);
            }
            catch (Exception ex)
            {
                Debug.Log("包圍盒檢查失敗: {0}", ex.Message);
                return false;
            }
        }

        private static Solid MakeProbePrism(PlanarFace pf, double depth, double eps)
        {
            try
            {
                var loops = pf.GetEdgesAsCurveLoops();
                if (loops == null || loops.Count == 0) return null;
                
                var grown = OffsetLoops(loops, eps, pf.FaceNormal);
                return CreateExtrusion(grown, pf.FaceNormal, depth, NoMatOpt);
            }
            catch (Exception ex)
            {
                Debug.Log("建立探測實體失敗: {0}", ex.Message);
                return null;
            }
        }

        private static CurveLoop[] OffsetLoops(IList<CurveLoop> loops, double offset, XYZ normal)
        {
            if (loops == null || loops.Count == 0) return new CurveLoop[0];
            try
            {
                return loops.Select(l => CurveLoop.CreateViaOffset(l, offset, normal)).ToArray();
            }
            catch (Exception ex)
            {
                Debug.Log($"偏移輪廓失敗: {ex.Message}");
                return new CurveLoop[0];
            }
        }

        private static Solid CreateExtrusion(IList<CurveLoop> loops, XYZ direction, double distance, SolidOptions opt)
        {
            try
            {
                return GeometryCreationUtilities.CreateExtrusionGeometry(loops, direction, distance, opt);
            }
            catch (Exception ex)
            {
                Debug.Log($"擠出實體失敗: {ex.Message}");
                return null;
            }
        }

        public static IList<Solid> GetElementSolids(Element e)
        {
            var result = new List<Solid>();
            try
            {
                var opt = new Options() 
                { 
                    ComputeReferences = true, 
                    DetailLevel = ViewDetailLevel.Fine 
                };
                
                var geomElem = e.get_Geometry(opt);
                if (geomElem != null)
                {
                    foreach (var obj in geomElem)
                    {
                        if (obj is Solid s && !IsNullOrTiny(s))
                        {
                            result.Add(s);
                        }
                        else if (obj is GeometryInstance gi)
                        {
                            var instanceGeom = gi.GetInstanceGeometry();
                            if (instanceGeom != null)
                            {
                                foreach (var instanceObj in instanceGeom)
                                {
                                    if (instanceObj is Solid ss && !IsNullOrTiny(ss))
                                    {
                                        result.Add(ss);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"取得元素實體失敗: {ex.Message}");
            }
            return result;
        }

        // 補上缺失的 BooleanUnionMany 方法
        private static Solid BooleanUnionMany(IList<Solid> solids)
        {
            if (solids == null || solids.Count == 0)
                return null;

            Solid result = null;
            foreach (var s in solids)
            {
                if (IsNullOrTiny(s)) continue;
                if (result == null)
                {
                    result = s;
                }
                else
                {
                    try
                    {
                        result = BooleanOperationsUtils.ExecuteBooleanOperation(result, s, BooleanOperationsType.Union);
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"布林聯集失敗: {ex.Message}");
                    }
                }
            }
            return result;
        }

        // 補上缺失的 ExtrudeFromFace 方法
        private static Solid ExtrudeFromFace(PlanarFace face, double thickness, bool grow, SolidOptions opt, XYZ direction = null)
        {
            try
            {
                var loops = face.GetEdgesAsCurveLoops();
                var normal = direction ?? face.FaceNormal;
                double offset = grow ? Mm(SHELL_EPS_MM) : 0.0;
                var grownLoops = OffsetLoops(loops, offset, normal);
                return CreateExtrusion(grownLoops, normal, thickness, opt);
            }
            catch (Exception ex)
            {
                Debug.Log($"ExtrudeFromFace 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 取得面的主要法向量（平面臉直接用 FaceNormal，曲面臉用中心點法向量）
        /// </summary>
        internal static XYZ GetFaceDominantNormal(Face face)
        {
            if (face is PlanarFace pf) return pf.FaceNormal;
            try
            {
                return face.ComputeNormal(new UV(0.5, 0.5)).Normalize();
            }
            catch
            {
                try { return face.ComputeNormal(new UV(0, 0)).Normalize(); }
                catch { return XYZ.BasisZ; }
            }
        }

        /// <summary>
        /// 統一的面→模板擠出：平面用傳統擠出，曲面用網格偏移法
        /// </summary>
        private static Solid ExtrudeOrOffsetFace(Face face, double thickness, bool grow, SolidOptions opt, XYZ direction = null)
        {
            if (face is PlanarFace pf)
                return ExtrudeFromFace(pf, thickness, grow, opt, direction);
            return CreateCurvedFormworkSolid(face, thickness);
        }

        /// <summary>
        /// 為曲面（非平面，如圓弧牆、曲梁）建立模板實體或 Mesh。
        /// 優先返回 Solid；若只有 Mesh，返回 null（呼叫端應使用 CreateCurvedFormworkGeometries 直接取幾何）。
        /// </summary>
        internal static Solid CreateCurvedFormworkSolid(Face face, double thickness)
        {
            // 優先嘗試 TessellatedShapeBuilder 方式，取 Solid
            var geoms = CreateCurvedFormworkGeometries(face, thickness);
            if (geoms != null)
            {
                var solid = geoms.OfType<Solid>().Where(s => !IsNullOrTiny(s))
                                 .OrderByDescending(s => s.Volume).FirstOrDefault();
                if (!IsNullOrTiny(solid))
                {
                    Debug.Log("CreateCurvedFormworkSolid: TessellatedShapeBuilder Solid 成功，體積={0:F6}", solid.Volume);
                    return solid;
                }
                // 有幾何但都是 Mesh（AnyGeometry fallback）→ 回傳 null，由呼叫端直接使用 geoms
                if (geoms.Count > 0)
                {
                    Debug.Log("CreateCurvedFormworkSolid: 幾何為 Mesh（非 Solid），呼叫端需改用 geoms 直接建立 DirectShape");
                    return null;
                }
            }

            // 退路：三角稜柱合併法（保留相容性）
            return CreateCurvedFormworkSolidFallback(face, thickness);
        }

        /// <summary>
        /// 為曲面建立模板幾何物件列表（使用 TessellatedShapeBuilder）。
        /// 核心演算法：
        ///   1. 三角化曲面取得網格
        ///   2. 計算每個頂點面積加權平均法向量
        ///   3. 沿頂點法向量偏移建立外側頂點
        ///   4. 用 TessellatedShapeBuilder 建立「內側面 + 外側面 + 側邊面」的封閉實體
        /// 優點：避免大量 Boolean Union 產生的幾何縫隙與視覺瑕疵。
        /// </summary>
        private static IList<GeometryObject> CreateCurvedFormworkGeometries(Face face, double thickness)
        {
            // ── 策略 1：高精度 Tessellation + AnyGeometry/Mesh（仿 Dynamo PolySurface.Surfaces 邏輯）
            // Dynamo 邏輯：PolySurface.BySolid → Surfaces → DirectShape.ByGeometry(Surface)
            // 等效 C# 作法：高精度三角化 → TessellatedShapeBuilder(AnyGeometry, Mesh)
            // 優點：不要求封閉實體，徹底消除 Salvage 產出的破面/碎片問題
            var result = TryBuildTessellatedFormwork(face, thickness, levelOfDetail: 0.02);
            if (result != null) return result;

            // ── 策略 2：降低精度重試（相容性兜底）
            result = TryBuildTessellatedFormwork(face, thickness, levelOfDetail: 0.1);
            if (result != null) return result;

            return null;
        }

        /// <summary>
        /// 仿 Dynamo DirectShape.ByGeometry(Surface) 的核心方法：
        /// 高精度三角化 → 雙層（內+外）+ 側壁 → TessellatedShapeBuilder(AnyGeometry, Mesh)
        /// AnyGeometry+Mesh 不要求封閉實體，即使拓撲有缺陷也能產出乾淨 mesh，無破面問題。
        /// </summary>
        private static IList<GeometryObject> TryBuildTessellatedFormwork(Face face, double thickness, double levelOfDetail)
        {
            try
            {
                Mesh mesh = face.Triangulate(levelOfDetail);
                if (mesh == null || mesh.NumTriangles == 0) return null;

                var meshVerts = mesh.Vertices;
                int nv = meshVerts.Count;
                int nt = mesh.NumTriangles;
                if (nv < 3 || nt == 0) return null;

                XYZ dominantNormal = GetFaceDominantNormal(face);

                // 步驟 1：收集三角形索引
                var triIdx = new int[nt][];
                for (int t = 0; t < nt; t++)
                {
                    var tri = mesh.get_Triangle(t);
                    triIdx[t] = new[] { (int)tri.get_Index(0), (int)tri.get_Index(1), (int)tri.get_Index(2) };
                }

                // 步驟 2：計算每個頂點的面積加權平均法向量（確保偏移方向一致）
                var accumX = new double[nv];
                var accumY = new double[nv];
                var accumZ = new double[nv];
                for (int t = 0; t < nt; t++)
                {
                    int i0 = triIdx[t][0], i1 = triIdx[t][1], i2 = triIdx[t][2];
                    XYZ v0 = meshVerts[i0], v1 = meshVerts[i1], v2 = meshVerts[i2];
                    XYZ cross = (v1 - v0).CrossProduct(v2 - v0);
                    double area = cross.GetLength() / 2.0;
                    if (area < 1e-10) continue;
                    XYZ n = cross.Divide(cross.GetLength());
                    if (n.DotProduct(dominantNormal) < 0) n = n.Negate();
                    accumX[i0] += n.X * area; accumX[i1] += n.X * area; accumX[i2] += n.X * area;
                    accumY[i0] += n.Y * area; accumY[i1] += n.Y * area; accumY[i2] += n.Y * area;
                    accumZ[i0] += n.Z * area; accumZ[i1] += n.Z * area; accumZ[i2] += n.Z * area;
                }

                // 步驟 3：建立內側與外側偏移頂點
                var innerVerts = new XYZ[nv];
                var outerVerts = new XYZ[nv];
                for (int i = 0; i < nv; i++)
                {
                    innerVerts[i] = meshVerts[i];
                    double len = Math.Sqrt(accumX[i] * accumX[i] + accumY[i] * accumY[i] + accumZ[i] * accumZ[i]);
                    XYZ vn = len > 1e-10 ? new XYZ(accumX[i] / len, accumY[i] / len, accumZ[i] / len) : dominantNormal;
                    outerVerts[i] = meshVerts[i] + vn.Multiply(thickness);
                }

                // 步驟 4：找出邊界邊（只出現一次的有向邊，即網格外輪廓）
                var edgeSet = new HashSet<long>();
                long maxV = (long)nv + 1L;
                for (int t = 0; t < nt; t++)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = triIdx[t][e], b = triIdx[t][(e + 1) % 3];
                        edgeSet.Add((long)a * maxV + b);
                    }

                var boundaryEdges = new List<(int from, int to)>();
                for (int t = 0; t < nt; t++)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = triIdx[t][e], b = triIdx[t][(e + 1) % 3];
                        if (!edgeSet.Contains((long)b * maxV + a))
                            boundaryEdges.Add((a, b));
                    }

                // 步驟 5：用 TessellatedShapeBuilder 建立幾何
                // ─ 關鍵修正（仿 Dynamo）─
                // Target.AnyGeometry + Fallback.Mesh：
                //   不要求封閉實體（Solid），拓撲不完美時回退為 Mesh 而非 Salvage
                //   → 徹底消除 Salvage 產出的破面/透明/碎片問題
                var builder = new TessellatedShapeBuilder();
                builder.OpenConnectedFaceSet(false); // false = 開放殼體（不強制封閉）
                var matId = ElementId.InvalidElementId;

                // 內側面（反向繞向）
                for (int t = 0; t < nt; t++)
                {
                    var idx = triIdx[t];
                    XYZ p0 = innerVerts[idx[0]], p1 = innerVerts[idx[1]], p2 = innerVerts[idx[2]];
                    if (p0.DistanceTo(p1) < 1e-6 || p1.DistanceTo(p2) < 1e-6 || p2.DistanceTo(p0) < 1e-6) continue;
                    try { builder.AddFace(new TessellatedFace(new[] { p0, p2, p1 }, matId)); }
                    catch { }
                }

                builder.CloseConnectedFaceSet(); // 關閉內側面集合

                // 外側面（正向繞向）
                builder.OpenConnectedFaceSet(false);
                for (int t = 0; t < nt; t++)
                {
                    var idx = triIdx[t];
                    XYZ p0 = outerVerts[idx[0]], p1 = outerVerts[idx[1]], p2 = outerVerts[idx[2]];
                    if (p0.DistanceTo(p1) < 1e-6 || p1.DistanceTo(p2) < 1e-6 || p2.DistanceTo(p0) < 1e-6) continue;
                    try { builder.AddFace(new TessellatedFace(new[] { p0, p1, p2 }, matId)); }
                    catch { }
                }

                builder.CloseConnectedFaceSet(); // 關閉外側面集合

                // 側邊面（邊界邊 → 四邊形，分 ConnectedFaceSet 避免拓撲錯誤）
                if (boundaryEdges.Count > 0)
                {
                    builder.OpenConnectedFaceSet(false);
                    foreach (var (fromIdx, toIdx) in boundaryEdges)
                    {
                        XYZ p0 = innerVerts[fromIdx], p1 = innerVerts[toIdx];
                        XYZ p2 = outerVerts[toIdx],   p3 = outerVerts[fromIdx];
                        if (p0.DistanceTo(p1) < 1e-6 || p1.DistanceTo(p2) < 1e-6 ||
                            p2.DistanceTo(p3) < 1e-6 || p3.DistanceTo(p0) < 1e-6) continue;
                        try { builder.AddFace(new TessellatedFace(new[] { p0, p3, p2, p1 }, matId)); }
                        catch { }
                    }
                    builder.CloseConnectedFaceSet();
                }

                // ─ 核心修正：AnyGeometry + Mesh（不強制 Solid，避免破面）─
                builder.Target   = TessellatedShapeBuilderTarget.AnyGeometry;
                builder.Fallback = TessellatedShapeBuilderFallback.Mesh;

                builder.Build();
                var buildResult = builder.GetBuildResult();
                if (buildResult != null && buildResult.Outcome != TessellatedShapeBuilderOutcome.Nothing)
                {
                    var geoms = buildResult.GetGeometricalObjects().ToList();
                    if (geoms.Count > 0)
                    {
                        Debug.Log("TryBuildTessellatedFormwork(lod={0}): 成功，{1} 個幾何物件，成果={2}",
                            levelOfDetail, geoms.Count, buildResult.Outcome);
                        return geoms;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"TryBuildTessellatedFormwork(lod={levelOfDetail}) 失敗: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 退路：三角稜柱合併法（原始方式，保留為備用）
        /// </summary>
        private static Solid CreateCurvedFormworkSolidFallback(Face face, double thickness)
        {
            try
            {
                Mesh mesh = face.Triangulate(0.8);
                if (mesh == null || mesh.NumTriangles == 0) return null;

                var meshVerts = mesh.Vertices;
                int nt = mesh.NumTriangles;
                if (meshVerts.Count < 3) return null;

                XYZ dominantNormal = GetFaceDominantNormal(face);
                var prisms = new List<Solid>();

                for (int t = 0; t < nt; t++)
                {
                    try
                    {
                        var tri = mesh.get_Triangle(t);
                        XYZ v0 = meshVerts[(int)tri.get_Index(0)];
                        XYZ v1 = meshVerts[(int)tri.get_Index(1)];
                        XYZ v2 = meshVerts[(int)tri.get_Index(2)];

                        XYZ triNormal = (v1 - v0).CrossProduct(v2 - v0);
                        double len = triNormal.GetLength();
                        if (len < 1e-10) continue;
                        triNormal = triNormal.Divide(len);
                        if (triNormal.DotProduct(dominantNormal) < 0)
                            triNormal = triNormal.Negate();

                        var loop = new CurveLoop();
                        loop.Append(Line.CreateBound(v0, v1));
                        loop.Append(Line.CreateBound(v1, v2));
                        loop.Append(Line.CreateBound(v2, v0));

                        var prism = GeometryCreationUtilities.CreateExtrusionGeometry(
                            new List<CurveLoop> { loop }, triNormal, thickness);

                        if (!IsNullOrTiny(prism))
                            prisms.Add(prism);
                    }
                    catch { }
                }

                if (prisms.Count == 0) return null;
                return BooleanUnionMany(prisms);
            }
            catch (Exception ex)
            {
                Debug.Log($"CreateCurvedFormworkSolidFallback 失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 為 CylindricalFace（弧牆曲面）建立旋轉體模板實體。
        /// 使用 CreateRevolvedGeometry 產生真正的圓柱 BREP 曲面，完全消除條紋/破面。
        /// </summary>
        private static Solid TryCreateRevolutionFormwork(Face face, double thicknessFt)
        {
            try
            {
                var loops = face.GetEdgesAsCurveLoops();
                if (loops == null || loops.Count == 0) return null;

                // 收集水平圓弧（頂/底圓弧），並取得 Z 範圍
                Arc refArc = null;
                double minZ = double.MaxValue, maxZ = double.MinValue;

                foreach (var loop in loops)
                {
                    foreach (var curve in loop)
                    {
                        double z0 = curve.GetEndPoint(0).Z;
                        double z1 = curve.GetEndPoint(1).Z;
                        minZ = Math.Min(minZ, Math.Min(z0, z1));
                        maxZ = Math.Max(maxZ, Math.Max(z0, z1));

                        if (curve is Arc arc)
                        {
                            // 近似水平弧（Z 跨度 < 0.01 ft ≈ 3 mm）
                            double dzSpan = Math.Abs(z1 - z0);
                            if (dzSpan < 0.01)
                            {
                                // 選最低位的水平弧作為參考
                                if (refArc == null || z0 < refArc.GetEndPoint(0).Z)
                                    refArc = arc;
                            }
                        }
                    }
                }

                if (refArc == null) return null;
                double height = maxZ - minZ;
                if (height < 1e-6) return null;

                // 判斷模板方向：面法向量是否「朝外」（遠離圓心）
                XYZ arcCenter = refArc.Center;
                XYZ arcMidPt  = refArc.Evaluate(0.5, true);
                XYZ radialDir = new XYZ(arcMidPt.X - arcCenter.X, arcMidPt.Y - arcCenter.Y, 0).Normalize();

                XYZ fn = face.ComputeNormal(new UV(0.5, 0.5));
                XYZ fn2D = new XYZ(fn.X, fn.Y, 0);
                if (fn2D.GetLength() < 1e-6) fn2D = radialDir;
                else fn2D = fn2D.Normalize();

                double dot = radialDir.DotProduct(fn2D);
                double faceR = refArc.Radius;
                // dot > 0 → 外側面，模板向外；dot < 0 → 內側面，模板向外（遠離牆）
                double r0 = dot >= 0 ? faceR : Math.Max(1e-4, faceR - thicknessFt);
                double r1 = dot >= 0 ? faceR + thicknessFt : faceR;
                if (r0 < 1e-4) return null;

                // 起始徑向方向（旋轉 Frame 的 X 軸）
                XYZ startPt  = refArc.GetEndPoint(0);
                XYZ startDir = new XYZ(startPt.X - arcCenter.X, startPt.Y - arcCenter.Y, 0).Normalize();
                XYZ tanDir   = new XYZ(-startDir.Y, startDir.X, 0); // CCW 90° 切線方向

                // 弧總角度（length / radius；始終為正）
                double sweepAngle = refArc.Length / faceR;
                if (sweepAngle < 1e-4 || sweepAngle > Math.PI * 2 - 1e-4) return null;

                // 如果弧是 CW（cross.Z < 0），用終點方向作為 Frame X 軸，讓革命方向對齊
                XYZ endPt  = refArc.GetEndPoint(1);
                XYZ endDir = new XYZ(endPt.X - arcCenter.X, endPt.Y - arcCenter.Y, 0).Normalize();
                double crossZ = startDir.X * endDir.Y - startDir.Y * endDir.X;
                if (crossZ < 0)
                {
                    // CW 弧：以終點為起始方向
                    startDir = endDir;
                    tanDir   = new XYZ(-startDir.Y, startDir.X, 0);
                }

                // 旋轉 Frame（Z = 世界 Z，為旋轉軸）
                var revFrame = new Frame(
                    new XYZ(arcCenter.X, arcCenter.Y, minZ),
                    startDir,
                    tanDir,
                    XYZ.BasisZ
                );

                // 旋轉剖面（矩形：r0~r1 × 0~height，位於 Frame XZ 平面）
                XYZ orig = new XYZ(arcCenter.X, arcCenter.Y, minZ);
                XYZ q00 = orig + startDir.Multiply(r0);
                XYZ q10 = orig + startDir.Multiply(r1);
                XYZ q11 = orig + startDir.Multiply(r1) + XYZ.BasisZ.Multiply(height);
                XYZ q01 = orig + startDir.Multiply(r0) + XYZ.BasisZ.Multiply(height);

                var profile = new CurveLoop();
                profile.Append(Line.CreateBound(q00, q10));
                profile.Append(Line.CreateBound(q10, q11));
                profile.Append(Line.CreateBound(q11, q01));
                profile.Append(Line.CreateBound(q01, q00));

                var solid = GeometryCreationUtilities.CreateRevolvedGeometry(
                    revFrame, new[] { profile }, 0.0, sweepAngle);

                return (!IsNullOrTiny(solid)) ? solid : null;
            }
            catch (Exception ex)
            {
                Debug.Log("TryCreateRevolutionFormwork 失敗: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 公開入口：為曲面或平面單一面建立模板 DirectShape（供 CmdPickFace 呼叫）
        /// </summary>
        public static ElementId BuildFromAnyFace(Document doc, Element host, Face face, double thicknessMm, Material mat)
        {
            if (doc == null || host == null || face == null) return ElementId.InvalidElementId;

            if (face is PlanarFace pf)
                return BuildFromFace(doc, host, pf, thicknessMm, mat);

            // 曲面路徑
            Debug.Log("BuildFromAnyFace: 曲面，Host:{0}, 厚度:{1}mm", host.Id.GetIdValue(), thicknessMm);

            double thk = Mm(thicknessMm);

            // ── 優先 1：CylindricalFace → CreateRevolvedGeometry（真正圓柱 BREP，完全無條紋/破面）
            if (face is CylindricalFace)
            {
                var revolvedSolid = TryCreateRevolutionFormwork(face, thk);
                if (!IsNullOrTiny(revolvedSolid))
                {
                    var revolvedId = CreateDS(doc, revolvedSolid, host, "CurvedFace", mat);
                    if (revolvedId != ElementId.InvalidElementId)
                    {
                        XYZ rn = GetFaceDominantNormal(face);
                        double rAreaM2 = ToM2(face.Area);
                        double rSide   = Math.Abs(Math.Abs(rn.Z) - 1.0) < 0.15 ? 0 : rAreaM2;
                        double rBottom = rAreaM2 - rSide;
                        WriteForDS(doc.GetElement(revolvedId), host, rSide, rBottom);
                        Debug.Log("BuildFromAnyFace: 旋轉體模板成功（無條紋）- 面積:{0:F3}m²", rAreaM2);
                        return revolvedId;
                    }
                }
                Debug.Log("BuildFromAnyFace: 旋轉體失敗，降級到 Tessellation");
            }

            // ── 優先 2：TessellatedShapeBuilder Solid（其他曲面或旋轉體失敗時使用）
            var tessGeoms = CreateCurvedFormworkGeometries(face, thk);
            if (tessGeoms != null && tessGeoms.Count > 0)
            {
                // 過濾出有效的幾何物件（Solid 優先，Mesh 備用）
                var validGeoms = tessGeoms
                    .Where(g => g != null)
                    .Where(g => !(g is Solid s) || !IsNullOrTiny(s))
                    .ToList();

                if (validGeoms.Count > 0)
                {
                    var ds = Autodesk.Revit.DB.DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    ds.ApplicationId = SharedParams.AppId;
                    ds.Name = "CurvedFace";
                    ds.SetShape(validGeoms);
                    if (mat != null && mat.Id != ElementId.InvalidElementId)
                    {
                        var param = ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (param != null && !param.IsReadOnly)
                            param.Set(mat.Id);
                    }

                    XYZ dominantNormal = GetFaceDominantNormal(face);
                    double areaM2 = ToM2(face.Area);
                    double sideM2   = Math.Abs(Math.Abs(dominantNormal.Z) - 1.0) < 0.15 ? 0 : areaM2;
                    double bottomM2 = Math.Abs(Math.Abs(dominantNormal.Z) - 1.0) < 0.15 ? areaM2 : 0;

                    WriteForDS(doc.GetElement(ds.Id), host, sideM2, bottomM2);
                    bool isSolid = validGeoms.Any(g => g is Solid);
                    Debug.Log("BuildFromAnyFace: 曲面模板建立成功（{0}）- 側:{1:F3}m², 底:{2:F3}m²",
                        isSolid ? "Solid" : "Mesh", sideM2, bottomM2);
                    return ds.Id;
                }
            }

            // 退路：三角稜柱合併法（最後手段，有縫隙但至少能生成）
            var formworkSolid = CreateCurvedFormworkSolidFallback(face, thk);
            if (IsNullOrTiny(formworkSolid))
            {
                Debug.Log("BuildFromAnyFace: 無法建立曲面模板實體（面積={0:F3}m²）", ToM2(face.Area));
                return ElementId.InvalidElementId;
            }

            // 鄰近結構裁切（退路才做，Mesh 路徑不做 Boolean）
            double eps    = Mm(SHELL_EPS_MM);
            double growFb = Mm(AABB_GROW_MM);
            var neighborInfosFb = GetNeighborSolidsWithElements(doc, host, thk + growFb);
            var neighborsFb     = neighborInfosFb.Select(n => n.Solid).ToList();
            var unionCutterFb   = BooleanUnionMany(neighborsFb);
            formworkSolid = ApplyDynamoLikeCut(formworkSolid, unionCutterFb, neighborsFb, eps, true);

            if (IsNullOrTiny(formworkSolid)) return ElementId.InvalidElementId;

            var id = CreateDS(doc, formworkSolid, host, "CurvedFace", mat);

            XYZ dominantNormal2 = GetFaceDominantNormal(face);
            double areaM2b = ToM2(face.Area);
            double sideM2b  = Math.Abs(Math.Abs(dominantNormal2.Z) - 1.0) < 0.15 ? 0 : areaM2b;
            double bottomM2b = Math.Abs(Math.Abs(dominantNormal2.Z) - 1.0) < 0.15 ? areaM2b : 0;

            WriteForDS(doc.GetElement(id), host, sideM2b, bottomM2b);
            Debug.Log("BuildFromAnyFace: 曲面模板建立成功（退路）- 側:{0:F3}m², 底:{1:F3}m²", sideM2b, bottomM2b);

            return id;
        }

        // 補上缺失的 ApplyDynamoLikeCut 方法 - 平衡的接觸面扣除
        private static Solid ApplyDynamoLikeCut(
            Solid target,
            Solid unionCutter,
            IList<Solid> neighbors,
            double eps,
            bool useUnionCutter)
        {
            if (IsNullOrTiny(target)) return null;

            Solid result = target;
            double originalVolume = target.Volume;
            
            try
            {
                // 先用聯合實體進行接觸面扣除（使用適中的閾值）
                if (useUnionCutter && !IsNullOrTiny(unionCutter))
                {
                    var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(result, unionCutter, BooleanOperationsType.Intersect);
                    if (!IsNullOrTiny(intersection))
                    {
                        double intersectionRatio = intersection.Volume / originalVolume;
                        Debug.Log($"接觸面交集比例: {intersectionRatio:F3}");
                        
                        // 使用適中的閾值（10%以上才扣除）
                        if (intersectionRatio > 0.10)
                        {
                            var cut = BooleanOperationsUtils.ExecuteBooleanOperation(result, unionCutter, BooleanOperationsType.Difference);
                            if (!IsNullOrTiny(cut) && cut.Volume > originalVolume * 0.15) // 保留至少15%
                            {
                                result = cut;
                                Debug.Log($"執行聯合扣除，剩餘體積比例: {result.Volume / originalVolume:F3}");
                            }
                            else
                            {
                                Debug.Log("聯合扣除會過度削減或無剩餘，保留原始形狀");
                            }
                        }
                        else
                        {
                            Debug.Log("接觸面交集比例過小，不扣除");
                        }
                    }
                }

                // 再逐一用鄰近元素扣除（更保守的閾值）
                foreach (var neighbor in neighbors)
                {
                    if (IsNullOrTiny(neighbor)) continue;
                    if (!IntersectBBox(result, neighbor)) continue;

                    try
                    {
                        var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(result, neighbor, BooleanOperationsType.Intersect);
                        if (!IsNullOrTiny(intersection))
                        {
                            double intersectionRatio = intersection.Volume / result.Volume;
                            Debug.Log($"鄰近元素交集比例: {intersectionRatio:F3}");
                            
                            // 使用保守的閾值（15%以上才扣除）
                            if (intersectionRatio > 0.15)
                            {
                                var cut = BooleanOperationsUtils.ExecuteBooleanOperation(result, neighbor, BooleanOperationsType.Difference);
                                if (!IsNullOrTiny(cut) && cut.Volume > result.Volume * 0.25) // 每次保留至少25%
                                {
                                    result = cut;
                                    Debug.Log($"執行鄰近扣除，剩餘體積比例: {result.Volume / originalVolume:F3}");
                                }
                                else
                                {
                                    Debug.Log("鄰近扣除會過度削減，跳過此鄰近元素");
                                }
                            }
                            else
                            {
                                Debug.Log("鄰近元素交集比例過小，不扣除");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"ApplyDynamoLikeCut: 布林差運算失敗: {ex.Message}");
                    }
                }
                
                Debug.Log($"最終體積保留比例: {result.Volume / originalVolume:F3}");
            }
            catch (Exception ex)
            {
                Debug.Log($"ApplyDynamoLikeCut: 失敗: {ex.Message}");
            }
            return result;
        }

        #endregion

        #region 面檢查與探測
        private static IList<Element> QueryIntersectionsWithProbe(Document doc, Element host, Solid probe, ICollection<BuiltInCategory> cats)
        {
            try
            {
                if (probe == null) return new List<Element>();

                // 取得探測範圍的包圍盒
                var bb = probe.GetBoundingBox();
                var outline = new Outline(bb.Min, bb.Max);
                
                var filter = new ElementMulticategoryFilter(cats);
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline));

                return collector
                    .Where(e => e.Id != host.Id && IntersectsSolid(e, probe))
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.Log($"探測相交失敗: {ex.Message}");
                return new List<Element>();
            }
        }

        private static bool IntersectsSolid(Element e, Solid probe)
        {
            try
            {
                var solids = GetElementSolids(e);
                foreach (var s in solids)
                {
                    if (IsNullOrTiny(s)) continue;
                    
                    // 先用包圍盒快速過濾
                    var bb = s.GetBoundingBox();
                    var outline = new Outline(bb.Min, bb.Max);
                    
                    if (outline.Contains(probe.GetBoundingBox().Min, ERROR_TOLERANCE) ||
                        outline.Contains(probe.GetBoundingBox().Max, ERROR_TOLERANCE))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"檢查實體相交失敗: {ex.Message}");
            }
            return false;
        }

        // 判斷 PlanarFace 是否需要模板（依實務施工邏輯）
        private static bool IsFaceExposed(Document doc, Element host, PlanarFace pf, double extraDepthInternal)
        {
            Debug.Log("檢查面是否需要模板 - Host:{0}, Face.Normal:({1:F2},{2:F2},{3:F2})",
                host.Id.GetIdValue(),
                pf.FaceNormal.X, pf.FaceNormal.Y, pf.FaceNormal.Z);

            // 實務模板邏輯：大部分面都需要模板，除非是特殊情況
            var normal = pf.FaceNormal;
            
            // 1. 樓板頂面通常不需要模板（澆置面）
            if (host is Floor && normal.Z > 0.85)
            {
                Debug.Log("  => 樓板頂面，不需要模板");
                return false;
            }

            // 2. 檢查是否完全被其他結構包覆（極少見情況）
            double depth = Mm(PROBE_DEPTH_MM) + extraDepthInternal;
            Solid probe = MakeProbePrism(pf, depth, Mm(PROBE_EPS_MM));
            if (probe == null)
            {
                Debug.Log("  建立探測體失敗，假設需要模板");
                return true;
            }

            // 快速檢查：如果沒有鄰近結構，肯定需要模板
            IList<Element> hits = QueryIntersectionsWithProbe(doc, host, probe, _catsStruct);
            if (hits?.Count == 0)
            {
                Debug.Log("  => 無鄰近結構，需要模板");
                return true;
            }

            // 3. 檢查是否完全被包覆（只有在探測體積 > 80% 被其他結構佔據時才不需要模板）
            double totalBlockedVolume = 0;
            double probeVolume = probe.Volume;
            
            foreach (var hit in hits)
            {
                if (ProbeIntersectsSignificantly(doc, probe, hit))
                {
                    try
                    {
                        var hitSolids = GetElementSolids(hit);
                        foreach (var hitSolid in hitSolids)
                        {
                            if (IsNullOrTiny(hitSolid)) continue;
                            var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                                probe, hitSolid, BooleanOperationsType.Intersect);
                            if (!IsNullOrTiny(intersection))
                            {
                                totalBlockedVolume += intersection.Volume;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略布林運算錯誤
                    }
                }
            }

            double blockedRatio = totalBlockedVolume / probeVolume;
            Debug.Log("  探測體積阻擋比例: {0:P1}", blockedRatio);

            if (blockedRatio > 0.8) // 超過 80% 被阻擋才視為不需要模板
            {
                Debug.Log("  => 面被大幅包覆，不需要模板");
                return false;
            }

            Debug.Log("  => 需要模板");
            return true;
        }

        // 判斷 probe 與單一 element 是否有顯著交集（以體積為閾值，避免邊界微接觸誤判）
        private static bool ProbeIntersectsSignificantly(Document doc, Solid probe, Element e)
        {
            Debug.StartTimer("ProbeIntersectsSignificantly");
            try
            {
                double volThresholdInternal = UnitUtils.ConvertToInternalUnits(_volThresholdMM3, UnitTypeId.CubicMillimeters);

                var solids = GetElementSolids(e);
                if (solids == null || solids.Count == 0)
                {
                    Debug.Log("元素 {0} 無實體", e.Id.GetIdValue());
                    return false;
                }

                foreach (var s in solids)
                {
                    if (IsNullOrTiny(s)) continue;
                    if (!IntersectBBox(probe, s)) continue;

                    try
                    {
                        var inter = BooleanOperationsUtils.ExecuteBooleanOperation(probe, s, BooleanOperationsType.Intersect);
                        if (!IsNullOrTiny(inter))
                        {
                            double vol = inter.Volume;
                            Debug.Log("交集體積: {0:F6} (閾值: {1:F6})", vol, volThresholdInternal);
                            if (vol > volThresholdInternal)
                            {
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("布林運算失敗: {0}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log("檢查交集失敗: {0}", ex.Message);
            }
            finally
            {
                Debug.StopTimer("ProbeIntersectsSignificantly");
            }
            return false;
        }

        #endregion

        #region 彙總（沿用）
        private class Bucket { public int Count; public double Area; }
        private static readonly Dictionary<string, Bucket> _sum = new Dictionary<string, Bucket>();
        private static string _lastSum = "";

        public static void BeginRun() { _sum.Clear(); _lastSum = ""; }
        
        public static void EndRun()
        {
            var lines = new List<string>();
            double grand = 0;
            string[] cats = { "牆", "結構柱", "結構梁", "板", "樓梯" };
            string[] kinds = { "側", "底" };

            foreach (var c in cats)
            {
                double row = 0;
                foreach (var k in kinds)
                {
                    var key = c + "-" + k;
                    if (_sum.TryGetValue(key, out var b) && b.Count > 0)
                    {
                        lines.Add(string.Format("{0}（{1}）: {2:0.###} m² 〔{3} 件〕", c, k, b.Area, b.Count));
                        row += b.Area;
                    }
                }
                if (row > 0) 
                { 
                    lines.Add(string.Format("— {0}小計: {1:0.###} m²", c, row)); 
                    lines.Add(""); 
                }
                grand += row;
            }
            lines.Add(string.Format("合計: {0:0.###} m²", grand));
            _lastSum = string.Join(Environment.NewLine, lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        public static string GetSummary() => _lastSum;

        private static void AddSum(string cat, string kind, double m2)
        {
            var key = cat + "-" + kind;
            if (!_sum.TryGetValue(key, out var b)) 
            { 
                b = new Bucket(); 
                _sum[key] = b; 
            }
            b.Count++; 
            b.Area += m2;
        }

        private static void WriteForDS(Element ds, Element host, double sideAreaM2, double bottomAreaM2)
        {
            string cat = GetCategoryName(host);

            // 彙總統計
            if (Math.Abs(sideAreaM2) > ERROR_TOLERANCE)
                AddSum(cat, "側", sideAreaM2);
            if (Math.Abs(bottomAreaM2) > ERROR_TOLERANCE)
                AddSum(cat, "底", bottomAreaM2);

            // 將資訊寫入 DirectShape 的參數
            if (ds != null)
            {
                // Revit 面積單位是 ft²，需要從 m² 轉換
                double sideAreaFt2 = UnitUtils.ConvertToInternalUnits(sideAreaM2, UnitTypeId.SquareMeters);
                double bottomAreaFt2 = UnitUtils.ConvertToInternalUnits(bottomAreaM2, UnitTypeId.SquareMeters);
                double totalAreaFt2 = sideAreaFt2 + bottomAreaFt2;

                ds.LookupParameter(SharedParams.P_Category)?.Set(cat);
                ds.LookupParameter(SharedParams.P_HostId)?.Set(host.Id.GetIdValue().ToString());
                ds.LookupParameter(SharedParams.P_Total)?.Set(totalAreaFt2);
                ds.LookupParameter(SharedParams.P_EffectiveArea)?.Set(totalAreaFt2);
            }
        }

        private static double LateralAreaM2(Solid solid)
        {
            if (IsNullOrTiny(solid)) return 0;
            double area = 0;
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    var normal = pf.FaceNormal;
                    // 垂直面 (Z 分量接近 0)
                    if (Math.Abs(normal.Z) < 0.1)
                    {
                        area += pf.Area;
                    }
                }
            }
            return ToM2(area);
        }

        private static double OneCapAreaM2(Solid solid)
        {
            if (IsNullOrTiny(solid)) return 0;
            double maxArea = 0;
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    var normal = pf.FaceNormal;
                    // 水平面 (Z 分量接近 ±1)
                    if (Math.Abs(Math.Abs(normal.Z) - 1.0) < 0.1)
                    {
                        if (pf.Area > maxArea)
                            maxArea = pf.Area;
                    }
                }
            }
            return ToM2(maxArea);
        }

        #endregion

        #region Revit 元素輔助方法

        private static ElementId CreateDS(Document doc, Solid solid, Element host, string name, Material mat)
        {
            // 建立 DirectShape 並加入 solid 幾何
            var ds = Autodesk.Revit.DB.DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = SharedParams.AppId;
            ds.Name = name;
            ds.SetShape(new List<GeometryObject> { solid });
            if (mat != null && mat.Id != ElementId.InvalidElementId)
            {
                var param = ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(mat.Id);
                }
            }
            // 可選：將 host Id 記錄到 DirectShape 的參數（如有自訂參數）
            return ds.Id;
        }

        private static string GetCategoryName(Element host)
        {
            if (host is Wall) return "牆";
            if (IsStructuralColumn(host)) return "結構柱";
            if (IsStructuralFraming(host)) return "結構梁";
            if (host is Floor) return "板";
            if (ElementCategorizer.IsStairs(host)) return "樓梯";
            return host.Category?.Name ?? "未知";
        }

        private static bool IsStructuralColumn(Element host)
        {
            return host != null && host.Category != null &&
                   host.Category.Id.GetIdValue() == (int)BuiltInCategory.OST_StructuralColumns;
        }

        private static bool IsStructuralFraming(Element host)
        {
            return host != null && host.Category != null &&
                   host.Category.Id.GetIdValue() == (int)BuiltInCategory.OST_StructuralFraming;
        }

        private static bool IsBeamShortSideAgainstColumn(Document doc, Element host, PlanarFace face)
        {
            // 檢查梁的短邊是否貼柱
            try
            {
                var probe = MakeProbePrism(face, Mm(PROBE_DEPTH_MM), Mm(PROBE_EPS_MM));
                if (probe == null) return false;

                var hits = QueryIntersectionsWithProbe(doc, host, probe, new[] { BuiltInCategory.OST_StructuralColumns });
                return hits?.Any(hit => ProbeIntersectsSignificantly(doc, probe, hit)) ?? false;
            }
            catch (Exception ex)
            {
                Debug.Log($"IsBeamShortSideAgainstColumn 失敗: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region 對外 API
        public class FormworkInfo 
        { 
            public Element Host { get; set; }
            public IList<Solid> HostSolids { get; set; } = new List<Solid>();
            public IList<Face> SideFaces { get; set; } = new List<Face>();
            public IList<Face> BottomFaces { get; set; } = new List<Face>();
        }
        
        public static FormworkInfo AnalyzeHost(Document doc, Element host, bool includeBottom)
        {
            var info = new FormworkInfo { Host = host };
            try
            {
                info.HostSolids = GetElementSolids(host);
                
                foreach (var solid in info.HostSolids)
                {
                    foreach (Face face in solid.Faces)
                    {
                        XYZ normal = GetFaceDominantNormal(face);
                        
                        // 垂直面（側面）
                        if (Math.Abs(normal.Z) < 0.15)
                        {
                            info.SideFaces.Add(face);
                        }
                        // 水平面（底面）
                        else if (includeBottom && Math.Abs(Math.Abs(normal.Z) - 1) < 0.15 && normal.Z < 0)
                        {
                            info.BottomFaces.Add(face);
                        }
                    }
                }

                Debug.Log("分析完成 - 側面:{0}, 底面:{1}", info.SideFaces.Count, info.BottomFaces.Count);
            }
            catch (Exception ex)
            {
                Debug.Log("分析主體失敗: {0}", ex.Message);
            }
            return info;
        }

        /// <summary>
        /// 主要入口：依元素類別產生模板
        /// </summary>
        public static IList<ElementId> BuildFormworkSolids(
            Document doc, Element host, FormworkInfo info, Material mat,
            View specificView, bool includeBottom, double thicknessMm, double bottomOffsetMm, bool generate)
        {
            Debug.StartTimer("BuildFormworkSolids");
            try
            {
                ValidateParameters(thicknessMm, bottomOffsetMm);
                
                var results = new List<ElementId>();
                if (host == null) return results;

                // 參數
                double thk = Mm(thicknessMm);
                double off = Mm(bottomOffsetMm);
                double eps = Mm(SHELL_EPS_MM);
                double grow = Mm(AABB_GROW_MM);

                var opt = (mat != null && mat.Id != ElementId.InvalidElementId)
                    ? new SolidOptions(mat.Id, ElementId.InvalidElementId)
                    : NoMatOpt;

                // Host 幾何
                var hostSolids = GetElementSolids(host);
                if (hostSolids.Count == 0) return results;

                // 鄰近幾何：供後續修剪
                var neighborInfos = GetNeighborSolidsWithElements(doc, host, thk + off + grow);
                var neighbors = neighborInfos.Select(n => n.Solid).ToList();
                var unionCutter = BooleanUnionMany(neighbors);

                // ── 牆：兩側 ─────────────────────────────────────────────
                if (host is Wall)
                {
                    var w = (Wall)host;
                    foreach (var f in GetWallSideFaces(w))
                    {
                        // 平面臉才做詳細曝露檢查；曲面臉保守假設需要模板
                        if (f is PlanarFace pfWall && !IsFaceExposed(doc, host, pfWall, thk)) continue;

                        double faceAreaM2 = ToM2(f.Area);

                        if (!(f is PlanarFace))
                        {
                            // 曲面：直接用 TessBuilder Mesh，跳過 Boolean 運算（Boolean 會失敗）
                            var tessGeoms = CreateCurvedFormworkGeometries(f, thk);
                            if (tessGeoms == null || tessGeoms.Count == 0) continue;
                            if (generate)
                            {
                                var dsCurved = Autodesk.Revit.DB.DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                                dsCurved.ApplicationId = SharedParams.AppId;
                                dsCurved.Name = "Wall-Side";
                                dsCurved.SetShape(tessGeoms.ToList());
                                if (mat != null && mat.Id != ElementId.InvalidElementId)
                                {
                                    var pm = dsCurved.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pm != null && !pm.IsReadOnly) pm.Set(mat.Id);
                                }
                                results.Add(dsCurved.Id);
                                WriteForDS(doc.GetElement(dsCurved.Id), host, faceAreaM2, 0);
                            }
                            else
                            {
                                WriteForDS(null, host, faceAreaM2, 0);
                            }
                            continue;
                        }

                        // 平面臉：原有 Boolean 管線
                        var s = ExtrudeOrOffsetFace(f, thk, true, opt);
                        s = ApplyDynamoLikeCut(s, unionCutter, neighbors, eps, true);
                        if (IsNullOrTiny(s)) continue;

                        if (generate)
                        {
                            var id = CreateDS(doc, s, host, "Wall-Side", mat);
                            results.Add(id);
                            WriteForDS(doc.GetElement(id), host, faceAreaM2, 0);
                        }
                        else
                        {
                            WriteForDS(null, host, faceAreaM2, 0);
                        }
                    }
                    return results;
                }

                // ── 柱：四側 ─────────────────────────────────────────────
                if (IsStructuralColumn(host))
                {
                    foreach (var f in GetVerticalFaces(hostSolids))
                    {
                        if (f is PlanarFace pfCol && !IsFaceExposed(doc, host, pfCol, thk)) continue;

                        var s = ExtrudeOrOffsetFace(f, thk, f is PlanarFace, opt);
                        s = ApplyDynamoLikeCut(s, unionCutter, neighbors, eps, true);
                        if (IsNullOrTiny(s)) continue;

                        double faceAreaM2 = ToM2(f.Area);
                        if (generate)
                        {
                            var id = CreateDS(doc, s, host, "Column-Side", mat);
                            results.Add(id);
                            WriteForDS(doc.GetElement(id), host, faceAreaM2, 0);
                        }
                        else
                        {
                            WriteForDS(null, host, faceAreaM2, 0);
                        }
                    }
                    return results;
                }

                // ── 梁：側 + 底 ──────────────────────────────────────────
                if (IsStructuralFraming(host))
                {
                    // 側板（平面臉短邊貼柱不生；曲面臉不做貼柱判斷）
                    foreach (var f in GetVerticalFaces(hostSolids))
                    {
                        if (f is PlanarFace pfBeam)
                        {
                            if (!IsFaceExposed(doc, host, pfBeam, thk)) continue;
                            if (IsBeamShortSideAgainstColumn(doc, host, pfBeam)) continue;
                        }

                        var s = ExtrudeOrOffsetFace(f, thk, f is PlanarFace, opt);
                        s = ApplyDynamoLikeCut(s, unionCutter, neighbors, eps, true);
                        if (IsNullOrTiny(s)) continue;

                        double faceAreaM2 = ToM2(f.Area);
                        if (generate)
                        {
                            var id = CreateDS(doc, s, host, "Beam-Side", mat);
                            results.Add(id);
                            WriteForDS(doc.GetElement(id), host, faceAreaM2, 0);
                        }
                        else
                        {
                            WriteForDS(null, host, faceAreaM2, 0);
                        }
                    }

                    // 底模
                    if (includeBottom)
                    {
                        foreach (var bf in GetHorizontalFaces(hostSolids, true))
                        {
                            if (bf is PlanarFace pfBmBot && !IsFaceExposed(doc, host, pfBmBot, thk)) continue;

                            var s = ExtrudeOrOffsetFace(bf, thk, bf is PlanarFace, opt, -XYZ.BasisZ);
                            s = ApplyDynamoLikeCut(s, unionCutter, neighbors, eps, true);
                            if (IsNullOrTiny(s)) continue;

                            double faceAreaM2 = ToM2(bf.Area);
                            if (generate)
                            {
                                var id = CreateDS(doc, s, host, "Beam-Bottom", mat);
                                results.Add(id);
                                WriteForDS(doc.GetElement(id), host, 0, faceAreaM2);
                            }
                            else
                            {
                                WriteForDS(null, host, 0, faceAreaM2);
                            }
                        }
                    }
                    return results;
                }

                // ── 板：底 + 懸臂側 ─────────────────────────────────────
                if (host is Floor)
                {
                    // 底
                    if (includeBottom)
                    {
                        foreach (var bf in GetHorizontalFaces(hostSolids, true))
                        {
                            if (bf is PlanarFace pfSlabBot && !IsFaceExposed(doc, host, pfSlabBot, thk)) continue;

                            Solid s;
                            if (bf is PlanarFace pf2)
                            {
                                // 平面底面：帶底部偏移量擠出
                                var loops = pf2.GetEdgesAsCurveLoops();
                                var grown = OffsetLoops(loops, off, pf2.FaceNormal);
                                s = CreateExtrusion(grown, -XYZ.BasisZ, thk, opt);
                            }
                            else
                            {
                                // 曲面底面：直接用偏移網格法
                                s = ExtrudeOrOffsetFace(bf, thk, false, opt, -XYZ.BasisZ);
                            }
                            s = ApplyDynamoLikeCut(s, unionCutter, neighbors, eps, true);
                            if (IsNullOrTiny(s)) continue;

                            double faceAreaM2 = ToM2(bf.Area);
                            if (generate)
                            {
                                var id = CreateDS(doc, s, host, "Slab-Bottom", mat);
                                results.Add(id);
                                WriteForDS(doc.GetElement(id), host, 0, faceAreaM2);
                            }
                            else
                            {
                                WriteForDS(null, host, 0, faceAreaM2);
                            }
                        }
                    }

                    // 懸臂側板：只有「外露的垂直面」才做，若邊有梁/牆則 IsFaceExposed 會擋掉
                    foreach (var vf in GetVerticalFaces(hostSolids))
                    {
                        if (vf is PlanarFace pfSlabSide && !IsFaceExposed(doc, host, pfSlabSide, thk)) continue;

                        var s = ExtrudeOrOffsetFace(vf, thk, vf is PlanarFace, opt);
                        s = ApplyDynamoLikeCut(s, unionCutter, neighbors, eps, true);
                        if (IsNullOrTiny(s)) continue;

                        double slabSideAreaM2 = ToM2(vf.Area);
                        if (generate)
                        {
                            var id = CreateDS(doc, s, host, "Slab-Side", mat);
                            results.Add(id);
                            WriteForDS(doc.GetElement(id), host, slabSideAreaM2, 0);
                        }
                        else
                        {
                            WriteForDS(null, host, slabSideAreaM2, 0);
                        }
                    }

                    return results;
                }

                // ── 樓梯：梯段 / 平台 / 支撐，依外露垂直面與底面產生模板 ──
                if (ElementCategorizer.IsStairs(host))
                {
                    var stairNeighbors = neighborInfos
                        .Where(n => !ElementCategorizer.IsStairs(n.Element))
                        .Select(n => n.Solid)
                        .ToList();
                    var stairUnionCutter = BooleanUnionMany(stairNeighbors);

                    foreach (var f in GetVerticalFaces(hostSolids))
                    {
                        if (f is PlanarFace pfStairSide && !IsFaceExposed(doc, host, pfStairSide, thk)) continue;

                        var s = ExtrudeOrOffsetFace(f, thk, f is PlanarFace, opt);
                        s = ApplyDynamoLikeCut(s, stairUnionCutter, stairNeighbors, eps, true);
                        if (IsNullOrTiny(s)) continue;

                        double faceAreaM2 = ToM2(f.Area);
                        if (generate)
                        {
                            var id = CreateDS(doc, s, host, "Stair-Side", mat);
                            results.Add(id);
                            WriteForDS(doc.GetElement(id), host, faceAreaM2, 0);
                        }
                        else
                        {
                            WriteForDS(null, host, faceAreaM2, 0);
                        }
                    }

                    if (includeBottom)
                    {
                        foreach (var bf in GetHorizontalFaces(hostSolids, true))
                        {
                            if (bf is PlanarFace pfStairBot && !IsFaceExposed(doc, host, pfStairBot, thk)) continue;

                            var s = ExtrudeOrOffsetFace(bf, thk, bf is PlanarFace, opt, -XYZ.BasisZ);
                            s = ApplyDynamoLikeCut(s, stairUnionCutter, stairNeighbors, eps, true);
                            if (IsNullOrTiny(s)) continue;

                            double faceAreaM2 = ToM2(bf.Area);
                            if (generate)
                            {
                                var id = CreateDS(doc, s, host, "Stair-Bottom", mat);
                                results.Add(id);
                                WriteForDS(doc.GetElement(id), host, 0, faceAreaM2);
                            }
                            else
                            {
                                WriteForDS(null, host, 0, faceAreaM2);
                            }
                        }
                    }

                    return results;
                }

                return results;
            }
            finally
            {
                Debug.StopTimer("BuildFormworkSolids");
            }
        }

        /// <summary>
        /// 面生面（點面擠出）：快速響應版本，簡化檢查流程以提升即時性
        /// </summary>
        public static ElementId BuildFromFace(Document doc, Element host, PlanarFace face, double thicknessMm, Material mat)
        {
            if (doc == null || host == null || face == null) return ElementId.InvalidElementId;

            Debug.Log("開始從面建立模板 - Host:{0}, 厚度:{1}mm", host.Id.GetIdValue(), thicknessMm);

            double thk = Mm(thicknessMm);
            var opt = (mat != null && mat.Id != ElementId.InvalidElementId)
                ? new SolidOptions(mat.Id, ElementId.InvalidElementId)
                : NoMatOpt;

            // 快速檢查面是否需要模板（簡化版以提升響應速度）
            if (!IsFaceExposedFast(doc, host, face, thk))
            {
                Debug.Log("面不需要模板，跳過");
                return ElementId.InvalidElementId;
            }

            // 建立基本模板實體
            var formworkSolid = ExtrudeFromFace(face, thk, true, opt);
            if (IsNullOrTiny(formworkSolid))
            {
                Debug.Log("無法建立模板實體");
                return ElementId.InvalidElementId;
            }

            // 快速模板處理：簡化版相接處理以提升響應速度
            formworkSolid = ApplyFormworkLogicFast(doc, host, formworkSolid, face, thk);
            if (IsNullOrTiny(formworkSolid))
            {
                Debug.Log("模板被完全裁切，跳過");
                return ElementId.InvalidElementId;
            }

            // 建立 DirectShape
            var id = CreateDS(doc, formworkSolid, host, "PickFace", mat);
            if (id == ElementId.InvalidElementId)
            {
                Debug.Log("建立 DirectShape 失敗");
                return ElementId.InvalidElementId;
            }

            // 計算面積並記錄
            double sideM2 = 0, bottomM2 = 0;
            var normal = face.FaceNormal;
            if (Math.Abs(Math.Abs(normal.Z) - 1.0) < 0.1) // 水平面
                bottomM2 = OneCapAreaM2(formworkSolid);
            else // 垂直面
                sideM2 = LateralAreaM2(formworkSolid);

            WriteForDS(doc.GetElement(id), host, sideM2, bottomM2);
            Debug.Log("成功建立模板 - 側面:{0:F3}m², 底面:{1:F3}m²", sideM2, bottomM2);
            
            return id;
        }

        /// <summary>
        /// 精確面生面方法：類似油漆功能，但能正確判斷模型交集完後的面
        /// </summary>
        public static ElementId BuildFromFaceAccurate(Document doc, Element host, PlanarFace face, double thicknessMm, Material mat)
        {
            if (doc == null || host == null || face == null) return ElementId.InvalidElementId;

            Debug.Log("開始精確面生面 - Host:{0}, 厚度:{1}mm", host.Id.GetIdValue(), thicknessMm);

            double thk = Mm(thicknessMm);
            var opt = (mat != null && mat.Id != ElementId.InvalidElementId)
                ? new SolidOptions(mat.Id, ElementId.InvalidElementId)
                : NoMatOpt;

            // 1. 精確檢查面是否真正暴露且需要模板
            if (!IsFaceExposedAccurate(doc, host, face, thk))
            {
                Debug.Log("面不暴露或不需要模板，跳過");
                return ElementId.InvalidElementId;
            }

            // 2. 建立基本模板實體
            var formworkSolid = ExtrudeFromFace(face, thk, true, opt);
            if (IsNullOrTiny(formworkSolid))
            {
                Debug.Log("無法建立模板實體");
                return ElementId.InvalidElementId;
            }

            // 3. 精確的模板處理：考慮所有相交結構的影響
            formworkSolid = ApplyFormworkLogicAccurate(doc, host, formworkSolid, face, thk);
            if (IsNullOrTiny(formworkSolid))
            {
                Debug.Log("模板被完全裁切，跳過");
                return ElementId.InvalidElementId;
            }

            // 4. 建立 DirectShape
            var id = CreateDS(doc, formworkSolid, host, "AccurateFace", mat);
            if (id == ElementId.InvalidElementId)
            {
                Debug.Log("建立 DirectShape 失敗");
                return ElementId.InvalidElementId;
            }

            // 5. 計算面積並記錄
            double sideM2 = 0, bottomM2 = 0;
            var normal = face.FaceNormal;
            if (Math.Abs(Math.Abs(normal.Z) - 1.0) < 0.1) // 水平面
                bottomM2 = OneCapAreaM2(formworkSolid);
            else // 垂直面
                sideM2 = LateralAreaM2(formworkSolid);

            WriteForDS(doc.GetElement(id), host, sideM2, bottomM2);
            Debug.Log("成功建立精確模板 - 側面:{0:F3}m², 底面:{1:F3}m²", sideM2, bottomM2);
            
            return id;
        }

        // 精確版面外露檢查 - 類似油漆功能，正確判斷交集後的面
        private static bool IsFaceExposedAccurate(Document doc, Element host, PlanarFace pf, double thickness)
        {
            Debug.Log("精確檢查面是否暴露 - Host:{0}", host.Id.GetIdValue());

            var normal = pf.FaceNormal;
            
            // 1. 基本規則檢查
            if (host is Floor && normal.Z > 0.85)
            {
                Debug.Log("  => 樓板頂面（澆置面），不需要模板");
                return false;
            }

            // 2. 創建探測實體以檢查面的實際暴露情況
            try
            {
                // 創建薄薄的探測層（1mm厚）
                var probeSolid = ExtrudeFromFace(pf, Mm(1), true, NoMatOpt);
                if (IsNullOrTiny(probeSolid))
                {
                    Debug.Log("  => 無法創建探測實體，預設需要模板");
                    return true;
                }

                // 3. 取得所有可能相交的結構
                var searchRange = thickness + Mm(200); // 較大的搜索範圍確保完整性
                var neighbors = GetNeighborSolidsWithElements(doc, host, searchRange);
                
                if (neighbors.Count == 0)
                {
                    Debug.Log("  => 沒有鄰近結構，面完全暴露");
                    return true;
                }

                // 4. 檢查探測實體是否被其他結構大面積覆蓋
                double totalCoveredVolume = 0;
                double originalVolume = probeSolid.Volume;

                foreach (var neighborInfo in neighbors)
                {
                    if (IsNullOrTiny(neighborInfo.Solid)) continue;

                    try
                    {
                        var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                            probeSolid, neighborInfo.Solid, BooleanOperationsType.Intersect);
                            
                        if (!IsNullOrTiny(intersection))
                        {
                            totalCoveredVolume += intersection.Volume;
                            Debug.Log("  => 被 {0} 部分覆蓋，體積: {1:F6}", 
                                neighborInfo.Element.Id.GetIdValue(), intersection.Volume);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("  => 交集計算失敗: {0}", ex.Message);
                    }
                }

                // 5. 判斷暴露程度
                double coveredRatio = totalCoveredVolume / originalVolume;
                Debug.Log("  => 總覆蓋比例: {0:P1}", coveredRatio);

                // 如果被覆蓋超過80%，則認為不需要模板
                if (coveredRatio > 0.8)
                {
                    Debug.Log("  => 面被大面積覆蓋，不需要模板");
                    return false;
                }

                Debug.Log("  => 面有足夠暴露，需要模板");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log("  => 暴露檢查失敗: {0}，預設需要模板", ex.Message);
                return true;
            }
        }

        // 精確版模板邏輯處理 - 完整考慮所有相交影響
        private static Solid ApplyFormworkLogicAccurate(Document doc, Element host, Solid formworkSolid, PlanarFace face, double thickness)
        {
            try
            {
                Debug.Log("開始精確模板處理");
                
                // 取得較大的搜索範圍以確保完整性
                var searchRange = thickness + Mm(300);
                var neighbors = GetNeighborSolidsWithElements(doc, host, searchRange);
                
                if (neighbors.Count == 0) 
                {
                    Debug.Log("沒有鄰近結構，直接返回原模板");
                    return formworkSolid;
                }

                Debug.Log("找到 {0} 個鄰近結構，開始精確裁切分析", neighbors.Count);

                var result = formworkSolid;
                var processedCount = 0;

                // 按距離排序，優先處理最近的結構
                var sortedNeighbors = neighbors
                    .Where(n => !IsNullOrTiny(n.Solid))
                    .OrderBy(n => GetDistanceBetweenSolids(result, n.Solid))
                    .ToList();

                foreach (var neighborInfo in sortedNeighbors)
                {
                    try
                    {
                        // 包圍盒快速檢查
                        if (!IntersectBBox(result, neighborInfo.Solid)) continue;

                        // 計算交集
                        var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                            result, neighborInfo.Solid, BooleanOperationsType.Intersect);
                        
                        if (IsNullOrTiny(intersection)) continue;

                        double intersectionRatio = intersection.Volume / result.Volume;
                        Debug.Log("與 {0} 的交集比例: {1:P1}", 
                            neighborInfo.Element.Id.GetIdValue(), intersectionRatio);

                        // 接合類型偵測 — 下列情況使用極低閾值 0.1%：
                        //   牆對牆: 交集比率 = 牆A厚/牆B長
                        //   樓板對牆: 交集比率 = 牆截面/樓板面積
                        //   牆對樓板/梁: 交集比率 = 板厚/牆高（高牆時 < 1%）
                        bool isHostWallFb    = host?.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_Walls;
                        bool isHostFloorFb   = host?.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_Floors;
                        bool isHostBeamFb    = host?.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_StructuralFraming;
                        bool isNeighborWallFb  = neighborInfo.Element?.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_Walls;
                        bool isNeighborSlabFb  = neighborInfo.Element?.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_Floors ||
                                                 neighborInfo.Element?.Category?.Id?.GetIdValue() == (long)BuiltInCategory.OST_StructuralFraming;
                        bool needsLowCutThresholdFb = (isHostWallFb  && isNeighborWallFb)  ||  // 牆對牆
                                                      (isHostFloorFb && isNeighborWallFb)  ||  // 樓板對牆
                                                      (isHostWallFb  && isNeighborSlabFb)  ||  // 牆對樓板/梁
                                                      (isHostBeamFb  && isNeighborWallFb)  ||  // 梁對牆: 交集比率 = 牆厚/梁長
                                                      (isHostBeamFb  && isNeighborSlabFb);     // 梁穿樓板
                        double effectiveCutThreshold = needsLowCutThresholdFb ? 0.001 : 0.01;

                        if (intersectionRatio > effectiveCutThreshold)
                        {
                            var cut = BooleanOperationsUtils.ExecuteBooleanOperation(
                                result, neighborInfo.Solid, BooleanOperationsType.Difference);
                            
                            if (!IsNullOrTiny(cut))
                            {
                                result = cut;
                                processedCount++;
                                Debug.Log("成功裁切 {0}，交集比例: {1:P1}（閾值: {2:P1}）", 
                                    neighborInfo.Element.Id.GetIdValue(), intersectionRatio, effectiveCutThreshold);
                            }
                        }

                        // 如果模板被裁切得太小，停止處理
                        if (IsNullOrTiny(result)) break;
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("處理鄰近結構 {0} 失敗: {1}", 
                            neighborInfo.Element.Id.GetIdValue(), ex.Message);
                    }
                }

                Debug.Log("精確處理完成，共處理 {0} 個相交結構", processedCount);
                return result;
            }
            catch (Exception ex)
            {
                Debug.Log("精確模板處理失敗: {0}", ex.Message);
                return formworkSolid; // 返回原始模板
            }
        }

        // 計算兩個實體之間的距離（簡化版）
        private static double GetDistanceBetweenSolids(Solid solid1, Solid solid2)
        {
            try
            {
                var bbox1 = solid1.GetBoundingBox();
                var bbox2 = solid2.GetBoundingBox();
                
                var center1 = (bbox1.Min + bbox1.Max) * 0.5;
                var center2 = (bbox2.Min + bbox2.Max) * 0.5;
                
                return center1.DistanceTo(center2);
            }
            catch
            {
                return double.MaxValue;
            }
        }

        // 改良版面外露檢查 - 正確判斷交集後的面是否需要模板
        private static bool IsFaceExposedFast(Document doc, Element host, PlanarFace pf, double extraDepthInternal)
        {
#if REVIT2024 || REVIT2025 || REVIT2026
            Debug.Log("檢查面是否需要模板 - Host:{0}", host.Id.GetIdValue());
#else
            Debug.Log("檢查面是否需要模板 - Host:{0}", host.Id.IntegerValue);
#endif

            var normal = pf.FaceNormal;
            
            // 1. 樓板頂面通常不需要模板（澆置面）
            if (host is Floor && normal.Z > 0.85)
            {
                Debug.Log("  => 樓板頂面，不需要模板");
                return false;
            }

            // 2. 改良的暴露檢查：從面上取樣點向法向量方向探測
            // 取得面上的採樣點
            XYZ faceCenter = null;
            try
            {
                // 嘗試評估面的中心點
                var bounds = pf.GetBoundingBox();
                var uMid = (bounds.Min.U + bounds.Max.U) * 0.5;
                var vMid = (bounds.Min.V + bounds.Max.V) * 0.5;
                faceCenter = pf.Evaluate(new UV(uMid, vMid));
            }
            catch
            {
                // 如果評估失敗，使用面的任意點
                try
                {
                    faceCenter = pf.Evaluate(new UV(0, 0));
                }
                catch
                {
                    Debug.Log("  => 無法取得面中心點，預設需要模板");
                    return true;
                }
            }
            
            // 向外探測距離，用於檢查是否有其他結構阻擋
            double probeDistance = extraDepthInternal + Mm(50); // 50mm 額外距離
            var probePoint = faceCenter + normal * probeDistance;

            // 3. 快速幾何檢查：檢查探測點周圍是否有結構阻擋
            var neighbors = GetNeighborSolidsWithElementsFast(doc, host, probeDistance * 2);
            
            foreach (var neighborInfo in neighbors)
            {
                if (IsNullOrTiny(neighborInfo.Solid)) continue;
                
                try
                {
                    // 使用包圍盒簡化檢查：如果探測點在鄰近結構的包圍盒內，可能被遮擋
                    var bbox = neighborInfo.Solid.GetBoundingBox();
                    if (bbox.Min.X <= probePoint.X && probePoint.X <= bbox.Max.X &&
                        bbox.Min.Y <= probePoint.Y && probePoint.Y <= bbox.Max.Y &&
                        bbox.Min.Z <= probePoint.Z && probePoint.Z <= bbox.Max.Z)
                    {
#if REVIT2024 || REVIT2025 || REVIT2026
                        Debug.Log("  => 面可能被 {0} 遮擋", neighborInfo.Element.Id.GetIdValue());
#else
                        Debug.Log("  => 面可能被 {0} 遮擋", neighborInfo.Element.Id.IntegerValue);
#endif
                        // 但不直接返回 false，繼續進行更精確的檢查
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log("檢查包圍盒失敗: {0}", ex.Message);
                }
            }

            // 4. 進一步檢查：建立小型探測實體檢查交集
            try
            {
                // 創建小型探測實體（向外擠出 1mm 用於檢測）
                var probeSolid = ExtrudeFromFace(pf, Mm(1), true, NoMatOpt);
                if (IsNullOrTiny(probeSolid)) 
                {
                    Debug.Log("  => 預設需要模板（無法創建探測實體）");
                    return true;
                }

                // 檢查探測實體是否與其他結構有大面積交集
                foreach (var neighborInfo in neighbors.Take(3)) // 限制檢查數量以平衡精度和性能
                {
                    if (IsNullOrTiny(neighborInfo.Solid)) continue;
                    
                    try
                    {
                        var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                            probeSolid, neighborInfo.Solid, BooleanOperationsType.Intersect);
                            
                        if (!IsNullOrTiny(intersection))
                        {
                            double intersectionRatio = intersection.Volume / probeSolid.Volume;
                            if (intersectionRatio > 0.7) // 70% 以上被遮擋則不需要模板
                            {
                                Debug.Log("  => 面被大面積遮擋（{0:P1}），不需要模板", intersectionRatio);
                                return false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("檢查交集失敗: {0}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log("探測實體檢查失敗: {0}", ex.Message);
            }

            Debug.Log("  => 面暴露，需要模板");
            return true;
        }

        // 快速版模板邏輯處理 - 簡化相接處理以提升面選工具的響應速度
        private static Solid ApplyFormworkLogicFast(Document doc, Element host, Solid formworkSolid, PlanarFace face, double thickness)
        {
            try
            {
                // 快速版：縮小搜索範圍並簡化處理邏輯
                var searchRange = thickness + Mm(100); // 縮小到 100mm 範圍以提升速度
                var neighbors = GetNeighborSolidsWithElementsFast(doc, host, searchRange);
                
                if (neighbors.Count == 0) return formworkSolid;

                Debug.Log("找到 {0} 個鄰近結構，開始快速裁切", neighbors.Count);

                var result = formworkSolid;

                // 快速版：只處理明顯的大相交，跳過細微的相接分析
                foreach (var neighborInfo in neighbors.Take(5)) // 限制處理數量以提升速度
                {
                    if (IsNullOrTiny(neighborInfo.Solid)) continue;

                    try
                    {
                        // 快速包圍盒檢查
                        if (!IntersectBBox(result, neighborInfo.Solid)) continue;

                        // 快速相交檢查 - 只處理明顯的大相交
                        var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                            result, neighborInfo.Solid, BooleanOperationsType.Intersect);
                        
                        if (IsNullOrTiny(intersection)) continue;

                        double intersectionRatio = intersection.Volume / result.Volume;
                        
                        // 快速版：只處理明顯的大相交（>15%）
                        if (intersectionRatio > 0.15)
                        {
                            var cut = BooleanOperationsUtils.ExecuteBooleanOperation(
                                result, neighborInfo.Solid, BooleanOperationsType.Difference);
                            if (!IsNullOrTiny(cut))
                            {
                                result = cut;
                                Debug.Log("快速裁切，相交比例: {0:P1}", intersectionRatio);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("快速裁切失敗: {0}", ex.Message);
                        // 繼續處理下一個鄰近結構
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.Log("快速模板邏輯失敗: {0}", ex.Message);
                return formworkSolid; // 返回原始模板
            }
        }

        // 快速版鄰近結構獲取 - 限制搜索範圍和數量以提升速度
        private static IList<NeighborInfo> GetNeighborSolidsWithElementsFast(Document doc, Element host, double searchRange)
        {
            var neighbors = new List<NeighborInfo>();
            try
            {
                // 取得 host 的包圍盒
                var hostSolids = GetElementSolids(host);
                if (hostSolids.Count == 0) return neighbors;

                var hostBB = hostSolids.FirstOrDefault()?.GetBoundingBox();
                if (hostBB == null) return neighbors;

                // 快速版：使用第一個實體的包圍盒，不合併多個實體
                var min = new XYZ(hostBB.Min.X - searchRange, hostBB.Min.Y - searchRange, hostBB.Min.Z - searchRange);
                var max = new XYZ(hostBB.Max.X + searchRange, hostBB.Max.Y + searchRange, hostBB.Max.Z + searchRange);
                var outline = new Outline(min, max);

                var filter = new ElementMulticategoryFilter(_catsStruct);
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .Where(e => e.Id != host.Id)
                    .Take(10); // 限制處理數量

                foreach (var e in collector)
                {
                    var solids = GetElementSolids(e);
                    foreach (var s in solids.Take(2)) // 每個元素最多處理2個實體
                    {
                        if (!IsNullOrTiny(s))
                        {
                            neighbors.Add(new NeighborInfo { Element = e, Solid = s });
                            if (neighbors.Count >= 10) return neighbors; // 限制總數量
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"快速取得鄰近實體失敗: {ex.Message}");
            }
            return neighbors;
        }

        // 應用實務模板邏輯的智慧裁切 - 改善版
        private static Solid ApplyFormworkLogic(Document doc, Element host, Solid formworkSolid, PlanarFace face, double thickness)
        {
            try
            {
                // 擴大搜索範圍以確保捕捉所有相接結構
                var searchRange = thickness + Mm(200); // 擴大到 200mm 範圍
                var neighbors = GetNeighborSolidsWithElements(doc, host, searchRange);
                
                if (neighbors.Count == 0) return formworkSolid;

                Debug.Log("找到 {0} 個鄰近結構，開始智慧裁切", neighbors.Count);

                var result = formworkSolid;
                var normal = face.FaceNormal;
                var hostType = GetStructuralElementType(host);

                // 根據結構類型和面的方向採用不同的裁切策略
                var originalVolume = formworkSolid.Volume; // 保存原始模板體積用於計算比例

                foreach (var neighborInfo in neighbors)
                {
                    if (IsNullOrTiny(neighborInfo.Solid)) continue;

                    try
                    {
                        // 檢查是否真的相交
                        if (!IntersectBBox(result, neighborInfo.Solid)) continue;

                        // 計算相交體積
                        var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                            result, neighborInfo.Solid, BooleanOperationsType.Intersect);

                        if (IsNullOrTiny(intersection)) continue;

                        // 使用原始模板體積計算相交比例，避免隨著裁切而變化
                        double intersectionRatio = intersection.Volume / originalVolume;
                        var neighborType = GetStructuralElementType(neighborInfo.Element);

                        Debug.Log("  檢查 {0}-{1} 相接：相交體積={2:F6}, 原始體積={3:F6}, 比例={4:P2}",
                            hostType, neighborType, intersection.Volume, originalVolume, intersectionRatio);

                        bool shouldCut = ShouldCutFormwork(hostType, neighborType, intersectionRatio, normal, face, neighborInfo);

                        if (shouldCut)
                        {
                            var cut = BooleanOperationsUtils.ExecuteBooleanOperation(
                                result, neighborInfo.Solid, BooleanOperationsType.Difference);
                            if (!IsNullOrTiny(cut))
                            {
                                result = cut;
                                Debug.Log("  ✅ 執行裁切 {0}-{1}，相交比例: {2:P2}",
                                    hostType, neighborType, intersectionRatio);
                            }
                            else
                            {
                                Debug.Log("  ⚠️ 裁切後模板過小，跳過");
                            }
                        }
                        else
                        {
                            Debug.Log("  ❌ 保留連接 {0}-{1}，相交比例: {2:P2} (未達閾值)",
                                hostType, neighborType, intersectionRatio);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("  ❌ 裁切失敗: {0}", ex.Message);
                        // 繼續處理下一個鄰近結構
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.Log("應用模板邏輯失敗: {0}", ex.Message);
                return formworkSolid; // 返回原始模板
            }
        }

        // 改進的相接判斷邏輯
        private static bool ShouldCutFormwork(StructuralElementType hostType, StructuralElementType neighborType,
            double intersectionRatio, XYZ normal, PlanarFace face, NeighborInfo neighborInfo)
        {
            // 根據實際施工經驗決定是否需要裁切模板

            // ⚠️ 重要：特定規則必須在一般規則之前檢查，避免被覆蓋

            // 1. 牆與牆相接：兩面牆相接處通常不需要模板（最優先處理）
            if (hostType == StructuralElementType.Wall && neighborType == StructuralElementType.Wall)
            {
                // 牆與牆相接時，只要有任何相交就應該裁切
                // 使用非常低的閾值（0.1%），幾乎任何接觸都會裁切
                Debug.Log("  🔍 牆與牆相接檢測：相交比例 {0:P2}", intersectionRatio);
                return intersectionRatio > 0.001; // 0.1% 以上相交就裁切
            }

            // 2. 柱與梁相接：梁的模板通常被柱部分遮擋，需要裁切
            if (hostType == StructuralElementType.Beam && neighborType == StructuralElementType.Column)
            {
                return intersectionRatio > 0.05; // 5% 以上相交就裁切
            }

            // 3. 梁與柱相接：柱的模板在梁的位置需要裁切
            if (hostType == StructuralElementType.Column && neighborType == StructuralElementType.Beam)
            {
                return intersectionRatio > 0.03; // 3% 以上相交就裁切
            }

            // 4. 板與梁相接：板底模板被梁遮擋的部分需要裁切
            if (hostType == StructuralElementType.Slab && neighborType == StructuralElementType.Beam)
            {
                // 檢查是否為板底面
                if (normal.Z < -0.7) // 向下的面
                {
                    return intersectionRatio > 0.02; // 2% 以上相交就裁切
                }
                return intersectionRatio > 0.1; // 其他面 10% 以上才裁切
            }

            // 5. 梁與板相接：梁的頂面通常被板覆蓋
            if (hostType == StructuralElementType.Beam && neighborType == StructuralElementType.Slab)
            {
                // 檢查是否為梁頂面
                if (normal.Z > 0.7) // 向上的面
                {
                    return intersectionRatio > 0.02; // 2% 以上相交就裁切
                }
                return intersectionRatio > 0.08; // 側面 8% 以上才裁切
            }

            // 6. 板與柱相接：板在柱位置的開口
            if (hostType == StructuralElementType.Slab && neighborType == StructuralElementType.Column)
            {
                return intersectionRatio > 0.01; // 1% 以上相交就裁切（柱穿板）
            }

            // 7. 柱與板相接：柱被板包圍的部分
            if (hostType == StructuralElementType.Column && neighborType == StructuralElementType.Slab)
            {
                return intersectionRatio > 0.05; // 5% 以上相交就裁切
            }

            // 8. 牆與其他結構相接
            if (hostType == StructuralElementType.Wall)
            {
                return intersectionRatio > 0.05; // 5% 以上相交就裁切
            }

            // 9. 相同類型結構相接（通常不需要模板）- 一般規則，放在最後
            if (hostType == neighborType)
            {
                return intersectionRatio > 0.02; // 2% 以上相交就裁切
            }

            // 默認情況：較保守的裁切
            return intersectionRatio > 0.15; // 15% 以上相交才裁切
        }

        private static StructuralElementType GetStructuralElementType(Element element)
        {
#if REVIT2024 || REVIT2025 || REVIT2026
            var categoryId = element.Category?.Id?.GetIdValue();
#else
            var categoryId = element.Category?.Id?.IntegerValue;
#endif
            if (categoryId.HasValue && ElementCategorizer.IsStairCategory(categoryId.Value))
                return StructuralElementType.Stair;

            switch (categoryId)
            {
                case (int)BuiltInCategory.OST_StructuralFraming:
                    return StructuralElementType.Beam;
                case (int)BuiltInCategory.OST_StructuralColumns:
                    return StructuralElementType.Column;
                case (int)BuiltInCategory.OST_Floors:
                    return StructuralElementType.Slab;
                case (int)BuiltInCategory.OST_Walls:
                    return StructuralElementType.Wall;
                default:
                    return StructuralElementType.Other;
            }
        }

        // 結構類型枚舉
        private enum StructuralElementType
        {
            Beam,
            Column, 
            Slab,
            Wall,
            Stair,
            Other
        }

        // 鄰近結構資訊
        private class NeighborInfo
        {
            public Element Element { get; set; }
            public Solid Solid { get; set; }
        }

        // 改進的鄰近結構獲取方法
        private static IList<NeighborInfo> GetNeighborSolidsWithElements(Document doc, Element host, double searchRange)
        {
            var neighbors = new List<NeighborInfo>();
            try
            {
                // 取得 host 的包圍盒，並擴大 searchRange
                var hostSolids = GetElementSolids(host);
                if (hostSolids.Count == 0) return neighbors;

                BoundingBoxXYZ hostBB = null;
                foreach (var s in hostSolids)
                {
                    var bb = s.GetBoundingBox();
                    if (hostBB == null)
                    {
                        hostBB = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max };
                    }
                    else
                    {
                        hostBB.Min = new XYZ(
                            Math.Min(hostBB.Min.X, bb.Min.X),
                            Math.Min(hostBB.Min.Y, bb.Min.Y),
                            Math.Min(hostBB.Min.Z, bb.Min.Z));
                        hostBB.Max = new XYZ(
                            Math.Max(hostBB.Max.X, bb.Max.X),
                            Math.Max(hostBB.Max.Y, bb.Max.Y),
                            Math.Max(hostBB.Max.Z, bb.Max.Z));
                    }
                }

                // 擴大包圍盒
                var min = new XYZ(hostBB.Min.X - searchRange, hostBB.Min.Y - searchRange, hostBB.Min.Z - searchRange);
                var max = new XYZ(hostBB.Max.X + searchRange, hostBB.Max.Y + searchRange, hostBB.Max.Z + searchRange);
                var outline = new Outline(min, max);

                var filter = new ElementMulticategoryFilter(_catsStruct);
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .Where(e => e.Id != host.Id);

                foreach (var e in collector)
                {
                    var solids = GetElementSolids(e);
                    foreach (var s in solids)
                    {
                        if (!IsNullOrTiny(s))
                        {
                            neighbors.Add(new NeighborInfo { Element = e, Solid = s });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"取得鄰近實體失敗: {ex.Message}");
            }
            return neighbors;
        }

        private static IList<PlanarFace> GetWallSidePlanarFaces(Wall wall)
        {
            var result = new List<PlanarFace>();
            var solids = GetElementSolids(wall);
            foreach (var s in solids)
            {
                foreach (var f in s.Faces.Cast<Face>())
                {
                    if (f is PlanarFace pf)
                    {
                        var normal = pf.FaceNormal;
                        if (Math.Abs(normal.Z) < 0.1) // 近乎垂直的面
                        {
                            result.Add(pf);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 取得牆的側面（含曲面壁、弧形牆的圓柱面等非平面）
        /// </summary>
        private static IList<Face> GetWallSideFaces(Wall wall)
        {
            var result = new List<Face>();
            var solids = GetElementSolids(wall);
            foreach (var s in solids)
            {
                foreach (Face f in s.Faces)
                {
                    XYZ normal = GetFaceDominantNormal(f);
                    if (Math.Abs(normal.Z) < 0.15) // 近乎垂直的面（容差稍大以包含曲面）
                        result.Add(f);
                }
            }
            return result;
        }

        private static IList<PlanarFace> GetVerticalPlanarFaces(IList<Solid> solids)
        {
            var result = new List<PlanarFace>();
            foreach (var s in solids)
            {
                foreach (var f in s.Faces.Cast<Face>())
                {
                    if (f is PlanarFace pf)
                    {
                        var normal = pf.FaceNormal;
                        if (Math.Abs(normal.Z) < 0.1) // 近乎垂直的面
                        {
                            result.Add(pf);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 取得元素的垂直面（含曲面）
        /// </summary>
        private static IList<Face> GetVerticalFaces(IList<Solid> solids)
        {
            var result = new List<Face>();
            foreach (var s in solids)
            {
                foreach (Face f in s.Faces)
                {
                    XYZ normal = GetFaceDominantNormal(f);
                    if (Math.Abs(normal.Z) < 0.15)
                        result.Add(f);
                }
            }
            return result;
        }

        private static IList<PlanarFace> GetHorizontalPlanarFaces(IList<Solid> solids, bool bottomOnly)
        {
            var result = new List<PlanarFace>();
            foreach (var s in solids)
            {
                foreach (var f in s.Faces.Cast<Face>())
                {
                    if (f is PlanarFace pf)
                    {
                        var normal = pf.FaceNormal;
                        if (Math.Abs(Math.Abs(normal.Z) - 1) < 0.1) // 近乎水平的面
                        {
                            if (!bottomOnly || normal.Z < 0) // 若只要底面則檢查法向量
                            {
                                result.Add(pf);
                            }
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 取得元素的水平面（含曲面）
        /// </summary>
        private static IList<Face> GetHorizontalFaces(IList<Solid> solids, bool bottomOnly)
        {
            var result = new List<Face>();
            foreach (var s in solids)
            {
                foreach (Face f in s.Faces)
                {
                    XYZ normal = GetFaceDominantNormal(f);
                    if (Math.Abs(Math.Abs(normal.Z) - 1) < 0.15)
                    {
                        if (!bottomOnly || normal.Z < 0)
                            result.Add(f);
                    }
                }
            }
            return result;
        }

        #endregion


    }
}
