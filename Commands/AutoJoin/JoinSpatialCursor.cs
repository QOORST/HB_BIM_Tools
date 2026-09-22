using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoJoin
{
    // Bounded rebuilds; every query returns the next original index, preserving execution order.
    internal sealed class JoinSpatialCursor
    {
        private sealed class Bounds
        {
            public double[] Min, Max;
            public bool Meets(Bounds b) {
                for(int a=0;a<3;a++) if(Max[a]<b.Min[a]-1e-6 || b.Max[a]<Min[a]-1e-6) return false;
                return true;
            }
        }
        private sealed class Node { public Bounds Box; public int MaxIndex; public Node Left,Right; public int[] Items; }
        private readonly int count;
        private readonly Func<int,BoundingBoxXYZ> read;
        private Bounds[] bounds;
        private Node root;
        private int builds;
        private bool fallback;
        public JoinSpatialCursor(int count,Func<int,BoundingBoxXYZ> read) { this.count=count;this.read=read;fallback=count<128; }
        public void Invalidate() { root=null;bounds=null; }
        public int Next(int source,int minimum)
        {
            if(minimum>=count)return count;
            if(fallback)return minimum;
            if(bounds==null)
            {
                if(builds>=4){fallback=true;return minimum;}
                builds++;
                bounds=new Bounds[count];
                var valid=new List<int>();
                for(int i=0;i<count;i++) {
                    var b=read(i);if(b==null)continue;
                    var min=new[]{b.Min.X,b.Min.Y,b.Min.Z};var max=new[]{b.Max.X,b.Max.Y,b.Max.Z};
                    if(min.Concat(max).Any(v=>double.IsNaN(v)||double.IsInfinity(v)) || Enumerable.Range(0,3).Any(a=>min[a]>max[a])) {
                        fallback=true;bounds=null;return minimum;
                    }
                    bounds[i]=new Bounds {Min=min,Max=max};valid.Add(i);
                }
                root=Build(valid.ToArray());
            }
            return bounds[source]==null?count:Search(root,bounds[source],minimum,count);
        }
        private Node Build(int[] ids)
        {
            if(ids.Length==0)return null;
            var box=new Bounds {Min=new double[3],Max=new double[3]};
            for(int a=0;a<3;a++){box.Min[a]=ids.Min(i=>bounds[i].Min[a]);box.Max[a]=ids.Max(i=>bounds[i].Max[a]);}
            var node=new Node {Box=box,MaxIndex=ids.Max()};
            if(ids.Length<=8){node.Items=ids;return node;}
            int axis=Enumerable.Range(0,3).OrderByDescending(a=>box.Max[a]-box.Min[a]).First();
            Array.Sort(ids,(i,j)=> { int c=(bounds[i].Min[axis]*.5+bounds[i].Max[axis]*.5).CompareTo(bounds[j].Min[axis]*.5+bounds[j].Max[axis]*.5);return c==0?i.CompareTo(j):c; });
            int half=ids.Length/2;
            node.Left=Build(ids.Take(half).ToArray());node.Right=Build(ids.Skip(half).ToArray());return node;
        }
        private int Search(Node node,Bounds target,int minimum,int best)
        {
            if(node==null || node.MaxIndex<minimum || !node.Box.Meets(target))return best;
            if(node.Items!=null){foreach(int id in node.Items)if(id>=minimum && id<best && bounds[id].Meets(target))best=id;return best;}
            best=Search(node.Left,target,minimum,best);
            return Search(node.Right,target,minimum,best);
        }
    }
}
