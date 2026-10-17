using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Commands.Data.NameOrganizer;
using YD_RevitTools.LicenseManager.Commands.Data.BimStandard;

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.Manual)]
    public class CmdModelDataManager : IExternalCommand
    {
        private static readonly List<Session> Sessions = new List<Session>();
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try { Open(data.Application); return Result.Succeeded; }
            catch (Exception ex) { message = ex.Message; TaskDialog.Show("模型名稱整理", ex.Message); return Result.Cancelled; }
        }
        internal static void Open(UIApplication app, string focusId = null, ModelAuditContext auditContext = null)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) throw new InvalidOperationException("請先開啟 Revit 專案模型。");
            if (!LicenseManager.Instance.HasFeatureAccess("Data.ModelManager")) throw new InvalidOperationException("模型名稱整理需要 Standard 或 Professional 授權。");
            var existing = Sessions.FirstOrDefault(s => s.Document == doc && !s.Form.IsDisposed);
            if (existing != null)
            {
                if (focusId != null) { existing.Form.SetRows(new NameOrganizerService(doc).Read(), true); existing.AuditContext = auditContext; existing.Form.FocusItem(focusId); return; }
                existing.Form.Show(); existing.Form.Activate(); return;
            }
            var session = new Session(doc) { AuditContext = auditContext };
            try
            {
                session.Event = ExternalEvent.Create(session);
                session.Form = new NameOrganizerForm(session.Request, focusId);
                session.Form.SetRows(new NameOrganizerService(doc).Read());
                Sessions.Add(session);
                session.Form.FormClosed += (_, _) => { Sessions.Remove(session); session.Event.Dispose(); };
                session.Form.Show(new Owner { Handle = app.MainWindowHandle });
            }
            catch { Sessions.Remove(session); session.Form?.Dispose(); session.Event?.Dispose(); throw; }
        }
        private sealed class Owner : IWin32Window { public IntPtr Handle { get; set; } }
        private sealed class Session : IExternalEventHandler
        {
            internal readonly Document Document;
            internal NameOrganizerForm Form;
            internal ExternalEvent Event;
            internal ModelAuditContext AuditContext;
            private NameAction _action;
            private List<NameRow> _targets;
            private bool _pending;
            internal Session(Document doc) { Document = doc; }
            internal void Request(NameAction action, List<NameRow> targets)
            {
                if (_pending) throw new InvalidOperationException("上一個操作尚未完成。");
                _action = action; _targets = targets.Select(r => r.Snapshot()).ToList(); _pending = true;
                try { if (Event.Raise() != ExternalEventRequest.Accepted) throw new InvalidOperationException("Revit 尚未接受操作，請稍後重試。"); }
                catch { _pending = false; throw; }
            }
            public void Execute(UIApplication app)
            {
                if (!_pending || Form.IsDisposed) return;
                try
                {
                    if (!Document.IsValidObject || app.ActiveUIDocument?.Document != Document) throw new InvalidOperationException("請切回開啟此視窗的模型後再操作。");
                    if (!LicenseManager.Instance.HasFeatureAccess("Data.ModelManager")) throw new InvalidOperationException("目前授權無法使用模型名稱整理。");
                    var service = new NameOrganizerService(Document);
                    if ((_action == NameAction.Apply || _action == NameAction.Delete) && Document.IsReadOnly) throw new InvalidOperationException("此文件目前無法修改。");
                    var refresh = false;
                    switch (_action)
                    {
                        case NameAction.Reload: refresh = true; break;
                        case NameAction.Apply: refresh = service.Apply(_targets, Form); break;
                        case NameAction.Delete: refresh = service.Delete(_targets, app.ActiveUIDocument.ActiveView.Id, Form); break;
                        case NameAction.Audit:
                            Form.DiscardPreviews(); Form.Hide();
                            try { AuditContext = CmdBimStandardAudit.ShowAudit(app, true, AuditContext); } finally { Form.Show(); }
                            refresh = true; break;
                    }
                    if (refresh) Form.SetRows(service.Read(), _action == NameAction.Apply || _action == NameAction.Delete);
                }
                catch (Exception ex) { MessageBox.Show(Form, ex.Message, "模型名稱整理", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                finally { _pending = false; if (!Form.IsDisposed) { Form.SetBusy(false); Form.Activate(); } }
            }
            public string GetName() => "HB 模型名稱整理";
        }
    }
}
