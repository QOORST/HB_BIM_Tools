using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;
using WpfVisibility = System.Windows.Visibility;

namespace YD_RevitTools.LicenseManager.UI.Finishings
{
    public class FinishingItemInfo
    {
        public ElementId Id { get; set; }
        public string LevelName { get; set; }
        public string RoomDisplay { get; set; }
    }

    /// <summary>
    /// 裝修刪除對話框（純 C#，避免 XAML 相依）
    /// </summary>
    public class DeleteFinishingsDialog : Window
    {
        public IList<ElementId> SelectedIds { get; private set; }

        private readonly IList<FinishingItemInfo> _allItems;
        private readonly HashSet<long> _selectedIdValues;

        private RadioButton _rbAll;
        private RadioButton _rbLevel;
        private RadioButton _rbRoom;
        private RadioButton _rbSelection;

        private StackPanel _levelPanel;
        private StackPanel _roomPanel;
        private StackPanel _selectionPanel;

        private readonly Dictionary<string, CheckBox> _levelChecks = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, CheckBox> _roomChecks = new Dictionary<string, CheckBox>();

        private TextBlock _previewText;
        private Button _btnConfirm;

        public DeleteFinishingsDialog(IList<FinishingItemInfo> items, ICollection<ElementId> currentSelection)
        {
            _allItems = items ?? new List<FinishingItemInfo>();
            _selectedIdValues = new HashSet<long>((currentSelection ?? new List<ElementId>()).Select(x => x.GetIdValue()));

            BuildWindow();
            UpdatePreview();
        }

        private void BuildWindow()
        {
            Title = "選擇性刪除裝修";
            Width = 440;
            SizeToContent = SizeToContent.Height;
            MinHeight = 220;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.WhiteSmoke;

            var root = new StackPanel { Margin = new Thickness(16) };
            Content = root;

            root.Children.Add(new TextBlock
            {
                Text = "選擇要刪除的裝修範圍",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            });

            root.Children.Add(new TextBlock
            {
                Text = $"專案中共找到 {_allItems.Count} 個裝修元素",
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var selectedCount = _allItems.Count(x => _selectedIdValues.Contains(x.Id.GetIdValue()));

            _rbAll = AddRadioRow(root, "全部刪除", $"（共 {_allItems.Count} 個）");
            _rbLevel = AddRadioRow(root, "依樓層刪除", "");
            _levelPanel = BuildLevelPanel();
            root.Children.Add(_levelPanel);

            _rbRoom = AddRadioRow(root, "依房間刪除", "");
            _roomPanel = BuildRoomPanel();
            root.Children.Add(_roomPanel);

            _rbSelection = AddRadioRow(root, "依目前選取集", $"（命中 {selectedCount} 個）");
            _selectionPanel = BuildSelectionPanel(selectedCount);
            root.Children.Add(_selectionPanel);

            _rbAll.IsChecked = true;
            foreach (var rb in new[] { _rbAll, _rbLevel, _rbRoom, _rbSelection })
                rb.GroupName = "DeleteMode";

            var previewBorder = new Border
            {
                BorderBrush = Brushes.CornflowerBlue,
                BorderThickness = new Thickness(1),
                Background = Brushes.AliceBlue,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 12, 0, 12),
                Padding = new Thickness(10, 6, 10, 6)
            };
            _previewText = new TextBlock { FontWeight = FontWeights.SemiBold };
            previewBorder.Child = _previewText;
            root.Children.Add(previewBorder);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "取消",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };

            _btnConfirm = new Button
            {
                Content = "確認刪除",
                Width = 90,
                Height = 30,
                Background = Brushes.Firebrick,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };

            buttonRow.Children.Add(btnCancel);
            buttonRow.Children.Add(_btnConfirm);
            root.Children.Add(buttonRow);

            _rbAll.Checked += (s, e) => { ShowPanel(null); UpdatePreview(); };
            _rbLevel.Checked += (s, e) => { ShowPanel(_levelPanel); UpdatePreview(); };
            _rbRoom.Checked += (s, e) => { ShowPanel(_roomPanel); UpdatePreview(); };
            _rbSelection.Checked += (s, e) => { ShowPanel(_selectionPanel); UpdatePreview(); };

            btnCancel.Click += (s, e) => { SelectedIds = null; DialogResult = false; Close(); };
            _btnConfirm.Click += (s, e) => Confirm();

            ShowPanel(null);
        }

