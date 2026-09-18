using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Autodesk.Revit.Attributes
{
    public enum TransactionMode { ReadOnly }
    public sealed class TransactionAttribute : Attribute { public TransactionAttribute(TransactionMode mode) { } }
}
namespace Autodesk.Revit.DB
{
    public sealed class ElementSet { }
    public enum BuiltInCategory { OST_GenericModel, OST_StructuralColumns, OST_StructuralFraming, OST_StructuralFoundation }
    public enum BuiltInParameter { SCHEDULE_LEVEL_PARAM, FAMILY_LEVEL_PARAM, INSTANCE_REFERENCE_LEVEL_PARAM }
    public enum StorageType { String, Integer, Double }
    public sealed class ElementId
    {
        public static readonly ElementId InvalidElementId = new ElementId(-1);
        public long Value;
        public ElementId(long value) { Value = value; }
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    public sealed class Category { public ElementId Id; }
    public sealed class Parameter
    {
        public bool HasValue = true;
        public StorageType StorageType;
        public object Value;
        public string AsString() => (string)Value;
        public double AsDouble() => (double)Value;
        public int AsInteger() => (int)Value;
        public ElementId AsElementId() => (ElementId)Value;
    }
    public class Element
    {
        public Document Document;
        public ElementId Id;
        public string Name = "Host";
        public Category Category;
        public ElementId LevelId = ElementId.InvalidElementId;
        public readonly Dictionary<string, Parameter> Parameters = new Dictionary<string, Parameter>();
        public ElementId GetTypeId() => ElementId.InvalidElementId;
        public Parameter LookupParameter(string name) => Parameters.TryGetValue(name, out var value) ? value : null;
        public Parameter get_Parameter(BuiltInParameter name) => null;
        public BoundingBoxXYZ get_BoundingBox(object view)
        {
            Document.BoundsCalls++;
            if (Document.ForbidBounds) throw new Exception("Unexpected geometry request");
            return new BoundingBoxXYZ();
        }
    }
    public sealed class DirectShape : Element { public string ApplicationId; public string ApplicationDataId; }
    public class Wall : Element { }
    public class Floor : Element { }
    public class FamilyInstance : Element { public Element Host; }
    public class Level : Element { public double Elevation; }
    public class View : Element { }
    public sealed class XYZ
    {
        public double X, Y, Z;
        public XYZ(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
    public sealed class Transform { public XYZ OfPoint(XYZ point) => point; }
    public sealed class BoundingBoxXYZ
    {
        public XYZ Min = new XYZ(0, 0, 0), Max = new XYZ(1, 1, 1);
        public Transform Transform = new Transform();
    }
    public sealed class Document
    {
        public readonly Dictionary<long, Element> Elements = new Dictionary<long, Element>();
        public bool ForbidBounds = true;
        public int BoundsCalls;
        public View ActiveView = new View { Name = "Test view" };
        public Element GetElement(ElementId id) => Elements.TryGetValue(id.Value, out var element) ? element : null;
        public void Add(Element element) { element.Document = this; Elements.Add(element.Id.Value, element); }
    }
    public sealed class FilteredElementCollector : IEnumerable<Element>
    {
        private IEnumerable<Element> _elements;
        public FilteredElementCollector(Document doc) { _elements = doc.Elements.Values; }
        public FilteredElementCollector OfClass(Type type) { _elements = _elements.Where(type.IsInstanceOfType); return this; }
        public FilteredElementCollector OfCategory(BuiltInCategory category)
        {
            _elements = _elements.Where(e => e.Category?.Id.Value == (long)category); return this;
        }
        public FilteredElementCollector WhereElementIsNotElementType() => this;
        public ICollection<ElementId> ToElementIds() => _elements.Select(e => e.Id).ToList();
        public IEnumerator<Element> GetEnumerator() => _elements.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace Autodesk.Revit.UI
{
    public enum Result { Succeeded, Cancelled, Failed }
    public interface IExternalCommand { Result Execute(ExternalCommandData data, ref string msg, Autodesk.Revit.DB.ElementSet set); }
    public sealed class ExternalCommandData { public UIApplication Application; }
    public sealed class UIApplication { public UIDocument ActiveUIDocument; public IntPtr MainWindowHandle; }
    public sealed class UIDocument { public Autodesk.Revit.DB.Document Document; }
    public enum TaskDialogCommonButtons { Cancel }
    public enum TaskDialogResult { CommandLink1, CommandLink2, Cancel }
    public enum TaskDialogCommandLinkId { CommandLink1, CommandLink2 }
    public sealed class TaskDialog
    {
        public string MainInstruction;
        public TaskDialogCommonButtons CommonButtons;
        public static int ShowCalls;
        public static int AddedLinks;
        private readonly HashSet<TaskDialogCommandLinkId> _links = new HashSet<TaskDialogCommandLinkId>();
        private TaskDialogResult _defaultButton;
        public TaskDialogResult DefaultButton
        {
            get => _defaultButton;
            set
            {
                if ((value == TaskDialogResult.CommandLink1 && !_links.Contains(TaskDialogCommandLinkId.CommandLink1))
                    || (value == TaskDialogResult.CommandLink2 && !_links.Contains(TaskDialogCommandLinkId.CommandLink2)))
                    throw new ArgumentException("Corresponding button not found.", "defaultButton");
                _defaultButton = value;
            }
        }
        public TaskDialog(string title) { }
        public void AddCommandLink(TaskDialogCommandLinkId id, string title, string text) { _links.Add(id); AddedLinks++; }
        public TaskDialogResult Show() { ShowCalls++; return TaskDialogResult.Cancel; }
        public static void Show(string title, string text) { }
    }
}
namespace YD_RevitTools.LicenseManager { public enum LicenseType { Professional } }
namespace YD_RevitTools.LicenseManager.Helpers
{
    public static class IdExtensions { public static long GetIdValue(this Autodesk.Revit.DB.ElementId id) => id.Value; }
}
namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    using Autodesk.Revit.DB;
    public static class LicenseHelper { public static bool CheckLicense(string key, string name, LicenseType license) => true; }
    public static class SharedParams { public const string P_HostId = "Host", P_EffectiveArea = "Area"; }
    public static class AreaCalculator { public static double ConvertToSquareMeters(double value) => value * 0.09290304; }
    public static class ElementCategorizer { public static bool IsStairs(Element element) => false; }
    public static class FormworkEngine
    {
        public static int Starts, Ends;
        public static void BeginRun() { Starts++; }
        public static void EndRun() { Ends++; }
        public static class Debug { public static void Enable(bool value) { } }
    }
    public static class CurvedMeshBudget
    {
        public static void StartRun(Action<string> callback) { }
        public static void EndRun() { }
        public static void ThrowIfExceeded() { }
    }
    public sealed class ProgressWindow : System.Windows.Window
    {
        public event Action CancelRequested;
        public void UpdateProgress(int current, int total, TimeSpan elapsed) { }
        public void UpdateStage(string stage, TimeSpan elapsed) { }
        public void ForceClose() { Close(); }
    }
    public enum StructuralElementType { Column, Beam, Slab, Wall, Foundation, Stair, Other }
    public sealed class ElementFormworkAnalysis
    {
        public StructuralElementType ElementType;
        public double ConcreteVolume, FormworkArea;
    }
    public sealed class StructuralAnalysisResult
    {
        public readonly Dictionary<Element, ElementFormworkAnalysis> ElementAnalyses = new Dictionary<Element, ElementFormworkAnalysis>();
        public int TotalElements;
        public double TotalConcreteVolume, EstimatedRebarWeight;
    }
    public static class StructuralFormworkAnalyzer
    {
        public static int Calls;
        public static bool Fail;
        public static StructuralAnalysisResult AnalyzeProject(Document doc)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("Analysis failure");
            return new StructuralAnalysisResult();
        }
    }
}
