using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using YD_RevitTools.LicenseManager.Helpers.Data;

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdCobieStandardExport : IExternalCommand
    {
        private const string DefaultEmail = "unknown@yd-bim.local";
        private const string DefaultCategory = "n/a";
        private const string ExtSystem = "Autodesk Revit";

        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet set)
        {
            var licenseManager = LicenseManager.Instance;
            if (!licenseManager.HasFeatureAccess("COBie.Export"))
            {
                TaskDialog.Show("COBie 標準匯出",
                    "此功能需要 Standard 或 Professional 授權。\n請升級您的授權以使用 COBie 標準匯出功能。");
                return Result.Cancelled;
            }

            var uidoc = cd.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                msg = "沒有作用中的 Revit 文件。";
                return Result.Failed;
            }

            var doc = uidoc.Document;

            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                var model = CobieStandardModel.Build(doc);
                var preflight = CobiePreflightSummary.From(model);
                var action = ShowPreflightDialog(preflight);
                if (action == CobiePreflightAction.ExportReport)
                {
                    ExportPreflightReport(model, preflight);
                    return Result.Succeeded;
                }

                bool autoFill = action == CobiePreflightAction.AutoFillAndExport;
                if (autoFill)
                    model = CobieStandardModel.Build(doc, true);

                using (var sfd = new SaveFileDialog
                {
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                    FileName = $"COBie_Standard_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                    DefaultExt = "xlsx",
                    Title = "匯出 COBie 標準工作簿"
                })
                {
                    if (sfd.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;

                    WriteWorkbook(sfd.FileName, model);

                    TaskDialog.Show("COBie 標準匯出",
                        $"已匯出 COBie 標準工作簿。\n\n" +
                        $"檔案：{Path.GetFileName(sfd.FileName)}\n" +
                        $"樓層：{model.Floors.Count}\n" +
                        $"空間：{model.Spaces.Count}\n" +
                        $"類型：{model.Types.Count}\n" +
                        $"元件：{model.Components.Count}\n" +
                        $"檢核：{model.ValidationRows.Count} 項\n" +
                        $"補值：{(autoFill ? "已套用於匯出檔" : "未套用")}");
                }

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                msg = ex.ToString();
                return Result.Failed;
            }
        }

        private enum CobiePreflightAction
        {
            AutoFillAndExport,
            DirectExport,
            ExportReport
        }

        private static CobiePreflightAction ShowPreflightDialog(CobiePreflightSummary summary)
        {
            var dialog = new TaskDialog("COBie 匯出前檢核")
            {
                MainInstruction = summary.HasMissingFields
                    ? "發現 COBie 欄位缺值。"
                    : "COBie 基本欄位檢核通過。",
                MainContent =
                    $"模型資料：樓層 {summary.FloorCount}、空間 {summary.SpaceCount}、類型 {summary.TypeCount}、元件 {summary.ComponentCount}\n" +
                    $"必要欄位缺值：{summary.RequiredMissingCount}\n" +
                    $"建議欄位缺值或 n/a：{summary.RecommendedMissingCount}\n" +
                    $"空白必要工作表：{summary.EmptyRequiredSheetCount}\n\n" +
                    "自動補值只會套用到本次匯出的 Excel，不會回寫或修改 Revit 模型。",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };

            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "自動補值後匯出", "以「未提供、未分配空間、0」等匯出用文字補齊空值。");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "直接匯出", "保留模型原始資料與缺值狀態。");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "只匯出缺值報告", "先輸出 FieldCheck 與 Validation，方便整理模型資料後再正式交付。");
            dialog.DefaultButton = TaskDialogResult.CommandLink1;

            var result = dialog.Show();
            if (result == TaskDialogResult.Cancel)
                throw new OperationCanceledException();

            if (result == TaskDialogResult.CommandLink1) return CobiePreflightAction.AutoFillAndExport;
            if (result == TaskDialogResult.CommandLink3) return CobiePreflightAction.ExportReport;
            return CobiePreflightAction.DirectExport;
        }

        private static void ExportPreflightReport(CobieStandardModel model, CobiePreflightSummary summary)
        {
            using (var sfd = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"COBie_FieldCheck_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                DefaultExt = "xlsx",
                Title = "匯出 COBie 缺值檢核報告"
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK)
                    throw new OperationCanceledException();

                WritePreflightReport(sfd.FileName, model, summary);

                TaskDialog.Show("COBie 缺值檢核報告",
                    $"已匯出 COBie 缺值檢核報告。\n\n" +
                    $"檔案：{Path.GetFileName(sfd.FileName)}\n" +
                    $"必要欄位缺值：{summary.RequiredMissingCount}\n" +
                    $"建議欄位缺值或 n/a：{summary.RecommendedMissingCount}");
            }
        }

        private static void WriteWorkbook(string filePath, CobieStandardModel model)
        {
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                WriteSheet(package, "Contact", CobieHeaders.Contact, model.Contacts);
                WriteSheet(package, "Facility", CobieHeaders.Facility, model.Facilities);
                WriteSheet(package, "Floor", CobieHeaders.Floor, model.Floors);
                WriteSheet(package, "Space", CobieHeaders.Space, model.Spaces);
                WriteSheet(package, "Zone", CobieHeaders.Zone, model.Zones);
                WriteSheet(package, "Type", CobieHeaders.Type, model.Types);
                WriteSheet(package, "Component", CobieHeaders.Component, model.Components);
                WriteSheet(package, "System", CobieHeaders.System, model.Systems);
                WriteSheet(package, "Assembly", CobieHeaders.Assembly, model.EmptyRows);
                WriteSheet(package, "Connection", CobieHeaders.Connection, model.EmptyRows);
                WriteSheet(package, "Spare", CobieHeaders.Spare, model.EmptyRows);
                WriteSheet(package, "Resource", CobieHeaders.Resource, model.EmptyRows);
                WriteSheet(package, "Job", CobieHeaders.Job, model.EmptyRows);
                WriteSheet(package, "Document", CobieHeaders.Document, model.EmptyRows);
                WriteSheet(package, "Attribute", CobieHeaders.Attribute, model.Attributes);
                WriteSheet(package, "Coordinate", CobieHeaders.Coordinate, model.Coordinates);
                WriteSheet(package, "Issue", CobieHeaders.Issue, model.Issues);
                WriteSheet(package, "PickLists", CobieHeaders.PickLists, model.PickLists);
                WriteSheet(package, "FieldCheck", CobieHeaders.FieldCheck, model.FieldCheckRows);
                WriteSheet(package, "Validation", CobieHeaders.Validation, model.ValidationRows);

                package.Save();
            }
        }

        private static void WritePreflightReport(string filePath, CobieStandardModel model, CobiePreflightSummary summary)
        {
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                WriteSheet(package, "Summary",
                    new[] { "項目", "數量", "說明" },
                    new[]
                    {
                        Row(new[] { "項目", "數量", "說明" }, "項目", "樓層", "數量", summary.FloorCount.ToString(), "說明", "Floor rows"),
                        Row(new[] { "項目", "數量", "說明" }, "項目", "空間", "數量", summary.SpaceCount.ToString(), "說明", "Space rows"),
                        Row(new[] { "項目", "數量", "說明" }, "項目", "類型", "數量", summary.TypeCount.ToString(), "說明", "Type rows"),
                        Row(new[] { "項目", "數量", "說明" }, "項目", "元件", "數量", summary.ComponentCount.ToString(), "說明", "Component rows"),
                        Row(new[] { "項目", "數量", "說明" }, "項目", "必要欄位缺值", "數量", summary.RequiredMissingCount.ToString(), "說明", "需要優先補齊"),
                        Row(new[] { "項目", "數量", "說明" }, "項目", "建議欄位缺值或 n/a", "數量", summary.RecommendedMissingCount.ToString(), "說明", "建議補齊以提升交付品質"),
                        Row(new[] { "項目", "數量", "說明" }, "項目", "空白必要工作表", "數量", summary.EmptyRequiredSheetCount.ToString(), "說明", "可能表示模型缺少對應類別資料")
                    });

                WriteSheet(package, "MissingFields",
                    CobieHeaders.FieldCheck,
                    model.FieldCheckRows.Where(r => ParseInt(GetCell(r, "MissingCount")) > 0 || GetCell(r, "Status") == "空表"));
                WriteSheet(package, "FieldCheck", CobieHeaders.FieldCheck, model.FieldCheckRows);
                WriteSheet(package, "Validation", CobieHeaders.Validation, model.ValidationRows);

                package.Save();
            }
        }

        private static void WriteSheet(ExcelPackage package, string sheetName, string[] headers, IEnumerable<Dictionary<string, string>> rows)
        {
            var ws = package.Workbook.Worksheets.Add(sheetName);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cells[1, i + 1];
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(31, 73, 125));
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);
            }

            int r = 2;
            foreach (var row in rows)
            {
                for (int c = 0; c < headers.Length; c++)
                {
                    string header = headers[c];
                    var cell = ws.Cells[r, c + 1];
                    cell.Value = row.TryGetValue(header, out var value) ? value : string.Empty;
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Hair);
                    cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    if (r % 2 == 0)
                    {
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(242, 246, 252));
                    }
                }
                r++;
            }

            ws.View.FreezePanes(2, 1);
            ws.Cells[ws.Dimension.Address].AutoFitColumns(8, 48);
            if (headers.Length > 0)
                ws.Cells[1, 1, Math.Max(1, r - 1), headers.Length].AutoFilter = true;
        }

        private sealed class CobieStandardModel
        {
            public List<Dictionary<string, string>> Contacts { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Facilities { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Floors { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Spaces { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Zones { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Types { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Components { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Systems { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Attributes { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Coordinates { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> Issues { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> PickLists { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> FieldCheckRows { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> ValidationRows { get; } = new List<Dictionary<string, string>>();
            public List<Dictionary<string, string>> EmptyRows { get; } = new List<Dictionary<string, string>>();
            public List<string> ValidationIssues { get; } = new List<string>();

            public static CobieStandardModel Build(Document doc, bool autoFill = false)
            {
                var model = new CobieStandardModel();
                string createdBy = GetDocumentAuthor(doc);
                string createdOn = DateTime.Today.ToString("yyyy-MM-dd");
                string facilityName = SafeName(doc.ProjectInformation?.Name, doc.Title);

                model.Contacts.Add(Row(CobieHeaders.Contact,
                    "Email", createdBy,
                    "CreatedBy", createdBy,
                    "CreatedOn", createdOn,
                    "Category", "Project Team",
                    "Company", SafeName(doc.ProjectInformation?.OrganizationName, "n/a"),
                    "Phone", "n/a",
                    "Department", "n/a",
                    "OrganizationCode", "n/a",
                    "GivenName", "n/a",
                    "FamilyName", "n/a",
                    "Street", "n/a",
                    "PostalBox", "n/a",
                    "Town", "n/a",
                    "StateRegion", "n/a",
                    "PostalCode", "n/a",
                    "Country", "n/a"));

                model.Facilities.Add(Row(CobieHeaders.Facility,
                    "Name", facilityName,
                    "CreatedBy", createdBy,
                    "CreatedOn", createdOn,
                    "Category", DefaultCategory,
                    "ProjectName", facilityName,
                    "SiteName", facilityName,
                    "LinearUnits", "millimeters",
                    "AreaUnits", "square meters",
                    "VolumeUnits", "cubic meters",
                    "CurrencyUnit", "n/a",
                    "AreaMeasurement", "Revit element quantities",
                    "ExternalSystem", ExtSystem,
                    "ExternalProjectObject", "ProjectInformation",
                    "ExternalProjectIdentifier", GetElementIdentifier(doc.ProjectInformation),
                    "ExternalSiteObject", "ProjectInformation",
                    "ExternalSiteIdentifier", GetElementIdentifier(doc.ProjectInformation),
                    "ExternalFacilityObject", "ProjectInformation",
                    "ExternalFacilityIdentifier", GetElementIdentifier(doc.ProjectInformation),
                    "Description", SafeName(doc.ProjectInformation?.BuildingName, facilityName),
                    "ProjectDescription", SafeName(doc.ProjectInformation?.Name, facilityName),
                    "SiteDescription", "n/a",
                    "Phase", "n/a"));

                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();
                foreach (var level in levels)
                {
                    model.Floors.Add(Row(CobieHeaders.Floor,
                        "Name", level.Name,
                        "CreatedBy", createdBy,
                        "CreatedOn", createdOn,
                        "Category", "Level",
                        "ExtSystem", ExtSystem,
                        "ExtObject", "Level",
                        "ExtIdentifier", GetElementIdentifier(level),
                        "Description", level.Name,
                        "Elevation", ToMillimeters(level.Elevation),
                        "Height", "n/a"));
                }

                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .OrderBy(r => GetLevelName(doc, r.LevelId))
                    .ThenBy(r => r.Number)
                    .ToList();
                foreach (var room in rooms)
                {
                    model.Spaces.Add(Row(CobieHeaders.Space,
                        "Name", SafeName(room.Number, room.Name),
                        "CreatedBy", createdBy,
                        "CreatedOn", createdOn,
                        "Category", "Room",
                        "FloorName", GetLevelName(doc, room.LevelId),
                        "Description", SafeName(room.Name, room.Number),
                        "ExtSystem", ExtSystem,
                        "ExtObject", "Room",
                        "ExtIdentifier", GetElementIdentifier(room),
                        "RoomTag", room.Number ?? string.Empty,
                        "UsableHeight", "n/a",
                        "GrossArea", ToSquareMeters(room.Area),
                        "NetArea", ToSquareMeters(room.Area)));
                }

                var components = CollectCobieComponents(doc);
                var typeGroups = components
                    .GroupBy(e => e.GetTypeId())
                    .Where(g => g.Key != ElementId.InvalidElementId)
                    .OrderBy(g => GetTypeName(doc, g.Key))
                    .ToList();

                var typeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in typeGroups)
                {
                    var type = doc.GetElement(group.Key) as ElementType;
                    if (type == null) continue;
                    string typeName = GetTypeName(type);
                    typeNames.Add(typeName);
                    model.Types.Add(Row(CobieHeaders.Type,
                        "Name", typeName,
                        "CreatedBy", createdBy,
                        "CreatedOn", createdOn,
                        "Category", GetCategoryName(type.Category),
                        "Description", SafeName(GetParameterText(type, BuiltInParameter.ALL_MODEL_DESCRIPTION), typeName),
                        "ExtSystem", ExtSystem,
                        "ExtObject", type.GetType().Name,
                        "ExtIdentifier", GetElementIdentifier(type),
                        "Manufacturer", GetParameterText(type, BuiltInParameter.ALL_MODEL_MANUFACTURER),
                        "ModelNumber", GetParameterText(type, BuiltInParameter.ALL_MODEL_MODEL),
                        "WarrantyGuarantorParts", "n/a",
                        "WarrantyDurationParts", "n/a",
                        "WarrantyGuarantorLabor", "n/a",
                        "WarrantyDurationLabor", "n/a",
                        "WarrantyDurationUnit", "n/a"));
                }

                foreach (var element in components.OrderBy(e => GetCategoryName(e.Category)).ThenBy(e => e.Name))
                {
                    string typeName = GetTypeName(doc, element.GetTypeId());
                    var room = GetRoomFromElement(doc, element);
                    model.Components.Add(Row(CobieHeaders.Component,
                        "Name", GetComponentName(element),
                        "CreatedBy", createdBy,
                        "CreatedOn", createdOn,
                        "TypeName", typeName,
                        "Space", room?.Number ?? "n/a",
                        "Description", SafeName(GetParameterText(element, BuiltInParameter.ALL_MODEL_DESCRIPTION), element.Name),
                        "ExtSystem", ExtSystem,
                        "ExtObject", element.GetType().Name,
                        "ExtIdentifier", GetElementIdentifier(element),
                        "SerialNumber", GetLookupParameterText(element, "SerialNumber", "序號"),
                        "InstallationDate", "n/a",
                        "WarrantyStartDate", "n/a",
                        "TagNumber", GetParameterText(element, BuiltInParameter.ALL_MODEL_MARK),
                        "BarCode", "n/a",
                        "AssetIdentifier", ParamTypeCompat.ElementIdToString(element.Id)));
                    if (!typeNames.Contains(typeName))
                        model.ValidationIssues.Add($"Component type missing: {typeName}");
                }

                foreach (var system in CollectSystems(doc))
                {
                    model.Systems.Add(Row(CobieHeaders.System,
                        "Name", system.Name,
                        "CreatedBy", createdBy,
                        "CreatedOn", createdOn,
                        "Category", GetCategoryName(system.Category),
                        "ExtSystem", ExtSystem,
                        "ExtObject", system.GetType().Name,
                        "ExtIdentifier", GetElementIdentifier(system),
                        "Description", SafeName(GetParameterText(system, BuiltInParameter.ALL_MODEL_DESCRIPTION), system.Name)));
                }

                AddConfiguredAttributes(doc, model, components, createdBy, createdOn);
                AddCoordinates(model, components, createdBy, createdOn);
                AddPickLists(model);
                if (autoFill)
                    ApplyAutoFill(model);
                AddValidation(model);
                return model;
            }
        }

        private sealed class CobiePreflightSummary
        {
            public int FloorCount { get; private set; }
            public int SpaceCount { get; private set; }
            public int TypeCount { get; private set; }
            public int ComponentCount { get; private set; }
            public int RequiredMissingCount { get; private set; }
            public int RecommendedMissingCount { get; private set; }
            public int EmptyRequiredSheetCount { get; private set; }
            public bool HasMissingFields => RequiredMissingCount > 0 || RecommendedMissingCount > 0 || EmptyRequiredSheetCount > 0;

            public static CobiePreflightSummary From(CobieStandardModel model)
            {
                var summary = new CobiePreflightSummary
                {
                    FloorCount = model.Floors.Count,
                    SpaceCount = model.Spaces.Count,
                    TypeCount = model.Types.Count,
                    ComponentCount = model.Components.Count
                };

                foreach (var row in model.FieldCheckRows)
                {
                    string requirement = GetCell(row, "Requirement");
                    int missing = ParseInt(GetCell(row, "MissingCount"));
                    string status = GetCell(row, "Status");

                    if (IsRequiredRequirement(requirement))
                    {
                        summary.RequiredMissingCount += missing;
                        if (status == "空表")
                            summary.EmptyRequiredSheetCount++;
                    }
                    else if (IsRecommendedRequirement(requirement))
                    {
                        summary.RecommendedMissingCount += missing;
                    }
                }

                return summary;
            }
        }

        private static void AddConfiguredAttributes(Document doc, CobieStandardModel model, List<Element> components, string createdBy, string createdOn)
        {
            var configs = CobieConfigIO.LoadConfig()
                .Where(c => c.ExportEnabled && !string.IsNullOrWhiteSpace(c.DisplayName))
                .ToList();

            foreach (var element in components)
            {
                foreach (var cfg in configs)
                {
                    string value = ReadConfigValue(doc, element, cfg);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    model.Attributes.Add(Row(CobieHeaders.Attribute,
                        "Name", CleanDisplayName(cfg.DisplayName),
                        "CreatedBy", createdBy,
                        "CreatedOn", createdOn,
                        "Category", SafeName(cfg.Category, DefaultCategory),
                        "SheetName", "Component",
                        "RowName", GetComponentName(element),
                        "Value", value,
                        "Unit", "n/a",
                        "ExtSystem", ExtSystem,
                        "ExtObject", element.GetType().Name,
                        "ExtIdentifier", GetElementIdentifier(element),
                        "Description", SafeName(cfg.CobieName, cfg.DisplayName),
                        "AllowedValues", "n/a"));
                }
            }
        }

        private static void AddCoordinates(CobieStandardModel model, List<Element> components, string createdBy, string createdOn)
        {
            foreach (var element in components)
            {
                var point = GetElementPoint(element);
                if (point == null) continue;
                model.Coordinates.Add(Row(CobieHeaders.Coordinate,
                    "Name", GetComponentName(element) + "-Location",
                    "CreatedBy", createdBy,
                    "CreatedOn", createdOn,
                    "Category", "Location",
                    "SheetName", "Component",
                    "RowName", GetComponentName(element),
                    "CoordinateXAxis", ToMillimeters(point.X),
                    "CoordinateYAxis", ToMillimeters(point.Y),
                    "CoordinateZAxis", ToMillimeters(point.Z),
                    "ExtSystem", ExtSystem,
                    "ExtObject", element.GetType().Name,
                    "ExtIdentifier", GetElementIdentifier(element),
                    "ClockwiseRotation", "n/a",
                    "ElevationalRotation", "n/a",
                    "YawRotation", "n/a"));
            }
        }

        private static void AddPickLists(CobieStandardModel model)
        {
            foreach (string sheet in new[] { "Contact", "Facility", "Floor", "Space", "Type", "Component", "System", "Attribute", "Coordinate" })
            {
                model.PickLists.Add(Row(CobieHeaders.PickLists,
                    "ListName", "SheetName",
                    "Value", sheet,
                    "Description", "COBie worksheet name"));
            }
        }

        private static void AddValidation(CobieStandardModel model)
        {
            ValidateRequired(model, "Contact", model.Contacts, CobieHeaders.ContactRequired);
            ValidateRequired(model, "Facility", model.Facilities, CobieHeaders.FacilityRequired);
            ValidateRequired(model, "Floor", model.Floors, CobieHeaders.CommonRequired);
            ValidateRequired(model, "Space", model.Spaces, CobieHeaders.SpaceRequired);
            ValidateRequired(model, "Type", model.Types, CobieHeaders.CommonRequired);
            ValidateRequired(model, "Component", model.Components, CobieHeaders.ComponentRequired);
            AddFieldCheck(model);

            if (!model.Floors.Any()) model.ValidationIssues.Add("Floor sheet has no rows.");
            if (!model.Spaces.Any()) model.ValidationIssues.Add("Space sheet has no rows.");
            if (!model.Types.Any()) model.ValidationIssues.Add("Type sheet has no rows.");
            if (!model.Components.Any()) model.ValidationIssues.Add("Component sheet has no rows.");

            foreach (var issue in model.ValidationIssues.Distinct())
            {
                model.ValidationRows.Add(Row(CobieHeaders.Validation,
                    "Severity", issue.Contains("has no rows") ? "Warning" : "Error",
                    "SheetName", GetIssueSheet(issue),
                    "RowName", "n/a",
                    "FieldName", "n/a",
                    "Message", issue));
            }

            if (!model.ValidationRows.Any())
            {
                model.ValidationRows.Add(Row(CobieHeaders.Validation,
                    "Severity", "Info",
                    "SheetName", "Workbook",
                    "RowName", "n/a",
                    "FieldName", "n/a",
                    "Message", "No basic validation issues found."));
            }
        }

        private static void ApplyAutoFill(CobieStandardModel model)
        {
            foreach (var row in AllRows(model))
            {
                FillIfEmpty(row, "CreatedBy", DefaultEmail);
                FillIfEmpty(row, "CreatedOn", DateTime.Today.ToString("yyyy-MM-dd"));
                FillIfEmpty(row, "ExtSystem", ExtSystem);
                FillIfEmpty(row, "ExternalSystem", ExtSystem);
                FillIfEmpty(row, "ExtObject", "RevitElement");
                FillIfEmpty(row, "ExtIdentifier", "n/a");
                FillIfEmpty(row, "Description", GetCell(row, "Name"));
            }

            foreach (var row in model.Contacts)
            {
                FillIfEmptyOrNa(row, "Company", "未提供");
                FillIfEmptyOrNa(row, "Phone", "未提供");
                FillIfEmptyOrNa(row, "GivenName", "未提供");
                FillIfEmptyOrNa(row, "FamilyName", "未提供");
                FillIfEmptyOrNa(row, "Street", "未提供");
                FillIfEmptyOrNa(row, "Town", "未提供");
                FillIfEmptyOrNa(row, "Country", "未提供");
            }

            foreach (var row in model.Facilities)
            {
                FillIfEmptyOrNa(row, "SiteDescription", GetCell(row, "SiteName"));
                FillIfEmptyOrNa(row, "ProjectDescription", GetCell(row, "ProjectName"));
                FillIfEmptyOrNa(row, "Phase", "未提供");
                FillIfEmptyOrNa(row, "CurrencyUnit", "未提供");
            }

            foreach (var row in model.Floors)
                FillIfEmptyOrNa(row, "Height", "未提供");

            foreach (var row in model.Spaces)
                FillIfEmptyOrNa(row, "UsableHeight", "未提供");

            foreach (var row in model.Types)
            {
                FillIfEmptyOrNa(row, "Manufacturer", "未提供");
                FillIfEmptyOrNa(row, "ModelNumber", GetCell(row, "Name"));
                FillIfEmptyOrNa(row, "WarrantyGuarantorParts", "未提供");
                FillIfEmptyOrNa(row, "WarrantyDurationParts", "未提供");
                FillIfEmptyOrNa(row, "WarrantyGuarantorLabor", "未提供");
                FillIfEmptyOrNa(row, "WarrantyDurationLabor", "未提供");
                FillIfEmptyOrNa(row, "WarrantyDurationUnit", "未提供");
            }

            foreach (var row in model.Components)
            {
                FillIfEmptyOrNa(row, "Space", "未分配空間");
                FillIfEmptyOrNa(row, "SerialNumber", "未提供");
                FillIfEmptyOrNa(row, "InstallationDate", "未提供");
                FillIfEmptyOrNa(row, "WarrantyStartDate", "未提供");
                FillIfEmptyOrNa(row, "TagNumber", GetCell(row, "Name"));
                FillIfEmptyOrNa(row, "BarCode", "未提供");
                FillIfEmptyOrNa(row, "AssetIdentifier", GetCell(row, "Name"));
            }

            foreach (var row in model.Attributes)
            {
                FillIfEmptyOrNa(row, "Unit", "未指定");
                FillIfEmptyOrNa(row, "AllowedValues", "未指定");
            }

            foreach (var row in model.Coordinates)
            {
                FillIfEmptyOrNa(row, "ClockwiseRotation", "0");
                FillIfEmptyOrNa(row, "ElevationalRotation", "0");
                FillIfEmptyOrNa(row, "YawRotation", "0");
            }

            model.ValidationRows.Add(Row(CobieHeaders.Validation,
                "Severity", "Info",
                "SheetName", "Workbook",
                "RowName", "n/a",
                "FieldName", "n/a",
                "Message", "已套用匯出用自動補值；未回寫 Revit 模型。"));
        }

        private static void ValidateRequired(CobieStandardModel model, string sheet, IEnumerable<Dictionary<string, string>> rows, string[] required)
        {
            foreach (var row in rows)
            {
                string rowName = row.TryGetValue("Name", out var name) ? name : "n/a";
                foreach (string field in required)
                {
                    if (!row.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                    {
                        model.ValidationIssues.Add($"{sheet}.{field} is empty at {rowName}.");
                        model.ValidationRows.Add(Row(CobieHeaders.Validation,
                            "Severity", "Error",
                            "SheetName", sheet,
                            "RowName", rowName,
                            "FieldName", field,
                            "Message", "必要欄位缺值。"));
                    }
                }
            }
        }

        private static void AddFieldCheck(CobieStandardModel model)
        {
            foreach (var rule in CobieFieldRules.All)
            {
                var rows = GetSheetRows(model, rule.SheetName);
                int rowCount = rows.Count;
                int missingCount = CountMissing(rows, rule.FieldName, rule.Requirement);
                string status;
                if (rowCount == 0)
                    status = rule.Requirement == "必要" ? "空表" : "無資料";
                else if (missingCount == 0)
                    status = "完成";
                else if (rule.Requirement == "必要")
                    status = "缺值";
                else
                    status = "建議補值";

                model.FieldCheckRows.Add(Row(CobieHeaders.FieldCheck,
                    "SheetName", rule.SheetName,
                    "FieldName", rule.FieldName,
                    "Requirement", rule.Requirement,
                    "DataSource", rule.DataSource,
                    "RowCount", rowCount.ToString(),
                    "MissingCount", missingCount.ToString(),
                    "Status", status,
                    "SuggestedAction", CobieFieldRules.GetSuggestedAction(rule, missingCount, rowCount),
                    "AutoFillValue", CobieFieldRules.GetAutoFillValue(rule),
                    "Note", rule.Note));

                if (rule.Requirement == "必要" && missingCount > 0)
                {
                    model.ValidationRows.Add(Row(CobieHeaders.Validation,
                        "Severity", "Error",
                        "SheetName", rule.SheetName,
                        "RowName", "多筆",
                        "FieldName", rule.FieldName,
                        "Message", $"必要欄位缺值 {missingCount} 筆，請參考 FieldCheck 工作表。"));
                }
                else if (rule.Requirement == "建議" && missingCount > 0)
                {
                    model.ValidationRows.Add(Row(CobieHeaders.Validation,
                        "Severity", "Warning",
                        "SheetName", rule.SheetName,
                        "RowName", "多筆",
                        "FieldName", rule.FieldName,
                        "Message", $"建議欄位缺值或為 n/a {missingCount} 筆。"));
                }
            }
        }

        private static List<Dictionary<string, string>> GetSheetRows(CobieStandardModel model, string sheetName)
        {
            switch (sheetName)
            {
                case "Contact": return model.Contacts;
                case "Facility": return model.Facilities;
                case "Floor": return model.Floors;
                case "Space": return model.Spaces;
                case "Zone": return model.Zones;
                case "Type": return model.Types;
                case "Component": return model.Components;
                case "System": return model.Systems;
                case "Attribute": return model.Attributes;
                case "Coordinate": return model.Coordinates;
                default: return model.EmptyRows;
            }
        }

        private static IEnumerable<Dictionary<string, string>> AllRows(CobieStandardModel model)
        {
            return model.Contacts
                .Concat(model.Facilities)
                .Concat(model.Floors)
                .Concat(model.Spaces)
                .Concat(model.Zones)
                .Concat(model.Types)
                .Concat(model.Components)
                .Concat(model.Systems)
                .Concat(model.Attributes)
                .Concat(model.Coordinates)
                .Concat(model.Issues)
                .Concat(model.PickLists);
        }

        private static void FillIfEmpty(Dictionary<string, string> row, string fieldName, string value)
        {
            if (!row.ContainsKey(fieldName)) return;
            if (string.IsNullOrWhiteSpace(row[fieldName]))
                row[fieldName] = string.IsNullOrWhiteSpace(value) ? "n/a" : value;
        }

        private static void FillIfEmptyOrNa(Dictionary<string, string> row, string fieldName, string value)
        {
            if (!row.ContainsKey(fieldName)) return;
            if (string.IsNullOrWhiteSpace(row[fieldName]) || IsNa(row[fieldName]))
                row[fieldName] = string.IsNullOrWhiteSpace(value) || IsNa(value) ? "未提供" : value;
        }

        private static int CountMissing(IEnumerable<Dictionary<string, string>> rows, string fieldName, string requirement)
        {
            return rows.Count(row =>
            {
                if (!row.TryGetValue(fieldName, out var value)) return true;
                if (string.IsNullOrWhiteSpace(value)) return true;
                return IsRecommendedRequirement(requirement) && IsNa(value);
            });
        }

        private static string GetCell(Dictionary<string, string> row, string fieldName)
        {
            return row != null && row.TryGetValue(fieldName, out var value) ? value ?? string.Empty : string.Empty;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, out int result) ? result : 0;
        }

        private static bool IsNa(string value)
        {
            return string.Equals(value?.Trim(), "n/a", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRequiredRequirement(string value)
        {
            return string.Equals(value?.Trim(), "必要", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRecommendedRequirement(string value)
        {
            return string.Equals(value?.Trim(), "建議", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetIssueSheet(string issue)
        {
            int idx = issue.IndexOf('.');
            if (idx > 0) return issue.Substring(0, idx);
            foreach (string sheet in new[] { "Contact", "Facility", "Floor", "Space", "Type", "Component" })
                if (issue.StartsWith(sheet, StringComparison.OrdinalIgnoreCase)) return sheet;
            return "Workbook";
        }

        private static List<Element> CollectCobieComponents(Document doc)
        {
            BuiltInCategory[] categories =
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_Sprinklers,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_SpecialityEquipment,
                BuiltInCategory.OST_DataDevices,
                BuiltInCategory.OST_SecurityDevices,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Furniture
            };

            var filters = categories.Select(c => (ElementFilter)new ElementCategoryFilter(c)).ToList();
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new LogicalOrFilter(filters))
                .Where(e => e.Category != null)
                .ToList();
        }

        private static IEnumerable<Element> CollectSystems(Document doc)
        {
            var result = new List<Element>();
            try { result.AddRange(new FilteredElementCollector(doc).OfClass(typeof(MEPSystem)).Cast<Element>()); } catch { }
            try { result.AddRange(new FilteredElementCollector(doc).OfClass(typeof(PipingSystem)).Cast<Element>()); } catch { }
            return result
                .GroupBy(e => ElementIdValue(e.Id))
                .Select(g => g.First())
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .OrderBy(e => e.Name);
        }

        private static Room GetRoomFromElement(Document doc, Element element)
        {
            try
            {
                var point = GetElementPoint(element);
                if (point == null) return null;
                Phase phase = doc.Phases.Size > 0 ? doc.Phases.get_Item(doc.Phases.Size - 1) : null;
                if (phase == null) return null;
                Room room = doc.GetRoomAtPoint(point, phase);
                if (room != null) return room;

                foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    var linkDoc = link.GetLinkDocument();
                    if (linkDoc == null || linkDoc.Phases.Size == 0) continue;
                    var linkPoint = link.GetTotalTransform().Inverse.OfPoint(point);
                    room = linkDoc.GetRoomAtPoint(linkPoint, linkDoc.Phases.get_Item(linkDoc.Phases.Size - 1));
                    if (room != null) return room;
                }
            }
            catch { }

            return null;
        }

        private static XYZ GetElementPoint(Element element)
        {
            if (element?.Location is LocationPoint lp) return lp.Point;
            if (element?.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
            try
            {
                var bb = element?.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) * 0.5;
            }
            catch { }
            return null;
        }

        private static string ReadConfigValue(Document doc, Element element, CmdCobieFieldManager.CobieFieldConfig cfg)
        {
            try
            {
                if (cfg.CobieName == "Space.Name" || cfg.CobieName == "Component.Space" || cfg.CobieName == "Component.SpaceCode")
                {
                    var room = GetRoomFromElement(doc, element);
                    if (room == null) return cfg.DefaultValue ?? string.Empty;
                    if (cfg.CobieName == "Space.Name") return SafeName(room.Name, room.Number);
                    return room.Number ?? cfg.DefaultValue ?? string.Empty;
                }

                if (cfg.CobieName == "Component.Name") return GetComponentName(element);
                if (cfg.CobieName == "Component.TypeName") return GetTypeName(doc, element.GetTypeId());

                if (cfg.IsBuiltIn && cfg.BuiltInParam.HasValue)
                    return cfg.IsInstance
                        ? GetParameterText(element, cfg.BuiltInParam.Value)
                        : GetParameterText(doc.GetElement(element.GetTypeId()), cfg.BuiltInParam.Value);

                if (!string.IsNullOrWhiteSpace(cfg.SharedParameterName))
                    return cfg.IsInstance
                        ? GetLookupParameterText(element, cfg.SharedParameterName)
                        : GetLookupParameterText(doc.GetElement(element.GetTypeId()), cfg.SharedParameterName);
            }
            catch { }

            return cfg.DefaultValue ?? string.Empty;
        }

        private static Dictionary<string, string> Row(string[] headers, params string[] pairs)
        {
            var row = headers.ToDictionary(h => h, h => string.Empty, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                row[pairs[i]] = pairs[i + 1] ?? string.Empty;
            return row;
        }

        private static string GetDocumentAuthor(Document doc)
        {
            string author = SafeName(doc.ProjectInformation?.Author, DefaultEmail);
            return author.Contains("@") ? author : DefaultEmail;
        }

        private static string GetLevelName(Document doc, ElementId levelId)
        {
            return (doc.GetElement(levelId) as Level)?.Name ?? "n/a";
        }

        private static string GetTypeName(Document doc, ElementId typeId)
        {
            return GetTypeName(doc.GetElement(typeId) as ElementType);
        }

        private static string GetTypeName(ElementType type)
        {
            if (type == null) return "n/a";
            string family = (type as FamilySymbol)?.Family?.Name ?? type.FamilyName;
            return string.IsNullOrWhiteSpace(family) ? type.Name : family + ":" + type.Name;
        }

        private static string GetComponentName(Element element)
        {
            string mark = GetParameterText(element, BuiltInParameter.ALL_MODEL_MARK);
            if (!string.IsNullOrWhiteSpace(mark)) return mark;
            return GetCategoryName(element?.Category) + "-" + ParamTypeCompat.ElementIdToString(element.Id);
        }

        private static string GetCategoryName(Category category)
        {
            return category?.Name ?? DefaultCategory;
        }

        private static string GetElementIdentifier(Element element)
        {
            return element?.UniqueId ?? (element != null ? ParamTypeCompat.ElementIdToString(element.Id) : "n/a");
        }

        private static string GetParameterText(Element element, BuiltInParameter builtInParameter)
        {
            if (element == null) return string.Empty;
            var p = element.get_Parameter(builtInParameter);
            return ParameterToText(p);
        }

        private static string GetLookupParameterText(Element element, params string[] names)
        {
            if (element == null || names == null) return string.Empty;
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var p = element.LookupParameter(name);
                string text = ParameterToText(p);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            return string.Empty;
        }

        private static string ParameterToText(Parameter p)
        {
            if (p == null || !p.HasValue) return string.Empty;
            string value = p.AsString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = p.AsValueString();
            if (!string.IsNullOrWhiteSpace(value)) return value;

            switch (p.StorageType)
            {
                case StorageType.Integer: return p.AsInteger().ToString();
                case StorageType.Double: return p.AsDouble().ToString("0.###");
                case StorageType.ElementId: return ElementIdValue(p.AsElementId()).ToString();
                default: return string.Empty;
            }
        }

        private static long ElementIdValue(ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return -1;
#if REVIT2024 || REVIT2025 || REVIT2026
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        private static string SafeName(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? (fallback ?? "n/a") : value.Trim();
        }

        private static string CleanDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "n/a";
            string text = Regex.Replace(value, @"^\d+\.\s*", string.Empty);
            int idx = text.IndexOf('(');
            if (idx > 0) text = text.Substring(0, idx).Trim();
            return string.IsNullOrWhiteSpace(text) ? value.Trim() : text;
        }

        private static string ToMillimeters(double feet)
        {
            return (feet * 304.8).ToString("0.##");
        }

        private static string ToSquareMeters(double squareFeet)
        {
            return (squareFeet * 0.09290304).ToString("0.##");
        }
    }

    internal static class CobieHeaders
    {
        public static readonly string[] Contact = { "Email", "CreatedBy", "CreatedOn", "Category", "Company", "Phone", "Department", "OrganizationCode", "GivenName", "FamilyName", "Street", "PostalBox", "Town", "StateRegion", "PostalCode", "Country" };
        public static readonly string[] Facility = { "Name", "CreatedBy", "CreatedOn", "Category", "ProjectName", "SiteName", "LinearUnits", "AreaUnits", "VolumeUnits", "CurrencyUnit", "AreaMeasurement", "ExternalSystem", "ExternalProjectObject", "ExternalProjectIdentifier", "ExternalSiteObject", "ExternalSiteIdentifier", "ExternalFacilityObject", "ExternalFacilityIdentifier", "Description", "ProjectDescription", "SiteDescription", "Phase" };
        public static readonly string[] Floor = { "Name", "CreatedBy", "CreatedOn", "Category", "ExtSystem", "ExtObject", "ExtIdentifier", "Description", "Elevation", "Height" };
        public static readonly string[] Space = { "Name", "CreatedBy", "CreatedOn", "Category", "FloorName", "Description", "ExtSystem", "ExtObject", "ExtIdentifier", "RoomTag", "UsableHeight", "GrossArea", "NetArea" };
        public static readonly string[] Zone = { "Name", "CreatedBy", "CreatedOn", "Category", "SpaceNames", "ExtSystem", "ExtObject", "ExtIdentifier", "Description" };
        public static readonly string[] Type = { "Name", "CreatedBy", "CreatedOn", "Category", "Description", "ExtSystem", "ExtObject", "ExtIdentifier", "Manufacturer", "ModelNumber", "WarrantyGuarantorParts", "WarrantyDurationParts", "WarrantyGuarantorLabor", "WarrantyDurationLabor", "WarrantyDurationUnit" };
        public static readonly string[] Component = { "Name", "CreatedBy", "CreatedOn", "TypeName", "Space", "Description", "ExtSystem", "ExtObject", "ExtIdentifier", "SerialNumber", "InstallationDate", "WarrantyStartDate", "TagNumber", "BarCode", "AssetIdentifier" };
        public static readonly string[] System = { "Name", "CreatedBy", "CreatedOn", "Category", "ExtSystem", "ExtObject", "ExtIdentifier", "Description" };
        public static readonly string[] Assembly = { "Name", "CreatedBy", "CreatedOn", "SheetName", "ParentName", "ChildNames", "AssemblyType", "ExtSystem", "ExtObject", "ExtIdentifier", "Description" };
        public static readonly string[] Connection = { "Name", "CreatedBy", "CreatedOn", "ConnectionType", "SheetName", "RowName1", "RowName2", "RealizingElement", "PortName1", "PortName2", "ExtSystem", "ExtObject", "ExtIdentifier", "Description" };
        public static readonly string[] Spare = { "Name", "CreatedBy", "CreatedOn", "Category", "TypeName", "Suppliers", "ExtSystem", "ExtObject", "ExtIdentifier", "Description", "SetNumber", "PartNumber" };
        public static readonly string[] Resource = { "Name", "CreatedBy", "CreatedOn", "Category", "ExtSystem", "ExtObject", "ExtIdentifier", "Description" };
        public static readonly string[] Job = { "Name", "CreatedBy", "CreatedOn", "Category", "Status", "TypeName", "Description", "Duration", "DurationUnit", "Start", "TaskStartUnit", "Frequency", "FrequencyUnit", "ExtSystem", "ExtObject", "ExtIdentifier", "TaskNumber", "Priors", "ResourceNames" };
        public static readonly string[] Document = { "Name", "CreatedBy", "CreatedOn", "Category", "ApprovalBy", "Stage", "SheetName", "RowName", "Directory", "File", "ExtSystem", "ExtObject", "ExtIdentifier", "Description", "Reference" };
        public static readonly string[] Attribute = { "Name", "CreatedBy", "CreatedOn", "Category", "SheetName", "RowName", "Value", "Unit", "ExtSystem", "ExtObject", "ExtIdentifier", "Description", "AllowedValues" };
        public static readonly string[] Coordinate = { "Name", "CreatedBy", "CreatedOn", "Category", "SheetName", "RowName", "CoordinateXAxis", "CoordinateYAxis", "CoordinateZAxis", "ExtSystem", "ExtObject", "ExtIdentifier", "ClockwiseRotation", "ElevationalRotation", "YawRotation" };
        public static readonly string[] Issue = { "Name", "CreatedBy", "CreatedOn", "Type", "Risk", "Chance", "Impact", "SheetName1", "RowName1", "SheetName2", "RowName2", "Description", "Owner", "Mitigation", "ExtSystem", "ExtObject", "ExtIdentifier" };
        public static readonly string[] PickLists = { "ListName", "Value", "Description" };
        public static readonly string[] FieldCheck = { "SheetName", "FieldName", "Requirement", "DataSource", "RowCount", "MissingCount", "Status", "SuggestedAction", "AutoFillValue", "Note" };
        public static readonly string[] Validation = { "Severity", "SheetName", "RowName", "FieldName", "Message" };

        public static readonly string[] CommonRequired = { "Name", "CreatedBy", "CreatedOn", "Category" };
        public static readonly string[] ContactRequired = { "Email", "CreatedBy", "CreatedOn", "Category" };
        public static readonly string[] FacilityRequired = { "Name", "CreatedBy", "CreatedOn", "Category", "ProjectName", "SiteName" };
        public static readonly string[] SpaceRequired = { "Name", "CreatedBy", "CreatedOn", "Category", "FloorName" };
        public static readonly string[] ComponentRequired = { "Name", "CreatedBy", "CreatedOn", "TypeName", "Space" };
    }

    internal sealed class CobieFieldRule
    {
        public string SheetName { get; }
        public string FieldName { get; }
        public string Requirement { get; }
        public string DataSource { get; }
        public string Note { get; }

        public CobieFieldRule(string sheetName, string fieldName, string requirement, string dataSource, string note = "")
        {
            SheetName = sheetName;
            FieldName = fieldName;
            Requirement = requirement;
            DataSource = dataSource;
            Note = note;
        }
    }

    internal static class CobieFieldRules
    {
        private const string DefaultEmail = "unknown@yd-bim.local";
        private const string ExtSystem = "Autodesk Revit";

        public static readonly List<CobieFieldRule> All = new List<CobieFieldRule>
        {
            R("Contact", "Email", "必要", "專案資訊/預設聯絡信箱", "COBie 用於追蹤資料建立者。"),
            R("Contact", "CreatedBy", "必要", "專案資訊/預設聯絡信箱"),
            R("Contact", "CreatedOn", "必要", "匯出日期"),
            R("Contact", "Category", "必要", "工具預設"),
            R("Contact", "Company", "建議", "Project Information.OrganizationName"),
            R("Contact", "Phone", "建議", "使用者補充"),
            R("Contact", "GivenName", "建議", "使用者補充"),
            R("Contact", "FamilyName", "建議", "使用者補充"),

            R("Facility", "Name", "必要", "Project Information.Name / 文件名稱"),
            R("Facility", "CreatedBy", "必要", "Contact.Email"),
            R("Facility", "CreatedOn", "必要", "匯出日期"),
            R("Facility", "Category", "必要", "工具預設/可後續分類"),
            R("Facility", "ProjectName", "必要", "Project Information.Name / 文件名稱"),
            R("Facility", "SiteName", "必要", "Project Information.Name / 文件名稱"),
            R("Facility", "LinearUnits", "建議", "工具預設：millimeters"),
            R("Facility", "AreaUnits", "建議", "工具預設：square meters"),
            R("Facility", "VolumeUnits", "建議", "工具預設：cubic meters"),
            R("Facility", "Description", "建議", "Project Information.BuildingName"),

            R("Floor", "Name", "必要", "Level.Name"),
            R("Floor", "CreatedBy", "必要", "Contact.Email"),
            R("Floor", "CreatedOn", "必要", "匯出日期"),
            R("Floor", "Category", "必要", "工具預設：Level"),
            R("Floor", "ExtSystem", "建議", "工具預設：Autodesk Revit"),
            R("Floor", "ExtIdentifier", "建議", "Level.UniqueId"),
            R("Floor", "Elevation", "建議", "Level.Elevation"),
            R("Floor", "Height", "建議", "樓層高度/使用者補充"),

            R("Space", "Name", "必要", "Room.Number / Room.Name"),
            R("Space", "CreatedBy", "必要", "Contact.Email"),
            R("Space", "CreatedOn", "必要", "匯出日期"),
            R("Space", "Category", "必要", "工具預設：Room"),
            R("Space", "FloorName", "必要", "Room.Level"),
            R("Space", "Description", "建議", "Room.Name"),
            R("Space", "ExtIdentifier", "建議", "Room.UniqueId"),
            R("Space", "RoomTag", "建議", "Room.Number"),
            R("Space", "GrossArea", "建議", "Room.Area"),
            R("Space", "NetArea", "建議", "Room.Area"),

            R("Type", "Name", "必要", "ElementType.Name"),
            R("Type", "CreatedBy", "必要", "Contact.Email"),
            R("Type", "CreatedOn", "必要", "匯出日期"),
            R("Type", "Category", "必要", "ElementType.Category"),
            R("Type", "Description", "建議", "類型 Description / Type.Name"),
            R("Type", "ExtIdentifier", "建議", "ElementType.UniqueId"),
            R("Type", "Manufacturer", "建議", "類型 Manufacturer"),
            R("Type", "ModelNumber", "建議", "類型 Model"),
            R("Type", "WarrantyDurationUnit", "建議", "保固資料/使用者補充"),

            R("Component", "Name", "必要", "Element.Name / ElementId"),
            R("Component", "CreatedBy", "必要", "Contact.Email"),
            R("Component", "CreatedOn", "必要", "匯出日期"),
            R("Component", "TypeName", "必要", "Element.GetTypeId"),
            R("Component", "Space", "必要", "元素所在房間"),
            R("Component", "Description", "建議", "元素 Description / Element.Name"),
            R("Component", "ExtIdentifier", "建議", "Element.UniqueId"),
            R("Component", "SerialNumber", "建議", "SerialNumber / 序號參數"),
            R("Component", "InstallationDate", "建議", "使用者補充"),
            R("Component", "WarrantyStartDate", "建議", "使用者補充"),
            R("Component", "TagNumber", "建議", "Mark"),
            R("Component", "AssetIdentifier", "建議", "ElementId"),

            R("System", "Name", "必要", "MEPSystem/PipingSystem.Name"),
            R("System", "CreatedBy", "必要", "Contact.Email"),
            R("System", "CreatedOn", "必要", "匯出日期"),
            R("System", "Category", "必要", "System.Category"),
            R("System", "ExtIdentifier", "建議", "System.UniqueId"),
            R("System", "Description", "建議", "System.Description / System.Name"),

            R("Attribute", "Name", "必要", "COBie 欄位管理 DisplayName"),
            R("Attribute", "CreatedBy", "必要", "Contact.Email"),
            R("Attribute", "CreatedOn", "必要", "匯出日期"),
            R("Attribute", "Category", "必要", "COBie 欄位分類"),
            R("Attribute", "SheetName", "必要", "工具目前輸出 Component"),
            R("Attribute", "RowName", "必要", "Component.Name"),
            R("Attribute", "Value", "必要", "元素/類型參數值"),
            R("Attribute", "Unit", "建議", "使用者補充/參數單位"),

            R("Coordinate", "Name", "必要", "Component.Name + Location"),
            R("Coordinate", "CreatedBy", "必要", "Contact.Email"),
            R("Coordinate", "CreatedOn", "必要", "匯出日期"),
            R("Coordinate", "Category", "必要", "工具預設：Location"),
            R("Coordinate", "SheetName", "必要", "工具目前輸出 Component"),
            R("Coordinate", "RowName", "必要", "Component.Name"),
            R("Coordinate", "CoordinateXAxis", "必要", "元素 Location / BoundingBox"),
            R("Coordinate", "CoordinateYAxis", "必要", "元素 Location / BoundingBox"),
            R("Coordinate", "CoordinateZAxis", "必要", "元素 Location / BoundingBox")
        };

        private static CobieFieldRule R(string sheetName, string fieldName, string requirement, string dataSource, string note = "")
        {
            return new CobieFieldRule(sheetName, fieldName, requirement, dataSource, note);
        }

        public static string GetSuggestedAction(CobieFieldRule rule, int missingCount, int rowCount)
        {
            if (rowCount == 0)
                return IsRequiredRequirement(rule.Requirement) ? "確認模型是否有對應資料" : "可視交付需求補充";
            if (missingCount == 0)
                return "不需處理";
            if (IsRequiredRequirement(rule.Requirement))
                return "優先回模型或欄位管理補齊";
            return "建議補齊；也可使用匯出用自動補值";
        }

        public static string GetAutoFillValue(CobieFieldRule rule)
        {
            string sheet = rule.SheetName;
            string field = rule.FieldName;

            if (field == "CreatedBy") return DefaultEmail;
            if (field == "CreatedOn") return DateTime.Today.ToString("yyyy-MM-dd");
            if (field == "ExtSystem" || field == "ExternalSystem") return ExtSystem;
            if (field == "ExtObject") return "RevitElement";
            if (field == "ExtIdentifier") return "n/a";
            if (field == "Description") return "Name";

            if (sheet == "Component" && field == "Space") return "未分配空間";
            if (sheet == "Component" && (field == "SerialNumber" || field == "InstallationDate" || field == "WarrantyStartDate" || field == "BarCode")) return "未提供";
            if (sheet == "Component" && field == "TagNumber") return "Name";

            if (sheet == "Contact" && (field == "Company" || field == "Phone" || field == "GivenName" || field == "FamilyName")) return "未提供";
            if (sheet == "Facility" && (field == "SiteDescription" || field == "ProjectDescription" || field == "Phase" || field == "CurrencyUnit")) return "未提供";
            if (sheet == "Floor" && field == "Height") return "未提供";
            if (sheet == "Space" && field == "UsableHeight") return "未提供";
            if (sheet == "Type" && (field.StartsWith("Warranty", StringComparison.OrdinalIgnoreCase) || field == "Manufacturer")) return "未提供";
            if (sheet == "Type" && field == "ModelNumber") return "Name";
            if (sheet == "Attribute" && (field == "Unit" || field == "AllowedValues")) return "未指定";
            if (sheet == "Coordinate" && (field == "ClockwiseRotation" || field == "ElevationalRotation" || field == "YawRotation")) return "0";

            return IsRequiredRequirement(rule.Requirement) ? "需由模型資料提供" : "未提供";
        }

        private static bool IsRequiredRequirement(string value)
        {
            return string.Equals(value?.Trim(), "必要", StringComparison.OrdinalIgnoreCase);
        }
    }
}
