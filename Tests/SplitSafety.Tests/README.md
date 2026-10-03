# Split safety regression checks

Run `dotnet run --project Tests/SplitSafety.Tests/SplitSafety.Tests.csproj`.

Executed on 2026-10-03 in the Linux cloud workspace with Microsoft .NET SDK
8.0.425: all 15 scenarios passed. This is not a native Revit build or test.

The project links the production SplitEngine.cs. Lightweight Revit doubles check
atomic per-original rollback, creation/commit failure counters, cascade deletion,
host dependencies, nested loops, unsupported slopes, parameter-copy rejection,
volume/overlap validation gates, a partial-wall-overlap rejection, and mixed-batch
isolation (15 scenarios total). Geometry uses synthetic cell sets; these
checks do NOT validate Revit's geometric kernel, native joins, parameter binding,
failure processing, API compatibility, or successful wall creation.

Native Windows/Revit 2022–2026 checks still required before release:
- Two disjoint rectangular horizontal floor regions; positive/negative offsets.
- Two rectangular vertical basic-wall regions, including full-width beam cuts.
- Partial length/thickness beam overlap, multiple side/top faces, holes, nested
  loops, curved/sloped/shape-edited hosts: unsupported shapes retain originals.
- Different writable instance/shared parameters, phases, marks, worksets.
- Hosted doors/windows/openings, dimensions/tags, constraints, analytical models,
  groups, assemblies, design options and existing non-cutter joins.
- Native creation errors and warnings at commit; verify no orphan pieces/joins,
  and displayed counts equal committed model changes. One failed original must
  not affect a separately successful original.
- Verify Boolean intersection/volume matches original post-join solids and that
  newly created hosts do not overlap. Confirm Undo returns the pre-command model.

Support is intentionally conservative: only axis-aligned rectangular horizontal
floor faces and rectangular vertical straight, unconnected/unattached basic-wall
faces. A face with multiple loops is never interpreted as multiple pieces. Beam
bounding boxes are not used to remove wall height bands. All joins are rolled
back for skipped/failed originals. Rebuilds change element IDs, so model consumers
that store IDs outside Revit must re-associate them manually; such external
references cannot be discovered by these tests or the dependency guard.
