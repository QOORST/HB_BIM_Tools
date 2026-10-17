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
            txtSlope.TextChanged += (_, _) =>
            {
                bool valid = double.TryParse(txtSlope.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
                    && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
                SlopeHint.Text = valid ? $"  1/{value:0.##} = {100 / value:0.###}%" : "  請輸入大於 0 的分母";
            };
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
                fail = "接入前保留長度請輸入數字。";
                return false;
            }
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0)
            {
                fail = "接入前保留長度需為大於或等於 0 的有限數字。";
                return false;
            }
            Options.DistancePipeMm = distance;

            if (!double.TryParse(txtSlope.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double slope)
                && !double.TryParse(txtSlope.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out slope))
            {
                fail = "洩水坡度請輸入數字。";
                return false;
            }
            if (double.IsNaN(slope) || double.IsInfinity(slope) || slope <= 0)
            {
                fail = "坡度分母需為大於 0 的有限數字（例如 100 代表 1/100＝1%）。";
                return false;
            }
            Options.SlopeDenominator = slope;
            return true;
        }
    }
}
