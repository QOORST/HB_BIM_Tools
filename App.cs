// Application.cs (每個工具專案中)
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media.Imaging;

namespace YD_RevitTools.LicenseManager
{
    public class App : IExternalApplication
    {
        private const string TAB_NAME = "YD_BIM Tools";
        private const string PANEL_AR = "AR";
        private const string PANEL_MEP = "MEP";
        private const string PANEL_FAMILY = "Family";
        private const string PANEL_DATA = "Data";
        private const string PANEL_ABOUT = "About";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 註冊編碼提供者（修復 GB18030 編碼錯誤）
                // 這對於 EPPlus 處理某些 Excel 檔案是必要的
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                }
                catch (Exception ex)
                {
                    // 如果註冊失敗，記錄但不中斷啟動
                    System.Diagnostics.Debug.WriteLine($"編碼提供者註冊失敗: {ex.Message}");
                }

                // 驗證授權
                var validationResult = LicenseManager.Instance.ValidateLicense();

                if (!validationResult.IsValid)
                {
                    TaskDialog td = new TaskDialog("授權提醒");
                    td.MainInstruction = "授權未啟用或已過期";
                    td.MainContent = $"{validationResult.Message}\n\n請點擊「授權管理」按鈕進行授權設定。";
                    td.CommonButtons = TaskDialogCommonButtons.Ok;
                    td.Show();
                }
                else if (validationResult.DaysUntilExpiry <= 30 && validationResult.DaysUntilExpiry > 0)
                {
                    // 授權即將到期提醒
                    TaskDialog td = new TaskDialog("授權提醒");
                    td.MainInstruction = "授權即將到期";
                    td.MainContent = $"您的授權將在 {validationResult.DaysUntilExpiry} 天後到期。\n\n" +
                                    $"到期日期：{validationResult.LicenseInfo.ExpiryDate:yyyy-MM-dd}\n\n" +
                                    "請及時聯繫技術支援進行續約。";
                    td.CommonButtons = TaskDialogCommonButtons.Ok;
                    td.Show();
                }

                // 創建或獲取 Ribbon Tab
                CreateRibbonTab(application);

                // 創建五大類別的 Ribbon Panel
                RibbonPanel arPanel = GetOrCreateRibbonPanel(application, PANEL_AR);
                RibbonPanel mepPanel = GetOrCreateRibbonPanel(application, PANEL_MEP);
                RibbonPanel familyPanel = GetOrCreateRibbonPanel(application, PANEL_FAMILY);
                RibbonPanel dataPanel = GetOrCreateRibbonPanel(application, PANEL_DATA);
                RibbonPanel aboutPanel = GetOrCreateRibbonPanel(application, PANEL_ABOUT);

                // === AR 面板 ===
                AddARToolButtons(arPanel);

                // === MEP 面板 ===
                AddMEPToolButtons(mepPanel);

                // === Family 面板 ===
                AddFamilyToolButtons(familyPanel);

                // === 資料 面板 ===
                AddDataToolButtons(dataPanel);

