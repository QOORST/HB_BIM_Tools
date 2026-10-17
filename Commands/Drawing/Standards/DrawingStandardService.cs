using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using YD_RevitTools.LicenseManager.Helpers;
using RevitView = Autodesk.Revit.DB.View;

namespace YD_RevitTools.LicenseManager.Commands.Drawing.Standards
{
    internal sealed class DrawingStandardService
    {
        private static readonly Guid StorageId = new Guid("e541fe68-bfa8-4cdb-842b-49fb4a39ae04");
        private readonly UIDocument _ui;
        private Document Doc => _ui.Document;
        internal DrawingStandardService(UIDocument ui) { _ui = ui; }

        internal DrawingStandardData Read()
        {
            var schema = Schema.Lookup(StorageId);
            if (schema == null) return new DrawingStandardData();
            var stores = Stores(schema);
            if (stores.Count > 1) throw new InvalidOperationException("發現重複的施工圖設定儲存區，請先處理模型資料衝突。");
            if (stores.Count == 0) return new DrawingStandardData();
            var data = JsonConvert.DeserializeObject<DrawingStandardData>(stores[0].GetEntity(schema).Get<string>("Json"));
            data?.Upgrade();
            if (data == null || data.FormatVersion != 2 || data.Definitions == null || data.CatalogHistory == null ||
                data.Revisions == null || data.Assignments == null ||
                data.Definitions.Any(d => d == null || string.IsNullOrWhiteSpace(d.Key) || string.IsNullOrWhiteSpace(d.Name) || d.DraftReferences == null ||
                    d.DraftReferences.Any(r => r == null || r.Values == null || string.IsNullOrWhiteSpace(r.UniqueId))) ||
                data.Definitions.GroupBy(d => d.Key).Any(g => g.Count() > 1) ||
                data.Definitions.GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
                data.Revisions.Any(r => r == null || r.Revision < 1 || !data.Definitions.Any(d => d.Key == r.Key) ||
                    r.References == null || r.References.Any(p => p == null || p.Values == null || string.IsNullOrWhiteSpace(p.UniqueId)) ||
                    r.References.Count(p => p.Kind == "視圖樣板") != 1) ||
                data.Revisions.GroupBy(r => r.Key + ":" + r.Revision).Any(g => g.Count() > 1) ||
                data.Assignments.Any(a => a == null || string.IsNullOrWhiteSpace(a.ViewUniqueId)) ||
                data.Assignments.GroupBy(a => a.ViewUniqueId).Any(g => g.Count() > 1))
                throw new InvalidOperationException("施工圖設定格式不支援或資料損毀；未覆寫既有紀錄。");
            return data;
        }

        private List<DataStorage> Stores(Schema schema) => new FilteredElementCollector(Doc).OfClass(typeof(DataStorage))
            .Cast<DataStorage>().Where(s => s.GetEntity(schema).IsValid()).ToList();

        private void Write(DrawingStandardData data)
        {
            var schema = Schema.Lookup(StorageId);
            if (schema == null)
            {
                var builder = new SchemaBuilder(StorageId);
                builder.SetSchemaName("HBDrawingStandardsV1");
                builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
                builder.AddSimpleField("Json", typeof(string)); schema = builder.Finish();
            }
            var stores = Stores(schema);
            if (stores.Count > 1) throw new InvalidOperationException("設定儲存區重複。");
            var store = stores.SingleOrDefault() ?? DataStorage.Create(Doc);
            var entity = new Entity(schema);
            entity.Set("Json", JsonConvert.SerializeObject(data)); store.SetEntity(entity);
        }

