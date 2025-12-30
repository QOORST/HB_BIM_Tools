using System;
using System.Windows;
using YD_RevitTools.LicenseManager.Commands.AutoJoin;

namespace YD_RevitTools.LicenseManager.UI.AutoJoin
{
    /// <summary>
    /// 簡化版自動接合視窗
    /// </summary>
    public partial class AutoJoinSimpleWindow : Window
    {
        public AutoJoinSimpleWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 取得選擇的物件類型（智慧模式：處理所有類型）
        /// </summary>
        public ElementTypeMode SelectedElementType
        {
            get
            {
                // 智慧模式：返回 All，表示處理所有類型
                return ElementTypeMode.All;
            }
        }

        /// <summary>
        /// 取得選擇的處理範圍
        /// </summary>
        public ProcessingScope SelectedScope
        {
            get
            {
                if (rbAllElements.IsChecked == true) return ProcessingScope.AllElements;
                if (rbCurrentView.IsChecked == true) return ProcessingScope.CurrentView;
                if (rbSelection.IsChecked == true) return ProcessingScope.Selection;
                return ProcessingScope.AllElements; // 預設
            }
        }

        /// <summary>
        /// 是否修正錯誤的接合順序
        /// </summary>
        public bool FixWrongOrder => cbFixWrongOrder.IsChecked == true;

        /// <summary>
        /// 是否為預覽模式
        /// </summary>
        public bool IsDryRun => cbDryRun.IsChecked == true;

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
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

