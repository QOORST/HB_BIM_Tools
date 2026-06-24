using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager;
using YD_RevitTools.LicenseManager.Helpers.Data;
using OfficeOpenXml;

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdCobieExportEnhanced : IExternalCommand
    {
        private static readonly List<System.Windows.Forms.Form> OpenExportForms = new List<System.Windows.Forms.Form>();

        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet set)
        {
            // 檢查授權 - COBie 匯出功能
            var licenseManager = YD_RevitTools.LicenseManager.LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("COBie.Export"))
            {
                TaskDialog.Show("授權限制",
                    "您的授權版本不支援 COBie 匯出功能。\n\n" +
                    "此功能僅適用於標準版和專業版授權。\n\n" +
                    "試用版用戶可以使用「COBie 欄位管理」和「COBie 範本」功能。\n\n" +
                    "點擊「授權管理」按鈕以查看或升級授權。");
                return Result.Cancelled;
            }

            var uidoc = cd.Application.ActiveUIDocument;
            if (uidoc == null) { msg = "沒有開啟的文件。"; return Result.Failed; }
            var doc = uidoc.Document;

            try
            {
                var cfgs = CobieConfigIO.LoadConfig();
                var exportFields = cfgs.Where(c => c.ExportEnabled).ToList();
                if (exportFields.Count == 0)
                {
                    TaskDialog.Show("自訂 COBie", "尚未勾選任何匯出欄位，請先於「COBie 欄位設定」設定。");
                    return Result.Cancelled;
                }

                // 定義設備類別篩選器 - 只匯出設備類物件
                var categoryMap = new Dictionary<BuiltInCategory, string>
                {
                    { BuiltInCategory.OST_MechanicalEquipment, "機械設備" },
                    { BuiltInCategory.OST_ElectricalEquipment, "電氣設備" },
                    { BuiltInCategory.OST_PlumbingFixtures, "衛浴設備" },
                    { BuiltInCategory.OST_LightingFixtures, "照明設備" },
                    { BuiltInCategory.OST_FireAlarmDevices, "火警設備" },
                    { BuiltInCategory.OST_ElectricalFixtures, "電氣裝置" },
                    { BuiltInCategory.OST_LightingDevices, "照明裝置" },
                    { BuiltInCategory.OST_Sprinklers, "灑水設備" },
                    { BuiltInCategory.OST_DuctTerminal, "風管末端" },
                    { BuiltInCategory.OST_DuctAccessory, "風管配件" },
                    { BuiltInCategory.OST_PipeAccessory, "管道配件" },
                    { BuiltInCategory.OST_SpecialityEquipment, "特殊設備" },
                    { BuiltInCategory.OST_DataDevices, "資料設備" },
                    { BuiltInCategory.OST_SecurityDevices, "保全設備" },
                    { BuiltInCategory.OST_Doors, "門" },
                    { BuiltInCategory.OST_Windows, "窗" },
                    { BuiltInCategory.OST_Furniture, "家具" }
                };
                
                // 檢查模型中存在的類別
                var existingCategories = new List<BuiltInCategory>();
                foreach (var cat in categoryMap.Keys)
                {
                    var filter = new ElementCategoryFilter(cat);
                    var elements = new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType().ToElements();
                    if (elements.Count > 0)
                    {
                        existingCategories.Add(cat);
                    }
                }
                
                // 如果沒有任何可用類別，顯示訊息並退出
                if (existingCategories.Count == 0)
                {
                    TaskDialog.Show("自訂 COBie", "模型中沒有找到任何支援的設備類別。");
                    return Result.Cancelled;
                }
                
                var handler = new CobieExportExternalEventHandler(doc, exportFields);
                var externalEvent = ExternalEvent.Create(handler);
                CustomCobieExportForm form = null;
                form = new CustomCobieExportForm(existingCategories, categoryMap, request =>
                {
                    handler.Request(request);
                    if (externalEvent.Raise() != ExternalEventRequest.Accepted)
                        form.SetRequestCompleted("目前無法送出 Revit 匯出動作，請稍後再試。");
                });
                handler.Attach(form);
                OpenExportForms.Add(form);
                form.FormClosed += (s, e) =>
                {
                    OpenExportForms.Remove(form);
                    externalEvent.Dispose();
                };
                form.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { msg = ex.ToString(); return Result.Failed; }
        }

        private sealed class CobieExportRequest
        {
            public List<BuiltInCategory> Categories { get; set; } = new List<BuiltInCategory>();
            public string FilePath { get; set; }
        }

        private sealed class CobieExportExternalEventHandler : IExternalEventHandler
        {
            private readonly Document _doc;
            private readonly List<CmdCobieFieldManager.CobieFieldConfig> _exportFields;
            private CobieExportRequest _request;
            private CustomCobieExportForm _form;

            public CobieExportExternalEventHandler(
                Document doc,
                List<CmdCobieFieldManager.CobieFieldConfig> exportFields)
            {
                _doc = doc;
                _exportFields = exportFields;
            }

            public void Attach(CustomCobieExportForm form)
            {
                _form = form;
            }

            public void Request(CobieExportRequest request)
            {
                _request = request;
            }

            public void Execute(UIApplication app)
            {
                try
                {
                    ExportSelectedCategories(_doc, _exportFields, _request);
                    _form?.SetRequestCompleted();
                }
                catch (Exception ex)
                {
                    _form?.SetRequestCompleted(ex.Message);
                    TaskDialog.Show("自訂 COBie", "匯出失敗：\n" + ex.Message);
                }
                finally
                {
                    _request = null;
                }
            }

            public string GetName()
            {
                return "YD BIM Tools - 自訂 COBie";
            }
        }

        private sealed class CustomCobieExportForm : System.Windows.Forms.Form
        {
            private readonly Dictionary<BuiltInCategory, System.Windows.Forms.CheckBox> _checkBoxes;
            private readonly Action<CobieExportRequest> _requestAction;
            private readonly System.Windows.Forms.Button _exportButton;
            private bool _requestPending;

            public CustomCobieExportForm(
                IEnumerable<BuiltInCategory> categories,
                IReadOnlyDictionary<BuiltInCategory, string> categoryNames,
                Action<CobieExportRequest> requestAction)
            {
                _requestAction = requestAction;
                _checkBoxes = new Dictionary<BuiltInCategory, System.Windows.Forms.CheckBox>();

                Text = "YD BIM Tools - 自訂 COBie";
                Width = 460;
                Height = 540;
                MinimumSize = new System.Drawing.Size(420, 460);
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
                Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5f);

                var header = new System.Windows.Forms.Label
                {
                    Text = "選擇匯出類別",
                    Dock = System.Windows.Forms.DockStyle.Top,
                    Height = 52,
                    Padding = new System.Windows.Forms.Padding(14, 14, 0, 0),
                    Font = new System.Drawing.Font("Microsoft JhengHei UI", 13f, System.Drawing.FontStyle.Bold),
                    ForeColor = System.Drawing.Color.White,
                    BackColor = System.Drawing.Color.FromArgb(16, 67, 108)
                };

                var hint = new System.Windows.Forms.Label
                {
                    Text = "視窗開啟時可回到 Revit 檢查模型；按「匯出」後才讀取模型資料。",
                    Dock = System.Windows.Forms.DockStyle.Top,
                    Height = 42,
                    Padding = new System.Windows.Forms.Padding(14, 10, 8, 0),
                    ForeColor = System.Drawing.Color.DimGray
                };

                var categoryPanel = new System.Windows.Forms.FlowLayoutPanel
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    Padding = new System.Windows.Forms.Padding(14, 8, 14, 8)
                };
                foreach (var category in categories)
                {
                    var checkBox = new System.Windows.Forms.CheckBox
                    {
                        Text = categoryNames[category],
                        Checked = true,
                        AutoSize = false,
                        Width = 380,
                        Height = 28
                    };
                    _checkBoxes.Add(category, checkBox);
                    categoryPanel.Controls.Add(checkBox);
                }

                var footer = new System.Windows.Forms.FlowLayoutPanel
                {
                    Dock = System.Windows.Forms.DockStyle.Bottom,
                    Height = 54,
                    FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
                    Padding = new System.Windows.Forms.Padding(8),
                    BackColor = System.Drawing.Color.FromArgb(242, 246, 250)
                };
                var closeButton = new System.Windows.Forms.Button { Text = "關閉", Width = 86, Height = 32 };
                closeButton.Click += (s, e) => Close();
                _exportButton = new System.Windows.Forms.Button
                {
                    Text = "匯出",
                    Width = 110,
                    Height = 32,
                    BackColor = System.Drawing.Color.FromArgb(16, 67, 108),
                    ForeColor = System.Drawing.Color.White,
                    FlatStyle = System.Windows.Forms.FlatStyle.Flat
                };
                _exportButton.Click += ExportButton_Click;
                footer.Controls.Add(closeButton);
                footer.Controls.Add(_exportButton);

                Controls.Add(categoryPanel);
                Controls.Add(hint);
                Controls.Add(header);
                Controls.Add(footer);
            }

            private void ExportButton_Click(object sender, EventArgs e)
            {
                if (_requestPending) return;

                var categories = _checkBoxes
                    .Where(pair => pair.Value.Checked)
                    .Select(pair => pair.Key)
                    .ToList();
                if (categories.Count == 0)
                {
                    MessageBox.Show("請至少選擇一個設備類別。", "自訂 COBie");
                    return;
                }

                using (var dialog = new SaveFileDialog
                {
                    Filter = "Excel 檔案 (*.xlsx)|*.xlsx|CSV 檔案 (*.csv)|*.csv",
                    FileName = $"COBie_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                    DefaultExt = "xlsx",
                    Title = "匯出自訂 COBie"
                })
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return;
                    _requestPending = true;
                    _exportButton.Enabled = false;
                    _exportButton.Text = "匯出中...";
                    _requestAction(new CobieExportRequest
                    {
                        Categories = categories,
                        FilePath = dialog.FileName
                    });
                }
            }

            public void SetRequestCompleted(string error = null)
            {
                if (IsDisposed) return;
                _requestPending = false;
                _exportButton.Enabled = true;
                _exportButton.Text = "匯出";
                if (!string.IsNullOrWhiteSpace(error))
                    MessageBox.Show(error, "自訂 COBie", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ExportSelectedCategories(
            Document doc,
            List<CmdCobieFieldManager.CobieFieldConfig> exportFields,
            CobieExportRequest request)
        {
            if (request == null || request.Categories.Count == 0)
                throw new InvalidOperationException("沒有選擇匯出類別。");

            var filters = request.Categories
                .Select(category => (ElementFilter)new ElementCategoryFilter(category))
                .ToList();
            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new LogicalOrFilter(filters))
                .ToElements()
                .Where(element => element.Category != null)
                .ToList();

            if (elements.Count == 0)
                throw new InvalidOperationException("專案中沒有找到所選類別的設備物件。");

            var stats = elements
                .GroupBy(element => element.Category?.Name ?? "未知")
                .Select(group => $"{group.Key}: {group.Count()} 個");
            if (TaskDialog.Show(
                    "確認匯出",
                    $"即將匯出 {elements.Count} 個設備類物件：\n\n{string.Join("\n", stats)}",
                    TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel) != TaskDialogResult.Ok)
                return;

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var headers = new List<string> { "UniqueId", "ElementId", "FamilyName", "TypeName" };
            headers.AddRange(exportFields
                .Select(field => field.DisplayName?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct());

            var rows = new List<List<string>>();
            foreach (var element in elements)
            {
                var familyAndType = GetFamilyAndType(doc, element);
                var row = new List<string>
                {
                    element.UniqueId ?? "",
                    ParamTypeCompat.ElementIdToString(element.Id),
                    familyAndType.family ?? "",
                    familyAndType.type ?? ""
                };
                row.AddRange(exportFields.Select(field => GetExportFieldValue(doc, element, field)));
                rows.Add(row);
            }

            var extension = Path.GetExtension(request.FilePath).ToLowerInvariant();
            if (extension == ".xlsx")
            {
                WriteExcelFile(request.FilePath, headers, rows);
            }
            else if (extension == ".csv")
            {
                using (var stream = new FileStream(request.FilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                {
                    writer.WriteLine(Csv(headers));
                    foreach (var row in rows) writer.WriteLine(Csv(row));
                }
            }
            else
            {
                throw new InvalidOperationException("不支援的檔案格式。請使用 .xlsx 或 .csv。");
            }

            TaskDialog.Show("自訂 COBie", $"已輸出 {elements.Count} 筆至：\n{Path.GetFileName(request.FilePath)}");
        }

        private static string GetExportFieldValue(
            Document doc,
            Element element,
            CmdCobieFieldManager.CobieFieldConfig field)
        {
            if (field.CobieName == "Space.Name" ||
                field.CobieName == "Component.Space" ||
                field.CobieName == "Component.SpaceCode")
            {
                var room = GetRoomFromElement(doc, element);
                if (room == null) return "";
                return field.CobieName == "Space.Name"
                    ? room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""
                    : room.Number ?? "";
            }

            if (field.CobieName == "Component.TagNumber") return "";

            if (field.CobieName == "System.Name" || field.CobieName == "System.Identifier")
            {
                var phase = GetElementPhase(doc, element);
                if (field.CobieName == "System.Name") return phase ?? "";
                if (string.IsNullOrEmpty(phase)) return "";
                if (phase.Contains("建築")) return "AR";
                if (phase.Contains("給水")) return "WW";
                if (phase.Contains("排水")) return "PP";
                if (phase.Contains("電氣")) return "EE";
                if (phase.Contains("弱電")) return "LC";
                if (phase.Contains("消防")) return "FP";
                if (phase.Contains("空調")) return "MC";
                return "OT";
            }

            if (field.CobieName == "Component.Name")
            {
                var familyAndType = GetFamilyAndType(doc, element);
                return !string.IsNullOrEmpty(familyAndType.family) && !string.IsNullOrEmpty(familyAndType.type)
                    ? $"{familyAndType.family}-{familyAndType.type}"
                    : "";
            }

            if (field.IsBuiltIn && field.BuiltInParam.HasValue)
            {
                if (field.IsInstance)
                    return TryGetStringParam(element, field.BuiltInParam.Value) ?? field.DefaultValue ?? "";
                var elementType = doc.GetElement(element.GetTypeId()) as ElementType;
                var parameter = elementType?.get_Parameter(field.BuiltInParam.Value);
                return parameter?.AsString() ?? parameter?.AsValueString() ?? field.DefaultValue ?? "";
            }

            if (!string.IsNullOrWhiteSpace(field.SharedParameterName))
            {
                if (field.IsInstance)
                    return TryGetStringParam(element, field.SharedParameterName) ?? field.DefaultValue ?? "";
                var elementType = doc.GetElement(element.GetTypeId()) as ElementType;
                var parameter = elementType?.Parameters
                    .Cast<Parameter>()
                    .FirstOrDefault(item => item.Definition?.Name == field.SharedParameterName);
                return parameter?.AsString() ?? parameter?.AsValueString() ?? field.DefaultValue ?? "";
            }

            return field.DefaultValue ?? "";
        }

        private static (string family, string type) GetFamilyAndType(Document doc, Element e)
        {
            try
            {
                var tid = e.GetTypeId();
                if (tid == ElementId.InvalidElementId) return (null, null);
                var et = doc.GetElement(tid) as ElementType;
                if (et == null) return (null, null);
                var fam = (et as FamilySymbol)?.Family?.Name ?? et.FamilyName;
                return (fam, et.Name);
            }
            catch { return (null, null); }
        }

        private static string TryGetStringParam(Element e, BuiltInParameter bip)
        { var p = e.get_Parameter(bip); return p?.AsString() ?? p?.AsValueString(); }

        private static string TryGetStringParam(Element e, string sharedName)
        {
            var p = e.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Definition?.Name == sharedName);
            return p?.AsString() ?? p?.AsValueString();
        }

        private static string Csv(IEnumerable<string> cells)
            => string.Join(",", cells.Select(EscapeCsv));
        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            bool need = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
            s = s.Replace("\"", "\"\"");
            return need ? $"\"{s}\"" : s;
        }
        
        // 獲取元件所在的房間（支援連結模型）
        private static Room GetRoomFromElement(Document doc, Element element)
        {
            try
            {
                // 獲取元件的位置點
                LocationPoint locPoint = element.Location as LocationPoint;
                if (locPoint == null) return null;

                XYZ point = locPoint.Point;

                // 獲取所有階段
                PhaseArray phases = doc.Phases;
                if (phases.Size == 0) return null;

                // 使用最後一個階段（通常是當前階段）
                Phase phase = phases.get_Item(phases.Size - 1);

                // 1. 首先嘗試從當前文件獲取房間
                Room room = doc.GetRoomAtPoint(point, phase);
                if (room != null) return room;

                // 2. 如果當前文件沒有房間，嘗試從連結模型獲取
                // 收集所有 Revit 連結
                FilteredElementCollector linkCollector = new FilteredElementCollector(doc);
                var revitLinks = linkCollector.OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();

                foreach (RevitLinkInstance linkInstance in revitLinks)
                {
                    Document linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc == null) continue;

                    // 將主文件中的點轉換到連結文件的座標系
                    Transform linkTransform = linkInstance.GetTotalTransform();
                    XYZ pointInLink = linkTransform.Inverse.OfPoint(point);

                    // 獲取連結文件的階段
                    PhaseArray linkPhases = linkDoc.Phases;
                    if (linkPhases.Size > 0)
                    {
                        Phase linkPhase = linkPhases.get_Item(linkPhases.Size - 1);
                        Room linkRoom = linkDoc.GetRoomAtPoint(pointInLink, linkPhase);

                        if (linkRoom != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"從連結模型 '{linkDoc.Title}' 找到房間: {linkRoom.Name} ({linkRoom.Number})");
                            return linkRoom;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetRoomFromElement 錯誤: {ex.Message}");
                return null;
            }
        }
        
        // 獲取元件的階段名稱
        private static string GetElementPhase(Document doc, Element element)
        {
            try
            {
                // 嘗試獲取元件的階段參數
                Parameter phaseParam = element.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (phaseParam != null && phaseParam.HasValue)
                {
                    ElementId phaseId = phaseParam.AsElementId();
                    if (phaseId != ElementId.InvalidElementId)
                    {
                        Phase phase = doc.GetElement(phaseId) as Phase;
                        if (phase != null)
                        {
                            return phase.Name;
                        }
                    }
                }
                
                // 如果沒有階段參數，嘗試從工作集獲取
                WorksetId worksetId = element.WorksetId;
                if (worksetId != WorksetId.InvalidWorksetId)
                {
                    WorksetTable worksetTable = doc.GetWorksetTable();
                    Workset workset = worksetTable.GetWorkset(worksetId);
                    if (workset != null)
                    {
                        return workset.Name;
                    }
                }
                
                return "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 寫入 Excel 檔案
        /// </summary>
        private static void WriteExcelFile(string filePath, List<string> headers, List<List<string>> rows)
        {
            var fileInfo = new FileInfo(filePath);
            using (var package = new ExcelPackage(fileInfo))
            {
                // 建立工作表
                var worksheet = package.Workbook.Worksheets.Add("COBie Data");

                // 寫入標題列
                for (int col = 0; col < headers.Count; col++)
                {
                    worksheet.Cells[1, col + 1].Value = headers[col];
                    worksheet.Cells[1, col + 1].Style.Font.Bold = true;
                    worksheet.Cells[1, col + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    worksheet.Cells[1, col + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                // 寫入資料列
                for (int row = 0; row < rows.Count; row++)
                {
                    var rowData = rows[row];
                    for (int col = 0; col < rowData.Count; col++)
                    {
                        worksheet.Cells[row + 2, col + 1].Value = rowData[col];
                    }
                }

                // 自動調整欄寬
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                // 儲存檔案
                package.Save();
            }
        }
    }
}
