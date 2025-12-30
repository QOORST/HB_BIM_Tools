using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin;
using Microsoft.Win32;

namespace YD_RevitTools.LicenseManager.UI.AutoJoin
{
    public partial class AutoJoinWindow : Window
    {
        private readonly AutoJoinSettings _s;
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private DispatcherTimer _scanTimer;

        public AutoJoinWindow(AutoJoinSettings s, UIDocument uidoc)
        {
            InitializeComponent();
            _s = s;
            _uidoc = uidoc;
            _doc = uidoc?.Document;

            // 內建規則預設值
            cbWF.IsChecked = _s.Rule_Wall_Floor_FloorCuts;
            cbWB.IsChecked = _s.Rule_Wall_Beam_BeamCuts;
            cbWC.IsChecked = _s.Rule_Wall_Column_ColumnCuts;
            cbFC.IsChecked = _s.Rule_Floor_Column_ColumnCuts;
            cbFB.IsChecked = _s.Rule_Floor_Beam_BeamCuts;

            cbDry.IsChecked = _s.DryRun;
            cbSwitchOnly.IsChecked = _s.SwitchOnly;

            // 處理範圍 RadioButton
            if (_s.OnlyUserSelection)
                rbSelection.IsChecked = true;
            else if (_s.OnlyActiveView)
                rbCurrentView.IsChecked = true;
            else
                rbAllElements.IsChecked = true;

            tbInflate.Text = _s.InflateFeet.ToString("0.###");

            cbLog.IsChecked = _s.EnableCsvLog;
            tbCsv.Text = _s.CsvPath;

            // ✅ 僅保留 5 大類
            var cats = new[]
            {
                new { Name = "結構基礎", Val = BuiltInCategory.OST_StructuralFoundation },
                new { Name = "結構構架", Val = BuiltInCategory.OST_StructuralFraming },
                new { Name = "結構柱",   Val = BuiltInCategory.OST_StructuralColumns },
                new { Name = "樓板",     Val = BuiltInCategory.OST_Floors },
                new { Name = "牆",       Val = BuiltInCategory.OST_Walls }
            }.ToList();

            cbCatA.ItemsSource = cats;
            cbCatA.DisplayMemberPath = "Name";
            cbCatA.SelectedValuePath = "Val";
            cbCatA.SelectedIndex = 0;

            cbCatB.ItemsSource = cats;
            cbCatB.DisplayMemberPath = "Name";
            cbCatB.SelectedValuePath = "Val";
            cbCatB.SelectedIndex = 1;

            // 載入已存在的自訂配對
            foreach (var pair in _s.CustomPairs)
            {
                var catAName = GetCategoryName(pair.A);
                var catBName = GetCategoryName(pair.B);
                lbPairs.Items.Add($"{catAName} → {catBName}");
            }

            // 初始化掃描
            ScanElements();

            // 設定自動更新計時器（每 2 秒更新一次）
            _scanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _scanTimer.Tick += (sender, args) => ScanElements();
            _scanTimer.Start();
        }

