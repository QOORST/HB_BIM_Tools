using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YDBIM.AutoDimension.Core;
using YDBIM.AutoDimension.UI;

namespace YDBIM.AutoDimension.App
{

internal static class AutoDimensionModelessController
{
    private static DimensionOptionsForm? _form;
    private static ExternalEvent? _externalEvent;
    private static AutoDimensionExternalEventHandler? _handler;
    private static DimensionMode _mode;
    private static bool _lockMode;

    public static Result Show(
        ExternalCommandData commandData,
        ref string message,
        DimensionMode mode,
        string windowTitle,
        bool lockMode)
    {
        UIDocument? uiDoc = commandData.Application.ActiveUIDocument;
        if (uiDoc is null)
        {
            message = "目前沒有開啟中的 Revit 文件。";
            return Result.Failed;
        }

        try
        {
            if (_form is not null && !_form.IsDisposed)
            {
                if (_mode == mode && _lockMode == lockMode)
                {
                    _form.Show();
                    _form.Activate();
                    _handler?.RequestRefresh();
                    RaiseExternalEvent();
                    return Result.Succeeded;
                }

                _form.Close();
            }

            Document doc = uiDoc.Document;
            View view = doc.ActiveView;
            SourceData source = CollectSources(doc, view);

            _mode = mode;
            _lockMode = lockMode;
            _handler = new AutoDimensionExternalEventHandler(mode, windowTitle);
            _externalEvent = ExternalEvent.Create(_handler);
            _form = new DimensionOptionsForm(
                source.DimensionTypeNames,
                source.HorizontalGrids,
                source.VerticalGrids,
                mode,
                lockMode,
                windowTitle,
                RequestApply,
                RequestRefresh);

            _handler.Attach(_form, doc, view.Id);
            _form.FormClosed += (_, _) => DisposeWindow();
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

    private static void RequestApply(DimensionOptions options)
    {
        if (_handler is null)
        {
            _form?.SetRequestCompleted("標註事件尚未初始化。", true);
            return;
        }

        _handler.RequestApply(options);
        RaiseExternalEvent();
    }

    private static void RequestRefresh()
    {
        if (_handler is null)
        {
            _form?.SetRequestCompleted("標註事件尚未初始化。", true);
            return;
        }

        _handler.RequestRefresh();
        RaiseExternalEvent();
    }

    private static void RaiseExternalEvent()
    {
        if (_externalEvent is null ||
            _externalEvent.Raise() != ExternalEventRequest.Accepted)
        {
            _form?.SetRequestCompleted("上一個 Revit 動作仍在處理中，請稍候。", true);
        }
    }

    private static void DisposeWindow()
    {
        DimensionOptionsForm? form = _form;
        ExternalEvent? externalEvent = _externalEvent;
        _form = null;
        _externalEvent = null;
        _handler = null;

        if (form is not null && !form.IsDisposed)
        {
            form.Dispose();
        }

        externalEvent?.Dispose();
    }

    private static SourceData CollectSources(Document doc, View view)
    {
        var (horizontal, vertical) = VisibleGridCollector.Collect(doc, view);
        IReadOnlyList<string> typeNames = new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType))
            .ToElements()
            .Select(e => e.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SourceData(typeNames, horizontal, vertical);
    }

    private sealed class SourceData
    {
        public SourceData(
            IReadOnlyList<string> dimensionTypeNames,
            IReadOnlyList<GridSelectionItem> horizontalGrids,
            IReadOnlyList<GridSelectionItem> verticalGrids)
        {
            DimensionTypeNames = dimensionTypeNames;
            HorizontalGrids = horizontalGrids;
            VerticalGrids = verticalGrids;
        }

        public IReadOnlyList<string> DimensionTypeNames { get; }
        public IReadOnlyList<GridSelectionItem> HorizontalGrids { get; }
        public IReadOnlyList<GridSelectionItem> VerticalGrids { get; }
    }

    private enum RequestKind
    {
        None,
        Refresh,
        Apply
    }

    private sealed class AutoDimensionExternalEventHandler : IExternalEventHandler
    {
        private readonly DimensionMode _mode;
        private readonly string _windowTitle;
        private DimensionOptionsForm? _form;
        private Document? _sourceDocument;
        private ElementId _sourceViewId = ElementId.InvalidElementId;
        private RequestKind _requestKind;
        private DimensionOptions? _options;

        public AutoDimensionExternalEventHandler(DimensionMode mode, string windowTitle)
        {
            _mode = mode;
            _windowTitle = windowTitle;
        }

        public void Attach(DimensionOptionsForm form, Document doc, ElementId viewId)
        {
            _form = form;
            _sourceDocument = doc;
            _sourceViewId = viewId;
        }

        public void RequestRefresh()
        {
            _requestKind = RequestKind.Refresh;
            _options = null;
        }

        public void RequestApply(DimensionOptions options)
        {
            _requestKind = RequestKind.Apply;
            _options = options;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument? uiDoc = app.ActiveUIDocument;
                if (uiDoc is null)
                {
                    Complete("目前沒有開啟中的 Revit 文件。", true);
                    return;
                }

                Document doc = uiDoc.Document;
                View view = doc.ActiveView;
                if (_requestKind == RequestKind.Refresh)
                {
                    RefreshSources(doc, view, "已重新讀取目前視圖。");
                    return;
                }

                if (_requestKind != RequestKind.Apply || _options is null)
                {
                    Complete();
                    return;
                }

                if (!ReferenceEquals(doc, _sourceDocument) || view.Id != _sourceViewId)
                {
                    RefreshSources(doc, view, "文件或視圖已切換；設定已重新整理，請確認後再次套用。");
                    return;
                }

                DimensionOptions options = _options;
                if (_mode == DimensionMode.ColumnSetout && options.Mode == PlacementMode.Manual)
                {
                    if (!TryResolveManualPlacement(uiDoc, view, options))
                    {
                        Complete("已取消手動選點。");
                        return;
                    }
                }

                var service = new AutoDimensionService();
                using var tx = new Transaction(doc, _windowTitle);
                tx.Start();
                int created = service.CreateDimensions(doc, view, options);
                if (created == 0)
                {
                    tx.RollBack();
                    Complete(DimensionCommandRunner.BuildNoChangeMessage(_mode, doc, view), true);
                    return;
                }

                tx.Commit();
                Complete($"已建立 {created} 條標註。");
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
            }
        }

        public string GetName()
        {
            return "YD BIM Auto Dimension";
        }

        private void RefreshSources(Document doc, View view, string status)
        {
            SourceData source = CollectSources(doc, view);
            _sourceDocument = doc;
            _sourceViewId = view.Id;
            _form?.UpdateSources(source.DimensionTypeNames, source.HorizontalGrids, source.VerticalGrids);
            Complete(status);
        }

        private bool TryResolveManualPlacement(UIDocument uiDoc, View view, DimensionOptions options)
        {
            _form?.Hide();
            try
            {
                XYZ picked = uiDoc.Selection.PickPoint("請點選標註放置側。");
                XYZ raw = picked - view.Origin;
                if (raw.GetLength() <= 1e-6)
                {
                    return true;
                }

                double alongRight = raw.DotProduct(view.RightDirection.Normalize());
                double alongUp = raw.DotProduct(view.UpDirection.Normalize());
                if (options.ColumnLeftRightSide != LeftRightSide.None)
                {
                    options.ColumnLeftRightSide = alongRight >= 0.0 ? LeftRightSide.Right : LeftRightSide.Left;
                }

                if (options.ColumnFrontBackSide != FrontBackSide.None)
                {
                    options.ColumnFrontBackSide = alongUp >= 0.0 ? FrontBackSide.Front : FrontBackSide.Back;
                }

                return true;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return false;
            }
            finally
            {
                if (_form is not null && !_form.IsDisposed)
                {
                    _form.Show();
                    _form.Activate();
                }
            }
        }

        private void Complete(string? status = null, bool isError = false)
        {
            _form?.SetRequestCompleted(status, isError);
        }
    }
}
}
