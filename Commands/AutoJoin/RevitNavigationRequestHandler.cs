using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    internal sealed class RevitNavigationRequestHandler : IExternalEventHandler
{
    private readonly object _sync = new();
    private List<ElementId> _pendingIds;
    private Document _document;
    public void BindDocument(Document document)
    {
        lock (_sync) { _document = document; _pendingIds = null; }
    }

    public void RequestShowElements(IList<ElementId> ids)
    {
        if (ids == null || ids.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            _pendingIds = ids.Where(id => id != null && id != ElementId.InvalidElementId).Distinct().ToList();
        }
    }

    public void Execute(UIApplication app)
    {
        List<ElementId> ids;
        lock (_sync)
        {
            ids = _pendingIds;
            _pendingIds = null;
        }

        if (ids == null || ids.Count == 0)
        {
            return;
        }

        var uiDoc = app.ActiveUIDocument;
        if (uiDoc == null || _document == null || !_document.IsValidObject || uiDoc.Document != _document)
        {
            return;
        }

        ids = ids.Where(id => uiDoc.Document.GetElement(id) != null).ToList();
        if (ids.Count == 0) return;
        var view3D = Find3DView(uiDoc.Document);
        if (view3D != null && uiDoc.ActiveView.Id != view3D.Id)
        {
            uiDoc.ActiveView = view3D;
        }

        uiDoc.Selection.SetElementIds(ids);
        uiDoc.ShowElements(ids);
    }

    public string GetName()
    {
        return "AutoJoin.RevitNavigationRequestHandler";
    }

    private static View3D Find3DView(Document doc)
    {
        var collector = new FilteredElementCollector(doc).OfClass(typeof(View3D));
        foreach (var element in collector)
        {
            if (element is View3D view && !view.IsTemplate)
            {
                return view;
            }
        }

        return null;
    }
    }
}
