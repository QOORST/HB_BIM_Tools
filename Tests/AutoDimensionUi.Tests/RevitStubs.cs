using System;
using System.Collections.Generic;
namespace Autodesk.Revit.DB {
    public sealed class ElementId { public int Value; public ElementId(int value) { Value = value; } }
    public static class UnitTypeId { public static readonly object Millimeters = new object(); }
    public static class UnitUtils {
        public static double ConvertFromInternalUnits(double value, object unit) => value * 304.8;
        public static double ConvertToInternalUnits(double value, object unit) => value / 304.8;
    }
    public class XYZ {
        public double X, Y, Z; public XYZ(double x, double y, double z=0) { X=x; Y=y; Z=z; }
        public static XYZ operator -(XYZ a, XYZ b) => new XYZ(a.X-b.X,a.Y-b.Y);
        public double DotProduct(XYZ other) => X*other.X + Y*other.Y;
        public XYZ Negate() => new XYZ(-X,-Y);
    }
    public abstract class Curve { public abstract XYZ GetEndPoint(int i); }
    public class Line : Curve {
        public XYZ A, B; public Line(XYZ a, XYZ b) { A=a; B=b; }
        public override XYZ GetEndPoint(int i) => i == 0 ? A : B;
    }
    public class View { public bool IsTemplate; public XYZ RightDirection = new XYZ(1,0), UpDirection = new XYZ(0,1); }
    public class ViewPlan : View { }
    public enum DatumEnds { End0, End1 }
    public enum DatumExtentType { ViewSpecific }
    public class Leader { public XYZ End = new XYZ(0,0); }
    public class Grid {
        public bool ReverseDatumEnds;
        public Leader?[] Leaders = new Leader?[2];
        public Leader? GetLeader(DatumEnds end, View v) => Leaders[(int)end];
        public Leader AddLeader(DatumEnds end, View v) { var leader=new Leader { End=Curve.GetEndPoint(ReverseDatumEnds ? 1-(int)end : (int)end) }; Leaders[(int)end]=leader; return leader; }
        public Curve Curve = new Line(new XYZ(0,0),new XYZ(1,0));
        public Curve? ViewCurve;
        public bool[] Visible = new bool[2];
        public bool CanBeVisibleInView(View v) => true;
        public List<Curve> GetCurvesInView(DatumExtentType type, View v) => new List<Curve>{ViewCurve ?? Curve};
        public void ShowBubbleInView(DatumEnds end, View v) => Visible[(int)end] = true;
        public void HideBubbleInView(DatumEnds end, View v) => Visible[(int)end] = false;
        public bool IsBubbleVisibleInView(DatumEnds end, View v) => Visible[(int)end];
    }
    public class ProjectInfo { public string UniqueId = "test-project"; }
    public class Document { public void Regenerate() { } public ProjectInfo ProjectInformation = new ProjectInfo(); public string PathName = "test.rvt", Title = "test"; public Dictionary<int, Grid> Grids = new Dictionary<int, Grid>(); public object GetElement(ElementId id) => Grids[id.Value]; }
    public enum TransactionStatus { Started, Committed, RolledBack }
    public class SubTransaction : IDisposable {
        private TransactionStatus status;
        private Document doc;
        private Dictionary<int,(bool[],Leader?[])> snapshot = new Dictionary<int,(bool[],Leader?[])>();
        public SubTransaction(Document d) { doc=d; }
        public void Start() { status = TransactionStatus.Started; foreach(var pair in doc.Grids) snapshot[pair.Key]=((bool[])pair.Value.Visible.Clone(),(Leader?[])pair.Value.Leaders.Clone()); }
        public TransactionStatus Commit() => status = TransactionStatus.Committed;
        public TransactionStatus GetStatus() => status;
        public void RollBack() { foreach(var pair in snapshot) { doc.Grids[pair.Key].Visible=pair.Value.Item1; doc.Grids[pair.Key].Leaders=pair.Value.Item2; } status = TransactionStatus.RolledBack; }
        public void Dispose() { }
    }
}
namespace YDBIM.AutoDimension.Core {
    internal static class ElementIdCompat { public static int ToInt32(Autodesk.Revit.DB.ElementId id) => id.Value; }
    internal sealed class GridSelectionItem {
        public Autodesk.Revit.DB.ElementId Id { get; }
        public string Label { get; }
        public GridSelectionItem(Autodesk.Revit.DB.ElementId id, string label, double axis) { Id=id; Label=label; }
    }
}
