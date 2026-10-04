using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace HB.SleeveQuantity.Native
{
    public sealed partial class Startup
    {
        private void RunHostNormalChecks(Level level)
        {
            CurveLoop Rectangle(double x, double y, double z)
            {
                var loop = new CurveLoop();
                var points = new[] { new XYZ(-x,-y,z),new XYZ(x,-y,z),new XYZ(x,y,z),new XYZ(-x,y,z) };
                for (int i=0;i<4;i++) loop.Append(Line.CreateBound(points[i],points[(i+1)%4]));
                return loop;
            }
            object[] Measure(Element host, string kind, XYZ center, XYZ pipeDirection)
            {
                var curve=Line.CreateBound(center-pipeDirection.Normalize()*20,center+pipeDirection.Normalize()*20);
                object[] args={curve,host,kind,null,0.0,null};
                Check((bool)Call("PipeSleeveService","TryGetIntersectionData",args),kind+" crossing has verified parallel real faces");
                return args;
            }
            DirectShape beam=null; Floor floor=null; Wall wall=null;
            var beamRotation=Transform.CreateRotation(XYZ.BasisX,.17);
            Tx("QA host solids: wall, rolled beam and sloping native floor",()=>
            {
                var solid=GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop>{Rectangle(5,.5,-1)},XYZ.BasisZ,2);
                beam=DirectShape.CreateElement(_doc,new ElementId(BuiltInCategory.OST_GenericModel));
                beam.SetShape(new GeometryObject[]{SolidUtils.CreateTransformed(solid,beamRotation)});
                var floorType=new FilteredElementCollector(_doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(t=>!t.IsFoundationSlab);
                floor=Floor.Create(_doc,new List<CurveLoop>{Rectangle(5,5,0)},floorType.Id,level.Id,false,
                    Line.CreateBound(new XYZ(-4,0,0),new XYZ(4,0,0)),.1);
                var wallType=new FilteredElementCollector(_doc).OfClass(typeof(WallType)).Cast<WallType>().First(t=>t.Kind==WallKind.Basic);
                wall=Wall.Create(_doc,Line.CreateBound(new XYZ(-5,0,0),new XYZ(5,0,0)),wallType.Id,level.Id,10,0,false,false);
                _doc.Regenerate();
            });
            var wallData=Measure(wall,"Wall",new XYZ(0,0,3),new XYZ(.05,1,.01));
            Check(Math.Abs(((XYZ)wallData[5]).DotProduct(XYZ.BasisY))>1-1e-8 && Math.Abs((double)wallData[4]-wall.Width)<1e-6,
                "Sloped pipe wall sleeve is face-normal with true normal wall thickness");
            var beamNormal=beamRotation.OfVector(XYZ.BasisY);
            var beamData=Measure(beam,"Beam",XYZ.Zero,new XYZ(.1,1,.01));
            Check(Math.Abs(((XYZ)beamData[5]).DotProduct(beamNormal))>1-1e-8 && Math.Abs((double)beamData[4]-1)<1e-6,
                "Rolled beam sleeve follows actual beam face normal and not pipe slope");
            var floorData=Measure(floor,"Floor",XYZ.Zero,XYZ.BasisZ);
            var floorNormal=((XYZ)floorData[5]).Normalize();
            var expected=new XYZ(-.1,0,1).Normalize();
            Check(Math.Abs(floorNormal.DotProduct(expected))>1-1e-8 && Math.Abs(floorNormal.X)>.09,
                "Sloping native floor sleeve is perpendicular to floor, not forced to project Z");
            var thickness=floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM).AsDouble();
            Check(Math.Abs((double)floorData[4]-thickness)<1e-6,"Sloping floor sleeve length is normal thickness, not vertical pipe penetration length");
            var linkTransform=Transform.CreateRotation(XYZ.BasisZ,.63).Multiply(Transform.CreateRotation(XYZ.BasisX,.2));
            var local=(XYZ)beamData[5];
            var global=linkTransform.OfVector(local).Normalize();
            var tangent=linkTransform.OfVector(beamRotation.OfVector(XYZ.BasisX)).Normalize();
            Check(Math.Abs(global.DotProduct(tangent))<1e-8,"Linked host normal transformed once remains perpendicular to transformed face tangent");
            // A corner entry has multiple incompatible face normals; reject rather than guess.
            // Incomplete traversal is rejected even when a usable normal can be inferred.
            object[] incomplete={Line.CreateBound(new XYZ(0,-5,3),new XYZ(0,0,3)),wall,"Wall",null,0.0,null};
            Check(!(bool)Call("PipeSleeveService","TryGetIntersectionData",incomplete),"Incomplete host crossing retains existing sleeve without guessed placement");
        }
    }
}
