using System.Collections.Generic;
using Autodesk.Revit.DB;

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

        public IReadOnlyList<ElementId> ReviewTagIds { get; private set; } = new List<ElementId>();
        public IReadOnlyList<ElementId> SkippedTagIds { get; private set; } = new List<ElementId>();
        public string Details { get; private set; } = string.Empty;

        public static AutoTagResult Completed(int processed, int matched, int created, int skipped, int failed,
            List<string> issues, List<ElementId> reviewIds, List<ElementId> skippedTagIds = null)
        {
            string summary = matched == 0 ? "沒有符合方向的構件；可改用全部方向。" :
                created == 0 && failed == 0 ? "依既有標籤參照略過所有符合方向的元素；可選取既有標籤核對。" : $"已建立 {created} 個標籤。";
            var result = new AutoTagResult(matched > 0 && failed == 0, processed, matched, created, skipped, failed,
                $"{summary} 略過 {skipped}、失敗 {failed}、需複核 {reviewIds.Count}。");
            result.ReviewTagIds = reviewIds;
            result.SkippedTagIds = skippedTagIds ?? new List<ElementId>();
            result.Details = $"候選：{processed}；符合方向：{matched}；方向略過：{processed - matched}\n" +
                string.Join("\n", issues);
            return result;
        }

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
