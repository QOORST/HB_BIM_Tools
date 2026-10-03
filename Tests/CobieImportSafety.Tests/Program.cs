using System;
using System.Linq;
using YD_RevitTools.LicenseManager.Helpers.Data;

internal static class Program
{
    private static int checks;
    private static void Main()
    {
        var first = Id("uid-a", "1", "DUP", "Family", "Type");
        var second = Id("uid-b", "2", "DUP", "Other", "OtherType");
        var third = Id("uid-c", "3", "UNIQUE", "Family", "Type");
        var matcher = new CobieImportMatcher(new[] { first, second, third });
        Match(matcher, "UniqueId wins with no other assertions", Id("uid-a"), "uid-a");
        Match(matcher, "all identifiers agree", Id("uid-c", "3", "UNIQUE", "Family", "Type"), "uid-c");
        Match(matcher, "stale UniqueId never falls back", Id("stale", "3", "UNIQUE"), null);
        Match(matcher, "conflicting numeric ID", Id("uid-a", "3"), null);
        Match(matcher, "conflicting mark", Id("uid-a", mark: "UNIQUE"), null);
        Match(matcher, "conflicting family", Id("uid-a", family: "Other"), null);
        Match(matcher, "conflicting type", Id("uid-a", type: "OtherType"), null);
        Match(matcher, "document local ID alone rejected", Id(id: "3"), null);
        Match(matcher, "numeric ID with mark", Id(id: "3", mark: "UNIQUE"), "uid-c");
        Match(matcher, "numeric ID with family and type", Id(id: "3", family: "Family", type: "Type"), "uid-c");
        Match(matcher, "numeric ID with family alone rejected", Id(id: "3", family: "Family"), null);
        Match(matcher, "stale numeric ID never falls back", Id(id: "404", mark: "UNIQUE"), null);
        Match(matcher, "duplicate Mark rejected", Id(mark: "DUP"), null);
        Match(matcher, "duplicate Mark remains rejected with family", Id(mark: "DUP", family: "Family"), null);
        Match(matcher, "unique Mark supported", Id(mark: "UNIQUE"), "uid-c");
        Match(matcher, "missing identity", Id(), null);
        Match(matcher, "trim and case", Id(" UID-C ", "3", " unique "), "uid-c");
        Match(matcher, "64-bit IDs", Id(id: "4294967296", mark: "UNIQUE"), null);
        var big = new CobieImportMatcher(new[] { Id("big", "4294967296", "BIG") });
        Match(big, "64-bit ID supported without truncation", Id(id: "4294967296", mark: "BIG"), "big");
        var duplicates = new CobieImportMatcher(new[] { first, first });
        Match(duplicates, "even duplicate unique IDs fail closed", Id("uid-a"), null);

        var writes = new[] { Write("instance:1", "same"), Write("instance:1", "same"),
            Write("type:2", "a"), Write("type:2", "b"), Write("instance:3", 3), Write("instance:4", 4) };
        var safe = CobieImportWritePlan.Resolve(writes, w => w.Item1, w => w.Item2, out var conflicts, out var count);
        Assert("same-value duplicates collapsed", count == 1 && safe.Count == 3);
        Assert("all conflicting type writes excluded", conflicts.Count == 2 && safe.All(w => w.Item1 != "type:2"));
        Assert("unrelated writes preserved", safe.Any(w => w.Item1 == "instance:3"));
        safe = CobieImportWritePlan.Resolve(new Tuple<string, object>[0], w => w.Item1, w => w.Item2, out conflicts, out count);
        Assert("empty plan", safe.Count == 0 && conflicts.Count == 0 && count == 0);
        Console.WriteLine($"All {checks} COBie import safety checks passed.");
    }

    private static Tuple<string, object> Write(string target, object value) => Tuple.Create(target, value);
    private static CobieImportIdentity Id(string uid = null, string id = null, string mark = null, string family = null, string type = null)
        => new CobieImportIdentity { UniqueId = uid, ElementId = id, Mark = mark, FamilyName = family, TypeName = type };
    private static void Match(CobieImportMatcher matcher, string name, CobieImportIdentity input, string expected)
    {
        var actual = matcher.Match(input, out var reason);
        Assert(name, actual?.UniqueId == expected && (expected != null || !string.IsNullOrWhiteSpace(reason)));
    }
    private static void Assert(string name, bool condition)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(name);
    }
}
