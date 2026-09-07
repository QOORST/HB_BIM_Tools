namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class AutoTagResult
    {
        private AutoTagResult(bool success, int processed, int matched, int created, int skipped, int failedCount, string message)
        {
            Success = success;
            Processed = processed;
            Matched = matched;
            Created = created;
            Skipped = skipped;
            FailedCount = failedCount;
            Message = message;
        }

        public bool Success { get; }

        public int Processed { get; }

        public int Matched { get; }

        public int Created { get; }

        public int Skipped { get; }

        public int FailedCount { get; }

        public string Message { get; }

        public static AutoTagResult Succeeded(int processed, int matched, int created, int skipped, int failed)
        {
            string message = $"已建立 {created} 個標籤。\n\n處理元素：{processed}\n符合方向：{matched}\n已標籤略過：{skipped}\n建立失敗：{failed}";
            return new AutoTagResult(true, processed, matched, created, skipped, failed, message);
        }

        public static AutoTagResult Failed(string message)
        {
            return new AutoTagResult(false, 0, 0, 0, 0, 0, message);
        }
    }
}
