using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Models;
using YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.Services;

namespace YD_RevitTools.LicenseManager.Commands.FamilyParameterRename.UI
{
    public class FamilyParameterRenameWindow : Window
    {
        private const string TitleText = "\u65cf\u53c3\u6578\u540d\u7a31\u4fee\u6539";
        private const string ConvertText = "\u8f49\u63db\u53c3\u6578\u540d\u7a31";
        private const string UpdateText = "\u66f4\u65b0\u53c3\u6578\u540d\u7a31";
        private const string FamilyText = "\u65cf\u7fa4";
        private const string OldNameText = "\u820a\u7684\u53c3\u6578\u540d\u7a31";
        private const string NewNameText = "\u65b0\u7684\u53c3\u6578\u540d\u7a31";

        private readonly FamilyParameterRenameService _service;
        private readonly ObservableCollection<ParameterRenameRow> _rows = new ObservableCollection<ParameterRenameRow>();
        private readonly ComboBox _familyCombo = new ComboBox();
        private readonly TextBlock _summary = new TextBlock();
        private readonly DataGrid _grid = new DataGrid();
        private readonly Button _updateButton = new Button();

        public FamilyParameterRenameWindow(FamilyParameterRenameService service)
        {
            _service = service;

            Title = "HB_BIM｜" + TitleText;
            Width = 760;
            Height = Math.Min(760, SystemParameters.WorkArea.Height);
            MinWidth = 640;
            MinHeight = Math.Min(480, SystemParameters.WorkArea.Height);
            FontFamily = new FontFamily("Microsoft JhengHei UI");
            FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Content = BuildLayout();
            ConfigureGrid();
            PopulateFamilies();
            RefreshButtons();
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(12) };

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var convertButton = new Button { Content = ConvertText, Width = 122, Height = 32, Margin = new Thickness(0, 0, 10, 0) };
            convertButton.Click += (_, __) => ConvertNames();
            DockPanel.SetDock(convertButton, Dock.Left);
            top.Children.Add(convertButton);

            _familyCombo.Height = 32;
            _familyCombo.MinWidth = 260;
            _familyCombo.DisplayMemberPath = nameof(FamilyRenameOption.DisplayName);
            _familyCombo.SelectionChanged += (_, __) => LoadSelectedFamily();
            top.Children.Add(_familyCombo);

            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            _summary.VerticalAlignment = VerticalAlignment.Center;
            _summary.TextWrapping = TextWrapping.Wrap;

            _updateButton.Content = UpdateText;
            _updateButton.Width = 126;
            _updateButton.Height = 38;
            _updateButton.HorizontalAlignment = HorizontalAlignment.Right;
            _updateButton.Background = new SolidColorBrush(Color.FromRgb(0, 105, 180));
            _updateButton.Foreground = Brushes.White;
            _updateButton.Click += (_, __) => UpdateNames();
            DockPanel.SetDock(_updateButton, Dock.Right);
            bottom.Children.Add(_updateButton);
            var closeButton = new Button
            {
                Content = "關閉", Width = 80, Height = 38,
                Margin = new Thickness(0, 0, 10, 0), IsCancel = true
            };
            closeButton.Click += (_, __) => Close();
            DockPanel.SetDock(closeButton, Dock.Right);
            bottom.Children.Add(closeButton);
            bottom.Children.Add(_summary);

            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            root.Children.Add(_grid);
            return root;
        }

        private void ConfigureGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.IsReadOnly = false;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            _grid.ItemsSource = _rows;

            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = OldNameText,
                MinWidth = 180,
                Binding = new Binding(nameof(ParameterRenameRow.OldName)),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });

            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = NewNameText,
                MinWidth = 180,
                Binding = new Binding(nameof(ParameterRenameRow.NewName)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "檢查訊息",
                MinWidth = 220,
                Binding = new Binding(nameof(ParameterRenameRow.Message)),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        private void PopulateFamilies()
        {
            if (_service.IsFamilyDocument)
            {
                _familyCombo.ItemsSource = new[] { new { DisplayName = "目前族群（族群編輯器）" } };
                _familyCombo.SelectedIndex = 0;
                _familyCombo.IsEnabled = false;
                LoadRows(_service.BuildRowsForCurrentFamily());
                return;
            }

            var families = _service.GetProjectFamilies().ToList();
            _familyCombo.ItemsSource = families;
            if (families.Count > 0)
            {
                _familyCombo.SelectedIndex = 0;
            }
            else
            {
                _summary.Text = "目前沒有可編輯的已載入族群。";
            }
        }

        private void LoadSelectedFamily()
        {
            if (_service.IsFamilyDocument)
            {
                return;
            }

            var option = _familyCombo.SelectedItem as FamilyRenameOption;
            if (option == null)
            {
                return;
            }

            try
            {
                LoadRows(_service.BuildRowsForProjectFamily(option.Family));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, TitleText, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRows(System.Collections.Generic.IEnumerable<ParameterRenameRow> rows)
        {
            _rows.Clear();
            foreach (var row in rows)
            {
                _rows.Add(row);
            }

            RefreshSummary();
            RefreshButtons();
        }

        private void ConvertNames()
        {
            foreach (var row in _rows)
            {
                row.NewName = FamilyParameterRenameService.SuggestName(row.OldName);
                row.Status = "";
                row.Message = "";
            }

            RefreshSummary();
            RefreshButtons();
        }

        private void UpdateNames()
        {
            if (!_grid.CommitEdit(DataGridEditingUnit.Cell, true) ||
                !_grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
            _service.ValidateRows(_rows.ToList());
            var readyCount = _rows.Count(x => x.Status == "Ready");
            if (readyCount == 0)
            {
                MessageBox.Show(this, "沒有可更新的參數名稱，請檢查新名稱與檢查訊息。", TitleText, MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshSummary();
                RefreshButtons();
                return;
            }

            var confirm = MessageBox.Show(this, "確定更新 " + readyCount + " 個參數名稱？", TitleText, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                var renamed = _service.IsFamilyDocument
                    ? _service.RenameCurrentFamilyParameters(_rows.ToList())
                    : _service.RenameProjectFamilyParameters((_familyCombo.SelectedItem as FamilyRenameOption)?.Family, _rows.ToList());

                MessageBox.Show(this, "已更新 " + renamed + " 個參數名稱。", TitleText, MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshSummary();
                RefreshButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, TitleText, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshSummary()
        {
            if (_rows.Count == 0)
            {
                _summary.Text = "目前沒有可修改的參數。";
                return;
            }

            _summary.Text = "共 " + _rows.Count + " 個參數";
        }

        private void RefreshButtons()
        {
            _updateButton.IsEnabled = _rows.Count > 0;
        }
    }
}
