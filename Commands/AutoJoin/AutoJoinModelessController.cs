using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class AutoJoinModelessController : IExternalEventHandler
    {
        private static AutoJoinModelessController _current;
        private readonly UIApplication _app;
        private readonly Document _document;
        private readonly AutoJoinForm _form;
        private readonly ExternalEvent _event;
        private string _request;
        private AutoJoinSettings _settings;
        private ExecutionAction _action;
        private bool _busy;

        private sealed class Owner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; }
            public Owner(IntPtr handle) { Handle = handle; }
        }

        private AutoJoinModelessController(UIApplication app)
        {
            _app = app;
            _document = app.ActiveUIDocument.Document;
            _form = new AutoJoinForm(SettingsSerializer.LoadOrDefault(SettingsSerializer.DefaultPath));
            _form.EnableModeless();
            _event = ExternalEvent.Create(this);
            _form.RunRequested += () => Queue("run");
            _form.SelectionRequested += () => Queue("selection");
            _form.PickRequested += () => Queue("pick");
            _form.FormClosed += (_, __) =>
            {
                _app.Idling -= CheckDocument;
                _event.Dispose();
                if (_current == this) _current = null;
            };
            _app.Idling += CheckDocument;
            _form.Show(new Owner(app.MainWindowHandle));
        }

        internal static void Show(UIApplication app)
        {
            if (_current != null && _current._document != app.ActiveUIDocument.Document)
            {
                if (_current._busy) return;
                _current._form.Close();
            }
            if (_current == null) _current = new AutoJoinModelessController(app);
            if (_current._form.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                _current._form.WindowState = System.Windows.Forms.FormWindowState.Normal;
            _current._form.BringToFront();
        }

        internal static void Shutdown()
        {
            if (_current == null) return;
            _current._form.SetBusy(false);
            _current._form.Close();
        }

        private void Queue(string request)
        {
            if (_busy) return;
            try
            {
                _settings = _form.BuildSettings();
                _action = _form.Action;
                _request = request;
                _busy = true;
                _form.SetBusy(true);
                _form.SetStatus("等待 Revit 執行；請勿切換模型。");
                if (_event.Raise() != ExternalEventRequest.Accepted)
                    throw new InvalidOperationException("Revit 目前無法接受操作，請稍後再試。");
            }
            catch (Exception ex)
            {
                _request = null;
                _busy = false;
                _form.SetBusy(false);
                _form.SetStatus(ex.Message);
            }
        }

        private void CheckDocument(object sender, IdlingEventArgs e)
        {
            if (!_busy && (!_document.IsValidObject || _app.ActiveUIDocument?.Document != _document))
                _form.Close(); // 文件切換即關閉；重新開啟會使用新文件，絕不沿用舊選取。
        }

        public void Execute(UIApplication app)
        {
            var request = _request;
            _request = null;
            try
            {
                if (request == null) return;
                var uiDoc = app.ActiveUIDocument;
                if (!_document.IsValidObject || uiDoc?.Document != _document)
                    throw new InvalidOperationException("模型已切換或關閉，未執行。請在目前模型重新開啟自動接合。");
                if (!LicenseManager.Instance.HasFeatureAccess("AutoJoin"))
                    throw new InvalidOperationException("目前授權不包含自動接合功能。");
                if (request == "pick")
                {
                    _form.Hide();
                    var picked = uiDoc.Selection.PickObjects(ObjectType.Element, "選取接合構件，按完成結束；Esc 取消。");
                    uiDoc.Selection.SetElementIds(picked.Select(x => x.ElementId).ToList());
                }
                if (request == "pick" || request == "selection")
                {
                    _form.UseSelectedScope();
                    _form.SetStatus($"已選取 {uiDoc.Selection.GetElementIds().Count} 個元素；執行時依勾選類別篩選。\n模型：{_document.Title}");
                }
                else
                {
                    _form.SetStatus(CmdAutoJoin.Run(uiDoc, _settings, _action));
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                _form.SetStatus("已取消選取，未執行接合。");
            }
            catch (Exception ex)
            {
                _form.SetStatus("操作未完成：" + ex.Message);
            }
            finally
            {
                _busy = false;
                _form.SetBusy(false);
                if (!_form.Visible) _form.Show(new Owner(app.MainWindowHandle));
            }
        }

        public string GetName() => "HB_BIM 自動接合非模態操作";
    }
}
