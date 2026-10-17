using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI.Selection;

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
                _form = new AutoTagOptionsForm(uiDoc.Document, mode, RequestApply, RequestRefresh, RequestPick);
                _form.SetSelectionCount(uiDoc.Selection.GetElementIds().Count);
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

        private static void RequestPick()
        {
            if (_handler == null) { _form?.SetRequestCompleted("標籤事件尚未初始化。", true); return; }
            _handler.RequestPick(_form.PickRequest);
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
            Pick,
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

            public void RequestPick(AutoTagOptions options)
            {
                _requestKind = RequestKind.Pick;
                _options = options;
                _rules = null;
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
                    _form?.SetSelectionCount(uiDoc.Selection.GetElementIds().Count);
                    if (_requestKind == RequestKind.Pick)
                    {
                        if (!IsSameDocument(doc, _sourceDocument))
                        {
                            RefreshSources(doc, "文件已切換；請確認族型設定後重新選取。");
                            return;
                        }
                        var previous = uiDoc.Selection.GetElementIds().ToList();
                        _form?.Hide();
                        try
                        {
                            if (_options?.Scope == AutoTagScope.LinkedSelection)
                            {
                                var link = string.IsNullOrEmpty(_options.LinkInstanceUniqueId) ? null : doc.GetElement(_options.LinkInstanceUniqueId) as RevitLinkInstance;
                                var linkedDoc = link?.GetLinkDocument();
                                if (linkedDoc == null) throw new InvalidOperationException("指定連結已卸載或不存在，請重新整理並選擇連結。");
                                var pickedLinks = uiDoc.Selection.PickObjects(ObjectType.LinkedElement, new LinkedTagTargetFilter(doc, link),
                                    "點選指定連結「" + link.Name + "」中的構件，按完成；Esc 保留原清單。");
                                var uniqueIds = pickedLinks.Select(reference => linkedDoc.GetElement(reference.LinkedElementId)?.UniqueId)
                                    .Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
                                _form?.SetLinkedSelection(link.UniqueId, uniqueIds, doc.ActiveView.UniqueId);
                                Complete($"已選取 {uniqueIds.Count} 個連結構件；僅處理本清單中已勾選分類的元素。");
                                return;
                            }
                            var picked = uiDoc.Selection.PickObjects(ObjectType.Element, new TagTargetFilter(), "選取要標註的構件，按完成；Esc 保留原選取。");
                            var ids = picked.Select(r => r.ElementId).Distinct().ToList();
                            uiDoc.Selection.SetElementIds(ids);
                            _form?.SetSelectionCount(ids.Count);
                            Complete($"已選取 {ids.Count} 個構件。");
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            if (_options?.Scope != AutoTagScope.LinkedSelection) uiDoc.Selection.SetElementIds(previous);
                            _form?.SetSelectionCount(previous.Count);
                            Complete("已取消選取，保留原選取。");
                        }
                        finally
                        {
                            if (_form != null && !_form.IsDisposed) { _form.Show(); _form.Activate(); }
                        }
                        return;
                    }
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
                    AutoTagResult result;
                    AutoTagPreviewDecision previewDecision = null;
                    var exclusions = new HashSet<string>(StringComparer.Ordinal);
                    if (_options.PreviewBeforeApply)
                    {
                        var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == doc.ActiveView.Id);
                        var zoom = uiView?.GetZoomCorners();
                        _form?.Hide();
                        try
                        {
                            do
                            {
                                previewDecision = null;
                                result = service.TagElements(uiDoc, _mode, _options, _rules, items =>
                                {
                                    using (var preview = new AutoTagPreviewForm(uiDoc, items))
                                    {
                                        preview.ShowDialog(new RevitWindow(app.MainWindowHandle));
                                        previewDecision = preview.Decision;
                                        exclusions.UnionWith(previewDecision.Excluded);
                                        return previewDecision;
                                    }
                                }, exclusions);
                            } while (previewDecision?.Action == AutoTagPreviewAction.Recalculate);
                        }
                        finally
                        {
                            try
                            {
                                if (zoom != null && zoom.Count == 2) uiView.ZoomAndCenterRectangle(zoom[0], zoom[1]);
                                uiDoc.RefreshActiveView();
                            }
                            finally
                            {
                                if (_form != null && !_form.IsDisposed) { _form.Show(); _form.Activate(); }
                            }
                        }
                        if (previewDecision?.Action == AutoTagPreviewAction.Cancel)
                        {
                            Complete("已取消預覽，未保留本批標籤。");
                            return;
                        }
                    }
                    else result = service.TagElements(uiDoc, _mode, _options, _rules);
                    Complete(result.Message, !result.Success);
                    // Clean success remains in the modeless status area; review cases keep actionable details.
                    if (result.Success && result.Created > 0 && result.Skipped == 0 &&
                        result.FailedCount == 0 && result.ReviewTagIds.Count == 0 &&
                        result.SkippedTagIds.Count == 0 && result.Processed == result.Matched)
                        return;
                    var report = new TaskDialog("自動標籤結果")
                    {
                        MainInstruction = result.Message,
                        ExpandedContent = result.Details,
                        CommonButtons = TaskDialogCommonButtons.Close
                    };
                    if (result.ReviewTagIds.Count > 0)
                        report.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "選取需複核標籤");
                    if (result.SkippedTagIds.Count > 0)
                        report.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "選取造成略過的既有標籤");
                    var response = report.Show();
                    if (response == TaskDialogResult.CommandLink1)
                        uiDoc.Selection.SetElementIds(new List<ElementId>(result.ReviewTagIds));
                    else if (response == TaskDialogResult.CommandLink2)
                        uiDoc.Selection.SetElementIds(new List<ElementId>(result.SkippedTagIds));
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

            private sealed class RevitWindow : System.Windows.Forms.IWin32Window
            {
                public RevitWindow(IntPtr handle) { Handle = handle; }
                public IntPtr Handle { get; }
            }

            private sealed class TagTargetFilter : ISelectionFilter
            {
                public bool AllowElement(Element element) => element.Category != null && !(element is ElementType) &&
                    AutoTagService.CategoryRules.Any(rule => element.Category.Id == new ElementId(rule.ElementCategory));
                public bool AllowReference(Reference reference, XYZ point) => false;
            }

            private sealed class LinkedTagTargetFilter : ISelectionFilter
            {
                private readonly Document doc;
                private readonly RevitLinkInstance link;
                internal LinkedTagTargetFilter(Document doc, RevitLinkInstance link) { this.doc = doc; this.link = link; }
                public bool AllowElement(Element element) => element.Id == link.Id;
                public bool AllowReference(Reference reference, XYZ point)
                {
                    if (reference.ElementId != link.Id || doc.GetElement(reference.ElementId) == null) return false;
                    var element = link.GetLinkDocument()?.GetElement(reference.LinkedElementId);
                    return element?.Category != null && !(element is ElementType) && !element.ViewSpecific &&
                        AutoTagService.CategoryRules.Any(rule => element.Category.Id == new ElementId(rule.ElementCategory));
                }
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

                    return current.Equals(expected);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
