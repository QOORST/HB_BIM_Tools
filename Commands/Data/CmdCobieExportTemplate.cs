using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YD_RevitTools.LicenseManager.Helpers.Data;
using OfficeOpenXml;

namespace YD_RevitTools.LicenseManager.Commands.Data
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdCobieExportTemplate : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet set)
        {
            try
            {
                // 載入欄位設定
                var cfgs = CobieConfigIO.LoadConfig();
                var exportFields = cfgs.Where(c => c.ExportEnabled).ToList();
                
                if (exportFields.Count == 0)
                {
                    TaskDialog.Show("COBie 範本匯出", 
                        "尚未勾選任何匯出欄位，請先於「COBie 欄位管理」設定。");
                    return Result.Cancelled;
                }

                // 設定 EPPlus 授權模式（非商業用途）
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                // 選擇儲存位置
                var sfd = new SaveFileDialog
                {
                    Filter = "Excel 檔案 (*.xlsx)|*.xlsx|CSV 檔案 (*.csv)|*.csv|所有檔案 (*.*)|*.*",
                    FileName = $"COBie_Template_{DateTime.Now:yyyyMMdd}.xlsx",
                    DefaultExt = "xlsx",
                    Title = "儲存 COBie 範本"
                };

                if (sfd.ShowDialog() != DialogResult.OK)
                {
                    return Result.Cancelled;
                }

                // 建立標題列
                var headers = new List<string> { "UniqueId", "ElementId", "FamilyName", "TypeName" };
                headers.AddRange(exportFields.Select(f => f.DisplayName?.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct());

                // 根據檔案類型寫入範本
                string fileExt = Path.GetExtension(sfd.FileName).ToLower();
                if (fileExt == ".xls")
                {
                    TaskDialog.Show("COBie 範本匯出", "不支援舊版 Excel .xls 格式。請改用 .xlsx 或 .csv。");
                    return Result.Cancelled;
                }

                if (fileExt == ".xlsx")
                {
                    // 寫入 Excel 範本
                    WriteExcelTemplate(sfd.FileName, headers, exportFields);
                }
                else if (fileExt == ".csv")
                {
                    // 寫入 CSV 範本
                    WriteCsvTemplate(sfd.FileName, headers);
                    WriteCsvFieldGuide(sfd.FileName);
                }
                else
                {
                    TaskDialog.Show("COBie 範本匯出", "不支援的檔案格式。請使用 .xlsx 或 .csv。");
                    return Result.Cancelled;
                }

                TaskDialog.Show("COBie 範本匯出", 
                    $"已成功匯出 COBie 範本！\n\n" +
                    $"檔案：{Path.GetFileName(sfd.FileName)}\n" +
                    $"欄位數：{headers.Count}\n\n" +
                    $"請將此範本提供給廠商填寫資料。");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                msg = ex.ToString();
                return Result.Failed;
            }
        }

        /// <summary>
        /// 寫入 Excel 範本
        /// </summary>
        private void WriteExcelTemplate(string filePath, List<string> headers, List<CmdCobieFieldManager.CobieFieldConfig> fields)
        {
            var fileInfo = new FileInfo(filePath);
            using (var package = new ExcelPackage(fileInfo))
            {
                // 建立工作表
                var worksheet = package.Workbook.Worksheets.Add("COBie Data");

                // 寫入標題列（第1列）
                for (int col = 0; col < headers.Count; col++)
                {
                    var cell = worksheet.Cells[1, col + 1];
                    cell.Value = headers[col];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196)); // 藍色
                    cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                    cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                }

                // 寫入說明列（第2列）- 顯示欄位說明
                worksheet.Cells[2, 1].Value = "請勿修改此列";
                worksheet.Cells[2, 2].Value = "請勿修改此列";
                worksheet.Cells[2, 3].Value = "族群名稱";
                worksheet.Cells[2, 4].Value = "類型名稱";
                
                for (int i = 0; i < fields.Count; i++)
                {
                    var field = fields[i];
                    var col = i + 5; // 前4欄是固定欄位
                    var cell = worksheet.Cells[2, col];
                    
                    // 建立說明文字
                    var description = $"{field.Category}";
                    if (!string.IsNullOrWhiteSpace(field.DataType))
                    {
                        description += $" | 類型: {field.DataType}";
                    }
                    if (field.IsRequired)
                    {
                        description += " | 必填";
                    }
                    
                    cell.Value = description;
                    cell.Style.Font.Italic = true;
                    cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                // 凍結前兩列
                worksheet.View.FreezePanes(3, 1);

                // 自動調整欄寬
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                WriteFieldGuideSheet(package);

                // 儲存檔案
                package.Save();
            }
        }

        private void WriteFieldGuideSheet(ExcelPackage package)
        {
            var worksheet = package.Workbook.Worksheets.Add("COBie Field Guide");
            var headers = new[] { "SheetName", "FieldName", "Requirement", "DataSource", "SuggestedAction", "AutoFillValue", "Note" };

            for (int col = 0; col < headers.Length; col++)
            {
                var cell = worksheet.Cells[1, col + 1];
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(31, 73, 125));
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            }

            int row = 2;
            foreach (var rule in CobieFieldRules.All)
            {
                worksheet.Cells[row, 1].Value = rule.SheetName;
                worksheet.Cells[row, 2].Value = rule.FieldName;
                worksheet.Cells[row, 3].Value = rule.Requirement;
                worksheet.Cells[row, 4].Value = rule.DataSource;
                worksheet.Cells[row, 5].Value = CobieFieldRules.GetSuggestedAction(rule, 1, 1);
                worksheet.Cells[row, 6].Value = CobieFieldRules.GetAutoFillValue(rule);
                worksheet.Cells[row, 7].Value = rule.Note;
                row++;
            }

            worksheet.View.FreezePanes(2, 1);
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns(10, 48);
            worksheet.Cells[1, 1, Math.Max(1, row - 1), headers.Length].AutoFilter = true;
        }

        /// <summary>
        /// 寫入 CSV 範本
        /// </summary>
        private void WriteCsvTemplate(string filePath, List<string> headers)
        {
            using (var sw = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
            {
                // 寫入標題列
                sw.WriteLine(string.Join(",", headers.Select(h => $"\"{h}\"")));
            }
        }

        private void WriteCsvFieldGuide(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            string name = Path.GetFileNameWithoutExtension(filePath);
            string guidePath = Path.Combine(dir ?? string.Empty, $"{name}_FieldGuide.csv");
            var headers = new[] { "SheetName", "FieldName", "Requirement", "DataSource", "SuggestedAction", "AutoFillValue", "Note" };

            using (var sw = new StreamWriter(guidePath, false, System.Text.Encoding.UTF8))
            {
                sw.WriteLine(string.Join(",", headers.Select(Csv)));
                foreach (var rule in CobieFieldRules.All)
                {
                    sw.WriteLine(string.Join(",", new[]
                    {
                        rule.SheetName,
                        rule.FieldName,
                        rule.Requirement,
                        rule.DataSource,
                        CobieFieldRules.GetSuggestedAction(rule, 1, 1),
                        CobieFieldRules.GetAutoFillValue(rule),
                        rule.Note
                    }.Select(Csv)));
                }
            }
        }

        private static string Csv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
