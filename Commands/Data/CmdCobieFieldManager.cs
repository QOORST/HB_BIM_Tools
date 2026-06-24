using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using YD_RevitTools.LicenseManager;
using YD_RevitTools.LicenseManager.Helpers.Data;

// 避免與 Revit.DB.Binding/ComboBox 衝突
using WpfGrid = System.Windows.Controls.Grid;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfBinding = System.Windows.Data.Binding;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.Manual)]
    public class CmdCobieFieldManager : IExternalCommand
    {
        private static readonly List<CobieFieldManagerWindow> OpenWindows = new List<CobieFieldManagerWindow>();

        public enum ConflictResolution
        {
            Cancel,                    // 取消操作
            OverwriteAll,              // 覆蓋所有衝突參數（會遺失資料）
            OverwriteWithDataBackup,   // 覆蓋參數但備份並還原資料
            SkipAll,                   // 跳過所有衝突參數
            RenameAll,                 // 重新命名所有衝突參數
            AskEach                    // 每個衝突都詢問
        }

        public class ParameterConflict
        {
            public CobieFieldConfig Config { get; set; }
            public string ConflictType { get; set; }  // SharedParameter, ProjectParameter, BuiltIn
            public string ExistingName { get; set; }
            public string ExistingGuid { get; set; }
            public string ExistingDataType { get; set; }
            public string Message { get; set; }
            public Definition ExistingDefinition { get; set; }  // 現有參數定義
            public bool CanBackupData { get; set; }  // 是否可以備份資料
        }

        public class ParameterDataBackup
        {
            public Element Element { get; set; }
            public string ParameterName { get; set; }
            public string Value { get; set; }
            public StorageType StorageType { get; set; }
        }

        public class CobieFieldConfig
        {
            public string DisplayName { get; set; }
            public string CobieName { get; set; }
            public string SharedParameterName { get; set; }
            public string SharedParameterGuid { get; set; }
            public bool IsBuiltIn { get; set; }
            public BuiltInParameter? BuiltInParam { get; set; }
            public bool IsRequired { get; set; }
            public bool ExportEnabled { get; set; }
            public bool ImportEnabled { get; set; }
            public string DefaultValue { get; set; }
            public string DataType { get; set; } = "Text";
            public string Category { get; set; } = "自定義";
            public bool IsInstance { get; set; } = false; // 新增：是否為實體參數
            
            // 衝突處理相關屬性
            public bool RequiresDataBackup { get; set; } = false;
            public ParameterConflict ConflictInfo { get; set; }
        }

        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet set)
        {
            // 檢查授權 - COBie 欄位管理功能
            var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("COBie.FieldManager"))
            {
                TaskDialog.Show("授權限制",
                    "您的授權版本不支援 COBie 欄位管理功能。\n\n" +
                    "請升級至標準版或專業版以使用此功能。\n\n" +
                    "點擊「授權管理」按鈕以查看或更新授權。");
                return Result.Cancelled;
            }

            var doc = cd.Application.ActiveUIDocument.Document;
            var app = cd.Application.Application;

            var handler = new CobieFieldManagerExternalEventHandler();
            var externalEvent = ExternalEvent.Create(handler);
            var win = new CobieFieldManagerWindow(doc, app, handler, externalEvent);
            handler.Attach(win);
            try
            {
                var h = cd.Application.MainWindowHandle;
                if (h != IntPtr.Zero)
                {
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = h;
                }
            }
            catch { }

            OpenWindows.Add(win);
            win.Closed += (s, e) =>
            {
                OpenWindows.Remove(win);
                externalEvent.Dispose();
            };
            win.Show();
            return Result.Succeeded;
        }

        internal enum CobieFieldManagerRequestKind
        {
            None,
            LoadSharedParameters,
            AutoMatchParameters,
            CreateAndBindParameters,
            AuditStandardFields
        }

        internal sealed class CobieFieldManagerExternalEventHandler : IExternalEventHandler
        {
            private readonly object _syncRoot = new object();
            private CobieFieldManagerWindow _window;
            private CobieFieldManagerRequestKind _requestKind;

            public void Attach(CobieFieldManagerWindow window)
            {
                _window = window;
            }

            public void Request(CobieFieldManagerRequestKind requestKind)
            {
                lock (_syncRoot)
                {
                    _requestKind = requestKind;
                }
            }

            public void Execute(UIApplication app)
            {
                CobieFieldManagerRequestKind requestKind;
                lock (_syncRoot)
                {
                    requestKind = _requestKind;
                    _requestKind = CobieFieldManagerRequestKind.None;
                }

                if (_window == null || !_window.IsLoaded)
                    return;

                try
                {
                    switch (requestKind)
                    {
                        case CobieFieldManagerRequestKind.LoadSharedParameters:
                            _window.LoadSharedParametersFromApi();
                            break;
                        case CobieFieldManagerRequestKind.AutoMatchParameters:
                            _window.AutoMatchParametersFromApi();
                            break;
                        case CobieFieldManagerRequestKind.CreateAndBindParameters:
                            _window.CreateSharedParameterForSelected();
                            break;
                        case CobieFieldManagerRequestKind.AuditStandardFields:
                            _window.ExportStandardFieldAudit();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "執行 COBie 欄位設定動作失敗：\n" + ex.Message,
                        "COBie 欄位設定",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    _window.SetApiRequestCompleted();
                }
            }

            public string GetName()
            {
                return "YD BIM Tools - COBie 欄位設定";
            }
        }

        internal class CobieFieldManagerWindow : Window
        {
            private readonly Document _doc;
            private readonly Autodesk.Revit.ApplicationServices.Application _app;
            private readonly List<CobieFieldConfig> _fieldConfigs;
            private readonly DataGrid _grid;
            private readonly WpfComboBox _categoryFilter;
            private readonly WpfTextBox _searchBox;
            private readonly TextBlock _statsText;
            private readonly CobieFieldManagerExternalEventHandler _externalHandler;
            private readonly ExternalEvent _externalEvent;
            private bool _apiRequestPending;

            public CobieFieldManagerWindow(
                Document doc,
                Autodesk.Revit.ApplicationServices.Application app,
                CobieFieldManagerExternalEventHandler externalHandler,
                ExternalEvent externalEvent)
            {
                _doc = doc; _app = app;
                _externalHandler = externalHandler;
                _externalEvent = externalEvent;
                _fieldConfigs = LoadOrCreateDefaultConfig();

                Title = "COBie 欄位管理器";
                Width = 1400; Height = 750;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                MinWidth = 1200; MinHeight = 600;

                var root = new DockPanel() { Margin = new Thickness(10) };

                var bar = CreateToolbar();
                DockPanel.SetDock(bar, Dock.Top);
                root.Children.Add(bar);

                var filter = new StackPanel() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8), VerticalAlignment = VerticalAlignment.Center };
                
                filter.Children.Add(new Label() { Content = "分類:", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
                _categoryFilter = new WpfComboBox() { Width = 120, Height = 26 };
                _categoryFilter.Items.Add("全部");
                _categoryFilter.Items.Add("基本資訊");
                _categoryFilter.Items.Add("空間資訊");
                _categoryFilter.Items.Add("系統資訊");
                _categoryFilter.Items.Add("維護資訊");
                _categoryFilter.Items.Add("自定義");
                _categoryFilter.SelectedIndex = 0;
                _categoryFilter.SelectionChanged += (s, e) => RefreshGrid();
                filter.Children.Add(_categoryFilter);
                
                // 搜尋方塊：可用於快速搜尋 顯示名稱 或 COBie 名稱
                filter.Children.Add(new Label() { Content = "搜尋:", Margin = new Thickness(20,0,0,0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
                _searchBox = new WpfTextBox() 
                { 
                    Width = 280, 
                    Height = 26,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    ToolTip = "輸入顯示名稱、COBie 名稱或共用參數名稱進行搜尋" 
                };
                _searchBox.TextChanged += (s, e) => RefreshGrid();
                filter.Children.Add(_searchBox);
                
                // 說明文字
                var helpText = new TextBlock() 
                { 
                    Text = "提示：按住 Ctrl 可多選欄位進行批次操作", 
                    Margin = new Thickness(20, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    FontSize = 11
                };
                filter.Children.Add(helpText);
                
                DockPanel.SetDock(filter, Dock.Top);
                root.Children.Add(filter);

                // 統計狀態列
                _statsText = new TextBlock
                {
                    Margin = new Thickness(0, 0, 0, 5),
                    FontSize = 11,
                    Foreground = Brushes.DarkBlue,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Left
                };
                var statsPanel = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 248, 255)),
                    BorderBrush = Brushes.LightBlue,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 4, 8, 4),
                    Child = _statsText
                };
                DockPanel.SetDock(statsPanel, Dock.Bottom);
                root.Children.Add(statsPanel);

                var btns = CreateButtonPanel();
                DockPanel.SetDock(btns, Dock.Bottom);
                root.Children.Add(btns);

                _grid = CreateDataGrid();
                root.Children.Add(_grid);
                
                // 監聽選取變更以更新統計
                _grid.SelectionChanged += (s, e) => UpdateStatistics();

                Content = root;
                RefreshGrid();
            }

            private void RaiseApiRequest(CobieFieldManagerRequestKind requestKind)
            {
                if (_apiRequestPending)
                {
                    MessageBox.Show("上一個 Revit 動作仍在處理中，請稍候。", "COBie 欄位設定");
                    return;
                }

                _apiRequestPending = true;
                _externalHandler.Request(requestKind);
                var result = _externalEvent.Raise();
                if (result != ExternalEventRequest.Accepted)
                {
                    _apiRequestPending = false;
                    MessageBox.Show("目前無法送出 Revit 動作，請稍後再試。", "COBie 欄位設定");
                }
            }

            internal void SetApiRequestCompleted()
            {
                _apiRequestPending = false;
            }

            private ToolBar CreateToolbar()
            {
                var t = new ToolBar() { Margin = new Thickness(0, 0, 0, 5), Height = 40 };

                // === 欄位管理群組 ===
                var btnAdd = new Button() 
                { 
                    Content = "➕ 新增欄位", 
                    Margin = new Thickness(2),
                    Padding = new Thickness(10, 6, 10, 6),
                    ToolTip = "新增一個自定義欄位",
                    FontSize = 12
                };
                btnAdd.Click += (s, e) =>
                {
                    _fieldConfigs.Add(new CobieFieldConfig
                    {
                        DisplayName = "新欄位",
                        CobieName = "NewField",
                        Category = "自定義",
                        DataType = "Text",
                        ExportEnabled = true,
                        ImportEnabled = true
                    });
                    RefreshGrid();
                };
                t.Items.Add(btnAdd);

                var btnDel = new Button() 
                { 
                    Content = "🗑 刪除欄位", 
                    Margin = new Thickness(2),
                    Padding = new Thickness(10, 6, 10, 6),
                    ToolTip = "刪除選中的自定義欄位（可複選）",
                    FontSize = 12
                };
                btnDel.Click += (s, e) =>
                {
                    var selectedItems = _grid.SelectedItems.Cast<CobieFieldConfig>().ToList();
                    if (selectedItems.Count == 0) 
                    { 
                        TaskDialog.Show("提示", "請先選擇要刪除的欄位"); 
                        return; 
                    }
                    
                    // 檢查是否有內建欄位
                    var builtInItems = selectedItems.Where(x => x.IsBuiltIn).ToList();
                    if (builtInItems.Count > 0) 
                    { 
                        TaskDialog.Show("無法刪除", $"選中的 {builtInItems.Count} 個內建欄位無法刪除，僅會刪除自定義欄位"); 
                    }
                    
                    var deletableItems = selectedItems.Where(x => !x.IsBuiltIn).ToList();
                    if (deletableItems.Count == 0)
                    {
                        TaskDialog.Show("提示", "沒有可刪除的自定義欄位");
                        return;
                    }
                    
                    var confirmMsg = deletableItems.Count == 1
                        ? $"確定要刪除欄位「{deletableItems[0].DisplayName}」嗎？"
                        : $"確定要刪除選中的 {deletableItems.Count} 個自定義欄位嗎？";
                    
                    var result = TaskDialog.Show("確認刪除", confirmMsg, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                    
                    if (result == TaskDialogResult.Yes)
                    {
                        foreach (var item in deletableItems)
                        {
                            _fieldConfigs.Remove(item);
                        }
                        RefreshGrid();
                        TaskDialog.Show("成功", $"已刪除 {deletableItems.Count} 個欄位");
                    }
                };
                t.Items.Add(btnDel);

                t.Items.Add(new Separator());

                // === 參數操作群組 ===
                var btnLoad = new Button() 
                { 
                    Content = "📥 載入共用參數", 
                    Margin = new Thickness(2),
                    Padding = new Thickness(10, 6, 10, 6),
                    ToolTip = "從共用參數檔案載入參數定義（可複選欄位批次關聯）",
                    FontSize = 12
                };
                btnLoad.Click += (s, e) => RaiseApiRequest(CobieFieldManagerRequestKind.LoadSharedParameters);
                t.Items.Add(btnLoad);

                var btnAutoMatch = new Button()
                {
                    Content = "🔍 自動匹配參數",
                    Margin = new Thickness(2),
                    Padding = new Thickness(10, 6, 10, 6),
                    ToolTip = "根據欄位名稱自動從共用參數檔案中尋找並關聯同名參數",
                    FontSize = 12
                };
                btnAutoMatch.Click += (s, e) => RaiseApiRequest(CobieFieldManagerRequestKind.AutoMatchParameters);
                t.Items.Add(btnAutoMatch);

                var btnCreate = new Button() 
                { 
                    Content = "⚙ 建立並綁定參數", 
                    Margin = new Thickness(2),
                    Padding = new Thickness(10, 6, 10, 6),
                    ToolTip = "為選中的欄位建立共用參數並綁定到相關類別（可複選批次處理）",
                    FontSize = 12
                };
                btnCreate.Click += (s, e) => RaiseApiRequest(CobieFieldManagerRequestKind.CreateAndBindParameters);
                t.Items.Add(btnCreate);

                var btnLoadStandard = new Button()
                {
                    Content = "📋 載入標準欄位",
                    Margin = new Thickness(2),
                    Padding = new Thickness(10, 6, 10, 6),
                    ToolTip = "依 COBie 標準檢核規則補齊欄位設定，既有欄位不會覆蓋",
                    FontSize = 12
                };
                btnLoadStandard.Click += (s, e) => LoadStandardCobieFields();
                t.Items.Add(btnLoadStandard);

                var btnAuditStandard = new Button()
                {
                    Content = "檢核標準欄位",
                    Margin = new Thickness(2),
                    Padding = new Thickness(10, 6, 10, 6),
                    ToolTip = "檢查目前 COBie 欄位設定是否符合標準欄位、匯出啟用與專案參數綁定狀態",
                    FontSize = 12
                };
                btnAuditStandard.Click += (s, e) => RaiseApiRequest(CobieFieldManagerRequestKind.AuditStandardFields);
                t.Items.Add(btnAuditStandard);

                t.Items.Add(new Separator());

                // === 設定管理群組 ===
                var btnImp = new Button() 
                { 
                    Content = "📂 匯入設定", 
                    Margin = new Thickness(2), 
                    Padding = new Thickness(10, 6, 10, 6),
                    FontSize = 12
                };
                btnImp.Click += (s, e) =>
                {
                    var dlg = new OpenFileDialog() 
                    { 
                        Filter = "XML 設定檔 (*.xml)|*.xml",
                        Title = "選擇要匯入的設定檔"
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        // 確認覆蓋
                        var result = TaskDialog.Show(
                            "確認匯入", 
                            $"匯入設定將會覆蓋目前的 {_fieldConfigs.Count} 個欄位設定。\n\n是否要建立備份？",
                            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel
                        );
                        
                        if (result == TaskDialogResult.Cancel) return;
                        
                        // 建立備份
                        if (result == TaskDialogResult.Yes && File.Exists(CobieConfigIO.ConfigPath))
                        {
                            var backupPath = CobieConfigIO.ConfigPath.Replace(".xml", $"_backup_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
                            File.Copy(CobieConfigIO.ConfigPath, backupPath, true);
                            TaskDialog.Show("備份完成", $"已備份至：\n{backupPath}");
                        }
                        
                        // 執行匯入
                        Directory.CreateDirectory(Path.GetDirectoryName(CobieConfigIO.ConfigPath));
                        File.Copy(dlg.FileName, CobieConfigIO.ConfigPath, true);
                        _fieldConfigs.Clear();
                        _fieldConfigs.AddRange(CobieConfigIO.LoadConfig());
                        RefreshGrid();
                        TaskDialog.Show("成功", $"已匯入 {_fieldConfigs.Count} 個欄位設定");
                    }
                };
                t.Items.Add(btnImp);

                var btnExp = new Button() 
                { 
                    Content = "💾 匯出設定", 
                    Margin = new Thickness(2), 
                    Padding = new Thickness(10, 6, 10, 6),
                    FontSize = 12
                };
                btnExp.Click += (s, e) =>
                {
                    var dlg = new SaveFileDialog() 
                    { 
                        Filter = "XML 設定檔 (*.xml)|*.xml", 
                        FileName = $"COBieFieldConfig_{DateTime.Now:yyyyMMdd}.xml",
                        Title = "匯出欄位設定"
                    };
                    if (dlg.ShowDialog() == true) 
                    { 
                        // 檢查是否覆蓋已存在檔案
                        if (File.Exists(dlg.FileName))
                        {
                            var result = TaskDialog.Show(
                                "確認覆蓋",
                                $"檔案已存在：\n{Path.GetFileName(dlg.FileName)}\n\n是否要覆蓋？",
                                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                            );
                            if (result != TaskDialogResult.Yes) return;
                        }
                        
                        SaveConfig(dlg.FileName); 
                        TaskDialog.Show("成功", $"已匯出 {_fieldConfigs.Count} 個欄位設定至：\n{dlg.FileName}"); 
                    }
                };
                t.Items.Add(btnExp);

                return t;
            }

            private DataGrid CreateDataGrid()
            {
                var g = new DataGrid()
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    Margin = new Thickness(0, 5, 0, 5),
                    GridLinesVisibility = DataGridGridLinesVisibility.All,
                    HeadersVisibility = DataGridHeadersVisibility.All,
                    CanUserResizeColumns = true,
                    CanUserSortColumns = true,
                    SelectionUnit = DataGridSelectionUnit.FullRow,
                    SelectionMode = DataGridSelectionMode.Extended,
                    IsReadOnly = false,
                    AlternationCount = 2,
                    AlternatingRowBackground = Brushes.WhiteSmoke,
                    RowHeight = Double.NaN,
                    HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
                };

                // 依使用者要求：移除右鍵選單以防呆，避免誤觸敏感操作
                
                // 加入列樣式：必填但未綁定參數時以淺紅色背景警示
                var rowStyle = new System.Windows.Style(typeof(DataGridRow));
                var trigger = new DataTrigger
                {
                    Binding = new WpfBinding("."),
                    Value = null
                };
                
                // 使用 MultiDataTrigger 檢查：IsRequired=true 且 (SharedParameterGuid 空 且 IsBuiltIn=false)
                var multiTrigger = new MultiDataTrigger();
                multiTrigger.Conditions.Add(new Condition
                {
                    Binding = new WpfBinding("IsRequired"),
                    Value = true
                });
                multiTrigger.Conditions.Add(new Condition
                {
                    Binding = new WpfBinding("IsBuiltIn"),
                    Value = false
                });
                multiTrigger.Conditions.Add(new Condition
                {
                    Binding = new WpfBinding("SharedParameterGuid"),
                    Value = ""
                });
                multiTrigger.Setters.Add(new Setter
                {
                    Property = DataGridRow.BackgroundProperty,
                    Value = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 240, 240))
                });
                rowStyle.Triggers.Add(multiTrigger);
                g.RowStyle = rowStyle;

                // 使用雙向綁定並移除重複欄位，讓欄位順序更直覺
                // 狀態指示欄位
                var statusColumn = new DataGridTextColumn
                {
                    Header = "狀態",
                    Width = 70,
                    MinWidth = 60,
                    IsReadOnly = true,
                    CanUserSort = true
                };
                statusColumn.Binding = new WpfBinding(".")
                {
                    Converter = new FieldStatusConverter()
                };
                statusColumn.ElementStyle = new System.Windows.Style(typeof(TextBlock));
                statusColumn.ElementStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
                g.Columns.Add(statusColumn);
                
                // 操作狀態欄位
                g.Columns.Add(new DataGridCheckBoxColumn 
                { 
                    Header = "匯出", 
                    Binding = new WpfBinding("ExportEnabled") { Mode = BindingMode.TwoWay }, 
                    Width = 50,
                    MinWidth = 40,
                    CanUserSort = true
                });
                g.Columns.Add(new DataGridCheckBoxColumn 
                { 
                    Header = "匯入", 
                    Binding = new WpfBinding("ImportEnabled") { Mode = BindingMode.TwoWay }, 
                    Width = 50,
                    MinWidth = 40,
                    CanUserSort = true
                });
                g.Columns.Add(new DataGridCheckBoxColumn 
                { 
                    Header = "必填", 
                    Binding = new WpfBinding("IsRequired") { Mode = BindingMode.TwoWay }, 
                    Width = 50,
                    MinWidth = 40,
                    CanUserSort = true
                });

                // 主要識別欄位
                g.Columns.Add(new DataGridTextColumn 
                { 
                    Header = "顯示名稱", 
                    Binding = new WpfBinding("DisplayName") { Mode = BindingMode.TwoWay }, 
                    Width = 160,
                    MinWidth = 120,
                    CanUserSort = true
                });
                
                g.Columns.Add(new DataGridComboBoxColumn 
                { 
                    Header = "分類", 
                    SelectedItemBinding = new WpfBinding("Category") { Mode = BindingMode.TwoWay }, 
                    ItemsSource = new[] { "基本資訊", "空間資訊", "系統資訊", "維護資訊", "自定義" }, 
                    Width = 90,
                    MinWidth = 80,
                    CanUserSort = true
                });
                
                g.Columns.Add(new DataGridTextColumn 
                { 
                    Header = "COBie 名稱", 
                    Binding = new WpfBinding("CobieName") { Mode = BindingMode.TwoWay }, 
                    Width = 180,
                    MinWidth = 140,
                    CanUserSort = true
                });
                
                // 共用參數資訊
                g.Columns.Add(new DataGridTextColumn 
                { 
                    Header = "共用參數", 
                    Binding = new WpfBinding("SharedParameterName") { Mode = BindingMode.TwoWay }, 
                    Width = 140,
                    MinWidth = 120,
                    CanUserSort = true
                });
                
                g.Columns.Add(new DataGridCheckBoxColumn 
                { 
                    Header = "實體參數", 
                    Binding = new WpfBinding("IsInstance") { Mode = BindingMode.TwoWay }, 
                    Width = 70,
                    MinWidth = 60,
                    CanUserSort = true
                });
                
                g.Columns.Add(new DataGridComboBoxColumn 
                { 
                    Header = "資料類型", 
                    SelectedItemBinding = new WpfBinding("DataType") { Mode = BindingMode.TwoWay }, 
                    ItemsSource = new[] { "Text", "Number", "Integer", "YesNo", "Date" }, 
                    Width = 90,
                    MinWidth = 80,
                    CanUserSort = true
                });

                // 內建參數（較少使用）
                g.Columns.Add(new DataGridCheckBoxColumn 
                { 
                    Header = "使用內建", 
                    Binding = new WpfBinding("IsBuiltIn") { Mode = BindingMode.TwoWay }, 
                    Width = 70,
                    MinWidth = 60,
                    CanUserSort = true
                });
                g.Columns.Add(new DataGridTextColumn 
                { 
                    Header = "內建參數", 
                    Binding = new WpfBinding("BuiltInParam") { Mode = BindingMode.TwoWay }, 
                    Width = 120,
                    MinWidth = 100,
                    CanUserSort = true
                });
                
                // 其他欄位
                g.Columns.Add(new DataGridTextColumn 
                { 
                    Header = "預設值", 
                    Binding = new WpfBinding("DefaultValue") { Mode = BindingMode.TwoWay }, 
                    Width = 100,
                    MinWidth = 80,
                    CanUserSort = true
                });
                
                // 隱藏的欄位
                g.Columns.Add(new DataGridTextColumn 
                { 
                    Header = "GUID", 
                    Binding = new WpfBinding("SharedParameterGuid") { Mode = BindingMode.TwoWay }, 
                    Width = 0,
                    MinWidth = 0,
                    Visibility = System.Windows.Visibility.Collapsed
                });

                return g;
            }

            private StackPanel CreateButtonPanel()
            {
                var panel = new StackPanel() 
                { 
                    Orientation = Orientation.Horizontal, 
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right, 
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 0),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 245))
                };
                
                var btnSave = new Button() 
                { 
                    Content = "✓ 儲存", 
                    Width = 100, 
                    Height = 36,
                    Margin = new Thickness(10, 8, 5, 8),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(10, 5, 10, 5),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btnSave.Click += (s, e) =>
                {
                    SaveConfig(CobieConfigIO.ConfigPath);
                    MessageBox.Show("設定已儲存。", "COBie 欄位設定", MessageBoxButton.OK, MessageBoxImage.Information);
                };
                
                var btnCancel = new Button() 
                { 
                    Content = "✕ 取消", 
                    Width = 100, 
                    Height = 36,
                    Margin = new Thickness(5, 8, 10, 8),
                    FontSize = 14,
                    Padding = new Thickness(10, 5, 10, 5),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btnCancel.Content = "關閉";
                btnCancel.Click += (s, e) => Close();
                
                panel.Children.Add(btnSave); 
                panel.Children.Add(btnCancel);
                return panel;
            }

            private void RefreshGrid()
            {
                var cat = _categoryFilter.SelectedItem?.ToString();
                var filteredData = (string.IsNullOrEmpty(cat) || cat == "全部")
                    ? _fieldConfigs.ToList()
                    : _fieldConfigs.Where(f => f.Category == cat).ToList();
                
                _grid.ItemsSource = null; // 先清空
                _grid.ItemsSource = filteredData; // 重新設定
                _grid.Items.Refresh(); // 強制刷新
                
                // 更新統計資訊
                UpdateStatistics();
            }
            
            private void UpdateStatistics()
            {
                var total = _fieldConfigs.Count;
                var bound = _fieldConfigs.Count(f => !string.IsNullOrEmpty(f.SharedParameterGuid) || f.IsBuiltIn);
                var unset = total - bound;
                var selected = _grid.SelectedItems.Count;
                var required = _fieldConfigs.Count(f => f.IsRequired);
                var requiredUnbound = _fieldConfigs.Count(f => f.IsRequired && string.IsNullOrEmpty(f.SharedParameterGuid) && !f.IsBuiltIn);
                
                var msg = $"總欄位：{total}  |  已綁定：{bound}  |  未設定：{unset}  |  必填：{required}";
                if (requiredUnbound > 0)
                {
                    msg += $"  |  ⚠ 必填未綁定：{requiredUnbound}";
                }
                if (selected > 0)
                {
                    msg += $"  |  已選取：{selected}";
                }
                
                _statsText.Text = msg;
            }
            
            private void BtnAddParam_Click(object sender, RoutedEventArgs e)
            {
                var dlg = new AddParamDialog(_doc, true); // 啟用複選模式
                if (dlg.ShowDialog() == true && dlg.SelectedParams != null && dlg.SelectedParams.Count > 0)
                {
                    foreach (var p in dlg.SelectedParams)
                    {
                        var def = p.GetDefinition();
                        var name = def.Name;
                        var guid = p.GuidValue;
                        
                        // 檢查是否已存在
                        if (_fieldConfigs.Any(f => f.CobieName == name))
                        {
                            TaskDialog.Show("提示", $"參數 {name} 已存在，已跳過");
                            continue;
                        }
                        
                        var cfg = new CobieFieldConfig
                        {
                            DisplayName = name,
                            CobieName = $"Component.{name.Replace(" ", "")}",
                            Category = "自定義",
                            SharedParameterName = name,
                            SharedParameterGuid = guid.ToString(),
                            ExportEnabled = true,
                            ImportEnabled = true,
                            DataType = "Text",
                            IsInstance = true
                        };
                        _fieldConfigs.Add(cfg);
                    }
                    RefreshGrid();
                }
            }

            internal void LoadSharedParametersFromApi()
            {
                var selectedItems = _grid.SelectedItems.Cast<CobieFieldConfig>().ToList();
                if (selectedItems.Count == 0)
                {
                    TaskDialog.Show("提示", "請先選擇一個或多個欄位");
                    return;
                }

                var sp = _app.OpenSharedParameterFile();
                if (sp == null)
                {
                    var ofd = new OpenFileDialog() { Filter = "共用參數檔案 (*.txt)|*.txt" };
                    if (ofd.ShowDialog() == true)
                    {
                        _app.SharedParametersFilename = ofd.FileName;
                        sp = _app.OpenSharedParameterFile();
                    }
                }
                if (sp == null) return;

                if (selectedItems.Count == 1)
                {
                    var dlg = new SelectDefinitionDialog(sp) { Owner = this };
                    if (dlg.ShowDialog() == true && dlg.Selected != null)
                    {
                        selectedItems[0].SharedParameterName = dlg.Selected.Name;
                        selectedItems[0].SharedParameterGuid = dlg.Selected.GUID.ToString();
                        RefreshGrid();
                        TaskDialog.Show("成功", $"已為「{selectedItems[0].DisplayName}」載入共用參數");
                    }
                }
                else
                {
                    var batchDlg = new BatchLoadParametersDialog(sp, selectedItems) { Owner = this };
                    if (batchDlg.ShowDialog() == true)
                    {
                        RefreshGrid();
                        TaskDialog.Show("成功", $"已為 {selectedItems.Count} 個欄位載入共用參數");
                    }
                }
            }

            internal void AutoMatchParametersFromApi()
            {
                var sp = _app.OpenSharedParameterFile();
                if (sp == null)
                {
                    var ofd = new OpenFileDialog() { Filter = "共用參數檔案 (*.txt)|*.txt" };
                    if (ofd.ShowDialog() == true)
                    {
                        _app.SharedParametersFilename = ofd.FileName;
                        sp = _app.OpenSharedParameterFile();
                    }
                }
                if (sp == null) return;

                var allParams = new List<ExternalDefinition>();
                foreach (DefinitionGroup g in sp.Groups)
                {
                    foreach (ExternalDefinition d in g.Definitions)
                        allParams.Add(d);
                }

                int matchCount = 0;
                foreach (var field in _fieldConfigs)
                {
                    if (!string.IsNullOrEmpty(field.SharedParameterName) &&
                        !string.IsNullOrEmpty(field.SharedParameterGuid))
                        continue;

                    var match = allParams.FirstOrDefault(p =>
                        p.Name.Equals(field.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(field.SharedParameterName) &&
                         p.Name.Equals(field.SharedParameterName, StringComparison.OrdinalIgnoreCase)));

                    if (match == null) continue;
                    field.SharedParameterName = match.Name;
                    field.SharedParameterGuid = match.GUID.ToString();
                    matchCount++;
                }

                RefreshGrid();
                TaskDialog.Show("自動匹配完成", $"成功匹配 {matchCount} 個欄位的共用參數");
            }

            private void LoadStandardCobieFields()
            {
                int added = 0;
                int skipped = 0;
                int matched = 0;

                foreach (var rule in CobieFieldRules.All)
                {
                    string cobieName = $"{rule.SheetName}.{rule.FieldName}";
                    string displayName = $"{rule.SheetName}_{rule.FieldName}";
                    var existing = FindMatchingConfig(rule);
                    if (existing != null)
                    {
                        if (string.IsNullOrWhiteSpace(existing.CobieName))
                            existing.CobieName = cobieName;
                        if (string.IsNullOrWhiteSpace(existing.Category) || existing.Category == "自定義")
                            existing.Category = rule.SheetName;
                        existing.IsRequired = rule.Requirement == "必要";
                        existing.ExportEnabled = true;
                        matched++;
                        skipped++;
                        continue;
                    }

                    _fieldConfigs.Add(new CobieFieldConfig
                    {
                        DisplayName = displayName,
                        CobieName = cobieName,
                        Category = rule.SheetName,
                        SharedParameterName = displayName,
                        DataType = "Text",
                        IsRequired = rule.Requirement == "必要",
                        ExportEnabled = true,
                        ImportEnabled = false,
                        IsInstance = true,
                        IsBuiltIn = false
                    });
                    added++;
                }

                RefreshGrid();
                TaskDialog.Show("載入標準欄位",
                    $"已新增 {added} 個標準 COBie 欄位設定。\n" +
                    $"已辨識並更新 {matched} 個既有相似欄位。\n" +
                    $"已略過 {skipped} 個既有欄位。\n\n" +
                    "請確認欄位名稱與共用參數對應後，再按「儲存設定」。");
            }

            internal void ExportStandardFieldAudit()
            {
                var rows = BuildStandardFieldAuditRows();
                int missing = rows.Count(r => r.Status == "缺少設定");
                int unbound = rows.Count(r => r.Status == "未綁定參數");
                int disabled = rows.Count(r => r.Status == "未啟用匯出");
                int ok = rows.Count(r => r.Status == "完成");

                var summary =
                    $"標準欄位總數：{rows.Count}\n" +
                    $"完成：{ok}\n" +
                    $"缺少設定：{missing}\n" +
                    $"未啟用匯出：{disabled}\n" +
                    $"未綁定專案參數：{unbound}\n\n" +
                    "是否匯出 CSV 檢核報告？";

                var result = TaskDialog.Show("COBie 標準欄位檢核", summary, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (result != TaskDialogResult.Yes)
                    return;

                var dlg = new SaveFileDialog
                {
                    Filter = "CSV 檔案 (*.csv)|*.csv",
                    FileName = $"COBie_FieldAudit_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    DefaultExt = "csv",
                    Title = "匯出 COBie 標準欄位檢核報告"
                };
                if (dlg.ShowDialog() != true)
                    return;

                using (var sw = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8))
                {
                    var headers = new[] { "SheetName", "FieldName", "Requirement", "Status", "ConfigDisplayName", "CobieName", "SharedParameterName", "ExportEnabled", "IsRequired", "BoundToProject", "SuggestedAction", "AutoFillValue", "Note" };
                    sw.WriteLine(string.Join(",", headers.Select(Csv)));
                    foreach (var row in rows)
                    {
                        sw.WriteLine(string.Join(",", new[]
                        {
                            row.SheetName,
                            row.FieldName,
                            row.Requirement,
                            row.Status,
                            row.ConfigDisplayName,
                            row.CobieName,
                            row.SharedParameterName,
                            row.ExportEnabled,
                            row.IsRequired,
                            row.BoundToProject,
                            row.SuggestedAction,
                            row.AutoFillValue,
                            row.Note
                        }.Select(Csv)));
                    }
                }

                TaskDialog.Show("COBie 標準欄位檢核", "已匯出標準欄位檢核報告。");
            }

            private List<CobieFieldAuditRow> BuildStandardFieldAuditRows()
            {
                var boundNames = GetBoundParameterNames();
                var rows = new List<CobieFieldAuditRow>();

                foreach (var rule in CobieFieldRules.All)
                {
                    var cfg = FindMatchingConfig(rule);
                    bool exists = cfg != null;
                    bool exportEnabled = cfg?.ExportEnabled == true;
                    bool bound = exists && (!string.IsNullOrWhiteSpace(cfg.SharedParameterName) && boundNames.Contains(cfg.SharedParameterName));

                    string status;
                    if (!exists) status = "缺少設定";
                    else if (!exportEnabled) status = "未啟用匯出";
                    else if (!bound && !cfg.IsBuiltIn) status = "未綁定參數";
                    else status = "完成";

                    rows.Add(new CobieFieldAuditRow
                    {
                        SheetName = rule.SheetName,
                        FieldName = rule.FieldName,
                        Requirement = rule.Requirement,
                        Status = status,
                        ConfigDisplayName = cfg?.DisplayName ?? string.Empty,
                        CobieName = cfg?.CobieName ?? string.Empty,
                        SharedParameterName = cfg?.SharedParameterName ?? string.Empty,
                        ExportEnabled = exists ? (cfg.ExportEnabled ? "是" : "否") : string.Empty,
                        IsRequired = exists ? (cfg.IsRequired ? "是" : "否") : string.Empty,
                        BoundToProject = exists ? (cfg.IsBuiltIn ? "內建" : (bound ? "是" : "否")) : string.Empty,
                        SuggestedAction = CobieFieldRules.GetSuggestedAction(rule, status == "完成" ? 0 : 1, exists ? 1 : 0),
                        AutoFillValue = CobieFieldRules.GetAutoFillValue(rule),
                        Note = rule.Note
                    });
                }

                return rows;
            }

            private HashSet<string> GetBoundParameterNames()
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var it = _doc.ParameterBindings.ForwardIterator();
                    it.Reset();
                    while (it.MoveNext())
                    {
                        if (it.Key != null && !string.IsNullOrWhiteSpace(it.Key.Name))
                            names.Add(it.Key.Name);
                    }
                }
                catch { }
                return names;
            }

            private CobieFieldConfig FindMatchingConfig(CobieFieldRule rule)
            {
                string cobieName = $"{rule.SheetName}.{rule.FieldName}";
                string displayName = $"{rule.SheetName}_{rule.FieldName}";
                string compactField = NormalizeKey(rule.FieldName);

                return _fieldConfigs.FirstOrDefault(f =>
                    SameKey(f.CobieName, cobieName) ||
                    SameKey(f.DisplayName, displayName) ||
                    SameKey(f.SharedParameterName, displayName) ||
                    (!string.IsNullOrWhiteSpace(compactField) &&
                     (SameKey(f.DisplayName, rule.FieldName) ||
                      SameKey(f.SharedParameterName, rule.FieldName) ||
                      NormalizeKey(f.DisplayName).EndsWith(compactField, StringComparison.OrdinalIgnoreCase) ||
                      NormalizeKey(f.SharedParameterName).EndsWith(compactField, StringComparison.OrdinalIgnoreCase))));
            }

            private static string NormalizeKey(string value)
            {
                return string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace(".", string.Empty);
            }

            private static bool SameKey(string left, string right)
            {
                return string.Equals(NormalizeKey(left), NormalizeKey(right), StringComparison.OrdinalIgnoreCase);
            }

            private static string Csv(string value)
            {
                value = value ?? string.Empty;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            private sealed class CobieFieldAuditRow
            {
                public string SheetName { get; set; }
                public string FieldName { get; set; }
                public string Requirement { get; set; }
                public string Status { get; set; }
                public string ConfigDisplayName { get; set; }
                public string CobieName { get; set; }
                public string SharedParameterName { get; set; }
                public string ExportEnabled { get; set; }
                public string IsRequired { get; set; }
                public string BoundToProject { get; set; }
                public string SuggestedAction { get; set; }
                public string AutoFillValue { get; set; }
                public string Note { get; set; }
            }

            // 只收集允許綁定參數的模型類別，避免空分類導致綁定失敗
            private CategorySet BuildDefaultCategorySet()
            {
                var cats = new CategorySet();
                try
                {
                    foreach (Category c in _doc.Settings.Categories)
                    {
                        try
                        {
                            if (c != null && c.CategoryType == CategoryType.Model && c.AllowsBoundParameters)
                            {
                                cats.Insert(c);
                            }
                        }
                        catch { }
                    }

                    if (cats.Size == 0)
                    {
                        try { cats.Insert(_doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel)); } catch { }
                    }
                }
                catch { }
                return cats;
            }

            internal void CreateSharedParameterForSelected()
            {
                // 支援多選
                var selectedItems = _grid.SelectedItems.Cast<CobieFieldConfig>().ToList();
                if (selectedItems.Count == 0) 
                { 
                    TaskDialog.Show("提示", "請先選擇一個或多個欄位"); 
                    return; 
                }

                // 檢查參數衝突
                var conflicts = CheckParameterConflicts(selectedItems);
                if (conflicts.Count > 0)
                {
                    var conflictResult = ShowConflictDialog(conflicts);
                    if (conflictResult == ConflictResolution.Cancel) return;
                    
                    // 根據使用者選擇處理衝突
                    selectedItems = ProcessConflicts(selectedItems, conflicts, conflictResult);
                    if (selectedItems.Count == 0)
                    {
                        TaskDialog.Show("提示", "所有選中的欄位都有衝突且已取消處理");
                        return;
                    }
                }

                // 確認對話框
                var confirmMsg = selectedItems.Count == 1 
                    ? $"確定要為欄位「{selectedItems[0].DisplayName}」建立並綁定共用參數嗎？"
                    : $"確定要為選中的 {selectedItems.Count} 個欄位批次建立並綁定共用參數嗎？";
                
                var confirmResult = TaskDialog.Show("確認", confirmMsg, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (confirmResult != TaskDialogResult.Yes) return;

                try
                {
                    var spFile = _app.OpenSharedParameterFile();
                    if (spFile == null)
                    {
                        var sfd = new SaveFileDialog() { Filter = "共用參數檔案 (*.txt)|*.txt", FileName = "COBie_SharedParameters.txt" };
                        if (sfd.ShowDialog() != true) return;
                        using (File.Create(sfd.FileName)) { }
                        _app.SharedParametersFilename = sfd.FileName;
                        spFile = _app.OpenSharedParameterFile();
                    }
                    var group = spFile.Groups.get_Item("COBie") ?? spFile.Groups.Create("COBie");

                    int successCount = 0;
                    int failCount = 0;
                    var errors = new System.Text.StringBuilder();

                    foreach (var sel in selectedItems)
                    {
                        List<ParameterDataBackup> dataBackups = null;
                        
                        try
                        {
                            // 以顯示名稱（中文）作為共用參數定義名稱，確保參數在 Revit 中顯示為中文
                            var paramName = (sel.DisplayName ?? sel.SharedParameterName ?? sel.CobieName ?? "COBieParam").Trim();

                            // 如果需要智慧覆蓋，先備份現有參數資料
                            if (sel.RequiresDataBackup && sel.ConflictInfo?.ExistingDefinition != null)
                            {
                                dataBackups = BackupParameterData(sel.ConflictInfo.ExistingDefinition);
                                if (dataBackups.Count > 0)
                                {
                                    TaskDialog.Show("資料備份", $"已備份參數「{paramName}」的 {dataBackups.Count} 個元件資料");
                                }
                            }

                            var existing = group.Definitions.get_Item(paramName) as ExternalDefinition;
                            if (existing == null)
                            {
                                var opt = ParamTypeCompat.MakeCreationOptions(paramName, sel.DataType, sel.DisplayName);
                                existing = group.Definitions.Create(opt) as ExternalDefinition;
                            }

                            // 將設定中的共用參數名稱更新為實際建立的名稱（中文）以便後續匯出/匯入使用
                            sel.SharedParameterName = existing.Name;
                            sel.SharedParameterGuid = existing.GUID.ToString();

                            using (var tx = new Transaction(_doc, $"Bind {paramName}"))
                            {
                                tx.Start();

                                // 使用穩健的分類集合建立方法，僅加入允許綁定的模型類別
                                var cats = BuildDefaultCategorySet();
                                if (cats == null || cats.Size == 0)
                                {
                                    errors.AppendLine($"• {sel.DisplayName}：專案中沒有可綁定的模型類別");
                                    tx.RollBack();
                                    failCount++;
                                    continue;
                                }

                                // 依設定使用者選擇的層級（IsInstance）
                                bool isInstance = sel.IsInstance;
                                Autodesk.Revit.DB.ElementBinding binding;
                                if (isInstance)
                                    binding = _app.Create.NewInstanceBinding(cats);
                                else
                                    binding = _app.Create.NewTypeBinding(cats);

                                // 先移除同名再插入（避免重複）
                                var map = _doc.ParameterBindings;
                                var it = map.ForwardIterator();
                                Definition exists = null;
                                for (; it.MoveNext();) if (it.Key?.Name == existing.Name) { exists = it.Key; break; }
                                if (exists != null) map.Remove(exists);

                                ParamTypeCompat.InsertBinding(map, existing, binding);
                                tx.Commit();
                            }

                            // 如果有備份資料，嘗試還原
                            if (dataBackups != null && dataBackups.Count > 0)
                            {
                                using (var tx = new Transaction(_doc, $"Restore Data {paramName}"))
                                {
                                    tx.Start();
                                    RestoreParameterData(dataBackups, existing);
                                    tx.Commit();
                                }
                            }

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"• {sel.DisplayName}：{ex.Message}");
                            failCount++;
                        }
                    }

                    RefreshGrid();

                    // 顯示結果摘要
                    var resultMsg = new System.Text.StringBuilder();
                    resultMsg.AppendLine($"批次處理完成：");
                    resultMsg.AppendLine($"成功：{successCount} 個");
                    if (failCount > 0)
                    {
                        resultMsg.AppendLine($"失敗：{failCount} 個");
                        resultMsg.AppendLine();
                        resultMsg.AppendLine("失敗詳情：");
                        resultMsg.Append(errors.ToString());
                    }

                    TaskDialog.Show(failCount > 0 ? "部分成功" : "成功", resultMsg.ToString());
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("錯誤", $"建立/綁定失敗：{ex.Message}");
                }
            }

            private List<ParameterConflict> CheckParameterConflicts(List<CobieFieldConfig> configs)
            {
                var conflicts = new List<ParameterConflict>();

                foreach (var config in configs)
                {
                    var paramName = (config.DisplayName ?? config.SharedParameterName ?? config.CobieName ?? "COBieParam").Trim();
                    
                    // 檢查共用參數檔案中的衝突
                    var spFile = _app.OpenSharedParameterFile();
                    if (spFile != null)
                    {
                        var group = spFile.Groups.get_Item("COBie");
                        if (group != null)
                        {
                            var existing = group.Definitions.get_Item(paramName) as ExternalDefinition;
                            if (existing != null)
                            {
                                // 檢查資料類型是否相符
                                var existingType = GetParameterTypeString(existing);
                                if (existingType != config.DataType)
                                {
                                    conflicts.Add(new ParameterConflict
                                    {
                                        Config = config,
                                        ConflictType = "SharedParameter",
                                        ExistingName = existing.Name,
                                        ExistingGuid = existing.GUID.ToString(),
                                        ExistingDataType = existingType,
                                        ExistingDefinition = existing,
                                        CanBackupData = CanBackupParameterData(existing),
                                        Message = $"共用參數檔案中已存在同名參數「{paramName}」，但資料類型不同（現有：{existingType}，設定：{config.DataType}）"
                                    });
                                }
                            }
                        }
                    }

                    // 檢查專案中的參數綁定衝突
                    var map = _doc.ParameterBindings;
                    var it = map.ForwardIterator();
                    while (it.MoveNext())
                    {
                        if (it.Key?.Name == paramName)
                        {
                            var existingDef = it.Key;
                            var existingBinding = it.Current as Autodesk.Revit.DB.ElementBinding;
                            
                            // 檢查是否為共用參數
                            if (existingDef is ExternalDefinition extDef)
                            {
                                // 共用參數衝突
                                if (!string.IsNullOrEmpty(config.SharedParameterGuid) && 
                                    extDef.GUID.ToString() != config.SharedParameterGuid)
                                {
                                    conflicts.Add(new ParameterConflict
                                    {
                                        Config = config,
                                        ConflictType = "SharedParameter",
                                        ExistingName = existingDef.Name,
                                        ExistingGuid = extDef.GUID.ToString(),
                                        ExistingDataType = GetParameterTypeString(existingDef),
                                        ExistingDefinition = existingDef,
                                        CanBackupData = CanBackupParameterData(existingDef),
                                        Message = $"專案中已綁定同名共用參數「{paramName}」，但 GUID 不同"
                                    });
                                }
                            }
                            else
                            {
                                // 專案參數衝突
                                conflicts.Add(new ParameterConflict
                                {
                                    Config = config,
                                    ConflictType = "ProjectParameter",
                                    ExistingName = existingDef.Name,
                                    ExistingDataType = GetParameterTypeString(existingDef),
                                    ExistingDefinition = existingDef,
                                    CanBackupData = CanBackupParameterData(existingDef),
                                    Message = $"專案中已存在同名專案參數「{paramName}」"
                                });
                            }
                            break;
                        }
                    }

                    // 檢查內建參數衝突
                    if (config.IsBuiltIn && config.BuiltInParam.HasValue)
                    {
                        try
                        {
                            // 檢查內建參數是否存在於目標類別中
                            var testElem = new FilteredElementCollector(_doc)
                                .WhereElementIsNotElementType()
                                .FirstOrDefault();
                            
                            if (testElem != null)
                            {
                                var builtInParam = testElem.get_Parameter(config.BuiltInParam.Value);
                                if (builtInParam != null && builtInParam.Definition.Name == paramName)
                                {
                                    conflicts.Add(new ParameterConflict
                                    {
                                        Config = config,
                                        ConflictType = "BuiltIn",
                                        ExistingName = builtInParam.Definition.Name,
                                        ExistingDataType = GetParameterTypeString(builtInParam.Definition),
                                        Message = $"與內建參數「{paramName}」衝突"
                                    });
                                }
                            }
                        }
                        catch { /* 忽略內建參數檢查錯誤 */ }
                    }
                }

                return conflicts;
            }

            private ConflictResolution ShowConflictDialog(List<ParameterConflict> conflicts)
            {
                var message = new System.Text.StringBuilder();
                message.AppendLine($"發現 {conflicts.Count} 個參數衝突：\n");
                
                foreach (var conflict in conflicts)
                {
                    message.AppendLine($"• {conflict.Config.DisplayName}");
                    message.AppendLine($"  {conflict.Message}\n");
                }

                message.AppendLine("請選擇處理方式：");

                var dialog = new TaskDialog("參數衝突處理")
                {
                    MainInstruction = "發現參數名稱衝突",
                    MainContent = message.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };

                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "智慧覆蓋（保留資料）", "備份現有參數資料，覆蓋參數後還原資料");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "強制覆蓋（會遺失資料）", "直接移除現有參數並建立新的參數");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "跳過衝突參數", "跳過有衝突的參數，只處理沒有衝突的");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "自動重新命名", "為衝突參數自動加上後綴");

                var result = dialog.Show();
                
                switch (result)
                {
                    case TaskDialogResult.CommandLink1: return ConflictResolution.OverwriteWithDataBackup;
                    case TaskDialogResult.CommandLink2: return ConflictResolution.OverwriteAll;
                    case TaskDialogResult.CommandLink3: return ConflictResolution.SkipAll;
                    case TaskDialogResult.CommandLink4: return ConflictResolution.RenameAll;
                    default: return ConflictResolution.Cancel;
                }
            }

            private List<CobieFieldConfig> ProcessConflicts(List<CobieFieldConfig> configs, List<ParameterConflict> conflicts, ConflictResolution resolution)
            {
                var result = new List<CobieFieldConfig>();
                var conflictConfigs = conflicts.Select(c => c.Config).ToHashSet();

                foreach (var config in configs)
                {
                    if (!conflictConfigs.Contains(config))
                    {
                        // 沒有衝突，直接添加
                        result.Add(config);
                    }
                    else
                    {
                        var conflict = conflicts.First(c => c.Config == config);
                        
                        switch (resolution)
                        {
                            case ConflictResolution.OverwriteWithDataBackup:
                                // 智慧覆蓋模式：添加到處理清單，標記需要備份資料
                                config.RequiresDataBackup = true;
                                config.ConflictInfo = conflict;
                                result.Add(config);
                                break;
                                
                            case ConflictResolution.OverwriteAll:
                                // 強制覆蓋模式：添加到處理清單，後續會直接移除現有參數
                                result.Add(config);
                                break;
                                
                            case ConflictResolution.SkipAll:
                                // 跳過模式：不添加到處理清單
                                break;
                                
                            case ConflictResolution.RenameAll:
                                // 重新命名模式：修改名稱後添加
                                var newConfig = new CobieFieldConfig
                                {
                                    DisplayName = config.DisplayName + "_COBie",
                                    CobieName = config.CobieName,
                                    SharedParameterName = (config.SharedParameterName ?? config.DisplayName) + "_COBie",
                                    SharedParameterGuid = config.SharedParameterGuid,
                                    IsBuiltIn = config.IsBuiltIn,
                                    BuiltInParam = config.BuiltInParam,
                                    IsRequired = config.IsRequired,
                                    ExportEnabled = config.ExportEnabled,
                                    ImportEnabled = config.ImportEnabled,
                                    DefaultValue = config.DefaultValue,
                                    DataType = config.DataType,
                                    Category = config.Category,
                                    IsInstance = config.IsInstance
                                };
                                result.Add(newConfig);
                                break;
                        }
                    }
                }

                return result;
            }

            private string GetParameterTypeString(Definition definition)
            {
                try
                {
                    // 使用相容的方式獲取參數類型
                    if (definition is ExternalDefinition extDef)
                    {
                        // 對於共用參數，通過 StorageType 推斷類型
                        var param = GetSampleParameter(definition);
                        if (param != null)
                        {
                            switch (param.StorageType)
                            {
                                case StorageType.String: return "Text";
                                case StorageType.Integer: return "Integer";
                                case StorageType.Double: return "Double";
                                case StorageType.ElementId: return "ElementId";
                                default: return "Text";
                            }
                        }
                    }
                    
                    // 默認使用文字類型
                    return "Text";
                }
                catch
                {
                    return "Text";
                }
            }

            private Parameter GetSampleParameter(Definition definition)
            {
                try
                {
                    // 尋找使用此定義的參數樣本
                    var collector = new FilteredElementCollector(_doc)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    
                    foreach (var elem in collector.Take(100)) // 只檢查前100個元素以提高效能
                    {
                        var param = elem.get_Parameter(definition);
                        if (param != null) return param;
                    }
                }
                catch { }
                return null;
            }

            private bool CanBackupParameterData(Definition definition)
            {
                try
                {
                    // 檢查是否有元素使用此參數且有值
                    var collector = new FilteredElementCollector(_doc)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    
                    foreach (var elem in collector.Take(50)) // 檢查前50個元素
                    {
                        var param = elem.get_Parameter(definition);
                        if (param != null && param.HasValue)
                        {
                            return true; // 找到有值的參數，可以備份
                        }
                    }
                }
                catch { }
                return false; // 沒有找到有值的參數或發生錯誤
            }

            private List<ParameterDataBackup> BackupParameterData(Definition definition)
            {
                var backups = new List<ParameterDataBackup>();
                
                try
                {
                    var collector = new FilteredElementCollector(_doc)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    
                    foreach (var elem in collector)
                    {
                        var param = elem.get_Parameter(definition);
                        if (param != null && param.HasValue)
                        {
                            string value = "";
                            switch (param.StorageType)
                            {
                                case StorageType.String:
                                    value = param.AsString() ?? "";
                                    break;
                                case StorageType.Integer:
                                    value = param.AsInteger().ToString();
                                    break;
                                case StorageType.Double:
                                    value = param.AsDouble().ToString();
                                    break;
                                case StorageType.ElementId:
                                    var eid = param.AsElementId();
                                    if (eid != null && ParamTypeCompat.IsValidElementId(eid))
                                    {
                                        var refElem = _doc.GetElement(eid);
                                        value = refElem?.Name ?? ParamTypeCompat.ElementIdToString(eid);
                                    }
                                    break;
                            }
                            
                            if (!string.IsNullOrEmpty(value))
                            {
                                backups.Add(new ParameterDataBackup
                                {
                                    Element = elem,
                                    ParameterName = definition.Name,
                                    Value = value,
                                    StorageType = param.StorageType
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("備份警告", $"備份參數資料時發生部分錯誤：{ex.Message}");
                }
                
                return backups;
            }

            private void RestoreParameterData(List<ParameterDataBackup> backups, Definition newDefinition)
            {
                if (backups == null || backups.Count == 0) return;
                
                int restoredCount = 0;
                int failedCount = 0;
                var errors = new System.Text.StringBuilder();
                
                try
                {
                    foreach (var backup in backups)
                    {
                        try
                        {
                            var param = backup.Element.get_Parameter(newDefinition);
                            if (param != null && !param.IsReadOnly)
                            {
                                bool success = false;
                                switch (param.StorageType)
                                {
                                    case StorageType.String:
                                        param.Set(backup.Value);
                                        success = true;
                                        break;
                                    case StorageType.Integer:
                                        if (int.TryParse(backup.Value, out int intVal))
                                        {
                                            param.Set(intVal);
                                            success = true;
                                        }
                                        break;
                                    case StorageType.Double:
                                        if (double.TryParse(backup.Value, out double doubleVal))
                                        {
                                            param.Set(doubleVal);
                                            success = true;
                                        }
                                        break;
                                    case StorageType.ElementId:
                                        var eid = ParamTypeCompat.ParseElementId(backup.Value);
                                        if (eid != null)
                                        {
                                            param.Set(eid);
                                            success = true;
                                        }
                                        break;
                                }
                                
                                if (success) restoredCount++;
                                else failedCount++;
                            }
                            else
                            {
                                failedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.AppendLine($"• 元件 {ParamTypeCompat.ElementIdToString(backup.Element.Id)}: {ex.Message}");
                            failedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("還原錯誤", $"還原參數資料時發生錯誤：{ex.Message}");
                    return;
                }
                
                // 顯示還原結果
                if (restoredCount > 0 || failedCount > 0)
                {
                    var msg = new System.Text.StringBuilder();
                    msg.AppendLine($"參數資料還原完成：");
                    msg.AppendLine($"成功還原：{restoredCount} 個元件");
                    
                    if (failedCount > 0)
                    {
                        msg.AppendLine($"還原失敗：{failedCount} 個元件");
                        if (errors.Length > 0)
                        {
                            msg.AppendLine("\n失敗詳情：");
                            msg.Append(errors.ToString());
                        }
                    }
                    
                    TaskDialog.Show("資料還原結果", msg.ToString());
                }
            }

            private List<CobieFieldConfig> LoadOrCreateDefaultConfig()
            {
                var local = CobieConfigIO.LoadConfig();

                // 建立最新預設欄位清單（含編號與英文對照）
                var defaults = new List<CobieFieldConfig>
                {
                    new CobieFieldConfig{ DisplayName="01.空間名稱 (Space.Name)", CobieName="Space.Name", Category="空間資訊", SharedParameterName="COBie_SpaceName", ExportEnabled=true, ImportEnabled=false, DataType="Text", IsRequired=false, IsInstance=true },
                    new CobieFieldConfig{ DisplayName="02.空間代碼 (Component.Space)", CobieName="Component.Space", Category="空間資訊", SharedParameterName="COBie_SpaceCode", ExportEnabled=true, ImportEnabled=false, DataType="Text", IsRequired=false, IsInstance=true },
                    new CobieFieldConfig{ DisplayName="03.系統名稱 (System.Name)", CobieName="System.Name", Category="系統資訊", SharedParameterName="COBie_SystemName", ExportEnabled=true, ImportEnabled=true, DataType="Text" },
                    new CobieFieldConfig{ DisplayName="04.系統代碼 (System.Identifier)", CobieName="System.Identifier", Category="系統資訊", SharedParameterName="COBie_SystemId", ExportEnabled=true, ImportEnabled=true, DataType="Text" },
                    new CobieFieldConfig{ DisplayName="05.元件名稱 (Component.Name)", CobieName="Component.Name", Category="基本資訊", SharedParameterName="COBie_Name", ExportEnabled=true, ImportEnabled=true, DataType="Text", IsInstance=true },
                    new CobieFieldConfig{ DisplayName="06.型號名稱 (Component.TypeName)", CobieName="Component.TypeName", Category="維護資訊", SharedParameterName="COBie_TypeName", ExportEnabled=true, ImportEnabled=true, DataType="Text" },
                    new CobieFieldConfig{ DisplayName="07.型號描述 (Type.Description)", CobieName="Type.Description", Category="維護資訊", SharedParameterName="COBie_TypeDescription", ExportEnabled=true, ImportEnabled=true, DataType="Text" },
                    new CobieFieldConfig{ DisplayName="08.序號 (Component.SerialNumber)", CobieName="Component.SerialNumber", Category="維護資訊", SharedParameterName="COBie_SerialNumber", ExportEnabled=true, ImportEnabled=true, DataType="Text" },
                    new CobieFieldConfig{ DisplayName="09.資產編號 (Component.TagNumber)", CobieName="Component.TagNumber", Category="基本資訊", IsBuiltIn=true, BuiltInParam=BuiltInParameter.ALL_MODEL_MARK, ExportEnabled=false, ImportEnabled=true, DataType="Text", DefaultValue="", IsInstance=true },
                    new CobieFieldConfig{ DisplayName="10.安裝/竣工日期 (Component.InstallationDate)", CobieName="Component.InstallationDate", Category="維護資訊", SharedParameterName="COBie_InstallDate", ExportEnabled=true, ImportEnabled=true, DataType="Date" },
                    new CobieFieldConfig{ DisplayName="11.製造廠商 (Component.Manufacturer)", CobieName="Component.Manufacturer", Category="維護資訊", SharedParameterName="COBie_Manufacturer", ExportEnabled=true, ImportEnabled=true, DataType="Text" },
                    new CobieFieldConfig{ DisplayName="12.保固時程 (Component.WarrantyDuration)", CobieName="Component.WarrantyDuration", Category="維護資訊", SharedParameterName="COBie_WarrantyDuration", ExportEnabled=true, ImportEnabled=true, DataType="Number" },
                    new CobieFieldConfig{ DisplayName="13.保固單位 (Component.WarrantyDurationUnit)", CobieName="Component.WarrantyDurationUnit", Category="維護資訊", SharedParameterName="COBie_WarrantyUnit", ExportEnabled=true, ImportEnabled=true, DataType="Text", DefaultValue="年" },
                    new CobieFieldConfig{ DisplayName="14.供應商 (Component.Supplier)", CobieName="Component.Supplier", Category="維護資訊", SharedParameterName="COBie_Supplier", ExportEnabled=true, ImportEnabled=true, DataType="Text" },
                    new CobieFieldConfig{ DisplayName="15.供應商電話 (Component.SupplierPhone)", CobieName="Component.SupplierPhone", Category="維護資訊", SharedParameterName="COBie_SupplierPhone", ExportEnabled=true, ImportEnabled=true, DataType="Text" }
                };

                // 若存在舊設定，執行升級與合併，否則直接使用預設
                if (local.Count > 0)
                {
                    // 修正舊版 CobieName 由 Type.* 改為 Component.*
                    var corrections = new Dictionary<string, string>
                    {
                        {"Type.TypeName", "Component.TypeName"},
                        {"Type.Manufacturer", "Component.Manufacturer"},
                        {"Type.WarrantyDuration", "Component.WarrantyDuration"},
                        {"Type.WarrantyDurationUnit", "Component.WarrantyDurationUnit"},
                        {"Type.Supplier", "Component.Supplier"},
                        {"Type.SupplierPhone", "Component.SupplierPhone"}
                    };
                    foreach (var c in local)
                    {
                        if (c != null && c.CobieName != null && corrections.TryGetValue(c.CobieName, out var fixedName))
                        {
                            c.CobieName = fixedName;
                        }
                    }

                    // 強制更新所有顯示名稱為最新的中英文對照格式
                    var nameMap = defaults.ToDictionary(d => d.CobieName, d => d.DisplayName);
                    foreach (var c in local)
                    {
                        if (c == null) continue;
                        if (c.CobieName != null && nameMap.TryGetValue(c.CobieName, out var dn))
                        {
                            // 強制更新為新的中英文對照格式（即使已有編號）
                            c.DisplayName = dn;
                        }
                    }

                    // 補齊缺少的預設欄位
                    foreach (var d in defaults)
                    {
                        if (!local.Any(x => x.CobieName == d.CobieName))
                        {
                            local.Add(d);
                        }
                    }

                    // 依編號排序以確保顯示順序一致
                    local = local
                        .OrderBy(x =>
                        {
                            if (x == null) return "ZZZ";
                            if (x.CobieName != null && nameMap.TryGetValue(x.CobieName, out var dn)) return dn;
                            return x.DisplayName ?? "ZZZ";
                        })
                        .ToList();

                    return local;
                }

                return defaults;
            }

            private void SaveConfig(string path)
            {
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var x = new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("COBieFieldConfiguration",
                    _fieldConfigs.Select(f => new System.Xml.Linq.XElement("Field",
                        new System.Xml.Linq.XElement("DisplayName", f.DisplayName),
                        new System.Xml.Linq.XElement("CobieName", f.CobieName),
                        new System.Xml.Linq.XElement("SharedParameterName", f.SharedParameterName),
                        new System.Xml.Linq.XElement("SharedParameterGuid", f.SharedParameterGuid),
                        new System.Xml.Linq.XElement("IsBuiltIn", f.IsBuiltIn),
                        new System.Xml.Linq.XElement("BuiltInParam", f.BuiltInParam?.ToString()),
                        new System.Xml.Linq.XElement("IsRequired", f.IsRequired),
                        new System.Xml.Linq.XElement("ExportEnabled", f.ExportEnabled),
                        new System.Xml.Linq.XElement("ImportEnabled", f.ImportEnabled),
                        new System.Xml.Linq.XElement("IsInstance", f.IsInstance),
                        new System.Xml.Linq.XElement("DefaultValue", f.DefaultValue),
                        new System.Xml.Linq.XElement("DataType", f.DataType),
                        new System.Xml.Linq.XElement("Category", f.Category)
                    ))
                ));
                x.Save(path);
            }
        }

        // ExternalDefinition 選擇器
        private class SelectDefinitionDialog : Window
        {
            private readonly ListBox _list = new ListBox();
            public ExternalDefinition Selected { get; private set; }

            public SelectDefinitionDialog(DefinitionFile spFile)
            {
                Title = "選擇共用參數"; Width = 420; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
                var root = new DockPanel() { Margin = new Thickness(10) };
                DockPanel.SetDock(_list, Dock.Top);
                root.Children.Add(_list);

                foreach (DefinitionGroup g in spFile.Groups)
                    foreach (ExternalDefinition d in g.Definitions) _list.Items.Add(d);

                var btns = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var ok = new Button() { Content = "確定", Width = 80, Margin = new Thickness(5, 0, 0, 0) };
                ok.Click += (s, e) => { Selected = _list.SelectedItem as ExternalDefinition; DialogResult = Selected != null; Close(); };
                var cancel = new Button() { Content = "取消", Width = 80, Margin = new Thickness(5, 0, 0, 0) };
                cancel.Click += (s, e) => { DialogResult = false; Close(); };
                btns.Children.Add(ok); btns.Children.Add(cancel);
                DockPanel.SetDock(btns, Dock.Bottom);
                root.Children.Add(btns);
                Content = root;
            }
        }

        // 共用參數選擇對話框（支援複選）
        private class AddParamDialog : Window
        {
            private readonly Document _doc;
            private readonly bool _multiSelect;
            public SharedParameterElement SelectedParam { get; private set; }
            public List<SharedParameterElement> SelectedParams { get; private set; } = new List<SharedParameterElement>();

            public AddParamDialog(Document doc, bool multiSelect = false)
            {
                _doc = doc;
                _multiSelect = multiSelect;
                Title = multiSelect ? "選擇共用參數 (可複選)" : "選擇共用參數";
                Width = 500;
                Height = 400;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ResizeMode = ResizeMode.CanResize;

                var root = new DockPanel();
                
                // 使用不同控件來支持單選或複選
                if (multiSelect)
                {
                    var list = new ListView { Margin = new Thickness(10) };
                    list.SelectionMode = SelectionMode.Multiple;
                    
                    var items = new FilteredElementCollector(doc)
                        .OfClass(typeof(SharedParameterElement))
                        .Cast<SharedParameterElement>()
                        .OrderBy(p => p.Name)
                        .ToList();

                    foreach (var p in items)
                    {
                        var item = new ListViewItem { Content = p, Tag = p };
                        item.Content = p.Name;
                        list.Items.Add(item);
                    }

                    root.Children.Add(list);

                    var btns = new StackPanel 
                    { 
                        Orientation = Orientation.Horizontal, 
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(10)
                    };
                    DockPanel.SetDock(btns, Dock.Bottom);

                    var btnOk = new Button { Content = "確定", Width = 80, Height = 30, Margin = new Thickness(5) };
                    btnOk.Click += (s, e) => 
                    { 
                        foreach (ListViewItem item in list.SelectedItems)
                        {
                            if (item.Content is string name)
                            {
                                var param = items.FirstOrDefault(p => p.Name == name);
                                if (param != null)
                                    SelectedParams.Add(param);
                            }
                        }
                        DialogResult = true; 
                        Close(); 
                    };
                    btns.Children.Add(btnOk);

                    var btnCancel = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(5) };
                    btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
                    btns.Children.Add(btnCancel);

                    root.Children.Add(btns);
                }
                else
                {
                    var list = new ListView { Margin = new Thickness(10) };
                    list.SelectionChanged += (s, e) => 
                    {
                        if (list.SelectedItem is SharedParameterElement sp) SelectedParam = sp;
                    };
                    list.MouseDoubleClick += (s, e) => 
                    {
                        if (list.SelectedItem != null) { DialogResult = true; Close(); }
                    };

                    var items = new FilteredElementCollector(doc)
                        .OfClass(typeof(SharedParameterElement))
                        .Cast<SharedParameterElement>()
                        .OrderBy(p => p.Name)
                        .ToList();

                    foreach (var p in items) list.Items.Add(p);
                    list.DisplayMemberPath = "Name";

                    root.Children.Add(list);

                    var btns = new StackPanel 
                    { 
                        Orientation = Orientation.Horizontal, 
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(10)
                    };
                    DockPanel.SetDock(btns, Dock.Bottom);

                    var btnOk = new Button { Content = "確定", Width = 80, Height = 30, Margin = new Thickness(5) };
                    btnOk.Click += (s, e) => { DialogResult = true; Close(); };
                    btns.Children.Add(btnOk);

                    var btnCancel = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(5) };
                    btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
                    btns.Children.Add(btnCancel);

                    root.Children.Add(btns);
                }
                
                Content = root;
            }
        }

        // 批次載入共用參數對話框
        private class BatchLoadParametersDialog : Window
        {
            private readonly List<CobieFieldConfig> _fields;
            private readonly Dictionary<CobieFieldConfig, WpfComboBox> _combos = new Dictionary<CobieFieldConfig, WpfComboBox>();

            public BatchLoadParametersDialog(DefinitionFile spFile, List<CobieFieldConfig> fields)
            {
                _fields = fields;
                Title = "批次載入共用參數";
                Width = 700;
                Height = 500;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var root = new DockPanel { Margin = new Thickness(10) };

                // 說明
                var header = new TextBlock
                {
                    Text = $"為選中的 {fields.Count} 個欄位分別指定對應的共用參數：",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                DockPanel.SetDock(header, Dock.Top);
                root.Children.Add(header);

                // 參數列表
                var allParams = new List<ExternalDefinition>();
                foreach (DefinitionGroup g in spFile.Groups)
                {
                    foreach (ExternalDefinition d in g.Definitions)
                    {
                        allParams.Add(d);
                    }
                }

                // 滾動區域
                var scroll = new System.Windows.Controls.ScrollViewer
                {
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
                };

                var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

                foreach (var field in fields)
                {
                    var panel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 5, 0, 5)
                    };

                    var label = new Label
                    {
                        Content = field.DisplayName + ":",
                        Width = 200,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    panel.Children.Add(label);

                    var combo = new WpfComboBox { Width = 350 };
                    combo.Items.Add("（不關聯）");
                    foreach (var p in allParams)
                    {
                        combo.Items.Add(p.Name);
                    }

                    // 智慧匹配：嘗試找到同名或相似的參數
                    var matchIndex = allParams.FindIndex(p =>
                        p.Name.Equals(field.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals(field.SharedParameterName, StringComparison.OrdinalIgnoreCase));

                    combo.SelectedIndex = matchIndex >= 0 ? matchIndex + 1 : 0;
                    combo.Tag = allParams; // 儲存參數列表供後續使用

                    panel.Children.Add(combo);
                    stack.Children.Add(panel);

                    _combos[field] = combo;
                }

                scroll.Content = stack;
                root.Children.Add(scroll);

                // 按鈕
                var btns = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                DockPanel.SetDock(btns, Dock.Bottom);

                var btnOk = new Button
                {
                    Content = "確定",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 5, 0)
                };
                btnOk.Click += (s, e) =>
                {
                    foreach (var kvp in _combos)
                    {
                        var field = kvp.Key;
                        var combo = kvp.Value;
                        if (combo.SelectedIndex > 0)
                        {
                            var paramList = combo.Tag as List<ExternalDefinition>;
                            var selectedParam = paramList[combo.SelectedIndex - 1];
                            field.SharedParameterName = selectedParam.Name;
                            field.SharedParameterGuid = selectedParam.GUID.ToString();
                        }
                    }
                    DialogResult = true;
                    Close();
                };
                btns.Children.Add(btnOk);

                var btnCancel = new Button
                {
                    Content = "取消",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
                btns.Children.Add(btnCancel);

                root.Children.Add(btns);
                Content = root;
            }

            // 欄位狀態轉換器：根據欄位綁定狀況顯示圖示文字
            private class FieldStatusConverter : System.Windows.Data.IValueConverter
            {
                public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                {
                    if (value is CobieFieldConfig field)
                    {
                        if (field.IsBuiltIn)
                            return "🔵 內建";
                        if (!string.IsNullOrEmpty(field.SharedParameterGuid))
                            return "🟢 已綁定";
                        return "🟠 未設定";
                    }
                    return string.Empty;
                }

                public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                {
                    return System.Windows.Data.Binding.DoNothing;
                }
            }
        }
    }

    // 供資料格狀態欄使用的轉換器（命名空間層級，避免巢狀類型解析問題）
    internal class FieldStatusConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is CmdCobieFieldManager.CobieFieldConfig field)
            {
                if (field.IsBuiltIn)
                    return "🔵 內建";
                if (!string.IsNullOrEmpty(field.SharedParameterGuid))
                    return "🟢 已綁定";
                return "🟠 未設定";
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