        private static RadioButton AddRadioRow(StackPanel parent, string label, string note)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var rb = new RadioButton { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(rb);
            row.Children.Add(new TextBlock { Text = "  " + label, VerticalAlignment = VerticalAlignment.Center });

            if (!string.IsNullOrWhiteSpace(note))
            {
                row.Children.Add(new TextBlock
                {
                    Text = "  " + note,
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            row.MouseDown += (s, e) => rb.IsChecked = true;
            parent.Children.Add(row);
            return rb;
        }

        private StackPanel BuildLevelPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(24, 4, 0, 4), Visibility = WpfVisibility.Collapsed };
            var groups = _allItems
                .GroupBy(x => string.IsNullOrWhiteSpace(x.LevelName) ? "（未知樓層）" : x.LevelName)
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in groups)
            {
                var cb = new CheckBox
                {
                    Content = $"{group.Key}  （{group.Count()} 個）",
                    IsChecked = true,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                cb.Checked += (s, e) => UpdatePreview();
                cb.Unchecked += (s, e) => UpdatePreview();
                _levelChecks[group.Key] = cb;
                panel.Children.Add(cb);
            }

            if (groups.Count == 0)
                panel.Children.Add(new TextBlock { Text = "（沒有可用樓層資訊）", Foreground = Brushes.Gray });

            return panel;
        }

        private StackPanel BuildRoomPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(24, 4, 0, 4), Visibility = WpfVisibility.Collapsed, MaxHeight = 220 };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 220
            };

            var host = new StackPanel();
            scroll.Content = host;
            panel.Children.Add(scroll);

            var groups = _allItems
                .GroupBy(x => string.IsNullOrWhiteSpace(x.RoomDisplay) ? "（無房間關聯）" : x.RoomDisplay)
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in groups)
            {
                var cb = new CheckBox
                {
                    Content = $"{group.Key}  （{group.Count()} 個）",
                    IsChecked = true,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                cb.Checked += (s, e) => UpdatePreview();
                cb.Unchecked += (s, e) => UpdatePreview();
                _roomChecks[group.Key] = cb;
                host.Children.Add(cb);
            }

            if (groups.Count == 0)
                host.Children.Add(new TextBlock { Text = "（沒有可用房間資訊）", Foreground = Brushes.Gray });

            return panel;
        }

        private static StackPanel BuildSelectionPanel(int count)
        {
            var panel = new StackPanel { Margin = new Thickness(24, 4, 0, 4), Visibility = WpfVisibility.Collapsed };
            panel.Children.Add(new TextBlock
            {
                Text = count > 0
                    ? $"目前選取集中有 {count} 個裝修元素。"
                    : "目前選取集中沒有裝修元素。",
                Foreground = count > 0 ? Brushes.DarkGreen : Brushes.OrangeRed,
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        private void ShowPanel(StackPanel active)
        {
            _levelPanel.Visibility = active == _levelPanel ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _roomPanel.Visibility = active == _roomPanel ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _selectionPanel.Visibility = active == _selectionPanel ? WpfVisibility.Visible : WpfVisibility.Collapsed;
        }

        private void UpdatePreview()
        {
            var ids = ComputeSelectedIds();
            _previewText.Text = $"將刪除：{ids.Count} 個裝修元素";
            _previewText.Foreground = ids.Count > 0 ? Brushes.DarkRed : Brushes.Gray;
            _btnConfirm.IsEnabled = ids.Count > 0;
        }

        private IList<ElementId> ComputeSelectedIds()
        {
            if (_rbAll.IsChecked == true)
                return _allItems.Select(x => x.Id).Distinct().ToList();

            if (_rbLevel.IsChecked == true)
            {
                var levels = _levelChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToHashSet();
                return _allItems
                    .Where(x => levels.Contains(string.IsNullOrWhiteSpace(x.LevelName) ? "（未知樓層）" : x.LevelName))
                    .Select(x => x.Id)
                    .Distinct()
                    .ToList();
            }

            if (_rbRoom.IsChecked == true)
            {
                var rooms = _roomChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToHashSet();
                return _allItems
                    .Where(x => rooms.Contains(string.IsNullOrWhiteSpace(x.RoomDisplay) ? "（無房間關聯）" : x.RoomDisplay))
                    .Select(x => x.Id)
                    .Distinct()
                    .ToList();
            }

            if (_rbSelection.IsChecked == true)
            {
                return _allItems
                    .Where(x => _selectedIdValues.Contains(x.Id.GetIdValue()))
                    .Select(x => x.Id)
                    .Distinct()
                    .ToList();
            }

            return new List<ElementId>();
        }

        private void Confirm()
        {
            var ids = ComputeSelectedIds();
            if (ids.Count == 0)
            {
                MessageBox.Show("沒有符合條件的裝修元素可刪除。", "刪除裝修", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedIds = ids;
            DialogResult = true;
            Close();
        }
    }
}
