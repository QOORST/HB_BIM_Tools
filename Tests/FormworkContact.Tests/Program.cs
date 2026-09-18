using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;

class Program
{
    static Element E(BuiltInCategory category, double volume, bool structural = true) =>
        new Element { Category = new Category { Id = new ElementId((long)category), Name = category.ToString() },
            Structural = structural, Solids = new List<Solid> { new Solid(volume) } };
    static int count;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    static Solid Run(Element host, params Element[] cutters) => GeometryExtractor.ApplySmartContactDeduction(new Solid(100), new List<Element>(cutters), 0.05, host);
    static void Main()
    {
        var wall = E(BuiltInCategory.OST_Walls, 100);
        var stair = E(BuiltInCategory.OST_Stairs, 1);
        foreach (var cat in new[] { BuiltInCategory.OST_Stairs, BuiltInCategory.OST_StairsRuns, BuiltInCategory.OST_StairsLandings, BuiltInCategory.OST_StairsSupports })
            Check(Math.Abs(Run(wall, E(cat, 1)).Volume - 99) < 1e-9, "wall deducts small contact " + cat);
        Check(Run(stair, E(BuiltInCategory.OST_Walls, 1)).Volume == 99, "stair deducts structural wall");
        Check(Run(stair, E(BuiltInCategory.OST_Walls, 10, false)).Volume == 100, "nonstructural partition excluded");
        Check(Run(wall, E(BuiltInCategory.OST_Stairs, 95)).Volume == 5, "small wall remnant retained");
        Check(Run(wall, E(BuiltInCategory.OST_Stairs, 100)) == null, "fully covered wall removed");
        Check(Run(wall, E(BuiltInCategory.OST_Stairs, 0)).Volume == 100, "no intersection unchanged");
        Check(Run(wall, E(BuiltInCategory.OST_StructuralColumns, 1)).Volume == 100, "unrelated threshold unchanged");
        Check(Run(wall).Volume == 100, "no neighbors unchanged");
        Check(Run(stair, E(BuiltInCategory.OST_Walls, 95)).Volume == 5, "small stair remnant retained");
        Console.WriteLine($"{count} contact-control tests passed; Boolean geometry is simulated, not native Revit.");
    }
}
namespace Autodesk.Revit.DB
{
    public enum BuiltInCategory { OST_Walls, OST_Stairs, OST_StairsRuns, OST_StairsLandings, OST_StairsSupports, OST_StructuralColumns, OST_StructuralFraming, OST_Floors }
    public class ElementId { public long Value; public ElementId(long value) { Value = value; } }
    public class Category { public ElementId Id; public string Name; }
    public class Element { public ElementId Id = new ElementId(1); public Category Category; public bool Structural; public List<Solid> Solids; }
    public class Solid { public double Volume; public Solid(double volume) { Volume = volume; } }
    public enum BooleanOperationsType { Intersect, Difference }
    public static class BooleanOperationsUtils
    {
        public static Solid ExecuteBooleanOperation(Solid a, Solid b, BooleanOperationsType op) =>
            new Solid(op == BooleanOperationsType.Intersect ? Math.Min(a.Volume,b.Volume) : Math.Max(0,a.Volume-b.Volume));
    }
}
namespace YD_RevitTools.LicenseManager.Helpers
{
    public static class IdExtensions { public static long GetIdValue(this ElementId id) => id.Value; }
}
namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    public static class CurvedMeshBudget { public static void Checkpoint(string stage) {} }
    public static class ElementCategorizer
    {
        public static bool IsStairs(Element e) => e != null && e.Category.Id.Value >= (long)BuiltInCategory.OST_Stairs && e.Category.Id.Value <= (long)BuiltInCategory.OST_StairsSupports;
        public static bool CanDeductFormwork(Element e) => e.Category.Id.Value != (long)BuiltInCategory.OST_Walls || e.Structural;
    }
}
