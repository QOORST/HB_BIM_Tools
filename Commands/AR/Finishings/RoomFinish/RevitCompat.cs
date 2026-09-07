using Autodesk.Revit.DB;
using System;
using System.Reflection;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    /// <summary>
    /// Revit 版本相容性輔助類別
    /// 處理 Revit 2022 (int) 和 Revit 2024+ (long) 的 ElementId 差異
    /// </summary>
    internal static class RevitCompat
    {
        private static readonly PropertyInfo ElementIdValueProperty = typeof(ElementId).GetProperty("Value");
        private static readonly ConstructorInfo ElementIdLongCtor = typeof(ElementId).GetConstructor(new[] { typeof(long) });
        private static readonly ConstructorInfo ElementIdIntCtor = typeof(ElementId).GetConstructor(new[] { typeof(int) });

        /// <summary>
        /// 取得 ElementId 的值（相容 int 和 long）
        /// </summary>
        public static long GetElementIdValue(ElementId elementId)
        {
            if (elementId == null)
                return -1;

            // 統一透過相容性輔助方法處理 Revit 2022-2026 差異
            return RevitApiCompatibility.GetIdValue(elementId);
        }

        /// <summary>
        /// 從 long 值創建 ElementId（相容 int 和 long）
        /// </summary>
        public static ElementId CreateElementId(long value)
        {
            if (value <= 0)
                return ElementId.InvalidElementId;

            // Revit 2024+ 使用 long 建構子
            if (ElementIdLongCtor != null)
                return (ElementId)ElementIdLongCtor.Invoke(new object[] { value });

            // Revit 2022 使用 int 建構子
            if (ElementIdIntCtor != null)
                return (ElementId)ElementIdIntCtor.Invoke(new object[] { Convert.ToInt32(value) });

            throw new NotSupportedException("無法建立 ElementId，請確認 Revit API 版本。");
        }
    }
}

