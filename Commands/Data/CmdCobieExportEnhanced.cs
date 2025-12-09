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

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdCobieExportEnhanced : IExternalCommand
    {
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
                    TaskDialog.Show("COBie 匯出", "尚未勾選任何匯出欄位，請先於「COBie 欄位管理」設定。");
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
                    TaskDialog.Show("COBie 匯出", "模型中沒有找到任何支援的設備類別。");
                    return Result.Cancelled;
                }
                
                // 建立勾選對話框
                var form = new System.Windows.Forms.Form
                {
                    Text = "選擇要匯出的設備類別",
                    Width = 400,
                    Height = 400,
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false
                };
                
                var panel = new System.Windows.Forms.Panel
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    AutoScroll = true
                };
                form.Controls.Add(panel);
                
                var checkBoxes = new Dictionary<BuiltInCategory, System.Windows.Forms.CheckBox>();
                int yPos = 10;
                
                foreach (var cat in existingCategories)
                {
                    var cb = new System.Windows.Forms.CheckBox
                    {
                        Text = categoryMap[cat],
                        Checked = true,
                        Location = new System.Drawing.Point(10, yPos),
                        Width = 350,
                        Height = 24
                    };
                    panel.Controls.Add(cb);
                    checkBoxes.Add(cat, cb);
                    yPos += 30;
                }
                
                var btnOk = new System.Windows.Forms.Button
                {
                    Text = "確定",
                    DialogResult = System.Windows.Forms.DialogResult.OK,
                    Location = new System.Drawing.Point(200, 320),
                    Width = 80
                };
                
                var btnCancel = new System.Windows.Forms.Button
                {
                    Text = "取消",
                    DialogResult = System.Windows.Forms.DialogResult.Cancel,
                    Location = new System.Drawing.Point(290, 320),
                    Width = 80
                };
                
                form.Controls.Add(btnOk);
                form.Controls.Add(btnCancel);
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;
                
                if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }
                
                // 獲取選中的類別
                var selectedCategories = checkBoxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList();
                
                if (selectedCategories.Count == 0)
                {
                    TaskDialog.Show("COBie 匯出", "請至少選擇一個設備類別。");
                    return Result.Cancelled;
                }
                
                // 建立篩選器，只選擇設備類物件
                var categoryFilters = selectedCategories.Select(cat => 
                    (ElementFilter)new ElementCategoryFilter(cat)).ToList();
                
                var combinedFilter = new LogicalOrFilter(categoryFilters);
                
                // 收集設備類元件
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(combinedFilter)
                    .ToElements();

                // 進一步篩選，確保只包含有實體的設備
                var elems = allElements.Where(e =>
                {
                    // 確保元件有幾個形狀或是設備類型
                    var category = e.Category;
                    if (category == null) return false;

                    // 檢查是否為設備類別（使用相容性方法避免 IntegerValue 警告）
                    var catIdStr = ParamTypeCompat.ElementIdToString(category.Id);
                    if (!int.TryParse(catIdStr, out int catId)) return false;
                    return selectedCategories.Any(bic => (int)bic == catId);
                }).ToList();

                if (elems.Count == 0)
                {
                    TaskDialog.Show("COBie 匯出", "專案中沒有找到任何設備類物件。\n\n支援的設備類別包括：機械設備、電氣設備、衛浴設備、照明設備等。");
                    return Result.Cancelled;
                }

                // 顯示將要匯出的設備統計
                var categoryStats = elems.GroupBy(e => e.Category?.Name ?? "未知")
                    .Select(g => $"{g.Key}: {g.Count()} 個")
                    .ToList();
                
                var statsMessage = $"即將匯出 {elems.Count} 個設備類物件：\n\n" + string.Join("\n", categoryStats);
                var confirmResult = TaskDialog.Show("確認匯出", statsMessage, TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);
                
                if (confirmResult != TaskDialogResult.Ok) return Result.Cancelled;

                var sfd = new SaveFileDialog { Filter = "CSV (逗號分隔)|*.csv", FileName = $"COBie_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
                if (sfd.ShowDialog() != DialogResult.OK) return Result.Cancelled;

                var headers = new List<string> { "UniqueId", "ElementId", "FamilyName", "TypeName" };
                headers.AddRange(exportFields.Select(f => f.DisplayName?.Trim()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());

                using (var fs = new FileStream(sfd.FileName, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(true)))
                {
                    sw.WriteLine(Csv(headers));
                    foreach (var e in elems)
                    {
                        var (fam, typ) = GetFamilyAndType(doc, e);
                        var row = new List<string>
                        {
                            e.UniqueId ?? "",
                            ParamTypeCompat.ElementIdToString(e.Id),
                            fam ?? "",
                            typ ?? ""
                        };

                        foreach (var f in exportFields)
                        {
                            string val = "";
                            
                            // 自動填入空間名稱與空間代碼
                            if (f.CobieName == "Space.Name" || f.CobieName == "Component.Space" || f.CobieName == "Component.SpaceCode")
                            {
                                var room = GetRoomFromElement(doc, e);
                                if (room != null)
                                {
                                    // 取得房間的名稱參數（不含編號）
                                    string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                                    string roomNumber = room.Number ?? "";

                                    System.Diagnostics.Debug.WriteLine($"房間資訊 - ID: {room.Id}, Name屬性: '{room.Name}', Name參數: '{roomName}', Number: '{roomNumber}'");

                                    if (f.CobieName == "Space.Name")
                                        val = roomName; // 使用 ROOM_NAME 參數，不含編號
                                    else if (f.CobieName == "Component.Space")
                                        val = roomNumber; // 使用房間編號作為空間代碼
                                    else if (f.CobieName == "Component.SpaceCode")
                                        val = roomNumber; // 編號只呈現於空間代碼
                                }
                            }
                            // 資產編號由廠商填寫，不自動填值
                            else if (f.CobieName == "Component.TagNumber")
                            {
                                val = "";
                            }
                            // 自動填入系統名稱和系統代碼
                            else if (f.CobieName == "System.Name" || f.CobieName == "System.Identifier")
                            {
                                var phase = GetElementPhase(doc, e);
                                if (!string.IsNullOrEmpty(phase))
                                {
                                    if (f.CobieName == "System.Name")
                                    {
                                        val = phase;
                                    }
                                    else if (f.CobieName == "System.Identifier")
                                    {
                                        if (phase.Contains("建築")) val = "AR";
                                        else if (phase.Contains("給水")) val = "WW";
                                        else if (phase.Contains("排水")) val = "PP";
                                        else if (phase.Contains("電氣")) val = "EE";
                                        else if (phase.Contains("弱電")) val = "LC";
                                        else if (phase.Contains("消防")) val = "FP";
                                        else if (phase.Contains("空調")) val = "MC";
                                        else val = "OT";
                                    }
                                }
                            }
                            else if (f.CobieName == "Component.Name")
                            {
                                var (family, type) = GetFamilyAndType(doc, e);
                                if (!string.IsNullOrEmpty(family) && !string.IsNullOrEmpty(type))
                                {
                                    val = $"{family}-{type}";
                                }
                            }
                            else if (f.IsBuiltIn && f.BuiltInParam.HasValue)
                            {
                                if (f.IsInstance)
                                    val = TryGetStringParam(e, f.BuiltInParam.Value) ?? f.DefaultValue ?? "";
                                else
                                {
                                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                                    var pt = et?.get_Parameter(f.BuiltInParam.Value);
                                    val = pt?.AsString() ?? pt?.AsValueString() ?? f.DefaultValue ?? "";
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(f.SharedParameterName))
                            {
                                if (f.IsInstance)
                                    val = TryGetStringParam(e, f.SharedParameterName) ?? f.DefaultValue ?? "";
                                else
                                {
                                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                                    var pt = et?.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Definition?.Name == f.SharedParameterName);
                                    val = pt?.AsString() ?? pt?.AsValueString() ?? f.DefaultValue ?? "";
                                }
                            }
                            else
                                val = f.DefaultValue ?? "";
                                
                            row.Add(val);
                        }
                        sw.WriteLine(Csv(row));
                    }
                }

                TaskDialog.Show("COBie 匯出", $"已輸出 {elems.Count} 筆。");
                return Result.Succeeded;
            }
            catch (Exception ex) { msg = ex.ToString(); return Result.Failed; }
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
    }
}
