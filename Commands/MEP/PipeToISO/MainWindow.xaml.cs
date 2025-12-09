using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯
    /// </summary>
    public partial class MainWindow : Window
    {
        private Document _doc;
        private UIDocument _uidoc;
        private List<PipingSystem> _pipingSystems;
        private PipingSystem _selectedSystem;

        public MainWindow(Document doc, UIDocument uidoc)
        {
            InitializeComponent();
            
            _doc = doc;
            _uidoc = uidoc;

            // 設定預設輸出路徑
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string defaultPath = Path.Combine(documentsPath, "Revit_ISO_Export");
            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }
            OutputPathTextBox.Text = defaultPath;

            // 載入管線系統
            LoadPipingSystems();
        }

        /// <summary>
        /// 載入所有管線系統
        /// </summary>
        private void LoadPipingSystems()
        {
            try
            {
                _pipingSystems = PipeToISOCommand.GetAllPipingSystems(_doc);

                SystemComboBox.ItemsSource = _pipingSystems;

                if (_pipingSystems.Count > 0)
                {
                    SystemComboBox.SelectedIndex = 0;
                }
                else
                {
                    MessageBox.Show("專案中沒有找到管線系統。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"載入管線系統失敗：\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 管線系統選擇變更
        /// </summary>
        private void SystemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SystemComboBox.SelectedItem is PipingSystem system)
            {
                _selectedSystem = system;

                // 顯示系統資訊
                var elements = PipeToISOCommand.GetPipeSystemElements(system);
                int pipeCount = elements.Count(elem => elem is Pipe);
                int fittingCount = elements.Count(elem => PipeToISOCommand.IsPipeFitting(elem));

                SystemInfoText.Text = $"系統包含 {pipeCount} 根管線和 {fittingCount} 個管配件";

                // 生成預設 ISO 編號
                string date = DateTime.Now.ToString("yyyyMMdd");
                ISONumberTextBox.Text = $"ISO-{system.Name}-{date}";
            }
        }

        /// <summary>
        /// 重新整理按鈕
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadPipingSystems();
        }

        /// <summary>
        /// 瀏覽資料夾按鈕
        /// </summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "選擇輸出資料夾",
                SelectedPath = OutputPathTextBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputPathTextBox.Text = dialog.SelectedPath;
            }
        }

        /// <summary>
        /// 開始生成按鈕
        /// </summary>
        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("========== 開始生成 ISO 流程 ==========");
            Logger.Info($"選擇的系統: {_selectedSystem?.Name ?? "未選擇"}");
            Logger.Info($"輸出路徑: {OutputPathTextBox.Text}");
            
            if (_selectedSystem == null)
            {
                Logger.Warning("未選擇管線系統");
                MessageBox.Show("請先選擇管線系統。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
            {
                Logger.Warning("未選擇輸出路徑");
                MessageBox.Show("請選擇輸出路徑。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!GenerateISOViewCheckBox.IsChecked.Value && 
                !ExportPCFCheckBox.IsChecked.Value && 
                !ExportBOMCheckBox.IsChecked.Value &&
                !ExportImageCheckBox.IsChecked.Value &&
                !GenerateScheduleCheckBox.IsChecked.Value)
            {
                Logger.Warning("未選擇任何輸出選項");
                MessageBox.Show("請至少選擇一個輸出選項。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 顯示進度
                ShowProgress(true, "開始處理...");
                GenerateButton.IsEnabled = false;

                // 執行生成
                PerformGeneration();

                // 隱藏進度
                ShowProgress(false);
                GenerateButton.IsEnabled = true;

                // 完成提示
                string logPath = Logger.GetLogFilePath();
                string message = "ISO 圖與 PCF 檔案已成功生成！\n\n";
                message += $"輸出位置：{OutputPathTextBox.Text}\n\n";
                message += $"日誌檔案：{logPath}";
                
                Logger.Info("========== 流程完成 ==========");
                
                MessageBox.Show(message, "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                // 關閉視窗
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                ShowProgress(false);
                GenerateButton.IsEnabled = true;

                Logger.Error("生成過程發生錯誤", ex);
                
                string logPath = Logger.GetLogFilePath();
                string errorMessage = $"生成失敗：\n{ex.Message}\n\n";
                errorMessage += $"詳細錯誤資訊請查看日誌檔案：\n{logPath}";
                
                MessageBox.Show(errorMessage, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 執行生成作業
        /// </summary>
        private void PerformGeneration()
        {
            Logger.Info("===== 開始執行生成作業 =====");
            
            // 1. 分析管線系統
            ShowProgress(true, "正在分析管線系統...", 20);
            Logger.Info("步驟 1: 分析管線系統");
            
            PipeAnalyzer analyzer = new PipeAnalyzer(_doc);
            ISOData isoData = null;
            
            try
            {
                isoData = analyzer.AnalyzePipingSystem(_selectedSystem);
                Logger.Info($"系統分析完成 - 主管: {isoData.MainPipeSegments.Count}, 分支: {isoData.BranchSegments.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error("分析管線系統失敗", ex);
                throw;
            }

            // 設定 ISO 編號
            if (!string.IsNullOrWhiteSpace(ISONumberTextBox.Text))
            {
                isoData.ISONumber = ISONumberTextBox.Text;
            }
            Logger.Info($"ISO 編號: {isoData.ISONumber}");

            // 重新從系統生成完整 BOM(確保收集所有元件)
            Logger.Info("從系統重新生成完整 BOM");
            try
            {
                isoData.GenerateBOMFromSystem(_doc);
                Logger.Info($"BOM 生成完成,共 {isoData.BillOfMaterials.Count} 個項目");
            }
            catch (Exception ex)
            {
                Logger.Warning($"使用系統生成 BOM 失敗,使用備用方法: {ex.Message}");
            }

            // 2. 生成 ISO 視圖
            View3D isoView = null;
            if (GenerateISOViewCheckBox.IsChecked.Value)
            {
                ShowProgress(true, "正在生成 ISO 視圖...", 40);
                Logger.Info("步驟 2: 生成 ISO 視圖");
                
                ISOGenerator generator = new ISOGenerator(_doc);
                
                try
                {
                    isoView = generator.GenerateISOView(isoData);
                    Logger.Info($"ISO 視圖建立成功: {isoView.Name}");
                }
                catch (Exception ex)
                {
                    Logger.Error("生成 ISO 視圖失敗", ex);
                    throw;
                }
                
                // 添加標註
                ShowProgress(true, "正在添加標註...", 50);
                Logger.Info("步驟 2.1: 添加標註");
                
                try
                {
                    generator.AddAnnotations(isoView, isoData);
                    Logger.Info("標註添加成功");
                }
                catch (Exception ex)
                {
                    Logger.Error("添加標註時發生錯誤", ex);
                    // 標註失敗不影響主流程
                }
                
                // 設定為當前視圖
                try
                {
                    _uidoc.ActiveView = isoView;
                    Logger.Info("已切換到 ISO 視圖");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"切換視圖失敗: {ex.Message}");
                }
            }

            // 3. 匯出 PCF
            if (ExportPCFCheckBox.IsChecked.Value)
            {
                ShowProgress(true, "正在匯出 PCF 檔案...", 60);
                Logger.Info("步驟 3: 匯出 PCF");
                
                string pcfPath = Path.Combine(OutputPathTextBox.Text, $"{isoData.ISONumber}.pcf");
                PCFExporter exporter = new PCFExporter();
                
                try
                {
                    exporter.ExportToPCF(isoData, pcfPath);
                    Logger.Info($"PCF 匯出成功: {pcfPath}");
                }
                catch (Exception ex)
                {
                    Logger.Error("PCF 匯出失敗", ex);
                    MessageBox.Show($"PCF 匯出失敗：{ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // 4. 匯出材料清單
            if (ExportBOMCheckBox.IsChecked.Value)
            {
                ShowProgress(true, "正在匯出材料清單...", 80);
                Logger.Info("步驟 4: 匯出 BOM");
                
                string bomPath = Path.Combine(OutputPathTextBox.Text, $"{isoData.ISONumber}_BOM.csv");
                PCFExporter exporter = new PCFExporter();
                
                try
                {
                    exporter.ExportBOMToCSV(isoData, bomPath);
                    Logger.Info($"BOM 匯出成功: {bomPath}");
                }
                catch (Exception ex)
                {
                    Logger.Error("BOM 匯出失敗", ex);
                    MessageBox.Show($"BOM 匯出失敗：{ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // 5. 匯出圖片
            if (ExportImageCheckBox.IsChecked.Value && isoView != null)
            {
                ShowProgress(true, "正在匯出視圖圖片...", 85);
                Logger.Info("步驟 5: 匯出圖片");
                
                string imagePath = Path.Combine(OutputPathTextBox.Text, isoData.ISONumber);
                ISOGenerator generator = new ISOGenerator(_doc);
                
                try
                {
                    generator.ExportViewAsImage(isoView, imagePath);
                    Logger.Info($"圖片匯出成功: {imagePath}.png");
                }
                catch (Exception ex)
                {
                    Logger.Error("圖片匯出失敗", ex);
                    MessageBox.Show($"圖片匯出失敗：{ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // 6. 建立 Revit 明細表
            if (GenerateScheduleCheckBox.IsChecked.Value)
            {
                ShowProgress(true, "正在建立 Revit 明細表...", 90);
                Logger.Info("步驟 6: 建立 Revit 明細表");

                ScheduleGenerator scheduleGenerator = new ScheduleGenerator(_doc);

                try
                {
                    ViewSchedule schedule = scheduleGenerator.CreateBOMSchedule(isoData);
                    Logger.Info($"明細表建立成功: {schedule.Name}");
                }
                catch (Exception ex)
                {
                    Logger.Error("明細表建立失敗", ex);
                    MessageBox.Show($"明細表建立失敗：{ex.Message}\n\n這不影響其他功能的使用。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            
            Logger.Info("===== 所有操作完成 =====");
            Logger.Info($"日誌檔案位置: {Logger.GetLogFilePath()}");
            
            ShowProgress(true, "完成！", 100);
        }

        /// <summary>
        /// 顯示/隱藏進度
        /// </summary>
        private void ShowProgress(bool show, string text = "", int value = 0)
        {
            ProgressPanel.Visibility = show ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            
            if (show)
            {
                ProgressText.Text = text;
                ProgressBar.Value = value;
            }
        }

        /// <summary>
        /// 取消按鈕
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
