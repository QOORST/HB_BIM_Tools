using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin;

namespace YD_RevitTools.LicenseManager.UI.AutoJoin
{
    public partial class AutoJoinWindow : Window
    {
        private readonly AutoJoinSettings _s;
        private readonly UIDocument _uidoc;

        public AutoJoinWindow(AutoJoinSettings s, UIDocument uidoc)
        {
            InitializeComponent();
            _s = s;
            _uidoc = uidoc;

            // 內建規則預設值
            cbWF.IsChecked = _s.Rule_Wall_Floor_FloorCuts;
            cbWB.IsChecked = _s.Rule_Wall_Beam_BeamCuts;
            cbFC.IsChecked = _s.Rule_Floor_Column_ColumnCuts;

            cbDry.IsChecked = _s.DryRun;
            cbSwitchOnly.IsChecked = _s.SwitchOnly;
            cbOnlyView.IsChecked = _s.OnlyActiveView;
            cbOnlySel.IsChecked = _s.OnlyUserSelection;
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

            cbCatB.ItemsSource = cats;
            cbCatB.DisplayMemberPath = "Name";
            cbCatB.SelectedValuePath = "Val";
        }

        private void btnAddPair_Click(object sender, RoutedEventArgs e)
        {
            if (cbCatA.SelectedValue is BuiltInCategory a && cbCatB.SelectedValue is BuiltInCategory b)
            {
                _s.CustomPairs.Add((a, b));
                lbPairs.Items.Add($"{cbCatA.Text} → {cbCatB.Text}");
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
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            _s.Rule_Wall_Floor_FloorCuts = cbWF.IsChecked == true;
            _s.Rule_Wall_Beam_BeamCuts = cbWB.IsChecked == true;
            _s.Rule_Floor_Column_ColumnCuts = cbFC.IsChecked == true;

            _s.DryRun = cbDry.IsChecked == true;
            _s.SwitchOnly = cbSwitchOnly.IsChecked == true;
            _s.OnlyActiveView = cbOnlyView.IsChecked == true;
            _s.OnlyUserSelection = cbOnlySel.IsChecked == true;

            if (double.TryParse(tbInflate.Text, out double f)) _s.InflateFeet = Math.Max(0, f);

            _s.EnableCsvLog = cbLog.IsChecked == true;
            _s.CsvPath = tbCsv.Text?.Trim();

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
