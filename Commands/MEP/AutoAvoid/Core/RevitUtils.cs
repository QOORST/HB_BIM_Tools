using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    /// <summary>
    /// Revit MEP 元素操作工具（整合優化版）
    /// 功能：管線/風管/電管的自動避讓替換、連接器管理、彎頭創建
    /// </summary>
    public static class RevitUtils
    {
        /// <summary>
        /// 用避讓路徑替換原始元素
        /// </summary>
        public static bool ReplaceWithDetour(Document doc, Element target, DetourPlan plan, AvoidOptions opt)
        {
            if (target == null || plan == null || !plan.IsValid)
            {
                Logger.Warning("無效的替換參數");
                return false;
            }

            try
            {
                if (target is Pipe p) return ReplacePipe(doc, p, plan);
                if (target is Duct d) return ReplaceDuct(doc, d, plan);
                if (target is Conduit c) return ReplaceConduit(doc, c, plan);
                
                Logger.Warning($"不支援的元素類型: {target.GetType().Name}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"替換元素 {target.Id} 時發生錯誤", ex);
                return false;
            }
        }

        private static bool ReplacePipe(Document doc, Pipe pipe, DetourPlan plan)
        {
            var lc = pipe.Location as LocationCurve;
            if (lc == null) 
            {
                Logger.Warning($"管線 {pipe.Id} 沒有 LocationCurve");
                return false;
            }

            try
            {
                ElementId oldPipeId = pipe.Id;
                Line pipeLine = lc.Curve as Line;
                if (pipeLine == null)
                {
                    Logger.Warning($"管線 {pipe.Id} 不是直線");
                    return false;
                }

                XYZ pStart = pipeLine.GetEndPoint(0);
                XYZ pEnd = pipeLine.GetEndPoint(1);

                // 關鍵步驟1：獲取原管線兩端的連接器（參考風管避讓邏輯）
                Connector conStart = ConnectorAtPoint(pipe, pStart);
                Connector conEnd = ConnectorAtPoint(pipe, pEnd);
                
                // 獲取與管線兩端相連的管件連接器（可能為 null）
                Connector fittingConStart = GetConnectorToFitting(conStart);
                Connector fittingConEnd = GetConnectorToFitting(conEnd);
                
                Logger.Debug($"管線 {oldPipeId} 端點連接: 起點={fittingConStart != null}, 終點={fittingConEnd != null}");

                // 關鍵步驟2：先斷開原管線兩端的連接（參考風管避讓邏輯）
                if (fittingConStart != null)
                {
                    conStart.DisconnectFrom(fittingConStart);
                    Logger.Debug($"已斷開起點連接");
                }
                if (fittingConEnd != null)
                {
                    conEnd.DisconnectFrom(fittingConEnd);
                    Logger.Debug($"已斷開終點連接");
                }

                // 關鍵步驟3：建立多段新管線（複製原管線屬性）
                var segments = new List<Pipe>();
                for (int i = 0; i < plan.Path.Count - 1; i++)
                {
                    // 使用 CopyElement 保留所有屬性
                    XYZ offset = new XYZ(1000, 0, 0);
                    var copiedIds = ElementTransformUtils.CopyElement(doc, pipe.Id, offset);
                    if (copiedIds.Count == 0)
                    {
                        Logger.Error($"複製管線段 {i} 失敗");
                        return false;
                    }
                    
                    Pipe seg = doc.GetElement(copiedIds.First()) as Pipe;
                    if (seg == null)
                    {
                        Logger.Error($"取得複製的管線段 {i} 失敗");
                        return false;
                    }
                    
                    // 修改位置為新路徑
                    LocationCurve segLc = seg.Location as LocationCurve;
                    Line newLine = Line.CreateBound(plan.Path[i], plan.Path[i + 1]);
                    segLc.Curve = newLine;
                    
                    segments.Add(seg);
                    Logger.Debug($"建立管線段 {i}: ({plan.Path[i].X * GeometryUtils.FT_TO_MM:F0}, {plan.Path[i].Y * GeometryUtils.FT_TO_MM:F0}, {plan.Path[i].Z * GeometryUtils.FT_TO_MM:F0}) → ({plan.Path[i + 1].X * GeometryUtils.FT_TO_MM:F0}, {plan.Path[i + 1].Y * GeometryUtils.FT_TO_MM:F0}, {plan.Path[i + 1].Z * GeometryUtils.FT_TO_MM:F0})");
                }

                // 關鍵步驟4：刪除原管線（在建立新管線後）
                doc.Delete(pipe.Id);
                Logger.Debug($"已刪除舊管線 {oldPipeId}");

                // 關鍵步驟5：建立彎頭連接新管線段
                CreateElbowsForSegments(doc, segments.Cast<MEPCurve>().ToList());

                // 關鍵步驟6：恢復原兩端連接（參考風管避讓邏輯）
                Connector newConStart = ConnectorAtPoint(segments.First(), pStart);
                Connector newConEnd = ConnectorAtPoint(segments.Last(), pEnd);
                
                if (fittingConStart != null && newConStart != null)
                {
                    newConStart.ConnectTo(fittingConStart);
                    Logger.Debug($"已恢復起點連接");
                }
                if (fittingConEnd != null && newConEnd != null)
                {
                    newConEnd.ConnectTo(fittingConEnd);
                    Logger.Debug($"已恢復終點連接");
                }

                Logger.Info($"成功替換管線 {oldPipeId}，建立 {segments.Count} 段");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"替換管線 {pipe.Id} 失敗", ex);
                return false;
            }
        }

        private static bool ReplaceDuct(Document doc, Duct duct, DetourPlan plan)
        {
            var lc = duct.Location as LocationCurve;
            if (lc == null) 
            {
                Logger.Warning($"風管 {duct.Id} 沒有 LocationCurve");
                return false;
            }

            try
            {
                // 使用 CopyElement 保留所有屬性
                ElementId oldDuctId = duct.Id;
                
                // 建立多段風管（複製原風管）
                var segments = new List<Duct>();
                for (int i = 0; i < plan.Path.Count - 1; i++)
                {
                    // 複製原風管到臨時位置
                    XYZ offset = new XYZ(1000, 0, 0);
                    var copiedIds = ElementTransformUtils.CopyElement(doc, duct.Id, offset);
                    if (copiedIds.Count == 0)
                    {
                        Logger.Error($"複製風管段 {i} 失敗");
                        return false;
                    }
                    
                    Duct seg = doc.GetElement(copiedIds.First()) as Duct;
                    if (seg == null)
                    {
                        Logger.Error($"取得複製的風管段 {i} 失敗");
                        return false;
                    }
                    
                    // 修改位置為新路徑
                    LocationCurve segLc = seg.Location as LocationCurve;
                    Line newLine = Line.CreateBound(plan.Path[i], plan.Path[i + 1]);
                    segLc.Curve = newLine;
                    
                    segments.Add(seg);
                }

                // 刪除原風管
                doc.Delete(duct.Id);
                Logger.Debug($"已刪除舊風管 {oldDuctId}");

                // 建立彎頭連接（使用新方法）
                CreateElbowsForSegments(doc, segments.Cast<MEPCurve>().ToList());

                Logger.Info($"成功替換風管 {oldDuctId}，建立 {segments.Count} 段");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"替換風管 {duct.Id} 失敗", ex);
                return false;
            }
        }

        private static bool ReplaceConduit(Document doc, Conduit cdt, DetourPlan plan)
        {
            var lc = cdt.Location as LocationCurve;
            if (lc == null) 
            {
                Logger.Warning($"電管 {cdt.Id} 沒有 LocationCurve");
                return false;
            }

            try
            {
                ElementId oldConduitId = cdt.Id;
                ElementId levelId = cdt.LevelId;
                ElementId typeId = cdt.GetTypeId();

                if (levelId == ElementId.InvalidElementId)
                {
                    Logger.Warning($"電管 {cdt.Id} 沒有參考樓層");
                    return false;
                }

                // 保存電管直徑（刪除前）
                double diameter = 0;
                try
                {
                    Parameter diamParam = cdt.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                    if (diamParam != null)
                        diameter = diamParam.AsDouble();
                }
                catch { }

                // 刪除舊電管
                doc.Delete(cdt.Id);
                Logger.Debug($"已刪除舊電管 {oldConduitId}");

                // 建立新電管段
                var segments = new List<Conduit>();
                for (int i = 0; i < plan.Path.Count - 1; i++)
                {
                    var seg = Conduit.Create(doc, typeId, plan.Path[i], plan.Path[i + 1], levelId);
                    if (seg == null)
                    {
                        Logger.Error($"建立電管段 {i} 失敗");
                        return false;
                    }
                    segments.Add(seg);
                    
                    // 設定電管直徑
                    if (diameter > 0)
                    {
                        try
                        {
                            Parameter diamParam = seg.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                            if (diamParam != null && !diamParam.IsReadOnly)
                                diamParam.Set(diameter);
                        }
                        catch { }
                    }
                }

                // 建立彎頭連接
                CreateElbowsForSegments(doc, segments.Cast<MEPCurve>().ToList());

                Logger.Info($"成功替換電管 {oldConduitId}，建立 {segments.Count} 段");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"替換電管 {cdt.Id} 失敗", ex);
                return false;
            }
        }

        /// <summary>
        /// 為多段管線建立彎頭連接（參考 Revit 定點翻彎邏輯）
        /// </summary>
        public static void CreateElbowsForSegments(Document doc, List<MEPCurve> segments)
        {
            if (segments == null || segments.Count < 2)
            {
                Logger.Debug("管線段數不足，無需建立彎頭");
                return;
            }

            try
            {
                Logger.Info($"開始為 {segments.Count} 段管線建立彎頭");

                // 收集所有未連接的 Connector（參考程式碼邏輯）
                List<Connector> allConnectors = new List<Connector>();
                foreach (var seg in segments)
                {
                    if (seg?.ConnectorManager == null) continue;
                    
                    foreach (Connector cn in seg.ConnectorManager.Connectors)
                    {
                        // 只收集未連接的連接器
                        if (!cn.IsConnected)
                        {
                            allConnectors.Add(cn);
                            Logger.Debug($"Connector: Owner={cn.Owner.Id}, 位置=({cn.Origin.X * GeometryUtils.FT_TO_MM:F0}, {cn.Origin.Y * GeometryUtils.FT_TO_MM:F0}, {cn.Origin.Z * GeometryUtils.FT_TO_MM:F0}), 方向={cn.CoordinateSystem.BasisZ}");
                        }
                    }
                }

                Logger.Debug($"收集到 {allConnectors.Count} 個未連接的 Connector");

                // 找出相近的 Connector 配對並建立彎頭
                List<Connector> processed = new List<Connector>();
                int elbowCount = 0;

                for (int i = 0; i < allConnectors.Count; i++)
                {
                    if (processed.Contains(allConnectors[i])) continue;

                    for (int j = i + 1; j < allConnectors.Count; j++)
                    {
                        if (processed.Contains(allConnectors[j])) continue;

                        // 檢查是否來自不同元素且位置相近
                        if (allConnectors[i].Owner.Id != allConnectors[j].Owner.Id &&
                            allConnectors[i].Origin.IsAlmostEqualTo(allConnectors[j].Origin, 0.01))
                        {
                            try
                            {
                                // 建立彎頭
                                FamilyInstance elbow = doc.Create.NewElbowFitting(allConnectors[i], allConnectors[j]);
                                processed.Add(allConnectors[i]);
                                processed.Add(allConnectors[j]);
                                elbowCount++;
                                
                                Logger.Debug($"彎頭 #{elbowCount}: Id={elbow.Id}, 名稱={elbow.Name}, 位置=({allConnectors[i].Origin.X * GeometryUtils.FT_TO_MM:F0}, {allConnectors[i].Origin.Y * GeometryUtils.FT_TO_MM:F0}, {allConnectors[i].Origin.Z * GeometryUtils.FT_TO_MM:F0})");
                                
                                // 驗證連接狀態（參考程式碼邏輯）
                                if (allConnectors[i].IsConnected && allConnectors[j].IsConnected)
                                {
                                    Logger.Debug($"  ✓ 兩個連接器已成功連接");
                                    
                                    // 驗證弯頭本身的連接器狀態
                                    ConnectorSet elbowConnectors = elbow.MEPModel.ConnectorManager.Connectors;
                                    int connectedCount = 0;
                                    foreach (Connector elbowConn in elbowConnectors)
                                    {
                                        if (elbowConn.IsConnected)
                                        {
                                            connectedCount++;
                                            // 檢查連接的管線（參考 AllRefs 邏輯）
                                            foreach (Connector refConn in elbowConn.AllRefs)
                                            {
                                                if (refConn.Owner is MEPCurve)
                                                {
                                                    Logger.Debug($"  → 連接到管線: Id={refConn.Owner.Id}");
                                                }
                                            }
                                        }
                                    }
                                    Logger.Debug($"  彎頭的 {connectedCount}/2 個連接器已連接");
                                }
                                else
                                {
                                    Logger.Warning($"  ⚠ 連接器狀態異常: cn1.IsConnected={allConnectors[i].IsConnected}, cn2.IsConnected={allConnectors[j].IsConnected}");
                                }
                                
                                break;
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning($"建立彎頭失敗: {ex.Message}");
                            }
                        }
                    }
                }

                Logger.Info($"共建立 {elbowCount} 個彎頭（預期 {segments.Count - 1} 個）");
                
                // 檢查是否有未配對的連接器
                if (elbowCount < segments.Count - 1)
                {
                    Logger.Warning($"⚠ 彎頭數量不足！可能有連接器未正確配對");
                    
                    var unprocessed = allConnectors.Where(c => !processed.Contains(c)).ToList();
                    if (unprocessed.Any())
                    {
                        Logger.Warning($"未配對的連接器: {unprocessed.Count} 個");
                        foreach (var c in unprocessed)
                        {
                            Logger.Debug($"  未配對: Owner={c.Owner.Id}, 位置=({c.Origin.X * GeometryUtils.FT_TO_MM:F0}, {c.Origin.Y * GeometryUtils.FT_TO_MM:F0}, {c.Origin.Z * GeometryUtils.FT_TO_MM:F0})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("建立彎頭過程發生錯誤", ex);
            }
        }

        /// <summary>
        /// 獲取指定點位置的連接器（參考風管避讓邏輯）
        /// </summary>
        private static Connector ConnectorAtPoint(Element element, XYZ point)
        {
            if (element == null || point == null) return null;

            ConnectorSet connectorSet = null;

            // 風管連接器集合
            if (element is Duct duct)
                connectorSet = duct.ConnectorManager?.Connectors;
            // 管線連接器集合
            else if (element is Pipe pipe)
                connectorSet = pipe.ConnectorManager?.Connectors;
            // 電纜架連接器集合
            else if (element is CableTray cableTray)
                connectorSet = cableTray.ConnectorManager?.Connectors;
            // 線槽連接器集合
            else if (element is Conduit conduit)
                connectorSet = conduit.ConnectorManager?.Connectors;
            // 管件等可載入族的連接器集合
            else if (element is FamilyInstance fi)
                connectorSet = fi.MEPModel?.ConnectorManager?.Connectors;

            if (connectorSet == null) return null;

            // 遍歷連接器集合，找到距離目標點最近的連接器
            const double tolerance = 1.0 / 304.8; // 1mm 容差
            foreach (Connector connector in connectorSet)
            {
                if (connector.Origin.DistanceTo(point) < tolerance)
                {
                    return connector;
                }
            }

            return null;
        }

        /// <summary>
        /// 獲取與管線相連的管件連接器（參考風管避讓邏輯）
        /// </summary>
        private static Connector GetConnectorToFitting(Connector connector)
        {
            if (connector == null || !connector.IsConnected) return null;

            try
            {
                foreach (Connector con in connector.AllRefs)
                {
                    // 只選擇管件（FamilyInstance）
                    if (con.Owner is FamilyInstance)
                    {
                        return con;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"獲取連接管件失敗: {ex.Message}");
            }

            return null;
        }
    }
}