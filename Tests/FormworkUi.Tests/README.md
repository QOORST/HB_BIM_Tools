# Formwork UI session tests

Run from the repository root:

```powershell
dotnet run --project Tests/FormworkUi.Tests/SessionSettings.Tests.csproj
```

Windows and .NET 8 Windows Desktop are required. The build extracts the current
UiVm, UiMain and ProgressWindow from CmdMain.cs. Only the host geometry query is
omitted. Revit document, material-list and external-event services are test
doubles; the actual WPF controls and settings logic are exercised offline.

Checks cover per-document settings, defaults, close/reopen of the tool window,
invalid numeric input, material removal, selection/run-state isolation,
cross-document guards, and fixed-footer/scroll layouts at 100/150/200% rendering
scales. PNGs are generated in this test directory. DPI rendering does not replace
testing a real Revit window on monitors with different scaling.

Live checks still needed: Revit returns stable document identity across command
invocations, switching open projects, Save As, closing/reopening a model, external
event dispatch, and persisted material choice on generated elements.
