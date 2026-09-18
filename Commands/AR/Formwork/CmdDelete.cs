using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers;
using YD_RevitTools.LicenseManager.UI.Formwork;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    [Transaction(TransactionMode.Manual)]
    public class CmdDelete : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet set)
        {
            var uiDoc = data.Application.ActiveUIDocument;
            var doc = uiDoc.Document;
            try
            {
                if (!LicenseHelper.CheckLicense("DeleteFormwork", "刪除模板", LicenseType.Standard))
                    return Result.Cancelled;

                var shapes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DirectShape)).OfCategory(BuiltInCategory.OST_GenericModel)
                    .Cast<DirectShape>()
                    .Where(ds => ExportTemplateRules.IsTemplate(ds.ApplicationId, ds.Name, HasMetadata(ds)))
                    .ToList();
                if (shapes.Count == 0)
                {
                    TaskDialog.Show("刪除模板", "專案中沒有找到可辨識的模板候選。");
                    return Result.Cancelled;
                }

                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().OrderBy(l => l.Elevation).ToList();
                var infos = shapes.Select(ds => BuildInfo(doc, ds, levels)).ToList();
                var dialog = new DeleteFormworkDialog(infos, uiDoc.Selection.GetElementIds());
                new System.Windows.Interop.WindowInteropHelper(dialog) { Owner = data.Application.MainWindowHandle };
                if (dialog.ShowDialog() != true || dialog.SelectedIds == null || dialog.SelectedIds.Count == 0)
                    return Result.Cancelled;

                var approved = new HashSet<ElementId>(infos.Where(i => i.CanDelete).Select(i => i.Id));
                var toDelete = dialog.SelectedIds.Distinct().ToList();
                if (toDelete.Any(id => !approved.Contains(id) || !IsDeletionAllowed(doc.GetElement(id) as DirectShape)))
                {
                    TaskDialog.Show("取消刪除", "候選資料已變更或識別不足，請重新檢查清單。");
                    return Result.Cancelled;
                }

                using (var transaction = new Transaction(doc, "Delete Formwork"))
                {
                    transaction.Start();
                    var deleted = doc.Delete(toDelete);
                    var requested = new HashSet<ElementId>(toDelete);
                    if (!requested.SetEquals(deleted))
                    {
                        // Copy ID values before rollback; reacquire restored elements afterwards.
                        var extra = deleted.Except(requested).Select(id => id.GetIdValue()).OrderBy(id => id).ToList();
                        var missing = requested.Except(deleted).Select(id => id.GetIdValue()).OrderBy(id => id).ToList();
                        if (transaction.RollBack() != TransactionStatus.RolledBack)
                        {
                            TaskDialog.Show("刪除未完成", "交易尚未確認回復，請先處理 Revit 失敗訊息。未提交本次刪除。");
                            return Result.Cancelled;
                        }
                        ShowDeletionMismatch(doc, requested.Count, deleted.Count, extra, missing);
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                    {
                        TaskDialog.Show("刪除未完成", "交易未成功提交；請檢查 Revit 的失敗訊息。");
                        return Result.Cancelled;
                    }
                }
                TaskDialog.Show("刪除完成", $"已刪除 {toDelete.Count} 個已確認的模板元素。");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                msg = $"刪除失敗: {ex.Message}";
                return Result.Failed;
            }
        }

        private static void ShowDeletionMismatch(Document doc, int requestedCount, int deletedCount,
            IList<long> extra, IList<long> missing)
        {
            var rows = new List<string>();
            foreach (var id in extra) rows.Add(DescribeRestoredElement(doc, id, "額外刪除（已回復）"));
            foreach (var id in missing) rows.Add(DescribeRestoredElement(doc, id, "未列於刪除回傳清單"));
            string summary = $"已回復本次所有變更，未提交刪除。\n選取模板：{requestedCount}；刪除回傳：{deletedCount}；額外：{extra.Count}；未回傳：{missing.Count}。";
            string logNotice;
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HB_BIM_Tools", "Logs", "FormworkDelete");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"delete-mismatch-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.txt");
                File.WriteAllText(path, $"時間：{DateTimeOffset.Now:o}\n工具版本：{typeof(CmdDelete).Assembly.GetName().Version}\n" +
                    summary + "\n\n" + string.Join(Environment.NewLine, rows), new UTF8Encoding(true));
                logNotice = "完整診斷檔：" + path;
            }
            catch (Exception ex)
            {
                logNotice = "診斷檔寫入失敗（不影響交易回復）：" + ex.Message;
            }
            var report = new TaskDialog("取消刪除 - 診斷")
            {
                MainInstruction = "刪除結果與確認清單不一致",
                MainContent = summary + "\n\n" + logNotice,
                ExpandedContent = string.Join(Environment.NewLine, rows.Take(50)) +
                    (rows.Count > 50 ? "\n畫面僅顯示前 50 項，完整清單請見診斷檔。" : string.Empty),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            report.Show();
        }

        private static string DescribeRestoredElement(Document doc, long id, string status)
        {
            try
            {
                var element = doc.GetElement(MakeId(id));
                if (element == null) return $"{status} | ID={id} | 回復後找不到元素";
                return $"{status} | ID={id} | 類別={element.Category?.Name ?? "無類別"} | " +
                    $"名稱={element.Name} | API類型={element.GetType().Name}";
            }
            catch (Exception ex)
            {
                return $"{status} | ID={id} | 讀取失敗：{ex.Message}";
            }
        }

        private static string ReadText(Element element, string name)
        {
            var parameter = element.LookupParameter(name);
            if (name == SharedParams.P_HostId && parameter?.StorageType == StorageType.Integer)
                return parameter.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
            return parameter?.StorageType == StorageType.String ? parameter.AsString() ?? string.Empty : string.Empty;
        }

        private static bool HasMetadata(DirectShape ds)
        {
            return ds.LookupParameter(SharedParams.P_HostId)?.HasValue == true &&
                   ds.LookupParameter(SharedParams.P_EffectiveArea)?.HasValue == true;
        }

        private static bool IsDeletionAllowed(DirectShape ds)
        {
            if (ds == null || ds.Category?.Id.GetIdValue() != (long)BuiltInCategory.OST_GenericModel) return false;
            if (ds.Pinned) return false;
            var area = ds.LookupParameter(SharedParams.P_EffectiveArea);
            bool validArea = area != null && area.HasValue && area.StorageType == StorageType.Double &&
                             ExportTemplateRules.IsValidArea(area.AsDouble());
            return ExportTemplateRules.CanDeleteTemplate(ds.ApplicationId, ds.Name,
                ExportTemplateRules.ParseHostId(ReadText(ds, SharedParams.P_HostId)).HasValue, validArea);
        }

        private static ElementId MakeId(long id)
        {
#if REVIT2024 || REVIT2025 || REVIT2026
            return new ElementId(id);
#else
            return id <= int.MaxValue ? new ElementId((int)id) : ElementId.InvalidElementId;
#endif
        }

        private static Level ResolveLevel(Document doc, Element element)
        {
            if (element == null) return null;
            var level = doc.GetElement(element.LevelId) as Level;
            if (level != null) return level;
            foreach (var builtIn in new[] { BuiltInParameter.WALL_BASE_CONSTRAINT,
                BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.FAMILY_LEVEL_PARAM,
                BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM })
            {
                var parameter = element.get_Parameter(builtIn);
                if (parameter?.StorageType != StorageType.ElementId) continue;
                level = doc.GetElement(parameter.AsElementId()) as Level;
                if (level != null) return level;
            }
            return null;
        }

        private static FormworkItemInfo BuildInfo(Document doc, DirectShape ds, IList<Level> levels)
        {
            var hostId = ExportTemplateRules.ParseHostId(ReadText(ds, SharedParams.P_HostId));
            var host = hostId.HasValue ? doc.GetElement(MakeId(hostId.Value)) : null;
            var category = ReadText(ds, SharedParams.P_Category);
            if (string.IsNullOrWhiteSpace(category) && host != null) category = ElementCategorizer.GetCategoryName(host);
            var level = ResolveLevel(doc, host);
            string levelBasis = level != null ? "宿主基準樓層" : string.Empty;
            if (level == null)
            {
                level = ResolveLevel(doc, ds);
                if (level != null) levelBasis = "模板樓層";
            }
            if (level == null && levels.Count > 0)
            {
                var bounds = ds.get_BoundingBox(null);
                if (bounds != null)
                {
                    var zs = new List<double>();
                    for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        zs.Add(bounds.Transform.OfPoint(new XYZ(x == 0 ? bounds.Min.X : bounds.Max.X,
                            y == 0 ? bounds.Min.Y : bounds.Max.Y, z == 0 ? bounds.Min.Z : bounds.Max.Z)).Z);
                    level = levels.LastOrDefault(l => l.Elevation <= zs.Min() + 1e-6);
                    if (level != null) levelBasis = "包圍盒底部推估";
                }
            }
            bool canDelete = IsDeletionAllowed(ds);
            return new FormworkItemInfo
            {
                Id = ds.Id, HostIdValue = hostId, Name = ds.Name, Category = category,
                LevelName = level?.Name ?? string.Empty, LevelBasis = levelBasis,
                Source = ExportTemplateRules.Source(ds.ApplicationDataId, ds.Name), CanDelete = canDelete,
                Warning = ds.Pinned ? "已釘選：不允許刪除" : !canDelete ? "識別不足：不允許刪除" : host == null ? "宿主缺失或未記錄" : string.Empty
            };
        }
    }
}
