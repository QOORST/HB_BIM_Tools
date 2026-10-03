// Behavioral doubles for transaction/control-flow tests, NOT a geometry engine.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
namespace Autodesk.Revit.UI.Selection { public interface ISelectionFilter { bool AllowElement(Autodesk.Revit.DB.Element e); bool AllowReference(Autodesk.Revit.DB.Reference r, Autodesk.Revit.DB.XYZ p); } }
namespace YD_RevitTools.LicenseManager.Helpers { public static class IdExtensions { public static long GetIdValue(this Autodesk.Revit.DB.ElementId id) => id.Value; } }
namespace Autodesk.Revit.DB
{
    public enum BuiltInCategory { OST_Floors, OST_StructuralFraming, OST_Walls, OST_StructuralColumns }
    public enum BuiltInParameter { WALL_HEIGHT_TYPE = -1, WALL_TOP_IS_ATTACHED = -2, WALL_BOTTOM_IS_ATTACHED = -3, WALL_BASE_OFFSET = -4, WALL_USER_HEIGHT_PARAM = -5, WALL_STRUCTURAL_USAGE_PARAM = -6 }
    public enum StorageType { None, Double, Integer, String, ElementId }
    public enum WallKind { Basic }
    public enum ShellLayerType { Exterior }
    public enum ViewDetailLevel { Fine }
    public enum BooleanOperationsType { Intersect }
    public enum TransactionStatus { Uninitialized, Started, Committed, RolledBack, Pending }
    public enum FailureProcessingResult { Continue, ProceedWithRollBack }
    public class ElementId : IEquatable<ElementId> { public int Value; public ElementId(int v) { Value=v; } public static ElementId InvalidElementId=new ElementId(-1); public bool Equals(ElementId o)=>o!=null&&o.Value==Value; public override bool Equals(object o)=>Equals(o as ElementId); public override int GetHashCode()=>Value; public static bool operator ==(ElementId a,ElementId b)=>ReferenceEquals(a,b)||(!(a is null)&&a.Equals(b)); public static bool operator !=(ElementId a,ElementId b)=>!(a==b); }
    public class Category { public ElementId Id; }
    public class Definition { public string Name="Test"; }
    public class Parameter { public bool IsReadOnly, IsShared; public bool HasValue=true; public StorageType StorageType; public ElementId Id=new ElementId(-20); public Guid GUID; public Definition Definition=new Definition(); public object Value; public double AsDouble()=>Convert.ToDouble(Value); public int AsInteger()=>Convert.ToInt32(Value); public string AsString()=>Value as string; public ElementId AsElementId()=>Value as ElementId; public bool Set(double v) {Value=v;return true;} public bool Set(int v) {Value=v;return true;} public bool Set(string v) {Value=v;return true;} public bool Set(ElementId v) {Value=v;return true;} }
    public class ElementIntersectsElementFilter { public ElementIntersectsElementFilter(Element e){} public bool PassesFilter(Element e)=>e.Intersects; }
    public class ElementClassFilter { public Type Type; public ElementClassFilter(Type t){Type=t;} }
    public class Element
    {
        public bool Intersects=true; public Document Document; public ElementId Id, LevelId=new ElementId(2); public ElementId GroupId=ElementId.InvalidElementId, AssemblyInstanceId=ElementId.InvalidElementId; public bool Pinned; public object DesignOption; public Category Category; public List<Parameter> Parameters=new List<Parameter>(); public List<ElementId> Dependents=new List<ElementId>(); public List<Guid> Schemas=new List<Guid>(); public GeometryElement Geometry=new GeometryElement();
        public ICollection<ElementId> GetMaterialIds(bool paint)=>new List<ElementId>();
        public virtual ElementId GetTypeId()=>new ElementId(3); public IList<Guid> GetEntitySchemaGuids()=>Schemas;
        public IList<ElementId> GetDependentElements(ElementClassFilter f)=>Dependents.Where(id=>f==null||f.Type.IsInstanceOfType(Document.GetElement(id))).ToList();
        public virtual Parameter get_Parameter(BuiltInParameter p)=>Parameters.FirstOrDefault(x=>x.Id.Value==(int)p);
        public Parameter get_Parameter(Guid g)=>Parameters.FirstOrDefault(x=>x.GUID==g); public Parameter get_Parameter(Definition d)=>Parameters.FirstOrDefault(x=>x.Definition==d);
        public GeometryElement get_Geometry(Options o)=>Geometry; public virtual object GetGeometryObjectFromReference(Reference r)=>r.Face;
    }
    public class Floor : Element
    {
        public List<Reference> Faces=new List<Reference>();
        public static Floor Create(Document d,List<CurveLoop> l,ElementId t,ElementId level) { d.CreateCount++; if(d.FailCreate==d.CreateCount) throw new InvalidOperationException("Injected creation failure"); var f=new Floor{Document=d,Id=new ElementId(100+d.CreateCount)}; f.Geometry.Add(new Solid(l[0].Cell)); d.Elements.Add(f); return f; }
    }
    public class Wall : Element { public object Location; public bool Flipped; public List<Reference> Faces=new List<Reference>(); public static Wall Create(Document d,Line l,ElementId t,ElementId level,double h,double b,bool flipped,bool structural)=>throw new NotImplementedException(); }
    public class WallType : Element { public WallKind Kind=WallKind.Basic; }
    public class FloorType : Element { }
    public class Level : Element { public double Elevation; }
    public class Sketch : Element { public Element SketchPlane; }
    public class CurveElement : Element { }
    public class LocationCurve { public Curve Curve; }
    public class Document
    {
        public List<Element> Elements=new List<Element>(); public int CreateCount, FailCreate, Joins; public bool DeleteExtra, RejectCommit; public int Commits;
        public Element GetElement(ElementId id)=>Elements.FirstOrDefault(e=>e.Id==id);
        public ICollection<ElementId> Delete(ElementId id) { Elements.RemoveAll(e=>e.Id==id); return DeleteExtra?new[]{id,new ElementId(999)}:new[]{id}; }
        public void Regenerate() { }
    }
    public class Transaction : IDisposable
    {
        Document d; List<Element> saved; int savedJoins; TransactionStatus status;
        public Transaction(Document d,string name){this.d=d;} public TransactionStatus Start(){saved=d.Elements.ToList();savedJoins=d.Joins;return status=TransactionStatus.Started;}
        public TransactionStatus GetStatus()=>status; public TransactionStatus RollBack(){d.Elements=saved;d.Joins=savedJoins;return status=TransactionStatus.RolledBack;}
        public TransactionStatus Commit(){if(d.RejectCommit)return RollBack();d.Commits++;return status=TransactionStatus.Committed;}
        public FailureHandlingOptions GetFailureHandlingOptions()=>new FailureHandlingOptions(); public void SetFailureHandlingOptions(FailureHandlingOptions o){} public void Dispose(){if(status==TransactionStatus.Started)RollBack();}
    }
    public class FailureHandlingOptions { public FailureHandlingOptions SetFailuresPreprocessor(IFailuresPreprocessor p)=>this; public FailureHandlingOptions SetClearAfterRollback(bool v)=>this; public FailureHandlingOptions SetForcedModalHandling(bool v)=>this; }
    public interface IFailuresPreprocessor { FailureProcessingResult PreprocessFailures(FailuresAccessor a); }
    public class FailuresAccessor { public IList<object> GetFailureMessages()=>new List<object>(); }
    public static class JoinGeometryUtils { public static IList<ElementId> GetJoinedElements(Document d,Element e)=>new List<ElementId>(); public static bool AreElementsJoined(Document d,Element a,Element b)=>false; public static void JoinGeometry(Document d,Element a,Element b){d.Joins++;} public static bool IsCuttingElementInJoin(Document d,Element a,Element b)=>true; public static void SwitchJoinOrder(Document d,Element a,Element b){} }
    public static class WallUtils { public static void DisallowWallJoinAtEnd(Wall w,int e){} }
    public static class HostObjectUtils { public static IList<Reference> GetTopFaces(Floor f)=>f.Faces; public static IList<Reference> GetSideFaces(Wall w,ShellLayerType t)=>w.Faces; }
    public class Reference { public PlanarFace Face; }
    public class PlanarFace { public XYZ FaceNormal=XYZ.BasisZ; public List<CurveLoop> Loops=new List<CurveLoop>(); public IList<CurveLoop> GetEdgesAsCurveLoops()=>Loops; }
    public class XYZ
    {
        public double X,Y,Z; public XYZ(double x,double y,double z){X=x;Y=y;Z=z;} public static XYZ BasisX=new XYZ(1,0,0),BasisY=new XYZ(0,1,0),BasisZ=new XYZ(0,0,1); public double DotProduct(XYZ b)=>X*b.X+Y*b.Y+Z*b.Z; public XYZ Multiply(double m)=>new XYZ(X*m,Y*m,Z*m); public static XYZ operator +(XYZ a,XYZ b)=>new XYZ(a.X+b.X,a.Y+b.Y,a.Z+b.Z); public static XYZ operator -(XYZ a,XYZ b)=>new XYZ(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    }
    public abstract class Curve { public abstract double Length{get;} public abstract XYZ GetEndPoint(int i); }
    public class Line : Curve
    {
        XYZ a,b; public static Line CreateBound(XYZ a,XYZ b)=>new Line{a=a,b=b}; public override double Length=>Math.Sqrt((b-a).DotProduct(b-a)); public XYZ Direction=>(b-a).Multiply(1/Length); public override XYZ GetEndPoint(int i)=>i==0?a:b;
    }
    public class CurveLoop : List<Curve> { public int Cell; public static CurveLoop CreateViaTransform(CurveLoop l,Transform t)=>l; }
    public class Transform { public static Transform Identity=new Transform(); public static Transform CreateTranslation(XYZ p)=>new Transform(); }
    public class Options { public ViewDetailLevel DetailLevel; }
    public class GeometryElement : List<object> { }
    public class GeometryInstance { }
    public class Solid { public HashSet<int> Cells; public Solid(params int[] cells){Cells=new HashSet<int>(cells);} public double Volume=>Cells.Count; }
    public static class SolidUtils { public static Solid CreateTransformed(Solid s,Transform t)=>new Solid(s.Cells.ToArray()); }
    public static class BooleanOperationsUtils { public static Solid ExecuteBooleanOperation(Solid a,Solid b,BooleanOperationsType type)=>new Solid(a.Cells.Intersect(b.Cells).ToArray()); }
}
