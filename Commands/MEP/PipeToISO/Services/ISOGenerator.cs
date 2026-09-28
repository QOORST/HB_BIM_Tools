using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    /// <summary>
    /// ISO 圖生成器 - 在 Revit 中生成等角視圖
    /// </summary>
    public class ISOGenerator
    {
        private Document _doc;

        public ISOGenerator(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// 生成 ISO 視圖
        /// </summary>
        public View3D GenerateISOView(ISOData isoData, string viewName = null)
        {
            Logger.Info("開始生成 ISO 視圖");
            
            if (isoData == null)
            {
                Logger.Error("isoData 為 null");
                throw new ArgumentNullException(nameof(isoData));
            }

            if (string.IsNullOrEmpty(viewName))
            {
                viewName = $"ISO - {isoData.SystemName}";
            }
            
            Logger.Info($"視圖名稱: {viewName}");

            using (Transaction trans = new Transaction(_doc, "建立 ISO 視圖"))
            {
                trans.Start();
                Logger.Info("開始交易");

                try
                {
                    // 建立 3D 視圖
                    Logger.Info("查找 3D 視圖類型");
                    ViewFamilyType viewFamilyType = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

                    if (viewFamilyType == null)
                    {
                        Logger.Error("找不到 3D 視圖類型");
                        throw new Exception("找不到 3D 視圖類型");
                    }
                    
                    Logger.Info($"找到視圖類型: {viewFamilyType.Name} (ID: {viewFamilyType.Id.GetIdValue()})");

                    Logger.Info("建立等角視圖");
                    View3D isoView = View3D.CreateIsometric(_doc, viewFamilyType.Id);
                    
                    string uniqueName = GetUniqueViewName(viewName);
                    isoView.Name = uniqueName;
                    Logger.Info($"視圖已建立,名稱: {uniqueName}");

                    // 設定視圖方向(等角視圖)
                    Logger.Info("設定視圖方向");
                    SetISOViewOrientation(isoView, isoData);

                    // 設定視圖範圍(只顯示選定的管線系統)
                    Logger.Info("設定視圖過濾器");
                    SetViewFilter(isoView, isoData);

                    // 設定顯示樣式(包含詳細度為中等)
                    Logger.Info("設定顯示樣式");
                    SetViewDisplayStyle(isoView);
                    
                    isoView.Scale = 50;
                    isoView.IsSectionBoxActive = false;
                    isoView.CropBoxActive = false;
                    // 鎖定視圖以穩定標註
                    Logger.Info("鎖定 3D 視圖");
                    isoView.SaveOrientationAndLock();

                    if (trans.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("視圖交易未提交。");
                    Logger.Info("交易已提交，ISO 視圖生成成功");

                    return isoView;
                }
                catch (Exception ex)
                {
                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                    Logger.Error("建立 ISO 視圖時發生錯誤", ex);
                    throw new Exception($"建立 ISO 視圖失敗：{ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 設定 ISO 視圖方向（等角 30度）
        /// </summary>
        private void SetISOViewOrientation(View3D view, ISOData isoData)
        {
            try
            {
                Logger.Info("設定 ISO 視角");
                
                // 建立等角視圖方向（標準 ISO 角度）
                XYZ eyePosition = new XYZ(1, 1, 1).Normalize();
                XYZ upDirection = new XYZ(-1, -1, 2).Normalize();
                XYZ forwardDirection = eyePosition.Negate();

                ViewOrientation3D orientation = new ViewOrientation3D(
                    eyePosition,
                    upDirection,
                    forwardDirection
                );

                view.SetOrientation(orientation);
                view.SaveOrientation();
                Logger.Info("視角設定完成");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("設定等角視角失敗。", ex);
            }
        }

        /// <summary>
        /// 設定視圖過濾器（只顯示特定管線系統）
        /// </summary>
        private void SetViewFilter(View3D view, ISOData isoData)
        {
            var system = _doc.GetElement(isoData.SystemId) as PipingSystem;
            if (system == null) throw new InvalidOperationException("選定系統已不存在。");
            var ids = isoData.GetScopedElements(_doc).Select(e => e.Id).ToList();
            if (ids.Count == 0) throw new InvalidOperationException("選定系統沒有可顯示的管路元件。");
            view.IsolateElementsTemporary(ids);
            view.ConvertTemporaryHideIsolateToPermanent();
        }
        /// <summary>
        /// 設定視圖顯示樣式
        /// </summary>
        private void SetViewDisplayStyle(View3D view)
        {
            Logger.Info("配置視圖顯示");
            
            view.DetailLevel = ViewDetailLevel.Medium;
            view.DisplayStyle = DisplayStyle.HLR;
            // 工作視圖沿用使用者背景；固定白色漸層會與深色介面的線色反轉衝突。
            // 白底僅於 ExportViewAsImage 暫時套用並還原。
            var graphics = new OverrideGraphicSettings().SetProjectionLineColor(new Color(0, 0, 0))
                .SetProjectionLineWeight(3);
            foreach (var category in new[] { BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_PipeTags })
                view.SetCategoryOverrides(new ElementId(category), graphics);
            view.AreAnnotationCategoriesHidden = false;

            try
            {
                Parameter shadowParam = view.LookupParameter("顯示陰影") ?? 
                                       view.LookupParameter("Show Shadows");
                if (shadowParam != null && !shadowParam.IsReadOnly)
                {
                    shadowParam.Set(0);
                }
            }
            catch { }
            
            Logger.Info("顯示設定完成");
        }

        /// <summary>
        /// 取得唯一的視圖名稱
        /// </summary>
        private string GetUniqueViewName(string baseName)
        {
            string name = baseName;
            int counter = 1;

            while (ViewNameExists(name))
            {
                name = $"{baseName} ({counter})";
                counter++;
            }

            return name;
        }

        /// <summary>
        /// 檢查視圖名稱是否已存在
        /// </summary>
        private bool ViewNameExists(string name)
        {
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            return collector
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 在視圖中添加標註
        /// </summary>
        public string AddAnnotations(View view, ISOData isoData)
        {
            var system = _doc.GetElement(isoData.SystemId) as PipingSystem;
            if (system == null) throw new InvalidOperationException("選定系統已不存在。");
            var pipes = isoData.GetScopedElements(_doc).OfType<Pipe>().OrderBy(p => p.Id.GetIdValue()).ToList();
            if (pipes.Count == 0) return "沒有需要標註的直管";

            using (var trans = new Transaction(_doc, "添加管路等角標籤"))
            {
                trans.Start();
                try
                {
                    var tagType = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_PipeTags).Cast<FamilySymbol>()
                        .OrderBy(TagRotationPreference).ThenByDescending(t => t.IsActive)
                        .ThenBy(t => t.Id.GetIdValue()).FirstOrDefault();
                    if (tagType == null) throw new InvalidOperationException("未載入管線標籤族，請先載入後再執行。");
                    int rotationPreference = TagRotationPreference(tagType);
                    string directionNote = rotationPreference == 0
                        ? "已要求水平方向，仍請核對族內文字設定"
                        : "標籤族會隨元件旋轉或無法確認方向設定；若需全部水平，請載入不隨元件旋轉的管標籤族";
                    Logger.Info($"標籤族：{tagType.FamilyName} / {tagType.Name}；{directionNote}");
                    // 使用既有交易啟用族群，禁止巢狀 Transaction。
                    if (!tagType.IsActive)
                    {
                        tagType.Activate();
                        _doc.Regenerate();
                    }
                    var failures = new List<string>();
                    int count = 0;
                    int unresolved = 0;
                    var occupied = new List<ViewRect>();
                    Transform plane = view.CropBox.Transform;
                    Transform inverse = plane.Inverse;
                    double step = UnitUtils.ConvertToInternalUnits(8 * view.Scale, UnitTypeId.Millimeters);
                    double gap = UnitUtils.ConvertToInternalUnits(1.5 * view.Scale, UnitTypeId.Millimeters);
                    foreach (var pipe in pipes)
                    {
                        try
                        {
                            var location = pipe.Location as LocationCurve;
                            if (location == null) throw new InvalidOperationException("沒有管線位置曲線");
                            var tag = IndependentTag.Create(_doc, tagType.Id, view.Id, new Reference(pipe),
                                true, TagOrientation.Horizontal, location.Curve.Evaluate(0.5, true));
                            if (tag == null) throw new InvalidOperationException("Revit 未建立標籤");
                            tag.TagOrientation = TagOrientation.Horizontal;
                            // 暫不顯示引線，避免把長引線當成文字框。
                            tag.HasLeader = false;
                            _doc.Regenerate();
                            var box = ProjectBounds(tag.get_BoundingBox(view), inverse);
                            if (box == null) throw new InvalidOperationException("無法取得標籤文字範圍");
                            var rect = new ViewRect(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
                            double dx, dy;
                            if (!ViewLayout.TryPlace(rect, occupied, step, gap, out dx, out dy)) unresolved++;
                            tag.TagHeadPosition += plane.BasisX * dx + plane.BasisY * dy;
                            _doc.Regenerate();
                            box = ProjectBounds(tag.get_BoundingBox(view), inverse);
                            if (box == null) throw new InvalidOperationException("移動後無法取得標籤文字範圍");
                            occupied.Add(new ViewRect(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y));
                            tag.HasLeader = true;
                            count++;
                        }
                        catch (Exception ex)
                        {
                            failures.Add(pipe.Id.GetIdValue() + "：" + ex.Message);
                        }
                    }
                    if (failures.Count > 0)
                    {
                        Logger.Warning(string.Join(Environment.NewLine, failures));
                        throw new InvalidOperationException(
                            $"共 {pipes.Count} 根管，{failures.Count} 根標註失敗；本次標籤全部回復。首筆：{failures[0]}");
                    }
                    if (trans.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("標籤交易未提交。");
                    return $"已建立 {count} 個標籤；{directionNote}；{unresolved} 個文字框未找到無重疊位置，請檢查管路與引線交叉";
                }
                catch
                {
                    if (trans.GetStatus() == TransactionStatus.Started) trans.RollBack();
                    throw;
                }
            }
        }

        private static int TagRotationPreference(FamilySymbol symbol)
        {
            var parameter = symbol.Family.get_Parameter(BuiltInParameter.FAMILY_ROTATE_WITH_COMPONENT)
                ?? symbol.get_Parameter(BuiltInParameter.FAMILY_ROTATE_WITH_COMPONENT);
            if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.Integer) return 1;
            return parameter.AsInteger() == 0 ? 0 : 2;
        }

        // BoundingBoxXYZ 可能有自身 Transform，必須投影全部 8 個角點。
        private static BoundingBoxXYZ ProjectBounds(BoundingBoxXYZ box, Transform inverse)
        {
            if (box == null) return null;
            XYZ min = null, max = null;
            for (int i = 0; i < 8; i++)
            {
                var corner = new XYZ((i & 1) == 0 ? box.Min.X : box.Max.X,
                    (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z);
                var point = inverse.OfPoint(box.Transform.OfPoint(corner));
                min = min == null ? point : new XYZ(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
                max = max == null ? point : new XYZ(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
            }
            return new BoundingBoxXYZ { Min = min, Max = max };
        }

        public string FitView(View3D view, ISOData data)
        {
            using (var transaction = new Transaction(_doc, "調整管路與標籤完整取景"))
            {
                transaction.Start();
                try
                {
                    view.CropBoxActive = false;
                    _doc.Regenerate();
                    var system = _doc.GetElement(data.SystemId) as PipingSystem;
                    if (system == null) throw new InvalidOperationException("選定系統已不存在。");
                    var elements = data.GetScopedElements(_doc);
                    elements.AddRange(new FilteredElementCollector(_doc, view.Id)
                        .OfClass(typeof(IndependentTag)).ToElements());
                    var crop = view.CropBox;
                    var boxes = elements.Select(e => ProjectBounds(e.get_BoundingBox(view), crop.Transform.Inverse)).ToList();
                    if (boxes.Count == 0 || boxes.Any(b => b == null))
                        throw new InvalidOperationException("部分元件無法取得範圍，未匯出可能遭裁切的圖片。");
                    var extent = new ViewRect(boxes.Min(b => b.Min.X), boxes.Min(b => b.Min.Y),
                        boxes.Max(b => b.Max.X), boxes.Max(b => b.Max.Y));
                    // 至少紙面 10 mm 或每側 8%，包括文字和引線端點。
                    double margin = UnitUtils.ConvertToInternalUnits(10 * view.Scale, UnitTypeId.Millimeters);
                    var padded = extent.Pad(margin, 0.08);
                    crop.Min = new XYZ(padded.Left, padded.Bottom, boxes.Min(b => b.Min.Z) - margin);
                    crop.Max = new XYZ(padded.Right, padded.Top, boxes.Max(b => b.Max.Z) + margin);
                    view.CropBox = crop;
                    view.CropBoxActive = true;
                    view.CropBoxVisible = false;
                    var annotationCrop = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                    if (annotationCrop != null && !annotationCrop.IsReadOnly) annotationCrop.Set(0);
                    _doc.Regenerate();
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("取景交易未提交。");
                    Logger.Info($"取景完成：{elements.Count} 個元件／標籤，比例 1:{view.Scale}，每側 8% 或紙面 10 mm 留邊");
                    return $"完整管路與標籤範圍，比例 1:{view.Scale}";
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    throw;
                }
            }
        }

        /// <summary>匯出指定等角視圖，暫用白底後還原使用者的全域背景。</summary>
        public void ExportViewAsImage(View view, string filePath)
        {
            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 3000,
                FilePath = filePath,
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_300,
                ShadowViewsFileType = ImageFileType.PNG
            };
            options.SetViewsAndSheets(new List<ElementId> { view.Id });
            var previous = _doc.Application.BackgroundColor;
            try
            {
                _doc.Application.BackgroundColor = new Color(255, 255, 255);
                _doc.ExportImage(options);
            }
            finally { _doc.Application.BackgroundColor = previous; }
        }
    }
}
