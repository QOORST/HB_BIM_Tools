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
        public bool HasGeneratedOutput { get; private set; }

        public MainWindow(Document doc, UIDocument uidoc)
        {
            InitializeComponent();
            
            _doc = doc;
            _uidoc = uidoc;

            // 設定預設輸出路徑
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string defaultPath = Path.Combine(documentsPath, "Revit_ISO_Export");
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
                _selectedSystem = null;
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

            if ((ExportPCFCheckBox.IsChecked == true || ExportBOMCheckBox.IsChecked == true ||
                 ExportImageCheckBox.IsChecked == true) && string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
            {
                Logger.Warning("未選擇輸出路徑");
                MessageBox.Show("請選擇輸出路徑。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (GenerateDimensionReviewCheckBox.IsChecked != true && !GenerateISOViewCheckBox.IsChecked.Value &&
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
                GenerationReport report = PerformGeneration();
                HasGeneratedOutput |= report.HasSuccess;

                // 隱藏進度
                ShowProgress(false);
                GenerateButton.IsEnabled = true;

                string message = report + "\n\n日誌：" + Logger.GetLogFilePath();
                if (report.HasFailures && report.HasSuccess)
                    message += "\n\n已完成的成果會保留；重新生成會新增視圖／明細表，並覆寫同名輸出檔案。";
                Logger.Info(message);
                MessageBox.Show(message, report.HasFailures ? (report.HasSuccess ? "處理完成，部分項目失敗" : "處理失敗") : "處理結果",
                    MessageBoxButton.OK, report.HasFailures ? MessageBoxImage.Warning : MessageBoxImage.Information);
                // 讓使用者閱讀逐項結果；全部成功才關閉，部分失敗可保留已完成成果。
                if (!report.HasFailures)
                {
                    DialogResult = true;
                    Close();
                }
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
        private GenerationReport PerformGeneration()
        {
            var report = new GenerationReport((name, error) => Logger.Error(name + "失敗", error));
            if (ExportPCFCheckBox.IsChecked == true || ExportBOMCheckBox.IsChecked == true || ExportImageCheckBox.IsChecked == true)
            {
                try { OutputPreflight.Check(OutputPathTextBox.Text, ISONumberTextBox.Text.Trim()); }
                catch (Exception ex)
                {
                    Logger.Error("輸出預檢失敗", ex);
                    report.Fail("輸出預檢", ex.Message);
                    report.Skip("生成作業", "尚未建立視圖或明細表，請改選資料夾後重試");
                    return report;
                }
            }
            var isoData = new ISOData
            {
                SystemId = _selectedSystem.Id,
                SystemName = _selectedSystem.Name,
                ProjectName = _doc.ProjectInformation.Name,
                ISONumber = ISONumberTextBox.Text.Trim()
            };
            try
            {
                isoData.Snapshot = SystemSnapshot.Capture(_selectedSystem);
                report.Note("系統範圍與連通檢查", isoData.Snapshot.Summary);
            }
            catch (Exception ex)
            {
                Logger.Error("系統範圍檢查失敗", ex);
                report.Fail("系統範圍與連通檢查", ex.Message);
                return report;
            }
            var generator = new ISOGenerator(_doc);
            var reviewGenerator = new DimensionReviewGenerator(_doc);
            var exporter = new PCFExporter();
            View3D isoView = null;
            ViewDrafting dimensionView = null;
            bool framingReady = false;
            bool needView = GenerateISOViewCheckBox.IsChecked == true || ExportImageCheckBox.IsChecked == true;
            ShowProgress(true, "正在處理等角視圖...", 20);
            if (needView)
            {
                report.Run("等角視圖", () =>
                {
                    isoView = generator.GenerateISOView(isoData);
                    return isoView.Name + (GenerateISOViewCheckBox.IsChecked != true ? "（供 PNG 匯出使用）" : "");
                });
                if (isoView != null)
                {
                    report.Run("管線標籤", () => generator.AddAnnotations(isoView, isoData));
                    framingReady = report.Run("完整取景", () => generator.FitView(isoView, isoData));
                    try { _uidoc.ActiveView = isoView; }
                    catch (Exception ex) { Logger.Warning("切換視圖失敗：" + ex.Message); }
                }
                else report.Skip("管線標籤", "等角視圖建立失敗");
            }
            else report.Skip("等角視圖／管線標籤", "未勾選");

            if (GenerateDimensionReviewCheckBox.IsChecked == true)
                report.Run("尺寸核對詳圖", () =>
                {
                    dimensionView = reviewGenerator.Create(isoData);
                    try { _uidoc.ActiveView = dimensionView; }
                    catch (Exception ex) { Logger.Warning("切換核對詳圖失敗：" + ex.Message); }
                    return dimensionView.Name + "；" + reviewGenerator.LayoutSummary + "（靜態投影）";
                });

            ShowProgress(true, "正在處理材料與檔案...", 60);
            if (ExportBOMCheckBox.IsChecked == true)
            {
                report.Run("離線重播資料 JSON", () =>
                {
                    var scene = reviewGenerator.Scene ?? reviewGenerator.CaptureScene(isoData);
                    string path = GetOutputFilePath(isoData.ISONumber, "_Review.json");
                    AtomicOutput.Write(path, writer => writer.Write(Newtonsoft.Json.JsonConvert.SerializeObject(scene,
                        Newtonsoft.Json.Formatting.Indented)));
                    return path;
                });
                if (reviewGenerator.Scene != null) report.Run("離線核對預覽 SVG", () =>
                {
                    string path = GetOutputFilePath(isoData.ISONumber, "_Review.svg");
                    string svg = ReviewSvg.Render(reviewGenerator.Scene);
                    AtomicOutput.Write(path, writer => writer.Write(svg));
                    return path + "（排版可離線重播；字型外觀以 Revit 為準）";
                });
                report.Run("逐管尺寸 CSV", () =>
                {
                    string pipePath = GetOutputFilePath(isoData.ISONumber, "_Pipes.csv");
                    isoData.Snapshot.ExportPipes(_doc, isoData.SystemName, pipePath);
                    return pipePath;
                });
                report.Run("逐件核對表 CSV", () =>
                {
                    string auditPath = GetOutputFilePath(isoData.ISONumber, "_Audit.csv");
                    isoData.Snapshot.ExportAudit(_doc, isoData.SystemName, auditPath);
                    return auditPath;
                });
                report.Run("材料清單 CSV", () =>
                {
                    isoData.GenerateBOMFromSystem(_doc);
                    string path = GetOutputFilePath(isoData.ISONumber, "_BOM.csv");
                    exporter.ExportBOMToCSV(isoData, path);
                    return path + $"（{isoData.BillOfMaterials.Sum(b => b.Quantity)} 件，模型管長 {isoData.TotalLength / 1000:0.###} m；非加工切長）";
                });
            }
            else report.Skip("材料清單 CSV", "未勾選");

            if (ExportPCFCheckBox.IsChecked == true)
            {
                report.Fail("PCF", "已暫停舊版匯出，待實際管件端點、接合與加工規則完成驗證後開放。");
            }
            else report.Skip("PCF（實驗性）", "未勾選");

            if (ExportImageCheckBox.IsChecked == true)
            {
                if (dimensionView != null) report.Run("尺寸核對詳圖 PNG", () =>
                {
                    generator.ExportViewAsImage(dimensionView, GetOutputFilePath(isoData.ISONumber, "_尺寸核對"));
                    return "已匯出至 " + OutputPathTextBox.Text;
                });
                if (isoView == null) report.Skip("PNG", "等角視圖建立失敗");
                else if (!framingReady) report.Skip("PNG", "完整取景失敗，避免匯出遭裁切的圖片");
                else report.Run("PNG", () =>
                {
                    string path = GetOutputFilePath(isoData.ISONumber, "");
                    generator.ExportViewAsImage(isoView, path);
                    return "已匯出至 " + OutputPathTextBox.Text + "（檔名由 Revit 加入視圖名稱）";
                });
            }
            else report.Skip("PNG", "未勾選");

            ShowProgress(true, "正在建立明細表...", 90);
            if (GenerateScheduleCheckBox.IsChecked == true)
            {
                report.Run("Revit 明細表", () =>
                {
                    var schedule = new ScheduleGenerator(_doc).CreateBOMSchedule(isoData);
                    return schedule.Name + " 與配件表（不含管路附件／設備）";
                });
            }
            else report.Skip("Revit 明細表", "未勾選");
            ShowProgress(true, "處理完成", 100);
            return report;
        }

        private string GetOutputFilePath(string number, string suffix)
        {
            if (string.IsNullOrWhiteSpace(number) || number.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                number.EndsWith(".") || number.EndsWith(" ") || number == "." || number == "..")
                throw new InvalidOperationException("ISO 編號不可空白或包含檔名不允許的字元。");
            Directory.CreateDirectory(OutputPathTextBox.Text);
            return Path.Combine(OutputPathTextBox.Text, number + suffix);
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
