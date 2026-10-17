# Split safety regression checks

Run `dotnet run --project Tests/SplitSafety.Tests/SplitSafety.Tests.csproj`.

Extended on 2026-10-17 to cover floor/beam/structural-column selection,
multi-loop floor profiles, Mark/Comments/shared/project parameter inheritance,
empty comments, regeneration changing values, read-only targets and narrowly
scoped duplicate-Mark warnings. These are control-flow tests, not native geometry.
All 37 assertions passed. Revit 2022 and 2024 production builds passed.

Revit 2024 native validation passed 7 isolated cases on 2026-10-17: beam split,
beam/column notches, column holes inside split regions, column-only retention,
positive/negative floor height offsets, and rotated floor/beam geometry. Every
successful replacement retained Mark, Comments and a bound shared text parameter;
the production splitter verified volume, solid intersection and no overlapping
pieces before commit. Evidence: `artifacts/split-floor-20261017/native4/result.txt`.
Revit 2022/2026 native behavior and existing project-specific dependencies remain
outside this native fixture's coverage.

The project links the production SplitEngine.cs. Lightweight Revit doubles check
atomic per-original rollback, creation/commit failure counters, cascade deletion,
host dependencies, inner-loop preservation, unsupported slopes, parameter-copy rejection,
volume/overlap validation gates, a partial-wall-overlap rejection, and mixed-batch
isolation. Geometry uses synthetic cell sets; these
checks do NOT validate Revit's geometric kernel, native joins, parameter binding,
failure processing, API compatibility, or successful wall creation.

Native Windows/Revit 2022–2026 checks still required before release:
- Two disjoint rectangular horizontal floor regions; positive/negative offsets.
- Two rectangular vertical basic-wall regions, including full-width beam cuts.
- Horizontal straight-edge floor regions retain notches and holes. Multiple outer
  loops on one face become separate regions; containment assigns holes to their
  outer loops independent of winding/order. One column hole is not a split.
- Partial thickness overlap and curved/sloped/shape-edited hosts remain subject
  to geometry equality verification; unsupported shapes retain originals.
- Different writable instance/shared parameters, phases, marks, worksets.
- Hosted doors/windows/openings, dimensions/tags, constraints, analytical models,
  groups, assemblies, design options and existing non-cutter joins.
- Native creation errors and warnings at commit; verify no orphan pieces/joins,
  and displayed counts equal committed model changes. One failed original must
  not affect a separately successful original.
- Verify Boolean intersection/volume matches original post-join solids and that
  newly created hosts do not overlap. Confirm Undo returns the pre-command model.

Floor support covers horizontal straight-edge regions, including rotated edges,
column notches and holes; wall support remains rectangular vertical straight,
unconnected/unattached basic-wall faces. A face with multiple loops is never
interpreted as multiple pieces. Beam
bounding boxes are not used to remove wall height bands. All joins are rolled
back for skipped/failed originals. Rebuilds change element IDs, so model consumers
that store IDs outside Revit must re-associate them manually; such external
references cannot be discovered by these tests or the dependency guard.

`Tests/SplitFloor.Native` links the production splitter for isolated Revit 2024
beam/column geometry and parameter tests. It creates its own temporary project,
rolls back each case, and never edits the user's active model.
