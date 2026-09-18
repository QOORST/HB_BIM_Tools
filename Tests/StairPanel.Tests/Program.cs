using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;
using Panel = YD_RevitTools.LicenseManager.Commands.AR.Formwork.StairPanelMerger.Panel;

// API doubles test orchestration, not Revit's native geometry kernel.
class Program
{
    static int passed;
    static Panel P(double x = 0, double plane = 0, XYZ normal = null) =>
        new Panel(new Solid(x, x + 1), new XYZ(plane, 0, 0), normal ?? new XYZ(1, 0, 0));
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        Console.WriteLine("PASS " + name);
        passed++;
    }
    static void Main()
    {
        Check(StairPanelMerger.SameSidePlane(P(), P(1)), "same side plane");
        Check(!StairPanelMerger.SameSidePlane(P(), P(plane: 0.1)), "different riser planes");
        Check(!StairPanelMerger.SameSidePlane(P(), P(normal: new XYZ(-1, 0, 0))), "opposite sides");
        Check(!StairPanelMerger.SameSidePlane(P(normal: new XYZ(0, 0, -1)), P(normal: new XYZ(0, 0, -1))), "soffits not merged as sides");
        Check(StairPanelMerger.Merge(new List<Panel>()).Count == 0, "empty input");
        Check(StairPanelMerger.Merge(new[] { P() }).Count == 1, "single panel");
        Check(StairPanelMerger.Merge(new[] { P(), P(1) }).Count == 1, "connected fragments");
        Check(StairPanelMerger.Merge(new[] { P(), P(2), P(1) }).Count == 1, "bridge revisits earlier fragments");
        BooleanOperationsUtils.Calls = 0;
        Check(StairPanelMerger.Merge(new[] { P(), P(3) }).Count == 2 && BooleanOperationsUtils.Calls == 0, "bbox skips disconnected fragments");
        BooleanOperationsUtils.Mode = "split";
        Check(StairPanelMerger.Merge(new[] { P(), P(1) }).Count == 2, "disconnected union rejected");
        BooleanOperationsUtils.Mode = "point";
        Check(StairPanelMerger.Merge(new[] { P(), P(1) }).Count == 2, "point-only contact rejected");
        BooleanOperationsUtils.Mode = "oversize";
        Check(StairPanelMerger.Merge(new[] { P(), P(1) }).Count == 2, "volume gain rejected");
        BooleanOperationsUtils.Mode = "throw";
        var first = P(); var second = P(1);
        var result = StairPanelMerger.Merge(new[] { first, second });
        Check(result.Count == 2 && result[0] == first && result[1] == second, "boolean failure preserves originals");
        var many = new List<Panel>();
        for (int i = 0; i < 30; i++) many.Add(P());
        BooleanOperationsUtils.Calls = 0;
        Check(StairPanelMerger.Merge(many).Count == 30 && BooleanOperationsUtils.Calls == 256, "union budget preserves remaining panels");
        CurvedMeshBudget.Cancel = true;
        bool cancelled = false;
        try { StairPanelMerger.Merge(new[] { P() }); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancellation escapes merger");
        Console.WriteLine($"{passed} policy checks passed; native Revit geometry not executed.");
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
    public class XYZ
    {
        public double X, Y, Z;
        public XYZ(double x, double y, double z) { X = x; Y = y; Z = z; }
        public XYZ Subtract(XYZ b) => new XYZ(X-b.X, Y-b.Y, Z-b.Z);
        public double DotProduct(XYZ b) => X*b.X + Y*b.Y + Z*b.Z;
        public double GetLength() => Math.Sqrt(DotProduct(this));
    }
    public class Transform { public XYZ OfPoint(XYZ p) => p; }
    public class BoundingBoxXYZ
    {
        public XYZ Min, Max;
        public Transform Transform = new Transform();
    }
    public class Solid
    {
        public double Min, Max, Volume, SurfaceArea;
        public int Parts = 1;
        public Solid(double min, double max)
        { Min = min; Max = max; Volume = max-min; SurfaceArea = 4*(max-min)+2; }
        public BoundingBoxXYZ GetBoundingBox() => new BoundingBoxXYZ { Min = new XYZ(Min, 0, 0), Max = new XYZ(Max, 1, 1) };
    }
    public enum BooleanOperationsType { Union }
    public static class SolidUtils
    {
        public static IList<Solid> SplitVolumes(Solid s) => s.Parts == 1 ? new[] { s } : new[] { s, s };
    }
    public static class BooleanOperationsUtils
    {
        public static string Mode = "normal";
        public static int Calls;
        public static Solid ExecuteBooleanOperation(Solid a, Solid b, BooleanOperationsType op)
        {
            Calls++;
            if (Mode == "throw") throw new InvalidOperationException("simulated kernel failure");
            var s = new Solid(Math.Min(a.Min,b.Min),Math.Max(a.Max,b.Max));
            if (Mode == "split") s.Parts = 2;
            if (Mode == "point") s.SurfaceArea = a.SurfaceArea+b.SurfaceArea;
            if (Mode == "oversize") s.Volume = a.Volume+b.Volume+1;
            return s;
        }
    }
}
