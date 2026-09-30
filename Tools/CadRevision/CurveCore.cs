using System;
using System.Collections.Generic;
using System.Linq;

namespace HB.CadRevision
{
    public sealed class RoundGeometry
    {
        public string Id,Layer;
        public bool IsCircle;
        public double X,Y,Radius,Start,Sweep;
    }
    public sealed class RoundChange
    {
        public string Kind;
        public RoundGeometry Old,New;
        public double Distance;
    }
    public static class CurveCore
    {
        static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        static double Hypot(double x,double y)=>Math.Sqrt(x*x+y*y);
        static double Distance(RoundGeometry a,RoundGeometry b)=>Hypot(a.X-b.X,a.Y-b.Y);
        static bool Shape(RoundGeometry a,RoundGeometry b,double tolerance)
        {
            if(a.IsCircle!=b.IsCircle||Math.Abs(a.Radius-b.Radius)>tolerance)return false;
            if(a.IsCircle)return true;
            foreach(double fraction in new[]{0.0,0.5,1.0})
            {
                double aa=a.Start+a.Sweep*fraction,bb=b.Start+b.Sweep*fraction;
                if(Hypot(a.Radius*Math.Cos(aa)-b.Radius*Math.Cos(bb),a.Radius*Math.Sin(aa)-b.Radius*Math.Sin(bb))>tolerance)return false;
            }
            return true;
        }
        public static List<RoundChange> Compare(IList<RoundGeometry> oldItems,IList<RoundGeometry> newItems,double tolerance,double radius)
        {
            if(!Finite(tolerance)||tolerance<=0||!Finite(radius)||radius<tolerance)throw new ArgumentException("無效曲線比對容差。");
            foreach(var c in oldItems.Concat(newItems))
                if(c==null||string.IsNullOrEmpty(c.Layer)||!Finite(c.X)||!Finite(c.Y)||!Finite(c.Radius)||c.Radius<=0||!Finite(c.Start)||!Finite(c.Sweep)||(!c.IsCircle&&(c.Sweep<=0||c.Sweep>=2*Math.PI)))throw new ArgumentException("無效圓／圓弧資料。");
            var result=new List<RoundChange>();var usedOld=new HashSet<int>();var usedNew=new HashSet<int>();
            var exact=new Index(newItems,tolerance,tolerance,Enumerable.Range(0,newItems.Count));
            long attempts=0;
            Action check=()=>{if(++attempts>25000000)throw new InvalidOperationException("曲線候選超過 2,500 萬次，請縮小圖層。");};
            for(int i=0;i<oldItems.Count;i++)
            {
                int best=int.MaxValue;
                foreach(var bucket in exact.Near(oldItems[i]))foreach(int j in bucket)
                {
                    if(j>=best)break;check();
                    if(Distance(oldItems[i],newItems[j])<=tolerance&&Shape(oldItems[i],newItems[j],tolerance)){best=j;break;}
                }
                if(best==int.MaxValue)continue;
                usedOld.Add(i);usedNew.Add(best);exact.Remove(best);result.Add(new RoundChange {Kind="未變更",Old=oldItems[i],New=newItems[best]});
            }
            var moving=new Index(newItems,radius,tolerance,Enumerable.Range(0,newItems.Count).Where(i=>!usedNew.Contains(i)));
            var forward=new Dictionary<int,List<int>>();var reverse=new Dictionary<int,List<int>>();
            for(int i=0;i<oldItems.Count;i++)if(!usedOld.Contains(i))foreach(var bucket in moving.Near(oldItems[i]))foreach(int j in bucket)
            {
                check();if(Distance(oldItems[i],newItems[j])>radius||!Shape(oldItems[i],newItems[j],tolerance))continue;
                if(!forward.ContainsKey(i))forward[i]=new List<int>();if(forward[i].Count<2)forward[i].Add(j);
                if(!reverse.ContainsKey(j))reverse[j]=new List<int>();if(reverse[j].Count<2)reverse[j].Add(i);
            }
            foreach(var p in forward.OrderBy(p=>p.Key))if(p.Value.Count==1&&reverse[p.Value[0]].Count==1)
            {
                int i=p.Key,j=p.Value[0];usedOld.Add(i);usedNew.Add(j);result.Add(new RoundChange {Kind="疑似位移",Old=oldItems[i],New=newItems[j],Distance=Distance(oldItems[i],newItems[j])});
            }
            for(int i=0;i<oldItems.Count;i++)if(!usedOld.Contains(i))result.Add(new RoundChange {Kind=forward.ContainsKey(i)?"刪除／配對待確認":"刪除",Old=oldItems[i]});
            for(int i=0;i<newItems.Count;i++)if(!usedNew.Contains(i))result.Add(new RoundChange {Kind=reverse.ContainsKey(i)?"新增／配對待確認":"新增",New=newItems[i]});
            return result;
        }
        sealed class Index
        {
            readonly IList<RoundGeometry> items;readonly double step,tolerance;
            readonly Dictionary<Tuple<string,bool,long,long,long>,SortedSet<int>> bins=new Dictionary<Tuple<string,bool,long,long,long>,SortedSet<int>>();
            static long Cell(double x){if(!Finite(x)||Math.Abs(x)>9e18)throw new ArgumentException("曲線座標超出索引範圍。");return (long)Math.Floor(x);}
            Tuple<string,bool,long,long,long> Key(RoundGeometry c)=>Tuple.Create(c.Layer.ToUpperInvariant(),c.IsCircle,Cell(c.X/step),Cell(c.Y/step),Cell(c.Radius/tolerance));
            internal Index(IList<RoundGeometry> source,double grid,double tol,IEnumerable<int> ids){items=source;step=grid;tolerance=tol;foreach(int i in ids){var k=Key(items[i]);if(!bins.ContainsKey(k))bins[k]=new SortedSet<int>();bins[k].Add(i);}}
            internal void Remove(int i){var k=Key(items[i]);bins[k].Remove(i);if(bins[k].Count==0)bins.Remove(k);}
            internal IEnumerable<SortedSet<int>> Near(RoundGeometry c){var k=Key(c);for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int r=-1;r<=1;r++)if(bins.TryGetValue(Tuple.Create(k.Item1,k.Item2,k.Item3+x,k.Item4+y,k.Item5+r),out var b))yield return b;}
        }
    }
}
