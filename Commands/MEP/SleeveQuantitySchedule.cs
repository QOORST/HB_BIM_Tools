using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveQuantitySchedule
    {
        private static readonly Guid SchemaId = new Guid("e51a2806-9a75-42bf-b0f4-1d7a30ddafad");
        private static Schema Metadata()
        {
            var schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("HBSleeveQuantityScheduleV1");
            builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("ScopeKey", typeof(string));
            return builder.Finish();
        }

        internal static ViewSchedule CreateOrUpdate(Document doc, SleeveQuantityOptions options)
        {
            if (doc.IsFamilyDocument) throw new InvalidOperationException("請在專案文件建立套管數量表。");
            using (var tx = new Transaction(doc, "套管數量表／放入圖紙"))
            {
                tx.Start();
                // Keep failures synchronous, so rollback never reports a completed output.
                var failureOptions = tx.GetFailureHandlingOptions();
                failureOptions.SetClearAfterRollback(true);
                failureOptions.SetForcedModalHandling(true);
                tx.SetFailureHandlingOptions(failureOptions);
                try
                {
                    SleeveQuantityData.EnsureParameters(doc);
                    doc.Regenerate();
                    var rows = SleeveQuantityData.Synchronize(doc, options.ManualIds);
                    SleeveStableNumbering.Assign(doc, rows.Where(options.Matches).Select(r => doc.GetElement(r.Id)).OfType<FamilyInstance>());
                    rows = SleeveQuantityData.Synchronize(doc);
                    var schema = Metadata();
                    var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                        .Where(v => v.GetEntity(schema).IsValid() && v.GetEntity(schema).Get<string>(schema.GetField("ScopeKey")) == options.Key).ToList();
                    if (existing.Count > 1) throw new InvalidOperationException("找到多張相同範圍的受管理套管表，請先移除重複副本。");
                    var schedule = existing.SingleOrDefault();
                    bool created = schedule == null;
                    if (created)
                    {
                        schedule = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
                        string title = "HB_套管" + (options.Detailed ? "定位明細" : "數量彙總") + "_" + options.LevelName + (options.Zone.Length > 0 ? "_" + options.Zone : "");
                        if (!NamingUtils.IsValidName(title)) title = "HB_套管" + (options.Detailed ? "定位明細" : "數量彙總");
                        var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Select(e => e.Name));
                        string name = title;
                        for (int suffix = 2; names.Contains(name); suffix++) name = title + " (" + suffix + ")";
                        schedule.Name = name;
                        var entity = new Entity(schema); entity.Set(schema.GetField("ScopeKey"), options.Key); schedule.SetEntity(entity);
                    }
                    Configure(doc, schedule, options, created);
                    doc.Regenerate();
                    var expected = new HashSet<long>(rows.Where(options.Matches).Select(r => r.Id.GetIdValue()));
                    var actual = new HashSet<long>(new FilteredElementCollector(doc, schedule.Id).WhereElementIsNotElementType().ToElementIds().Select(id => id.GetIdValue()));
                    if (!expected.SetEquals(actual))
                        throw new InvalidOperationException($"套管表成員不一致：應有 {expected.Count} 支、明細表 {actual.Count} 支。請檢查階段、設計選項及明細表樣板；本次操作已取消。");
                    if (options.SheetId != ElementId.InvalidElementId) Place(doc, schedule, options);
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 未提交套管數量表。");
                    return schedule;
                }
                catch { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); throw; }
            }
        }

        private static void Configure(Document doc, ViewSchedule schedule, SleeveQuantityOptions options, bool created)
        {
            var def = schedule.Definition;
            def.IncludeLinkedFiles = false;
            var fields = new Dictionary<int, ScheduleField>();
            var visible = options.Detailed ? new[] {11, 4, 2, 5, 6, 7, 8, 14, 15, 12, 13, 9} : new[] {4, 2, 5, 6, 7, 8, 14, 15, 9};
            foreach (int index in visible.Concat(new[] {0, 3, 10}))
            {
                var parameterId = SharedParameterElement.Lookup(doc, SleeveQuantityData.Id(index)).Id;
                var field = def.GetFieldOrder().Select(def.GetField).FirstOrDefault(f => f.ParameterId == parameterId);
                if (field == null)
                {
                    var schedulable = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == parameterId);
                    if (schedulable == null) throw new InvalidOperationException("明細表無法加入欄位：" + SleeveQuantityData.Name(index));
                    field = def.AddField(schedulable);
                    field.ColumnHeading = SleeveQuantityData.Names[index];
                    if (index == 4) field.ColumnHeading = "約束樓層";
                    if (index == 12) field.ColumnHeading = "距約束樓層標高 mm";
                    field.GridColumnWidth = (index == 6 || index == 7 || index == 9 ? 45 : 25) / 304.8;
                    field.SheetColumnWidth = field.GridColumnWidth;
                }
                if (index == 0 || index == 3 || index == 10) field.IsHidden = true;
                fields[index] = field;
            }
            var count = def.GetFieldOrder().Select(def.GetField).FirstOrDefault(f => f.FieldType == ScheduleFieldType.Count);
            if (count == null) { count = def.AddField(ScheduleFieldType.Count); count.ColumnHeading = "支數"; }
            count.DisplayType = ScheduleFieldDisplayType.Totals;
            def.ClearFilters();
            def.AddFilter(new ScheduleFilter(fields[0].FieldId, ScheduleFilterType.Equal, "1"));
            if (options.LevelKey.Length > 0) def.AddFilter(new ScheduleFilter(fields[3].FieldId, ScheduleFilterType.Equal, options.LevelKey));
            if (options.Zone.Length > 0) def.AddFilter(new ScheduleFilter(fields[2].FieldId, ScheduleFilterType.Equal, options.Zone));
            def.ClearSortGroupFields();
            def.AddSortGroupField(new ScheduleSortGroupField(fields[10].FieldId));
            if (options.Detailed) def.AddSortGroupField(new ScheduleSortGroupField(fields[11].FieldId));
            def.IsItemized = options.Detailed;
            def.ShowGrandTotal = true; def.ShowGrandTotalCount = true; def.ShowGrandTotalTitle = true;
            def.GrandTotalTitle = "模型支數（含待確認，依狀態分列）";
            if (created) { def.ShowTitle = true; def.ShowHeaders = true; }
        }

        private static void Place(Document doc, ViewSchedule schedule, SleeveQuantityOptions options)
        {
            var sheet = doc.GetElement(options.SheetId) as ViewSheet;
            if (sheet == null || sheet.IsPlaceholder) throw new InvalidOperationException("選定圖紙不存在或為預留圖紙。");
            // Already placed, including a split schedule: retain all existing locations and segments.
            if (new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                .Any(s => s.OwnerViewId == sheet.Id && s.ScheduleId == schedule.Id)) return;
            var outline = sheet.Outline;
            var point = new XYZ(outline.Min.U + options.LeftMm / 304.8, outline.Max.V - options.TopMm / 304.8, 0);
            var instance = ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, point);
            doc.Regenerate();
            var box = instance.get_BoundingBox(sheet);
            if (box == null || box.Min.X < outline.Min.U || box.Max.X > outline.Max.U || box.Min.Y < outline.Min.V || box.Max.Y > outline.Max.V)
                throw new InvalidOperationException("數量表超出圖紙邊界。請調整位置，或先只建立表格、縮小欄寬／分割明細表後再放入圖紙。");
        }
    }
}
