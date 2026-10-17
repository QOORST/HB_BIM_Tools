using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveDocumentStorage
    {
        internal static readonly Guid NumberSchema = new Guid("784ca963-7a2f-4606-9b80-7109f343e61a");
        internal static readonly Guid RevisionSchema = new Guid("8c773acf-dd5c-4edb-8552-f04463c4c97b");
        private static Schema Ensure(Guid id)
        {
            var schema = Schema.Lookup(id);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(id);
            builder.SetSchemaName(id == NumberSchema ? "HBSleeveNumbersV1" : id == RevisionSchema ? "HBSleeveRevisionV1" : "HBSleeveConstructionV1");
            builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("Json", typeof(string));
            return builder.Finish();
        }
        internal static List<DataStorage> Find(Document doc, Guid id)
        {
            var schema = Schema.Lookup(id);
            return schema == null ? new List<DataStorage>() : new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>()
                .Where(s => s.GetEntity(schema).IsValid()).ToList();
        }
        internal static T Read<T>(DataStorage storage, Guid id) where T : class
        {
            var schema = Schema.Lookup(id);
            return JsonConvert.DeserializeObject<T>(storage.GetEntity(schema).Get<string>(schema.GetField("Json")))
                ?? throw new InvalidOperationException("套管版本資料損毀，請先檢查模型備份。");
        }
        internal static void Write(Document doc, Guid id, object data, DataStorage storage = null)
        {
            if (!doc.IsModifiable) throw new InvalidOperationException("套管紀錄必須在交易內寫入。");
            var schema = Ensure(id); var entity = new Entity(schema);
            entity.Set(schema.GetField("Json"), JsonConvert.SerializeObject(data));
            (storage ?? DataStorage.Create(doc)).SetEntity(entity);
        }
    }
}
