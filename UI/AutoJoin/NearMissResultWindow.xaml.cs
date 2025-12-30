using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin;

namespace YD_RevitTools.LicenseManager.UI.AutoJoin
{
    /// <summary>
    /// 近距離元素偵測結果視窗
    /// </summary>
    public partial class NearMissResultWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly List<NearMissDisplayItem> _displayItems;

        public NearMissResultWindow(UIDocument uidoc, List<NearMissInfo> nearMisses)
        {
            InitializeComponent();
            _uidoc = uidoc;

            // 轉換為顯示項目
            _displayItems = nearMisses.Select(nm => new NearMissDisplayItem(nm)).ToList();

            // 設定資料來源
            dgNearMisses.ItemsSource = _displayItems;

            // 更新標題
            tbSubtitle.Text = $"發現 {nearMisses.Count} 組接近但未相交的元素（容差範圍內）";
        }

        /// <summary>
        /// 雙擊清單項目時，在 Revit 中選取並定位該組元素
        /// </summary>
        private void dgNearMisses_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgNearMisses.SelectedItem is NearMissDisplayItem item)
            {
                try
                {
                    // 選取兩個元素
                    var ids = new List<ElementId> { item.Info.ElementAId, item.Info.ElementBId };
                    _uidoc.Selection.SetElementIds(ids);

                    // 縮放到選取的元素
                    _uidoc.ShowElements(ids);

                    // 提示使用者
                    TaskDialog.Show("元素已選取",
                        $"已選取並定位到以下元素：\n\n" +
                        $"元素 A: {item.ElementADisplay}\n" +
                        $"元素 B: {item.ElementBDisplay}\n\n" +
                        $"距離: {item.DistanceMmDisplay}\n\n" +
                        $"建議手動調整元素輪廓，使其相交後再執行自動接合。");
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("錯誤", $"無法選取元素：{ex.Message}");
                }
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// 用於 DataGrid 顯示的項目
    /// </summary>
    public class NearMissDisplayItem
    {
        public NearMissInfo Info { get; }

        public NearMissDisplayItem(NearMissInfo info)
        {
            Info = info;
        }

        public string ElementADisplay => $"{Info.ElementACategory} - {Info.ElementAName}";
        public string ElementBDisplay => $"{Info.ElementBCategory} - {Info.ElementBName}";
        public string DistanceMmDisplay => $"{Info.DistanceMm:F1} mm";
    }
}

