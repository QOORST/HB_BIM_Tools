// Application.cs (每個工具專案中)
using Autodesk.Revit.DB;
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
        private const string TAB_NAME = "HB_BIM Tools";
        private const string PANEL_MODELING = "建模工具";
        private const string PANEL_MEP = "MEP 工具";
        private const string PANEL_FAMILY = "族群 / 參數";
        private const string PANEL_DATA = "COBie / Data";
        private const string PANEL_SUPPORT = "診斷 / 支援";
        private const string LICENSE_AVAILABILITY_CLASS = "YD_RevitTools.LicenseManager.LicenseCommandAvailability";

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

                // 創建主要功能區的 Ribbon Panel
                RibbonPanel modelingPanel = GetOrCreateRibbonPanel(application, PANEL_MODELING);
                RibbonPanel mepPanel = GetOrCreateRibbonPanel(application, PANEL_MEP);
                RibbonPanel familyPanel = GetOrCreateRibbonPanel(application, PANEL_FAMILY);
                RibbonPanel dataPanel = GetOrCreateRibbonPanel(application, PANEL_DATA);
                RibbonPanel supportPanel = GetOrCreateRibbonPanel(application, PANEL_SUPPORT);

                // === 建模工具 面板 ===
                AddARToolButtons(modelingPanel);

                // === MEP 面板 ===
                AddMEPToolButtons(mepPanel);

                // === Family 面板 ===
                AddFamilyToolButtons(familyPanel);

                // === COBie / Data 面板 ===
                AddDataToolButtons(dataPanel);

                // === 診斷 / 支援 面板 ===
                AddAiAssistantButton(supportPanel);

                if (!HasButton(supportPanel, "LicenseManagement"))
                {
                    AddLicenseManagementButton(supportPanel);
                }

                AddAboutButtons(supportPanel);

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
                "YD_RevitTools.LicenseManager.Commands.AR.CmdLicenseInfo");

            buttonData.ToolTip = "管理 HB_BIM Tools 授權";
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
                "YD_RevitTools.LicenseManager.Commands.AR.Formwork.CmdMain");
            formworkGenerateData.ToolTip = "模板生成工具";
            formworkGenerateData.LongDescription = "自動生成建築模板系統 (Trial+)";
            SetButtonIcon(formworkGenerateData, "formwork");
            formworkPulldown.AddPushButton(formworkGenerateData);

            // 刪除模板
            PushButtonData formworkDeleteData = new PushButtonData(
                "FormworkDelete",
                "刪除模板",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Formwork.CmdDelete");
            formworkDeleteData.ToolTip = "刪除模板工具";
            formworkDeleteData.LongDescription = "刪除已生成的模板 (Trial+)";
            SetButtonIcon(formworkDeleteData, "formwork_delete");
            formworkPulldown.AddPushButton(formworkDeleteData);

            // 面選模板
            PushButtonData formworkPickFaceData = new PushButtonData(
                "FormworkPickFace",
                "面選模板",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Formwork.CmdPickFace");
            formworkPickFaceData.ToolTip = "面選模板工具";
            formworkPickFaceData.LongDescription = "透過選擇面來生成模板 (Standard+)";
            SetButtonIcon(formworkPickFaceData, "formwork_pick");
            formworkPulldown.AddPushButton(formworkPickFaceData);

            // 匯出CSV
            PushButtonData formworkExportCsvData = new PushButtonData(
                "FormworkExportCsv",
                "匯出CSV",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Formwork.CmdExportCsv");
            formworkExportCsvData.ToolTip = "匯出CSV工具";
            formworkExportCsvData.LongDescription = "匯出模板數量到CSV檔案 (Standard+)";
            SetButtonIcon(formworkExportCsvData, "export_csv");
            formworkPulldown.AddPushButton(formworkExportCsvData);

            // 結構分析
            PushButtonData structuralAnalysisData = new PushButtonData(
                "StructuralAnalysis",
                "結構分析",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Formwork.CmdStructuralAnalysis");
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

#if !REVIT2025
            // 房間裝修 (Revit 2022-2024、2026 支援)
            PushButtonData roomFinishData = new PushButtonData(
                "RoomFinish",
                "房間裝修",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish.CmdRoomFinish");
            roomFinishData.ToolTip = "房間裝修工具";
            roomFinishData.LongDescription = "根據房間邊界自動生成裝修面（牆、樓板、天花板、踢腳板）\n\n" +
                "功能特色：\n" +
                "• 選擇房間批次生成裝修\n" +
                "• 支援多種邊界模式（內裝修面、中心線、外裝修面）\n" +
                "• 自動連接牆體\n" +
                "• 參數化設定（高度、偏移、厚度）\n" +
                "• 匯出數量到 Excel\n\n" +
                "授權要求：Standard+";
            SetButtonIcon(roomFinishData, "finishings");
            finishingsPulldown.AddPushButton(roomFinishData);
#endif

            // 面生面
            PushButtonData faceToFaceData = new PushButtonData(
                "FaceToFace",
                "面生面",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Finishings.CmdFaceToFace");
            faceToFaceData.ToolTip = "面生面工具";
            faceToFaceData.LongDescription = "透過選擇面來生成裝修面，參數寫入材料資訊供數量產出 (Standard+)";
            SetButtonIcon(faceToFaceData, "face_to_face");
            finishingsPulldown.AddPushButton(faceToFaceData);

            // 更新粉刷面參數
            PushButtonData refreshParamsData = new PushButtonData(
                "RefreshFinishParams",
                "更新\n粉刷參數",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Finishings.CmdRefreshFinishParams");
            refreshParamsData.ToolTip = "更新粉刷面共用參數";
            refreshParamsData.LongDescription =
                "重新掃描模型中的粉刷元素（面生面或手動調整），依目前幾何位置回寫：\n" +
                "• 房間 ID / 名稱 / 編號\n" +
                "• 裝修面積\n" +
                "• 材料名稱 / 厚度\n\n" +
                "確保數量表輸出資訊正確。(Standard+)";
            SetButtonIcon(refreshParamsData, "refresh_finish_params");
            finishingsPulldown.AddPushButton(refreshParamsData);

            // 外牆粉刷驗算
            PushButtonData exteriorWallFinishReportData = new PushButtonData(
                "ExteriorWallFinishReport",
                "外牆粉刷\n驗算",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Finishings.CmdExteriorWallFinishReport");
            exteriorWallFinishReportData.ToolTip = "外牆粉刷驗算報表";
            exteriorWallFinishReportData.LongDescription =
                "依外牆外側面計算粉刷面積並匯出 Excel 驗算報表。\n\n" +
                "第一版不生成模型粉刷面，適合用於外牆粉刷明細交付前檢核。\n\n" +
                "功能：\n" +
                "• 可先選取外牆，或自動掃描外牆候選\n" +
                "• 依樓層、立面方向、牆類型/材料彙總\n" +
                "• 建立 AR_Check 外牆粉刷 3D 驗算視圖";
            SetButtonIcon(exteriorWallFinishReportData, "schedule_export");
            finishingsPulldown.AddPushButton(exteriorWallFinishReportData);

            // 更換裝修面顏色
            PushButtonData changeColorData = new PushButtonData(
                "ChangeFinishingColor",
                "更換顏色",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Finishings.CmdChangeFinishingColor");
            changeColorData.ToolTip = "更換裝修面顏色";
            changeColorData.LongDescription = "選擇已創建的一般模型裝修面，更換其材質顏色（支援多選）";
            SetButtonIcon(changeColorData, "change_finishing_color");
            finishingsPulldown.AddPushButton(changeColorData);

            // 刪除裝修
            PushButtonData deleteFinishingsData = new PushButtonData(
                "DeleteFinishings",
                "刪除裝修",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.Finishings.CmdDeleteFinishings");
            deleteFinishingsData.ToolTip = "刪除 AR 裝修元素";
            deleteFinishingsData.LongDescription = "刪除由 AR 裝修工具建立的牆、樓板、天花板與一般模型裝修元素。";
            SetButtonIcon(deleteFinishingsData, "formwork_delete");
            finishingsPulldown.AddPushButton(deleteFinishingsData);

            // === 接合工具組 (Pulldown Button) ===
            PulldownButtonData joinPulldownData = new PulldownButtonData("JoinTools", "接合\n工具");
            joinPulldownData.ToolTip = "接合與分割工具組";
            joinPulldownData.LongDescription = "提供模型元素自動接合、解除接合、牆輪廓對齊與牆/樓板分割工具";
            SetButtonIcon(joinPulldownData, "auto_join");

            PulldownButton joinPulldown = panel.AddItem(joinPulldownData) as PulldownButton;

            // 自動接合
            PushButtonData autoJoinData = new PushButtonData(
                "AutoJoin",
                "自動接合",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.AutoJoin.CmdAutoJoin");
            autoJoinData.ToolTip = "自動接合 / 解除接合";
            autoJoinData.LongDescription = "依範圍、類別與優先序，自動接合或解除接合牆、樓板、柱、梁等模型元素，也可執行牆輪廓對齊。";
            SetButtonIcon(autoJoinData, "auto_join");
            joinPulldown.AddPushButton(autoJoinData);

            // 對齊牆輪廓
            PushButtonData alignWallProfileData = new PushButtonData(
                "AlignWallProfile",
                "對齊\n牆輪廓",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.AutoJoin.CmdAlignWallProfile");
            alignWallProfileData.ToolTip = "對齊牆輪廓";
            alignWallProfileData.LongDescription =
                "依目前範圍與類別設定，將基本牆的頂部/底部對齊至樓板或梁。\n\n" +
                "牆頂通常優先貼附樓板下；若牆位與梁投影面間距小於等於 0.5 cm，且梁底低於樓板貼附高度，會改貼附梁下。\n" +
                "此判斷可避免牆頂在梁邊殘留薄縫或短凸出。";
            SetButtonIcon(alignWallProfileData, "align_wall_profile");
            joinPulldown.AddPushButton(alignWallProfileData);

            // 分割樓板
            PushButtonData splitFloorData = new PushButtonData(
                "SplitFloor",
                "分割樓板",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.AutoJoin.CmdSplitFloor");
            splitFloorData.ToolTip = "分割樓板工具";
            splitFloorData.LongDescription =
                "依選取的結構構架（梁）將樓板分割為多塊。\n\n" +
                "操作步驟：\n" +
                "  同時選取要分割的樓板及穿越其中的梁，完成後按 Finish。";
            SetButtonIcon(splitFloorData, "split_floor");
            joinPulldown.AddPushButton(splitFloorData);

            // 分割牆
            PushButtonData splitWallData = new PushButtonData(
                "SplitWall",
                "分割牆",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.AR.AutoJoin.CmdSplitWall");
            splitWallData.ToolTip = "分割牆工具";
            splitWallData.LongDescription =
                "依切割構件在交接處將牆分割為多段直線牆。\n\n" +
                "操作步驟：\n" +
                "  步驟 1：選取要分割的目標牆。\n" +
                "  步驟 2：選取切割構件（結構柱、梁或其他牆）。\n\n" +
                "適用於直線基本牆；弧牆或複雜輪廓會自動略過。";
            SetButtonIcon(splitWallData, "split_wall");
            joinPulldown.AddPushButton(splitWallData);

            // === 標註工具組 (Pulldown Button) ===
            if (!HasButton(panel, "AutoDimensionTools"))
            {
                PulldownButtonData autoDimensionPulldownData = new PulldownButtonData("AutoDimensionTools", "標註\n工具");
                autoDimensionPulldownData.ToolTip = "自動標註工具組";
                autoDimensionPulldownData.LongDescription = "提供柱、梁、軸線與房間內容的快速標註與更新工具。";
                SetButtonIcon(autoDimensionPulldownData, "auto_dimension");

                PulldownButton autoDimensionPulldown = panel.AddItem(autoDimensionPulldownData) as PulldownButton;

                PushButtonData autoDimensionData = new PushButtonData(
                    "AutoDimensionMain",
                    "自動\n標註",
                    assemblyPath,
                    "YDBIM.AutoDimension.App.AutoDimensionCommand");
                autoDimensionData.ToolTip = "依可見構件快速建立標註";
                autoDimensionData.LongDescription = "開啟完整標註設定視窗，可選擇模式、方向、偏移量與標註型式。";
                SetButtonIcon(autoDimensionData, "auto_dimension");
                autoDimensionPulldown.AddPushButton(autoDimensionData);

                PushButtonData roomContentData = new PushButtonData(
                    "AutoDimensionRoomContent",
                    "房間內容",
                    assemblyPath,
                    "YDBIM.AutoDimension.App.RoomContentCommand");
                roomContentData.ToolTip = "更新房間內容參數";
                roomContentData.LongDescription = "批次更新目前視圖可處理房間的內容資訊，若資料已最新則不重複寫入。";
                SetButtonIcon(roomContentData, "model_data_manager");
                autoDimensionPulldown.AddPushButton(roomContentData);

                PushButtonData autoTagHorizontalData = new PushButtonData(
                    "AutoTagHorizontal",
                    "水平元素\n標籤",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.AR.AutoTag.CmdAutoTagHorizontal");
                autoTagHorizontalData.ToolTip = "自動建立水平元素標籤";
                autoTagHorizontalData.LongDescription =
                    "開啟自動標籤設定，可選擇軀體圖或機電圖樣板，依不同構件分類指定標籤族型，並控制標籤放置位置、偏移距離與引線。";
                SetButtonIcon(autoTagHorizontalData, "auto_tag_horizontal");
                autoDimensionPulldown.AddPushButton(autoTagHorizontalData);

                PushButtonData autoTagVerticalData = new PushButtonData(
                    "AutoTagVertical",
                    "垂直元素\n標籤",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.AR.AutoTag.CmdAutoTagVertical");
                autoTagVerticalData.ToolTip = "自動建立垂直元素標籤";
                autoTagVerticalData.LongDescription =
                    "開啟自動標籤設定，可選擇軀體圖或機電圖樣板，針對垂直向構件建立可控位置的分類標籤。";
                SetButtonIcon(autoTagVerticalData, "auto_tag_vertical");
                autoDimensionPulldown.AddPushButton(autoTagVerticalData);

                PushButtonData selectRelatedTagsData = new PushButtonData(
                    "SelectRelatedTags",
                    "關聯\n標籤",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.AR.AutoTag.CmdSelectRelatedTags");
                selectRelatedTagsData.ToolTip = "選取構件與關聯標籤";
                selectRelatedTagsData.LongDescription =
                    "依目前選取的構件或標籤，在目前視圖中同步選取其關聯項目，方便確認自動標籤是否與模型構件連動。";
                SetButtonIcon(selectRelatedTagsData, "auto_tag_horizontal");
                autoDimensionPulldown.AddPushButton(selectRelatedTagsData);

                PushButtonData tagAlignData = new PushButtonData(
                    "TagAlign",
                    "標籤\n對齊",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.AR.AutoTag.CmdTagAlign");
                tagAlignData.ToolTip = "標籤輔助對齊";
                tagAlignData.LongDescription =
                    "整理已建立的 Revit 標籤，可針對目前選取或目前視圖的標籤進行水平對齊、垂直對齊、等距水平與等距垂直排列，並可避免標籤互相重疊。";
                SetButtonIcon(tagAlignData, "auto_tag_horizontal");
                autoDimensionPulldown.AddPushButton(tagAlignData);
            }
        }

#pragma warning disable CS0162
        private void AddMEPToolButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            AddOptimizedMEPToolButtons(panel, assemblyPath);
            return;

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

                SetButtonIcon(pipeSleeveData, "pipe_sleeve_wall");

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

            // === 支管中心對齊工具 ===
            if (!HasButton(panel, "PipeCenterAlign"))
            {
                PushButtonData pipeCenterAlignData = new PushButtonData(
                    "PipeCenterAlign",
                    "支管\n對齊",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdPipeCenterAlign");

                pipeCenterAlignData.ToolTip = "支管中心對齊工具";
                pipeCenterAlignData.LongDescription = "將支管端點中心對齊到幹管中心線\n\n" +
                    "功能特色：\n" +
                    "• 先選擇幹管（平面直管，可帶坡度），再選擇支管\n" +
                    "• 支援先框選/複選兩支管後直接執行\n" +
                    "• 自動取支管最靠近幹管的一端\n" +
                    "• 依支管原本平面角度延伸/修剪到幹管中心線\n" +
                    "• 可選擇切開幹管並自動建立三通\n" +
                    "• 已連接的支管端點會提示先斷開，避免破壞既有接頭\n\n" +
                    "授權要求：Trial+";

                SetButtonIcon(pipeCenterAlignData, "pipe_center_align");

                panel.AddItem(pipeCenterAlignData);
            }

            // === 支管中心對齊設定 ===
            if (!HasButton(panel, "PipeCenterAlignSettings"))
            {
                PushButtonData pipeCenterAlignSettingsData = new PushButtonData(
                    "PipeCenterAlignSettings",
                    "支管\n設定",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdPipeCenterAlignSettings");

                pipeCenterAlignSettingsData.ToolTip = "支管中心對齊設定";
                pipeCenterAlignSettingsData.LongDescription =
                    "設定支管中心對齊是否每次詢問、自動建立接頭，或只對齊端點。\n\n" +
                    "用於避免每次執行都要選擇是否生成三通 / Takeoff / Wye。";

                SetButtonIcon(pipeCenterAlignSettingsData, "pipe_center_align_settings");

                panel.AddItem(pipeCenterAlignSettingsData);
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

                SetButtonIcon(pipeToISOData, "pipe_iso");

                panel.AddItem(pipeToISOData);
            }

            // === MEP 自動配管 (Beta) ===
            if (!HasButton(panel, "AutoPipeRouting"))
            {
                PushButtonData autoPipeRoutingData = new PushButtonData(
                    "AutoPipeRouting",
                    "自動\n配管",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdAutoPipeRouting");

                autoPipeRoutingData.ToolTip = "MEP 自動配管 (Beta)";
                autoPipeRoutingData.LongDescription =
                    "依幹管與設備/支管空間關係自動生成出管段、過渡管並嘗試建立接頭。\n\n" +
                    "操作：\n" +
                    "1) 先選幹管 (Axis Pipe)\n" +
                    "2) 再框選/複選設備或多支管\n" +
                    "3) 工具會依投影里程排序逐一建立\n\n" +
                    "目前為 Beta：優先提供幾何排序、過渡管建立與接頭嘗試。";
                SetButtonIcon(autoPipeRoutingData, "auto_pipe_routing");
                panel.AddItem(autoPipeRoutingData);
            }

            // === 排水支管連接幹管 ===
            if (!HasButton(panel, "DrainBranchConnect"))
            {
                PushButtonData drainConnectData = new PushButtonData(
                    "DrainBranchConnect",
                    "排水\n連接",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdDrainBranchConnect");

                drainConnectData.ToolTip = "排水支管自動連接幹管";
                drainConnectData.LongDescription =
                    "選取一支未連接的排水支管與排水幹管，可選擇「雙 45°偏移＋三通」或「單 45°＋Y 型斜接」；並可設定前後直管長度與接入方向。\n\n" +
                    "安全限制：\n" +
                    "• 支管與幹管必須為直線管段\n" +
                    "• 幹管接入點必須低於支管端點\n" +
                    "• 支管與幹管必須屬於相同系統類型\n" +
                    "• 無可用配件時整筆交易回滾";
                SetButtonIcon(drainConnectData, "auto_pipe_routing");
                panel.AddItem(drainConnectData);
            }

            // === MEP 上下翻彎 ===
            if (!HasButton(panel, "MepUpDownOffset"))
            {
                PushButtonData upDownOffsetData = new PushButtonData(
                    "MepUpDownOffset",
                    "手動\n翻彎",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepUpDownOffset");

                upDownOffsetData.ToolTip = "MEP 管線/風管上下翻彎";
                upDownOffsetData.LongDescription =
                    "選取一支直線 Pipe 或 Duct，依選取點建立上翻/下翻避讓路徑。\n\n" +
                    "功能：\n" +
                    "• 支援 Pipe、Duct\n" +
                    "• 可選 90° 或 45°/30°/20°/15°斜管翻彎\n" +
                    "• 可設定偏移高度與避讓中段長度\n" +
                    "• 會複製原元素屬性並嘗試建立彎頭";
                SetButtonIcon(upDownOffsetData, "manual_offset");
                panel.AddItem(upDownOffsetData);
            }

            if (!HasButton(panel, "MepManualUpOffset"))
            {
                PushButtonData upOffsetData = new PushButtonData(
                    "MepManualUpOffset",
                    "手動\n上翻",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepManualUpOffset");

                upOffsetData.ToolTip = "MEP 管線/風管手動上翻";
                upOffsetData.LongDescription =
                    "選取一支直線 Pipe 或 Duct，固定以上翻方向建立避讓路徑。\n\n" +
                    "可設定角度、偏移高度與避讓中段長度；若選點靠近端點，會自動調整到可放入翻彎的位置。";
                SetButtonIcon(upOffsetData, "auto_avoid");
                panel.AddItem(upOffsetData);
            }

            if (!HasButton(panel, "MepManualDownOffset"))
            {
                PushButtonData downOffsetData = new PushButtonData(
                    "MepManualDownOffset",
                    "手動\n下翻",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepManualDownOffset");

                downOffsetData.ToolTip = "MEP 管線/風管手動下翻";
                downOffsetData.LongDescription =
                    "選取一支直線 Pipe 或 Duct，固定以下翻方向建立避讓路徑。\n\n" +
                    "可設定角度、偏移高度與避讓中段長度；若選點靠近端點，會自動調整到可放入翻彎的位置。";
                SetButtonIcon(downOffsetData, "auto_avoid");
                panel.AddItem(downOffsetData);
            }

            if (!HasButton(panel, "MepEndUpOffset"))
            {
                PushButtonData endUpOffsetData = new PushButtonData(
                    "MepEndUpOffset",
                    "末端\n上行",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepEndUpOffset");

                endUpOffsetData.ToolTip = "MEP 管線/風管末端自動上行";
                endUpOffsetData.LongDescription =
                    "選取一支直線 Pipe 或 Duct 的端點附近，從最近的未連接端點往外建立上行管段。\n\n" +
                    "適合管線末端接高低位轉折；不會切改原管中段。";
                SetButtonIcon(endUpOffsetData, "auto_avoid");
                panel.AddItem(endUpOffsetData);
            }

            if (!HasButton(panel, "MepEndDownOffset"))
            {
                PushButtonData endDownOffsetData = new PushButtonData(
                    "MepEndDownOffset",
                    "末端\n下行",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepEndDownOffset");

                endDownOffsetData.ToolTip = "MEP 管線/風管末端自動下行";
                endDownOffsetData.LongDescription =
                    "選取一支直線 Pipe 或 Duct 的端點附近，從最近的未連接端點往外建立下行管段。\n\n" +
                    "適合管線末端接高低位轉折；不會切改原管中段。";
                SetButtonIcon(endDownOffsetData, "auto_avoid");
                panel.AddItem(endDownOffsetData);
            }

            // === MEP 多點接入主管 ===
            if (!HasButton(panel, "MepMultiConnectInto"))
            {
                PushButtonData multiConnectData = new PushButtonData(
                    "MepMultiConnectInto",
                    "多點\n接入",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepMultiConnectInto");

                multiConnectData.ToolTip = "多設備 Connector 批次接入主管";
                multiConnectData.LongDescription =
                    "參考 Dynamo「Connect IntoMULTIPLE」流程，選取多個設備與多支主管後，依 Connector/主管系統自動建立支管並接入主管。\n\n" +
                    "功能：\n" +
                    "• 支援 SupplyHydronic、ReturnHydronic、Sanitary/Condensate 辨識\n" +
                    "• 可設定 CHWS/CHWR/COND 偏好接入距離與排水坡度\n" +
                    "• 自動打斷主管並建立 Tee\n" +
                    "• 任一接入失敗時整批交易回復";
                SetButtonIcon(multiConnectData, "auto_pipe_routing");
                panel.AddItem(multiConnectData);
            }

            // === MEP 旋轉設定 ===
            if (!HasButton(panel, "MepRotateSettings"))
            {
                PushButtonData rotateSettingsData = new PushButtonData(
                    "MepRotateSettings",
                    "旋轉\n設定",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepRotateSettings");
                rotateSettingsData.ToolTip = "MEP 旋轉設定";
                rotateSettingsData.LongDescription = "設定旋轉元素類型與角度。";
                SetButtonIcon(rotateSettingsData, "mep_rotate_settings");
                panel.AddItem(rotateSettingsData);
            }

            // === MEP 順時針旋轉 ===
            if (!HasButton(panel, "MepRotateClockwise"))
            {
                PushButtonData rotateCwData = new PushButtonData(
                    "MepRotateClockwise",
                    "順時針\n旋轉",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepRotateClockwise");
                rotateCwData.ToolTip = "順時針旋轉 MEP 元素";
                rotateCwData.LongDescription = "依旋轉設定對選取元素做順時針旋轉。";
                SetButtonIcon(rotateCwData, "mep_rotate");
                panel.AddItem(rotateCwData);
            }

            // === MEP 逆時針旋轉 ===
            if (!HasButton(panel, "MepRotateCounterClockwise"))
            {
                PushButtonData rotateCcwData = new PushButtonData(
                    "MepRotateCounterClockwise",
                    "逆時針\n旋轉",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepRotateCounterClockwise");
                rotateCcwData.ToolTip = "逆時針旋轉 MEP 元素";
                rotateCcwData.LongDescription = "依旋轉設定對選取元素做逆時針旋轉。";
                SetButtonIcon(rotateCcwData, "mep_rotate");
                panel.AddItem(rotateCcwData);
            }

            // === 接點生成管 ===
            if (!HasButton(panel, "MepPipeFromConnectors"))
            {
                PushButtonData connectorPipeData = new PushButtonData(
                    "MepPipeFromConnectors",
                    "接點\n生成管",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepPipeFromConnectors");
                connectorPipeData.ToolTip = "接點生成管";
                connectorPipeData.LongDescription = "點選兩個管件/管段接點，自動建立管段並連接。";
                SetButtonIcon(connectorPipeData, "pipe_sleeve");
                panel.AddItem(connectorPipeData);
            }

            // === MEP 檢查工具 ===
            if (!HasButton(panel, "MepCheck"))
            {
                PushButtonData mepCheckData = new PushButtonData(
                    "MepCheck",
                    "MEP\n檢查",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepCheck");

                mepCheckData.ToolTip = "MEP 檢查工具";
                mepCheckData.LongDescription =
                    "檢查 MEP 模型常見資料與幾何問題。\n\n" +
                    "目前支援：\n" +
                    "• 管洩水方向與坡度檢查\n" +
                    "• 設備樓層分布檢查\n" +
                    "• Connector 未連接與系統中斷檢查\n" +
                    "• 系統資料與設備編號重複檢查\n" +
                    "• 結果表格回查模型元素\n" +
                    "• CSV 檢查報告匯出";

                SetButtonIcon(mepCheckData, "pipe_iso");
                panel.AddItem(mepCheckData);
            }
        }

#pragma warning restore CS0162
        private void AddOptimizedMEPToolButtons(RibbonPanel panel, string assemblyPath)
        {
            // MEP 正式功能：保留常用接管、管線避讓、手動翻彎與自動套管；其餘 MEP 工具仍保留程式碼但暫不顯示。
            if (!HasButton(panel, "PipeCenterAlign"))
            {
                PushButtonData pipeCenterAlignData = new PushButtonData(
                    "PipeCenterAlign",
                    "支管\n對齊",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdPipeCenterAlign");

                pipeCenterAlignData.ToolTip = "支管中心對齊工具";
                pipeCenterAlignData.LongDescription = "將支管端點中心對齊到幹管中心線，可選擇只對齊端點、建立 Tee / Takeoff，或使用 45° 垂直對齊。";
                SetButtonIcon(pipeCenterAlignData, "pipe_center_align");
                panel.AddItem(pipeCenterAlignData);
            }

            if (!HasButton(panel, "PipeBatchCenterAlign"))
            {
                PushButtonData pipeBatchCenterAlignData = new PushButtonData(
                    "PipeBatchCenterAlign",
                    "批次\n對齊",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdPipeBatchCenterAlign");

                pipeBatchCenterAlignData.ToolTip = "批次支管中心對齊";
                pipeBatchCenterAlignData.LongDescription = "先選取幹管，再框選或複選多支支管，批次延伸/修剪支管端點到幹管中心線，並可依設定建立 Tee / Takeoff。";
                SetButtonIcon(pipeBatchCenterAlignData, "pipe_center_align_settings");
                panel.AddItem(pipeBatchCenterAlignData);
            }

            if (!HasButton(panel, "MepAvoidOffsetTools"))
            {
                SplitButtonData avoidOffsetPulldownData = new SplitButtonData(
                    "MepAvoidOffsetTools",
                    "避讓\n翻彎");
                avoidOffsetPulldownData.ToolTip = "管線避讓與手動翻彎";
                avoidOffsetPulldownData.LongDescription = "集中放置管線自動避讓與手動上下翻彎工具。";
                SetButtonIcon(avoidOffsetPulldownData, "auto_avoid");
                SplitButton avoidOffsetPulldown = panel.AddItem(avoidOffsetPulldownData) as SplitButton;

                PushButtonData autoAvoidData = new PushButtonData(
                    "AutoAvoid",
                    "管線避讓",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdAutoAvoid");

                autoAvoidData.ToolTip = "管線避讓工具";
                autoAvoidData.LongDescription =
                    "自動避讓管線與障礙物衝突。\n\n" +
                    "功能：\n" +
                    "• 選擇管線和避讓範圍\n" +
                    "• 自動生成翻彎路徑\n" +
                    "• 支援多種避讓方向與偏移設定";
                SetButtonIcon(autoAvoidData, "auto_avoid");
                avoidOffsetPulldown?.AddPushButton(autoAvoidData);

                PushButtonData upDownOffsetData = new PushButtonData(
                    "MepUpDownOffset",
                    "手動翻彎",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdMepUpDownOffset");

                upDownOffsetData.ToolTip = "MEP 管線/風管上下翻彎";
                upDownOffsetData.LongDescription =
                    "選取一支直線 Pipe 或 Duct，依選取點建立上翻/下翻避讓路徑。\n\n" +
                    "功能：\n" +
                    "• 支援 Pipe、Duct\n" +
                    "• 可選 90° 或 45°/30°/20°/15°斜管翻彎\n" +
                    "• 可設定偏移高度與避讓中段長度\n" +
                    "• 會複製原元素屬性並嘗試建立彎頭";
                SetButtonIcon(upDownOffsetData, "auto_avoid");
                avoidOffsetPulldown?.AddPushButton(upDownOffsetData);
            }

            if (!HasButton(panel, "PipeSleeve"))
            {
                PushButtonData pipeSleeveData = new PushButtonData(
                    "PipeSleeve",
                    "Pipe\nSleeve",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdPipeSleeve");

                pipeSleeveData.ToolTip = "Pipe Sleeve Tool";
                pipeSleeveData.LongDescription = "Automatically place sleeves for pipes and ducts passing through walls, floors and beams.";
                SetButtonIcon(pipeSleeveData, "pipe_sleeve_wall");
                panel.AddItem(pipeSleeveData);
            }

            if (!HasButton(panel, "PipeSleeveManager"))
            {
                PushButtonData pipeSleeveManagerData = new PushButtonData(
                    "PipeSleeveManager",
                    "套管\n管理",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdPipeSleeveManager");

                pipeSleeveManagerData.ToolTip = "套管管理";
                pipeSleeveManagerData.LongDescription = "檢視、篩選、定位、刪除與更新自動生成的管線套管。";
                SetButtonIcon(pipeSleeveManagerData, "pipe_sleeve");
                panel.AddItem(pipeSleeveManagerData);
            }
            if (!HasButton(panel, "ArchitecturalOpeningFromSleeves"))
            {
                PushButtonData architecturalOpeningData = new PushButtonData(
                    "ArchitecturalOpeningFromSleeves",
                    "建築\n開孔",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.MEP.CmdArchitecturalOpeningFromSleeves");

                architecturalOpeningData.ToolTip = "依 MEP 套管建立建築開孔/預留洞";
                architecturalOpeningData.LongDescription = "在建築模型中讀取連結 MEP 模型的自動套管需求，建立或更新建築端開孔/預留洞切割元件。";
                SetButtonIcon(architecturalOpeningData, "pipe_sleeve_wall");
                panel.AddItem(architecturalOpeningData);
            }
        }
        private void AddFamilyToolButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string familyLibraryAssemblyPath = Path.Combine(
                Path.GetDirectoryName(assemblyPath) ?? string.Empty,
                "CompanyFamilyLibraryMvp.dll");

            // === 族群資料庫 ===
            if (File.Exists(familyLibraryAssemblyPath) && !HasButton(panel, "CompanyFamilyLibrary"))
            {
                PushButtonData familyLibraryData = new PushButtonData(
                    "CompanyFamilyLibrary",
                    "族群\n資料庫",
                    familyLibraryAssemblyPath,
                    "CompanyFamilyLibraryMvp.AppCommand");

                familyLibraryData.ToolTip = "族群資料庫";
                familyLibraryData.LongDescription = "開啟標準族庫瀏覽器，可依系統分類、Revit 版本、命名狀態搜尋並安全載入族群。";
                SetExternalButtonIcon(familyLibraryData, "family_database");

                panel.AddItem(familyLibraryData);
            }

            // === 族參數名稱修改 ===
            if (!HasButton(panel, "FamilyParameterRename"))
            {
                PushButtonData familyParameterRenameData = new PushButtonData(
                    "FamilyParameterRename",
                    "族參數\n名稱修改",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.CmdFamilyParameterRename");

                familyParameterRenameData.ToolTip = "族參數名稱修改";
                familyParameterRenameData.LongDescription = "依表格檢視舊的參數名稱與新的參數名稱，確認後批次更新族群參數名稱。可在 Family Editor 使用，也可在專案中針對已載入族群執行。";
                SetButtonIcon(familyParameterRenameData, "family_rename");

                panel.AddItem(familyParameterRenameData);
            }

#if !REVIT2025 && !REVIT2026
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
#endif
        }

        private void AddDataToolButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // === COBie 設定 ===
            PulldownButtonData cobieSettingsPulldownData = new PulldownButtonData("CobieSettingsTools", "COBie\n設定");
            cobieSettingsPulldownData.ToolTip = "COBie 設定工具組";
            cobieSettingsPulldownData.LongDescription = "整理 COBie 欄位、樣板與 BIM 標準檢查等前置設定工具。";
            SetButtonIcon(cobieSettingsPulldownData, "cobie_field");
            PulldownButton cobieSettingsPulldown = panel.AddItem(cobieSettingsPulldownData) as PulldownButton;

            PushButtonData cobieFieldManagerData = new PushButtonData(
                "CobieFieldManager",
                "COBie 欄位設定",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieFieldManager");
            cobieFieldManagerData.ToolTip = "COBie 欄位設定";
            cobieFieldManagerData.LongDescription = "設定自訂 COBie 匯出使用的欄位、參數對照與標準欄位檢核。(Trial+)";
            SetButtonIcon(cobieFieldManagerData, "cobie_field");
            cobieSettingsPulldown.AddPushButton(cobieFieldManagerData);

            PushButtonData cobieExportTemplateData = new PushButtonData(
                "CobieExportTemplate",
                "COBie 樣板",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieExportTemplate");
            cobieExportTemplateData.ToolTip = "COBie 樣板";
            cobieExportTemplateData.LongDescription = "匯出 COBie 欄位樣板與欄位填寫說明。(Trial+)";
            SetButtonIcon(cobieExportTemplateData, "cobie_template");
            cobieSettingsPulldown.AddPushButton(cobieExportTemplateData);

            PushButtonData bimStandardAuditData = new PushButtonData(
                "BimStandardAudit",
                "BIM 標準檢查",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.BimStandard.CmdBimStandardAudit");
            bimStandardAuditData.ToolTip = "BIM 標準檢查";
            bimStandardAuditData.LongDescription =
                "檢查目前 Revit 文件的樣板健康度與族群品質。\n\n" +
                "功能特色：\n" +
                "• 檢查 Project Information 必填欄位\n" +
                "• 檢查視圖樣板、視圖、圖紙、明細表命名\n" +
                "• 檢查材質、篩選器與文字樣式命名\n" +
                "• 檢查模型內族群與 Type 命名品質\n" +
                "• 匯出 Excel 檢查報告\n\n" +
                "授權要求：檢查 Trial+，匯出 Standard+";
            SetButtonIcon(bimStandardAuditData, "bim_standard");
            cobieSettingsPulldown.AddPushButton(bimStandardAuditData);

            // === COBie 匯入匯出 ===
            PulldownButtonData cobieExchangePulldownData = new PulldownButtonData("CobieExchangeTools", "COBie\n匯入匯出");
            cobieExchangePulldownData.ToolTip = "COBie 匯入匯出工具組";
            cobieExchangePulldownData.LongDescription = "集中 COBie 資料交換、標準工作簿與 Revit 明細表匯出工具。";
            SetButtonIcon(cobieExchangePulldownData, "cobie_export");
            PulldownButton cobieExchangePulldown = panel.AddItem(cobieExchangePulldownData) as PulldownButton;

            PushButtonData cobieExportData = new PushButtonData(
                "CobieExport",
                "自訂 COBie",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieExportEnhanced");
            cobieExportData.ToolTip = "自訂 COBie";
            cobieExportData.LongDescription = "依「COBie 欄位設定」中的自訂欄位與參數對照規則匯出資料。(Standard+)";
            SetButtonIcon(cobieExportData, "cobie_export");
            cobieExchangePulldown.AddPushButton(cobieExportData);

            PushButtonData cobieStandardExportData = new PushButtonData(
                "CobieStandardExport",
                "標準 COBie",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieStandardExport");
            cobieStandardExportData.ToolTip = "COBie 標準工作簿匯出";
            cobieStandardExportData.LongDescription =
                "依 COBie 資料交換架構匯出固定工作表。\n\n" +
                "包含 Contact、Facility、Floor、Space、Type、Component、System、Attribute、Coordinate、PickLists 與 Validation 等工作表。\n\n" +
                "此功能適合交付前檢核與資料交換格式整理。";
            SetButtonIcon(cobieStandardExportData, "cobie_standard");
            cobieExchangePulldown.AddPushButton(cobieStandardExportData);

            PushButtonData cobieImportData = new PushButtonData(
                "CobieImport",
                "COBie 匯入",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieImportEnhanced");
            cobieImportData.ToolTip = "COBie 匯入";
            cobieImportData.LongDescription = "匯入 COBie 資料 (Standard+)";
            SetButtonIcon(cobieImportData, "cobie_import");
            cobieExchangePulldown.AddPushButton(cobieImportData);

            PushButtonData scheduleExportData = new PushButtonData(
                "ScheduleExport",
                "明細表匯出",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdScheduleExport");
            scheduleExportData.ToolTip = "明細表匯出工具";
            scheduleExportData.LongDescription =
                "將 Revit 明細表匯出為 Excel 或 PDF 格式\n\n" +
                "功能特色：\n" +
                "• 列出文件中所有明細表供選擇\n" +
                "• 支援 Excel (.xlsx) 格式，含格式化樣式\n" +
                "• 支援 PDF 格式，使用 Revit 內建匯出引擎\n" +
                "• 可選擇合併至單一檔案或各自分開匯出\n" +
                "• 支援 Revit 2024 / 2025 / 2026\n\n" +
                "授權要求：Standard+";
            SetButtonIcon(scheduleExportData, "schedule_export");
            cobieExchangePulldown.AddPushButton(scheduleExportData);

            // === 資料管理 ===
            PulldownButtonData dataManagementPulldownData = new PulldownButtonData("DataManagementTools", "資料\n管理");
            dataManagementPulldownData.ToolTip = "資料管理工具組";
            dataManagementPulldownData.LongDescription = "集中模型命名整理與交付資料整理工具。";
            SetButtonIcon(dataManagementPulldownData, "data_management");
            PulldownButton dataManagementPulldown = panel.AddItem(dataManagementPulldownData) as PulldownButton;

            PushButtonData modelDataManagerData = new PushButtonData(
                "ModelDataManager",
                "模型資料管理",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdModelDataManager");
            modelDataManagerData.ToolTip = "模型資料管理";
            modelDataManagerData.LongDescription =
                "批次檢視與調整 Revit 管理介面常用名稱資料。\n\n" +
                "目前支援：\n" +
                "• 族群名稱\n" +
                "• 類型名稱\n" +
                "• 視圖名稱\n" +
                "• 材料名稱\n\n" +
                "可搜尋、篩選、選取後批次套用，適合整理模型命名與交付資料。";
            SetButtonIcon(modelDataManagerData, "model_data_manager");
            dataManagementPulldown.AddPushButton(modelDataManagerData);

            PushButtonData clarificationDeckData = new PushButtonData(
                "ClarificationDeckExport",
                "釋疑簡報",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.ClarificationDeck.CmdClarificationDeckExport");
            clarificationDeckData.ToolTip = "釋疑簡報快速產出";
            clarificationDeckData.LongDescription =
                "依固定釋疑單版型快速產出 PowerPoint 簡報。\n\n" +
                "功能特色：\n" +
                "• 填寫項次、標題、系統、日期與辦理情形\n" +
                "• 套入平面總圖、局部放大圖與 3D 視圖\n" +
                "• 自動建立參考排版、框線、箭頭與標籤\n\n" +
                "授權要求：Standard+";
            SetButtonIcon(clarificationDeckData, "clarification_deck");
            dataManagementPulldown.AddPushButton(clarificationDeckData);
        }

        private void AddClarificationToolButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // 釋疑簡報快速產出
            if (!HasButton(panel, "ClarificationDeckExport"))
            {
                PushButtonData clarificationDeckData = new PushButtonData(
                    "ClarificationDeckExport",
                    "釋疑\n簡報",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.Data.ClarificationDeck.CmdClarificationDeckExport");
                clarificationDeckData.ToolTip = "釋疑簡報快速產出";
                clarificationDeckData.LongDescription =
                    "依固定釋疑單版型快速產出 PowerPoint 簡報。\n\n" +
                    "功能特色：\n" +
                    "• 填寫項次、標題、系統、日期與辦理情形\n" +
                    "• 套入平面總圖、局部放大圖與 3D 視圖\n" +
                    "• 自動建立參考排版、框線、箭頭與標籤\n\n" +
                    "授權要求：Standard+";
                SetButtonIcon(clarificationDeckData, "clarification_deck");
                panel.AddItem(clarificationDeckData);
            }
        }

        private void AddAiAssistantButton(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            if (!HasButton(panel, "AiAssistant"))
            {
                PushButtonData aiAssistantData = new PushButtonData(
                    "AiAssistant",
                    "AI\n助理",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.AI.CmdAiAssistant");

                aiAssistantData.ToolTip = "本機 AI 助理";
                aiAssistantData.LongDescription =
                    "連接本機 AI 部署，依目前 Revit 模型摘要提供檢查、整理與工具使用建議。\n\n" +
                    "預設支援 Ollama，也可設定為 OpenAI-compatible local server。第一版僅讀取摘要與產生建議，不直接修改模型。";
                SetButtonIcon(aiAssistantData, "ai_assistant");

                panel.AddItem(aiAssistantData);
            }
        }

        private void AddAboutButtons(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // 關於按鈕
            if (!HasButton(panel, "About"))
            {
                PushButtonData aboutData = new PushButtonData(
                    "About",
                    "關於\nHB_BIM",
                    assemblyPath,
                    "YD_RevitTools.LicenseManager.Commands.AboutCommand");

                aboutData.ToolTip = "關於 HB_BIM Tools";
                aboutData.LongDescription = "查看 HB_BIM Tools 的版本資訊和說明";

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
            ApplyLicenseAvailability(buttonData);

            var smallIcon = LoadIcon(iconName, 16);
            var largeIcon = LoadIcon(iconName, 32);

            if (smallIcon != null)
                buttonData.Image = smallIcon;

            if (largeIcon != null)
                buttonData.LargeImage = largeIcon;
        }

        private void ApplyLicenseAvailability(PushButtonData buttonData)
        {
            if (buttonData == null || IsLicenseFreeCommand(buttonData.ClassName))
                return;

            buttonData.AvailabilityClassName = LICENSE_AVAILABILITY_CLASS;
        }

        private void SetExternalButtonIcon(PushButtonData buttonData, string iconName)
        {
            var smallIcon = LoadIcon(iconName, 16);
            var largeIcon = LoadIcon(iconName, 32);

            if (smallIcon != null)
                buttonData.Image = smallIcon;

            if (largeIcon != null)
                buttonData.LargeImage = largeIcon;
        }

        private bool IsLicenseFreeCommand(string className)
        {
            return string.Equals(className, "YD_RevitTools.LicenseManager.Commands.AR.CmdLicenseInfo", StringComparison.Ordinal) ||
                   string.Equals(className, "YD_RevitTools.LicenseManager.Commands.AboutCommand", StringComparison.Ordinal) ||
                   string.Equals(className, "YD_RevitTools.LicenseManager.Commands.CheckUpdateCommand", StringComparison.Ordinal);
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

    public class LicenseCommandAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                return LicenseManager.Instance.ValidateLicense().IsValid;
            }
            catch
            {
                return false;
            }
        }
    }
}



