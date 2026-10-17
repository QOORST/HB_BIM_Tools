using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // Source geometry is read in its own document, then transformed into the sleeve document.
    // A linked element ID alone is never sufficient: one file may have several link instances.
    internal sealed class SleevePipeSource
    {
        internal Element Element { get; }
        internal RevitLinkInstance Link { get; }
        internal string Key => (Link?.UniqueId ?? "local") + "|" + Element.UniqueId;
        internal string Label => Link == null ? "目前模型／" + Element.Id : Link.Name + "／" + Element.Id;
        internal Transform ToModel => Link?.GetTotalTransform() ?? Transform.Identity;
        internal Curve ModelCurve => ((LocationCurve)Element.Location).Curve.CreateTransformed(ToModel);
        internal SleevePipeSource(Element element, RevitLinkInstance link = null)
        {
            if (!Supported(element)) throw new InvalidOperationException("來源必須是有效的管線、風管、電管或電纜線架。");
            if (link != null && !element.Document.Equals(link.GetLinkDocument()))
                throw new InvalidOperationException("來源構件不屬於選取的直接連結。");
            Element = element; Link = link;
        }
        internal static bool Supported(Element element)
        {
            if (!(element?.Location is LocationCurve)) return false;
            return element is Pipe || element is Duct ||
                element.Category?.Id.GetIdValue() == (long)BuiltInCategory.OST_Conduit ||
                element.Category?.Id.GetIdValue() == (long)BuiltInCategory.OST_CableTray;
        }
        internal static List<SleevePipeSource> For(Document doc, IList<Element> pipes, PipeSleeveOptions options)
        {
            var sources = options?.PipeSources ?? pipes.Where(Supported).Select(p => new SleevePipeSource(p)).ToList();
            foreach (var source in sources)
            {
                if (source.Link == null ? !source.Element.Document.Equals(doc) :
                    !source.Link.Document.Equals(doc) || !source.Element.Document.Equals(source.Link.GetLinkDocument()))
                    throw new InvalidOperationException("管線來源文件與連結不一致，請重新選取來源。");
            }
            return sources.GroupBy(s => s.Key).Select(g => g.First()).ToList();
        }
    }

    internal static class SleeveSourceRecord
    {
        private static readonly Guid SchemaId = new Guid("a35613c9-347d-44cc-99dd-ab9f3c9be92c");
        private static readonly string[] Fields = { "SourceLink", "SourceElement", "HostLink", "HostElement" };
        private static Entity Read(Element sleeve)
        {
            var schema = Schema.Lookup(SchemaId);
            return schema == null ? new Entity() : sleeve.GetEntity(schema);
        }
        private static string Value(Entity entity, string name) => entity.Get<string>(entity.Schema.GetField(name)) ?? "";
        internal static bool HasRecord(Element sleeve) => Read(sleeve).IsValid();
        internal static void Save(FamilyInstance sleeve, PipeSleeveCandidate candidate)
        {
            var source = candidate.Source ?? new SleevePipeSource(candidate.Pipe);
            var schema = Schema.Lookup(SchemaId);
            if (schema == null)
            {
                var builder = new SchemaBuilder(SchemaId);
                builder.SetSchemaName("HBSleeveSourceV1");
                builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
                foreach (string field in Fields) builder.AddSimpleField(field, typeof(string));
                schema = builder.Finish();
            }
            var entity = new Entity(schema);
            entity.Set(schema.GetField("SourceLink"), source.Link?.UniqueId ?? "");
            entity.Set(schema.GetField("SourceElement"), source.Element.UniqueId);
            entity.Set(schema.GetField("HostLink"), candidate.HostLinkUniqueId ?? "");
            entity.Set(schema.GetField("HostElement"), candidate.HostElement?.UniqueId ?? "");
            sleeve.SetEntity(entity);
        }
        internal static string LegacyId(Element sleeve)
        {
            foreach (string name in new[] { "來源管線Id", "Pipe Id", "Source Pipe Id" })
            {
                var p = sleeve.LookupParameter(name);
                if (p?.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(p.AsString())) return p.AsString();
            }
            return "";
        }
        internal static bool Matches(Element sleeve, SleevePipeSource source)
        {
            var entity = Read(sleeve);
            if (entity.IsValid())
                return Value(entity, "SourceElement") == source.Element.UniqueId && Value(entity, "SourceLink") == (source.Link?.UniqueId ?? "");
            // Legacy sleeves belong to local sources only. Never adopt one through a colliding link ID.
            return source.Link == null && LegacyId(sleeve) == source.Element.Id.GetIdValue().ToString();
        }
        internal static bool? MatchesHost(Element sleeve, PipeSleeveCandidate candidate)
        {
            var entity = Read(sleeve);
            if (!entity.IsValid() || string.IsNullOrEmpty(Value(entity, "HostElement"))) return null;
            return Value(entity, "HostElement") == candidate.HostElement?.UniqueId &&
                Value(entity, "HostLink") == (candidate.HostLinkUniqueId ?? "");
        }
        internal static SleevePipeSource Resolve(Document doc, Element sleeve, out string reason)
        {
            reason = "";
            var entity = Read(sleeve);
            if (entity.IsValid())
            {
                string linkId = Value(entity, "SourceLink");
                RevitLinkInstance link = null;
                var sourceDoc = doc;
                if (!string.IsNullOrEmpty(linkId))
                {
                    link = doc.GetElement(linkId) as RevitLinkInstance;
                    if (link == null) { reason = "來源連結實例已移除或替換，保留套管；請確認來源。"; return null; }
                    sourceDoc = link.GetLinkDocument();
                    if (sourceDoc == null) { reason = "來源連結「" + link.Name + "」尚未載入；請重新載入後檢查。"; return null; }
                }
                string uid = Value(entity, "SourceElement");
                var element = string.IsNullOrEmpty(uid) ? null : sourceDoc.GetElement(uid);
                if (!SleevePipeSource.Supported(element)) { reason = "來源管線已刪除、重建或不是支援的管段；不依數字 ID 改配其他構件。"; return null; }
                return new SleevePipeSource(element, link);
            }
            if (long.TryParse(LegacyId(sleeve), out long id) && id > 0)
            {
                var element = doc.GetElement(RevitApiCompatibility.CreateElementId(id));
                if (SleevePipeSource.Supported(element)) return new SleevePipeSource(element);
            }
            reason = "目前模型找不到有效來源管線；請確認來源是否已刪除或套管由其他模型複製。";
            return null;
        }
    }
}
