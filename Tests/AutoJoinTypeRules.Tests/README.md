# AutoJoin type-rule executable specifications

Requires .NET SDK 10. No NuGet test framework or Revit installation is required.
The executable prints each case and returns exit code 1 if any assertion fails.
Run with `dotnet run`, not `dotnet test`.

When installed at repository `Tests/AutoJoinTypeRules.Tests`, the default source
links resolve to `../../Commands/AutoJoin/`. Only four production files are linked:
TypeJoinRule.cs, TypeJoinResolver.cs, AutoJoinSettings.cs, SettingsSerializer.cs.

```powershell
dotnet run --project Tests/AutoJoinTypeRules.Tests/AutoJoinTypeRules.Tests.csproj
```

From the staging workspace, override SourceRoot with an absolute directory:

```powershell
dotnet run --project work/type-join-20261015/tests/AutoJoinTypeRules.Tests/AutoJoinTypeRules.Tests.csproj -p:SourceRoot="C:/Users/BIMer/Desktop/工作區/Revit API/Codex/work/type-join-20261015/core"
```

Validated result: 59 passed, 0 failed.

Contains mode matches literal type-name substrings (case-insensitive, trimmed
keyword), with an optional exact family filter. Empty keywords are rejected.
Legacy XML without match modes remains exact. Predicates persist as conditions,
not expanded type lists, so later matching types are included automatically.
Validation conservatively rejects potentially overlapping/cyclic directions,
including contains predicates that could overlap on future type names. Narrow
family filters or exact selectors resolve these ambiguities.

Fakes.cs supplies the minimal catalog/scope/classification surface and a stateful
Revit double. Join state is keyed by unordered element IDs; switching toggles the
actual stored cutter. Resolver tests assert the stored cutter and switch count,
including reversed inputs, repeated calls, and a deliberately ineffective switch.
The catalog keys and default ordering mirror the production catalog at authoring.
Keep this fixture aligned if production catalog values change.

XML tests use unique temporary directories and delete them on completion. They
never use the user's default settings path. Schema 3 tests retain the saved enabled
subset and a complete custom priority order; normalization still intentionally
appends missing priority categories. No default type rules may be appended.

These tests validate policy, resolver decisions/order application, and persistence.
They do not model Revit geometry, transactions, or engine-level atomic rollback.
Only copy the source project files when integrating; bin/obj are generated output.
