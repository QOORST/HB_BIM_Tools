"""Source-contract checks only; native geometry checks are separate."""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[2]
command = (root / 'Commands/MEP/CmdAutoAvoid.cs').read_text(encoding='utf-8-sig')
preview = (root / 'Commands/MEP/CmdAutoAvoid.Preview.cs').read_text(encoding='utf-8-sig')
utils = (root / 'Commands/MEP/AutoAvoid/Core/RevitUtils.cs').read_text(encoding='utf-8-sig')
assert 'successCount += kept;' in command
assert 'successCount++' not in command
assert 'failureOptions.SetForcedModalHandling(true)' in preview
assert 'failureOptions.SetFailuresPreprocessor(new RollbackOnFailure())' in preview
assert re.search(r'GetFailureMessages\(\).Count == 0\s*\? FailureProcessingResult.Continue\s*:\s*FailureProcessingResult.ProceedWithRollBack', command)
assert 'while (repeatMode && !transactionPending)' in command
assert preview.index('DetourCollisionReview.Check(') < preview.index('trans.Commit()') < preview.index('created.AddRange(candidateIds)')
assert 'if (status != TransactionStatus.Committed) throw' in preview
assert 'transactionPending = true;' in preview and 'if (transactionPending) throw;' in preview
assert '!= WinForms.DialogResult.OK || failed || created.Count == 0)' in preview
assert '{ group.RollBack(); return 0; }' in preview
assert preview.index('group.Assimilate() != TransactionStatus.Committed') < preview.index('return sourceIds.Count;')
assert 'deletedIds.Count != 1 || !deletedIds.Contains(originalId)' in utils
assert utils.count('ValidateConnections(doc, segments, elbows, start, end, externalStart, externalEnd);') == 2
assert 'catch' not in utils[utils.index('public static List<FamilyInstance> CreateElbowsForSegments'):utils.index('private static List<ConnectorReference> CaptureExternalConnections')]
print('15 source-contract checks passed (not Revit runtime transaction validation).')
