using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
namespace HB.CadRevision
{
    internal static class Report
    {
        static string Extent(DrawingData data)
        {
            var s=data.Segments;
            if(s.Count==0)return "無直線段";
            return "("+N(s.Min(v=>Math.Min(v.X1,v.X2)))+", "+N(s.Min(v=>Math.Min(v.Y1,v.Y2)))+") → ("+N(s.Max(v=>Math.Max(v.X1,v.X2)))+", "+N(s.Max(v=>Math.Max(v.Y1,v.Y2)))+")";
        }
        static string H(string s)=>WebUtility.HtmlEncode(s??"");
        static string N(double d)=>d.ToString("0.###",CultureInfo.InvariantCulture);
        static string Csv(string s)=>"\""+("'"+(s??"")).Replace("\"","\"\"")+"\"";
        internal static void Write(string folder,string oldPath,string newPath,DrawingData a,DrawingData b,List<Change> changes,double tolerance,double radius)
        {
            var all=a.Segments.Concat(b.Segments).ToList();
            double x=all.Min(s=>Math.Min(s.X1,s.X2)),y=all.Min(s=>Math.Min(-s.Y1,-s.Y2));
            double w=Math.Max(1,all.Max(s=>Math.Max(s.X1,s.X2))-x),h=Math.Max(1,all.Max(s=>Math.Max(-s.Y1,-s.Y2))-y),pad=Math.Max(w,h)*.02;
            string bounds=$"{N(x-pad)} {N(y-pad)} {N(w+pad*2)} {N(h+pad*2)}";
            var html=new StringBuilder("<!doctype html><meta charset='utf-8'><title>CAD 變更比對</title><style>body{font:15px 'Microsoft JhengHei',sans-serif;margin:24px;color:#243447}main{display:grid;grid-template-columns:380px 1fr;gap:20px}aside{max-height:65vh;overflow:auto}button{display:block;width:100%;padding:9px;text-align:left;border:1px solid #ddd;background:white}svg{width:100%;height:65vh;background:#fafafa}table{border-collapse:collapse}td,th{padding:6px;border:1px solid #ddd}.selected{stroke:#ad00ff!important;stroke-width:4!important}</style><h1>CAD 變更比對 · 線段試行版</h1>");
            html.Append("<p>舊版："+H(oldPath)+"<br>新版："+H(newPath)+"<br>"+H(a.Units)+" / "+H(b.Units)+"<br>容差 "+N(tolerance)+" mm；位移候選範圍 "+N(radius)+" mm。對齊由使用者確認，未執行自動校正。</p>");
            html.Append("<p><a href='curves.html'>查看圓與圓弧比對（獨立統計）</a></p><p>本頁僅模型空間 LINE／直線聚合線段。紅＝舊版刪除，綠＝新版新增，橘＝疑似位移，灰＝未變更。包含可解析圖塊／外部參考；文字及裁切參考等不納入；可見性與圖層開關未模擬。此結果不是整張圖的變更率或 BIM 修改量。</p><p>");
            html.Append("</p><h2>圖層用途分組</h2><p>僅依 DIM、jet-dim、TEXT、字體圖層名稱分類（含外部參考前綴），不代表已辨識所有建築或標註物件。</p>");
            foreach(var group in changes.GroupBy(c=>CompareCore.LayerGroup((c.Old??c.New).Layer)))
                html.Append("<p>"+H(group.Key)+"："+H(string.Join("、",group.GroupBy(c=>c.Kind).Select(g=>g.Key+"="+g.Count())))+"</p>");
            html.Append("<h2>座標範圍檢查（mm）</h2><p>"+H("舊版："+Extent(a)+"；新版："+Extent(b))+"<br>範圍相近仍不代表對齊；未自動移動或旋轉圖面。請核對共同軸線與外部參考插入點。</p>");
            html.Append("<details><summary>外部參考解析紀錄</summary><p>舊版："+H(string.Join("；",a.Notes))+"</p><p>新版："+H(string.Join("；",b.Notes))+"</p></details><p>");
            foreach(var g in changes.GroupBy(c=>c.Kind)) html.Append(H(g.Key)+"："+g.Count()+"　");
            html.Append("</p><details open><summary>未納入範圍</summary>舊版："+H(string.Join("、",a.Skipped.Select(k=>k.Key+"="+k.Value)))+"<br>新版："+H(string.Join("、",b.Skipped.Select(k=>k.Key+"="+k.Value)))+"</details><main><aside>");
            for(int i=0;i<changes.Count;i++) if(changes[i].Kind!="未變更") html.Append("<button data-id='"+i+"'>"+H(changes[i].Kind+"｜"+(changes[i].Old??changes[i].New).Layer+"｜"+N(changes[i].DistanceMm)+" mm")+"</button>");
            html.Append("</aside><section><button id='reset'>顯示全圖</button><svg xmlns='http://www.w3.org/2000/svg' viewBox='"+bounds+"'>");
            for(int i=0;i<changes.Count;i++)
            {
                var c=changes[i];foreach(var s in new[]{c.Old,c.New}.Where(s=>s!=null))
                { string color=c.Kind=="未變更"?"#ccc":c.Kind=="疑似位移"?"#d77a00":s==c.Old?"#d33":"#15964a";
                    html.Append($"<line data-change='{i}' x1='{N(s.X1)}' y1='{N(-s.Y1)}' x2='{N(s.X2)}' y2='{N(-s.Y2)}' stroke='{color}' stroke-width='1.5' vector-effect='non-scaling-stroke'/>"); }
            }
            html.Append("</svg></section></main><h2>圖層統計（線段）</h2><table><tr><th>圖層</th><th>分類</th><th>數量</th></tr>");
            foreach(var g in changes.GroupBy(c=>new { Layer=(c.Old??c.New).Layer,c.Kind })) html.Append("<tr><td>"+H(g.Key.Layer)+"</td><td>"+H(g.Key.Kind)+"</td><td>"+g.Count()+"</td></tr>");
            html.Append("</table><script>const svg=document.querySelector('svg'),full=svg.getAttribute('viewBox');document.querySelector('#reset').onclick=()=>svg.setAttribute('viewBox',full);document.querySelectorAll('[data-id]').forEach(b=>b.onclick=()=>{document.querySelectorAll('.selected').forEach(e=>e.classList.remove('selected'));let lines=[...svg.querySelectorAll('[data-change=\"'+b.dataset.id+'\"]')],xs=[],ys=[];lines.forEach(l=>{l.classList.add('selected');xs.push(+l.getAttribute('x1'),+l.getAttribute('x2'));ys.push(+l.getAttribute('y1'),+l.getAttribute('y2'))});let x=Math.min(...xs),y=Math.min(...ys),w=Math.max(...xs)-x,h=Math.max(...ys)-y,p=Math.max(100,w*.2,h*.2);svg.setAttribute('viewBox',[x-p,y-p,w+2*p,h+2*p].join(' '))});</script>");
            html.Append(ReportNavigation.Html());
            File.WriteAllText(Path.Combine(folder,"report.html"),html.ToString(),new UTF8Encoding(true));
            var csv=new StringBuilder("分類,圖層,舊Handle,新Handle,候選位移mm\r\n");
            foreach(var c in changes) csv.AppendLine(string.Join(",",new[]{Csv(c.Kind),Csv((c.Old??c.New).Layer),Csv(c.Old?.Id),Csv(c.New?.Id),N(c.DistanceMm)}));
            File.WriteAllText(Path.Combine(folder,"changes.csv"),csv.ToString(),new UTF8Encoding(true));
        }
    }
}
