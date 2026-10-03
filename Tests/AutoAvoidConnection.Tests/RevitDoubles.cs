// Focused API doubles: exercise production control flow, not Revit geometry/transactions.
using System;
using System.Collections.Generic;
using System.Linq;
namespace Autodesk.Revit.DB
{
    public class ElementId
    {
        public static readonly ElementId InvalidElementId = new ElementId(-1);
        public int Value; public ElementId(int value) { Value = value; }
        public override string ToString() => Value.ToString();
    }
    public class XYZ
    {
        public double X, Y, Z; public XYZ(double x, double y, double z) { X=x; Y=y; Z=z; }
        public double DistanceTo(XYZ p) => Math.Sqrt(Math.Pow(X-p.X,2)+Math.Pow(Y-p.Y,2)+Math.Pow(Z-p.Z,2));
    }
    public abstract class Curve { public abstract XYZ GetEndPoint(int index); }
    public class Line : Curve
    {
        private XYZ a,b; public static Line CreateBound(XYZ a, XYZ b) => new Line { a=a, b=b };
        public override XYZ GetEndPoint(int index) => index==0 ? a : b;
    }
    public class LocationCurve
    {
        private Curve curve; private MEPCurve owner;
        public LocationCurve(MEPCurve owner) { this.owner=owner; }
        public Curve Curve { get => curve; set { curve=value; for(int i=0;i<2;i++) owner.ConnectorManager.Connectors[i].Origin=value.GetEndPoint(i); } }
    }
    public enum BuiltInParameter { RBS_CONDUIT_DIAMETER_PARAM }
    public class Parameter
    {
        public bool IsReadOnly; public bool RefuseSet; public double Value=1;
        public double AsDouble() => Value;
        public bool Set(double value) { if(RefuseSet) return false; Value=value; return true; }
    }
    public class Element
    {
        public ElementId Id; public Document Document; public bool IsValidObject=true;
        public object Location; public ElementId LevelId = new ElementId(1);
        public Parameter Diameter = new Parameter();
        public ElementId GetTypeId() => new ElementId(1);
        public Parameter get_Parameter(BuiltInParameter parameter) => Diameter;
    }
    public enum ConnectorType { End, Logical, Curve }
    public class Connector
    {
        public Element Owner; public int Id; public XYZ Origin;
        public ConnectorType ConnectorType=ConnectorType.End;
        public List<Connector> AllRefs=new List<Connector>();
        public bool IsConnected => AllRefs.Count>0;
        public bool IsConnectedTo(Connector other) => AllRefs.Contains(other);
        public void ConnectTo(Connector other)
        {
            if (Owner.Document.IgnoreConnect || (Owner.Document.IgnoreExternalConnect && Owner is MEPCurve)) return;
            if (!AllRefs.Contains(other)) AllRefs.Add(other);
            if (!other.AllRefs.Contains(this)) other.AllRefs.Add(this);
        }
        public void DisconnectFrom(Connector other)
        {
            if (Owner.Document.IgnoreDisconnect) return;
            AllRefs.Remove(other); other.AllRefs.Remove(this);
        }
    }
    public class ConnectorManager
    {
        public List<Connector> Connectors=new List<Connector>();
        public Connector Lookup(int id) => Connectors.FirstOrDefault(c=>c.Id==id);
    }
    public class MEPCurve : Element
    {
        public ConnectorManager ConnectorManager=new ConnectorManager();
        public MEPCurve()
        {
            ConnectorManager.Connectors.Add(new Connector { Owner=this, Id=0 });
            ConnectorManager.Connectors.Add(new Connector { Owner=this, Id=1 });
            Location=new LocationCurve(this);
        }
    }
    public class MEPModel { public ConnectorManager ConnectorManager=new ConnectorManager(); }
    public class FamilyInstance : Element { public MEPModel MEPModel=new MEPModel(); }
    public class Document
    {
        private int nextId=1; public bool IsModifiable=true;
        public bool IgnoreConnect, IgnoreExternalConnect, IgnoreDisconnect, WrongElbow, CascadeDelete;
        public int FailElbowOn, ElbowCalls, Regenerations;
        public ElementId CascadeTarget;
        public Dictionary<ElementId,Element> Elements=new Dictionary<ElementId,Element>();
        public Document Create => this;
        public T Add<T>(T element) where T:Element { element.Id=new ElementId(nextId++); element.Document=this; Elements.Add(element.Id,element); return element; }
        public Element GetElement(ElementId id) => Elements.TryGetValue(id,out var e) && e.IsValidObject ? e : null;
        public void Regenerate() { Regenerations++; }
        public ICollection<ElementId> Delete(ElementId id)
        {
            Elements[id].IsValidObject=false;
            var deleted=new List<ElementId> {id};
            if(CascadeDelete && CascadeTarget!=null) { Elements[CascadeTarget].IsValidObject=false; deleted.Add(CascadeTarget); }
            return deleted;
        }
        public FamilyInstance NewElbowFitting(Connector first, Connector second)
        {
            if (++ElbowCalls==FailElbowOn) throw new InvalidOperationException("Fitting creation failed");
            var elbow=Add(new FamilyInstance());
            var a=new Connector { Owner=elbow, Id=0, Origin=first.Origin };
            var b=new Connector { Owner=elbow, Id=1, Origin=second.Origin };
            elbow.MEPModel.ConnectorManager.Connectors.AddRange(new[] {a,b});
            a.ConnectTo(first); b.ConnectTo(WrongElbow ? first : second); return elbow;
        }
    }
    public static class ElementTransformUtils
    {
        public static ICollection<ElementId> CopyElement(Document doc, ElementId id, XYZ offset)
        {
            var source=(MEPCurve)doc.GetElement(id);
            var clone=(MEPCurve)Activator.CreateInstance(source.GetType()); doc.Add(clone);
            ((LocationCurve)clone.Location).Curve=((LocationCurve)source.Location).Curve;
            return new List<ElementId> {clone.Id};
        }
    }
}
namespace Autodesk.Revit.DB.Mechanical { public class Duct : Autodesk.Revit.DB.MEPCurve {} }
namespace Autodesk.Revit.DB.Plumbing { public class Pipe : Autodesk.Revit.DB.MEPCurve {} }
namespace Autodesk.Revit.DB.Electrical
{
    public class Conduit : Autodesk.Revit.DB.MEPCurve
    {
        public static Conduit Create(Autodesk.Revit.DB.Document doc, Autodesk.Revit.DB.ElementId type,
            Autodesk.Revit.DB.XYZ a, Autodesk.Revit.DB.XYZ b, Autodesk.Revit.DB.ElementId level)
        {
            var result=doc.Add(new Conduit()); ((Autodesk.Revit.DB.LocationCurve)result.Location).Curve=Autodesk.Revit.DB.Line.CreateBound(a,b); return result;
        }
    }
}
namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    public class AvoidOptions {}
    public class DetourPlan { public bool IsValid=true; public List<Autodesk.Revit.DB.XYZ> Path; }
    public static class Logger
    {
        public static void Warning(string text) {} public static void Info(string text) {}
        public static void Error(string text, Exception error) {}
    }
}
