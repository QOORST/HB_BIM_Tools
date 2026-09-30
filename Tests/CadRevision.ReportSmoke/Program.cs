using System;
using System.IO;
using System.Collections.Generic;
namespace HB.CadRevision
{
    internal class DrawingData {internal string Units="mm";internal List<string> Notes=new List<string>();internal Dictionary<string,int> Skipped=new Dictionary<string,int>();}
    class Program
    {
        static void Main(string[] args)
        {
            string folder=args[0];Directory.CreateDirectory(folder);
            var a=new DrawingData();a.Notes.Add("僅主圖；試驗 </script><script>alert(1)</script>");a.Skipped.Add("MText",2);
            var lines=new List<Change>{new Change{Kind="新增",New=new Segment{Id="A",Layer="WALL</script>",X1=0,Y1=0,X2=100,Y2=0}}};
            var curves=new List<RoundChange>{new RoundChange{Kind="刪除",Old=new RoundGeometry{Id="B",Layer="PIPE",X=50,Y=10,Radius=5,IsCircle=true,Sweep=2*Math.PI}}};
            UnifiedReport.Write(folder,"old.dwg","new.dwg",a,a,lines,curves,1,500);
            var text=File.ReadAllText(Path.Combine(folder,"review.html"));
            if(text.Contains("__PAYLOAD__")||text.Contains("__EVIDENCE__")||text.Contains("WALL</script>")||!text.Contains("compare-mode"))throw new Exception("Template or escaping failed");
            UnifiedReport.Write(Path.Combine(folder),"old.dwg","new.dwg",a,a,new List<Change>(),curves,1,500);
            text=File.ReadAllText(Path.Combine(folder,"review.html"));if(!text.Contains("PIPE")||!text.Contains("scope" )&&!File.Exists(Path.Combine(folder,"scope.txt")))throw new Exception("Curve-only report failed");
            Console.WriteLine("PASS embedded template, safe escaping, mixed geometry and curve-only output");
        }
    }
}
