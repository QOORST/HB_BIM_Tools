using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YD_RevitTools.LicenseManager.UI
{
    public partial class LicenseWindow : Window
    {
        private readonly LicenseManager _licenseManager;

        public LicenseWindow()
        {
            InitializeComponent();
            _licenseManager = LicenseManager.Instance;
            LoadLicenseInfo();
        }

        /// <summary>
        /// 載入授權資訊並更新 UI
        /// </summary>
        private void LoadLicenseInfo()
        {
            var license = _licenseManager.GetCurrentLicense();
            var validation = _licenseManager.ValidateLicense();
            var machineCode = _licenseManager.GetMachineCode();

            // 更新機器碼（總是顯示）
            txtMachineCode.Text = machineCode;

            if (license == null || !license.IsEnabled)
            {
                // 未啟用狀態
                UpdateUIForNoLicense();
                return;
            }

            // 已啟用狀態
            UpdateUIForActiveLicense(license, validation);
        }

        /// <summary>
        /// 更新 UI 為未啟用狀態
        /// </summary>
        private void UpdateUIForNoLicense()
        {
            statusIndicator.Fill = new SolidColorBrush(Colors.Gray);
            txtStatus.Text = "未啟用";
            txtStatus.Foreground = new SolidColorBrush(Colors.Gray);
            
            txtLicenseType.Text = "-";
            txtUserName.Text = "-";
            txtCompany.Text = "-";
            txtStartDate.Text = "-";
            txtExpiryDate.Text = "-";
            txtDaysRemaining.Text = "";
            txtLicenseKeyDisplay.Text = "-";

            licenseTypeBadge.Visibility = Visibility.Collapsed;
            licenseMessageBorder.Visibility = Visibility.Visible;
            txtLicenseMessage.Text = "尚未啟用授權。請在「啟用授權」頁籤中輸入授權金鑰。";

            // 清空功能列表
            featureList.Children.Clear();
            featureList.Children.Add(new TextBlock 
            { 
                Text = "請先啟用授權以查看可用功能",
                Foreground = new SolidColorBrush(Colors.Gray),
                FontStyle = FontStyles.Italic
            });
        }

        /// <summary>
        /// 更新 UI 為已啟用狀態
        /// </summary>
        private void UpdateUIForActiveLicense(LicenseInfo license, LicenseValidationResult validation)
        {
            // 狀態指示器
            if (validation.IsValid)
            {
                statusIndicator.Fill = new SolidColorBrush(Colors.Green);
                txtStatus.Text = "已啟用";
                txtStatus.Foreground = new SolidColorBrush(Colors.Green);
                licenseMessageBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                statusIndicator.Fill = new SolidColorBrush(Colors.Red);
                txtStatus.Text = "已過期";
                txtStatus.Foreground = new SolidColorBrush(Colors.Red);
                licenseMessageBorder.Visibility = Visibility.Visible;
                txtLicenseMessage.Text = validation.Message;
            }

            // 授權類型
            txtLicenseType.Text = license.GetLicenseTypeName();
            licenseTypeBadge.Visibility = Visibility.Visible;
            txtLicenseTypeBadge.Text = $"{license.GetLicenseDuration()} 天";

            // 設定授權類型徽章顏色
            switch (license.LicenseType)
            {
                case LicenseType.Trial:
                    licenseTypeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800"));
                    break;
                case LicenseType.Standard:
                    licenseTypeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3"));
                    break;
                case LicenseType.Professional:
                    licenseTypeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                    break;
            }

            // 使用者資訊
            txtUserName.Text = license.UserName ?? "-";
            txtCompany.Text = license.Company ?? "-";
            txtStartDate.Text = license.StartDate.ToString("yyyy-MM-dd");
            txtExpiryDate.Text = license.ExpiryDate.ToString("yyyy-MM-dd");

            // 剩餘天數
            int daysRemaining = (license.ExpiryDate - DateTime.Now).Days;
            if (daysRemaining > 0)
            {
                txtDaysRemaining.Text = $"(剩餘 {daysRemaining} 天)";
                txtDaysRemaining.Foreground = daysRemaining < 30 
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));
            }
            else
            {
                txtDaysRemaining.Text = "(已過期)";
                txtDaysRemaining.Foreground = new SolidColorBrush(Colors.Red);
            }

            // 授權金鑰（顯示前後部分）
            if (!string.IsNullOrEmpty(license.LicenseKey))
            {
                string key = license.LicenseKey;
                if (key.Length > 40)
                {
                    txtLicenseKeyDisplay.Text = $"{key.Substring(0, 20)}...{key.Substring(key.Length - 20)}";
                }
                else
                {
                    txtLicenseKeyDisplay.Text = key;
                }
            }
            else
            {
                txtLicenseKeyDisplay.Text = "-";
            }

            // 更新功能列表
            UpdateFeatureList(license.LicenseType);
        }

        /// <summary>
        /// 更新功能列表
        /// </summary>
        private void UpdateFeatureList(LicenseType licenseType)
        {
            featureList.Children.Clear();

            var features = _licenseManager.GetAvailableFeatures();

            if (features.Count == 0)
            {
                featureList.Children.Add(new TextBlock
                {
                    Text = "無可用功能",
                    Foreground = new SolidColorBrush(Colors.Gray)
                });
                return;
            }

            // 如果是專業版（包含 "*"），顯示所有功能
            if (features.Contains("*"))
            {
                featureList.Children.Add(CreateFeatureItem("✓ 所有功能", true));
                return;
            }

            // 顯示具體功能
            foreach (var feature in features)
            {
                string displayName = GetFeatureDisplayName(feature);
                featureList.Children.Add(CreateFeatureItem(displayName, true));
            }
        }

        /// <summary>
        /// 創建功能項目 UI 元素
        /// </summary>
        private UIElement CreateFeatureItem(string text, bool isEnabled)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 2, 0, 2),
                Foreground = isEnabled
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"))
                    : new SolidColorBrush(Colors.Gray)
            };
            return textBlock;
        }

        /// <summary>
        /// 取得功能的顯示名稱
        /// </summary>
        private string GetFeatureDisplayName(string featureName)
        {
            var displayNames = new System.Collections.Generic.Dictionary<string, string>
            {
                ["AR.Finishings"] = "✓ 裝修工具",
                ["AR.AutoJoin"] = "✓ 自動接合工具",
                ["Data.COBie.FieldManager"] = "✓ COBie 欄位管理",
                ["Data.COBie.Export"] = "✓ COBie 匯出",
                ["Data.COBie.Import"] = "✓ COBie 匯入",
                ["Data.COBie.Template"] = "✓ COBie 範本",
                ["Family.ParameterSlider"] = "✓ 族參數滑桿",
                ["Family.ProjectSlider"] = "✓ 專案參數滑桿",
                ["MEP.PipeSleeve"] = "✓ 管線套管",
                ["MEP.PipeToISO"] = "✓ 管線轉 ISO",
                ["MEP.AutoAvoid"] = "✓ 管線自動避讓"
            };

            return displayNames.ContainsKey(featureName)
                ? displayNames[featureName]
                : $"✓ {featureName}";
        }

        /// <summary>
        /// 複製機器碼按鈕事件
        /// </summary>
        private void BtnCopyMachineCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(txtMachineCode.Text);
                MessageBox.Show("機器碼已複製到剪貼簿！", "成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"複製失敗：{ex.Message}", "錯誤",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 啟用授權按鈕事件
        /// </summary>
        private void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            string licenseKey = txtLicenseKey.Text.Trim();

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                MessageBox.Show("請輸入授權金鑰！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 驗證並啟用授權
                var result = _licenseManager.ActivateLicense(licenseKey);

                if (result.IsValid)
                {
                    MessageBox.Show(
                        $"授權啟用成功！\n\n" +
                        $"授權類型：{result.LicenseInfo.GetLicenseTypeName()}\n" +
                        $"使用者：{result.LicenseInfo.UserName}\n" +
                        $"到期日期：{result.LicenseInfo.ExpiryDate:yyyy-MM-dd}",
                        "成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // 重新載入授權資訊
                    LoadLicenseInfo();

                    // 清空輸入框
                    txtLicenseKey.Clear();
                }
                else
                {
                    MessageBox.Show(
                        $"授權啟用失敗！\n\n{result.Message}",
                        "錯誤",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"授權啟用時發生錯誤：\n\n{ex.Message}",
                    "錯誤",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 清除按鈕事件
        /// </summary>
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtLicenseKey.Clear();
        }

        /// <summary>
        /// 重新整理按鈕事件
        /// </summary>
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadLicenseInfo();
        }

        /// <summary>
        /// 關閉按鈕事件
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

