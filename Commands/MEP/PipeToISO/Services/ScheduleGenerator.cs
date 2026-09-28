using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    public class ScheduleGenerator
    {
        private readonly Document _doc;
        public ScheduleGenerator(Document doc) { _doc = doc; }

        // 兩張表視為同一成果：欄位、篩選或成員驗證失敗時整批回復。
        public ViewSchedule CreateBOMSchedule(ISOData data, string scheduleName = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var system = _doc.GetElement(data.SystemId) as PipingSystem;
            if (system == null) throw new InvalidOperationException("選定系統已不存在。");
            var network = data.GetScopedElements(_doc);
            string name = scheduleName ?? $"BOM - {system.Name}";
            using (var transaction = new Transaction(_doc, "建立管路明細表"))
            {
                transaction.Start();
                try
                {
                    var pipes = CreateSchedule(name + " - 管線", BuiltInCategory.OST_PipeCurves, system.Name, false);
                    var fittings = CreateSchedule(name + " - 配件", BuiltInCategory.OST_PipeFitting, system.Name, true);
                    _doc.Regenerate();
                    ValidateMembers(pipes, network, BuiltInCategory.OST_PipeCurves);
                    ValidateMembers(fittings, network, BuiltInCategory.OST_PipeFitting);
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("明細表交易未提交。");
                    return pipes;
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    throw;
                }
            }
        }

        private ViewSchedule CreateSchedule(string name, BuiltInCategory category, string systemName, bool fitting)
        {
            var schedule = ViewSchedule.CreateSchedule(_doc, new ElementId(category));
            schedule.Name = GetUniqueScheduleName(name);
            var definition = schedule.Definition;
            var systemField = AddField(definition, BuiltInParameter.RBS_SYSTEM_NAME_PARAM, "系統名稱");
            definition.AddFilter(new ScheduleFilter(systemField.FieldId, ScheduleFilterType.Equal, systemName));
            var typeField = AddField(definition, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "族與類型");
            var sizeField = AddField(definition,
                fitting ? BuiltInParameter.RBS_CALCULATED_SIZE : BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,
                fitting ? "尺寸" : "管徑");
            if (!fitting)
            {
                var materialField = AddField(definition, BuiltInParameter.RBS_PIPE_MATERIAL_PARAM, "材料");
                var length = AddField(definition, BuiltInParameter.CURVE_ELEM_LENGTH, "模型長度");
                length.DisplayType = ScheduleFieldDisplayType.Totals;
                definition.AddSortGroupField(new ScheduleSortGroupField(typeField.FieldId));
                definition.AddSortGroupField(new ScheduleSortGroupField(sizeField.FieldId));
                definition.AddSortGroupField(new ScheduleSortGroupField(materialField.FieldId));
            }
            // 配件保留逐件顯示；不以單一尺寸誤合併不同族群。
            definition.IsItemized = fitting;
            definition.ShowGrandTotal = true;
            definition.ShowGrandTotalTitle = true;
            definition.ShowGrandTotalCount = true;
            return schedule;
        }

        private static ScheduleField AddField(ScheduleDefinition definition, BuiltInParameter parameter, string heading)
        {
            var field = definition.GetSchedulableFields()
                .FirstOrDefault(f => f.ParameterId.GetIdValue() == (long)parameter);
            if (field == null) throw new InvalidOperationException($"明細表缺少必要欄位「{heading}」，已取消建立。");
            var result = definition.AddField(field);
            result.ColumnHeading = heading;
            return result;
        }

        private void ValidateMembers(ViewSchedule schedule, List<Element> network, BuiltInCategory category)
        {
            var expected = new HashSet<long>(network
                .Where(e => e.Category != null && e.Category.Id.GetIdValue() == (long)category)
                .Select(e => e.Id.GetIdValue()));
            var actual = new HashSet<long>(new FilteredElementCollector(_doc, schedule.Id)
                .OfCategory(category).WhereElementIsNotElementType().ToElementIds().Select(id => id.GetIdValue()));
            if (!expected.SetEquals(actual))
                throw new InvalidOperationException(
                    $"{schedule.Name} 範圍不符：應有 {expected.Count} 件，實際 {actual.Count} 件；已取消兩張明細表，避免漏算或混入其他系統。");
        }

        private string GetUniqueScheduleName(string baseName)
        {
            var names = new HashSet<string>(new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule)).Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            string name = baseName;
            int counter = 1;
            while (names.Contains(name)) name = $"{baseName} ({counter++})";
            return name;
        }
    }
}
