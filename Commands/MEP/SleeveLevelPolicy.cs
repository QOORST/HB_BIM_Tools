using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveLevelPolicy
    {
        private static readonly Guid SchemaId = new Guid("c136c68d-2e9b-4580-b19b-d392f46d9988");
        internal sealed class Choice
        {
            public string Key { get; set; }
            public string Name { get; set; }
        }

        internal static List<Choice> Choices(Document doc)
        {
            var result = new List<Choice> { new Choice { Key = "", Name = "依來源管線樓層" } };
            result.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).Select(l => new Choice { Key = l.UniqueId, Name = "指定樓層：" + l.Name }));
            string saved = Read(doc);
            if (!result.Any(c => c.Key == saved))
                result.Add(new Choice { Key = saved, Name = "原指定樓層已不存在，請重新選擇" });
            return result;
        }

        // Remember the creation rule per view, never migrate the old project-wide GL implicitly.
        internal static string Read(Document doc)
        {
            var schema = Schema.Lookup(SchemaId);
            if (schema == null || doc.ActiveView == null) return "";
            var entity = doc.ActiveView.GetEntity(schema);
            return entity.IsValid() ? entity.Get<string>(schema.GetField("LevelUniqueId")) ?? "" : "";
        }

        internal static void Save(Document doc, string key)
        {
            var schema = Schema.Lookup(SchemaId);
            if (schema == null)
            {
                var builder = new SchemaBuilder(SchemaId);
                builder.SetSchemaName("HBSleeveCreationLevelV1");
                builder.SetReadAccessLevel(AccessLevel.Public);
                builder.SetWriteAccessLevel(AccessLevel.Public);
                builder.AddSimpleField("LevelUniqueId", typeof(string));
                schema = builder.Finish();
            }
            var entity = new Entity(schema);
            entity.Set(schema.GetField("LevelUniqueId"), key ?? "");
            doc.ActiveView.SetEntity(entity);
        }

        internal static Level Resolve(Document doc, Element source, string key)
        {
            if (!string.IsNullOrEmpty(key))
                return doc.GetElement(key) as Level ?? throw new InvalidOperationException("指定的約束樓層已不存在，請重新設定。");
            var level = (source as MEPCurve)?.ReferenceLevel ?? doc.GetElement(source?.LevelId ?? ElementId.InvalidElementId) as Level;
            if (level == null)
                throw new InvalidOperationException($"來源 {source?.Id} 無有效樓層；請指定約束樓層，不依高度猜測。");
            return level;
        }

        internal static string Summary(Document doc, IList<Element> sources, string key)
        {
            return string.Join("\n", sources.Select(s => Resolve(doc, s, key)).GroupBy(l => l.UniqueId)
                .Select(g => g.First().Name + "：" + g.Count() + " 個來源構件"));
        }

        internal static bool Confirm(Document doc, IList<Element> sources, string key)
        {
            string summary = Summary(doc, sources, key);
            var dialog = new Autodesk.Revit.UI.TaskDialog("套管樓層確認") {
                MainInstruction = "確認本次新增套管的約束樓層",
                MainContent = summary + "\n\n既有套管保留原約束樓層。此設定僅記憶於目前視圖，不影響其他視圖。",
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Ok | Autodesk.Revit.UI.TaskDialogCommonButtons.Cancel,
                DefaultButton = Autodesk.Revit.UI.TaskDialogResult.Cancel
            };
            return dialog.Show() == Autodesk.Revit.UI.TaskDialogResult.Ok;
        }
    }
}
