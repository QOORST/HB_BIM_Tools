using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.AR.AutoJoin;

class Program
{
    static int tests;
    static void Main()
    {
        Check("committed counts", (d,f)=>{}, r=>r.OriginalDeleted==1&&r.NewElementsCreated==2&&r.FailedOperations==0, false);
        Check("second creation failure rolls back first piece and joins", (d,f)=>d.FailCreate=2, r=>r.OriginalDeleted==0&&r.NewElementsCreated==0&&r.FailedOperations==1, true);
        Check("commit rollback publishes no success", (d,f)=>d.RejectCommit=true, r=>r.OriginalDeleted==0&&r.NewElementsCreated==0&&r.FailedOperations==1, true);
        Check("deletion cascade rolls back", (d,f)=>d.DeleteExtra=true, r=>r.Skipped==1&&r.OriginalDeleted==0, true);
        Check("holes stay within one region", (d,f)=>{
            f.Faces[0].Face.Loops.Add(Rectangle(3,.2,.2,.5));
            d.AfterRegenerate=()=>{var pieces=d.Elements.OfType<Floor>().Where(x=>x!=f).ToList();if(pieces.Count>0)Require(pieces.Count==2&&pieces[0].Profiles.Count==2&&pieces[1].Profiles.Count==1,"inner loop passed with outer loop");};
        }, r=>r.NewElementsCreated==2, false);
        Check("one face is not a split", (d,f)=>f.Faces.RemoveAt(1), r=>r.Skipped==1, true);
        Check("one face can contain two split islands", (d,f)=>{f.Faces.RemoveAt(1);f.Faces[0].Face.Loops.Add(Rectangle(2,3,0));},r=>r.NewElementsCreated==2,false);
        Check("one outer and one hole is not two pieces", (d,f)=>{f.Faces.RemoveAt(1);f.Faces[0].Face.Loops.Add(Rectangle(3,.2,.2,.5));},r=>r.Skipped==1,true);
        Check("loop order and winding do not identify holes", (d,f)=>{
            f.Faces.RemoveAt(1);f.Faces[0].Face.Loops=new List<CurveLoop>{Rectangle(3,.2,.2,.5,true),Rectangle(2,3,0,1,true),Rectangle(1)};
            d.AfterRegenerate=()=>{var pieces=d.Elements.OfType<Floor>().Where(x=>x!=f).ToList();if(pieces.Count>0)Require(pieces.Any(p=>p.Profiles.Count==2),"hole assigned to enclosing outer");};
        },r=>r.NewElementsCreated==2,false);
        Check("sloped floor skipped", (d,f)=>f.Faces[0].Face.FaceNormal=XYZ.BasisX, r=>r.Skipped==1, true);
        Check("overlapping regions rejected", (d,f)=>f.Faces[1].Face.Loops[0].Cell=1, r=>r.Skipped==1, true);
        Check("missing volume rejected", (d,f)=>f.Geometry[0]=new Solid(1,2,3), r=>r.Skipped==1, true);
        Check("hosted dependency rejected before joins", (d,f)=>f.Dependents.Add(new ElementId(888)), r=>r.Skipped==1, true);
        Check("untransferable parameter rejected", (d,f)=>f.Parameters.Add(new Parameter{StorageType=StorageType.String,Value="Keep me"}), r=>r.Skipped==1, true);
        Check("pinned original preserved", (d,f)=>f.Pinned=true, r=>r.Skipped==1, true);
        Check("custom schema preserved", (d,f)=>f.Schemas.Add(Guid.NewGuid()), r=>r.Skipped==1, true);
        CheckWallPartialOverlap();
        CheckBatchPartialSuccess();
        CheckSelection();
        CheckParameters();
        CheckWarnings();
        Console.WriteLine($"PASS {tests} source-linked split safety tests (stub control flow only).");
    }
    static void Require(bool ok,string name) { if(!ok)throw new Exception(name); tests++; }
    static void CheckSelection()
    {
        var filter=new FloorAndFramingFilter();
        foreach(var category in new[]{BuiltInCategory.OST_Floors,BuiltInCategory.OST_StructuralFraming,BuiltInCategory.OST_StructuralColumns})
            Require(filter.AllowElement(new Element{Category=new Category{Id=new ElementId((int)category)}}),"floor/beam/column selectable");
        Require(!filter.AllowElement(new Element{Category=new Category{Id=new ElementId((int)BuiltInCategory.OST_Walls)}}),"wall rejected");
        Require(!filter.AllowElement(null)&&!filter.AllowElement(new Element()),"null/categoryless rejected");
    }
    static Parameter Text(BuiltInParameter id,string value)=>new Parameter{Id=new ElementId((int)id),StorageType=StorageType.String,Value=value};
    static void CheckParameters()
    {
        var guid=Guid.NewGuid(); var definition=new Definition{Name="施工備註"};
        Action<Document,Floor> setup=(d,f)=>{
            f.Parameters.Add(Text(BuiltInParameter.ALL_MODEL_MARK,"F-01"));
            f.Parameters.Add(Text(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,"施工縫北側"));
            f.Parameters.Add(new Parameter{Id=new ElementId(900),IsShared=true,GUID=guid,StorageType=StorageType.String,Value="共享編號"});
            f.Parameters.Add(new Parameter{Id=new ElementId(901),Definition=definition,StorageType=StorageType.String,Value="專案備註"});
            d.NewParameters.Add(()=>Text(BuiltInParameter.ALL_MODEL_MARK,"generated"));
            d.NewParameters.Add(()=>Text(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,"default"));
            d.NewParameters.Add(()=>new Parameter{Id=new ElementId(900),IsShared=true,GUID=guid,StorageType=StorageType.String,Value=""});
            d.NewParameters.Add(()=>new Parameter{Id=new ElementId(901),Definition=definition,StorageType=StorageType.String,Value=""});
            d.AfterRegenerate=()=>{
                foreach(var piece in d.Elements.OfType<Floor>().Where(x=>x!=f))
                    Require(piece.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString()=="F-01" &&
                        piece.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString()=="施工縫北側" &&
                        piece.get_Parameter(guid).AsString()=="共享編號" && piece.get_Parameter(definition).AsString()=="專案備註","parameters inherited on each piece");
            };
        };
        Check("built-in shared project inheritance",setup,r=>r.NewElementsCreated==2,false);
        Check("empty comment clears default",(d,f)=>{
            var empty=Text(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,null); empty.HasValue=false; f.Parameters.Add(empty);
            d.NewParameters.Add(()=>Text(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,"default"));
            d.AfterRegenerate=()=>{foreach(var p in d.Elements.OfType<Floor>().Where(x=>x!=f))Require(p.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString()=="","empty retained");};
        },r=>r.NewElementsCreated==2,false);
        Check("regeneration changing inherited value rolls back",(d,f)=>{
            f.Parameters.Add(Text(BuiltInParameter.ALL_MODEL_MARK,"F-01"));d.NewParameters.Add(()=>Text(BuiltInParameter.ALL_MODEL_MARK,""));
            d.AfterRegenerate=()=>{foreach(var p in d.Elements.OfType<Floor>().Where(x=>x!=f))p.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("lost");};
        },r=>r.Skipped==1,true);
        Check("readonly target rolls back",(d,f)=>{f.Parameters.Add(Text(BuiltInParameter.ALL_MODEL_MARK,"F-01"));d.NewParameters.Add(()=>{var p=Text(BuiltInParameter.ALL_MODEL_MARK,"");p.IsReadOnly=true;return p;});},r=>r.Skipped==1,true);
    }
    static void CheckWarnings()
    {
        var source=new Floor{Id=new ElementId(1)};source.Parameters.Add(Text(BuiltInParameter.ALL_MODEL_MARK,"F-01"));
        var piece=new Floor{Id=new ElementId(2)};piece.Parameters.Add(Text(BuiltInParameter.ALL_MODEL_MARK,"F-01"));
        var handler=new SplitEngine.RollbackOnFailure(source);handler.AllowInheritedMark(piece);
        Func<FailureSeverity,object,int,FailuresAccessor> failures=(severity,id,element)=>new FailuresAccessor{Messages=new List<FailureMessageAccessor>{new FailureMessageAccessor{Severity=severity,Definition=id,Ids=new List<ElementId>{source.Id,new ElementId(element)}}}};
        var expected=failures(FailureSeverity.Warning,BuiltInFailures.GeneralFailures.DuplicateValue,2);
        Require(handler.PreprocessFailures(expected)==FailureProcessingResult.Continue&&expected.Messages.Count==0,"expected inherited mark warning allowed");
        Require(handler.PreprocessFailures(failures(FailureSeverity.Warning,BuiltInFailures.GeneralFailures.DuplicateValue,3))==FailureProcessingResult.ProceedWithRollBack,"unrelated duplicate retained");
        Require(handler.PreprocessFailures(failures(FailureSeverity.Error,BuiltInFailures.GeneralFailures.DuplicateValue,2))==FailureProcessingResult.ProceedWithRollBack,"duplicate error retained");
        Require(handler.PreprocessFailures(failures(FailureSeverity.Warning,new object(),2))==FailureProcessingResult.ProceedWithRollBack,"other warning retained");
    }
    static void CheckWallPartialOverlap()
    {
        var d=new Document();
        var wall=new Wall{Document=d,Id=new ElementId(1),Location=new LocationCurve{Curve=Line.CreateBound(new XYZ(0,0,0),new XYZ(10,0,0))}};
        wall.Parameters.Add(new Parameter{Id=new ElementId((int)BuiltInParameter.WALL_HEIGHT_TYPE),StorageType=StorageType.ElementId,Value=ElementId.InvalidElementId});
        wall.Geometry.Add(new Solid(1,2));
        // A local beam notch leaves a single connected side face. It must not
        // trigger the old full-wall-height-band shortcut.
        wall.Faces.Add(new Reference{Face=new PlanarFace()});
        d.Elements.Add(wall);d.Elements.Add(new Level{Id=new ElementId(2)});d.Elements.Add(new WallType{Id=new ElementId(3)});
        var r=SplitEngine.RunSplitWall(d,new[]{wall},new[]{new Element{Id=new ElementId(4)}});
        if(r.Skipped!=1||r.OriginalDeleted!=0||d.Joins!=0||!d.Elements.Contains(wall))throw new Exception("Partial wall overlap did not preserve original");
        tests++;
    }
    static void CheckBatchPartialSuccess()
    {
        var d=new Document{FailCreate=2};
        var floors=new List<Floor>();
        for(int i=0;i<2;i++)
        {
            var floor=new Floor{Document=d,Id=new ElementId(10+i)};
            floor.Geometry.Add(new Solid(2*i+1,2*i+2));
            for(int j=1;j<=2;j++)floor.Faces.Add(new Reference{Face=new PlanarFace{Loops=new List<CurveLoop>{Rectangle(2*i+j)}}});
            d.Elements.Add(floor);floors.Add(floor);
        }
        d.Elements.Add(new Level{Id=new ElementId(2)});d.Elements.Add(new FloorType{Id=new ElementId(3)});
        var r=SplitEngine.RunSplitFloor(d,floors,new[]{new Element{Id=new ElementId(4)}});
        if(r.FailedOperations!=1||r.OriginalDeleted!=1||r.NewElementsCreated!=2||!d.Elements.Contains(floors[0])||d.Elements.Contains(floors[1])||d.Commits!=1||d.Joins!=1)
            throw new Exception("One failure contaminated another original's transaction");
        tests++;
    }
    static CurveLoop Rectangle(int cell,double x=0,double y=0,double size=1,bool reverse=false)
    {
        var points=new[]{new XYZ(x,y,0),new XYZ(x+size,y,0),new XYZ(x+size,y+size,0),new XYZ(x,y+size,0)};
        if(reverse)Array.Reverse(points);
        var loop=new CurveLoop{Cell=cell};
        for(int i=0;i<4;i++)loop.Add(Line.CreateBound(points[i],points[(i+1)%4]));
        return loop;
    }
    static void Check(string name,Action<Document,Floor> setup,Func<SplitResult,bool> verify,bool retained)
    {
        var d=new Document();
        var f=new Floor{Document=d,Id=new ElementId(1)};
        f.Geometry.Add(new Solid(1,2));
        for(int i=1;i<=2;i++)f.Faces.Add(new Reference{Face=new PlanarFace{Loops=new List<CurveLoop>{Rectangle(i)}}});
        d.Elements.Add(f);d.Elements.Add(new Level{Id=new ElementId(2)});d.Elements.Add(new FloorType{Id=new ElementId(3)});
        setup(d,f);
        var r=SplitEngine.RunSplitFloor(d,new[]{f},new[]{new Element{Id=new ElementId(4)}});
        if(!verify(r)||d.Elements.Contains(f)!=retained||(retained&&(d.Elements.OfType<Floor>().Count()!=1||d.Joins!=0||d.Commits!=0)))
            throw new Exception(name+" failed: "+string.Join("; ",r.FailureSamples));
        tests++;
    }
}
