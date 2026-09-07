using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;
using MediaColor = System.Windows.Media.Color;
using WpfVisibility = System.Windows.Visibility;

namespace YD_RevitTools.LicenseManager.UI.Formwork
{
    /// <summary>
    /// 各模板元素的基本資訊，由 CmdDelete 預先收集後傳入對話框
    /// </summary>
    public class FormworkItemInfo
    {
        public ElementId Id       { get; set; }
        public string Category    { get; set; }  // 模板_類別 參數值（牆/柱/梁/板 等）
        public string LevelName   { get; set; }  // 對應樓層名稱
    }

    /// <summary>
    /// 依類別 / 樓層 / 選取集 選擇性刪除模板元素的 WPF 對話框（純程式碼，無 .xaml 相依）
    /// </summary>
    public class DeleteFormworkDialog : Window
    {
        // ── 輸出 ──
        /// <summary>確認後要刪除的 ElementId 清單（若用戶取消則為 null）</summary>
        public IList<ElementId> SelectedIds { get; private set; }

        // ── 內部資料 ──
        private readonly IList<FormworkItemInfo> _allItems;
        private readonly HashSet<long>           _selectionIdValues;

        // ── UI 控件引用 ──
        private RadioButton _rbAll;
        private RadioButton _rbCategory;
        private RadioButton _rbLevel;
        private RadioButton _rbSelection;

        private StackPanel _panelCategory;
        private StackPanel _panelLevel;
        private StackPanel _panelSelection;

        private readonly Dictionary<string, CheckBox> _categoryCheckboxes = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, CheckBox> _levelCheckboxes    = new Dictionary<string, CheckBox>();

        private TextBlock _previewText;
        private Button    _btnConfirm;

        // ── 建構子 ──
        public DeleteFormworkDialog(IList<FormworkItemInfo> items, ICollection<ElementId> currentSelection)
        {
            _allItems         = items ?? new List<FormworkItemInfo>();
            _selectionIdValues = new HashSet<long>(
                (currentSelection ?? new List<ElementId>()).Select(id => id.GetIdValue()));

            BuildWindow();
            UpdatePreview();
        }

