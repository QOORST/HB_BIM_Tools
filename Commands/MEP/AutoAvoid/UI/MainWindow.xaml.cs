using System;
using System.Windows;
using System.Windows.Controls;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.UI
{
    public partial class MainWindow : Window
    {
        public AvoidOptions Options { get; private set; } = new AvoidOptions();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = Options;
            
            // 預設值
            Options.BendAngle = 45;  // 預設 45 度
            Options.Direction = DirectionMode.Up;  // 預設向上翻彎
        }

        private void AngleChanged(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag != null)
            {
                if (double.TryParse(rb.Tag.ToString(), out double angle))
                {
                    Options.BendAngle = angle;
                }
            }
        }

        private void DirectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                string direction = btn.Tag.ToString();
                string directionText = "";
                
                switch (direction)
                {
                    case "Up":
                        Options.Direction = DirectionMode.Up;
                        directionText = "向上";
                        break;
                    case "Down":
                        Options.Direction = DirectionMode.Down;
                        directionText = "向下";
                        break;
                    case "Auto":
                        Options.Direction = DirectionMode.Auto;
                        directionText = "自動";
                        break;
                }

                // 顯示已選擇的方向
                MessageBox.Show($"已設定翻彎方向：{directionText}", "方向設定", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // 驗證輸入參數
            var (isValid, errors) = Options.Validate();
            if (!isValid)
            {
                string errorMsg = "參數驗證失敗:\n\n" + string.Join("\n", errors);
                MessageBox.Show(errorMsg, "參數錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
            this.Close();
        }
    }
}
