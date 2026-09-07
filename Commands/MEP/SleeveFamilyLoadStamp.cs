using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // Stored on the family so saving, undo and transaction rollback also apply to the stamp.
    internal static class SleeveFamilyLoadStamp
    {
        private static readonly Guid SchemaId = new Guid("7cb63d21-a57e-4fca-b63f-1e4ffed874f8");

        internal static string Fingerprint(string path, string requiredVersion)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return (requiredVersion ?? string.Empty) + ":" +
                    Convert.ToBase64String(hash.ComputeHash(stream));
        }

        private static Autodesk.Revit.DB.Family FindFamily(Document doc, string name)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Family)).Cast<Autodesk.Revit.DB.Family>()
                .FirstOrDefault(family => string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool WasLoaded(Document doc, string name, string fingerprint)
        {
            var schema = Schema.Lookup(SchemaId);
            var family = FindFamily(doc, name);
            if (schema == null || family == null) return false;
            var entity = family.GetEntity(schema);
            return entity.IsValid() && entity.Get<string>("SourceFingerprint") == fingerprint;
        }

        internal static bool Record(Document doc, string name, string fingerprint, bool versionCurrent)
        {
            var family = FindFamily(doc, name);
            if (family == null) return false;
            var schema = Schema.Lookup(SchemaId);
            if (schema == null)
            {
                var builder = new SchemaBuilder(SchemaId);
                builder.SetSchemaName("HBSleeveFamilyLoadStamp");
                builder.SetReadAccessLevel(AccessLevel.Public);
                builder.SetWriteAccessLevel(AccessLevel.Public);
                builder.AddSimpleField("SourceFingerprint", typeof(string));
                builder.AddSimpleField("VersionCurrent", typeof(bool));
                schema = builder.Finish();
            }
            var entity = new Entity(schema);
            entity.Set("SourceFingerprint", fingerprint);
            entity.Set("VersionCurrent", versionCurrent);
            family.SetEntity(entity);
            if (!versionCurrent)
                System.Diagnostics.Trace.TraceWarning(
                    "套管族群 {0} 載入後仍缺少有效的 HB_族群版本，或低於要求版本；已記錄來源，避免重複載入。", name);
            return true;
        }
    }
}