        // ═══════════════════════════════════════════════════════════
        //  建立視窗 UI（全部以程式碼構建，無 XAML）
        // ═══════════════════════════════════════════════════════════
        private void BuildWindow()
        {
            Title                 = "選擇性刪除模板元素";
            Width                 = 420;
            SizeToContent         = SizeToContent.Height;
            MinHeight             = 200;
            ResizeMode            = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background            = new SolidColorBrush(MediaColor.FromRgb(245, 245, 245));

            var root = new StackPanel { Margin = new Thickness(16) };
            Content = root;

            // ─── 標題 ───
            root.Children.Add(new TextBlock
            {
                Text       = "選擇要刪除的模板範圍",
                FontWeight = FontWeights.Bold,
                FontSize   = 14,
                Margin     = new Thickness(0, 0, 0, 10)
            });

            // ─── 總數提示 ───
            var totalCount = _allItems.Count;
            root.Children.Add(new TextBlock
            {
                Text       = $"專案中共有 {totalCount} 個模板元素",
                Foreground = Brushes.Gray,
                Margin     = new Thickness(0, 0, 0, 10)
            });

            // ─── 四個模式 RadioButton（每個 radio 與其標籤放在同一個 StackPanel row 裡）──
            var selectionCount = _allItems.Count(i => _selectionIdValues.Contains(i.Id.GetIdValue()));

            _rbAll       = AddRadioRow(root, "全部刪除",      $"（共 {totalCount} 個）");
            _rbCategory  = AddRadioRow(root, "依類別刪除",    "");
            // 類別面板緊接在 _rbCategory 後
            _panelCategory = BuildCategoryPanel();
            root.Children.Add(_panelCategory);

            _rbLevel     = AddRadioRow(root, "依樓層刪除",    "");
            // 樓層面板緊接在 _rbLevel 後
            _panelLevel  = BuildLevelPanel();
            root.Children.Add(_panelLevel);

            _rbSelection = AddRadioRow(root, "依目前選取集",  $"（選取集中有 {selectionCount} 個模板）");
            // 選取集提示面板
            _panelSelection = BuildSelectionPanel(selectionCount);
            root.Children.Add(_panelSelection);

            // 設定預設選取 & GroupName
            _rbAll.IsChecked = true;
            foreach (var rb in new[] { _rbAll, _rbCategory, _rbLevel, _rbSelection })
                rb.GroupName = "mode";

            // ─── 預覽行 ───
            var previewBorder = new Border
            {
                BorderBrush     = Brushes.CornflowerBlue,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Background      = new SolidColorBrush(MediaColor.FromRgb(235, 242, 255)),
                Margin          = new Thickness(0, 12, 0, 12),
                Padding         = new Thickness(10, 6, 10, 6)
            };
            _previewText = new TextBlock { FontWeight = FontWeights.SemiBold };
            previewBorder.Child = _previewText;
            root.Children.Add(previewBorder);

            // ─── 按鈕列 ───
            var buttonRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var btnCancel = new Button { Content = "取消",   Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            _btnConfirm   = new Button
            {
                Content    = "確認刪除",
                Width      = 90,
                Height     = 30,
                Background = new SolidColorBrush(MediaColor.FromRgb(198, 60, 60)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };
            buttonRow.Children.Add(btnCancel);
            buttonRow.Children.Add(_btnConfirm);
            root.Children.Add(buttonRow);

            // ─── 事件 ───
            _rbAll.Checked       += (s, e) => { ShowPanel(null);            UpdatePreview(); };
            _rbCategory.Checked  += (s, e) => { ShowPanel(_panelCategory);  UpdatePreview(); };
            _rbLevel.Checked     += (s, e) => { ShowPanel(_panelLevel);     UpdatePreview(); };
            _rbSelection.Checked += (s, e) => { ShowPanel(_panelSelection); UpdatePreview(); };

            btnCancel.Click   += (s, e) => { SelectedIds = null; DialogResult = false; Close(); };
            _btnConfirm.Click += (s, e) => Confirm();

            ShowPanel(null);
        }

        /// <summary>
        /// 在 parent 中新增一個 RadioButton 行（含文字標籤與附注），並回傳 RadioButton 本身。
        /// 整個 StackPanel 容器也被加進 parent，所以外部只需保存 RadioButton 引用。
        /// </summary>
        private static RadioButton AddRadioRow(StackPanel parent, string label, string note)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 4, 0, 0)
            };
            var rb = new RadioButton { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(rb);
            row.Children.Add(new TextBlock
            {
                Text              = "  " + label,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrEmpty(note))
                row.Children.Add(new TextBlock
                {
                    Text              = "  " + note,
                    Foreground        = Brushes.Gray,
                    FontSize          = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });

            // 讓點擊整列都能觸發 RadioButton
            row.MouseDown += (s, e) => rb.IsChecked = true;

            parent.Children.Add(row);
            return rb;
        }

        // ═══════════════════════════════════════════════════════════
        //  子面板建構
        // ═══════════════════════════════════════════════════════════
        private StackPanel BuildCategoryPanel()
        {
            var panel    = new StackPanel { Margin = new Thickness(24, 4, 0, 4), Visibility = WpfVisibility.Collapsed };
            var groups   = _allItems.GroupBy(i => NormalizeCategory(i.Category))
                                    .OrderBy(g => CategoryOrder(g.Key))
                                    .ToList();

            if (groups.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text       = "（未找到有類別標記的模板元素）",
                    Foreground = Brushes.Gray,
                    Margin     = new Thickness(0, 2, 0, 2)
                });
            }
            else
            {
                foreach (var g in groups)
                {
                    var cb = new CheckBox
                    {
                        Content   = $"{g.Key}  （{g.Count()} 個）",
                        IsChecked = true,
                        Margin    = new Thickness(0, 3, 0, 3)
                    };
                    cb.Checked   += (s, e) => UpdatePreview();
                    cb.Unchecked += (s, e) => UpdatePreview();
                    _categoryCheckboxes[g.Key] = cb;
                    panel.Children.Add(cb);
                }
            }
            return panel;
        }

