namespace Autodesk.Revit.DB
{
    public enum BuiltInCategory { OST_Conduit = 1, OST_CableTray = 2 }
    public enum StorageType { String }
    public class Parameter { public StorageType StorageType => StorageType.String; public string Text; public string AsString() => Text; }
    public class ElementId { public long Value; public ElementId(long id) { Value = id; } public override string ToString() => Value.ToString(); }
    public class Category { public ElementId Id; }
    public class XYZ
    {
        public double X,Y,Z; public XYZ(double x,double y,double z) { X=x;Y=y;Z=z; }
        public static XYZ operator +(XYZ a, XYZ b) => new XYZ(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static XYZ operator *(XYZ a, double b) => new XYZ(a.X*b,a.Y*b,a.Z*b);
        public double DistanceTo(XYZ b) => Math.Sqrt(Math.Pow(X-b.X,2)+Math.Pow(Y-b.Y,2)+Math.Pow(Z-b.Z,2));
    }
    public class Transform
    {
        public XYZ X=new XYZ(1,0,0),Y=new XYZ(0,1,0),Z=new XYZ(0,0,1),Origin=new XYZ(0,0,0);
        public static Transform Identity => new Transform();
        public XYZ OfPoint(XYZ p) => Origin+X*p.X+Y*p.Y+Z*p.Z;
    }
    public class Curve
    {
        public XYZ A,B; public Curve(XYZ a, XYZ b){A=a;B=b;}
        public Curve CreateTransformed(Transform t) => new Curve(t.OfPoint(A),t.OfPoint(B));
        public XYZ GetEndPoint(int i) => i==0?A:B;
    }
    public class LocationCurve { public Curve Curve; }
    public class Document
    {
        public readonly List<Element> Elements=new List<Element>();
        public Element GetElement(string uid)=>Elements.Find(e=>e.UniqueId==uid);
        public Element GetElement(ElementId id)=>Elements.Find(e=>e.Id.Value==id.Value);
        public T Add<T>(T e) where T:Element { e.Document=this;Elements.Add(e);return e; }
    }
    public class Element
    {
        public Document Document; public ElementId Id=new ElementId(1); public string UniqueId=Guid.NewGuid().ToString(); public string Name="測試";
        public object Location; public Category Category;
        public Dictionary<string,Parameter> Parameters=new Dictionary<string,Parameter>();
        readonly Dictionary<Guid,ExtensibleStorage.Entity> entities=new Dictionary<Guid,ExtensibleStorage.Entity>();
        public Parameter LookupParameter(string name)=>Parameters.TryGetValue(name,out var p)?p:null;
        public ExtensibleStorage.Entity GetEntity(ExtensibleStorage.Schema schema)=>entities.TryGetValue(schema.Id,out var e)?e:new ExtensibleStorage.Entity();
        public void SetEntity(ExtensibleStorage.Entity entity)=>entities[entity.Schema.Id]=entity;
    }
    public class FamilyInstance:Element {}
    public class RevitLinkInstance:Element { public Document Linked; public Transform Transform=Transform.Identity; public Document GetLinkDocument()=>Linked; public Transform GetTotalTransform()=>Transform; }
}
namespace Autodesk.Revit.DB.Plumbing { public class Pipe:Autodesk.Revit.DB.Element {} }
namespace Autodesk.Revit.DB.Mechanical { public class Duct:Autodesk.Revit.DB.Element {} }
namespace Autodesk.Revit.DB.ExtensibleStorage
{
    public enum AccessLevel { Public }
    public class Field { public string Name; }
    public class Schema
    {
        internal static readonly Dictionary<Guid,Schema> Registry=new Dictionary<Guid,Schema>();
        public Guid Id; public Field GetField(string n)=>new Field{Name=n};
        public static Schema Lookup(Guid id)=>Registry.TryGetValue(id,out var s)?s:null;
    }
    public class SchemaBuilder
    {
        readonly Guid id; public SchemaBuilder(Guid id){this.id=id;}
        public void SetSchemaName(string n){} public void SetReadAccessLevel(AccessLevel l){} public void SetWriteAccessLevel(AccessLevel l){} public void AddSimpleField(string n,Type t){}
        public Schema Finish(){var s=new Schema{Id=id};Schema.Registry[id]=s;return s;}
    }
    public class Entity
    {
        public Schema Schema; readonly Dictionary<string,object> values=new Dictionary<string,object>();
        public Entity(){} public Entity(Schema s){Schema=s;}
        public bool IsValid()=>Schema!=null; public T Get<T>(Field f)=>(T)values[f.Name]; public void Set<T>(Field f,T value)=>values[f.Name]=value;
    }
}
namespace YD_RevitTools.LicenseManager.Helpers
{
    public static class IdHelper { public static long GetIdValue(this Autodesk.Revit.DB.ElementId id)=>id.Value; }
    public static class RevitApiCompatibility { public static Autodesk.Revit.DB.ElementId CreateElementId(long id)=>new Autodesk.Revit.DB.ElementId(id); }
}
namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal class PipeSleeveOptions { public List<SleevePipeSource> PipeSources; }
    internal class PipeSleeveCandidate { public SleevePipeSource Source; public Autodesk.Revit.DB.Element Pipe,HostElement; public string HostLinkUniqueId; }
}
