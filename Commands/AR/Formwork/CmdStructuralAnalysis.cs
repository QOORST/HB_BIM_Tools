using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    [Transaction(TransactionMode.Manual)]
    public class CmdStructuralAnalysis : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 授權檢查
                if (!LicenseHelper.CheckLicense("StructuralAnalysis", "結構分析", LicenseType.Professional))
                {
                    return Result.Cancelled;
                }

                var doc = commandData.Application.ActiveUIDocument.Document;
                var uidoc = commandData.Application.ActiveUIDocument;

                // 確保共用參數存在
                SharedParams.Ensure(doc);

                using (var tx = new Transaction(doc, "結構模板分析"))
                {
                    tx.Start();
                    
                    // 使用傳統模式進行分析
                    FormworkEngine.Debug.Enable(true);
                    FormworkEngine.BeginRun();

                    // 執行完整的結構分析
                    var analysisResult = StructuralFormworkAnalyzer.AnalyzeProject(doc);
                    
                    // 生成模板幾何
                    GenerateFormworkGeometry(doc, analysisResult);
                    
                    FormworkEngine.EndRun();

                    // 顯示分析結果
                    ShowAnalysisResults(analysisResult);
                    
                    tx.Commit();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
        


        private void GenerateFormworkGeometry(Document doc, StructuralAnalysisResult result)
        {
            var generatedFormworkIds = new List<ElementId>();
            
            foreach (var elementAnalysis in result.ElementAnalyses)
            {
                var element = elementAnalysis.Key;
                var analysis = elementAnalysis.Value;

                try
                {
                    var formworkIds = new List<ElementId>();
                    
                    // 第一優先：改進的模板引擎（基於 Dynamo 邏輯）
                    formworkIds = GenerateFormworkWithImprovedEngine(doc, element);
                    generatedFormworkIds.AddRange(formworkIds);

                    System.Diagnostics.Debug.WriteLine($"改進引擎生成 {formworkIds.Count} 個模板");

                    // 如果改進引擎失敗，嘗試 Wall/Floor 引擎
                    if (formworkIds.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("改進引擎失敗，嘗試 Wall/Floor 引擎");
                        var wallFloorIds = GenerateFormworkWithWallFloor(doc, element);
                        generatedFormworkIds.AddRange(wallFloorIds);
                        formworkIds = wallFloorIds;

                        // 如果都失敗，最後回退到原始方法
                        if (wallFloorIds.Count == 0)
                        {
                            System.Diagnostics.Debug.WriteLine("Wall/Floor 引擎也失敗，使用原始方法");
                            var fallbackIds = FormworkEngine.BuildFormworkSolids(
                                doc, element, analysis.FormworkInfo, null, null, 
                                true, 20, 30, true);
                            generatedFormworkIds.AddRange(fallbackIds);
                            formworkIds = fallbackIds.ToList();
                        }
                    }

                    // ✅ 修正順序：先設定宿主ID，再計算面積，最後設定顏色
                    // 1. 先設定宿主ID參數（面積計算需要）
                    SetHostIdParametersForFormwork(doc, formworkIds, element);
                    
                    // 2. 計算並設定面積參數
                    SetFormworkAreaParameters(doc, formworkIds, element);
                    
                    // 3. 最後設定模板顏色和材質
                    SetFormworkAppearance(doc, formworkIds, analysis);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"生成元素 {element.Id} 模板失敗: {ex.Message}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"總共生成 {generatedFormworkIds.Count} 個模板元素");
        }

        /// <summary>
        /// 使用改進的模板引擎生成模板（基於 Dynamo 邏輯）
        /// </summary>
        private List<ElementId> GenerateFormworkWithImprovedEngine(Document doc, Element element)
        {
            try
            {
                // 使用改進的引擎，基於 Dynamo 腳本邏輯
                return ImprovedFormworkEngine.CreateFormworkFromElement(doc, element, 18.0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"使用改進引擎生成模板失敗: {ex.Message}");
                return new List<ElementId>();
            }
        }

        /// <summary>
        /// 使用 Wall 和 Floor 生成模板（備用方法）
        /// </summary>
        private List<ElementId> GenerateFormworkWithWallFloor(Document doc, Element element)
        {
            var formworkIds = new List<ElementId>();

            try
            {
                // 取得元素的所有面
                var faces = GetElementFaces(element);
                
                foreach (var face in faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        // 檢查面是否需要模板
                        if (ShouldGenerateFormwork(planarFace, element))
                        {
                            // 使用 FormworkEngine 的方法替代
                            var formworkId = FormworkEngine.BuildFromFaceAccurate(
                                doc, element, planarFace, 18.0, null); // 18mm 厚度
                            
                            if (formworkId != ElementId.InvalidElementId)
                            {
                                formworkIds.Add(formworkId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"使用 Wall/Floor 生成模板失敗: {ex.Message}");
            }

            return formworkIds;
        }

        /// <summary>
        /// 取得元素的所有面
        /// </summary>
        private List<Face> GetElementFaces(Element element)
        {
            var faces = new List<Face>();

            try
            {
                var geom = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
                if (geom == null) return faces;

                foreach (var obj in geom)
                {
                    if (obj is Solid solid && solid.Volume > 0.001)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            faces.Add(face);
                        }
                    }
                    else if (obj is GeometryInstance instance)
                    {
                        var instGeom = instance.GetInstanceGeometry();
                        foreach (var instObj in instGeom)
                        {
                            if (instObj is Solid instSolid && instSolid.Volume > 0.001)
                            {
                                foreach (Face face in instSolid.Faces)
                                {
                                    faces.Add(face);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"取得元素面失敗: {ex.Message}");
            }

            return faces;
        }

        /// <summary>
        /// 判斷面是否需要生成模板
        /// </summary>
        private bool ShouldGenerateFormwork(PlanarFace face, Element host)
        {
            try
            {
                var normal = face.FaceNormal;
                var area = face.Area * 0.092903; // 轉換為平方米

                // 基本規則
                if (area < 0.1) return false; // 太小的面

                // 樓板頂面通常不需要模板
                if (host is Floor && normal.Z > 0.85) return false;

                // 牆的頂面和底面可能不需要模板
                if (host is Wall && Math.Abs(normal.Z) > 0.85) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 根據結構類型設定模板實體的顏色外觀
        /// </summary>
        private void SetFormworkAppearance(Document doc, IList<ElementId> formworkIds, ElementFormworkAnalysis analysis)
        {
            // 🚀 重構: 使用統一的 VisualEffectsManager 和 ElementCategorizer
            var colorScheme = GetStructuralColorScheme();
            
            // 判斷結構類型
            var structuralType = ElementCategorizer.GetCategoryName(analysis.Element);
            Color targetColor;
            if (!colorScheme.TryGetValue(structuralType, out targetColor))
            {
                targetColor = colorScheme["預設"];
            }
            
            System.Diagnostics.Debug.WriteLine($"🎨 設定模板顏色: {structuralType} -> RGB({targetColor.Red}, {targetColor.Green}, {targetColor.Blue})");

            // 🚀 重構: 使用批量設定避免重複查詢
            var elementIds = formworkIds.ToList();
            var colors = Enumerable.Repeat(targetColor, formworkIds.Count).ToList();
            var categories = Enumerable.Repeat(structuralType, formworkIds.Count).ToList();
            
            VisualEffectsManager.SetBatchStructuralAnalysisAppearance(doc, elementIds, colors, categories);
            
            System.Diagnostics.Debug.WriteLine($"✅ 批量設定 {formworkIds.Count} 個模板的結構分析外觀完成");
        }

        /// <summary>
        /// 定義結構類型的顏色方案
        /// </summary>
        private Dictionary<string, Color> GetStructuralColorScheme()
        {
            return new Dictionary<string, Color>
            {
                // ✅ 修正：使用與 ElementCategorizer.GetCategoryName 一致的鍵
                ["結構柱"] = new Color(255, 100, 100),      // 紅色 - 柱子模板
                ["結構梁"] = new Color(100, 255, 100),      // 綠色 - 梁模板  
                ["樓板"] = new Color(100, 100, 255),        // 藍色 - 板模板
                ["結構牆"] = new Color(255, 255, 100),      // 黃色 - 牆模板
                ["基礎"] = new Color(150, 75, 0),           // 棕色 - 基礎模板
                ["樓梯"] = new Color(255, 150, 255),        // 粉紅色 - 樓梯模板
                
                // 向下兼容舊的鍵名
                ["柱"] = new Color(255, 100, 100),
                ["梁"] = new Color(100, 255, 100),
                ["板"] = new Color(100, 100, 255),
                ["牆"] = new Color(255, 255, 100),
                
                ["預設"] = new Color(128, 128, 128),        // 灰色 - 未分類
                ["未知構件"] = new Color(128, 128, 128)     // 灰色 - 未知
            };
        }

        /// <summary>
        /// 判斷結構元素的類型
        /// </summary>
        private string DetermineStructuralType(Element element)
        {
            if (element == null) return "預設";

            // 根據 Revit 內建類別判斷
            var category = element.Category;
            if (category != null)
            {
                switch (category.Id.Value)
                {
                    case (long)BuiltInCategory.OST_Columns:
                    case (long)BuiltInCategory.OST_StructuralColumns:
                        return "柱";
                        
                    case (long)BuiltInCategory.OST_StructuralFraming:
                        return "梁";
                        
                    case (long)BuiltInCategory.OST_Floors:
                    case (long)BuiltInCategory.OST_StructuralFoundation:
                        // 進一步區分板和基礎
                        if (element.Name.Contains("基礎") || element.Name.Contains("Foundation"))
                            return "基礎";
                        return "板";
                        
                    case (long)BuiltInCategory.OST_Walls:
                        return "牆";
                        
                    case (long)BuiltInCategory.OST_Stairs:
                        return "樓梯";
                }
            }

            // 根據元素名稱進行判斷（備用方案）
            var name = element.Name.ToLower();
            if (name.Contains("柱") || name.Contains("column")) return "柱";
            if (name.Contains("梁") || name.Contains("beam")) return "梁";
            if (name.Contains("板") || name.Contains("slab") || name.Contains("floor")) return "板";
            if (name.Contains("牆") || name.Contains("wall")) return "牆";
            if (name.Contains("基礎") || name.Contains("foundation")) return "基礎";
            if (name.Contains("樓梯") || name.Contains("stair")) return "樓梯";

            return "預設";
        }

        /// <summary>
        /// 設定結構類別參數
        /// </summary>
        private void SetStructuralCategoryParameter(Element element, string structuralType)
        {
            try
            {
                var categoryParam = element.LookupParameter(SharedParams.P_Category);
                if (categoryParam != null && !categoryParam.IsReadOnly)
                {
                    categoryParam.Set($"{structuralType}模板");
                    System.Diagnostics.Debug.WriteLine($"✅ 設定類別參數: {structuralType}模板");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"設定類別參數失敗: {ex.Message}");
            }
        }

        private void ShowAnalysisResults(StructuralAnalysisResult result)
        {
            // 創建更詳細的結果顯示
            ShowDetailedAnalysisDialog(result);
        }

        private void ShowDetailedAnalysisDialog(StructuralAnalysisResult result)
        {
            var mainContent = GenerateMainSummary(result);
            var expandedContent = GenerateDetailedReport(result);
            
            var dialog = new TaskDialog("BIM 結構模板準確分析結果")
            {
                MainInstruction = "模板數量分析完成",
                MainContent = mainContent,
                ExpandedContent = expandedContent,
                CommonButtons = TaskDialogCommonButtons.Ok,
                DefaultButton = TaskDialogResult.Ok,
                FooterText = "💡 提示: 點擊「顯示詳細資料」查看完整分析報告，或使用「匯出CSV」功能保存詳細數據"
            };

            // 添加額外按鈕
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "匯出詳細CSV報告", "導出包含所有計算細節的CSV檔案");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "顯示計算方法說明", "查看各類型構件的模板面積計算公式");

            var dialogResult = dialog.Show();
            
            // 處理用戶選擇
            HandleDialogResult(dialogResult, result);
        }

        private string GenerateMainSummary(StructuralAnalysisResult result)
        {
            var lines = new List<string>();
            
            lines.Add($"🏗️ 分析構件總數: {result.TotalElements} 件");
            lines.Add($"📐 模板總面積: {result.TotalFormworkArea:F2} m² ({result.TotalFormworkArea * 10.764:F1} ft²)");
            lines.Add($"🧱 混凝土總體積: {result.TotalConcreteVolume:F2} m³ ({result.TotalConcreteVolume * 35.315:F1} ft³)");
            lines.Add($"🔩 鋼筋估算重量: {result.EstimatedRebarWeight:F2} 噸");
            lines.Add("");

            // 成本估算 (假設值，可調整)
            double formworkCostPerM2 = 350; // 每平方米模板成本
            double concreteCostPerM3 = 2800; // 每立方米混凝土成本
            double rebarCostPerTon = 25000; // 每噸鋼筋成本

            double totalCost = (result.TotalFormworkArea * formworkCostPerM2) + 
                              (result.TotalConcreteVolume * concreteCostPerM3) + 
                              (result.EstimatedRebarWeight * rebarCostPerTon);

            lines.Add($"💰 估算總成本: NT$ {totalCost:N0}");
            lines.Add($"   - 模板費用: NT$ {result.TotalFormworkArea * formworkCostPerM2:N0}");
            lines.Add($"   - 混凝土費用: NT$ {result.TotalConcreteVolume * concreteCostPerM3:N0}");
            lines.Add($"   - 鋼筋費用: NT$ {result.EstimatedRebarWeight * rebarCostPerTon:N0}");

            return string.Join(Environment.NewLine, lines);
        }

        private string GenerateDetailedReport(StructuralAnalysisResult result)
        {
            var lines = new List<string>();
            
            lines.Add("=== 📊 詳細分類統計 ===");
            lines.Add("");

            foreach (var category in result.CategorySummary.OrderByDescending(c => c.Value.FormworkArea))
            {
                string categoryName = GetCategoryDisplayName(category.Key);
                var summary = category.Value;
                double avgAreaPerElement = summary.Count > 0 ? summary.FormworkArea / summary.Count : 0;
                double areaPercentage = result.TotalFormworkArea > 0 ? (summary.FormworkArea / result.TotalFormworkArea) * 100 : 0;

                lines.Add($"🔸 {categoryName}:");
                lines.Add($"   數量: {summary.Count} 件 ({GetElementCountPercentage(summary.Count, result.TotalElements):F1}%)");
                lines.Add($"   模板面積: {summary.FormworkArea:F2} m² ({areaPercentage:F1}%)");
                lines.Add($"   混凝土體積: {summary.ConcreteVolume:F2} m³");
                lines.Add($"   平均每件模板面積: {avgAreaPerElement:F2} m²");
                lines.Add($"   計算方法: {GetCalculationMethodDescription(categoryName)}");
                lines.Add("");
            }

            // 添加效率分析
            lines.Add("=== 📈 效率分析 ===");
            lines.Add($"混凝土模板比: {(result.TotalConcreteVolume > 0 ? result.TotalFormworkArea / result.TotalConcreteVolume : 0):F2} m²/m³");
            lines.Add($"平均每件模板面積: {(result.TotalElements > 0 ? result.TotalFormworkArea / result.TotalElements : 0):F2} m²");
            lines.Add("");

            // 添加品質指標
            lines.Add("=== ✅ 計算品質指標 ===");
            int successfulCalculations = result.ElementAnalyses.Count(kvp => kvp.Value.FormworkArea > 0);
            double successRate = result.TotalElements > 0 ? (double)successfulCalculations / result.TotalElements * 100 : 0;
            lines.Add($"成功計算率: {successRate:F1}% ({successfulCalculations}/{result.TotalElements})");
            lines.Add($"計算系統: Formwork_V1 準確計算引擎");
            lines.Add($"計算時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            return string.Join(Environment.NewLine, lines);
        }

        private void HandleDialogResult(TaskDialogResult dialogResult, StructuralAnalysisResult result)
        {
            switch (dialogResult)
            {
                case TaskDialogResult.CommandLink1:
                    // 觸發CSV導出
                    ShowExportMessage();
                    break;
                case TaskDialogResult.CommandLink2:
                    // 顯示計算方法說明
                    ShowCalculationMethodsDialog();
                    break;
            }
        }

        private void ShowExportMessage()
        {
            var dialog = new TaskDialog("CSV導出提示")
            {
                MainInstruction = "導出詳細分析報告",
                MainContent = "請使用 Revit 工具列上的「匯出CSV」按鈕來導出詳細的分析報告。\n\n" +
                             "導出的CSV檔案將包含：\n" +
                             "• 每個構件的詳細計算結果\n" +
                             "• 幾何參數和計算方法\n" +
                             "• 連接關係分析\n" +
                             "• 可用於進一步分析的數據",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            dialog.Show();
        }

        private void ShowCalculationMethodsDialog()
        {
            var content = "=== 🧮 模板面積計算方法說明 ===\n\n" +
                         "📏 梁 (結構梁):\n" +
                         "基本公式: 梁長 × (梁的上下模板面積)\n" +
                         "計算內容: 頂部+底部+側邊模板面積\n" +
                         "扣除項目: 與樑、柱接觸的部分\n\n" +
                         
                         "🏛️ 柱 (結構柱):\n" +
                         "基本公式: 柱高 × 柱周長\n" +
                         "扣除項目: 樓板厚度、梁斷面積、RC牆連接面\n\n" +
                         
                         "🏢 板 (樓板):\n" +
                         "基本公式: 板長 × 板寬\n" +
                         "扣除項目: 與樑、柱接觸部分、開口面積(樓梯口等)\n\n" +
                         
                         "🧱 牆 (結構牆):\n" +
                         "基本公式: 牆長 × 牆高 × 2面\n" +
                         "扣除項目: 門窗開口、與樑板接觸面積\n\n" +
                         
                         "✨ 此計算系統完全按照 BIM 建模規範實現，\n" +
                         "確保模板數量計算的準確性和實用性。";

            var dialog = new TaskDialog("計算方法說明")
            {
                MainContent = content,
                CommonButtons = TaskDialogCommonButtons.Ok,
                FooterText = "💡 這些計算公式基於實際施工經驗和 BIM 建模最佳實踐"
            };
            dialog.Show();
        }

        private string GetCategoryDisplayName(string category)
        {
            switch (category)
            {
                case "梁": return "結構梁";
                case "柱": return "結構柱";
                case "板": return "樓板";
                case "牆": return "結構牆";
                default: return category;
            }
        }

        private string GetCalculationMethodDescription(string categoryName)
        {
            switch (categoryName)
            {
                case "結構梁": return "梁長×模板面積 - 接觸扣除";
                case "結構柱": return "柱高×周長 - 板梁扣除";
                case "樓板": return "長×寬 - 接觸開口扣除";
                case "結構牆": return "長×高×2面 - 開口扣除";
                default: return "通用面積計算";
            }
        }

        private double GetElementCountPercentage(int count, int total)
        {
            return total > 0 ? (double)count / total * 100 : 0;
        }

        /// <summary>
        /// 獲取實體填充圖案ID，用於實體顏色填充
        /// </summary>
        private ElementId GetSolidFillPatternId(Document doc)
        {
            try
            {
                // 方法1: 查找實體填充圖案
                var fillPatterns = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .ToList();

                var solidPattern = fillPatterns.FirstOrDefault(fp => 
                    fp.GetFillPattern().IsSolidFill || 
                    fp.Name.Contains("Solid") || 
                    fp.Name.Contains("實體") ||
                    fp.Name == "<Solid fill>");

                if (solidPattern != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 找到實體填充圖案: {solidPattern.Name} (ID: {solidPattern.Id.Value})");
                    return solidPattern.Id;
                }

                // 方法2: 使用第一個可用的填充圖案
                System.Diagnostics.Debug.WriteLine($"⚠️ 未找到實體填充圖案，使用第一個可用圖案");
                var firstPattern = fillPatterns.FirstOrDefault();
                if (firstPattern != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 使用填充圖案: {firstPattern.Name} (ID: {firstPattern.Id.Value})");
                    return firstPattern.Id;
                }

                // 方法3: 返回無效ID
                System.Diagnostics.Debug.WriteLine($"⚠️ 無可用填充圖案，返回無效ID");
                return ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 獲取實體填充圖案失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 批量設定模板的宿主ID參數
        /// </summary>
        private void SetHostIdParametersForFormwork(Document doc, IList<ElementId> formworkIds, Element hostElement)
        {
            System.Diagnostics.Debug.WriteLine($"🔧 開始為 {formworkIds.Count} 個模板設定宿主ID: {hostElement.Id.Value}");
            
            int successCount = 0;
            foreach (var id in formworkIds)
            {
                try
                {
                    var element = doc.GetElement(id);
                    if (element is DirectShape ds)
                    {
                        SetHostIdParameter(ds, hostElement);
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 設定元素 {id.Value} 宿主ID失敗: {ex.Message}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ 成功設定 {successCount}/{formworkIds.Count} 個元素的宿主ID");
        }

        /// <summary>
        /// 設定模板實體的面積參數（改進版 - 直接使用 hostElement）
        /// </summary>
        private void SetFormworkAreaParameters(Document doc, IList<ElementId> formworkIds, Element hostElement)
        {
            System.Diagnostics.Debug.WriteLine($"📐 開始為 {formworkIds.Count} 個模板計算面積（宿主: {hostElement.Category?.Name ?? "未知"} ID:{hostElement.Id.Value}）");
            
            int successCount = 0;
            double totalCalculatedArea = 0;
            
            foreach (var id in formworkIds)
            {
                try
                {
                    var element = doc.GetElement(id);
                    if (element is DirectShape ds)
                    {
                        System.Diagnostics.Debug.WriteLine($"📊 處理元素 {id.Value}");
                        
                        // ✅ 直接傳入宿主元素進行面積計算（含接觸面扣除）
                        double areaM2 = CalculateDirectShapeArea(ds, hostElement);
                        
                        if (areaM2 > 0)
                        {
                            // 設定面積相關的共用參數
                            SetAreaToSharedParameters(ds, areaM2);
                            
                            successCount++;
                            totalCalculatedArea += areaM2;
                            
                            System.Diagnostics.Debug.WriteLine($"✅ 元素 {id.Value} 面積參數設定完成: {areaM2:F2} m²");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ 元素 {id.Value} 面積計算為0，跳過參數設定");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 設定元素 {id.Value} 面積參數失敗: {ex.Message}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"📊 面積計算完成: 成功 {successCount}/{formworkIds.Count} 個，總面積 {totalCalculatedArea:F2} m²");
        }

        /// <summary>
        /// 計算DirectShape的面積 - 包含智能扣除邏輯（改進版 - 直接使用宿主元素）
        /// </summary>
        private double CalculateDirectShapeArea(DirectShape directShape, Element hostElement)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 開始計算 DirectShape {directShape.Id.Value} 的面積（宿主ID: {hostElement.Id.Value}）");
                
                var geometry = directShape.get_Geometry(new Options());
                double totalSurfaceArea = 0;
                int faceCount = 0;

                foreach (GeometryObject geomObj in geometry)
                {
                    if (geomObj is Solid solid && solid.Volume > 0)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            totalSurfaceArea += face.Area;
                            faceCount++;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"  ├─ 找到 {faceCount} 個面，總表面積 = {totalSurfaceArea:F6} sq ft");

                // 使用 AreaCalculator 轉換為平方米
                double baseSurfaceAreaM2 = AreaCalculator.ConvertToSquareMeters(totalSurfaceArea);
                System.Diagnostics.Debug.WriteLine($"  ├─ 轉換為平方米 = {baseSurfaceAreaM2:F6} m²");
                
                // ✅ 直接使用傳入的 hostElement 計算精確面積（含接觸面扣除）
                System.Diagnostics.Debug.WriteLine($"  ├─ 宿主元素: {hostElement.Category?.Name ?? "未知"} (ID: {hostElement.Id.Value})");
                double accurateArea = CalculateAccurateFormworkAreaWithDeduction(hostElement, baseSurfaceAreaM2);
                
                System.Diagnostics.Debug.WriteLine($"  └─ ✅ 最終面積（含扣除）= {accurateArea:F6} m²");
                return accurateArea;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算DirectShape面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 計算精確的模板面積（包含接觸面扣除邏輯）
        /// </summary>
        private double CalculateAccurateFormworkAreaWithDeduction(Element hostElement, double baseSurfaceArea)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 開始計算接觸面扣除（主件ID: {hostElement.Id.Value}）");
                
                // 🚀 重構: 使用 AreaCalculator 和整合的工具類別
                
                // 1. 取得宿主元素的實體幾何
                var hostSolids = GeometryExtractor.GetElementSolids(hostElement);
                if (hostSolids.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"  └─ ⚠️ 無法取得宿主元素幾何，返回基本面積");
                    return baseSurfaceArea;
                }
                
                var hostSolid = GeometryExtractor.GetLargestSolid(hostSolids);
                if (hostSolid == null)
                {
                    System.Diagnostics.Debug.WriteLine($"  └─ ⚠️ 無法取得有效實體，返回基本面積");
                    return baseSurfaceArea;
                }
                
                System.Diagnostics.Debug.WriteLine($"  ├─ 宿主實體體積: {hostSolid.Volume:F2} cu ft");
                
                // 2. 收集鄰近的結構元素
                var nearbyElements = FindNearbyStructuralElements(hostElement);
                System.Diagnostics.Debug.WriteLine($"  ├─ 找到 {nearbyElements.Count} 個鄰近結構元素");
                
                if (nearbyElements.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"  └─ 無鄰近元素，返回基本面積");
                    return baseSurfaceArea;
                }
                
                // 列出鄰近元素
                foreach (var nearby in nearbyElements)
                {
                    var categoryName = ElementCategorizer.GetCategoryName(nearby);
                    System.Diagnostics.Debug.WriteLine($"  │  ├─ {categoryName} (ID: {nearby.Id.Value})");
                }
                
                // 3. 使用 AreaCalculator 計算接觸面扣除
                System.Diagnostics.Debug.WriteLine($"  ├─ 開始計算接觸面積扣除...");
                double contactDeductionSqFt = AreaCalculator.CalculateContactDeductionArea(hostSolid, nearbyElements, tolerance: 0.001);
                double contactDeductionM2 = AreaCalculator.ConvertToSquareMeters(contactDeductionSqFt);
                
                System.Diagnostics.Debug.WriteLine($"  ├─ 接觸面扣除: {contactDeductionSqFt:F6} sq ft = {contactDeductionM2:F6} m²");
                
                // 4. 計算最終面積
                double finalArea = Math.Max(0, baseSurfaceArea - contactDeductionM2);
                
                double deductionPercentage = baseSurfaceArea > 0 ? (contactDeductionM2 / baseSurfaceArea * 100) : 0;
                string category = ElementCategorizer.GetCategoryName(hostElement);
                
                System.Diagnostics.Debug.WriteLine($"  └─ ✅ {category} 面積計算完成:");
                System.Diagnostics.Debug.WriteLine($"      ├─ 基本面積: {baseSurfaceArea:F3} m²");
                System.Diagnostics.Debug.WriteLine($"      ├─ 扣除面積: {contactDeductionM2:F3} m² ({deductionPercentage:F1}%)");
                System.Diagnostics.Debug.WriteLine($"      └─ 最終面積: {finalArea:F3} m²");
                
                return finalArea;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 精確面積計算失敗: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   堆疊追蹤: {ex.StackTrace}");
                return baseSurfaceArea; // 發生錯誤時返回基本面積
            }
        }

        /// <summary>
        /// 取得結構元素類別（已棄用，使用 ElementCategorizer.GetCategoryName）
        /// </summary>
        [Obsolete("使用 ElementCategorizer.GetCategoryName 代替")]
        private string GetStructuralElementCategory(Element element)
        {
            if (element.Category?.Name != null)
            {
                return element.Category.Name;
            }
            
            // 備用方法：根據參數判斷
            if (element.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH) != null)
            {
                return element is FamilyInstance ? "結構柱" : "結構梁";
            }
            
            return "未知構件";
        }

        /// <summary>
        /// 找出鄰近的結構元素（改進版 - 增強 Debug 輸出）
        /// </summary>
        private List<Element> FindNearbyStructuralElements(Element hostElement)
        {
            var nearbyElements = new List<Element>();
            
            try
            {
                var doc = hostElement.Document;
                var structuralCategories = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Walls
                };

                // 取得宿主元素的邊界框
                var hostBBox = hostElement.get_BoundingBox(null);
                if (hostBBox == null) return nearbyElements;

                // 🎯 擴大搜索範圍: 改為 5 英尺（約 1.5 公尺）
                double expandDistance = 5.0; // 從 3.0 增加到 5.0
                var expandedBBox = new BoundingBoxXYZ
                {
                    Min = hostBBox.Min - new XYZ(expandDistance, expandDistance, expandDistance),
                    Max = hostBBox.Max + new XYZ(expandDistance, expandDistance, expandDistance)
                };
                
                System.Diagnostics.Debug.WriteLine($"  ├─ 🔍 搜索範圍: 擴大 {expandDistance} ft (約 {expandDistance * 0.3048:F2} m)");
                
                // 計算宿主元素中心（用於距離計算）
                XYZ hostCenter = (hostBBox.Min + hostBBox.Max) / 2;
                
                int totalChecked = 0;
                int foundCount = 0;

                // 在每個類別中搜索
                foreach (var category in structuralCategories)
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType();

                    foreach (Element element in collector)
                    {
                        totalChecked++;
                        
                        if (element.Id == hostElement.Id) continue; // 跳過自己

                        var elemBBox = element.get_BoundingBox(null);
                        if (elemBBox != null && BoundingBoxesOverlap(expandedBBox, elemBBox))
                        {
                            foundCount++;
                            nearbyElements.Add(element);
                            
                            // 計算距離
                            XYZ elementCenter = (elemBBox.Min + elemBBox.Max) / 2;
                            double distance = hostCenter.DistanceTo(elementCenter);
                            
                            var categoryName = ElementCategorizer.GetCategoryName(element);
                            System.Diagnostics.Debug.WriteLine($"  │  ├─ 找到: {categoryName} (ID: {element.Id.Value}, 距離: {distance:F2} ft = {distance * 0.3048:F2} m)");
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"  ├─ 搜索統計: 檢查 {totalChecked} 個元素，發現 {foundCount} 個鄰近元素");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"  └─ ❌ 搜索鄰近元素失敗: {ex.Message}");
            }

            return nearbyElements;
        }

        /// <summary>
        /// 檢查兩個邊界框是否重疊
        /// </summary>
        private bool BoundingBoxesOverlap(BoundingBoxXYZ box1, BoundingBoxXYZ box2)
        {
            return box1.Min.X <= box2.Max.X && box1.Max.X >= box2.Min.X &&
                   box1.Min.Y <= box2.Max.Y && box1.Max.Y >= box2.Min.Y &&
                   box1.Min.Z <= box2.Max.Z && box1.Max.Z >= box2.Min.Z;
        }

        /// <summary>
        /// 計算接觸面積扣除量（已棄用，使用 AreaCalculator.CalculateContactDeductionArea）
        /// </summary>
        [Obsolete("使用 AreaCalculator.CalculateContactDeductionArea 代替")]
        private double CalculateContactDeduction(Element hostElement, List<Element> nearbyElements)
        {
            double totalDeduction = 0;
            string hostCategory = GetStructuralElementCategory(hostElement);

            try
            {
                foreach (var nearbyElement in nearbyElements)
                {
                    string nearbyCategory = GetStructuralElementCategory(nearbyElement);
                    
                    // 根據不同的接觸組合計算扣除量
                    double deduction = CalculateContactDeductionBetweenElements(hostElement, nearbyElement, hostCategory, nearbyCategory);
                    totalDeduction += deduction;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算接觸扣除失敗: {ex.Message}");
            }

            return totalDeduction;
        }

        /// <summary>
        /// 計算兩個元素間的接觸扣除量（已棄用，使用 AreaCalculator.CalculateContactArea）
        /// </summary>
        [Obsolete("使用 AreaCalculator.CalculateContactArea 代替")]
        private double CalculateContactDeductionBetweenElements(Element element1, Element element2, string category1, string category2)
        {
            try
            {
                // 簡化的接觸面積估算
                double estimatedContactArea = EstimateContactAreaBetweenElements(element1, element2);
                
                if (estimatedContactArea < 0.01) return 0; // 接觸面積太小，忽略

                // 根據接觸類型決定扣除率
                double deductionRate = GetContactDeductionRate(category1, category2);
                
                double deduction = estimatedContactArea * deductionRate;
                
                System.Diagnostics.Debug.WriteLine($"📊 接觸扣除: {category1}-{category2}, 接觸面積={estimatedContactArea:F3}m², 扣除率={deductionRate:F1}%, 扣除量={deduction:F3}m²");
                
                return deduction;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算元素間接觸扣除失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 估算兩個元素間的接觸面積
        /// </summary>
        private double EstimateContactAreaBetweenElements(Element element1, Element element2)
        {
            try
            {
                var bbox1 = element1.get_BoundingBox(null);
                var bbox2 = element2.get_BoundingBox(null);
                
                if (bbox1 == null || bbox2 == null) return 0;

                // 計算重疊體積
                var overlapMin = new XYZ(
                    Math.Max(bbox1.Min.X, bbox2.Min.X),
                    Math.Max(bbox1.Min.Y, bbox2.Min.Y),
                    Math.Max(bbox1.Min.Z, bbox2.Min.Z)
                );

                var overlapMax = new XYZ(
                    Math.Min(bbox1.Max.X, bbox2.Max.X),
                    Math.Min(bbox1.Max.Y, bbox2.Max.Y),
                    Math.Min(bbox1.Max.Z, bbox2.Max.Z)
                );

                if (overlapMin.X >= overlapMax.X || overlapMin.Y >= overlapMax.Y || overlapMin.Z >= overlapMax.Z)
                    return 0; // 沒有重疊

                // 簡化估算：取重疊區域的最大面
                double overlapLengthX = overlapMax.X - overlapMin.X;
                double overlapLengthY = overlapMax.Y - overlapMin.Y;
                double overlapLengthZ = overlapMax.Z - overlapMin.Z;

                // 取最大的兩個維度作為接觸面積
                double[] dimensions = { overlapLengthX, overlapLengthY, overlapLengthZ };
                Array.Sort(dimensions);
                
                double contactAreaSqFt = dimensions[1] * dimensions[2]; // 取最大的兩個維度
                return UnitUtils.ConvertFromInternalUnits(contactAreaSqFt, UnitTypeId.SquareMeters);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 估算接觸面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 取得接觸扣除率
        /// </summary>
        private double GetContactDeductionRate(string category1, string category2)
        {
            // 柱-梁接觸：高扣除率
            if ((category1 == "結構柱" && category2 == "結構梁") || 
                (category1 == "結構梁" && category2 == "結構柱"))
                return 0.9; // 90%

            // 柱-板接觸：中等扣除率
            if ((category1 == "結構柱" && category2 == "樓板") || 
                (category1 == "樓板" && category2 == "結構柱"))
                return 0.6; // 60%

            // 梁-板接觸：中等扣除率
            if ((category1 == "結構梁" && category2 == "樓板") || 
                (category1 == "樓板" && category2 == "結構梁"))
                return 0.7; // 70%

            // 牆相關接觸：較低扣除率
            if (category1 == "結構牆" || category2 == "結構牆")
                return 0.5; // 50%

            // 其他接觸：保守扣除率
            return 0.3; // 30%
        }

        /// <summary>
        /// 計算理論模板面積
        /// </summary>
        private double CalculateTheoreticalFormworkArea(Element element, string category)
        {
            try
            {
                switch (category)
                {
                    case "結構柱":
                        return CalculateColumnTheoreticalArea(element);
                        
                    case "結構梁":
                        return CalculateBeamTheoreticalArea(element);
                        
                    case "樓板":
                        return CalculateSlabTheoreticalArea(element);
                        
                    case "結構牆":
                        return CalculateWallTheoreticalArea(element);
                        
                    default:
                        return 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算理論模板面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 計算柱理論模板面積
        /// </summary>
        private double CalculateColumnTheoreticalArea(Element column)
        {
            try
            {
                var widthParam = column.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
                var depthParam = column.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
                var heightParam = column.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);

                if (widthParam == null || depthParam == null) return 0;

                double width = UnitUtils.ConvertFromInternalUnits(widthParam.AsDouble(), UnitTypeId.Meters);
                double depth = UnitUtils.ConvertFromInternalUnits(depthParam.AsDouble(), UnitTypeId.Meters);
                
                // 如果沒有高度參數，嘗試從幾何體積計算
                double height = 0;
                if (heightParam != null)
                {
                    height = UnitUtils.ConvertFromInternalUnits(heightParam.AsDouble(), UnitTypeId.Meters);
                }
                else
                {
                    // 從幾何計算高度
                    var bbox = column.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        height = UnitUtils.ConvertFromInternalUnits(bbox.Max.Z - bbox.Min.Z, UnitTypeId.Meters);
                    }
                }

                if (height <= 0) return 0;

                // 基本模板面積 = 周長 × 高度
                double perimeter = 2 * (width + depth);
                return perimeter * height;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算柱理論面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 計算梁理論模板面積
        /// </summary>
        private double CalculateBeamTheoreticalArea(Element beam)
        {
            try
            {
                var widthParam = beam.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
                var depthParam = beam.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
                var lengthParam = beam.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);

                if (widthParam == null || depthParam == null || lengthParam == null) return 0;

                double width = UnitUtils.ConvertFromInternalUnits(widthParam.AsDouble(), UnitTypeId.Meters);
                double depth = UnitUtils.ConvertFromInternalUnits(depthParam.AsDouble(), UnitTypeId.Meters);
                double length = UnitUtils.ConvertFromInternalUnits(lengthParam.AsDouble(), UnitTypeId.Meters);

                // 基本模板面積 = 底面 + 兩側面 (通常不包含頂面)
                return (width * length) + (2 * depth * length);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算梁理論面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 計算板理論模板面積
        /// </summary>
        private double CalculateSlabTheoreticalArea(Element slab)
        {
            try
            {
                // 從幾何體積計算板面積
                var geometry = slab.get_Geometry(new Options());
                double maxHorizontalArea = 0;

                foreach (GeometryObject geomObj in geometry)
                {
                    if (geomObj is Solid solid && solid.Volume > 0)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            var normal = face.ComputeNormal(UV.Zero);
                            if (Math.Abs(normal.Z) > 0.9) // 水平面
                            {
                                if (face.Area > maxHorizontalArea)
                                    maxHorizontalArea = face.Area;
                            }
                        }
                    }
                }

                return UnitUtils.ConvertFromInternalUnits(maxHorizontalArea, UnitTypeId.SquareMeters);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算板理論面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 計算牆理論模板面積
        /// </summary>
        private double CalculateWallTheoreticalArea(Element wall)
        {
            try
            {
                var lengthParam = wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                var heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                
                if (lengthParam == null || heightParam == null) return 0;

                double length = UnitUtils.ConvertFromInternalUnits(lengthParam.AsDouble(), UnitTypeId.Meters);
                double height = UnitUtils.ConvertFromInternalUnits(heightParam.AsDouble(), UnitTypeId.Meters);

                // 基本模板面積 = 兩面
                return 2 * length * height;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 計算牆理論面積失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 設定面積到共用參數
        /// </summary>
        private void SetAreaToSharedParameters(DirectShape element, double areaM2)
        {
            try
            {
                // 🚀 重構: 使用 AreaCalculator 的單位轉換方法
                double areaInSquareFeet = AreaCalculator.ConvertToSquareFeet(areaM2);

                // 設定有效面積參數
                var effectiveAreaParam = element.LookupParameter(SharedParams.P_EffectiveArea);
                if (effectiveAreaParam != null && !effectiveAreaParam.IsReadOnly)
                {
                    effectiveAreaParam.Set(areaInSquareFeet);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定有效面積參數: {areaM2:F2} m²");
                }

                // 設定總面積參數
                var totalParam = element.LookupParameter(SharedParams.P_Total);
                if (totalParam != null && !totalParam.IsReadOnly)
                {
                    totalParam.Set(areaInSquareFeet);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定總面積參數: {areaM2:F2} m²");
                }

                // 設定分析時間
                var timeParam = element.LookupParameter(SharedParams.P_AnalysisTime);
                if (timeParam != null && !timeParam.IsReadOnly)
                {
                    var currentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                    timeParam.Set(currentTime);
                    System.Diagnostics.Debug.WriteLine($"✅ 設定分析時間: {currentTime}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 設定共用參數失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定宿主元素ID參數
        /// </summary>
        private void SetHostIdParameter(DirectShape element, Element hostElement)
        {
            try
            {
                var hostIdParam = element.LookupParameter(SharedParams.P_HostId);
                if (hostIdParam != null && !hostIdParam.IsReadOnly)
                {
                    // ✅ 修正：根據參數類型使用正確的設定方法
                    if (hostIdParam.StorageType == StorageType.Integer)
                    {
                        hostIdParam.Set((int)hostElement.Id.Value);
                        System.Diagnostics.Debug.WriteLine($"✅ 設定宿主ID參數(整數): {hostElement.Id.Value}");
                    }
                    else if (hostIdParam.StorageType == StorageType.String)
                    {
                        hostIdParam.Set(hostElement.Id.Value.ToString());
                        System.Diagnostics.Debug.WriteLine($"✅ 設定宿主ID參數(字串): {hostElement.Id.Value}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ 不支援的參數類型: {hostIdParam.StorageType}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 無法找到或參數唯讀: {SharedParams.P_HostId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 設定宿主ID參數失敗: {ex.Message}");
            }
        }
    }
}