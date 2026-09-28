using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services;

int assertions = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    assertions++;
}

foreach (var culture in new[] { "zh-TW", "en-US", "de-DE" })
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
    foreach (var sample in new (string Text, double Mm)[]
    {
        ("DN25", 25), ("dn 40", 40), ("25mm", 25), ("25", 25),
        ("2\"", 50.8), ("1/2\"", 12.7), ("1 1/2 in", 38.1), ("1-1/2″", 38.1),
        ("50.5 MM", 50.5), ("DN100×50", 0), ("2,5", 0), ("-25", 0),
        ("0", 0), ("NaN", 0), ("Infinity", 0), ("1/0\"", 0), ("DN25\"", 0), (null, 0)
    }) Check(Math.Abs(ExportFormatting.ParseDiameterMm(sample.Text) - sample.Mm) < 1e-8, culture + ": " + sample.Text);
    Check(ExportFormatting.Number(1234.5, "F2") == "1234.50", culture + ": invariant decimal");
}

Check(ExportFormatting.MaterialOrUnknown(null) == "未指定", "null material");
Check(ExportFormatting.MaterialOrUnknown(" \t") == "未指定", "blank material");
Check(ExportFormatting.MaterialOrUnknown(" SUS304 ") == "SUS304", "known material");

var report = new GenerationReport();
Check(!report.Run("CSV", () => throw new IOException("拒絕存取")), "failed output returns false");
Check(report.HasFailures && !report.HasSuccess, "all failed status");
Check(report.Run("視圖", () => "ISO A"), "later output still succeeds");
report.Skip("PNG", "未勾選");
Check(report.HasFailures && report.HasSuccess, "partial success status");
Check(report.ToString().Contains("失敗｜CSV：拒絕存取") && report.ToString().Contains("成功｜視圖：ISO A")
    && report.ToString().Contains("略過｜PNG：未勾選"), "explicit per-output summary");
var skipped = new GenerationReport();
skipped.Skip("PCF", "未勾選");
Check(!skipped.HasSuccess && !skipped.HasFailures, "skips are not success");

// 實際執行 production ExportBOMToCSV，以獨立 CSV parser 回讀欄位。
string path = Path.Combine(Path.GetTempPath(), "hb-pipeiso-" + Guid.NewGuid().ToString("N") + ".csv");
try
{
    string description = "管材,含\"引號\"\r\n第二行";
    var data = new ISOData();
    data.BillOfMaterials.Add(new BOMItem
    {
        ItemNumber = 1, Type = "Pipe", Diameter = 25, Description = description,
        Material = null, Quantity = 2, Unit = "m", TotalLength = 1234.6
    });
    data.BillOfMaterials.Add(new BOMItem
    {
        ItemNumber = 2, Type = "Reducer", SizeText = "100×50", Quantity = 1, Material = "SUS304"
    });
    new PCFExporter().ExportBOMToCSV(data, path);
    var bytes = File.ReadAllBytes(path);
    Check(bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }), "UTF-8 BOM");
    using var parser = new TextFieldParser(path);
    parser.SetDelimiters(",");
    parser.HasFieldsEnclosedInQuotes = true;
    string[] header = parser.ReadFields(), pipe = parser.ReadFields(), reducer = parser.ReadFields();
    Check(header.Length == 9 && pipe.Length == 9 && reducer.Length == 9, "CSV column count");
    Check(pipe[3] == description, "comma quote multiline roundtrip");
    Check(pipe[2] == "25" && pipe[4] == "未指定", "diameter and unknown material");
    Check(pipe[5] == "2" && pipe[6] == "段" && pipe[7] == "1.235", "count separate from meters");
    Check(reducer[2] == "100×50" && reducer[6] == "個" && reducer[7] == "-", "fitting dimensions and units");
    Check(parser.EndOfData, "no extra CSV records");
    parser.Close();
    var lockedReport = new GenerationReport();
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        Check(!lockedReport.Run("CSV", () =>
        {
            new PCFExporter().ExportBOMToCSV(data, path);
            return path;
        }), "locked file is a failed export");
        Check(lockedReport.HasFailures && !lockedReport.HasSuccess, "IO failure never reports success");
    }
    Check(File.ReadAllBytes(path).SequenceEqual(bytes), "locked export preserves existing file");
}
finally { if (File.Exists(path)) File.Delete(path); }
var padded = new ViewRect(-100, -20, 300, 80).Pad(10, 0.08);
Check(padded.Left == -132 && padded.Right == 332 && padded.Bottom == -30 && padded.Top == 90, "padding includes negative coordinates and minimum margin");
var tiny = new ViewRect(5, 5, 5, 5).Pad(2, 0.08);
Check(tiny.Right - tiny.Left == 4 && tiny.Top - tiny.Bottom == 4, "degenerate bounds still have margin");
var occupied = new List<ViewRect>();
for (int i = 0; i < 12; i++)
{
    var original = new ViewRect(0, 0, 2, 1);
    Check(ViewLayout.TryPlace(original, occupied, 2, 0.5, out double dx, out double dy), "dense labels find candidate " + i);
    var placed = original.Move(dx, dy);
    Check(!occupied.Any(box => placed.Overlaps(box, 0.5)), "candidate keeps spacing " + i);
    occupied.Add(placed);
}
Check(!ViewLayout.TryPlace(new ViewRect(0, 0, 1, 1), new[] { new ViewRect(-100, -100, 100, 100) },
    2, 0.5, out _, out _), "bounded collision search reports unresolved");
