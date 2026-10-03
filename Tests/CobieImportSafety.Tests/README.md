# COBie import safety checks

Run the Revit-independent regression suite with .NET 8:

```sh
dotnet run --project Tests/CobieImportSafety.Tests/CobieImportSafety.Tests.csproj
```

Executed on 2026-10-03 in the Linux cloud workspace with Microsoft .NET SDK 8.0.425: all 24 checks passed.

The suite exercises the production matching and conflicting-write policies, without mock implementations of either. It does not exercise Revit parameter access, failure processing, EPPlus, or UI.

## Safety behavior / compatibility

- CSV and the first XLSX worksheet remain supported. Duplicate headers are rejected before any model write.
- A supplied UniqueId or ElementId is an assertion. Stale or contradictory identifiers never fall back to Mark.
- Mark-only matching requires exactly one matching instance in the current document. Family/type does not silently disambiguate duplicate Marks.
- A numeric ElementId alone is rejected because it is document-local. Without UniqueId, supply Mark or both FamilyName and TypeName to corroborate it. This still cannot prove source-document identity; the confirmation asks the user to verify the source model.
- Configured instance/type scope is honored exactly; a missing/read-only/invalid instance parameter cannot silently write a shared type instead.
- Every candidate field is checked before any transaction. Type-wide writes report all affected instance counts, including instances absent from matched rows.
- Conflicting values for the same parameter (including rows sharing a type) are all excluded. Identical duplicates are written once.
- Room-derived defaults respect ImportEnabled and only fill fields absent from the file. Explicit file values retain precedence.
- Issue CSVs remain available if the user cancels, no safe writes exist, runtime writes fail, or the transaction rolls back. SourceRow is the Excel worksheet row or parsed nonempty CSV record number, counting the header as 1 (comment/blank lines are excluded; embedded newlines do not create another record).

## Required Windows/Revit acceptance tests (2022–2026)

Use a disposable model copy; no live project testing is implied by the pure suite.

1. Round-trip CSV and XLSX exports with UniqueId/ElementId/Mark/family/type intact. Check confirmed counts and changed values.
2. Change a UniqueId to a nonexistent one while preserving a valid ElementId and Mark: no fallback write.
3. Point ElementId at a different existing instance while retaining UniqueId: row rejected.
4. Import Mark-only rows for unique and duplicate Marks: only the unique Mark matches; ambiguity is in the CSV.
5. Import bare ElementId, and then ElementId plus contradictory metadata: both rejected.
6. Choose No or close the preview, including a mixed valid/invalid file: no transaction/model changes; issue CSV can still be saved.
7. Configure an instance field whose parameter exists only on its type: reject without modifying the type.
8. Import a valid type field for one instance in a type shared by several others: preview reports the entire affected population and those outside matched rows. Confirm and inspect all affected instances.
9. Give two instances sharing a type conflicting values: neither conflicting type value is written; unrelated valid instance fields can still import after confirmation.
10. Exercise duplicate column aliases, duplicate rows, same-name parameters, read-only parameters, invalid numbers, nonfinite numbers, integer/boolean values, and missing headers.
11. Force a Revit commit error or cancel failure processing: no success count is reported and the issue CSV identifies uncommitted planned writes.
12. Test an XLSX table beginning below row 1 and blank data rows; check issue source rows. Test CSV quoted multiline cells and a row wider than its header.
