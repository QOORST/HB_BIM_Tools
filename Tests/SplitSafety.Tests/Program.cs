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
        Check("holes never become solids", (d,f)=>f.Faces[0].Face.Loops.Add(Rectangle(3)), r=>r.Skipped==1, true);
        Check("one face is not a split", (d,f)=>f.Faces.RemoveAt(1), r=>r.Skipped==1, true);
        Check("sloped floor skipped", (d,f)=>f.Faces[0].Face.FaceNormal=XYZ.BasisX, r=>r.Skipped==1, true);
        Check("overlapping regions rejected", (d,f)=>f.Faces[1].Face.Loops[0].Cell=1, r=>r.Skipped==1, true);
        Check("missing volume rejected", (d,f)=>f.Geometry[0]=new Solid(1,2,3), r=>r.Skipped==1, true);
        Check("hosted dependency rejected before joins", (d,f)=>f.Dependents.Add(new ElementId(888)), r=>r.Skipped==1, true);
        Check("untransferable parameter rejected", (d,f)=>f.Parameters.Add(new Parameter{StorageType=StorageType.String,Value="Keep me"}), r=>r.Skipped==1, true);
        Check("pinned original preserved", (d,f)=>f.Pinned=true, r=>r.Skipped==1, true);
        Check("custom schema preserved", (d,f)=>f.Schemas.Add(Guid.NewGuid()), r=>r.Skipped==1, true);
        CheckWallPartialOverlap();
        CheckBatchPartialSuccess();
        Console.WriteLine($"PASS {tests} source-linked split safety tests (stub control flow only).");
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
    static CurveLoop Rectangle(int cell)
    {
        var points=new[]{new XYZ(0,0,0),new XYZ(1,0,0),new XYZ(1,1,0),new XYZ(0,1,0)};
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
