using System;
using System.Diagnostics;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    internal sealed class CurvedMeshLimitException : Exception
    {
        internal CurvedMeshLimitException(string message) : base(message) { }
    }

    // Main Revit thread only. Limits bound downstream work, not an in-flight native API call.
    internal static class CurvedMeshBudget
    {
        internal const double PrimaryLevelOfDetail = 0.2;
        internal const double RetryLevelOfDetail = 0.1;
        internal const int MaxTrianglesPerFace = 12000;
        internal const int MaxVerticesPerFace = 24000;
        internal const int MaxTrianglesPerRun = 120000;
        internal const int MaxBooleanTriangles = 128;
        internal const long MaxMemoryGrowthBytes = 2L * 1024 * 1024 * 1024;
        internal const long MaxPrivateMemoryBytes = 24L * 1024 * 1024 * 1024;

        private static bool _active;
        private static bool _inCallback;
        private static int _triangles;
        private static long _memoryLimit;
        private static Action<string> _checkpoint;
        private static Func<long> _readMemory;
        private static Exception _failure;

        internal static void StartRun(Action<string> checkpoint, Func<long> readMemory = null)
        {
            if (_active) throw new InvalidOperationException("模板生成已在執行中。");
            var reader = readMemory ?? ReadPrivateMemory;
            long baseline = reader();
            if (baseline < 0) throw new InvalidOperationException("無法讀取 Revit 記憶體用量。");
            _memoryLimit = baseline >= MaxPrivateMemoryBytes - MaxMemoryGrowthBytes
                ? MaxPrivateMemoryBytes : baseline + MaxMemoryGrowthBytes;
            _triangles = 0;
            _failure = null;
            _checkpoint = checkpoint;
            _readMemory = reader;
            _active = true;
        }

        internal static void EndRun()
        {
            _active = false;
            _inCallback = false;
            _triangles = 0;
            _failure = null;
            _checkpoint = null;
            _readMemory = null;
        }

        internal static void ThrowIfExceeded()
        {
            if (_failure != null) throw _failure;
        }

        internal static void Checkpoint(string stage)
        {
            if (!_active) return;
            ThrowIfExceeded();
            try
            {
                long bytes = _readMemory();
                if (bytes < 0) throw new InvalidOperationException("無法讀取 Revit 記憶體用量。");
                if (bytes >= _memoryLimit)
                    Fail($"{stage}：Revit 私用記憶體已達安全門檻 {_memoryLimit / 1073741824.0:F1} GiB，本輪停止。請縮小選取範圍或簡化曲面後再測試。");
                if (_checkpoint != null && !_inCallback)
                {
                    _inCallback = true;
                    try { _checkpoint(stage); }
                    finally { _inCallback = false; }
                }
            }
            catch (Exception ex)
            {
                // Older geometry callers catch Exception; preserve stop requests until pre-commit.
                _failure = _failure ?? ex;
                throw;
            }
        }

        internal static void ValidateAndReserve(int triangles, int vertices, bool booleanFallback = false)
        {
            ThrowIfExceeded();
            if (triangles < 0 || vertices < 0)
                Fail("曲面網格數量無效，本輪停止。");
            int limit = booleanFallback ? MaxBooleanTriangles : MaxTrianglesPerFace;
            if (triangles > limit || vertices > MaxVerticesPerFace)
                Fail($"曲面網格超限：{triangles} 個三角片、{vertices} 個頂點；此路徑上限為 {limit} 個三角片及 {MaxVerticesPerFace} 個頂點。已停止，避免記憶體持續增加。");
            if (_active)
            {
                if (triangles > MaxTrianglesPerRun - _triangles)
                    Fail($"本輪曲面網格累計超過 {MaxTrianglesPerRun} 個三角片。請分批選取後再測試。");
                _triangles += triangles;
            }
        }

        private static void Fail(string message)
        {
            var exception = new CurvedMeshLimitException(message);
            if (_active) _failure = exception;
            throw exception;
        }

        private static long ReadPrivateMemory()
        {
            using (var process = Process.GetCurrentProcess())
                return process.PrivateMemorySize64;
        }
    }
}
