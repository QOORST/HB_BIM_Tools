using System;
using System.Linq;
using System.Collections.Generic;
using YD_RevitTools.LicenseManager.Commands.MEP;

internal static class RevisionChecks
{
    private static SleeveNumberItem N(string id, string mark) => new SleeveNumberItem { Id = id, Mark = mark };
    private static SleeveRevisionRecord R(string id, bool included = true) => new SleeveRevisionRecord { Id = id, Mark = "PS-" + id,
        Included = included, Control = included ? "納入" : "排除", PositionMm = new[] {1.0,2.0,3.0}, Length = "200", Specification = "Ø114.3" };
    private static SleeveRevisionSnapshot S(params SleeveRevisionRecord[] rows) => new SleeveRevisionSnapshot { Name = "A", Records = rows.ToList() };
    internal static void Run(Action<bool,string> check)
    {
        var first = SleeveNumberPolicy.Plan(new[] {N("a","PS-001"), N("z","PS-020"), N("b","")}, new[] {"a","b"}, new SleeveNumberLedger());
        check(!first.Writes.ContainsKey("a") && first.Writes["b"] == "PS-021", "preserve old mark and reserve numbers outside scope");
        var second = SleeveNumberPolicy.Plan(new[] {N("a","PS-001"), N("b","PS-021")}, new[] {"a","b"}, first.Ledger);
        check(second.Writes.Count == 0, "rerun idempotent");
        var next = SleeveNumberPolicy.Plan(new[] {N("a","PS-001"),N("c","")},new[] {"c"},second.Ledger);
        check(next.Writes["c"] == "PS-022", "deleted high number never recycled");
        var copy = SleeveNumberPolicy.Plan(new[] {N("a","PS-001"),N("copy","PS-001")},new[] {"a","copy"},next.Ledger);
        check(copy.Writes.Count == 1 && copy.Writes["copy"] == "PS-023", "registered original preserved when duplicated");
        var restored = SleeveNumberPolicy.Plan(new[] {N("a","")},new[] {"a"},copy.Ledger);
        check(restored.Writes["a"] == "PS-001", "cleared mark restored");
        var custom = SleeveNumberPolicy.Plan(new[] {N("a","A棟-01")},new[] {"a"},new SleeveNumberLedger());
        check(custom.Writes.Count == 0 && custom.Ledger.Assigned["a"] == "A棟-01", "custom mark retained");
        bool conflict = false;
        try { SleeveNumberPolicy.Plan(new[] {N("a","PS-001"),N("b","PS-1")},new[] {"a","b"},new SleeveNumberLedger()); }
        catch (InvalidOperationException) { conflict = true; }
        check(conflict, "unknown originals with canonical duplicate abort");
        conflict = false;
        try { SleeveNumberPolicy.Plan(new[] {N("a","PS-021"),N("b","PS-021")},new[] {"a"},second.Ledger); }
        catch (InvalidOperationException) { conflict = true; }
        check(conflict && second.Ledger.Assigned["a"] == "PS-001", "manual conflict abort without mutating saved ledger");
        var changed = SleeveNumberPolicy.Plan(new[] {N("a","CUSTOM")},new[] {"a"},second.Ledger);
        check(changed.Writes.Count == 0 && changed.Ledger.Owners.ContainsKey("PS-1"), "manual unique rename retains tombstone");
        conflict = false;
        try { SleeveNumberPolicy.Plan(new[] {N("a","")},new[] {"a"},new SleeveNumberLedger {LastNumber=long.MaxValue}); }
        catch (OverflowException) { conflict = true; }
        check(conflict, "number overflow aborts safely");
        var defaults = new[] {N("old","1"), N("n1","1"), N("n2","1")};
        defaults[1].CreatedInOperation = defaults[2].CreatedInOperation = true;
        var created = SleeveNumberPolicy.Plan(defaults, new[] {"old","n1","n2"}, new SleeveNumberLedger());
        check(created.Writes.Count == 2 && created.Writes["n1"] == "PS-001" && created.Writes["n2"] == "PS-002" &&
            created.Ledger.Assigned["old"] == "1", "new family defaults replaced while existing custom mark preserved");
        var psDefault = N("new","PS-999"); psDefault.CreatedInOperation = true;
        check(SleeveNumberPolicy.Plan(new[]{N("old","PS-010"),psDefault},new[]{"new"},new SleeveNumberLedger()).Writes["new"] == "PS-011",
            "new family PS default does not inflate number ledger");
        check(defaults[1].Mark == "1", "planning leaves source parameter values untouched");
        conflict=false; var wrong=N("a","1"); wrong.CreatedInOperation=true;
        try {SleeveNumberPolicy.Plan(new[]{wrong},new[]{"a"},second.Ledger);}catch(InvalidOperationException){conflict=true;}
        check(conflict,"registered original cannot be mislabeled new to bypass preservation");
        conflict=false; try {SleeveNumberPolicy.Plan(defaults,new[]{"old"},new SleeveNumberLedger());}catch(InvalidOperationException){conflict=true;}
        check(conflict,"new provenance outside target scope rejected");
        conflict=false; try {SleeveNumberPolicy.Plan(new[]{N("x","1"),N("y","1")},new[]{"x","y"},new SleeveNumberLedger());}catch(InvalidOperationException){conflict=true;}
        check(conflict,"genuine pre-existing duplicate 1 still protected");

        check(SleeveRevisionPolicy.Compare(S(R("1")),S(R("1"))).Count == 0, "unchanged snapshot no delta");
        var delta = SleeveRevisionPolicy.Compare(S(R("1")), S(R("2")));
        check(delta.Count == 2 && delta.Sum(d => d.QuantityDelta) == 0 && delta.Any(d => d.Changes == "刪除"), "delete plus add not mistaken for move");
        var moved = R("1"); moved.PositionMm[0] += 0.49;
        check(SleeveRevisionPolicy.Compare(S(R("1")),S(moved)).Count == 0, "submillimetre jitter ignored");
        moved.PositionMm[1] += 0.49;
        check(SleeveRevisionPolicy.Compare(S(R("1")),S(moved)).Single().Changes == "移動", "Euclidean move tolerance");
        var resized = R("1"); resized.Length = "300"; resized.Material = "PVC"; resized.Mark = "PS-100";
        delta = SleeveRevisionPolicy.Compare(S(R("1")),S(resized));
        check(delta.Single().QuantityDelta == 0 && delta.Single().Changes.Contains("尺寸") && delta.Single().Changes.Contains("材質") && delta.Single().Changes.Contains("編號"), "concurrent changes on permanent identity counted once");
        var voided = R("1", false); voided.Control = "作廢";
        check(SleeveRevisionPolicy.Compare(S(R("1")),S(voided)).Single().QuantityDelta == -1, "void removes one included quantity");
        check(SleeveRevisionPolicy.Compare(S(voided),S()).Single().QuantityDelta == 0, "deleting voided item doesn't remove count twice");
        check(SleeveRevisionPolicy.Compare(S(voided),S(R("1"))).Single().Changes.Contains("恢復納入"), "reinstate recognized");
        var lost = R("1",false); lost.Recognized = false;
        check(SleeveRevisionPolicy.Compare(S(R("1")),S(lost)).Single().Changes.Contains("失去套管辨識"), "lost enrollment distinguished from deletion");
        var rotated = R("1"); rotated.Orientation = new[] {1.0,0,0,0,1,0,0,0,1};
        check(SleeveRevisionPolicy.Compare(S(R("1")),S(rotated)).Single().Changes == "方向", "orientation change detected");
        conflict = false; try { SleeveRevisionPolicy.Compare(S(R("1"),R("1")),S()); } catch (InvalidOperationException) { conflict = true; }
        check(conflict, "duplicated snapshot identity rejected");
        conflict = false; try { SleeveRevisionPolicy.Compare(new SleeveRevisionSnapshot {Version=99},S()); } catch (InvalidOperationException) { conflict = true; }
        check(conflict, "unknown schema version rejected");
        var invalid = R("1"); invalid.PositionMm[0] = double.NaN;
        conflict = false; try { SleeveRevisionPolicy.Compare(S(invalid),S()); } catch (InvalidOperationException) { conflict = true; }
        check(conflict, "invalid coordinate rejected");
        check(SleeveRevisionPolicy.CsvCell(" =SUM(A1)").StartsWith("\"'"), "CSV formula injection neutralized");
        check(SleeveRevisionPolicy.CsvCell("a,\"b\"\nc") == "\"a,\"\"b\"\"\nc\"", "CSV quotes delimiters and newlines");
        check(SleeveRevisionPolicy.Csv(S(R("1")),S(R("2"))).Contains("基準方向"), "CSV includes full change details");
    }
}
