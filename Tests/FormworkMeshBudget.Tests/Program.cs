using System;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;

internal static class Program
{
    private static int _passed;

    private static void Main()
    {
        Test("LOD ordered and capped", () => Require(
            CurvedMeshBudget.PrimaryLevelOfDetail <= 0.2 &&
            CurvedMeshBudget.PrimaryLevelOfDetail > CurvedMeshBudget.RetryLevelOfDetail &&
            CurvedMeshBudget.RetryLevelOfDetail > 0));
        Test("face boundary accepted", () => CurvedMeshBudget.ValidateAndReserve(12000, 24000));
        Test("triangle overflow", () => Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.ValidateAndReserve(12001, 3)));
        Test("vertex overflow", () => Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.ValidateAndReserve(1, 24001)));
        Test("negative rejected", () => Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.ValidateAndReserve(-1, 3)));
        Test("fallback bound accepted", () => CurvedMeshBudget.ValidateAndReserve(128, 384, true));
        Test("fallback overflow", () => Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.ValidateAndReserve(129, 387, true)));
        Test("cumulative boundary and sticky failure", () =>
        {
            CurvedMeshBudget.StartRun(null, () => 0);
            for (int i = 0; i < 10; i++) CurvedMeshBudget.ValidateAndReserve(12000, 100);
            Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.ValidateAndReserve(1, 3));
            Throws<CurvedMeshLimitException>(CurvedMeshBudget.ThrowIfExceeded);
            Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.Checkpoint("commit"));
        });
        Test("failure cleared next run", () =>
        {
            CurvedMeshBudget.StartRun(null, () => 0);
            Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.ValidateAndReserve(int.MaxValue, 3));
            CurvedMeshBudget.EndRun();
            CurvedMeshBudget.StartRun(null, () => 0);
            CurvedMeshBudget.ValidateAndReserve(1, 3);
            CurvedMeshBudget.ThrowIfExceeded();
        });
        Test("memory growth boundary", () =>
        {
            long memory = 1024;
            CurvedMeshBudget.StartRun(null, () => memory);
            memory += CurvedMeshBudget.MaxMemoryGrowthBytes - 1;
            CurvedMeshBudget.Checkpoint("below");
            memory++;
            Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.Checkpoint("at limit"));
            memory = 0;
            Throws<CurvedMeshLimitException>(CurvedMeshBudget.ThrowIfExceeded);
        });
        Test("absolute memory bound", () =>
        {
            CurvedMeshBudget.StartRun(null, () => CurvedMeshBudget.MaxPrivateMemoryBytes);
            Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.Checkpoint("start"));
        });
        Test("long baseline does not overflow", () =>
        {
            CurvedMeshBudget.StartRun(null, () => long.MaxValue);
            Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.Checkpoint("start"));
        });
        Test("callback cancel is sticky", () =>
        {
            CurvedMeshBudget.StartRun(_ => throw new OperationCanceledException(), () => 0);
            Throws<OperationCanceledException>(() => CurvedMeshBudget.Checkpoint("face"));
            Throws<OperationCanceledException>(CurvedMeshBudget.ThrowIfExceeded);
        });
        Test("nested callback suppressed", () =>
        {
            int calls = 0;
            CurvedMeshBudget.StartRun(_ => { calls++; CurvedMeshBudget.Checkpoint("nested"); }, () => 0);
            CurvedMeshBudget.Checkpoint("outer");
            Require(calls == 1);
        });
        Test("run reentry rejected without reset", () =>
        {
            CurvedMeshBudget.StartRun(null, () => 0);
            CurvedMeshBudget.ValidateAndReserve(12000, 100);
            Throws<InvalidOperationException>(() => CurvedMeshBudget.StartRun(null, () => 0));
            for (int i = 0; i < 9; i++) CurvedMeshBudget.ValidateAndReserve(12000, 100);
            Throws<CurvedMeshLimitException>(() => CurvedMeshBudget.ValidateAndReserve(1, 3));
        });
        Test("outside run callback disabled", () => CurvedMeshBudget.Checkpoint("analysis outside generation"));
        Test("reader failure remains fatal", () =>
        {
            bool fail = false;
            CurvedMeshBudget.StartRun(null, () => fail ? throw new InvalidOperationException("reader") : 0);
            fail = true;
            Throws<InvalidOperationException>(() => CurvedMeshBudget.Checkpoint("read"));
            fail = false;
            Throws<InvalidOperationException>(CurvedMeshBudget.ThrowIfExceeded);
        });
        Console.WriteLine($"{_passed} budget tests passed. No Revit model was modified.");
    }

    private static void Test(string name, Action action)
    {
        CurvedMeshBudget.EndRun();
        try { action(); _passed++; Console.WriteLine("PASS " + name); }
        finally { CurvedMeshBudget.EndRun(); }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new Exception("Assertion failed");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
