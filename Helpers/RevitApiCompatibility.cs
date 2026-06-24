using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Helpers
{
    /// <summary>
    /// Revit API 版本兼容性輔助類
    /// 處理不同 Revit 版本之間的 API 差異
    /// </summary>
    public static class RevitApiCompatibility
    {
        /// <summary>
        /// 取得 ElementId 的值（兼容 Revit 2022-2026）
        /// Revit 2022-2023: 使用 IntegerValue
        /// Revit 2024+: 使用 Value
        /// </summary>
        public static long GetIdValue(this ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId)
                return -1;

#if REVIT2022 || REVIT2023
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        /// <summary>
        /// 從 long 值創建 ElementId（兼容 Revit 2022-2026）
        /// Revit 2022-2023: 使用 ElementId(int)
        /// Revit 2024+: 使用 new ElementId(long)
        /// </summary>
        public static ElementId CreateElementId(long value)
        {
#if REVIT2022 || REVIT2023
            return new ElementId((int)value);
#else
            return new ElementId(value);
#endif
        }

        /// <summary>
        /// 從 BuiltInCategory 取得 ElementId（兼容 Revit 2022-2026）
        /// </summary>
        public static ElementId GetCategoryId(BuiltInCategory category)
        {
#if REVIT2022 || REVIT2023
            return new ElementId(category);
#else
            return new ElementId((long)category);
#endif
        }
    }
}

