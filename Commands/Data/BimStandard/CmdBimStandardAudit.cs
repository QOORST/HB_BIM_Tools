using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using YD_RevitTools.LicenseManager.Helpers;
using RevitView = Autodesk.Revit.DB.View;
using RevitFamily = Autodesk.Revit.DB.Family;

namespace YD_RevitTools.LicenseManager.Commands.Data.BimStandard
{
    // Keep the command class and license keys stable for existing installations.
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdBimStandardAudit : IExternalCommand
    {
        private sealed class Owner : IWin32Window { public IntPtr Handle { get; set; } }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument)
            { TaskDialog.Show("模型管理檢查", "請先開啟 Revit 專案模型。"); return Result.Cancelled; }
            if (!LicenseManager.Instance.HasFeatureAccess("Data.BimStandardAudit"))
            { TaskDialog.Show("模型管理檢查", "此功能需要 Trial 或以上授權。"); return Result.Cancelled; }
            try
            {
                ShowAudit(data.Application);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; TaskDialog.Show("模型管理檢查", ex.Message); return Result.Failed; }
        }
        internal static ModelAuditContext ShowAudit(UIApplication app, bool allowOrganize = true, ModelAuditContext context = null)
        {
            if (!LicenseManager.Instance.HasFeatureAccess("Data.BimStandardAudit"))
                throw new InvalidOperationException("模型管理檢查需要 Trial 或以上授權。");
            var ui = app.ActiveUIDocument;
            var service = new ModelAuditService(ui.Document);
            Element target = null;
            string organizeId = null;
            using (var form = new ModelAuditForm(ui.Document.Title, service.Check,
                issue => target = service.ResolveLocation(issue), service.Export,
                allowOrganize ? (Action<ModelAuditIssue>)(issue => {
                    if (!LicenseManager.Instance.HasFeatureAccess("Data.ModelManager"))
                        throw new InvalidOperationException("名稱整理需要 Standard 或 Professional 授權。");
                    if (!NameOrganizer.NameOrganizerService.Supports(ui.Document.GetElement(issue.UniqueId)))
                        throw new InvalidOperationException("此項目無法帶入名稱整理，請重新檢查。");
                    organizeId = issue.UniqueId;
                }) : null, context))
            {
                form.ShowDialog(new Owner { Handle = app.MainWindowHandle });
                context = form.Context;
            }
            if (organizeId != null) CmdModelDataManager.Open(app, organizeId, context);
            else if (target is RevitView view) ui.ActiveView = view;
            else if (target != null) { ui.Selection.SetElementIds(new[] { target.Id }); ui.ShowElements(target.Id); }
            return context;
        }
    }

    internal sealed class ModelAuditService
    {
        private readonly Document _doc;
        internal ModelAuditService(Document doc) { _doc = doc; }
        internal List<ModelAuditIssue> Check(ModelAuditGroup group, ModelAuditOptions options)
        {
            var issues = new List<ModelAuditIssue>();
            switch (group)
            {
                case ModelAuditGroup.Project: Project(issues); break;
                case ModelAuditGroup.Naming: Naming(issues, options); break;
                case ModelAuditGroup.Families: Families(issues); break;
                default: throw new InvalidOperationException("請選擇檢查項目。");
            }
            return issues;
        }

        private void Project(List<ModelAuditIssue> issues)
        {
            var info = _doc.ProjectInformation;
            if (info == null) Add(issues, null, "專案資訊", "無法讀取專案資訊。", "請確認目前為專案文件。", true);
            else
            {
                CheckText(BuiltInParameter.PROJECT_NAME, "專案名稱");
                CheckText(BuiltInParameter.PROJECT_NUMBER, "專案編號");
                CheckText(BuiltInParameter.PROJECT_STATUS, "專案狀態");
                CheckText(BuiltInParameter.CLIENT_NAME, "業主名稱");
                void CheckText(BuiltInParameter id, string label)
                {
                    var parameter = info.get_Parameter(id);
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.AsString()))
                        Add(issues, info, "專案基本資訊", label + "尚未填寫。", "若專案需要此資訊，請由「管理 → 專案資訊」填寫；不適用可填理由略過。", name: label);
                }
            }
            foreach (var schedule in new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule))
            {
                try
                {
                    var definition = schedule.Definition;
                    if (definition == null) { Add(issues, schedule, "明細表可讀性", "無法讀取明細表定義。", "開啟明細表確認內容；無法讀取時請檢查模型備份。", true); continue; }
                    var visible = Enumerable.Range(0, definition.GetFieldCount()).Count(i => !definition.GetField(i).IsHidden);
                    if (visible == 0) Add(issues, schedule, "明細表可讀性", "明細表沒有可見欄位。", "確認是否為暫存表或刻意隱藏；需要使用時請檢查欄位設定。");
                }
                catch (Exception ex) { Add(issues, schedule, "明細表可讀性", "無法完整讀取欄位：" + ex.Message, "請開啟此明細表確認；此結果不代表內容已通過檢查。", true); }
            }
        }

        private void Naming(List<ModelAuditIssue> issues, ModelAuditOptions options)
        {
            foreach (var family in new FilteredElementCollector(_doc).OfClass(typeof(RevitFamily)).Cast<RevitFamily>())
                Inspect(family, options.FamilyPrefix, "族群");
            foreach (var view in new FilteredElementCollector(_doc).OfClass(typeof(RevitView)).Cast<RevitView>()
                .Where(v => v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser))
                Inspect(view, view.IsTemplate ? options.TemplatePrefix : "", view.IsTemplate ? "視圖樣板" : "視圖／圖紙");
            foreach (var filter in new FilteredElementCollector(_doc).OfClass(typeof(FilterElement))) Inspect(filter, options.FilterPrefix, "篩選器");
            foreach (var material in new FilteredElementCollector(_doc).OfClass(typeof(Material))) Inspect(material, options.MaterialPrefix, "材質");
            foreach (var type in new FilteredElementCollector(_doc).WhereElementIsElementType())
                if (type is FamilySymbol || type is TextNoteType || type is DimensionType) Inspect(type, "", "類型");

            void Inspect(Element element, string prefix, string category)
            {
                if (ModelAuditOptions.PrefixMismatch(element.Name, prefix))
                    Add(issues, element, category + "前綴", "名稱未使用本次指定前綴「" + prefix.Trim() + "」。",
                        "先確認此前綴適用於此內容；外部提供或專案例外可填理由略過。工具不自動改名。");
                else if (options.ReadabilityHints && WeakName(element.Name))
                    Add(issues, element, category + "可讀性", "名稱較簡略或疑似預設名稱，建議確認用途是否易辨識。",
                        "需要時依專案約定補充用途或規格；既有標準名稱可保留並略過此提醒。");
            }
        }
        private static bool WeakName(string name) => string.IsNullOrWhiteSpace(name) ||
            Regex.IsMatch(name.Trim(), @"^(Type\s*\d+|Default|Standard|Generic|New|Copy\s*\d*|文字\s*\d*|類型\s*\d*|標準\s*\d*)$", RegexOptions.IgnoreCase);

        private void Families(List<ModelAuditIssue> issues)
        {
            var symbols = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().Where(s => s.Family != null).ToList();
            foreach (var group in symbols.GroupBy(s => s.Family.Id).Where(g => g.Count() > 30))
                Add(issues, group.First().Family, "類型數量", "此族群有 " + group.Count() + " 個類型，供整理時確認。",
                    "類型多不代表品質有問題。請確認是否為完整型錄；不要只依數量刪除類型。");
            foreach (var group in symbols.GroupBy(s => (s.Category?.Id.GetIdValue().ToString() ?? "") + "|" + Normalize(s.Name))
                .Where(g => g.Select(s => s.Family.Id).Distinct().Count() > 1))
            {
                var first = group.First();
                Add(issues, first, "跨族群同名類型", "同分類的 " + group.Select(s => s.Family.Id).Distinct().Count() + " 個族群共用類型名稱「" + first.Name + "」。",
                    "確認選型或明細表是否會混淆。相同規格可以同名，必要時於明細表同時列出族群與類型。涉及：" +
                    string.Join("、", group.Select(s => s.Family.Name).Distinct()));
            }
        }
        private static string Normalize(string name) => Regex.Replace(name ?? "", @"[\s_\-\.]+", "").ToUpperInvariant();
        private void Add(List<ModelAuditIssue> issues, Element element, string rule, string message, string recommendation, bool action = false, string name = null)
        {
            issues.Add(new ModelAuditIssue { CanOrganize = NameOrganizer.NameOrganizerService.Supports(element), NeedsAction = action, Rule = rule, Message = message, Recommendation = recommendation,
                ElementName = name ?? (element is FamilySymbol symbol ? symbol.FamilyName + " : " + symbol.Name : element?.Name) ?? _doc.Title,
                Category = element?.Category?.Name ?? "專案內容", UniqueId = element?.UniqueId, ElementId = element?.Id.GetIdValue().ToString() ?? "" });
        }

        internal Element ResolveLocation(ModelAuditIssue issue)
        {
            var element = issue == null || string.IsNullOrWhiteSpace(issue.UniqueId) ? null : _doc.GetElement(issue.UniqueId);
            if (element == null) throw new InvalidOperationException("此項目已不存在，請重新檢查。");
            if (element is ProjectInfo) throw new InvalidOperationException("請由 Revit「管理 → 專案資訊」查看及填寫此欄位。");
            if (element is RevitView view)
            {
                if (view.IsTemplate) throw new InvalidOperationException("此項目為視圖樣板，請由 Revit「視圖 → 視圖樣板 → 管理視圖樣板」查看。");
                return view;
            }
            if (element is RevitFamily family)
            {
                var types = new HashSet<ElementId>(family.GetFamilySymbolIds());
                element = new FilteredElementCollector(_doc).OfClass(typeof(FamilyInstance)).FirstOrDefault(e => types.Contains(e.GetTypeId()));
            }
            else if (element is ElementType type)
                element = new FilteredElementCollector(_doc).WhereElementIsNotElementType().FirstOrDefault(e => e.GetTypeId() == type.Id);
            if (element == null) throw new InvalidOperationException("此族群／類型沒有可定位的實例，請於專案瀏覽器查看內容。");
            if (element is Material || element is FilterElement)
                throw new InvalidOperationException("此項目為設定內容，請由 Revit 材質或篩選器管理介面查看。");
            return element;
        }

        internal void Export(ModelAuditGroup group, ModelAuditOptions options, IReadOnlyList<ModelAuditIssue> issues, IWin32Window owner)
        {
            if (!LicenseManager.Instance.HasFeatureAccess("Data.BimStandardReport"))
                throw new InvalidOperationException("匯出報告需要 Standard 或 Professional 授權。");
            using (var dialog = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", DefaultExt = "xlsx",
                FileName = "模型管理檢查_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx", Title = "匯出模型管理檢查報告" })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                ModelAuditReport.Write(dialog.FileName, _doc.Title, group, options, issues);
                MessageBox.Show(owner, "報告已匯出，包含本次略過原因。", "模型管理檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
