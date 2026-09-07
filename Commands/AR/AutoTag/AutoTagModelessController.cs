using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal static class AutoTagModelessController
    {
        private static AutoTagOptionsForm _form;
        private static ExternalEvent _externalEvent;
        private static AutoTagExternalEventHandler _handler;
        private static AutoTagMode _mode;

        public static Result Show(ExternalCommandData commandData, AutoTagMode mode, ref string message)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "目前沒有開啟中的 Revit 文件。";
                return Result.Failed;
            }

            try
            {
                if (_form != null && !_form.IsDisposed)
                {
                    if (_mode == mode)
                    {
                        _form.Show();
                        _form.Activate();
                        RequestRefresh();
                        return Result.Succeeded;
                    }

                    _form.Close();
                }

                _mode = mode;
                _handler = new AutoTagExternalEventHandler(mode);
                _externalEvent = ExternalEvent.Create(_handler);
                _form = new AutoTagOptionsForm(uiDoc.Document, mode, RequestApply, RequestRefresh);
                _handler.Attach(_form, uiDoc.Document);
                _form.FormClosed += (sender, args) => DisposeWindow();
                _form.Show();
                _form.Activate();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                DisposeWindow();
                return Result.Failed;
            }
        }

        private static void RequestApply(AutoTagOptions options, IReadOnlyList<AutoTagRuleSelection> rules)
        {
            if (_handler == null)
            {
                _form?.SetRequestCompleted("標籤事件尚未初始化。", true);
                return;
            }

            _handler.RequestApply(options, rules);
            RaiseExternalEvent();
        }

        private static void RequestRefresh()
        {
            if (_handler == null)
            {
                _form?.SetRequestCompleted("標籤事件尚未初始化。", true);
                return;
            }

            _handler.RequestRefresh();
            RaiseExternalEvent();
        }

        private static void RaiseExternalEvent()
        {
            if (_externalEvent == null || _externalEvent.Raise() != ExternalEventRequest.Accepted)
            {
                _form?.SetRequestCompleted("上一個 Revit 動作仍在處理中，請稍候。", true);
            }
        }

        private static void DisposeWindow()
        {
            AutoTagOptionsForm form = _form;
            ExternalEvent externalEvent = _externalEvent;
            _form = null;
            _externalEvent = null;
            _handler = null;

            if (form != null && !form.IsDisposed)
                form.Dispose();

            externalEvent?.Dispose();
        }

        private enum RequestKind
        {
            None,
            Refresh,
            Apply
        }

        private sealed class AutoTagExternalEventHandler : IExternalEventHandler
        {
            private readonly AutoTagMode _mode;
            private AutoTagOptionsForm _form;
            private Document _sourceDocument;
            private RequestKind _requestKind;
            private AutoTagOptions _options;
            private IReadOnlyList<AutoTagRuleSelection> _rules;

            public AutoTagExternalEventHandler(AutoTagMode mode)
            {
                _mode = mode;
            }

            public void Attach(AutoTagOptionsForm form, Document doc)
            {
                _form = form;
                _sourceDocument = doc;
            }

            public void RequestRefresh()
            {
                _requestKind = RequestKind.Refresh;
                _options = null;
                _rules = null;
            }

            public void RequestApply(AutoTagOptions options, IReadOnlyList<AutoTagRuleSelection> rules)
            {
                _requestKind = RequestKind.Apply;
                _options = options;
                _rules = rules;
            }

            public void Execute(UIApplication app)
            {
                try
                {
                    UIDocument uiDoc = app.ActiveUIDocument;
                    if (uiDoc == null)
                    {
                        Complete("目前沒有開啟中的 Revit 文件。", true);
                        return;
                    }

                    Document doc = uiDoc.Document;
                    if (_requestKind == RequestKind.Refresh)
                    {
                        RefreshSources(doc, "已重新讀取目前文件的標籤族型。");
                        return;
                    }

                    if (_requestKind != RequestKind.Apply || _options == null || _rules == null)
                    {
                        Complete();
                        return;
                    }

                    if (!IsSameDocument(doc, _sourceDocument))
                    {
                        RefreshSources(doc, "文件已切換；標籤族型已重新讀取，請確認設定後再次執行。");
                        return;
                    }

                    var service = new AutoTagService();
                    AutoTagResult result = service.TagElements(uiDoc, _mode, _options, _rules);
                    Complete(result.Message, !result.Success);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    Complete("已取消操作。");
                }
                catch (Exception ex)
                {
                    Complete($"命令執行失敗：{ex.Message}", true);
                }
                finally
                {
                    _requestKind = RequestKind.None;
                    _options = null;
                    _rules = null;
                }
            }

            public string GetName()
            {
                return "HB_BIM Auto Tag";
            }

            private void RefreshSources(Document doc, string status)
            {
                _sourceDocument = doc;
                _form?.UpdateTagTypes(doc);
                Complete(status);
            }

            private void Complete(string status = null, bool isError = false)
            {
                _form?.SetRequestCompleted(status, isError);
            }

            private static bool IsSameDocument(Document current, Document expected)
            {
                if (current == null || expected == null)
                    return false;

                if (ReferenceEquals(current, expected))
                    return true;

                try
                {
                    if (!current.IsValidObject || !expected.IsValidObject)
                        return false;

                    if (!string.IsNullOrWhiteSpace(current.PathName) ||
                        !string.IsNullOrWhiteSpace(expected.PathName))
                    {
                        return string.Equals(current.PathName, expected.PathName, StringComparison.OrdinalIgnoreCase);
                    }

                    return current.IsFamilyDocument == expected.IsFamilyDocument &&
                           string.Equals(current.Title, expected.Title, StringComparison.Ordinal);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