                // === 關於 面板 ===
                // 添加授權管理按鈕
                if (!HasButton(aboutPanel, "LicenseManagement"))
                {
                    AddLicenseManagementButton(aboutPanel);
                }
                // 添加其他關於資訊按鈕
                AddAboutButtons(aboutPanel);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"工具載入失敗：{ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        private void CreateRibbonTab(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TAB_NAME);
            }
            catch
            {
                // Tab 已存在，忽略錯誤
            }
        }

        private RibbonPanel GetOrCreateRibbonPanel(UIControlledApplication application, string panelName)
        {
            // 檢查 Panel 是否已存在
            foreach (RibbonPanel panel in application.GetRibbonPanels(TAB_NAME))
            {
                if (panel.Name == panelName)
                {
                    return panel;
                }
            }

            // Panel 不存在，創建新的
            return application.CreateRibbonPanel(TAB_NAME, panelName);
        }

        private bool HasButton(RibbonPanel panel, string buttonName)
        {
            foreach (RibbonItem item in panel.GetItems())
            {
                if (item.Name == buttonName)
                    return true;
            }
            return false;
        }

        private void AddLicenseManagementButton(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            PushButtonData buttonData = new PushButtonData(
                "LicenseManagement",
                "授權\n管理",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.LicenseManagementCommand");

            buttonData.ToolTip = "管理 YD BIM 工具授權";
            buttonData.LongDescription = "開啟授權管理視窗，查看授權狀態、啟用新授權或更新現有授權。";

            // 設定圖示
            SetButtonIcon(buttonData, "license");

            PushButton button = panel.AddItem(buttonData) as PushButton;
        }

        private void AddARToolButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // === 模板工具組 (Pulldown Button) ===
            PulldownButtonData formworkPulldownData = new PulldownButtonData("FormworkTools", "模板\n工具");
            formworkPulldownData.ToolTip = "模板工具組";
            formworkPulldownData.LongDescription = "建築模板相關工具集合";
            SetButtonIcon(formworkPulldownData, "formwork");

            PulldownButton formworkPulldown = panel.AddItem(formworkPulldownData) as PulldownButton;

            // 模板生成
            PushButtonData formworkGenerateData = new PushButtonData(
                "FormworkGenerate",
                "模板生成",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.CmdMain");
            formworkGenerateData.ToolTip = "模板生成工具";
            formworkGenerateData.LongDescription = "自動生成建築模板系統 (Trial+)";
            SetButtonIcon(formworkGenerateData, "formwork");
            formworkPulldown.AddPushButton(formworkGenerateData);

            // 刪除模板
            PushButtonData formworkDeleteData = new PushButtonData(
                "FormworkDelete",
                "刪除模板",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.CmdDelete");
            formworkDeleteData.ToolTip = "刪除模板工具";
            formworkDeleteData.LongDescription = "刪除已生成的模板 (Trial+)";
            SetButtonIcon(formworkDeleteData, "formwork_delete");
            formworkPulldown.AddPushButton(formworkDeleteData);

            // 面選模板
            PushButtonData formworkPickFaceData = new PushButtonData(
                "FormworkPickFace",
                "面選模板",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.CmdPickFace");
            formworkPickFaceData.ToolTip = "面選模板工具";
            formworkPickFaceData.LongDescription = "透過選擇面來生成模板 (Standard+)";
            SetButtonIcon(formworkPickFaceData, "formwork_pick");
            formworkPulldown.AddPushButton(formworkPickFaceData);

            // 匯出CSV
            PushButtonData formworkExportCsvData = new PushButtonData(
                "FormworkExportCsv",
                "匯出CSV",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.CmdExportCsv");
            formworkExportCsvData.ToolTip = "匯出CSV工具";
            formworkExportCsvData.LongDescription = "匯出模板數量到CSV檔案 (Standard+)";
            SetButtonIcon(formworkExportCsvData, "export_csv");
            formworkPulldown.AddPushButton(formworkExportCsvData);

            // 結構分析
            PushButtonData structuralAnalysisData = new PushButtonData(
                "StructuralAnalysis",
                "結構分析",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.CmdStructuralAnalysis");
            structuralAnalysisData.ToolTip = "結構分析工具";
            structuralAnalysisData.LongDescription = "分析結構並計算模板需求 (Professional)";
            SetButtonIcon(structuralAnalysisData, "structural_analysis");
            formworkPulldown.AddPushButton(structuralAnalysisData);

            // === 裝修工具組 (Pulldown Button) ===
            PulldownButtonData finishingsPulldownData = new PulldownButtonData("FinishingsTools", "裝修\n工具");
            finishingsPulldownData.ToolTip = "裝修工具組";
            finishingsPulldownData.LongDescription = "建築裝修相關工具集合";
            SetButtonIcon(finishingsPulldownData, "finishings");

            PulldownButton finishingsPulldown = panel.AddItem(finishingsPulldownData) as PulldownButton;

            // 裝修生成
            PushButtonData finishingsGenerateData = new PushButtonData(
                "FinishingsGenerate",
                "裝修生成",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Finishings.CmdFinishings");
            finishingsGenerateData.ToolTip = "裝修生成工具";
            finishingsGenerateData.LongDescription = "自動生成房間裝修（地板、天花板、牆面、踢腳板）";
            SetButtonIcon(finishingsGenerateData, "finishings");
            finishingsPulldown.AddPushButton(finishingsGenerateData);

            // === 接合工具組 (Pulldown Button) ===
            PulldownButtonData joinPulldownData = new PulldownButtonData("JoinTools", "接合\n工具");
            joinPulldownData.ToolTip = "接合工具組";
            joinPulldownData.LongDescription = "結構元素自動接合工具集合";
            SetButtonIcon(joinPulldownData, "auto_join");

            PulldownButton joinPulldown = panel.AddItem(joinPulldownData) as PulldownButton;

            // 自動接合
            PushButtonData autoJoinData = new PushButtonData(
                "AutoJoin",
                "自動接合",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.AutoJoin.CmdAutoJoin");
            autoJoinData.ToolTip = "自動結構接合工具";
            autoJoinData.LongDescription = "自動檢測並接合結構元素（柱、梁、牆、樓板）";
            SetButtonIcon(autoJoinData, "auto_join");
            joinPulldown.AddPushButton(autoJoinData);

            // 接合到選取
            PushButtonData joinToPickedData = new PushButtonData(
                "JoinToPicked",
                "接合到選取",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.AutoJoin.CmdJoinToPicked");
            joinToPickedData.ToolTip = "接合所有相交元素到選取的目標";
            joinToPickedData.LongDescription = "選取一個目標元素，自動接合所有與其相交的結構元素";
            SetButtonIcon(joinToPickedData, "auto_join");
            joinPulldown.AddPushButton(joinToPickedData);
        }

        private void AddMEPToolButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // === 管線套管工具 ===
            if (!HasButton(panel, "PipeSleeve"))
            {
                PushButtonData pipeSleeveData = new PushButtonData(
                    "PipeSleeve",
                    "Pipe\nSleeve",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdPipeSleeve");

                pipeSleeveData.ToolTip = "Pipe Sleeve Tool";
                pipeSleeveData.LongDescription = "Automatically place sleeves for pipes passing through walls and floors/beams.\n\n" +
                    "Features:\n" +
                    "• Auto-detect pipe intersections with walls and structures\n" +
                    "• One-click batch placement\n" +
                    "• Auto-numbering and distance measurement";

                SetButtonIcon(pipeSleeveData, "pipe_sleeve");

                panel.AddItem(pipeSleeveData);
            }

            // === 管線避讓工具 ===
            if (!HasButton(panel, "AutoAvoid"))
            {
                PushButtonData autoAvoidData = new PushButtonData(
                    "AutoAvoid",
                    "管線\n避讓",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdAutoAvoid");

                autoAvoidData.ToolTip = "管線避讓工具";
                autoAvoidData.LongDescription = "自動避讓管線與障礙物衝突\n\n" +
                    "功能特色：\n" +
                    "• 選擇管線和避讓範圍\n" +
                    "• 自動生成翻彎路徑\n" +
                    "• 支援 Pipe、Duct、Conduit\n" +
                    "• 可自訂彎角和偏移量\n\n" +
                    "授權要求：Trial+";

                SetButtonIcon(autoAvoidData, "auto_avoid");

                panel.AddItem(autoAvoidData);
            }

            // === 管線轉 ISO 圖工具 ===
            if (!HasButton(panel, "PipeToISO"))
            {
                PushButtonData pipeToISOData = new PushButtonData(
                    "PipeToISO",
                    "管線轉\nISO圖",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdPipeToISO");

                pipeToISOData.ToolTip = "管線轉 ISO 圖工具";
                pipeToISOData.LongDescription = "將 Revit 管線系統轉換為標準 ISO 等角圖與 PCF 檔案\n\n" +
                    "功能特色：\n" +
                    "• 選擇管線系統生成 ISO 圖\n" +
                    "• 自動建立等角視圖\n" +
                    "• 匯出 PCF 檔案（管線加工標準格式）\n" +
                    "• 生成 BOM 明細表\n" +
                    "• 支援管件標註與尺寸標記\n\n" +
                    "授權要求：Trial+";

                SetButtonIcon(pipeToISOData, "pipe_sleeve");  // 暫時使用 pipe_sleeve 圖示

                panel.AddItem(pipeToISOData);
            }
        }

        private void AddFamilyToolButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // === 族參數滑桿 ===
            if (!HasButton(panel, "FamilyParameterSlider"))
            {
                PushButtonData familySliderData = new PushButtonData(
                    "FamilyParameterSlider",
                    "Family\nSlider",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.Family.CmdFamilyParameterSlider");

                familySliderData.ToolTip = "Family Parameter Slider";
                familySliderData.LongDescription = "Adjust family parameters using sliders in real-time";
                SetButtonIcon(familySliderData, "family_slider");

                panel.AddItem(familySliderData);
            }

            // === 專案參數滑桿 ===
            if (!HasButton(panel, "ProjectParameterSlider"))
            {
                PushButtonData projectSliderData = new PushButtonData(
                    "ProjectParameterSlider",
                    "Project\nSlider",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.Family.CmdProjectParameterSlider");

                projectSliderData.ToolTip = "Project Parameter Slider";
                projectSliderData.LongDescription = "Adjust project parameters using sliders";
                SetButtonIcon(projectSliderData, "project_slider");

                panel.AddItem(projectSliderData);
            }
        }

        private void AddDataToolButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // COBie 欄位管理按鈕
            PushButtonData cobieFieldManagerData = new PushButtonData(
                "CobieFieldManager",
                "欄位\n管理",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieFieldManager");
            cobieFieldManagerData.ToolTip = "COBie 欄位管理";
            cobieFieldManagerData.LongDescription = "管理 COBie 欄位和參數 (Trial+)";
            SetButtonIcon(cobieFieldManagerData, "cobie_field");
            panel.AddItem(cobieFieldManagerData);

            // COBie 範本匯出按鈕
            PushButtonData cobieExportTemplateData = new PushButtonData(
                "CobieExportTemplate",
                "範本\n匯出",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieExportTemplate");
            cobieExportTemplateData.ToolTip = "COBie 範本匯出";
            cobieExportTemplateData.LongDescription = "匯出 COBie 範本 (Trial+)";
            SetButtonIcon(cobieExportTemplateData, "cobie_template");
            panel.AddItem(cobieExportTemplateData);

            // COBie 匯出按鈕
            PushButtonData cobieExportData = new PushButtonData(
                "CobieExport",
                "COBie\n匯出",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieExportEnhanced");
            cobieExportData.ToolTip = "COBie 匯出";
            cobieExportData.LongDescription = "匯出 COBie 資料 (Standard+)";
            SetButtonIcon(cobieExportData, "cobie_export");
            panel.AddItem(cobieExportData);

            // COBie 匯入按鈕
            PushButtonData cobieImportData = new PushButtonData(
                "CobieImport",
                "COBie\n匯入",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieImportEnhanced");
            cobieImportData.ToolTip = "COBie 匯入";
            cobieImportData.LongDescription = "匯入 COBie 資料 (Standard+)";
            SetButtonIcon(cobieImportData, "cobie_import");
            panel.AddItem(cobieImportData);
        }

        private void AddAboutButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // 關於按鈕
            if (!HasButton(panel, "About"))
            {
                PushButtonData aboutData = new PushButtonData(
                    "About",
                    "關於\nYD BIM",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.AboutCommand");

                aboutData.ToolTip = "關於 YD BIM 工具";
                aboutData.LongDescription = "查看 YD BIM 工具的版本資訊和說明";

                // 設定圖示
                SetButtonIcon(aboutData, "about");

                panel.AddItem(aboutData);
            }

            // 檢查更新按鈕
            if (!HasButton(panel, "CheckUpdate"))
            {
                PushButtonData updateData = new PushButtonData(
                    "CheckUpdate",
                    "檢查\n更新",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.CheckUpdateCommand");

                updateData.ToolTip = "檢查更新";
                updateData.LongDescription = "檢查是否有新版本可用，並自動下載安裝更新。\n\n" +
                    "功能特色：\n" +
                    "• 自動檢查最新版本\n" +
                    "• 一鍵下載並安裝\n" +
                    "• 無需手動下載安裝程式\n" +
                    "• 查看更新內容和發布日期";

                // 設定圖示
                SetButtonIcon(updateData, "update");

                panel.AddItem(updateData);
            }
        }

        /// <summary>
        /// 載入圖示的輔助方法
        /// </summary>
        private BitmapImage LoadIcon(string iconName, int size)
        {
            try
            {
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);
                string iconPath = Path.Combine(assemblyDir, "Resources", "Icons", $"{iconName}_{size}.png");

                if (File.Exists(iconPath))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    return bitmap;
                }
                else
                {
                    // 如果圖示不存在，返回 null（Revit 會使用預設圖示）
                    return null;
                }
            }
            catch (Exception ex)
            {
                // 記錄錯誤但不顯示對話框，避免干擾啟動
                System.Diagnostics.Debug.WriteLine($"圖示載入失敗 {iconName}_{size}.png: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 為按鈕設定圖示 (支援 PushButtonData)
        /// </summary>
        private void SetButtonIcon(PushButtonData buttonData, string iconName)
        {
            var smallIcon = LoadIcon(iconName, 16);
            var largeIcon = LoadIcon(iconName, 32);

            if (smallIcon != null)
                buttonData.Image = smallIcon;

            if (largeIcon != null)
                buttonData.LargeImage = largeIcon;
        }

        /// <summary>
        /// 為按鈕設定圖示 (支援 PulldownButtonData)
        /// </summary>
        private void SetButtonIcon(PulldownButtonData buttonData, string iconName)
        {
            var smallIcon = LoadIcon(iconName, 16);
            var largeIcon = LoadIcon(iconName, 32);

            if (smallIcon != null)
                buttonData.Image = smallIcon;

            if (largeIcon != null)
                buttonData.LargeImage = largeIcon;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}