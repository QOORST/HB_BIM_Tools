using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;

namespace YD_RevitTools.LicenseManager.Commands.Data.BimStandard
{
    internal static class ModelAuditReport
    {
        internal static void Write(string path, string documentTitle, ModelAuditGroup group,
            ModelAuditOptions options, IReadOnlyList<ModelAuditIssue> issues)
        {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage())
                {
                    var summary = package.Workbook.Worksheets.Add("檢查說明");
                    var rows = new[] { new[] { "模型", documentTitle }, new[] { "檢查項目", ModelAuditOptions.GroupName(group) },
                        new[] { "匯出時間", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                        new[] { "結果", ModelAuditReview.Summary(issues) },
                        new[] { "命名設定", group == ModelAuditGroup.Naming ? options.Description : "本次未執行命名檢查" },
                        new[] { "範圍", "只包含本次選定項目；不代表模型品質評分或驗收。略過項目及原因一併匯出。" } };
                    for (int i = 0; i < rows.Length; i++) { summary.Cells[i + 1, 1].Value = rows[i][0]; summary.Cells[i + 1, 2].Value = rows[i][1]; }
                    summary.Column(1).Width = 18; summary.Column(2).Width = 90; summary.Cells.Style.WrapText = true;
                    var sheet = package.Workbook.Worksheets.Add("檢查結果");
                    var headers = new[] { "狀態", "項目", "類別", "規則", "待確認內容", "處理建議", "本次略過原因", "元素 ID" };
                    for (int i = 0; i < headers.Length; i++) { sheet.Cells[1, i + 1].Value = headers[i]; sheet.Cells[1, i + 1].Style.Font.Bold = true; }
                    for (int i = 0; i < issues.Count; i++)
                    {
                        var issue = issues[i]; var cells = new[] { issue.Status, issue.ElementName, issue.Category, issue.Rule, issue.Message, issue.Recommendation, issue.IgnoredReason, issue.ElementId };
                        for (int j = 0; j < cells.Length; j++) sheet.Cells[i + 2, j + 1].Value = cells[j];
                    }
                    sheet.View.FreezePanes(2, 1); sheet.Cells.Style.WrapText = true;
                    sheet.Column(1).Width = 14; sheet.Column(2).Width = 32; sheet.Column(3).Width = 18; sheet.Column(4).Width = 24;
                    sheet.Column(5).Width = 55; sheet.Column(6).Width = 65; sheet.Column(7).Width = 40; sheet.Column(8).Width = 18;
                    package.SaveAs(new FileInfo(path));
                }
        }
    }
}