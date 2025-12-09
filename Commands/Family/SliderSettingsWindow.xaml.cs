using System;
using System.Windows;

namespace YD_RevitTools.LicenseManager.Commands.Family
{
    public partial class SliderSettingsWindow : Window
    {
        public double Min { get; private set; }
        public double Max { get; private set; }
        public double Step { get; private set; }

        public SliderSettingsWindow(double currentMin, double currentMax, double currentStep)
        {
            InitializeComponent();
            MinBox.Text = currentMin.ToString();
            MaxBox.Text = currentMax.ToString();
            StepBox.Text = currentStep.ToString();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!double.TryParse(MinBox.Text, out double min))
                {
                    MessageBox.Show("最小值必須是有效數值。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!double.TryParse(MaxBox.Text, out double max))
                {
                    MessageBox.Show("最大值必須是有效數值。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!double.TryParse(StepBox.Text, out double step))
                {
                    MessageBox.Show("步進單位必須是有效數值。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 驗證邏輯關係
                if (min >= max)
                {
                    MessageBox.Show("最小值必須小於最大值。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (step <= 0)
                {
                    MessageBox.Show("步進單位必須大於 0。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (step > (max - min))
                {
                    MessageBox.Show("步進單位不能大於最大值與最小值的差。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 驗證合理範圍
                if (min < 0)
                {
                    MessageBox.Show("最小值不能為負數。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (max > 1000000)
                {
                    MessageBox.Show("最大值不能超過 1,000,000。", "輸入錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Min = min;
                Max = max;
                Step = step;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
