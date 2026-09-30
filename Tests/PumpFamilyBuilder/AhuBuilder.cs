using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace HB.PumpFamilyBuilder
{
    public class AhuType {
        public string model {get;set;}
        public double C {get;set;} public double D {get;set;} public double E {get;set;}
        public double F {get;set;} public double G {get;set;} public double H {get;set;}
        public double A {get;set;} public double B {get;set;} public double @base {get;set;}
    }
    public class AhuRecipe { public string template_id {get;set;} public List<AhuType> types {get;set;} }
    public class AhuBuild {
        Document doc; FamilyManager fm; ViewPlan plan; string output;
        Extrusion body, plinth;
        readonly Dictionary<string,FamilyParameter> p=new Dictionary<string,FamilyParameter>();
        readonly List<string> warnings=new List<string>();
        readonly List<object> checks=new List<object>();
        static double Ft(double mm)=>UnitUtils.ConvertToInternalUnits(mm,UnitTypeId.Millimeters);
        static double Mm(double ft)=>UnitUtils.ConvertFromInternalUnits(ft,UnitTypeId.Millimeters);
        void Tx(string name,Action action) {
            File.AppendAllText(Path.Combine(output,"progress.log"),name+Environment.NewLine);
            using(var t=new Transaction(doc,name)) {
                t.Start();var guard=new FailureGuard();
                t.SetFailureHandlingOptions(t.GetFailureHandlingOptions().SetFailuresPreprocessor(guard).SetClearAfterRollback(true));
                action();if(t.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException(name+": "+string.Join(";",guard.Messages));
                warnings.AddRange(guard.Messages);
            }
        }
        void Length(string name,double value,string formula=null) {
            var parameter=fm.AddParameter(name,GroupTypeId.Geometry,SpecTypeId.Length,false);p.Add(name,parameter);fm.Set(parameter,Ft(value));
            if(formula!=null)fm.SetFormula(parameter,formula);
        }
        void Text(string name,string value) {
            var parameter=fm.AddParameter(name,GroupTypeId.IdentityData,SpecTypeId.String.Text,false);p.Add(name,parameter);fm.Set(parameter,value);
        }
        void Associate(Element e,BuiltInParameter id,string name) {
            var parameter=e.get_Parameter(id);
            if(parameter==null || !fm.CanElementParameterBeAssociated(parameter))throw new InvalidOperationException("Cannot associate "+id);
            fm.AssociateElementParameterToFamilyParameter(parameter,p[name]);
        }
        ReferencePlane Plane(string name,double value,bool x) {
            var rp=doc.FamilyCreate.NewReferencePlane(x?new XYZ(Ft(value),-10,0):new XYZ(-10,Ft(value),0),
                x?new XYZ(Ft(value),10,0):new XYZ(10,Ft(value),0),XYZ.BasisZ,plan);
            rp.Name=name;return rp;
        }
        void Dimension(ReferencePlane a,ReferencePlane b,string name,bool x) {
            var refs=new ReferenceArray();refs.Append(a.GetReference());refs.Append(b.GetReference());
            var line=x?Line.CreateBound(new XYZ(0,-2,0),new XYZ(10,-2,0)):Line.CreateBound(new XYZ(-2,0,0),new XYZ(-2,10,0));
            doc.FamilyCreate.NewLinearDimension(plan,line,refs).FamilyLabel=p[name];
        }
        Extrusion Box(double l,double w,double h,string start,string end,ReferencePlane[] planes,ElementId material) {
            var pts=new[]{XYZ.Zero,new XYZ(Ft(l),0,0),new XYZ(Ft(l),Ft(w),0),new XYZ(0,Ft(w),0)};
            var loop=new CurveArray();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(pts[i],pts[(i+1)%4]));
            var loops=new CurveArrArray();loops.Append(loop);
            var sketch=SketchPlane.Create(doc,Autodesk.Revit.DB.Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero));
            var ex=doc.FamilyCreate.NewExtrusion(true,loops,sketch,Ft(h));
            if(start!=null)Associate(ex,BuiltInParameter.EXTRUSION_START_PARAM,start);
            Associate(ex,BuiltInParameter.EXTRUSION_END_PARAM,end);
            ex.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(material);doc.Regenerate();
            var curves=ex.Sketch.Profile.get_Item(0);
            // Revit can reorder the sketch curves. Match their geometry, not their indices.
            for(int i=0;i<curves.Size;i++) {
                var curve=curves.get_Item(i);var a=curve.GetEndPoint(0);var b=curve.GetEndPoint(1);
                int index;
                if(Math.Abs(a.X-b.X)<1e-6)index=Math.Abs(a.X)<1e-6?0:1;
                else if(Math.Abs(a.Y-b.Y)<1e-6)index=Math.Abs(a.Y)<1e-6?2:3;
                else throw new InvalidDataException("Unexpected non-rectangular sketch");
                doc.FamilyCreate.NewAlignment(plan,planes[index].GetReference(),curve.Reference);
            }
            return ex;
        }
        PlanarFace Face(XYZ normal) => body.get_Geometry(new Options{ComputeReferences=true}).OfType<Solid>()
            .SelectMany(s=>s.Faces.Cast<Face>()).OfType<PlanarFace>().First(f=>f.FaceNormal.DotProduct(normal)>0.99);
        void DuctPort(XYZ normal,DuctSystemType system,string width,string height,FlowDirectionType direction,string description) {
            var connector=ConnectorElement.CreateDuctConnector(doc,system,ConnectorProfileType.Rectangular,Face(normal).Reference);
            Associate(connector,BuiltInParameter.CONNECTOR_WIDTH,width);Associate(connector,BuiltInParameter.CONNECTOR_HEIGHT,height);
            connector.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_DIRECTION_PARAM).Set((int)direction);
            connector.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION).Set(description);
        }
        void SetType(AhuType t) {
            foreach(var item in new Dictionary<string,double>{{"箱體總長",t.C},{"箱體寬度",t.D},{"箱體高度",t.E},{"底座高度",t.@base},{"送風口寬",t.G},{"送風口高",t.H},{"來源尺寸_A",t.A},{"來源尺寸_B",t.B}})fm.Set(p[item.Key],Ft(item.Value));
            fm.Set(p["原廠型號"],t.model);
        }
        void Check(string name,double l,double w,double height) {
            var bb=body.get_BoundingBox(null);
            double actualL=Mm(bb.Max.X-bb.Min.X),actualW=Mm(bb.Max.Y-bb.Min.Y),actualH=Mm(bb.Max.Z-bb.Min.Z);
            if(Math.Abs(actualL-l)>0.1 || Math.Abs(actualW-w)>0.1 || Math.Abs(actualH-height)>0.1)throw new InvalidDataException("Geometry flex failed "+name+" "+actualL+","+actualW+","+actualH);
            if(Math.Abs(Mm(bb.Min.X))>0.1 || Math.Abs(Mm(bb.Min.Y))>0.1)throw new InvalidDataException("Origin moved during flex");
            var baseBB=plinth.get_BoundingBox(null);
            if(Math.Abs(Mm(baseBB.Max.X-baseBB.Min.X)-l)>0.1 || Math.Abs(Mm(baseBB.Max.Y-baseBB.Min.Y)-w)>0.1)throw new InvalidDataException("Base flex failed");
            checks.Add(new{test=name,length_mm=actualL,width_mm=actualW,body_height_mm=actualH});
        }
        public static void Run(Autodesk.Revit.ApplicationServices.Application app,string output,string recipePath) {
            if(app.VersionNumber!="2024")throw new InvalidOperationException("Only Revit 2024 is validated");
            var recipe=new JavaScriptSerializer().Deserialize<AhuRecipe>(File.ReadAllText(recipePath));
            if(recipe.template_id!="teco_ahu_hs" || recipe.types==null || recipe.types.Count!=2 || recipe.types.Select(t=>t.model).Distinct().Count()!=2)throw new InvalidDataException("Invalid AHU recipe");
            foreach(var t in recipe.types) {
                double[] expected=t.model=="PJ0043-HS"?new double[]{1400,950,650,725,289,334}:t.model=="PJ0063-HS"?new double[]{1700,1250,750,825,344,398}:null;
                if(expected==null || !new[]{t.C,t.D,t.E,t.F,t.G,t.H}.SequenceEqual(expected) || t.@base!=75 || t.A!=950 || t.B!=650)throw new InvalidDataException("Unsupported catalog configuration");
            }
            Directory.CreateDirectory(output);if(Directory.EnumerateFiles(output,"*.rfa").Any())throw new IOException("Output already exists");
            var b=new AhuBuild{output=output};
            try {
                b.doc=app.NewFamilyDocument(@"C:\ProgramData\Autodesk\RVT 2024\Family Templates\English\Metric Mechanical Equipment.rft");
                b.fm=b.doc.FamilyManager;b.plan=new FilteredElementCollector(b.doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v=>!v.IsTemplate && v.ViewType==ViewType.FloorPlan);
                b.Tx("建立空調箱参数及外殼",()=> {
                    b.fm.NewType(recipe.types[0].model);var t=recipe.types[0];
                    b.Length("箱體總長",t.C);b.Length("箱體寬度",t.D);b.Length("箱體高度",t.E);b.Length("底座高度",75);
                    b.Length("設備總高",t.F,"箱體高度 + 底座高度");
                    b.Length("送風口寬",t.G);b.Length("送風口高",t.H);
                    b.Length("回風口寬_概念",t.D,"箱體寬度");b.Length("回風口高_概念",t.E,"箱體高度");
                    b.Length("來源尺寸_A",t.A);b.Length("來源尺寸_B",t.B);
                    b.Text("原廠型號",t.model);b.Text("廠牌","TECO 東元");
                    b.Text("資料來源","11 AHU組合式空調箱系列.pdf 第10頁 臥式單風車");
                    b.Text("模型範圍","概念外殼；不含混合箱、內部構造、維修空間、水管及電氣接頭");
                    b.Text("接頭假設","頂部中心送風；正X端面中心回風，回風尺寸暫採D×E。位置及淨尺寸需原廠確認。");
                    b.Text("尺寸注意","總長採表列C；A+B與C不一致。A、B不驅動幾何；任意調整後不代表原廠規格。");
                    foreach(string name in new[]{"風量_待選定","機外靜壓_待選定","冷房能力_待選定","電壓相數_待補","馬達功率_待選定","冰水接頭_待補"})b.Text(name,"未選定／未提供");
                    var planes=new[]{b.Plane("長度起點",0,true),b.Plane("長度終點",t.C,true),b.Plane("寬度起點",0,false),b.Plane("寬度終點",t.D,false)};
                    planes[0].Pinned=true;planes[2].Pinned=true;
                    b.doc.Regenerate();
                    b.Dimension(planes[0],planes[1],"箱體總長",true);b.Dimension(planes[2],planes[3],"箱體寬度",false);
                    var shell=Material.Create(b.doc,"空調箱_概念箱板");((Material)b.doc.GetElement(shell)).Color=new Color(175,191,196);
                    var steel=Material.Create(b.doc,"空調箱_概念底座");((Material)b.doc.GetElement(steel)).Color=new Color(65,72,78);
                    b.plinth=b.Box(t.C,t.D,75,null,"底座高度",planes,steel);
                    b.body=b.Box(t.C,t.D,t.F,"底座高度","設備總高",planes,shell);b.doc.Regenerate();
                    b.DuctPort(XYZ.BasisZ,DuctSystemType.SupplyAir,"送風口寬","送風口高",FlowDirectionType.Out,"送風；頂面中心位置為概念假設");
                    b.DuctPort(XYZ.BasisX,DuctSystemType.ReturnAir,"回風口寬_概念","回風口高_概念",FlowDirectionType.In,"回風；全端面尺寸為概念假設，淨開口待確認");
                    b.fm.NewType(recipe.types[1].model);b.SetType(recipe.types[1]);
                });
                foreach(var t in recipe.types) {
                    b.Tx("核對型號 "+t.model,()=>{b.fm.CurrentType=b.fm.Types.Cast<FamilyType>().Single(x=>x.Name==t.model);b.doc.Regenerate();});
                    b.Check(t.model,t.C,t.D,t.E);
                }
                b.Tx("測試獨立尺寸變更",()=>{b.fm.Set(b.p["箱體總長"],Ft(1900));b.fm.Set(b.p["箱體寬度"],Ft(1400));b.fm.Set(b.p["箱體高度"],Ft(900));b.fm.Set(b.p["底座高度"],Ft(100));b.fm.Set(b.p["送風口寬"],Ft(400));b.fm.Set(b.p["送風口高"],Ft(450));b.doc.Regenerate();});
                b.Check("independent_flex",1900,1400,900);
                b.Tx("還原型錄值",()=>{b.SetType(recipe.types[1]);b.doc.Regenerate();});
                b.Preview();
                string path=Path.Combine(output,"TECO_PJ0043_0063_HS_CONCEPT_2024.rfa");
                b.doc.SaveAs(path,new SaveAsOptions{OverwriteExistingFile=false,MaximumBackups=1});b.doc.Close(false);b.doc=null;
                var reopened=app.OpenDocumentFile(path);try {if(!reopened.IsFamilyDocument || reopened.FamilyManager.Types.Size!=2)throw new InvalidDataException("Reopen failed");}finally{reopened.Close(false);}
                b.ProjectTest(app,path);
                File.WriteAllText(Path.Combine(output,"geometry-checks.json"),new JavaScriptSerializer().Serialize(b.checks));
                File.WriteAllText(Path.Combine(output,"result.json"),new JavaScriptSerializer().Serialize(new{status="family_saved_reopened",path,revit="2024",project_duct_connection="passed_two_types",warnings=b.warnings,water_electrical_connectors="not_created_pending_data",revit_2025_2026="not_tested",limits="Concept enclosure only; supply centered on top and return uses full end face; no internal sections; performance not selected."}));
            } finally {if(b.doc!=null && b.doc.IsValidObject)b.doc.Close(false);}
        }
        void Preview() {
            View3D view=null;
            Tx("建立預覽",()=>{
                var vt=new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v=>v.ViewFamily==ViewFamily.ThreeDimensional);
                view=View3D.CreateIsometric(doc,vt.Id);view.Name="空調箱概念外殼";view.DisplayStyle=DisplayStyle.Shading;
                var forward=new XYZ(-1,1,-0.6).Normalize();var right=forward.CrossProduct(XYZ.BasisZ).Normalize();
                view.SetOrientation(new ViewOrientation3D(new XYZ(10,-10,8),right.CrossProduct(forward),forward));
                var ids=new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Select(e=>e.Id).ToList();view.HideElements(ids);
            });
            var options=new ImageExportOptions{ExportRange=ExportRange.SetOfViews,FilePath=Path.Combine(output,"preview"),HLRandWFViewsFileType=ImageFileType.PNG,ShadowViewsFileType=ImageFileType.PNG,PixelSize=1200,ZoomType=ZoomFitType.FitToPage};
            options.SetViewsAndSheets(new List<ElementId>{view.Id});doc.ExportImage(options);
        }
        void ProjectTest(Autodesk.Revit.ApplicationServices.Application app,string familyPath) {
            var project=app.NewProjectDocument(UnitSystem.Metric);
            try {
                var rows=new List<object>();
                using(var tx=new Transaction(project,"載入空調箱並實際接風管")) {
                    tx.Start();Family family;if(!project.LoadFamily(familyPath,out family))throw new InvalidOperationException("Load failed");
                    var level=new FilteredElementCollector(project).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault() ?? Level.Create(project,0);
                    var ductType=new FilteredElementCollector(project).OfClass(typeof(DuctType)).Cast<DuctType>().First();int index=0;
                    foreach(var id in family.GetFamilySymbolIds()) {
                        var symbol=(FamilySymbol)project.GetElement(id);symbol.Activate();project.Regenerate();
                        var instance=project.Create.NewFamilyInstance(new XYZ(0,Ft(4000*index++),0),symbol,level,Autodesk.Revit.DB.Structure.StructuralType.NonStructural);project.Regenerate();
                        var connectors=instance.MEPModel.ConnectorManager.Connectors.Cast<Connector>().ToList();if(connectors.Count!=2)throw new InvalidDataException("Expected 2 connectors");
                        foreach(var c in connectors) {
                            var duct=Duct.Create(project,ductType.Id,level.Id,c,c.Origin+c.CoordinateSystem.BasisZ*Ft(1500));project.Regenerate();
                            bool connected=duct.ConnectorManager.Connectors.Cast<Connector>().Any(dc=>dc.IsConnectedTo(c));if(!connected)throw new InvalidOperationException("Duct not connected");
                            rows.Add(new{type=symbol.Name,system=c.DuctSystemType.ToString(),direction=c.Direction.ToString(),width_mm=Mm(c.Width),height_mm=Mm(c.Height),connected});
                        }
                    }
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Duct QA transaction failed");
                }
                project.SaveAs(Path.Combine(output,"Duct-Connection-QA-2024.rvt"),new SaveAsOptions{OverwriteExistingFile=false});
                File.WriteAllText(Path.Combine(output,"connection-checks.json"),new JavaScriptSerializer().Serialize(rows));
            }finally{project.Close(false);}
        }
    }
}
