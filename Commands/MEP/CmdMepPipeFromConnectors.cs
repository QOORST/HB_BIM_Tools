using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class CmdMepPipeFromConnectors : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            try
            {
                Reference r1 = uiDoc.Selection.PickObject(ObjectType.PointOnElement, new PipeElementFilter(), "選第一個接點（管件/管段端點附近）");
                Reference r2 = uiDoc.Selection.PickObject(ObjectType.PointOnElement, new PipeElementFilter(), "選第二個接點（管件/管段端點附近）");

                Element e1 = doc.GetElement(r1);
                Element e2 = doc.GetElement(r2);
                XYZ p1 = r1.GlobalPoint;
                XYZ p2 = r2.GlobalPoint;
                Connector c1 = FindNearestOpenConnector(e1, p1);
                Connector c2 = FindNearestOpenConnector(e2, p2);
                if (c1 == null || c2 == null)
                {
                    TaskDialog.Show("接點生成管", "找不到可用接點，請點選端點附近。");
                    return Result.Cancelled;
                }

                Pipe seedPipe = e1 as Pipe ?? e2 as Pipe;
                if (seedPipe == null)
                {
                    TaskDialog.Show("接點生成管", "至少其中一個元素需為 Pipe。");
                    return Result.Cancelled;
                }

                ElementId systemTypeId = GetSystemTypeId(seedPipe);
                ElementId pipeTypeId = seedPipe.GetTypeId();
                ElementId levelId = GetReferenceLevelId(seedPipe);
                if (systemTypeId == ElementId.InvalidElementId || levelId == ElementId.InvalidElementId)
                {
                    TaskDialog.Show("接點生成管", "無法取得 Pipe 系統/樓層。");
                    return Result.Cancelled;
                }

                using (Transaction tx = new Transaction(doc, "接點生成管"))
                {
                    tx.Start();
                    Pipe p = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, c1.Origin, c2.Origin);
                    doc.Regenerate();

                    Connector pc1 = FindNearestOpenConnector(p, c1.Origin);
                    Connector pc2 = FindNearestOpenConnector(p, c2.Origin);
                    bool ok1 = TryConnect(doc, c1, pc1);
                    bool ok2 = TryConnect(doc, c2, pc2);
                    tx.Commit();

                    TaskDialog.Show("接點生成管", $"生成完成。\n接點1：{(ok1 ? "成功" : "失敗")}\n接點2：{(ok2 ? "成功" : "失敗")}");
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("接點生成管", $"執行失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static bool TryConnect(Document doc, Connector a, Connector b)
        {
            if (a == null || b == null) return false;
            try
            {
                a.ConnectTo(b);
                doc.Regenerate();
                return true;
            }
            catch
            {
                try
                {
                    var fi = doc.Create.NewElbowFitting(a, b);
                    return fi != null && fi.IsValidObject;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static Connector FindNearestOpenConnector(Element e, XYZ near)
        {
            var connectors = GetConnectors(e);
            Connector best = null;
            double bestD = double.MaxValue;
            foreach (Connector c in connectors)
            {
                if (c.ConnectorType != ConnectorType.End || c.IsConnected) continue;
                double d = c.Origin.DistanceTo(near);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best != null) return best;

            foreach (Connector c in connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                double d = c.Origin.DistanceTo(near);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private static IEnumerable<Connector> GetConnectors(Element e)
        {
            if (e is Pipe p && p.ConnectorManager?.Connectors != null) return p.ConnectorManager.Connectors.Cast<Connector>();
            if (e is FamilyInstance fi && fi.MEPModel?.ConnectorManager?.Connectors != null) return fi.MEPModel.ConnectorManager.Connectors.Cast<Connector>();
            return Enumerable.Empty<Connector>();
        }

        private static ElementId GetReferenceLevelId(Pipe pipe)
        {
            Parameter p = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            return p?.AsElementId() ?? ElementId.InvalidElementId;
        }

        private static ElementId GetSystemTypeId(Pipe pipe)
        {
            Parameter p = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId id = p?.AsElementId() ?? ElementId.InvalidElementId;
            if (id != ElementId.InvalidElementId) return id;
            return pipe?.MEPSystem?.GetTypeId() ?? ElementId.InvalidElementId;
        }

        private class PipeElementFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Pipe || elem is FamilyInstance;
            public bool AllowReference(Reference reference, XYZ position) => true;
        }
    }
}
