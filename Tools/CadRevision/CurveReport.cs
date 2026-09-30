using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace HB.CadRevision
{
    internal static class CurveReport
    {
        static string N(double n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
        static string H(string s)=>WebUtility.HtmlEncode(s??"");
        static string Q(string s)=>"\"'"+(s??"").Replace("\"","\"\"")+"\"";
        internal static void Write(string folder,DrawingData a,DrawingData b,List<RoundChange> changes)
        {
            var all=a.Curves.Concat(b.Curves).ToList();
            var html=new StringBuilder("<!doctype html><meta charset='utf-8'><title>HB 圓與圓弧比對</title><style>body{font:15px system-ui;margin:24px;color:#203348}svg{width:100%;height:70vh;background:#fafbfd}td,th{padding:8px;border-bottom:1px solid #ddd}table{border-collapse:collapse}pre{white-space:pre-wrap}</style><h1>圓與圓弧比對</h1><p><a href='report.html'>返回直線報告</a></p>");
            html.Append("<p>"+H(a.Units)+"<br>"+H(b.Units)+"</p><p>每個圓／弧各一筆；聚合線每個弧段各一筆。半徑變更呈現新增與刪除；位移僅列雙向唯一候選。未包含文字、橢圓、裁切幾何；不是建築物件數或影響面積。</p>");
            html.Append("<p>"+H(string.Join("　",changes.GroupBy(c=>c.Kind).Select(g=>g.Key+"："+g.Count())))+"</p>");
            if(all.Count>0)
            {
                double x=all.Min(c=>c.X-c.Radius),y=all.Min(c=>-c.Y-c.Radius),w=all.Max(c=>c.X+c.Radius)-x,h=all.Max(c=>-c.Y+c.Radius)-y;
                html.Append("<button id='reset'>顯示全圖</button><p>滾輪縮放；點選圖形放大。紅＝刪除、綠＝新增、橘＝疑似位移、灰＝未變更。</p><svg viewBox='"+N(x)+" "+N(y)+" "+N(Math.Max(w,1))+" "+N(Math.Max(h,1))+"'>");
                foreach(var change in changes)
                {
                    var geometry=change.Kind=="未變更"?new[]{change.New}:new[]{change.Old,change.New};
                    foreach(var c in geometry.Where(c=>c!=null))
                    {
                        string color=change.Kind=="未變更"?"#ccd3db":change.Kind=="疑似位移"?"#c87800":c==change.Old?"#ca4040":"#18864d";
                        string attributes=" fill='none' stroke='"+color+"' stroke-width='1.2' vector-effect='non-scaling-stroke'";
                        if(c.IsCircle)html.Append("<circle cx='"+N(c.X)+"' cy='"+N(-c.Y)+"' r='"+N(c.Radius)+"'"+attributes+">");
                        else
                        {
                            double end=c.Start+c.Sweep;
                            html.Append("<path d='M "+N(c.X+c.Radius*Math.Cos(c.Start))+" "+N(-c.Y-c.Radius*Math.Sin(c.Start))+" A "+N(c.Radius)+" "+N(c.Radius)+" 0 "+(c.Sweep>Math.PI?"1":"0")+" 0 "+N(c.X+c.Radius*Math.Cos(end))+" "+N(-c.Y-c.Radius*Math.Sin(end))+"'"+attributes+">");
                        }
                        html.Append("<title>"+H(change.Kind+"｜"+c.Layer+"｜"+c.Id)+"</title>"+(c.IsCircle?"</circle>":"</path>"));
                    }
                }
                html.Append("</svg><script>const s=document.querySelector('svg'),full=s.getAttribute('viewBox');document.querySelector('#reset').onclick=()=>s.setAttribute('viewBox',full);s.onclick=e=>{if(!['path','circle'].includes(e.target.tagName))return;let b=e.target.getBBox(),p=Math.max(100,b.width*.3,b.height*.3);s.setAttribute('viewBox',[b.x-p,b.y-p,b.width+2*p,b.height+2*p].join(' '))};s.onwheel=e=>{e.preventDefault();let p=new DOMPoint(e.clientX,e.clientY).matrixTransform(s.getScreenCTM().inverse()),b=s.viewBox.baseVal,f=e.deltaY>0?1.2:1/1.2;s.setAttribute('viewBox',[p.x+(b.x-p.x)*f,p.y+(b.y-p.y)*f,b.width*f,b.height*f].join(' '))};</script>");
            }
            html.Append("<h2>圖層統計</h2><table><tr><th>圖層</th><th>種類</th><th>結果</th><th>筆數</th></tr>");
            foreach(var g in changes.GroupBy(c=>new {Layer=(c.Old??c.New).Layer,Circle=(c.Old??c.New).IsCircle,c.Kind}))
                html.Append("<tr><td>"+H(g.Key.Layer)+"</td><td>"+(g.Key.Circle?"圓":"圓弧")+"</td><td>"+H(g.Key.Kind)+"</td><td>"+g.Count()+"</td></tr>");
            html.Append("</table><h2>略過項目（包含直線讀取）</h2><pre>"+H("舊版："+string.Join("、",a.Skipped.Select(c=>c.Key+"="+c.Value))+"\n新版："+string.Join("、",b.Skipped.Select(c=>c.Key+"="+c.Value)))+"</pre>");
            html.Append(ReportNavigation.Html());
            File.WriteAllText(Path.Combine(folder,"curves.html"),html.ToString(),new UTF8Encoding(true));
            var csv=new StringBuilder("分類,圖層,幾何,舊Handle,新Handle,候選位移mm,舊圓心X,舊圓心Y,舊半徑,舊起角弧度,舊掃角弧度,新圓心X,新圓心Y,新半徑,新起角弧度,新掃角弧度\r\n");
            foreach(var c in changes)
            {
                var fields=new List<string>{Q(c.Kind),Q((c.Old??c.New).Layer),Q((c.Old??c.New).IsCircle?"圓":"圓弧"),Q(c.Old?.Id),Q(c.New?.Id),N(c.Distance)};
                foreach(var g in new[]{c.Old,c.New})fields.AddRange(g==null?new[]{"","","","",""}:new[]{N(g.X),N(g.Y),N(g.Radius),N(g.Start),N(g.Sweep)});
                csv.AppendLine(string.Join(",",fields));
            }
            File.WriteAllText(Path.Combine(folder,"curves.csv"),csv.ToString(),new UTF8Encoding(true));
        }
    }
}
