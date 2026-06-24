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
#if !REVIT2025 && !REVIT2026
                AddFamilyToolButtons(familyPanel);
#endif

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
                "YD_RevitTools.LicenseManager.Commands.AR.CmdLicenseInfo");

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
                SetButtonIcon(autoDimensionPulldownData, "schedule_export");

                PulldownButton autoDimensionPulldown = panel.AddItem(autoDimensionPulldownData) as PulldownButton;

                PushButtonData autoDimensionData = new PushButtonData(
                    "AutoDimensionMain",
                    "上方自動\n標註",
                    assemblyPath,
                    "YDBIM.AutoDimension.App.AutoDimensionCommand");
                autoDimensionData.ToolTip = "依可見構件快速建立標註";
                autoDimensionData.LongDescription = "開啟完整標註設定視窗，可選擇模式、方向、偏移量與標註型式。";
                autoDimensionPulldown.AddPushButton(autoDimensionData);

                PushButtonData columnLineGridData = new PushButtonData(
                    "AutoDimensionColumnGrid",
                    "柱線 / 網格\n標註",
                    assemblyPath,
                    "YDBIM.AutoDimension.App.ColumnLineGridCommand");
                columnLineGridData.ToolTip = "建立柱線與軸線標註";
                columnLineGridData.LongDescription = "依目前視圖可見軸線建立同方向標註，適合快速完成柱線/網格尺寸標註。";
                autoDimensionPulldown.AddPushButton(columnLineGridData);

                PushButtonData columnDimensionData = new PushButtonData(
                    "AutoDimensionColumn",
                    "柱標註",
                    assemblyPath,
                    "YDBIM.AutoDimension.App.ColumnDimensionCommand");
                columnDimensionData.ToolTip = "建立柱邊定位標註";
                columnDimensionData.LongDescription = "針對可見結構柱建立定位標註，可指定左右/前後方向。";
                autoDimensionPulldown.AddPushButton(columnDimensionData);

                PushButtonData beamDimensionData = new PushButtonData(
                    "AutoDimensionBeam",
                    "梁標註",
                    assemblyPath,
                    "YDBIM.AutoDimension.App.BeamDimensionCommand");
                beamDimensionData.ToolTip = "建立梁寬與間距標註";
                beamDimensionData.LongDescription = "對可見直線結構梁建立梁寬與梁間距標註，並可調整偏移量。";
                autoDimensionPulldown.AddPushButton(beamDimensionData);

                PushButtonData roomContentData = new PushButtonData(
                    "AutoDimensionRoomContent",
                    "房間內容",
                    assemblyPath,
                    "YDBIM.AutoDimension.App.RoomContentCommand");
                roomContentData.ToolTip = "更新房間內容參數";
                roomContentData.LongDescription = "批次更新目前視圖可處理房間的內容資訊，若資料已最新則不重複寫入。";
                autoDimensionPulldown.AddPushButton(roomContentData);
            }
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
                SetButtonIcon(rotateSettingsData, "pipe_center_align_settings");
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
                SetButtonIcon(rotateCwData, "auto_avoid");
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
                SetButtonIcon(rotateCcwData, "auto_avoid");
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

            // COBie 欄位設定按鈕
            PushButtonData cobieFieldManagerData = new PushButtonData(
                "CobieFieldManager",
                "COBie\n欄位設定",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieFieldManager");
            cobieFieldManagerData.ToolTip = "COBie 欄位設定";
            cobieFieldManagerData.LongDescription = "設定自訂 COBie 匯出使用的欄位、參數對照與標準欄位檢核。(Trial+)";
            SetButtonIcon(cobieFieldManagerData, "cobie_field");
            panel.AddItem(cobieFieldManagerData);

            // COBie 樣板按鈕
            PushButtonData cobieExportTemplateData = new PushButtonData(
                "CobieExportTemplate",
                "COBie\n樣板",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieExportTemplate");
            cobieExportTemplateData.ToolTip = "COBie 樣板";
            cobieExportTemplateData.LongDescription = "匯出 COBie 欄位樣板與欄位填寫說明。(Trial+)";
            SetButtonIcon(cobieExportTemplateData, "cobie_template");
            panel.AddItem(cobieExportTemplateData);

            // 自訂 COBie 匯出按鈕
            PushButtonData cobieExportData = new PushButtonData(
                "CobieExport",
                "自訂\nCOBie",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieExportEnhanced");
            cobieExportData.ToolTip = "自訂 COBie";
            cobieExportData.LongDescription = "依「COBie 欄位設定」中的自訂欄位與參數對照規則匯出資料。(Standard+)";
            SetButtonIcon(cobieExportData, "cobie_export");
            panel.AddItem(cobieExportData);

            // COBie 標準工作簿匯出按鈕
            PushButtonData cobieStandardExportData = new PushButtonData(
                "CobieStandardExport",
                "標準\nCOBie",
                assemblyPath,
                "YD_RevitTools.LicenseManager.Commands.Data.CmdCobieStandardExport");
            cobieStandardExportData.ToolTip = "COBie 標準工作簿匯出";
            cobieStandardExportData.LongDescription =
                "依 COBie 資料交換架構匯出固定工作表。\n\n" +
                "包含 Contact、Facility、Floor、Space、Type、Component、System、Attribute、Coordinate、PickLists 與 Validation 等工作表。\n\n" +
                "此功能適合交付前檢核與資料交換格式整理。";
            SetButtonIcon(cobieStandardExportData, "cobie_export");
            panel.AddItem(cobieStandardExportData);

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

            // 明細表匯出按鈕
            if (!HasButton(panel, "ScheduleExport"))
            {
                PushButtonData scheduleExportData = new PushButtonData(
                    "ScheduleExport",
                    "明細表\n匯出",
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
                panel.AddItem(scheduleExportData);
            }

            // 模型資料管理按鈕
            if (!HasButton(panel, "ModelDataManager"))
            {
                PushButtonData modelDataManagerData = new PushButtonData(
                    "ModelDataManager",
                    "模型資料\n管理",
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
                SetButtonIcon(modelDataManagerData, "cobie_field");
                panel.AddItem(modelDataManagerData);
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
