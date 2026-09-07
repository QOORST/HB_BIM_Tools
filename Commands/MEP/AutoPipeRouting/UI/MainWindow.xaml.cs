using System;
using System.Globalization;
using System.Windows;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoPipeRouting.UI
{
    public class AutoPipeRoutingOptions
    {
        public double DistancePipeMm { get; set; } = 200.0;
        public double SlopeDenominator { get; set; } = 100.0;
    }

    public partial class MainWindow : Window
    {
        public AutoPipeRoutingOptions Options { get; } = new AutoPipeRoutingOptions();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadOptions(out string fail))
            {
                MessageBox.Show(fail, "自動配管設定", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool TryReadOptions(out string fail)
        {
            fail = string.Empty;

            if (!double.TryParse(txtDistancePipe.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double distance)
                && !double.TryParse(txtDistancePipe.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out distance))
            {
                fail = "Distance Pipe 請輸入數字。";
                return false;
            }
            if (distance < 0)
            {
                fail = "Distance Pipe 不可小於 0。";
                return false;
            }
            Options.DistancePipeMm = distance;

            if (!double.TryParse(txtSlope.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double slope)
                && !double.TryParse(txtSlope.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out slope))
            {
                fail = "Slope 請輸入數字。";
                return false;
            }
            if (slope <= 0)
            {
                fail = "Slope 需大於 0（例如 100 代表 1/100）。";
                return false;
            }
            Options.SlopeDenominator = slope;
            return true;
        }
    }
}
