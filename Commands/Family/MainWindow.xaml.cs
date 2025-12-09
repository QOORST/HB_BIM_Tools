using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace YD_RevitTools.LicenseManager.Commands.Family
{
    // ExternalEvent 處理器
    public class FamilyParameterUpdateHandler : IExternalEventHandler
    {
        public FamilyParameter ParameterToUpdate { get; set; }
        public double ValueToSet { get; set; }
        public FamilyManager FamilyManager { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                Document doc = app.ActiveUIDocument.Document;
                using (Transaction t = new Transaction(doc, "更新參數"))
                {
                    t.Start();
                    FamilyManager.Set(ParameterToUpdate, ValueToSet);
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"更新參數失敗：{ex.Message}");
            }
        }

        public string GetName()
        {
            return "族參數更新器";
        }
    }

    public partial class MainWindow : Window
    {
        private readonly Document _doc;
        private readonly FamilyManager _fm;
        private double sliderMin = 1;
        private double sliderMax = 10000;
        private double sliderStep = 0.1;
        private DispatcherTimer _updateTimer;
        private readonly FamilyParameterUpdateHandler _updateHandler;
        private readonly ExternalEvent _externalEvent;

        public MainWindow(ExternalCommandData commandData)
        {
            InitializeComponent();
            
            try
            {
                _doc = commandData.Application.ActiveUIDocument.Document;

                if (!_doc.IsFamilyDocument)
                {
                    MessageBox.Show("請在族編輯器中執行此工具。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Close();
                    return;
                }

                _fm = _doc.FamilyManager;
                
                // 初始化 ExternalEvent
                _updateHandler = new FamilyParameterUpdateHandler
                {
                    FamilyManager = _fm
                };
                _externalEvent = ExternalEvent.Create(_updateHandler);
                
                // 初始化延遲更新計時器（100ms 快速且穩定）
                _updateTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                
                TypeLabel.Text = $"當前類型：{_fm.CurrentType.Name}";
                RefreshSliders();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _externalEvent?.Dispose();
        }

        private void RefreshSliders()
        {
            try
            {
                MainStack.Children.Clear();

                var lengthParams = _fm.Parameters.Cast<FamilyParameter>()
                    .Where(p => p.Definition.GetDataType() == SpecTypeId.Length && !p.IsInstance)
                    .ToList();

                if (lengthParams.Count == 0)
                {
                    MessageBox.Show("此族類型中沒有可編輯的長度類型參數。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 取得當前族類型對應的 Element
                foreach (var param in lengthParams)
                {
                    // 直接從 FamilyParameter 取得值
                    double valueFeet = _fm.CurrentType.AsDouble(param) ?? 0;
                    double valueMm = UnitUtils.ConvertFromInternalUnits(valueFeet, UnitTypeId.Millimeters);

                    var label = new TextBlock 
                    { 
                        Text = param.Definition.Name,
                        Margin = new Thickness(0, 5, 0, 2)
                    };
                    
                    var slider = new Slider
                    {
                        Minimum = sliderMin,
                        Maximum = sliderMax,
                        TickFrequency = sliderStep,
                        Value = valueMm,
                        Margin = new Thickness(0, 0, 5, 0),
                        Width = 180
                    };
                    
                    var valueBox = new System.Windows.Controls.TextBox 
                    { 
                        Text = valueMm.ToString("F2"), 
                        Width = 80,
                        Margin = new Thickness(5, 0, 0, 0)
                    };

                    // 滑桿拖動時延遲更新
                    slider.ValueChanged += (s, e) =>
                    {
                        valueBox.Text = slider.Value.ToString("F2");
                        
                        // 停止舊的計時器
                        _updateTimer.Stop();
                        _updateTimer.Tick -= null;
                        
                        // 設定新的更新事件
                        _updateTimer.Tick += (sender, args) =>
                        {
                            _updateTimer.Stop();
                            UpdateParameter(param, slider.Value);
                        };
                        
                        _updateTimer.Start();
                    };

                    // 文字框輸入時更新滑桿和參數
                    valueBox.KeyDown += (s, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Enter)
                        {
                            if (double.TryParse(valueBox.Text, out double newValue))
                            {
                                if (newValue >= sliderMin && newValue <= sliderMax)
                                {
                                    slider.Value = newValue;
                                }
                                else
                                {
                                    MessageBox.Show($"數值必須在 {sliderMin} 到 {sliderMax} 之間。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    valueBox.Text = slider.Value.ToString("F2");
                                }
                            }
                            else
                            {
                                MessageBox.Show("請輸入有效的數值。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                                valueBox.Text = slider.Value.ToString("F2");
                            }
                        }
                    };

                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    panel.Children.Add(slider);
                    panel.Children.Add(valueBox);

                    MainStack.Children.Add(label);
                    MainStack.Children.Add(panel);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新滑桿失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateParameter(FamilyParameter param, double valueMm)
        {
            try
            {
                double newValFeet = UnitUtils.ConvertToInternalUnits(valueMm, UnitTypeId.Millimeters);
                
                // 設定要更新的參數和值
                _updateHandler.ParameterToUpdate = param;
                _updateHandler.ValueToSet = newValFeet;
                
                // 觸發 ExternalEvent
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新參數 '{param.Definition.Name}' 失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new SliderSettingsWindow(sliderMin, sliderMax, sliderStep);
                if (settings.ShowDialog() == true)
                {
                    sliderMin = settings.Min;
                    sliderMax = settings.Max;
                    sliderStep = settings.Step;
                    RefreshSliders();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"開啟設定失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TypeLabel.Text = $"當前類型：{_fm.CurrentType.Name}";
                RefreshSliders();
                MessageBox.Show("參數已刷新", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
