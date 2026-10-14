using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

namespace YD_RevitTools.LicenseManager.Commands.Drawing
{
    internal sealed class LegendRow
    {
        public LegendEntry Entry { get; set; }
        public string Component { get; set; }
        public string Note { get; set; }
        public double[] AutoComponent { get; set; }
        public double[] AutoNote { get; set; }
        public int Direction { get; set; }
        public string TextType { get; set; }
        public double TextWidth { get; set; }
        public string Formatting { get; set; }
    }
    internal sealed class LegendRecord
    {
        public int Version { get; set; } = 1;
        public string View { get; set; }
        public int Scale { get; set; }
        public double[] Origin { get; set; }
        public LegendSettings Settings { get; set; }
        public List<LegendRow> Rows { get; set; } = new List<LegendRow>();
    }
    internal sealed class LegendResult
    {
        public string View { get; set; }
        public List<LegendChange> Changes { get; set; } = new List<LegendChange>();
        public List<string> Problems { get; set; } = new List<string>();
        public List<string> Notes { get; set; } = new List<string>();
        public bool Success => Problems.Count == 0;
    }

    internal static class LegendService
    {
        internal static readonly Guid SchemaId = new Guid("9b1d8ca4-63dc-42a9-98e2-35b2685bfe3e");
        private const double Mm = 1.0 / 304.8;
        private const double Tolerance = 0.1 * Mm;

        internal static bool IsManaged(View view)
        {
            var schema = Schema.Lookup(SchemaId);
            return schema != null && view.GetEntity(schema).IsValid();
        }
        internal static LegendRecord Read(View view)
        {
            if (!IsManaged(view)) throw new InvalidOperationException("目標不是 HB 管理的圖例，不會覆寫人工圖例。");
            var schema = Schema.Lookup(SchemaId);
            var record = JsonConvert.DeserializeObject<LegendRecord>(view.GetEntity(schema).Get<string>("Json"));
            if (record == null || record.Version != 1 || record.View != view.UniqueId || record.Rows == null || record.Rows.Count == 0 || record.Origin?.Length != 3)
                throw new InvalidOperationException("圖例管理資料不相符；複製的 HB 圖例不可直接更新，請另建圖例。");
            return record;
        }
        private static void Write(View view, LegendRecord record)
        {
            var schema = Schema.Lookup(SchemaId);
            if (schema == null)
            {
                var builder = new SchemaBuilder(SchemaId);
                builder.SetSchemaName("HBSmartLegendV1");
                builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
                builder.AddSimpleField("Json", typeof(string)); schema = builder.Finish();
            }
            var entity = new Entity(schema); entity.Set("Json", JsonConvert.SerializeObject(record)); view.SetEntity(entity);
        }
        internal static List<Element> Components(Document doc, View view) => new FilteredElementCollector(doc, view.Id)
            .OfCategory(BuiltInCategory.OST_LegendComponents).WhereElementIsNotElementType().ToList();

        internal static List<ElementType> CollectTypes(Document doc, View source, ICollection<ElementId> selection)
        {
            IEnumerable<Element> elements;
            if (selection != null) elements = selection.Select(doc.GetElement);
            else
            {
                if (source == null || !FilteredElementCollector.IsViewValidForElementIteration(doc, source.Id)
                    || source.ViewType == ViewType.Legend || source.ViewType == ViewType.DrawingSheet)
                    throw new InvalidOperationException("請切換到來源模型視圖，或先選取模型構件。");
                elements = new FilteredElementCollector(doc, source.Id).WhereElementIsNotElementType();
            }
            return elements.Where(e => e != null && e.Category?.CategoryType == CategoryType.Model && !(e is RevitLinkInstance))
                .Select(e => e as ElementType ?? doc.GetElement(e.GetTypeId()) as ElementType).Where(e => e != null)
                .GroupBy(e => e.UniqueId).Select(g => g.First()).OrderBy(e => e.Category?.Name).ThenBy(e => e.FamilyName).ThenBy(e => e.Name)
                .ThenBy(e => e.UniqueId).ToList();
        }

