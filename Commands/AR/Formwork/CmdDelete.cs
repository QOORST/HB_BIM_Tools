using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager;
using YD_RevitTools.LicenseManager.UI.Formwork;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    [Transaction(TransactionMode.Manual)]
    public class CmdDelete : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet set)
        {
            var uiDoc = data.Application.ActiveUIDocument;
            var doc   = uiDoc.Document;

            try
            {
                // 授權檢查
                if (!LicenseHelper.CheckLicense("DeleteFormwork", "刪除模板", LicenseType.Standard))
                    return Result.Cancelled;

                // 只收集模板工具生成的元素（ApplicationId = "HB_BIM_Formwork"）
                var formworkShapes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DirectShape))
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .Cast<DirectShape>()
                    .Where(ds => ds.ApplicationId == "HB_BIM_Formwork")
                    .ToList();

                if (formworkShapes.Count == 0)
                {
                    TaskDialog.Show("刪除模板", "專案中沒有找到模板元素。");
                    return Result.Cancelled;
                }

                // 預建樓層查找表（elevation → Level），用於 bounding-box 歸属
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                // 取得目前 Revit 選取集
                var currentSelection = uiDoc.Selection.GetElementIds();

                // 組裝 FormworkItemInfo 清單
                var infos = formworkShapes
                    .Select(ds => BuildInfo(doc, ds, levels))
                    .ToList();

                // 開啟對話框
                var dialog = new DeleteFormworkDialog(infos, currentSelection);
                if (dialog.ShowDialog() != true || dialog.SelectedIds == null || !dialog.SelectedIds.Any())
                    return Result.Cancelled;

                var toDelete = dialog.SelectedIds;

                using (var t = new Transaction(doc, "Delete Formwork"))
                {
                    t.Start();
                    doc.Delete(toDelete as ICollection<ElementId> ?? toDelete.ToList());
                    t.Commit();
                }

                TaskDialog.Show("刪除完成", $"已成功刪除 {toDelete.Count} 個模板元素。");
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                msg = $"刪除失敗: {ex.Message}";
                return Result.Failed;
            }
        }

        // ── private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// 從 DirectShape 讀取類別參數並推算所屬樓層，組成 FormworkItemInfo
        /// </summary>
        private static FormworkItemInfo BuildInfo(Document doc, DirectShape ds, IList<Level> levels)
        {
            // 類別：讀 P_Category 共用參數
            var categoryParam = ds.LookupParameter(SharedParams.P_Category);
            var category      = categoryParam?.AsString() ?? string.Empty;

            // 樓層：先試 LevelId，再退回 bounding box Z
            string levelName = string.Empty;
            if (ds.LevelId != null && ds.LevelId != ElementId.InvalidElementId)
            {
                var lvl = doc.GetElement(ds.LevelId) as Level;
                levelName = lvl?.Name ?? string.Empty;
            }

            if (string.IsNullOrEmpty(levelName) && levels.Count > 0)
            {
                var bb = ds.get_BoundingBox(null);
                if (bb != null)
                {
                    // 取模型中心 Z（英呎），找最近的「下方」樓層
                    var centerZ = (bb.Min.Z + bb.Max.Z) / 2.0;
                    var best    = levels.LastOrDefault(l => l.Elevation <= centerZ + 1e-6)
                               ?? levels.First();
                    levelName = best.Name;
                }
            }

            return new FormworkItemInfo
            {
                Id        = ds.Id,
                Category  = category,
                LevelName = levelName
            };
        }
    }
}
