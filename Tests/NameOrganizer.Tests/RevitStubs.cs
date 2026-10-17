// Test doubles for failure injection, not a simulation of Revit geometry or native transaction validation.
using System.Collections;
namespace Autodesk.Revit.DB
{
    public sealed class ElementId : IEquatable<ElementId>
    {
        public long Value; public ElementId(long value) { Value = value; }
        public static readonly ElementId InvalidElementId = new(-1);
        public bool Equals(ElementId other) => other != null && Value == other.Value;
        public override bool Equals(object obj) => Equals(obj as ElementId);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ElementId a, ElementId b) => ReferenceEquals(a, b) || a is not null && a.Equals(b);
        public static bool operator !=(ElementId a, ElementId b) => !(a == b);
    }
    public class Category { public ElementId Id = new(1); public string Name = "設備"; }
    public class Element
    {
        public ElementId Id; public string UniqueId; public Category Category = new(); public Document Document;
        internal string RawName; public string FailName; public ElementId TypeId = ElementId.InvalidElementId;
        public virtual string Name
        {
            get => RawName;
            set { if (value == FailName) throw new InvalidOperationException("Injected setter failure");
                if (this is Material && Document?.Elements.Any(e => e != this && e is Material && e.Name == value) == true) throw new InvalidOperationException("Duplicate material"); RawName = value; }
        }
        public ElementId GetTypeId() => TypeId;
    }
    public class ElementType : Element { public string FamilyName = "系統"; }
    public class DimensionType : ElementType { }
    public class Family : Element { public Category FamilyCategory = new(); }
    public class FamilySymbol : ElementType { public Family Family; }
    public class Material : Element { public string MaterialCategory = "一般"; }
    public class FilterElement : Element { }
    public class ParameterFilterElement : FilterElement { }
    public enum ViewType { FloorPlan, ProjectBrowser, SystemBrowser, Internal }
    public class View : Element
    {
        public bool IsTemplate; public ViewType ViewType; public ElementId ViewTemplateId = ElementId.InvalidElementId;
        public List<ElementId> Filters = new(); public bool FilterReadFailed;
        public bool AreGraphicsOverridesAllowed() => true;
        public ICollection<ElementId> GetFilters() => FilterReadFailed ? throw new InvalidOperationException("filter error") : Filters;
    }
    public class ViewSheet : View { public string SheetNumber = "A01"; public List<ElementId> Placed = new(); public ICollection<ElementId> GetAllPlacedViews() => Placed; }
    public class ViewSchedule : View { public bool IsTitleblockRevisionSchedule; }
    public class ScheduleSheetInstance : Element { public bool IsTitleblockRevisionSchedule; public ElementId OwnerViewId; public ElementId ScheduleId; }
    public class Document
    {
        public List<Element> Elements = new(); public List<ElementId> Cascade = new(); public TransactionStatus NextCommit = TransactionStatus.Committed;
        public int Commits, Rollbacks, DeleteCalls; public bool FailDelete;
        public T Add<T>(T e, long id, string name) where T : Element { e.Id = new(id); e.UniqueId = "uid" + id; e.RawName = name; e.Document = this; Elements.Add(e); return e; }
        public Element GetElement(ElementId id) => Elements.FirstOrDefault(e => e.Id == id);
        public Element GetElement(string id) => Elements.FirstOrDefault(e => e.UniqueId == id);
        public ICollection<ElementId> Delete(ICollection<ElementId> ids)
        {
            DeleteCalls++; var all = ids.Concat(Cascade).Distinct().ToList(); Elements.RemoveAll(e => all.Contains(e.Id));
            if (FailDelete) throw new InvalidOperationException("Injected partial delete failure"); return all;
        }
    }
    public class FilteredElementCollector : IEnumerable<Element>
    {
        private IEnumerable<Element> _elements; public FilteredElementCollector(Document doc) { _elements = doc.Elements; }
        public FilteredElementCollector WhereElementIsNotElementType() { _elements = _elements.Where(e => e is not ElementType); return this; }
        public FilteredElementCollector WhereElementIsElementType() { _elements = _elements.Where(e => e is ElementType); return this; }
        public IEnumerator<Element> GetEnumerator() => _elements.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public enum TransactionStatus { Uninitialized, Started, Committed, RolledBack, Pending }
    public class Transaction : IDisposable
    {
        private readonly Document _doc; private TransactionStatus _status; private List<Element> _before; private Dictionary<Element, string> _names;
        public Transaction(Document doc, string name) { _doc = doc; }
        public void Start() { _before = _doc.Elements.ToList(); _names = _before.ToDictionary(e => e, e => e.RawName); _status = TransactionStatus.Started; }
        public TransactionStatus GetStatus() => _status;
        public FailureHandlingOptions GetFailureHandlingOptions() => new(); public void SetFailureHandlingOptions(FailureHandlingOptions options) { }
        public TransactionStatus RollBack() { _doc.Elements = _before.ToList(); foreach (var p in _names) p.Key.RawName = p.Value; _doc.Rollbacks++; return _status = TransactionStatus.RolledBack; }
        public TransactionStatus Commit() { _doc.Commits++; if (_doc.NextCommit != TransactionStatus.Committed) { RollBack(); return _doc.NextCommit; } return _status = TransactionStatus.Committed; }
        public void Dispose() { if (_status == TransactionStatus.Started) RollBack(); }
    }
    public class FailureHandlingOptions
    {
        public FailureHandlingOptions SetFailuresPreprocessor(IFailuresPreprocessor handler) => this;
        public FailureHandlingOptions SetClearAfterRollback(bool value) => this;
        public FailureHandlingOptions SetForcedModalHandling(bool value) => this;
    }
    public interface IFailuresPreprocessor { FailureProcessingResult PreprocessFailures(FailuresAccessor accessor); }
    public enum FailureProcessingResult { Continue, ProceedWithRollBack }
    public enum FailureSeverity { Warning, Error }
    public class FailureMessageAccessor { public FailureSeverity GetSeverity() => FailureSeverity.Error; }
    public class FailuresAccessor { public List<FailureMessageAccessor> GetFailureMessages() => new(); }
}
namespace YD_RevitTools.LicenseManager.Helpers
{ internal static class RevitApiCompatibility { public static long GetIdValue(this Autodesk.Revit.DB.ElementId id) => id.Value; } }
