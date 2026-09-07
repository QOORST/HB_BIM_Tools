using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using OfficeOpenXml.Style;

using WinButton = System.Windows.Forms.Button;
using WinCheckBox = System.Windows.Forms.CheckBox;
using WinComboBox = System.Windows.Forms.ComboBox;
using WinForm = System.Windows.Forms.Form;
using WinLabel = System.Windows.Forms.Label;
using WinPanel = System.Windows.Forms.Panel;
using RevitView = Autodesk.Revit.DB.View;
using SysColor = System.Drawing.Color;

namespace YD_RevitTools.LicenseManager.Commands.Data.BimStandard
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdBimStandardAudit : IExternalCommand
    {
        private static readonly List<WinForm> OpenForms = new List<WinForm>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null)
            {
                message = "No active Revit document.";
                return Result.Failed;
            }

            if (!LicenseManager.Instance.HasFeatureAccess("Data.BimStandardAudit"))
            {
                TaskDialog.Show("BIM 標準檢查", "此功能需要 Trial 或以上授權。");
                return Result.Cancelled;
            }

            try
            {
                var form = new BimStandardAuditDialog(doc);
                OpenForms.Add(form);
                form.FormClosed += (s, e) => OpenForms.Remove(form);
                form.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("BIM 標準檢查", "執行失敗：\n" + ex.Message);
                return Result.Failed;
            }
        }

        private enum AuditScope
        {
            TemplateHealth,
            FamilyQuality,
            All
        }

        private enum AuditSeverity
        {
            Info,
            Warning,
            Error
        }

        private sealed class AuditIssue
        {
            public AuditSeverity Severity { get; set; }
            public string Scope { get; set; }
            public string Category { get; set; }
            public string ElementName { get; set; }
            public string Rule { get; set; }
            public string Message { get; set; }
            public string Recommendation { get; set; }
            public ElementId ElementId { get; set; }
        }

        private sealed class AuditSummary
        {
            public int Score { get; set; }
            public int TotalIssues { get; set; }
            public int Errors { get; set; }
            public int Warnings { get; set; }
            public int Infos { get; set; }
        }

        private sealed class BimStandardAuditDialog : WinForm
        {
            private readonly Document _doc;
            private readonly DataGridView _grid = new DataGridView();
            private readonly WinComboBox _scopeCombo = new WinComboBox();
            private readonly WinComboBox _severityCombo = new WinComboBox();
            private readonly WinCheckBox _requireYdPrefix = new WinCheckBox();
            private readonly WinLabel _summaryLabel = new WinLabel();
            private List<AuditIssue> _issues = new List<AuditIssue>();

            public BimStandardAuditDialog(Document doc)
            {
                _doc = doc;
                Text = "HB_BIM Tools - BIM 標準檢查";
                StartPosition = FormStartPosition.CenterScreen;
                MinimumSize = new Size(1180, 720);
                Size = new Size(1380, 820);
                BackColor = SysColor.FromArgb(245, 248, 252);
                Font = new Font("Microsoft JhengHei UI", 9.5F);

                BuildLayout();
                RunAudit();
            }

            private void BuildLayout()
            {
                SuspendLayout();

                var header = new WinPanel
                {
                    Dock = DockStyle.Top,
                    Height = 78,
                    BackColor = SysColor.FromArgb(5, 43, 68),
                    Padding = new Padding(18, 12, 18, 10)
                };

                var title = new WinLabel
                {
                    Text = "BIM 標準檢查",
                    ForeColor = SysColor.White,
                    Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
                    Dock = DockStyle.Top,
                    Height = 30
                };
                var subtitle = new WinLabel
                {
                    Text = "檢查樣板健康度、族群命名與模型資料標準，並輸出 Excel 報告。",
                    ForeColor = SysColor.FromArgb(210, 226, 238),
                    Dock = DockStyle.Fill
                };
                header.Controls.Add(subtitle);
                header.Controls.Add(title);

                var toolbar = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = 58,
                    BackColor = SysColor.White,
                    Padding = new Padding(14, 10, 14, 10),
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false
                };

                _scopeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                _scopeCombo.Width = 170;
                _scopeCombo.Items.AddRange(new object[] { "全部檢查", "樣板健康檢查", "族群品質檢查" });
                _scopeCombo.SelectedIndex = 0;
                _scopeCombo.SelectedIndexChanged += (s, e) => RunAudit();

                _severityCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                _severityCombo.Width = 130;
                _severityCombo.Items.AddRange(new object[] { "全部等級", "錯誤", "警告", "資訊" });
                _severityCombo.SelectedIndex = 0;
                _severityCombo.SelectedIndexChanged += (s, e) => ApplyFilter();

                _requireYdPrefix.Text = "檢查 YD_ 前綴";
                _requireYdPrefix.Width = 130;
                _requireYdPrefix.Checked = true;
                _requireYdPrefix.CheckedChanged += (s, e) => RunAudit();

                var refreshButton = BuildButton("重新檢查", 96);
                refreshButton.Click += (s, e) => RunAudit();

                var exportButton = BuildButton("匯出 Excel", 104);
                exportButton.Click += (s, e) => ExportReport();

                var openDocumentButton = BuildButton("文件資訊", 92);
                openDocumentButton.Click += (s, e) => ShowDocumentInfo();

                toolbar.Controls.Add(openDocumentButton);
                toolbar.Controls.Add(exportButton);
                toolbar.Controls.Add(refreshButton);
                toolbar.Controls.Add(_requireYdPrefix);
                toolbar.Controls.Add(_severityCombo);
                toolbar.Controls.Add(_scopeCombo);

                _summaryLabel.Dock = DockStyle.Top;
                _summaryLabel.Height = 34;
                _summaryLabel.Padding = new Padding(16, 7, 16, 0);
                _summaryLabel.BackColor = SysColor.FromArgb(232, 238, 245);
                _summaryLabel.ForeColor = SysColor.FromArgb(31, 50, 68);

                _grid.Dock = DockStyle.Fill;
                _grid.AllowUserToAddRows = false;
                _grid.AllowUserToDeleteRows = false;
                _grid.ReadOnly = true;
                _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                _grid.MultiSelect = false;
                _grid.AutoGenerateColumns = false;
                _grid.RowHeadersVisible = false;
                _grid.BackgroundColor = SysColor.White;
                _grid.BorderStyle = BorderStyle.None;
                _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
                _grid.CellDoubleClick += (s, e) => ZoomToSelectedElement();

                AddTextColumn("Severity", "等級", 70);
                AddTextColumn("Scope", "範圍", 90);
                AddTextColumn("Category", "類別", 130);
                AddTextColumn("ElementName", "項目", 230);
                AddTextColumn("Rule", "規則", 170);
                AddTextColumn("Message", "問題", 330);
                AddTextColumn("Recommendation", "建議", 330);

                Controls.Add(_grid);
                Controls.Add(_summaryLabel);
                Controls.Add(toolbar);
                Controls.Add(header);
                ResumeLayout();
            }

            private WinButton BuildButton(string text, int width)
            {
                return new WinButton
                {
                    Text = text,
                    Width = width,
                    Height = 32,
                    Margin = new Padding(8, 0, 0, 0),
                    BackColor = SysColor.FromArgb(31, 108, 159),
                    ForeColor = SysColor.White,
                    FlatStyle = FlatStyle.Flat
                };
            }

            private void AddTextColumn(string propertyName, string header, int width)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = propertyName,
                    HeaderText = header,
                    Width = width,
                    SortMode = DataGridViewColumnSortMode.Automatic
                });
            }

            private void RunAudit()
            {
                var scope = GetSelectedScope();
                _issues = BuildIssues(scope);
                ApplyFilter();
            }

            private AuditScope GetSelectedScope()
            {
                switch (_scopeCombo.SelectedIndex)
                {
                    case 1:
                        return AuditScope.TemplateHealth;
                    case 2:
                        return AuditScope.FamilyQuality;
                    default:
                        return AuditScope.All;
                }
            }

            private List<AuditIssue> BuildIssues(AuditScope scope)
            {
                var issues = new List<AuditIssue>();
                if (scope == AuditScope.All || scope == AuditScope.TemplateHealth)
                    AddTemplateHealthIssues(issues);
                if (scope == AuditScope.All || scope == AuditScope.FamilyQuality)
                    AddFamilyQualityIssues(issues);

                if (issues.Count == 0)
                {
                    issues.Add(new AuditIssue
                    {
                        Severity = AuditSeverity.Info,
                        Scope = "總覽",
                        Category = "檢查結果",
                        ElementName = _doc.Title,
                        Rule = "BIM 標準檢查",
                        Message = "未發現需要處理的問題。",
                        Recommendation = "可匯出報告作為目前模型或樣板的檢查紀錄。",
                        ElementId = ElementId.InvalidElementId
                    });
                }

                return issues;
            }

            private void AddTemplateHealthIssues(List<AuditIssue> issues)
            {
                CheckProjectInformation(issues);
                CheckProjectUnits(issues);
                CheckViewTemplates(issues);
                CheckViews(issues);
                CheckSheets(issues);
                CheckSchedules(issues);
                CheckMaterials(issues);
                CheckFilters(issues);
                CheckTextStyles(issues);
                CheckDimensionStyles(issues);
                CheckAnnotationFamilies(issues);
            }

            private void AddFamilyQualityIssues(List<AuditIssue> issues)
            {
                var symbols = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(symbol => symbol.Family != null)
                    .ToList();

                CheckEngineeringFamilyAndTypeNames(issues, symbols);

                foreach (var familyGroup in symbols.GroupBy(symbol => symbol.Family.Id))
                {
                    var first = familyGroup.First();
                    var family = first.Family;
                    var familyName = family.Name ?? string.Empty;
                    var category = first.Category?.Name ?? family.FamilyCategory?.Name ?? "";

                    if (_requireYdPrefix.Checked && !familyName.StartsWith("YD_", StringComparison.OrdinalIgnoreCase))
                    {
                        AddIssue(issues, AuditSeverity.Warning, "族群品質", category, familyName,
                            "族群命名前綴", "族群名稱未使用 YD_ 前綴。",
                            "正式庫族群建議使用 YD_ 前綴，便於辨識公司標準內容。", family.Id);
                    }

                    var typeCount = familyGroup.Count();
                    if (typeCount > 30)
                    {
                        AddIssue(issues, AuditSeverity.Warning, "族群品質", category, familyName,
                            "Type 數量", $"此族群包含 {typeCount} 個 Type，可能造成管理與載入負擔。",
                            "確認是否可拆分族群或刪除未使用 Type。", family.Id);
                    }

                    foreach (var symbol in familyGroup)
                    {
                        if (Regex.IsMatch(symbol.Name ?? "", @"^(Type\s*\d+|\d+)$", RegexOptions.IgnoreCase))
                        {
                            AddIssue(issues, AuditSeverity.Warning, "族群品質", category,
                                $"{familyName} : {symbol.Name}", "Type 命名",
                                "Type 名稱過於簡略，明細表與交付資料不易辨識。",
                                "Type 名稱建議包含尺寸、規格或關鍵性能資訊。", symbol.Id);
                        }

                        if (_requireYdPrefix.Checked)
                        {
                            var hasYdParameter = symbol.Parameters
                                .Cast<Parameter>()
                                .Any(parameter => parameter.Definition != null &&
                                    parameter.Definition.Name.StartsWith("YD_", StringComparison.OrdinalIgnoreCase));
                            if (!hasYdParameter)
                            {
                                AddIssue(issues, AuditSeverity.Info, "族群品質", category,
                                    $"{familyName} : {symbol.Name}", "公司參數",
                                    "此 Type 未偵測到 YD_ 公司參數。",
                                    "若此族群需進入正式庫，建議補齊公司共享參數。", symbol.Id);
                            }
                        }
                    }
                }
            }

            private void CheckProjectInformation(List<AuditIssue> issues)
            {
                var projectInfo = _doc.ProjectInformation;
                if (projectInfo == null)
                    return;

                CheckBuiltInTextParam(issues, projectInfo, BuiltInParameter.PROJECT_NAME, "專案名稱");
                CheckBuiltInTextParam(issues, projectInfo, BuiltInParameter.PROJECT_NUMBER, "專案編號");
                CheckBuiltInTextParam(issues, projectInfo, BuiltInParameter.PROJECT_STATUS, "專案狀態");
                CheckBuiltInTextParam(issues, projectInfo, BuiltInParameter.CLIENT_NAME, "業主名稱");
            }

            private void CheckBuiltInTextParam(List<AuditIssue> issues, Element element, BuiltInParameter builtInParameter, string displayName)
            {
                var parameter = element.get_Parameter(builtInParameter);
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.AsString()))
                {
                    AddIssue(issues, AuditSeverity.Warning, "樣板健康", "Project Information", displayName,
                        "專案資訊必填", $"{displayName} 尚未填寫。",
                        "在樣板或專案啟動時補齊，方便圖框、報表與交付資料引用。", element.Id);
                }
            }

            private void CheckProjectUnits(List<AuditIssue> issues)
            {
                var units = _doc.GetUnits();
                if (units == null)
                {
                    AddIssue(issues, AuditSeverity.Warning, "樣板健康", "Units", _doc.Title,
                        "專案單位", "無法讀取專案單位設定。",
                        "請確認樣板單位與公司標準一致。", ElementId.InvalidElementId);
                }
            }

            private void CheckViewTemplates(List<AuditIssue> issues)
            {
                var templates = new FilteredElementCollector(_doc)
                    .OfClass(typeof(RevitView))
                    .Cast<RevitView>()
                    .Where(view => view.IsTemplate)
                    .OrderBy(view => view.Name)
                    .ToList();

                if (templates.Count == 0)
                {
                    AddIssue(issues, AuditSeverity.Error, "樣板健康", "View Template", _doc.Title,
                        "視圖樣板", "目前文件沒有任何視圖樣板。",
                        "正式樣板應建立平面、剖面、立面、出圖與協調用途的視圖樣板。", ElementId.InvalidElementId);
                    return;
                }

                foreach (var template in templates)
                {
                    if (!template.Name.StartsWith("VT_", StringComparison.OrdinalIgnoreCase))
                    {
                        AddIssue(issues, AuditSeverity.Warning, "樣板健康", "View Template", template.Name,
                            "視圖樣板命名", "視圖樣板名稱未使用 VT_ 前綴。",
                            "建議格式：VT_ARCH_PLAN_施工圖_1-100。", template.Id);
                    }
                }
            }

            private void CheckViews(List<AuditIssue> issues)
            {
                var views = new FilteredElementCollector(_doc)
                    .OfClass(typeof(RevitView))
                    .Cast<RevitView>()
                    .Where(view => !view.IsTemplate && view.ViewType != ViewType.ProjectBrowser && view.ViewType != ViewType.SystemBrowser)
                    .ToList();

                var unnamed = views
                    .Where(view => Regex.IsMatch(view.Name ?? "", @"^(View|Floor Plan|Ceiling Plan|3D View|Section|Elevation)(\s*\d*)?$", RegexOptions.IgnoreCase))
                    .Take(30);

                foreach (var view in unnamed)
                {
                    AddIssue(issues, AuditSeverity.Info, "樣板健康", "View", view.Name,
                        "視圖命名", "視圖名稱疑似仍為預設名稱。",
                        "專案交付視圖建議依專業、圖種、樓層與比例命名。", view.Id);
                }
            }

            private void CheckSheets(List<AuditIssue> issues)
            {
                var sheets = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(sheet => !sheet.IsPlaceholder)
                    .ToList();

                foreach (var sheet in sheets.Where(sheet => string.IsNullOrWhiteSpace(sheet.SheetNumber) || string.IsNullOrWhiteSpace(sheet.Name)))
                {
                    AddIssue(issues, AuditSeverity.Warning, "樣板健康", "Sheet", sheet.SheetNumber,
                        "圖紙資料", "圖紙編號或圖名未完整填寫。",
                        "出圖樣板應確保圖紙清單可直接被報表與圖框引用。", sheet.Id);
                }
            }

            private void CheckSchedules(List<AuditIssue> issues)
            {
                var schedules = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(schedule => !schedule.IsTemplate && !schedule.IsTitleblockRevisionSchedule)
                    .ToList();

                foreach (var schedule in schedules.Where(schedule => schedule.Name.IndexOf("Schedule", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    AddIssue(issues, AuditSeverity.Info, "樣板健康", "Schedule", schedule.Name,
                        "明細表命名", "明細表名稱含預設英文名稱。",
                        "正式樣板建議使用專業與用途清楚的明細表命名。", schedule.Id);
                }
            }

            private void CheckMaterials(List<AuditIssue> issues)
            {
                var materials = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .ToList();

                var duplicates = materials
                    .GroupBy(material => NormalizeName(material.Name))
                    .Where(group => group.Count() > 1 && !string.IsNullOrWhiteSpace(group.Key))
                    .Take(30);

                foreach (var group in duplicates)
                {
                    AddIssue(issues, AuditSeverity.Warning, "樣板健康", "Material", string.Join(", ", group.Select(item => item.Name).Take(4)),
                        "材質重複", "偵測到名稱近似的材質。",
                        "請確認是否可合併，避免數量、視覺與渲染設定分散。", group.First().Id);
                }

                if (_requireYdPrefix.Checked)
                {
                    foreach (var material in materials.Where(material => !material.Name.StartsWith("YD_", StringComparison.OrdinalIgnoreCase)).Take(30))
                    {
                        AddIssue(issues, AuditSeverity.Info, "樣板健康", "Material", material.Name,
                            "材質命名前綴", "材質名稱未使用 YD_ 前綴。",
                            "公司標準材質建議使用 YD_ 前綴；專案臨時材質可另行標示。", material.Id);
                    }
                }
            }

            private void CheckFilters(List<AuditIssue> issues)
            {
                var filters = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .ToList();

                foreach (var filter in filters.Where(filter => !filter.Name.StartsWith("FL_", StringComparison.OrdinalIgnoreCase)).Take(30))
                {
                    AddIssue(issues, AuditSeverity.Info, "樣板健康", "Filter", filter.Name,
                        "篩選器命名", "篩選器名稱未使用 FL_ 前綴。",
                        "建議篩選器命名可區分專業、用途與顯示規則。", filter.Id);
                }
            }

            private void CheckTextStyles(List<AuditIssue> issues)
            {
                var textTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();

                foreach (var textType in textTypes.Where(type => Regex.IsMatch(type.Name ?? "", @"^(Text|文字)\s*\d*$", RegexOptions.IgnoreCase)))
                {
                    AddIssue(issues, AuditSeverity.Info, "樣板健康", "Text Style", textType.Name,
                        "文字樣式命名", "文字樣式疑似仍為預設命名。",
                        "建議文字樣式包含用途與字高，例如 TXT_NOTE_2.5mm。", textType.Id);
                }
            }

            private void CheckEngineeringFamilyAndTypeNames(List<AuditIssue> issues, List<FamilySymbol> symbols)
            {
                foreach (var symbol in symbols)
                {
                    var familyName = symbol.Family?.Name ?? "";
                    var typeName = symbol.Name ?? "";
                    var category = symbol.Category?.Name ?? symbol.Family?.FamilyCategory?.Name ?? "";

                    if (IsWeakEngineeringName(familyName))
                    {
                        AddIssue(issues, AuditSeverity.Warning, "族群/類型", category, familyName,
                            "族群名稱可讀性", "族群名稱未清楚表達工程用途或元件類別。",
                            "建議族群名稱包含專業、類別與用途，例如 MEP_DuctAccessory_Damper、YD_PipeValve_Gate。", symbol.Family?.Id ?? symbol.Id);
                    }

                    if (IsWeakEngineeringName(typeName))
                    {
                        AddIssue(issues, AuditSeverity.Warning, "族群/類型", category, $"{familyName} : {typeName}",
                            "類型名稱可讀性", "類型名稱未包含尺寸、規格或工程用途，後續明細表與交付資料不易判讀。",
                            "建議類型名稱至少包含尺寸/容量/規格/型號，例如 100A_SCH40、600x300、DN50_GV。", symbol.Id);
                    }
                }

                var duplicatedTypeNames = symbols
                    .GroupBy(symbol => $"{symbol.Category?.Name}|{NormalizeName(symbol.Name)}")
                    .Where(group => group.Count() > 1 && !string.IsNullOrWhiteSpace(group.Key))
                    .Take(50);

                foreach (var group in duplicatedTypeNames)
                {
                    var sample = group.Take(4).Select(symbol => $"{symbol.Family.Name}:{symbol.Name}");
                    AddIssue(issues, AuditSeverity.Info, "族群/類型", group.First().Category?.Name ?? "",
                        string.Join(", ", sample), "類型同名",
                        "同一類別中偵測到相同或近似的 Type 名稱，可能造成明細表與選型混淆。",
                        "若不同族群共用相同 Type 名稱，建議加入規格、系統或廠牌識別。", group.First().Id);
                }
            }

            private void CheckDimensionStyles(List<AuditIssue> issues)
            {
                var dimensionTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .OrderBy(type => type.Name)
                    .ToList();

                if (dimensionTypes.Count == 0)
                {
                    AddIssue(issues, AuditSeverity.Error, "標註及尺寸", "Dimension Type", _doc.Title,
                        "尺寸樣式缺失", "文件中未偵測到尺寸樣式。",
                        "樣板至少應包含一般尺寸、連續尺寸、標高/定位用途尺寸樣式。", ElementId.InvalidElementId);
                    return;
                }

                foreach (var type in dimensionTypes.Where(type => IsWeakEngineeringName(type.Name) || Regex.IsMatch(type.Name ?? "", @"^(Linear|Dimension|尺寸)\s*\d*$", RegexOptions.IgnoreCase)).Take(60))
                {
                    AddIssue(issues, AuditSeverity.Warning, "標註及尺寸", "Dimension Type", type.Name,
                        "尺寸樣式命名", "尺寸樣式名稱不易辨識工程用途。",
                        "建議尺寸樣式名稱包含用途與字高/單位，例如 DIM_PLAN_2.5mm、DIM_DETAIL_1.8mm。", type.Id);
                }

                var duplicates = dimensionTypes
                    .GroupBy(type => NormalizeName(type.Name))
                    .Where(group => group.Count() > 1 && !string.IsNullOrWhiteSpace(group.Key));
                foreach (var group in duplicates)
                {
                    AddIssue(issues, AuditSeverity.Info, "標註及尺寸", "Dimension Type", group.First().Name,
                        "尺寸樣式近似重複", "偵測到近似重複的尺寸樣式名稱。",
                        "請確認是否為同用途樣式，避免出圖標準分散。", group.First().Id);
                }
            }

            private void CheckAnnotationFamilies(List<AuditIssue> issues)
            {
                CheckEngineeringViewsAndSheets(issues);
                CheckEngineeringSchedules(issues);

                var annotationSymbols = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(symbol => symbol.Category != null && symbol.Category.CategoryType == CategoryType.Annotation)
                    .ToList();

                foreach (var symbol in annotationSymbols.Where(symbol => IsWeakEngineeringName(symbol.Family?.Name) || IsWeakEngineeringName(symbol.Name)).Take(80))
                {
                    AddIssue(issues, AuditSeverity.Warning, "標註及尺寸", symbol.Category?.Name ?? "Annotation",
                        $"{symbol.Family?.Name} : {symbol.Name}", "標註族群命名",
                        "標註族群或類型名稱不易辨識用途。",
                        "標註族群建議依標高、剖面、詳圖索引、設備標籤、管線標籤等用途命名。", symbol.Id);
                }

                var textTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();

                foreach (var textType in textTypes.Where(type => IsWeakEngineeringName(type.Name)).Take(50))
                {
                    AddIssue(issues, AuditSeverity.Warning, "標註及尺寸", "Text Style", textType.Name,
                        "文字樣式命名", "文字樣式名稱未清楚標示用途或字高。",
                        "建議文字樣式包含用途與字高，例如 TXT_NOTE_2.5mm、TXT_TITLE_5mm。", textType.Id);
                }
            }

            private void CheckEngineeringViewsAndSheets(List<AuditIssue> issues)
            {
                var views = new FilteredElementCollector(_doc)
                    .OfClass(typeof(RevitView))
                    .Cast<RevitView>()
                    .Where(view => !view.IsTemplate && view.ViewType != ViewType.ProjectBrowser && view.ViewType != ViewType.SystemBrowser)
                    .ToList();

                var duplicateViewNames = views
                    .GroupBy(view => NormalizeName(view.Name))
                    .Where(group => group.Count() > 1 && !string.IsNullOrWhiteSpace(group.Key))
                    .Take(50);
                foreach (var group in duplicateViewNames)
                {
                    AddIssue(issues, AuditSeverity.Warning, "視圖", "View", group.First().Name,
                        "視圖重複命名", "偵測到近似或重複的視圖名稱。",
                        "視圖名稱建議包含專業、圖種、樓層、用途與比例，避免出圖或協調時選錯視圖。", group.First().Id);
                }

                foreach (var view in views
                    .Where(view => IsDocumentationView(view) && view.ViewTemplateId == ElementId.InvalidElementId)
                    .Take(80))
                {
                    AddIssue(issues, AuditSeverity.Warning, "視圖", view.ViewType.ToString(), view.Name,
                        "未套用視圖樣板", "施工圖/協調常用視圖未套用 View Template。",
                        "樣板應以視圖樣板控制顯示、比例、過濾器與標註風格，減少人工設定差異。", view.Id);
                }

                var sheets = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(sheet => !sheet.IsPlaceholder)
                    .ToList();

                var duplicatedSheetNumbers = sheets
                    .GroupBy(sheet => NormalizeName(sheet.SheetNumber))
                    .Where(group => group.Count() > 1 && !string.IsNullOrWhiteSpace(group.Key));
                foreach (var group in duplicatedSheetNumbers)
                {
                    AddIssue(issues, AuditSeverity.Error, "圖紙", "Sheet", group.First().SheetNumber,
                        "圖號重複", "偵測到重複或近似的圖紙編號。",
                        "圖號必須唯一，否則會影響圖紙清單、出圖與交付索引。", group.First().Id);
                }

                foreach (var sheet in sheets.Where(sheet => IsWeakEngineeringName(sheet.Name)).Take(50))
                {
                    AddIssue(issues, AuditSeverity.Warning, "圖紙", "Sheet", $"{sheet.SheetNumber} - {sheet.Name}",
                        "圖名可讀性", "圖紙名稱未清楚表達專業、系統或圖種。",
                        "建議圖名包含專業與用途，例如 給排水平面圖、空調風管平面圖、電力平面圖。", sheet.Id);
                }
            }

            private void CheckEngineeringSchedules(List<AuditIssue> issues)
            {
                var schedules = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(schedule => !schedule.IsTemplate && !schedule.IsTitleblockRevisionSchedule)
                    .ToList();

                foreach (var schedule in schedules)
                {
                    var fieldNames = GetScheduleFieldNames(schedule);
                    if (fieldNames.Count == 0)
                    {
                        AddIssue(issues, AuditSeverity.Warning, "明細表", "Schedule", schedule.Name,
                            "明細表欄位", "明細表沒有可讀取欄位或欄位架構異常。",
                            "請確認此明細表是否仍需保留，或補齊交付所需欄位。", schedule.Id);
                        continue;
                    }

                    if (fieldNames.Count < 3)
                    {
                        AddIssue(issues, AuditSeverity.Info, "明細表", "Schedule", schedule.Name,
                            "明細表欄位不足", $"此明細表僅有 {fieldNames.Count} 個欄位。",
                            "工程明細表通常應至少包含名稱/類型、規格、數量或系統資訊。", schedule.Id);
                    }

                    var hasQuantityField = fieldNames.Any(name => ContainsAny(name, "Count", "數量", "Quantity", "Length", "Area", "Volume", "長度", "面積", "體積"));
                    if (!hasQuantityField)
                    {
                        AddIssue(issues, AuditSeverity.Warning, "明細表", "Schedule", schedule.Name,
                            "缺少數量欄位", "明細表未偵測到數量、長度、面積或體積相關欄位。",
                            "若此表用於估算或交付，建議補齊可核對的數量欄位。", schedule.Id);
                    }

                    var hasIdentityField = fieldNames.Any(name => ContainsAny(name, "Family", "Type", "Name", "Mark", "族群", "類型", "名稱", "編號", "標記"));
                    if (!hasIdentityField)
                    {
                        AddIssue(issues, AuditSeverity.Warning, "明細表", "Schedule", schedule.Name,
                            "缺少識別欄位", "明細表未偵測到族群、類型、名稱、Mark 或編號欄位。",
                            "交付明細表應提供足夠識別資訊，讓模型元素與工程項目可回查。", schedule.Id);
                    }
                }
            }

            private static List<string> GetScheduleFieldNames(ViewSchedule schedule)
            {
                var names = new List<string>();
                try
                {
                    var definition = schedule.Definition;
                    if (definition == null)
                        return names;

                    for (int index = 0; index < definition.GetFieldCount(); index++)
                    {
                        var field = definition.GetField(index);
                        if (field == null || field.IsHidden)
                            continue;

                        var name = field.GetName();
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name.Trim());
                    }
                }
                catch
                {
                    return names;
                }

                return names;
            }

            private static bool IsDocumentationView(RevitView view)
            {
                return view.ViewType == ViewType.FloorPlan ||
                       view.ViewType == ViewType.CeilingPlan ||
                       view.ViewType == ViewType.EngineeringPlan ||
                       view.ViewType == ViewType.Section ||
                       view.ViewType == ViewType.Elevation ||
                       view.ViewType == ViewType.Detail ||
                       view.ViewType == ViewType.ThreeD;
            }

            private static bool IsWeakEngineeringName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return true;

                var trimmed = name.Trim();
                if (trimmed.Length <= 2)
                    return true;

                return Regex.IsMatch(trimmed, @"^(Type\s*\d+|Default|Standard|Generic|New|Copy\s*\d*|文字\s*\d*|類型\s*\d*|標準\s*\d*)$", RegexOptions.IgnoreCase);
            }

            private static bool ContainsAny(string text, params string[] tokens)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                return tokens.Any(token => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            private static string NormalizeName(string name)
            {
                return Regex.Replace(name ?? "", @"[\s_\-\.]+", "").ToUpperInvariant();
            }

            private static void AddIssue(
                List<AuditIssue> issues,
                AuditSeverity severity,
                string scope,
                string category,
                string elementName,
                string rule,
                string message,
                string recommendation,
                ElementId elementId)
            {
                issues.Add(new AuditIssue
                {
                    Severity = severity,
                    Scope = scope,
                    Category = category,
                    ElementName = elementName,
                    Rule = rule,
                    Message = message,
                    Recommendation = recommendation,
                    ElementId = elementId ?? ElementId.InvalidElementId
                });
            }

            private void ApplyFilter()
            {
                IEnumerable<AuditIssue> rows = _issues;
                switch (_severityCombo.SelectedIndex)
                {
                    case 1:
                        rows = rows.Where(row => row.Severity == AuditSeverity.Error);
                        break;
                    case 2:
                        rows = rows.Where(row => row.Severity == AuditSeverity.Warning);
                        break;
                    case 3:
                        rows = rows.Where(row => row.Severity == AuditSeverity.Info);
                        break;
                }

                var filtered = rows.ToList();
                _grid.DataSource = filtered;
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (!(row.DataBoundItem is AuditIssue issue))
                        continue;
                    row.DefaultCellStyle.BackColor = issue.Severity == AuditSeverity.Error
                        ? SysColor.FromArgb(255, 235, 235)
                        : issue.Severity == AuditSeverity.Warning
                            ? SysColor.FromArgb(255, 248, 224)
                            : SysColor.White;
                }

                var summary = BuildSummary(_issues);
                _summaryLabel.Text = $"健康分數：{summary.Score} / 100    問題總數：{summary.TotalIssues}    錯誤：{summary.Errors}    警告：{summary.Warnings}    資訊：{summary.Infos}    目前顯示：{filtered.Count}";
            }

            private static AuditSummary BuildSummary(List<AuditIssue> issues)
            {
                var errors = issues.Count(issue => issue.Severity == AuditSeverity.Error);
                var warnings = issues.Count(issue => issue.Severity == AuditSeverity.Warning);
                var infos = issues.Count(issue => issue.Severity == AuditSeverity.Info);
                var score = Math.Max(0, 100 - errors * 12 - warnings * 4 - infos);
                return new AuditSummary
                {
                    Score = score,
                    TotalIssues = issues.Count,
                    Errors = errors,
                    Warnings = warnings,
                    Infos = infos
                };
            }

            private void ExportReport()
            {
                if (!LicenseManager.Instance.HasFeatureAccess("Data.BimStandardReport"))
                {
                    MessageBox.Show("匯出 Excel 報告需要 Standard 或 Professional 授權。", "BIM 標準檢查",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (var dialog = new SaveFileDialog())
                {
                    dialog.Filter = "Excel (*.xlsx)|*.xlsx";
                    dialog.FileName = $"BIM_Standard_Audit_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    dialog.DefaultExt = "xlsx";
                    dialog.Title = "匯出 BIM 標準檢查報告";

                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;

                    try
                    {
                        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                        WriteExcelReport(dialog.FileName);

                        var result = MessageBox.Show("報告已匯出完成，是否開啟所在資料夾？", "BIM 標準檢查",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (result == DialogResult.Yes)
                        {
                            Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\"");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("匯出失敗：\n" + ex.Message, "BIM 標準檢查",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            private void WriteExcelReport(string filePath)
            {
                var fileInfo = new FileInfo(filePath);
                using (var package = new ExcelPackage(fileInfo))
                {
                    var summary = BuildSummary(_issues);
                    var summarySheet = package.Workbook.Worksheets.Add("Summary");
                    summarySheet.Cells[1, 1].Value = "HB_BIM Tools - BIM 標準檢查";
                    summarySheet.Cells[1, 1, 1, 4].Merge = true;
                    summarySheet.Cells[1, 1].Style.Font.Bold = true;
                    summarySheet.Cells[1, 1].Style.Font.Size = 16;

                    var summaryRows = new[]
                    {
                        new[] { "Document", _doc.Title },
                        new[] { "ExportTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                        new[] { "HealthScore", summary.Score.ToString() },
                        new[] { "TotalIssues", summary.TotalIssues.ToString() },
                        new[] { "Errors", summary.Errors.ToString() },
                        new[] { "Warnings", summary.Warnings.ToString() },
                        new[] { "Infos", summary.Infos.ToString() }
                    };

                    for (int i = 0; i < summaryRows.Length; i++)
                    {
                        summarySheet.Cells[i + 3, 1].Value = summaryRows[i][0];
                        summarySheet.Cells[i + 3, 2].Value = summaryRows[i][1];
                    }
                    summarySheet.Cells[3, 1, summaryRows.Length + 2, 1].Style.Font.Bold = true;
                    summarySheet.Cells.AutoFitColumns();

                    var issueSheet = package.Workbook.Worksheets.Add("Issues");
                    var headers = new[] { "Severity", "Scope", "Category", "ElementName", "Rule", "Message", "Recommendation", "ElementId" };
                    for (int col = 0; col < headers.Length; col++)
                    {
                        var cell = issueSheet.Cells[1, col + 1];
                        cell.Value = headers[col];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(SysColor.FromArgb(31, 73, 125));
                        cell.Style.Font.Color.SetColor(SysColor.White);
                    }

                    for (int row = 0; row < _issues.Count; row++)
                    {
                        var issue = _issues[row];
                        var excelRow = row + 2;
                        issueSheet.Cells[excelRow, 1].Value = issue.Severity.ToString();
                        issueSheet.Cells[excelRow, 2].Value = issue.Scope;
                        issueSheet.Cells[excelRow, 3].Value = issue.Category;
                        issueSheet.Cells[excelRow, 4].Value = issue.ElementName;
                        issueSheet.Cells[excelRow, 5].Value = issue.Rule;
                        issueSheet.Cells[excelRow, 6].Value = issue.Message;
                        issueSheet.Cells[excelRow, 7].Value = issue.Recommendation;
                        issueSheet.Cells[excelRow, 8].Value = ElementIdToString(issue.ElementId);
                    }

                    issueSheet.View.FreezePanes(2, 1);
                    issueSheet.Cells[issueSheet.Dimension.Address].AutoFitColumns();
                    package.Save();
                }
            }

            private void ShowDocumentInfo()
            {
                var centralPath = string.Empty;
                try
                {
                    centralPath = _doc.IsWorkshared ? ModelPathUtils.ConvertModelPathToUserVisiblePath(_doc.GetWorksharingCentralModelPath()) : "";
                }
                catch
                {
                    centralPath = "";
                }

                MessageBox.Show(
                    $"文件：{_doc.Title}\n" +
                    $"族群文件：{(_doc.IsFamilyDocument ? "是" : "否")}\n" +
                    $"工作共享：{(_doc.IsWorkshared ? "是" : "否")}\n" +
                    $"路徑：{_doc.PathName}\n" +
                    $"中心檔：{centralPath}",
                    "文件資訊",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            private void ZoomToSelectedElement()
            {
                if (_grid.CurrentRow?.DataBoundItem is AuditIssue issue &&
                    issue.ElementId != null &&
                    issue.ElementId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var uidoc = new UIDocument(_doc);
                        uidoc.Selection.SetElementIds(new[] { issue.ElementId });
                        uidoc.ShowElements(issue.ElementId);
                    }
                    catch
                    {
                        MessageBox.Show("此項目無法在目前視圖中定位。", "BIM 標準檢查",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }

            private static string ElementIdToString(ElementId id)
            {
                if (id == null || id == ElementId.InvalidElementId)
                    return "";
#if REVIT2024 || REVIT2025 || REVIT2026
                return id.Value.ToString();
#else
                return id.IntegerValue.ToString();
#endif
            }
        }
    }
}