Exception recorded = null;
var diagnostic = new GenerationReport((_, error) => recorded = error);
diagnostic.Run("輸出", () => throw new IOException("outer", new UnauthorizedAccessException("inner")));
Check(recorded?.InnerException is UnauthorizedAccessException, "full exception passed to diagnostics");
var badLogger = new GenerationReport((_, error) => throw new Exception("logger failed"));
Check(!badLogger.Run("輸出", () => throw new IOException("original")) && badLogger.ToString().Contains("original"),
    "logger failure does not mask original error");

string folder = Path.Combine(Path.GetTempPath(), "hb-iso-preflight-" + Guid.NewGuid().ToString("N"));
try
{
    OutputPreflight.Check(folder, "ISO-消防 1");
    Check(Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any(), "write probe cleans itself");
    string marker = Path.Combine(folder, "existing.txt");
    File.WriteAllText(marker, "preserve");
    OutputPreflight.Check(folder, "ISO-消防 1");
    Check(File.ReadAllText(marker) == "preserve", "preflight preserves existing files");
    foreach (var invalid in new[] { "", "CON", "NUL.txt", "LPT1", "a/b", "trailing." })
    {
        bool rejected = false;
        try { OutputPreflight.Check(folder, invalid); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "invalid filename rejected: " + invalid);
    }
    bool fileRejected = false;
    try { OutputPreflight.Check(marker, "valid"); }
    catch (IOException ex) { fileRejected = ex.InnerException != null && ex.Message.Contains(marker); }
    Check(fileRejected, "unwritable destination retains underlying error and path");
    File.Delete(marker);
}
finally { if (Directory.Exists(folder)) Directory.Delete(folder); }
// 不依元件輸入順序、方向、距離猜測連線；迴路與重複邊不漏件。
var graph = new ConnectivityGraph(new long[] { 5, 4, 3, 2, 1, 1 });
graph.Connect(1, 2); graph.Connect(2, 3); graph.Connect(2, 4);
graph.Connect(2, 1); graph.Connect(1, 1); graph.Connect(4, 999);
var components = graph.Components();
Check(components.Count == 5, "deduplicate scope IDs");
Check(components[1] == components[4] && components[5] != components[1], "tee branches and disconnected scope");
Check(graph.Neighbors(2).SequenceEqual(new long[] { 1, 3, 4 }), "tee preserves all three neighbors");
Check(!graph.Neighbors(4).Contains(999), "external connection does not expand scope");
graph.Connect(3, 4);
Check(graph.Components().Count == 5, "cycle visits each element exactly once");
var reversed = new ConnectivityGraph(new long[] { 1, 2, 3, 4, 5 });
reversed.Connect(4, 3); reversed.Connect(4, 2); reversed.Connect(3, 2); reversed.Connect(2, 1);
Check(graph.Components().OrderBy(p => p.Key).SequenceEqual(reversed.Components().OrderBy(p => p.Key)), "component numbers deterministic");
Check(!graph.Neighbors(5).Any(), "unlinked element remains isolated");
Check(new ConnectivityGraph(Array.Empty<long>()).Components().Count == 0, "empty scope");
var checkOnly = new GenerationReport();
checkOnly.Note("範圍", "已檢查");
Check(!checkOnly.HasSuccess, "inspection is not generated output");
var measured = new PipeMeasurement { X0 = 0, Y0 = 0, Z0 = 500, X1 = 3000, Y1 = 4000, Z1 = 600,
    LengthMm = Math.Sqrt(25010000), DiameterMm = 100, IsStraight = true };
Check(measured.HorizontalMm == 5000 && measured.SlopePercent == 2, "slope uses horizontal run not spatial length");
var reversedMeasure = new PipeMeasurement { X0 = 3000, Y0 = 4000, Z0 = 600, X1 = 0, Y1 = 0, Z1 = 500, IsStraight = true };
Check(reversedMeasure.SlopePercent == measured.SlopePercent, "absolute slope independent of endpoint direction");
var verticalMeasure = new PipeMeasurement { Z1 = 1000, IsStraight = true };
Check(verticalMeasure.SlopePercent == null && verticalMeasure.SlopeText == "垂直", "vertical pipe does not produce infinite slope");
measured.IsStraight = false;
Check(measured.SlopePercent == null && measured.SlopeText.Contains("曲管"), "curved pipe cannot imply a single slope");
measured.IsStraight = true;
measured.Z1 = measured.Z0;
Check(measured.SlopePercent == 0, "horizontal pipe slope is zero");
foreach (var culture in new[] { "zh-TW", "en-US", "de-DE" })
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
    measured.LengthMm = 1000.125;
    Check(measured.Label("P123").Contains("模型長 1000.125 mm"), "dimension label invariant and explicitly model length " + culture);
}
OfflineReviewTests.Run(Check, args);
Console.WriteLine($"PASS: {assertions} assertions. Revit runtime behavior NOT tested.");