        private StackPanel BuildLevelPanel()
        {
            var panel  = new StackPanel { Margin = new Thickness(24, 4, 0, 4), Visibility = WpfVisibility.Collapsed };
            var groups = _allItems.GroupBy(i => string.IsNullOrWhiteSpace(i.LevelName) ? "（未知樓層）" : i.LevelName)
                                  .OrderBy(g => g.Key)
                                  .ToList();

            if (groups.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text       = "（未找到樓層資訊）",
                    Foreground = Brushes.Gray,
                    Margin     = new Thickness(0, 2, 0, 2)
                });
            }
            else
            {
                foreach (var g in groups)
                {
                    var cb = new CheckBox
                    {
                        Content   = $"{g.Key}  （{g.Count()} 個）",
                        IsChecked = true,
                        Margin    = new Thickness(0, 3, 0, 3)
                    };
                    cb.Checked   += (s, e) => UpdatePreview();
                    cb.Unchecked += (s, e) => UpdatePreview();
                    _levelCheckboxes[g.Key] = cb;
                    panel.Children.Add(cb);
                }
            }
            return panel;
        }

        private static StackPanel BuildSelectionPanel(int count)
        {
            var panel = new StackPanel { Margin = new Thickness(24, 4, 0, 8), Visibility = WpfVisibility.Collapsed };
            var color = count > 0 ? Brushes.DarkGreen : Brushes.OrangeRed;
            panel.Children.Add(new TextBlock
            {
                Text       = count > 0
                    ? $"選取集中共有 {count} 個模板元素，將全數刪除。"
                    : "目前選取集中沒有模板元素。請先在 Revit 中框選目標再執行此命令。",
                Foreground = color,
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        // ═══════════════════════════════════════════════════════════
        //  輔助方法
        // ═══════════════════════════════════════════════════════════
        private void ShowPanel(StackPanel active)
        {
            _panelCategory.Visibility  = (active == _panelCategory)  ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _panelLevel.Visibility     = (active == _panelLevel)     ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _panelSelection.Visibility = (active == _panelSelection) ? WpfVisibility.Visible : WpfVisibility.Collapsed;
        }

        private void UpdatePreview()
        {
            var ids     = ComputeSelectedIds();
            var count   = ids.Count;
            _previewText.Text = $"將刪除：{count} 個模板元素";
            _previewText.Foreground = count > 0 ? Brushes.DarkRed : Brushes.Gray;
            _btnConfirm.IsEnabled = count > 0;
        }

        private IList<ElementId> ComputeSelectedIds()
        {
            if (_rbAll.IsChecked == true)
                return _allItems.Select(i => i.Id).ToList();

            if (_rbCategory.IsChecked == true)
            {
                var selectedCats = _categoryCheckboxes
                    .Where(kv => kv.Value.IsChecked == true)
                    .Select(kv => kv.Key)
                    .ToHashSet();
                return _allItems
                    .Where(i => selectedCats.Contains(NormalizeCategory(i.Category)))
                    .Select(i => i.Id)
                    .ToList();
            }

            if (_rbLevel.IsChecked == true)
            {
                var selectedLevels = _levelCheckboxes
                    .Where(kv => kv.Value.IsChecked == true)
                    .Select(kv => kv.Key)
                    .ToHashSet();
                return _allItems
                    .Where(i =>
                    {
                        var name = string.IsNullOrWhiteSpace(i.LevelName) ? "（未知樓層）" : i.LevelName;
                        return selectedLevels.Contains(name);
                    })
                    .Select(i => i.Id)
                    .ToList();
            }

            if (_rbSelection.IsChecked == true)
                return _allItems
                    .Where(i => _selectionIdValues.Contains(i.Id.GetIdValue()))
                    .Select(i => i.Id)
                    .ToList();

            return new List<ElementId>();
        }

        private void Confirm()
        {
            var ids = ComputeSelectedIds();
            if (ids.Count == 0)
            {
                MessageBox.Show("未選擇任何模板元素。", "刪除模板", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedIds   = ids;
            DialogResult  = true;
            Close();
        }

        // ─── 類別標準化 ───
        private static string NormalizeCategory(string raw)
            => string.IsNullOrWhiteSpace(raw) ? "（未分類）" : raw.Trim();

        private static int CategoryOrder(string cat)
        {
            switch (cat)
            {
                case "牆": return 0;
                case "柱": return 1;
                case "梁": return 2;
                case "板": return 3;
                default:   return 99;
            }
        }
    }
}
