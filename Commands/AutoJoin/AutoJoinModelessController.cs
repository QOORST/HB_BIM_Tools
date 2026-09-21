using System;
using System.IO;
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
        private bool _closeRequested;
        private bool _subscribed;
        private bool _released;

        // Revit may return different managed wrappers for the same open document.
        internal static bool SameDocument(Document expected, Document actual) =>
            expected != null && actual != null && expected.IsValidObject && actual.IsValidObject && expected.Equals(actual);

        internal static string DiagnosticPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HB_BIM", "AutoJoin", "startup.log");

        internal static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath));
                if (File.Exists(DiagnosticPath) && new FileInfo(DiagnosticPath).Length > 1024 * 1024)
                    File.Move(DiagnosticPath, DiagnosticPath + "." + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
                File.AppendAllText(DiagnosticPath, DateTime.Now.ToString("O") + " " + text + Environment.NewLine);
            }
            catch { /* Diagnostics must not interrupt Revit. */ }
        }

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
                // WinForms callbacks are outside Revit API context. Defer API cleanup.
                _closeRequested = true;
                Log("Window closed; API cleanup queued.");
            };
        }

        private void ReleaseInApiContext()
        {
            if (_released) return;
            if (_subscribed)
            {
                _app.Idling -= CheckDocument;
                _subscribed = false;
            }
            _event.Dispose();
            _released = true;
            _request = null;
            if (_current == this) _current = null;
            Log("API cleanup completed.");
        }

        internal static void Show(UIApplication app)
        {
            if (_current != null && (_current._closeRequested || _current._form.IsDisposed))
                _current.ReleaseInApiContext();
            if (_current != null && !SameDocument(_current._document, app.ActiveUIDocument?.Document))
            {
                if (_current._busy) return;
                Log("Close: active document changed before reopening.");
                var previous = _current;
                previous._form.Close();
                previous.ReleaseInApiContext();
            }
            if (_current == null)
            {
                var controller = new AutoJoinModelessController(app);
                _current = controller;
                try
                {
                    controller._form.Show(new Owner(app.MainWindowHandle));
                    app.Idling += controller.CheckDocument;
                    controller._subscribed = true;
                    Log("Opened: Revit " + app.Application.VersionNumber);
                }
                catch
                {
                    controller._form.Dispose();
                    controller.ReleaseInApiContext();
                    throw;
                }
            }
            if (_current._form.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                _current._form.WindowState = System.Windows.Forms.FormWindowState.Normal;
            _current._form.BringToFront();
        }

        internal static void Shutdown()
        {
            if (_current == null) return;
            var controller = _current;
            controller._form.SetBusy(false);
            controller._form.Close();
            controller.ReleaseInApiContext();
        }

        private void Queue(string request)
        {
            if (_busy || _closeRequested || _released) return;
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
            try
            {
                if (_closeRequested || _form.IsDisposed)
                {
                    ReleaseInApiContext();
                    return;
                }
                if (_busy) return;
                if (!SameDocument(_document, _app.ActiveUIDocument?.Document))
                {
                    Log("Close: source document closed or active document changed (Idling).");
                    _form.Close();
                    ReleaseInApiContext();
                }
            }
            catch (Exception ex)
            {
                Log("Document check failed: " + ex);
                if (!_form.IsDisposed) _form.SetStatus("模型檢查未完成，請關閉工具後重新開啟。");
            }
        }

        public void Execute(UIApplication app)
        {
            if (_closeRequested || _released || _form.IsDisposed)
            {
                ReleaseInApiContext();
                return;
            }
            var request = _request;
            _request = null;
            try
            {
                if (request == null) return;
                var uiDoc = app.ActiveUIDocument;
                if (!SameDocument(_document, uiDoc?.Document))
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
                Log("Execute failed: " + ex);
                _form.SetStatus("操作未完成：" + ex.Message);
            }
            finally
            {
                _busy = false;
                if (!_closeRequested && !_form.IsDisposed)
                {
                    _form.SetBusy(false);
                    if (!_form.Visible) _form.Show(new Owner(app.MainWindowHandle));
                }
            }
        }

        public string GetName() => "HB_BIM 自動接合非模態操作";
    }
}
