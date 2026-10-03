"""Run source-linked offline safety checks; never deploys or invokes Revit."""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet', help='Path to an approved .NET 8 SDK executable')
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        print('NOT RUN: .NET SDK is unavailable. No C# tests passed or failed.', file=sys.stderr)
        return 2

    # Explicit list avoids accidentally executing UI tests or deployment helpers.
    projects = [
        'Tests/CobieImportSafety.Tests/CobieImportSafety.Tests.csproj',
        'Tests/AutoAvoidConnection.Tests/AutoAvoidConnection.Tests.csproj',
        'Tests/FormworkAnalysisScope.Tests/FormworkAnalysisScope.Tests.csproj',
        'Tests/FormworkMeshBudget.Tests/FormworkMeshBudget.Tests.csproj',
    ]
    split_projects = sorted((ROOT / 'Tests').glob('SplitSafety.Tests/*.csproj'))
    if len(split_projects) != 1:
        print('FAIL: expected exactly one SplitSafety.Tests project.', file=sys.stderr)
        return 1
    projects.insert(0, split_projects[0].relative_to(ROOT).as_posix())
    failed = []
    for relative in projects:
        project = ROOT / relative
        print('\nRUN: ' + relative, flush=True)
        if not project.is_file():
            failed.append(relative + ' (missing project)')
            continue
        result = subprocess.run([dotnet, 'run', '--project', str(project)], cwd=ROOT)
        if result.returncode:
            failed.append(relative)
    if failed:
        print('\nFAILED: ' + ', '.join(failed), file=sys.stderr)
        return 1
    print('\nPASS: offline suites only. Revit API compilation, native geometry, transactions,')
    print('Windows installer execution and per-version live acceptance remain separate gates.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
