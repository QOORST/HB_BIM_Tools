using Autodesk.Revit.DB;
using System.Xml.Linq;
using YD_RevitTools.LicenseManager.Commands.AR.AutoJoin;

internal static class Program
{
    private static int passed, failed;
    private static TypeJoinRule Rule(string a = "A", string b = "B", string familyA = "Basic", string familyB = "Basic", string category = "Wall")
        => new() { CategoryKey = category, WinnerFamilyName = familyA, WinnerTypeName = a, LoserFamilyName = familyB, LoserTypeName = b };
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected InvalidOperationException");
    }
    private static void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex}"); }
    }
    private static void FileTest(Action<string> action)
    {
        string dir = Path.Combine(Path.GetTempPath(), "AutoJoinTypeRules.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { action(Path.Combine(dir, "settings.xml")); }
        finally { Directory.Delete(dir, true); }
    }
    private static (Document Doc, ClassifiedElement A, ClassifiedElement B, AutoJoinSettings Settings) Fixture()
    {
        var doc = new Document();
        var a = new Element { Id = new(1), TypeId = new(101) };
        var b = new Element { Id = new(2), TypeId = new(102) };
        doc.Elements[a.TypeId] = new ElementType { Id = a.TypeId, FamilyName = "Basic", Name = "A" };
        doc.Elements[b.TypeId] = new ElementType { Id = b.TypeId, FamilyName = "Basic", Name = "B" };
        return (doc, new(a, new("Wall")), new(b, new("Wall")), new() { UseSameCategoryTypeRules = true, SameCategoryTypeRules = [Rule()] });
    }
    public static int Main()
    {
        TypeJoinRule ContainsRule() => new() { CategoryKey = "StructuralFraming", WinnerFamilyName = "Drain", WinnerTypeName = "Channel",
            LoserMatchMode = TypeJoinMatchMode.Contains, LoserTypeName = "增築" };
        Test("Contains matches existing and future sizes, both directions", () => {
            var p = new TypeJoinPolicy([ContainsRule()]);
            foreach (var name in new[] { "RC 梁側增築_20*30", "RC 梁頂/底增築_180*10", "新尺寸增築_99*20" }) {
                Equal(-1, p.Resolve("StructuralFraming", "Drain", "Channel", "Beam", name));
                Equal(1, p.Resolve("StructuralFraming", "Beam", name, "Drain", "Channel"));
            }
            Equal(0, p.Resolve("StructuralFraming", "Drain", "Channel", "Beam", "RC 梁_50*80"));
            Equal(0, p.Resolve("Wall", "Drain", "Channel", "Beam", "梁增築"));
        });
        Test("Contains optional family is exact, not fuzzy", () => {
            var r = ContainsRule(); r.LoserFamilyName = "Beam";
            var p = new TypeJoinPolicy([r]);
            Equal(-1, p.Resolve("StructuralFraming", "Drain", "Channel", "Beam", "增築"));
            Equal(0, p.Resolve("StructuralFraming", "Drain", "Channel", "OtherBeam", "增築"));
        });
        Test("Contains is literal, trims keyword and ignores Latin case", () => {
            Equal(true, TypeJoinPolicy.Matches(TypeJoinMatchMode.Contains, "", " RC_ ", "Beam", "rc_50"));
            Equal(false, TypeJoinPolicy.Matches(TypeJoinMatchMode.Contains, "", "*", "Beam", "rc_50"));
            Equal(false, TypeJoinPolicy.Matches(TypeJoinMatchMode.Contains, "", " ", "Beam", "rc_50"));
        });
        Test("Both sides can contain with disjoint family filters", () => {
            var r = ContainsRule(); r.WinnerMatchMode = TypeJoinMatchMode.Contains; r.WinnerTypeName = "Chan"; r.LoserFamilyName = "Beam";
            Equal(-1, new TypeJoinPolicy([r]).Resolve("StructuralFraming", "Drain", "Channel new", "Beam", "新增增築"));
        });
        Test("Blank contains and invalid modes rejected", () => {
            var r = ContainsRule(); r.LoserTypeName = " "; Reject(() => TypeJoinPolicy.Validate([r]));
            r.LoserTypeName = "增築"; r.LoserMatchMode = (TypeJoinMatchMode)99; Reject(() => TypeJoinPolicy.Validate([r]));
        });
        Test("Overlapping same-rule predicates rejected conservatively", () => {
            var r = ContainsRule(); r.WinnerTypeName = "水溝增築"; Reject(() => TypeJoinPolicy.Validate([r]));
            r.WinnerMatchMode = TypeJoinMatchMode.Contains; r.WinnerTypeName = "水溝"; Reject(() => TypeJoinPolicy.Validate([r]));
        });
        Test("Contains reverse and three-rule cycle rejected", () => {
            var r = ContainsRule();
            var reverse = Rule("梁增築", "Channel", "Beam", "Drain", "StructuralFraming");
            Reject(() => TypeJoinPolicy.Validate([r, reverse]));
            var second = Rule("梁增築", "C", "Beam", "Other", "StructuralFraming");
            var third = Rule("C", "Channel", "Other", "Drain", "StructuralFraming");
            Reject(() => TypeJoinPolicy.Validate([r, second, third]));
        });
        Test("Contains rules are snapshotted and do not imply transitive pairs", () => {
            var r = ContainsRule();
            var next = Rule("梁增築", "C", "Beam", "Other", "StructuralFraming");
            var p = new TypeJoinPolicy([r, next]); r.LoserTypeName = "Changed";
            Equal(-1, p.Resolve("StructuralFraming", "Drain", "Channel", "Beam", "梁增築"));
            Equal(0, p.Resolve("StructuralFraming", "Drain", "Channel", "Other", "C"));
        });
        Test("Contains XML persists predicate not expanded type list", () => FileTest(path => {
            SettingsSerializer.Save(path, new() { UseSameCategoryTypeRules = true, SameCategoryTypeRules = [ContainsRule()] });
            var settings = SettingsSerializer.LoadOrDefault(path);
            Equal(1, settings.SameCategoryTypeRules.Count);
            Equal(TypeJoinMatchMode.Contains, settings.SameCategoryTypeRules[0].LoserMatchMode);
            Equal("", settings.SameCategoryTypeRules[0].LoserFamilyName);
            Equal(-1, new TypeJoinPolicy(settings.SameCategoryTypeRules).Resolve("StructuralFraming", "Drain", "Channel", "FutureFamily", "增築_new"));
        }));
        Test("Legacy XML missing modes remains exact", () => FileTest(path => {
            SettingsSerializer.Save(path, new() { SameCategoryTypeRules = [Rule()] });
            var xml = XDocument.Load(path);
            xml.Descendants("WinnerMatchMode").Remove(); xml.Descendants("LoserMatchMode").Remove(); xml.Save(path);
            var r = SettingsSerializer.LoadOrDefault(path).SameCategoryTypeRules[0];
            Equal(TypeJoinMatchMode.Exact, r.WinnerMatchMode); Equal(TypeJoinMatchMode.Exact, r.LoserMatchMode);
            Equal(0, new TypeJoinPolicy([r]).Resolve("Wall", "Basic", "AA", "Basic", "B"));
        }));
        Test("Exact category/family/type and reverse direction", () => {
            var p = new TypeJoinPolicy([Rule()]);
            Equal(-1, p.Resolve("Wall", "Basic", "A", "Basic", "B"));
            Equal(1, p.Resolve("Wall", "Basic", "B", "Basic", "A"));
        });
        foreach (string category in new[] { "Floor", "wall", "Unknown", "", " " })
            Test($"Category does not match: '{category}'", () => Equal(0, new TypeJoinPolicy([Rule()]).Resolve(category, "Basic", "A", "Basic", "B")));
        foreach (var names in new[] { ("basic", "A", "Basic", "B"), ("Basic", "a", "Basic", "B"), ("Basic", "A", "basic", "B"), ("Basic", "A", "Basic", "b"), ("Basic", "A ", "Basic", "B"), ("", "A", "Basic", "B"), ("Basic", "", "Basic", "B"), ("Basic", "A", " ", "B"), ("Basic", "A", "Basic", ""), ("Basic", "Unknown", "Basic", "B") })
            Test($"Exact names required: {names}", () => Equal(0, new TypeJoinPolicy([Rule()]).Resolve("Wall", names.Item1, names.Item2, names.Item3, names.Item4)));
        Test("No implicit A > C", () => {
            var p = new TypeJoinPolicy([Rule(), Rule("B", "C")]);
            Equal(-1, p.Resolve("Wall", "Basic", "B", "Basic", "C"));
            Equal(0, p.Resolve("Wall", "Basic", "A", "Basic", "C"));
            Equal(0, p.Resolve("Wall", "Basic", "C", "Basic", "A"));
        });
        Test("Family disambiguates identical type names", () => {
            var p = new TypeJoinPolicy([Rule("Shared", "Shared", "Family1", "Family2")]);
            Equal(-1, p.Resolve("Wall", "Family1", "Shared", "Family2", "Shared"));
            Equal(1, p.Resolve("Wall", "Family2", "Shared", "Family1", "Shared"));
            Equal(0, p.Resolve("Wall", "Family3", "Shared", "Family2", "Shared"));
        });
        Test("Length-prefixed names cannot collide", () => Equal(0, new TypeJoinPolicy([Rule("BC", "Z", "A", "X")]).Resolve("Wall", "AB", "C", "X", "Z")));
        Test("Opposite rules rejected", () => Reject(() => new TypeJoinPolicy([Rule(), Rule("B", "A")])));
        Test("Three-node cycle rejected", () => Reject(() => new TypeJoinPolicy([Rule(), Rule("B", "C"), Rule("C", "A")])));
        Test("Self edge rejected", () => Reject(() => new TypeJoinPolicy([Rule("A", "A")])));
        Test("Unknown rule category rejected", () => Reject(() => new TypeJoinPolicy([Rule(category: "Unknown")])));
        Test("Empty rule name rejected", () => Reject(() => new TypeJoinPolicy([Rule(familyA: " ")])));
        Test("Null rule rejected", () => Reject(() => new TypeJoinPolicy([null])));
        Test("Null rule list rejected", () => Reject(() => new TypeJoinPolicy(null)));
        Test("Same pair in different categories is independent", () => new TypeJoinPolicy([Rule(), Rule("B", "A", category: "Floor")]));
        Test("Clone is independent", () => { var r = Rule(); var c = r.Clone(); c.WinnerTypeName = "C"; Equal("A", r.WinnerTypeName); });
        foreach (bool reverse in new[] { false, true })
            Test($"Resolver applies winner and repeated apply does not switch, reverse={reverse}", () => {
                var f = Fixture(); var r = new TypeJoinResolver(f.Doc, f.Settings);
                var a = reverse ? f.B : f.A; var b = reverse ? f.A : f.B;
                Equal(true, r.TryResolve(a, b, out var winner)); Equal(f.A.Element, winner);
                f.Doc.Join(a.Element, b.Element, f.B.Element);
                Equal(true, TypeJoinResolver.ApplyOrder(f.Doc, a.Element, b.Element, winner));
                Equal(true, f.Doc.IsCutting(f.A.Element, f.B.Element));
                Equal(false, TypeJoinResolver.ApplyOrder(f.Doc, b.Element, a.Element, winner)); Equal(1, f.Doc.SwitchCount);
            });
        Test("Already correct order is untouched", () => {
            var f = Fixture(); f.Doc.Join(f.A.Element, f.B.Element, f.A.Element);
            Equal(false, TypeJoinResolver.ApplyOrder(f.Doc, f.A.Element, f.B.Element, f.A.Element)); Equal(0, f.Doc.SwitchCount);
        });
        Test("Failed switch is detected", () => {
            var f = Fixture(); f.Doc.Join(f.A.Element, f.B.Element, f.B.Element); f.Doc.IgnoreSwitch = true;
            Reject(() => TypeJoinResolver.ApplyOrder(f.Doc, f.A.Element, f.B.Element, f.A.Element));
        });
        foreach (bool allow in new[] { false, true })
            Test($"Disabled rules preserve legacy AllowSameCategoryJoin={allow}", () => {
                var f = Fixture(); f.Settings.UseSameCategoryTypeRules = false; f.Settings.AllowSameCategoryJoin = allow;
                Equal(allow, new TypeJoinResolver(f.Doc, f.Settings).TryResolve(f.A, f.B, out var winner)); Equal<Element>(null, winner);
                Equal(false, TypeJoinResolver.ApplyOrder(f.Doc, f.A.Element, f.B.Element, winner)); Equal(0, f.Doc.SwitchCount);
            });
        foreach (string missing in new[] { "missing", "not-type", "empty-family", "empty-type", "other-family", "unknown-type" })
            Test($"Enabled rules skip {missing} even with legacy allow", () => {
                var f = Fixture(); f.Settings.AllowSameCategoryJoin = true;
                f.Doc.Elements.Remove(f.B.Element.TypeId);
                if (missing != "missing") f.Doc.Elements[f.B.Element.TypeId] = missing == "not-type" ? new Element() : new ElementType {
                    FamilyName = missing == "empty-family" ? "" : missing == "other-family" ? "Other" : "Basic",
                    Name = missing == "empty-type" ? "" : missing == "unknown-type" ? "Unknown" : "B" };
                Equal(false, new TypeJoinResolver(f.Doc, f.Settings).TryResolve(f.A, f.B, out var winner)); Equal<Element>(null, winner);
            });
        Test("Enabled empty rules rejected, never wildcard", () => { var f = Fixture(); f.Settings.SameCategoryTypeRules.Clear(); Reject(() => new TypeJoinResolver(f.Doc, f.Settings)); });
        Test("Resolver honors structural exclusion", () => { var f = Fixture(); f.Settings.AllowStructuralNonStructuralJoin = false; Equal(false, new TypeJoinResolver(f.Doc, f.Settings).TryResolve(f.A, new(f.B.Element, new("StructuralWall", true)), out _)); });
        Test("Cross-category priority still selects cutter", () => { var f = Fixture(); Equal(true, new TypeJoinResolver(f.Doc, f.Settings).TryResolve(f.A, new(f.B.Element, new("Floor")), out var winner)); Equal(f.B.Element, winner); });
        Test("Old schema3 XML retains user selection and legacy flags", () => FileTest(path => {
            File.WriteAllText(path, "<AutoJoinSettings><SchemaVersion>3</SchemaVersion><EnabledCategoryKeys><string>Wall</string></EnabledCategoryKeys><PriorityKeys><string>Wall</string><string>Floor</string><string>StructuralFraming</string><string>StructuralColumn</string><string>StructuralFoundation</string></PriorityKeys><AllowSameCategoryJoin>true</AllowSameCategoryJoin></AutoJoinSettings>");
            var s = SettingsSerializer.LoadOrDefault(path);
            Equal(3, s.SchemaVersion); Equal("Wall", string.Join(",", s.EnabledCategoryKeys));
            Equal("Wall,Floor,StructuralFraming,StructuralColumn,StructuralFoundation", string.Join(",", s.PriorityKeys));
            Equal(true, s.AllowSameCategoryJoin); Equal(false, s.UseSameCategoryTypeRules); Equal(0, s.SameCategoryTypeRules.Count);
            SettingsSerializer.Save(path, s); var again = SettingsSerializer.LoadOrDefault(path);
            Equal("Wall", string.Join(",", again.EnabledCategoryKeys)); Equal(true, again.AllowSameCategoryJoin); Equal(3, again.SchemaVersion);
        }));
        Test("Rules roundtrip exactly without appended defaults across repeated loads", () => FileTest(path => {
            var s = Fixture().Settings; s.EnabledCategoryKeys = ["Wall"]; s.PriorityKeys = ["Wall", "Floor", "StructuralFraming", "StructuralColumn", "StructuralFoundation"];
            s.SameCategoryTypeRules = [Rule("A<&", "B", "Family:1", "Family:2"), Rule("B", "C", "Family:2", "Family:3")];
            for (int i = 0; i < 3; i++) {
                SettingsSerializer.Save(path, s); s = SettingsSerializer.LoadOrDefault(path);
                Equal(3, s.SchemaVersion); Equal(true, s.UseSameCategoryTypeRules); Equal(2, s.SameCategoryTypeRules.Count);
                var r = s.SameCategoryTypeRules[0]; Equal("Wall", r.CategoryKey); Equal("A<&", r.WinnerTypeName); Equal("Family:1", r.WinnerFamilyName); Equal("B", r.LoserTypeName); Equal("Family:2", r.LoserFamilyName);
                Equal("C", s.SameCategoryTypeRules[1].LoserTypeName); Equal("Family:3", s.SameCategoryTypeRules[1].LoserFamilyName);
                Equal("Wall", string.Join(",", s.EnabledCategoryKeys)); Equal("Wall,Floor,StructuralFraming,StructuralColumn,StructuralFoundation", string.Join(",", s.PriorityKeys));
                Equal("3", XDocument.Load(path).Root.Element("SchemaVersion").Value);
            }
        }));
        Test("Disabled saved rules do not become wildcard after load", () => FileTest(path => {
            var f = Fixture(); f.Settings.UseSameCategoryTypeRules = false; SettingsSerializer.Save(path, f.Settings);
            var s = SettingsSerializer.LoadOrDefault(path); Equal(1, s.SameCategoryTypeRules.Count); Equal(false, s.UseSameCategoryTypeRules);
            Equal(false, new TypeJoinResolver(f.Doc, s).TryResolve(f.A, f.B, out var winner)); Equal<Element>(null, winner);
        }));
        Test("Save rejects conflicts before overwriting file", () => FileTest(path => {
            File.WriteAllText(path, "sentinel"); var s = Fixture().Settings; s.SameCategoryTypeRules.Add(Rule("B", "A"));
            Reject(() => SettingsSerializer.Save(path, s)); Equal("sentinel", File.ReadAllText(path));
        }));
        Test("Save rejects enabled empty list", () => FileTest(path => Reject(() => SettingsSerializer.Save(path, new() { UseSameCategoryTypeRules = true }))));
        Test("Absent settings have no implicit rules", () => FileTest(path => { var s = SettingsSerializer.LoadOrDefault(path); Equal(false, s.UseSameCategoryTypeRules); Equal(0, s.SameCategoryTypeRules.Count); }));
        Console.WriteLine($"{passed} passed; {failed} failed; {passed + failed} total");
        return failed == 0 ? 0 : 1;
    }
}
