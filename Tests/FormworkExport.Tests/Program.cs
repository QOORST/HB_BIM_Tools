using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using OfficeOpenXml;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;

static class Program
{
    private static int _checks;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name); _checks++;
    }
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Private).SetValue(instance, value);
    private static object Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance).GetValue(instance);
    private static object Property(object instance, string name) => instance.GetType().GetProperty(name).GetValue(instance);
    private static object Call(object instance, string name, params object[] args)
    {
        try { return instance.GetType().GetMethod(name, Private).Invoke(instance, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    private static DirectShape Template(long id, long host, double area, string source = "ImprovedEngine")
    {
        var shape = new DirectShape
        {
            Id = new ElementId(id), Name = "Template, \"A\"", ApplicationId = "HB_BIM_Formwork",
            ApplicationDataId = source, Category = new Category { Id = new ElementId((long)BuiltInCategory.OST_GenericModel) }
        };
        shape.Parameters[SharedParams.P_HostId] = new Parameter { StorageType = StorageType.String, Value = host.ToString(CultureInfo.InvariantCulture) };
        shape.Parameters[SharedParams.P_EffectiveArea] = new Parameter { StorageType = StorageType.Double, Value = area };
        return shape;
    }

    [STAThread]
    public static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "hb-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = new Document();
            doc.Add(new Floor { Id = new ElementId(100), Name = "Ramp, A", LevelId = new ElementId(10) });
            doc.Add(new Level { Id = new ElementId(10), Name = "B2" });
            doc.Add(Template(101, 100, 10));
            doc.Add(Template(102, 100, 20, "ImprovedPickFace"));
            doc.Add(Template(103, 999, 5));
            doc.Add(Template(104, 100, double.NaN));
            var unrelated = Template(105, 100, 1); unrelated.ApplicationId = "OtherTool"; doc.Add(unrelated);
            var command = new CmdExportCsv(); Set(command, "_generatedOnly", true);
            var data = new Autodesk.Revit.UI.ExternalCommandData
            {
                Application = new Autodesk.Revit.UI.UIApplication
                {
                    ActiveUIDocument = new Autodesk.Revit.UI.UIDocument { Document = doc }
                }
            };
            string message = string.Empty;
            var cancelledDialog = command.Execute(data, ref message, new ElementSet());
            Check(cancelledDialog == Autodesk.Revit.UI.Result.Cancelled && Autodesk.Revit.UI.TaskDialog.ShowCalls == 1
                && Autodesk.Revit.UI.TaskDialog.AddedLinks == 2, "mode dialog creates both choices without invalid default button");
            Check(command.Execute(data, ref message, new ElementSet()) == Autodesk.Revit.UI.Result.Cancelled
                && Autodesk.Revit.UI.TaskDialog.ShowCalls == 2, "cancelled mode dialog releases export reentry guard");
            var analysis = Call(command, "CollectAnalysis", doc);
            Check(StructuralFormworkAnalyzer.Calls == 0 && FormworkEngine.Starts == 0, "fast export skips analysis and geometry cache");
            var templates = (IList)Call(command, "CollectTemplates", doc);
            Check(templates.Count == 4, "includes generated and manual templates; excludes other tools");
            Check(doc.BoundsCalls == 0, "fast template collection performs zero bounding-box calls");
            var details = (IList)Call(command, "CollectGeneratedDetailData", doc, templates);
            Check(doc.BoundsCalls == 0, "fast host metadata performs zero bounding-box calls");
            Check(details.Count == 1 && (int)Property(details[0], "FormworkCount") == 3, "host grouping preserves all related templates");
            Check(Math.Abs((double)Property(details[0], "ActualFormworkArea") - 30 * 0.09290304) < 1e-9, "valid areas summed once; invalid areas excluded");
            Check((string)Property(details[0], "Level") == "B2", "level resolved from parameter without geometry");
            Check(((string)Property(details[0], "Sources")).Contains("面生面"), "manual source retained");
            Check(templates.Cast<object>().Any(t => ((string)Field(t, "Warning")).Contains("宿主不存在")), "orphan retained with warning");
            Check(templates.Cast<object>().Any(t => ((string)Field(t, "Warning")).Contains("有效面積")), "invalid area warning retained");
            Set(command, "_templateCount", templates.Count);

            var oldCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string csv = Path.Combine(root, "sample.csv");
            using (var writer = new StreamWriter(csv, false, new UTF8Encoding(true)))
            {
                Call(command, "WriteHeader", writer);
                Call(command, "WriteSummary", writer, analysis, details);
                Call(command, "WriteDetailData", writer, details);
                Call(command, "WriteTemplateData", writer, templates);
            }
            string text = File.ReadAllText(csv);
            Check(text.Contains("未重新分析") && text.Contains("未執行包圍盒"), "fast report discloses uncomputed quantities and unchecked duplicates");
            Check(text.Contains("模板總面積(m²),2.787") && text.Contains("未歸屬宿主總計的模板數,1"), "CSV totals and decimal separator remain correct");
            Check(text.Contains("\"Template, \"\"A\"\"\""), "CSV quotes escaped");
            Check(File.ReadAllBytes(csv).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "CSV UTF-8 BOM");
            CultureInfo.CurrentCulture = oldCulture;

            string xlsx = Path.Combine(root, "sample.xlsx");
            Call(command, "ExportToExcel", xlsx, analysis, details, "Test view", templates);
            using (var package = new ExcelPackage(new FileInfo(xlsx)))
            {
                Check(package.Workbook.Worksheets.Count == 4, "Excel retains summary, detail, category and template sheets");
                Check(package.Workbook.Worksheets["總覽"].Cells[11, 2].Text == "未重新分析", "Excel does not report fake concrete quantities");
                Check(package.Workbook.Worksheets["詳細資料"].Cells[3, 8].Value == null, "Excel unknown volume remains blank");
                Check(package.Workbook.Worksheets["逐片模板"].Dimension.End.Row == 6, "Excel includes all four template rows");
                Check(package.Workbook.Worksheets["詳細資料"].Column(6).Width == 55, "bounded Excel widths avoid all-cell autofit");
            }

            var many = Enumerable.Range(0, 100000).Select(i => new KeyValuePair<string, double>(i.ToString(), 1.25));
            string formula = ExportTemplateRules.BuildAreaFormula(many);
            Check(formula.Length < 4096 && formula.Contains("99950") && formula.Contains("逐片模板"), "large-host formula bounded without dropping detail reference");

            Set(command, "_cancelRequested", true);
            bool cancelled = false;
            try { Call(command, "CollectTemplates", doc); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "collection honors cancellation");
            Set(command, "_cancelRequested", false);
            Set(command, "_generatedOnly", false);
            StructuralFormworkAnalyzer.Fail = true;
            try { Call(command, "CollectAnalysis", doc); } catch (InvalidOperationException) { }
            Check(StructuralFormworkAnalyzer.Calls == 1 && FormworkEngine.Starts == 1 && FormworkEngine.Ends == 1, "explicit full-analysis failure releases cache");
            StructuralFormworkAnalyzer.Fail = false;
            doc.ForbidBounds = false;
            var fullRows = (IList)Call(command, "CollectTemplates", doc);
            Check(doc.BoundsCalls > 0 && fullRows.Cast<object>().Any(t => ((string)Field(t, "DuplicateGroup")).Length > 0), "full mode retains bounding-box duplicate candidates");
            var fullAnalysis = new StructuralAnalysisResult { TotalElements = 1, TotalConcreteVolume = 12.5, EstimatedRebarWeight = 1.5 };
            fullAnalysis.ElementAnalyses.Add(doc.GetElement(new ElementId(100)), new ElementFormworkAnalysis
            {
                ElementType = StructuralElementType.Slab, ConcreteVolume = 12.5, FormworkArea = 80
            });
            var fullDetails = (IList)Call(command, "CollectDetailData", doc, fullAnalysis, fullRows);
            Check(Math.Abs((double)Property(fullDetails[0], "ActualFormworkArea") - 30 * 0.09290304) < 1e-9,
                "full mode still prefers generated area over estimates");
            string fullXlsx = Path.Combine(root, "full.xlsx");
            Call(command, "ExportToExcel", fullXlsx, fullAnalysis, fullDetails, "Test view", fullRows);
            using (var package = new ExcelPackage(new FileInfo(fullXlsx)))
                Check(Convert.ToDouble(package.Workbook.Worksheets["總覽"].Cells[11, 2].Value) == 12.5,
                    "explicit full report retains computed concrete totals");
            Set(command, "_cancelRequested", true);
            using (var writer = new StreamWriter(Path.Combine(root, "cancel-write.csv")))
            {
                bool writeCancelled = false;
                try { Call(command, "WriteTemplateData", writer, fullRows); } catch (OperationCanceledException) { writeCancelled = true; }
                Check(writeCancelled, "CSV writing honors cancellation between rows");
            }
            Set(command, "_cancelRequested", false);

            string destination = Path.Combine(root, "existing.csv");
            File.WriteAllText(destination, "original");
            string temporary;
            using (var output = new ExportOutputFile(destination))
            {
                temporary = output.TemporaryPath;
                File.WriteAllText(temporary, "partial");
            }
            Check(File.ReadAllText(destination) == "original" && !File.Exists(temporary), "cancelled write preserves old report and removes staging");
            using (var output = new ExportOutputFile(destination))
            {
                File.WriteAllText(output.TemporaryPath, "complete"); output.Commit();
            }
            Check(File.ReadAllText(destination) == "complete", "successful write atomically replaces report");
            using (var locked = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new ExportOutputFile(destination))
            {
                File.WriteAllText(output.TemporaryPath, "must-not-replace");
                bool failed = false;
                try { output.Commit(); } catch (IOException) { failed = true; }
                Check(failed, "locked destination rejects commit");
            }
            Check(File.ReadAllText(destination) == "complete", "locked-file failure preserves original report");
            string newPath = Path.Combine(root, "new.csv");
            using (var output = new ExportOutputFile(newPath))
            {
                File.WriteAllText(output.TemporaryPath, "new"); output.Commit();
            }
            Check(File.ReadAllText(newPath) == "new", "new destination committed");

            var largeDoc = new Document(); largeDoc.Add(new Floor { Id = new ElementId(100) });
            for (int i = 0; i < 20000; i++) largeDoc.Add(Template(1000 + i, 100, 1));
            Set(command, "_generatedOnly", true);
            var timer = Stopwatch.StartNew();
            var largeTemplates = (IList)Call(command, "CollectTemplates", largeDoc);
            var largeDetails = (IList)Call(command, "CollectGeneratedDetailData", largeDoc, largeTemplates);
            Check(largeTemplates.Count == 20000 && largeDetails.Count == 1 && largeDoc.BoundsCalls == 0, "20k-template synthetic path keeps every row without geometry");
            Console.WriteLine($"Synthetic metadata-only 20k rows: {timer.Elapsed.TotalMilliseconds:F0} ms; not a Revit benchmark.");
            Console.WriteLine($"{_checks} checks passed with Revit service doubles and real CSV/EPPlus file writers.");
        }
        finally
        {
            string resolved = Path.GetFullPath(root);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("hb-export-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Test cleanup target outside temporary workspace.");
            Directory.Delete(resolved, true);
        }
    }
}