        /// <summary>
        /// 掃描元素數量
        /// </summary>
        private void ScanElements()
        {
            if (_doc == null) return;

            try
            {
                var counts = new Dictionary<string, int>();
                var categories = new[]
                {
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_StructuralFoundation
                };

                int total = 0;
                foreach (var category in categories)
                {
                    var count = new FilteredElementCollector(_doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .ToElements()
                        .Count(JoinGeometryHelper.IsJoinable);

                    counts[GetCategoryName(category)] = count;
                    total += count;
                }

                tbElementCount.Text = $"📊 共 {total} 個可接合元素";
                tbStatus.Text = $"柱:{counts["結構柱"]} | 梁:{counts["結構構架"]} | 板:{counts["樓板"]} | 牆:{counts["牆"]} | 基礎:{counts["結構基礎"]}";
            }
            catch
            {
                tbElementCount.Text = "掃描失敗";
                tbStatus.Text = "無法讀取元素資訊";
            }
        }

        /// <summary>
        /// 取得類別名稱
        /// </summary>
        private string GetCategoryName(BuiltInCategory cat)
        {
            switch (cat)
            {
                case BuiltInCategory.OST_StructuralColumns:
                    return "結構柱";
                case BuiltInCategory.OST_StructuralFraming:
                    return "結構構架";
                case BuiltInCategory.OST_Floors:
                    return "樓板";
                case BuiltInCategory.OST_Walls:
                    return "牆";
                case BuiltInCategory.OST_StructuralFoundation:
                    return "結構基礎";
                default:
                    return cat.ToString();
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ScanElements();
        }

        private void btnAddPair_Click(object sender, RoutedEventArgs e)
        {
            if (cbCatA.SelectedValue is BuiltInCategory a && cbCatB.SelectedValue is BuiltInCategory b)
            {
                if (a == b)
                {
                    MessageBox.Show("無法將相同類別設定為接合配對", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _s.CustomPairs.Add((a, b));
                lbPairs.Items.Add($"{cbCatA.Text} → {cbCatB.Text}");
            }
            else
            {
                MessageBox.Show("請選擇兩個類別", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnRemovePair_Click(object sender, RoutedEventArgs e)
        {
            int idx = lbPairs.SelectedIndex;
            if (idx >= 0 && idx < _s.CustomPairs.Count)
            {
                _s.CustomPairs.RemoveAt(idx);
                lbPairs.Items.RemoveAt(idx);
            }
            else
            {
                MessageBox.Show("請先選擇要移除的配對", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnBrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV 檔案 (*.csv)|*.csv|所有檔案 (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = "AutoJoinLog.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                tbCsv.Text = dialog.FileName;
            }
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            // 停止計時器
            _scanTimer?.Stop();

            // 儲存設定
            _s.Rule_Wall_Floor_FloorCuts = cbWF.IsChecked == true;
            _s.Rule_Wall_Beam_BeamCuts = cbWB.IsChecked == true;
            _s.Rule_Wall_Column_ColumnCuts = cbWC.IsChecked == true;
            _s.Rule_Floor_Column_ColumnCuts = cbFC.IsChecked == true;
            _s.Rule_Floor_Beam_BeamCuts = cbFB.IsChecked == true;

            _s.DryRun = cbDry.IsChecked == true;
            _s.SwitchOnly = cbSwitchOnly.IsChecked == true;

            // 處理範圍
            _s.OnlyActiveView = rbCurrentView.IsChecked == true;
            _s.OnlyUserSelection = rbSelection.IsChecked == true;

            if (double.TryParse(tbInflate.Text, out double f))
                _s.InflateFeet = Math.Max(0, f);
            else
                _s.InflateFeet = 1.0;

            // 近距離偵測設定
            _s.DetectNearMisses = cbDetectNearMisses.IsChecked == true;
            if (double.TryParse(tbProximityTolerance.Text, out double tolerance))
                _s.ProximityToleranceMm = Math.Max(0, Math.Min(50, tolerance)); // 限制在 0-50mm
            else
                _s.ProximityToleranceMm = 5.0;

            _s.EnableCsvLog = cbLog.IsChecked == true;
            _s.CsvPath = tbCsv.Text?.Trim();

            // 驗證設定
            if (!_s.Rule_Wall_Floor_FloorCuts &&
                !_s.Rule_Wall_Beam_BeamCuts &&
                !_s.Rule_Wall_Column_ColumnCuts &&
                !_s.Rule_Floor_Column_ColumnCuts &&
                _s.CustomPairs.Count == 0)
            {
                MessageBox.Show("請至少選擇一個接合規則或建立自訂配對", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            // 停止計時器
            _scanTimer?.Stop();

            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // 確保計時器被停止
            _scanTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
