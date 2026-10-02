namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class TagAlignResult
    {
        private TagAlignResult(bool success, int processed, string message, bool needsReview = false, bool cancelled = false)
        {
            Success = success;
            Processed = processed;
            Message = message;
            NeedsReview = needsReview;
            IsCancelled = cancelled;
        }

        public bool Success { get; }

        public int Processed { get; }

        public string Message { get; }
        public bool NeedsReview { get; }
        public bool IsCancelled { get; }

        public static TagAlignResult Succeeded(int processed, string detail = null, bool needsReview = false)
        {
            return new TagAlignResult(true, processed, detail ?? $"已整理 {processed} 個標籤。", needsReview);
        }

        public static TagAlignResult Failed(string message)
        {
            return new TagAlignResult(false, 0, message);
        }
        public static TagAlignResult Cancelled()
        {
            return new TagAlignResult(false, 0, "已取消，未修改標籤。", cancelled: true);
        }
    }
}