        internal List<StandardChoice> Choices()
        {
            var choices = new List<StandardChoice>();
            foreach (var view in new FilteredElementCollector(Doc).OfClass(typeof(RevitView)).Cast<RevitView>())
            {
                if (view.IsTemplate && !(view is ViewSchedule)) Add(view, "視圖樣板");
                else if (!view.IsTemplate && view.ViewType == ViewType.Legend) Add(view, "圖例");
                else if (view is ViewSchedule schedule && !schedule.IsTemplate && !schedule.IsTitleblockRevisionSchedule) Add(view, "明細表");
            }
            foreach (var type in new FilteredElementCollector(Doc).WhereElementIsElementType())
            {
                if (type is TextNoteType) Add(type, "文字樣式");
                else if (type is DimensionType dim && dim.StyleType == DimensionStyleType.Linear) Add(type, "尺寸樣式");
                else if (type is FamilySymbol && type.Category?.IsTagCategory == true) Add(type, "標籤族型");
            }
            return choices.OrderBy(c => c.Kind).ThenBy(c => c.Name).ToList();
            void Add(Element element, string kind) => choices.Add(new StandardChoice {
                Kind = kind, UniqueId = element.UniqueId, Name = DisplayName(element)
            });
        }

        private static string DisplayName(Element element) => element is FamilySymbol symbol
            ? symbol.FamilyName + " : " + symbol.Name : element.Name;

        internal string CreateStandard(string name, string sourceKey)
        {
            var data = Read();
            var key = DrawingStandardCatalog.Create(data, name, sourceKey, Doc.Application.Username);
            using (var transaction = NewTransaction("HB 新增施工圖設定組合")) { Write(data); Commit(transaction); }
            return key;
        }
        internal void RenameStandard(string key, string name)
        {
            var data = Read(); DrawingStandardCatalog.Rename(data, key, name, Doc.Application.Username);
            using (var transaction = NewTransaction("HB 施工圖設定組合改名")) { Write(data); Commit(transaction); }
        }
        internal void ArchiveStandard(string key, bool archived)
        {
            var data = Read(); DrawingStandardCatalog.SetArchived(data, key, archived, Doc.Application.Username);
            using (var transaction = NewTransaction(archived ? "HB 停用施工圖設定組合" : "HB 啟用施工圖設定組合")) { Write(data); Commit(transaction); }
        }

        internal TemplateInventory Inventory()
        {
            var data = Read();
            var standards = data.Definitions.Select(d => data.Latest(d.Key)).ToList();
            var allViews = new FilteredElementCollector(Doc).OfClass(typeof(RevitView)).Cast<RevitView>().ToList();
            var sheets = allViews.OfType<ViewSheet>().Where(s => !s.IsPlaceholder)
                .Select(s => new { Label = s.SheetNumber + " · " + s.Name, Views = s.GetAllPlacedViews() }).ToList();
            var active = _ui.ActiveView;
            var inventory = new TemplateInventory {
                ActiveViewUniqueId = active?.UniqueId, ActiveViewName = active?.Name,
                CanCreateFromActive = active != null && !active.IsTemplate && !(active is ViewSchedule) && active.IsViewValidForTemplateCreation()
            };
            foreach (var template in allViews.Where(v => v.IsTemplate && !(v is ViewSchedule)).OrderBy(v => v.Name))
            {
                inventory.Templates.Add(new TemplateInventoryItem {
                    UniqueId = template.UniqueId, Name = template.Name, ViewType = ViewTypeLabel(template.ViewType),
                    CanDuplicate = template.CanViewBeDuplicated(ViewDuplicateOption.Duplicate),
                    Uses = allViews.Where(v => !v.IsTemplate && v.ViewTemplateId == template.Id).OrderBy(v => v.Name)
                        .Select(v => new TemplateUsage { ViewUniqueId = v.UniqueId, ViewName = v.Name,
                            Sheets = string.Join("、", sheets.Where(s => s.Views.Contains(v.Id)).Select(s => s.Label)) }).ToList(),
                    StandardNames = standards.Where(s => s.Template?.UniqueId == template.UniqueId)
                        .Select(s => s.ToString()).ToList()
                });
            }
            return inventory;
        }

        private static string ViewTypeLabel(ViewType type)
        {
            switch (type)
            {
                case ViewType.FloorPlan: return "平面";
                case ViewType.EngineeringPlan: return "結構平面";
                case ViewType.CeilingPlan: return "天花板平面";
                case ViewType.Section: return "剖面";
                case ViewType.Elevation: return "立面";
                case ViewType.ThreeD: return "3D";
                case ViewType.DraftingView: return "製圖";
                case ViewType.Legend: return "圖例";
                default: return type.ToString();
            }
        }

