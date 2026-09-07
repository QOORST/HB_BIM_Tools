using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    internal static class AiRevitContextBuilder
    {
        public static string Build(ExternalCommandData commandData)
        {
            UIDocument uiDoc = commandData?.Application?.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            if (doc == null)
                return "目前沒有開啟的 Revit 文件。";

            var sb = new StringBuilder();
            sb.AppendLine("Revit 模型摘要");
            sb.AppendLine($"- Revit 版本：{commandData.Application.Application.VersionNumber}");
            sb.AppendLine($"- 文件名稱：{doc.Title}");
            sb.AppendLine($"- 工作共用：{(doc.IsWorkshared ? "是" : "否")}");
            sb.AppendLine($"- 目前視圖：{doc.ActiveView?.Name ?? "無"}");
            sb.AppendLine($"- 目前選取：{uiDoc.Selection.GetElementIds().Count} 個元素");
            sb.AppendLine();
            sb.AppendLine("主要類別數量");

            foreach (var item in CountCategories(doc))
            {
                sb.AppendLine($"- {item.Key}：{item.Value}");
            }

            return sb.ToString();
        }

        private static IEnumerable<KeyValuePair<string, int>> CountCategories(Document doc)
        {
            var targets = new[]
            {
                new KeyValuePair<string, BuiltInCategory>("牆", BuiltInCategory.OST_Walls),
                new KeyValuePair<string, BuiltInCategory>("樓板", BuiltInCategory.OST_Floors),
                new KeyValuePair<string, BuiltInCategory>("柱", BuiltInCategory.OST_StructuralColumns),
                new KeyValuePair<string, BuiltInCategory>("梁", BuiltInCategory.OST_StructuralFraming),
                new KeyValuePair<string, BuiltInCategory>("房間", BuiltInCategory.OST_Rooms),
                new KeyValuePair<string, BuiltInCategory>("管線", BuiltInCategory.OST_PipeCurves),
                new KeyValuePair<string, BuiltInCategory>("風管", BuiltInCategory.OST_DuctCurves),
                new KeyValuePair<string, BuiltInCategory>("電纜橋架", BuiltInCategory.OST_CableTray),
                new KeyValuePair<string, BuiltInCategory>("明細表", BuiltInCategory.OST_Schedules),
                new KeyValuePair<string, BuiltInCategory>("圖紙", BuiltInCategory.OST_Sheets)
            };

            foreach (var target in targets)
            {
                int count = 0;
                try
                {
                    count = new FilteredElementCollector(doc)
                        .OfCategory(target.Value)
                        .WhereElementIsNotElementType()
                        .Count();
                }
                catch
                {
                    count = 0;
                }

                yield return new KeyValuePair<string, int>(target.Key, count);
            }
        }
    }
}
