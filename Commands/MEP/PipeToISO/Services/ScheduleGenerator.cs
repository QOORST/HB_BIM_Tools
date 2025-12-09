using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    /// <summary>
    /// 明細表生成器 - 在 Revit 中自動建立管線 BOM 明細表
    /// </summary>
    public class ScheduleGenerator
    {
        private Document _doc;

        public ScheduleGenerator(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// 為指定的管線系統建立 BOM 明細表
        /// </summary>
        /// <param name="isoData">ISO 資料</param>
        /// <param name="scheduleName">明細表名稱</param>
        /// <returns>建立的明細表視圖</returns>
        public ViewSchedule CreateBOMSchedule(ISOData isoData, string scheduleName = null)
        {
            Logger.Info("開始建立 BOM 明細表");

            if (isoData == null)
            {
                Logger.Error("isoData 為 null");
                throw new ArgumentNullException(nameof(isoData));
            }

            if (string.IsNullOrEmpty(scheduleName))
            {
                scheduleName = $"BOM - {isoData.SystemName}";
            }

            Logger.Info($"明細表名稱: {scheduleName}");

            using (Transaction trans = new Transaction(_doc, "建立 BOM 明細表"))
            {
                trans.Start();
                Logger.Info("開始交易");

                try
                {
                    // 建立管線明細表
                    Logger.Info("建立管線類別明細表");
                    ViewSchedule pipeSchedule = ViewSchedule.CreateSchedule(
                        _doc, 
                        new ElementId(BuiltInCategory.OST_PipeCurves)
                    );

                    string uniqueName = GetUniqueScheduleName(scheduleName + " - 管線");
                    pipeSchedule.Name = uniqueName;
                    Logger.Info($"管線明細表已建立: {uniqueName}");

                    // 設定管線明細表欄位
                    SetupPipeScheduleFields(pipeSchedule, isoData);

                    // 建立配件明細表
                    Logger.Info("建立配件類別明細表");
                    ViewSchedule fittingSchedule = ViewSchedule.CreateSchedule(
                        _doc,
                        new ElementId(BuiltInCategory.OST_PipeFitting)
                    );

                    uniqueName = GetUniqueScheduleName(scheduleName + " - 配件");
                    fittingSchedule.Name = uniqueName;
                    Logger.Info($"配件明細表已建立: {uniqueName}");

                    // 設定配件明細表欄位
                    SetupFittingScheduleFields(fittingSchedule, isoData);

                    trans.Commit();
                    Logger.Info("交易已提交，明細表建立成功");

                    // 返回管線明細表作為主要明細表
                    return pipeSchedule;
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    Logger.Error("建立明細表時發生錯誤", ex);
                    throw new Exception($"建立明細表失敗：{ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 設定管線明細表的欄位
        /// </summary>
        private void SetupPipeScheduleFields(ViewSchedule schedule, ISOData isoData)
        {
            try
            {
                Logger.Info("設定管線明細表欄位");

                ScheduleDefinition definition = schedule.Definition;

                // 取得所有可排程的欄位
                IList<SchedulableField> schedulableFields = definition.GetSchedulableFields();

                // 添加欄位：系統類型
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, "系統類型");

                // 添加欄位：管徑
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, "管徑");

                // 添加欄位：長度
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.CURVE_ELEM_LENGTH, "長度");

                // 添加欄位：族與類型
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "族與類型");

                // 添加欄位：材料
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.RBS_PIPE_MATERIAL_PARAM, "材料");

                // 設定過濾器 - 只顯示指定系統
                SetScheduleFilter(schedule, isoData);

                // 設定分組和總計
                SetScheduleGroupingAndTotals(schedule);

                Logger.Info($"管線明細表欄位設定完成，共 {definition.GetFieldCount()} 個欄位");
            }
            catch (Exception ex)
            {
                Logger.Warning($"設定管線明細表欄位時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定配件明細表的欄位
        /// </summary>
        private void SetupFittingScheduleFields(ViewSchedule schedule, ISOData isoData)
        {
            try
            {
                Logger.Info("設定配件明細表欄位");

                ScheduleDefinition definition = schedule.Definition;

                // 取得所有可排程的欄位
                IList<SchedulableField> schedulableFields = definition.GetSchedulableFields();

                // 添加欄位：系統類型
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, "系統類型");

                // 添加欄位：族與類型
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "族與類型");

                // 添加欄位：尺寸
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.RBS_CALCULATED_SIZE, "尺寸");

                // 添加欄位:材料 (配件使用一般材料參數)
                AddFieldByBuiltInParameter(definition, schedulableFields, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "備註");

                // 設定過濾器 - 只顯示指定系統
                SetScheduleFilter(schedule, isoData);

                // 設定分組和計數
                SetScheduleGroupingAndTotals(schedule, true);

                Logger.Info($"配件明細表欄位設定完成，共 {definition.GetFieldCount()} 個欄位");
            }
            catch (Exception ex)
            {
                Logger.Warning($"設定配件明細表欄位時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 根據內建參數添加欄位
        /// </summary>
        private void AddFieldByBuiltInParameter(
            ScheduleDefinition definition,
            IList<SchedulableField> schedulableFields,
            BuiltInParameter builtInParam,
            string columnName = null)
        {
            try
            {
                // 查找對應的可排程欄位
                SchedulableField targetField = schedulableFields.FirstOrDefault(
                    sf => sf.ParameterId.Value == (int)builtInParam
                );

                if (targetField != null)
                {
                    ScheduleField field = definition.AddField(targetField);
                    
                    // 如果提供了自訂欄位名稱，則設定它
                    if (!string.IsNullOrEmpty(columnName))
                    {
                        field.ColumnHeading = columnName;
                    }

                    Logger.Info($"已添加欄位: {columnName ?? field.GetName()}");
                }
                else
                {
                    Logger.Warning($"找不到參數 {builtInParam} 的可排程欄位");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"添加欄位 {columnName} 時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定明細表過濾器，只顯示特定系統
        /// </summary>
        private void SetScheduleFilter(ViewSchedule schedule, ISOData isoData)
        {
            try
            {
                Logger.Info("設定明細表過濾器");

                ScheduleDefinition definition = schedule.Definition;

                // 查找系統類型欄位
                int fieldCount = definition.GetFieldCount();
                for (int i = 0; i < fieldCount; i++)
                {
                    ScheduleField field = definition.GetField(i);
                    
                    // 如果是系統類型參數,設定過濾器
                    if (field.ParameterId.Value == (long)BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)
                    {
                        // 查找系統類型 ID
                        ElementId systemTypeId = FindSystemTypeId(isoData.SystemName);
                        
                        if (systemTypeId != null && systemTypeId != ElementId.InvalidElementId)
                        {
                            ScheduleFilter filter = new ScheduleFilter(field.FieldId, ScheduleFilterType.Equal, systemTypeId);
                            definition.AddFilter(filter);
                            Logger.Info($"已添加過濾器: 系統類型 = {isoData.SystemName}");
                        }
                        else
                        {
                            Logger.Warning($"找不到系統類型 '{isoData.SystemName}' 的 ID");
                        }
                        
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"設定過濾器時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定明細表分組和總計
        /// </summary>
        private void SetScheduleGroupingAndTotals(ViewSchedule schedule, bool isItemized = false)
        {
            try
            {
                Logger.Info("設定明細表分組和總計");

                ScheduleDefinition definition = schedule.Definition;

                // 設定為明細化 (每個實例一行) 或分組統計
                definition.IsItemized = isItemized;

                // 如果不是明細化，則設定分組
                if (!isItemized)
                {
                    int fieldCount = definition.GetFieldCount();
                    
                    // 查找管徑或尺寸欄位進行分組
                    for (int i = 0; i < fieldCount; i++)
                    {
                        ScheduleField field = definition.GetField(i);
                        long paramId = field.ParameterId.Value;
                        
                        if (paramId == (long)BuiltInParameter.RBS_PIPE_DIAMETER_PARAM ||
                            paramId == (long)BuiltInParameter.RBS_CALCULATED_SIZE)
                        {
                            // 按此欄位分組
                            ScheduleSortGroupField sortGroup = new ScheduleSortGroupField(field.FieldId);
                            sortGroup.ShowHeader = true;
                            sortGroup.ShowFooter = true;
                            definition.AddSortGroupField(sortGroup);
                            Logger.Info($"已添加分組: {field.ColumnHeading}");
                            break;
                        }
                    }

                    // 對長度欄位設定總計
                    for (int i = 0; i < fieldCount; i++)
                    {
                        ScheduleField field = definition.GetField(i);
                        
                        if (field.ParameterId.Value == (long)BuiltInParameter.CURVE_ELEM_LENGTH)
                        {
                            field.DisplayType = ScheduleFieldDisplayType.Totals;
                            Logger.Info("已設定長度欄位顯示總計");
                            break;
                        }
                    }
                }

                // 設定計數
                definition.ShowGrandTotal = true;
                definition.ShowGrandTotalTitle = true;
                definition.ShowGrandTotalCount = true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"設定分組和總計時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 根據系統名稱查找系統類型 ID
        /// </summary>
        private ElementId FindSystemTypeId(string systemName)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(_doc)
                    .OfClass(typeof(PipingSystem));

                foreach (PipingSystem system in collector)
                {
                    if (system.Name == systemName)
                    {
                        Parameter systemTypeParam = system.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                        if (systemTypeParam != null && systemTypeParam.HasValue)
                        {
                            return systemTypeParam.AsElementId();
                        }
                    }
                }

                Logger.Warning($"找不到系統 '{systemName}'");
                return ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                Logger.Error($"查找系統類型 ID 時發生錯誤: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// 取得唯一的明細表名稱
        /// </summary>
        private string GetUniqueScheduleName(string baseName)
        {
            string name = baseName;
            int counter = 1;

            FilteredElementCollector collector = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule));

            while (collector.Any(v => v.Name == name))
            {
                name = $"{baseName} ({counter})";
                counter++;
            }

            return name;
        }
    }
}