        internal string CreateTemplate(TemplateCreationRequest request)
        {
            if (request == null) throw new InvalidOperationException("沒有樣板建立要求。");
            var source = Doc.GetElement(request.SourceUniqueId) as RevitView;
            if (source == null || source is ViewSchedule) throw new InvalidOperationException("來源視圖已不存在或不支援明細表樣板。");
            var name = DrawingStandardCatalog.ValidateName(request.Name, new FilteredElementCollector(Doc)
                .OfClass(typeof(RevitView)).Cast<RevitView>().Select(v => v.Name));
            if (request.FromCurrentView)
            {
                if (source.UniqueId != _ui.ActiveView?.UniqueId || source.IsTemplate || !source.IsViewValidForTemplateCreation())
                    throw new InvalidOperationException("目前視圖已變更或無法建立樣板，請更新清單後重試。");
            }
            else if (!source.IsTemplate || !source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
                throw new InvalidOperationException("指定來源不是可複製的視圖樣板。");
            var originalTemplateId = source.ViewTemplateId;
            using (var transaction = NewTransaction(request.FromCurrentView ? "HB 從目前視圖建立樣板" : "HB 複製視圖樣板"))
            {
                var created = request.FromCurrentView ? source.CreateViewTemplate() :
                    Doc.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate)) as RevitView;
                if (created == null || !created.IsTemplate || created.Id == source.Id)
                    throw new InvalidOperationException("Revit 未建立獨立視圖樣板；本次作業復原。");
                created.Name = name;
                Doc.Regenerate();
                if (source.ViewTemplateId != originalTemplateId)
                    throw new InvalidOperationException("來源視圖的樣板指派意外改變；本次作業復原。");
                var uid = created.UniqueId; Commit(transaction); return uid;
            }
        }

        internal void Save(string key, IEnumerable<string> selectedIds, string note)
        {
            if (string.IsNullOrWhiteSpace(note)) throw new InvalidOperationException("請填寫此版設定用途或變更說明。");
            var ids = new HashSet<string>(selectedIds);
            var choices = Choices().Where(c => ids.Contains(c.UniqueId)).ToList();
            if (choices.Count != ids.Count) throw new InvalidOperationException("選定的標準來源已不存在，請重新設定。");
            if (choices.Count(c => c.Kind == "視圖樣板") != 1) throw new InvalidOperationException("請指定一個視圖樣板。");
            if (!choices.Any(c => c.Kind == "文字樣式") || !choices.Any(c => c.Kind == "尺寸樣式") || !choices.Any(c => c.Kind == "標籤族型"))
                throw new InvalidOperationException("請至少選取一個文字樣式、線性尺寸樣式及標籤族型；缺少內容請先載入專案。");
            var data = Read();
            var previous = data.Latest(key);
            if (previous.Archived) throw new InvalidOperationException("請先啟用設定組合，再儲存新版。");
            var standard = new DrawingStandard {
                Key = key, Name = previous.Name, Revision = previous.Revision + 1,
                Note = note.Trim(), SavedAt = DateTimeOffset.Now.ToString("o"), SavedBy = Doc.Application.Username,
                References = choices.Select(c => Snapshot(c.UniqueId, c.Kind)).ToList()
            };
            using (var transaction = NewTransaction("HB 儲存施工圖設定版本"))
            {
                data.Revisions.Add(standard);
                data.Definitions.Single(d => d.Key == key).DraftReferences.Clear();
                Write(data); Commit(transaction);
            }
        }

        private static bool Supported(RevitView view) => view != null && !view.IsTemplate &&
            (view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.CeilingPlan ||
             view.ViewType == ViewType.EngineeringPlan || view.ViewType == ViewType.Section || view.ViewType == ViewType.Elevation);

