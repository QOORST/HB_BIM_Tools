using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;

class Program
{
    static int checks;
    static Solid S(double a, double b) => new Solid(new[] { (a,b) });
    static Solid Trim(Solid panel, params Solid[] hosts) => StairRiserGeometry.TrimHostIntrusions(panel, hosts);
    static void Check(bool valid, string label)
    { if (!valid) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static void Main()
    {
        var panel = S(0,10);
        Check(Trim(null, S(0,10)) == null, "null panel");
        Check(Trim(S(0,0), S(0,10)) == null, "empty panel");
        Check(ReferenceEquals(Trim(panel),panel), "no host solids preserves panel");
        Check(ReferenceEquals(Trim(panel,S(10,20)),panel), "touching host does not remove normal riser");
        Check(ReferenceEquals(Trim(panel,S(20,30)),panel), "disjoint host unchanged");
        Check(Trim(panel,S(8,20)).Volume == 8, "partial nosing intrusion trimmed");
        Check(Trim(panel,S(0,10)) == null, "internal fully occupied face omitted");
        var split = Trim(panel,S(4,6));
        Check(split.Volume == 8 && split.Parts.Count == 2, "separated exposed remnants retained");
        Check(Trim(panel,S(0,3),S(7,10)).Volume == 4, "multiple host solids trimmed");
        Check(Trim(panel,S(0,3),S(0,3)).Volume == 7, "repeated host solid not double deducted");
        Check(Math.Abs(Trim(panel,S(0,9.99)).Volume - 0.01) < 1e-8, "small exposed return not discarded");
        Check(Trim(panel,null,S(0,0)).Volume == 10, "empty host solids ignored");
        BooleanOperationsUtils.BadDifference = true;
        bool rejected = false;
        try { Trim(panel,S(0,3)); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "invalid difference rejected");
        BooleanOperationsUtils.BadDifference = false;
        CurvedMeshBudget.Cancel = true;
        bool cancelled = false;
        try { Trim(panel,S(0,3)); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancellation checked between host solids");
        Console.WriteLine($"{checks} host-trim policy tests passed with interval geometry doubles, not native Revit.");
    }
}
namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    internal static class CurvedMeshBudget
    {
        internal static bool Cancel;
        internal static void Checkpoint(string stage) { if (Cancel) throw new OperationCanceledException(); }
    }
}
namespace Autodesk.Revit.DB
{
    public class Solid
    {
        public List<(double A,double B)> Parts;
        public double Volume => Parts.Sum(p=>p.B-p.A);
        public Solid(IEnumerable<(double,double)> parts) { Parts = parts.ToList(); }
    }
    public enum BooleanOperationsType { Intersect, Difference }
    public static class BooleanOperationsUtils
    {
        public static bool BadDifference;
        public static Solid ExecuteBooleanOperation(Solid a, Solid b, BooleanOperationsType op)
        {
            if (op == BooleanOperationsType.Difference && BadDifference) return a;
            var parts = new List<(double A,double B)>();
            if (op == BooleanOperationsType.Intersect)
            {
                foreach (var x in a.Parts) foreach (var y in b.Parts)
                { var lo = Math.Max(x.A,y.A); var hi = Math.Min(x.B,y.B); if (hi>lo) parts.Add((lo,hi)); }
            }
            else
            {
                parts.AddRange(a.Parts);
                foreach(var y in b.Parts)
                {
                    var next = new List<(double A,double B)>();
                    foreach (var x in parts)
                    {
                        if(y.B<=x.A || y.A>=x.B) { next.Add(x); continue; }
                        if(y.A>x.A) next.Add((x.A,y.A));
                        if(y.B<x.B) next.Add((y.B,x.B));
                    }
                    parts=next;
                }
            }
            return new Solid(parts);
        }
    }
}
