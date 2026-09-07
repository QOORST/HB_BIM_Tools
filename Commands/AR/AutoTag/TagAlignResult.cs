namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal sealed class TagAlignResult
    {
        private TagAlignResult(bool success, int processed, string message)
        {
            Success = success;
            Processed = processed;
            Message = message;
        }

        public bool Success { get; }

        public int Processed { get; }

        public string Message { get; }

        public static TagAlignResult Succeeded(int processed)
        {
            return new TagAlignResult(true, processed, $"已整理 {processed} 個標籤。");
        }

        public static TagAlignResult Failed(string message)
        {
            return new TagAlignResult(false, 0, message);
        }
    }
}
