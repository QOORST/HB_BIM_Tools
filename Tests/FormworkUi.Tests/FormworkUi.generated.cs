// Generated from CmdMain.cs; Revit services are offline test doubles.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WpfWindow = System.Windows.Window;
using WpfThickness = System.Windows.Thickness;
using WpfHorizontal = System.Windows.HorizontalAlignment;
using WpfPanel = System.Windows.Controls.Panel;
using WpfGrid = System.Windows.Controls.Grid;
using WpfRowDef = System.Windows.Controls.RowDefinition;
using WpfColumnDef = System.Windows.Controls.ColumnDefinition;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfLabel = System.Windows.Controls.Label;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfGroupBox = System.Windows.Controls.GroupBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfFontWeights = System.Windows.FontWeights;
namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    public class UiVm
    {
        public enum FormworkRunOutcome
        {
            Completed,
            Cancelled,
            Failed
        }

        internal readonly Document _doc;
        private readonly UIDocument _uidoc;
        private int _runState; // 0 = idle, 1 = queued, 2 = executing
        private int _cancellationRequested;

        // Keep open-document settings alive until DocumentClosed, even between tool windows.
        private static readonly Dictionary<Document, SessionSettings> Sessions
            = new Dictionary<Document, SessionSettings>();
        private readonly SessionSettings _settings;

        private sealed class SessionSettings
        {
            internal bool IncludeWall = true, IncludeColumn = true, IncludeBeam = true,
                IncludeSlab = true, IncludeStairs = true;
            internal bool DrawFormwork = true, Isolate = true, WriteExplanation = true,
                ActiveViewOnly = false, IncludeStructuralBottom = true, IncludeFoundationBottom = true;
            internal double ThicknessMm = 20.0, BottomOffsetMm = 30.0;
            internal long WallMaterialId = -1, ColumnMaterialId = -1, BeamMaterialId = -1, SlabMaterialId = -1;
        }

        // ���O
        public bool IncludeWall { get => _settings.IncludeWall; set => _settings.IncludeWall = value; }
        public bool IncludeColumn { get => _settings.IncludeColumn; set => _settings.IncludeColumn = value; }
        public bool IncludeBeam { get => _settings.IncludeBeam; set => _settings.IncludeBeam = value; }
        public bool IncludeSlab { get => _settings.IncludeSlab; set => _settings.IncludeSlab = value; }
        public bool IncludeStairs { get => _settings.IncludeStairs; set => _settings.IncludeStairs = value; }

        // �ﶵ
        public bool DrawFormwork { get => _settings.DrawFormwork; set => _settings.DrawFormwork = value; }
        public bool Isolate { get => _settings.Isolate; set => _settings.Isolate = value; }
        public bool WriteExplanation { get => _settings.WriteExplanation; set => _settings.WriteExplanation = value; }
        public bool ActiveViewOnly { get => _settings.ActiveViewOnly; set => _settings.ActiveViewOnly = value; }
        public bool IncludeStructuralBottom { get => _settings.IncludeStructuralBottom; set => _settings.IncludeStructuralBottom = value; }
        public bool IncludeFoundationBottom { get => _settings.IncludeFoundationBottom; set => _settings.IncludeFoundationBottom = value; }

        // �Ѽ�
        public double ThicknessMm { get => _settings.ThicknessMm; set => _settings.ThicknessMm = value; }
        public double BottomOffsetMm { get => _settings.BottomOffsetMm; set => _settings.BottomOffsetMm = value; }
        public ElementId MaterialId = ElementId.InvalidElementId;

        // 分類材質設定
        public ElementId WallMaterialId = ElementId.InvalidElementId;
        public ElementId ColumnMaterialId = ElementId.InvalidElementId;
        public ElementId BeamMaterialId = ElementId.InvalidElementId;
        public ElementId SlabMaterialId = ElementId.InvalidElementId;

        // �ƥ�
        public event Action<int> SelectionChanged;
        public event Action<int> RunStarted;
        public event Action<int, int, TimeSpan> ProgressChanged;
        public event Action<string, TimeSpan> RunStageChanged;
        public event Action<FormworkRunOutcome, string> RunFinished;

        private IList<ElementId> _pickedHostIds = new List<ElementId>();

        public UiVm(Document doc, UIDocument uidoc)
        {
            _doc = doc;
            _uidoc = uidoc;
            ClearClosedSessions();
            if (!Sessions.TryGetValue(doc, out var settings))
            {
                settings = new SessionSettings();
                Sessions.Add(doc, settings);
            }
            _settings = settings;
        }

        internal static void ClearClosedSessions()
        {
            foreach (var doc in Sessions.Keys.Where(doc => !doc.IsValidObject).ToList())
                Sessions.Remove(doc);
        }

        internal bool IsActiveDocument(UIApplication app)
            => _doc.IsValidObject && _doc.Equals(app.ActiveUIDocument?.Document);

        internal bool IsRunPending => System.Threading.Volatile.Read(ref _runState) != 0;

        public void SetPicked(IList<ElementId> ids)
        {
            _pickedHostIds = ids ?? new List<ElementId>();
            SelectionChanged?.Invoke(_pickedHostIds.Count);
        }

        internal void RaiseRunStarted(int total) => RunStarted?.Invoke(total);
        internal void RaiseProgress(int c, int t, TimeSpan e) => ProgressChanged?.Invoke(c, t, e);
        internal void RaiseRunStage(string stage, TimeSpan elapsed) => RunStageChanged?.Invoke(stage, elapsed);

        internal bool TryQueueRun()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _runState, 1, 0) != 0)
                return false;
            System.Threading.Volatile.Write(ref _cancellationRequested, 0);
            return true;
        }

        internal bool TryStartRun()
            => System.Threading.Interlocked.CompareExchange(ref _runState, 2, 1) == 1;

        internal void CancelQueuedRun()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _runState, 0, 1) == 1)
                System.Threading.Volatile.Write(ref _cancellationRequested, 0);
        }

        internal void RequestCancellation()
        {
            if (System.Threading.Volatile.Read(ref _runState) != 0)
                System.Threading.Volatile.Write(ref _cancellationRequested, 1);
        }

        internal void ThrowIfCancellationRequested(string stage)
        {
            if (System.Threading.Volatile.Read(ref _cancellationRequested) != 0)
                throw new System.OperationCanceledException($"已在安全檢查點取消（{stage}）");
        }

        internal void RaiseRunFinished(FormworkRunOutcome outcome, string detail)
        {
            System.Threading.Volatile.Write(ref _cancellationRequested, 0);
            System.Threading.Volatile.Write(ref _runState, 0);
            RunFinished?.Invoke(outcome, detail);
        }

        // --- 主視窗 ---
        public class UiMain : WpfWindow
        {
            private readonly UiVm _vm;
            private readonly ExternalEvent _pickEvt;
            private readonly ExternalEvent _runEvt;

            private WpfLabel _lblCount;
            private ProgressWindow _progressWindow;

            internal Document SourceDocument => _vm._doc;
            internal bool IsRunPending => _vm.IsRunPending;

            public UiMain(UiVm vm, ExternalEvent pickEvt, ExternalEvent runEvt)
            {
                _vm = vm; _pickEvt = pickEvt; _runEvt = runEvt;
                Title = "模板生成";
                Width = 540; Height = 690;
                MinWidth = 460; MinHeight = 440;
                MaxHeight = System.Windows.SystemParameters.WorkArea.Height;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei UI");
                FontSize = 13;
                UseLayoutRounding = true;
                Background = Brush("#FFFFFF");
                Foreground = Brush("#20262E");

                var inputStyle = new System.Windows.Style(typeof(WpfTextBox));
                inputStyle.Setters.Add(new System.Windows.Setter(WpfTextBox.PaddingProperty, new WpfThickness(9, 6, 9, 6)));
                inputStyle.Setters.Add(new System.Windows.Setter(WpfTextBox.MinHeightProperty, 34.0));
                inputStyle.Setters.Add(new System.Windows.Setter(WpfTextBox.BorderBrushProperty, Brush("#C7CED6")));
                Resources.Add(typeof(WpfTextBox), inputStyle);
                var comboStyle = new System.Windows.Style(typeof(WpfComboBox));
                comboStyle.Setters.Add(new System.Windows.Setter(WpfComboBox.MinHeightProperty, 34.0));
                comboStyle.Setters.Add(new System.Windows.Setter(WpfComboBox.PaddingProperty, new WpfThickness(8, 5, 8, 5)));
                comboStyle.Setters.Add(new System.Windows.Setter(WpfComboBox.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
                Resources.Add(typeof(WpfComboBox), comboStyle);

                var root = new WpfGrid { Background = Brush("#FFFFFF") };
                root.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new WpfRowDef { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                root.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                Content = root;

                var header = new WpfStackPanel { Margin = new WpfThickness(24, 16, 24, 12) };
                header.Children.Add(new WpfTextBlock { Text = "模板生成", FontSize = 22, FontWeight = WpfFontWeights.SemiBold });
                header.Children.Add(new WpfTextBlock
                {
                    Text = _vm._doc.Title, ToolTip = _vm._doc.Title,
                    Foreground = Brush("#65717E"), Margin = new WpfThickness(0, 5, 0, 0),
                    TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
                });
                root.Children.Add(header);

                var body = new WpfStackPanel { Margin = new WpfThickness(24, 0, 24, 16) };
                var scroll = new System.Windows.Controls.ScrollViewer
                {
                    Content = body, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
                };
                root.Children.Add(scroll); WpfGrid.SetRow(scroll, 1);

                var pickRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 2) };
                pickRow.ColumnDefinitions.Add(new WpfColumnDef { Width = System.Windows.GridLength.Auto });
                pickRow.ColumnDefinitions.Add(new WpfColumnDef());
                var btnPick = ActionButton("選取模型", false);
                btnPick.ToolTip = "在目前專案選取要處理的元素";
                btnPick.Click += (s, e) => { try { _pickEvt.Raise(); } catch { } };
                _lblCount = new WpfLabel
                {
                    Content = "已選取 0 個模型", Foreground = Brush("#65717E"),
                    Margin = new WpfThickness(12, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                pickRow.Children.Add(btnPick);
                pickRow.Children.Add(_lblCount); WpfGrid.SetColumn(_lblCount, 1);
                body.Children.Add(pickRow);

                _vm.SelectionChanged += n => Dispatcher.Invoke(() => _lblCount.Content = $"已選取 {n} 個模型");
                _vm.RunStarted += total => Dispatcher.Invoke(() => _progressWindow?.UpdateProgress(0, total, TimeSpan.Zero));
                _vm.ProgressChanged += (curr, total, elapsed) => Dispatcher.Invoke(() => _progressWindow?.UpdateProgress(curr, total, elapsed));
                _vm.RunStageChanged += (stage, elapsed) => Dispatcher.Invoke(() => _progressWindow?.UpdateStage(stage, elapsed));
                _vm.RunFinished += (outcome, detail) => Dispatcher.Invoke(() =>
                {
                    _progressWindow?.Finish(outcome, detail);
                    Show();
                    WindowState = System.Windows.WindowState.Normal;
                    Activate();
                });

                var categories = new System.Windows.Controls.WrapPanel { ItemWidth = 90, ItemHeight = 28 };
                AddCheck(categories, "牆", v => _vm.IncludeWall = v, _vm.IncludeWall);
                AddCheck(categories, "結構柱", v => _vm.IncludeColumn = v, _vm.IncludeColumn);
                AddCheck(categories, "結構梁", v => _vm.IncludeBeam = v, _vm.IncludeBeam);
                AddCheck(categories, "樓板", v => _vm.IncludeSlab = v, _vm.IncludeSlab);
                AddCheck(categories, "樓梯", v => _vm.IncludeStairs = v, _vm.IncludeStairs);
                AddSection(body, "包含類別", categories);

                var options = new System.Windows.Controls.WrapPanel { ItemWidth = 224, ItemHeight = 30 };
                AddCheck(options, "繪製模板", v => _vm.DrawFormwork = v, _vm.DrawFormwork);
                AddCheck(options, "隔離模板", v => _vm.Isolate = v, _vm.Isolate);
                AddCheck(options, "寫入解說參數", v => _vm.WriteExplanation = v, _vm.WriteExplanation);
                AddCheck(options, "僅目前視圖", v => _vm.ActiveViewOnly = v, _vm.ActiveViewOnly);
                AddCheck(options, "結構產出底模", v => _vm.IncludeStructuralBottom = v, _vm.IncludeStructuralBottom);
                AddCheck(options, "基礎產出底模", v => _vm.IncludeFoundationBottom = v, _vm.IncludeFoundationBottom);
                AddSection(body, "處理選項", options);

                var parameters = TwoColumnGrid(1);
                var tbThk = new WpfTextBox { Text = _vm.ThicknessMm.ToString(CultureInfo.InvariantCulture) };
                var tbOff = new WpfTextBox { Text = _vm.BottomOffsetMm.ToString(CultureInfo.InvariantCulture) };
                tbThk.TextChanged += (s, e) => RememberNumber(tbThk.Text, 0.1, v => _vm.ThicknessMm = v);
                tbOff.TextChanged += (s, e) => RememberNumber(tbOff.Text, 0.0, v => _vm.BottomOffsetMm = v);
                AddField(parameters, "模板厚度 (mm)", tbThk, 0, 0);
                AddField(parameters, "底模下偏 (mm)", tbOff, 0, 1);
                AddSection(body, "尺寸", parameters);

                var materials = TwoColumnGrid(2);
                var mats = new FilteredElementCollector(_vm._doc)
                    .OfClass(typeof(Material)).Cast<Material>().OrderBy(m => m.Name).ToList();
                var matItems = new List<ComboItem> { new ComboItem("不指定", ElementId.InvalidElementId) };
                foreach (var m in mats) matItems.Add(new ComboItem(m.Name, m.Id));
                AddField(materials, "牆", MaterialCombo(matItems, _vm._settings.WallMaterialId, id =>
                {
                    _vm.WallMaterialId = id; _vm._settings.WallMaterialId = id.GetIdValue();
                }), 0, 0);
                AddField(materials, "柱", MaterialCombo(matItems, _vm._settings.ColumnMaterialId, id =>
                {
                    _vm.ColumnMaterialId = id; _vm._settings.ColumnMaterialId = id.GetIdValue();
                }), 0, 1);
                AddField(materials, "梁", MaterialCombo(matItems, _vm._settings.BeamMaterialId, id =>
                {
                    _vm.BeamMaterialId = id; _vm._settings.BeamMaterialId = id.GetIdValue();
                }), 1, 0);
                AddField(materials, "板", MaterialCombo(matItems, _vm._settings.SlabMaterialId, id =>
                {
                    _vm.SlabMaterialId = id; _vm._settings.SlabMaterialId = id.GetIdValue();
                }), 1, 1);
                AddSection(body, "模板材質", materials);

                var footer = new System.Windows.Controls.Border
                {
                    BorderBrush = Brush("#E3E7EB"), BorderThickness = new WpfThickness(0, 1, 0, 0),
                    Background = Brush("#F7F8FA"), Padding = new WpfThickness(24, 14, 24, 14)
                };
                var buttons = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontal.Right };
                var btnClose = ActionButton("關閉", false);
                btnClose.IsCancel = true;
                btnClose.Margin = new WpfThickness(0, 0, 10, 0);
                btnClose.Click += (s, e) => Close();
                var btnRun = ActionButton("開始生成", true);
                btnRun.IsDefault = true;
                btnRun.Click += (s, e) =>
                {
                    if (!_vm.TryQueueRun())
                    {
                        System.Windows.MessageBox.Show(this, "模板生成已在排程或執行中。", "模板生成",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        return;
                    }
                    _vm.ThicknessMm = Math.Max(0.1, ParseOr(_vm.ThicknessMm, tbThk.Text));
                    _vm.BottomOffsetMm = Math.Max(0.0, ParseOr(_vm.BottomOffsetMm, tbOff.Text));
                    tbThk.Text = _vm.ThicknessMm.ToString(CultureInfo.InvariantCulture);
                    tbOff.Text = _vm.BottomOffsetMm.ToString(CultureInfo.InvariantCulture);
                    Hide();
                    ShowProgressWindow();
                    try
                    {
                        if (_runEvt.Raise() != ExternalEventRequest.Accepted)
                        {
                            _vm.CancelQueuedRun();
                            _progressWindow?.Finish(FormworkRunOutcome.Failed, "Revit 未接受執行要求，請稍後再試。");
                            Show(); Activate();
                        }
                    }
                    catch (Exception ex)
                    {
                        _vm.CancelQueuedRun();
                        _progressWindow?.Finish(FormworkRunOutcome.Failed, "無法排程模板生成：" + ex.Message);
                        Show(); Activate();
                    }
                };
                buttons.Children.Add(btnClose); buttons.Children.Add(btnRun);
                footer.Child = buttons;
                root.Children.Add(footer); WpfGrid.SetRow(footer, 2);
            }

            private static System.Windows.Media.Brush Brush(string color)
                => (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(color);

            private static WpfButton ActionButton(string text, bool primary)
                => new WpfButton
                {
                    Content = text, MinWidth = 100, Height = 36, Padding = new WpfThickness(16, 0, 16, 0),
                    FontWeight = primary ? WpfFontWeights.SemiBold : WpfFontWeights.Normal,
                    Background = Brush(primary ? "#087F8C" : "#FFFFFF"),
                    Foreground = Brush(primary ? "#FFFFFF" : "#303B47"),
                    BorderBrush = Brush(primary ? "#087F8C" : "#C7CED6"),
                    BorderThickness = new WpfThickness(1)
                };

            private static void AddSection(WpfPanel panel, string title, System.Windows.UIElement content)
            {
                panel.Children.Add(new WpfTextBlock
                {
                    Text = title, FontSize = 13, FontWeight = WpfFontWeights.SemiBold,
                    Margin = new WpfThickness(0, 14, 0, 7), Foreground = Brush("#303B47")
                });
                panel.Children.Add(content);
            }

            private static WpfGrid TwoColumnGrid(int rows)
            {
                var grid = new WpfGrid();
                grid.ColumnDefinitions.Add(new WpfColumnDef());
                grid.ColumnDefinitions.Add(new WpfColumnDef());
                for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
                return grid;
            }

            private static void AddField(WpfGrid grid, string label, System.Windows.FrameworkElement input, int row, int column)
            {
                var field = new WpfStackPanel { Margin = new WpfThickness(column == 0 ? 0 : 8, row > 0 ? 10 : 0, column == 0 ? 8 : 0, 0) };
                field.Children.Add(new WpfTextBlock { Text = label, Foreground = Brush("#65717E"), Margin = new WpfThickness(0, 0, 0, 5) });
                field.Children.Add(input);
                grid.Children.Add(field); WpfGrid.SetRow(field, row); WpfGrid.SetColumn(field, column);
            }

            private static WpfComboBox MaterialCombo(IList<ComboItem> items, long savedId, Action<ElementId> set)
            {
                var combo = new WpfComboBox { ItemsSource = items, MinWidth = 0 };
                // Material IDs are resolved against the current document's fresh material list.
                combo.SelectedItem = items.FirstOrDefault(item => item.Id.GetIdValue() == savedId) ?? items[0];
                set(((ComboItem)combo.SelectedItem).Id);
                combo.ToolTip = combo.SelectedItem.ToString();
                combo.SelectionChanged += (s, e) =>
                {
                    var item = combo.SelectedItem as ComboItem;
                    set(item?.Id ?? ElementId.InvalidElementId);
                    combo.ToolTip = item?.Name;
                };
                var template = new System.Windows.DataTemplate();
                var text = new System.Windows.FrameworkElementFactory(typeof(WpfTextBlock));
                text.SetBinding(WpfTextBlock.TextProperty, new System.Windows.Data.Binding());
                text.SetValue(WpfTextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
                template.VisualTree = text;
                combo.ItemTemplate = template;
                return combo;
            }
            private void ShowProgressWindow()
            {
                if (_progressWindow != null)
                {
                    _progressWindow.ForceClose();
                    _progressWindow = null;
                }

                _progressWindow = new ProgressWindow();
                _progressWindow.Owner = this;
                _progressWindow.CancelRequested += _vm.RequestCancellation;
                var shownWindow = _progressWindow;
                _progressWindow.Closed += (s, e) =>
                {
                    if (ReferenceEquals(_progressWindow, shownWindow))
                        _progressWindow = null;
                };
                _progressWindow.Show();
            }

            private static double ParseOr(double def, string s)
            {
                double v;
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)
                    && !double.IsNaN(v) && !double.IsInfinity(v) ? v : def;
            }

            private static void RememberNumber(string text, double minimum, Action<double> set)
            {
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
                    && !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum)
                    set(value);
            }

            private static void AddCheck(WpfPanel p, string text, Action<bool> set, bool init)
            {
                var cb = new WpfCheckBox { Content = text, IsChecked = init, Margin = new WpfThickness(0, 2, 0, 2) };
                cb.Checked += (s, e) => set(true);
                cb.Unchecked += (s, e) => set(false);
                p.Children.Add(cb);
            }

            private class ComboItem
            {
                public string Name; public ElementId Id;
                public ComboItem(string n, ElementId i) { Name = n; Id = i; }
                public override string ToString() => Name;
            }
        }
    }

    // ---------- 宿主過濾器 ----------
    public class ProgressWindow : WpfWindow
    {
        private WpfProgressBar _progressBar;
        private WpfLabel _lblProgress;
        private WpfLabel _lblTime;
        private WpfTextBlock _lblStatus;
        private WpfButton _btnCancel;
        private DateTime _startTime;
        private bool _cancelRequested;
        private bool _finished;

        public event Action CancelRequested;

        public ProgressWindow()
        {
            InitializeWindow();
            _startTime = DateTime.Now;
            Closing += OnClosing;
        }

        private void InitializeWindow()
        {
            Title = "模板生成進度";
            Width = 480;
            Height = 320;
            MaxHeight = System.Windows.SystemParameters.WorkArea.Height - 40;
            WindowStyle = System.Windows.WindowStyle.ToolWindow;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            ResizeMode = System.Windows.ResizeMode.NoResize;
            FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei UI");
            FontSize = 12;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));

            var grid = new WpfGrid { Margin = new WpfThickness(20) };
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new WpfRowDef { Height = new System.Windows.GridLength(20) });
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new WpfRowDef { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new WpfRowDef { Height = System.Windows.GridLength.Auto });

            // 狀態標籤
            _lblStatus = new WpfTextBlock
            { 
                Text = "正在準備...",
                FontWeight = WpfFontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 130, 180)),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                MaxHeight = 72,
                Margin = new WpfThickness(5, 4, 5, 8),
                ToolTip = "Revit 原生 API 的單次運算無法強制中斷；取消會在下一個安全檢查點生效。"
            };
            grid.Children.Add(_lblStatus);
            WpfGrid.SetRow(_lblStatus, 0);

            // 進度條
            _progressBar = new WpfProgressBar 
            { 
                Height = 24, 
                Minimum = 0, 
                Maximum = 100,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 230, 230)),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 139, 34))
            };
            grid.Children.Add(_progressBar);
            WpfGrid.SetRow(_progressBar, 2);

            // 進度文字
            _lblProgress = new WpfLabel 
            { 
                Content = "0 / 0", 
                HorizontalAlignment = WpfHorizontal.Center,
                Margin = new WpfThickness(0, 5, 0, 0)
            };
            grid.Children.Add(_lblProgress);
            WpfGrid.SetRow(_lblProgress, 3);

            // 時間標籤
            _lblTime = new WpfLabel 
            { 
                Content = "用時: 00:00", 
                HorizontalAlignment = WpfHorizontal.Center,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100))
            };
            grid.Children.Add(_lblTime);
            WpfGrid.SetRow(_lblTime, 4);

            // 取消按鈕
            _btnCancel = new WpfButton 
            { 
                Content = "取消", 
                Width = 80, 
                Height = 30,
                HorizontalAlignment = WpfHorizontal.Center,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160))
            };
            _btnCancel.Click += (s, e) =>
            {
                if (_finished) Close();
                else RequestCancellation();
            };
            grid.Children.Add(_btnCancel);
            WpfGrid.SetRow(_btnCancel, 6);

            Content = grid;
        }

        public void UpdateProgress(int current, int total, TimeSpan elapsed)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateProgress(current, total, elapsed)));
                return;
            }

            if (total > 0)
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.Value = (double)current / total * 100;
                _lblProgress.Content = $"{current} / {total}";
            }
            else
            {
                _progressBar.IsIndeterminate = true;
                _lblProgress.Content = "準備中...";
            }

            UpdateElapsed(elapsed);
            if (!_cancelRequested && !_finished)
                _lblStatus.Text = total > 0 ? $"正在處理第 {current} 個元素..." : "正在初始化...";
        }

        public void UpdateStage(string stage, TimeSpan elapsed)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateStage(stage, elapsed)));
                return;
            }

            UpdateElapsed(elapsed);
            if (!_cancelRequested && !_finished)
                _lblStatus.Text = string.IsNullOrWhiteSpace(stage) ? "正在處理..." : stage;
        }

        internal void Finish(UiVm.FormworkRunOutcome outcome, string detail)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Finish(outcome, detail)));
                return;
            }

            _finished = true;
            _progressBar.IsIndeterminate = false;
            _lblStatus.Text = detail;
            _btnCancel.IsEnabled = true;
            _btnCancel.Content = "關閉";

            if (outcome == UiVm.FormworkRunOutcome.Completed)
            {
                _progressBar.Value = 100;
                _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 139, 34));
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    Close();
                };
                timer.Start();
            }
            else if (outcome == UiVm.FormworkRunOutcome.Cancelled)
            {
                _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 134, 11));
            }
            else
            {
                _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(178, 34, 34));
            }
        }

        internal void ForceClose()
        {
            _finished = true;
            Close();
        }

        private void RequestCancellation()
        {
            if (_cancelRequested || _finished) return;
            _cancelRequested = true;
            _btnCancel.IsEnabled = false;
            _btnCancel.Content = "取消中...";
            _lblStatus.Text = "已要求取消；正在等待安全檢查點...";
            _lblStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 134, 11));
            CancelRequested?.Invoke();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_finished) return;
            e.Cancel = true;
            RequestCancellation();
        }

        private void UpdateElapsed(TimeSpan elapsed)
        {
            var timeString = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"hh\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
            _lblTime.Content = $"用時: {timeString}";
        }
    }
}
