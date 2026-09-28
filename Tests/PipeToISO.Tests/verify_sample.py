"""Read-only cross-check of the user-provided straight-run sample, not a general model validator."""
import csv
import json
import sys
from decimal import Decimal
from pathlib import Path

audit_path, bom_path = map(Path, sys.argv[1:3])
with audit_path.open(encoding="utf-8-sig", newline="") as f:
    rows = list(csv.DictReader(f))
with bom_path.open(encoding="utf-8-sig", newline="") as f:
    bom = list(csv.DictReader(f))
by_id = {r["ElementId"]: r for r in rows}
assert len(by_id) == len(rows) == 15, "duplicate or missing elements"
assert all(r["算量範圍"] == "納入" and not r["檢查事項"] for r in rows)
assert len({r["UniqueId"] for r in rows}) == 15
assert len({r["系統UniqueId"] for r in rows}) == 1
edges = {key: set(filter(None, row["相連ElementId"].split(";"))) for key, row in by_id.items()}
for key, neighbors in edges.items():
    assert key not in neighbors
    for neighbor in neighbors:
        assert neighbor in by_id and key in edges[neighbor], "non-reciprocal connection"
visited, pending = set(), [next(iter(by_id))]
while pending:
    key = pending.pop()
    if key not in visited:
        visited.add(key)
        pending.extend(edges[key] - visited)
assert len(visited) == 15
assert sorted(map(len, edges.values())) == [1, 1] + [2] * 13
assert sum(int(r["未接端數"]) for r in rows) == 2
pipes = [r for r in rows if r["模型長度mm"]]
length_mm = sum(Decimal(r["模型長度mm"]) for r in pipes)
pipe_bom = next(r for r in bom if r["元件類型"] == "Pipe")
assert len(pipes) == int(pipe_bom["數量"]) == 8
assert length_mm == Decimal(pipe_bom["模型總長度(m)"]) * 1000 == Decimal("29542")
assert sum(int(r["數量"]) for r in bom) == 15
print(json.dumps({"audit": str(audit_path), "bom": str(bom_path), "elements": len(rows),
    "pipes": len(pipes), "fittings": len(rows) - len(pipes), "model_length_mm": str(length_mm),
    "connected_components": 1, "unique_edges": sum(map(len, edges.values())) // 2,
    "open_ends": 2, "result": "PASS", "scope": "CSV reconciliation only, not fabrication approval"}, ensure_ascii=False))
