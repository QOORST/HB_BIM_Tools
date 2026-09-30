using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HB.CadRevision
{
    internal sealed class DrawingData
    {
        internal readonly List<Segment> Segments=new List<Segment>();
        internal readonly List<RoundGeometry> Curves=new List<RoundGeometry>();
        internal readonly Dictionary<string,int> Skipped=new Dictionary<string,int>();
        internal string Units;
        internal bool IncludeXrefs=true;
        internal HashSet<string> ExcludedXrefs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal string SourcePath;
        internal int GeometryLimit=200000;
        internal bool ProjectPlan;
        internal int ProjectedLines,ProjectedCurves;
        internal bool IncludeSharedNested;
        internal Dictionary<string,string> SharedXrefs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Notes=new List<string>();
        internal void Skip(string reason) { if(!Skipped.ContainsKey(reason)) Skipped[reason]=0; Skipped[reason]++; }
    }
    internal static class DwgReader
    {
        internal static string[] ListXrefs(string path)
        {
            ValidateHeader(path);
            using(var db=new Database(false,true))
            {
                db.ReadDwgFile(path,FileOpenMode.OpenForReadAndReadShare,false,null);db.CloseInput(true);
                using(var tx=db.TransactionManager.StartOpenCloseTransaction())
                {
                    var names=new List<string>();
                    foreach(ObjectId id in (BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))
                    {
                        var block=(BlockTableRecord)tx.GetObject(id,OpenMode.ForRead);
                        if(block.IsFromExternalReference&&!block.IsDependent)names.Add(block.Name);
                    }
                    return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToArray();
                }
            }
        }
        internal static DrawingData Read(string path,HashSet<string> layers,UnitsValue? unitOverride=null,bool includeXrefs=true,HashSet<string> excludedXrefs=null,Dictionary<string,string> sharedXrefs=null,bool includeSharedNested=false,int geometryLimit=200000,bool projectPlan=false)
        {
            if(geometryLimit!=200000&&geometryLimit!=500000)throw new ArgumentOutOfRangeException(nameof(geometryLimit));
            ValidateHeader(path);
            var data=new DrawingData { IncludeXrefs=includeXrefs,SourcePath=path,IncludeSharedNested=includeSharedNested,GeometryLimit=geometryLimit,ProjectPlan=projectPlan };
            data.Notes.Add(projectPlan?"比較模式：平行 XY 的非零高程幾何投影到平面；不比較高程差異，傾斜幾何仍略過。":"比較模式：僅 XY 零高程幾何。");
            data.Notes.Add("每版幾何容量上限："+geometryLimit.ToString("N0")+"；超限停止，不截斷。");
            if(sharedXrefs!=null)data.SharedXrefs=new Dictionary<string,string>(sharedXrefs,StringComparer.OrdinalIgnoreCase);
            if(excludedXrefs!=null)data.ExcludedXrefs=new HashSet<string>(excludedXrefs,StringComparer.OrdinalIgnoreCase);
            if(includeXrefs&&data.ExcludedXrefs.Count>0)data.Notes.Add("依名稱排除外部參考（含其巢狀內容）："+string.Join("、",data.ExcludedXrefs.OrderBy(x=>x)));
            data.Notes.Add(includeXrefs?"比對範圍：主圖及可解析的外部參考":"比對範圍：僅主圖（含一般圖塊），外部參考未納入；不代表完整圖面比對。");
            using(var db=new Database(false,true))
            {
                try { db.ReadDwgFile(path,FileOpenMode.OpenForReadAndReadShare,false,null); }
                catch(Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    throw new InvalidOperationException("無法讀取 DWG："+path+"\nAutoCAD 錯誤："+ex.ErrorStatus,ex);
                }
                db.CloseInput(true);
                var units=unitOverride??db.Insunits;
                if(units==UnitsValue.Undefined) throw new InvalidOperationException("DWG 未指定 INSUNITS，請在比對介面選擇此圖的繪圖單位（不必修改來源）："+path);
                double scale=UnitsConverter.GetConversionFactor(units,UnitsValue.Millimeters);
                if(double.IsNaN(scale)||double.IsInfinity(scale)||scale<=0) throw new InvalidOperationException("無法換算圖面單位："+path);
                data.Units=(unitOverride.HasValue?"手動指定":"依 INSUNITS")+"："+units+" → mm（×"+scale+"）；原始 INSUNITS="+db.Insunits;
                try { if(includeXrefs) db.ResolveXrefs(false,false); }
                catch(Autodesk.AutoCAD.Runtime.Exception ex) { data.Notes.Add("外部參考解析未完成："+ex.ErrorStatus); }
                using(var tx=db.TransactionManager.StartOpenCloseTransaction())
                {
                    var table=(BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead);
                    var model=(BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace],OpenMode.ForRead);
                    Walk(model,tx,Matrix3d.Identity,scale,"","0",layers,data,new HashSet<ObjectId>(),0);

                }
            }
            data.Notes.Add("非零高程平面投影：線段="+data.ProjectedLines+"；圓與圓弧="+data.ProjectedCurves);
            return data;
        }
        static void Walk(BlockTableRecord block,Transaction tx,Matrix3d transform,double scale,string prefix,string inheritedLayer,HashSet<string> layers,DrawingData data,HashSet<ObjectId> stack,int depth,string layerPrefix="")
        {
            if(depth>32||!stack.Add(block.ObjectId)) { data.Skip("循環參考或超過 32 層");return; }
            try
            {
                foreach(ObjectId id in block)
                {
                    string context="開啟物件 "+id.Handle;
                    try
                    {
                    var entity=tx.GetObject(id,OpenMode.ForRead) as Entity;
                    if(entity==null) continue;
                    var layer=entity.Layer=="0"?inheritedLayer:layerPrefix+entity.Layer;
                    var handle=prefix+entity.Handle;
                    context="來源="+handle+"；類型="+entity.GetType().Name+"；圖層="+layer;
                    if(entity is BlockReference br)
                    {
                        if(br is MInsertBlock) { data.Skip("陣列圖塊 MINSERT");continue; }
                        // A clipped reference cannot be represented by its entire definition.
                        if(!br.ExtensionDictionary.IsNull)
                        {
                            var dict=(DBDictionary)tx.GetObject(br.ExtensionDictionary,OpenMode.ForRead);
                            if(dict.Contains("ACAD_FILTER")) { data.Skip("具有裁切／篩選的圖塊");continue; }
                        }
                        var child=(BlockTableRecord)tx.GetObject(br.BlockTableRecord,OpenMode.ForRead);
                        if(child.IsFromExternalReference)
                        {
                            if(layerPrefix.Length>0&&!data.IncludeSharedNested) { data.Skip("共用底圖未納入巢狀參考："+child.Name);continue; }
                            if(!data.IncludeXrefs) { data.Skip("依設定略過外部參考："+child.Name);continue; }
                            if(data.ExcludedXrefs.Contains(child.Name)||data.ExcludedXrefs.Contains(child.Name.Split('|').Last())) { data.Skip("依選擇略過外部參考："+child.Name);continue; }
                            string sharedPath;
                            if(layerPrefix.Length==0&&!child.IsDependent&&data.SharedXrefs.TryGetValue(child.Name,out sharedPath))
                            {
                                ReadShared(sharedPath,child.Name,transform*br.BlockTransform,scale,handle+"/",layer,layers,data,depth+1);
                                continue;
                            }
                            data.Notes.Add("外部參考："+child.Name+" | "+child.PathName+" | "+child.XrefStatus+" | 插入點="+br.Position+" | 比例="+br.ScaleFactors+" | 旋轉="+br.Rotation);
                            if(child.XrefStatus!=XrefStatus.Resolved) { data.Skip("未解析／卸載外部參考");continue; }
                        }
                        Walk(child,tx,transform*br.BlockTransform,scale,handle+"/",layer,layers,data,stack,depth+1,layerPrefix);
                        continue;
                    }
                    if(layers.Count>0&&!layers.Contains(layer)) { data.Skip("篩除圖層物件");continue; }
                    if(entity is Line line) Add(data,line.StartPoint.TransformBy(transform),line.EndPoint.TransformBy(transform),scale,handle,layer);
                    else if(entity is Circle circle)
                        AddRound(data,circle.Center,circle.StartPoint,circle.StartPoint,circle.StartPoint,circle.Normal,true,transform,scale,handle,layer);
                    else if(entity is Arc arc)
                        AddRound(data,arc.Center,arc.StartPoint,arc.GetPointAtParameter((arc.StartParam+arc.EndParam)/2),arc.EndPoint,arc.Normal,false,transform,scale,handle,layer);
                    else if(entity is Polyline poly)
                    {
                        int count=poly.Closed?poly.NumberOfVertices:poly.NumberOfVertices-1;
                        for(int i=0;i<count;i++)
                        {
                            context="來源="+handle+":"+i+"；類型=Polyline；圖層="+layer;
                            var segmentType=poly.GetSegmentType(i);
                            if(segmentType==SegmentType.Coincident||segmentType==SegmentType.Empty||segmentType==SegmentType.Point)
                            { data.Skip("聚合線重合／空白頂點段");continue; }
                            if(segmentType==SegmentType.Arc)
                            {
                                using(var circular=poly.GetArcSegmentAt(i))
                                    AddRound(data,circular.Center,poly.GetPoint3dAt(i),poly.GetPointAtParameter(i+0.5),poly.GetPoint3dAt((i+1)%poly.NumberOfVertices),poly.Normal,false,transform,scale,handle+":"+i,layer);
                                continue;
                            }
                            if(segmentType!=SegmentType.Line) { data.Skip("不支援的聚合線段："+segmentType);continue; }
                            Add(data,poly.GetPoint3dAt(i).TransformBy(transform),poly.GetPoint3dAt((i+1)%poly.NumberOfVertices).TransformBy(transform),scale,handle+":"+i,layer);
                        }
                    }
                    else data.Skip(entity.GetType().Name);
                    }
                    catch(Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        throw new InvalidOperationException("幾何擷取失敗："+data.SourcePath+"\n"+context+"\nAutoCAD："+ex.ErrorStatus,ex);
                    }
                }
            }
            finally { stack.Remove(block.ObjectId); }
        }
        static void ReadShared(string path,string name,Matrix3d transform,double scale,string prefix,string inheritedLayer,HashSet<string> layers,DrawingData data,int depth)
        {
            if(depth>32)throw new InvalidOperationException("共用底圖超過 32 層，已停止。");
            ValidateHeader(path);
            try
            {
                using(var db=new Database(false,true))
                {
                    db.ReadDwgFile(path,FileOpenMode.OpenForReadAndReadShare,false,null);db.CloseInput(true);
                    data.Notes.Add(data.IncludeSharedNested?"共用底圖範圍：含可解析巢狀參考":"共用底圖範圍：僅自身模型及一般圖塊，排除巢狀外部參考");
                    data.Notes.Add("共用底圖（非歷史底圖比對）："+name+" | 獨立讀取="+Path.GetFullPath(path)+" | 底圖 INSUNITS="+db.Insunits+"；套用原插入轉換及主圖單位換算，不另行縮放、不寫回來源。");
                    try { if(data.IncludeSharedNested) db.ResolveXrefs(false,false); }
                    catch(Autodesk.AutoCAD.Runtime.Exception ex) { data.Notes.Add("共用底圖巢狀參考解析未完成："+ex.ErrorStatus); }
                    using(var tx=db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var table=(BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead);
                        var model=(BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace],OpenMode.ForRead);
                        Walk(model,tx,transform,scale,prefix,inheritedLayer,layers,data,new HashSet<ObjectId>(),depth,name+"|");
                    }
                }
            }
            catch(System.Exception ex) { throw new InvalidOperationException("共用底圖獨立讀取失敗："+path+"\n"+ex.Message,ex); }
        }
        static void ValidateHeader(string path)
        {
            if(Path.GetFileName(path).StartsWith("._",StringComparison.Ordinal))
                throw new InvalidOperationException("選到 macOS 中繼資料檔，並非 DWG 圖面："+path+"\n請選擇同資料夾內不以「._」開頭的圖檔。");
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {
                var bytes=new byte[6];
                int count=input.Read(bytes,0,bytes.Length);
                var header=Encoding.ASCII.GetString(bytes,0,count);
                if(count!=6||!header.StartsWith("AC10",StringComparison.Ordinal)||header[4]<'0'||header[4]>'9'||header[5]<'0'||header[5]>'9')
                    throw new InvalidOperationException("檔案缺少可辨識的 DWG 標頭，請確認是否誤選、損毀或僅更改副檔名："+path);
            }
        }
        static void CheckCapacity(DrawingData data,string layer,string handle)
        {
            if(data.Segments.Count+data.Curves.Count<data.GeometryLimit)return;
            var top=data.Segments.Select(x=>x.Layer).Concat(data.Curves.Select(x=>x.Layer))
                .GroupBy(x=>x).OrderByDescending(x=>x.Count()).Take(5).Select(x=>x.Key+"："+x.Count().ToString("N0"));
            throw new InvalidOperationException("圖檔："+data.SourcePath+"\n已達 "+data.GeometryLimit.ToString("N0")+" 筆幾何項目上限，已停止；沒有產生截斷的比對結果。\n目前圖層："+layer+"；來源："+handle+"\n參考設定："+string.Join("；",data.Notes)+"\n已擷取數量最多的圖層：\n"+string.Join("\n",top)+"\n可取消「納入外部參考底圖」先比對主圖，或指定圖層縮小範圍。");
        }
        static double Angle(double a){a%=2*Math.PI;return a<0?a+2*Math.PI:a;}
        static void AddRound(DrawingData data,Point3d center,Point3d start,Point3d middle,Point3d end,Vector3d normal,bool circle,Matrix3d transform,double scale,string id,string layer)
        {
            var axis=normal.GetPerpendicularVector().GetNormal();
            var u=axis.TransformBy(transform);var v=normal.GetNormal().CrossProduct(axis).TransformBy(transform);
            double size=Math.Max(u.Length,v.Length);
            if(size<=1e-12||Math.Abs(u.Length-v.Length)>size*1e-9||Math.Abs(u.DotProduct(v))>size*size*1e-9)
            {data.Skip("非等比例縮放／剪切的圓弧（成為橢圓）");return;}
            center=center.TransformBy(transform);start=start.TransformBy(transform);middle=middle.TransformBy(transform);end=end.TransformBy(transform);
            double radius=center.DistanceTo(start)*scale;
            if((!data.ProjectPlan&&Math.Abs(center.Z*scale)>0.01)||Math.Abs(u.Z)>size*1e-9||Math.Abs(v.Z)>size*1e-9)
            {data.Skip("非 XY 零高程圓弧");return;}
            if(radius<=1e-8){data.Skip("零半徑圓弧");return;}
            double a=Angle(Math.Atan2(start.Y-center.Y,start.X-center.X));
            double b=Angle(Math.Atan2(end.Y-center.Y,end.X-center.X));
            double m=Angle(Math.Atan2(middle.Y-center.Y,middle.X-center.X));
            double sweep=Angle(b-a);
            if(!circle&&Angle(m-a)>sweep) {a=b;sweep=2*Math.PI-sweep;}
            if(!circle&&(sweep<=1e-12||sweep>=2*Math.PI-1e-12)){data.Skip("退化圓弧");return;}
            CheckCapacity(data,layer,id);
            if(data.ProjectPlan&&Math.Abs(center.Z*scale)>0.01)data.ProjectedCurves++;
            data.Curves.Add(new RoundGeometry {Id=id,Layer=layer,IsCircle=circle,X=center.X*scale,Y=center.Y*scale,Radius=radius,Start=circle?0:a,Sweep=circle?2*Math.PI:sweep});
        }
        static void Add(DrawingData data,Point3d a,Point3d b,double scale,string id,string layer)
        {
            if(!PlanProjection.AcceptLine(a.Z*scale,b.Z*scale,data.ProjectPlan)) { data.Skip(data.ProjectPlan?"傾斜或無效高程線段":"非 XY 零高程線段");return; }
            if(Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y))*scale<=1e-8) { data.Skip("零長度線段");return; }
            CheckCapacity(data,layer,id);
            if(data.ProjectPlan&&(Math.Abs(a.Z*scale)>0.01||Math.Abs(b.Z*scale)>0.01))data.ProjectedLines++;
            data.Segments.Add(new Segment { Id=id,Layer=layer,X1=a.X*scale,Y1=a.Y*scale,X2=b.X*scale,Y2=b.Y*scale });
        }
    }
}
