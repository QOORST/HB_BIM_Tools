using System;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    internal static class FinishingElementGuard
    {
        internal const string StableMarker = "YD_BIM_Finishings";

        internal static bool IsManagedFinishingElement(Element element)
        {
            if (!IsSupportedFinishCategory(element))
                return false;

            if (element is DirectShape ds)
                return string.Equals(ds.ApplicationId, StableMarker, StringComparison.OrdinalIgnoreCase);

            return HasStableMarker(element) || HasFinishNameMarker(element);
        }

        internal static bool IsSpatialLookupCandidate(Element element)
        {
            if (!IsSupportedFinishCategory(element))
                return false;

            if (element is DirectShape ds)
                return string.Equals(ds.ApplicationId, StableMarker, StringComparison.OrdinalIgnoreCase);

            return HasStableMarker(element) || HasFinishNameMarker(element);
        }

        internal static bool IsSupportedFinishCategory(Element element)
        {
            if (element?.Category == null)
                return false;

            var categoryId = RevitCompat.GetElementIdValue(element.Category.Id);
            return categoryId == (long)BuiltInCategory.OST_Walls
                || categoryId == (long)BuiltInCategory.OST_Floors
                || categoryId == (long)BuiltInCategory.OST_Ceilings
                || categoryId == (long)BuiltInCategory.OST_GenericModel;
        }

        internal static void MarkAsManagedFinishing(Element element)
        {
            try
            {
                var comments = element?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments == null || comments.IsReadOnly)
                    return;

                var existing = comments.AsString() ?? string.Empty;
                if (existing.IndexOf(StableMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return;

                comments.Set(string.IsNullOrWhiteSpace(existing)
                    ? StableMarker
                    : existing + " | " + StableMarker);
            }
            catch
            {
                // 標記失敗不應中斷生成流程；後續仍可用類型/名稱輔助識別。
            }
        }

        internal static bool HasStableMarker(Element element)
        {
            try
            {
                var comments = element?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                return !string.IsNullOrWhiteSpace(comments)
                    && comments.IndexOf(StableMarker, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool HasFinishNameMarker(Element element)
        {
            try
            {
                var name = element?.Name ?? string.Empty;
                var typeName = element?.Document?.GetElement(element.GetTypeId())?.Name ?? string.Empty;
                var combined = $"{name} {typeName}";
                return combined.IndexOf("AR_", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("裝修", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("粉刷", StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf("finish", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
