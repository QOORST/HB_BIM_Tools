using System;
using System.Collections.Generic;
using System.Linq;

namespace HB.CadRevision
{
    public sealed class Segment
    {
        public string Id, Layer;
        public double X1,Y1,X2,Y2;
        public double Length => Math.Sqrt((X2-X1)*(X2-X1)+(Y2-Y1)*(Y2-Y1));
    }
    public sealed class Change
    {
        public string Kind;
        public Segment Old, New;
        public double DistanceMm;
    }
    public static class CompareCore
    {
        public static List<Change> Compare(IList<Segment> oldItems,IList<Segment> newItems,double tolerance,double moveRadius)
        {
            if(!Finite(tolerance)||tolerance<=0||!Finite(moveRadius)||moveRadius<tolerance) throw new ArgumentException("容差必須大於零，候選位移範圍不得小於容差。");

            foreach(var s in oldItems.Concat(newItems))
                if(s==null||string.IsNullOrEmpty(s.Layer)||!Finite(s.X1)||!Finite(s.Y1)||!Finite(s.X2)||!Finite(s.Y2)||!Finite(s.Length)||s.Length<=0) throw new ArgumentException("無效線段。");
            long examined=0;
            Action check=()=> { if(++examined>25000000)throw new ArgumentException("超過 2,500 萬次有效候選檢查，請縮小圖層範圍。"); };
            var usedOld=new HashSet<int>(); var usedNew=new HashSet<int>(); var result=new List<Change>();
            var exact=new SegmentIndex(newItems,tolerance,2*tolerance,Enumerable.Range(0,newItems.Count));
            for(int i=0;i<oldItems.Count;i++)
            {
                int best=int.MaxValue;
                foreach(var bucket in exact.Buckets(oldItems[i]))
                    foreach(int j in bucket)
                    {
                        if(j>=best)break;
                        check();
                        if(EndpointError(oldItems[i],newItems[j])<=tolerance) { best=j;break; }
                    }
                if(best!=int.MaxValue)
                {
                    usedOld.Add(i);usedNew.Add(best);exact.Remove(best);
                    result.Add(new Change { Kind="未變更",Old=oldItems[i],New=newItems[best] });
                }
            }
            var moving=new SegmentIndex(newItems,moveRadius,tolerance,Enumerable.Range(0,newItems.Count).Where(j=>!usedNew.Contains(j)));
            var candidates=new Dictionary<int,List<int>>(); var reverse=new Dictionary<int,List<int>>();
            for(int i=0;i<oldItems.Count;i++) if(!usedOld.Contains(i))
                foreach(int j in moving.Buckets(oldItems[i]).SelectMany(bucket=>bucket))
                {
                    check();
                    var a=oldItems[i];var b=newItems[j];
                    double dot=(a.X2-a.X1)*(b.X2-b.X1)+(a.Y2-a.Y1)*(b.Y2-b.Y1);
                    double bx=dot>=0 ? b.X2-b.X1 : b.X1-b.X2, by=dot>=0 ? b.Y2-b.Y1 : b.Y1-b.Y2;
                    // A translation candidate requires congruent endpoint vectors, not only equal length.
                    if(Hypot((a.X2-a.X1)-bx,(a.Y2-a.Y1)-by)>tolerance || MidDistance(a,b)>moveRadius) continue;
                    if(!candidates.ContainsKey(i)) candidates[i]=new List<int>(); if(candidates[i].Count<2)candidates[i].Add(j);
                    if(!reverse.ContainsKey(j)) reverse[j]=new List<int>(); if(reverse[j].Count<2)reverse[j].Add(i);
                }
            foreach(var pair in candidates.OrderBy(p=>p.Key))
                if(pair.Value.Count==1&&reverse[pair.Value[0]].Count==1)
                { int i=pair.Key,j=pair.Value[0];usedOld.Add(i);usedNew.Add(j);result.Add(new Change { Kind="疑似位移",Old=oldItems[i],New=newItems[j],DistanceMm=MidDistance(oldItems[i],newItems[j]) }); }
            for(int i=0;i<oldItems.Count;i++) if(!usedOld.Contains(i)) result.Add(new Change { Kind=candidates.ContainsKey(i)?"刪除／配對待確認":"刪除",Old=oldItems[i] });
            for(int j=0;j<newItems.Count;j++) if(!usedNew.Contains(j)) result.Add(new Change { Kind=reverse.ContainsKey(j)?"新增／配對待確認":"新增",New=newItems[j] });
            return result;
        }
        // Separate fine unchanged index and broader movement index. Signed vector bins
        // reject incompatible directions/lengths before scanning dense position bins.
        sealed class SegmentIndex
        {
            readonly IList<Segment> items;
            readonly double positionStep,vectorStep;
            readonly Dictionary<Tuple<string,long,long,long,long>,SortedSet<int>> bins=new Dictionary<Tuple<string,long,long,long,long>,SortedSet<int>>();
            internal SegmentIndex(IList<Segment> source,double position,double vector,IEnumerable<int> indices)
            {
                items=source;positionStep=position;vectorStep=vector;
                foreach(int j in indices) { var k=Key(items[j]);if(!bins.ContainsKey(k))bins[k]=new SortedSet<int>();bins[k].Add(j); }
            }
            Tuple<string,long,long,long,long> Key(Segment s)=>Tuple.Create(s.Layer.ToUpperInvariant(),Cell((s.X1/2+s.X2/2)/positionStep),Cell((s.Y1/2+s.Y2/2)/positionStep),Cell((s.X2-s.X1)/vectorStep),Cell((s.Y2-s.Y1)/vectorStep));
            internal void Remove(int j) { var k=Key(items[j]);bins[k].Remove(j);if(bins[k].Count==0)bins.Remove(k); }
            internal IEnumerable<SortedSet<int>> Buckets(Segment s)
            {
                var k=Key(s);var visited=new HashSet<Tuple<long,long>>();
                foreach(int sign in new[]{1,-1})
                {
                    long vx=Cell(sign*(s.X2-s.X1)/vectorStep),vy=Cell(sign*(s.Y2-s.Y1)/vectorStep);
                    for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
                    {
                        var vector=Tuple.Create(vx+dx,vy+dy);if(!visited.Add(vector))continue;
                        for(int px=-1;px<=1;px++)for(int py=-1;py<=1;py++)
                            if(bins.TryGetValue(Tuple.Create(k.Item1,k.Item2+px,k.Item3+py,vector.Item1,vector.Item2),out var bucket))yield return bucket;
                    }
                }
            }
        }
        static long Cell(double value)
        {
            if(!Finite(value)||Math.Abs(value)>9e18) throw new ArgumentException("座標超出索引範圍。");
            return (long)Math.Floor(value);
        }
        public static string LayerGroup(string layer)
        {
            var name=(layer??"").Split('|').Last().ToUpperInvariant();
            if(name=="DIM"||name=="JET-DIM"||name=="TEXT"||name=="字體") return "標註候選圖層";
            return "其他圖層（含建築候選）";
        }
        static bool SameLayer(Segment a,Segment b)=>string.Equals(a.Layer,b.Layer,StringComparison.OrdinalIgnoreCase);
        static bool Finite(double d)=>!double.IsNaN(d)&&!double.IsInfinity(d);
        static double Hypot(double x,double y)=>Math.Sqrt(x*x+y*y);
        static double MidDistance(Segment a,Segment b)=>Hypot((a.X1+a.X2-b.X1-b.X2)/2,(a.Y1+a.Y2-b.Y1-b.Y2)/2);
        static double EndpointError(Segment a,Segment b)=>Math.Min(
            Math.Max(Hypot(a.X1-b.X1,a.Y1-b.Y1),Hypot(a.X2-b.X2,a.Y2-b.Y2)),
            Math.Max(Hypot(a.X1-b.X2,a.Y1-b.Y2),Hypot(a.X2-b.X1,a.Y2-b.Y1)));
    }
}
