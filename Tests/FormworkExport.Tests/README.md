# Export performance and report regression tests

From the repository root on Windows:

```powershell
dotnet run --project Tests/FormworkExport.Tests/ExportPerformance.Tests.csproj
```

Links the current CmdExportCsv, ExportTemplateRules and ExportOutputFile sources.
Revit and progress-window services are isolated test doubles; actual EPPlus
7.5.2 and CSV writers produce temporary files and read them back.

Coverage: fast mode bypasses analysis/cache/bounding boxes; manual and generated
templates both included; invalid areas/orphan hosts remain visible; totals,
culture-safe CSV numbers/escaping/BOM, Excel sheets and blank uncomputed values;
bounded formula display with complete per-template rows; full-mode cache cleanup;
cancellation and atomic file replacement including locked destinations; 20,000
synthetic template rows without geometry calls.

The synthetic timing is not a Revit performance result. Live checks remain for
native parameter collection cost, actual save dialog/mode choice, progress and
cancellation, installed-version behavior, network destinations, and model totals.
