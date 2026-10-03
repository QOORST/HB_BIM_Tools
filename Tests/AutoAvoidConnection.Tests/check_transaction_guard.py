"""Source-contract checks only; not runtime/transaction tests."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
command = (root / 'Commands/MEP/CmdAutoAvoid.cs').read_text(encoding='utf-8-sig')
utils = (root / 'Commands/MEP/AutoAvoid/Core/RevitUtils.cs').read_text(encoding='utf-8-sig')
assert re.search(r'TransactionStatus status = trans.Commit\(\);\s*if \(status == TransactionStatus.Committed\)\s*\{\s*successCount\+\+;', command)
assert command.count('successCount++') == 1
assert 'failureOptions.SetForcedModalHandling(true)' in command
assert 'failureOptions.SetFailuresPreprocessor(new RollbackOnFailure())' in command
assert re.search(r'GetFailureMessages\(\).Count == 0\s*\? FailureProcessingResult.Continue\s*:\s*FailureProcessingResult.ProceedWithRollBack', command)
assert 'while (repeatMode && !transactionPending)' in command
assert re.search(r'else\s*\{\s*trans.RollBack\(\);\s*failCount\+\+;', command)
assert 'deletedIds.Count != 1 || !deletedIds.Contains(originalId)' in utils
assert utils.count('ValidateConnections(doc, segments, elbows, start, end, externalStart, externalEnd);') == 2
assert 'catch' not in utils[utils.index('public static List<FamilyInstance> CreateElbowsForSegments'):utils.index('private static List<ConnectorReference> CaptureExternalConnections')]
print('10 source-contract checks passed (not Revit runtime validation).')