        internal static LegendEntry Describe(ElementType type, string[] fields)
        {
            string Value(string field)
            {
                if (field == "族群") return type.FamilyName;
                if (field == "型別") return type.Name;
                if (field == "類別") return type.Category?.Name ?? "";
                var parameter = field == "型別代碼" ? type.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)
                    : field == "說明" ? type.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION) : null;
                if (parameter == null && field != "型別代碼" && field != "說明")
                {
                    var matches = type.GetParameters(field);
                    if (matches.Count != 1) throw new InvalidOperationException(type.Name + "：型別參數「" + field + "」不存在或名稱重複。");
                    parameter = matches[0];
                }
                return parameter == null ? "—" : (parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString()) ?? "—";
            }
            return new LegendEntry { Type = type.UniqueId, Category = type.Category?.Name ?? "", Family = type.FamilyName,
                Name = type.Name, Text = string.Join("\n", fields.Select(f => f + "：" + Value(f))) };
        }

        private static double[] Pack(XYZ point) => new[] { point.X, point.Y, point.Z };
        private static XYZ Unpack(double[] point) => new XYZ(point[0], point[1], point[2]);
        private static BoundingBoxXYZ Bounds(Element element, View view) => element.get_BoundingBox(view)
            ?? throw new InvalidOperationException("無法取得圖例顯示尺寸：" + element.Id);
        private static XYZ Center(Element element, View view) { var box = Bounds(element, view); return (box.Min + box.Max) / 2; }
        private static int Direction(Element component) => component.get_Parameter(BuiltInParameter.LEGEND_COMPONENT_VIEW)?.AsInteger() ?? 0;
        private static void Move(Document doc, Element element, XYZ from, XYZ to)
        { if (from.DistanceTo(to) > Tolerance) ElementTransformUtils.MoveElement(doc, element.Id, to - from); }
        private static bool Moved(XYZ actual, double[] automatic) => automatic == null || automatic.Length != 3 || actual.DistanceTo(Unpack(automatic)) > Tolerance;

        private static string Formatting(TextNote note)
        {
            var formatted = note.GetFormattedText(); var text = formatted.GetPlainText();
            var signature = new System.Text.StringBuilder();
            signature.Append(note.HorizontalAlignment).Append('|').Append(note.VerticalAlignment).Append('|');
            for (int i = 0; i < text.Length; i++)
            {
                var range = new TextRange(i, 1);
                signature.Append((int)formatted.GetBoldStatus(range)).Append((int)formatted.GetItalicStatus(range))
                    .Append((int)formatted.GetUnderlineStatus(range)).Append((int)formatted.GetAllCapsStatus(range))
                    .Append((int)formatted.GetSubscriptStatus(range)).Append((int)formatted.GetSuperscriptStatus(range))
                    .Append(':').Append((int)formatted.GetListType(range)).Append(':').Append(formatted.GetIndentLevel(range)).Append(';');
            }
            return signature.ToString();
        }

        private static Element CopyComponent(Document doc, Element seed, View view, ElementType type)
        {
            var ids = ElementTransformUtils.CopyElement(doc, seed.Id, XYZ.Zero);
            if (ids.Count != 1) throw new InvalidOperationException("範本元件帶有相依標註，請改用無相依標註的單一圖例元件。");
            var component = doc.GetElement(ids.First());
            var parameter = component.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
            if (parameter == null || parameter.IsReadOnly || !parameter.Set(type.Id))
                throw new InvalidOperationException("此型別無法套用到範本的圖例元件。");
            doc.Regenerate(); Bounds(component, view);
            return component;
        }
        private static void SafeDelete(Document doc, ICollection<ElementId> ids)
        {
            if (ids.Count == 0) return;
            var allowed = new HashSet<ElementId>(ids);
            var dependents = ids.SelectMany(id => doc.GetElement(id).GetDependentElements(null)).Distinct().Select(doc.GetElement).Where(e => e != null).ToList();
            // Revit legend components own an uncategorized internal graphics element.
            // Only these direct, untyped internal elements may accompany deletion;
            // dimensions, notes, lines, constraints and other categorized content remain protected.
            foreach (var dependent in dependents.Where(e => e.GetType() == typeof(Element) && e.Category == null && e.GetTypeId() == ElementId.InvalidElementId))
                allowed.Add(dependent.Id);
            var dependentNames = dependents.ToDictionary(e => e.Id, e => e.GetType().Name + ":" + e.Category?.Name);
            var deleted = doc.Delete(ids);
            if (deleted.Any(id => !allowed.Contains(id)))
                throw new InvalidOperationException("待移除的圖例列含有其他相依內容；已取消整批更新，請先處理相依內容。" +
                    string.Join(", ", deleted.Where(id => !allowed.Contains(id)).Select(id => id + ":" + (dependentNames.TryGetValue(id, out var name) ? name : "Unknown"))));
        }
        private static void ValidateOwned(Document doc, View view, LegendRecord record)
        {
            if (view.Scale != record.Scale) throw new InvalidOperationException("圖例比例已人工變更，請還原比例後更新，或以新比例範本另建圖例。");
            var ids = new HashSet<string>();
            foreach (var row in record.Rows)
            {
                var component = doc.GetElement(row.Component);
                var note = doc.GetElement(row.Note) as TextNote;
                if (component == null || note == null || component.OwnerViewId != view.Id || note.OwnerViewId != view.Id
                    || !ids.Add(component.UniqueId) || !ids.Add(note.UniqueId))
                    throw new InvalidOperationException("HB 圖例列曾被刪除或管理資料不完整，請復原後更新，或另建圖例。");
                var type = doc.GetElement(component.get_Parameter(BuiltInParameter.LEGEND_COMPONENT)?.AsElementId() ?? ElementId.InvalidElementId);
                var changed = new List<string>();
                if (type?.UniqueId != row.Entry.Type) changed.Add("元件型別");
                if (Direction(component) != row.Direction) changed.Add("元件觀看方向");
                if (LegendPolicy.NormalizeText(note.Text) != LegendPolicy.NormalizeText(row.Entry.Text)) changed.Add("文字內容");
                if (doc.GetElement(note.GetTypeId())?.UniqueId != row.TextType) changed.Add("文字型式");
                if (Math.Abs(note.Width - row.TextWidth) > Tolerance) changed.Add("文字寬度");
                if (Math.Abs(note.BaseDirection.DotProduct(view.RightDirection) - 1) > 1e-6) changed.Add("文字旋轉");
                if (note.GetLeaders().Count != 0) changed.Add("文字引線");
                if (Formatting(note) != row.Formatting) changed.Add("文字格式");
                if (changed.Count > 0)
                    throw new InvalidOperationException(row.Entry.Name + "：HB 列的" + string.Join("、", changed) + "已人工修改；為保留修改，本次不更新。人工移動位置可正常保留。");
            }
        }

        private sealed class Failures : IFailuresPreprocessor
        {
            private readonly LegendResult _result;
            internal Failures(LegendResult result) { _result = result; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                var errors = false;
                foreach (var failure in accessor.GetFailureMessages())
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                    { _result.Notes.Add(failure.GetDescriptionText()); accessor.DeleteWarning(failure); }
                    else { _result.Problems.Add(failure.GetDescriptionText()); errors = true; }
                return errors ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }

        // Both preview and apply execute this exact path. Preview rolls back the whole group,
        // including the duplicate view, all elements, sheet placement and ownership data.
        internal static LegendResult Run(Document doc, LegendSettings settings, IList<string> selectedTypes, bool preview)
        {
            var result = new LegendResult();
            try
            {
                var entries = selectedTypes.Select(uid => Describe(doc.GetElement(uid) as ElementType
                    ?? throw new InvalidOperationException("來源型別已不存在，請重新讀取。"), settings.Fields)).ToList();
                LegendPolicy.Validate(settings, entries);
                var target = string.IsNullOrEmpty(settings.Target) ? null : doc.GetElement(settings.Target) as View;
                if (!string.IsNullOrEmpty(settings.Target) && target == null) throw new InvalidOperationException("目標圖例已不存在。");
                var previous = target == null ? null : Read(target);
                if (target != null) ValidateOwned(doc, target, previous);
                var template = doc.GetElement(settings.Template) as View;
                if (template == null || template.ViewType != ViewType.Legend || IsManaged(template))
                    throw new InvalidOperationException("請指定非 HB 輸出的既有圖例作為範本。");
                if (target != null && previous.Settings.Template != settings.Template)
                    throw new InvalidOperationException("更新時請使用原範本；更換範本請另建圖例。");
                if (Components(doc, template).Count != 1) throw new InvalidOperationException("範本必須包含恰好一個圖例元件；可保留人工標題與線條。");
                if (!(doc.GetElement(settings.TextType) is TextNoteType)) throw new InvalidOperationException("請選擇有效的文字型式。");
                if (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase) && v.Id != target?.Id))
                    throw new InvalidOperationException("已有同名視圖；請選取該 HB 圖例更新，或使用另一個名稱。");
                result.Changes = LegendPolicy.Compare(previous?.Rows.Select(r => r.Entry) ?? Enumerable.Empty<LegendEntry>(), entries);
                using (var group = new TransactionGroup(doc, "HB 智慧圖例"))
                {
                    group.Start();
                    using (var tx = new Transaction(doc, "建立／更新智慧圖例"))
                    {
                        tx.Start(); tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetClearAfterRollback(true).SetFailuresPreprocessor(new Failures(result)));
                        Element seed;
                        if (target == null)
                        {
                            target = (View)doc.GetElement(template.Duplicate(ViewDuplicateOption.WithDetailing));
                            seed = Components(doc, target).Single();
                        }
                        else seed = doc.GetElement(previous.Rows[0].Component);
                        target.Name = settings.Name;
                        // Probe every selected type independently, collecting unsupported combinations.
                        foreach (var entry in entries)
                        {
                            using (var probe = new SubTransaction(doc))
                            {
                                probe.Start();
                                try { CopyComponent(doc, seed, target, (ElementType)doc.GetElement(entry.Type)); }
                                catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
                                catch (Exception ex) { result.Problems.Add(entry.Family + " : " + entry.Name + "：" + ex.Message); }
                                finally { probe.RollBack(); }
                            }
                        }
                        if (!result.Success) { tx.RollBack(); group.RollBack(); return result; }
                        var record = new LegendRecord { View = target.UniqueId, Scale = target.Scale, Settings = settings,
                            Origin = previous?.Origin ?? Pack(Center(seed, target)) };
                        BuildRows(doc, target, seed, previous, record, entries, result);
                        if (previous == null) SafeDelete(doc, new[] { seed.Id });
                        Write(target, record); doc.Regenerate();
                        PlaceOnSheet(doc, target, settings.Sheet, result);
                        result.View = target.UniqueId;
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 未完成圖例交易，已回復原狀。");
                    }
                    if (preview) group.RollBack();
                    else if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("圖例交易群組未完成。");
                }
            }
            catch (Exception ex) { result.Problems.Add(ex.Message); }
            return result;
        }

        private static void BuildRows(Document doc, View view, Element seed, LegendRecord previous, LegendRecord record,
            IList<LegendEntry> entries, LegendResult result)
        {
            var settings = record.Settings; var scaleMm = view.Scale * Mm;
            var origin = Unpack(record.Origin); var right = view.RightDirection; var up = view.UpDirection;
            var prior = previous?.Rows.ToDictionary(x => x.Entry.Type) ?? new Dictionary<string, LegendRow>();
            var generated = new List<Element>(); double offset = 0;
            foreach (var entry in entries)
            {
                prior.TryGetValue(entry.Type, out var old);
                var component = old == null ? CopyComponent(doc, seed, view, (ElementType)doc.GetElement(entry.Type)) : doc.GetElement(old.Component);
                var note = old == null ? TextNote.Create(doc, view.Id, origin, settings.TextWidth * Mm, entry.Text,
                    new TextNoteOptions(doc.GetElement(settings.TextType).Id) { HorizontalAlignment = HorizontalTextAlignment.Left }) : (TextNote)doc.GetElement(old.Note);
                bool symbolMoved = old != null && Moved(Center(component, view), old.AutoComponent);
                bool textMoved = old != null && Moved(note.Coord, old.AutoNote);
                if (old != null)
                {
                    if (LegendPolicy.NormalizeText(note.Text) != LegendPolicy.NormalizeText(entry.Text)) note.Text = entry.Text;
                    if (note.GetTypeId() != doc.GetElement(settings.TextType).Id) note.ChangeTypeId(doc.GetElement(settings.TextType).Id);
                    note.Width = settings.TextWidth * Mm;
                }
                doc.Regenerate();
                var symbolBox = Bounds(component, view); var textBox = Bounds(note, view);
                double symbolWidth = symbolBox.Max.X - symbolBox.Min.X;
                if (symbolWidth > settings.SymbolWidth * scaleMm)
                    throw new InvalidOperationException(entry.Name + "：圖例符號超出符號欄寬，請增加欄寬或改用較小比例的範本。");
                double height = LegendPolicy.RowHeight((symbolBox.Max.Y - symbolBox.Min.Y) / scaleMm,
                    (textBox.Max.Y - textBox.Min.Y) / scaleMm, settings.MinimumRow, settings.Gap) * scaleMm;
                var componentPosition = origin + right * (settings.SymbolWidth * scaleMm / 2) - up * (offset + height / 2);
                var notePosition = origin + right * ((settings.SymbolWidth + settings.Gap) * scaleMm) - up * (offset + settings.Gap * scaleMm);
                if (!symbolMoved) Move(doc, component, Center(component, view), componentPosition);
                if (!textMoved) Move(doc, note, note.Coord, notePosition);
                if (symbolMoved || textMoved) result.Notes.Add(entry.Name + "：保留人工調整的位置。");
                doc.Regenerate();
                record.Rows.Add(new LegendRow { Entry = entry, Component = component.UniqueId, Note = note.UniqueId,
                    AutoComponent = symbolMoved ? old.AutoComponent : Pack(Center(component, view)),
                    AutoNote = textMoved ? old.AutoNote : Pack(note.Coord), Direction = Direction(component),
                    TextType = doc.GetElement(note.GetTypeId()).UniqueId, TextWidth = note.Width, Formatting = Formatting(note) });
                generated.Add(component); generated.Add(note); offset += height;
            }
            var removed = prior.Values.Where(r => entries.All(e => e.Type != r.Entry.Type)).SelectMany(r => new[] { doc.GetElement(r.Component).Id, doc.GetElement(r.Note).Id }).ToList();
            SafeDelete(doc, removed); doc.Regenerate();
            for (int i = 0; i < generated.Count; i++)
                for (int j = i + 1; j < generated.Count; j++)
                {
                    var a = Bounds(generated[i], view); var b = Bounds(generated[j], view);
                    if (Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X) > Tolerance
                        && Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y) > Tolerance)
                        throw new InvalidOperationException("圖例符號或文字重疊；請調整欄寬／列距，或還原人工移動的位置後再預覽。");
                }
        }

        private static void PlaceOnSheet(Document doc, View view, string sheetUid, LegendResult result)
        {
            if (!string.IsNullOrEmpty(sheetUid))
            {
                var sheet = doc.GetElement(sheetUid) as ViewSheet ?? throw new InvalidOperationException("指定圖紙已不存在。");
                if (sheet.IsPlaceholder) throw new InvalidOperationException("不可配置到預留圖紙。");
                var existing = sheet.GetAllViewports().Select(id => (Viewport)doc.GetElement(id)).Where(v => v.ViewId == view.Id).ToList();
                if (existing.Count > 1) throw new InvalidOperationException("同一圖紙上已有重複圖例配置，請先檢查。");
                if (existing.Count == 0)
                {
                    if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id)) throw new InvalidOperationException("此圖例無法配置到指定圖紙。");
                    var outline = PaperBounds(doc, sheet);
                    Viewport.Create(doc, sheet.Id, view.Id, (outline.Min + outline.Max) / 2);
                    doc.Regenerate(); result.Notes.Add("新圖例已配置到圖紙中央。");
                }
            }
            // Changing a legend affects every sheet containing it, even without a new placement.
            var placements = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Where(v => v.ViewId == view.Id).ToList();
            foreach (var viewport in placements) ValidatePlacement(doc, viewport);
            if (placements.Count > 0) result.Notes.Add($"已檢查 {placements.Count} 張圖紙的範圍與視埠重疊；既有位置保留，圖框、明細表與人工註記淨空仍須目視確認。");
        }

        private static void ValidatePlacement(Document doc, Viewport viewport)
        {
            var sheet = (ViewSheet)doc.GetElement(viewport.SheetId); var outline = PaperBounds(doc, sheet);
            var box = viewport.GetBoxOutline(); var label = viewport.GetLabelOutline();
            foreach (var bounds in new[] { box, label })
                if (bounds.MinimumPoint.X < outline.Min.X || bounds.MaximumPoint.X > outline.Max.X
                    || bounds.MinimumPoint.Y < outline.Min.Y || bounds.MaximumPoint.Y > outline.Max.Y)
                    throw new InvalidOperationException(sheet.SheetNumber + "：圖例或視埠標題超出圖紙範圍；請調整圖例尺寸或圖紙配置後再執行。");
            foreach (var other in sheet.GetAllViewports().Where(id => id != viewport.Id).Select(id => (Viewport)doc.GetElement(id)))
            {
                foreach (var a in new[] { box, label })
                    foreach (var b in new[] { other.GetBoxOutline(), other.GetLabelOutline() })
                        if (Math.Min(a.MaximumPoint.X, b.MaximumPoint.X) - Math.Max(a.MinimumPoint.X, b.MinimumPoint.X) > Tolerance
                            && Math.Min(a.MaximumPoint.Y, b.MaximumPoint.Y) - Math.Max(a.MinimumPoint.Y, b.MinimumPoint.Y) > Tolerance)
                            throw new InvalidOperationException(sheet.SheetNumber + "：圖例與其他視埠或標題重疊；請先建立圖例再於圖紙安排空位，或調整既有位置。");
            }
        }

        private static BoundingBoxXYZ PaperBounds(Document doc, ViewSheet sheet)
        {
            // ViewSheet.Outline expands to include off-paper viewports; it is not a
            // reliable paper boundary. Use the independent title block instance.
            var frames = new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().ToList();
            if (frames.Count != 1) throw new InvalidOperationException(sheet.SheetNumber + "：需有恰好一個圖框才能確認圖紙範圍；請先整理圖框或暫不配置。");
            return Bounds(frames[0], sheet);
        }
    }
}
