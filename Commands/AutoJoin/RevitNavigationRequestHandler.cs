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
    private JoinFailureDetail _pendingDiagnostic;
    public event System.Action<JoinFailureDetail, string> DiagnosticCompleted;
    public void RequestDiagnostic(JoinFailureDetail item)
    {
        lock (_sync) { _pendingDiagnostic = item; }
    }
    private Document _document;
    private string _inspectionViewUniqueId;
    public event System.Action<string> StatusChanged;
    private void Fail(string message)
    {
        StatusChanged?.Invoke("定位未完成：" + message);
        TaskDialog.Show("自動接合定位", message);
    }
    public void BindDocument(Document document)
    {
        lock (_sync) {
            if (_document == null || !_document.IsValidObject || !_document.Equals(document)) _inspectionViewUniqueId = null;
            _document = document; _pendingIds = null; _pendingDiagnostic = null;
        }
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
        try { ExecuteDiagnostic(app); ExecuteNavigation(app); }
        catch (System.Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private void ExecuteNavigation(UIApplication app)
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
        if (uiDoc == null || _document == null || !_document.IsValidObject || !uiDoc.Document.Equals(_document))
        {
            Fail("請先切回產生這份結果的模型，再執行定位。");
            return;
        }

        ids = ids.Where(id => uiDoc.Document.GetElement(id) != null).ToList();
        if (ids.Count == 0)
        {
            Fail("這筆結果的構件已不存在，請重新執行檢查。");
            return;
        }
        var view3D = PrepareInspectionView(uiDoc.Document, ids);
        if (view3D != null && uiDoc.ActiveView.Id != view3D.Id)
        {
            uiDoc.ActiveView = view3D;
        }

        uiDoc.Selection.SetElementIds(ids);
        uiDoc.ShowElements(ids);
        StatusChanged?.Invoke("已隔離並選取 " + ids.Count + " 個構件｜接合檢查視圖");
    }

    private void ExecuteDiagnostic(UIApplication app)
    {
        JoinFailureDetail item;
        lock (_sync) { item = _pendingDiagnostic; _pendingDiagnostic = null; }
        if (item == null) return;
        string result;
        try
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || _document == null || !_document.IsValidObject || !doc.Equals(_document))
                result = "無法判定：請切回產生清單的模型。";
            else
                result = Diagnose(doc.GetElement(item.FirstElementId), doc.GetElement(item.SecondElementId));
        }
        catch (System.Exception ex) { result = "無法判定：" + ex.Message; }
        DiagnosticCompleted?.Invoke(item, result);
    }

    private static string Diagnose(Element a, Element b)
    {
        if (a == null || b == null) return "無法判定：構件已刪除。";
        var options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
        using (var ga = a.get_Geometry(options))
        using (var gb = b.get_Geometry(options))
        {
            var sa = new List<Solid>(); var sb = new List<Solid>();
            CollectSolids(ga, sa); CollectSolids(gb, sb);
            if (sa.Count == 0 || sb.Count == 0) return "無法判定：至少一個構件沒有可分析實體。";
            if ((long)sa.Count * sb.Count > 400) return "無法判定：實體組合過多，請改用原生工具檢查。";
            bool failed = false;
            foreach (var x in sa) foreach (var y in sb)
            {
                try
                {
                    using (var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(x, y, BooleanOperationsType.Intersect))
                        if (intersection.Volume > 1e-9)
                            return "目前有實體交集；先前接合遭拒。交集不代表一定可接合，仍需原生接合核對。";
                }
                catch { failed = true; }
            }
            return failed ? "無法判定：部分實體交集運算失敗。" :
                "目前未檢出正體積交集（容差約 0.028 mm³）；可能分離、僅接觸或交集過小，不代表模型錯誤。";
        }
    }

    private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
    {
        if (geometry == null) return;
        foreach (var obj in geometry)
        {
            if (obj is Solid solid && solid.Volume > 1e-9) solids.Add(solid);
            else if (obj is GeometryInstance instance) CollectSolids(instance.GetInstanceGeometry(), solids);
        }
    }

    public string GetName()
    {
        return "AutoJoin.RevitNavigationRequestHandler";
    }

    private View3D PrepareInspectionView(Document doc, IList<ElementId> ids)
    {
        var points = new List<XYZ>();
        foreach (var id in ids)
        {
            var box = doc.GetElement(id)?.get_BoundingBox(null);
            if (box == null) throw new System.InvalidOperationException("構件 " + id + " 無可用幾何範圍，未變更檢查視圖。");
            for (int x=0;x<2;x++) for(int y=0;y<2;y++) for(int z=0;z<2;z++)
                points.Add(box.Transform.OfPoint(new XYZ(x==0?box.Min.X:box.Max.X,y==0?box.Min.Y:box.Max.Y,z==0?box.Min.Z:box.Max.Z)));
        }
        var min = new XYZ(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z));
        var max = new XYZ(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z));
        double padding = System.Math.Max(300.0/304.8, max.DistanceTo(min)*0.08);
        var offset = new XYZ(padding,padding,padding);
        var region = new BoundingBoxXYZ {Min=min-offset,Max=max+offset};
        // Reuse only a view created by this handler, never a user view found by name.
        var view = _inspectionViewUniqueId == null ? null : doc.GetElement(_inspectionViewUniqueId) as View3D;
        using (var tx = new Transaction(doc,"自動接合：準備檢查視圖"))
        {
            tx.Start();
            if(view==null || view.IsTemplate || view.IsPerspective)
            {
                var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(t=>t.ViewFamily==ViewFamily.ThreeDimensional);
                if(type==null) throw new System.InvalidOperationException("模型沒有可用的 3D 視圖類型。");
                view=View3D.CreateIsometric(doc,type.Id);
                view.Name="HB_接合檢查_"+System.Guid.NewGuid().ToString("N").Substring(0,8);
                view.ViewTemplateId=ElementId.InvalidElementId;
                view.DisplayStyle=DisplayStyle.HLR;
            }
            if(view.IsTemporaryHideIsolateActive()) view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            view.SetSectionBox(region);
            view.IsSectionBoxActive=true;
            view.IsolateElementsTemporary(ids);
            doc.Regenerate();
            if(tx.Commit()!=TransactionStatus.Committed) throw new System.InvalidOperationException("檢查視圖設定未成功提交。");
        }
        _inspectionViewUniqueId=view.UniqueId;
        return view;
    }
    }
}
