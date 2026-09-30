using System;
using System.Linq;
using HB.CadRevision;
class Program
{
    static int count;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);count++; }
    static Segment S(double y,string layer="A",bool reverse=false)=>new Segment { Id="x",Layer=layer,X1=reverse?100:0,X2=reverse?0:100,Y1=y,Y2=y };
    static RoundGeometry C(double x=0,double radius=100,bool circle=true,double start=0,double sweep=Math.PI)=>new RoundGeometry {Id="c",Layer="A",X=x,Y=0,Radius=radius,IsCircle=circle,Start=start,Sweep=sweep};
    static void Main()
    {
        Check(PlanProjection.AcceptLine(0,0,false),"zero plane");
        Check(!PlanProjection.AcceptLine(3000,3000,false),"strict excludes elevation");
        Check(PlanProjection.AcceptLine(3000,3000,true),"horizontal elevated line");
        Check(!PlanProjection.AcceptLine(0,3000,true),"sloped line excluded");
        Check(!PlanProjection.AcceptLine(double.NaN,0,true),"invalid elevation excluded");
        Check(CompareCore.Compare(new[]{S(0)},new[]{S(0,reverse:true)},1,50).Single().Kind=="未變更","reverse endpoints unchanged");
        Check(CompareCore.Compare(new[]{S(0)},new[]{S(.5)},1,50).Single().Kind=="未變更","noise tolerance");
        var move=CompareCore.Compare(new[]{S(0)},new[]{S(20)},1,50).Single();Check(move.Kind=="疑似位移"&&move.DistanceMm==20,"translation candidate");
        var ambiguous=CompareCore.Compare(new[]{S(0)},new[]{S(20),S(30)},1,50);Check(ambiguous.Count==3&&ambiguous.All(c=>c.Kind.Contains("待確認")),"ambiguous matches not forced");
        Check(CompareCore.Compare(new[]{S(0),S(0)},new[]{S(0)},1,50).Count==2,"duplicate counts preserved");
        Check(CompareCore.Compare(new[]{S(0)},new[]{S(0,"B")},1,50).Count==2,"layer change retained");
        Check(CompareCore.Compare(new[]{S(0)},Array.Empty<Segment>(),1,50).Single().Kind=="刪除","empty new");
        var invalid=false;try{CompareCore.Compare(new[]{S(0)},new[]{S(0)},double.NaN,50);}catch(ArgumentException){invalid=true;}Check(invalid,"invalid tolerance rejected");
        var changed=S(0);changed.X2=110;Check(CompareCore.Compare(new[]{S(0)},new[]{changed},1,50).Count==2,"length changes not translations");
        var boundary=CompareCore.Compare(new[]{S(-.1)},new[]{S(.1)},.3,50);Check(boundary.Single().Kind=="未變更","index crosses negative cell boundary");
        Check(CompareCore.LayerGroup("plan|DIM")=="標註候選圖層","xref layer grouping");
        Check(CompareCore.LayerGroup("WALL").StartsWith("其他"),"unknown layers not declared architecture");
        var many=Enumerable.Range(0,6000).Select(i=>S(i*1000)).ToArray();
        Check(CompareCore.Compare(many,many,1,50).Count==6000,"sparse large input indexed");
        var dense=Enumerable.Range(0,8000).Select(i=>S(0)).ToArray();
        Check(CompareCore.Compare(dense,dense,1,500).All(c=>c.Kind=="未變更"),"8000 coincident duplicates preserve multiplicity without quadratic scan");
        var vectors=Enumerable.Range(0,8000).Select(i=>new Segment { Layer="A",Id=i.ToString(),X1=-100-i*10,X2=100+i*10,Y1=0,Y2=0 }).ToArray();
        var shifted=vectors.Select(s=>new Segment { Layer=s.Layer,Id=s.Id,X1=s.X1,X2=s.X2,Y1=20,Y2=20 }).ToArray();
        Check(CompareCore.Compare(vectors,shifted,1,500).All(c=>c.Kind=="疑似位移"),"8000 co-centered different vectors avoid quadratic movement scan");
        var opposite=S(20,reverse:true);
        Check(CompareCore.Compare(new[]{S(0)},new[]{opposite},1,50).Single().Kind=="疑似位移","reversed move vector");
        var shortA=new Segment {Layer="A",X1=0,X2=.1,Y1=0,Y2=0};
        var shortB=new Segment {Layer="A",X1=0,X2=.1,Y1=20,Y2=20};
        Check(CompareCore.Compare(new[]{shortA},new[]{shortB},1,50).Single().Kind=="疑似位移","overlapping signed vector bins do not duplicate candidates");
        Check(CurveCore.Compare(new[]{C()},new[]{C()},1,50).Single().Kind=="未變更","same circle");
        Check(CurveCore.Compare(new[]{C()},new[]{C(20)},1,50).Single().Kind=="疑似位移","circle translation");
        Check(CurveCore.Compare(new[]{C()},new[]{C(radius:110)},1,50).Count==2,"radius change not translation");
        Check(CurveCore.Compare(new[]{C()},new[]{C(circle:false)},1,50).Count==2,"circle not matched to arc");
        Check(CurveCore.Compare(new[]{C(circle:false,start:Math.PI*1.5)},new[]{C(circle:false,start:-Math.PI*.5)},1,50).Single().Kind=="未變更","arc angle wrap");
        Check(CurveCore.Compare(new[]{C(circle:false,sweep:Math.PI/2)},new[]{C(circle:false,start:Math.PI/2,sweep:Math.PI*1.5)},1,50).Count==2,"major and minor arc distinct");
        Check(CurveCore.Compare(new[]{C(circle:false)},new[]{C(circle:false,start:Math.PI)},1,50).Count==2,"opposite semicircles distinct");
        Check(CurveCore.Compare(new[]{C()},new[]{C(20),C(30)},1,50).All(c=>c.Kind.Contains("待確認")),"ambiguous curve moves");
        Check(CurveCore.Compare(new[]{C(),C()},new[]{C()},1,50).Count==2,"curve duplicate multiplicity");
        Check(CurveCore.Compare(Array.Empty<RoundGeometry>(),new[]{C()},1,50).Single().Kind=="新增","empty old curves");
        var bad=false;try{CurveCore.Compare(new[]{C(radius:double.NaN)},new[]{C()},1,50);}catch(ArgumentException){bad=true;}Check(bad,"invalid curve rejected");
        Console.WriteLine("PASS "+count+" core scenarios; DWG extraction not tested.");
    }
}
