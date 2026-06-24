#if REVIT2026
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DrawingColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    internal static class RoomFinish2026ModelessController
    {
        private static RoomFinish2026Form _form;
        private static ExternalEvent _externalEvent;
        private static RoomFinish2026Handler _handler;

        internal static Result Show(UIApplication uiApp, ref string message)
        {
            UIDocument uiDoc = uiApp?.ActiveUIDocument;
            if (uiDoc?.Document == null)
            {
                message = "目前沒有可用的 Revit 文件。";
                return Result.Failed;
            }

            if (_form != null && !_form.IsDisposed)
            {
                _form.Activate();
                _handler.UpdateContext(uiDoc);
                _form.Reload(uiDoc);
                return Result.Succeeded;
            }

            try
            {
                _handler = new RoomFinish2026Handler(uiDoc);
                _externalEvent = ExternalEvent.Create(_handler);
                _form = new RoomFinish2026Form(uiDoc, Request);
                _handler.Attach(_form);
                _form.FormClosed += (_, __) => Dispose();
                _form.Show(new RevitWindow(uiApp.MainWindowHandle));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Dispose();
                return Result.Failed;
            }
        }

        private static void Request(RoomFinishRequest request, FinishSettings settings)
        {
            if (_handler == null || _externalEvent == null)
                return;

            _handler.Request(request, settings);
            if (_externalEvent.Raise() != ExternalEventRequest.Accepted)
                _form?.Complete("目前無法送出 Revit 動作，請稍後再試。", true);
        }

        private static void Dispose()
        {
            RoomFinish2026Form form = _form;
            ExternalEvent externalEvent = _externalEvent;
            _form = null;
            _externalEvent = null;
            _handler = null;
            if (form != null && !form.IsDisposed)
                form.Dispose();
            externalEvent?.Dispose();
        }

        private sealed class RevitWindow : WinForms.IWin32Window
        {
            internal RevitWindow(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }
    }

    internal enum RoomFinishRequest
    {
        None,
        PickRooms,
        Run
    }

    internal sealed class RoomFinish2026Handler : IExternalEventHandler
    {
        private UIDocument _uiDoc;
        private RoomFinish2026Form _form;
        private RoomFinishRequest _request;
        private FinishSettings _settings;

        internal RoomFinish2026Handler(UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
        }

        internal void Attach(RoomFinish2026Form form)
        {
            _form = form;
        }

        internal void UpdateContext(UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
        }

        internal void Request(RoomFinishRequest request, FinishSettings settings)
        {
            _request = request;
            _settings = settings;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument current = app.ActiveUIDocument;
                if (current?.Document == null)
                {
                    _form?.Complete("目前沒有可用的 Revit 文件。", true);
                    return;
                }

                _uiDoc = current;
                if (_request == RoomFinishRequest.PickRooms)
                {
                    _form?.SetVisibleForPick(false);
                    IList<Reference> refs = current.Selection.PickObjects(
                        ObjectType.Element,
                        new RoomSelectionFilter(),
                        "請選擇要處理的房間，完成後按 Finish");
                    List<ElementId> ids = refs
                        .Select(x => x.ElementId)
                        .Distinct()
                        .ToList();
                    current.Selection.SetElementIds(ids);
                    _form?.SetPickedRooms(ids);
                    _form?.Complete($"已選取 {ids.Count} 間房間。");
                    return;
                }

                if (_request == RoomFinishRequest.Run)
                {
                    FinishSettings settings = _settings;
                    if (settings == null)
                    {
                        _form?.Complete("找不到執行設定。", true);
                        return;
                    }

                    settings.TargetRoomIds = ResolveTargetRooms(current, settings.TargetRoomIds);
                    if (settings.TargetRoomIds.Count == 0)
                    {
                        _form?.Complete("沒有可處理的房間。", true);
                        return;
                    }

                    string message = string.Empty;
                    Result result = CmdRoomFinish.ExecuteRoomFinish(current, settings, ref message);
                    _form?.Complete(
                        string.IsNullOrWhiteSpace(message)
                            ? (result == Result.Succeeded ? "房間裝修執行完成。" : "房間裝修未完成。")
                            : message,
                        result == Result.Failed);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                _form?.Complete("已取消模型選取。");
            }
            catch (Exception ex)
            {
                _form?.Complete(ex.Message, true);
            }
            finally
            {
                _request = RoomFinishRequest.None;
                _settings = null;
                _form?.SetVisibleForPick(true);
            }
        }

        public string GetName() => "YD BIM Room Finish 2026";

        private static List<ElementId> ResolveTargetRooms(
            UIDocument uiDoc,
            IList<ElementId> requested)
        {
            Document doc = uiDoc.Document;
            if (requested != null && requested.Count > 0)
                return requested.Where(id => doc.GetElement(id) is Room).Distinct().ToList();

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .Where(id => doc.GetElement(id) is Room)
                .ToList();
        }

        private sealed class RoomSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Room;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }

    internal sealed class RoomFinish2026Form : WinForms.Form
    {
        private readonly Action<RoomFinishRequest, FinishSettings> _request;
        private readonly WinForms.ComboBox _wallType = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _floorType = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _ceilingType = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _skirtingType = new WinForms.ComboBox();
        private readonly WinForms.ComboBox _boundary = new WinForms.ComboBox();
        private readonly WinForms.NumericUpDown _wallHeight = new WinForms.NumericUpDown();
        private readonly WinForms.NumericUpDown _ceilingHeight = new WinForms.NumericUpDown();
        private readonly WinForms.NumericUpDown _skirtingHeight = new WinForms.NumericUpDown();
        private readonly WinForms.CheckBox _generate = new WinForms.CheckBox();
        private readonly WinForms.CheckBox _update = new WinForms.CheckBox();
        private readonly WinForms.CheckBox _join = new WinForms.CheckBox();
        private readonly WinForms.CheckBox _usePicked = new WinForms.CheckBox();
        private readonly WinForms.Label _pickedLabel = new WinForms.Label();
        private readonly WinForms.Label _status = new WinForms.Label();
        private readonly WinForms.Button _pickButton = new WinForms.Button();
        private readonly WinForms.Button _runButton = new WinForms.Button();
        private List<ElementId> _pickedRoomIds = new List<ElementId>();
        private Document _document;

        internal RoomFinish2026Form(
            UIDocument uiDoc,
            Action<RoomFinishRequest, FinishSettings> request)
        {
            _request = request;
            Text = "YD BIM Tools - 房間裝修";
            StartPosition = WinForms.FormStartPosition.CenterParent;
            FormBorderStyle = WinForms.FormBorderStyle.FixedToolWindow;
            ClientSize = new Size(520, 535);
            Font = new Font("Microsoft JhengHei UI", 9.5f);
            MaximizeBox = false;
            BuildUi();
            Reload(uiDoc);
        }

        internal void Reload(UIDocument uiDoc)
        {
            if (uiDoc?.Document == null)
                return;

            _document = uiDoc.Document;
            FinishSettings saved = FinishSettings.LoadFromFile() ?? new FinishSettings();
            FillTypes(_wallType, typeof(WallType), saved.SelectedWallTypeId, x => x is WallType wt && wt.Kind == WallKind.Basic);
            FillTypes(_floorType, typeof(FloorType), saved.SelectedFloorTypeId);
            FillTypes(_ceilingType, typeof(CeilingType), saved.SelectedCeilingTypeId);
            FillTypes(_skirtingType, typeof(WallType), saved.SelectedSkirtingTypeId, x => x is WallType wt && wt.Kind == WallKind.Basic);
            _boundary.SelectedIndex = saved.BoundaryMode == FloorBoundaryMode.Centerline
                ? 1
                : saved.BoundaryMode == FloorBoundaryMode.OuterFinish ? 2 : 0;
            _wallHeight.Value = Clamp(saved.WallHeightMm, _wallHeight);
            _ceilingHeight.Value = Clamp(saved.CeilingHeightMm, _ceilingHeight);
            _skirtingHeight.Value = Clamp(saved.SkirtingHeightMm, _skirtingHeight);
            _generate.Checked = saved.GenerateGeometry;
            _update.Checked = saved.UpdateValues;
            _join.Checked = saved.AutoJoinWalls;
            SetPickedRooms(uiDoc.Selection.GetElementIds().Where(id => _document.GetElement(id) is Room).ToList());
            _status.Text = $"目前文件：{_document.Title}";
        }

        internal void SetPickedRooms(ICollection<ElementId> ids)
        {
            _pickedRoomIds = ids?.Distinct().ToList() ?? new List<ElementId>();
            _usePicked.Enabled = _pickedRoomIds.Count > 0;
            _usePicked.Checked = _pickedRoomIds.Count > 0;
            _pickedLabel.Text = _pickedRoomIds.Count > 0
                ? $"已選取 {_pickedRoomIds.Count} 間房間"
                : "尚未選取房間；執行時將處理全部房間";
        }

        internal void SetVisibleForPick(bool visible)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetVisibleForPick), visible);
                return;
            }
            if (visible)
            {
                Show();
                Activate();
            }
            else
            {
                Hide();
            }
        }

        internal void Complete(string text, bool isError = false)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, bool>(Complete), text, isError);
                return;
            }
            SetBusy(false);
            _status.ForeColor = isError ? DrawingColor.Firebrick : DrawingColor.DarkGreen;
            _status.Text = text;
        }

        private void BuildUi()
        {
            var root = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                Padding = new WinForms.Padding(16),
                ColumnCount = 2,
                RowCount = 14
            };
            root.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Absolute, 150));
            root.ColumnStyles.Add(new WinForms.ColumnStyle(WinForms.SizeType.Percent, 100));
            Controls.Add(root);

            AddRow(root, 0, "牆類型", _wallType);
            AddRow(root, 1, "樓板類型", _floorType);
            AddRow(root, 2, "天花板類型", _ceilingType);
            AddRow(root, 3, "踢腳板類型", _skirtingType);
            AddRow(root, 4, "邊界模式", _boundary);
            _boundary.Items.AddRange(new object[] { "內裝修面", "中心線", "外裝修面" });

            ConfigureNumeric(_wallHeight, 1000, 10000);
            ConfigureNumeric(_ceilingHeight, 1000, 10000);
            ConfigureNumeric(_skirtingHeight, 0, 1000);
            AddRow(root, 5, "牆高 (mm)", _wallHeight);
            AddRow(root, 6, "天花高度 (mm)", _ceilingHeight);
            AddRow(root, 7, "踢腳板高度 (mm)", _skirtingHeight);

            var options = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Fill, AutoSize = true };
            _generate.Text = "生成幾何";
            _update.Text = "更新參數";
            _join.Text = "自動接合牆";
            options.Controls.AddRange(new WinForms.Control[] { _generate, _update, _join });
            AddRow(root, 8, "處理內容", options);

            var selection = new WinForms.FlowLayoutPanel { Dock = WinForms.DockStyle.Fill, AutoSize = true };
            _pickButton.Text = "從模型選房";
            _pickButton.AutoSize = true;
            _pickButton.Click += (_, __) =>
            {
                SetBusy(true);
                _request(RoomFinishRequest.PickRooms, null);
            };
            _usePicked.Text = "只處理已選房間";
            _usePicked.AutoSize = true;
            selection.Controls.AddRange(new WinForms.Control[] { _pickButton, _usePicked });
            AddRow(root, 9, "處理範圍", selection);

            _pickedLabel.AutoSize = true;
            _pickedLabel.ForeColor = DrawingColor.DimGray;
            root.Controls.Add(_pickedLabel, 1, 10);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(330, 70);
            _status.ForeColor = DrawingColor.DimGray;
            root.Controls.Add(_status, 0, 11);
            root.SetColumnSpan(_status, 2);

            var buttons = new WinForms.FlowLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                FlowDirection = WinForms.FlowDirection.RightToLeft,
                AutoSize = true
            };
            var close = new WinForms.Button { Text = "關閉", Width = 90, Height = 34 };
            close.Click += (_, __) => Close();
            _runButton.Text = "套用並更新";
            _runButton.Width = 120;
            _runButton.Height = 34;
            _runButton.Click += (_, __) => Run();
            buttons.Controls.AddRange(new WinForms.Control[] { close, _runButton });
            root.Controls.Add(buttons, 0, 13);
            root.SetColumnSpan(buttons, 2);
        }

        private void Run()
        {
            if (!_generate.Checked && !_update.Checked && !_join.Checked)
            {
                Complete("請至少選擇一項處理內容。", true);
                return;
            }

            var settings = FinishSettings.LoadFromFile() ?? new FinishSettings();
            settings.GenerateGeometry = _generate.Checked;
            settings.UpdateValues = _update.Checked;
            settings.SetValuesForGeometry = _update.Checked;
            settings.SetValuesForRooms = _update.Checked;
            settings.AutoJoinWalls = _join.Checked;
            settings.SkipDoorsForSkirting = true;
            settings.SkipWindowsForSkirting = true;
            settings.SkipOpeningsForWalls = true;
            settings.SelectedWallTypeId = SelectedId(_wallType);
            settings.SelectedFloorTypeId = SelectedId(_floorType);
            settings.SelectedCeilingTypeId = SelectedId(_ceilingType);
            settings.SelectedSkirtingTypeId = SelectedId(_skirtingType);
            settings.WallHeightMm = (double)_wallHeight.Value;
            settings.CeilingHeightMm = (double)_ceilingHeight.Value;
            settings.SkirtingHeightMm = (double)_skirtingHeight.Value;
            settings.WallOffsetMm = Math.Max(0, settings.WallHeightMm - settings.CeilingHeightMm);
            settings.BoundaryMode = _boundary.SelectedIndex == 1
                ? FloorBoundaryMode.Centerline
                : _boundary.SelectedIndex == 2 ? FloorBoundaryMode.OuterFinish : FloorBoundaryMode.InnerFinish;
            settings.TargetRoomIds = _usePicked.Checked ? _pickedRoomIds.ToList() : new List<ElementId>();
            settings.RoomOverrides = new List<RoomFinishOverride>();
            try { settings.SaveToFile(); } catch { }

            SetBusy(true);
            _request(RoomFinishRequest.Run, settings);
        }

        private void SetBusy(bool busy)
        {
            _pickButton.Enabled = !busy;
            _runButton.Enabled = !busy;
            if (busy)
            {
                _status.ForeColor = DrawingColor.DimGray;
                _status.Text = "等待 Revit 執行...";
            }
        }

        private void FillTypes(
            WinForms.ComboBox combo,
            Type type,
            ElementId selectedId,
            Func<Element, bool> predicate = null)
        {
            combo.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
            combo.Items.Clear();
            IEnumerable<Element> elements = new FilteredElementCollector(_document)
                .OfClass(type)
                .WhereElementIsElementType()
                .ToElements();
            if (predicate != null)
                elements = elements.Where(predicate);
            foreach (Element element in elements.OrderBy(x => x.Name))
                combo.Items.Add(new ElementOption(element.Id, element.Name));

            long selectedValue = selectedId == null ? -1 : selectedId.Value;
            int index = combo.Items.Cast<ElementOption>().ToList()
                .FindIndex(x => x.Id.Value == selectedValue);
            combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);
        }

        private static void AddRow(
            WinForms.TableLayoutPanel root,
            int row,
            string label,
            WinForms.Control control)
        {
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.Controls.Add(new WinForms.Label
            {
                Text = label,
                AutoSize = true,
                Anchor = WinForms.AnchorStyles.Left
            }, 0, row);
            control.Dock = WinForms.DockStyle.Fill;
            root.Controls.Add(control, 1, row);
        }

        private static void ConfigureNumeric(WinForms.NumericUpDown input, decimal min, decimal max)
        {
            input.Minimum = min;
            input.Maximum = max;
            input.DecimalPlaces = 0;
            input.ThousandsSeparator = true;
        }

        private static decimal Clamp(double value, WinForms.NumericUpDown input)
        {
            decimal candidate = (decimal)value;
            return Math.Min(input.Maximum, Math.Max(input.Minimum, candidate));
        }

        private static ElementId SelectedId(WinForms.ComboBox combo)
        {
            return (combo.SelectedItem as ElementOption)?.Id ?? ElementId.InvalidElementId;
        }

        private sealed class ElementOption
        {
            internal ElementOption(ElementId id, string name)
            {
                Id = id;
                Name = name;
            }
            internal ElementId Id { get; }
            internal string Name { get; }
            public override string ToString() => Name;
        }
    }
}
#endif
