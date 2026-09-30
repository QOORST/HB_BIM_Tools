using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace HB.CadRevision
{
    internal static class UnifiedReport
    {
        static string Serialize(object value)
        {
#if NETFRAMEWORK
            return new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Serialize(value);
#else
            return JsonSerializer.Serialize(value);
#endif
        }
        static string Resource(string name)
        {
            using(var stream=typeof(UnifiedReport).Assembly.GetManifestResourceStream("HB.CadRevision."+name))
            { if(stream==null)throw new InvalidOperationException("缺少報告資源："+name);using(var reader=new StreamReader(stream))return reader.ReadToEnd(); }
        }
        static object Line(Segment s)=>new double[]{s.X1,-s.Y1,s.X2,-s.Y2};
        static object Curve(RoundGeometry s)=>new object[]{s.X,-s.Y,s.Radius,s.Start,s.Sweep,s.IsCircle};
        internal static void Write(string folder,string oldPath,string newPath,DrawingData a,DrawingData b,List<Change> lines,List<RoundChange> curves,double tolerance,double radius)
        {
            var layers=lines.Select(x=>(x.Old??x.New).Layer).Concat(curves.Select(x=>(x.Old??x.New).Layer)).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var kinds=lines.Select(x=>x.Kind).Concat(curves.Select(x=>x.Kind)).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var layerIds=layers.Select((x,i)=>new {x,i}).ToDictionary(x=>x.x,x=>x.i);
            var kindIds=kinds.Select((x,i)=>new {x,i}).ToDictionary(x=>x.x,x=>x.i);
            var data=new List<object>();
            foreach(var c in lines)
            {
                var shapes=new List<object>();if(c.Old!=null)shapes.Add(Line(c.Old));if(c.New!=null&&(c.Kind!="未變更"||c.Old==null))shapes.Add(Line(c.New));
                data.Add(new object[]{layerIds[(c.Old??c.New).Layer],kindIds[c.Kind],shapes,c.Old?.Id??"",c.New?.Id??"","直線"});
            }
            foreach(var c in curves)
            {
                var shapes=new List<object>();if(c.Old!=null)shapes.Add(Curve(c.Old));if(c.New!=null&&(c.Kind!="未變更"||c.Old==null))shapes.Add(Curve(c.New));
                data.Add(new object[]{layerIds[(c.Old??c.New).Layer],kindIds[c.Kind],shapes,c.Old?.Id??"",c.New?.Id??"",(c.Old??c.New).IsCircle?"圓":"圓弧"});
            }
            var evidence="舊版："+oldPath+"\n新版："+newPath+"\n單位："+a.Units+" / "+b.Units+"\n容差 mm："+tolerance+"；位移範圍 mm："+radius+"\n舊版設定："+string.Join("；",a.Notes)+"\n新版設定："+string.Join("；",b.Notes)+"\n舊版略過："+string.Join("；",a.Skipped.Select(x=>x.Key+"="+x.Value))+"\n新版略過："+string.Join("；",b.Skipped.Select(x=>x.Key+"="+x.Value));

            var identity=Serialize(new {layers,kinds,data,evidence});string sourceId;
            using(var hash=SHA256.Create())sourceId=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").ToLowerInvariant();
            var json=Serialize(new {layers,groups=layers.Select(x=>"待分類").ToArray(),kinds,data,sourceId}).Replace("<","\\u003c").Replace("\u2028","\\u2028").Replace("\u2029","\\u2029");
            var template=Resource("review-template.html");
            var page=template.Replace("__EVIDENCE__",WebUtility.HtmlEncode(evidence)).Replace("__PAYLOAD__",json);
            page=page.Replace("</script></html>",Resource("compare_modes.js")+"</script></html>");
            File.WriteAllText(Path.Combine(folder,"review.html"),page,Encoding.UTF8);
            File.WriteAllText(Path.Combine(folder,"scope.txt"),"HB CAD 比對試用版 0.1.0\n完成擷取與比對不代表全圖完整支援。\n"+evidence,Encoding.UTF8);
        }
    }
}