        internal List<RevitView> Targets(int scope)
        {
            var views = new List<RevitView>();
            if (scope == 0) Add(_ui.ActiveView);
            else if (scope == 1)
            {
                foreach (var id in _ui.Selection.GetElementIds())
                {
                    var element = Doc.GetElement(id);
                    if (element is Viewport viewport) Add(Doc.GetElement(viewport.ViewId) as RevitView);
                    else Add(element as RevitView);
                }
            }
            else if (scope == 2)
            {
                foreach (var sheet in new FilteredElementCollector(Doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder)) Add(sheet);
            }
            return views.GroupBy(v => v.UniqueId).Select(g => g.First()).OrderBy(v => v.Name).ToList();
            void Add(RevitView view)
            {
                if (view is ViewSheet sheet)
                    views.AddRange(sheet.GetAllPlacedViews().Select(id => Doc.GetElement(id) as RevitView).Where(Supported));
                else if (Supported(view)) views.Add(view);
            }
        }

        internal StandardPreview Preview(string key, IList<RevitView> views)
        {
            var data = Read(); var standard = data.Latest(key);
            var preview = new StandardPreview { StandardKey = key, Revision = standard.Revision, TargetTemplate = standard.Template?.Name };
            if (standard.Archived) preview.DefinitionIssues.Add("此設定組合已停用，可查閱但不能套用；請先啟用。");
            if (standard.Revision == 0) { preview.DefinitionIssues.Add("此用途尚未設定；請先建立設定組合。"); return preview; }
            foreach (var reference in standard.References)
                preview.DefinitionIssues.AddRange(DrawingStandardPolicy.Compare(reference, Snapshot(reference.UniqueId, reference.Kind)));
            var template = Doc.GetElement(standard.Template.UniqueId) as RevitView;
            var sheets = new FilteredElementCollector(Doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder).ToList();
            var placements = sheets.ToDictionary(s => s.UniqueId, s => new HashSet<ElementId>(s.GetAllPlacedViews()));
            foreach (var instance in new FilteredElementCollector(Doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                if (Doc.GetElement(instance.OwnerViewId) is ViewSheet sheet && placements.TryGetValue(sheet.UniqueId, out var placed)) placed.Add(instance.ScheduleId);
            foreach (var view in views)
            {
                var ownerSheets = sheets.Where(s => placements[s.UniqueId].Contains(view.Id)).ToList();
                var assignment = data.Assignments.SingleOrDefault(a => a.ViewUniqueId == view.UniqueId);
                var issues = new List<string>();
                var assignmentIssue = DrawingStandardPolicy.AssignmentIssue(assignment, standard);
                if (!string.IsNullOrEmpty(assignmentIssue)) issues.Add(assignmentIssue);
                var block = "";
                if (template == null || !view.IsValidViewTemplate(template.Id)) block = "視圖與指定樣板不相容";
                if (view.GetPrimaryViewId() != ElementId.InvalidElementId || view.GetDependentViewIds().Count > 0)
                    block = "此視圖具有主從關係，請在 Revit 處理並登錄例外，避免連帶變動";
                if (view.ViewTemplateId != template?.Id) issues.Add("視圖樣板與組合不同");
                issues.AddRange(CheckAnnotations(view, standard));
                if (ownerSheets.Count == 0) issues.Add("尚未配置圖紙");
                foreach (var required in standard.References.Where(r => r.Kind == "圖例" || r.Kind == "明細表"))
                {
                    var element = Doc.GetElement(required.UniqueId);
                    foreach (var sheet in ownerSheets.Where(s => element == null || !placements[s.UniqueId].Contains(element.Id)))
                        issues.Add(sheet.SheetNumber + " 缺少" + required.Kind + "「" + required.Name + "」");
                }
                if (!string.IsNullOrEmpty(block)) issues.Add(block);
                var exception = assignment?.StandardKey == key ? assignment.ExceptionReason : "";
                preview.Rows.Add(new StandardViewRow {
                    UniqueId = view.UniqueId, ViewName = view.Name, Sheets = string.Join("、", ownerSheets.Select(s => s.SheetNumber)),
                    CurrentTemplate = (Doc.GetElement(view.ViewTemplateId) as RevitView)?.Name ?? "（未指定）",
                    CurrentTemplateUniqueId = Doc.GetElement(view.ViewTemplateId)?.UniqueId,
                    TargetTemplate = standard.Template.Name, BlockReason = block, ExceptionReason = exception,
                    Apply = false, Action = !string.IsNullOrWhiteSpace(exception) ? "保留例外" : !string.IsNullOrEmpty(block) ? "無法套用" :
                        view.ViewTemplateId == template?.Id ? "登錄／更新版本" : "指定樣板並登錄版本",
                    Issues = issues.Count == 0 ? "已檢項目符合；仍需人工複核" : string.Join("；", issues)
                });
            }
            preview.Detail = "標準：" + standard.Name + " v" + standard.Revision + "\r\n指定樣板：" + standard.Template.Name + "\r\n" + standard.Note + "\r\n" +
                "儲存：" + standard.SavedAt + " / " + standard.SavedBy + "\r\n\r\n" +
                "版本紀錄：\r\n" + string.Join("\r\n", data.Revisions.Where(r => r.Key == key).OrderByDescending(r => r.Revision)
                    .Select(r => "v" + r.Revision + " · " + r.SavedAt + " · " + r.SavedBy + " · " + r.Note)) + "\r\n\r\n" +
                "組合管理紀錄：\r\n" + string.Join("\r\n", data.CatalogHistory.Where(e => e.Key == key)
                    .Select(e => e.At + " · " + e.By + " · " + e.Action)) + "\r\n\r\n" +
                "實際設定差異：\r\n" + (preview.DefinitionIssues.Count == 0 ? "已檢項目與儲存基準一致。" : string.Join("\r\n", preview.DefinitionIssues)) +
                "\r\n\r\n檢查範圍：樣板參數、納管項目、分類顯示／覆寫、篩選器條件／覆寫；文字、尺寸、標籤族型參數及尺寸格式；視圖內已存在標註的類型；圖例／明細表是否配置於相同圖紙。" +
                "\r\n人工複核：連結顯示、工作集、階段圖形、個別元素覆寫／隱藏、圖例內容、明細表內容、標籤族內文字／標籤設定、漏標、重疊及模型正確性。此檢查不代表施工圖已可發行。" +
                "\r\n套用範圍：只指定勾選視圖的樣板並記錄版本；已建立的標籤、文字、尺寸及圖紙配置不會被批次改寫。";
            return preview;
        }

        private IEnumerable<string> CheckAnnotations(RevitView view, DrawingStandard standard)
        {
            var allowed = standard.References.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(r => r.UniqueId)));
            var errors = new Dictionary<string, int>();
            foreach (var element in new FilteredElementCollector(Doc).OwnedByView(view.Id).WhereElementIsNotElementType())
            {
                var kind = element is TextNote ? "文字樣式" : element is Dimension dim && dim.DimensionType?.StyleType == DimensionStyleType.Linear ? "尺寸樣式" :
                    element is IndependentTag ? "標籤族型" : null;
                if (kind == null || !allowed.TryGetValue(kind, out var ids)) continue;
                var type = Doc.GetElement(element.GetTypeId());
                if (type != null && !ids.Contains(type.UniqueId))
                {
                    var label = kind + "「" + DisplayName(type) + "」不在允許清單";
                    errors[label] = errors.TryGetValue(label, out var count) ? count + 1 : 1;
                }
            }
            return errors.Select(e => e.Key + "（" + e.Value + " 個）");
        }

