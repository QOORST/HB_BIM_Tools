using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;

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
                    
                    Logger.Info($"找到視圖類型: {viewFamilyType.Name} (ID: {viewFamilyType.Id.Value})");

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
                    
                    // 鎖定視圖以穩定標註
                    Logger.Info("鎖定 3D 視圖");
                    isoView.SaveOrientationAndLock();

                    trans.Commit();
                    Logger.Info("交易已提交，ISO 視圖生成成功");

                    return isoView;
                }
                catch (Exception ex)
                {
                    trans.RollBack();
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
                Logger.Warning($"設定視角失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 設定視圖過濾器（只顯示特定管線系統）
        /// </summary>
        private void SetViewFilter(View3D view, ISOData isoData)
        {
            try
            {
                Logger.Info("收集要顯示的元件");
                
                List<ElementId> elementIds = new List<ElementId>();

                // 從 ISOData 收集元件
                foreach (var segment in isoData.MainPipeSegments)
                {
                    elementIds.Add(segment.ElementId);
                }

                foreach (var branch in isoData.BranchSegments.Values)
                {
                    foreach (var segment in branch)
                    {
                        elementIds.Add(segment.ElementId);
                    }
                }

                Logger.Info($"ISOData 中共 {elementIds.Count} 個元件");

                // 從系統直接獲取所有元件
                if (!string.IsNullOrEmpty(isoData.SystemName))
                {
                    try
                    {
                        Logger.Info($"從系統 '{isoData.SystemName}' 獲取元件");
                        
                        FilteredElementCollector systemCollector = new FilteredElementCollector(_doc)
                            .OfClass(typeof(PipingSystem));
                        
                        foreach (PipingSystem system in systemCollector)
                        {
                            if (system.Name == isoData.SystemName)
                            {
                                Logger.Info($"找到系統: {system.Name}");
                                
                                ElementSet networkElements = system.PipingNetwork;
                                if (networkElements != null)
                                {
                                    foreach (Element element in networkElements)
                                    {
                                        if (element != null && !elementIds.Contains(element.Id))
                                        {
                                            elementIds.Add(element.Id);
                                        }
                                    }
                                    Logger.Info($"總計 {elementIds.Count} 個元件");
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"從系統獲取元件失敗: {ex.Message}");
                    }
                }

                Logger.Info($"最終顯示 {elementIds.Count} 個元件");

                if (elementIds.Count > 0)
                {
                    view.IsolateElementsTemporary(elementIds);
                    Logger.Info("元件隔離完成");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"設定過濾器失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 設定視圖顯示樣式
        /// </summary>
        private void SetViewDisplayStyle(View3D view)
        {
            Logger.Info("配置視圖顯示");
            
            view.DetailLevel = ViewDetailLevel.Medium;
            view.DisplayStyle = DisplayStyle.Shading;
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
        public void AddAnnotations(View view, ISOData isoData)
        {
            Logger.Info("開始添加標註");
            
            using (Transaction trans = new Transaction(_doc, "添加 ISO 標註"))
            {
                trans.Start();

                try
                {
                    AddPipeTagsToView(view, isoData);
                    Logger.Info("標註添加完成");
                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    Logger.Error("添加標註失敗", ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// 添加管線標籤 - 使用與 Revit UI 相同的標註方法
        /// </summary>
        private void AddPipeTagsToView(View view, ISOData isoData)
        {
            try
            {
                Logger.Info("========== 開始添加管線標籤 ==========");
                
                // 檢查視圖類型
                Logger.Info($"視圖類型: {view.ViewType}");
                Logger.Info($"視圖名稱: {view.Name}");
                
                // 直接從視圖中收集所有可見管線
                FilteredElementCollector collector = new FilteredElementCollector(_doc, view.Id);
                var pipes = collector.OfClass(typeof(Pipe))
                                   .WhereElementIsNotElementType()
                                   .Cast<Pipe>()
                                   .ToList();
                
                Logger.Info($"視圖中找到 {pipes.Count} 條管線");
                
                if (pipes.Count == 0)
                {
                    Logger.Warning("視圖中沒有可見的管線!");
                    return;
                }
                
                // 檢查是否有管線標籤族
                FilteredElementCollector tagCollector = new FilteredElementCollector(_doc);
                var pipeTagTypes = tagCollector.OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_PipeTags)
                    .Cast<FamilySymbol>()
                    .Where(x => x.IsActive)
                    .ToList();
                
                Logger.Info($"找到 {pipeTagTypes.Count} 個已啟用的管線標籤類型");
                
                if (pipeTagTypes.Count == 0)
                {
                    Logger.Warning("沒有啟用的管線標籤類型，嘗試啟用預設標籤族...");
                    
                    // 嘗試啟用第一個可用的管線標籤
                    var allPipeTagTypes = new FilteredElementCollector(_doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_PipeTags)
                        .Cast<FamilySymbol>()
                        .ToList();
                    
                    if (allPipeTagTypes.Count > 0)
                    {
                        using (Transaction activateTrans = new Transaction(_doc, "啟用標籤族"))
                        {
                            activateTrans.Start();
                            allPipeTagTypes[0].Activate();
                            activateTrans.Commit();
                            pipeTagTypes.Add(allPipeTagTypes[0]);
                            Logger.Info($"已啟用標籤族: {allPipeTagTypes[0].FamilyName} - {allPipeTagTypes[0].Name}");
                        }
                    }
                    else
                    {
                        Logger.Error("專案中沒有載入管線標籤族!");
                        Logger.Info("請先載入管線標籤族: 插入 > 載入族群 > 註釋 > 管 > 管標籤.rfa");
                        return;
                    }
                }
                
                // 使用第一個可用的標籤類型
                FamilySymbol tagType = pipeTagTypes[0];
                Logger.Info($"使用標籤類型: {tagType.FamilyName} - {tagType.Name}");
                
                int tagCount = 0;
                int failCount = 0;
                
                // 為每條管線添加標籤 - 使用簡化的方法
                foreach (var pipe in pipes)
                {
                    try
                    {
                        // 取得管線中點
                        LocationCurve locationCurve = pipe.Location as LocationCurve;
                        if (locationCurve == null) continue;
                        
                        Curve curve = locationCurve.Curve;
                        XYZ tagPoint = curve.Evaluate(0.5, true);
                        
                        // 方法: 使用 TagMode.TM_ADDBY_CATEGORY 讓 Revit 自動處理
                        // 關鍵: 不指定特定 Reference，而是讓 Revit 根據元件類別自動選擇
                        IndependentTag tag = IndependentTag.Create(
                            _doc,
                            view.Id,
                            new Reference(pipe),  // 直接使用元件 Reference
                            true,  // addLeader = true
                            TagMode.TM_ADDBY_CATEGORY,  // 按類別自動標註
                            TagOrientation.Horizontal,
                            tagPoint
                        );
                        
                        if (tag != null)
                        {
                            tagCount++;
                            
                            // 設定標籤使用指定的類型
                            if (tag.GetTypeId() != tagType.Id)
                            {
                                tag.ChangeTypeId(tagType.Id);
                            }
                            
                            Logger.Info($"✓ 標籤 #{tagCount}: 管線 {pipe.Id}");
                        }
                        else
                        {
                            failCount++;
                            Logger.Warning($"✗ 標籤返回 null: 管線 {pipe.Id}");
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException argEx)
                    {
                        // 這通常表示 3D 視圖不支援此類型的標註
                        if (failCount == 0)  // 只記錄第一次錯誤
                        {
                            Logger.Error($"ArgumentException: {argEx.Message}");
                            Logger.Warning("3D 視圖可能不支援管線標籤 (Revit API 限制)");
                        }
                        failCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        Logger.Error($"✗ 管線 {pipe.Id} 失敗: {ex.Message}");
                    }
                }
                
                Logger.Info("========== 標籤統計 ==========");
                Logger.Info($"成功: {tagCount} 個");
                Logger.Info($"失敗: {failCount} 個");
                Logger.Info($"總計: {pipes.Count} 條管線");
                
                if (tagCount == 0 && pipes.Count > 0)
                {
                    Logger.Warning("======================================");
                    Logger.Warning("無法在 3D 視圖中添加管線標籤");
                    Logger.Warning("======================================");
                    Logger.Warning("這是 Revit API 的已知限制:");
                    Logger.Warning("IndependentTag 在某些 3D 視圖中不支援管線元件");
                    Logger.Warning("");
                    Logger.Warning("替代方案:");
                    Logger.Warning("1. 使用已生成的明細表查看管線資訊");
                    Logger.Warning("2. 查看匯出的 CSV 檔案");
                    Logger.Warning("3. 在平面視圖或剖面視圖中手動添加標籤");
                    Logger.Warning("4. 使用 PCF 檔案進行 ISOGEN 處理");
                }
                else if (tagCount > 0)
                {
                    Logger.Info($"成功添加 {tagCount} 個標籤!");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("添加標籤過程發生錯誤", ex);
                // 不要 throw，讓其他功能繼續執行
            }
        }

        /// <summary>
        /// 匯出視圖為圖片
        /// </summary>
        public void ExportViewAsImage(View view, string filePath)
        {
            ImageExportOptions options = new ImageExportOptions
            {
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 1920,
                FilePath = filePath,
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_300,
                ShadowViewsFileType = ImageFileType.PNG
            };

            options.SetViewsAndSheets(new List<ElementId> { view.Id });

            _doc.ExportImage(options);
        }
    }
}
