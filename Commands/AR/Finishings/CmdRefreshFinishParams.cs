using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings
{
    /// <summary>
    /// AR 裝修工具 - 更新粉刷面共用參數
    /// 重新掃描所有（或選取的）粉刷元素，依目前幾何位置回寫
    /// AR_RoomId / AR_RoomNames / AR_RoomNumbers / 面積 / 材料名稱 / 厚度
    /// 適用於面生面或手動調整後的粉刷面，確保數量表資訊正確。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdRefreshFinishParams : IExternalCommand
    {
        private static readonly BuiltInCategory[] TargetCategories =
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_GenericModel
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc   = uidoc.Document;

            try
            {
                // ── 授權檢查 ────────────────────────────────────────
                var licMgr = LicenseManager.Instance;
                if (!licMgr.HasFeatureAccess("Finishings.FaceToFace") &&
                    !licMgr.HasFeatureAccess("Finishings.Generate"))
                {
                    TaskDialog.Show("授權限制",
                        "您的授權版本不支援此功能。\n\n" +
                        "請升級至 Standard+ 以使用「更新粉刷面參數」。");
                    return Result.Cancelled;
                }

                // ── 確保 AR 裝修共用參數已建立並綁定 ─────────────────────
                var paramWriter = new RoomFinish.ValueWriter(uidoc);
                paramWriter.EnsureSharedParameters();

                // ── 收集候選元素 ─────────────────────────────────────
                var allItems = CollectFinishingElements(doc);

                // 同時檢查目前選取集中哪些是粉刷元素
                var selectedIds = uidoc.Selection.GetElementIds();
                var selectedItems = selectedIds.Count > 0
                    ? allItems.Where(e => selectedIds.Contains(e.Id)).ToList()
                    : new List<Element>();

                if (allItems.Count == 0)
                {
                    TaskDialog.Show("更新粉刷面參數",
                        "模型中未找到已標記的 AR 粉刷元素（YD_BIM_Finishings）。\n\n" +
                        "請先透過「房間裝修」或「面生面」功能生成粉刷元素。");
                    return Result.Cancelled;
                }

                // ── 選項對話框 ────────────────────────────────────────
                var dlg = new TaskDialog("更新粉刷面共用參數")
                {
                    MainInstruction = "選擇要更新的範圍",
                    MainContent =
                        "將依粉刷元素目前在模型中的幾何位置，重新計算並回寫下列共用參數：\n\n" +
                        "• 房間 ID / 名稱 / 編號（AR_RoomId / AR_RoomNames / AR_RoomNumbers）\n" +
                        "• 裝修面積（面積參數）\n" +
                        "• 材料名稱\n" +
                        "• 厚度\n\n" +
                        "適用於面生面元素及手動調整後的粉刷面，確保數量表資料正確。",
                    FooterText = $"模型中共 {allItems.Count} 個粉刷元素" +
                                 (selectedItems.Count > 0 ? $"，目前選取 {selectedItems.Count} 個" : "")
                };

                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"更新全部粉刷元素（{allItems.Count} 個）");

                if (selectedItems.Count > 0)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        $"只更新目前選取的粉刷元素（{selectedItems.Count} 個）");

                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var result = dlg.Show();

                List<Element> targets;
                if (result == TaskDialogResult.CommandLink1)
                    targets = allItems;
                else if (result == TaskDialogResult.CommandLink2)
                    targets = selectedItems;
                else
                    return Result.Cancelled;

                // ── 執行更新 ───────────────────────────────────────────
                var rooms = GetValidRooms(doc);
                int updated = 0, skipped = 0, failed = 0;

                using (var tx = new Transaction(doc, "AR裝修-更新粉刷面參數"))
                {
                    tx.Start();

                    foreach (var elem in targets)
                    {
                        try
                        {
                            bool ok = RefreshElementParams(doc, elem, rooms);
                            if (ok) updated++;
                            else    skipped++; // 無參數可更新（非錯誤）
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"⚠️ 元素 {elem.Id} 更新失敗: {ex.Message}");
                            failed++;
                        }
                    }

                    tx.Commit();
                }

                // ── 結果摘要 ────────────────────────────────────────────
                string summary = $"✅ 更新完成\n\n" +
                                 $"成功: {updated} 個元素\n";
                if (skipped > 0)
                    summary += $"跳過（無需更新）: {skipped} 個元素\n";
                if (failed > 0)
                    summary += $"失敗: {failed} 個元素（詳見 Debug 輸出）\n";

                TaskDialog.Show("更新粉刷面參數", summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"更新粉刷面參數失敗: {ex.Message}";
                TaskDialog.Show("錯誤", $"執行時發生錯誤:\n{ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  收集粉刷元素
        // ─────────────────────────────────────────────────────────────────

        private List<Element> CollectFinishingElements(Document doc)
        {
            var result = new List<Element>();
            var rooms = GetValidRooms(doc);
            foreach (var cat in TargetCategories)
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType();

                foreach (Element elem in collector)
                {
                    if (IsFinishingElement(elem, rooms))
                        result.Add(elem);
                }
            }
            return result;
        }

        /// <summary>
        /// 判斷元素是否為 AR 裝修系統產生的粉刷元素：
        /// 1. ALL_MODEL_INSTANCE_COMMENTS 含 "YD_BIM_Finishings"（新版標記）
        /// 2. 或 AR_RoomId 參數有值（舊版生成但有房間參數）
        /// </summary>
        private static bool IsFinishingElement(Element elem, List<Room> rooms = null)
        {
            try
            {
                // 優先：穩定識別標記
                var comments = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                if (!string.IsNullOrEmpty(comments) &&
                    comments.IndexOf("YD_BIM_Finishings", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // 備用：有 AR_RoomId 參數且有值
                var pId = elem.LookupParameter("房間ID(AR_RoomId)")
                       ?? elem.LookupParameter("房間ID")
                       ?? elem.LookupParameter("AR_RoomId");
                if (pId != null)
                {
                    if (pId.StorageType == StorageType.Integer && pId.AsInteger() != 0)
                        return true;
                    if (pId.StorageType == StorageType.String && !string.IsNullOrEmpty(pId.AsString()))
                        return true;
                }

                // 手動補建 fallback：
                // 若元素位於房間內，且其 Type 名稱與房間 AR 設定材料一致，視為粉刷元素。
                if (rooms != null && rooms.Count > 0 && (elem is Wall || elem is Floor || elem is Ceiling))
                {
                    var room = FindRoomByElementPoints(elem, rooms);
                    if (room != null)
                    {
                        var typeName = (elem.Document.GetElement(elem.GetTypeId()) as ElementType)?.Name ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(typeName))
                        {
                            string target = string.Empty;
                            if (elem is Wall)
                                target = GetRoomStringParam(room, "AR_牆面塗層", "牆面塗層");
                            else if (elem is Floor)
                                target = GetRoomStringParam(room, "AR_樓板塗層", "樓板塗層");
                            else if (elem is Ceiling)
                                target = GetRoomStringParam(room, "AR_天花板塗層", "天花板塗層");

                            if (!string.IsNullOrWhiteSpace(target) &&
                                string.Equals(typeName.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static Room FindRoomByElementPoints(Element elem, List<Room> rooms)
        {
            foreach (var pt in GetCandidateTestPoints(elem))
            {
                var room = rooms.FirstOrDefault(r => r.IsPointInRoom(pt));
                if (room != null)
                    return room;
            }
            return null;
        }

        private static string GetRoomStringParam(Room room, params string[] names)
        {
            foreach (var n in names)
            {
                var p = room.LookupParameter(n);
                if (p != null)
                {
                    var s = p.AsString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }
            return string.Empty;
        }

        // ─────────────────────────────────────────────────────────────────
        //  取得有效房間列表
        // ─────────────────────────────────────────────────────────────────

        private static List<Room> GetValidRooms(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        //  重新計算並寫入單一元素的共用參數
        // ─────────────────────────────────────────────────────────────────

        private static bool RefreshElementParams(Document doc, Element elem, List<Room> rooms)
        {
            bool anyChanged = false;

            // 1. 房間關聯參數（AR_RoomId / AR_RoomNames / AR_RoomNumbers）
            if (TryWriteRoomParams(elem, rooms))
                anyChanged = true;

            // 2. 面積（取自 Revit HOST_AREA_COMPUTED，比儲存值更準確）
            if (TryWriteAreaParam(elem))
                anyChanged = true;

            // 3. 材料名稱 + 厚度（從元素類型推導；DirectShape 無類型則跳過）
            if (TryWriteMaterialAndThickness(doc, elem))
                anyChanged = true;

            return anyChanged;
        }

        // ─── 子函式：房間參數 ──────────────────────────────────────────────

        private static bool TryWriteRoomParams(Element elem, List<Room> rooms)
        {
            try
            {
                // 對牆元素需要嘗試多個候選點（牆中心線在牆體內，IsPointInRoom 會失敗）
                Room found = null;
                foreach (var pt in GetCandidateTestPoints(elem))
                {
                    found = rooms.FirstOrDefault(r => r.IsPointInRoom(pt));
                    if (found != null) break;
                }
                if (found == null) return false;

                bool changed = false;

                // AR_RoomId
                var pId = elem.LookupParameter("房間ID(AR_RoomId)")
                       ?? elem.LookupParameter("房間ID")
                       ?? elem.LookupParameter("AR_RoomId");
                if (pId != null && !pId.IsReadOnly)
                {
                    var rid = found.Id.GetIdValue();
                    if (pId.StorageType == StorageType.Integer &&
                        rid >= int.MinValue && rid <= int.MaxValue)
                    {
                        pId.Set((int)rid);
                        changed = true;
                    }
                    else if (pId.StorageType == StorageType.String)
                    {
                        pId.Set(rid.ToString());
                        changed = true;
                    }
                }

                // AR_RoomNames
                var pName = elem.LookupParameter("房間名稱(AR_RoomNames)")
                         ?? elem.LookupParameter("房間名稱")
                         ?? elem.LookupParameter("AR_RoomNames");
                if (pName != null && !pName.IsReadOnly &&
                    pName.StorageType == StorageType.String)
                {
                    pName.Set(BuildRoomDisplayName(found));
                    changed = true;
                }

                // AR_RoomNumbers
                var pNum = elem.LookupParameter("房間編號(AR_RoomNumbers)")
                        ?? elem.LookupParameter("房間編號")
                        ?? elem.LookupParameter("AR_RoomNumbers");
                if (pNum != null && !pNum.IsReadOnly &&
                    pNum.StorageType == StorageType.String)
                {
                    pNum.Set(found.Number ?? "");
                    changed = true;
                }

                if (changed)
                    Debug.WriteLine($"    ✅ 房間參數已更新: {found.Number} {found.Name} → 元素 {elem.Id}");

                return changed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"    ⚠️ TryWriteRoomParams 失敗: {ex.Message}");
                return false;
            }
        }

        // ─── 子函式：取元素候選測試點（優先取牆面外側點確保在房間內）────────

        private static IEnumerable<XYZ> GetCandidateTestPoints(Element elem)
        {
            var pts = new List<XYZ>();
            try
            {
                // 對牆元素：從中心線往兩側偏移，取牆面外的點
                if (elem is Wall wall && wall.Location is LocationCurve wallCurve)
                {
                    var curve = wallCurve.Curve;
                    var midPt = curve.Evaluate(0.5, true);
                    var wallDir = curve.GetEndPoint(1) - curve.GetEndPoint(0);
                    if (wallDir.GetLength() > 1e-9)
                    {
                        wallDir = wallDir.Normalize();
                        // 在水平面旋轉 90°，取垂直方向
                        var perpDir = new XYZ(-wallDir.Y, wallDir.X, 0);
                        // 偏移量：半牆厚 + 0.25 ft（約 76mm）確保跑出牆體
                        double offset = wall.Width / 2.0 + 0.25;
                        double zUp   = 1.0; // 約 300mm 高，避免落在底板
                        pts.Add(midPt + perpDir.Multiply(offset) + XYZ.BasisZ.Multiply(zUp));
                        pts.Add(midPt - perpDir.Multiply(offset) + XYZ.BasisZ.Multiply(zUp));
                        pts.Add(midPt + XYZ.BasisZ.Multiply(zUp)); // 退回中心線但抬高
                    }
                    pts.Add(midPt);
                    return pts;
                }

                if (elem.Location is LocationPoint lp)
                {
                    pts.Add(lp.Point);
                    return pts;
                }
                if (elem.Location is LocationCurve lc)
                {
                    pts.Add(lc.Curve.Evaluate(0.5, true));
                    return pts;
                }

                var bbox = elem.get_BoundingBox(null);
                if (bbox != null)
                {
                    var center = (bbox.Min + bbox.Max) * 0.5;
                    pts.Add(center);
                    // 對薄型面元素（DirectShape），BBox 中心可能在牆體上，嘗試四方向微移
                    const double nudge = 0.5; // ft ≈ 150mm
                    pts.Add(new XYZ(center.X + nudge, center.Y, center.Z));
                    pts.Add(new XYZ(center.X - nudge, center.Y, center.Z));
                    pts.Add(new XYZ(center.X, center.Y + nudge, center.Z));
                    pts.Add(new XYZ(center.X, center.Y - nudge, center.Z));
                }
            }
            catch { }
            return pts;
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

        // ─── 子函式：面積 ──────────────────────────────────────────────────

        private static bool TryWriteAreaParam(Element elem)
        {
            try
            {
                double areaM2 = 0;

                if (elem is Wall || elem is Floor || elem is Ceiling)
                {
                    // HOST_AREA_COMPUTED 為 Revit 即時計算值（ft²）
                    var areaFt2Param = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (areaFt2Param != null)
                        areaM2 = areaFt2Param.AsDouble() * 0.09290304; // ft² → m²
                }
                // DirectShape：無法自動取得 HOST_AREA_COMPUTED，保留既有值

                if (areaM2 <= 0) return false;

                var pArea = elem.LookupParameter(SharedParams.P_Area);
                if (pArea == null || pArea.IsReadOnly) return false;

                double areaInFt2 = AreaCalculator.ConvertToSquareFeet(areaM2);
                pArea.Set(areaInFt2);
                Debug.WriteLine($"    ✅ 面積更新: {areaM2:F3} m² → 元素 {elem.Id}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"    ⚠️ TryWriteAreaParam 失敗: {ex.Message}");
                return false;
            }
        }

        // ─── 子函式：材料名稱 + 厚度 ────────────────────────────────────────

        private static bool TryWriteMaterialAndThickness(Document doc, Element elem)
        {
            try
            {
                CompoundStructure cs = null;
                if (elem is Wall w)
                    cs = w.WallType?.GetCompoundStructure();
                else if (elem is Floor f)
                    cs = f.FloorType?.GetCompoundStructure();
                else if (elem is Ceiling c)
                    cs = (doc.GetElement(c.GetTypeId()) as CeilingType)?.GetCompoundStructure();

                if (cs == null) return false;

                var layers = cs.GetLayers();
                if (layers == null || layers.Count == 0) return false;

                // 取最外層（裝修面）的材料和厚度
                var outerLayer = layers[0];
                var material = doc.GetElement(outerLayer.MaterialId) as Material;
                double thicknessMm = outerLayer.Width * 304.8; // ft → mm

                bool changed = false;

                var pMat = elem.LookupParameter(SharedParams.P_MaterialName);
                if (pMat != null && !pMat.IsReadOnly &&
                    pMat.StorageType == StorageType.String && material != null)
                {
                    pMat.Set(material.Name);
                    changed = true;
                }

                var pThk = elem.LookupParameter(SharedParams.P_Thickness);
                if (pThk != null && !pThk.IsReadOnly && thicknessMm > 0)
                {
                    pThk.Set(thicknessMm);
                    changed = true;
                }

                if (changed)
                    Debug.WriteLine($"    ✅ 材料/厚度更新: {material?.Name} / {thicknessMm:F1}mm → 元素 {elem.Id}");

                return changed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"    ⚠️ TryWriteMaterialAndThickness 失敗: {ex.Message}");
                return false;
            }
        }
    }
}