        internal int Apply(StandardPreview preview)
        {
            var data = Read(); var standard = data.Latest(preview.StandardKey);
            var selected = DrawingStandardPolicy.ValidateApply(preview, standard);
            var views = selected.Select(row => Doc.GetElement(row.UniqueId) as RevitView).ToList();
            if (views.Any(v => !Supported(v))) throw new InvalidOperationException("視圖已不存在或不支援，請重新預覽。");
            var fresh = Preview(standard.Key, views);
            foreach (var row in fresh.Rows)
            {
                var original = selected.Single(r => r.UniqueId == row.UniqueId);
                if (original.CurrentTemplateUniqueId != row.CurrentTemplateUniqueId) throw new InvalidOperationException("視圖樣板已變更，請重新預覽。");
                row.Apply = true; row.ExceptionReason = original.ExceptionReason?.Trim();
            }
            DrawingStandardPolicy.ValidateApply(fresh, standard);
            var template = Doc.GetElement(standard.Template.UniqueId) as RevitView;
            using (var transaction = NewTransaction("HB 套用施工圖設定組合"))
            {
                foreach (var row in fresh.Rows)
                {
                    var view = (RevitView)Doc.GetElement(row.UniqueId);
                    if (string.IsNullOrWhiteSpace(row.ExceptionReason)) view.ViewTemplateId = template.Id;
                    data.Assignments.RemoveAll(a => a.ViewUniqueId == row.UniqueId);
                    data.Assignments.Add(new StandardAssignment { ViewUniqueId = row.UniqueId, StandardKey = standard.Key,
                        Revision = standard.Revision, ExceptionReason = row.ExceptionReason,
                        UpdatedAt = DateTimeOffset.Now.ToString("o"), UpdatedBy = Doc.Application.Username });
                }
                Doc.Regenerate();
                foreach (var row in fresh.Rows.Where(r => string.IsNullOrWhiteSpace(r.ExceptionReason)))
                    if (((RevitView)Doc.GetElement(row.UniqueId)).ViewTemplateId != template.Id)
                        throw new InvalidOperationException(row.ViewName + " 未成功套用樣板；本批全部復原。");
                Write(data); Commit(transaction);
            }
            return selected.Count;
        }

