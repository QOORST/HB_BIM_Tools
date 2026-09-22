using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using F = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveGlLevel
    {
        private static readonly Guid Id = new Guid("f5c11636-7ea7-4844-a6d4-549c09ff851b");
        private static Schema GetSchema()
        {
            var schema = Schema.Lookup(Id);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(Id);
            builder.SetSchemaName("HBSleeveGlLevelV1");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("LevelUniqueId", typeof(string));
            return builder.Finish();
        }
        internal static Level Read(Document doc)
        {
            var schema = Schema.Lookup(Id);
            if (schema == null) return null;
            var entity = doc.ProjectInformation.GetEntity(schema);
            return entity.IsValid() ? doc.GetElement(entity.Get<string>(schema.GetField("LevelUniqueId"))) as Level : null;
        }
        internal static Level Require(Document doc) => Read(doc) ?? throw new InvalidOperationException("尚未指定有效的套管 GL 約束樓層，請重新設定。");
        internal static Level Select(Document doc)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
            var choices = new[] { new MepConnectionUi.Choice { Text = "請指定本專案 GL 樓層", Value = null } }
                .Concat(levels.Select(l => new MepConnectionUi.Choice { Text = l.Name, Value = l }));
            using (var combo = MepConnectionUi.Choices(choices))
            using (var form = MepConnectionUi.Form("HB_BIM｜套管 GL 約束樓層", "GL 樓層", combo))
            {
                var old = Read(doc);
                if (old != null) combo.SelectedIndex = levels.FindIndex(l => l.Id == old.Id) + 1;
                if (form.ShowDialog() != F.DialogResult.OK) throw new System.OperationCanceledException("已取消 GL 設定。");
                var level = ((MepConnectionUi.Choice)combo.SelectedItem).Value as Level;
                if (level == null) throw new InvalidOperationException("未指定 GL 樓層，未修改套管。");
                var schema = GetSchema(); var entity = new Entity(schema);
                entity.Set(schema.GetField("LevelUniqueId"), level.UniqueId);
                doc.ProjectInformation.SetEntity(entity);
                return level;
            }
        }
        internal static Level Actual(Document doc, FamilyInstance sleeve)
        {
            foreach (var id in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM })
            {
                var p = sleeve.get_Parameter(id);
                if (p != null && p.StorageType == StorageType.ElementId && p.HasValue && doc.GetElement(p.AsElementId()) is Level level) return level;
            }
            return doc.GetElement(sleeve.LevelId) as Level;
        }
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdSleeveGlSettings : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = data.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) return Result.Cancelled;
            try
            {
                using (var tx = new Transaction(doc, "設定套管 GL 樓層"))
                {
                    tx.Start(); SleeveGlLevel.Select(doc);
                    if (tx.Commit() != TransactionStatus.Committed) return Result.Failed;
                }
                TaskDialog.Show("套管 GL 樓層", "歸位基準已儲存。既有套管尚未變更；需變更約束樓層時，請選取套管執行樓層歸位。一般更新會保留原樓層。");
                return Result.Succeeded;
            }
            catch (System.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
