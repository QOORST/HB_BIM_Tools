# AutoAvoid connection safety tests

This portable console harness links the production `RevitUtils.cs` with focused Revit API doubles. It checks fail-closed control flow and expected connection identities, including:

- Pipe, Duct and Conduit keep both original external connections, including direct curve connections.
- Open endpoints remain open; the original is deleted only after complete replacement validation.
- A missing, disconnected or incorrectly connected elbow returns failure.
- A failed disconnection or external reconnection returns failure.
- Cascading deletion of other elements returns failure so the caller can roll back.
- Connected mid-run branches and endpoint-changing plans are rejected.
- An active transaction is required.

Run with a .NET 8 SDK:

```sh
dotnet run --project Tests/AutoAvoidConnection.Tests/AutoAvoidConnection.Tests.csproj
python3 Tests/AutoAvoidConnection.Tests/check_transaction_guard.py
```

## Verification status and limits

On 2026-10-03, all 10 Python source guards and all 24 C# checks passed in the Linux cloud workspace using Microsoft .NET SDK 8.0.425. These doubles do not replace a Revit build or integration test, and do not simulate geometry trimming, regeneration failures or actual transaction rollback.

Required Windows/Revit QA (2022–2026): compile against each target API; exercise pipe/duct/conduit with both ends free, existing fittings, directly connected curves, missing fitting families, unsupported bend angles, short segments, attached dependents, and commit-time Revit failures. Failed cases must leave the original and every external connection/dependent intact after transaction rollback, with no orphan replacement segments; only committed transactions may increase the success count. Verify fitting geometry and connection topology on successful cases.

The command rolls back on any commit failure message (including warnings) rather than accepting Revit repairs that could alter the validated graph. It requests forced-modal failure handling to keep commit failure processing synchronous. Any unexpected Pending status stops further target/repeat processing and is never counted as success; the host lifecycle still needs live Revit validation.