        private Transaction NewTransaction(string name)
        {
            var transaction = new Transaction(Doc, name); transaction.Start();
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new RollbackErrors()).SetClearAfterRollback(true));
            return transaction;
        }
        private static void Commit(Transaction transaction)
        {
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit 未完成交易，請確認模型權限及錯誤；本批未套用。");
        }
        private sealed class RollbackErrors : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failures) => failures.GetFailureMessages()
                .Any(f => f.GetSeverity() == FailureSeverity.Error) ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        private StandardReference Snapshot(string uid, string kind)
        {
            var element = Doc.GetElement(uid);
            if (element == null) return null;
            var reference = new StandardReference { UniqueId = uid, Kind = kind, Name = DisplayName(element) };
            var values = reference.Values;
            values["名稱"] = reference.Name;
            foreach (Parameter parameter in element.Parameters)
            {
                var key = "參數 / " + parameter.Definition.Name + " [" + parameter.Id.GetIdValue() + "]";
                values[key] = ParameterValue(parameter);
            }
            if (element is DimensionType dimension)
                values["尺寸格式"] = Describe(dimension.GetUnitsFormatOptions());
            if (element is DimensionType || element is TextNoteType)
                values["專案長度格式"] = Describe(Doc.GetUnits().GetFormatOptions(SpecTypeId.Length));
            if (element is RevitView view && view.IsTemplate)
            {
                values["不納管參數"] = string.Join(",", view.GetNonControlledTemplateParameterIds().Select(id => id.GetIdValue()).OrderBy(n => n));
                values["納管候選參數"] = string.Join(",", view.GetTemplateParameterIds().Select(id => id.GetIdValue()).OrderBy(n => n));
                foreach (Category category in Doc.Settings.Categories) CategoryValues(category);
                var filters = view.GetOrderedFilters().ToList();
                values["篩選器順序"] = string.Join(",", filters.Select(id => id.GetIdValue()));
                foreach (var id in filters)
                {
                    var filter = Doc.GetElement(id);
                    var prefix = "篩選器 / " + id.GetIdValue() + " / ";
                    values[prefix + "名稱"] = filter.Name;
                    values[prefix + "顯示"] = view.GetFilterVisibility(id).ToString();
                    values[prefix + "啟用"] = view.GetIsFilterEnabled(id).ToString();
                    values[prefix + "覆寫"] = Describe(view.GetFilterOverrides(id));
                    if (filter is ParameterFilterElement parameterFilter)
                    {
                        values[prefix + "分類"] = string.Join(",", parameterFilter.GetCategories().Select(c => c.GetIdValue()).OrderBy(n => n));
                        values[prefix + "條件"] = FilterText(parameterFilter.GetElementFilter());
                    }
                    else if (filter is SelectionFilterElement selectionFilter)
                        values[prefix + "選取元素"] = string.Join(",", selectionFilter.GetElementIds().Select(c => c.GetIdValue()).OrderBy(n => n));
                }
                void CategoryValues(Category category)
                {
                    if (view.CanCategoryBeHidden(category.Id))
                    {
                        var prefix = "分類 / " + category.Name + " [" + category.Id.GetIdValue() + "] / ";
                        values[prefix + "隱藏"] = view.GetCategoryHidden(category.Id).ToString();
                        values[prefix + "覆寫"] = Describe(view.GetCategoryOverrides(category.Id));
                    }
                    foreach (Category sub in category.SubCategories) CategoryValues(sub);
                }
            }
            return reference;
        }

        private static string ParameterValue(Parameter parameter)
        {
            if (!parameter.HasValue) return "（未設定）";
            switch (parameter.StorageType)
            {
                case StorageType.String: return parameter.AsString() ?? "";
                case StorageType.Integer: return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double: return parameter.AsDouble().ToString("R", CultureInfo.InvariantCulture);
                case StorageType.ElementId: return parameter.AsElementId().GetIdValue().ToString(CultureInfo.InvariantCulture);
                default: return "（無值）";
            }
        }

        private static string FilterText(ElementFilter filter)
        {
            if (filter == null) return "（無條件）";
            if (filter is ElementLogicalFilter logical) return filter.GetType().Name + "(" + string.Join(";", logical.GetFilters().Select(FilterText)) + ")";
            if (filter is ElementParameterFilter parameters)
                return "參數(" + parameters.Inverted + "):" + string.Join(";", parameters.GetRules().Select(RuleText));
            throw new InvalidOperationException("無法檢查篩選器條件：" + filter.GetType().Name);
        }

        private static string RuleText(FilterRule rule)
        {
            if (rule is FilterInverseRule inverse) return "NOT(" + RuleText(inverse.GetInnerRule()) + ")";
            var evaluator = rule.GetType().GetMethod("GetEvaluator", Type.EmptyTypes)?.Invoke(rule, null);
            return rule.GetType().Name + ":" + rule.GetRuleParameter().GetIdValue() + ":" + evaluator?.GetType().Name + ":" + Describe(rule);
        }

        // Only public scalar values are read; never serialize Revit objects or retain live handles.
        private static string Describe(object value)
        {
            var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && ScalarType(p.PropertyType)))
            {
                try { fields[property.Name] = Scalar(property.GetValue(value)); }
                catch (TargetInvocationException ex) when (ex.InnerException is Autodesk.Revit.Exceptions.InvalidOperationException)
                { fields[property.Name] = "（不適用）"; }
            }
            if (value is FormatOptions format && !format.UseDefault)
            { fields["UnitTypeId"] = format.GetUnitTypeId().TypeId; fields["SymbolTypeId"] = format.GetSymbolTypeId().TypeId; }
            return string.Join(" | ", fields.Select(p => p.Key + "=" + p.Value));
        }
        private static bool ScalarType(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string) ||
            type == typeof(ElementId) || type == typeof(Color) || type == typeof(ForgeTypeId);
        private static string Scalar(object value)
        {
            if (value == null) return "";
            if (value is ElementId id) return id.GetIdValue().ToString(CultureInfo.InvariantCulture);
            if (value is Color color) return color.IsValid ? color.Red + "," + color.Green + "," + color.Blue : "（未覆寫）";
            if (value is ForgeTypeId forge) return forge.TypeId;
            if (value is double number) return number.ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
