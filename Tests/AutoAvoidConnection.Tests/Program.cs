using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core;

internal static class Program
{
    private static int checks;
    private static readonly XYZ Start=new XYZ(0,0,0), End=new XYZ(10,0,0);
    private static DetourPlan Plan() => new DetourPlan { Path=new List<XYZ> {Start,new XYZ(3,0,0),new XYZ(5,2,0),End} };
    private static T AddCurve<T>(Document doc) where T:MEPCurve,new()
    {
        var curve=doc.Add(new T()); ((LocationCurve)curve.Location).Curve=Line.CreateBound(Start,End); return curve;
    }
    private static void Check(bool condition, string name)
    {
        if(!condition) throw new Exception(name); checks++; Console.WriteLine("PASS "+name);
    }
    private static void RoundTrip<T>() where T:MEPCurve,new()
    {
        var doc=new Document(); var source=AddCurve<T>(doc);
        var before=AddCurve<Pipe>(doc); var after=AddCurve<Duct>(doc);
        source.ConnectorManager.Lookup(0).ConnectTo(before.ConnectorManager.Lookup(1));
        source.ConnectorManager.Lookup(1).ConnectTo(after.ConnectorManager.Lookup(0));
        Check(RevitUtils.ReplaceWithDetour(doc,source,Plan(),new AvoidOptions()),typeof(T).Name+" connected reroute succeeds");
        Check(!source.IsValidObject && doc.ElbowCalls==2,"source deleted only after two valid elbows");
        Check(before.ConnectorManager.Lookup(1).AllRefs.Single().Owner!=source &&
            after.ConnectorManager.Lookup(0).AllRefs.Single().Owner!=source,"both direct external connections restored");
    }
    private static void Main()
    {
        RoundTrip<Pipe>(); RoundTrip<Duct>(); RoundTrip<Conduit>();
        {
            var doc=new Document(); var source=AddCurve<Pipe>(doc);
            Check(RevitUtils.ReplaceWithDetour(doc,source,Plan(),new AvoidOptions()),"open endpoints stay open");
        }
        foreach(var failure in new[] {"missing elbow","wrong elbow","disconnected elbow","disconnect failure","external restoration failure","external owner deleted"})
        {
            var doc=new Document(); var source=AddCurve<Pipe>(doc); var neighbour=AddCurve<Duct>(doc);
            source.ConnectorManager.Lookup(0).ConnectTo(neighbour.ConnectorManager.Lookup(1));
            doc.FailElbowOn=failure=="missing elbow" ? 2 : 0;
            doc.WrongElbow=failure=="wrong elbow";
            doc.IgnoreConnect=failure=="disconnected elbow";
            doc.IgnoreDisconnect=failure=="disconnect failure";
            doc.IgnoreExternalConnect=failure=="external restoration failure";
            doc.CascadeDelete=failure=="external owner deleted"; doc.CascadeTarget=neighbour.Id;
            Check(!RevitUtils.ReplaceWithDetour(doc,source,Plan(),new AvoidOptions()),failure+" propagates false for caller rollback");
            if(!doc.CascadeDelete) Check(source.IsValidObject,"do not delete original before validation failure");
        }
        {
            var doc=new Document(); var source=AddCurve<Pipe>(doc); var neighbour=AddCurve<Duct>(doc);
            var branch=new Connector {Owner=source,Id=2,Origin=new XYZ(5,0,0),ConnectorType=ConnectorType.Curve};
            source.ConnectorManager.Connectors.Add(branch); branch.ConnectTo(neighbour.ConnectorManager.Lookup(0));
            Check(!RevitUtils.ReplaceWithDetour(doc,source,Plan(),new AvoidOptions()) && source.IsValidObject,"connected mid-run branch rejected");
        }
        {
            var doc=new Document(); var source=AddCurve<Pipe>(doc); var plan=Plan(); plan.Path[0]=new XYZ(1,0,0);
            Check(!RevitUtils.ReplaceWithDetour(doc,source,plan,new AvoidOptions()),"changed original endpoint rejected");
        }
        {
            var doc=new Document {IsModifiable=false}; var source=AddCurve<Pipe>(doc);
            Check(!RevitUtils.ReplaceWithDetour(doc,source,Plan(),new AvoidOptions()),"transaction required");
        }
        Console.WriteLine($"{checks} checks passed. Doubles do not validate Revit geometry or real transaction rollback.");
    }
}
