using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace HB.SleeveQuantity.Native
{
    public sealed partial class Startup
    {
        private void RunAxisChecks(UIApplication ui)
        {
            string path = Path.Combine(_output,"QA-Axis-Sleeve.rfa");
            using(var family=ui.Application.NewFamilyDocument(@"C:\ProgramData\Autodesk\RVT 2024\Family Templates\English\Metric Generic Model.rft"))
            {
                using(var tx=new Transaction(family,"QA round connector sleeve"))
                {
                    tx.Start();
                    family.OwnerFamily.FamilyCategory=Category.GetCategory(family,BuiltInCategory.OST_PipeAccessory);
                    var vertical=family.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_ALWAYS_VERTICAL);
                    if(vertical!=null&&!vertical.IsReadOnly)vertical.Set(0);
                    var fm=family.FamilyManager; fm.NewType("150A");
                    fm.Set(fm.AddParameter("有效內徑",GroupTypeId.Geometry,SpecTypeId.Length,true),150/304.8);
                    fm.Set(fm.AddParameter("套管長度",GroupTypeId.Geometry,SpecTypeId.Length,true),200/304.8);
                    fm.AddParameter("來源管線Id",GroupTypeId.Data,SpecTypeId.String.Text,true);
                    fm.AddParameter("穿越構件",GroupTypeId.Data,SpecTypeId.String.Text,true);
                    var profile=new CurveArray();
                    profile.Append(Arc.Create(XYZ.Zero,100/304.8,0,2*Math.PI,XYZ.BasisX,XYZ.BasisY));
                    var loops=new CurveArrArray(); loops.Append(profile);
                    var solid=family.FamilyCreate.NewExtrusion(true,loops,SketchPlane.Create(family,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero)),200/304.8);
                    family.Regenerate();
                    var faces=solid.get_Geometry(new Options{ComputeReferences=true}).OfType<Solid>()
                        .SelectMany(s=>s.Faces.Cast<Face>()).OfType<PlanarFace>().Where(f=>Math.Abs(f.FaceNormal.Z)>.99).ToList();
                    foreach(var face in faces)
                    {
                        var c=ConnectorElement.CreatePipeConnector(family,PipeSystemType.DomesticColdWater,face.Reference);
                        c.get_Parameter(BuiltInParameter.CONNECTOR_RADIUS).Set(75/304.8);
                    }
                    tx.Commit();
                }
                family.SaveAs(path); family.Close(false);
            }
            using(var group=new TransactionGroup(_doc,"QA axis checks"))
            {
                group.Start(); FamilyInstance sleeve=null; Pipe pipe=null; PipeType pipeType=null; PipingSystemType system=null; Level level=null;
                Tx("QA axis family and horizontal source",()=>
                {
                    _doc.LoadFamily(path,out Family family);
                    var symbol=(FamilySymbol)_doc.GetElement(family.GetFamilySymbolIds().First());symbol.Activate();
                    level=(Level)Call("SleeveGlLevel","Actual",_doc,_doc.GetElement(_first));
                    pipeType=new FilteredElementCollector(_doc).OfClass(typeof(PipeType)).Cast<PipeType>().First();
                    system=PipingSystemType.Create(_doc,MEPSystemClassification.DomesticColdWater,"QA-AXIS");
                    pipe=Pipe.Create(_doc,system.Id,pipeType.Id,level.Id,new XYZ(0,-5,3),new XYZ(0,5,3));
                    pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(100/304.8);
                    sleeve=_doc.Create.NewFamilyInstance(new XYZ(0,0,3),symbol,level,StructuralType.NonStructural);
                    sleeve.LookupParameter("來源管線Id").Set(pipe.Id.Value.ToString());sleeve.LookupParameter("穿越構件").Set("穿牆");
                    _doc.Regenerate();ElementTransformUtils.MoveElement(_doc,sleeve.Id,new XYZ(0,0,3)-Center(sleeve));
                });
                XYZ Axis(){var p=sleeve.MEPModel.ConnectorManager.Connectors.Cast<Connector>().Where(c=>c.ConnectorType==ConnectorType.End).ToList();return(p[1].Origin-p[0].Origin).Normalize();}
                object Inspect()=>Call("PipeSleeveManagerForm+PipeSleeveManagerRow","From",_doc,sleeve);
                Check(Math.Abs(Axis().DotProduct(sleeve.FacingOrientation))<.01,"Real connector axis differs from family FacingOrientation");
                Check((string)Property(Inspect(),"Status")=="需更新","Perpendicular connector axis is reported as update required");
                string uid=sleeve.UniqueId;
                Tx("QA align actual bore axis",()=>
                {
                    Log("Before align: axis="+Axis()+" basisZ="+sleeve.GetTransform().BasisZ+" vertical="+sleeve.Symbol.Family.get_Parameter(BuiltInParameter.FAMILY_ALWAYS_VERTICAL)?.AsInteger());
                    try { Call("PipeSleeveService","AlignSleeveToDirection",_doc,sleeve,Center(sleeve),XYZ.BasisY); }
                    catch { Log("After failed align: axis="+Axis()+" basisZ="+sleeve.GetTransform().BasisZ+" facing="+sleeve.FacingOrientation); throw; }
                });
                Check(Math.Abs(Axis().Z)<1e-8&&Math.Abs(Axis().Y)>1-1e-8&&sleeve.UniqueId==uid,"Bore axis is horizontal and aligned without replacing original sleeve");
                Tx("QA repair slight tilt below former rotation tolerance",()=>
                {
                    var center=Center(sleeve);
                    Call("PipeSleeveService","AlignSleeveToDirection",_doc,sleeve,center,new XYZ(0,Math.Cos(.01),Math.Sin(.01)));
                    Check(Math.Abs(Axis().Z)>.005,"Tilt fixture actually inclines before correction");
                    _doc.Regenerate();Call("PipeSleeveService","AlignSleeveToDirection",_doc,sleeve,center,XYZ.BasisY);
                });
                Check(Math.Abs(Axis().Z)<1e-8,"Small visible tilt is corrected instead of skipped by old 0.001 dot tolerance");
                RunHostNormalChecks(level);
                Tx("QA one percent slope",()=>
                {
                    var slope=Pipe.Create(_doc,system.Id,pipeType.Id,level.Id,new XYZ(0,-5,2.95),new XYZ(0,5,3.05));
                    slope.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(100/304.8);
                    sleeve.LookupParameter("來源管線Id").Set(slope.Id.Value.ToString());
                });
                var row=Inspect();
                Check(new[]{"正常","偏心可容納"}.Contains((string)Property(row,"Status"))&&((string)Property(row,"CheckDetails")).Contains("0.573"),
                    "One percent sloped pipe in horizontal sleeve reports measured angle and conservative clearance");
                RunConstructionChecks(sleeve,level);
                Tx("QA excessive angle",()=>
                {
                    var slope=Pipe.Create(_doc,system.Id,pipeType.Id,level.Id,new XYZ(0,-5,-2),new XYZ(0,5,8));
                    slope.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(100/304.8);
                    sleeve.LookupParameter("來源管線Id").Set(slope.Id.Value.ToString());
                });
                Check((string)Property(Inspect(),"Status")=="需處理","Oblique pipe with insufficient bore clearance never receives passing status");
                group.RollBack();
            }
        }
    }
}
