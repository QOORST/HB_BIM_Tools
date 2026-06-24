using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    /// <summary>
    /// 在 Revit 文件中自動建立房間粉刷明細表（ViewSchedule）
    /// </summary>
    internal static class RevitScheduleBuilder
    {
        private const string ScheduleName = "AR_粉刷明細表";

        /// <summary>
        /// 建立或重建「AR_粉刷明細表」ViewSchedule。
        /// 包含：樓層、房間編號、房間名稱、面積、牆面塗層、樓板塗層、天花板塗層、天花板高度。
        /// </summary>
        /// <returns>建立的 ViewSchedule，若發生錯誤則回傳 null</returns>
        public static ViewSchedule CreateOrUpdate(Document doc)
        {
            using (var t = new Transaction(doc, "建立 AR 粉刷明細表"))
            {
                t.Start();
                try
                {
                    var schedule = BuildSchedule(doc);
                    t.Commit();
                    return schedule;
                }
                catch
                {
                    t.RollBack();
                    throw;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 內部實作
        // ─────────────────────────────────────────────────────────────────

        private static ViewSchedule BuildSchedule(Document doc)
        {
            // 刪除同名的舊明細表
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(v => string.Equals(v.Name, ScheduleName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                doc.Delete(existing.Id);

            // 建立房間明細表 (OST_Rooms)
            var categoryId = new ElementId(BuiltInCategory.OST_Rooms);
            var schedule = ViewSchedule.CreateSchedule(doc, categoryId);
            schedule.Name = ScheduleName;

            var def = schedule.Definition;
            def.IncludeLinkedFiles = false;

            var schedulableFields = def.GetSchedulableFields();

            // ── 依序新增欄位 ─────────────────────────────────────────
            // 1. 樓層
            ScheduleField levelField = TryAddBuiltIn(def, schedulableFields, BuiltInParameter.ROOM_LEVEL_ID);

            // 2. 房間編號
            ScheduleField numberField = TryAddBuiltIn(def, schedulableFields, BuiltInParameter.ROOM_NUMBER);

            // 3. 房間名稱
            TryAddBuiltIn(def, schedulableFields, BuiltInParameter.ROOM_NAME);

            // 4. 面積 (㎡)
            var areaField = TryAddBuiltIn(def, schedulableFields, BuiltInParameter.ROOM_AREA);
            if (areaField != null)
            {
                // 設定面積欄位顯示單位為平方公尺
                SetAreaFieldUnit(areaField, doc);
            }

            // 5. 自訂共享參數
            TryAddByName(def, schedulableFields, doc, "牆面塗層");
            TryAddByName(def, schedulableFields, doc, "樓板塗層");
            TryAddByName(def, schedulableFields, doc, "天花板塗層");
            TryAddByName(def, schedulableFields, doc, "天花板高度");
            TryAddByName(def, schedulableFields, doc, "踢腳板塗層");

            // ── 排序：依樓層群組，再依房間編號 ──────────────────────
            if (levelField != null)
            {
                var sgf = new ScheduleSortGroupField(levelField.FieldId);
                sgf.ShowHeader    = true;
                sgf.ShowBlankLine = false;
                def.AddSortGroupField(sgf);
            }

            if (numberField != null)
                def.AddSortGroupField(new ScheduleSortGroupField(numberField.FieldId));

            // ── 顯示群組總計 ─────────────────────────────────────────
            try { def.ShowGrandTotal = true; } catch { }

            return schedule;
        }

        // ── 輔助：新增內建欄位 ───────────────────────────────────────
        private static ScheduleField TryAddBuiltIn(
            ScheduleDefinition def,
            IList<SchedulableField> schedulableFields,
            BuiltInParameter bip)
        {
            var paramId = new ElementId(bip);
            var sf = schedulableFields.FirstOrDefault(f => f.ParameterId == paramId);
            if (sf == null) return null;

            return def.AddField(sf);
        }

        // ── 輔助：依名稱新增共享參數欄位 ─────────────────────────────
        private static ScheduleField TryAddByName(
            ScheduleDefinition def,
            IList<SchedulableField> schedulableFields,
            Document doc,
            string paramName)
        {
            var sf = schedulableFields.FirstOrDefault(f =>
            {
                var idVal = RevitCompat.GetElementIdValue(f.ParameterId);
                if (idVal <= 0) return false;   // 負值為 BuiltInParameter，跳過
                var elem = doc.GetElement(f.ParameterId) as ParameterElement;
                return string.Equals(elem?.Name, paramName, StringComparison.OrdinalIgnoreCase);
            });

            if (sf == null) return null;
            return def.AddField(sf);
        }


        // ── 輔助：設定面積欄位為平方公尺 ─────────────────────────────
        private static void SetAreaFieldUnit(ScheduleField field, Document doc)
        {
            try
            {
                var formatOptions = field.GetFormatOptions();
                formatOptions.UseDefault = false;
                formatOptions.SetUnitTypeId(UnitTypeId.SquareMeters);
                field.SetFormatOptions(formatOptions);
            }
            catch
            {
                // 無法設定單位時靜默略過，使用預設格式
            }
        }
    }
}
